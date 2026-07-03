using System.Diagnostics;
using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace CSSProfiler;

public partial class CSSProfilerPlugin
{
    [ConsoleCommand("css_prof_calls_start", "Start instrumented per-call timing of every plugin handler. Usage: css_prof_calls_start [seconds]")]
    public void OnCallsStart(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        if (!callProfiler.TryStart(out int hookedCount))
        {
            command.ReplyToCommand("[CSS-Profiler] Call profiling is already running. Stop it with css_prof_calls_stop.");
            return;
        }

        if (TryParseSeconds(command, out int seconds))
        {
            AddTimer(seconds, () => callProfiler.TryStop());
            command.ReplyToCommand($"[CSS-Profiler] Instrumented {hookedCount} handler(s); recording per-call timings for {seconds} s.");
            return;
        }

        command.ReplyToCommand($"[CSS-Profiler] Instrumented {hookedCount} handler(s); recording until css_prof_calls_stop.");
        Logger.LogWarning("[CSS-Profiler] Open-ended call profiling keeps every call in memory. Keep the window short.");
    }

    [ConsoleCommand("css_prof_calls_stop", "Stop instrumented per-call timing and restore all handlers")]
    public void OnCallsStop(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        command.ReplyToCommand(callProfiler.TryStop()
            ? $"[CSS-Profiler] Call profiling stopped; {callProfiler.Store.TotalCalls} call(s) recorded. Use css_prof_calls_report / css_prof_calls_export."
            : "[CSS-Profiler] Call profiling is not running.");
    }

    [ConsoleCommand("css_prof_calls_status", "Show instrumented per-call profiling state")]
    public void OnCallsStatus(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        command.ReplyToCommand(callProfiler.Recording
            ? $"[CSS-Profiler] Recording: {callProfiler.HookedCount} handler(s) hooked, {callProfiler.Store.TotalCalls} call(s) so far."
            : $"[CSS-Profiler] Idle; {callProfiler.Store.TotalCalls} call(s) from the last window.");
    }

    [ConsoleCommand("css_prof_calls_report", "Print per-handler call counts and timing. Usage: css_prof_calls_report [top] [filter]")]
    public void OnCallsReport(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        if (callProfiler.Store.TotalCalls == 0)
        {
            command.ReplyToCommand("[CSS-Profiler] No calls recorded. Run css_prof_calls_start then css_prof_calls_stop.");
            return;
        }

        double seconds = (callProfiler.CaptureStopTicks - callProfiler.CaptureStartTicks) * 1.0 / Stopwatch.Frequency;
        InstrumentedReport.Write(Logger, callProfiler.Store, seconds, ParseTop(command), ParseFilter(command));
        command.ReplyToCommand("[CSS-Profiler] Report written to the server console.");
    }

    [ConsoleCommand("css_prof_calls_export", "Write recorded per-call timings as an evented speedscope timeline")]
    public void OnCallsExport(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        if (callProfiler.Store.TotalCalls == 0)
        {
            command.ReplyToCommand("[CSS-Profiler] No calls recorded to export.");
            return;
        }

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string output = Path.Combine(captureDirectory, $"CSSProfiler_calls_{stamp}.speedscope.json");

        try
        {
            InstrumentedSpeedscopeWriter.Export(callProfiler, "CSS-Profiler calls " + stamp, output);
            command.ReplyToCommand($"[CSS-Profiler] Per-call speedscope written to {output}");
        }
        catch (Exception e)
        {
            Logger.LogError(e, "[CSS-Profiler] Per-call speedscope export failed.");
            command.ReplyToCommand("[CSS-Profiler] Export failed; see the server console.");
        }
    }

    [ConsoleCommand("css_prof_calls_reset", "Clear recorded per-call timings")]
    public void OnCallsReset(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        if (callProfiler.Recording)
        {
            command.ReplyToCommand("[CSS-Profiler] Stop call profiling before resetting.");
            return;
        }

        callProfiler.Store.Reset();
        command.ReplyToCommand("[CSS-Profiler] Per-call timings cleared.");
    }
}
