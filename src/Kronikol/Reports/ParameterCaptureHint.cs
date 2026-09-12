using System.Text.RegularExpressions;
using Kronikol.Constants;

namespace Kronikol.Reports;

/// <summary>
/// Detects the one dead end a report cannot answer its way out of: a SQL statement captured with
/// placeholders and no parameter values. <c>WHERE id = @p0</c> tells a reader nothing about which order
/// was asked for, the values are not anywhere in the report, and no further query will find them — so
/// the only useful reply is "turn the capture on and run it again".
///
/// <para>The reply has to name <b>two</b> settings. <c>LogParameters</c> alone changes nothing: the
/// <c>-- Parameters:</c> block is appended only at <see cref="Sql.SqlTrackingVerbosityLevel.Raw"/> and the
/// default is <c>Detailed</c>. And the EF Core interceptor has no parameter capture at all while stamping
/// the same <c>SQL</c> category as Dapper, so the text says so rather than sending an EF Core user to a
/// property that does not exist.</para>
///
/// <para>The hint never appears on the console pointer or the CI annotation — those carry paths, counts,
/// stableIds and scenario names only (LLM_FRIENDLY_PLAN §3.3). Its homes are <c>Failures.md</c> and
/// <c>kronikol query failures</c>.</para>
/// </summary>
public static class ParameterCaptureHint
{
    /// <summary>What the report says when it finds an unparameterised statement. One sentence, two settings.</summary>
    public const string Message =
        "some SQL was captured with placeholders and no values (@p0, $1, :name, ?) — the arguments are not in this "
        + "report. Set both LogParameters = true and Verbosity = Raw on the tracker and re-run; the EF Core "
        + "interceptor captures no parameters at all, so its statements stay as they are.";

    /// <summary>The exact block all three trackers append. Its presence means the values were captured.</summary>
    private const string CapturedMarker = "\n-- Parameters: ";

    /// <summary>
    /// The categories whose content is SQL-shaped. Deliberately a closed list: a <c>?</c> in a URI query
    /// string, a <c>:</c> in <c>host:8080</c>, an <c>@</c> in an email literal and a <c>$1</c> in shell-ish
    /// text would all trip a placeholder scan run over everything.
    /// </summary>
    private static readonly HashSet<string> SqlShaped = new(StringComparer.OrdinalIgnoreCase)
    {
        DependencyCategories.SQL, DependencyCategories.Database, DependencyCategories.PostgreSQL,
        DependencyCategories.SqlServer, DependencyCategories.MySQL, DependencyCategories.SQLite,
        DependencyCategories.Oracle, DependencyCategories.ClickHouse, DependencyCategories.Spanner,
        DependencyCategories.BigQuery
    };

    // @p0 / @orderId, $1, :name, and a bare ? used as a positional placeholder. `??` is excluded so a
    // C#-ish literal in a statement comment cannot trip it.
    private static readonly Regex Placeholder = new(
        @"(?<![\w@])@[A-Za-z_]\w*|(?<!\w)\$\d+|(?<![\w:]):[A-Za-z_]\w*|(?<![?\w])\?(?!\?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when this captured statement names placeholders it never fills in. False for anything that is
    /// not SQL-shaped, anything carrying the captured-parameters block, and anything with its values inline.
    /// </summary>
    public static bool Applies(string? dependencyCategory, string? content) =>
        IsSqlShaped(dependencyCategory) && ContentLacksParameters(content);

    /// <summary>
    /// Whether this dependency category's content is SQL-shaped. Separate from
    /// <see cref="ContentLacksParameters"/> because <c>kronikol query</c> knows the category from its index
    /// but has to read the statement from disk, and the cheap half decides whether the expensive half runs.
    /// </summary>
    public static bool IsSqlShaped(string? dependencyCategory) =>
        dependencyCategory is not null && SqlShaped.Contains(dependencyCategory);

    /// <summary>Whether a captured statement names placeholders and never says what they were.</summary>
    public static bool ContentLacksParameters(string? content)
    {
        if (content is null || content.Length == 0)
            return false;
        if (content.Contains(CapturedMarker, StringComparison.Ordinal))
            return false;

        return Placeholder.IsMatch(content);
    }
}
