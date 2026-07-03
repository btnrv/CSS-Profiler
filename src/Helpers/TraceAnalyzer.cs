namespace CSSProfiler;

internal static class TraceAnalyzer
{
    public static CaptureAnalysis Analyze(string nettracePath, DateTime capturedAtUtc, TimeSpan duration)
    {
        using var trace = TraceLogSession.Open(nettracePath);

        var (methods, modules) = CpuStackAggregator.Aggregate(StackSourceBuilder.Build(trace.Log));
        var allocations = AllocationAggregator.Aggregate(trace.Log);
        return new CaptureAnalysis(capturedAtUtc, duration, nettracePath, methods, modules, allocations);
    }
}
