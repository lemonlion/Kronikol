namespace Kronikol.Parties;

/// <summary>
/// The parties a run declared test doubles, for the diagrams it draws (#190, Q11): each such participant carries the
/// stereotype «test double» in the sequence and component diagrams. Only a declaration draws it, so no suite's diagrams
/// change until it declares a double. Ambient, as the report's diagnostics are, because the diagrams are drawn through
/// public methods whose parameter lists a new option must not grow.
/// </summary>
internal static class DiagramParties
{
    /// <summary>What a declared double's participant carries.</summary>
    internal const string Stereotype = "<<test double>>";

    private static readonly AsyncLocal<IReadOnlySet<string>?> Doubles = new();

    /// <summary>Declares <paramref name="doubles"/> for what is drawn until the returned scope is disposed.</summary>
    internal static IDisposable Declare(IReadOnlySet<string>? doubles)
    {
        var before = Doubles.Value;
        Doubles.Value = doubles;
        return new Scope(before);
    }

    internal static bool IsDeclaredDouble(string? name) => name is not null && Doubles.Value?.Contains(name) == true;

    /// <summary>The stereotype a participant declaration carries after its alias, or nothing.</summary>
    internal static string StereotypeOf(string? name) => IsDeclaredDouble(name) ? " " + Stereotype : "";

    private sealed class Scope(IReadOnlySet<string>? before) : IDisposable
    {
        public void Dispose() => Doubles.Value = before;
    }
}
