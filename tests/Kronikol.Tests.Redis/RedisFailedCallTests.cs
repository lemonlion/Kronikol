using System.Net;
using System.Reflection;
using Kronikol.Extensions.Redis;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using StackExchange.Redis;

namespace Kronikol.Tests.Redis;

/// <summary>
/// A Redis command that throws (plans/FAILED_CALLS_PLAN.md F4, F16, §5.7): on every path, synchronous, asynchronous, and an
/// asynchronous command that throws before it has a task, the call is answered with the exception's type and why, and the caller
/// gets the exception the client threw, not a <see cref="TargetInvocationException"/> around it.
/// </summary>
public class RedisFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private RedisTrackingDatabaseOptions Options() => new()
    {
        ServiceName = "Redis",
        CallerName = "Caller",
        CurrentTestInfoFetcher = () => ("Carts are cached", _testId),
    };

    private static RedisConnectionException Unreachable() =>
        new(ConnectionFailureType.UnableToConnect, "It was not possible to connect to the redis server(s).", new IOException("Connection refused"));

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_command_that_throws_is_answered_and_the_caller_gets_what_the_client_threw()
    {
        var thrown = Unreachable();
        var db = FailingDatabase.Create(thrown, FailingDatabase.Way.Synchronously).WithRedisTestTracking(Options());

        var caught = Assert.Throws<RedisConnectionException>(() => db.StringGet("user:123"));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task An_async_command_whose_task_faults_is_answered()
    {
        var thrown = Unreachable();
        var db = FailingDatabase.Create(thrown, FailingDatabase.Way.InItsTask).WithRedisTestTracking(Options());

        var caught = await Assert.ThrowsAsync<RedisConnectionException>(() => db.StringGetAsync("user:123"));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void An_async_command_that_throws_before_it_has_a_task_is_answered_and_the_caller_gets_what_the_client_threw()
    {
        var thrown = Unreachable();
        var db = FailingDatabase.Create(thrown, FailingDatabase.Way.BeforeItsTask).WithRedisTestTracking(Options());

        var caught = Assert.Throws<RedisConnectionException>(() => { _ = db.StringSetAsync("user:123", "John"); });

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task An_async_command_with_no_result_whose_task_faults_is_answered()
    {
        var thrown = Unreachable();
        var db = FailingDatabase.Create(thrown, FailingDatabase.Way.InItsTask).WithRedisTestTracking(Options());

        var caught = await Assert.ThrowsAsync<RedisConnectionException>(() => db.HashSetAsync("cart:1", [new HashEntry("sku", 2)]));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public void A_member_the_tracker_does_not_track_hands_on_what_the_client_threw()
    {
        var thrown = Unreachable();
        var db = FailingDatabase.Create(thrown, FailingDatabase.Way.Synchronously).WithRedisTestTracking(Options());

        var caught = Assert.Throws<RedisConnectionException>(() => db.KeyMigrate("user:123", new DnsEndPoint("other", 6379)));

        FailedCallContract.AssertUntouched(caught, thrown);
        Assert.Empty(Logs());
    }

    [Fact]
    public void History_rules_before_12_read_the_answer_as_none_as_they_recorded_none()
    {
        var db = FailingDatabase.Create(Unreachable(), FailingDatabase.Way.Synchronously).WithRedisTestTracking(Options());

        Assert.Throws<RedisConnectionException>(() => db.StringGet("user:123"));

        Assert.Equal("!RedisConnectionException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public async Task A_command_that_succeeds_is_answered_as_before()
    {
        var db = StubDatabase.CreateProxy(s => s.NextStringGetResult = "John").WithRedisTestTracking(Options());

        Assert.Equal("John", (string?)await db.StringGetAsync("user:123"));

        var answer = Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal("OK", answer.StatusCode?.Value);
        Assert.Null(answer.Error);
    }

    /// <summary>An <see cref="IDatabase"/> whose every command throws, the way a client that cannot reach its server does.</summary>
    public class FailingDatabase : DispatchProxy
    {
        public enum Way { Synchronously, InItsTask, BeforeItsTask }

        private Exception _exception = null!;
        private Way _way;

        public static IDatabase Create(Exception exception, Way way)
        {
            var proxy = Create<IDatabase, FailingDatabase>();
            var failing = (FailingDatabase)(object)proxy;
            failing._exception = exception;
            failing._way = way;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name == "get_Database") return 0;
            if (name == "get_Multiplexer") return null;

            var returnType = targetMethod.ReturnType;
            var isTask = typeof(Task).IsAssignableFrom(returnType);
            if (!isTask || _way != Way.InItsTask)
                FailedCallContract.Throw(_exception);
            return returnType == typeof(Task) ? Faulted() : FaultedOf(returnType.GetGenericArguments()[0]);
        }

        private async Task Faulted()
        {
            await Task.Yield();
            FailedCallContract.Throw(_exception);
        }

        private object FaultedOf(Type result) =>
            typeof(FailingDatabase).GetMethod(nameof(FaultedAs), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(result).Invoke(this, null)!;

        private async Task<T> FaultedAs<T>()
        {
            await Task.Yield();
            FailedCallContract.Throw(_exception);
            return default!;
        }
    }
}
