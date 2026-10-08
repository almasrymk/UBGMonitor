using System.Reflection;

namespace MonitorAgent.Tests;

// A deterministic, offline provider for HTTP contract tests. Never starts sensors or network probes.
public class OfflineTelemetry : DispatchProxy
{
    public static T Create<T>() where T : class => DispatchProxy.Create<T, OfflineTelemetry>();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Value(method!.ReturnType);
    private static object? Value(Type type)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(string)) return "fixture";
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var result = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result).Invoke(null, [Value(result)]);
        }
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && type.IsInterface && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
        if (type.IsValueType) return Activator.CreateInstance(type);
        var constructor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
        return constructor.Invoke(constructor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : Value(p.ParameterType)).ToArray());
    }
}
