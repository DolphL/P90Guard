using System;
using Mirror;
using UnityEngine;

namespace P90Guard.Core
{
    /// <summary>
    /// All obfuscated game names in one place. The game ships IL2CPP with obfuscated member names that change on
    /// every game update, so this file is the only thing to re-map for a new game version.
    /// Mapped for SCP: Project 90 v0.4.2 [F05] (GameAssembly.dll SHA-256 in <see cref="GameVersion.KnownHashes"/>).
    /// "Invoker" = Mirror's static command handler (NetworkBehaviour a, NetworkReader b, NetworkConnectionToClient c);
    /// "UserCode" = the command body.
    /// </summary>
    internal static class G
    {
        // ---------- Command invokers / user code (Harmony targets) ----------
        public const string PS_UserCode_CmdSetSteamData = "ckl";     // PlayerStatistics.UserCode_CmdSetSteamData(ulong steamId, string nick)
        public const string GR_UserCode_AssignRoleBySteamId = "bdb"; // GameRoles.UserCode_CmdAssignRoleBySteamId(CSteamID)
        public const string PA_UserCode_ExecuteAdminAction = "cfj";  // PlayerAdmin.UserCode_CmdExecuteAdminAction(action, arg, target, sender)
        public const string RM_InvokeRoundLockOn = "cpt";            // RoundManager CmdSetRoundLockOn  (requiresAuthority=false)
        public const string RM_InvokeRoundLockOff = "cpv";           // RoundManager CmdSetRoundLockOff (requiresAuthority=false)
        public const string RMM_InvokeSetBool = "cqi";               // RoundModeManager CmdSetBool
        public const string Inv_InvokeSendInvToAdmin = "byi";        // Inventory CmdSendInventoryToAdmin (requiresAuthority=false)
        public const string ZG_InvokeReGenerateMap = "bbf";          // ZoneGenerator ReGenerateMap (requiresAuthority=false)
        public const string Door_InvokeCloseDoor = "bro";            // Door CmdCloseDoor (requiresAuthority=false)
        public const string CCM_InvokeSetImDedicated = "bmy";        // CharacterClassManager CmdSetImDedicatedServer
        public const string CCM_InvokeAddItem = "bns";               // CmdAddItem(Inventory, int)
        public const string CCM_InvokeClearInv = "bno";              // CmdClearInv(Inventory)
        public const string CCM_InvokeSp = "bnc";                    // CmdSp(Vector3)
        public const string CCM_InvokeSpD = "bnw";                   // CmdSpD()
        public const string CCM_InvokeForceInSpectator = "bng";      // CmdForceInSpectator(GameObject)
        public const string CCM_InvokeSetDead = "bni";               // CmdSetDead(bool)
        public const string CCM_InvokeSetAmmo = "bnu";               // CmdSetAmmo(int classId)
        public const string PS_InvokeSetHP = "ckq";                  // PlayerStatistics CmdSetHP(int)
        public const string Medkit_InvokeHealMe = "cbt";             // Medkit HealMe
        public const string Scp458_InvokeHealMe = "cxn";             // Scp458Item HealMe
        public const string GM_InvokeSpawnGrenade = "bvb";           // GrenadeManager CmdSpawnGrenade(Vector3)
        public const string Landmine_InvokeSpawnGrenade = "pf";      // Landmine CmdSpawnGrenade(Vector3, Vector3) (requiresAuthority=false)
        public const string Landmine_UserCode_SpawnGrenade = "pe";   // Landmine UserCode_CmdSpawnGrenade (server)
        public const string Grenade_Explode = "bub";                 // [Server] Grenade.Explode()
        public const string HC_InvokeCuffPlayer = "cbi";             // HandCuffs CmdCuffPlayer(Inventory)
        public const string ST_InvokeSpawnProop = "yz";              // SpawnTool commands
        public const string ST_InvokeDeleteProop = "zf";
        public const string ST_InvokeRotateProop = "yj";
        public const string ST_InvokeRotateProopZ = "yn";
        public const string ST_InvokeChangeScale = "yr";
        public const string ST_InvokeMoveObjectY = "yv";
        public const string ST_InvokeApplyMat = "zb";
        public const string NB_SendCommandInternal = "gif";          // NetworkBehaviour.SendCommandInternal
        public const string CCM_Prop_CurClass = "mzo";               // SyncVar property Networkcurclass (setter)
        public const string Inv_Prop_Items = "nba";                  // SyncVar property Network_currentItems (setter)
        public const string SC_HandleCommand = "bhm";                // ServerConsole: handle one console line (help/list/stop...)

        // ---------- Fields / calls ----------
        public static bool IsServer(this NetworkBehaviour nb) { try { return nb.nja; } catch { return false; } }   // isServer
        public static gu Conn(this NetworkBehaviour nb) { try { return nb.njj; } catch { return null; } }         // connectionToClient
        public static string Address(this gu conn) => conn?.llw;                                                // address (SteamID64 with FizzySteamworks)
        public static void Disconnect(this gt conn) => conn.ggj();
        public static void TargetKick(this CharacterClassManager ccm, gt conn, string reason) => ccm.blq(conn, reason);
        public static Vector3 SyncedPosition(this PlayerTransformSync pts) => pts.kko;                         // last position the client reported
        public static void NetDestroy(GameObject go) => NetworkServer.hbk(go);                                 // NetworkServer.Destroy
        /// <summary>5-second message on the screen of this player only (Scp294Cup.RpcMessage, shown to the owner).</summary>
        public static void ShowMessage(GameObject player, string text)
        {
            var cup = player.GetComponent<Scp294Cup>() ?? player.GetComponentInChildren<Scp294Cup>(true);
            cup?.cch(text ?? "");
        }

        // NetworkReader position (for peeking command arguments)
        public static int GetPos(this hd r) => r.lnx;
        public static void SetPos(this hd r, int p) => r.lnx = p;
        public static NetworkBehaviour ReadBehaviour(hd r) => hf.gxt(r);
        public static GameObject ReadGameObject(hd r) => hf.gxx(r);
        public static int ReadInt(hd r) => hf.gwb(r);
        public static bool ReadBool(hd r) => hf.gvn(r);
        public static Vector3 ReadVector3(hd r) => hf.gws(r);

        /// <summary>Read a command argument without consuming it: the game reads the same bytes afterwards.</summary>
        public static T Peek<T>(hd r, Func<hd, T> read)
        {
            int p = r.GetPos();
            try { return read(r); }
            finally { r.SetPos(p); }
        }

        public static int AdminRank(GameObject go)
        {
            try { var u = go.GetComponent<UserPermisionManager>(); return u != null ? (int)u.jzw : 0; }   // rank from ServerAdmins.json
            catch { return 0; }
        }
    }
}
