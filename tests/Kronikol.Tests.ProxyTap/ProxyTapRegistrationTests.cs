using System.Collections;
using System.Net;
using System.Reflection;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// <see cref="ProxyTapServiceCollectionExtensions.AddProxyTapTestTracking(IServiceCollection, IEnumerable{ProxyTapOptions})"/>
/// copies each options object member by member, so an option the copy leaves out is silently off for a tap registered that
/// way. <see cref="ProxyTapOptions.InFlightRegistry"/> was left out from the day it was added.
/// </summary>
public class ProxyTapRegistrationTests
{
    [Fact]
    public async Task Registering_taps_from_a_list_keeps_every_option()
    {
        var source = new ProxyTapOptions();
        var expected = new List<(PropertyInfo Property, object? Value)>();
        foreach (var property in typeof(ProxyTapOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite)
            {
                var value = ValueOtherThanTheDefault(property, property.GetValue(source));
                property.SetValue(source, value);
                expected.Add((property, value));
            }
            else
            {
                // The four header lists are read-only properties the caller fills.
                var collection = property.GetValue(source);
                var add = collection?.GetType().GetMethod("Add", [typeof(string)]);
                Assert.True(add is not null, $"ProxyTapOptions.{property.Name} is read-only and not a list of strings, so this guard cannot tell whether it is copied; teach it");
                add!.Invoke(collection, [$"x-{property.Name.ToLowerInvariant()}"]);
                expected.Add((property, ((IEnumerable)collection!).Cast<string>().ToArray()));
            }
        }

        var services = new ServiceCollection();
        services.AddProxyTapTestTracking([source]);
        await using var provider = services.BuildServiceProvider();
        var copy = provider.GetRequiredService<Tap>().Options;

        Assert.NotSame(source, copy);
        var dropped = new List<string>();
        foreach (var (property, value) in expected)
        {
            var actual = property.GetValue(copy);
            var kept = value is string[] items
                ? ((IEnumerable)actual!).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(items)
                : value is null || value.GetType().IsValueType || value is string || value is Uri
                    ? Equals(value, actual)
                    : ReferenceEquals(value, actual);
            if (!kept)
                dropped.Add($"{property.Name} (set {Describe(value)}, copied {Describe(actual)})");
        }

        Assert.True(dropped.Count == 0, "AddProxyTapTestTracking(IEnumerable<ProxyTapOptions>) drops: " + string.Join("; ", dropped));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tap_publishes_each_request_it_forwards_to_its_in_flight_registry(bool registeredFromAList)
    {
        var registry = new InFlightIdentityRegistry();
        InFlightIdentityRegistry.InFlightIdentity? duringTheForward = null;
        await using var upstream = new StubUpstream((_, _) =>
        {
            duringTheForward = registry.MostRecentFor("graphql");
            return (200, [], null, null, null);
        });
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = upstream.BaseUri,
            CallerName = "web",
            ServiceName = "graphql",
            Sink = new ListSink(),
            InFlightRegistry = registry,
        };

        await using var provider = registeredFromAList
            ? new ServiceCollection().AddProxyTapTestTracking([options]).BuildServiceProvider()
            : null;
        var tap = provider?.GetRequiredService<Tap>() ?? new Tap(options);
        await tap.StartAsync();
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/cart"));
            request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestNameHeader, "checkout");
            request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestIdHeader, "checkout-id");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await tap.DisposeAsync();
        }

        Assert.NotNull(duringTheForward);
        Assert.Equal("checkout", duringTheForward!.Name);
        Assert.Equal("checkout-id", duringTheForward.Id);
        Assert.Null(registry.MostRecentFor("graphql")); // released once the exchange was answered
    }

    /// <summary>
    /// A value for <paramref name="property"/> that differs from <paramref name="current"/>, its default. A property of a type
    /// this guard does not know fails the fact, so a new option cannot slip past it.
    /// </summary>
    private static object? ValueOtherThanTheDefault(PropertyInfo property, object? current)
    {
        var type = property.PropertyType;
        object? value = type switch
        {
            _ when type == typeof(int) => 4321,
            _ when type == typeof(int?) => 1234,
            _ when type == typeof(bool) => !(bool)current!,
            _ when type == typeof(string) => $"{property.Name} value",
            _ when type == typeof(Uri) => new Uri($"http://localhost:4322/{property.Name}"),
            _ when type == typeof(TimeSpan) => (TimeSpan)current! + TimeSpan.FromSeconds(7),
            _ when type.IsEnum => Enum.GetValues(type).Cast<object>().First(v => !v.Equals(current)),
            _ when type == typeof(IRequestResponseSink) => new ListSink(),
            _ when type == typeof(InFlightIdentityRegistry) => new InFlightIdentityRegistry(),
            _ when type == typeof(Action<string>) => new Action<string>(_ => { }),
            _ when type == typeof(Func<IReadOnlyDictionary<string, string[]>, TraceParent?, (string Name, string Id)?>) =>
                new Func<IReadOnlyDictionary<string, string[]>, TraceParent?, (string Name, string Id)?>((_, _) => null),
            _ when type == typeof(Func<(string Name, string Id), TraceParent?>) => new Func<(string Name, string Id), TraceParent?>(_ => null),
            _ => throw new InvalidOperationException($"ProxyTapOptions.{property.Name} is a {type}, which this guard has no value for; add one"),
        };

        Assert.False(Equals(value, current), $"The guard's value for ProxyTapOptions.{property.Name} is its default, so it proves nothing");
        return value;
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string[] items => "[" + string.Join(", ", items) + "]",
        IEnumerable and not string => "[" + string.Join(", ", ((IEnumerable)value).Cast<object>()) + "]",
        Delegate => "a delegate",
        _ => value.ToString() ?? value.GetType().Name,
    };
}
