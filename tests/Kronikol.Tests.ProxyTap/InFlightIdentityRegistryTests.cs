using Kronikol.Extensions.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// The registry a database tap asks "who is this service working for right now?" — the second, optional
/// attribution layer for capturers that cannot read a test header off the wire.
/// </summary>
public class InFlightIdentityRegistryTests
{
    [Fact]
    public void An_idle_service_has_no_identity()
    {
        var registry = new InFlightIdentityRegistry();

        Assert.Null(registry.MostRecentFor("data-insights"));
        Assert.Null(registry.MostRecentFor(null));
        Assert.Null(registry.MostRecentFor(""));
        Assert.Equal(0, registry.CountFor("data-insights"));
        Assert.Empty(registry.ActiveServices);
    }

    [Fact]
    public void A_registration_is_visible_until_it_is_disposed()
    {
        var registry = new InFlightIdentityRegistry();

        using (registry.Register("data-insights", "overview renders", "abc"))
        {
            var identity = registry.MostRecentFor("data-insights");
            Assert.NotNull(identity);
            Assert.Equal("overview renders", identity!.Name);
            Assert.Equal("abc", identity.Id);
            Assert.Equal(["data-insights"], registry.ActiveServices);
        }

        Assert.Null(registry.MostRecentFor("data-insights"));
        Assert.Empty(registry.ActiveServices);
    }

    [Fact]
    public void The_most_recently_started_request_wins()
    {
        // A call a service makes belongs to the request it is currently handling, and nested work always
        // starts after its cause — so "most recent" is the right answer, not "first".
        var registry = new InFlightIdentityRegistry();

        using var first = registry.Register("data-insights", "first", "1");
        Thread.Sleep(2);
        using var second = registry.Register("data-insights", "second", "2");

        Assert.Equal("2", registry.MostRecentFor("data-insights")!.Id);
        Assert.Equal(2, registry.CountFor("data-insights"));

        second.Dispose();
        Assert.Equal("1", registry.MostRecentFor("data-insights")!.Id);
    }

    [Fact]
    public void Services_are_matched_case_insensitively_and_kept_apart()
    {
        var registry = new InFlightIdentityRegistry();

        using var _ = registry.Register("data-insights", "di", "1");
        using var __ = registry.Register("graphql", "gql", "2");

        Assert.Equal("1", registry.MostRecentFor("Data-Insights")!.Id);
        Assert.Equal("2", registry.MostRecentFor("graphql")!.Id);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var registry = new InFlightIdentityRegistry();
        var registration = registry.Register("data-insights", "overview", "abc");

        registration.Dispose();
        registration.Dispose();

        Assert.Equal(0, registry.CountFor("data-insights"));
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var registry = new InFlightIdentityRegistry();
        using var _ = registry.Register("data-insights", "overview", "abc");

        registry.Clear();

        Assert.Null(registry.MostRecentFor("data-insights"));
    }

    [Fact]
    public void Registering_a_nameless_service_is_a_caller_error()
    {
        var registry = new InFlightIdentityRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(" ", "overview", "abc"));
    }

    [Fact]
    public async Task Concurrent_registration_and_release_stays_consistent()
    {
        var registry = new InFlightIdentityRegistry();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            using var _ = registry.Register("data-insights", $"test {i}", i.ToString());
            Assert.NotNull(registry.MostRecentFor("data-insights"));
        })));

        Assert.Equal(0, registry.CountFor("data-insights"));
    }

    [Fact]
    public void A_tap_publishes_nothing_unless_a_registry_is_configured()
    {
        // The whole mechanism is opt-in: with no registry set, a tap behaves exactly as it always has.
        Assert.Null(new ProxyTapOptions().InFlightRegistry);
    }
}
