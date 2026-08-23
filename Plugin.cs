using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ModProfiler.UI;
using UnityEngine;

namespace ModProfiler
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(WebOverlayGate.LibraryGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.maschine.ModProfiler";
        public const string PluginName = "maschine-ModProfiler";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> ProfileHarmonyPatches;
        internal static ConfigEntry<bool> ProfileMonoBehaviours;
        internal static ConfigEntry<bool> ProfileCoroutines;
        internal static ConfigEntry<bool> IncludeSptPlugins;
        internal static ConfigEntry<int> TopMethodsPerMod;
        internal static ConfigEntry<float> RefreshInterval;
        internal static ConfigEntry<bool> PreferWebUi;
        internal static ConfigEntry<float> SpikeThresholdMs;

        private void Awake()
        {
            Log = Logger;
            ToggleKey = Config.Bind("General", "ToggleOverlay", new KeyboardShortcut(KeyCode.F11),
                Tagged("Profiler Hotkey", 10,
                    "Opens/closes the profiler overlay. The first activation instruments all mod code, " +
                    "which can freeze the game for a few seconds."));

            PreferWebUi = Config.Bind("UI", "PreferWebOverlay", true,
                Tagged("Use Web Overlay Window", 50,
                    "Show the profiler in an Anvil-WebOverlay window when that library is installed " +
                    "(movable, sortable, filterable, with trend graphs). The built-in overlay " +
                    "remains the fallback and can be forced by turning this off. Read once at startup."));
            RefreshInterval = Config.Bind("UI", "RefreshInterval", 0.5f,
                Tagged("Refresh Interval (s)", 40,
                    "Seconds between overlay table refreshes.",
                    new AcceptableValueRange<float>(0.1f, 5f)));
            TopMethodsPerMod = Config.Bind("UI", "TopMethodsPerMod", 15,
                Tagged("Methods Per Expanded Mod", 30,
                    "How many of the most expensive methods to list when a mod row is expanded.",
                    new AcceptableValueRange<int>(1, 100)));
            SpikeThresholdMs = Config.Bind("UI", "SpikeThresholdMs", 30f,
                Tagged("Spike Threshold (ms)", 20,
                    "Frames slower than this (and at least 1.5x the current average) are " +
                    "recorded in the web window's spike log together with their top contributors. 0 disables.",
                    new AcceptableValueRange<float>(0f, 1000f)));

            ProfileHarmonyPatches = Config.Bind("Profiling", "HarmonyPatches", true,
                Tagged("Measure Harmony Patches", 40,
                    "Measure every Harmony prefix/postfix/finalizer that other mods have applied."));
            ProfileMonoBehaviours = Config.Bind("Profiling", "MonoBehaviours", true,
                Tagged("Measure Unity Frame Methods", 30,
                    "Measure Update/FixedUpdate/LateUpdate/OnGUI of MonoBehaviours defined in mod assemblies."));
            ProfileCoroutines = Config.Bind("Profiling", "CoroutinesAndAsync", true,
                Tagged("Measure Coroutines And Async", 20,
                    "Measure coroutine/async state machine steps (MoveNext) defined in mod assemblies."));
            IncludeSptPlugins = Config.Bind("Profiling", "IncludeSptCorePlugins", true,
                Tagged("Include SPT Core Plugins", 10,
                    "Also profile the SPT core plugins (BepInEx/plugins/spt). Turning this off only affects " +
                    "methods that have not been instrumented yet; a restart removes existing instrumentation."));

            GameInputBlocker.Apply();
            gameObject.AddComponent<ProfilerWindow>();
            gameObject.AddComponent<WebPanelController>();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Press {ToggleKey.Value} to open the profiler overlay.");
        }

        private static ConfigDescription Tagged(string displayName, int order, string description)
        {
            return Tagged(displayName, order, description, null);
        }

        private static ConfigDescription Tagged(string displayName, int order, string description,
            AcceptableValueBase acceptableValues)
        {
            return new ConfigDescription(
                description,
                acceptableValues,
                new ConfigurationManagerAttributes
                {
                    DispName = displayName,
                    Order = order,
                    ShowRangeAsPercent = false,
                });
        }
    }
}
