using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions.AssemblyFixture;

namespace Probe;

/// <summary>An assembly fixture: AssemblyFixtureFramework creates it once and hands it to every IAssemblyFixture user.</summary>
public sealed class SharedThing
{
    public static int Created;

    public Guid Id { get; } = Guid.NewGuid();

    public SharedThing() => Interlocked.Increment(ref Created);
}

public class AssemblyFixtureUserA(SharedThing thing, ITestOutputHelper output) : IAssemblyFixture<SharedThing>
{
    [Fact]
    public async Task Receives_the_assembly_fixture()
    {
        output.WriteLine($"shared-thing: {thing.Id} created={SharedThing.Created}");
        await ProbeHttp.GetAsync("/assemblyfixture/a", output);
        Assert.Equal(1, SharedThing.Created);
    }
}

public class AssemblyFixtureUserB(SharedThing thing, ITestOutputHelper output) : IAssemblyFixture<SharedThing>
{
    [Fact]
    public async Task Receives_the_assembly_fixture()
    {
        output.WriteLine($"shared-thing: {thing.Id} created={SharedThing.Created}");
        await ProbeHttp.GetAsync("/assemblyfixture/b", output);
        Assert.Equal(1, SharedThing.Created);
    }
}
