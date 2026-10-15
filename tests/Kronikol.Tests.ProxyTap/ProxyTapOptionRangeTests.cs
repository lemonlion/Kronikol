using System.Net;
using Kronikol.Extensions.ProxyTap;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// Every value of <see cref="ProxyTapOptions.ForwardTimeout"/>, <see cref="ProxyTapOptions.ConnectTimeout"/> and
/// <see cref="ProxyTapOptions.BodyCapBytes"/> either works or is refused at start (plans/TAP_CALLER_TIME_LIMITS_PLAN.md
/// §4.6 items 1 and 7). <c>HttpClient.Timeout</c> was <c>ForwardTimeout</c> plus 10 s, so an infinite forward timeout
/// (-1 ms) cut every forward at 9.999 s and blamed <c>ConnectTimeout</c>; zero failed every forward, -10 s threw from
/// <c>StartAsync</c>, and a value past what <c>HttpClient</c> and <c>SocketsHttpHandler</c> take threw there too.
/// </summary>
public class ProxyTapOptionRangeTests
{
    [Fact]
    public async Task An_infinite_forward_timeout_never_cuts_a_slow_upstream()
    {
        await using var upstream = new StubUpstream((_, _) =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(15)); // past the 10 s the client's timeout came to
            return (200, "late"u8.ToArray(), "text/plain", null, null);
        });
        await using var tap = await StartAsync(upstream.BaseUri, o => o.ForwardTimeout = Timeout.InfiniteTimeSpan);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var response = await client.GetAsync(new Uri(tap.ListenUri, "/slow"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("late", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Theory]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), 0)]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), -5)]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), -10)]
    [InlineData(nameof(ProxyTapOptions.ConnectTimeout), 0)]
    [InlineData(nameof(ProxyTapOptions.ConnectTimeout), -5)]
    [InlineData(nameof(ProxyTapOptions.ConnectTimeout), -10)]
    public async Task Forward_and_connect_timeouts_are_checked_at_start(string option, int seconds)
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var value = TimeSpan.FromSeconds(seconds);
        var tap = new Tap(Options(upstream.BaseUri, o => Set(o, option, value)));

        var refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());

        Assert.Equal(option, refused.ParamName);
        Assert.False(tap.IsListening);
        await tap.DisposeAsync();
    }

    [Theory]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), "TimeSpan.MaxValue")]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), "30 days")]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), "int.MaxValue ms less 5 s")]
    [InlineData(nameof(ProxyTapOptions.ForwardTimeout), "Timeout.InfiniteTimeSpan")]
    [InlineData(nameof(ProxyTapOptions.ConnectTimeout), "TimeSpan.MaxValue")]
    [InlineData(nameof(ProxyTapOptions.ConnectTimeout), "30 days")]
    [InlineData(nameof(ProxyTapOptions.ConnectTimeout), "Timeout.InfiniteTimeSpan")]
    public async Task A_timeout_too_long_to_set_means_no_deadline(string option, string value)
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var span = value switch
        {
            "TimeSpan.MaxValue" => TimeSpan.MaxValue,
            "30 days" => TimeSpan.FromDays(30),
            "int.MaxValue ms less 5 s" => TimeSpan.FromMilliseconds(int.MaxValue) - TimeSpan.FromSeconds(5),
            _ => Timeout.InfiniteTimeSpan,
        };
        await using var tap = await StartAsync(upstream.BaseUri, o => Set(o, option, span));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await client.GetAsync(new Uri(tap.ListenUri, "/any"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Fact]
    public async Task A_negative_cap_is_refused()
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var tap = new Tap(Options(upstream.BaseUri, o => o.BodyCapBytes = -1));

        var refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());

        Assert.Equal(nameof(ProxyTapOptions.BodyCapBytes), refused.ParamName);
        await tap.DisposeAsync();
    }

    // ------------------------------------------------------------------ helpers

    private static void Set(ProxyTapOptions options, string option, TimeSpan value)
    {
        if (option == nameof(ProxyTapOptions.ForwardTimeout))
            options.ForwardTimeout = value;
        else
            options.ConnectTimeout = value;
    }

    private static ProxyTapOptions Options(Uri forward, Action<ProxyTapOptions> configure)
    {
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = forward,
            CallerName = "web",
            ServiceName = "api",
            Sink = new ListSink(),
        };
        configure(options);
        return options;
    }

    private static async Task<Tap> StartAsync(Uri forward, Action<ProxyTapOptions> configure)
    {
        var tap = new Tap(Options(forward, configure));
        await tap.StartAsync();
        return tap;
    }
}
