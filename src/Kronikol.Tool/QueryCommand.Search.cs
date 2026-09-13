using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// Search and comparison. <c>grep</c> answers the question a passing test suite still leaves open — "the
/// number on screen is wrong, where did it come from" — by returning addresses rather than content.
/// <c>compare</c> uses a passing neighbour as an oracle for a failing scenario, and <c>diff</c> does the
/// same across two runs.
/// </summary>
internal static partial class QueryCommand
{
    /// <summary>What <c>grep --in</c> accepts. The first four are the default set.</summary>
    internal static readonly string[] GrepTargets = ["bodies", "uris", "steps", "assertions", "names", "errors", "headers", "notes"];

    /// <summary>
    /// Resolves <c>--in</c>, refusing an unknown target. Dropping one silently is the worst failure this
    /// command has: <c>--in bodys</c> would search nothing, print nothing, and read exactly like proof
    /// that the value is not in the report - a false negative on the one question grep exists to answer.
    /// </summary>
    private static string[]? ResolveGrepTargets(QueryOptions options, TextWriter error)
    {
        // `names` and `errors` are in the default set because they are the answer to the question the verb
        // is usually asked - where did this value come from - and because they cost nothing: both are
        // already in the index, and neither opens a payload. Their absence made a value that is in the
        // report in three places come back as `"…" is not in bodies, uris, steps, assertions`, which
        // reads as a proof of absence from the one verb whose job is to give one honestly.
        var targets = (options.In ?? "bodies,uris,steps,assertions,names,errors")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // `--in ""`, `","` and `" "` are not null, so the default set does not apply, and they all split
        // to nothing - leaving a loop that never runs and every target test below false. The guard whose
        // whole purpose is that grep never silently searches nothing, defeated by the emptiest input.
        if (targets.Length == 0)
        {
            error.WriteLine("--in was given no targets.");
            error.WriteLine("Valid targets: " + string.Join(", ", GrepTargets));
            return null;
        }

        foreach (var target in targets)
            if (!GrepTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                error.WriteLine($"Unknown --in target: {target}");
                error.WriteLine("Valid targets: " + string.Join(", ", GrepTargets));
                return null;
            }

        return targets;
    }

    internal static int Grep(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count == 0)
        {
            error.WriteLine("What are you looking for? kronikol query grep <report> \"4173\"");
            return 2;
        }

        if (ResolveGrepTargets(options, error) is not { } targets)
            return 2;

        if (options.Number)
            return NumberGrep(index, options, writer, error, targets);

        var needle = options.Positional[0];
        var wantsBodies = targets.Contains("bodies");
        var hits = new List<string>();

        // Bodies and diagrams alike read through the cache's single open handle - grep touches every
        // distinct body, so it gains the most from not re-opening the file per read (QUERY_PERF_PLAN 3.3).
        using var cache = new BodyCache(index);

        foreach (var scenario in index.Scenarios)
        {
            if (targets.Contains("steps") || targets.Contains("assertions"))
            {
                foreach (var (path, _, step) in scenario.AllSteps())
                {
                    if (step.IsAssertion && !targets.Contains("assertions"))
                        continue;
                    if (!step.IsAssertion && !targets.Contains("steps"))
                        continue;

                    if (step.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || (step.FailureMessage?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
                        hits.Add($"{scenario.Address}/{path,-6} {(step.IsAssertion ? "assertion" : "step")}  "
                                 + QueryWriter.OneLine(step.FailureMessage ?? step.Text, 110));
                }
            }

            if (targets.Contains("names")
                && (scenario.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || scenario.FeatureName.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                hits.Add($"{scenario.Address,-9} name       {QueryWriter.OneLine($"{scenario.FeatureName} › {scenario.Name}", 110)}");

            if (targets.Contains("errors"))
            {
                if (scenario.ErrorMessage is { } message && message.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    hits.Add($"{scenario.Address,-9} error      {QueryWriter.OneLine(message, 110)}");
                if (scenario.ErrorStackTrace is { } stack && stack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    hits.Add($"{scenario.Address,-9} stack      {QueryWriter.OneLine(StackFrameAround(stack, needle), 110)}");
            }

            if (targets.Contains("uris"))
                foreach (var interaction in scenario.Interactions.Where(i => i.Uri.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                    hits.Add($"{interaction.Address(scenario),-9} uri        {QueryWriter.OneLine(interaction.Uri, 110)}");

            if (targets.Contains("headers"))
                foreach (var interaction in scenario.Interactions.Where(i => i.HeaderCount > 0))
                    foreach (var (key, value) in PayloadReader.Headers(index, interaction))
                        if (key.Contains(needle, StringComparison.OrdinalIgnoreCase) || (value?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
                            hits.Add($"{interaction.Address(scenario),-9} header     {key}: {QueryWriter.OneLine(value, 90)}");
        }

        // Bodies are searched once per distinct content, not once per occurrence: the same body appears
        // dozens of times in a real report and reading it dozens of times would be the slow half of this.
        if (wantsBodies)
        {
            foreach (var body in index.Bodies.Values)
            {
                var content = cache.Raw(body.Hash);
                if (content is null || !content.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                var where = body.Occurrences.Count == 1
                    ? body.Occurrences[0]
                    : $"{body.Occurrences[0]} ×{body.Occurrences.Count}";

                if (options.Values)
                {
                    var paths = PathEngine.PathsContaining(content, needle).Take(4).ToList();
                    if (paths.Count == 0)
                        hits.Add($"{where,-16} body       {Excerpt(content, needle)}");
                    foreach (var path in paths)
                        hits.Add($"{where,-16} body       {path}");
                }
                else
                {
                    hits.Add($"{where,-16} body       {body.Hash} {QueryWriter.Size(body.Length)}  {Excerpt(content, needle)}");
                }
            }
        }

        if (targets.Contains("notes"))
        {
            foreach (var scenario in index.Scenarios)
                for (var d = 0; d < scenario.Diagrams.Count; d++)
                {
                    var diagram = cache.ReadSlice(scenario.Diagrams[d]);
                    if (diagram is null)
                        continue;
                    foreach (var (i, text) in PayloadReader.Notes(diagram))
                        if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                            hits.Add($"{scenario.Address}/d{d}/n{i,-4} note       {Excerpt(text, needle)}");
                }
        }

        if (options.Count)
        {
            writer.Count(hits.Count);
            return 0;
        }

        if (hits.Count == 0)
        {
            writer.Line($"\"{needle}\" is not in {string.Join(", ", targets)}");

            // `grep`'s positional is a search TERM, so an address pasted into it was reinterpreted as a
            // needle and answered "not found" at exit 0 - a confident negative about a scenario that is
            // in the report, from the one verb whose whole job is to answer that question honestly.
            // The grammar stays as it is; the miss says what it did with what it was given.
            if (Address.TryParse(needle, out var mistaken))
                writer.Note(mistaken.Kind switch
                {
                    AddressKind.Body => $"! {needle} is an address, not text — `body <report> {needle}` reads that payload",
                    AddressKind.StableId => $"! {needle} is an address, not text — `steps <report> {needle}` opens that scenario",
                    _ => $"! {needle} is an address, not text — `steps {needle}` opens it"
                });

            writer.Footer("--in bodies,headers,uris,steps,assertions,notes widens the search · notes are searched last because they are the expensive one");
            return 0;
        }

        // The needle alone was not enough: the offset indexes THIS hit list, and without --in and --values
        // the command in the footer rebuilds a different one, so page two silently comes from a different
        // corpus and the total changes underneath the reader. (The numeric path already did this.)
        writer.Page(hits, options.Offset, Math.Min(options.Limit, 200), "hits", hit => writer.Line(hit),
            ["grep", needle, .. options.RerunArgs()]);
        return 0;
    }

    /// <summary>
    /// The one stack frame the needle is in, rather than the whole trace. A stack is the longest string a
    /// scenario carries and printing it whole would make one hit cost more than the rest of the answer;
    /// the frame that matched is what tells the reader where to look.
    /// </summary>
    private static string StackFrameAround(string stack, string needle)
    {
        foreach (var line in stack.Split('\n'))
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return line.Trim();
        return stack;
    }

    private static string Excerpt(string text, string needle)
    {
        var at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return "";
        var from = Math.Max(0, at - 30);
        var to = Math.Min(text.Length, at + needle.Length + 30);
        return (from > 0 ? "…" : "") + QueryWriter.OneLine(text[from..to], 80) + (to < text.Length ? "…" : "");
    }

    private static int Compare(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count < 2
            || !Address.TryParse(options.Positional[0], out var leftAddress)
            || !Address.TryParse(options.Positional[1], out var rightAddress))
        {
            error.WriteLine("Which two? kronikol query compare <report> s3 s7");
            return 2;
        }

        if (index.Scenario(leftAddress.Scenario) is not { } left || index.Scenario(rightAddress.Scenario) is not { } right)
        {
            error.WriteLine("One of those scenarios is not in this report.");
            return 2;
        }

        writer.Line($"{left.Address} {QueryWriter.OneLine(left.Name, 60)}  [{left.Result}]  {left.DurationSeconds:0.##}s");
        writer.Line($"{right.Address} {QueryWriter.OneLine(right.Name, 60)}  [{right.Result}]  {right.DurationSeconds:0.##}s");
        writer.Line();

        if (left.ExampleValues.Count > 0 || right.ExampleValues.Count > 0)
        {
            foreach (var key in left.ExampleValues.Keys.Union(right.ExampleValues.Keys).Order())
            {
                var a = left.ExampleValues.GetValueOrDefault(key, "—");
                var b = right.ExampleValues.GetValueOrDefault(key, "—");
                if (a != b)
                    writer.Line($"example {key}: {a} → {b}");
            }
            writer.Line();
        }

        CompareSequences(writer, "steps",
            left.AllSteps().Select(s => $"{s.Step.Display} [{s.Step.Status}]").ToList(),
            right.AllSteps().Select(s => $"{s.Step.Display} [{s.Step.Status}]").ToList());

        CompareSequences(writer, "calls",
            left.Interactions.Where(i => i.Type == "Request").Select(i => $"{i.ServiceName} {i.Summary()}").ToList(),
            right.Interactions.Where(i => i.Type == "Request").Select(i => $"{i.ServiceName} {i.Summary()}").ToList());

        var leftBodies = left.Interactions.Select(i => i.BodyHash).Where(h => h is not null).ToHashSet();
        var rightBodies = right.Interactions.Select(i => i.BodyHash).Where(h => h is not null).ToHashSet();
        var shared = leftBodies.Intersect(rightBodies).Count();
        writer.Line();
        writer.Line($"bodies: {leftBodies.Count} vs {rightBodies.Count}, {shared} byte-identical");

        // The footer's claim — the first differing call is usually the answer — as an address, not advice.
        var leftPairs = AllInteractions(index, left).ToList();
        var rightPairs = AllInteractions(index, right).ToList();
        for (var i = 0; i < Math.Min(leftPairs.Count, rightPairs.Count); i++)
        {
            var (_, leftRequest, leftResponse) = leftPairs[i];
            var (_, rightRequest, rightResponse) = rightPairs[i];
            if (leftRequest.BodyHash is not null && rightRequest.BodyHash is not null && leftRequest.BodyHash != rightRequest.BodyHash)
            {
                writer.Line($"first differing body: diff {leftRequest.Address(left)} {rightRequest.Address(right)}");
                break;
            }
            if (leftResponse?.BodyHash is not null && rightResponse?.BodyHash is not null && leftResponse.BodyHash != rightResponse.BodyHash)
            {
                writer.Line($"first differing body: diff {leftResponse.Address(left)} {rightResponse.Address(right)}");
                break;
            }
        }

        writer.Footer("a passing neighbour is the best oracle for a failing scenario — the first differing call is usually the answer");
        return 0;
    }

    /// <summary>
    /// One scenario-level change between two runs: broken, new, fixed, slower or gone. The text form
    /// is carried alongside so the five sections render exactly as they did when they were built as
    /// lists of strings, while <c>--json</c> gets one flat list keyed by <c>kind</c>.
    /// </summary>
    private sealed record RunChange(string Kind, string Address, string StableId, string Name, string Result,
        string? ErrorMessage, double? BeforeSeconds, double? AfterSeconds, string Text);

    /// <summary>Scenarios keyed by stableId, each key holding every scenario that carries it, in file order.</summary>
    private static Dictionary<string, List<ScenarioEntry>> GroupByStableId(ReportIndex index)
    {
        var groups = new Dictionary<string, List<ScenarioEntry>>(StringComparer.Ordinal);
        foreach (var scenario in index.Scenarios)
        {
            if (!groups.TryGetValue(scenario.StableId, out var group))
                groups[scenario.StableId] = group = [];
            group.Add(scenario);
        }
        return groups;
    }

    private static void CompareSequences(QueryWriter writer, string noun, List<string> left, List<string> right)
    {
        writer.Line($"{noun}: {left.Count} vs {right.Count}");
        var shown = 0;
        for (var i = 0; i < Math.Max(left.Count, right.Count) && shown < 20; i++)
        {
            var a = i < left.Count ? left[i] : null;
            var b = i < right.Count ? right[i] : null;
            if (a == b)
                continue;

            writer.Line($"  {i,3}  - {QueryWriter.OneLine(a ?? "(none)", 70)}");
            writer.Line($"       + {QueryWriter.OneLine(b ?? "(none)", 70)}");
            shown++;
        }

        if (shown == 0)
            writer.Line("  identical");
    }

    /// <summary>
    /// Services that captured fewer calls than they did in the older run, worst first, plus the total
    /// when it fell. This is the "gold standard" check a suite runs after changing tracking
    /// configuration: interactions can decrease silently, because nothing fails when a client stops
    /// being tracked - the tests still pass and the diagrams are just thinner.
    /// </summary>
    /// <remarks>
    /// Requests only, so a response the capture missed does not read as a lost call. A report carrying
    /// no interactions at all is skipped rather than reported as a total loss: that is what every
    /// mergeable file written before 3.1.0 looks like, and what any run with tracking off looks like.
    /// </remarks>
    private static List<string> TrackingLosses(ReportIndex left, ReportIndex right)
    {
        var rows = new List<string>();
        if (!left.Scenarios.Any(sc => sc.Interactions.Count > 0) || !right.Scenarios.Any(sc => sc.Interactions.Count > 0))
            return rows;

        static Dictionary<string, int> Requests(ReportIndex index) =>
            index.Scenarios.SelectMany(sc => sc.Interactions)
                .Where(i => string.Equals(i.Type, "Request", StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => i.ServiceName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var before = Requests(left);
        var after = Requests(right);

        var totalBefore = before.Values.Sum();
        var totalAfter = after.Values.Sum();
        if (totalAfter < totalBefore)
            rows.Add($"total  {totalBefore} → {totalAfter} calls  ({totalAfter - totalBefore})");

        foreach (var (service, then) in before.OrderByDescending(e => e.Value - (after.TryGetValue(e.Key, out var n) ? n : 0)))
        {
            var now = after.TryGetValue(service, out var count) ? count : 0;
            if (now >= then)
                continue;
            rows.Add(now == 0
                ? $"{QueryWriter.OneLine(service, 40)}  {then} → 0 calls  — no longer tracked"
                : $"{QueryWriter.OneLine(service, 40)}  {then} → {now} calls  ({now - then})");
        }

        return rows;
    }

    /// <summary>
    /// The second report of a diff, resolved and gated exactly as the first one is.
    /// </summary>
    /// <remarks>
    /// This is the third path to <see cref="ReportScanner"/> and it used to be the least careful: no
    /// identity gate, no version gates, and no <see cref="QueryCommand.ResolveReport"/>, so it could not
    /// be handed a directory the way every other verb can, and an arbitrary JSON file became "the new
    /// run" - producing a full removed-everything diff against a file that was never a run at all.
    /// `diff` is also the one verb that returns early from WriteProvenance, so nothing downstream would
    /// have remarked on it either.
    /// </remarks>
    private static ReportIndex? Scan(string path, TextWriter error)
    {
        if (QueryCommand.ResolveReport(path, error) is not { } resolved)
            return null;

        ReportIndex index;
        try
        {
            index = ReportScanner.Scan(resolved);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error.WriteLine($"Could not read {resolved}: {exception.Message}");
            return null;
        }

        return ReportGate.Refuse(index, resolved, error) is null ? index : null;
    }

    /// <summary>One side's provenance header, prefixed with which side it is.</summary>
    private static void WriteSideProvenance(ReportIndex index, string side, QueryWriter writer)
    {
        foreach (var line in QueryCommand.ProvenanceNotes(index))
            writer.Note($"! {side}: {line}");
    }

    /// <summary>
    /// Names for the two sides of a diff: the file name, unless both files are called the same thing -
    /// which is the norm under <c>--baseline</c> - in which case each is shown relative to the deepest
    /// directory they share, so the labels differ by exactly what distinguishes the files.
    /// </summary>
    private static (string Left, string Right) DiffLabels(string leftPath, string rightPath)
    {
        var left = Path.GetFileName(leftPath);
        var right = Path.GetFileName(rightPath);
        if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return (left, right);

        var common = CommonDirectory(Path.GetFullPath(leftPath), Path.GetFullPath(rightPath));
        return common is null
            ? (leftPath, rightPath)
            : (Path.GetRelativePath(common, leftPath), Path.GetRelativePath(common, rightPath));
    }

    private static string? CommonDirectory(string left, string right)
    {
        for (var directory = Path.GetDirectoryName(left); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            if (right.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return directory;
        return null;
    }

    /// <summary>
    /// Compares two bodies in one report, or two runs.
    ///
    /// <para><paramref name="given"/> is the report named on the command line. In
    /// <c>diff &lt;old&gt; &lt;new&gt;</c> that is the OLD one; under <c>--baseline</c> it is the CURRENT
    /// run and the old side is resolved by convention. The two are oriented before anything is printed,
    /// because a diff running backwards reports every regression as a fix and still exits 0.</para>
    /// </summary>
    private static int Diff(ReportIndex given, QueryOptions options, QueryWriter writer, TextWriter error,
        Func<string, string?> getEnv)
    {
        if (options.Positional.Count == 0 && !options.Baseline)
        {
            error.WriteLine("Diff takes two reports (kronikol query diff <old.json> <new.json> [--body s3/i47])");
            error.WriteLine("or two bodies in one report (kronikol query diff <report> s3/i47 s7/i47).");
            error.WriteLine("Or compare against last-green: kronikol query diff <report> --baseline.");
            return 2;
        }

        // A first positional that parses as an address is a body diff inside the single report;
        // otherwise it is the second report of the two-file run diff.
        if (options.Positional.Count > 0 && Address.TryParse(options.Positional[0], out _))
            return BodyDiff(given, options, writer, error);

        ReportIndex left, right;
        if (options.Baseline)
        {
            if (ResolveBaseline(given, getEnv, error) is not { } baseline)
                return 2;

            if (Scan(baseline, error) is not { } scanned)
                return 1;

            // The named report is the new run; the baseline is what it is measured against.
            (left, right) = (scanned, given);
        }
        else
        {
            if (Scan(options.Positional[0], error) is not { } scanned)
                return 1;

            (left, right) = (given, scanned);
        }

        if (options.BodyAddress is { } bodyAddress)
            return CrossRunBodyDiff(left, right, bodyAddress, options, writer, error);

        // The provenance header every other verb gets, which `diff` alone used to skip because a note
        // with two reports in scope does not say which one it is about. The side is the whole point: a
        // defaulted verdict on the NEW run turns a scenario that died mid-run into `Fixed`, and the same
        // diagnostic on the OLD run means the opposite. `WriteProvenance` returns early for this verb so
        // that these can be written once both sides are resolved and can be named.
        WriteSideProvenance(left, "old", writer);
        WriteSideProvenance(right, "new", writer);

        // Under --baseline both files are usually called TestRunReport.json, so a bare file name would
        // label the two sides identically.
        var (leftLabel, rightLabel) = DiffLabels(left.Path, right.Path);
        writer.Line($"- {leftLabel}  {left.StartTime}  {left.Scenarios.Count} scenarios, {left.Scenarios.Count(s => s.Failed)} failed");
        writer.Line($"+ {rightLabel}  {right.StartTime}  {right.Scenarios.Count} scenarios, {right.Scenarios.Count(s => s.Failed)} failed");
        writer.Line();

        // stableId is the cross-run key: it survives a re-run, and since example values went into the hash
        // it tells one row of a scenario outline from another, which is where per-row matching matters.
        // It is not unique, though: a [Theory] with repeated data, the same row in two Examples: blocks or a
        // retried scenario give several scenarios one id, so each id holds a list and the lists are matched
        // in order — a duplicate key must never take the whole diff down.
        var before = GroupByStableId(left);
        var after = GroupByStableId(right);

        // A report older than 3.0.47 carries no stableId at all, so every scenario lands in the
        // empty-string group. That is not a collision - it is a file with no cross-run identity, and
        // the match falls back to position. Saying "N scenarios share a stableId (repeated rows or
        // retries)" about the whole file would be alarming and untrue.
        var unidentified = before.Values.Concat(after.Values).Where(group => group[0].StableId.Length == 0).Sum(group => group.Count);
        if (unidentified > 0)
            writer.Note("! this report has no stableIds (written before 3.0.47) — scenarios matched by position");

        var repeated = before.Values.Concat(after.Values)
            .Where(group => group.Count > 1 && group[0].StableId.Length > 0)
            .Sum(group => group.Count);
        if (repeated > 0)
            writer.Note($"! {repeated} scenarios share a stableId (repeated rows or retries) — matched in order");

        // Records, not pre-rendered strings. The text below renders them back byte for byte; a
        // string is the one thing `items` cannot make structure out of, and these five sections are
        // the whole content of a run diff.
        var broke = new List<RunChange>();
        var fixedUp = new List<RunChange>();
        var slower = new List<RunChange>();

        foreach (var (id, nowGroup) in after)
        {
            before.TryGetValue(id, out var thenGroup);
            for (var position = 0; position < nowGroup.Count; position++)
            {
                var now = nowGroup[position];
                if (thenGroup is null || position >= thenGroup.Count)
                {
                    broke.Add(new RunChange("new", now.Address, now.StableId, now.Name, now.Result, null, null, null,
                        $"  new   {now.Address} {QueryWriter.OneLine(now.Name, 70)} [{now.Result}]"));
                    continue;
                }

                var then = thenGroup[position];
                if (then.Failed && !now.Failed)
                    fixedUp.Add(new RunChange("fixed", now.Address, now.StableId, now.Name, now.Result, null, null, null,
                        $"  fixed {now.Address} {QueryWriter.OneLine(now.Name, 70)}"));
                else if (!then.Failed && now.Failed)
                    broke.Add(new RunChange("broke", now.Address, now.StableId, now.Name, now.Result,
                        now.ErrorMessage, null, null,
                        $"  BROKE {now.Address} {QueryWriter.OneLine(now.Name, 70)}"
                        + (now.ErrorMessage is { } e ? $"\n          {QueryWriter.OneLine(e, 100)}" : "")));

                if (then.DurationSeconds > 0.1 && now.DurationSeconds > then.DurationSeconds * 1.5)
                    slower.Add(new RunChange("slower", now.Address, now.StableId, now.Name, now.Result, null,
                        then.DurationSeconds, now.DurationSeconds,
                        $"  {now.Address} {then.DurationSeconds:0.##}s → {now.DurationSeconds:0.##}s  {QueryWriter.OneLine(now.Name, 60)}"));
            }
        }

        // Every old scenario beyond what the new run holds under the same id — a whole id that vanished,
        // or the third repeat of a row that now runs twice.
        var gone = before
            .SelectMany(entry => entry.Value.Skip(after.TryGetValue(entry.Key, out var nowGroup) ? nowGroup.Count : 0))
            .ToArray();

        var goneRows = gone
            .Select(scenario => new RunChange("gone", scenario.Address, scenario.StableId, scenario.Name,
                scenario.Result, scenario.ErrorMessage, null, null,
                $"  {QueryWriter.OneLine(scenario.Name, 80)}"))
            .ToList();

        Section("Broken", broke);
        Section("Fixed", fixedUp);
        Section("Slower", slower);
        if (goneRows.Count > 0)
        {
            writer.Line($"Gone ({goneRows.Count}):");
            foreach (var row in goneRows.Take(10))
                writer.Line(row.Text);
        }

        // Tracking fidelity is the failure this diff exists to catch that no test failure will: an
        // upgrade or a config change stops a client being tracked, every test still passes, and the
        // diagrams quietly lose a service. Reported only when calls were LOST - a run that captured
        // more than last time needs no warning.
        var trackingRows = TrackingLosses(left, right);
        if (trackingRows.Count > 0)
        {
            writer.Line($"Tracking ({trackingRows.Count}):");
            foreach (var row in trackingRows.Take(10))
                writer.Line("  " + row);
            if (trackingRows.Count > 10)
                writer.Line($"  … and {trackingRows.Count - 10} more");
            writer.Line();
        }

        if (broke.Count == 0 && fixedUp.Count == 0 && slower.Count == 0 && goneRows.Count == 0 && trackingRows.Count == 0)
            writer.Note("no change in results, timings or tracked calls");

        // One flat `items`, each row saying which kind of change it is - the text renders the same
        // rows under five headings, but a heading is a layout, not a field. Uncapped here on purpose:
        // the Take(15)/Take(10) above keep the terminal readable, and a script asked for everything.
        writer.Data("left", new { report = left.Path, startTime = left.StartTime, scenarios = left.Scenarios.Count, failed = left.Scenarios.Count(s => s.Failed) });
        writer.Data("right", new { report = right.Path, startTime = right.StartTime, scenarios = right.Scenarios.Count, failed = right.Scenarios.Count(s => s.Failed) });
        foreach (var change in broke.Concat(fixedUp).Concat(slower).Concat(goneRows))
            writer.Item(new
            {
                kind = change.Kind,
                address = change.Address,
                stableId = change.StableId,
                scenario = change.Name,
                result = change.Result,
                errorMessage = change.ErrorMessage,
                beforeSeconds = change.BeforeSeconds,
                afterSeconds = change.AfterSeconds
            });
        writer.Data("tracking", trackingRows);

        writer.Footer("matched on stableId · compare s3 s7 for two scenarios in one run");
        return 0;

        void Section(string title, List<RunChange> rows)
        {
            if (rows.Count == 0)
                return;
            writer.Line($"{title} ({rows.Count}):");
            foreach (var row in rows.Take(15))
                writer.Line(row.Text);
            if (rows.Count > 15)
                writer.Line($"  … {rows.Count - 15} more");
            writer.Line();
        }
    }
}
