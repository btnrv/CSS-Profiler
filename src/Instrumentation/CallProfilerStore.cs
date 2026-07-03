using System.Collections.Concurrent;

namespace CSSProfiler;

internal sealed class CallProfilerStore
{
    internal sealed class ThreadBuffer(int threadId)
    {
        public int ThreadId { get; } = threadId;
        public List<CallRecord> Records { get; } = new(4096);
    }

    private readonly object internGate = new();
    private readonly Dictionary<string, int> labelIds = new(StringComparer.Ordinal);
    private readonly List<string> labels = new();
    private readonly ConcurrentBag<ThreadBuffer> allBuffers = new();
    private readonly ThreadLocal<ThreadBuffer> buffers;

    public CallProfilerStore()
    {
        buffers = new ThreadLocal<ThreadBuffer>(CreateBuffer);
    }

    private ThreadBuffer CreateBuffer()
    {
        var buffer = new ThreadBuffer(Environment.CurrentManagedThreadId);
        allBuffers.Add(buffer);
        return buffer;
    }

    public int Intern(string label)
    {
        lock (internGate)
        {
            if (labelIds.TryGetValue(label, out var id)) return id;
            id = labels.Count;
            labels.Add(label);
            labelIds[label] = id;
            return id;
        }
    }

    public void Record(int labelId, long enterTicks, long exitTicks) =>
        buffers.Value!.Records.Add(new CallRecord(labelId, enterTicks, exitTicks));

    public IReadOnlyList<string> Labels => labels;

    public IReadOnlyList<ThreadBuffer> Buffers => allBuffers.ToArray();

    public long TotalCalls => allBuffers.Sum(buffer => (long)buffer.Records.Count);

    public void Reset()
    {
        lock (internGate)
        {
            foreach (var buffer in allBuffers) buffer.Records.Clear();
            labels.Clear();
            labelIds.Clear();
        }
    }
}
