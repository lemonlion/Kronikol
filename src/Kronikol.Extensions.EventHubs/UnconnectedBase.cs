using Azure.Core;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;

namespace Kronikol.Extensions.EventHubs;

/// <summary>
/// What a tracking client builds the Event Hubs client it derives from out of. The clients' names, identifier and
/// <c>IsClosed</c> are not virtual, so code holding a tracking client as the SDK type reads them from the base: built
/// from the names and identifier of the client the tracking client wraps, the base answers as that client does, and it
/// is closed with it. The base never connects. Each call it could make goes to the client it wraps, and its credential,
/// which the SDK asks for a token before it opens a connection, fails at once a call that reaches the base
/// (<c>plans/CLIENT_FLOORS_PLAN.md</c> R2b).
/// </summary>
internal static class UnconnectedBase
{
    /// <summary>The name a base takes where the client it wraps has none to give, as one built through the constructor
    /// for mocking has not: its names read a connection it does not have.</summary>
    internal const string Unknown = "unknown";

    internal static TokenCredential Credential { get; } = new UnusedCredential();

    internal static (string Namespace, string EventHub, string? Identifier) NamesOf(EventHubProducerClient inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return (NameOr(() => inner.FullyQualifiedNamespace), NameOr(() => inner.EventHubName), inner.Identifier);
    }

    internal static (string ConsumerGroup, string Namespace, string EventHub, string? Identifier) NamesOf(EventHubConsumerClient inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return (NameOr(() => inner.ConsumerGroup), NameOr(() => inner.FullyQualifiedNamespace), NameOr(() => inner.EventHubName),
            inner.Identifier);
    }

    // A name the client cannot give is no reason to refuse to wrap it: a mock answers each call the test sets up.
    private static string NameOr(Func<string?> read)
    {
        try
        {
            return read() is { Length: > 0 } name ? name : Unknown;
        }
        catch (Exception)
        {
            return Unknown;
        }
    }

    private sealed class UnusedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw Unused();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromException<AccessToken>(Unused());

        private static InvalidOperationException Unused() => new(
            "This call reached the Event Hubs client a Kronikol tracking client derives from, which never connects, rather " +
            "than the client the tracking client wraps. Make the call on the tracking client's Inner.");
    }
}
