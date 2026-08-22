using Kronikol.Tracking;

namespace Kronikol.Tests;

public class TrackingComponentRegistryFixture : IDisposable
{
    public TrackingComponentRegistryFixture() => TrackingComponentRegistry.Clear();
    public void Dispose() => TrackingComponentRegistry.Clear();
}

[Collection("DiagramsFetcher")]
public class TrackingComponentRegistryTests : IDisposable
{
    public TrackingComponentRegistryTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    [Fact]
    public void Register_AddsComponentToRegistry()
    {
        var component = new StubTrackingComponent("Test_Register", wasInvoked: false);

        TrackingComponentRegistry.Register(component);

        Assert.Contains(TrackingComponentRegistry.GetRegisteredComponents(), c => c.ComponentName == "Test_Register");
    }

    [Fact]
    public void GetUnusedComponents_ReturnsUninvokedComponents()
    {
        var unused = new StubTrackingComponent("Unused", wasInvoked: false);
        var used = new StubTrackingComponent("Used", wasInvoked: true);

        TrackingComponentRegistry.Register(unused);
        TrackingComponentRegistry.Register(used);

        var result = TrackingComponentRegistry.GetUnusedComponents();
        Assert.Contains(result, c => c.ComponentName == "Unused");
    }

    [Fact]
    public void GetUnusedComponents_ReturnsEmpty_WhenAllInvoked()
    {
        TrackingComponentRegistry.Register(new StubTrackingComponent("A", wasInvoked: true));
        TrackingComponentRegistry.Register(new StubTrackingComponent("B", wasInvoked: true));

        Assert.Empty(TrackingComponentRegistry.GetUnusedComponents());
    }

    [Fact]
    public void GetRegisteredComponents_ReturnsAll()
    {
        var a = new StubTrackingComponent("A", wasInvoked: false);
        var b = new StubTrackingComponent("B", wasInvoked: true);

        TrackingComponentRegistry.Register(a);
        TrackingComponentRegistry.Register(b);

        var registered = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(registered, c => c.ComponentName == "A");
        Assert.Contains(registered, c => c.ComponentName == "B");
    }

    [Fact]
    public void Clear_RemovesAllComponents()
    {
        TrackingComponentRegistry.Register(new StubTrackingComponent("A", wasInvoked: false));
        TrackingComponentRegistry.Register(new StubTrackingComponent("B", wasInvoked: false));

        TrackingComponentRegistry.Clear();

        Assert.Empty(TrackingComponentRegistry.GetRegisteredComponents());
    }

    private class StubTrackingComponent(string name, bool wasInvoked) : ITrackingComponent
    {
        public string ComponentName => name;
        public bool WasInvoked => wasInvoked;
        public int InvocationCount => wasInvoked ? 1 : 0;
    }
}
