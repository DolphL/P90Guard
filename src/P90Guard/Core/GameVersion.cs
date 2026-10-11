using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace P90Guard.Core
{
    /// <summary>
    /// Game names are obfuscated and change with every game update. A patch bound by an old name may attach to the
    /// wrong method, so the plugin checks the exact game build (SHA-256 of GameAssembly.dll) before patching.
    /// </summary>
    internal static class GameVersion
    {
        /// <summary>Builds this version of the plugin is mapped to.</summary>
        internal static readonly string[] KnownHashes =
        {
            "ba69d16edce7b6fb2362823b8d7285b9905105c648289f0f25cbccdb9973164a",   // SCP: Project 90 v0.4.2 [F05]
        };

        internal static string Current { get; private set; }

        /// <summary>true = safe to patch.</summary>
        internal static bool Check()
        {
            var cfg = GuardConfig.Current;
            var mode = (cfg.GameHashCheck ?? "safe").Trim().ToLowerInvariant();
            if (mode == "off") return true;
            try
            {
                using var fs = File.OpenRead(Path.Combine(BepInEx.Paths.GameRootPath, "GameAssembly.dll"));
                using var sha = SHA256.Create();
                Current = string.Concat(sha.ComputeHash(fs).Select(b => b.ToString("x2")));
            }
            catch (Exception e) { GuardLog.Warn($"cannot hash GameAssembly.dll: {e.Message}"); return mode != "safe"; }

            bool ok = KnownHashes.Contains(Current) || cfg.AllowedGameHashes.Any(h => string.Equals(h?.Trim(), Current, StringComparison.OrdinalIgnoreCase));
            if (ok) { GuardLog.Info($"game build OK ({Current.Substring(0, 12)}...)"); return true; }

            GuardLog.Error("==============================================================");
            GuardLog.Error(" P90Guard: unknown game build (GameAssembly.dll differs from the one this version supports)");
            GuardLog.Error($"   current SHA-256: {Current}");
            GuardLog.Error(mode == "safe"
                ? "   GameHashCheck=safe: the anti-cheat is NOT active. Update P90Guard for this game version."
                : "   GameHashCheck=warn: patching anyway - checks may break or misfire.");
            GuardLog.Error("   If you are sure it is the same game build, add the hash to AllowedGameHashes in the config.");
            GuardLog.Error("==============================================================");
            return mode != "safe";
        }
    }
}
