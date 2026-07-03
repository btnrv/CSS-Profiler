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

## Two profilers

The sampler (`css_prof_*`) snapshots every managed thread about 1000 times a second and records allocations while it runs. Overhead is low and constant. It answers the average question: which plugin costs the most, and what is it allocating.

The instrumented profiler (`css_prof_calls_*`) is the opposite trade-off. It wraps every plugin's event, listener, and command handler in a stopwatch and times each call on its own, so the single call that spiked shows up instead of getting averaged away. Use it when you need the exact cost of one call and its worst case.

Run one or the other, never both. The instrumented hooks would skew a sampled run.

> [!WARNING]
> This is a debugging tool, not something to leave running on a live server. A capture can eat gigabytes of disk (the sampler writes about 1.4 GB an hour), and while it records it adds enough overhead to stutter the game. Load it to measure something, stop the capture, then unload it.

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

2. **Export** once it stops. This writes a `.speedscope.json` file and logs its full path:

   ```
   css_prof_export
   css_prof_calls_export
   ```

3. **Open** [speedscope.app](https://www.speedscope.app) and drag the exported file onto the page (or click *Browse* and pick it). The file is under:

   ```
   addons/counterstrikesharp/logs/CSS-Profiler/
   ```

You can also print a quick table to the console instead of exporting: `css_prof_report` or `css_prof_calls_report`.

## Reading it in speedscope

Speedscope has three views, switched with the buttons at the top left.

**Time Order** lays every call out left to right in the order it happened. Time runs along the x-axis, so a wide box is a slow call. This is the view for hunting a specific lag spike: find the fat box, click it, read its exact duration. On an instrumented export, every box is one real invocation.

<p align="center"><img src="https://github.com/user-attachments/assets/b475b83e-3587-4add-a965-d1e400e4f343" alt="Time Order view in speedscope" width="820"></p>

**Left Heavy** merges identical stacks and sorts them heaviest first, so the most expensive code is always on the left. Timing order is lost, but it's the fastest way to see what dominates overall.

<p align="center"><img src="https://github.com/user-attachments/assets/73aa42a2-8bd0-4ef3-b75a-595e18ae1bdb" alt="Left Heavy view in speedscope" width="820"></p>

**Sandwich** is a sortable table of every function by total and self time. Click a row and speedscope shows its callers above it and its callees below (the "sandwich"), which is how you trace who called an expensive function and what it called in turn.

<p align="center"><img src="https://github.com/user-attachments/assets/360ba76b-b51f-4131-9b0d-2720de492200" alt="Sandwich view in speedscope" width="820"></p>

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

## License

[MIT](LICENSE) © btnrv
