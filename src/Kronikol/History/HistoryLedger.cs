using System.Diagnostics;

namespace Kronikol.History;

/// <summary>
/// The deterministic observables of one ledger read, in the house style (<c>PayloadOpens</c> is the
/// precedent): the invariant a test pins is <c>LinesParsed ≤ window × suites + rosters + header</c>,
/// never a wall-clock. Scanning is file-bounded and <c>kronikol history prune</c> is what keeps it cheap;
/// parsing is window-bounded, and a regression to "parse the whole ledger" fails a test rather than slowly
/// ruining everyone's test runs (§2.6, §7.7).
/// </summary>
/// <param name="LinesScanned">Every line the reader looked at.</param>
/// <param name="LinesParsed">The lines it parsed in full: the header, every roster, and the windowed runs.</param>
/// <param name="RunsKept">The run lines kept across every suite.</param>
/// <param name="RostersKept">The roster lines kept.</param>
/// <param name="DamagedLines">Lines that could not be parsed and were skipped.</param>
/// <param name="Elapsed">How long the read took.</param>
/// <param name="ShapesKept">The shapes lines kept.</param>
public sealed record HistoryStats(int LinesScanned, int LinesParsed, int RunsKept, int RostersKept, int DamagedLines, TimeSpan Elapsed, int ShapesKept = 0);

/// <summary>
/// A ledger in memory: every roster, the last <c>window</c> runs of each suite in append order, and the
/// id and stream of every run line, which is what lets a run be read against its own stream's runs from
/// before its own line however far back that is.
/// </summary>
public sealed class HistoryLedger
{
    private readonly Dictionary<string, HistoryRoster> _rosters;
    private readonly Dictionary<string, List<HistoryRun>> _runs;
    private readonly Dictionary<string, HistoryShapes> _shapes;
    private readonly Dictionary<string, HistorySuiteLines> _lines;
    private readonly Func<string[]?>? _reload;
    private readonly object _gate = new();

    internal HistoryLedger(int? version, string? generator, Dictionary<string, HistoryRoster> rosters,
        Dictionary<string, List<HistoryRun>> runs, HistoryStats stats, Dictionary<string, HistoryShapes>? shapes = null,
        Dictionary<string, HistorySuiteLines>? lines = null, Func<string[]?>? reload = null)
    {
        _shapes = shapes ?? new Dictionary<string, HistoryShapes>(StringComparer.Ordinal);
        _lines = lines ?? new Dictionary<string, HistorySuiteLines>(StringComparer.Ordinal);
        _reload = reload;
        Version = version;
        Generator = generator;
        _rosters = rosters;
        _runs = runs;
        Stats = stats;
    }

    /// <summary>An empty ledger — what a missing file reads as.</summary>
    public static HistoryLedger Empty { get; } = new(null, null, new Dictionary<string, HistoryRoster>(StringComparer.Ordinal),
        new Dictionary<string, List<HistoryRun>>(StringComparer.Ordinal), new HistoryStats(0, 0, 0, 0, 0, TimeSpan.Zero));

    /// <summary>The format version the file declared; null for a missing or headerless file.</summary>
    public int? Version { get; }

    /// <summary>The writer that created the file.</summary>
    public string? Generator { get; }

    /// <summary>What the read cost.</summary>
    public HistoryStats Stats { get; }

    /// <summary>
    /// Run lines parsed after the read, because an analysis asked for runs the window had not kept. The
    /// observable beside <see cref="HistoryStats.LinesParsed"/>: a test run reading its own stream leaves it at 0.
    /// </summary>
    internal int LinesParsedOnDemand { get; private set; }

    /// <summary>The suites with at least one run in the window. A null suite is keyed as the empty string.</summary>
    public IReadOnlyList<string?> Suites => _runs.Keys.Select(k => k.Length == 0 ? null : k).ToArray();

    /// <summary>The runs of a suite in append order, oldest first; empty when it has none.</summary>
    public IReadOnlyList<HistoryRun> Runs(string? suite) =>
        _runs.TryGetValue(SuiteKey(suite), out var runs) ? runs : [];

    /// <summary>The roster with a hash, or null.</summary>
    public HistoryRoster? Roster(string hash) =>
        _rosters.TryGetValue(hash, out var roster) ? roster : null;

    /// <summary>Every roster the ledger holds.</summary>
    public IReadOnlyCollection<HistoryRoster> Rosters => _rosters.Values;

    /// <summary>The shapes list with a hash, or null.</summary>
    public HistoryShapes? Shapes(string hash) =>
        _shapes.TryGetValue(hash, out var shapes) ? shapes : null;

    /// <summary>Every shapes list the ledger holds.</summary>
    public IReadOnlyCollection<HistoryShapes> AllShapes => _shapes.Values;

    /// <summary>The most recent run of a suite, or null.</summary>
    public HistoryRun? LatestRun(string? suite)
    {
        var runs = Runs(suite);
        return runs.Count == 0 ? null : runs[^1];
    }

    /// <summary>The runs of a suite in one branch stream, oldest first.</summary>
    public IReadOnlyList<HistoryRun> Runs(string? suite, string stream) =>
        Runs(suite).Where(r => string.Equals(r.Stream, stream, StringComparison.Ordinal)).ToArray();

    /// <summary>
    /// The history a run is read against: the last <paramref name="window"/> runs (0 for all) of one
    /// stream of its suite that were appended before the run's own line, oldest first. A run that is not
    /// in the ledger yet, which is every run while it is being generated, reads against all of it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Runs(string?)"/> filtered: that is the last lines of the suite whatever their stream
    /// and wherever the run sits, so a report read again later saw the runs that came after it, a report
    /// older than the window saw nothing else, and pull request runs crowded a quiet target branch out
    /// (#95). The index of every run line is what answers instead, and a line the read did not keep is
    /// parsed when it is asked for, from the source read again: that costs a second scan, and only for a
    /// request the window cannot answer, where keeping every line would cost memory on every run.
    /// </remarks>
    internal IReadOnlyList<HistoryRun> PriorRuns(string? suite, string stream, string currentRunId, int window)
    {
        if (!_lines.TryGetValue(SuiteKey(suite), out var lines))
            return [];

        lock (_gate)
        {
            var entries = lines.Entries;
            // The run's own line ends its history, and the first of them when merge=union left it twice:
            // that is when it was recorded.
            var end = entries.FindIndex(e => string.Equals(e.Id, currentRunId, StringComparison.Ordinal));
            if (end < 0)
                end = entries.Count;

            var prior = new List<HistoryRun>();
            string?[]? source = null;
            var reloaded = false;
            for (var ordinal = end - 1; ordinal >= 0 && (window <= 0 || prior.Count < window); ordinal--)
            {
                var entry = entries[ordinal];
                if (!string.Equals(entry.Stream, stream, StringComparison.Ordinal) || lines.Unreadable.Contains(ordinal))
                    continue;

                if (!lines.Parsed.TryGetValue(ordinal, out var run))
                {
                    if (!reloaded)
                    {
                        source = Reload(SuiteKey(suite));
                        reloaded = true;
                    }
                    run = ParseOnDemand(lines, ordinal, source);
                    if (run is null)
                        continue;
                }

                prior.Add(run);
            }

            prior.Reverse();
            return prior;
        }
    }

    /// <summary>A suite's run lines by their place among its runs, from the source read again; null when it cannot be.</summary>
    private string?[]? Reload(string suiteKey)
    {
        if (_reload?.Invoke() is not { } raw)
            return null;

        var found = new List<string?>();
        foreach (var text in raw)
        {
            var line = text.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            var (kind, _, suite, _, _) = HistoryJson.PeekRun(line);
            if (kind == HistoryLineKind.Run && string.Equals(SuiteKey(suite), suiteKey, StringComparison.Ordinal))
                found.Add(line);
        }
        return found.ToArray();
    }

    private HistoryRun? ParseOnDemand(HistorySuiteLines lines, int ordinal, string?[]? source)
    {
        // The source may have been pruned or compacted since the read, and then a place among the runs no
        // longer names the same line: the id says whether it does, and a line that does not is left out.
        if (source is null || ordinal >= source.Length || source[ordinal] is not { } line
            || !string.Equals(HistoryJson.PeekRun(line).Id, lines.Entries[ordinal].Id, StringComparison.Ordinal))
            return null;

        try
        {
            LinesParsedOnDemand++;
            if (HistoryJson.Parse(line).Run is { } run)
                return lines.Parsed[ordinal] = run;
        }
        catch (FormatException)
        {
        }

        lines.Unreadable.Add(ordinal);
        return null;
    }

    /// <summary>The key a suite is stored under.</summary>
    public static string SuiteKey(string? suite) => suite ?? "";

    /// <summary>Whether any suite has a run.</summary>
    public bool IsEmpty => _runs.Count == 0;
}

/// <summary>
/// Every run line of one suite as the reader scanned it, in append order: its id and its stream, which
/// cost a peek, and the run itself once the line has been parsed. Small beside the lines themselves, so
/// it is kept for every line where the window keeps only the last of them.
/// </summary>
internal sealed class HistorySuiteLines
{
    /// <summary>The id and stream of each run line, by its place among the suite's runs.</summary>
    public List<(string? Id, string Stream)> Entries { get; } = [];

    /// <summary>The runs parsed so far, by the same place.</summary>
    public Dictionary<int, HistoryRun> Parsed { get; } = [];

    /// <summary>The places whose line was tried and could not be parsed.</summary>
    public HashSet<int> Unreadable { get; } = [];
}

/// <summary>How a read ended.</summary>
public enum HistoryReadOutcome
{
    /// <summary>The ledger was read; it may still hold no runs.</summary>
    Read,

    /// <summary>There is no ledger at that path yet — no history, and not an error.</summary>
    Missing,

    /// <summary>A writer held the lock for longer than the retry budget; the read was abandoned (§3.4).</summary>
    Locked,

    /// <summary>The file declares a format version this build does not understand (§3.5).</summary>
    UnsupportedVersion,

    /// <summary>The file could not be read for another reason, named in the message.</summary>
    Unreadable
}

/// <summary>The result of a read: a ledger when there is one, and why not otherwise.</summary>
/// <param name="Ledger">The ledger, on <see cref="HistoryReadOutcome.Read"/> and <see cref="HistoryReadOutcome.Missing"/>; null otherwise.</param>
/// <param name="Outcome">How the read ended.</param>
/// <param name="Message">What to tell a person, when the outcome is not a plain read.</param>
public sealed record HistoryReadResult(HistoryLedger? Ledger, HistoryReadOutcome Outcome, string? Message);

/// <summary>
/// The retry budget for a locked file: <paramref name="Attempts"/> tries, each preceded by a jittered
/// delay of up to <paramref name="MaxDelayMilliseconds"/>. The defaults come from the measured harness in
/// <c>tools/history-bench</c>: 32 writers appending one line each were all served in 135&#160;ms, and
/// 3,200 contended appends exhausted a 200-attempt budget three times — and said so (§6.5).
/// </summary>
public sealed record HistoryLockBudget(int Attempts = 200, int MaxDelayMilliseconds = 25)
{
    /// <summary>The budget a run uses when nothing overrides it.</summary>
    public static HistoryLockBudget Default { get; } = new();
}

/// <summary>
/// Reads a ledger by streaming it: every line scanned, only the windowed runs parsed, a damaged line
/// skipped and counted rather than fatal, and a locked file retried rather than failed (§3.4).
/// </summary>
public static class HistoryLedgerReader
{
    /// <summary>
    /// Reads the last <paramref name="window"/> runs of every suite (0 for all of them). Never throws for
    /// a missing, locked, damaged or foreign file: the outcome says which, because a test run must never
    /// fail over its history (§6.2).
    /// </summary>
    public static HistoryReadResult Read(string path, int window, HistoryLockBudget? budget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        budget ??= HistoryLockBudget.Default;

        if (!File.Exists(path))
            return new HistoryReadResult(HistoryLedger.Empty, HistoryReadOutcome.Missing, $"no ledger at {path}");

        var watch = Stopwatch.StartNew();
        string[] lines;
        try
        {
            lines = ReadLines(path, budget);
        }
        catch (HistoryLockTimeoutException exception)
        {
            return new HistoryReadResult(null, HistoryReadOutcome.Locked,
                $"the ledger at {path} stayed locked by another writer for {exception.Attempts} attempts; history is unavailable for this run");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HistoryReadResult(null, HistoryReadOutcome.Unreadable, $"could not read the ledger at {path}: {exception.Message}");
        }

        // The lines are let go once the window is built. A run read against a stream the window did not
        // keep (HistoryLedger.PriorRuns) has them read again, under the same budget, rather than held.
        return Build(lines, window, watch, path, () =>
        {
            try
            {
                return File.Exists(path) ? ReadLines(path, budget) : null;
            }
            catch (Exception exception) when (exception is HistoryLockTimeoutException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        });
    }

    private static string[] ReadLines(string path, HistoryLockBudget budget) =>
        HistoryLock.Retry(budget, () =>
        {
            // Permissive sharing, so a reader is denied only while a writer holds the exclusive lock
            // for the length of one append; the retry covers exactly that window.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var read = new List<string>();
            while (reader.ReadLine() is { } line)
                read.Add(line);
            return read.ToArray();
        });

    /// <summary>Reads a ledger already in memory — the same rules over text, for tests and for verification.</summary>
    public static HistoryReadResult Parse(string text, int window)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] Lines() => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return Build(Lines(), window, Stopwatch.StartNew(), "<text>", Lines);
    }

    private static HistoryReadResult Build(string[] lines, int window, Stopwatch watch, string path, Func<string[]?> reload)
    {
        int? version = null;
        string? generator = null;
        var rosters = new Dictionary<string, HistoryRoster>(StringComparer.Ordinal);
        var shapes = new Dictionary<string, HistoryShapes>(StringComparer.Ordinal);
        var kept = new Dictionary<string, Queue<(int Ordinal, string Line)>>(StringComparer.Ordinal);
        var index = new Dictionary<string, HistorySuiteLines>(StringComparer.Ordinal);
        var scanned = 0;
        var parsed = 0;
        var damaged = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            scanned++;

            var (kind, id, suite, _, branch) = HistoryJson.PeekRun(line);
            switch (kind)
            {
                case HistoryLineKind.Header:
                    // The header is read for its version and not counted as parsed: LinesParsed is the
                    // window-bounded figure a test pins, and the header is one fixed line.
                    var headerParsed = 0;
                    if (TryParse(line, ref headerParsed, ref damaged) is { } header)
                    {
                        version = header.Version;
                        generator = header.Generator;
                        if (version is { } declared && declared > HistoryFormat.Version)
                            return new HistoryReadResult(null, HistoryReadOutcome.UnsupportedVersion,
                                $"the ledger at {path} declares historyFormatVersion {declared}; this build understands {HistoryFormat.Version}. Upgrade Kronikol.");
                    }
                    break;

                case HistoryLineKind.Roster:
                    if (TryParse(line, ref parsed, ref damaged)?.Roster is { } roster)
                        rosters[roster.Hash] = roster;
                    break;

                case HistoryLineKind.Shapes:
                    if (TryParse(line, ref parsed, ref damaged)?.Shapes is { } shapesLine)
                        shapes[shapesLine.Hash] = shapesLine;
                    break;

                case HistoryLineKind.Run:
                    // Bucketed by suite before it is parsed: the window is per suite, and only the lines
                    // that survive it pay for a full parse.
                    var key = HistoryLedger.SuiteKey(suite);
                    if (!kept.TryGetValue(key, out var queue))
                    {
                        kept[key] = queue = new Queue<(int, string)>();
                        index[key] = new HistorySuiteLines();
                    }
                    // Every run line is indexed by id and stream, which the peek already paid for: what a
                    // run is read against is a stream's runs before its own line, not the suite's last ones.
                    var entries = index[key].Entries;
                    queue.Enqueue((entries.Count, line));
                    entries.Add((id, branch is { Length: > 0 } ? branch : HistoryRun.LocalStream));
                    if (window > 0 && queue.Count > window)
                        queue.Dequeue();
                    break;

                default:
                    damaged++;
                    break;
            }
        }

        var runs = new Dictionary<string, List<HistoryRun>>(StringComparer.Ordinal);
        foreach (var (key, queue) in kept)
        {
            var list = new List<HistoryRun>(queue.Count);
            foreach (var (ordinal, line) in queue)
            {
                if (TryParse(line, ref parsed, ref damaged)?.Run is { } run)
                {
                    list.Add(run);
                    index[key].Parsed[ordinal] = run;
                }
                else
                {
                    index[key].Unreadable.Add(ordinal);
                }
            }
            if (list.Count > 0)
                runs[key] = list;
        }

        watch.Stop();
        var stats = new HistoryStats(scanned, parsed, runs.Values.Sum(r => r.Count), rosters.Count, damaged, watch.Elapsed, shapes.Count);
        return new HistoryReadResult(new HistoryLedger(version, generator, rosters, runs, stats, shapes, index, reload), HistoryReadOutcome.Read, null);
    }

    private static HistoryLine? TryParse(string line, ref int parsed, ref int damaged)
    {
        try
        {
            var result = HistoryJson.Parse(line);
            parsed++;
            return result;
        }
        catch (FormatException)
        {
            damaged++;
            return null;
        }
    }

    /// <summary>
    /// The structural checks <c>kronikol history verify</c> runs: a version this build reads, every run's
    /// roster present, every positional array the roster's length, and — the one <c>merge=union</c> can
    /// produce — no two rosters sharing a key with different contents (§3.1). Empty means sound.
    /// </summary>
    public static IReadOnlyList<string> Verify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var findings = new List<string>();
        if (!File.Exists(path))
        {
            findings.Add($"no ledger at {path}");
            return findings;
        }

        var rosters = new Dictionary<string, HistoryRoster>(StringComparer.Ordinal);
        var shapes = new Dictionary<string, HistoryShapes>(StringComparer.Ordinal);
        var runs = new List<(int Line, HistoryRun Run)>();
        var number = 0;
        var sawHeader = false;

        foreach (var raw in File.ReadAllLines(path))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            HistoryLine parsed;
            try
            {
                parsed = HistoryJson.Parse(line);
            }
            catch (FormatException exception)
            {
                findings.Add($"line {number}: unreadable ({exception.Message})");
                continue;
            }

            switch (parsed.Kind)
            {
                case HistoryLineKind.Header:
                    sawHeader = true;
                    if (number != 1)
                        findings.Add($"line {number}: a header that is not the first line");
                    if (parsed.Version is { } version && version > HistoryFormat.Version)
                        findings.Add($"line {number}: historyFormatVersion {version} is newer than this build understands ({HistoryFormat.Version})");
                    break;

                case HistoryLineKind.Roster:
                    var roster = parsed.Roster!;
                    if (!string.Equals(HistoryRoster.ComputeHash(roster.Suite, roster.Ids), roster.Hash, StringComparison.Ordinal))
                        findings.Add($"line {number}: roster {roster.Hash} does not hash its own suite and ids (expected {HistoryRoster.ComputeHash(roster.Suite, roster.Ids)})");
                    if (rosters.TryGetValue(roster.Hash, out var existing))
                    {
                        if (!existing.Ids.SequenceEqual(roster.Ids, StringComparer.Ordinal))
                            findings.Add($"line {number}: roster {roster.Hash} appears twice with different contents — a counter-keyed roster merged under union; every run referencing it is ambiguous");
                    }
                    else
                    {
                        rosters[roster.Hash] = roster;
                    }
                    break;

                case HistoryLineKind.Run:
                    runs.Add((number, parsed.Run!));
                    break;

                case HistoryLineKind.Shapes:
                    var shapesLine = parsed.Shapes!;
                    if (!string.Equals(HistoryShapes.ComputeHash(shapesLine.Calls), shapesLine.Hash, StringComparison.Ordinal))
                        findings.Add($"line {number}: shapes {shapesLine.Hash} does not hash its own calls (expected {HistoryShapes.ComputeHash(shapesLine.Calls)})");
                    if (shapes.TryGetValue(shapesLine.Hash, out var existingShapes))
                    {
                        if (!existingShapes.Calls.SequenceEqual(shapesLine.Calls, StringComparer.Ordinal))
                            findings.Add($"line {number}: shapes {shapesLine.Hash} appears twice with different contents");
                    }
                    else
                    {
                        shapes[shapesLine.Hash] = shapesLine;
                    }
                    break;
            }
        }

        if (!sawHeader)
            findings.Add("no header line: the file does not declare a historyFormatVersion");

        var seen = new HashSet<(string?, string)>();
        foreach (var (line, run) in runs)
        {
            if (!rosters.TryGetValue(run.RosterHash, out var roster))
            {
                findings.Add($"line {line}: run {run.Id} references roster {run.RosterHash}, which is not in the file");
                continue;
            }

            if (!seen.Add((run.Suite, run.Id)))
                findings.Add($"line {line}: run {run.Id} of suite {run.Suite ?? "(null)"} appears more than once");

            if (run.Results.Length != roster.Count)
                findings.Add($"line {line}: run {run.Id} has {run.Results.Length} results against a roster of {roster.Count}");
            if (run.Attempts.Length != 0 && run.Attempts.Length != roster.Count)
                findings.Add($"line {line}: run {run.Id} has {run.Attempts.Length} attempts against a roster of {roster.Count}");
            if (run.Durations is { } durations && durations.Count != roster.Count)
                findings.Add($"line {line}: run {run.Id} has {durations.Count} durations against a roster of {roster.Count}");
            if (run.ShapeSet is { } shapeSet && shapeSet.Count != roster.Count)
                findings.Add($"line {line}: run {run.Id} has {shapeSet.Count} shapes against a roster of {roster.Count}");
            if (run.ShapesHash is { } shapesHash)
            {
                if (!shapes.TryGetValue(shapesHash, out var referencedShapes))
                    findings.Add($"line {line}: run {run.Id} references shapes {shapesHash}, which is not in the file");
                if (run.CallSets is { } callSets)
                {
                    if (callSets.Count != roster.Count)
                        findings.Add($"line {line}: run {run.Id} has {callSets.Count} call sets against a roster of {roster.Count}");
                    if (referencedShapes is not null && callSets.Any(set => set.Any(index => index < 0 || index >= referencedShapes.Count)))
                        findings.Add($"line {line}: run {run.Id} indexes past the {referencedShapes.Count} calls of shapes {shapesHash}");
                }
            }
            else if (run.CallSets is not null)
            {
                findings.Add($"line {line}: run {run.Id} carries call sets but names no shapes line");
            }
            if (run.Errors is { } errors)
            {
                if (errors.Count != roster.Count)
                    findings.Add($"line {line}: run {run.Id} has {errors.Count} errors against a roster of {roster.Count}");
                foreach (var key in errors.Where(k => k is not null && !run.ErrorText.ContainsKey(k)).Distinct())
                    findings.Add($"line {line}: run {run.Id} references error key {key} with no text");
            }
        }

        return findings;
    }
}

/// <summary>Thrown inside <see cref="HistoryLock.Retry"/> when the budget is exhausted; callers translate it into an outcome.</summary>
public sealed class HistoryLockTimeoutException(int attempts, Exception inner)
    : IOException($"the ledger stayed locked for {attempts} attempts", inner)
{
    /// <summary>How many attempts were made.</summary>
    public int Attempts { get; } = attempts;
}

/// <summary>The jittered retry both the reader and the writer use around a file that another process holds.</summary>
internal static class HistoryLock
{
    /// <summary>Runs <paramref name="action"/> until it succeeds or the budget is spent.</summary>
    public static T Retry<T>(HistoryLockBudget budget, Func<T> action)
    {
        var attempts = Math.Max(1, budget.Attempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException exception) when (attempt < attempts && IsSharingViolation(exception))
            {
                // Jitter, not a fixed delay: thirty-two writers released at once and retrying on the same
                // clock would collide again on the same clock.
                Thread.Sleep(Random.Shared.Next(1, Math.Max(2, budget.MaxDelayMilliseconds + 1)));
            }
            catch (IOException exception) when (attempt >= attempts && IsSharingViolation(exception))
            {
                throw new HistoryLockTimeoutException(attempts, exception);
            }
        }
    }

    /// <summary>
    /// Whether an <see cref="IOException"/> is the file being held by someone else rather than a
    /// permanent condition. Neither <c>FileNotFound</c> nor <c>DirectoryNotFound</c> nor a full disk is
    /// worth retrying two hundred times.
    /// </summary>
    private static bool IsSharingViolation(IOException exception) =>
        exception is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException);
}
