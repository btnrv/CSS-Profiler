using System.Collections;
using System.Diagnostics;
using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using Microsoft.Extensions.Logging;

namespace CSSProfiler;

internal sealed class CallProfiler
{
    private static readonly Type GameEventHandlerDefinition = typeof(BasePlugin.GameEventHandler<>);

    private static readonly MethodInfo MakeEventWrapperMethod =
        typeof(CallProfiler).GetMethod(nameof(MakeEventWrapper), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly ILogger logger;
    private readonly CallProfilerStore store = new();
    private readonly List<HookedEntry> hooked = new();
    private readonly Assembly ownAssembly = typeof(CallProfiler).Assembly;

    private bool recording;
    private long captureStartTicks;
    private long captureStopTicks;

    public CallProfiler(ILogger logger) => this.logger = logger;

    public bool Recording => recording;
    public int HookedCount => hooked.Count;
    public CallProfilerStore Store => store;
    public long CaptureStartTicks => captureStartTicks;
    public long CaptureStopTicks => recording ? Stopwatch.GetTimestamp() : captureStopTicks;

    public bool TryStart(out int hookedCount)
    {
        hookedCount = 0;
        if (recording) return false;

        store.Reset();
        HookAll();
        captureStartTicks = Stopwatch.GetTimestamp();
        recording = true;
        hookedCount = hooked.Count;
        return true;
    }

    public bool TryStop()
    {
        if (!recording) return false;

        captureStopTicks = Stopwatch.GetTimestamp();
        recording = false;
        UnhookAll();
        return true;
    }

    public void Shutdown()
    {
        recording = false;
        UnhookAll();
    }

    private void HookAll()
    {
        UnhookAll();

        var registry = CssInternals.GetFunctionReferenceRegistry();
        if (registry is null)
        {
            logger.LogError("[CSS-Profiler] FunctionReference registry not found; call profiling unavailable.");
            return;
        }

        var plugins = CssInternals.GetLoadedPlugins().ToList();
        var map = CssInternals.BuildPluginAssemblyMap(plugins);

        int eventCount = HookEventsAndCommands(registry, map, out int commandCount);
        int listenerCount = HookListeners(registry, plugins);

        logger.LogInformation(
            "[CSS-Profiler] Instrumented {Events} event handler(s), {Commands} command dispatcher(s), {Listeners} listener(s).",
            eventCount, commandCount, listenerCount);
    }

    private int HookEventsAndCommands(IDictionary registry, Dictionary<Assembly, string> map, out int commandCount)
    {
        int eventCount = 0;
        commandCount = 0;

        foreach (DictionaryEntry entry in registry)
        {
            if (entry.Value is null) continue;

            var target = CssInternals.FindFirstDelegateField(entry.Value);
            if (target is null) continue;

            var (field, original) = target.Value;
            if (original.Method.DeclaringType?.Assembly == ownAssembly) continue;

            bool isCommand = CssInternals.IsCommandDispatcher(original);
            Delegate? wrapper = isCommand
                ? MakeCommandDispatchWrapper(original, map)
                : TryBuildEventWrapper(original, CssInternals.ResolvePluginName(original, map));
            if (wrapper is null) continue;

            field.SetValue(entry.Value, wrapper);
            hooked.Add(new HookedEntry(entry.Value, field, original, wrapper));

            if (isCommand) commandCount++;
            else eventCount++;
        }

        return eventCount;
    }

    private int HookListeners(IDictionary registry, IReadOnlyList<BasePlugin> plugins)
    {
        int listenerCount = 0;

        foreach (var (functionReference, field, original, plugin, label) in CssInternals.FindListenerHandlers(registry, plugins))
        {
            if (original is not Func<ScriptContext, HookResult> inner) continue;
            if (inner.Method.DeclaringType?.Assembly == ownAssembly) continue;

            int labelId = store.Intern($"{plugin} | {label}");
            Delegate wrapper = MakeListenerWrapper(inner, labelId);

            field.SetValue(functionReference, wrapper);
            hooked.Add(new HookedEntry(functionReference, field, original, wrapper));
            listenerCount++;
        }

        return listenerCount;
    }

    // Restore each field only where our wrapper is still current, so a hook chained over us by another plugin is left intact.
    private void UnhookAll()
    {
        foreach (var hook in hooked)
        {
            try
            {
                if (ReferenceEquals(hook.DelegateField.GetValue(hook.FunctionReference), hook.Wrapper))
                    hook.DelegateField.SetValue(hook.FunctionReference, hook.Original);
            }
            catch (Exception e)
            {
                logger.LogError(e, "[CSS-Profiler] Failed to restore a hooked handler; server may be unstable.");
            }
        }

        hooked.Clear();
    }

    private Delegate? TryBuildEventWrapper(Delegate original, string plugin)
    {
        var delegateType = original.GetType();
        if (!delegateType.IsGenericType || delegateType.GetGenericTypeDefinition() != GameEventHandlerDefinition)
            return null;

        var eventType = delegateType.GetGenericArguments()[0];
        string eventName = CssInternals.GetEventName(eventType) ?? eventType.Name;
        int labelId = store.Intern($"{plugin} | event:{eventName}");

        return (Delegate)MakeEventWrapperMethod.MakeGenericMethod(eventType).Invoke(this, [original, labelId])!;
    }

    // Invoked through reflection because the concrete event type is only known at hook time.
    private Delegate MakeEventWrapper<T>(Delegate original, int labelId) where T : GameEvent
    {
        var inner = (BasePlugin.GameEventHandler<T>)original;

        BasePlugin.GameEventHandler<T> wrapper = (gameEvent, info) =>
        {
            long start = Stopwatch.GetTimestamp();
            try { return inner(gameEvent, info); }
            finally { store.Record(labelId, start, Stopwatch.GetTimestamp()); }
        };

        return wrapper;
    }

    // Inner is invoked directly (not DynamicInvoke) so CounterStrikeSharp's PluginTerminationException flow is preserved.
    private Delegate? MakeCommandDispatchWrapper(Delegate original, Dictionary<Assembly, string> map)
    {
        if (original is not Action<int, IntPtr> inner)
        {
            logger.LogWarning("[CSS-Profiler] Command dispatcher had unexpected signature ({Type}); commands not profiled.",
                original.GetType().Name);
            return null;
        }

        var definitions = original.Target is null ? null : CssInternals.GetCommandDefinitions(original.Target);

        Action<int, IntPtr> wrapper = (playerSlot, commandInfo) =>
        {
            string name = CssInternals.ReadCommandName(commandInfo);
            string plugin = CssInternals.ResolveCommandPlugin(definitions, name, map);
            int labelId = store.Intern($"{plugin} | cmd:{name}");

            long start = Stopwatch.GetTimestamp();
            try { inner(playerSlot, commandInfo); }
            finally { store.Record(labelId, start, Stopwatch.GetTimestamp()); }
        };

        return wrapper;
    }

    // A plain lambda (not a compiled expression tree) keeps its captures in Target, leaving ScriptContext as the only
    // parameter; FunctionReference dispatch rejects a leading Closure parameter and would throw on every call otherwise.
    private Func<ScriptContext, HookResult> MakeListenerWrapper(Func<ScriptContext, HookResult> inner, int labelId)
    {
        return context =>
        {
            long start = Stopwatch.GetTimestamp();
            try { return inner(context); }
            finally { store.Record(labelId, start, Stopwatch.GetTimestamp()); }
        };
    }
}
