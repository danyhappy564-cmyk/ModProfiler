using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;

namespace ModProfiler.UI
{
    internal static class WebOverlayGate
    {
        public const string LibraryGuid = "com.anvil.weboverlay";
        public static readonly Version MinimumVersion = new Version(1, 8, 0);

        private static bool? loaded;
        private static Version foundVersion;
        private static object overlay;
        private static volatile bool overlayFailed;

        public static bool IsLoaded
        {
            get
            {
                if (loaded == null)
                {
                    loaded = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(LibraryGuid, out BepInEx.PluginInfo info);
                    if (loaded.Value)
                        foundVersion = info.Metadata.Version;
                }
                return loaded.Value;
            }
        }
        public static Version FoundVersion => IsLoaded ? foundVersion : null;
        public static bool IsUsable => IsLoaded && foundVersion != null && foundVersion >= MinimumVersion;
        public static bool HasFailed => overlayFailed;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Toggle(string html, int width, int height, KeyboardShortcut toggleKey,
            Action<string, string> onChannel, Func<string, string, string> onRequest,
            Action onFailed, Action<string> logWarning)
        {
            if (overlayFailed)
                return false;

            var handle = overlay as WebOverlay.IWebOverlay;
            if (handle == null)
            {
                handle = WebOverlay.WebOverlays.Create("ModProfiler", new WebOverlay.OverlayOptions
                {
                    Width = width,
                    Height = height,
                    CloseKeys = WebOverlay.WebOverlayPlugin.CloseKeysFor(toggleKey),
                    Dispatch = WebOverlay.EventDispatch.Manual,
                    FreeCursorWhileShown = true,
                });
                if (handle == null)
                {
                    logWarning("overlays are unavailable (is the WebView2 runtime installed?); using the IMGUI overlay.");
                    overlayFailed = true;
                    return false;
                }

                overlay = handle;
                object created = handle;
                handle.ChannelMessage += onChannel;
                handle.OnRequest("methods", payload => onRequest("methods", payload));
                handle.OnRequest("action", payload => onRequest("action", payload));
                handle.OnRequest("failures", payload => onRequest("failures", payload));
                handle.Failed += () =>
                {
                    overlayFailed = true;
                    var dead = (WebOverlay.IWebOverlay)created;
                    string why;
                    switch (dead.Failure)
                    {
                        case WebOverlay.OverlayFailure.RuntimeMissing:
                            why = "no WebView2 runtime is installed - install it from Microsoft and the web window will work";
                            break;
                        case WebOverlay.OverlayFailure.LibraryIncomplete:
                            why = "Anvil-WebOverlay is incomplete (WebView2Loader.dll missing) - reinstall the library";
                            break;
                        case WebOverlay.OverlayFailure.EnvironmentFailed:
                            why = "the shared browser could not start this session";
                            break;
                        case WebOverlay.OverlayFailure.RendererCrashed:
                            why = "the browser process died";
                            break;
                        default:
                            why = dead.Failure.ToString();
                            break;
                    }
                    logWarning("the profiler panel failed: " + why + " (" + dead.FailureMessage
                        + "). The IMGUI overlay takes over on the next press.");
                    dead.Dispose();
                    if (ReferenceEquals(overlay, created))
                        overlay = null;
                    onFailed();
                };

                handle.LoadHtml(html);
                return true;
            }

            handle.Toggle();
            return true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Pump()
        {
            (overlay as WebOverlay.IWebOverlay)?.PumpEvents();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Post(string channel, string payload)
        {
            (overlay as WebOverlay.IWebOverlay)?.Post(channel, payload);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsVisible()
        {
            var handle = overlay as WebOverlay.IWebOverlay;
            return handle != null && handle.IsVisible;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Shutdown()
        {
            (overlay as WebOverlay.IWebOverlay)?.Dispose();
            overlay = null;
        }
    }
}
