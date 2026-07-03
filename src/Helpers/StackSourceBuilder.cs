using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace CSSProfiler;

internal static class StackSourceBuilder
{
    public static StackSource Build(TraceLog traceLog)
    {
        using var symbolReader = new SymbolReader(TextWriter.Null);

        var stackSource = new MutableTraceEventStackSource(traceLog) { OnlyManagedCodeStacks = true };
        var computer = new SampleProfilerThreadTimeComputer(traceLog, symbolReader) { IncludeEventSourceEvents = false };
        computer.GenerateThreadTimeStacks(stackSource);
        return stackSource;
    }
}
