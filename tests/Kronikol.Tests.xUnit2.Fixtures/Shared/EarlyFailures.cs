using Xunit;
using Xunit.Abstractions;

namespace Kronikol.Tests.xUnit2.Fixtures;

// Tests that fail before their first line runs: xUnit sends their ITestStarting and a failure, and never calls
// a BeforeAfterTestAttribute's Before.

public partial class CtorThrows
{
    private readonly ITestOutputHelper _output;

    public CtorThrows(ITestOutputHelper output)
    {
        _output = output;
        throw new InvalidOperationException("the test class constructor throws on purpose");
    }

    [Fact]
    public Task Never_runs() => FixtureHttp.GetAsync("/ctor/never", _output);
}

public partial class InitThrows(ITestOutputHelper output) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("InitializeAsync throws on purpose");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public Task Never_runs() => FixtureHttp.GetAsync("/init/never", output);
}

public sealed class ThrowingFixture
{
    public ThrowingFixture() => throw new InvalidOperationException("the class fixture constructor throws on purpose");
}

public partial class FixtureThrows(ThrowingFixture fixture, ITestOutputHelper output) : IClassFixture<ThrowingFixture>
{
    [Fact]
    public Task Never_runs()
    {
        _ = fixture;
        return FixtureHttp.GetAsync("/fixture/never", output);
    }
}

/// <summary>Nothing provides one, so xUnit cannot build the test class.</summary>
public sealed class NotProvided;

public partial class UnresolvableArgument(NotProvided notProvided, ITestOutputHelper output)
{
    [Fact]
    public Task Never_runs()
    {
        _ = notProvided;
        return FixtureHttp.GetAsync("/unresolvable/never", output);
    }
}

/// <summary>
/// A call in the test class's constructor runs before TestTrackingAttribute.Before opens the test's identity
/// window, so it is not one of the scenario's calls (T21, the plan's Q6).
/// </summary>
public partial class ConstructorCall
{
    private readonly ITestOutputHelper _output;

    public ConstructorCall(ITestOutputHelper output)
    {
        _output = output;
        FixtureHttp.GetAsync("/constructor-call/from-constructor").GetAwaiter().GetResult();
    }

    [Fact]
    public Task Calls_from_the_test() => FixtureHttp.GetAsync("/constructor-call/from-test", _output);
}

/// <summary>TestTrackingAttribute never applies here, so none of these is a scenario.</summary>
public partial class Untracked(ITestOutputHelper output)
{
    [Fact]
    public Task Passes_untracked() => FixtureHttp.GetAsync("/untracked/passes", output);

    [Fact]
    public async Task Fails_untracked()
    {
        await FixtureHttp.GetAsync("/untracked/fails", output);
        Assert.Equal(1, 2);
    }

    [Fact(Skip = "untracked and skipped on purpose")]
    public Task Skipped_untracked() => FixtureHttp.GetAsync("/untracked/skipped", output);
}
