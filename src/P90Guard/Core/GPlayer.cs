using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace P90Guard.Core
{
    /// <summary>Thin wrapper over a player object (no dependency on any framework).</summary>
    internal sealed class GPlayer
    {
        private static readonly Dictionary<IntPtr, GPlayer> Cache = new();

        public GameObject Go { get; }
        public PlayerStatistics Stats { get; }
        public CharacterClassManager Ccm { get; }
        public global::Inventory Inv { get; }
        public GameRoles Roles { get; }
        public PlayerTransformSync Pts { get; }
        public IntPtr Ptr { get; }

        private GPlayer(GameObject go, PlayerStatistics ps)
        {
            Go = go; Stats = ps; Ptr = go.Pointer;
            Ccm = go.GetComponent<CharacterClassManager>();
            Inv = go.GetComponent<global::Inventory>();
            Roles = go.GetComponent<GameRoles>();
            Pts = go.GetComponent<PlayerTransformSync>();
        }

        public static GPlayer Get(GameObject go)
        {
            if (go == null) return null;
            if (Cache.TryGetValue(go.Pointer, out var p) && p.Go != null) return p;
            var ps = go.GetComponent<PlayerStatistics>();
            if (ps == null) return null;
            p = new GPlayer(go, ps);
            Cache[go.Pointer] = p;
            return p;
        }

        /// <summary>Owner of a component on a player object (or its children).</summary>
        public static GPlayer FromBehaviour(NetworkBehaviour nb)
        {
            if (nb == null) return null;
            try
            {
                var p = Get(nb.gameObject);
                if (p != null) return p;
                var ps = nb.GetComponentInParent<PlayerStatistics>();
                return ps != null ? Get(ps.gameObject) : null;
            }
            catch { return null; }
        }

        /// <summary>Player who sent a command (by connection). Commands on scene objects have no owner — use this.</summary>
        public static GPlayer FromConn(gu c)
        {
            if (c == null) return null;
            var ptr = c.Pointer;
            foreach (var p in All())
            {
                try { var pc = p.Conn; if (pc != null && pc.Pointer == ptr) return p; } catch { }
            }
            return null;
        }

        private static List<GPlayer> _all = new();
        private static int _allFrame = -1;

        /// <summary>All players (cached for the current frame: commands are checked many times per frame).</summary>
        public static List<GPlayer> All()
        {
            if (_allFrame == Time.frameCount) return _all;
            var res = new List<GPlayer>();
            try { foreach (var ps in UnityEngine.Object.FindObjectsOfType<PlayerStatistics>()) { var p = Get(ps.gameObject); if (p != null) res.Add(p); } }
            catch { }
            _all = res; _allFrame = Time.frameCount;
            return res;
        }

        public static void Forget(GameObject go) { if (go != null) Cache.Remove(go.Pointer); }

        /// <summary>Drop wrappers of players that left.</summary>
        public static void Cleanup()
        {
            var dead = new List<IntPtr>();
            foreach (var kv in Cache) { bool ok; try { ok = kv.Value.Go != null; } catch { ok = false; } if (!ok) dead.Add(kv.Key); }
            foreach (var k in dead) Cache.Remove(k);
        }

        public bool IsValid { get { try { return Go != null && Stats != null; } catch { return false; } } }

        public gu Conn => Ccm != null ? Ccm.Conn() : null;

        private ulong _transportId;
        /// <summary>SteamID64 from the transport (FizzySteamworks) — cannot be spoofed by the client. 0 if unknown.</summary>
        public ulong TransportSteamId
        {
            get
            {
                if (_transportId != 0) return _transportId;
                try { if (ulong.TryParse(Conn.Address(), out var id) && id > 76561197960265728UL) _transportId = id; } catch { }
                return _transportId;
            }
        }

        /// <summary>SteamID the client reported (0 until it sends it).</summary>
        public ulong ReportedSteamId { get { try { return Stats.steamId.m_SteamID; } catch { return 0; } } }

        /// <summary>Best known SteamID: transport first.</summary>
        public ulong SteamId => TransportSteamId != 0 ? TransportSteamId : ReportedSteamId;

        public string Nick
        {
            get
            {
                try { var n = Stats.myNick; return string.IsNullOrEmpty(n) ? Stats.mySteamNick ?? "?" : n; }
                catch { return "?"; }
            }
        }

        public int RoleId { get { try { return Ccm.curClass; } catch { return -1; } } }

        public Class RoleClass
        {
            get
            {
                try { var r = Ccm.roles; int id = RoleId; return r != null && id >= 0 && id < r.Length ? r[id] : null; }
                catch { return null; }
            }
        }

        public string RoleName { get { try { return RoleClass?.fullName ?? $"#{RoleId}"; } catch { return "?"; } } }
        public int MaxHealth { get { try { return RoleClass?.maxHP ?? 0; } catch { return 0; } } }
        public int Health { get { try { return Stats.curHP; } catch { return 0; } } }

        public bool HasItem(int itemId)
        {
            try { var arr = Inv?._currentItems; if (arr == null) return false; for (int i = 0; i < arr.Length; i++) if (arr[i] == itemId) return true; }
            catch { }
            return false;
        }

        public Vector3 Position
        {
            get
            {
                try { if (Pts != null) return Pts.SyncedPosition(); } catch { }
                try { return Go.transform.position; } catch { return Vector3.zero; }
            }
        }

        public bool IsAdmin
        {
            get
            {
                var cfg = GuardConfig.Current;
                try { if (cfg.ExemptAdminToolsAccess && Roles != null && Roles.hasAccessToAdminTools) return true; } catch { }
                if (cfg.ExemptMinAdminRank > 0 && G.AdminRank(Go) >= cfg.ExemptMinAdminRank) return true;
                var id = TransportSteamId;   // the reported id could be someone else's
                return id != 0 && cfg.ExemptSteamIds != null && cfg.ExemptSteamIds.Contains(id.ToString());
            }
        }

        public void Kick(string reason)
        {
            var conn = Conn;
            if (conn == null) return;
            try { Ccm.TargetKick(conn, reason); } catch { }
            Scheduler.After(0.5f, () => { try { conn.Disconnect(); } catch { } });
        }

        public override string ToString() => $"{Nick} ({SteamId}, {RoleName})";
    }

    internal static class Items
    {
        private static global::Inventory Any => UnityEngine.Object.FindObjectOfType<global::Inventory>();

        public static string Name(int id)
        {
            try { var inv = Any; if (inv?._items == null || id < 0 || id >= inv._items.Count) return $"#{id}"; return inv._items[id].Name; }
            catch { return $"#{id}"; }
        }
    }
}
