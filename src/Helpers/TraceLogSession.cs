using Microsoft.Diagnostics.Tracing.Etlx;

namespace CSSProfiler;

internal sealed class TraceLogSession : IDisposable
{
    private readonly string etlxPath;

    public TraceLog Log { get; }

    private TraceLogSession(TraceLog log, string etlxPath)
    {
        Log = log;
        this.etlxPath = etlxPath;
    }

    public static TraceLogSession Open(string nettracePath)
    {
        string etlxPath = Path.ChangeExtension(nettracePath, ".etlx");
        TraceLog.CreateFromEventPipeDataFile(nettracePath, etlxPath);
        try
        {
            return new TraceLogSession(new TraceLog(etlxPath), etlxPath);
        }
        catch
        {
            TryDelete(etlxPath);
            throw;
        }
    }

    public void Dispose()
    {
        Log.Dispose();
        TryDelete(etlxPath);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
