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
    /// The verbs, read out of the rendered help and held equal to <see cref="VerbTable"/>. Since 3.7.0 the
    /// table is the one source - the help is rendered from it, the dispatch refuses anything outside it,
    /// and <c>--describe</c> prints it - so the reading here exists for one reason: a verb whose usage
    /// line stops matching the shape (the longest name, <c>interactions</c>, leaves a single space before
    /// <c>&lt;report&gt;</c>) fails here rather than silently shrinking every check below into a weaker
    /// one. Equality with the table replaces the literal count the previous version asserted, which was
    /// a promise the next verb broke.
    /// </summary>
    internal static IReadOnlyList<string> UsageVerbs()
    {
        var verbs = Regex.Matches(RenderedUsage(), @"^  ([a-z][a-z-]*) +<report>", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(VerbTable.Names, verbs);
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
            // A `dotnet test --filter ...` line is another program's command line, quoted because the tool
            // prints it (`repro`, `failures`); its flags are not something the document tells an agent to
            // type to kronikol. Only that one program is exempt, by name - a bare `--flag` anywhere else
            // in a code context is still held to the parser.
            .Select(c => Regex.Replace(c, @"dotnet test\b[^\n`]*", ""))
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
    /// Vocabulary is not legality. Every flag below is a real flag and every verb below is a real verb,
    /// and <c>kronikol query failures &lt;report&gt; --service X</c> is still a command the tool refuses —
    /// so a reference that assembles the two independently can tell an agent to type something that
    /// cannot run. This reads whole command lines and puts each one through the same table the tool
    /// enforces.
    /// </summary>
    [Fact]
    public void Every_command_line_the_reference_prints_uses_flags_that_verb_reads()
    {
        var illegal = new List<string>();

        foreach (var line in Reference.ReplaceLineEndings("\n").Split('\n'))
            foreach (Match invocation in Regex.Matches(line, @"kronikol query ([a-z][a-z-]*)([^\n`|]*)"))
            {
                var verb = invocation.Groups[1].Value;
                if (!QueryCommand.FlagsByVerb.TryGetValue(verb, out var legal))
                    continue;

                foreach (Match flag in Regex.Matches(invocation.Groups[2].Value, @"--[a-z][a-z-]*"))
                    if (!legal.Contains(flag.Value, StringComparer.Ordinal)
                        && !QueryCommand.UniversalFlags.Contains(flag.Value, StringComparer.Ordinal))
                        illegal.Add($"{verb} {flag.Value}  —  {line.Trim()}");
            }

        Assert.True(illegal.Count == 0,
            "references/commands.md tells an agent to type commands the tool refuses:\n  " + string.Join("\n  ", illegal));
    }

    [Fact]
    public void Every_flag_the_parser_has_a_case_for_is_listed_in_KnownFlags()
    {
        // The other direction is covered by a parse round-trip in QueryCommandTests. This one keeps the
        // list from falling behind the switch, which would drop a real flag out of the legality table and
        // so refuse it on every verb.
        var cases = Regex.Matches(
                File.ReadAllText(Path.Combine(RepoRoot, "src", "Kronikol.Tool", "Query", "QueryOptions.cs")),
                @"case ""(--[a-z][a-z-]*)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

        var missing = cases.Except(QueryOptions.KnownFlags, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "QueryOptions.KnownFlags has fallen behind the parser: " + string.Join(", ", missing));
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

        // Any string literal that opens with `! `, wherever it sits. Keying on `Note($"! ` instead - the
        // shape the banners happened to be written in - left a banner invisible the moment one was
        // written as a switch arm rather than as the direct argument of the call, which is precisely the
        // drift this fact exists to catch.
        // Recursively: `Query/` is a subdirectory, so a top-directory-only enumeration could not see
        // QueryWriter or QueryOptions - two files that between them hold the pager's own caveats.
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "src", "Kronikol.Tool"), "*.cs", SearchOption.AllDirectories))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\$?""! (.*)$", RegexOptions.Multiline))
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

    /// <summary>
    /// The emitted <c>Reports/CLAUDE.md</c>, checked the same way the skill and the reference are.
    ///
    /// <para>It was outside every one of these guards, and it is the copy that matters most: the skill
    /// ships with the templates and is installed on purpose, while this file is written beside every
    /// report on every run and is what an agent loads by walking into the directory. A command in it that
    /// the tool refuses costs the reader their first move, at the moment they have the least context.</para>
    ///
    /// <para>Only one direction is checked. Every verb it names must be real; it need NOT name every verb,
    /// because it is deliberately a short ladder rather than a reference, and requiring completeness here
    /// would push the whole flag table into a file whose entire value is being short.</para>
    /// </summary>
    private static string EmittedInstructions => AgentInstructionsGenerator.Build("TestRunReport");

    [Fact]
    public void Every_verb_the_emitted_instructions_name_is_a_real_command()
    {
        var named = Regex.Matches(EmittedInstructions, @"kronikol query ([a-z][a-z-]*)")
            .Select(m => m.Groups[1].Value)
            .Where(v => v != "help")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(named);

        var invented = named.Except(UsageVerbs(), StringComparer.Ordinal).ToList();
        Assert.True(invented.Count == 0,
            "the emitted CLAUDE.md sends an agent to commands the tool does not have: " + string.Join(", ", invented));
    }

    [Fact]
    public void Every_flag_the_emitted_instructions_show_is_legal_for_its_verb()
    {
        var illegal = new List<string>();

        foreach (var line in EmittedInstructions.ReplaceLineEndings("\n").Split('\n'))
            foreach (Match invocation in Regex.Matches(line, @"kronikol query ([a-z][a-z-]*)([^\n`|]*)"))
            {
                var verb = invocation.Groups[1].Value;
                if (!QueryCommand.FlagsByVerb.TryGetValue(verb, out var legal))
                    continue;

                foreach (Match flag in Regex.Matches(invocation.Groups[2].Value, @"--[a-z][a-z-]*"))
                    if (!legal.Contains(flag.Value, StringComparer.Ordinal)
                        && !QueryCommand.UniversalFlags.Contains(flag.Value, StringComparer.Ordinal))
                        illegal.Add($"{verb} {flag.Value}  \u2014  {line.Trim()}");
            }

        Assert.True(illegal.Count == 0,
            "the emitted CLAUDE.md tells an agent to type commands the tool refuses:\n  " + string.Join("\n  ", illegal));
    }

    /// <summary>
    /// The repo's own copy of the skill and the copy shipped in <c>templates/</c> are the same files.
    ///
    /// <para>They are two checkouts of one document: <c>.claude/skills/</c> is what this repo's agent
    /// loads and <c>templates/skills/</c> is what <c>kronikol init-agents</c> installs into a consumer's
    /// project. Nothing made them agree, and they had already drifted — the shipped
    /// <c>scripts/query.py</c> was missing the <c>failureCause</c> line the repo's copy grew in 3.1.0, so
    /// the fallback a user without the .NET tool reaches for was a version behind the one every test here
    /// exercises. The drift is invisible from inside either copy.</para>
    /// </summary>
    [Fact]
    public void The_two_copies_of_the_skill_are_the_same_files()
    {
        var mine = Path.Combine(RepoRoot, ".claude", "skills", "kronikol-test-debugging");
        var shipped = Path.Combine(RepoRoot, "templates", "skills", "kronikol-test-debugging");

        var minesFiles = Relative(mine);
        var shippedFiles = Relative(shipped);
        Assert.Equal(minesFiles, shippedFiles);

        var differing = minesFiles
            .Where(f => !File.ReadAllText(Path.Combine(mine, f)).ReplaceLineEndings("\n")
                    .Equals(File.ReadAllText(Path.Combine(shipped, f)).ReplaceLineEndings("\n"), StringComparison.Ordinal))
            .ToList();

        Assert.True(differing.Count == 0,
            ".claude/skills and templates/skills have drifted apart, so the skill this repo reads is not the "
            + "skill `kronikol init-agents` installs: " + string.Join(", ", differing));

        static List<string> Relative(string root) =>
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
    }

    /// <summary>
    /// The same again for the managed block: <c>templates/agents/CLAUDE.md</c> is what
    /// <c>kronikol init-agents</c> splices into a consumer's <c>CLAUDE.md</c> and <c>AGENTS.md</c>, and this
    /// repository carries that block in its own two. Nothing held them to it, and they had drifted: the
    /// repo's copies lacked the <c>kronikol query history</c> line the template grew.
    /// </summary>
    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData("AGENTS.md")]
    public void The_managed_block_in_this_repository_s_agent_files_is_the_template_s(string file)
    {
        static string Block(string path)
        {
            var text = File.ReadAllText(path).ReplaceLineEndings("\n");
            var begin = text.IndexOf("<!-- kronikol:begin -->", StringComparison.Ordinal);
            var end = text.IndexOf("<!-- kronikol:end -->", StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin, $"no managed block in {path}");
            return text[begin..end];
        }

        // AGENTS.md is git-ignored here: a checkout that never ran `kronikol init-agents` has none, and
        // there is nothing to have drifted.
        if (!File.Exists(Path.Combine(RepoRoot, file)))
            return;

        Assert.Equal(Block(Path.Combine(RepoRoot, "templates", "agents", "CLAUDE.md")), Block(Path.Combine(RepoRoot, file)));
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
