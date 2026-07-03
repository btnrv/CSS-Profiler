namespace CSSProfiler;

internal readonly record struct AllocationStat
(
    string TypeName,
    long   Ticks,
    long   SampledBytes,
    string TopSite
);
