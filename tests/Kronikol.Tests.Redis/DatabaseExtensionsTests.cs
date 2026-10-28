using StackExchange.Redis;
using Kronikol.Extensions.Redis;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.Redis;

public class DatabaseExtensionsTests
{
    [Fact]
    public void WithRedisTestTracking_Returns_IDatabase_Proxy()
    {
        var options = new RedisTrackingDatabaseOptions
        {
            CurrentTestInfoFetcher = () => ("Test", "id"),
        };
        var stub = StubDatabase.CreateProxy();

        var result = stub.WithRedisTestTracking(options);

        Assert.IsAssignableFrom<IDatabase>(result);
    }

    [Fact]
    public void Proxy_Database_Property_Forwards_To_Inner()
    {
        var options = new RedisTrackingDatabaseOptions
        {
            CurrentTestInfoFetcher = () => ("Test", "id"),
        };
        var stub = StubDatabase.CreateProxy(s => s.DatabaseNumber = 5);

        var result = stub.WithRedisTestTracking(options);

        Assert.Equal(5, result.Database);
    }
}

public class RedisTrackingDatabaseProxyTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private RedisTrackingDatabaseOptions MakeOptions(
        RedisTrackingVerbosity verbosity = RedisTrackingVerbosity.Detailed) => new()
    {
        ServiceName = "Redis",
        CallerName = "TestCaller",
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    public void Dispose() { }

    [Fact]
    public void Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard).
        var stub = StubDatabase.CreateProxy(s => s.NextStringGetResult = "John");
        var db = stub.WithRedisTestTracking(MakeOptions());
        db.StringGet("user:123");

        HistoryGuard.Pin(GetLogsFromThisTest(), 8,
            "TestCaller>Redis Get /db0/user:{n} OK");
    }

    [Fact]
    public void StringGet_Hit_Tracks_Request_And_Response_With_HitLabel()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextStringGetResult = "John");
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.StringGet("user:123");

        Assert.Equal("John", result.ToString());
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal("Get", logs[0].Method.Value?.ToString());
        Assert.Equal("Get (Hit)", logs[1].Method.Value?.ToString());
    }

    [Fact]
    public void StringGet_Miss_Tracks_With_MissLabel()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.StringGet("user:999");

        Assert.True(result.IsNull);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Get (Miss)", logs[1].Method.Value?.ToString());
    }

    [Fact]
    public async Task StringGetAsync_Hit_Tracks()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextStringGetResult = "Value");
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = await db.StringGetAsync("key");

        Assert.Equal("Value", result.ToString());
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Get (Hit)", logs[1].Method.Value?.ToString());
    }

    [Fact]
    public void StringSet_Tracks_Request_And_Response()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.StringSet("user:123", "John");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Set", logs[0].Method.Value?.ToString());
        Assert.Equal("John", logs[0].Content);
    }

    [Fact]
    public async Task StringSetAsync_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        await db.StringSetAsync("key", "value");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Set", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void KeyDelete_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.KeyDelete("user:123");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Delete", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void KeyExists_Tracks()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextKeyExistsResult = true);
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.KeyExists("user:123");

        Assert.True(result);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("KeyExists", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void HashGet_Hit_Tracks_With_HitLabel()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextHashGetResult = "John");
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.HashGet("user:123", "name");

        Assert.Equal("John", result.ToString());
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("HashGet (Hit)", logs[1].Method.Value?.ToString());
    }

    [Fact]
    public void HashGet_Miss_Tracks_With_MissLabel()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.HashGet("user:123", "missing");

        Assert.True(result.IsNull);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("HashGet (Miss)", logs[1].Method.Value?.ToString());
    }

    [Fact]
    public void HashSet_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.HashSet("user:123", "name", "John");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("HashSet", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void ListLeftPush_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.ListLeftPush("queue", "message");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("ListPush", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void SetAdd_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.SetAdd("tags", "redis");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("SetAdd", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void KeyExpire_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.KeyExpire("key", TimeSpan.FromMinutes(5));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Expire", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void StringIncrement_Tracks()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextIncrementResult = 42);
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.StringIncrement("counter");

        Assert.Equal(42, result);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Increment", logs[0].Method.Value?.ToString());
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_number_the_server_answers_reads_the_same_on_every_machine(string culture)
    {
        // Until 4.16.3 a double read -1,5 on a German machine and a negative count took a Swedish minus sign.
        var stub = StubDatabase.CreateProxy(s => { s.NextDoubleResult = -1.5; s.NextIncrementResult = -3; });
        var db = stub.WithRedisTestTracking(MakeOptions());
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
        try
        {
            db.StringIncrement("price", 0.5);
            db.StringDecrement("stock");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(["-1.5", "-3"], GetLogsFromThisTest().Where(l => l.Type == RequestResponseType.Response).Select(l => l.Content));
    }

    [Fact]
    public void Untracked_Method_Does_Not_Log()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.KeyTimeToLive("key");

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void Detailed_Uri_Contains_Key()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextStringGetResult = "value");
        var db = stub.WithRedisTestTracking(MakeOptions(RedisTrackingVerbosity.Detailed));

        db.StringGet("user:123");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("redis://db0/user:123", log.Uri.ToString());
    }

    [Fact]
    public void Summarised_Uri_Has_DbOnly()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextStringGetResult = "value");
        var db = stub.WithRedisTestTracking(MakeOptions(RedisTrackingVerbosity.Summarised));

        db.StringGet("user:123");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("redis://db0/", log.Uri.ToString());
    }

    [Fact]
    public void Response_Has_OK_StatusCode()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextStringGetResult = "value");
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.StringGet("key");

        var response = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal("OK", response.StatusCode?.Value?.ToString());
    }

    [Fact]
    public void HashDelete_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.HashDelete("user:123", "name");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("HashDelete", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void Summarised_OmitsContent()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions(RedisTrackingVerbosity.Summarised));

        db.StringSet("key", "value");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void ListRightPush_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        db.ListRightPush("queue", "msg");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("ListPush", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public void StringDecrement_Tracks()
    {
        var stub = StubDatabase.CreateProxy(s => s.NextIncrementResult = 10);
        var db = stub.WithRedisTestTracking(MakeOptions());

        var result = db.StringDecrement("counter");

        Assert.Equal(10, result);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Decrement", logs[0].Method.Value?.ToString());
    }

    [Fact]
    public async Task KeyDeleteAsync_Tracks()
    {
        var stub = StubDatabase.CreateProxy();
        var db = stub.WithRedisTestTracking(MakeOptions());

        await db.KeyDeleteAsync("key");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Delete", logs[0].Method.Value?.ToString());
    }
}
