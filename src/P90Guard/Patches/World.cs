using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using P90Guard.Core;
using UnityEngine;

namespace P90Guard.Patches
{
    // =====================================================================================
    //  SpawnTool (sandbox props: walls, landmines, fireworks...). The server does not check that the player has the
    //  tool at all — a modified client can place props anywhere. Vanilla clients send these only holding the tool.
    // =====================================================================================

    internal static class SpawnToolCheck
    {
        internal static bool Check(NetworkBehaviour a, gu c, string what)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("spawn_tool")) return true;
                var st = a?.TryCast<SpawnTool>();
                if (st != null && p.HasItem(st.invItemID)) return true;
                return !Detect.Hit(p, "spawn_tool", $"{what} with the sandbox SpawnTool without having it");
            }
            catch (Exception e) { GuardLog.Warn($"spawn_tool: {e.Message}"); return true; }
        }
    }

    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeSpawnProop)]
    internal static class SpawnToolSpawn
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            bool ok = SpawnToolCheck.Check(a, c, "placed an object");
            if (ok && Detect.Active)
            {
                // who places what — useful to trace mines/walls on the map
                try
                {
                    int idx = G.Peek(b, G.ReadInt);
                    var st = a.TryCast<SpawnTool>();
                    string name = idx.ToString();
                    try { if (st?.objects != null && idx >= 0 && idx < st.objects.Length && st.objects[idx].prefab != null) name = st.objects[idx].prefab.name; } catch { }
                    var p = GPlayer.FromConn(c) ?? GPlayer.FromBehaviour(a);
                    GuardLog.Info($"[SpawnTool] {p?.ToString() ?? "?"} places \"{name}\"");
                }
                catch { }
            }
            return ok;
        }
    }

    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeDeleteProop)] internal static class SpawnToolDelete { private static bool Prefix(NetworkBehaviour a, gu c) => SpawnToolCheck.Check(a, c, "deleted an object"); }
    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeRotateProop)] internal static class SpawnToolRotate { private static bool Prefix(NetworkBehaviour a, gu c) => SpawnToolCheck.Check(a, c, "rotated an object"); }
    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeRotateProopZ)] internal static class SpawnToolRotateZ { private static bool Prefix(NetworkBehaviour a, gu c) => SpawnToolCheck.Check(a, c, "rotated an object"); }
    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeChangeScale)] internal static class SpawnToolScale { private static bool Prefix(NetworkBehaviour a, gu c) => SpawnToolCheck.Check(a, c, "scaled an object"); }
    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeMoveObjectY)] internal static class SpawnToolMove { private static bool Prefix(NetworkBehaviour a, gu c) => SpawnToolCheck.Check(a, c, "moved an object"); }
    [HarmonyPatch(typeof(SpawnTool), G.ST_InvokeApplyMat)] internal static class SpawnToolMaterial { private static bool Prefix(NetworkBehaviour a, gu c) => SpawnToolCheck.Check(a, c, "changed an object material"); }

    // =====================================================================================
    //  Landmines. CmdSpawnGrenade(pos, velocity) has no owner check (any client, any mine) and takes the explosion
    //  point from the client. A vanilla client sends the mine's own position.
    // =====================================================================================

    [HarmonyPatch(typeof(global::Landmine), G.Landmine_InvokeSpawnGrenade)]
    internal static class LandmineTrigger
    {
        internal static readonly HashSet<IntPtr> Exploded = new();

        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            if (!Application.isBatchMode || a == null) return true;
            try
            {
                // fix: after the first explosion every client that sees the (not destroyed) mine sends it again
                if (GuardConfig.Current.Fixes.DestroyExplodedLandmines && Exploded.Contains(a.Pointer)) return false;
                if (Detect.Active && !Detect.IsOff("landmine_far"))
                {
                    var pos = G.Peek(b, G.ReadVector3);
                    float dist = Vector3.Distance(pos, a.transform.position);
                    if (dist > Math.Max(1f, GuardConfig.Current.Landmine.MaxDistance))
                    {
                        var p = GPlayer.FromConn(c);
                        if (p == null || !p.IsAdmin)
                            if (Detect.Hit(p, "landmine_far", $"triggered mine \"{a.gameObject.name}\" with the explosion {dist:0} m away from it (grenade anywhere)")) return false;
                    }
                }
                if (GuardConfig.Current.Fixes.DestroyExplodedLandmines) Exploded.Add(a.Pointer);
                return true;
            }
            catch (Exception e) { GuardLog.Debug($"landmine: {e.Message}"); return true; }
        }
    }

    /// <summary>Fix: destroy the mine on the server after it exploded (the game destroys it only on clients).</summary>
    [HarmonyPatch(typeof(global::Landmine), G.Landmine_UserCode_SpawnGrenade)]
    internal static class LandmineExplodedFix
    {
        private static void Postfix(global::Landmine __instance)
        {
            if (!Application.isBatchMode || __instance == null || !GuardConfig.Current.Fixes.DestroyExplodedLandmines) return;
            try
            {
                var go = __instance.gameObject;
                var ptr = __instance.Pointer;
                Scheduler.After(0.05f, () =>
                {
                    try { if (go != null) G.NetDestroy(go); } catch { }
                    // the native address may be reused by a new mine later
                    Scheduler.After(1f, () => LandmineTrigger.Exploded.Remove(ptr));
                });
            }
            catch { }
        }
    }

    /// <summary>Fix: destroy grenades on the server after they exploded (the game destroys them only on clients).</summary>
    [HarmonyPatch(typeof(global::Grenade), G.Grenade_Explode)]
    internal static class GrenadeExplodedFix
    {
        private static void Postfix(global::Grenade __instance)
        {
            if (!Application.isBatchMode || __instance == null || !GuardConfig.Current.Fixes.DestroyExplodedGrenades) return;
            try
            {
                var go = __instance.gameObject;
                float t = Mathf.Clamp(__instance.destroyTime, 0.5f, 30f) + 0.5f;
                Scheduler.After(t, () => { try { if (go != null) G.NetDestroy(go); } catch { } });
            }
            catch { }
        }
    }

    // =====================================================================================
    //  Handcuffs: the game checks the distance only on the client.
    // =====================================================================================

    [HarmonyPatch(typeof(HandCuffs), G.HC_InvokeCuffPlayer)]
    internal static class CuffFar
    {
        private static bool Prefix(NetworkBehaviour a, hd b, gu c)
        {
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null || Detect.IsOff("cuff_far")) return true;
                var target = GPlayer.FromBehaviour(G.Peek(b, G.ReadBehaviour));
                if (target == null || target == p) return true;
                float dist = Vector3.Distance(p.Position, target.Position);
                if (dist <= Math.Max(2f, GuardConfig.Current.Cuff.MaxDistance)) return true;
                return !Detect.Hit(p, "cuff_far", $"cuffed {target.Nick} from {dist:0} m away");
            }
            catch (Exception e) { GuardLog.Warn($"cuff_far: {e.Message}"); return true; }
        }
    }

    // =====================================================================================
    //  Consumables used without the item (report only by default)
    // =====================================================================================

    /// <summary>Fixes.BlockSpectatorItemUse: a dead player / spectator sends a stale "throw" / "heal" (client bug).</summary>
    internal static class DeadUse
    {
        internal static bool Blocked(NetworkBehaviour a, gu c, string what)
        {
            try
            {
                if (!Application.isBatchMode || Detect.InServerOrigin || !GuardConfig.Current.Fixes.BlockSpectatorItemUse) return false;
                var p = GPlayer.FromConn(c) ?? GPlayer.FromBehaviour(a);
                if (p == null || !p.IsDeadOrSpectator) return false;
                GuardLog.Debug($"[DeadUse] {p} is dead/spectator - \"{what}\" ignored (stale client state)");
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Medkit.HealMe: the game may remove the medkit by a separate command first — allow "recently had it".</summary>
    [HarmonyPatch(typeof(Medkit), G.Medkit_InvokeHealMe)]
    internal static class MedkitUse
    {
        private static bool Prefix(NetworkBehaviour a, gu c)
        {
            if (DeadUse.Blocked(a, c, "heal")) return false;
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null) return true;
                var m = a.TryCast<Medkit>();
                if (m == null) return true;
                float since = SpawnTracker.SinceLastHeal(p);
                bool has = p.HasItem(m.medkitID) || SpawnTracker.ItemGoneRecently(p, m.medkitID, Math.Max(15f, m.healDuration + 10f));
                // the player has TWO Medkit components: Medkit (5) and Pills (44) - everything is checked by this item's own ID
                if (!has) return !Detect.Hit(p, "use_missing", $"healed with \"{Items.Name(m.medkitID)}\" that is not in the inventory");
                if (since < Math.Max(1f, m.healDuration * 0.5f)) return !Detect.Hit(p, "heal_spam", $"\"{Items.Name(m.medkitID)}\" heals too often: {since:0.0} s apart (a heal lasts {m.healDuration:0.#} s)");
                return true;
            }
            catch { return true; }
        }
    }

    // SCP-458: no check on purpose. The item it would be compared with (a private runtime field) is not stored in the
    // prefab, so "has the pizza" cannot be checked reliably. The server itself removes the held item and heals only 13 HP.

    [HarmonyPatch(typeof(GrenadeManager), G.GM_InvokeSpawnGrenade)]
    internal static class GrenadeThrow
    {
        private static bool Prefix(NetworkBehaviour a, gu c)
        {
            if (DeadUse.Blocked(a, c, "throw grenade")) return false;
            try
            {
                var p = Detect.Sender(a, c);
                if (p == null) return true;
                var g = a.TryCast<GrenadeManager>();
                // thrown at the moment of death: items already dropped, the command arrives right after
                if (g == null || p.HasItem(g.grenadeItemID) || SpawnTracker.ItemGoneRecently(p, g.grenadeItemID, 5f)) return true;
                return !Detect.Hit(p, "use_missing", $"threw a grenade (\"{Items.Name(g.grenadeItemID)}\") that is not in the inventory");
            }
            catch { return true; }
        }
    }
}
