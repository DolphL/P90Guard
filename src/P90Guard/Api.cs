using System;
using System.Collections.Generic;
using P90Guard.Core;

namespace P90Guard
{
    /// <summary>
    /// Public API for other plugins (websites, Discord bots, panels). Only primitive types are used, so another plugin
    /// can subscribe through reflection without referencing this assembly:
    /// <code>
    /// var t = Type.GetType("P90Guard.Api, P90Guard");
    /// t?.GetEvent("Flagged")?.AddEventHandler(null, new Action&lt;IReadOnlyDictionary&lt;string, object&gt;&gt;(d => ...));
    /// </code>
    /// Keys of Flagged: steam_id (ulong), nick, role, check, details, blocked (bool), count (int), points (int).
    /// Keys of Punished: steam_id, nick, action ("kick"/"ban"), reason.
    /// Handlers run on the main thread; keep them fast. Call the methods from the main thread only.
    /// </summary>
    public static class Api
    {
        public static event Action<IReadOnlyDictionary<string, object>> Flagged;
        public static event Action<IReadOnlyDictionary<string, object>> Punished;

        public static string Version => Plugin.Version;

        /// <summary>Is the SteamID64 banned by the anti-cheat right now.</summary>
        public static bool IsBanned(ulong steamId) => Bans.Find(steamId) != null;

        /// <summary>Current anti-cheat score of a player (points in the sliding window).</summary>
        public static int GetPoints(ulong steamId) => Detect.Points(steamId);

        internal static void RaiseFlagged(GPlayer p, string check, string details, bool blocked, int count, string nickFallback)
        {
            var h = Flagged;
            if (h == null) return;
            var d = new Dictionary<string, object>
            {
                ["steam_id"] = p?.SteamId ?? 0UL, ["nick"] = p?.Nick ?? nickFallback, ["role"] = p?.RoleName,
                ["check"] = check, ["details"] = details, ["blocked"] = blocked, ["count"] = count,
                ["points"] = p != null ? Detect.Points(p.TransportSteamId) : 0,
            };
            foreach (Action<IReadOnlyDictionary<string, object>> f in h.GetInvocationList())
            {
                try { f(d); } catch (Exception e) { GuardLog.Warn($"Api.Flagged handler: {e.Message}"); }
            }
        }

        internal static void RaisePunished(GPlayer p, string action, string reason)
        {
            var h = Punished;
            if (h == null) return;
            var d = new Dictionary<string, object> { ["steam_id"] = p?.SteamId ?? 0UL, ["nick"] = p?.Nick, ["action"] = action, ["reason"] = reason };
            foreach (Action<IReadOnlyDictionary<string, object>> f in h.GetInvocationList())
            {
                try { f(d); } catch (Exception e) { GuardLog.Warn($"Api.Punished handler: {e.Message}"); }
            }
        }
    }
}
