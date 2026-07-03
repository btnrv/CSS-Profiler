<h1 align="center"><code>CSS-Profiler</code></h1>

<p align="center">
  A profiler for CounterStrikeSharp servers. Record where your plugins spend time and read it as a <a href="https://www.speedscope.app">speedscope</a> flame graph in the browser.
</p>

<div align="center">
  <a href="https://github.com/btnrv/CSS-Profiler/releases"><img src="https://img.shields.io/github/v/release/btnrv/CSS-Profiler?style=flat-square&label=latest"></a>
  <a href="https://github.com/btnrv/CSS-Profiler/releases"><img src="https://img.shields.io/github/release-date/btnrv/CSS-Profiler?style=flat-square&label=last%20release"></a>
  <a href="https://github.com/btnrv/CSS-Profiler/releases"><img src="https://img.shields.io/github/downloads/btnrv/CSS-Profiler/total.svg?style=flat-square&label=downloads"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/btnrv/CSS-Profiler?style=flat-square"></a>
</div>

<p align="center">
  <a href="https://github.com/btnrv/CSS-Profiler/releases/latest">
    <img src="https://img.shields.io/badge/download-latest%20release-2ea44f?style=for-the-badge&logo=github" alt="Download latest release">
  </a>
</p>

<p align="center">
  <a href="#install"><b>Install</b></a> ·
  <a href="#usage"><b>Usage</b></a> ·
  <a href="#reading-it-in-speedscope"><b>Reading it in speedscope</b></a> ·
  <a href="#commands"><b>Commands</b></a>
</p>

---

There are two profilers:

- **Sampler** (`css_prof_*`) — samples every managed thread ~1000×/second and tracks allocations. Low, fixed overhead. Use it to answer "which plugin is expensive on average, and what is it allocating?"
- **Instrumented** (`css_prof_calls_*`) — wraps every plugin's event, listener, and command handlers with a stopwatch and times each individual call. Use it to answer "how long did one call take, and what was the worst spike?" A sampler averages spikes away; this catches them exactly.

Run one or the other, never both at once. The instrumented hooks would skew a sampled run.

## Install

1. Download the latest `CSSProfiler` zip from the [Releases page](https://github.com/btnrv/CSS-Profiler/releases).
2. Unzip it into `addons/counterstrikesharp/plugins/`. The zip already contains the plugin DLL and its `Microsoft.Diagnostics.*` dependencies.
3. Load it at server boot (or `css_plugins load CSSProfiler`). No load order matters.

The only requirement is that .NET diagnostics IPC is on, which it is by default. It's off only if someone set `DOTNET_EnableDiagnostics=0` on the server process.

## Usage

The flow is the same for both profilers: record, export, open in speedscope.

1. **Record** while the thing you want to measure is happening. Pass a duration in seconds so it stops itself:

   ```
   css_prof_start 30          // sampler
   css_prof_calls_start 30    // instrumented
   ```

   <p align="center"><img src="https://github.com/user-attachments/assets/PLACEHOLDER-01-console-start" alt="Starting a capture from the server console" width="720"></p>

2. **Export** once it stops. This writes a `.speedscope.json` file and logs its full path:

   ```
   css_prof_export
   css_prof_calls_export
   ```

3. **Open** [speedscope.app](https://www.speedscope.app) and drag the exported file onto the page (or click *Browse* and pick it). The file is under:

   ```
   addons/counterstrikesharp/logs/CSS-Profiler/
   ```

   <p align="center"><img src="https://github.com/user-attachments/assets/PLACEHOLDER-02-speedscope-import" alt="Dropping the exported file into speedscope" width="720"></p>

You can also print a quick table to the console instead of exporting: `css_prof_report` or `css_prof_calls_report`.

## Reading it in speedscope

Speedscope has three views, switched with the buttons at the top left.

**Time Order** lays every call out left to right in the order it happened. Time runs along the x-axis, so a wide box is a slow call. This is the view for hunting a specific lag spike: find the fat box, click it, read its exact duration. On an instrumented export, every box is one real invocation.

<p align="center"><img src="https://github.com/user-attachments/assets/PLACEHOLDER-03-time-order" alt="Time Order view in speedscope" width="820"></p>

**Left Heavy** merges identical stacks and sorts them heaviest first, so the most expensive code is always on the left. Timing order is lost, but it's the fastest way to see what dominates overall.

<p align="center"><img src="https://github.com/user-attachments/assets/PLACEHOLDER-04-left-heavy" alt="Left Heavy view in speedscope" width="820"></p>

**Sandwich** is a sortable table of every function by total and self time. Click a row and speedscope shows its callers above it and its callees below (the "sandwich"), which is how you trace who called an expensive function and what it called in turn.

<p align="center"><img src="https://github.com/user-attachments/assets/PLACEHOLDER-05-sandwich" alt="Sandwich view in speedscope" width="820"></p>

## Commands

Sampler:

| Command | What it does |
|---|---|
| `css_prof_start [seconds]` | Start recording. With `seconds` it stops itself; without, it runs until `css_prof_stop`. |
| `css_prof_stop` | Stop and analyze in the background. |
| `css_prof_status` | Show state and the last analysis summary. |
| `css_prof_report [top] [filter]` | Print the analysis tables (default top 25, optional substring filter). |
| `css_prof_export` | Write the capture as a speedscope profile. |

Instrumented:

| Command | What it does |
|---|---|
| `css_prof_calls_start [seconds]` | Hook every plugin handler and time each call. With `seconds` it stops itself. |
| `css_prof_calls_stop` | Stop and unhook everything. |
| `css_prof_calls_status` | Show whether recording is active and the call count so far. |
| `css_prof_calls_report [top] [filter]` | Print per-handler calls, average ms, worst-case ms, and total ms. |
| `css_prof_calls_export` | Write the recorded calls as an evented speedscope timeline. |
| `css_prof_calls_reset` | Discard the recorded calls. |

## Keep captures short

Prefer a `seconds` argument. An open-ended capture grows fast (about 1.4 GB of trace per hour for the sampler), and analyzing a long one needs several GB of RAM on the same machine as the game server. The instrumented profiler holds every call in memory while it runs. A window of seconds to a few minutes is almost always enough.

## Files

Everything lands in `addons/counterstrikesharp/logs/CSS-Profiler/`:

- `CSSProfiler_<timestamp>.nettrace` — raw sampler trace (also opens in PerfView, `dotnet-trace`, or Visual Studio).
- `CSSProfiler_<timestamp>.speedscope.json` — sampler flame graph.
- `CSSProfiler_calls_<timestamp>.speedscope.json` — instrumented per-call timeline.

## Notes

- In the sampler's speedscope export, the self time of every managed method shows as 0. That's a known artifact of how the sampler builds thread-time stacks. Read per-method self time from the `EXCL` column in `css_prof_report`, or use the instrumented profiler for exact self time.
- The instrumented profiler only times CounterStrikeSharp dispatch boundaries (event handlers, `RegisterListener` handlers, command callbacks), not arbitrary methods inside them. It reflects into framework internals to install its wrappers, so revalidate it after a CounterStrikeSharp upgrade.
- Hot-reloading this plugin while an old capture is still winding down can wedge the reload. Load it at boot, or restart the server between plugin versions.

## License

[MIT](LICENSE) © btnrv
