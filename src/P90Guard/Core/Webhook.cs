using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace P90Guard.Core
{
    /// <summary>Optional notifications to a Discord webhook or any JSON endpoint. Rate limited, never blocks the game.</summary>
    internal static class Webhook
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Queue<DateTime> Sent = new();
        private static DateTime _lastError = DateTime.MinValue;

        public static void Send(string title, string text, int color)
        {
            var cfg = GuardConfig.Current.Webhook;
            if (string.IsNullOrWhiteSpace(cfg?.Url)) return;
            var now = DateTime.UtcNow;
            lock (Sent)
            {
                while (Sent.Count > 0 && (now - Sent.Peek()).TotalSeconds > 60) Sent.Dequeue();
                if (Sent.Count >= Math.Max(1, cfg.MaxPerMinute)) return;
                Sent.Enqueue(now);
            }

            string body;
            if (string.Equals(cfg.Format, "json", StringComparison.OrdinalIgnoreCase))
            {
                body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["source"] = "P90Guard", ["server"] = cfg.ServerName, ["title"] = title, ["text"] = text, ["time"] = now.ToString("o"),
                });
            }
            else
            {
                body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["content"] = string.IsNullOrWhiteSpace(cfg.DiscordMention) ? null : cfg.DiscordMention,
                    ["embeds"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["title"] = Trim(title, 250), ["description"] = Trim(text, 3900), ["color"] = color,
                            ["footer"] = new Dictionary<string, object> { ["text"] = "P90Guard · " + cfg.ServerName },
                            ["timestamp"] = now.ToString("o"),
                        },
                    },
                });
            }

            var url = cfg.Url;
            Task.Run(async () =>
            {
                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var resp = await Http.PostAsync(url, content).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) Fail($"HTTP {(int)resp.StatusCode}");
                }
                catch (Exception e) { Fail(e.Message); }
            });
        }

        private static void Fail(string why)
        {
            if ((DateTime.UtcNow - _lastError).TotalMinutes < 5) return;
            _lastError = DateTime.UtcNow;
            GuardLog.Warn($"webhook: {why}");
        }

        private static string Trim(string s, int max) => s == null ? null : s.Length <= max ? s : s.Substring(0, max);
    }
}
