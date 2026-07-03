using CounterStrikeSharp.API.Core;

namespace CSSProfiler;

public partial class CSSProfilerPlugin : BasePlugin
{
    public override string ModuleName => "CSS-Profiler";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "gyro";
    public override string ModuleDescription => "CS# runtime profiler.";

    private string captureDirectory = null!;
    private CaptureController captureController = null!;
    private CallProfiler callProfiler = null!;

    public override void Load(bool hotReload)
    {
        captureDirectory = Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "logs", "CSS-Profiler"));
        captureController = new CaptureController(Logger, captureDirectory);
        callProfiler = new CallProfiler(Logger);
    }

    public override void Unload(bool hotReload)
    {
        callProfiler.Shutdown();
        captureController.Shutdown();
    }
}
