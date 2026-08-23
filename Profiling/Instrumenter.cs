using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace ModProfiler.Profiling
{
    internal static class Instrumenter
    {
        private static Harmony _harmony;
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static readonly Dictionary<Assembly, ModStats> ModsByAssembly = new Dictionary<Assembly, ModStats>();

        private static readonly string[] UnityFrameMethods = { "Update", "FixedUpdate", "LateUpdate", "OnGUI" };

        private const BindingFlags DeclaredInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        internal static int InstrumentAll()
        {
            bool wasActive = ProfilerCore.Active;
            ProfilerCore.Active = false;
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony(Plugin.PluginGuid + ".timing");

                RefreshModMap();

                var stats = new Dictionary<MethodBase, MethodStats>(ProfilerCore.StatsByMethod);
                int before = stats.Count;

                if (Plugin.ProfileHarmonyPatches.Value)
                    ScanHarmonyPatches(stats);
                if (Plugin.ProfileMonoBehaviours.Value || Plugin.ProfileCoroutines.Value)
                    ScanTypes(stats);

                ProfilerCore.StatsByMethod = stats;
                ProfilerCore.Instrumented = true;

                int added = stats.Count - before;
                Plugin.Log.LogInfo(
                    $"instrumented {stats.Count} methods across {ProfilerCore.Mods.Count} mods " +
                    $"(+{added} new, {ProfilerCore.PatchFailures} failures)");
                return added;
            }
            finally
            {
                ProfilerCore.Active = wasActive;
                ProfilerCore.SkipNextFlush = true;
            }
        }

        private static void RefreshModMap()
        {
            Assembly self = typeof(Instrumenter).Assembly;

            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                object instance = info.Instance;
                if (instance == null)
                    continue;
                Assembly asm = instance.GetType().Assembly;
                if (asm == self || ModsByAssembly.ContainsKey(asm))
                    continue;
                if (!Plugin.IncludeSptPlugins.Value && IsSptAssembly(asm))
                    continue;
                AddMod(asm, info.Metadata.Name);
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || asm == self || ModsByAssembly.ContainsKey(asm))
                    continue;
                string location = SafeLocation(asm);
                if (string.IsNullOrEmpty(location) ||
                    !location.StartsWith(Paths.PluginPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Plugin.IncludeSptPlugins.Value && IsSptAssembly(asm))
                    continue;
                AddMod(asm, asm.GetName().Name);
            }
        }

        private static void AddMod(Assembly asm, string name)
        {
            var mod = new ModStats(name, asm);
            ModsByAssembly[asm] = mod;
            ProfilerCore.Mods.Add(mod);
        }

        private static string SafeLocation(Assembly asm)
        {
            try { return asm.Location; }
            catch { return null; }
        }

        private static bool IsSptAssembly(Assembly asm)
        {
            string location = SafeLocation(asm);
            return location != null &&
                   location.IndexOf(@"\plugins\spt\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ScanHarmonyPatches(Dictionary<MethodBase, MethodStats> stats)
        {
            List<MethodBase> originals;
            try
            {
                originals = Harmony.GetAllPatchedMethods().ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"could not enumerate patched methods: {e.Message}");
                return;
            }

            foreach (MethodBase original in originals)
            {
                Patches info;
                try { info = Harmony.GetPatchInfo(original); }
                catch { continue; }
                if (info == null)
                    continue;

                CollectPatches(info.Prefixes, "prefix", original, stats);
                CollectPatches(info.Postfixes, "postfix", original, stats);
                CollectPatches(info.Finalizers, "finalizer", original, stats);
            }
        }

        private static void CollectPatches(ReadOnlyCollection<Patch> patches, string kind,
            MethodBase original, Dictionary<MethodBase, MethodStats> stats)
        {
            if (patches == null)
                return;
            foreach (Patch patch in patches)
            {
                MethodInfo method = patch.PatchMethod;
                if (method == null || method.DeclaringType == null)
                    continue;
                if (!ModsByAssembly.TryGetValue(method.DeclaringType.Assembly, out ModStats mod))
                    continue;

                string target = original.DeclaringType != null
                    ? original.DeclaringType.Name + "." + original.Name
                    : original.Name;
                string name = $"{method.DeclaringType.Name}.{method.Name} ({kind} → {target})";
                Instrument(method, name, "Harmony", mod, stats);
            }
        }

        private static void ScanTypes(Dictionary<MethodBase, MethodStats> stats)
        {
            foreach (KeyValuePair<Assembly, ModStats> pair in ModsByAssembly)
            {
                Type[] types;
                try
                {
                    types = pair.Key.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    try { ScanType(type, pair.Value, stats); }
                    catch { /* single broken type must not abort the scan */ }
                }
            }
        }

        private static void ScanType(Type type, ModStats mod, Dictionary<MethodBase, MethodStats> stats)
        {
            if (type.IsInterface || type.ContainsGenericParameters)
                return;

            if (Plugin.ProfileMonoBehaviours.Value && typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                foreach (string name in UnityFrameMethods)
                {
                    MethodInfo method = type.GetMethod(name, DeclaredInstance, null, Type.EmptyTypes, null);
                    if (method != null)
                        Instrument(method, $"{type.Name}.{name}", "Unity", mod, stats);
                }
            }

            if (Plugin.ProfileCoroutines.Value && IsCompilerStateMachine(type))
            {
                MethodInfo method = type.GetMethod("MoveNext", DeclaredInstance, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    string owner = type.DeclaringType != null ? type.DeclaringType.Name + "." : "";
                    Instrument(method, $"{owner}{PrettyStateMachineName(type.Name)} (coroutine/async)",
                        "Coroutine", mod, stats);
                }
            }
        }
        private static bool IsCompilerStateMachine(Type type)
        {
            if (typeof(IAsyncStateMachine).IsAssignableFrom(type))
                return true;
            return typeof(IEnumerator).IsAssignableFrom(type)
                && type.IsDefined(typeof(CompilerGeneratedAttribute), false);
        }
        private static string PrettyStateMachineName(string raw)
        {
            int lt = raw.IndexOf('<');
            int gt = raw.IndexOf('>');
            if (lt >= 0 && gt > lt + 1)
                return raw.Substring(lt + 1, gt - lt - 1);
            return raw;
        }

        private static void Instrument(MethodBase target, string displayName, string category,
            ModStats mod, Dictionary<MethodBase, MethodStats> stats)
        {
            if (stats.ContainsKey(target) || Patched.Contains(target))
                return;
            if (target.IsAbstract || target.IsGenericMethod || target.ContainsGenericParameters)
                return;

            try
            {
                if (target.GetMethodBody() == null)
                    return;
                _harmony.Patch(target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(ProfilerCore), nameof(ProfilerCore.TimerPrefix)))
                    {
                        priority = Priority.First
                    },
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(ProfilerCore), nameof(ProfilerCore.TimerPostfix)))
                    {
                        priority = Priority.Last
                    });
            }
            catch (Exception e)
            {
                ProfilerCore.PatchFailures++;
                Exception cause = e;
                while (cause.InnerException != null)
                    cause = cause.InnerException;
                string reason = (cause.Message ?? cause.GetType().Name).Split('\n')[0].Trim();
                if (reason.Length > 200)
                    reason = reason.Substring(0, 200) + "...";
                if (ProfilerCore.PatchFailureDetails.Count < ProfilerCore.MaxPatchFailureDetails)
                    ProfilerCore.PatchFailureDetails.Add(new ProfilerCore.PatchFailure
                    {
                        Mod = mod.Name,
                        Method = displayName,
                        Reason = reason,
                    });
                Plugin.Log.LogDebug($"could not instrument {target.DeclaringType?.FullName}.{target.Name}: {reason}");
                return;
            }

            Patched.Add(target);
            var methodStats = new MethodStats(target, displayName, category, mod);
            mod.Methods.Add(methodStats);
            stats[target] = methodStats;
        }
    }
}
