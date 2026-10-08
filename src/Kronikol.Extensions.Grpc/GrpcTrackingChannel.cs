using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace Kronikol.Extensions.Grpc;

/// <summary>
/// Factory methods for creating gRPC <see cref="CallInvoker"/> instances that record
/// interactions for diagram generation. Use these for the test-to-SUT (incoming)
/// direction so that gRPC calls appear with rich protobuf-aware labels instead
/// of raw HTTP/2 traffic. Each call also carries the test's identity
/// (<see cref="GrpcTrackingOptions.PropagateTestIdentity"/>), so a service that calls
/// <c>AddTestTrackingContextPropagation()</c> attributes its own database, HTTP and messaging
/// calls to the scenario.
/// </summary>
public static class GrpcTrackingChannel
{
    /// <summary>
    /// Creates a <see cref="CallInvoker"/> that intercepts all gRPC calls through the given
    /// <paramref name="handler"/> and records them for test diagrams.
    /// Typical usage with <c>WebApplicationFactory</c>:
    /// <code>
    /// var invoker = GrpcTrackingChannel.Create(
    ///     factory.Server.CreateHandler(),
    ///     factory.Server.BaseAddress,
    ///     new GrpcTrackingOptions { ServiceName = "My API", CallerName = "Test" });
    /// var client = new MyService.MyServiceClient(invoker);
    /// </code>
    /// </summary>
    /// <param name="handler">The HTTP message handler (e.g. from <c>TestServer.CreateHandler()</c>).</param>
    /// <param name="baseAddress">The base address of the server.</param>
    /// <param name="options">gRPC tracking options controlling service naming and verbosity.</param>
    /// <returns>A <see cref="CallInvoker"/> with the tracking interceptor installed.</returns>
    public static CallInvoker Create(HttpMessageHandler handler, Uri baseAddress, GrpcTrackingOptions options)
    {
        var channel = GrpcChannel.ForAddress(baseAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        return channel.Intercept(new GrpcTrackingInterceptor(options, options.HttpContextAccessor));
    }

    /// <summary>
    /// Creates a <see cref="CallInvoker"/> and returns the underlying <see cref="GrpcChannel"/>
    /// so the caller can dispose it when the test completes.
    /// </summary>
    /// <param name="handler">The HTTP message handler.</param>
    /// <param name="baseAddress">The base address of the server.</param>
    /// <param name="options">gRPC tracking options.</param>
    /// <returns>A tuple of the tracked <see cref="CallInvoker"/> and the underlying <see cref="GrpcChannel"/>.</returns>
    public static (CallInvoker Invoker, GrpcChannel Channel) CreateWithChannel(
        HttpMessageHandler handler, Uri baseAddress, GrpcTrackingOptions options)
    {
        var channel = GrpcChannel.ForAddress(baseAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        var invoker = channel.Intercept(new GrpcTrackingInterceptor(options, options.HttpContextAccessor));
        return (invoker, channel);
    }
}

/// <summary>
/// Extension methods on <see cref="HttpMessageHandler"/> for convenient gRPC tracking setup.
/// </summary>
public static class GrpcTrackingHttpHandlerExtensions
{
    /// <summary>
    /// Creates a gRPC <see cref="CallInvoker"/> from this handler with tracking enabled.
    /// Shorthand for <c>GrpcTrackingChannel.Create(handler, baseAddress, options)</c>.
    /// </summary>
    public static CallInvoker AsGrpcTrackingCallInvoker(
        this HttpMessageHandler handler, Uri baseAddress, GrpcTrackingOptions options)
    {
        return GrpcTrackingChannel.Create(handler, baseAddress, options);
    }
}
