using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace CSSProfiler;

internal static class AllocationAggregator
{
    private const int MaxWalkDepth = 128;

    private sealed class AllocationAccumulator
    {
        public long Ticks;
        public long SampledBytes;
        public readonly Dictionary<string, long> Sites = new(StringComparer.Ordinal);
    }

    public static IReadOnlyList<AllocationStat> Aggregate(TraceLog traceLog)
    {
        var accumulators = new Dictionary<string, AllocationAccumulator>(StringComparer.Ordinal);

        foreach (var tick in traceLog.Events.ByEventType<GCAllocationTickTraceData>())
        {
            string typeName = string.IsNullOrEmpty(tick.TypeName) ? "<unknown>" : tick.TypeName;
            if (!accumulators.TryGetValue(typeName, out var accumulator))
                accumulators[typeName] = accumulator = new AllocationAccumulator();

            accumulator.Ticks++;
            accumulator.SampledBytes += tick.AllocationAmount64;

            string site = ResolveAllocationSite(tick);
            accumulator.Sites[site] = accumulator.Sites.GetValueOrDefault(site) + 1;
        }

        return accumulators
            .OrderByDescending(entry => entry.Value.SampledBytes)
            .Select(entry => new AllocationStat(
                entry.Key,
                entry.Value.Ticks,
                entry.Value.SampledBytes,
                TopSite(entry.Value.Sites)))
            .ToList();
    }

    private static string ResolveAllocationSite(GCAllocationTickTraceData tick)
    {
        var frame = tick.CallStack();

        for (int depth = 0; frame is not null && depth < MaxWalkDepth; depth++)
        {
            string module = frame.CodeAddress.ModuleName;
            string method = frame.CodeAddress.FullMethodName;
            if (IsUserSite(module, method)) return module + "!" + method;
            frame = frame.Caller;
        }

        return "<unresolved>";
    }

    private static bool IsUserSite(string module, string method) =>
        method.Length > 0
        && module.Length > 0
        && module != "coreclr"
        && !module.StartsWith("System.", StringComparison.Ordinal)
        && !module.StartsWith("Microsoft.", StringComparison.Ordinal)
        && !module.StartsWith("libcoreclr", StringComparison.Ordinal);

    private static string TopSite(Dictionary<string, long> sites) =>
        sites.Count == 0 ? "<unresolved>" : sites.MaxBy(entry => entry.Value).Key;
}
