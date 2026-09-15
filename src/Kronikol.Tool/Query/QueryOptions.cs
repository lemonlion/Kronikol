using System.Globalization;

namespace Kronikol.Tool.Query;

/// <summary>
/// The flags every <c>kronikol query</c> command shares, plus the positional arguments each one reads for
/// itself. Parsed once so that <c>--max-bytes</c>, <c>--offset</c> and <c>--out</c> mean the same thing
/// everywhere — a budget an agent has to re-learn per command is a budget it will get wrong.
/// </summary>
internal sealed class QueryOptions
{
    public string? File { get; private set; }
    public List<string> Positional { get; } = [];

    public int MaxBytes { get; private set; } = DefaultMaxBytes;
    public int Offset { get; private set; }
    public int Limit { get; private set; } = int.MaxValue;
    public bool Count { get; private set; }

    /// <summary>
    /// One JSON envelope instead of text, on the listing verbs. Not the default and not what an agent
    /// reading a terminal wants: the same answer costs roughly twice the tokens. It is for scripts, and
    /// for the MCP wrapper that has to hand structure to a caller that never sees the terminal.
    /// </summary>
    public bool Json { get; private set; }

    public string? Out { get; private set; }

    public string? Result { get; private set; }
    public string? Feature { get; private set; }
    public string? Label { get; private set; }
    public string? Service { get; private set; }
    public string? Status { get; private set; }
    public string? Method { get; private set; }
    public string? Grep { get; private set; }
    public string? Step { get; private set; }

    /// <summary>
    /// Narrows to a step path that arrived as part of the positional address rather than through
    /// <c>--step</c>. A null does nothing, so a caller can hand over whatever the address resolver gave
    /// it; an explicit <c>--step</c> wins, because a caller that passed both said the narrower thing
    /// twice and the flag is the one they can see.
    /// </summary>
    internal void ScopeToStep(string? stepPath) => Step ??= stepPath;
    public string? Sort { get; private set; }
    public string? Path { get; private set; }
    public string? In { get; private set; }
    public double? SlowerThan { get; private set; }
    public (int From, int To)? LineRange { get; private set; }

    /// <summary>Repeatable body-content predicates; they compose as AND.</summary>
    public List<string> Where { get; } = [];
    public string? GroupBy { get; private set; }
    public string? Tolerance { get; private set; }

    /// <summary>The body address a cross-run <c>diff --body s3/i47</c> names (distinct from the bare <c>--body</c> bool).</summary>
    public string? BodyAddress { get; private set; }

    public bool Failed { get; private set; }
    public bool ErrorsOnly { get; private set; }
    public bool Headers { get; private set; }
    public bool Body { get; private set; }
    public bool Keys { get; private set; }
    public bool Values { get; private set; }
    public bool Group { get; private set; }
    public bool Stats { get; private set; }
    public bool Request { get; private set; }
    public bool Both { get; private set; }
    public bool Number { get; private set; }

    /// <summary>
    /// <c>diff --baseline</c>: the report named on the command line is the CURRENT run and the one to
    /// compare against is found by convention. Diff-only - it is not in <see cref="RerunPrefix"/>
    /// because <c>diff</c> does not page.
    /// </summary>
    public bool Baseline { get; private set; }

    /// <summary><c>history --history FILE</c>: the ledger, instead of the one resolved from the environment or the report's repository.</summary>
    public string? HistoryPath { get; private set; }

    /// <summary><c>history --flaky</c>.</summary>
    public bool Flaky { get; private set; }

    /// <summary><c>history --new</c>.</summary>
    public bool New { get; private set; }

    /// <summary><c>history --failing</c>.</summary>
    public bool Failing { get; private set; }

    /// <summary><c>history --regressed</c>.</summary>
    public bool Regressed { get; private set; }

    /// <summary><c>history --changed</c>.</summary>
    public bool Changed { get; private set; }

    /// <summary><c>history --branch NAME</c>: the stream to read against.</summary>
    public string? Branch { get; private set; }

    /// <summary><c>history --compare-branch NAME</c>: a second stream to read against.</summary>
    public string? CompareBranch { get; private set; }

    /// <summary><c>history --min-runs N</c>: the recorded runs the flaky and duration verdicts need; null for the analyzer's default.</summary>
    public int? MinRuns { get; private set; }

    /// <summary>How far back a set of calls the scenario held counts as a known state (history only); null leaves the report's bar.</summary>
    public int? AlternatingRuns { get; private set; }

    /// <summary><c>history --suite NAME</c>: the suite, when the report does not carry one.</summary>
    public string? SuiteOverride { get; private set; }

    /// <summary>The window the history verb reads; the library's default, because the verb has no flag for it yet.</summary>
    public int HistoryWindowOrDefault => 50;

    /// <summary>
    /// The flags actually written on the command line, as their argv tokens. A value alone cannot answer
    /// this: <c>--limit</c> defaults to <see cref="int.MaxValue"/> and <c>--failed</c> to false, so a verb
    /// asking "was this given?" would get "no" from a flag that was given its own default. Per-verb
    /// legality is decided from this set, before any verb runs.
    /// </summary>
    public HashSet<string> Given { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Every flag the switch below has a case for. Kept beside it rather than derived from it, and held to
    /// it by <c>Every_flag_the_table_names_parses_as_a_flag</c> — a name here that reaches no case is a
    /// name the command line does not have.
    /// <para>
    /// Drift in the other direction is safe by construction: a flag added to the parser and not to a
    /// verb's list is refused on every verb, which is loud, rather than accepted and ignored, which is the
    /// silence this whole mechanism exists to remove.
    /// </para>
    /// </summary>
    public static readonly string[] KnownFlags =
    [
        "--max-bytes", "--offset", "--limit", "--slower-than", "--lines", "--out", "--result", "--feature",
        "--label", "--service", "--status", "--method", "--grep", "--step", "--sort", "--path", "--in",
        "--where", "--group-by", "--tolerance", "--count", "--json", "--failed", "--errors-only",
        "--headers", "--body", "--keys", "--values", "--group", "--stats", "--request", "--both",
        "--number", "--baseline", "--describe", "--history", "--flaky", "--new", "--failing", "--regressed",
        "--changed", "--branch", "--compare-branch", "--min-runs", "--alternating-runs", "--suite"
    ];

    /// <summary>
    /// <c>--describe</c>. Answered by <c>QueryCommand.Run</c> before any report is resolved, so this is
    /// never read there; the case exists so the flag is a flag the parser knows, which is what the
    /// reference documents and the drift guard checks.
    /// </summary>
    public bool Describe { get; set; }

    /// <summary>Null when a flag was malformed; the message has already been written to <paramref name="error"/>.</summary>
    public static QueryOptions? Parse(IReadOnlyList<string> args, TextWriter error)
    {
        var options = new QueryOptions();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Next(string flag)
            {
                if (++i < args.Count)
                    return args[i];
                error.WriteLine("Missing value for " + flag);
                return null;
            }

            switch (arg)
            {
                case "--max-bytes":
                    if (Next(arg) is not { } maxBytes) return null;
                    if (!int.TryParse(maxBytes, out var parsedMax) || parsedMax < 0)
                    {
                        error.WriteLine("--max-bytes takes a non-negative number of bytes (0 removes the budget).");
                        return null;
                    }
                    options.MaxBytes = parsedMax;
                    break;

                case "--offset":
                    if (Next(arg) is not { } offset) return null;
                    if (!int.TryParse(offset, out var parsedOffset) || parsedOffset < 0)
                    {
                        error.WriteLine("--offset takes a non-negative row number.");
                        return null;
                    }
                    options.Offset = parsedOffset;
                    break;

                case "--limit":
                    if (Next(arg) is not { } limit) return null;
                    if (!int.TryParse(limit, out var parsedLimit) || parsedLimit <= 0)
                    {
                        error.WriteLine("--limit takes a positive row count.");
                        return null;
                    }
                    options.Limit = parsedLimit;
                    break;

                case "--slower-than":
                    if (Next(arg) is not { } slower) return null;
                    if (!double.TryParse(slower.TrimEnd('s'), out var parsedSlower))
                    {
                        error.WriteLine("--slower-than takes a number of seconds.");
                        return null;
                    }
                    options.SlowerThan = parsedSlower;
                    break;

                case "--lines":
                    if (Next(arg) is not { } lines) return null;
                    var range = lines.Split('-', 2);
                    if (range.Length != 2 || !int.TryParse(range[0], out var from) || !int.TryParse(range[1], out var to))
                    {
                        error.WriteLine("--lines takes a range like 20-60.");
                        return null;
                    }
                    options.LineRange = (from, to);
                    break;

                case "--out": if (Next(arg) is not { } output) return null; options.Out = output; break;
                case "--result": if (Next(arg) is not { } result) return null; options.Result = result; break;
                case "--feature": if (Next(arg) is not { } feature) return null; options.Feature = feature; break;
                case "--label": if (Next(arg) is not { } label) return null; options.Label = label; break;
                case "--service": if (Next(arg) is not { } service) return null; options.Service = service; break;
                case "--status": if (Next(arg) is not { } status) return null; options.Status = status; break;
                case "--method": if (Next(arg) is not { } method) return null; options.Method = method; break;
                case "--grep": if (Next(arg) is not { } grep) return null; options.Grep = grep; break;
                case "--step": if (Next(arg) is not { } step) return null; options.Step = step; break;
                // Lower-cased here, not at the point of use: both are validated case-insensitively and
                // then consumed by ordinal switches and Contains calls, so an accepted `--sort DURATION`
                // or `--in BODIES` would sort by the default and search nothing - the same silence the
                // validation exists to remove, one capital letter further from being noticed.
                case "--sort": if (Next(arg) is not { } sort) return null; options.Sort = sort.ToLowerInvariant(); break;
                case "--path": if (Next(arg) is not { } path) return null; options.Path = path; break;
                case "--in": if (Next(arg) is not { } inTargets) return null; options.In = inTargets.ToLowerInvariant(); break;
                case "--where": if (Next(arg) is not { } where) return null; options.Where.Add(where); break;
                case "--group-by": if (Next(arg) is not { } groupBy) return null; options.GroupBy = groupBy; break;
                case "--tolerance": if (Next(arg) is not { } tolerance) return null; options.Tolerance = tolerance; break;

                case "--count": options.Count = true; break;
                case "--json": options.Json = true; break;
                case "--describe": options.Describe = true; break;
                case "--failed": options.Failed = true; break;
                case "--errors-only": options.ErrorsOnly = true; break;
                case "--headers": options.Headers = true; break;
                case "--body":
                    // Bare `--body` prints the payload; `--body s3/i47` (cross-run diff) names one.
                    // Disambiguated by lookahead so one flag never silently swallows a positional.
                    //
                    // A value that is NOT an address used to fall through to bare `--body`, leaving the
                    // token unconsumed in the positionals where nothing read it: `diff <old> <new> --body
                    // s0/1` printed a full unfiltered run diff at exit 0 and never mentioned the request
                    // it had been asked about. That is the silence the per-verb flag validator exists to
                    // remove, arriving through a flag's VALUE instead of through its name.
                    if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        var given = args[++i];
                        if (!Address.TryParse(given, out var bodyAddress)
                            || bodyAddress.Kind is not (AddressKind.Interaction or AddressKind.Body))
                        {
                            error.WriteLine($"--body {given}: not a call or body address.");
                            error.WriteLine("Give it s3/i47 (a call) or b:4bdea521 (a body), or pass --body on its own to print the payload.");
                            return null;
                        }

                        options.BodyAddress = given;
                    }
                    else
                    {
                        options.Body = true;
                    }

                    break;
                case "--keys": options.Keys = true; break;
                case "--values": options.Values = true; break;
                case "--group": options.Group = true; break;
                case "--stats": options.Stats = true; break;
                case "--request": options.Request = true; break;
                case "--both": options.Both = true; break;
                case "--number": options.Number = true; break;
                case "--baseline": options.Baseline = true; break;
                case "--history": if (Next(arg) is not { } history) return null; options.HistoryPath = history; break;
                case "--branch": if (Next(arg) is not { } branch) return null; options.Branch = branch; break;
                case "--compare-branch": if (Next(arg) is not { } compareBranch) return null; options.CompareBranch = compareBranch; break;
                case "--min-runs":
                    if (Next(arg) is not { } minRuns) return null;
                    if (!int.TryParse(minRuns, out var parsedMinRuns) || parsedMinRuns <= 0)
                    {
                        error.WriteLine("--min-runs takes a positive number of runs: the bar the flaky and duration verdicts need, the report's HistoryMinRuns (default 5).");
                        return null;
                    }
                    options.MinRuns = parsedMinRuns;
                    break;
                case "--alternating-runs":
                    if (Next(arg) is not { } alternating) return null;
                    if (!int.TryParse(alternating, out var parsedAlternating) || parsedAlternating <= 0)
                    {
                        error.WriteLine("--alternating-runs takes a positive number of runs: how far back a set of calls the scenario held counts as a known state, the report's HistoryAlternatingRuns (default 10).");
                        return null;
                    }
                    options.AlternatingRuns = parsedAlternating;
                    break;
                case "--suite": if (Next(arg) is not { } suiteName) return null; options.SuiteOverride = suiteName; break;
                case "--flaky": options.Flaky = true; break;
                case "--new": options.New = true; break;
                case "--failing": options.Failing = true; break;
                case "--regressed": options.Regressed = true; break;
                case "--changed": options.Changed = true; break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        error.WriteLine("Unknown option: " + arg);
                        return null;
                    }

                    if (options.File is null)
                        options.File = arg;
                    else
                        options.Positional.Add(arg);
                    break;
            }

            // After the switch, so the token recorded is the flag and not the value it swallowed. An
            // unknown flag never reaches here - it has already returned.
            if (arg.StartsWith("--", StringComparison.Ordinal))
                options.Given.Add(arg);
        }

        return options;
    }

    /// <summary>The default byte budget, and the value <see cref="RerunArgs"/> leaves unsaid.</summary>
    internal const int DefaultMaxBytes = 6000;

    /// <summary>
    /// The page size a verb will actually use: <see cref="Limit"/> capped at that verb's ceiling, and a
    /// note when the cap bit.
    /// </summary>
    /// <remarks>
    /// Every verb caps its page, and the cap used to be silent: <c>failures --limit 50</c> showed 25 rows
    /// and said nothing, so a consumer advancing by the 50 it asked for stepped over rows 25 to 49. That
    /// is the same silent skip the pager exists to remove, arriving through the flag rather than through
    /// the footer. The footer's own offset was always right; what was missing was any sign that the page
    /// was not the size that had been requested.
    /// </remarks>
    public int PageSize(int ceiling, QueryWriter writer, string noun)
    {
        if (Limit <= ceiling)
            return Limit;

        // Only when the caller actually asked for more. `Limit` defaults to int.MaxValue, which is above
        // every ceiling in the tool, so keying on the value alone would put this note on every page of
        // every answer - and a caveat that is always true is one a reader stops reading.
        if (Given.Contains("--limit"))
            writer.Note($"! --limit {Limit} is above this verb's ceiling of {ceiling} {noun} a page — showing {ceiling}; follow `next:` for the rest");

        return ceiling;
    }

    /// <summary>
    /// The flags that must be repeated for a paged re-run to mean the same thing, as argv tokens —
    /// unquoted, one element per argument. The tokens are the contract and the rendered string is a
    /// view of them: a value with a space in it (a service genuinely called <c>Dessert Provider</c>)
    /// cannot survive being flattened into one string, because whatever quoting the footer picks, a
    /// consumer that splits on whitespace gets a flag and a stray positional.
    /// </summary>
    public List<string> RerunArgs()
    {
        var parts = new List<string>();

        void Flag(string name, string? value = null)
        {
            parts.Add(name);
            if (value is not null)
                parts.Add(value);
        }

        if (Service is not null) Flag("--service", Service);
        if (Status is not null) Flag("--status", Status);
        if (Method is not null) Flag("--method", Method);
        if (Grep is not null) Flag("--grep", Grep);
        if (Result is not null) Flag("--result", Result);
        if (Feature is not null) Flag("--feature", Feature);
        if (Label is not null) Flag("--label", Label);
        if (Failed) Flag("--failed");
        if (Group) Flag("--group");
        foreach (var where in Where) Flag("--where", where);
        if (GroupBy is not null) Flag("--group-by", GroupBy);
        if (Request) Flag("--request");
        if (Both) Flag("--both");
        if (Number) Flag("--number");
        if (Tolerance is not null) Flag("--tolerance", Tolerance);
        // Order and corpus, not just filters: an offset means nothing without the ordering it was counted
        // against, and a grep offset means nothing without the targets it was counted over.
        if (Sort is not null) Flag("--sort", Sort);
        if (In is not null) Flag("--in", In);
        if (Values) Flag("--values");
        if (Step is not null) Flag("--step", Step);
        if (SlowerThan is not null) Flag("--slower-than", SlowerThan.Value.ToString(CultureInfo.InvariantCulture));
        // The history verb's corpus: which ledger, which stream, which verdicts - a page resumed against
        // a different ledger or stream is a different listing.
        if (HistoryPath is not null) Flag("--history", HistoryPath);
        if (SuiteOverride is not null) Flag("--suite", SuiteOverride);
        if (Branch is not null) Flag("--branch", Branch);
        if (CompareBranch is not null) Flag("--compare-branch", CompareBranch);
        if (MinRuns is not null) Flag("--min-runs", MinRuns.Value.ToString(CultureInfo.InvariantCulture));
        if (AlternatingRuns is not null) Flag("--alternating-runs", AlternatingRuns.Value.ToString(CultureInfo.InvariantCulture));
        if (Flaky) Flag("--flaky");
        if (New) Flag("--new");
        if (Failing) Flag("--failing");
        if (Regressed) Flag("--regressed");
        if (Changed) Flag("--changed");
        // Format last, so a text footer's flag order is untouched: this only ever fires when --json was
        // given, and then the whole point of `next` is that it can be appended verbatim to the same call.
        if (Json) Flag("--json");
        // The budget is part of the corpus, not of the presentation: page two counted against a different
        // one holds a different number of rows than page one did, so a pointer that drops it resumes a
        // walk other than the one it came from. Said only when it differs from the default, so the common
        // footer is unchanged.
        if (MaxBytes != DefaultMaxBytes) Flag("--max-bytes", MaxBytes.ToString(CultureInfo.InvariantCulture));
        return parts;
    }

    /// <summary>
    /// <see cref="RerunArgs"/> rendered for a terminal — quoted only where a token would not survive
    /// being pasted back into a shell — with the single trailing space a footer appends its offset to.
    /// </summary>
    public string RerunPrefix()
    {
        var args = RerunArgs();
        return args.Count == 0 ? "" : Shell(args) + " ";
    }

    /// <summary>Argv tokens as one pasteable command line.</summary>
    public static string Shell(IReadOnlyList<string> args) =>
        string.Join(" ", args.Select(Quote));

    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.AsSpan().ContainsAny(" \t\"'")
            ? arg
            : "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
