using System.Globalization;
using System.Reflection;
using StackExchange.Redis;

namespace Kronikol.Extensions.Redis;

/// <summary>
/// Tracking decorator for Redis <c>IDatabase</c> that logs operations for test diagrams.
/// </summary>
public class RedisTrackingDatabase : DispatchProxy
{
    private IDatabase _inner = null!;
    private RedisTracker _tracker = null!;

    public static IDatabase Create(IDatabase inner, RedisTrackingDatabaseOptions options)
    {
        var proxy = Create<IDatabase, RedisTrackingDatabase>();
        var trackingDb = (RedisTrackingDatabase)(object)proxy;
        trackingDb._inner = inner;
        var endpoint = inner.Multiplexer?.GetEndPoints()?.FirstOrDefault();
        var name = EndpointName(endpoint);
        var before = endpoint?.ToString() ?? "localhost";
        trackingDb._tracker = new RedisTracker(options, name, options.HttpContextAccessor)
        {
            EndpointBefore8 = string.Equals(before, name, StringComparison.Ordinal) ? null : before
        };
        return proxy;
    }

    /// <summary>
    /// The server a connection reached, as <c>host:port</c>. <see cref="DnsEndPoint.ToString"/> puts the address family
    /// first (<c>Unspecified/localhost:6379</c>), which until #192 R1 stood where the host goes in every Raw address.
    /// </summary>
    internal static string EndpointName(System.Net.EndPoint? endpoint) => endpoint switch
    {
        null => "localhost",
        System.Net.DnsEndPoint dns => dns.Port == 0 ? dns.Host : $"{dns.Host}:{dns.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        var other => other.ToString() ?? "localhost"
    };

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        var (command, key) = MapMethodToCommand(targetMethod, args);

        if (command is null)
            return targetMethod.Invoke(_inner, args);

        var returnType = targetMethod.ReturnType;

        // Async methods returning Task<T>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return InvokeAsyncGeneric(targetMethod, args, command, key, returnType);
        }

        // Async methods returning Task (void)
        if (returnType == typeof(Task))
        {
            return InvokeAsyncVoid(targetMethod, args, command, key);
        }

        // Synchronous methods
        return InvokeSync(targetMethod, args, command, key);
    }

    private object? InvokeSync(MethodInfo targetMethod, object?[]? args, string command, string? key)
    {
        var content = GetRequestContent(targetMethod, args);
        var (reqId, traceId) = _tracker.LogRedisRequest(command, key, _inner.Database, content);

        var result = targetMethod.Invoke(_inner, args);

        var hasResult = DetermineHasResult(result);
        var responseContent = GetResponseContent(result);
        _tracker.LogRedisResponse(command, key, _inner.Database, hasResult, reqId, traceId, responseContent);

        return result;
    }

    private object InvokeAsyncGeneric(MethodInfo targetMethod, object?[]? args, string command, string? key, Type returnType)
    {
        var content = GetRequestContent(targetMethod, args);
        var (reqId, traceId) = _tracker.LogRedisRequest(command, key, _inner.Database, content);

        var task = targetMethod.Invoke(_inner, args)!;
        var innerType = returnType.GetGenericArguments()[0];

        // Use reflection to call TrackAsyncResult<T>
        var method = typeof(RedisTrackingDatabase)
            .GetMethod(nameof(TrackAsyncResult), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(innerType);

        return method.Invoke(this, [task, command, key, reqId, traceId])!;
    }

    private async Task<T> TrackAsyncResult<T>(object task, string command, string? key, Guid reqId, Guid traceId)
    {
        var result = await ((Task<T>)task);

        var hasResult = DetermineHasResult(result);
        var responseContent = GetResponseContent(result);
        _tracker.LogRedisResponse(command, key, _inner.Database, hasResult, reqId, traceId, responseContent);

        return result;
    }

    private async Task InvokeAsyncVoid(MethodInfo targetMethod, object?[]? args, string command, string? key)
    {
        var content = GetRequestContent(targetMethod, args);
        var (reqId, traceId) = _tracker.LogRedisRequest(command, key, _inner.Database, content);

        await (Task)targetMethod.Invoke(_inner, args)!;

        _tracker.LogRedisResponse(command, key, _inner.Database, false, reqId, traceId, null);
    }

    // ─── Method-to-command mapping ─────────────────────────────

    private static (string? Command, string? Key) MapMethodToCommand(MethodInfo method, object?[]? args)
    {
        var name = method.Name.Replace("Async", "");
        var key = ExtractKey(args);

        return name switch
        {
            // String operations
            "StringGet" or "StringGetWithExpiry" or "StringGetLease" or "StringGetSetExpiry" or "StringGetRange"
                => ("GET", key),
            "StringGetDelete" => ("GETDEL", key),
            "StringGetSet" => ("GETSET", key),
            "StringSet" or "StringSetAndGet" or "StringSetRange" or "StringAppend"
                => ("SET", key),
            "StringIncrement" => ("INCR", key),
            "StringDecrement" => ("DECR", key),
            "StringDelete" => ("DEL", key),

            // Key operations
            "KeyDelete" => ("DEL", key),
            "KeyExists" => ("EXISTS", key),
            "KeyExpire" => ("EXPIRE", key),
            "KeyPersist" => ("PERSIST", key),

            // Hash operations
            "HashGet" or "HashGetLease" => ("HGET", key),
            "HashGetAll" => ("HGETALL", key),
            "HashSet" => ("HSET", key),
            "HashDelete" => ("HDEL", key),
            "HashDecrement" => ("DECR", key),
            "HashIncrement" => ("INCR", key),
            "HashExists" => ("EXISTS", key),

            // List operations
            "ListLeftPush" => ("LPUSH", key),
            "ListRightPush" => ("RPUSH", key),
            "ListLeftPop" => ("LPOP", key),
            "ListRightPop" => ("RPOP", key),
            "ListRange" => ("LRANGE", key),
            "ListRightPopLeftPush" or "ListMove" => ("RPOPLPUSH", key),

            // Set operations
            "SetAdd" => ("SADD", key),
            "SetMembers" => ("SMEMBERS", key),
            "SetRemove" => ("SREM", key),
            "SetPop" => ("SPOP", key),
            "SetContains" => ("SISMEMBER", key),

            // Pub/Sub
            "Publish" => ("PUBLISH", ExtractChannel(args)),

            // Everything else is not tracked
            _ => (null, null),
        };
    }

    private static string? ExtractKey(object?[]? args)
    {
        if (args is null || args.Length == 0) return null;

        return args[0] switch
        {
            RedisKey key => key!,
            RedisKey[] keys => string.Join(",", keys.Select(k => (string)k!)),
            _ => null
        };
    }

    private static string? ExtractChannel(object?[]? args)
    {
        if (args is null || args.Length == 0) return null;
        return args[0] is RedisChannel channel ? channel.ToString() : null;
    }

    private static bool DetermineHasResult(object? result)
    {
        return result switch
        {
            RedisValue rv => !rv.IsNull,
            RedisValue[] arr => arr.Any(r => !r.IsNull),
            Lease<byte> lease => lease is not null && lease.Length > 0,
            RedisValueWithExpiry rve => !rve.Value.IsNull,
            HashEntry[] entries => entries.Length > 0,
            bool b => b,
            null => false,
            _ => true,
        };
    }

    private static string? GetRequestContent(MethodInfo method, object?[]? args)
    {
        if (args is null || args.Length < 2) return null;

        var name = method.Name.Replace("Async", "");

        // For Set operations, the value is typically the second argument
        if (name is "StringSet" or "StringSetAndGet" or "StringAppend")
        {
            return args.Length >= 2 && args[1] is RedisValue val && !val.IsNull ? val.ToString() : null;
        }

        if (name is "HashSet" && args.Length >= 2)
        {
            if (args[1] is RedisValue field && args.Length >= 3 && args[2] is RedisValue value)
                return $"{field}={value}";
        }

        if (name is "Publish" && args.Length >= 2 && args[1] is RedisValue msg)
            return msg.IsNull ? null : msg.ToString();

        return null;
    }

    private static string? GetResponseContent(object? result)
    {
        return result switch
        {
            RedisValue rv when !rv.IsNull => rv.ToString(),
            RedisValueWithExpiry rve when !rve.Value.IsNull => rve.Value.ToString(),
            Lease<byte> lease when lease is not null && lease.Length > 0 => $"{lease.Length} bytes",
            // Invariant: a double read 1,5 on a German machine and a negative long took the culture's minus sign (until 4.16.3).
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            bool b => b.ToString(),
            _ => null,
        };
    }
}
