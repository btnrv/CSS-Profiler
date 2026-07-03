using Microsoft.Extensions.Logging;

namespace CSSProfiler;

internal static class ConsoleReport
{
    private const int MethodWidth = 88;
    private const int ModuleWidth = 64;
    private const int TypeWidth = 56;

    public static void Write(ILogger logger, CaptureAnalysis analysis, int top, string? filter)
    {
        logger.LogInformation("[CSS-Profiler] {Header}", FormattableString.Invariant(
            $"capture {analysis.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}Z, duration {analysis.Duration.TotalSeconds:F1} s, trace {analysis.NettracePath}"));

        WriteModules(logger, analysis.Modules, top);
        WriteMethods(logger, analysis.Methods, top, filter);
        WriteAllocations(logger, analysis.Allocations, top, filter);
    }

    private static void WriteModules(ILogger logger, IReadOnlyList<ModuleCpuTiming> modules, int top)
    {
        logger.LogInformation("[CSS-Profiler] top modules by exclusive thread time");
        logger.LogInformation("[CSS-Profiler] {Header}", $"{"MODULE",-ModuleWidth}{"SAMPLES",10}{"EXCL ms",12}");

        foreach (var module in modules.Take(top))
            logger.LogInformation("[CSS-Profiler] {Row}", FormattableString.Invariant(
                $"{Trim(module.Module, ModuleWidth),-ModuleWidth}{module.Samples,10}{module.ExclusiveMs,12:F1}"));
    }

    private static void WriteMethods(ILogger logger, IReadOnlyList<MethodCpuTiming> methods, int top, string? filter)
    {
        logger.LogInformation("[CSS-Profiler] top methods by exclusive thread time (INCL includes callees)");
        logger.LogInformation("[CSS-Profiler] {Header}",
            $"{"METHOD",-MethodWidth}{"SAMPLES",9}{"INCL ms",11}{"EXCL ms",11}");

        foreach (var method in methods.Where(method => Matches(method.Name, filter)).Take(top))
            logger.LogInformation("[CSS-Profiler] {Row}", FormattableString.Invariant(
                $"{Trim(method.Name, MethodWidth),-MethodWidth}{method.Samples,9}{method.InclusiveMs,11:F1}{method.ExclusiveMs,11:F1}"));
    }

    private static void WriteAllocations(ILogger logger, IReadOnlyList<AllocationStat> allocations, int top, string? filter)
    {
        logger.LogInformation("[CSS-Profiler] top allocated types (sampled via GCAllocationTick, ~100 KB granularity)");
        logger.LogInformation("[CSS-Profiler] {Header}", $"{"TYPE",-TypeWidth}{"TICKS",8}{"~ALLOC KB",12}  TOP SITE");

        foreach (var allocation in allocations.Where(allocation => Matches(allocation.TypeName, filter)).Take(top))
            logger.LogInformation("[CSS-Profiler] {Row}", FormattableString.Invariant(
                $"{Trim(allocation.TypeName, TypeWidth),-TypeWidth}{allocation.Ticks,8}{allocation.SampledBytes / 1024.0,12:F0}  {allocation.TopSite}"));
    }

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrEmpty(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Trim(string value, int width) =>
        value.Length <= width ? value : value[..(width - 1)] + "…";
}
