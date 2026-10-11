using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace P90Guard.Core
{
    /// <summary>A report about a player from the shared cheater database.</summary>
    public sealed class NetReport
    {
        public long Id;
        public string Server;
        public string Reason;
        public DateTime Created;
        public DateTime? Until;
        public int EvidenceCount;
        /// <summary>Created by this server.</summary>
        public bool Own;
        /// <summary>The author server is verified by the network owner (a new server can be on probation).</summary>
        public bool Verified = true;
    }

    public sealed class NetCheck
    {
        public ulong SteamId;
        public List<NetReport> Reports = new();
        /// <summary>Reports from other servers.</summary>
        public List<NetReport> Others => Reports.Where(r => !r.Own && (r.Until == null || r.Until > DateTime.UtcNow)).ToList();
        public int OtherServers => Others.Select(r => r.Server ?? "?").Distinct().Count();
        /// <summary>Different VERIFIED servers that reported the player - only these count for automatic Kick/Ban.</summary>
        public int VerifiedOtherServers => Others.Where(r => r.Verified).Select(r => r.Server ?? "?").Distinct().Count();
    }

    /// <summary>
    /// Client of the shared cheater database (protocol v1, see docs/NETWORK_API.md). All HTTP runs in the background;
    /// callbacks are delivered on the main thread (GuardTicker → Network.Drain). Fail-open: if the site is down,
    /// the server plays normally.
    /// </summary>
    internal static class Network
    {
        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
        private static readonly ConcurrentQueue<Action> MainQueue = new();
        private static readonly Dictionary<ulong, (DateTime At, NetCheck Result)> Cache = new();
        private static readonly HashSet<ulong> InFlight = new();
        private static readonly HashSet<IntPtr> CheckedObjects = new();
        private static DateTime _lastError = DateTime.MinValue;

        private static NetworkConfig Cfg => GuardConfig.Current.Network;
        internal static bool On => Cfg.Enabled && !string.IsNullOrWhiteSpace(Cfg.Url) && !string.IsNullOrWhiteSpace(Cfg.ApiKey);

        /// <summary>Printed at start: the server owner must know what is sent and where.</summary>
        internal static void Announce()
        {
            if (!Cfg.Enabled) { GuardLog.Info("[Network] cheater network: off (Network.Enabled=false)"); return; }
            if (string.IsNullOrWhiteSpace(Cfg.ApiKey)) { GuardLog.Info($"[Network] cheater network: not connected - no ApiKey (keys are issued by the network owner, {Cfg.Url})"); return; }
            GuardLog.Warn($"[Network] cheater network ON: {Cfg.Url}");
            GuardLog.Warn("[Network]   sends: SteamID of every joining player (lookup); for \"pg cheatban\" - SteamID, nick, reason, duration, server name, recent anti-cheat lines");
            GuardLog.Warn("[Network]   to keep everything local set Network.Enabled=false in P90Guard.json");
        }
        internal static string LastError { get; private set; }

        // ---------------- main-thread plumbing ----------------

        internal static void Drain()
        {
            int n = 0;
            while (n++ < 50 && MainQueue.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { GuardLog.Warn($"network callback: {e.Message}"); }
            }
        }

        private static void OnMain(Action a) => MainQueue.Enqueue(a);

        // ---------------- HTTP ----------------

        private static string Url(string path) => Cfg.Url.TrimEnd('/') + path;

        private static async Task<(bool Ok, JsonElement Body, string Error)> Send(HttpMethod method, string path, object body, bool auth = true)
        {
            try
            {
                using var req = new HttpRequestMessage(method, Url(path));
                if (auth) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (Cfg.ApiKey ?? "").Trim());
                req.Headers.TryAddWithoutValidation("User-Agent", "P90Guard/" + Plugin.Version);
                if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(Cfg.TimeoutSeconds, 2, 30)));
                using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonElement json = default;
                try { json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone(); } catch { }
                bool ok = resp.IsSuccessStatusCode && json.ValueKind == JsonValueKind.Object
                          && (!json.TryGetProperty("ok", out var okp) || okp.ValueKind == JsonValueKind.True);
                string err = ok ? null
                    : json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var e) ? $"HTTP {(int)resp.StatusCode}: {e}"
                    : $"HTTP {(int)resp.StatusCode}";
                return (ok, json, err);
            }
            catch (OperationCanceledException) { return (false, default, "timeout"); }
            catch (Exception e) { return (false, default, e.GetBaseException().Message); }
        }

        private static void NoteError(string what, string err)
        {
            LastError = $"{DateTime.Now:HH:mm:ss} {what}: {err}";
            if ((DateTime.UtcNow - _lastError).TotalMinutes < 5) return;   // no spam when the site is down
            _lastError = DateTime.UtcNow;
            GuardLog.Warn($"[Network] {what}: {err}");
        }

        // ---------------- API ----------------

        internal static void Ping(Action<bool, string> done)
        {
            Task.Run(async () =>
            {
                var r = await Send(HttpMethod.Get, "/ping", null);
                string info = r.Ok ? DescribeServer(r.Body) : r.Error;
                OnMain(() => done(r.Ok, info));
            });
        }

        private static string DescribeServer(JsonElement b)
        {
            try { if (b.TryGetProperty("server", out var s) && s.TryGetProperty("name", out var n)) return "connected as \"" + n.GetString() + "\""; } catch { }
            return "connected";
        }

        internal static void Check(ulong steamId, bool useCache, Action<NetCheck, string> done)
        {
            if (useCache && Cache.TryGetValue(steamId, out var c) && (DateTime.UtcNow - c.At).TotalMinutes < Math.Max(0, Cfg.CacheMinutes))
            { done(c.Result, null); return; }
            Task.Run(async () =>
            {
                var r = await Send(HttpMethod.Get, "/check?steam_id=" + steamId, null);
                NetCheck res = null; string err = r.Error;
                if (r.Ok) { try { res = ParseCheck(steamId, r.Body); } catch (Exception e) { err = "bad response: " + e.Message; } }
                OnMain(() =>
                {
                    if (res != null) Cache[steamId] = (DateTime.UtcNow, res);
                    else NoteError("check " + steamId, err);
                    done(res, res == null ? err : null);
                });
            });
        }

        private static NetCheck ParseCheck(ulong steamId, JsonElement b)
        {
            var res = new NetCheck { SteamId = steamId };
            if (!b.TryGetProperty("reports", out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
            foreach (var x in arr.EnumerateArray())
            {
                var r = new NetReport
                {
                    Id = x.TryGetProperty("id", out var id) && id.TryGetInt64(out var idv) ? idv : 0,
                    Server = Str(x, "server") ?? "?",
                    Reason = Str(x, "reason") ?? "",
                    EvidenceCount = x.TryGetProperty("evidence_count", out var ec) && ec.TryGetInt32(out var ecv) ? ecv : 0,
                    Own = x.TryGetProperty("own", out var own) && own.ValueKind == JsonValueKind.True,
                    Verified = !x.TryGetProperty("verified", out var ver) || ver.ValueKind != JsonValueKind.False,
                };
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var st = System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal;
                if (DateTime.TryParse(Str(x, "created_at"), ci, st, out var cr)) r.Created = cr;
                if (DateTime.TryParse(Str(x, "until"), ci, st, out var un)) r.Until = un;
                res.Reports.Add(r);
            }
            return res;
        }

        private static string Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        internal static void Report(ulong steamId, string nick, string reason, int minutes, List<string> evidence, Action<bool, string> done)
        {
            var body = new Dictionary<string, object>
            {
                ["steam_id"] = steamId.ToString(), ["nick"] = nick ?? "", ["reason"] = Trim(reason, 200),
                ["duration_minutes"] = Math.Max(0, minutes),
                ["evidence"] = (evidence ?? new List<string>()).Take(30).Select(l => Trim(l, 500)).ToList(),
                ["server_name"] = GuardConfig.Current.Webhook.ServerName ?? "", ["plugin"] = "P90Guard " + Plugin.Version,
            };
            Task.Run(async () =>
            {
                var r = await Send(HttpMethod.Post, "/reports", body);
                OnMain(() => { Cache.Remove(steamId); if (!r.Ok) NoteError("report " + steamId, r.Error); done(r.Ok, r.Ok ? null : r.Error); });
            });
        }

        internal static void Revoke(ulong steamId, string reason, Action<bool, int, string> done)
        {
            var body = new Dictionary<string, object> { ["steam_id"] = steamId.ToString(), ["reason"] = Trim(reason ?? "", 200) };
            Task.Run(async () =>
            {
                var r = await Send(HttpMethod.Post, "/reports/revoke", body);
                int n = 0;
                try { if (r.Ok && r.Body.TryGetProperty("revoked", out var v)) v.TryGetInt32(out n); } catch { }
                OnMain(() => { Cache.Remove(steamId); if (!r.Ok) NoteError("revoke " + steamId, r.Error); done(r.Ok, n, r.Ok ? null : r.Error); });
            });
        }

        private static string Trim(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max);

        // ---------------- check on join ----------------

        /// <summary>Every ~2 s: check players we have not checked yet (once per player object).</summary>
        /// <summary>Seconds after a player appears before automatic Kick/Ban (admin rank must be known by then).</summary>
        private const float ActionDelaySeconds = 10f;

        internal static void Tick()
        {
            var all = GPlayer.All();
            var alive = new HashSet<IntPtr>(all.Select(x => x.Ptr));
            CheckedObjects.RemoveWhere(x => !alive.Contains(x));   // addresses can be reused by new players
            var now = DateTime.UtcNow;
            foreach (var k in Cache.Where(kv => (now - kv.Value.At).TotalMinutes >= Math.Max(1, Cfg.CacheMinutes)).Select(kv => kv.Key).ToList()) Cache.Remove(k);

            if (!On || !Cfg.CheckOnJoin || !GuardConfig.Current.Enabled) return;
            foreach (var p in all)
            {
                try
                {
                    if (!p.IsValid || CheckedObjects.Contains(p.Ptr)) continue;
                    ulong id = p.TransportSteamId;           // only the unforgeable id
                    if (id == 0) continue;                   // not known yet - next tick
                    CheckedObjects.Add(p.Ptr);
                    if (Bans.Find(id) != null || Bans.Find(p.ReportedSteamId) != null) continue;   // banned locally: Bans kicks
                    if (!InFlight.Add(id)) continue;
                    Check(id, true, (res, err) =>
                    {
                        InFlight.Remove(id);
                        if (res == null) return;
                        // apply to the CURRENT session(s) of this SteamID (the player may have reconnected meanwhile)
                        foreach (var cur in GPlayer.All().Where(x => x.IsValid && x.TransportSteamId == id))
                        {
                            var target = cur;
                            float wait = Math.Max(0f, ActionDelaySeconds - SpawnTracker.SinceFirstSeen(target));
                            if (wait <= 0f) OnChecked(target, res);
                            else Scheduler.After(wait, () => { if (target.IsValid) OnChecked(target, res); });
                        }
                    });
                }
                catch { }
            }
        }

        private static void OnChecked(GPlayer p, NetCheck res)
        {
            var others = res.Others;
            if (others.Count == 0 || !p.IsValid) return;
            if (Bans.Find(p.TransportSteamId) != null) return;   // already banned here
            int servers = res.OtherServers, verified = res.VerifiedOtherServers;
            string reasons = string.Join("; ", others.Take(3).Select(r => $"{r.Server}{(r.Verified ? "" : " (unverified)")}: {r.Reason}"));
            GuardLog.Detection($"[P90Guard Network] WARNING: {p} has cheat reports from {servers} server(s) of the network:");
            foreach (var r in others.Take(10))
                GuardLog.Detection($"    {r.Server}{(r.Verified ? "" : " (unverified server)")} - {r.Created:yyyy-MM-dd} - \"{r.Reason}\"{(r.Until.HasValue ? $" (until {r.Until:yyyy-MM-dd})" : "")}{(r.EvidenceCount > 0 ? $" (+{r.EvidenceCount} anti-cheat lines)" : "")}");
            Api.RaiseFlagged(p, "network", $"cheat reports from {servers} server(s): {reasons}", false, others.Count, null);
            Webhook.Send("Reported player joined", $"{p}\n{servers} server(s): {reasons}", 0xE5484D);

            var action = Cfg.OnReportedJoin;
            // automatic Kick/Ban only on reports from VERIFIED servers (a server on probation cannot get anyone kicked)
            if ((action == NetworkAction.Kick || action == NetworkAction.Ban) && verified < Math.Max(1, Cfg.ActionMinServers))
                action = NetworkAction.NotifyAdmins;
            if (p.IsAdmin) action = NetworkAction.NotifyAdmins;   // never act on exempt players automatically

            if (action != NetworkAction.Log) NotifyAdmins(GuardConfig.Current.Messages.NetworkAdminNotice
                .Replace("{nick}", p.Nick).Replace("{servers}", servers.ToString()).Replace("{reasons}", reasons));

            if (action == NetworkAction.Kick)
            {
                GuardLog.Detection($"[P90Guard Network] KICK {p} - reported by {servers} servers");
                p.Kick(GuardConfig.Current.Messages.Kick.Replace("{reason}", $"cheat reports from {servers} servers"));
            }
            else if (action == NetworkAction.Ban)
            {
                int minutes = Math.Max(0, Cfg.ActionBanMinutes);
                var until = Bans.Add(p.TransportSteamId, p.Nick, minutes, $"network: reported by {servers} servers", "P90Guard Network");
                GuardLog.Detection($"[P90Guard Network] BAN {p} - reported by {servers} servers");
                var m = GuardConfig.Current.Messages;
                p.Kick(minutes == 0 ? m.BanPermanent.Replace("{reason}", "cheat reports") :
                    m.Ban.Replace("{reason}", "cheat reports").Replace("{until}", until.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
            }
        }

        internal static void NotifyAdmins(string text)
        {
            foreach (var a in GPlayer.All())
            {
                try { if (a.IsValid && a.IsAdmin) G.ShowMessage(a.Go, text); } catch { }
            }
        }

        internal static int CacheCount => Cache.Count;
    }
}
