using System;
using System.Collections.Generic;
using System.Reflection;

namespace ModProfiler.Profiling
{
    internal sealed class ModStats
    {
        internal readonly string Name;
        internal readonly Assembly Assembly;
        internal readonly List<MethodStats> Methods = new List<MethodStats>();

        internal readonly long[] TicksWindow = new long[ProfilerCore.WindowSize];
        internal readonly long[] CallsWindow = new long[ProfilerCore.WindowSize];

        internal long TotalTicks;
        internal long TotalCalls;

        internal ModStats(string name, Assembly assembly)
        {
            Name = name;
            Assembly = assembly;
        }

        internal void FlushFrame(int slot)
        {
            long ticks = 0L;
            long calls = 0L;
            for (int i = 0; i < Methods.Count; i++)
            {
                Methods[i].FlushFrame(slot, out long t, out long c);
                ticks += t;
                calls += c;
            }
            TicksWindow[slot] = ticks;
            CallsWindow[slot] = calls;
            TotalTicks += ticks;
            TotalCalls += calls;
        }

        internal void ResetWindow()
        {
            Array.Clear(TicksWindow, 0, TicksWindow.Length);
            Array.Clear(CallsWindow, 0, CallsWindow.Length);
            for (int i = 0; i < Methods.Count; i++)
                Methods[i].ResetWindow();
        }

        internal void ResetTotals()
        {
            TotalTicks = 0L;
            TotalCalls = 0L;
            for (int i = 0; i < Methods.Count; i++)
                Methods[i].ResetTotals();
        }

        internal double WindowAvgMs(int frames)
        {
            return ProfilerCore.Sum(TicksWindow, frames) * ProfilerCore.MsPerTick / frames;
        }

        internal double WindowMaxMs(int frames)
        {
            return ProfilerCore.Max(TicksWindow, frames) * ProfilerCore.MsPerTick;
        }

        internal double WindowCallsPerFrame(int frames)
        {
            return (double)ProfilerCore.Sum(CallsWindow, frames) / frames;
        }
    }
}
