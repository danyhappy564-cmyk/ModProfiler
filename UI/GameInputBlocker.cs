using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ModProfiler.UI
{
    internal static class GameInputBlocker
    {
        internal static volatile bool Blocked;

        private static bool _attempted;

        internal static void Apply()
        {
            if (_attempted)
                return;
            _attempted = true;
            try
            {
                Type type = AccessTools.TypeByName("EFT.InputSystem.InputManager");
                if (type == null)
                    throw new TypeLoadException("EFT.InputSystem.InputManager not found");
                MethodInfo target = FindDeliverMethod(type);
                var harmony = new Harmony(Plugin.PluginGuid + ".inputblock");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(GameInputBlocker), nameof(Prefix))));
                Plugin.Log.LogDebug($"game input blocker installed on InputManager.{target.Name}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "could not install the game input blocker; mouse mode will unlock the cursor " +
                    "but not suppress game input: " + e.Message);
            }
        }
        private static MethodInfo FindDeliverMethod(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.ReturnType != typeof(void))
                    continue;
                ParameterInfo[] p = method.GetParameters();
                if (p.Length == 2 &&
                    p[0].ParameterType.IsGenericType &&
                    p[0].ParameterType.GetGenericTypeDefinition() == typeof(List<>) &&
                    p[0].ParameterType.GetGenericArguments()[0].Name == "ECommand" &&
                    p[1].ParameterType == typeof(float[]))
                {
                    return method;
                }
            }
            throw new MissingMethodException(
                "no delivery method with signature (List<ECommand>, float[]) on EFT.InputSystem.InputManager");
        }
        private static void Prefix(IList __0, float[] __1)
        {
            if (!Blocked)
                return;
            __0?.Clear();
            if (__1 != null)
                Array.Clear(__1, 0, __1.Length);
        }
    }
}
