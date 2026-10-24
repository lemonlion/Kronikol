using Kronikol.Constants;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.Grpc;

/// <summary>
/// Extension methods on <see cref="WebApplicationFactory{TEntryPoint}"/> for creating
/// gRPC clients with test tracking enabled. These are the gRPC equivalent of
/// <c>WebApplicationFactoryExtensions.CreateTestTrackingClient</c> for HTTP.
/// </summary>
public static class GrpcWebApplicationFactoryExtensions
{
    /// <summary>
    /// Creates a gRPC client of type <typeparamref name="TClient"/> that records all gRPC
    /// calls for test diagram generation. This is a one-liner convenience method that handles:
    /// <list type="bullet">
    ///   <item>Creating the HTTP handler from the test server</item>
    ///   <item>Applying <see cref="GrpcResponseVersionHandler"/> (fixes TestServer HTTP/1.1 → HTTP/2)</item>
    ///   <item>Creating a <see cref="GrpcChannel"/> with the tracked handler</item>
    ///   <item>Installing the <see cref="GrpcTrackingInterceptor"/></item>
    ///   <item>Constructing the typed gRPC client</item>
    /// </list>
    /// <para>
    /// Usage:
    /// <code>
    /// var client = factory.CreateTestTrackingGrpcClient&lt;Program, Greeter.GreeterClient&gt;(
    ///     new GrpcTrackingOptions
    ///     {
    ///         ServiceName = "My API",
    ///         CallerName = TrackingDefaults.CallerName,
    ///         CurrentTestInfoFetcher = () => (testName, testId)
    ///     });
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the application under test.</typeparam>
    /// <typeparam name="TClient">The generated gRPC client type (must inherit from <see cref="ClientBase"/>).</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="options">gRPC tracking options controlling service naming, verbosity, and test context.</param>
    /// <returns>A tracked gRPC client instance.</returns>
    public static TClient CreateTestTrackingGrpcClient<TEntryPoint, TClient>(
        this WebApplicationFactory<TEntryPoint> factory,
        GrpcTrackingOptions options)
        where TEntryPoint : class
        where TClient : ClientBase
    {
        // the server under the factory's lock, as every client Kronikol makes from a factory: two tests reaching a
        // factory's server first at once each started the application
        var channel = global::Kronikol.WebApplicationFactoryExtensions.OneAtATime(factory, () =>
        {
            options.HttpContextAccessor ??= factory.Services.GetService<IHttpContextAccessor>();
            return GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
            {
                HttpHandler = new GrpcResponseVersionHandler(factory.Server.CreateHandler())
            });
        });
        var invoker = channel.Intercept(new GrpcTrackingInterceptor(options, options.HttpContextAccessor));
        return (TClient)Activator.CreateInstance(typeof(TClient), invoker)!;
    }

    /// <summary>
    /// Creates a gRPC client with tracking and returns the underlying <see cref="GrpcChannel"/>
    /// so it can be disposed when the test completes. Otherwise identical to
    /// <see cref="CreateTestTrackingGrpcClient{TEntryPoint,TClient}"/>.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the application under test.</typeparam>
    /// <typeparam name="TClient">The generated gRPC client type (must inherit from <see cref="ClientBase"/>).</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="options">gRPC tracking options.</param>
    /// <returns>A tuple of the tracked gRPC client and the underlying channel (for disposal).</returns>
    public static (TClient Client, GrpcChannel Channel) CreateTestTrackingGrpcClientWithChannel<TEntryPoint, TClient>(
        this WebApplicationFactory<TEntryPoint> factory,
        GrpcTrackingOptions options)
        where TEntryPoint : class
        where TClient : ClientBase
    {
        // the server under the factory's lock, as every client Kronikol makes from a factory: two tests reaching a
        // factory's server first at once each started the application
        var channel = global::Kronikol.WebApplicationFactoryExtensions.OneAtATime(factory, () =>
        {
            options.HttpContextAccessor ??= factory.Services.GetService<IHttpContextAccessor>();
            return GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
            {
                HttpHandler = new GrpcResponseVersionHandler(factory.Server.CreateHandler())
            });
        });
        var invoker = channel.Intercept(new GrpcTrackingInterceptor(options, options.HttpContextAccessor));
        var client = (TClient)Activator.CreateInstance(typeof(TClient), invoker)!;
        return (client, channel);
    }
}
