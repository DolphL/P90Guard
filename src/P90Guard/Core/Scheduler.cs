using System;
using System.Collections.Generic;
using UnityEngine;

namespace P90Guard.Core
{
    /// <summary>Delayed actions on the main thread.</summary>
    internal static class Scheduler
    {
        private static readonly List<(float At, Action Do)> Pending = new();

        public static void After(float seconds, Action a) => Pending.Add((Time.realtimeSinceStartup + Math.Max(0f, seconds), a));

        public static void Tick()
        {
            if (Pending.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (Pending[i].At > now) continue;
                var a = Pending[i].Do;
                Pending.RemoveAt(i);
                try { a(); } catch (Exception e) { GuardLog.Warn($"scheduled action: {e.Message}"); }
            }
        }
    }

    /// <summary>Unity component that drives the plugin every frame.</summary>
    public sealed class GuardTicker : MonoBehaviour
    {
        public GuardTicker(IntPtr ptr) : base(ptr) { }

        private float _nextSlow;

        private void Update()
        {
            try { Scheduler.Tick(); } catch { }
            try { SpawnTracker.Tick(); } catch { }
            float now = Time.realtimeSinceStartup;
            if (now < _nextSlow) return;
            _nextSlow = now + 2f;
            try { GuardConfig.ReloadIfChanged(); } catch { }
            try { Bans.Tick(); } catch { }
        }
    }
}
