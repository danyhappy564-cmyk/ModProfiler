using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace ModProfiler.Profiling
{
    internal static class ProfilerCore
    {
        internal const int WindowSize = 150;

        internal static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        internal static volatile bool Active;
        internal static bool Instrumented;
        internal static int PatchFailures;

        internal sealed class PatchFailure
        {
            internal string Mod;
            internal string Method;
            internal string Reason;
        }
        internal static readonly List<PatchFailure> PatchFailureDetails = new List<PatchFailure>();
        internal const int MaxPatchFailureDetails = 200;
        internal static volatile Dictionary<MethodBase, MethodStats> StatsByMethod =
            new Dictionary<MethodBase, MethodStats>();

        internal static readonly List<ModStats> Mods = new List<ModStats>();

        internal static readonly float[] FrameMsWindow = new float[WindowSize];
        internal static int Cursor;
        internal static int FramesObserved;
        internal static void TimerPrefix(out long __state)
        {
            __state = Active ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void TimerPostfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0L)
                return;
            long elapsed = Stopwatch.GetTimestamp() - __state;
            if (StatsByMethod.TryGetValue(__originalMethod, out MethodStats stats))
                stats.Record(elapsed);
        }
        internal static bool SkipNextFlush;

        internal static void FlushFrame(float frameMs)
        {
            if (SkipNextFlush)
            {
                SkipNextFlush = false;
                return;
            }

            int slot = Cursor;
            FrameMsWindow[slot] = frameMs;
            for (int i = 0; i < Mods.Count; i++)
                Mods[i].FlushFrame(slot);
            Cursor = (slot + 1) % WindowSize;
            if (FramesObserved < WindowSize)
                FramesObserved++;
            float threshold = Plugin.SpikeThresholdMs.Value;
            if (threshold > 0f && FramesObserved >= 30
                && frameMs >= threshold && frameMs >= AverageFrameMs() * 1.5f)
                CaptureSpike(slot, frameMs);
        }

        internal const int MaxSpikes = 20;

        internal sealed class SpikeEntry
        {
            internal string Time;
            internal float FrameMs;
            internal List<KeyValuePair<string, double>> Top;
        }
        internal static readonly List<SpikeEntry> Spikes = new List<SpikeEntry>();

        private static void CaptureSpike(int slot, float frameMs)
        {
            var contributors = new List<KeyValuePair<string, double>>();
            for (int i = 0; i < Mods.Count; i++)
            {
                double ms = Mods[i].TicksWindow[slot] * MsPerTick;
                if (ms >= 0.05)
                    contributors.Add(new KeyValuePair<string, double>(Mods[i].Name, ms));
            }
            contributors.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (contributors.Count > 3)
                contributors.RemoveRange(3, contributors.Count - 3);

            Spikes.Add(new SpikeEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                FrameMs = frameMs,
                Top = contributors,
            });
            if (Spikes.Count > MaxSpikes)
                Spikes.RemoveAt(0);
        }

        internal static void ResetWindows()
        {
            Array.Clear(FrameMsWindow, 0, WindowSize);
            for (int i = 0; i < Mods.Count; i++)
                Mods[i].ResetWindow();
            Cursor = 0;
            FramesObserved = 0;
        }

        internal static void ResetAll()
        {
            ResetWindows();
            Spikes.Clear();
            for (int i = 0; i < Mods.Count; i++)
                Mods[i].ResetTotals();
        }

        internal static float AverageFrameMs()
        {
            int n = FramesObserved;
            if (n == 0)
                return 0f;
            float sum = 0f;
            for (int i = 0; i < n; i++)
                sum += FrameMsWindow[i];
            return sum / n;
        }

        internal static long Sum(long[] window, int frames)
        {
            long sum = 0L;
            for (int i = 0; i < frames; i++)
                sum += window[i];
            return sum;
        }

        internal static long Max(long[] window, int frames)
        {
            long max = 0L;
            for (int i = 0; i < frames; i++)
                if (window[i] > max)
                    max = window[i];
            return max;
        }
    }
}
