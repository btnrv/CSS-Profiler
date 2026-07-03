using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace CSSProfiler;

public partial class CSSProfilerPlugin
{
    [ConsoleCommand("css_prof_start", "Start a sampled runtime capture. Usage: css_prof_start [seconds]")]
    public void OnSamplerStart(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        if (!captureController.TryStart(out int id, out string tracePath))
        {
            command.ReplyToCommand("[CSS-Profiler] A capture is already running or analyzing.");
            return;
        }

        if (TryParseSeconds(command, out int seconds))
        {
            AddTimer(seconds, () => captureController.TryStop(id));
            command.ReplyToCommand($"[CSS-Profiler] Recording to {tracePath} for {seconds} s.");
            return;
        }

        command.ReplyToCommand($"[CSS-Profiler] Recording to {tracePath} until css_prof_stop.");
        Logger.LogWarning(
            "[CSS-Profiler] Open-ended capture: the .nettrace grows ~1.4 GB/hour and analysis needs several GB of RAM. Keep it short.");
    }

    [ConsoleCommand("css_prof_stop", "Stop the running sampled capture and analyze it in the background")]
    public void OnSamplerStop(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        command.ReplyToCommand(captureController.TryStop()
            ? "[CSS-Profiler] Capture stopping; analysis runs in the background."
            : "[CSS-Profiler] No capture is running.");
    }

    [ConsoleCommand("css_prof_status", "Show the sampled capture state and the last analysis summary")]
    public void OnSamplerStatus(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        var analysis = captureController.LastAnalysis;
        string state = captureController.State switch
        {
            CaptureState.Recording => $"recording for {captureController.RecordingElapsed.TotalSeconds:F0} s",
            CaptureState.Analyzing => "analyzing",
            _ => "idle",
        };

        command.ReplyToCommand(analysis is null
            ? $"[CSS-Profiler] {state}; no analyzed capture."
            : FormattableString.Invariant(
                $"[CSS-Profiler] {state}; last capture {analysis.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}Z, {analysis.Duration.TotalSeconds:F1} s, {analysis.Methods.Count} method(s), {analysis.Allocations.Count} allocation type(s)."));
    }

    [ConsoleCommand("css_prof_report", "Print the sampled analysis tables. Usage: css_prof_report [top] [filter]")]
    public void OnSamplerReport(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        var analysis = captureController.LastAnalysis;
        if (analysis is null)
        {
            command.ReplyToCommand("[CSS-Profiler] No analyzed capture. Run css_prof_start then css_prof_stop.");
            return;
        }

        ConsoleReport.Write(Logger, analysis, ParseTop(command), ParseFilter(command));
        command.ReplyToCommand("[CSS-Profiler] Report written to the server console.");
    }

    [ConsoleCommand("css_prof_export", "Write the last sampled capture as a speedscope profile with full stacks")]
    public void OnSamplerExport(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) return;

        var analysis = captureController.LastAnalysis;
        if (analysis is null)
        {
            command.ReplyToCommand("[CSS-Profiler] No analyzed capture to export.");
            return;
        }

        string nettrace = analysis.NettracePath;
        string output = Path.ChangeExtension(nettrace, ".speedscope.json");
        command.ReplyToCommand("[CSS-Profiler] Export started; the file path is logged when finished.");

        Task.Run(() =>
        {
            try
            {
                SpeedscopeWriter.Export(nettrace, output);
                Logger.LogInformation("[CSS-Profiler] Speedscope written to {Path}", output);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "[CSS-Profiler] Speedscope export failed.");
            }
        });
    }

    private static bool TryParseSeconds(CommandInfo command, out int seconds)
    {
        seconds = 0;
        return command.ArgCount > 1 && int.TryParse(command.GetArg(1), out seconds) && seconds > 0;
    }

    private static int ParseTop(CommandInfo command) =>
        command.ArgCount > 1 && int.TryParse(command.GetArg(1), out int top) && top > 0 ? top : 25;

    private static string? ParseFilter(CommandInfo command) =>
        command.ArgCount > 2 ? command.GetArg(2) : null;
}
