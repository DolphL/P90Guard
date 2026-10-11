using System;
using System.IO;
using BepInEx.Logging;

namespace P90Guard.Core
{
    internal static class GuardLog
    {
        internal static ManualLogSource Source;
        internal static string LogDir;
        private static readonly object FileLock = new();

        public static void Info(string m) => Source?.LogInfo(m);
        public static void Warn(string m) => Source?.LogWarning(m);
        public static void Error(string m) => Source?.LogError(m);
        public static void Debug(string m) => Source?.LogDebug(m);

        /// <summary>Detection line: red in the console + daily file.</summary>
        public static void Detection(string line)
        {
            Source?.LogError(line);
            ToFile(line);
        }

        public static void ToFile(string line)
        {
            if (!GuardConfig.Current.LogToFile || string.IsNullOrEmpty(LogDir)) return;
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(LogDir);
                    File.AppendAllText(Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                        $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
