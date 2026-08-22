// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.Redis;
#endif

/// <summary>
/// Classifies Redis HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
public static class RedisOperationClassifier
{
    private static readonly HashSet<string> ReadOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "GETDEL", "GETSET", "GETEX", "MGET"
    };

    private static readonly HashSet<string> HashReadOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "HGET", "HMGET"
    };

    public static RedisOperationInfo Classify(string? commandName, bool hasResult, string? key = null, int db = 0)
    {
        if (string.IsNullOrEmpty(commandName))
            return new RedisOperationInfo(RedisOperation.Other, RedisCacheResult.None, key, db);

        var cmd = commandName.ToUpperInvariant();

        return cmd switch
        {
            // String read operations (support hit/miss)
            "GET" or "GETDEL" or "GETSET" or "GETEX" or "MGET"
                => new RedisOperationInfo(RedisOperation.Get, hasResult ? RedisCacheResult.Hit : RedisCacheResult.Miss, key, db),

            // String write operations
            "SET" or "SETEX" or "SETNX" or "PSETEX" or "MSET" or "MSETNX" or "APPEND" or "GETRANGE"
                => new RedisOperationInfo(RedisOperation.Set, RedisCacheResult.None, key, db),

            // Increment/Decrement
            "INCR" or "INCRBY" or "INCRBYFLOAT"
                => new RedisOperationInfo(RedisOperation.Increment, RedisCacheResult.None, key, db),
            "DECR" or "DECRBY"
                => new RedisOperationInfo(RedisOperation.Decrement, RedisCacheResult.None, key, db),

            // Key operations
            "DEL" or "UNLINK"
                => new RedisOperationInfo(RedisOperation.Delete, RedisCacheResult.None, key, db),
            "EXISTS"
                => new RedisOperationInfo(RedisOperation.KeyExists, RedisCacheResult.None, key, db),
            "EXPIRE" or "PEXPIRE" or "EXPIREAT" or "PEXPIREAT" or "PERSIST"
                => new RedisOperationInfo(RedisOperation.Expire, RedisCacheResult.None, key, db),

            // Hash read operations (support hit/miss)
            "HGET" or "HMGET"
                => new RedisOperationInfo(RedisOperation.HashGet, hasResult ? RedisCacheResult.Hit : RedisCacheResult.Miss, key, db),

            // Hash get all (always returns something, no hit/miss)
            "HGETALL"
                => new RedisOperationInfo(RedisOperation.HashGetAll, RedisCacheResult.None, key, db),

            // Hash write operations
            "HSET" or "HMSET" or "HSETNX"
                => new RedisOperationInfo(RedisOperation.HashSet, RedisCacheResult.None, key, db),
            "HDEL"
                => new RedisOperationInfo(RedisOperation.HashDelete, RedisCacheResult.None, key, db),

            // List operations
            "LPUSH" or "RPUSH" or "LPUSHX" or "RPUSHX"
                => new RedisOperationInfo(RedisOperation.ListPush, RedisCacheResult.None, key, db),
            "LRANGE"
                => new RedisOperationInfo(RedisOperation.ListRange, RedisCacheResult.None, key, db),

            // Set operations
            "SADD"
                => new RedisOperationInfo(RedisOperation.SetAdd, RedisCacheResult.None, key, db),
            "SMEMBERS"
                => new RedisOperationInfo(RedisOperation.SetMembers, RedisCacheResult.None, key, db),

            // Pub/Sub
            "PUBLISH"
                => new RedisOperationInfo(RedisOperation.Publish, RedisCacheResult.None, key, db),

            _ => new RedisOperationInfo(RedisOperation.Other, RedisCacheResult.None, key, db),
        };
    }

    public static string? GetDiagramLabel(RedisOperationInfo op, RedisTrackingVerbosity verbosity)
    {
        if (verbosity == RedisTrackingVerbosity.Raw)
            return null;

        var name = op.Operation.ToString();

        return op.CacheResult switch
        {
            RedisCacheResult.Hit => $"{name} (Hit)",
            RedisCacheResult.Miss => $"{name} (Miss)",
            _ => name,
        };
    }
}