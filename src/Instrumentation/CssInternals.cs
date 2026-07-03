using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Commands;

namespace CSSProfiler;

internal static class CssInternals
{
    private static readonly Assembly ApiAssembly = typeof(BasePlugin).Assembly;

    // drift: located by shape (the only static int->FunctionReference dictionary) so a field rename does not break discovery.
    public static IDictionary? GetFunctionReferenceRegistry()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        foreach (var field in typeof(FunctionReference).GetFields(flags))
        {
            if (!field.FieldType.IsGenericType) continue;

            var args = field.FieldType.GetGenericArguments();
            if (args.Length == 2 && args[0] == typeof(int) && typeof(FunctionReference).IsAssignableFrom(args[1])
                && field.GetValue(null) is IDictionary registry)
                return registry;
        }

        return null;
    }

    public static (FieldInfo Field, Delegate Delegate)? FindFirstDelegateField(object functionReference)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        foreach (var field in functionReference.GetType().GetFields(flags))
        {
            if (typeof(Delegate).IsAssignableFrom(field.FieldType) && field.GetValue(functionReference) is Delegate target)
                return (field, target);
        }

        return null;
    }

    // drift: walks Application._pluginManager.GetLoadedPlugins().Plugin, all CounterStrikeSharp internals.
    public static IEnumerable<BasePlugin> GetLoadedPlugins()
    {
        object application = Application.Instance;

        if (application.GetType().GetField("_pluginManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(application) is not { } pluginManager)
            yield break;

        if (pluginManager.GetType().GetMethod("GetLoadedPlugins")?.Invoke(pluginManager, null) is not IEnumerable contexts)
            yield break;

        foreach (var context in contexts)
        {
            if (context.GetType().GetProperty("Plugin")?.GetValue(context) is BasePlugin plugin)
                yield return plugin;
        }
    }

    // drift: relies on BasePlugin.Listeners (public dict keyed by user delegate) and CallbackSubscriber.GetReferenceIdentifier.
    public static IEnumerable<(object FunctionReference, FieldInfo Field, Delegate Original, string Plugin, string Label)>
        FindListenerHandlers(IDictionary registry, IEnumerable<BasePlugin> plugins)
    {
        var listenersField = typeof(BasePlugin).GetField("Listeners", BindingFlags.Instance | BindingFlags.Public);

        foreach (var plugin in plugins)
        {
            if (listenersField?.GetValue(plugin) is not IDictionary listeners) continue;

            foreach (DictionaryEntry entry in listeners)
            {
                if (entry.Key is not Delegate handler || entry.Value is null) continue;
                if (handler.Method.DeclaringType?.Assembly == ApiAssembly) continue;

                if (entry.Value.GetType().GetMethod("GetReferenceIdentifier", BindingFlags.Instance | BindingFlags.Public)
                        ?.Invoke(entry.Value, null) is not int referenceId) continue;

                if (registry[referenceId] is not { } functionReference) continue;

                var target = FindFirstDelegateField(functionReference);
                if (target is null) continue;

                yield return (functionReference, target.Value.Field, target.Value.Delegate,
                              DisplayName(plugin), "listener:" + handler.GetType().Name);
            }
        }
    }

    public static Dictionary<Assembly, string> BuildPluginAssemblyMap(IEnumerable<BasePlugin> plugins)
    {
        var map = new Dictionary<Assembly, string>();
        foreach (var plugin in plugins)
            map.TryAdd(plugin.GetType().Assembly, DisplayName(plugin));
        return map;
    }

    public static string ResolvePluginName(Delegate handler, Dictionary<Assembly, string> map)
    {
        var assembly = handler.Method.DeclaringType?.Assembly;
        return assembly is not null && map.TryGetValue(assembly, out var name)
            ? name
            : handler.Method.DeclaringType?.Name ?? "?";
    }

    public static string? GetEventName(Type eventType)
    {
        foreach (var attribute in eventType.GetCustomAttributes(true))
        {
            if (attribute.GetType().Name == "EventNameAttribute"
                && attribute.GetType().GetProperty("Name")?.GetValue(attribute) is string name)
                return name;
        }

        return null;
    }

    // drift: every command routes through CommandManager.HandleCommandInternal(int, IntPtr); matched by declaring type + parameter shape, not method name.
    public static bool IsCommandDispatcher(Delegate handler)
    {
        if (handler.Method.DeclaringType?.Name != "CommandManager") return false;

        var parameters = handler.Method.GetParameters();
        return parameters.Length == 2
            && parameters[0].ParameterType == typeof(int)
            && parameters[1].ParameterType == typeof(IntPtr);
    }

    // drift: CommandManager._commandDefinitions holds the per-command callbacks (absent from the FunctionReference registry).
    public static IDictionary? GetCommandDefinitions(object commandManager) =>
        commandManager.GetType()
            .GetField("_commandDefinitions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(commandManager) as IDictionary;

    public static string ReadCommandName(IntPtr commandInfo)
    {
        try { return NativeAPI.CommandGetArgByIndex(commandInfo, 0); }
        catch { return "?"; }
    }

    public static string ResolveCommandPlugin(IDictionary? definitions, string name, Dictionary<Assembly, string> map)
    {
        if (definitions is null || definitions[name] is not IEnumerable matches) return "?";

        foreach (CommandDefinition definition in matches)
        {
            var assembly = definition.Callback?.Method.DeclaringType?.Assembly;
            if (assembly is null) continue;

            return map.TryGetValue(assembly, out var owner)
                ? owner
                : definition.Callback!.Method.DeclaringType?.Name ?? "?";
        }

        return "?";
    }

    private static string DisplayName(BasePlugin plugin)
    {
        try { return string.IsNullOrEmpty(plugin.ModuleName) ? plugin.GetType().Name : plugin.ModuleName; }
        catch { return plugin.GetType().Name; }
    }
}
