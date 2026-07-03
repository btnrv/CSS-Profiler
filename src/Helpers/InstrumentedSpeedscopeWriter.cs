using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSSProfiler;

// Serializes recorded per-call enter/exit timings as an EVENTED speedscope profile
// (https://github.com/jlfwong/speedscope): one profile per thread, one Open/Close event pair per real call,
// so each box in the flame graph is a single invocation with its exact millisecond duration. This is the
// instrumented counterpart to the sampled SpeedscopeWriter, which cannot express individual calls.
internal static class InstrumentedSpeedscopeWriter
{
    private const string SchemaUrl = "https://www.speedscope.app/file-format-schema.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Export(CallProfiler profiler, string profileName, string outputPath)
    {
        var store = profiler.Store;
        long startTicks = profiler.CaptureStartTicks;

        var frames = store.Labels.Select(name => new Frame(name)).ToList();
        var profiles = new List<Profile>();

        foreach (var buffer in store.Buffers)
        {
            if (buffer.Records.Count == 0) continue;
            profiles.Add(BuildThreadProfile(buffer, startTicks));
        }

        var document = new Document(SchemaUrl, profileName, "CSS-Profiler", 0, new Shared(frames), profiles);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, Options));
        return outputPath;
    }

    // Reconstructs the call tree for one thread from its flat (enter,exit) records. Synchronous handler calls on a
    // single thread are properly nested, so sorting by enter time and walking a stack recovers the nesting; each
    // child's close time is clamped to its parent's so the emitted events stay strictly ordered even if two
    // intervals ever overlap improperly (re-entrancy), which speedscope requires.
    private static Profile BuildThreadProfile(CallProfilerStore.ThreadBuffer buffer, long startTicks)
    {
        var records = buffer.Records
            .OrderBy(record => record.EnterTicks)
            .ThenByDescending(record => record.ExitTicks)
            .ToList();

        var events = new List<EventItem>(records.Count * 2);
        var open = new Stack<(int Frame, long ExitTicks)>();

        foreach (var record in records)
        {
            while (open.Count > 0 && open.Peek().ExitTicks <= record.EnterTicks)
            {
                var closed = open.Pop();
                events.Add(new EventItem("C", closed.Frame, ToMs(closed.ExitTicks, startTicks)));
            }

            long parentExit = open.Count > 0 ? open.Peek().ExitTicks : long.MaxValue;
            long effExit = Math.Min(record.ExitTicks, parentExit);
            if (effExit < record.EnterTicks) effExit = record.EnterTicks;

            events.Add(new EventItem("O", record.LabelId, ToMs(record.EnterTicks, startTicks)));
            open.Push((record.LabelId, effExit));
        }

        while (open.Count > 0)
        {
            var closed = open.Pop();
            events.Add(new EventItem("C", closed.Frame, ToMs(closed.ExitTicks, startTicks)));
        }

        double profileStart = events.Min(e => e.At);
        double profileEnd = events.Max(e => e.At);
        return new Profile("evented", $"Thread ({buffer.ThreadId})", "milliseconds", profileStart, profileEnd, events);
    }

    private static double ToMs(long ticks, long startTicks)
    {
        double ms = (ticks - startTicks) * 1000.0 / Stopwatch.Frequency;
        return ms < 0 ? 0 : ms;
    }

    private sealed record Frame(
        [property: JsonPropertyName("name")] string Name);

    private sealed record Shared(
        [property: JsonPropertyName("frames")] IReadOnlyList<Frame> Frames);

    private sealed record EventItem(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("frame")] int Frame,
        [property: JsonPropertyName("at")] double At);

    private sealed record Profile(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("unit")] string Unit,
        [property: JsonPropertyName("startValue")] double StartValue,
        [property: JsonPropertyName("endValue")] double EndValue,
        [property: JsonPropertyName("events")] IReadOnlyList<EventItem> Events);

    private sealed record Document(
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("exporter")] string Exporter,
        [property: JsonPropertyName("activeProfileIndex")] int ActiveProfileIndex,
        [property: JsonPropertyName("shared")] Shared Shared,
        [property: JsonPropertyName("profiles")] IReadOnlyList<Profile> Profiles);
}
