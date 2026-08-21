namespace Kronikol;

/// <summary>
/// Broad classification of a dependency for visual differentiation in diagrams.
/// </summary>
public enum DependencyType
{
    HttpApi,
    Database,
    Cache,
    MessageQueue,
    Storage,
    /// <summary>An AI / large-language-model provider (see <c>DependencyCategories.AI</c>).</summary>
    AI,
    Unknown
}
