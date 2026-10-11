using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace P90Guard.Core
{
    public sealed class BanEntry
    {
        [JsonConverter(typeof(Bans.StringOrNumber))]
        public string SteamId { get; set; }
        public string Nick { get; set; }
        public string Reason { get; set; }
        public string By { get; set; }
        public DateTime Created { get; set; }
        /// <summary>UTC; null = permanent.</summary>
        public DateTime? Until { get; set; }
    }

    /// <summary>
    /// Anti-cheat's own ban list (BepInEx/config/P90Guard.bans.json). Banned players are kicked as soon as they connect.
    /// Edit the file to unban (it is re-read automatically). SteamID comes from the transport, so it cannot be spoofed.
    /// </summary>
    internal static class Bans
    {
        public static string FilePath { get; set; }
        private static List<BanEntry> _list = new();
        private static DateTime _mtime;
        private static readonly Dictionary<IntPtr, float> Kicked = new();
        /// <summary>The file could not be parsed: never overwrite it (the admin's bans would be lost) until it is fixed.</summary>
        private static bool _broken;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true };

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { _broken = false; _list = new(); Save(); return; }
                _mtime = File.GetLastWriteTimeUtc(FilePath);
                var list = JsonSerializer.Deserialize<List<BanEntry>>(File.ReadAllText(FilePath), Json) ?? new();
                list.RemoveAll(b => b == null || string.IsNullOrWhiteSpace(b.SteamId));
                foreach (var b in list) b.SteamId = b.SteamId.Trim();
                _list = list;
                if (_broken) GuardLog.Info("bans: file is valid again");
                _broken = false;
            }
            catch (Exception e)
            {
                _broken = true;
                GuardLog.Error($"bans: cannot read {FilePath}: {e.Message}. The file will NOT be overwritten until it is fixed; new bans are kept in memory only.");
            }
        }

        private static void Save()
        {
            if (_broken) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_list, Json));
                _mtime = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception e) { GuardLog.Warn($"bans: cannot write {FilePath}: {e.Message}"); }
        }

        public static DateTime Add(ulong steamId, string nick, int minutes, string reason, string by)
        {
            ReloadIfChanged();   // do not overwrite a manual edit made in the last seconds
            var id = steamId.ToString();
            _list.RemoveAll(b => b.SteamId == id);
            var until = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : (DateTime?)null;
            _list.Add(new BanEntry { SteamId = id, Nick = nick, Reason = reason, By = by, Created = DateTime.UtcNow, Until = until });
            Save();
            return until ?? DateTime.MaxValue;
        }

        public static BanEntry Find(ulong steamId)
        {
            if (steamId == 0) return null;
            var id = steamId.ToString();
            var b = _list.FirstOrDefault(x => x.SteamId == id);
            if (b == null) return null;
            if (b.Until.HasValue && b.Until.Value <= DateTime.UtcNow) return null;
            return b;
        }

        /// <summary>Every ~2 s: reload the file if edited, kick banned players.</summary>
        public static void Tick()
        {
            ReloadIfChanged();
            if (!GuardConfig.Current.Enabled || _list.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (var p in GPlayer.All())
            {
                try
                {
                    var b = Find(p.TransportSteamId) ?? Find(p.ReportedSteamId);
                    if (b == null) continue;
                    if (Kicked.TryGetValue(p.Ptr, out var t) && now - t < 5f) continue;
                    Kicked[p.Ptr] = now;
                    var m = GuardConfig.Current.Messages;
                    string text = b.Until.HasValue
                        ? m.Ban.Replace("{reason}", b.Reason ?? "").Replace("{until}", b.Until.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                        : m.BanPermanent.Replace("{reason}", b.Reason ?? "");
                    GuardLog.Info($"banned player {p} tried to join - kicked");
                    p.Kick(text);
                }
                catch { }
            }
        }

        private static void ReloadIfChanged()
        {
            try { if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _mtime) Load(); } catch { }
        }

        /// <summary>Forget kick timestamps of players that left.</summary>
        public static void Cleanup(HashSet<IntPtr> alive)
        {
            foreach (var k in Kicked.Keys.Where(k => !alive.Contains(k)).ToList()) Kicked.Remove(k);
        }

        /// <summary>Accept "SteamId": 7656... as well as "SteamId": "7656..." in a hand-edited file.</summary>
        internal sealed class StringOrNumber : JsonConverter<string>
        {
            public override string Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
                r.TokenType == JsonTokenType.Number ? (r.TryGetUInt64(out var u) ? u.ToString() : r.GetDouble().ToString("0"))
                : r.TokenType == JsonTokenType.Null ? null : r.GetString();
            public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
        }

        public static int Count => _list.Count(b => !b.Until.HasValue || b.Until.Value > DateTime.UtcNow);
    }
}
