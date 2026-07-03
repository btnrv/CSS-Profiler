namespace CSSProfiler;

internal readonly record struct ModuleCpuTiming
(
    string Module,
    long   Samples,
    double ExclusiveMs
);
