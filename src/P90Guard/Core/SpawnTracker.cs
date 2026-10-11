using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace P90Guard.Core
{
    /// <summary>
    /// Per-player state for "spawn" commands. The game trusts the client to give itself its spawn loadout
    /// (CmdAddItem, CmdSetHP, CmdSetAmmo, CmdSp...). A vanilla client sends them only from its class-change hook,
    /// and exactly this many per class change (from the game code):
    ///   spectator:   CmdSp 1, CmdSetHP 1 (= class max HP), CmdClearInv 1, CmdSetDead 1 (true), CmdSpD 1;
    ///   other class: CmdSp 1, CmdSetHP 1, CmdClearInv 2, CmdSetDead 1 (false), CmdSetAmmo 2 (arg = class id),
    ///                CmdAddItem once per item of the class loadout.
    /// Every class change (server-side, so before the client knows) grants exactly these permissions.
    /// Class changes closer than MergeSeconds add up (the previous class's coroutine may still be sending).
    /// Permissions expire after LifetimeSeconds — enough for a slow client, but no reserve for later.
    /// State is keyed by the player OBJECT, so it works before the SteamID arrives and reconnects do not mix.
    /// </summary>
    internal static class SpawnTracker
    {
        private sealed class State
        {
            public GPlayer Owner;
            public float FirstSeen = -1f, SpawnAt = -1f;
            public int LastRole = int.MinValue;
            public readonly Dictionary<string, int> Tokens = new();
            public readonly Dictionary<int, int> Kit = new(), Given = new();
            public readonly HashSet<int> Roles = new(), Hp = new();
            public bool KitKnown;
            public int Merges;
            public readonly Dictionary<int, float> ItemGoneAt = new();
            public float LastHeal = -999f;
        }

        private const float MergeSeconds = 60f;
        private const float LifetimeSeconds = 120f;
        private const int MaxMerges = 10;
        private const int SpectatorRoleId = 1;   // "Spectator Class"

        private static readonly Dictionary<IntPtr, State> States = new();
        private static float _nextPoll, _nextClean;

        private static State Get(GPlayer p)
        {
            if (!States.TryGetValue(p.Ptr, out var st) || st.Owner != p)
                States[p.Ptr] = st = new State { Owner = p, FirstSeen = Time.realtimeSinceStartup };
            return st;
        }

        /// <summary>Safety net: catch class changes the setter patch might miss (poll 4x per second).</summary>
        public static void Tick()
        {
            float t = Time.realtimeSinceStartup;
            if (t < _nextPoll) return;
            _nextPoll = t + 0.25f;
            foreach (var p in GPlayer.All())
            {
                try { if (p.IsValid && Get(p).LastRole != p.RoleId) OnClassChanged(p); } catch { }
            }
            if (t >= _nextClean)
            {
                _nextClean = t + 30f;
                foreach (var k in States.Where(kv => kv.Value.Owner == null || !kv.Value.Owner.IsValid).Select(kv => kv.Key).ToList()) States.Remove(k);
                GPlayer.Cleanup();
                var alive = new HashSet<IntPtr>();
                foreach (var p in GPlayer.All()) { try { if (p.IsValid) alive.Add(p.Ptr); } catch { } }
                try { Detect.Cleanup(alive); } catch { }
                try { Bans.Cleanup(alive); } catch { }
                try { Patches.ItemsTrackPatch.Cleanup(); } catch { }
            }
        }

        public static void OnClassChanged(GPlayer p)
        {
            if (p == null || !p.IsValid) return;
            var st = Get(p);
            float now = Time.realtimeSinceStartup;
            int role = p.RoleId;
            st.LastRole = role;
            bool merge = st.SpawnAt >= 0f && now - st.SpawnAt <= MergeSeconds && st.Merges < MaxMerges;
            if (!merge) { st.Tokens.Clear(); st.Kit.Clear(); st.Given.Clear(); st.Roles.Clear(); st.Hp.Clear(); st.KitKnown = false; st.Merges = 0; }
            else st.Merges++;
            st.SpawnAt = now;
            st.Roles.Add(role);

            bool spectator = role == SpectatorRoleId;
            Add(st.Tokens, "sp", 1);
            Add(st.Tokens, "hp", 1);
            Add(st.Tokens, "dead", 1);
            Add(st.Tokens, "clear", spectator ? 1 : 2);
            if (spectator) Add(st.Tokens, "spd", 1); else Add(st.Tokens, "ammo", 2);

            try
            {
                var c = p.RoleClass;
                if (c != null)
                {
                    st.Hp.Add(c.maxHP);
                    if (c.startItems != null) foreach (var it in c.startItems) st.Kit[it] = (st.Kit.TryGetValue(it, out var n) ? n : 0) + 1;
                    st.KitKnown = true;
                }
            }
            catch { }
        }

        private static void Add(Dictionary<string, int> d, string k, int n) => d[k] = (d.TryGetValue(k, out var v) ? v : 0) + n;
        private static bool Fresh(State st) => st.SpawnAt >= 0f && Time.realtimeSinceStartup - st.SpawnAt <= LifetimeSeconds;

        public static bool TakeToken(GPlayer p, string name)
        {
            var st = Get(p);
            if (!Fresh(st) || !st.Tokens.TryGetValue(name, out var n) || n <= 0) return false;
            st.Tokens[name] = n - 1;
            return true;
        }

        public static bool IsSpawnHp(GPlayer p, int hp) { var st = Get(p); return Fresh(st) && st.Hp.Contains(hp); }
        public static bool IsSpawnRole(GPlayer p, int roleId) { var st = Get(p); return Fresh(st) && st.Roles.Contains(roleId); }

        public static bool IsSpawnDeadState(GPlayer p, bool dead)
        {
            var st = Get(p);
            if (!Fresh(st)) return false;
            return dead ? st.Roles.Contains(SpectatorRoleId) : st.Roles.Any(r => r != SpectatorRoleId);
        }

        public static bool KitKnown(GPlayer p) => Get(p).KitKnown;
        public static bool IsKitItem(GPlayer p, int item) => Get(p).Kit.ContainsKey(item);

        public static bool TakeKitItem(GPlayer p, int item)
        {
            var st = Get(p);
            if (!Fresh(st) || !st.Kit.TryGetValue(item, out var max)) return false;
            st.Given.TryGetValue(item, out var n);
            if (n >= max) return false;
            st.Given[item] = n + 1;
            return true;
        }

        /// <summary>Seconds since the player object was first seen on the server.</summary>
        public static float SinceFirstSeen(GPlayer p) => Time.realtimeSinceStartup - Get(p).FirstSeen;

        // ---------- consumables ----------

        public static void OnItemRemoved(GPlayer p, int item) { try { Get(p).ItemGoneAt[item] = Time.realtimeSinceStartup; } catch { } }

        /// <summary>The item was in the inventory and disappeared not longer than `seconds` ago.</summary>
        public static bool ItemGoneRecently(GPlayer p, int item, float seconds) =>
            Get(p).ItemGoneAt.TryGetValue(item, out var t) && Time.realtimeSinceStartup - t <= seconds;

        /// <summary>Seconds since the previous heal, and remember this one.</summary>
        public static float SinceLastHeal(GPlayer p)
        {
            var st = Get(p); float now = Time.realtimeSinceStartup;
            float d = now - st.LastHeal; st.LastHeal = now; return d;
        }
    }
}
