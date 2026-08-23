using BepInEx.Configuration;
using UnityEngine;

namespace ModProfiler.UI
{
    internal static class Hotkey
    {
        internal static bool IsPressed(KeyboardShortcut shortcut)
        {
            if (!Input.GetKeyDown(shortcut.MainKey))
                return false;
            foreach (KeyCode modifier in shortcut.Modifiers)
                if (!Input.GetKey(modifier))
                    return false;
            return true;
        }
    }
}
