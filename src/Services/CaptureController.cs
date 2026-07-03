using System.Globalization;
using Microsoft.Extensions.Logging;

namespace CSSProfiler;

internal sealed class CaptureController
{
    private readonly ILogger logger;
    private readonly string captureDirectory;
    private readonly TraceRecorder recorder = new();
    private readonly object gate = new();

    private DateTime captureStartedUtc;
    private int captureId;

    public CaptureState State { get; private set; } = CaptureState.Idle;
    public CaptureAnalysis? LastAnalysis { get; private set; }

    public CaptureController(ILogger logger, string captureDirectory)
    {
        this.logger = logger;
        this.captureDirectory = captureDirectory;
        Directory.CreateDirectory(captureDirectory);
    }

    public TimeSpan RecordingElapsed =>
        State == CaptureState.Recording ? DateTime.UtcNow - captureStartedUtc : TimeSpan.Zero;

    public bool TryStart(out int id, out string tracePath)
    {
        lock (gate)
        {
            id = 0;
            tracePath = string.Empty;
            if (State is CaptureState.Recording or CaptureState.Analyzing) return false;

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            tracePath = Path.Combine(captureDirectory, $"CSSProfiler_{stamp}.nettrace");
            recorder.Start(tracePath);

            captureStartedUtc = DateTime.UtcNow;
            State = CaptureState.Recording;
            id = ++captureId;
            return true;
        }
    }

    public bool TryStop(int? id = null)
    {
        lock (gate)
        {
            if (State != CaptureState.Recording) return false;
            if (id.HasValue && id.Value != captureId) return false;
            State = CaptureState.Analyzing;
        }

        Task.Run(StopAndAnalyze);
        return true;
    }

    public void Shutdown()
    {
        lock (gate)
        {
            if (State == CaptureState.Recording) recorder.Abort();
            if (State != CaptureState.Analyzing) State = CaptureState.Idle;
        }
    }

    private void StopAndAnalyze()
    {
        try
        {
            var duration = DateTime.UtcNow - captureStartedUtc;
            string tracePath = recorder.Stop();
            var analysis = TraceAnalyzer.Analyze(tracePath, captureStartedUtc, duration);

            lock (gate)
            {
                LastAnalysis = analysis;
                State = CaptureState.Ready;
            }

            logger.LogInformation(
                "[CSS-Profiler] Capture analyzed: {Methods} method(s), {Modules} module(s), {Allocations} allocation type(s). Run css_prof_report.",
                analysis.Methods.Count, analysis.Modules.Count, analysis.Allocations.Count);
        }
        catch (Exception e)
        {
            lock (gate) State = CaptureState.Idle;
            logger.LogError(e, "[CSS-Profiler] Capture analysis failed.");
        }
    }
}
