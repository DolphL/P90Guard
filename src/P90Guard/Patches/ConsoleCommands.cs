using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using P90Guard.Core;

namespace P90Guard.Patches
{
    /// <summary>
    /// Server console commands "pg ..." (also "p90guard ..."). Hooks the game's console line handler, so it works with
    /// or without other frameworks: a line that is not ours goes to the game as usual.
    /// </summary>
    [HarmonyPatch(typeof(ServerConsole), G.SC_HandleCommand)]
    internal static class ConsoleCommands
    {
        private static bool Prefix(string a)
        {
            try
            {
                var line = (a ?? "").Trim();
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return true;
                var head = parts[0].ToLowerInvariant();
                if (head != "pg" && head != "p90guard") return true;
                Run(parts.Skip(1).ToArray());
                return false;
            }
            catch (Exception e) { GuardLog.Warn($"console: {e.Message}"); return false; }
        }

        private static List<string> _capture;

        private static void Out(string s)
        {
            if (_capture != null) _capture.Add(s);   // the caller prints the result itself
            else GuardLog.Info("[pg] " + s);
        }

        /// <summary>
        /// Run "pg ..." arguments for another plugin (e.g. a web panel) and return what was printed synchronously.
        /// Network answers arrive later and go to the log only.
        /// </summary>
        internal static string RunCaptured(string args)
        {
            var prev = _capture; _capture = new List<string>();
            try { Run((args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)); return string.Join("\n", _capture); }
            catch (Exception e) { return "error: " + e.Message; }
            finally { _capture = prev; }
        }

        private static void Run(string[] a)
        {
            var sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
            switch (sub)
            {
                case "status": Status(); break;
                case "cheatban": CheatBan(a.Skip(1).ToArray()); break;
                case "unban": Unban(a.Skip(1).ToArray()); break;
                case "check": CheckCmd(a.Skip(1).ToArray()); break;
                case "recent":
                    lock (Detect.Recent) { var l = Detect.Recent.Skip(Math.Max(0, Detect.Recent.Count - 20)).ToList(); if (l.Count == 0) Out("no detections yet"); foreach (var x in l) Out(x); }
                    break;
                default:
                    Out("pg status - plugin and network status");
                    Out("pg cheatban <nick|SteamID> <minutes|perm> <reason> - ban for cheating (and report to the network)");
                    Out("pg unban <SteamID> - remove the ban (and this server's network report)");
                    Out("pg check <nick|SteamID> - look the player up in the network");
                    Out("pg recent - last 20 detections");
                    break;
            }
        }

        private static void Status()
        {
            Out($"P90Guard v{Plugin.Version}, checks {(GuardConfig.Current.Enabled ? "ON" : "OFF")}, bans: {Bans.Count}, punishments: {(GuardConfig.Current.Punishments.Enabled ? "on" : "off")}");
            if (!Network.On) { Out("network: off (Network.Enabled / Url / ApiKey in P90Guard.json)"); return; }
            Out($"network: {GuardConfig.Current.Network.Url} - checking...");
            Network.Ping((ok, info) => Out(ok ? $"network: {info}" : $"network: ERROR {info}"));
            if (Network.LastError != null) Out("last network error: " + Network.LastError);
        }

        /// <summary>Player by SteamID64 or a part of the nickname (must be unique). Offline SteamID is allowed.</summary>
        private static bool Resolve(string q, out ulong id, out string nick, out GPlayer online)
        {
            id = 0; nick = null; online = null;
            if (string.IsNullOrWhiteSpace(q)) return false;
            if (ulong.TryParse(q, out var sid) && sid > 76561197960265728UL)
            {
                id = sid;
                online = GPlayer.All().FirstOrDefault(p => p.TransportSteamId == sid || p.ReportedSteamId == sid);
                nick = online?.Nick;
                return true;
            }
            var matches = GPlayer.All().Where(p => p.IsValid && p.Nick.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var exact = matches.Where(p => string.Equals(p.Nick, q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) matches = exact;
            if (matches.Count != 1) { Out(matches.Count == 0 ? $"player \"{q}\" not found" : $"\"{q}\" matches {matches.Count} players - use the SteamID"); return false; }
            online = matches[0]; id = online.TransportSteamId; nick = online.Nick;
            // only the SteamID of the connection: the reported one can be someone else's (spoofing)
            if (id == 0) { Out("the player's connection SteamID is unknown - use the SteamID from the Steam profile"); return false; }
            return true;
        }

        private static void CheatBan(string[] a)
        {
            if (a.Length < 3) { Out("usage: pg cheatban <nick|SteamID> <minutes|perm> <reason>"); return; }
            if (!Resolve(a[0], out var id, out var nick, out var online)) return;
            int minutes;
            if (a[1].Equals("perm", StringComparison.OrdinalIgnoreCase) || a[1] == "0") minutes = 0;
            else if (!int.TryParse(a[1], out minutes) || minutes < 0) { Out("duration: minutes or perm"); return; }
            var reason = string.Join(" ", a.Skip(2)).Trim();
            if (reason.Length < 3) { Out("write a reason (what cheat)"); return; }

            var until = Bans.Add(id, nick ?? "?", minutes, "cheating: " + reason, "admin (console)");
            try { Api.RaisePunished(online, id, nick, "ban", "cheating (admin): " + reason); } catch { }
            Out($"banned {nick ?? "?"} ({id}) {(minutes == 0 ? "permanently" : $"for {minutes} min")}: {reason}");
            GuardLog.ToFile($"[P90Guard] CHEATBAN {nick} ({id}) - {reason} ({(minutes == 0 ? "permanent" : minutes + " min")})");
            if (online != null && online.IsValid)
            {
                var m = GuardConfig.Current.Messages;
                online.Kick(minutes == 0 ? m.BanPermanent.Replace("{reason}", reason) :
                    m.Ban.Replace("{reason}", reason).Replace("{until}", until.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
            }

            if (!Network.On || !GuardConfig.Current.Network.ShareBans) { Out("not sent to the network (network off or ShareBans=false)"); return; }
            var evidence = Detect.Evidence(id, GuardConfig.Current.Network.EvidenceLines);
            Network.Report(id, nick, reason, minutes, evidence, (ok, err) =>
                Out(ok ? $"reported to the network ({evidence.Count} anti-cheat lines attached)" : $"network report FAILED: {err}"));
        }

        private static void Unban(string[] a)
        {
            if (a.Length < 1 || !ulong.TryParse(a[0], out var id)) { Out("usage: pg unban <SteamID>"); return; }
            bool had = Bans.Remove(id);
            Out(had ? $"ban of {id} removed" : $"{id} was not banned locally");
            if (!Network.On) return;
            Network.Revoke(id, string.Join(" ", a.Skip(1)), (ok, n, err) =>
                Out(ok ? $"network: {n} report(s) of this server revoked" : $"network revoke FAILED: {err}"));
        }

        private static void CheckCmd(string[] a)
        {
            if (a.Length < 1) { Out("usage: pg check <nick|SteamID>"); return; }
            if (!Resolve(string.Join(" ", a), out var id, out var nick, out _)) return;
            var local = Bans.Find(id);
            Out($"{nick ?? "?"} ({id}): local ban: {(local == null ? "no" : $"yes - {local.Reason}")}");
            if (!Network.On) { Out("network: off"); return; }
            Network.Check(id, false, (res, err) =>
            {
                if (res == null) { Out("network: ERROR " + err); return; }
                if (res.Reports.Count == 0) { Out("network: no reports"); return; }
                Out($"network: {res.Reports.Count} report(s), {res.OtherServers} other server(s):");
                foreach (var r in res.Reports.Take(15))
                    Out($"  #{r.Id} {r.Server}{(r.Own ? " (this server)" : "")} - {r.Created:yyyy-MM-dd} - \"{r.Reason}\"{(r.Until.HasValue ? $" until {r.Until:yyyy-MM-dd}" : "")}");
            });
        }
    }
}
