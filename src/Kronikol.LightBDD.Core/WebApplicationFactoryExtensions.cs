using Microsoft.AspNetCore.Mvc.Testing;
using Kronikol.Tracking;

namespace Kronikol.LightBDD;

/// <summary>
/// Provides extension methods for creating test-tracking HTTP clients from <c>WebApplicationFactory</c> in LightBDD tests.
/// </summary>
[Obsolete("Use Kronikol.WebApplicationFactoryExtensions instead. This wrapper will be removed in a future version.")]
public static class WebApplicationFactoryExtensions
{
    public static HttpClient CreateTestTrackingClient<T>(this WebApplicationFactory<T> factory, LightBddTestTrackingMessageHandlerOptions options) where T : class
    {
        return global::Kronikol.WebApplicationFactoryExtensions.OneAtATime(factory, () => factory.CreateDefaultClient(new TestTrackingMessageHandler(options)));
    }

    public static HttpClient CreateTestTrackingClient<T>(this WebApplicationFactory<T> factory, LightBddTestTrackingMessageHandlerOptions options, params DelegatingHandler[] additionalHandlers) where T : class
    {
        var handlers = new DelegatingHandler[additionalHandlers.Length + 1];
        handlers[0] = new TestTrackingMessageHandler(options);
        additionalHandlers.CopyTo(handlers, 1);
        return global::Kronikol.WebApplicationFactoryExtensions.OneAtATime(factory, () => factory.CreateDefaultClient(handlers));
    }
}