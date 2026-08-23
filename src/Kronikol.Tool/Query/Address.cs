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
/// beside an interaction is the address that fetches the step it belongs to.</para>
/// </summary>
internal readonly record struct Address(
    AddressKind Kind,
    int Scenario = -1,
    int Interaction = -1,
    int Diagram = -1,
    int Note = -1,
    string? StepPath = null,
    string? BodyHash = null)
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

    public override string ToString() => Kind switch
    {
        AddressKind.Body => BodyHash ?? "b:?",
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
}
