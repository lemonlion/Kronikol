namespace Kronikol.PlantUml;

/// <summary>
/// Detects GraphQL operations (query, mutation, subscription) from HTTP request bodies
/// and extracts operation type and name for diagram arrow labels.
/// </summary>
public static class GraphQlOperationDetector
{
    /// <summary>
    /// Attempts to detect a GraphQL request body and extract a human-readable label
    /// (e.g. "query GetUser", "mutation CreateOrder", "subscription OnMessage", or just "query" for anonymous).
    /// A persisted query sent by its hash alone is labelled by its operation name ("GetUser").
    /// Returns null if the content is not a GraphQL request: the body's top level is read as JSON, so a key named
    /// <c>query</c> in the variables is not the query, and the query text must parse as a GraphQL document.
    /// </summary>
    public static string? TryExtractLabel(string? content) =>
        GraphQlOperationReader.FromBody(content)?.DetectorLabel is { Length: > 0 } label ? label : null;
}
