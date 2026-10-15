using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tests.ProxyTap;

/// <summary>Registering taps through <c>AddProxyTapTestTracking</c>.</summary>
public class ProxyTapRegistrationTests
{
    /// <summary>A value of the property's type that differs from a fresh <see cref="ProxyTapOptions"/>'s.</summary>
    private static object NonDefault(PropertyInfo property, object? fresh) => property.PropertyType switch
    {
        var t when t == typeof(int) && property.Name == nameof(ProxyTapOptions.ListenPort) => 54321,
        var t when t == typeof(int) || t == typeof(int?) => (fresh as int? ?? 0) + 17,
        var t when t == typeof(string) => "x-" + property.Name,
        var t when t == typeof(Uri) => new Uri("http://upstream.example:8181/"),
        var t when t == typeof(bool) => !(bool)fresh!,
        var t when t.IsEnum => Enum.GetValues(t).Cast<object>().First(v => !v.Equals(fresh)),
        var t when t == typeof(TimeSpan) => (TimeSpan)fresh! + TimeSpan.FromSeconds(1),
        var t when t == typeof(TimeSpan?) => ((TimeSpan?)fresh ?? TimeSpan.Zero) + TimeSpan.FromSeconds(1),
        var t when t == typeof(IRequestResponseSink) => new ListSink(),
        var t when t == typeof(InFlightIdentityRegistry) => new InFlightIdentityRegistry(),
        var t when t == typeof(Action<string>) => new Action<string>(_ => { }),
        var t when t == typeof(Func<IReadOnlyDictionary<string, string[]>, TraceParent?, (string Name, string Id)?>) =>
            new Func<IReadOnlyDictionary<string, string[]>, TraceParent?, (string Name, string Id)?>((_, _) => null),
        var t => throw new InvalidOperationException(
            $"No test value for ProxyTapOptions.{property.Name} ({t}); add one so the copy of the new option is checked."),
    };

    [Fact]
    public void Registering_taps_from_a_list_copies_every_option()
    {
        var fresh = new ProxyTapOptions();
        var source = new ProxyTapOptions();
        var properties = typeof(ProxyTapOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            var freshValue = property.GetValue(fresh);
            if (property.CanWrite)
            {
                var value = NonDefault(property, freshValue);
                property.SetValue(source, value);
                Assert.False(Equals(freshValue, property.GetValue(source)), $"{property.Name} still has its default");
            }
            else if (property.GetValue(source) is HashSet<string> set)
                set.Add("x-" + property.Name);
            else if (property.GetValue(source) is List<string> list)
                list.Add("x-" + property.Name);
            else
                Assert.Fail($"ProxyTapOptions.{property.Name} is get-only and not a collection the fact knows how to fill");
        }

        var services = new ServiceCollection();
        services.AddProxyTapTestTracking([source]);
        using var provider = services.BuildServiceProvider();
        var copy = provider.GetRequiredService<Kronikol.Extensions.ProxyTap.ProxyTap>().Options;

        Assert.NotSame(source, copy);
        foreach (var property in properties)
        {
            var expected = property.GetValue(source);
            var actual = property.GetValue(copy);
            if (expected is IEnumerable items and not string)
                Assert.True(
                    items.Cast<object>().OrderBy(x => x.ToString()).SequenceEqual(((IEnumerable)actual!).Cast<object>().OrderBy(x => x.ToString())),
                    $"{property.Name} was not copied");
            else
                Assert.True(Equals(expected, actual), $"{property.Name} was not copied: expected {expected}, got {actual}");
        }
    }

    [Fact]
    public async Task A_tap_registered_from_a_list_publishes_its_in_flight_identity()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var upstream = new StubUpstream((_, _) =>
        {
            release.Task.Wait(TimeSpan.FromSeconds(30));
            return (200, Encoding.UTF8.GetBytes("{}"), "application/json", null, null);
        });
        var registry = new InFlightIdentityRegistry();
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = upstream.BaseUri,
            CallerName = "web",
            ServiceName = "orders",
            Sink = new ListSink(),
            InFlightRegistry = registry,
        };
        var services = new ServiceCollection();
        services.AddProxyTapTestTracking([options]);
        await using var provider = services.BuildServiceProvider();
        var tap = provider.GetRequiredService<Kronikol.Extensions.ProxyTap.ProxyTap>();
        await tap.StartAsync();
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/slow"));
            request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestNameHeader, "places an order");
            request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestIdHeader, "order-test-1");
            var sending = client.SendAsync(request);

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (registry.MostRecentFor("orders") is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            var identity = registry.MostRecentFor("orders");
            Assert.NotNull(identity);
            Assert.Equal("order-test-1", identity.Id);
            release.SetResult();
            using var response = await sending;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            release.TrySetResult();
            await tap.DisposeAsync();
        }
    }
}
