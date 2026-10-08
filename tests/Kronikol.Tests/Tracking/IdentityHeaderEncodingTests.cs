using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// Identity header values travel in the RFC 8187 <c>UTF-8''</c> form when they are not plain printable ASCII
/// (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md section 4.1, T7 to T12): what <see cref="TrackingHeaderValue"/> writes,
/// that the HTTP handler writes it, that every reader in core decodes it, and that names which failed the call
/// before now reach the called host exactly, in memory and over a real socket.
/// </summary>
[Collection("DiagramsFetcher")]
public class IdentityHeaderEncodingTests
{
    // xUnit v3 shortens a string argument longer than 50 characters with three U+00B7, so an all-ASCII theory
    // gets a non-ASCII display name.
    private static readonly string XunitShortenedName =
        "Places_an_order(text: \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"" + new string((char)0xB7, 3) + ")";

    public static readonly TheoryData<string> RoundTripValues = new()
    {
        "Place an order",
        "Café order",
        "Order a coffee ☕",
        XunitShortenedName,
        "Theory(a\nb)",
        "Theory(a\r\nb)",
        "tab\there",
        " Place an order ",
        "trailing ",
        " leading",
        "UTF-8''Caf%C3%A9",
        "",
        " ",
        "Ns.Klass.Prüfung",
        "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        "0-1001",
        "100% sure",
        "a%20b",
        "quote \" and backslash \\",
    };

    public static readonly TheoryData<string> PlainValues = new()
    {
        "Place an order",
        "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        "0-1001",
        "Ns.Klass.Method",
        "",
        "100% sure",
        "a%20b",
        "quote \" and backslash \\",
        "utf-8''lower case prefix is not ours",
        "1c0a1a3a41c5c5d1f4b7f0e6e2d9a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7",
    };

    public static readonly TheoryData<string> EncodedValues = new()
    {
        "Café order",
        "Order a coffee ☕",
        XunitShortenedName,
        "Theory(a\nb)",
        "tab\there",
        " Place an order ",
        "UTF-8''x",
        " ",
        "Ns.Klass.Prüfung",
    };

    // ─── T7: the encoding ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(RoundTripValues))]
    public void Decode_gives_back_exactly_what_Encode_was_given(string value)
    {
        Assert.Equal(value, TrackingHeaderValue.Decode(TrackingHeaderValue.Encode(value)));
    }

    [Theory]
    [MemberData(nameof(PlainValues))]
    public void A_plain_printable_ascii_value_is_sent_and_read_unchanged(string value)
    {
        Assert.Equal(value, TrackingHeaderValue.Encode(value));
        Assert.Equal(value, TrackingHeaderValue.Decode(value));
    }

    [Theory]
    [MemberData(nameof(EncodedValues))]
    public void Any_other_value_is_sent_as_printable_ascii_with_no_space_after_the_prefix(string value)
    {
        var encoded = TrackingHeaderValue.Encode(value);

        Assert.StartsWith("UTF-8''", encoded, StringComparison.Ordinal);
        Assert.All(encoded, c => Assert.InRange(c, (char)0x21, (char)0x7E));
    }

    public static readonly TheoryData<string, string> WireForms = new()
    {
        { "Café order", "UTF-8''Caf%C3%A9%20order" },
        { " Place an order ", "UTF-8''%20Place%20an%20order%20" },
        { "Theory(a\nb)", "UTF-8''Theory%28a%0Ab%29" },
        { "Order a coffee ☕", "UTF-8''Order%20a%20coffee%20%E2%98%95" },
        { "UTF-8''x", "UTF-8''UTF-8%27%27x" },
    };

    [Theory]
    [MemberData(nameof(WireForms))]
    public void A_value_that_is_not_plain_ascii_is_written_in_the_rfc8187_form(string value, string wire)
    {
        Assert.Equal(wire, TrackingHeaderValue.Encode(value));
        Assert.Equal(value, TrackingHeaderValue.Decode(wire));
    }

    [Fact]
    public void A_long_non_ascii_value_round_trips()
    {
        var value = new string('é', 70_000);

        var encoded = TrackingHeaderValue.Encode(value);

        Assert.Equal(value, TrackingHeaderValue.Decode(encoded));
        Assert.Equal("UTF-8''".Length + 70_000 * 6, encoded.Length);
    }

    [Fact]
    public void A_lone_surrogate_comes_back_as_the_replacement_character()
    {
        var value = "before " + (char)0xD800 + " after";

        var decoded = TrackingHeaderValue.Decode(TrackingHeaderValue.Encode(value));

        Assert.Equal("before " + (char)0xFFFD + " after", decoded);
    }

    [Fact]
    public void A_malformed_escape_is_left_as_written_and_nothing_throws()
    {
        Assert.Equal("100%zz", TrackingHeaderValue.Decode("UTF-8''100%zz"));
        Assert.Equal("", TrackingHeaderValue.Decode("UTF-8''"));
        Assert.Null(TrackingHeaderValue.Decode(null));
        Assert.Null(TrackingHeaderValue.Encode(null));
    }

    // ─── The HTTP handler writes the form ───────────────────────

    [Theory]
    [InlineData("Café order")]
    [InlineData("Theory(a\nb)")]
    [InlineData(" Place an order ")]
    public async Task The_handler_sends_a_name_and_id_that_are_not_plain_ascii_encoded(string name)
    {
        var id = "Ns.Klass.Prüfung." + Guid.NewGuid().ToString("N");
        var inner = new CapturingHandler();
        using var invoker = new HttpMessageInvoker(new TestTrackingMessageHandler(
            new TestTrackingMessageHandlerOptions
            {
                CallerName = "Caller ☕",
                FixedNameForReceivingService = "Target",
                CurrentTestInfoFetcher = () => (name, id),
            })
        { InnerHandler = inner });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://target:5000/x"), TestContext.Current.CancellationToken);

        var sent = inner.Captured!.Headers;
        Assert.Equal(Rfc8187(name), Assert.Single(sent.GetValues(TestTrackingHttpHeaders.CurrentTestNameHeader)));
        Assert.Equal(Rfc8187(id), Assert.Single(sent.GetValues(TestTrackingHttpHeaders.CurrentTestIdHeader)));
        Assert.Equal(Rfc8187("Caller ☕"), Assert.Single(sent.GetValues(TestTrackingHttpHeaders.CallerNameHeader)));

        var log = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.TestId == id && l.Type == RequestResponseType.Request);
        Assert.Equal(name, log.TestName);
    }

    [Fact]
    public async Task The_handler_sends_a_plain_name_and_id_byte_for_byte()
    {
        var id = Guid.NewGuid().ToString();
        var inner = new CapturingHandler();
        using var invoker = new HttpMessageInvoker(new TestTrackingMessageHandler(
            new TestTrackingMessageHandlerOptions
            {
                CallerName = "Caller",
                FixedNameForReceivingService = "Target",
                CurrentTestInfoFetcher = () => ("Place an order", id),
            })
        { InnerHandler = inner });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://target:5000/x"), TestContext.Current.CancellationToken);

        var sent = inner.Captured!.Headers;
        Assert.Equal("Place an order", Assert.Single(sent.GetValues(TestTrackingHttpHeaders.CurrentTestNameHeader)));
        Assert.Equal(id, Assert.Single(sent.GetValues(TestTrackingHttpHeaders.CurrentTestIdHeader)));
        Assert.Equal("Caller", Assert.Single(sent.GetValues(TestTrackingHttpHeaders.CallerNameHeader)));
    }

    // ─── T8 and T9: names that failed the call reach the host ───

    [Fact]
    public async Task A_name_with_a_line_feed_reaches_a_host_in_memory_exactly()
    {
        await using var host = await StartIdentityEchoHost(kestrel: false);
        var id = Guid.NewGuid().ToString();

        var seen = await CallThroughHandler(host, "Theory(a\nb)", id);

        Assert.Equal("Theory(a\nb)", seen.Name);
        Assert.Equal(id, seen.Id);
    }

    public static readonly TheoryData<string, string> SocketIdentities = new()
    {
        { "Café order", Guid.NewGuid().ToString() },
        { "Order a coffee ☕", Guid.NewGuid().ToString() },
        { XunitShortenedName, Guid.NewGuid().ToString() },
        { " Place an order ", Guid.NewGuid().ToString() },
        { "Theory(a\nb)", Guid.NewGuid().ToString() },
        { "Place an order", "Ns.Klass.Prüfung" },
    };

    [Theory]
    [MemberData(nameof(SocketIdentities))]
    public async Task A_name_or_id_that_is_not_plain_ascii_reaches_a_host_over_a_real_socket_exactly(string name, string id)
    {
        await using var host = await StartIdentityEchoHost(kestrel: true);

        var seen = await CallThroughHandler(host, name, id);

        Assert.Equal(name, seen.Name);
        Assert.Equal(id, seen.Id);
    }

    // ─── T10: every reader in core decodes ──────────────────────

    [Fact]
    public async Task The_middleware_decodes_the_identity_it_puts_in_scope()
    {
        TestIdentityScope.Reset();
        (string Name, string Id)? scoped = null;
        var middleware = new TestTrackingContextMiddleware(_ =>
        {
            scoped = TestIdentityScope.Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ContextWith("Café order", "Ns.Klass.Prüfung"));

        Assert.Equal(("Café order", "Ns.Klass.Prüfung"), scoped);
    }

    [Fact]
    public void The_resolver_decodes_the_request_headers()
    {
        var accessor = new HttpContextAccessor { HttpContext = ContextWith("Café order", "Ns.Klass.Prüfung") };

        var identity = TestInfoResolver.ResolveWithSource(accessor, (Func<(string Name, string Id)>?)null);

        Assert.NotNull(identity);
        Assert.Equal("Café order", identity.Value.Name);
        Assert.Equal("Ns.Klass.Prüfung", identity.Value.Id);
        Assert.Equal(AttributionSource.RequestHeader, identity.Value.Source);
    }

    [Fact]
    public async Task The_handler_decodes_the_identity_of_the_request_it_is_serving()
    {
        var id = "Ns.Klass.Prüfung." + Guid.NewGuid().ToString("N");
        var accessor = new StubAccessor(ContextWith("Café order", id));
        using var invoker = new HttpMessageInvoker(new TestTrackingMessageHandler(
            new TestTrackingMessageHandlerOptions { CallerName = "Host", FixedNameForReceivingService = "Next" },
            accessor)
        { InnerHandler = new CapturingHandler() });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://next:5000/x"), TestContext.Current.CancellationToken);

        var log = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.TestId == id && l.Type == RequestResponseType.Request);
        Assert.Equal("Café order", log.TestName);
    }

    [Fact]
    public void The_message_tracker_decodes_the_request_headers()
    {
        var id = "Ns.Klass.Prüfung." + Guid.NewGuid().ToString("N");
        var context = ContextWith("Café order", id);
        context.Request.Headers[TestTrackingHttpHeaders.TraceIdHeader] = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new HttpContextAccessor { HttpContext = context }, "Host");

        var requestResponseId = tracker.TrackMessageRequest("Kafka", "Orders", new Uri("kafka://orders"), new { Id = 1 });

        var log = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.RequestResponseId == requestResponseId);
        Assert.Equal(id, log.TestId);
        Assert.Equal("Café order", log.TestName);
    }

    [Fact]
    public void The_server_bridge_decodes_the_request_headers()
    {
        var accessor = new HttpContextAccessor { HttpContext = ContextWith("Café order", "Ns.Klass.Prüfung") };

        Assert.Equal(("Café order", "Ns.Klass.Prüfung"), TestTrackingServerBridge.GetCurrentTestInfo(accessor));
    }

    // ─── T12: a trace id that is not a GUID ─────────────────────

    [Fact]
    public async Task The_handler_gives_a_call_whose_inbound_trace_id_is_not_a_guid_a_trace_id_of_its_own()
    {
        var id = Guid.NewGuid().ToString();
        var context = PlainContextWith("Place an order", id);
        context.Request.Headers[TestTrackingHttpHeaders.TraceIdHeader] = "abc";
        using var invoker = new HttpMessageInvoker(new TestTrackingMessageHandler(
            new TestTrackingMessageHandlerOptions { CallerName = "Host", FixedNameForReceivingService = "Next" },
            new StubAccessor(context))
        { InnerHandler = new CapturingHandler() });

        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://next:5000/x"), TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var log = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.TestId == id && l.Type == RequestResponseType.Request);
        Assert.NotEqual(Guid.Empty, log.TraceId);
    }

    [Fact]
    public void The_message_tracker_keeps_the_header_identity_when_the_trace_id_is_not_a_guid()
    {
        var id = Guid.NewGuid().ToString();
        var context = PlainContextWith("Place an order", id);
        context.Request.Headers[TestTrackingHttpHeaders.TraceIdHeader] = "abc";
        var tracker = new MessageTracker(new HttpContextAccessor { HttpContext = context }, "Host");

        var requestResponseId = tracker.TrackMessageRequest("Kafka", "Orders", new Uri("kafka://orders"), new { Id = 1 });

        var log = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.RequestResponseId == requestResponseId);
        Assert.Equal(id, log.TestId);
        Assert.NotEqual(Guid.Empty, log.TraceId);
    }

    // ─── helpers ────────────────────────────────────────────────

    /// <summary>The RFC 8187 form written by hand, independent of <see cref="TrackingHeaderValue"/>: the oracle.</summary>
    private static string Rfc8187(string value) => "UTF-8''" + Uri.EscapeDataString(value);

    /// <summary>
    /// A request carrying <paramref name="name"/> and <paramref name="id"/> as a Kronikol writer sends them: in the
    /// RFC 8187 form, written here by hand and not through <see cref="TrackingHeaderValue"/>, so a reader that does not
    /// decode reads the encoded text.
    /// </summary>
    private static DefaultHttpContext ContextWith(string name, string id)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "UTF-8''" + Uri.EscapeDataString(name);
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "UTF-8''" + Uri.EscapeDataString(id);
        return context;
    }

    /// <summary>A request carrying a plain ASCII name and id as they are.</summary>
    private static DefaultHttpContext PlainContextWith(string name, string id)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = name;
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = id;
        return context;
    }

    private sealed record SeenIdentity(string? Name, string? Id);

    /// <summary>A host that calls <c>AddTestTrackingContextPropagation()</c> and answers with the scope it sees.</summary>
    private static async Task<WebApplication> StartIdentityEchoHost(bool kestrel)
    {
        var builder = WebApplication.CreateBuilder();
        if (kestrel)
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        else
            builder.WebHost.UseTestServer();
        builder.Services.AddTestTrackingContextPropagation();

        var app = builder.Build();
        app.MapGet("/who", () => TestIdentityScope.Current is { } scope
            ? new SeenIdentity(scope.Name, scope.Id)
            : new SeenIdentity(null, null));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<SeenIdentity> CallThroughHandler(WebApplication host, string name, string id)
    {
        HttpMessageHandler transport;
        Uri baseAddress;
        if (host.Services.GetRequiredService<IServer>() is TestServer testServer)
        {
            transport = testServer.CreateHandler();
            baseAddress = testServer.BaseAddress;
        }
        else
        {
            transport = new SocketsHttpHandler();
            baseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        }

        using var client = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test",
            FixedNameForReceivingService = "Host",
            CurrentTestInfoFetcher = () => (name, id),
        })
        { InnerHandler = transport })
        { BaseAddress = baseAddress };

        return (await client.GetFromJsonAsync<SeenIdentity>("/who", TestContext.Current.CancellationToken))!;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") });
        }
    }

    /// <summary>An accessor without <see cref="HttpContextAccessor"/>'s static AsyncLocal, which leaks between tests.</summary>
    private sealed class StubAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}
