namespace CSSProfiler;

internal readonly record struct MethodCpuTiming
(
    string Name,
    long   Samples,
    double InclusiveMs,
    double ExclusiveMs
);
