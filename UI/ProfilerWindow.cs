using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using ModProfiler.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ModProfiler.UI
{
    internal sealed class ProfilerWindow : MonoBehaviour
    {
        private enum SortColumn
        {
            Name,
            Avg,
            Max,
            Calls,
            Total
        }

        private sealed class MethodRow
        {
            internal string Name;
            internal string Category;
            internal double AvgMs;
            internal double MaxMs;
            internal double Calls;
            internal double TotalMs;
        }

        private sealed class ModRow
        {
            internal ModStats Mod;
            internal double AvgMs;
            internal double MaxMs;
            internal double Calls;
            internal double Percent;
            internal double TotalMs;
            internal List<MethodRow> Methods = new List<MethodRow>();
        }

        private const int WindowId = 0x4D505246;

        private const float NameWidth = 340f;
        private const float NumWidth = 72f;
        private const float TotalWidth = 92f;

        private bool _visible;
        private bool _mouseMode;
        private EventSystem _suppressedEventSystem;
        private Rect _rect = new Rect(80f, 80f, 880f, 560f);
        private Vector2 _scroll;
        private readonly HashSet<ModStats> _expanded = new HashSet<ModStats>();
        private SortColumn _sort = SortColumn.Avg;
        private bool _sortDesc = true;
        private readonly List<ModRow> _rows = new List<ModRow>();
        private float _nextRefresh;
        private string _headerText = "";
        private string _statusText = "";

        private GUIStyle _rowStyle;
        private GUIStyle _numStyle;
        private GUIStyle _methodStyle;
        private GUIStyle _methodNumStyle;
        private GUIStyle _headStyle;

        private void Start()
        {
            Plugin.Log.LogInfo($"profiler overlay component running (hotkey: {Plugin.ToggleKey.Value})");
        }

        private void Update()
        {
            if (!WebPanelController.OwnsHotkey && Hotkey.IsPressed(Plugin.ToggleKey.Value))
                Toggle();

            if (ProfilerCore.Active)
                ProfilerCore.FlushFrame(Time.unscaledDeltaTime * 1000f);

            if (!_visible)
                return;

            if (_mouseMode)
                UnlockCursor();
            UpdateEventSystemSuppression();

            if (Time.unscaledTime >= _nextRefresh)
            {
                RefreshRows();
                _nextRefresh = Time.unscaledTime + Plugin.RefreshInterval.Value;
            }
        }

        private void LateUpdate()
        {
            if (_visible && _mouseMode)
                UnlockCursor();
        }

        private void OnDestroy()
        {
            SetMouseMode(false);
            RestoreEventSystem();
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetMouseMode(bool on)
        {
            _mouseMode = on;
            GameInputBlocker.Blocked = on;
        }
        private void UpdateEventSystemSuppression()
        {
            bool suppress = _mouseMode;
            if (!suppress && Cursor.visible && Cursor.lockState != CursorLockMode.Locked)
            {
                Vector3 mouse = Input.mousePosition;
                suppress = _rect.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
            }

            if (suppress)
            {
                if (_suppressedEventSystem != null)
                    return;
                EventSystem current = EventSystem.current;
                if (current != null && current.enabled)
                {
                    current.enabled = false;
                    _suppressedEventSystem = current;
                }
            }
            else
            {
                RestoreEventSystem();
            }
        }

        private void RestoreEventSystem()
        {
            if (_suppressedEventSystem == null)
                return;
            if (_suppressedEventSystem)
                _suppressedEventSystem.enabled = true;
            _suppressedEventSystem = null;
        }

        private void Toggle()
        {
            if (_visible)
            {
                _visible = false;
                ProfilerCore.Active = false;
                SetMouseMode(false);
                RestoreEventSystem();
                return;
            }

            if (!ProfilerCore.Instrumented)
            {
                Plugin.Log.LogInfo("first activation - instrumenting all mod code, this can take a moment...");
                Instrumenter.InstrumentAll();
            }

            ProfilerCore.ResetWindows();
            ProfilerCore.Active = true;
            _visible = true;
            _nextRefresh = 0f;
        }

        private void RefreshRows()
        {
            int frames = Math.Max(1, ProfilerCore.FramesObserved);
            double frameAvgMs = ProfilerCore.AverageFrameMs();

            _rows.Clear();
            double profiledAvgMs = 0.0;
            foreach (ModStats mod in ProfilerCore.Mods)
            {
                if (mod.Methods.Count == 0)
                    continue;

                var row = new ModRow
                {
                    Mod = mod,
                    AvgMs = mod.WindowAvgMs(frames),
                    MaxMs = mod.WindowMaxMs(frames),
                    Calls = mod.WindowCallsPerFrame(frames),
                    TotalMs = mod.TotalTicks * ProfilerCore.MsPerTick
                };
                row.Percent = frameAvgMs > 0.0 ? row.AvgMs / frameAvgMs * 100.0 : 0.0;
                profiledAvgMs += row.AvgMs;

                if (_expanded.Contains(mod))
                {
                    var methods = new List<MethodRow>(mod.Methods.Count);
                    foreach (MethodStats m in mod.Methods)
                    {
                        methods.Add(new MethodRow
                        {
                            Name = m.DisplayName,
                            Category = m.Category,
                            AvgMs = m.WindowAvgMs(frames),
                            MaxMs = m.WindowMaxMs(frames),
                            Calls = m.WindowCallsPerFrame(frames),
                            TotalMs = m.TotalTicks * ProfilerCore.MsPerTick
                        });
                    }
                    methods.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
                    int n = Math.Min(Plugin.TopMethodsPerMod.Value, methods.Count);
                    row.Methods = methods.GetRange(0, n);
                }

                _rows.Add(row);
            }

            SortRows();

            double fps = frameAvgMs > 0.0 ? 1000.0 / frameAvgMs : 0.0;
            _headerText =
                $"Frame Ø {frameAvgMs:F1} ms ({fps:F0} FPS)  |  profiled mods Σ {profiledAvgMs:F2} ms/frame  |  " +
                $"{ProfilerCore.StatsByMethod.Count} methods, window {frames} frames" +
                (ProfilerCore.PatchFailures > 0 ? $"  |  {ProfilerCore.PatchFailures} patch failures" : "");
        }

        private void SortRows()
        {
            Comparison<ModRow> cmp;
            switch (_sort)
            {
                case SortColumn.Name:
                    cmp = (a, b) => string.Compare(a.Mod.Name, b.Mod.Name, StringComparison.OrdinalIgnoreCase);
                    break;
                case SortColumn.Max:
                    cmp = (a, b) => a.MaxMs.CompareTo(b.MaxMs);
                    break;
                case SortColumn.Calls:
                    cmp = (a, b) => a.Calls.CompareTo(b.Calls);
                    break;
                case SortColumn.Total:
                    cmp = (a, b) => a.TotalMs.CompareTo(b.TotalMs);
                    break;
                default:
                    cmp = (a, b) => a.AvgMs.CompareTo(b.AvgMs);
                    break;
            }
            _rows.Sort(cmp);
            if (_sortDesc)
                _rows.Reverse();
        }

        private void OnGUI()
        {
            if (!_visible)
                return;
            EnsureStyles();
            _rect = GUI.Window(WindowId, _rect, DrawWindow, "ModProfiler — CPU time per mod");
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null)
                return;
            _rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _numStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
            _methodStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            _methodStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);
            _methodNumStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
            _methodNumStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);
            _headStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(ProfilerCore.Active ? "Pause" : "Resume", GUILayout.Width(70f)))
                ProfilerCore.Active = !ProfilerCore.Active;
            if (GUILayout.Button("Rescan", GUILayout.Width(70f)))
            {
                Instrumenter.InstrumentAll();
                _nextRefresh = 0f;
            }
            if (GUILayout.Button("Reset", GUILayout.Width(70f)))
            {
                ProfilerCore.ResetAll();
                _nextRefresh = 0f;
            }
            if (GUILayout.Button("CSV", GUILayout.Width(60f)))
                ExportCsv();
            string mouseLabel = _mouseMode ? "Mouse ✓" : "Mouse";
            if (GUILayout.Button(mouseLabel, GUILayout.Width(80f)))
                SetMouseMode(!_mouseMode);
            GUILayout.Space(12f);
            GUILayout.Label(_headerText, _headStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(26f)))
                Toggle();
            GUILayout.EndHorizontal();

            DrawHeaderRow();

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _rows.Count; i++)
                DrawModRow(_rows[i]);
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_statusText))
                GUILayout.Label(_statusText, _headStyle);

            GUI.DragWindow(new Rect(0f, 0f, 100000f, 20f));
        }

        private void DrawHeaderRow()
        {
            GUILayout.BeginHorizontal();
            SortButton("Mod", SortColumn.Name, NameWidth);
            SortButton("Ø ms", SortColumn.Avg, NumWidth);
            SortButton("Max ms", SortColumn.Max, NumWidth);
            SortButton("Calls/F", SortColumn.Calls, NumWidth);
            SortButton("% Frame", SortColumn.Avg, NumWidth);
            SortButton("Σ ms", SortColumn.Total, TotalWidth);
            GUILayout.EndHorizontal();
        }

        private void SortButton(string label, SortColumn column, float width)
        {
            string text = _sort == column ? label + (_sortDesc ? " ▼" : " ▲") : label;
            if (GUILayout.Button(text, GUILayout.Width(width)))
            {
                if (_sort == column)
                {
                    _sortDesc = !_sortDesc;
                }
                else
                {
                    _sort = column;
                    _sortDesc = column != SortColumn.Name;
                }
                SortRows();
            }
        }

        private void DrawModRow(ModRow row)
        {
            bool expanded = _expanded.Contains(row.Mod);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button((expanded ? "▼ " : "▶ ") + row.Mod.Name, _rowStyle, GUILayout.Width(NameWidth)))
            {
                if (expanded)
                    _expanded.Remove(row.Mod);
                else
                    _expanded.Add(row.Mod);
                _nextRefresh = 0f;
            }
            GUILayout.Label(row.AvgMs.ToString("F2"), _numStyle, GUILayout.Width(NumWidth));
            GUILayout.Label(row.MaxMs.ToString("F2"), _numStyle, GUILayout.Width(NumWidth));
            GUILayout.Label(row.Calls.ToString("F1"), _numStyle, GUILayout.Width(NumWidth));
            GUILayout.Label(row.Percent.ToString("F1") + " %", _numStyle, GUILayout.Width(NumWidth));
            GUILayout.Label(row.TotalMs.ToString("F0"), _numStyle, GUILayout.Width(TotalWidth));
            GUILayout.EndHorizontal();

            if (!expanded)
                return;

            for (int i = 0; i < row.Methods.Count; i++)
            {
                MethodRow m = row.Methods[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label("      [" + m.Category[0] + "] " + m.Name, _methodStyle, GUILayout.Width(NameWidth));
                GUILayout.Label(m.AvgMs.ToString("F3"), _methodNumStyle, GUILayout.Width(NumWidth));
                GUILayout.Label(m.MaxMs.ToString("F3"), _methodNumStyle, GUILayout.Width(NumWidth));
                GUILayout.Label(m.Calls.ToString("F1"), _methodNumStyle, GUILayout.Width(NumWidth));
                GUILayout.Label("", _methodNumStyle, GUILayout.Width(NumWidth));
                GUILayout.Label(m.TotalMs.ToString("F0"), _methodNumStyle, GUILayout.Width(TotalWidth));
                GUILayout.EndHorizontal();
            }
        }

        private void ExportCsv()
        {
            try
            {
                _statusText = "CSV written: " + CsvExport.Write();
                Plugin.Log.LogInfo(_statusText);
            }
            catch (Exception e)
            {
                _statusText = "CSV export failed: " + e.Message;
                Plugin.Log.LogError(_statusText);
            }
        }
    }
}
