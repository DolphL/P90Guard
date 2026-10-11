using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using P90Guard.Core;
using UnityEngine;

namespace P90Guard.Patches
{
    /// <summary>Every server-side class change (SyncVar setter) grants the spawn-command permissions.</summary>
    [HarmonyPatch(typeof(CharacterClassManager), G.CCM_Prop_CurClass, MethodType.Setter)]
    internal static class ClassChangePatch
    {
        private static void Prefix(CharacterClassManager __instance, out int __state)
        {
            __state = int.MinValue;
            try { if (__instance.IsServer()) __state = __instance.curClass; } catch { }
        }

        private static void Postfix(CharacterClassManager __instance, int __state)
        {
            try
            {
                if (__state == int.MinValue || __instance.curClass == __state) return;
                SpawnTracker.OnClassChanged(GPlayer.Get(__instance.gameObject));
            }
            catch { }
        }
    }

    /// <summary>Remember when items leave an inventory (for "used an item that is not in the inventory").</summary>
    [HarmonyPatch(typeof(global::Inventory), G.Inv_Prop_Items, MethodType.Setter)]
    internal static class ItemsTrackPatch
    {
        private static readonly Dictionary<IntPtr, (global::Inventory Inv, int[] Items)> Last = new();

        internal static void Cleanup()
        {
            var dead = new List<IntPtr>();
            foreach (var kv in Last) { bool ok; try { ok = kv.Value.Inv != null && kv.Value.Inv.Pointer == kv.Key; } catch { ok = false; } if (!ok) dead.Add(kv.Key); }
            foreach (var k in dead) Last.Remove(k);
        }

        private static void Postfix(global::Inventory __instance)
        {
            try
            {
                if (!__instance.IsServer()) return;
                var arr = __instance._currentItems;
                var now = new int[arr?.Length ?? 0];
                for (int i = 0; i < now.Length; i++) now[i] = arr[i];
                int[] before = null;
                if (Last.TryGetValue(__instance.Pointer, out var prev) && prev.Inv == __instance) before = prev.Items;
                Last[__instance.Pointer] = (__instance, now);
                if (before == null) return;
                GPlayer p = null;
                foreach (var b in before)
                {
                    if (Array.IndexOf(now, b) >= 0) continue;
                    p ??= GPlayer.Get(__instance.gameObject);
                    if (p != null) SpawnTracker.OnItemRemoved(p, b);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Commands the SERVER sends to itself (some server plugins execute Cmd... on the server, like a listen host would)
    /// must not be checked as client actions. Runs before other plugins' prefixes on SendCommandInternal.
    /// </summary>
    [HarmonyPatch(typeof(NetworkBehaviour), G.NB_SendCommandInternal)]
    internal static class ServerOriginPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(NetworkBehaviour __instance, out bool __state)
        {
            __state = false;
            try { if (__instance != null && __instance.IsServer()) { Detect.EnterServerOrigin(); __state = true; } } catch { }
        }

        // Finalizer (not Postfix): runs even if the method or another patch throws, so the depth never gets stuck.
        private static void Finalizer(bool __state) { if (__state) Detect.ExitServerOrigin(); }
    }
}
