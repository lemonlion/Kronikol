using System.Diagnostics;
using System.Net;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Kronikol.Constants;
using Kronikol.Extensions.Grpc;
using Kronikol.Tests.Grpc.Hop;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kronikol.Tests.Grpc.MultiHost;

/// <summary>
/// A host in a multi-host fact: an ASP.NET Core app that calls <c>AddTestTrackingContextPropagation()</c>, serves
/// <c>Hop</c>, and calls its next hop (over gRPC through <c>AddTrackedGrpcClient</c>, or over HTTP through
/// <see cref="TestTrackingMessageHandler"/>, each with the host's own accessor) while it serves each call. It runs on
/// <see cref="TestServer"/>, as a <c>WebApplicationFactory</c> suite's hosts do, or on Kestrel with an h2c endpoint.
/// </summary>
internal sealed class HopHost : IAsyncDisposable
{
    private readonly List<GrpcChannel> _channels = [];

    private HopHost(string name, WebApplication app, Uri baseAddress, bool kestrel)
    {
        Name = name;
        App = app;
        BaseAddress = baseAddress;
        Kestrel = kestrel;
    }

    public string Name { get; }
    public WebApplication App { get; }
    public Uri BaseAddress { get; }
    public bool Kestrel { get; }

    /// <summary>The host's own accessor, as a suite's <c>factory.Services</c> hands it out.</summary>
    public IHttpContextAccessor Accessor => App.Services.GetRequiredService<IHttpContextAccessor>();

    public HttpMessageHandler CreateHandler() => Kestrel
        ? new SocketsHttpHandler()
        : new GrpcResponseVersionHandler(App.GetTestServer().CreateHandler());

    /// <summary>
    /// A test-side client for this host in the shape <c>CreateTestTrackingGrpcClient</c> builds: the interceptor gets
    /// the host's accessor, which has no request on the test's side.
    /// </summary>
    public HopService.HopServiceClient CreateTestClient(GrpcTrackingOptions options, IHttpContextAccessor? accessor = null, params Interceptor[] after)
    {
        options.HttpContextAccessor ??= accessor ?? Accessor;
        var channel = GrpcChannel.ForAddress(BaseAddress, new GrpcChannelOptions { HttpHandler = CreateHandler() });
        _channels.Add(channel);
        CallInvoker invoker = channel.Intercept(new GrpcTrackingInterceptor(options, options.HttpContextAccessor));
        foreach (var interceptor in after)
            invoker = invoker.Intercept(interceptor);
        return new HopService.HopServiceClient(invoker);
    }

    public static async Task<HopHost> StartAsync(string name, Action<HopHostOptions>? configure = null)
    {
        var hostOptions = new HopHostOptions();
        configure?.Invoke(hostOptions);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        if (hostOptions.Kestrel)
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2));
        else
            builder.WebHost.UseTestServer();

        builder.Services.AddGrpc();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTestTrackingContextPropagation();

        if (hostOptions.NextGrpc is { } nextGrpc)
        {
            builder.Services.AddTrackedGrpcClient<HopService.HopServiceClient>(nextGrpc.CreateHandler(), nextGrpc.BaseAddress, o =>
            {
                o.ServiceName = nextGrpc.Name;
                o.CallerName = name;
                hostOptions.ConfigureNextGrpc?.Invoke(o);
            });
        }

        builder.Services.AddSingleton(sp => new HopNext(
            hostOptions.NextGrpc is null ? null : sp.GetRequiredService<HopService.HopServiceClient>(),
            hostOptions.NextHttp is { } stub
                ? new HttpClient(new TestTrackingMessageHandler(
                    new TestTrackingMessageHandlerOptions { CallerName = name, FixedNameForReceivingService = stub.Name },
                    sp.GetRequiredService<IHttpContextAccessor>())
                { InnerHandler = stub.CreateHandler() })
                { BaseAddress = stub.BaseAddress }
                : null));

        var app = builder.Build();
        app.MapGrpcService<HopServiceImpl>();
        // An HTTP entry point, for a host the test calls over HTTP: it calls the next hop and answers.
        app.MapGet("/start", async (HopNext next, CancellationToken cancellationToken) =>
        {
            await next.CallAsync("start", cancellationToken);
            return "ok";
        });
        await app.StartAsync();

        var baseAddress = hostOptions.Kestrel
            ? new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First())
            : app.GetTestServer().BaseAddress;
        return new HopHost(name, app, baseAddress, hostOptions.Kestrel);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _channels)
            channel.Dispose();
        await App.DisposeAsync();
    }
}

internal sealed class HopHostOptions
{
    /// <summary>Kestrel with an h2c endpoint on a real socket, not <see cref="TestServer"/>.</summary>
    public bool Kestrel { get; set; }

    /// <summary>The next hop over gRPC, registered with <c>AddTrackedGrpcClient</c>.</summary>
    public HopHost? NextGrpc { get; set; }

    public Action<GrpcTrackingOptions>? ConfigureNextGrpc { get; set; }

    /// <summary>The next hop over HTTP, called through <see cref="TestTrackingMessageHandler"/> with the host's accessor.</summary>
    public StubHost? NextHttp { get; set; }
}

internal sealed record HopNext(HopService.HopServiceClient? Grpc, HttpClient? Http)
{
    public async Task CallAsync(string text, CancellationToken cancellationToken)
    {
        if (Grpc is not null)
            await Grpc.CallAsync(new HopRequest { Text = text }, cancellationToken: cancellationToken);
        if (Http is not null)
            (await Http.GetAsync("/next", cancellationToken)).EnsureSuccessStatusCode();
    }
}

internal sealed class HopServiceImpl(HopNext next) : HopService.HopServiceBase
{
    public override async Task<HopReply> Call(HopRequest request, ServerCallContext context)
    {
        var reply = Observe(context);
        await next.CallAsync(request.Text, context.CancellationToken);
        Fail(request);
        return reply;
    }

    public override async Task Stream(HopRequest request, IServerStreamWriter<HopReply> responseStream, ServerCallContext context)
    {
        await next.CallAsync(request.Text, context.CancellationToken);
        for (var i = 0; i < request.Replies; i++)
            await responseStream.WriteAsync(Observe(context));
        Fail(request);
    }

    public override async Task<HopReply> Upload(IAsyncStreamReader<HopRequest> requestStream, ServerCallContext context)
    {
        var last = new HopRequest();
        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            last = request;
        await next.CallAsync(last.Text, context.CancellationToken);
        Fail(last);
        return Observe(context);
    }

    public override async Task Chat(IAsyncStreamReader<HopRequest> requestStream, IServerStreamWriter<HopReply> responseStream, ServerCallContext context)
    {
        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            await next.CallAsync(request.Text, context.CancellationToken);
            for (var i = 0; i < Math.Max(1, request.Replies); i++)
                await responseStream.WriteAsync(Observe(context));
            Fail(request);
        }
    }

    private static void Fail(HopRequest request)
    {
        if (request.FailWith != 0)
            throw new RpcException(new Status((StatusCode)request.FailWith, "failed on purpose"));
    }

    private static HopReply Observe(ServerCallContext context)
    {
        var headers = context.GetHttpContext().Request.Headers;
        string Seen(string key) => headers.TryGetValue(key, out var values) ? string.Join(" | ", values.ToArray()) : "";

        var scope = TestIdentityScope.Current;
        var span = Activity.Current;
        return new HopReply
        {
            SeenTestName = Seen(TestTrackingHttpHeaders.CurrentTestNameHeader),
            SeenTestId = Seen(TestTrackingHttpHeaders.CurrentTestIdHeader),
            SeenTraceId = Seen(TestTrackingHttpHeaders.TraceIdHeader),
            SeenCallerName = Seen(TestTrackingHttpHeaders.CallerNameHeader),
            SeenTraceparents = Seen("traceparent"),
            ScopeTestName = scope?.Name ?? "",
            ScopeTestId = scope?.Id ?? "",
            ServerTraceId = span?.TraceId.ToString() ?? "",
            ServerParentSpanId = span?.ParentSpanId.ToString() ?? "",
            ServerRecorded = span?.Recorded ?? false,
        };
    }
}

/// <summary>The last hop: a host on <see cref="TestServer"/> that answers every request with 200 and calls nothing.</summary>
internal sealed class StubHost : IAsyncDisposable
{
    private StubHost(string name, WebApplication app)
    {
        Name = name;
        App = app;
    }

    public string Name { get; }
    public WebApplication App { get; }
    public Uri BaseAddress => App.GetTestServer().BaseAddress;
    public HttpMessageHandler CreateHandler() => App.GetTestServer().CreateHandler();

    public static async Task<StubHost> StartAsync(string name)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.Run(context => context.Response.WriteAsync("ok"));
        await app.StartAsync();
        return new StubHost(name, app);
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();
}
