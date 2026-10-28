using System.Text.RegularExpressions;

namespace Kronikol.History;

/// <summary>
/// One consumer templating rule (<see cref="ReportConfigurationOptions.HistoryShapeTemplates"/>): what in a
/// call's path or statement head is variable in THIS application, and what to write in its place.
/// </summary>
/// <param name="Pattern">A .NET regular expression, matched culture-invariantly against the path (before its query string) as the data file writes it, unescaped, and against a statement head.</param>
/// <param name="Placeholder">
/// What replaces each match. Conventionally a name in braces, <c>{report-key}</c>; it is a replacement
/// pattern, so <c>$1</c> keeps a group of the match (<c>key-$1-{rest}</c>).
/// </param>
public sealed record HistoryShapeTemplate(string Pattern, string Placeholder);

/// <summary>
/// The consumer's templating rules, compiled: applied in order, before the built-in rules of
/// <see cref="InteractionShape"/>. Cache-key formats are the application's own, and no built-in rule
/// covers them all; a key holding a tenant, a hash and a period reads as a different call on every run
/// until somebody who knows the format says which part varies.
///
/// <para>The expressions are the consumer's, so nothing here can fail a test run: a pattern that does
/// not compile is skipped, and one that runs past its time limit on some text is skipped for that text.
/// Both are said once, in <see cref="Problems"/>, which the run reports as a diagnostic.</para>
///
/// <para>The rules have a hash, recorded on the run line beside the rule version, because a fingerprint
/// is comparable only with one the same rules made: editing a rule costs one quiet run instead of
/// calling every scenario changed once.</para>
/// </summary>
public sealed partial class HistoryShapeRules
{
    /// <summary>How long one rule may spend on one text before it is skipped for it.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(250);

    private readonly (Regex Expression, string Placeholder, string Pattern)[] _rules;
    private readonly List<string> _problems = [];
    private readonly HashSet<string> _timedOut = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private HistoryShapeRules(IReadOnlyList<HistoryShapeTemplate> templates, TimeSpan timeout)
    {
        // The hash is over what was configured, compiled or not: it names the configuration.
        Hash = InteractionShape.Hash8(string.Join("\n", templates.Select(t => t.Pattern + "\u001f" + t.Placeholder)));
        Templates = [.. templates];

        var rules = new List<(Regex, string, string)>(templates.Count);
        foreach (var template in templates)
        {
            try
            {
                rules.Add((new Regex(template.Pattern, RegexOptions.CultureInvariant, timeout), template.Placeholder ?? "", template.Pattern));
            }
            catch (ArgumentException exception)
            {
                _problems.Add($"the HistoryShapeTemplates pattern {template.Pattern} does not compile and was skipped: {exception.Message}");
                continue;
            }
            if (UnescapedRun(template.Pattern) is { } run)
                _problems.Add($"the HistoryShapeTemplates pattern {template.Pattern} holds the escape {run.Escape}, which a path no longer holds: the rules see the path as the data file writes it, where {run.Escape} is written as '{run.Text}', so match that instead");
        }
        _rules = [.. rules];
    }

    /// <summary>
    /// The first run of <c>%XX</c> escapes in a pattern that the data file's string writes as the text it stands for
    /// (<c>%20</c> as a space, <c>%C3%A9</c> as <c>é</c>), or null. Asked of <see cref="Uri"/> itself, which writes that
    /// string: a reserved character's escape (<c>%2F</c>, <c>%25</c>) and a control character's stay as written.
    /// </summary>
    private static (string Escape, string Text)? UnescapedRun(string pattern)
    {
        foreach (Match match in EscapeRun().Matches(pattern))
        {
            var written = new Uri("http://localhost/" + match.Value).ToString();
            if (!written.EndsWith(match.Value, StringComparison.OrdinalIgnoreCase))
                return (match.Value, written["http://localhost/".Length..]);
        }
        return null;
    }

    [GeneratedRegex("(?:%[0-9A-Fa-f]{2})+", RegexOptions.CultureInvariant)]
    private static partial Regex EscapeRun();

    /// <summary>The rules compiled, or null when there are none.</summary>
    /// <param name="templates">The rules, in the order they apply.</param>
    /// <param name="timeout">How long one rule may spend on one text; <see cref="DefaultTimeout"/> when null.</param>
    public static HistoryShapeRules? Create(IEnumerable<HistoryShapeTemplate>? templates, TimeSpan? timeout = null)
    {
        var list = templates?.Where(t => t is not null && !string.IsNullOrEmpty(t.Pattern)).ToArray() ?? [];
        return list.Length == 0 ? null : new HistoryShapeRules(list, timeout ?? DefaultTimeout);
    }

    /// <summary>The first eight hex of SHA-256 over the configured rules, in order: what the run line records as <c>shapeRules</c>.</summary>
    public string Hash { get; }

    /// <summary>The rules as configured, in order, compiled or not: what the run line records beside <see cref="Hash"/>.</summary>
    internal IReadOnlyList<HistoryShapeTemplate> Templates { get; }

    /// <summary>What went wrong with a rule, each said once: a pattern that does not compile, a rule that timed out.</summary>
    public IReadOnlyList<string> Problems
    {
        get { lock (_gate) return [.. _problems]; }
    }

    /// <summary>The text with every rule applied, in order.</summary>
    internal string Apply(string text)
    {
        foreach (var (expression, placeholder, pattern) in _rules)
        {
            try
            {
                text = expression.Replace(text, placeholder);
            }
            catch (RegexMatchTimeoutException)
            {
                lock (_gate)
                    if (_timedOut.Add(pattern))
                        _problems.Add($"the HistoryShapeTemplates pattern {pattern} timed out after {expression.MatchTimeout.TotalMilliseconds:0} ms and was skipped for the text it timed out on; a pattern that backtracks is usually one nested quantifier away from not doing so");
            }
        }
        return text;
    }
}
