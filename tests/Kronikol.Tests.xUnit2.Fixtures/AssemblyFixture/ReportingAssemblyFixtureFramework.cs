using System.Reflection;
using Kronikol.xUnit2;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions.AssemblyFixture;

[assembly: TestFramework("Kronikol.Tests.xUnit2.Fixtures.ReportingAssemblyFixtureFramework", "AssemblyFixture")]

namespace Kronikol.Tests.xUnit2.Fixtures;

/// <summary>What the wiki tells a suite on Xunit.Extensions.AssemblyFixture to write.</summary>
public sealed class ReportingAssemblyFixtureFramework(IMessageSink messageSink) : AssemblyFixtureFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        base.CreateExecutor(assemblyName).WithKronikolReporting();
}

/// <summary>An assembly fixture: the framework makes it once and hands it to every test that asks for it.</summary>
public sealed class SharedThing
{
    public static int Created;

    public Guid Id { get; } = Guid.NewGuid();

    public SharedThing() => Interlocked.Increment(ref Created);
}

[TestTracking]
public class AssemblyFixtureUsers(SharedThing thing, ITestOutputHelper output) : IAssemblyFixture<SharedThing>
{
    [Fact]
    public async Task First_user()
    {
        output.WriteLine($"shared-thing: {thing.Id} created={SharedThing.Created}");
        await FixtureHttp.GetAsync("/assemblyfixture/first", output);
        Assert.Equal(1, SharedThing.Created);
    }

    [Fact]
    public async Task Second_user()
    {
        output.WriteLine($"shared-thing: {thing.Id} created={SharedThing.Created}");
        await FixtureHttp.GetAsync("/assemblyfixture/second", output);
        Assert.Equal(1, SharedThing.Created);
    }
}
