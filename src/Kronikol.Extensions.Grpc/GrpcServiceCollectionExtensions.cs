using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.Grpc;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering gRPC clients
/// with test tracking. These handle the SUT-to-downstream direction where the
/// application under test makes gRPC calls to downstream services and the
/// <see cref="IHttpContextAccessor"/> is auto-resolved from DI so test identity
/// flows through without manual wiring: the client's calls are attributed to the test whose request the
/// application is serving, and carry that identity on to the downstream service
/// (<see cref="GrpcTrackingOptions.PropagateTestIdentity"/>), which attributes its own calls to the same
/// scenario when it calls <c>AddTestTrackingContextPropagation()</c>.
/// </summary>
public static class GrpcServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton gRPC client of type <typeparamref name="TClient"/> with test tracking
    /// enabled. The <see cref="IHttpContextAccessor"/> is automatically resolved from DI when not
    /// explicitly set in <paramref name="configure"/>, following the same pattern as other
    /// Kronikol extensions (BigQuery, Bigtable, MongoDB, etc.).
    /// <para>
    /// Usage in <c>ConfigureTestServices</c>:
    /// <code>
    /// services.AddTrackedGrpcClient&lt;NotificationService.NotificationServiceClient&gt;(
    ///     handler,
    ///     new Uri("http://localhost"),
    ///     opts =&gt;
    ///     {
    ///         opts.ServiceName = "Notification Service";
    ///         opts.CallerName = "My API";
    ///     });
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="TClient">The generated gRPC client type (must inherit from <see cref="ClientBase"/>).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The HTTP message handler for the gRPC channel (e.g. from a downstream test server).</param>
    /// <param name="baseAddress">The base address of the downstream gRPC service.</param>
    /// <param name="configure">Optional callback to configure <see cref="GrpcTrackingOptions"/>.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddTrackedGrpcClient<TClient>(
        this IServiceCollection services,
        HttpMessageHandler handler,
        Uri baseAddress,
        Action<GrpcTrackingOptions>? configure = null)
        where TClient : ClientBase
    {
        services.AddSingleton(sp =>
        {
            var options = new GrpcTrackingOptions();
            configure?.Invoke(options);
            options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();

            var channel = GrpcChannel.ForAddress(baseAddress, new GrpcChannelOptions
            {
                HttpHandler = handler
            });
            var invoker = channel.Intercept(new GrpcTrackingInterceptor(options));
            return (TClient)Activator.CreateInstance(typeof(TClient), invoker)!;
        });

        return services;
    }
}
