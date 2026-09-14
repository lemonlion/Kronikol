namespace Kronikol.Tool.Query;

/// <summary>One flag <c>kronikol query</c> reads, as the help and <c>--describe</c> state it.</summary>
/// <param name="Name">The flag, with its dashes.</param>
/// <param name="Arg">What the flag takes, or null for a switch. In brackets when the value is optional.</param>
/// <param name="Summary">One line, for a reader who has not seen the flag before.</param>
/// <param name="Repeatable">Whether giving it twice means two constraints rather than the second winning.</param>
internal sealed record FlagSpec(string Name, string? Arg, string Summary, bool Repeatable = false);

/// <summary>Everything the CLI knows about one verb, in one place.</summary>
/// <param name="Name">The verb as typed.</param>
/// <param name="Group">The heading it is listed under in the help.</param>
/// <param name="Summary">One line, the same one the help prints.</param>
/// <param name="Forms">The ways it is invoked, positionals only - <c>&lt;report&gt; s3</c>; several for <c>diff</c>.</param>
/// <param name="Flags">The flags it reads, beside <see cref="VerbTable.UniversalFlags"/>. Enforced, not documentary.</param>
/// <param name="Json">Whether <c>--json</c> answers it.</param>
/// <param name="Usage">The help lines, verbatim; the first opens with two spaces and the verb.</param>
internal sealed record VerbSpec(
    string Name,
    string Group,
    string Summary,
    string[] Forms,
    string[] Flags,
    bool Json,
    string[] Usage)
{
    /// <summary>Whether the verb lists rows and so takes <c>--offset</c> / <c>--limit</c>.</summary>
    public bool Pages => Flags.Contains("--offset", StringComparer.Ordinal);
}

/// <summary>
/// The one table the query CLI is described from: the verbs it dispatches, the flags each one reads, and
/// the help text. <c>QueryCommand</c>'s dispatch switch, its flag legality table, its <c>--json</c> list
/// and <c>PrintUsage</c> all derive from here, and <c>--describe</c> prints it.
/// </summary>
/// <remarks>
/// <para>Before this the verb set lived in four hand-kept places - the switch, a <c>Verbs</c> array, the
/// legality dictionary and sixty lines of usage text - plus a byte-identical dogfood copy of the skill, and
/// the drift guard recovered the set by regexing the rendered help for <c>^  verb &lt;report&gt;</c>. That
/// regex had already silently dropped a verb once (C66), and a table nobody can read from outside the
/// process cannot feed an MCP server's tool list or a wrapper's argument validation. One table, read by
/// the code and printed by <c>--describe</c>, is the shape that cannot drift from itself.</para>
///
/// <para>The switch expression in <c>QueryCommand.RunCore</c> still exists, because a switch is what
/// dispatches; what changed is that a verb absent from this table is refused before the switch is reached,
/// so an arm without a row is unreachable and a row without an arm fails
/// <c>DescribeTests.Every_verb_describe_lists_dispatches</c>.</para>
/// </remarks>
internal static class VerbTable
{
    /// <summary>The flags that mean the same thing on every verb, applied by <c>QueryWriter</c> around whatever the verb produced.</summary>
    public static readonly string[] UniversalFlags = ["--max-bytes", "--out", "--describe"];

    /// <summary>Every flag the parser has a case for, with what it takes and what it means.</summary>
    public static readonly FlagSpec[] Flags =
    [
        new("--max-bytes", "N", "Output budget in bytes; default 6000, 0 removes it. --out lifts it, because a file is not a context window."),
        new("--out", "FILE", "Write the answer to a file instead of stdout. On http, body, note and diagram the payload itself is what is saved."),
        new("--offset", "N", "Resume a truncated listing at row N - the value the previous page's next: line printed."),
        new("--limit", "N", "Cap the rows shown; each verb has a ceiling and says so when N is above it."),
        new("--slower-than", "SECONDS", "Only scenarios that took longer than this."),
        new("--lines", "RANGE", "A line range of a payload, like 20-60."),
        new("--result", "RESULT", "Only scenarios with this result: Passed, Failed, Skipped."),
        new("--feature", "TEXT", "Only scenarios whose feature name contains TEXT."),
        new("--label", "LABEL", "Only scenarios carrying this label or tag."),
        new("--service", "TEXT", "Only calls whose service name contains TEXT."),
        new("--status", "STATUS", "Only calls with this status: a code (500), a name (InternalServerError) or a class (5xx)."),
        new("--method", "METHOD", "Only calls with this HTTP method or tracker label."),
        new("--grep", "TEXT", "A substring filter: on scenarios the scenario name, on interactions and values the URI."),
        new("--step", "PATH", "Only calls made under this step path - 2, b0, 2.1 - and every step under it."),
        new("--sort", "KEY", "Order rows by calls, duration, bytes or errors; only services and interactions --group-by order theirs."),
        new("--path", "JSONPATH", "One value out of a body: $.a.b[2]; [*] every element; ['a.b'] a dotted key; .length() a count. Quote it."),
        new("--in", "TARGETS", "Where grep looks, as a comma list: bodies, uris, steps, assertions, names, errors, headers, notes."),
        new("--where", "EXPR", "Keep only calls whose body satisfies EXPR, like $.success = false; req: targets the request body. AND across repeats.", Repeatable: true),
        new("--group-by", "DIMS", "Bucket calls by a comma list of dimensions: service method status path step phase category kind capturedBy."),
        new("--tolerance", "T", "How far a numeric grep match may be from the needle: absolute (0.5) or relative (1%)."),
        new("--count", null, "Print only how many matched - one token, with any caveats on stderr."),
        new("--json", null, "One JSON envelope instead of text, on the verbs that list like things."),
        new("--failed", null, "Only what failed."),
        new("--errors-only", null, "Only calls whose response was an error."),
        new("--headers", null, "Print the call's headers."),
        new("--body", "[s3/i47]", "On http: print the whole payload. On diff between two runs: the call whose bodies to compare, resolved in the old run."),
        new("--keys", null, "The body's shape - keys and array lengths - without its values."),
        new("--values", null, "On grep: say where in each matching body the value sits, as a --path an agent can pull."),
        new("--group", null, "On interactions: fold consecutive identical calls into one row with a range address."),
        new("--stats", null, "On values: min, median, max, sum and mean of the numeric values, with the address of each extreme."),
        new("--request", null, "On values: read the request bodies instead of the responses."),
        new("--both", null, "On values: read both halves of every call."),
        new("--number", null, "On grep: match numbers across formatting - 4,173.00, 4173 and 4.173,00 are one number."),
        new("--baseline", null, "On diff: compare the report against last-green, found at <reports>/baseline/TestRunReport.json or $KRONIKOL_BASELINE."),
        new("--describe", null, "Print the verbs, their flags, the address forms and the exit codes as JSON; needs no report.")
    ];

    /// <summary>The headings the help groups verbs under, in order, with the caption printed beside each.</summary>
    public static readonly (string Name, string? Caption)[] Groups =
    [
        ("Overview", null),
        ("Narrative", null),
        ("Aggregation", "reads bodies freely, prints values one-lined, never whole payloads"),
        ("Payloads", "never printed unless asked for"),
        ("Search and comparison", null)
    ];

    /// <summary>Every verb, in the order the help prints them.</summary>
    public static readonly VerbSpec[] Verbs =
    [
        new("summary", "Overview",
            "The run header, per-feature results, the slowest scenarios and the run's diagnostics.",
            ["<report>"],
            ["--count", "--json"], Json: true,
            ["  summary      <report>                        run header, per-feature results, slowest scenarios, diagnostics"]),

        new("scenarios", "Overview",
            "Scenarios, filtered by result, feature, label, name or duration.",
            ["<report>"],
            ["--result", "--failed", "--feature", "--label", "--grep", "--slower-than", "--count", "--offset", "--limit", "--json"], Json: true,
            ["  scenarios    <report> [--result Failed] [--feature X] [--label L] [--grep T] [--slower-than 5]"]),

        new("services", "Overview",
            "Per service: calls, status mix, errors, bytes and timings. Absence from the table is the answer to 'did it call X?'.",
            ["<report>", "<report> s3"],
            ["--sort", "--count", "--offset", "--limit", "--json"], Json: true,
            ["  services     <report> [s3] [--sort duration]  per service: calls, status mix, errors, bytes, timings"]),

        new("failures", "Narrative",
            "Why each failing test failed, in context: the message, the failing step, its calls, where it was written and thrown.",
            ["<report>", "<report> s3"],
            ["--count", "--offset", "--limit", "--json"], Json: true,
            ["  failures     <report>                        why each failing test failed, in context"]),

        new("steps", "Narrative",
            "One scenario's step and assertion tree, with the interaction range each step made.",
            ["<report> s3", "<report> s3/2"],
            [], Json: false,
            ["  steps        <report> s3                     the step and assertion tree, with interaction ranges"]),

        new("assertions", "Narrative",
            "Every tracked assertion as a flat list, with its result, message and source location.",
            ["<report>", "<report> s3", "<report> s3/2"],
            ["--failed", "--count", "--offset", "--limit", "--json"], Json: true,
            ["  assertions   <report> [s3] [--failed]        flat assertion list with results and source locations"]),

        new("flow", "Narrative",
            "One scenario's calls in order, grouped under the step that made them - the diagram as text, in 1-2 KB.",
            ["<report> s3", "<report> s3/2"],
            ["--step", "--service", "--errors-only", "--count"], Json: false,
            ["  flow         <report> s3 [--step 2] [--service X] [--errors-only]"]),

        new("annotations", "Narrative",
            "The example-row markers and injected diagram fragments of one scenario.",
            ["<report> s3"],
            ["--count"], Json: false,
            ["  annotations  <report> s3                     example-row markers and injected diagram fragments"]),

        new("values", "Aggregation",
            "The distinct values one JSON path holds across bodies, counted, each with an address that fetches a body holding it.",
            ["<report> --path '$.x'", "<report> s3 --path '$.x'"],
            ["--path", "--service", "--status", "--method", "--step", "--grep", "--where", "--request", "--both", "--stats", "--count", "--offset", "--limit"], Json: false,
            [
                "  values       <report> [s3] --path '$.status' [--service X] [--status 5xx] [--method M] [--step 2]",
                "               [--grep URI] [--where E] [--stats] [--request|--both]   distinct values × counts, with addresses"
            ]),

        new("interactions", "Payloads",
            "The calls, filtered, folded or bucketed; a row names the call's address and the size and hash of each payload, never the payload.",
            ["<report>", "<report> s3", "<report> s3/2"],
            ["--service", "--status", "--method", "--step", "--grep", "--where", "--group", "--group-by", "--sort", "--count", "--offset", "--limit", "--json"], Json: true,
            [
                "  interactions <report> [s3] [--service X] [--status 5xx] [--method GET] [--grep T] [--group]",
                "               [--where \"$.success = false\"]   repeatable; AND; req: prefix targets the request body",
                "               [--group-by service,status]      buckets with calls/errors/median/max/bodies; dims:",
                "                                                service method status path step phase category kind capturedBy"
            ]),

        new("http", "Payloads",
            "One call: its status, timing, step, the other half of the pair, and - only when asked - its headers or payload.",
            ["<report> s3/i47", "<report> b:4bdea521"],
            ["--headers", "--body", "--keys", "--path", "--lines"], Json: false,
            ["  http         <report> s3/i47 [--headers] [--body] [--keys] [--path $.a.b] [--lines 20-60] [--out F]"]),

        new("body", "Payloads",
            "One payload, by content hash or by call address: its shape, one value, a line range, or the whole thing to a file.",
            ["<report> b:4bdea521", "<report> s3/i47"],
            ["--keys", "--path", "--lines", "--offset", "--limit"], Json: false,
            [
                "  body         <report> b:4bdea521 [--keys] [--path $.a.b] [--lines 20-60] [--out F]",
                "               --path grammar: $.a.b[2] · [*] every element · ['a.b'] dotted key · .length() count — quote the path"
            ]),

        new("note", "Payloads",
            "What the HTML rendered for a diagram note, when it differs from what was captured.",
            ["<report> s3/d0", "<report> s3/d0/n12"],
            [], Json: false,
            ["  note         <report> s3/d0 [/n12] [--out F]  what the HTML rendered, when it differs from the capture"]),

        new("diagram", "Payloads",
            "The raw PlantUML of one diagram, written to a file and never printed.",
            ["<report> s3/d0 --out FILE"],
            [], Json: false,
            ["  diagram      <report> s3/d0 --out F          the raw PlantUML; never printed to stdout"]),

        new("grep", "Search and comparison",
            "Where a value appears in the run - bodies, URIs, steps, assertions, names, errors, headers, notes - with the address of each hit.",
            ["<report> \"text\""],
            ["--in", "--values", "--number", "--tolerance", "--count", "--offset", "--limit"], Json: false,
            [
                "  grep         <report> \"4173\" [--in bodies,uris,steps,assertions,names,errors,headers,notes] [--values]",
                "               [--number [--tolerance 0.5|1%]]   numeric match across formatting — 4,173.00 ≈ 4173 ≈ 4.173,00"
            ]),

        new("trace", "Search and comparison",
            "Every call sharing one W3C trace id, across the whole run, in time order.",
            ["<report> <trace id>", "<report> <prefix of at least 8 hex>", "<report> s3/i47"],
            ["--count"], Json: false,
            ["  trace        <report> <id | prefix≥8hex | s3/i47>   follow a W3C trace id across the run, chronologically"]),

        new("compare", "Search and comparison",
            "Two scenarios of one run side by side: steps, calls, and the first body that differs.",
            ["<report> s3 s7"],
            ["--count"], Json: false,
            ["  compare      <report> s3 s7                  two scenarios in one run"]),

        new("diff", "Search and comparison",
            "Two bodies in one report, printed as the paths that differ; or two runs matched on stableId - what broke, was fixed, is new, got slower, disappeared, and which services lost tracked calls.",
            ["<report> s3/i47 s7/i47", "<report> b:4bdea521 b:9f31c02a", "<old.json> <new.json>", "<old.json> <new.json> --body s3/i47", "<report> --baseline"],
            ["--baseline", "--body", "--count", "--offset", "--limit", "--json"], Json: true,
            [
                "  diff         <report> s3/i47 s7/i47          two bodies in one report — only the differing paths (also b:hashes)",
                "  diff         <old.json> <new.json> [--body s3/i47]   two runs matched on stableId; --body diffs one call across them",
                "  diff         <report> --baseline               the same, against last-green: <reports>/baseline/TestRunReport.json,",
                "                                                 else $KRONIKOL_BASELINE (a report, or a directory holding one)"
            ])
    ];

    /// <summary>The verb by name, or null - which is how a name that is not a verb is told apart, before any switch is reached.</summary>
    public static VerbSpec? Find(string name) =>
        Verbs.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));

    /// <summary>The flag by name. Throws for a name no flag has, which is a programming error and not an input one.</summary>
    public static FlagSpec Flag(string name) =>
        Flags.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentException($"{name} is not a flag the table knows.", nameof(name));

    /// <summary>The verb names in help order.</summary>
    public static string[] Names => Verbs.Select(v => v.Name).ToArray();

    /// <summary>The verbs <c>--json</c> answers.</summary>
    public static string[] JsonVerbs => Verbs.Where(v => v.Json).Select(v => v.Name).ToArray();

    /// <summary>What each verb reads, beside <see cref="UniversalFlags"/>.</summary>
    public static Dictionary<string, string[]> FlagsByVerb() =>
        Verbs.ToDictionary(v => v.Name, v => v.Flags, StringComparer.Ordinal);

    /// <summary>
    /// The address forms every command prints and accepts, with an example of each that
    /// <see cref="Address.TryParse"/> resolves to the stated kind - <c>DescribeTests</c> checks that.
    /// </summary>
    public static readonly (AddressKind Kind, string Form, string Example, string Meaning)[] AddressForms =
    [
        (AddressKind.Scenario, "sN", "s3", "The N-th scenario in file order, counted from 0, as summary and scenarios print it."),
        (AddressKind.Step, "sN/<stepPath>", "s3/2.1", "A step by its path: a top-level index (2), bN for a background step (b0), dotted for a sub-step or assertion (2.1). Covers that step and everything under it."),
        (AddressKind.Interaction, "sN/iM", "s3/i47", "The M-th interaction of scenario N - a request or a response; listings print the request's, and http names the other half."),
        (AddressKind.Diagram, "sN/dK", "s3/d0", "The K-th diagram of scenario N."),
        (AddressKind.Note, "sN/dK/nJ", "s3/d0/n12", "The J-th note of that diagram."),
        (AddressKind.Body, "b:<hash>", "b:4bdea521", "A payload by content hash: the same across every call in the run that carried the same bytes, and the only address a grep hit carries."),
        (AddressKind.StableId, "sid:<16 hex>", "sid:1a2b3c4d5e6f7a8b", "A scenario by its cross-run identity, the key diff matches on. The prefix is mandatory, because diff tells an address from a report path by this grammar.")
    ];

    /// <summary>The exit codes, as the help and the reference state them.</summary>
    public static readonly (int Code, string Meaning)[] ExitCodes =
    [
        (0, "Answered."),
        (1, "The report could not be read: not a file, not valid JSON, not a Kronikol report, or a format this build does not understand."),
        (2, "Bad usage: an unknown verb, a malformed or out-of-range address, a flag the verb does not read, a directory holding several reports.")
    ];
}
