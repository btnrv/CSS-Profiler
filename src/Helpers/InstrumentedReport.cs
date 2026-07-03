using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CSSProfiler;

internal static class InstrumentedReport
{
    private const int LabelWidth = 72;

    private sealed class Row
    {
        public string Label = "";
        public long Count;
        public long SumTicks;
        public long MaxTicks;
    }

    public static void Write(ILogger logger, CallProfilerStore store, double captureSeconds, int top, string? filter)
    {
        var rows = Aggregate(store);

        logger.LogInformation("[CSS-Profiler] {Header}", FormattableString.Invariant(
            $"instrumented per-call timing, window {captureSeconds:F1} s, {store.TotalCalls} call(s) across {rows.Count} handler(s)"));
        logger.LogInformation("[CSS-Profiler] {Header}",
            $"{"HANDLER",-LabelWidth}{"CALLS",10}{"AVG ms",11}{"MAX ms",11}{"TOTAL ms",12}");

        foreach (var row in rows.Where(row => Matches(row.Label, filter)).Take(top))
        {
            double total = Ms(row.SumTicks);
            double avg = row.Count == 0 ? 0 : total / row.Count;
            logger.LogInformation("[CSS-Profiler] {Row}", FormattableString.Invariant(
                $"{Trim(row.Label, LabelWidth),-LabelWidth}{row.Count,10}{avg,11:F3}{Ms(row.MaxTicks),11:F3}{total,12:F2}"));
        }
    }

    private static List<Row> Aggregate(CallProfilerStore store)
    {
        var labels = store.Labels;
        var byLabel = new Dictionary<int, Row>();

        foreach (var buffer in store.Buffers)
        foreach (var record in buffer.Records)
        {
            long duration = record.ExitTicks - record.EnterTicks;
            if (duration < 0) duration = 0;

            if (!byLabel.TryGetValue(record.LabelId, out var row))
            {
                byLabel[record.LabelId] = row = new Row
                {
                    Label = record.LabelId < labels.Count ? labels[record.LabelId] : "?",
                    MaxTicks = long.MinValue,
                };
            }

            row.Count++;
            row.SumTicks += duration;
            if (duration > row.MaxTicks) row.MaxTicks = duration;
        }

        return byLabel.Values.OrderByDescending(row => row.SumTicks).ToList();
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrEmpty(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Trim(string value, int width) =>
        value.Length <= width ? value : value[..(width - 1)] + "…";
}
