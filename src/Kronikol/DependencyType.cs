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
    /// <summary>A person acting on the system — the caller of UI actions (see <c>DependencyCategories.User</c>).</summary>
    User,
    Unknown
}
