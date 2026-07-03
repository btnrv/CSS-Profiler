namespace CSSProfiler;

internal sealed record CaptureAnalysis
(
    DateTime CapturedAtUtc,
    TimeSpan Duration,
    string NettracePath,
    IReadOnlyList<MethodCpuTiming> Methods,
    IReadOnlyList<ModuleCpuTiming> Modules,
    IReadOnlyList<AllocationStat> Allocations
);
