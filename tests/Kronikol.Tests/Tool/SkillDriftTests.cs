using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tool.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// Pins the agent-facing documentation to the tool it documents. A skill that describes a command the
/// tool no longer has, or omits one it grew, is worse than no skill: the agent spends its turn on a
/// command that exits 2, or never learns the command that would have answered the question.
///
/// <para><b>Why the matching is strict.</b> The obvious test - <c>Assert.Contains(verb, skillText)</c> -
/// passes on day one and proves nothing, because <c>body</c>, <c>diagram</c>, <c>steps</c>, <c>flow</c>,
/// <c>values</c>, <c>trace</c>, <c>note</c>, <c>grep</c> and <c>summary</c> are all ordinary English words
/// in these documents. It is the bug class fixed in 3.0.82, where a bare <c>Contains</c> over a whole
/// report could not tell markup from a CSS selector naming the same string. So a verb counts only when it
/// is <i>demonstrated as a command</i>: inside a code span or a fenced block, as a whole word, not
/// preceded by a hyphen - which would otherwise let the flag <c>--grep</c> stand in for the verb
/// <c>grep</c>. Written that way the test started red on <c>body</c> and <c>diagram</c>, which SKILL.md
/// mentioned only as English nouns.</para>
///
/// <para>The flag half is a forward guard rather than a discovered gap: the reference and the parser
/// already agreed exactly when this was written. It stays because the next flag added to one of them is
/// the one that will not be added to the other.</para>
/// </summary>
public class SkillDriftTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string CanonicalSkillDir =>
        Path.Combine(RepoRoot, "templates", "skills", "kronikol-test-debugging");

    private static string Skill => File.ReadAllText(Path.Combine(CanonicalSkillDir, "SKILL.md"));

    private static string Reference =>
        File.ReadAllText(Path.Combine(CanonicalSkillDir, "references", "commands.md"));

    private static string RenderedUsage()
    {
        var writer = new StringWriter();
        QueryCommand.PrintUsage(writer);
        return writer.ToString();
    }

    /// <summary>
    /// The verbs, read out of the rendered help rather than out of the source. <c>QueryCommand.Run</c>
    /// dispatches on a switch <i>expression</i>, which no reflection can enumerate, and a private array
    /// shared by the switch and the help text would let the help drift from what the array promises.
    /// Parsing the rendered text is the only reading that catches both.
    ///
    /// <para>The count is asserted so that a verb whose help line stops matching the shape - the longest
    /// name, <c>interactions</c>, already leaves only a single space before <c>&lt;report&gt;</c> - fails
    /// here rather than silently shrinking every check below it into a weaker one.</para>
    /// </summary>
    internal static IReadOnlyList<string> UsageVerbs()
    {
        var verbs = Regex.Matches(RenderedUsage(), @"^  ([a-z][a-z-]*) +<report>", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(18, verbs.Count);
        return verbs;
    }

    /// <summary>
    /// Every fenced block whole, plus every inline code span outside a fence. Prose is deliberately left
    /// out: prose is where the English nouns live.
    /// </summary>
    private static IReadOnlyList<string> CodeContexts(string markdown)
    {
        var fence = new Regex(@"^```.*?^```", RegexOptions.Singleline | RegexOptions.Multiline);
        var contexts = fence.Matches(markdown).Select(m => m.Value).ToList();
        contexts.AddRange(Regex.Matches(fence.Replace(markdown, "\n"), @"`([^`\n]+)`").Select(m => m.Groups[1].Value));
        return contexts;
    }

    private static bool UsedAsCommand(string word, IReadOnlyList<string> contexts)
    {
        var pattern = new Regex(@"(?<![-\w.$/])" + Regex.Escape(word) + @"(?![-\w])");
        return contexts.Any(c => pattern.IsMatch(c));
    }

    [Fact]
    public void Every_query_verb_is_demonstrated_as_a_command_in_the_skill()
    {
        var contexts = CodeContexts(Skill);
        var missing = UsageVerbs().Where(v => !UsedAsCommand(v, contexts)).ToList();

        Assert.True(missing.Count == 0,
            "SKILL.md never shows these verbs being run, so an agent reading it will not know they exist: "
            + string.Join(", ", missing)
            + ". Naming the word in prose does not count - add a recipe row or a code example.");
    }

    [Fact]
    public void Every_query_verb_has_an_entry_in_the_flag_reference()
    {
        var missing = UsageVerbs()
            .Where(v => !Regex.IsMatch(Reference, "^### `" + Regex.Escape(v) + @"[`\s]", RegexOptions.Multiline))
            .ToList();

        Assert.True(missing.Count == 0,
            "references/commands.md has no heading for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_verb_the_reference_documents_is_a_real_command()
    {
        var documented = Regex.Matches(Reference, @"^### `([a-z][a-z-]*)[`\s]", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

        var invented = documented.Except(UsageVerbs(), StringComparer.Ordinal).ToList();

        Assert.True(invented.Count == 0,
            "references/commands.md documents commands the tool does not have: " + string.Join(", ", invented));
    }

    /// <summary>
    /// Flags are read out of the reference's code contexts and put through the real parser. Regexing
    /// <c>QueryOptions.cs</c> for <c>case "--x":</c> on both sides would only prove a file matches itself;
    /// a parse round-trip proves the tool accepts what the document tells an agent to type.
    /// </summary>
    private static IReadOnlyList<string> DocumentedFlags() =>
        CodeContexts(Reference)
            .SelectMany(c => Regex.Matches(c, @"--[a-z][a-z-]*").Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Every_flag_the_reference_documents_is_known_to_the_parser()
    {
        var unknown = new List<string>();
        foreach (var flag in DocumentedFlags())
        {
            // The question is whether the parser KNOWS the flag, not whether it likes this value: `--lines`
            // wants a range, `--slower-than` a number. A flag that reads its value and complains about it
            // has still been recognised; only "Unknown option" means the document invented it.
            var error = new StringWriter();
            QueryOptions.Parse([flag, "1"], error);
            if (error.ToString().Contains("Unknown option", StringComparison.Ordinal))
                unknown.Add(flag);
        }

        Assert.True(unknown.Count == 0,
            "references/commands.md tells an agent to type flags the tool does not have: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Every_flag_the_parser_accepts_is_documented_in_the_reference()
    {
        var parsed = Regex.Matches(
                File.ReadAllText(Path.Combine(RepoRoot, "src", "Kronikol.Tool", "Query", "QueryOptions.cs")),
                @"case ""(--[a-z][a-z-]*)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

        var undocumented = parsed.Except(DocumentedFlags(), StringComparer.Ordinal).ToList();

        Assert.True(undocumented.Count == 0,
            "the tool accepts flags the skill never mentions, so no agent will use them: "
            + string.Join(", ", undocumented));
    }

    /// <summary>
    /// Finds the line that enumerates something, and checks the enumeration against the tool's own array.
    ///
    /// <para>Anchored on the line rather than on the whole document, because a bare
    /// <c>Contains("`steps`")</c> is satisfied by the sentence "those are already in `steps`" and
    /// <c>Contains("bodies")</c> by the section title "Aggregation (reads bodies freely, ...)" - five of
    /// nine dimensions and four of six targets passed that way without appearing in any list at all. The
    /// bug class the class docstring cites, committed inside the fact that cites it.</para>
    /// </summary>
    private static void AssertLineEnumerates(string document, string lineMarker, IEnumerable<string> expected,
        string what)
    {
        var line = document.Split('\n').FirstOrDefault(l => l.Contains(lineMarker, StringComparison.Ordinal));
        Assert.True(line is not null, $"no line containing {lineMarker} - {what} is no longer enumerated anywhere.");

        // The enumeration may wrap, so the following line counts as part of it.
        var lines = document.Split('\n').ToList();
        var index = lines.IndexOf(line!);
        var window = line + (index + 1 < lines.Count ? lines[index + 1] : "");

        var missing = expected.Where(e => !window.Contains(e, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"{what} missing from the {lineMarker} line: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_reference_enumerates_every_group_by_dimension_the_tool_accepts()
    {
        AssertLineEnumerates(Reference, "Dimensions (comma list", QueryCommand.GroupByDimensions,
            "--group-by dimensions");
    }

    [Fact]
    public void The_reference_enumerates_every_grep_target_the_tool_accepts()
    {
        AssertLineEnumerates(Reference, "`--in` picks the targets", QueryCommand.GrepTargets, "--in targets");
    }

    [Fact]
    public void The_help_text_enumerates_every_group_by_dimension_and_grep_target()
    {
        var usage = RenderedUsage();

        AssertLineEnumerates(usage, "service method status path", QueryCommand.GroupByDimensions,
            "--group-by dimensions");
        AssertLineEnumerates(usage, "[--in ", QueryCommand.GrepTargets, "--in targets");
    }

    /// <summary>
    /// Every <c>!</c> line the tool can print, read out of the source rather than listed by hand.
    ///
    /// <para>The hand-written version of this fact was worthless twice over: it asserted three strings a
    /// person had typed, so the direction that matters - the tool grows a banner and the skill does not -
    /// could never fail it; and the three it named were only the ones <c>WriteProvenance</c> emits, while
    /// six more live in individual commands. SKILL.md said "three of them exist", which was false about
    /// the shipped tool, and the fact meant to guard that sentence reported green. Worse for <c>diff</c>,
    /// which <c>WriteProvenance</c> skips entirely: every banner a diff can lead with was one of the six
    /// the skill said did not exist.</para>
    /// </summary>
    private static IReadOnlyList<string> BannerPhrases()
    {
        var phrases = new List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "src", "Kronikol.Tool"), "*.cs"))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"(?:Line|Note)\(\$?""! (.*)$", RegexOptions.Multiline))
            phrases.Add(Anchor(match.Groups[1].Value));

        // The two the tool composes at run time, keyed on the stable half of each message.
        phrases.Add("recorded no result");
        phrases.Add(Anchor(ParameterCaptureHint.Message));

        return phrases;
    }

    /// <summary>
    /// The wording of one banner that SKILL.md has to contain: the longest run of literal text in it.
    ///
    /// <para>An interpolated banner is the reason this is not simply the string. <c>$"! {n} scenarios
    /// share a stableId"</c> has no fixed prefix, and the previous extraction matched only
    /// non-interpolated calls - so three banners were exempt from the fact that exists to catch exactly
    /// this, and the skill documented none of them. The holes are cut out, the longest surviving run
    /// wins, and only then is the trailing advice trimmed - and only while something substantial is left,
    /// because in <c>! spans {n} scenarios (...) - shared state or fixture leakage</c> the distinctive
    /// wording is the advice.</para>
    /// </summary>
    private static string Anchor(string source)
    {
        var withoutHoles = Regex.Replace(source, @"\{[^{}]*\}", "\u0001");

        // The C# literal ends at the first quote that is not escaped; a hole's own quotes went with it.
        var quote = Regex.Match(withoutHoles, @"(?<!\\)""");
        if (quote.Success)
            withoutHoles = withoutHoles[..quote.Index];

        var longest = withoutHoles.Split('\u0001')
            .Select(part => part.Replace("\\\"", "\"", StringComparison.Ordinal).Trim())
            .OrderByDescending(part => part.Length)
            .First();

        // The leading clause where there is one: what follows an em dash is advice and what follows a
        // bracket is detail, so pinning a whole sentence would turn every wording tweak into a failure.
        var trimmed = longest.Split('\u2014')[0].Split('(')[0].Trim();
        var banner = trimmed.Length >= 15 ? trimmed : longest.Trim('\u2014', ')', '(', ' ', '.', ',');

        return banner.Length > 55 ? banner[..55].Trim() : banner;
    }

    [Fact]
    public void Every_banner_the_tool_can_print_is_explained_in_the_skill()
    {
        var skill = Skill;
        var missing = BannerPhrases()
            .Where(p => !skill.Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            "the tool prints these ! lines and SKILL.md explains none of them, so an agent that reads one "
            + "has no rule for it: " + string.Join(" | ", missing));
    }

    [Fact]
    public void The_skill_does_not_claim_a_count_of_banners_it_cannot_keep_true()
    {
        // The previous wording said "Three of them exist". A hard-coded count in prose is a promise the
        // next release breaks silently, so the section is organised by category instead.
        Assert.DoesNotContain("Three of them exist", Skill, StringComparison.OrdinalIgnoreCase);
        Assert.True(BannerPhrases().Count >= 8,
            "the extraction found almost nothing - the shape of the banner call must have changed, and this "
            + "fact is now guarding an empty list.");
    }
}
