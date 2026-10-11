using System;
using HarmonyLib;
using Mirror;
using P90Guard.Core;

namespace P90Guard.Patches
{
    // =====================================================================================
    //  "Spawn" commands. A vanilla client sends them only from its own class-change hook (loadout, HP, ammo,
    //  spawn position). SpawnTracker grants exactly the permissions a vanilla client needs per class change.
    // =====================================================================================

    internal static class SpawnCmd
    {
        /// <summary>Command without arguments: a permission from the last class change, or a hit.</summary>
        internal static bool TokenOnly(NetworkBehaviour a, gu c, string token, string check, string why)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff(check) || SpawnTracker.TakeToken(p, token)) return true;
                return !Detect.Hit(p, check, why);
            }
            catch (Exception e) { GuardLog.Warn($"{check}: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdAddItem(Inventory, item): only the class loadout, only to yourself, not more than the loadout has.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeAddItem)]
    internal static class AddItem
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("add_item")) return true;
                NetworkBehaviour invNb = null; int item = -1;
                int pos = b.GetPos();
                try { invNb = G.ReadBehaviour(b); item = G.ReadInt(b); }
                finally { b.SetPos(pos); }
                var target = GPlayer.FromBehaviour(invNb);
                string name = Items.Name(item);

                string why;
                if (target != null && target != p) why = $"gave item \"{name}\" to another player {target.Nick}";
                else if (!SpawnTracker.KitKnown(p)) return true;                          // loadout unknown - do not risk it
                else if (SpawnTracker.TakeKitItem(p, item)) return true;
                else why = SpawnTracker.IsKitItem(p, item)
                    ? $"requested \"{name}\" more times than the {p.RoleName} loadout has"
                    : $"gave themselves \"{name}\", which is not in the {p.RoleName} loadout";
                return !Detect.Hit(p, "add_item", why);
            }
            catch (Exception e) { GuardLog.Warn($"add_item: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdClearInv(Inventory): own inventory on class change; someone else's never.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeClearInv)]
    internal static class ClearInv
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("clear_inv")) return true;
                var target = GPlayer.FromBehaviour(G.Peek(b, G.ReadBehaviour));
                string why;
                if (target != null && target != p) why = $"cleared the inventory of another player {target.Nick}";
                else if (SpawnTracker.TakeToken(p, "clear")) return true;
                else why = "cleared own inventory without a class change";
                return !Detect.Hit(p, "clear_inv", why);
            }
            catch (Exception e) { GuardLog.Warn($"clear_inv: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdSetHP(int): only the max HP of the new class, once per class change.</summary>
    [HarmonyPatch(typeof(PlayerStatistics), G.PS_InvokeSetHP)]
    internal static class SetHp
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("set_hp")) return true;
                int hp = G.Peek(b, G.ReadInt);
                int cur = p.Health, max = p.MaxHealth;
                if (hp == cur) return true;
                if (SpawnTracker.IsSpawnHp(p, hp) && SpawnTracker.TakeToken(p, "hp")) return true;
                string why = max > 0 && hp > max ? $"set own HP above the class maximum: {hp} > {max}" : $"changed own HP without a class change: {cur} -> {hp}";
                return !Detect.Hit(p, "set_hp", why);
            }
            catch (Exception e) { GuardLog.Warn($"set_hp: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdSetDead(bool): "dead" only when becoming a spectator, "alive" only when becoming a living class.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeSetDead)]
    internal static class SetDead
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("set_dead")) return true;
                bool dead = G.Peek(b, G.ReadBool);
                if (SpawnTracker.IsSpawnDeadState(p, dead) && SpawnTracker.TakeToken(p, "dead")) return true;
                return !Detect.Hit(p, "set_dead", $"set own \"dead = {dead}\" without a matching class change (god mode / fake death)");
            }
            catch (Exception e) { GuardLog.Warn($"set_dead: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdSetAmmo(classId): ammo of the new class only.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeSetAmmo)]
    internal static class SetAmmo
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("set_ammo")) return true;
                int role = G.Peek(b, G.ReadInt);
                if (SpawnTracker.IsSpawnRole(p, role) && SpawnTracker.TakeToken(p, "ammo")) return true;
                return !Detect.Hit(p, "set_ammo", $"gave themselves the ammo of class #{role} without a matching class change");
            }
            catch (Exception e) { GuardLog.Warn($"set_ammo: {e.Message}"); return true; }
        }
    }

    /// <summary>CmdSp / CmdSpD: spawn position — without a class change it is a teleport.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeSp)]
    internal static class SpawnTp
    {
        private static bool Prefix(NetworkBehaviour a, gu c) =>
            SpawnCmd.TokenOnly(a, c, "sp", "spawn_tp", "teleported with the spawn-position command (CmdSp) without a class change");
    }

    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeSpD)]
    internal static class SpawnTpDelayed
    {
        private static bool Prefix(NetworkBehaviour a, gu c) =>
            SpawnCmd.TokenOnly(a, c, "spd", "spawn_tp", "teleported with the delayed spawn-position command (CmdSpD) without becoming a spectator");
    }

    /// <summary>CmdForceInSpectator(GameObject): yourself (the game sends it on join) is fine, another player never.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_InvokeForceInSpectator)]
    internal static class ForceSpectator
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("force_spectator")) return true;
                var target = GPlayer.Get(G.Peek(b, G.ReadGameObject));
                if (target == null || target == p) return true;
                return !Detect.Hit(p, "force_spectator", $"forced another player {target.Nick} into spectators");
            }
            catch (Exception e) { GuardLog.Warn($"force_spectator: {e.Message}"); return true; }
        }
    }
}
