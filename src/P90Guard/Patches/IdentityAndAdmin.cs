using System;
using HarmonyLib;
using Mirror;
using P90Guard.Core;
using Steamworks;

namespace P90Guard.Patches
{
    // =====================================================================================
    //  Identity: the client reports its own SteamID. Compared with the transport (Steam P2P) SteamID.
    // =====================================================================================

    /// <summary>CmdSetSteamData(steamId, nick): someone else's SteamID = impersonation (admin rank, badge, bans).</summary>
    [HarmonyPatch(typeof(PlayerStatistics), G.PS_UserCode_CmdSetSteamData)]
    internal static class SteamIdSpoof
    {
        private static bool Prefix(PlayerStatistics __instance, ulong a, string b)
        {
            try
            {
                if (!Detect.Active || Detect.IsOff("steamid_spoof")) return true;
                var p = GPlayer.Get(__instance.gameObject);
                ulong real = p?.TransportSteamId ?? 0;
                if (real == 0 || real == a) return true;   // transport id unknown -> cannot judge
                return !Detect.Hit(p, "steamid_spoof", $"sent SteamID {a}, but the connection belongs to {real} (impersonation)", b);
            }
            catch (Exception e) { GuardLog.Warn($"steamid_spoof: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdAssignRoleBySteamId(CSteamID): role, badge and admin tools are given by the SteamID in the argument.</summary>
    [HarmonyPatch(typeof(GameRoles), G.GR_UserCode_AssignRoleBySteamId)]
    internal static class RoleSpoof
    {
        private static bool Prefix(GameRoles __instance, CSteamID a)
        {
            try
            {
                if (!Detect.Active || Detect.IsOff("role_spoof")) return true;
                var p = GPlayer.Get(__instance.gameObject);
                ulong real = p?.TransportSteamId ?? 0;
                if (real == 0 || real == a.m_SteamID) return true;
                return !Detect.Hit(p, "role_spoof", $"requested the role of SteamID {a.m_SteamID}, real {real} (stealing a role / admin tools)");
            }
            catch (Exception e) { GuardLog.Warn($"role_spoof: {e.Message}"); return true; }
        }
    }

    // =====================================================================================
    //  Admin-only commands without a server-side permission check
    // =====================================================================================

    internal static class AdminOnly
    {
        /// <summary>true = execute.</summary>
        internal static bool Check(NetworkBehaviour a, gu c, string what)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null) return true;
                return !Detect.Hit(p, "admin_cmd", $"sent an admin-panel command without permissions: {what}");
            }
            catch (Exception e) { GuardLog.Warn($"admin_cmd: {e.Message}"); return true; }
        }
    }

    [HarmonyPatch(typeof(RoundManager), G.RM_InvokeRoundLockOn)]
    internal static class AdminRoundLockOn { private static bool Prefix(NetworkBehaviour a, gu c) => AdminOnly.Check(a, c, "round lock ON"); }

    [HarmonyPatch(typeof(RoundManager), G.RM_InvokeRoundLockOff)]
    internal static class AdminRoundLockOff { private static bool Prefix(NetworkBehaviour a, gu c) => AdminOnly.Check(a, c, "round lock OFF"); }

    [HarmonyPatch(typeof(RoundModeManager), G.RMM_InvokeSetBool)]
    internal static class AdminRoundMode { private static bool Prefix(NetworkBehaviour a, gu c) => AdminOnly.Check(a, c, "round mode switch"); }

    [HarmonyPatch(typeof(ZoneGenerator), G.ZG_InvokeReGenerateMap)]
    internal static class AdminRegenerateMap { private static bool Prefix(NetworkBehaviour a, gu c) => AdminOnly.Check(a, c, "regenerate map"); }

    /// <summary>
    /// CmdSendInventoryToAdmin: a normal player clicking a nickname in the player list also sends it — so no report,
    /// just do not reveal someone else's inventory to a non-admin (only when admin_cmd is in block mode).
    /// </summary>
    [HarmonyPatch(typeof(global::Inventory), G.Inv_InvokeSendInvToAdmin)]
    internal static class AdminInventoryPeek
    {
        private static bool Prefix(NetworkBehaviour a, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null) return true;
                return GuardConfig.Current.Check("admin_cmd").Mode != CheckMode.Block;
            }
            catch { return true; }
        }
    }

    /// <summary>Built-in admin panel action without rank. The game itself refuses it — report only.</summary>
    [HarmonyPatch(typeof(PlayerAdmin), G.PA_UserCode_ExecuteAdminAction)]
    internal static class AdminPanelAction
    {
        private static void Prefix(PlayerAdmin __instance, string a)
        {
            try
            {
                if (!Detect.Active || Detect.IsOff("admin_action")) return;
                var p = GPlayer.Get(__instance.gameObject);
                if (p == null || p.IsAdmin) return;
                Detect.Hit(p, "admin_action", $"sent an admin-panel action without rank: {a} (refused by the game)");
            }
            catch { }
        }
    }

    /// <summary>CmdSetImDedicatedServer: a normal client never sends it.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeSetImDedicated)]
    internal static class FakeServer
    {
        private static bool Prefix(NetworkBehaviour a, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null) return true;
                return !Detect.Hit(p, "fake_server", "sent the service command \"I am the dedicated server\"");
            }
            catch { return true; }
        }
    }

    /// <summary>
    /// Door.CmdCloseDoor has no owner check: any client can close any door from anywhere. The game sends it only
    /// from RestartRound, so from a client during a running round it is remote door closing.
    /// </summary>
    [HarmonyPatch(typeof(global::Door), G.Door_InvokeCloseDoor)]
    internal static class DoorClose
    {
        private static bool Prefix(NetworkBehaviour a, gu c)
        {
            try
            {
                if (!Detect.Active) return true;
                var rm = RoundManager.singleton;
                if (rm == null || rm.info != "started") return true;
                var p = GPlayer.FromConn(c);
                if (p == null || p.IsAdmin) return true;
                string name = "?"; try { name = a.gameObject.name; } catch { }
                return !Detect.Hit(p, "door_close", $"closed door \"{name}\" remotely during the round");
            }
            catch { return true; }
        }
    }
}
