using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TrackingProxyTests
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private const string TestName = "TrackingProxyTest";
    private readonly string _sourceName = $"ProxyTest.{Guid.NewGuid():N}";

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    public void Proxied_method_call_logs_request_and_response()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        var result = proxy.Add(2, 3);

        Assert.Equal(5, result);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal("Calculator", logs[0].ServiceName);
    }

    [Fact]
    public void Proxy_builds_correct_uri()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            UriScheme = "mock",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(1, 1);

        var logs = GetLogsForTest();
        Assert.Contains("mock://calculator/icalculator/add", logs[0].Uri.ToString().ToLower());
    }

    [Fact]
    public void Proxy_serializes_arguments_as_request_content()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(7, 8);

        var logs = GetLogsForTest();
        Assert.Contains("7", logs[0].Content!);
        Assert.Contains("8", logs[0].Content!);
    }

    [Fact]
    public void Proxy_serializes_return_value_as_response_content()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(7, 8);

        var logs = GetLogsForTest();
        Assert.Equal("15", logs[1].Content);
    }

    [Fact]
    public async Task Async_method_is_tracked()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        var result = await proxy.MultiplyAsync(3, 4);

        Assert.Equal(12, result);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Contains("12", logs[1].Content!);
    }

    [Fact]
    public void Exception_is_rethrown_and_logged()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        Assert.Throws<DivideByZeroException>(() => proxy.Divide(1, 0));

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Contains("DivideByZero", logs[1].Content!);
    }

    [Fact]
    public void Deferred_mode_enqueues_pending_logs()
    {
        PendingRequestResponseLogs.Clear();
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            LogMode = TrackingLogMode.Deferred
        });

        var countBefore = PendingRequestResponseLogs.Count;
        proxy.Add(1, 2);
        Assert.Equal(countBefore + 1, PendingRequestResponseLogs.Count);

        // Flush and verify
        PendingRequestResponseLogs.FlushAll(TestName, _testId);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void Deferred_mode_preserves_dependency_category()
    {
        PendingRequestResponseLogs.Clear();
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "SmtpRelay",
            DependencyCategory = "Email",
            LogMode = TrackingLogMode.Deferred
        });

        proxy.Add(1, 2);
        PendingRequestResponseLogs.FlushAll(TestName, _testId);

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Email", l.DependencyCategory));
    }

    [Fact]
    public void Proxy_creates_activity_when_source_configured()
    {
        var mock = new FakeCalculator();
        using var source = new ActivitySource(_sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            ActivitySourceName = _sourceName,
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(1, 1);

        var spans = InternalFlowSpanStore.GetSpans()
            .Where(s => s.Source.Name == _sourceName)
            .ToArray();
        Assert.NotEmpty(spans);
        Assert.Contains(spans, s => s.DisplayName == "Add");
    }

    [Fact]
    public void Void_method_is_tracked()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Reset();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Theory]
    [InlineData("Cosmos DB")]
    [InlineData("OTP Service")]
    [InlineData("Fraud Check Service")]
    [InlineData("Enhanced KYC API")]
    [InlineData("Auth & Token")]
    [InlineData("Cache (Redis)")]
    public void Service_name_with_spaces_or_special_chars_does_not_throw(string serviceName)
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = serviceName,
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        var result = proxy.Add(1, 2);

        Assert.Equal(3, result);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void Service_name_with_spaces_preserves_original_name_in_logs()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Cosmos DB",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(1, 2);

        var logs = GetLogsForTest();
        Assert.All(logs, log => Assert.Equal("Cosmos DB", log.ServiceName));
    }

    [Fact]
    public void Service_name_with_spaces_builds_valid_uri()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Cosmos DB",
            UriScheme = "mock",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(1, 2);

        var logs = GetLogsForTest();
        var uri = logs[0].Uri;
        Assert.Contains("mock://", uri.ToString());
        Assert.Contains("/ICalculator/Add", uri.AbsolutePath);
    }

    [Fact]
    public void Proxy_sets_dependency_category_on_logged_entries()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "SmtpServer",
            DependencyCategory = "Email",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(1, 2);

        var logs = GetLogsForTest();
        Assert.All(logs, l => Assert.Equal("Email", l.DependencyCategory));
    }

    [Fact]
    public void Proxy_dependency_category_defaults_to_null()
    {
        var mock = new FakeCalculator();
        var proxy = TrackingProxy<ICalculator>.Create(mock, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId)
        });

        proxy.Add(1, 2);

        var logs = GetLogsForTest();
        Assert.All(logs, l => Assert.Null(l.DependencyCategory));
    }

    public interface ICalculator
    {
        int Add(int a, int b);
        Task<int> MultiplyAsync(int a, int b);
        int Divide(int a, int b);
        void Reset();
    }

    private class FakeCalculator : ICalculator
    {
        public int Add(int a, int b) => a + b;
        public Task<int> MultiplyAsync(int a, int b) => Task.FromResult(a * b);
        public int Divide(int a, int b) => a / b;
        public void Reset() { }
    }
}
