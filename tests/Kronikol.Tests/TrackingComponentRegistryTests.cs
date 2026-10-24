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
        var a = new StubTrackingComponent("A", wasInvoked: true);
        var b = new StubTrackingComponent("B", wasInvoked: true);
        TrackingComponentRegistry.Register(a);
        TrackingComponentRegistry.Register(b);

        // its own components only: the registry is the process's, and a test in another collection registers a handler
        // it has not used yet at any moment (5.7.3)
        var unused = TrackingComponentRegistry.GetUnusedComponents();
        Assert.DoesNotContain(a, unused);
        Assert.DoesNotContain(b, unused);
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

        // the two it held are gone; the registry is the process's, and a test in another collection can register its
        // own handler the moment after
        Assert.DoesNotContain(TrackingComponentRegistry.GetRegisteredComponents(), c => c.ComponentName is "A" or "B");
    }

    private class StubTrackingComponent(string name, bool wasInvoked) : ITrackingComponent
    {
        public string ComponentName => name;
        public bool WasInvoked => wasInvoked;
        public int InvocationCount => wasInvoked ? 1 : 0;
    }
}
