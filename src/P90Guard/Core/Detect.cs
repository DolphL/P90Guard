using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace P90Guard.Core
{
    /// <summary>Reporting, blocking decisions, points and punishments.</summary>
    internal static class Detect
    {
        // ---------- server-originated commands ----------
        // Some server plugins execute Commands on the server itself (the game calls Cmd... from server code, which only
        // works on a listen host). Those must never be treated as client actions.
        private static int _originDepth, _originFrame = -1;
        internal static void EnterServerOrigin() { if (_originFrame != Time.frameCount) { _originDepth = 0; _originFrame = Time.frameCount; } _originDepth++; }
        internal static void ExitServerOrigin() { if (_originDepth > 0) _originDepth--; }
        internal static bool InServerOrigin => _originDepth > 0 && _originFrame == Time.frameCount;

        /// <summary>The plugin is on and the current command came from a client.</summary>
        internal static bool Active => GuardConfig.Current.Enabled && Application.isBatchMode && !InServerOrigin;

        /// <summary>Sender of a command, or null if it must not be checked (server-originated, admin, unknown).</summary>
        internal static GPlayer Sender(Mirror.NetworkBehaviour a, gu c)
        {
            try
            {
                if (!Active) return null;
                var p = GPlayer.FromConn(c) ?? GPlayer.FromBehaviour(a);
                return p == null || p.IsAdmin ? null : p;
            }
            catch { return null; }
        }

        internal static bool IsOff(string check) => GuardConfig.Current.Check(check).Mode == CheckMode.Off;

        // ---------- hits ----------

        private static readonly Dictionary<(IntPtr, string), (float Last, int Count)> Repeats = new();
        private static readonly Dictionary<ulong, List<(float At, int Points)>> Score = new();
        private static readonly Dictionary<ulong, List<DateTime>> Kicks = new();
        internal static readonly List<string> Recent = new();
        /// <summary>Recent detection lines per player (by transport SteamID) - evidence for network reports.</summary>
        private static readonly Dictionary<ulong, LinkedList<string>> ByPlayer = new();

        internal static List<string> Evidence(ulong steamId, int max)
        {
            if (steamId == 0 || max <= 0 || !ByPlayer.TryGetValue(steamId, out var l)) return new List<string>();
            return l.Skip(Math.Max(0, l.Count - max)).ToList();
        }

        /// <summary>
        /// A check fired. Logs (rate-limited), adds points, punishes, notifies. Returns true if the command must be blocked.
        /// </summary>
        internal static bool Hit(GPlayer p, string check, string details, string nickFallback = null)
        {
            try
            {
                var cfg = GuardConfig.Current;
                var cc = cfg.Check(check);
                if (cc.Mode == CheckMode.Off || !Active) return false;
                bool spoof = check == "steamid_spoof" || check == "role_spoof";
                if (p != null && !spoof && p.IsAdmin) return false;

                bool block = cc.Mode == CheckMode.Block;
                // grace: a loading client is only reported - never blocked, no points, no kick (except identity spoofing)
                bool grace = false;
                if (!spoof && p != null)
                {
                    float since = SpawnTracker.SinceFirstSeen(p);
                    float limit = p.SteamId == 0 ? cfg.NoSteamIdGraceSeconds : cfg.JoinGraceSeconds;
                    if (since < limit) { grace = true; block = false; }
                }

                var key = (p?.Ptr ?? IntPtr.Zero, check);
                float now = Time.realtimeSinceStartup;
                Repeats.TryGetValue(key, out var r);
                r.Count++;
                bool show = r.Count == 1 || now - r.Last >= Math.Max(0f, cfg.RepeatLogSeconds);
                if (show) r.Last = now;
                Repeats[key] = r;

                if (show)
                {
                    string who = p != null ? p.ToString() : nickFallback ?? "?";
                    string line = $"[P90Guard] {(block ? "BLOCKED" : "SUSPICIOUS")} {check}: {who} - {details}{(r.Count > 1 ? $" (x{r.Count})" : "")}{(grace ? " [join grace]" : "")}";
                    GuardLog.Detection(line);
                    lock (Recent) { Recent.Add($"{DateTime.Now:HH:mm:ss} {line}"); if (Recent.Count > 200) Recent.RemoveAt(0); }
                    ulong tid = p?.TransportSteamId ?? 0;
                    if (tid != 0)
                    {
                        if (!ByPlayer.TryGetValue(tid, out var pl)) ByPlayer[tid] = pl = new LinkedList<string>();
                        pl.AddLast($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC {(block ? "BLOCKED" : "SUSPICIOUS")} {check}: {details}{(r.Count > 1 ? $" (x{r.Count})" : "")}");
                        while (pl.Count > 50) pl.RemoveFirst();
                        if (ByPlayer.Count > 2000) ByPlayer.Clear();
                    }
                    Api.RaiseFlagged(p, check, details, block, r.Count, nickFallback);
                    if (block || !cfg.Webhook.OnlyBlockedAndPunishments)
                        Webhook.Send($"{(block ? "Blocked" : "Suspicious")}: {check}", $"{who}\n{details}", block ? 0xE5484D : 0xF5A524);
                }

                if (p != null && !grace)
                {
                    if (cc.InstantKick)
                    {
                        Punish(p, kick: true, ban: false, reason: check, msg: spoof ? cfg.Messages.KickSpoof : null);
                    }
                    else if (cc.Points > 0) AddPoints(p, check, cc.Points);
                }
                return block;
            }
            catch (Exception e) { GuardLog.Warn($"detect {check}: {e.Message}"); return false; }
        }

        // ---------- points & punishments ----------

        private static void AddPoints(GPlayer p, string check, int points)
        {
            var pc = GuardConfig.Current.Punishments;
            ulong id = p.TransportSteamId;   // only the unforgeable id (a reported one could frame another player)
            if (id == 0) return;
            float now = Time.realtimeSinceStartup;
            if (!Score.TryGetValue(id, out var list)) Score[id] = list = new();
            list.Add((now, points));
            float window = Math.Max(1f, pc.WindowMinutes) * 60f;
            list.RemoveAll(x => now - x.At > window);
            int total = list.Sum(x => x.Points);
            if (!pc.Enabled) return;
            if (pc.BanAtPoints > 0 && total >= pc.BanAtPoints) { list.Clear(); Punish(p, kick: true, ban: true, reason: $"{check}, {total} points"); }
            else if (pc.KickAtPoints > 0 && total >= pc.KickAtPoints) { list.Clear(); Punish(p, kick: true, ban: false, reason: $"{check}, {total} points"); }
        }

        internal static int Points(ulong steamId)
        {
            if (!Score.TryGetValue(steamId, out var list)) return 0;
            float window = Math.Max(1f, GuardConfig.Current.Punishments.WindowMinutes) * 60f, now = Time.realtimeSinceStartup;
            return list.Where(x => now - x.At <= window).Sum(x => x.Points);
        }

        private static readonly HashSet<IntPtr> Punishing = new();

        private static void Punish(GPlayer p, bool kick, bool ban, string reason, string msg = null)
        {
            if (p == null || !p.IsValid || !Punishing.Add(p.Ptr)) return;   // one punishment per player object
            var cfg = GuardConfig.Current;
            ulong id = p.TransportSteamId;   // bans only by the unforgeable id; without it - kick only
            string who = p.ToString();

            // repeated kicks escalate to a ban
            if (!ban && id != 0 && cfg.Punishments.Enabled && cfg.Punishments.BanAfterKicks > 0)
            {
                if (!Kicks.TryGetValue(id, out var k)) Kicks[id] = k = new();
                k.RemoveAll(t => (DateTime.UtcNow - t).TotalHours > Math.Max(0.1f, cfg.Punishments.KickMemoryHours));
                k.Add(DateTime.UtcNow);
                if (k.Count >= cfg.Punishments.BanAfterKicks) { ban = true; reason += $", {k.Count} kicks"; k.Clear(); }
            }

            string text;
            if (ban && id != 0)
            {
                int minutes = Math.Max(0, cfg.Punishments.BanMinutes);
                var until = Bans.Add(id, p.Nick, minutes, reason, "P90Guard");
                text = minutes == 0 ? cfg.Messages.BanPermanent.Replace("{reason}", reason)
                                    : cfg.Messages.Ban.Replace("{reason}", reason).Replace("{until}", until.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                GuardLog.Detection($"[P90Guard] BAN {who} - {reason} ({(minutes == 0 ? "permanent" : minutes + " min")})");
                Webhook.Send("Ban", $"{who}\n{reason}\n{(minutes == 0 ? "permanent" : $"{minutes} min")}", 0x8B0000);
            }
            else
            {
                text = msg ?? cfg.Messages.Kick.Replace("{reason}", reason);
                GuardLog.Detection($"[P90Guard] KICK {who} - {reason}");
                Webhook.Send("Kick", $"{who}\n{reason}", 0xC2410C);
            }
            Api.RaisePunished(p, ban ? "ban" : "kick", reason);
            Scheduler.After(0.3f, () => { try { p.Kick(text); } catch { } });
            Scheduler.After(5f, () => Punishing.Remove(p.Ptr));
        }

        /// <summary>Forget per-object state of players that left (object pointers can be reused by new players).</summary>
        internal static void Cleanup(HashSet<IntPtr> alive)
        {
            foreach (var k in Repeats.Keys.Where(k => k.Item1 != IntPtr.Zero && !alive.Contains(k.Item1)).ToList()) Repeats.Remove(k);
            float window = Math.Max(1f, GuardConfig.Current.Punishments.WindowMinutes) * 60f, now = Time.realtimeSinceStartup;
            foreach (var kv in Score.ToList()) { kv.Value.RemoveAll(x => now - x.At > window); if (kv.Value.Count == 0) Score.Remove(kv.Key); }
            double hours = Math.Max(0.1f, GuardConfig.Current.Punishments.KickMemoryHours);
            foreach (var kv in Kicks.ToList()) { kv.Value.RemoveAll(t => (DateTime.UtcNow - t).TotalHours > hours); if (kv.Value.Count == 0) Kicks.Remove(kv.Key); }
        }
    }
}
