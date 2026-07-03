namespace CSSProfiler;

internal readonly record struct CallRecord(int LabelId, long EnterTicks, long ExitTicks);
