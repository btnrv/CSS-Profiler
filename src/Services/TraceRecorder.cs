using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace CSSProfiler;

internal sealed class TraceRecorder
{
    private EventPipeSession? session;
    private Task? copyTask;
    private string? outputPath;

    public void Start(string path)
    {
        var providers = new[]
        {
            new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
            new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, (long)ClrTraceEventParser.Keywords.GC),
        };

        var client = new DiagnosticsClient(Environment.ProcessId);
        session = client.StartEventPipeSession(providers);
        outputPath = path;
        copyTask = CopyEventStream(session.EventStream, path);
    }

    public string Stop()
    {
        var active = session ?? throw new InvalidOperationException("No trace session is active.");

        active.Stop();
        copyTask?.GetAwaiter().GetResult();
        Cleanup(active);
        return outputPath!;
    }

    public void Abort()
    {
        var active = session;
        if (active is null) return;

        try { active.Stop(); } catch { }
        try { copyTask?.Wait(TimeSpan.FromSeconds(10)); } catch { }
        Cleanup(active);
    }

    private void Cleanup(EventPipeSession active)
    {
        active.Dispose();
        session = null;
        copyTask = null;
    }

    private static async Task CopyEventStream(Stream eventStream, string path)
    {
        await using var file = File.Create(path);
        await eventStream.CopyToAsync(file);
    }
}
