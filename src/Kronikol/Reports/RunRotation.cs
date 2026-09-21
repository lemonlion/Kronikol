using System.Globalization;
using System.Text.Json;
using Kronikol.History;

namespace Kronikol.Reports;

/// <summary>
/// The last N runs are kept: before a run writes, the run before it is moved to
/// <c>&lt;reports&gt;/runs/&lt;run&gt;/</c> (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6, issue #80).
///
/// <para><b>Rotate the old run, never redirect the new one.</b> The top level of the reports directory
/// keeps holding the newest run byte for byte, because that is what every consumer's CI globs. A move
/// inside one volume is a rename — constant time for an 80&#160;MB report, no second copy — and it never
/// touches how the new run is written, which two writers resolve for themselves anyway.</para>
///
/// <para><b>What moves is what the previous run said it wrote</b>: the <see cref="RunManifest.Files"/> and
/// <see cref="RunManifest.Attachments"/> of the <c>Run.json</c> on top, and that file itself, last. Never
/// <c>CLAUDE.md</c> or <c>AGENTS.md</c>, never anything outside the directory, never another report's
/// files — and nothing at all unless that manifest is the previous run of <em>this</em> report.</para>
///
/// <para><b>Staged, then published.</b> Files go to <c>runs/.incoming-&lt;name&gt;/</c>, the report's data
/// file first because it is the one a reader is most likely to hold; if that first move fails nothing has
/// moved and the rotation is abandoned whole. A later failure moves what was staged back. One directory
/// rename then publishes the run, so a retained run is whole or absent.</para>
///
/// <para><b>It never fails the run</b> (the rule history runs under). Anything that goes wrong is a
/// <see cref="DiagnosticKind.ReportRotationFailed"/> naming the file, and the run overwrites the previous
/// one exactly as it did before any of this existed.</para>
/// </summary>
internal static class RunRotation
{
    /// <summary>How many runs to keep, when <see cref="ReportConfigurationOptions.KeepRuns"/> does not say: a number, or <c>off</c>.</summary>
    internal const string EnvironmentVariable = "KRONIKOL_KEEP_RUNS";

    /// <summary>The value of <see cref="EnvironmentVariable"/> that keeps nothing.</summary>
    internal const string EnvironmentOff = "off";

    /// <summary>What is kept off CI when nothing says otherwise. On CI it is 0.</summary>
    internal const int DefaultKeepRuns = 3;

    /// <summary>
    /// How far apart the report and a companion file beside it may have been written and still be one
    /// run's, where no manifest says so. One run writes them in the same parallel batch; ten minutes is
    /// for a report large enough to take minutes to serialise.
    /// </summary>
    private static readonly TimeSpan SameRunTolerance = TimeSpan.FromMinutes(10);

    private static readonly object Gate = new();

    /// <summary>
    /// The directories this process has already written a run into. At most one rotation per directory
    /// per process: LightBDD's formatter and <c>kronikol ingest</c> both reach the generator, and a second
    /// call in one process is the same run writing again — rotating then would move the run's own output
    /// out from under it. "Same id as the current run" cannot be the test: on CI a genuine re-run (a retry
    /// extension, a second workflow step) has the same id. A new process is a new run, whatever its id.
    /// </summary>
    private static readonly HashSet<string> Claimed =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <param name="ReportsDirectory">The directory this run is about to write.</param>
    /// <param name="RunId">This run's id — what its own <c>Run.json</c> will say.</param>
    /// <param name="OnCi">Whether the run is on a CI provider Kronikol detects.</param>
    /// <param name="KeepRuns">The resolved number (<see cref="ResolveKeepRuns"/>).</param>
    /// <param name="ReportName">This run's <see cref="ReportConfigurationOptions.HtmlTestRunReportFileName"/>.</param>
    /// <param name="DataExtension">The extension of this run's data file, without the dot.</param>
    /// <param name="PlannedFiles">The top-level files this run is about to write — what is rotated from a directory that has no manifest.</param>
    internal sealed record Request(string ReportsDirectory, string RunId, bool OnCi, int KeepRuns, string ReportName, string DataExtension, IReadOnlyList<string> PlannedFiles);

    /// <param name="KeptDirectory">Where the previous run went, when one was rotated by this call.</param>
    /// <param name="Kept">That run's manifest; null when the directory had none and nothing in it said how the run went.</param>
    /// <param name="FailedAttemptOfThisRun">The newest retained run with this run's id that failed: an earlier attempt of the same run.</param>
    internal sealed record Outcome(string? KeptDirectory, RunManifest? Kept, RetainedRun? FailedAttemptOfThisRun)
    {
        internal static readonly Outcome Nothing = new(null, null, null);
    }

    /// <summary>
    /// <see cref="ReportConfigurationOptions.KeepRuns"/>, then <c>KRONIKOL_KEEP_RUNS</c>, then 3 off CI and
    /// 0 on it. A value that is not a non-negative number (or <c>off</c>) is not a value: it falls through
    /// to the next source rather than switching retention off, or on, by accident.
    /// </summary>
    internal static int ResolveKeepRuns(int? option, Func<string, string?> getEnv, bool onCi)
    {
        ArgumentNullException.ThrowIfNull(getEnv);

        if (option is >= 0)
            return option.Value;

        var variable = getEnv(EnvironmentVariable)?.Trim();
        if (string.Equals(variable, EnvironmentOff, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (int.TryParse(variable, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number;

        return onCi ? 0 : DefaultKeepRuns;
    }

    /// <summary>Makes this process forget it has written into <paramref name="reportsDirectory"/>, so a test's next run there stands for a new process. That directory only: the suite's other classes are writing theirs in parallel.</summary>
    internal static void ForgetForTests(string reportsDirectory)
    {
        lock (Gate)
            Claimed.Remove(Normalise(reportsDirectory));
    }

    /// <summary>
    /// Everything that happens to a reports directory before a run writes into it. Never throws.
    /// </summary>
    internal static Outcome Prepare(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = Outcome.Nothing;
        try
        {
            request = request with { ReportsDirectory = Normalise(request.ReportsDirectory) };

            // One lock for the claim AND the moves, so that a second generation arriving in this process
            // while the first is still rotating waits for it, rather than writing files into a directory
            // that is being emptied. Only a test suite generates into one directory from two threads;
            // Kronikol's own does, by the hundred.
            lock (Gate)
            {
                // On CI at 0 the earlier attempts of this same run are still kept (F13): retention is
                // off only when it is 0 off CI.
                if (Claimed.Add(request.ReportsDirectory) && (request.KeepRuns > 0 || request.OnCi))
                {
                    var runs = Path.Combine(request.ReportsDirectory, ReportFolders.RunsFolderName);
                    PublishWhatWasStagedWhole(runs);
                    outcome = Rotate(request, runs);
                    Prune(request, runs);
                }
            }

            outcome = outcome with { FailedAttemptOfThisRun = FailedAttemptOf(request) };
        }
        catch (Exception exception)
        {
            Failed($"the previous run in {request.ReportsDirectory} was not rotated", exception);
        }

        RemoveThePreviousManifest(request.ReportsDirectory);
        return outcome;
    }

    // ─── Resume: what a killed process left (§11.4 row 4) ──────

    /// <summary>
    /// A <c>.incoming-*</c> that holds a <c>Run.json</c> is complete — the manifest is the last thing
    /// staged — and was killed (or blocked) between the last move and the rename. Publish it.
    /// </summary>
    private static void PublishWhatWasStagedWhole(string runs)
    {
        if (!Directory.Exists(runs))
            return;

        foreach (var staged in Directory.GetDirectories(runs).Where(ReportFolders.IsIncoming))
        {
            if (RunManifest.TryRead(Path.Combine(staged, RunManifest.FileName)) is null)
                continue;

            try
            {
                Directory.Move(staged, Path.Combine(runs, FreeName(runs, Path.GetFileName(staged)[ReportFolders.IncomingPrefix.Length..])));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Failed($"{Relative(runs, staged)} holds a whole run and could not be published", exception);
            }
        }
    }

    // ─── Rotation ──────────────────────────────────────────────

    private static Outcome Rotate(Request request, string runs)
    {
        var directory = request.ReportsDirectory;
        if (!Directory.Exists(directory))
            return Outcome.Nothing;

        var manifestPath = Path.Combine(directory, RunManifest.FileName);
        var previous = RunManifest.TryRead(manifestPath);

        string previousRunId;
        List<string> files;
        List<string> attachments;
        RunManifest? written;
        if (previous is not null)
        {
            files = previous.Files.Where(IsTopLevelName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            attachments = previous.Attachments.Where(IsAttachmentName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            previousRunId = previous.Run;
            written = previous;
        }
        else
        {
            // A directory written before manifests existed, or by a run killed before its own. Nothing says
            // which files were that run's, so: the ones this run is about to overwrite. attachments/ is
            // left alone — nothing names the ones that run owned.
            files = request.PlannedFiles.Where(IsTopLevelName).Where(name => File.Exists(Path.Combine(directory, name)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            attachments = [];
            previousRunId = "";
            written = null;
        }

        // Only the previous run of THIS report. HtmlTestRunReportFileName lets a host render Checkout.*
        // and Payments.* into one folder from two processes, and Run.json — like Failures.md — has one
        // name, so the manifest on top is the last writer's. When that is the OTHER report, it is that
        // report's CURRENT run: rotating it would take it out of the top level, where its reader and its
        // CI glob expect it, on the word of a process that was never going to overwrite it.
        if (!files.Any(name => IsThisReport(name, request)))
            return Outcome.Nothing;

        if (previous is null)
            (previousRunId, written) = Reconstruct(directory, files, request);

        // On CI at KeepRuns = 0 only an earlier attempt of this same run is worth keeping; any other run
        // is overwritten as it always was.
        if (request.KeepRuns == 0 && !string.Equals(previousRunId, request.RunId, StringComparison.Ordinal))
            return Outcome.Nothing;

        // The data file first: the largest, and the one `kronikol query` holds open. If it will not move,
        // nothing has moved.
        files = files
            .OrderBy(name => string.Equals(name, $"{request.ReportName}.{request.DataExtension}", StringComparison.OrdinalIgnoreCase) ? 0 : IsThisReport(name, request) ? 1 : 2)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToList();

        var name = HistoryRunId.DirectoryName(previousRunId);
        var staging = StagingFor(runs, name, directory, files, attachments);
        var createdRuns = !Directory.Exists(runs);
        var createdStaging = !Directory.Exists(staging);
        var moved = new List<(string From, string To)>();
        var copied = new List<string>();
        var current = Relative(directory, staging);
        try
        {
            Directory.CreateDirectory(staging);

            foreach (var file in files)
            {
                current = file;
                Move(Path.Combine(directory, file), Path.Combine(staging, file), moved);
            }

            // An attachment another report in this folder may link to is copied, not taken: a report on
            // top that this manifest does not list is somebody else's, and attachments/ is shared.
            var shared = attachments.Count > 0 && AnotherReportIsPresent(directory, files);
            foreach (var attachment in attachments)
            {
                current = attachment;
                var from = Path.Combine(directory, attachment.Replace('/', Path.DirectorySeparatorChar));
                var to = Path.Combine(staging, attachment.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(from))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (shared)
                {
                    File.Copy(from, to, overwrite: false);
                    copied.Add(to);
                }
                else
                {
                    Move(from, to, moved);
                }
            }

            // The manifest last, so that a staging directory holding one is a whole run.
            current = RunManifest.FileName;
            if (previous is not null)
                Move(manifestPath, Path.Combine(staging, RunManifest.FileName), moved);
            else
                written?.Write(staging);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A manifest this rotation wrote itself and did not finish must not stay: a staging directory
            // that holds a Run.json is, by the rule above, a whole run waiting to be published.
            if (previous is null)
                copied.Add(Path.Combine(staging, RunManifest.FileName));

            var stuck = PutBack(moved, copied);
            RemoveIfEmpty(Path.Combine(staging, RunFileCollector.AttachmentsFolderName));
            if (createdStaging)
                RemoveIfEmpty(staging);
            if (createdRuns)
                RemoveIfEmpty(runs);

            Failed($"the previous run was not moved to {Relative(directory, Path.Combine(runs, name))}: it stopped at {current}; this run overwrites the previous one as before"
                   + (stuck.Count == 0 ? "" : $". {stuck.Count} file(s) could not be moved back and are in {Relative(directory, staging)}: {string.Join(", ", stuck)}"), exception);
            return Outcome.Nothing;
        }

        var published = Path.Combine(runs, FreeName(runs, name));
        try
        {
            Directory.Move(staging, published);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Whole under its staging name. With its manifest inside, the next rotation publishes it; a
            // run that had none to give stays there for `kronikol history doctor`.
            Failed($"the previous run is whole in {Relative(directory, staging)} and could not be published as {Path.GetFileName(published)}", exception);
            return Outcome.Nothing;
        }

        return new Outcome(published, written, null);
    }

    /// <summary>
    /// A file that will not move is an exception; so, deliberately, is one marked read-only. NTFS and
    /// every POSIX file system rename a read-only file without complaint (RUN), so without this a
    /// read-only report would be rotated away and the run would then succeed where today it cannot
    /// replace the file — a change to what happens at the top level, made on the word of nobody. The
    /// owner said "do not touch"; the rotation stops, says which file, and the run does what it always did.
    /// </summary>
    private static void Move(string from, string to, List<(string From, string To)> moved)
    {
        if (!File.Exists(from))
            return;
        if ((File.GetAttributes(from) & FileAttributes.ReadOnly) != 0)
            throw new IOException($"{Path.GetFileName(from)} is read-only");

        File.Move(from, to);
        moved.Add((from, to));
    }

    /// <summary>Moves what was staged back, newest first, and removes what was copied. Returns the names that would not go back.</summary>
    private static List<string> PutBack(List<(string From, string To)> moved, List<string> copied)
    {
        var stuck = new List<string>();
        for (var i = moved.Count - 1; i >= 0; i--)
        {
            try
            {
                File.Move(moved[i].To, moved[i].From);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stuck.Add(Path.GetFileName(moved[i].From));
            }
        }

        foreach (var copy in copied)
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A copy: the original never left. Whatever holds it, the staging directory says so.
            }
        }

        return stuck;
    }

    /// <summary>
    /// <c>.incoming-&lt;name&gt;</c> — and when one is already there, whether it is this rotation's own,
    /// interrupted: no manifest inside (that would have been published above) and none of the files still
    /// waiting on top. Then the moves carry on into it. A staging directory that holds one of those names
    /// is somebody else's, is left for <c>kronikol history doctor</c>, and this rotation stages beside it.
    /// </summary>
    private static string StagingFor(string runs, string name, string directory, List<string> files, List<string> attachments)
    {
        for (var attempt = 1; ; attempt++)
        {
            var staging = Path.Combine(runs, ReportFolders.IncomingPrefix + (attempt == 1 ? name : $"{name}-{attempt.ToString(CultureInfo.InvariantCulture)}"));
            if (!Directory.Exists(staging) && !File.Exists(staging))
                return staging;
            if (!Directory.Exists(staging) || File.Exists(Path.Combine(staging, RunManifest.FileName)))
                continue;

            var collides = files.Concat(attachments.Select(a => a.Replace('/', Path.DirectorySeparatorChar)))
                .Any(file => File.Exists(Path.Combine(directory, file)) && File.Exists(Path.Combine(staging, file)));
            if (!collides)
                return staging;
        }
    }

    /// <summary><c>name</c>, then <c>name-2</c>, <c>name-3</c>… while the name is taken: on CI two steps of one workflow run have one id (F9).</summary>
    private static string FreeName(string runs, string name)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = attempt == 1 ? name : $"{name}-{attempt.ToString(CultureInfo.InvariantCulture)}";
            var path = Path.Combine(runs, candidate);
            if (!Directory.Exists(path) && !File.Exists(path))
                return candidate;
        }
    }

    private static bool IsThisReport(string name, Request request) =>
        string.Equals(name, $"{request.ReportName}.{request.DataExtension}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, $"{request.ReportName}.html", StringComparison.OrdinalIgnoreCase);

    /// <summary>Any HTML on top that the rotated manifest does not list: another report, which may link into <c>attachments/</c>.</summary>
    private static bool AnotherReportIsPresent(string directory, List<string> files) =>
        Directory.EnumerateFiles(directory, "*.html").Any(path => !files.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// A manifest is a file on disk, and a file on disk can say anything. A <see cref="RunManifest.Files"/>
    /// entry is a bare file name — never a path, never the agent files, never the manifest itself (which
    /// moves last, by its own rule).
    /// </summary>
    private static bool IsTopLevelName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && name is not ("." or "..")
        && !string.Equals(name, RunManifest.FileName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(name, AgentInstructionsGenerator.ClaudeFileName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(name, AgentInstructionsGenerator.AgentsFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>An <see cref="RunManifest.Attachments"/> entry is <c>attachments/&lt;bare file name&gt;</c> and nothing else.</summary>
    private static bool IsAttachmentName(string name)
    {
        var prefix = RunFileCollector.AttachmentsFolderName + "/";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && name.Length > prefix.Length
               && name.IndexOfAny(['/', '\\', ':'], prefix.Length) < 0
               && name[prefix.Length..] is not ("." or "..");
    }

    // ─── A directory with no manifest ──────────────────────────

    /// <summary>
    /// What a directory with no <c>Run.json</c> still says about the run in it: its id and time from
    /// <c>History.run.json</c>, how it went from <c>Failures.jsonl</c>'s header line (a green run writes
    /// one too) or from the fragment's results. With a count, the rotated run gets a manifest and is a
    /// retained run like any other — counted, prunable, its failure pinned. Without one it gets none:
    /// the rotation will not invent a <c>failed</c> the pin would then trust, so the run is kept, never
    /// counted, never pruned, and <c>kronikol history doctor</c> names it. That is the run killed before
    /// its digest was written.
    /// </summary>
    private static (string RunId, RunManifest? Manifest) Reconstruct(string directory, List<string> files, Request request)
    {
        // The report's own timestamp: the data file, else the HTML, else whatever there is.
        var report = files.FirstOrDefault(name => string.Equals(name, $"{request.ReportName}.{request.DataExtension}", StringComparison.OrdinalIgnoreCase))
                     ?? files.FirstOrDefault(name => IsThisReport(name, request))
                     ?? files[0];
        var stamp = new DateTimeOffset(File.GetLastWriteTimeUtc(Path.Combine(directory, report)));

        string? runId = null;
        DateTimeOffset? at = null;
        string? suite = null;
        string? version = null;
        int? scenarios = null;
        int? failed = null;
        bool? partial = null;

        // A companion file is believed only if it was written with the report: a fragment left by a run
        // from before history was switched off would otherwise name this run after that one.
        var fragmentPath = Path.Combine(directory, HistoryFormat.FragmentFileName);
        if (WrittenWith(fragmentPath, stamp))
        {
            try
            {
                var fragment = HistoryFragment.Parse(File.ReadAllText(fragmentPath));
                runId = fragment.Run.Id;
                at = fragment.Run.At;
                suite = fragment.Run.Suite;
                partial = fragment.Run.Partial;
                scenarios = fragment.Run.Results.Length;
                failed = fragment.Run.Results.Count(result => result == HistoryFormat.Failed);
                if (!files.Contains(HistoryFormat.FragmentFileName, StringComparer.OrdinalIgnoreCase))
                    files.Add(HistoryFormat.FragmentFileName);
            }
            catch (Exception exception) when (exception is FormatException or JsonException or IOException or UnauthorizedAccessException)
            {
                // Not a fragment, or not readable: the timestamp names the run.
            }
        }

        var digestPath = Path.Combine(directory, ReportGenerator.FailuresDigestJsonlFileName);
        if (files.Contains(ReportGenerator.FailuresDigestJsonlFileName, StringComparer.OrdinalIgnoreCase) && WrittenWith(digestPath, stamp))
        {
            try
            {
                using var reader = new StreamReader(new FileStream(digestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                using var header = JsonDocument.Parse(reader.ReadLine() ?? "");
                if (header.RootElement.ValueKind == JsonValueKind.Object
                    && header.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "header"
                    && header.RootElement.TryGetProperty("scenarios", out var count) && count.TryGetInt32(out var scenarioCount)
                    && header.RootElement.TryGetProperty("failures", out var failures) && failures.TryGetInt32(out var failureCount))
                {
                    scenarios = scenarioCount;
                    failed = failureCount;
                    suite ??= header.RootElement.TryGetProperty("suite", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    version = header.RootElement.TryGetProperty("kronikolVersion", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A digest from before the header line existed, or a torn one: no count from here.
            }
        }

        // Off CI by construction — null metadata — so the name says when, whatever this run is on.
        runId ??= HistoryRunBuilder.RunId(null, stamp);
        if (scenarios is null || failed is null)
            return (runId, null);

        return (runId, new RunManifest
        {
            Run = runId,
            At = at ?? stamp,
            Suite = suite,
            Scenarios = scenarios.Value,
            Failed = failed.Value,
            Partial = partial,
            KronikolVersion = version,
            Files = files.Order(StringComparer.Ordinal).ToArray(),
            Attachments = []
        });
    }

    private static bool WrittenWith(string path, DateTimeOffset stamp) =>
        File.Exists(path) && (new DateTimeOffset(File.GetLastWriteTimeUtc(path)) - stamp).Duration() <= SameRunTolerance;

    // ─── Pruning ───────────────────────────────────────────────

    /// <summary>
    /// Keeps the newest <see cref="Request.KeepRuns"/> retained runs — and, whatever the number, the
    /// newest one that failed, so the last failure survives any number of green re-runs (at most
    /// <c>KeepRuns + 1</c> directories). Reads each run's <c>Run.json</c> and nothing else. A directory
    /// with no readable manifest, and a staging directory, are never counted and never removed.
    ///
    /// <para>On CI the earlier attempts of this same run are kept as well, at 0 too. At 0 there is no
    /// failure pin: a persistent runner would otherwise carry, and publish, the last failing attempt of
    /// some other workflow run for ever.</para>
    /// </summary>
    private static void Prune(Request request, string runs)
    {
        var retained = ReportFolders.RetainedRuns(request.ReportsDirectory);
        if (retained.Count == 0)
            return;

        var keep = new HashSet<string>(retained.Take(request.KeepRuns).Select(run => run.Directory), StringComparer.Ordinal);
        if (request.KeepRuns > 0 && retained.FirstOrDefault(run => run.Manifest.Failed > 0) is { } lastFailure)
            keep.Add(lastFailure.Directory);
        if (request.OnCi)
            keep.UnionWith(retained.Where(run => string.Equals(run.Manifest.Run, request.RunId, StringComparison.Ordinal)).Select(run => run.Directory));

        foreach (var run in retained.Where(run => !keep.Contains(run.Directory)))
        {
            try
            {
                Directory.Delete(run.Directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Failed($"the retained run {Relative(request.ReportsDirectory, run.Directory)} is past KeepRuns = {request.KeepRuns.ToString(CultureInfo.InvariantCulture)} and could not be removed", exception);
            }
        }

        RemoveIfEmpty(runs);
    }

    private static RetainedRun? FailedAttemptOf(Request request) =>
        ReportFolders.RetainedRuns(request.ReportsDirectory)
            .FirstOrDefault(run => run.Manifest.Failed > 0 && string.Equals(run.Manifest.Run, request.RunId, StringComparison.Ordinal));

    // ─── The manifest on top ───────────────────────────────────

    /// <summary>
    /// A <c>Run.json</c> still on top when a run starts writing — rotation off, abandoned, another
    /// report's, a second call in this process — describes files that are about to stop being true. It
    /// goes before the first write, so that "no <c>Run.json</c>" means what it says for a run that dies
    /// half way: this directory is not a finished run.
    /// </summary>
    private static void RemoveThePreviousManifest(string reportsDirectory)
    {
        try
        {
            // Asked first: File.Delete is silent about a file that is not there and throws about a
            // DIRECTORY that is not there — and a first run's reports directory does not exist yet.
            var manifest = Path.Combine(reportsDirectory, RunManifest.FileName);
            if (File.Exists(manifest))
                File.Delete(manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Failed($"{RunManifest.FileName} in {reportsDirectory} is the previous run's and could not be removed before this run wrote", exception);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────

    private static void Failed(string what, Exception exception) =>
        ReportDiagnosticsScope.Record(DiagnosticKind.ReportRotationFailed, what, exception);

    private static void RemoveIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An empty directory left behind costs nothing.
        }
    }

    private static string Normalise(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
