using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;

namespace Kronikol;

/// <summary>
/// Extension methods for creating test HTTP clients that record requests for diagram generation.
/// </summary>
public static class WebApplicationFactoryExtensions
{
    /// <summary>
    /// Creates an <see cref="HttpClient"/> that records all outgoing requests and responses
    /// for use in generated sequence diagrams.
    /// </summary>
    /// <typeparam name="T">The entry point class of the application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="options">Options controlling service naming and test context resolution.</param>
    /// <returns>An HTTP client with test-tracking middleware installed.</returns>
    public static HttpClient CreateTestTrackingClient<T>(this WebApplicationFactory<T> factory, TestTrackingMessageHandlerOptions options) where T : class
    {
        return OneAtATime(factory, () =>
        {
            options.HttpContextAccessor ??= factory.Services.GetService<IHttpContextAccessor>();
            return factory.CreateDefaultClient(TestClientHandler<T>(options));
        });
    }

    /// <summary>The test client's handler, registered as the test's (#190, O1): its caller is the test, and whom it calls the service under test.</summary>
    internal static TestTrackingMessageHandler TestClientHandler<T>(TestTrackingMessageHandlerOptions options) where T : class =>
        new(options) { TestClient = Parties.TestClientRegistry.Register(options.CallerName, typeof(T).Assembly.GetName().Name) };

    /// <summary>
    /// Creates an <see cref="HttpClient"/> that records all outgoing requests and responses,
    /// with additional delegating handlers in the pipeline.
    /// </summary>
    /// <typeparam name="T">The entry point class of the application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="options">Options controlling service naming and test context resolution.</param>
    /// <param name="additionalHandlers">Additional delegating handlers to include in the HTTP pipeline after the tracking handler.</param>
    /// <returns>An HTTP client with test-tracking middleware and additional handlers installed.</returns>
    public static HttpClient CreateTestTrackingClient<T>(this WebApplicationFactory<T> factory, TestTrackingMessageHandlerOptions options, params DelegatingHandler[] additionalHandlers) where T : class
    {
        return OneAtATime(factory, () =>
        {
            options.HttpContextAccessor ??= factory.Services.GetService<IHttpContextAccessor>();
            var handlers = new DelegatingHandler[additionalHandlers.Length + 1];
            handlers[0] = TestClientHandler<T>(options);
            additionalHandlers.CopyTo(handlers, 1);
            return factory.CreateDefaultClient(handlers);
        });
    }

    // A WebApplicationFactory starts its application on first use and keeps the clients it makes in a list, both with
    // no lock. Tests running in parallel share one factory and make their clients at the same moment (a Reqnroll suite's
    // features, each in a [BeforeScenario] hook), so each of the first clients started its own copy of the application,
    // and a client could be lost from the list or leave a null in it, which made the factory's Dispose throw
    // NullReferenceException. Everything here that makes a client or reaches the server does it under a lock of the
    // factory's own, held as long as the factory lives.
    private static readonly ConditionalWeakTable<object, object> FactoryLocks = new();

    internal static TResult OneAtATime<TResult>(object factory, Func<TResult> make)
    {
        lock (FactoryLocks.GetValue(factory, _ => new object()))
            return make();
    }
}
