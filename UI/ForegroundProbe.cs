using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ModProfiler.UI
{
    internal static class ForegroundProbe
    {
        private static bool resolved;
        private static IntPtr gameWindow;
        private static uint processId;

        internal static bool OverlayInFront()
        {
            if (!resolved)
                Resolve();

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;
            if (gameWindow == IntPtr.Zero)
                return !Application.isFocused;
            if (foreground == gameWindow)
                return false;
            GetWindowThreadProcessId(foreground, out uint foregroundProcess);
            return foregroundProcess == processId;
        }

        private static void Resolve()
        {
            resolved = true;
            processId = GetCurrentProcessId();
            EnumThreadWindows(GetCurrentThreadId(), (hwnd, param) =>
            {
                var className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() != "UnityWndClass")
                    return true;
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero)
                    return true;
                gameWindow = hwnd;
                return false;
            }, IntPtr.Zero);
        }

        private delegate bool EnumThreadWindowsProc(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsProc callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
    }
}
