using System.Reflection;

namespace CSSProfiler;

internal sealed record HookedEntry(
    object    FunctionReference,
    FieldInfo DelegateField,
    Delegate  Original,
    Delegate  Wrapper
);
