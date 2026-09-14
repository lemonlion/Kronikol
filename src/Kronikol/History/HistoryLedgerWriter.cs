using System.Text;

namespace Kronikol.History;

/// <summary>How an append ended.</summary>
public enum HistoryAppendOutcome
{
    /// <summary>The run line (and its roster, if new) was written and flushed.</summary>
    Appended,

    /// <summary>A run with the same suite and id is already in the file; nothing was written (§6.5).</summary>
    Duplicate,

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
public sealed record HistoryRewriteResult(int RunsKept, int RunsDropped, int RostersDropped, int Version);

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
    public static HistoryAppendResult Append(string path, HistoryRoster roster, HistoryRun run, string generator, HistoryLockBudget? budget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(run);
        budget ??= HistoryLockBudget.Default;

        if (!string.Equals(roster.Hash, run.RosterHash, StringComparison.Ordinal))
            throw new ArgumentException("The run does not reference the roster it was given.", nameof(run));

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
                return AppendLocked(stream, path, roster, run, generator, attempts);
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

    private static HistoryAppendResult AppendLocked(FileStream stream, string path, HistoryRoster roster, HistoryRun run, string generator, int attempts)
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
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var (kind, id, suite, suiteIsNull) = HistoryJson.Peek(line);
                if (kind == HistoryLineKind.Roster && string.Equals(id, roster.Hash, StringComparison.Ordinal))
                    rosterPresent = true;
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
            builder.Append(HistoryJson.RunLine(run)).Append('\n');
            Write(stream, builder.ToString());
            return new HistoryAppendResult(HistoryAppendOutcome.Appended, null, attempts);
        }

        builder.Append(HistoryJson.RosterLine(roster)).Append('\n');
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
    /// rosters those runs alone referenced. The one rewrite the design allows, and an explicit one: it is
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
    /// on (§3.5).
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

            var builder = new StringBuilder();
            builder.Append(HistoryJson.HeaderLine(generator)).Append('\n');
            foreach (var roster in rosters.OrderBy(r => order.GetValueOrDefault((r.Suite, "roster:" + r.Hash), int.MaxValue)))
                builder.Append(HistoryJson.RosterLine(roster)).Append('\n');
            foreach (var (run, inWindow) in kept)
            {
                var line = dropOldErrorText && !inWindow
                    ? run with { ErrorText = new Dictionary<string, string>(), Errors = run.Errors?.Select(_ => (string?)null).ToArray() }
                    : run;
                builder.Append(HistoryJson.RunLine(line)).Append('\n');
            }

            stream.SetLength(0);
            Write(stream, builder.ToString());
            return new HistoryRewriteResult(kept.Count, dropped, rostersDropped, HistoryFormat.Version);
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
    /// <summary>The runs the fragments describe, each with the roster it references.</summary>
    public static IReadOnlyList<(HistoryRoster Roster, HistoryRun Run)> Fold(IEnumerable<HistoryFragment> fragments)
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

    private static (HistoryRoster, HistoryRun) FoldGroup(List<HistoryFragment> shards)
    {
        if (shards.Count == 1)
        {
            var only = shards[0];
            return (only.Roster, only.Run with { Shards = Math.Max(1, only.Run.Shards) });
        }

        var entries = new List<HistoryRosterEntry>();
        var results = new StringBuilder();
        var attempts = new StringBuilder();
        var durations = new List<int?>();
        var calls = new List<int>();
        var shapeSet = new List<string>();
        var shapeOrdered = new List<string>();
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
            anyErrors |= run.Errors is not null;
            if (run.Deps is { } shardDeps)
                foreach (var dep in shardDeps) deps.Add(dep);
            // An explicit partial on any shard makes the whole run partial; undecided shards stay so and
            // the append resolves the run against the ledger.
            if (run.Partial == true) partial = true;
        }

        var first = shards[0].Run;
        var folded = HistoryRoster.Create(first.Suite, entries);
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
            Errors = anyErrors ? errors : null,
            ErrorText = errorText,
            Deps = deps.Count > 0 || shards.Any(s => s.Run.Deps is not null) ? deps.ToArray() : null
        };
        return (folded, run2);
    }
}
