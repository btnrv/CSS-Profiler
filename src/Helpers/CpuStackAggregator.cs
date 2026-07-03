using Microsoft.Diagnostics.Tracing.Stacks;

namespace CSSProfiler;

internal static class CpuStackAggregator
{
    private const int MaxWalkDepth = 512;

    private sealed class MethodAccumulator
    {
        public long Samples;
        public double InclusiveMs;
        public double ExclusiveMs;
    }

    public static (IReadOnlyList<MethodCpuTiming> Methods, IReadOnlyList<ModuleCpuTiming> Modules) Aggregate(StackSource source)
    {
        var accumulators = new Dictionary<string, MethodAccumulator>(StringComparer.Ordinal);
        source.ForEach(sample => AccumulateSample(source, sample, accumulators));
        return (BuildMethodTable(accumulators), BuildModuleTable(accumulators));
    }

    private static void AccumulateSample(
        StackSource source, StackSourceSample sample, Dictionary<string, MethodAccumulator> accumulators)
    {
        var stackIndex = sample.StackIndex;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool exclusiveTaken = false;

        for (int depth = 0; depth < MaxWalkDepth && stackIndex != StackSourceCallStackIndex.Invalid; depth++)
        {
            string frame = source.GetFrameName(source.GetFrameIndex(stackIndex), false);

            if (IsMethodFrame(frame))
            {
                if (!accumulators.TryGetValue(frame, out var accumulator))
                    accumulators[frame] = accumulator = new MethodAccumulator();

                if (seen.Add(frame)) accumulator.InclusiveMs += sample.Metric;

                if (!exclusiveTaken)
                {
                    accumulator.ExclusiveMs += sample.Metric;
                    accumulator.Samples++;
                    exclusiveTaken = true;
                }
            }

            stackIndex = source.GetCallerIndex(stackIndex);
        }
    }

    private static bool IsMethodFrame(string frame)
    {
        int separator = frame.IndexOf('!');
        return separator > 0 && frame[..separator] != "?";
    }

    private static IReadOnlyList<MethodCpuTiming> BuildMethodTable(Dictionary<string, MethodAccumulator> accumulators) =>
        accumulators
            .OrderByDescending(entry => entry.Value.ExclusiveMs)
            .Select(entry => new MethodCpuTiming(
                entry.Key,
                entry.Value.Samples,
                entry.Value.InclusiveMs,
                entry.Value.ExclusiveMs))
            .ToList();

    private static IReadOnlyList<ModuleCpuTiming> BuildModuleTable(Dictionary<string, MethodAccumulator> accumulators) =>
        accumulators
            .GroupBy(entry => ModuleOf(entry.Key), StringComparer.Ordinal)
            .Select(group => new ModuleCpuTiming(
                group.Key,
                group.Sum(entry => entry.Value.Samples),
                group.Sum(entry => entry.Value.ExclusiveMs)))
            .OrderByDescending(module => module.ExclusiveMs)
            .ToList();

    private static string ModuleOf(string frameName) => frameName[..frameName.IndexOf('!')];
}
