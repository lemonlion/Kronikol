using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.EfCore.Relational;

public class SqlTrackingInterceptorOptionsExtensionsTests
{
    [Fact]
    public void WithTestInfoFrom_CopiesCurrentTestInfoFetcher()
    {
        Func<(string Name, string Id)> fetcher = () => ("Test1", "id-1");
        var httpOptions = new TestTrackingMessageHandlerOptions { CurrentTestInfoFetcher = fetcher };
        var sqlOptions = new SqlTrackingInterceptorOptions();

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Same(fetcher, sqlOptions.CurrentTestInfoFetcher);
    }

    [Fact]
    public void WithTestInfoFrom_CopiesCurrentStepTypeFetcher()
    {
        Func<string?> fetcher = () => "Given";
        var httpOptions = new TestTrackingMessageHandlerOptions { CurrentStepTypeFetcher = fetcher };
        var sqlOptions = new SqlTrackingInterceptorOptions();

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Same(fetcher, sqlOptions.CurrentStepTypeFetcher);
    }

    [Fact]
    public void WithTestInfoFrom_CopiesCallerName()
    {
        var httpOptions = new TestTrackingMessageHandlerOptions { CallerName = "My API" };
        var sqlOptions = new SqlTrackingInterceptorOptions();

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Equal("My API", sqlOptions.CallerName);
    }

    [Fact]
    public void WithTestInfoFrom_ReturnsSameInstance()
    {
        var httpOptions = new TestTrackingMessageHandlerOptions();
        var sqlOptions = new SqlTrackingInterceptorOptions();

        var result = sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Same(sqlOptions, result);
    }

    [Fact]
    public void WithTestInfoFrom_HandlesNullFetchers()
    {
        var httpOptions = new TestTrackingMessageHandlerOptions
        {
            CurrentTestInfoFetcher = null,
            CurrentStepTypeFetcher = null
        };
        var sqlOptions = new SqlTrackingInterceptorOptions();

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Null(sqlOptions.CurrentTestInfoFetcher);
        Assert.Null(sqlOptions.CurrentStepTypeFetcher);
    }

    [Fact]
    public void WithTestInfoFrom_PreservesExistingServiceName()
    {
        var httpOptions = new TestTrackingMessageHandlerOptions();
        var sqlOptions = new SqlTrackingInterceptorOptions { ServiceName = "Identity Database" };

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Equal("Identity Database", sqlOptions.ServiceName);
    }

    [Fact]
    public void WithTestInfoFrom_PreservesExistingVerbosity()
    {
        var httpOptions = new TestTrackingMessageHandlerOptions();
        var sqlOptions = new SqlTrackingInterceptorOptions { Verbosity = SqlTrackingVerbosity.Summarised };

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Equal(SqlTrackingVerbosity.Summarised, sqlOptions.Verbosity);
    }

    [Fact]
    public void WithTestInfoFrom_FetcherDelegatesAreCallable()
    {
        var httpOptions = new TestTrackingMessageHandlerOptions
        {
            CurrentTestInfoFetcher = () => ("My Test", "abc-123"),
            CurrentStepTypeFetcher = () => "When"
        };
        var sqlOptions = new SqlTrackingInterceptorOptions();

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Equal(("My Test", "abc-123"), sqlOptions.CurrentTestInfoFetcher!());
        Assert.Equal("When", sqlOptions.CurrentStepTypeFetcher!());
    }

    [Fact]
    public void WithTestInfoFrom_CopiesHttpContextAccessor_when_the_sql_options_carry_none()
    {
        var accessor = RequestHeaderAccessor.For("Header Test", "header-id");
        var httpOptions = new TestTrackingMessageHandlerOptions { HttpContextAccessor = accessor };
        var sqlOptions = new SqlTrackingInterceptorOptions();

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Same(accessor, sqlOptions.HttpContextAccessor);
    }

    [Fact]
    public void WithTestInfoFrom_keeps_the_sql_options_own_HttpContextAccessor()
    {
        var own = RequestHeaderAccessor.For("Own Test", "own-id");
        var httpOptions = new TestTrackingMessageHandlerOptions { HttpContextAccessor = RequestHeaderAccessor.For("Http Test", "http-id") };
        var sqlOptions = new SqlTrackingInterceptorOptions { HttpContextAccessor = own };

        sqlOptions.WithTestInfoFrom(httpOptions);

        Assert.Same(own, sqlOptions.HttpContextAccessor);
    }
}
