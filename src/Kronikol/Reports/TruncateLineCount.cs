namespace Kronikol.Reports;

/// <summary>
/// The truncate-lines dropdown's preset line counts — one member per dropdown row, in numeric
/// order, with the underlying value equal to the line count it names. This enum is the single
/// source of truth for the dropdown: the report markup builds its <c>&lt;option&gt;</c> list from
/// these members, so a configured default is always one of the states the reader can pick.
/// Off-preset values are unrepresentable by design (adding or removing a preset is a breaking
/// API change, accepted knowingly); an undefined cast like <c>(TruncateLineCount)37</c> fails
/// report generation with an error naming the valid members. The built-in default lives in the
/// resolver: <see cref="Lines40"/>.
/// </summary>
public enum TruncateLineCount
{
    /// <summary>3 lines.</summary>
    Lines3 = 3,
    /// <summary>4 lines.</summary>
    Lines4 = 4,
    /// <summary>5 lines.</summary>
    Lines5 = 5,
    /// <summary>10 lines.</summary>
    Lines10 = 10,
    /// <summary>15 lines.</summary>
    Lines15 = 15,
    /// <summary>20 lines.</summary>
    Lines20 = 20,
    /// <summary>25 lines.</summary>
    Lines25 = 25,
    /// <summary>30 lines.</summary>
    Lines30 = 30,
    /// <summary>35 lines.</summary>
    Lines35 = 35,
    /// <summary>40 lines (the built-in default).</summary>
    Lines40 = 40,
    /// <summary>50 lines.</summary>
    Lines50 = 50,
    /// <summary>60 lines.</summary>
    Lines60 = 60,
    /// <summary>80 lines.</summary>
    Lines80 = 80,
    /// <summary>100 lines.</summary>
    Lines100 = 100
}
