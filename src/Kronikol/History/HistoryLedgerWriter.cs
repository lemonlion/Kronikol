using System.Text;

namespace Kronikol.History;

/// <summary>How an append ended.</summary>
public enum HistoryAppendOutcome
{
    /// <summary>The run line (and its roster, if new) was written and flushed.</summary>
    Appended,

    /// <summary>A run with the same suite and id is already in the file; nothing was written (§6.5).</summary>
    Duplicate,

    /// <summary>
    /// A run with the same suite and id was already in the file from an EARLIER attempt of the same run,
    /// and this attempt was overlaid on its line (<see cref="HistoryLedgerWriter.Amend"/>).
    /// </summary>
    Amended,

    /// <summary>The file declares a version this build does not write; nothing was written (§3.5).</summary>
    UnsupportedVersion,

    /// <summary>The lock could not be won within the budget; nothing was written, and the caller keeps the fragment (§6.5).</summary>
    LockTimeout,

    /// <summary>The write failed for another reason, named in the message.</summary>
    Failed
}

/// <summary>The result of an append.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Message">What to tell a person, when it did not simply succeed.</param>
/// <param name="Attempts">How many lock attempts it took.</param>
public sealed record HistoryAppendResult(HistoryAppendOutcome Outcome, string? Message, int Attempts);

/// <summary>What a prune or compact did.</summary>
/// <param name="RunsKept">Run lines that survived.</param>
/// <param name="RunsDropped">Run lines outside the window that were removed.</param>
/// <param name="RostersDropped">Rosters no surviving run referenced.</param>
/// <param name="Version">The format version the file now declares.</param>
/// <param name="ShapesDropped">Shapes lists no surviving run referenced.</param>
public sealed record HistoryRewriteResult(int RunsKept, int RunsDropped, int RostersDropped, int Version, int ShapesDropped = 0);

/// <summary>
/// Appends to a ledger under an exclusive lock.
///
/// <para><b>The lock is the mechanism, not decoration.</b> Measured on NTFS, overlayfs and ext4: a plain
/// append from thirty-two processes silently loses a third to two-thirds of its lines, because each seeks
/// to a stale end offset and overwrites the others, and believes it succeeded. <c>FileShare.None</c> with a
/// jittered retry kept 320 of 320 everywhere. Starvation is detectable — a writer that exhausts its budget
/// says so — which is what makes "keep the fragment, diagnose, let the next run fold it" safe (§6.5).</para>
///
/// <para><b>What happens under the lock.</b> An empty file gets a header. A file whose header declares a
/// newer version is left exactly as it is: an older line in a newer file is a file neither version reads
/// correctly, and no later diagnostic would catch it. Otherwise the existing lines are peeked for the run's
/// (suite, id) and the roster's hash, the roster line is written only when new, the run line always, and
/// both are flushed to disk before the lock is released.</para>
/// </summary>
public static class HistoryLedgerWriter
{
    /// <summary>Appends one run to the ledger at <paramref name="path"/>, creating the file and its directory when absent.</summary>
    public static HistoryAppendResult Append(string path, HistoryRoster roster, HistoryRun run, string generator, HistoryLockBudget? budget = null,
        HistoryShapes? shapes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(run);
        budget ??= HistoryLockBudget.Default;

        if (!string.Equals(roster.Hash, run.RosterHash, StringComparison.Ordinal))
            throw new ArgumentException("The run does not reference the roster it was given.", nameof(run));
        if (run.ShapesHash is { } shapesHash && shapes is not null && !string.Equals(shapes.Hash, shapesHash, StringComparison.Ordinal))
            throw new ArgumentException("The run references a different shapes list from the one it was given.", nameof(shapes));
        // A caller with no list to give (an older caller, a run rebuilt from a report) writes a line without
        // the references: the hash and the call sets are only meaningful beside the list they index into.
        if (shapes is null && (run.ShapesHash is not null || run.CallSets is not null))
            run = run with { ShapesHash = null, CallSets = null };

        var attempts = 0;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            return HistoryLock.Retry(budget, () =>
            {
                attempts++;
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return AppendLocked(stream, path, roster, run, generator, attempts, shapes);
            });
        }
        catch (HistoryLockTimeoutException exception)
        {
            return new HistoryAppendResult(HistoryAppendOutcome.LockTimeout,
                $"the ledger at {path} stayed locked by another writer for {exception.Attempts} attempts; the run's fragment is kept and the next run folds it",
                exception.Attempts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HistoryAppendResult(HistoryAppendOutcome.Failed, $"could not append to the ledger at {path}: {exception.Message}", attempts);
        }
    }

    /// <summary>
    /// Appends a run, or - when the ledger already holds a line for its suite and id, written EARLIER than
    /// this run ended - overlays this run on that line as a later attempt of the same run and replaces the
    /// line (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6.3). For the job that appends to the ledger itself
    /// under a retry extension: the retry is a new process with the same CI run id, <see cref="Append"/>
    /// calls its line a <see cref="HistoryAppendOutcome.Duplicate"/>, and the ledger keeps attempt 1's
    /// failure for a job that went green.
    ///
    /// <para>The one rewrite a test run performs, and a narrow one: only the run's OWN line changes, in
    /// place, under the writer's lock, and every other line keeps its bytes and its order - so the file is
    /// still append-only to everybody but the run amending itself. A line whose <c>at</c> is not earlier
    /// than the run's is the same attempt written twice, and stays a duplicate.</para>
    /// </summary>
    public static HistoryAppendResult Amend(string path, HistoryRoster roster, HistoryRun run, string generator, HistoryLockBudget? budget = null, HistoryShapes? shapes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(run);
        budget ??= HistoryLockBudget.Default;
        if (!File.Exists(path))
            return Append(path, roster, run, generator, budget, shapes);

        var attempts = 0;
        try
        {
            return HistoryLock.Retry(budget, () =>
            {
                attempts++;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var text = ReadAll(stream);
                var lines = text.Split('\n');
                var at = -1;
                for (var i = 0; i < lines.Length && at < 0; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;
                    var (kind, id, suite, suiteIsNull) = HistoryJson.Peek(line);
                    if (kind == HistoryLineKind.Run && string.Equals(id, run.Id, StringComparison.Ordinal) && SameSuite(suite, suiteIsNull, run.Suite))
                        at = i;
                }

                if (at < 0)
                    return AppendLocked(stream, path, roster, run, generator, attempts, shapes);

                var ledger = HistoryLedgerReader.Parse(text, window: 0).Ledger;
                var existing = ledger?.Runs(run.Suite).LastOrDefault(r => string.Equals(r.Id, run.Id, StringComparison.Ordinal));
                if (ledger is null || existing is null || ledger.Roster(existing.RosterHash) is not { } existingRoster || existing.At >= run.At)
                    return new HistoryAppendResult(HistoryAppendOutcome.Duplicate, $"run {run.Id} of suite {run.Suite ?? "(null)"} is already in the ledger", attempts);

                var combined = HistoryFold.Attempts([
                    new HistoryFragment(HistoryFormat.Version, existingRoster, existing, existing.ShapesHash is { } hash ? ledger.Shapes(hash) : null),
                    new HistoryFragment(HistoryFormat.Version, roster, run, shapes)
                ]);
                // The earlier attempt judged whether the run was partial against the ledger; the retry is a
                // handful of scenarios and would be "partial" by any measure. The run is what attempt 1 was.
                var amended = combined.Run with { Partial = existing.Partial };

                var builder = new StringBuilder();
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i == at)
                    {
                        // The roster and the calls the new line references go in front of it, when the file
                        // does not have them yet: a reader meets a run's roster before the run.
                        if (ledger.Roster(combined.Roster.Hash) is null)
                            builder.Append(HistoryJson.RosterLine(combined.Roster)).Append('\n');
                        if (combined.Shapes is { } combinedShapes && ledger.Shapes(combinedShapes.Hash) is null)
                            builder.Append(HistoryJson.ShapesLine(combinedShapes)).Append('\n');
                        builder.Append(HistoryJson.RunLine(combined.Shapes is null ? amended with { ShapesHash = null, CallSets = null } : amended)).Append('\n');
                        continue;
                    }

                    if (i == lines.Length - 1 && lines[i].Length == 0) continue;
                    builder.Append(lines[i].TrimEnd('\r')).Append('\n');
                }

                stream.SetLength(0);
                Write(stream, builder.ToString());
                return new HistoryAppendResult(HistoryAppendOutcome.Amended, $"run {run.Id} was already in the ledger from an earlier attempt; this attempt was overlaid on its line", attempts);
            });
        }
        catch (HistoryLockTimeoutException exception)
        {
            return new HistoryAppendResult(HistoryAppendOutcome.LockTimeout,
                $"the ledger at {path} stayed locked by another writer for {exception.Attempts} attempts; the run's fragment is kept and the next run folds it",
                exception.Attempts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HistoryAppendResult(HistoryAppendOutcome.Failed, $"could not amend the ledger at {path}: {exception.Message}", attempts);
        }
    }

    private static HistoryAppendResult AppendLocked(FileStream stream, string path, HistoryRoster roster, HistoryRun run, string generator, int attempts,
        HistoryShapes? shapes)
    {
        var text = ReadAll(stream);
        var builder = new StringBuilder();

        if (text.Length == 0)
        {
            builder.Append(HistoryJson.HeaderLine(generator)).Append('\n');
        }
        else
        {
            var lines = text.Split('\n');
            var header = HistoryJson.Peek(lines[0].TrimEnd('\r'));
            if (header.Kind == HistoryLineKind.Header)
            {
                int? declared = null;
                try { declared = HistoryJson.Parse(lines[0].TrimEnd('\r')).Version; } catch (FormatException) { }
                if (declared is { } version && version > HistoryFormat.Version)
                    return new HistoryAppendResult(HistoryAppendOutcome.UnsupportedVersion,
                        $"the ledger at {path} declares historyFormatVersion {version}; this build writes {HistoryFormat.Version} and will not append to it. Upgrade Kronikol, or run kronikol history compact with the newer tool.",
                        attempts);
            }

            var rosterPresent = false;
            var shapesPresent = shapes is null;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var (kind, id, suite, suiteIsNull) = HistoryJson.Peek(line);
                if (kind == HistoryLineKind.Roster && string.Equals(id, roster.Hash, StringComparison.Ordinal))
                    rosterPresent = true;
                if (kind == HistoryLineKind.Shapes && shapes is not null && string.Equals(id, shapes.Hash, StringComparison.Ordinal))
                    shapesPresent = true;
                if (kind == HistoryLineKind.Run && string.Equals(id, run.Id, StringComparison.Ordinal)
                    && SameSuite(suite, suiteIsNull, run.Suite))
                    return new HistoryAppendResult(HistoryAppendOutcome.Duplicate, $"run {run.Id} of suite {run.Suite ?? "(null)"} is already in the ledger", attempts);
            }

            // A torn tail from a killed writer: start on a fresh line rather than gluing onto the stub,
            // which the reader would then count as one damaged line instead of two.
            if (!text.EndsWith('\n'))
                builder.Append('\n');

            if (!rosterPresent)
                builder.Append(HistoryJson.RosterLine(roster)).Append('\n');
            if (!shapesPresent)
                builder.Append(HistoryJson.ShapesLine(shapes!)).Append('\n');
            builder.Append(HistoryJson.RunLine(run)).Append('\n');
            Write(stream, builder.ToString());
            return new HistoryAppendResult(HistoryAppendOutcome.Appended, null, attempts);
        }

        builder.Append(HistoryJson.RosterLine(roster)).Append('\n');
        if (shapes is not null)
            builder.Append(HistoryJson.ShapesLine(shapes)).Append('\n');
        builder.Append(HistoryJson.RunLine(run)).Append('\n');
        Write(stream, builder.ToString());
        return new HistoryAppendResult(HistoryAppendOutcome.Appended, null, attempts);
    }

    private static bool SameSuite(string? peeked, bool peekedNull, string? suite) =>
        suite is null ? peekedNull : string.Equals(peeked, suite, StringComparison.Ordinal);

    private static string ReadAll(FileStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static void Write(FileStream stream, string text)
    {
        stream.Seek(0, SeekOrigin.End);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        // To the disk, not to the OS cache: the reader on the other side of the lock reads what reached
        // disk, and a killed process loses only what it had not flushed.
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Rewrites the ledger without the runs outside <paramref name="window"/> per suite, and without the
    /// rosters those runs alone referenced. With <see cref="Compact"/>, the one wholesale rewrite the design
    /// allows (<see cref="Amend"/> replaces a single line, the amending run's own), and an explicit one: it is
    /// what keeps the scan cheap (§2.6), and it makes the repository <i>larger</i>, not smaller, because
    /// dropping the oldest line breaks git's delta chains (§5.10) — so it is a read-cost control, never a
    /// storage one.
    /// </summary>
    public static HistoryRewriteResult Prune(string path, int window, string generator, HistoryLockBudget? budget = null) =>
        Rewrite(path, window, generator, dropOldErrorText: false, budget);

    /// <summary>
    /// Rewrites the ledger to the current format version, folds every roster and drops the error text of
    /// runs outside <paramref name="window"/>. Explicit only: nothing migrates during a test run, because a
    /// silent rewrite would destroy the append-only shape git deltas cheaply and <c>merge=union</c> relies
    /// on (§3.5). The one thing a test run does change is its own line, on a retry: <see cref="Amend"/>.
    /// </summary>
    public static HistoryRewriteResult Compact(string path, int window, string generator, HistoryLockBudget? budget = null) =>
        Rewrite(path, window, generator, dropOldErrorText: true, budget);

    private static HistoryRewriteResult Rewrite(string path, int window, string generator, bool dropOldErrorText, HistoryLockBudget? budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        budget ??= HistoryLockBudget.Default;

        return HistoryLock.Retry(budget, () =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var text = ReadAll(stream);
            var all = HistoryLedgerReader.Parse(text, window: 0);
            if (all.Ledger is null)
                throw new InvalidOperationException(all.Message ?? "the ledger could not be read");

            var kept = new List<(HistoryRun Run, bool InWindow)>();
            var dropped = 0;
            foreach (var suite in all.Ledger.Suites)
            {
                var runs = all.Ledger.Runs(suite);
                var start = window > 0 && runs.Count > window ? runs.Count - window : 0;
                for (var i = 0; i < runs.Count; i++)
                {
                    if (dropOldErrorText || i >= start)
                        kept.Add((runs[i], i >= start));
                    else
                        dropped++;
                }
            }

            // Append order across suites is what the file had; the per-suite lists are already in it, so
            // the rewrite interleaves by the original position of each run.
            var order = OriginalOrder(text);
            kept.Sort((a, b) => order.GetValueOrDefault((a.Run.Suite, a.Run.Id), int.MaxValue).CompareTo(order.GetValueOrDefault((b.Run.Suite, b.Run.Id), int.MaxValue)));

            var referenced = kept.Select(k => k.Run.RosterHash).ToHashSet(StringComparer.Ordinal);
            var rosters = all.Ledger.Rosters.Where(r => referenced.Contains(r.Hash)).ToArray();
            var rostersDropped = all.Ledger.Rosters.Count - rosters.Length;
            var referencedShapes = kept.Select(k => k.Run.ShapesHash).Where(h => h is not null).ToHashSet(StringComparer.Ordinal);
            var shapesLines = all.Ledger.AllShapes.Where(s => referencedShapes.Contains(s.Hash)).OrderBy(s => s.Hash, StringComparer.Ordinal).ToArray();
            var shapesDropped = all.Ledger.AllShapes.Count - shapesLines.Length;

            var builder = new StringBuilder();
            builder.Append(HistoryJson.HeaderLine(generator)).Append('\n');
            foreach (var roster in rosters.OrderBy(r => order.GetValueOrDefault((r.Suite, "roster:" + r.Hash), int.MaxValue)))
                builder.Append(HistoryJson.RosterLine(roster)).Append('\n');
            foreach (var shapesLine in shapesLines)
                builder.Append(HistoryJson.ShapesLine(shapesLine)).Append('\n');
            foreach (var (run, inWindow) in kept)
            {
                var line = dropOldErrorText && !inWindow
                    ? run with { ErrorText = new Dictionary<string, string>(), Errors = run.Errors?.Select(_ => (string?)null).ToArray() }
                    : run;
                builder.Append(HistoryJson.RunLine(line)).Append('\n');
            }

            stream.SetLength(0);
            Write(stream, builder.ToString());
            return new HistoryRewriteResult(kept.Count, dropped, rostersDropped, HistoryFormat.Version, shapesDropped);
        });
    }

    private static Dictionary<(string?, string), int> OriginalOrder(string text)
    {
        var order = new Dictionary<(string?, string), int>();
        var position = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            position++;
            var (kind, id, suite, suiteIsNull) = HistoryJson.Peek(line);
            if (id is null) continue;
            var key = kind == HistoryLineKind.Roster ? "roster:" + id : id;
            order.TryAdd((suiteIsNull ? null : suite, key), position);
        }
        return order;
    }
}

/// <summary>
/// Folds fragments into runs: shards sharing a suite and a run id become one line, in fragment order
/// (§5.4). The caller decides the order of the fragments it hands over — by artifact path, so the fold
/// is deterministic across machines.
/// </summary>
public static class HistoryFold
{
    /// <summary>The runs the fragments describe, each with the roster it references and the shapes list its call sets index into.</summary>
    public static IReadOnlyList<HistoryFoldedRun> Fold(IEnumerable<HistoryFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        var groups = new List<(string? Suite, string Id, List<HistoryFragment> Shards)>();
        foreach (var fragment in fragments)
        {
            var group = groups.FirstOrDefault(g => string.Equals(g.Suite, fragment.Run.Suite, StringComparison.Ordinal)
                                                   && string.Equals(g.Id, fragment.Run.Id, StringComparison.Ordinal));
            if (group.Shards is null)
            {
                group = (fragment.Run.Suite, fragment.Run.Id, []);
                groups.Add(group);
            }
            group.Shards.Add(fragment);
        }

        return groups.Select(g => FoldGroup(g.Shards)).ToArray();
    }

    /// <summary>
    /// The attempts of one run as one fragment (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md F13/F15): a runner's
    /// retry extension, or a second step of one CI job, runs again in a new process under the SAME run id
    /// and in the SAME reports directory. Those are not shards - shards hold different scenarios - and
    /// folding them as shards gives the retried scenario a second slot: a phantom scenario that reads as
    /// new in this run and absent in the next. Here a scenario run again keeps its one position, takes the
    /// later attempt's result, and counts the attempts, which is what <c>attempts</c> has meant since v1
    /// and what the analyzer already reads as "passed on retry". A scenario only one attempt ran is kept
    /// as it is.
    /// </summary>
    /// <param name="oldestFirst">The attempts, oldest first - by the run's <c>at</c>, never by path.</param>
    public static HistoryFragment Attempts(IReadOnlyList<HistoryFragment> oldestFirst)
    {
        ArgumentNullException.ThrowIfNull(oldestFirst);
        if (oldestFirst.Count == 0) throw new ArgumentException("at least one attempt is needed", nameof(oldestFirst));
        if (oldestFirst.Count == 1) return oldestFirst[0];

        var positions = new Dictionary<(string Id, int Slot), int>();
        var entries = new List<HistoryRosterEntry>();
        var cells = new List<(char Result, int? Attempt, int? Duration, int? Calls, string? ShapeSet, string? ShapeOrdered, IReadOnlyList<string>? CallLines, string? Error)>();
        var deps = new SortedSet<string>(StringComparer.Ordinal);
        var anyDeps = false;

        foreach (var attempt in oldestFirst)
        {
            var roster = attempt.Roster;
            var run = attempt.Run;
            var i = 0;
            foreach (var entry in roster.Entries())
            {
                var lines = run.CallSetAt(i) is { } set && attempt.Shapes is { } shapes
                    ? set.Select(shapes.At).Where(line => line is not null).Select(line => line!).ToArray()
                    : null;
                var cell = (run.ResultAt(i), run.AttemptAt(i), run.DurationAt(i), run.CallsAt(i), run.ShapeSetAt(i), run.ShapeOrderedAt(i), (IReadOnlyList<string>?)lines, run.ErrorAt(i));
                if (positions.TryGetValue((roster.Ids[i], roster.Slots[i]), out var at))
                {
                    // Run again: the later attempt is the verdict, the count says it took more than one, and
                    // a pass on retry keeps the failure it recovered from - the evidence is the point.
                    var earlier = cells[at];
                    cells[at] = cell with
                    {
                        Item2 = (earlier.Attempt ?? 1) + (cell.Item2 ?? 1),
                        Item8 = cell.Item8 ?? earlier.Error
                    };
                }
                else
                {
                    positions[(roster.Ids[i], roster.Slots[i])] = entries.Count;
                    entries.Add(entry);
                    cells.Add(cell);
                }
                i++;
            }
            if (run.Deps is { } attemptDeps)
            {
                anyDeps = true;
                foreach (var dep in attemptDeps) deps.Add(dep);
            }
        }

        var first = oldestFirst[0].Run;
        var last = oldestFirst[^1].Run;
        var combined = HistoryRoster.Create(first.Suite, entries);
        var anyCallSets = cells.Any(c => c.CallLines is not null);
        var combinedShapes = anyCallSets ? HistoryShapes.Create(cells.SelectMany(c => c.CallLines ?? [])) : null;
        var index = combinedShapes?.Calls.Select((line, n) => (line, n)).ToDictionary(p => p.line, p => p.n, StringComparer.Ordinal);
        var errorKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
        string? KeyOf(string? text)
        {
            if (text is null) return null;
            if (!errorKeys.TryGetValue(text, out var key))
            {
                key = "e" + (errorKeys.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                errorKeys[text] = key;
                errorText[key] = text;
            }
            return key;
        }

        var line = new HistoryRun
        {
            Id = first.Id,
            Suite = first.Suite,
            Partial = first.Partial,
            At = oldestFirst.Max(a => a.Run.At),
            Branch = first.Branch,
            Commit = first.Commit,
            Provider = first.Provider,
            Url = first.Url ?? last.Url,
            Shards = Math.Max(1, first.Shards),
            RosterHash = combined.Hash,
            Results = new string(cells.Select(c => c.Result).ToArray()),
            Attempts = new string(cells.Select(c => HistoryFormat.AttemptChar(c.Attempt)).ToArray()),
            Durations = oldestFirst.Any(a => a.Run.Durations is not null) ? cells.Select(c => c.Duration).ToArray() : null,
            Calls = oldestFirst.Any(a => a.Run.Calls is not null) ? cells.Select(c => c.Calls ?? 0).ToArray() : null,
            ShapeSet = oldestFirst.Any(a => a.Run.ShapeSet is not null) ? cells.Select(c => c.ShapeSet ?? "").ToArray() : null,
            ShapeOrdered = oldestFirst.Any(a => a.Run.ShapeSet is not null) ? cells.Select(c => c.ShapeOrdered ?? "").ToArray() : null,
            ShapeVersion = oldestFirst.Select(a => a.Run.ShapeVersion).FirstOrDefault(v => v is not null),
            ShapesHash = combinedShapes?.Hash,
            CallSets = index is null ? null : cells.Select(c => (IReadOnlyList<int>)(c.CallLines ?? []).Select(l => index[l]).OrderBy(n => n).ToArray()).ToArray(),
            Errors = oldestFirst.Any(a => a.Run.Errors is not null) ? cells.Select(c => KeyOf(c.Error)).ToArray() : null,
            ErrorText = errorText,
            Deps = anyDeps ? deps.ToArray() : null
        };
        return new HistoryFragment(oldestFirst.Max(a => a.Version), combined, line, combinedShapes);
    }

    private static HistoryFoldedRun FoldGroup(List<HistoryFragment> shards)
    {
        if (shards.Count == 1)
        {
            var only = shards[0];
            return new HistoryFoldedRun(only.Roster, only.Run with { Shards = Math.Max(1, only.Run.Shards) }, only.Shapes);
        }

        var entries = new List<HistoryRosterEntry>();
        var results = new StringBuilder();
        var attempts = new StringBuilder();
        var durations = new List<int?>();
        var calls = new List<int>();
        var shapeSet = new List<string>();
        var shapeOrdered = new List<string>();
        // Each shard's call sets index its own shapes list; the folded run gets one list over all of
        // them, so the calls are resolved to their lines here and re-indexed once the list is known.
        var callLines = new List<IReadOnlyList<string>>();
        var anyCallSets = false;
        var errors = new List<string?>();
        var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
        var errorKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var deps = new SortedSet<string>(StringComparer.Ordinal);
        var anyDurations = false;
        var anyCalls = false;
        var anyShapes = false;
        var anyErrors = false;
        bool? partial = null;

        foreach (var shard in shards)
        {
            var roster = shard.Roster;
            var run = shard.Run;
            entries.AddRange(roster.Entries());
            results.Append(run.Results.PadRight(roster.Count, HistoryFormat.Absent)[..roster.Count]);
            attempts.Append((run.Attempts.Length == 0 ? new string(HistoryFormat.AttemptUnknown, roster.Count) : run.Attempts).PadRight(roster.Count, HistoryFormat.AttemptUnknown)[..roster.Count]);

            for (var i = 0; i < roster.Count; i++)
            {
                durations.Add(run.DurationAt(i));
                calls.Add(run.CallsAt(i) ?? 0);
                shapeSet.Add(run.ShapeSetAt(i) ?? "");
                shapeOrdered.Add(run.ShapeOrderedAt(i) ?? "");
                callLines.Add(run.CallSetAt(i) is { } set && shard.Shapes is { } shardShapes
                    ? set.Select(shardShapes.At).Where(line => line is not null).Select(line => line!).ToArray()
                    : []);

                var text = run.ErrorAt(i);
                if (text is null)
                {
                    errors.Add(null);
                }
                else
                {
                    if (!errorKeys.TryGetValue(text, out var key))
                    {
                        key = "e" + (errorKeys.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        errorKeys[text] = key;
                        errorText[key] = text;
                    }
                    errors.Add(key);
                }
            }

            anyDurations |= run.Durations is not null;
            anyCalls |= run.Calls is not null;
            anyShapes |= run.ShapeSet is not null;
            anyCallSets |= run.CallSets is not null;
            anyErrors |= run.Errors is not null;
            if (run.Deps is { } shardDeps)
                foreach (var dep in shardDeps) deps.Add(dep);
            // An explicit partial on any shard makes the whole run partial; undecided shards stay so and
            // the append resolves the run against the ledger.
            if (run.Partial == true) partial = true;
        }

        // A fingerprint is comparable only with one the same rule made, and the rule is the pair of the
        // built-in version and the consumer's rules. Shards that disagree (one job configured differently,
        // one on an older package) cannot fold into a line that claims one rule: the fingerprints are
        // blanked, which costs the run its behaviour verdicts and nothing else, and the fold says so.
        string? note = null;
        var shapedShards = shards.Where(s => s.Run.ShapeSet is not null).ToList();
        if (shapedShards.Select(s => (s.Run.ShapeVersion ?? 1, s.Run.ShapeRules)).Distinct().Count() > 1)
        {
            note = $"the shards of {shards[0].Run.Id} were fingerprinted under different templating rules ({string.Join(", ", shapedShards.Select(s => $"v{s.Run.ShapeVersion ?? 1}{(s.Run.ShapeRules is { } h ? "+" + h : "")}").Distinct())}); their fingerprints are not comparable, so none are recorded for this run and it reads no behaviour verdict. Give every shard the same HistoryShapeTemplates and the same Kronikol version.";
            anyShapes = false;
            anyCalls = false;
            anyCallSets = false;
        }

        var first = shards[0].Run;
        var folded = HistoryRoster.Create(first.Suite, entries);
        var foldedShapes = anyCallSets ? HistoryShapes.Create(callLines.SelectMany(lines => lines)) : null;
        var index = foldedShapes is null ? null : foldedShapes.Calls.Select((line, i) => (line, i)).ToDictionary(p => p.line, p => p.i, StringComparer.Ordinal);
        var callSets = index is null ? null
            : callLines.Select(lines => (IReadOnlyList<int>)lines.Select(line => index[line]).OrderBy(i => i).ToArray()).ToArray();
        var run2 = new HistoryRun
        {
            Id = first.Id,
            Suite = first.Suite,
            Partial = partial,
            At = shards.Max(s => s.Run.At),
            Branch = first.Branch,
            Commit = first.Commit,
            Provider = first.Provider,
            Url = first.Url,
            Shards = shards.Count,
            RosterHash = folded.Hash,
            Results = results.ToString(),
            Attempts = attempts.ToString(),
            Durations = anyDurations ? durations : null,
            Calls = anyCalls ? calls : null,
            ShapeSet = anyShapes ? shapeSet : null,
            ShapeOrdered = anyShapes ? shapeOrdered : null,
            ShapeVersion = anyShapes ? shards.Select(s => s.Run.ShapeVersion).FirstOrDefault(v => v is not null) : null,
            ShapeRules = anyShapes ? shapedShards.Select(s => s.Run.ShapeRules).FirstOrDefault() : null,
            ShapesHash = foldedShapes?.Hash,
            CallSets = callSets,
            Errors = anyErrors ? errors : null,
            ErrorText = errorText,
            Deps = deps.Count > 0 || shards.Any(s => s.Run.Deps is not null) ? deps.ToArray() : null
        };
        return new HistoryFoldedRun(folded, run2, foldedShapes) { Note = note };
    }
}

/// <summary>One folded run: its roster, its line, and the shapes list the line's call sets index into (null when no shard recorded one).</summary>
public sealed record HistoryFoldedRun(HistoryRoster Roster, HistoryRun Run, HistoryShapes? Shapes)
{
    /// <summary>Something the fold had to give up, in words; null when it gave up nothing. Shards fingerprinted under different templating rules lose their fingerprints (3.25.0).</summary>
    public string? Note { get; init; }

    /// <summary>The pair a caller that has no use for the shapes takes.</summary>
    public void Deconstruct(out HistoryRoster roster, out HistoryRun run)
    {
        roster = Roster;
        run = Run;
    }
}
