using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P90Guard.Core
{
    public enum CheckMode { Off, Log, Block }

    public sealed class CheckConfig
    {
        /// <summary>off: ignore; log: report only; block: the server does not execute the command (and reports).</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CheckMode Mode { get; set; } = CheckMode.Block;
        /// <summary>Points added to the player's score each time this check fires (see Punishments).</summary>
        public int Points { get; set; } = 5;
        /// <summary>Kick immediately when this check fires (independent of points).</summary>
        public bool InstantKick { get; set; }
        public CheckConfig() { }
        public CheckConfig(CheckMode mode, int points, bool instantKick = false) { Mode = mode; Points = points; InstantKick = instantKick; }
    }

    public sealed class PunishmentConfig
    {
        /// <summary>Automatic kick/ban by points. Off by default: watch the log for a few days first.</summary>
        public bool Enabled { get; set; } = false;
        /// <summary>Points are summed over this sliding window.</summary>
        public float WindowMinutes { get; set; } = 10f;
        /// <summary>Kick when the score reaches this value (0 = never).</summary>
        public int KickAtPoints { get; set; } = 15;
        /// <summary>Ban when the score reaches this value (0 = never).</summary>
        public int BanAtPoints { get; set; } = 40;
        /// <summary>Ban length in minutes (0 = permanent).</summary>
        public int BanMinutes { get; set; } = 1440;
        /// <summary>Each kick within this many hours counts as a repeat; the player is banned on the N-th kick (0 = off).</summary>
        public int BanAfterKicks { get; set; } = 3;
        public float KickMemoryHours { get; set; } = 6f;
    }

    public sealed class WebhookConfig
    {
        /// <summary>Empty = off. Discord webhook URL or any HTTP endpoint accepting JSON.</summary>
        public string Url { get; set; } = "";
        /// <summary>"discord" (embed message) or "json" (raw event object).</summary>
        public string Format { get; set; } = "discord";
        /// <summary>Send only blocked actions and punishments (not plain "log" detections).</summary>
        public bool OnlyBlockedAndPunishments { get; set; } = true;
        /// <summary>Do not send more than this many messages per minute.</summary>
        public int MaxPerMinute { get; set; } = 20;
        /// <summary>Prefix for Discord messages, e.g. a role mention "&lt;@&amp;123&gt;".</summary>
        public string DiscordMention { get; set; } = "";
        public string ServerName { get; set; } = "SCP: Project 90 server";
    }

    public sealed class LandmineConfig
    {
        /// <summary>The explosion point a client sends must be within this distance of the mine (meters).</summary>
        public float MaxDistance { get; set; } = 5f;
    }

    public sealed class CuffConfig
    {
        /// <summary>Maximum distance between cuffer and target (meters). The game checks it only on the client.</summary>
        public float MaxDistance { get; set; } = 12f;
    }

    public sealed class FixesConfig
    {
        /// <summary>
        /// Game bug on dedicated servers: exploded grenades are destroyed only on clients and stay on the server forever
        /// (players see "dead" grenades everywhere). Destroy them on the server after the explosion.
        /// Turn off if another plugin already does it.
        /// </summary>
        public bool DestroyExplodedGrenades { get; set; } = true;
        /// <summary>Same bug for landmines: destroy the mine on the server after it explodes; ignore repeated triggers.</summary>
        public bool DestroyExplodedLandmines { get; set; } = true;
    }

    public sealed class MessagesConfig
    {
        public string KickSpoof { get; set; } = "Anti-cheat: identity spoofing";
        public string Kick { get; set; } = "Anti-cheat: kicked ({reason})";
        public string Ban { get; set; } = "Anti-cheat: banned until {until} ({reason})";
        public string BanPermanent { get; set; } = "Anti-cheat: permanently banned ({reason})";
    }

    public sealed class GuardConfig
    {
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// The plugin is mapped to one game build. "safe": do nothing on an unknown build; "warn": patch anyway (may break);
        /// "off": skip the check.
        /// </summary>
        public string GameHashCheck { get; set; } = "safe";
        public List<string> AllowedGameHashes { get; set; } = new();

        /// <summary>Players with an admin rank (ServerAdmins.json) of at least this value are not checked (0 = check everyone).</summary>
        public int ExemptMinAdminRank { get; set; } = 1;
        /// <summary>Players with access to the built-in admin tools are not checked.</summary>
        public bool ExemptAdminToolsAccess { get; set; } = true;
        /// <summary>SteamID64 list that is never checked.</summary>
        public List<string> ExemptSteamIds { get; set; } = new();

        /// <summary>First seconds after a player appears on the server: report only, never block (client is loading).</summary>
        public float JoinGraceSeconds { get; set; } = 30f;
        /// <summary>A player who has not sent their SteamID yet: report only for this long, then checks apply normally.</summary>
        public float NoSteamIdGraceSeconds { get; set; } = 60f;
        /// <summary>Do not repeat the same report for the same player more often than this (the counter still grows).</summary>
        public float RepeatLogSeconds { get; set; } = 10f;
        /// <summary>Also write reports to BepInEx/P90Guard/logs/YYYY-MM-DD.log.</summary>
        public bool LogToFile { get; set; } = true;

        public Dictionary<string, CheckConfig> Checks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public PunishmentConfig Punishments { get; set; } = new();
        public WebhookConfig Webhook { get; set; } = new();
        public LandmineConfig Landmine { get; set; } = new();
        public CuffConfig Cuff { get; set; } = new();
        public FixesConfig Fixes { get; set; } = new();
        public MessagesConfig Messages { get; set; } = new();
        /// <summary>Harmony patch class names not to install (troubleshooting).</summary>
        public List<string> DisabledPatches { get; set; } = new();

        /// <summary>Default checks. New checks from plugin updates are added to an existing config automatically.</summary>
        public static readonly IReadOnlyDictionary<string, CheckConfig> DefaultChecks = new Dictionary<string, CheckConfig>
        {
            ["steamid_spoof"] = new(CheckMode.Block, 50, instantKick: true),
            ["role_spoof"] = new(CheckMode.Block, 50, instantKick: true),
            ["admin_cmd"] = new(CheckMode.Block, 5),
            ["admin_action"] = new(CheckMode.Log, 2),
            ["fake_server"] = new(CheckMode.Log, 2),
            ["add_item"] = new(CheckMode.Block, 5),
            ["clear_inv"] = new(CheckMode.Block, 5),
            ["set_hp"] = new(CheckMode.Block, 5),
            ["set_dead"] = new(CheckMode.Block, 5),
            ["set_ammo"] = new(CheckMode.Block, 3),
            ["spawn_tp"] = new(CheckMode.Block, 5),
            ["force_spectator"] = new(CheckMode.Block, 10),
            ["door_close"] = new(CheckMode.Block, 2),
            ["spawn_tool"] = new(CheckMode.Block, 5),
            ["landmine_far"] = new(CheckMode.Block, 10),
            ["cuff_far"] = new(CheckMode.Log, 3),
            ["use_missing"] = new(CheckMode.Log, 0),
            ["heal_spam"] = new(CheckMode.Log, 0),
        };

        public CheckConfig Check(string name) =>
            Checks != null && Checks.TryGetValue(name, out var c) && c != null ? c
            : DefaultChecks.TryGetValue(name, out var d) ? d : new CheckConfig(CheckMode.Log, 0);

        // ---------- load / save ----------

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };

        public static string FilePath { get; set; }
        public static GuardConfig Current { get; private set; } = WithDefaults(new GuardConfig());
        private static DateTime _mtime;

        private static GuardConfig WithDefaults(GuardConfig c)
        {
            c.Checks = c.Checks == null ? new(StringComparer.OrdinalIgnoreCase) : new(c.Checks, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in DefaultChecks)
                if (!c.Checks.ContainsKey(kv.Key)) c.Checks[kv.Key] = new CheckConfig(kv.Value.Mode, kv.Value.Points, kv.Value.InstantKick);
            c.Punishments ??= new(); c.Webhook ??= new(); c.Landmine ??= new(); c.Cuff ??= new(); c.Fixes ??= new(); c.Messages ??= new();
            c.AllowedGameHashes ??= new(); c.ExemptSteamIds ??= new(); c.DisabledPatches ??= new();
            return c;
        }

        /// <summary>Load (creating the file with defaults if missing); new default checks are written back to the file.</summary>
        public static void Load()
        {
            try
            {
                GuardConfig c;
                bool exists = File.Exists(FilePath);
                if (exists) c = JsonSerializer.Deserialize<GuardConfig>(File.ReadAllText(FilePath), Json) ?? new GuardConfig();
                else c = new GuardConfig();
                int before = c.Checks?.Count ?? -1;
                Current = WithDefaults(c);
                // rewrite the file only when it is new or new checks were added (keeps the admin's comments otherwise)
                if (!exists || Current.Checks.Count != before) Save();
            }
            catch (Exception e)
            {
                GuardLog.Error($"config: cannot read {FilePath}: {e.Message}. Using previous/default settings.");
            }
            try { _mtime = File.GetLastWriteTimeUtc(FilePath); } catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
                _mtime = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception e) { GuardLog.Warn($"config: cannot write {FilePath}: {e.Message}"); }
        }

        /// <summary>Reload if the file was edited (checked every few seconds).</summary>
        public static void ReloadIfChanged()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var m = File.GetLastWriteTimeUtc(FilePath);
                if (m == _mtime) return;
                _mtime = m;
                Load();
                GuardLog.Info("config reloaded");
            }
            catch { }
        }
    }
}
