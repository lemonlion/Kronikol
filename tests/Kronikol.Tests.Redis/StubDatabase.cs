using System.Reflection;
using StackExchange.Redis;

namespace Kronikol.Tests.Redis;

public class StubDatabase : DispatchProxy
{
    public int DatabaseNumber { get; set; } = 0;
    public RedisValue NextStringGetResult { get; set; } = RedisValue.Null;
    public RedisValue NextHashGetResult { get; set; } = RedisValue.Null;
    public bool NextKeyExistsResult { get; set; } = false;
    public long NextIncrementResult { get; set; } = 1;
    public double? NextDoubleResult { get; set; }
    public bool NextBoolResult { get; set; } = true;

    public static IDatabase CreateProxy(Action<StubDatabase>? configure = null)
    {
        var proxy = Create<IDatabase, StubDatabase>();
        if (configure is not null)
            configure((StubDatabase)(object)proxy);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        var name = targetMethod.Name;

        // Properties
        if (name == "get_Database") return DatabaseNumber;
        if (name == "get_Multiplexer") return null;

        // Return type-based default responses
        var returnType = targetMethod.ReturnType;

        // Handle async versions by wrapping sync results
        if (returnType == typeof(Task<RedisValue>))
        {
            if (name.Contains("Hash")) return Task.FromResult(NextHashGetResult);
            return Task.FromResult(NextStringGetResult);
        }
        if (returnType == typeof(Task<RedisValue[]>))
            return Task.FromResult(new[] { NextStringGetResult });
        if (returnType == typeof(Task<bool>))
        {
            if (name.Contains("Exists")) return Task.FromResult(NextKeyExistsResult);
            return Task.FromResult(NextBoolResult);
        }
        if (returnType == typeof(Task<long>))
            return Task.FromResult(NextIncrementResult);
        if (returnType == typeof(Task<double>))
            return Task.FromResult(NextDoubleResult ?? NextIncrementResult);
        if (returnType == typeof(Task<HashEntry[]>))
            return Task.FromResult(Array.Empty<HashEntry>());
        if (returnType == typeof(Task<RedisValueWithExpiry>))
            return Task.FromResult(new RedisValueWithExpiry(NextStringGetResult, null));
        if (returnType == typeof(Task))
            return Task.CompletedTask;

        // Sync versions
        if (returnType == typeof(RedisValue))
        {
            if (name.Contains("Hash")) return NextHashGetResult;
            return NextStringGetResult;
        }
        if (returnType == typeof(RedisValue[]))
            return new[] { NextStringGetResult };
        if (returnType == typeof(bool))
        {
            if (name.Contains("Exists")) return NextKeyExistsResult;
            return NextBoolResult;
        }
        if (returnType == typeof(long))
            return NextIncrementResult;
        if (returnType == typeof(double))
            return NextDoubleResult ?? NextIncrementResult;
        if (returnType == typeof(HashEntry[]))
            return Array.Empty<HashEntry>();
        if (returnType == typeof(RedisValueWithExpiry))
            return new RedisValueWithExpiry(NextStringGetResult, null);
        if (returnType == typeof(TimeSpan?))
            return (TimeSpan?)TimeSpan.FromMinutes(5);
        if (returnType == typeof(Task<TimeSpan?>))
            return Task.FromResult((TimeSpan?)TimeSpan.FromMinutes(5));
        if (returnType == typeof(void))
            return null;

        // Nullable and other reference types
        if (!returnType.IsValueType)
            return null;

        // Default for value types
        return Activator.CreateInstance(returnType);
    }
}
