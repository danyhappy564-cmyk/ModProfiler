using System;
using System.Reflection;
using System.Threading;

namespace ModProfiler.Profiling
{
    internal sealed class MethodStats
    {
        internal readonly MethodBase Method;
        internal readonly string DisplayName;
        internal readonly string Category;
        internal readonly ModStats Mod;
        private long _pendingTicks;
        private long _pendingCalls;

        internal readonly long[] TicksWindow = new long[ProfilerCore.WindowSize];
        internal readonly long[] CallsWindow = new long[ProfilerCore.WindowSize];

        internal long TotalTicks;
        internal long TotalCalls;

        internal MethodStats(MethodBase method, string displayName, string category, ModStats mod)
        {
            Method = method;
            DisplayName = displayName;
            Category = category;
            Mod = mod;
        }

        internal void Record(long ticks)
        {
            Interlocked.Add(ref _pendingTicks, ticks);
            Interlocked.Increment(ref _pendingCalls);
        }

        internal void FlushFrame(int slot, out long ticks, out long calls)
        {
            ticks = Interlocked.Exchange(ref _pendingTicks, 0L);
            calls = Interlocked.Exchange(ref _pendingCalls, 0L);
            TicksWindow[slot] = ticks;
            CallsWindow[slot] = calls;
            TotalTicks += ticks;
            TotalCalls += calls;
        }

        internal void ResetWindow()
        {
            Interlocked.Exchange(ref _pendingTicks, 0L);
            Interlocked.Exchange(ref _pendingCalls, 0L);
            Array.Clear(TicksWindow, 0, TicksWindow.Length);
            Array.Clear(CallsWindow, 0, CallsWindow.Length);
        }

        internal void ResetTotals()
        {
            TotalTicks = 0L;
            TotalCalls = 0L;
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
