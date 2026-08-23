# Changelog

## [Unreleased]

## [1.0.0]

### Forge version notes

- Shows live how much CPU time each installed client mod costs per frame, so you
  can find what is slowing your game down without disabling mods one by one.
- Press **F11** to open it. With the optional
  [Anvil-WebOverlay](https://sp-mod.com/mod/2879/weboverlay) library (1.8.0 or
  newer) it opens as its own window over the game, with a sortable table, a
  filter field, per-mod trend graphs, a frame-time history, the garbage-collector
  rate and a spike log that names the top contributors of a stuttering frame.
  Without the library a built-in overlay shows the core table instead.
- Read the "Limits" section of the README before drawing conclusions from the
  numbers: they rank mods usefully, they are not a frame-exact truth.

### Added

- Per-mod CPU profiling on the Dubs Performance Analyzer principle: the Harmony
  patch methods other mods applied to the game are themselves Harmony-patched
  with a stopwatch, as are the Unity frame methods and compiler-generated
  coroutine/async state machines of every mod assembly. Each sample is
  attributed to the assembly that owns the code.
- Core table in both interfaces: average and peak milliseconds per frame, calls
  per frame, share of the frame and the accumulated total, with sorting,
  expandable per-method detail, pause/resume, a rescan for patches created later
  (raid start), reset and CSV export.
- Web window through the optional Anvil-WebOverlay library: draggable with
  remembered position and size, filter field, per-mod trend sparklines, frame
  history, GC rate with markers on the frame graph, a spike log for frames over
  `UI/SpikeThresholdMs`, a clickable patch-failure counter listing the methods
  Harmony could not wrap and why, and tooltips on every column, metric and
  button. It also adds a `µs/Call` column that greys out rows whose value
  approaches the ~1 µs measurement overhead, because such a row is mostly
  profiler cost and its ranking is not reliable.
- Built-in IMGUI overlay as the fallback, with a mouse mode that frees the
  cursor and mutes the game's controls. It takes over when the library is
  missing, too old, or the window fails at runtime, with the cause in the log.
