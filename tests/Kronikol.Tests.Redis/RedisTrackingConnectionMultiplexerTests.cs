using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Kronikol.Extensions.Redis;

namespace Kronikol.Tests.Redis;

public class RedisTrackingConnectionMultiplexerTests
{
    [Fact]
    public void Create_returns_proxy_wrapping_inner()
    {
        var inner = StubConnectionMultiplexer.CreateProxy();
        var options = new RedisTrackingDatabaseOptions { ServiceName = "TestRedis" };

        var proxy = RedisTrackingConnectionMultiplexer.Create(inner, options);

        Assert.NotNull(proxy);
        Assert.IsAssignableFrom<IConnectionMultiplexer>(proxy);
    }

    [Fact]
    public void GetDatabase_returns_tracked_database()
    {
        var inner = StubConnectionMultiplexer.CreateProxy();
        var options = new RedisTrackingDatabaseOptions { ServiceName = "TestRedis" };

        var proxy = RedisTrackingConnectionMultiplexer.Create(inner, options);
        var db = proxy.GetDatabase();

        Assert.IsAssignableFrom<RedisTrackingDatabase>((object)db);
    }

    [Fact]
    public void AddRedisConnectionMultiplexerTracking_decorates_registered_multiplexer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(StubConnectionMultiplexer.CreateProxy());

        services.AddRedisConnectionMultiplexerTracking(options =>
        {
            options.ServiceName = "TestRedis";
        });

        var provider = services.BuildServiceProvider();
        var mux = provider.GetRequiredService<IConnectionMultiplexer>();
        var db = mux.GetDatabase();

        Assert.IsAssignableFrom<RedisTrackingDatabase>((object)db);
    }

    [Fact]
    public void AddRedisConnectionMultiplexerTracking_noop_when_no_registrations()
    {
        var services = new ServiceCollection();
        services.AddRedisConnectionMultiplexerTracking();

        // Should not throw
        var provider = services.BuildServiceProvider();
        var mux = provider.GetService<IConnectionMultiplexer>();
        Assert.Null(mux);
    }
}

public class StubConnectionMultiplexer : DispatchProxy
{
    private System.Net.EndPoint[] _endPoints = [];

    public static IConnectionMultiplexer CreateProxy(params System.Net.EndPoint[] endPoints)
    {
        var proxy = Create<IConnectionMultiplexer, StubConnectionMultiplexer>();
        ((StubConnectionMultiplexer)(object)proxy)._endPoints = endPoints;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        if (targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase))
        {
            return StubDatabase.CreateProxy();
        }

        if (targetMethod.Name == "get_Configuration")
            return "localhost:6379";

        if (targetMethod.Name == "GetEndPoints")
            return _endPoints;

        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(void)) return null;
        if (returnType.IsValueType) return Activator.CreateInstance(returnType);
        return null;
    }
}
