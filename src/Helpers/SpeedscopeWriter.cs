using Microsoft.Diagnostics.Tracing.Stacks.Formats;

namespace CSSProfiler;

internal static class SpeedscopeWriter
{
    public static string Export(string nettracePath, string outputPath)
    {
        using var trace = TraceLogSession.Open(nettracePath);
        SpeedScopeStackSourceWriter.WriteStackViewAsJson(StackSourceBuilder.Build(trace.Log), outputPath);
        return outputPath;
    }
}
