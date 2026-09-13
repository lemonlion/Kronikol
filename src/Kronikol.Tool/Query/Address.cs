namespace Kronikol.Tool.Query;

/// <summary>
/// Parses the addresses every command prints and every command accepts.
///
/// <para>Two kinds of address, for two kinds of question. Ordinals — <c>s3</c>, <c>s3/i47</c> — are short
/// enough to print in bulk and are what a listing hands back so the next command can be aimed. Content
/// hashes — <c>b:4bdea521</c> — identify a payload by what it is, so they survive a re-run and are equal
/// across every scenario that saw the same bytes.</para>
///
/// <para>A step is addressed by its <c>stepPath</c> — <c>s3/2</c>, <c>s3/b0</c> for a background step,
/// <c>s3/2.1</c> for an assertion under it — rather than by a scheme of its own, so the address printed
/// beside an interaction is the address that fetches the step it belongs to. A step path always means
/// <b>that step and everything under it</b>: the addresses <c>failures</c> prints are frequently parents,
/// so exact-match would be the wrong answer in the common case rather than in the corner.</para>
///
/// <para><c>sid:1a2b3c4d5e6f7a8b</c> is a scenario by its <c>stableId</c> — the cross-run identity the
/// reference documents tell you to use, printed by <c>steps</c> and handed back by <c>diff</c>'s own
/// refusal message, and until 3.5.0 an address no verb would take. The <c>sid:</c> prefix is mandatory
/// rather than a convenience: <c>diff</c> decides whether its second positional is an address or a report
/// FILE by asking this grammar, so a bare sixteen-hex form would flip a hash-named artifact path from
/// "the new run" to "a body to compare", which is how CI layouts name files.</para>
/// </summary>
internal readonly record struct Address(
    AddressKind Kind,
    int Scenario = -1,
    int Interaction = -1,
    int Diagram = -1,
    int Note = -1,
    string? StepPath = null,
    string? BodyHash = null,
    string? StableId = null)
{
    public static bool TryParse(string text, out Address address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
        {
            address = new Address(AddressKind.Body, BodyHash: text.ToLowerInvariant());
            return true;
        }

        if (text.StartsWith("sid:", StringComparison.OrdinalIgnoreCase))
        {
            var stableId = text[4..].Trim();
            if (stableId.Length == 0)
                return false;

            address = new Address(AddressKind.StableId, StableId: stableId.ToLowerInvariant());
            return true;
        }

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryOrdinal(parts[0], 's', out var scenario))
            return false;

        if (parts.Length == 1)
        {
            address = new Address(AddressKind.Scenario, scenario);
            return true;
        }

        if (TryOrdinal(parts[1], 'i', out var interaction))
        {
            address = new Address(AddressKind.Interaction, scenario, interaction);
            return true;
        }

        if (TryOrdinal(parts[1], 'd', out var diagram))
        {
            if (parts.Length == 2)
            {
                address = new Address(AddressKind.Diagram, scenario, Diagram: diagram);
                return true;
            }

            if (TryOrdinal(parts[2], 'n', out var note))
            {
                address = new Address(AddressKind.Note, scenario, Diagram: diagram, Note: note);
                return true;
            }

            return false;
        }

        // Anything else in the second position is a step path: 2, b0, 2.1.
        if (IsStepPath(parts[1]))
        {
            address = new Address(AddressKind.Step, scenario, StepPath: parts[1]);
            return true;
        }

        return false;
    }

    private static bool TryOrdinal(string text, char prefix, out int value)
    {
        value = -1;
        return text.Length > 1
               && char.ToLowerInvariant(text[0]) == prefix
               && int.TryParse(text[1..], out value)
               && value >= 0;
    }

    private static bool IsStepPath(string text)
    {
        var body = text.StartsWith('b') ? text[1..] : text;
        return body.Length > 0 && body.Split('.').All(part => int.TryParse(part, out _));
    }

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="scope"/> or sits under it.
    /// </summary>
    /// <remarks>
    /// The one definition of what a step path in an address covers, because the alternative is two: the
    /// <c>--step</c> flag used string equality while an address would have to walk the subtree, and one
    /// address form meaning two different things depending on which door it came through is worse than
    /// either. Segment-wise, so <c>1</c> does not swallow <c>10</c>.
    /// </remarks>
    public static bool PathCoveredBy(string? path, string scope) =>
        path is not null
        && (string.Equals(path, scope, StringComparison.Ordinal)
            || path.StartsWith(scope + ".", StringComparison.Ordinal));

    public override string ToString() => Kind switch
    {
        AddressKind.Body => BodyHash ?? "b:?",
        AddressKind.StableId => $"sid:{StableId}",
        AddressKind.Scenario => $"s{Scenario}",
        AddressKind.Interaction => $"s{Scenario}/i{Interaction}",
        AddressKind.Step => $"s{Scenario}/{StepPath}",
        AddressKind.Diagram => $"s{Scenario}/d{Diagram}",
        AddressKind.Note => $"s{Scenario}/d{Diagram}/n{Note}",
        _ => "?"
    };
}

internal enum AddressKind
{
    Scenario,
    Interaction,
    Step,
    Diagram,
    Note,
    Body,
    StableId,
}
