using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using P90Guard.Core;
using UnityEngine;

namespace P90Guard
{
    /// <summary>
    /// P90Guard - server-side anti-cheat for SCP: Project 90 dedicated servers (BepInEx 6 IL2CPP).
    /// The game trusts clients with many commands; P90Guard checks them on the server and blocks the impossible ones.
    /// Standalone: depends only on BepInEx and the game.
    /// </summary>
    [BepInPlugin(Guid, "P90Guard", Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "p90guard";
        public const string Version = "1.0.0";

        internal static Harmony Harmony;

        public override void Load()
        {
            GuardLog.Source = Log;
            GuardLog.LogDir = Path.Combine(Paths.BepInExRootPath, "P90Guard", "logs");
            GuardConfig.FilePath = Path.Combine(Paths.ConfigPath, "P90Guard.json");
            Bans.FilePath = Path.Combine(Paths.ConfigPath, "P90Guard.bans.json");

            Log.LogInfo($"P90Guard v{Version}");
            if (!Application.isBatchMode)
            {
                Log.LogWarning("Not a dedicated server (no -batchmode) - P90Guard does nothing on clients.");
                return;
            }

            GuardConfig.Load();
            Bans.Load();
            if (!GuardConfig.Current.Enabled) Log.LogWarning("Enabled=false in the config: patches are installed, but no checks run until it is set to true.");
            if (!GameVersion.Check()) return;

            AddComponent<GuardTicker>();

            Harmony = new Harmony(Guid);
            var disabled = GuardConfig.Current.DisabledPatches.Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int ok = 0, fail = 0;
            foreach (var t in typeof(Plugin).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
            {
                if (disabled.Contains(t.Name)) { Log.LogWarning($"patch {t.Name} disabled in config"); continue; }
                try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e) { fail++; Log.LogError($"patch {t.Name} failed: {e.InnerException?.Message ?? e.Message}"); }
            }
            Log.LogInfo($"P90Guard ready: {ok} patches{(fail > 0 ? $", {fail} FAILED" : "")}. Config: {GuardConfig.FilePath}");
        }
    }
}
