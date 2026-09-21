namespace Kronikol.Reports;

/// <summary>A run kept under <c>runs/</c>: where it is, and what its <c>Run.json</c> says.</summary>
/// <param name="Directory">The retained run's directory, as a full path.</param>
/// <param name="Manifest">Its manifest. <see cref="RunManifest.Run"/> is the id <c>--run</c> matches; the directory name is only its sanitised form.</param>
public sealed record RetainedRun(string Directory, RunManifest Manifest);

/// <summary>
/// The folders of a reports directory that are not the newest run, and the one reading of them everything
/// shares (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6.3).
///
/// <para>A reports directory holds the newest run at its top level, byte for byte what it always held.
/// Beneath it, <c>runs/&lt;run&gt;/</c> holds the runs before it and <c>baseline/</c> the report a
/// <c>diff --baseline</c> compares against. Both are full of files that look exactly like a report, so
/// every recursive sweep — <c>kronikol merge &lt;dir&gt;</c>, <c>kronikol query &lt;parent&gt;</c>,
/// <c>kronikol history record</c> — has to be told to leave them alone, or a retained failing run is
/// merged into the green report beside it as one more shard. One helper, so the rule cannot be right in
/// two sweeps and forgotten in the third.</para>
/// </summary>
public static class ReportFolders
{
    /// <summary>The folder retained runs are kept in, one directory each, under the reports directory.</summary>
    public const string RunsFolderName = "runs";

    /// <summary>The conventional folder a <c>--baseline</c> report lives in, beside the current one.</summary>
    public const string BaselineFolderName = "baseline";

    /// <summary>
    /// What a rotation in flight is called under <c>runs/</c>: files are staged into
    /// <c>.incoming-&lt;name&gt;</c> and one directory rename publishes it as <c>&lt;name&gt;</c>, so a
    /// retained run is whole or absent and never half-present. A directory still carrying the prefix is
    /// one a killed process left: never a retained run, never counted by pruning, named by
    /// <see cref="Unfinished"/>.
    /// </summary>
    public const string IncomingPrefix = ".incoming-";

    /// <summary>
    /// Whether a sweep that started at <paramref name="root"/> leaves <paramref name="file"/> alone: true
    /// when <c>runs</c> or <c>baseline</c> is a segment of the file's path <em>relative to the root</em>,
    /// in any case.
    /// </summary>
    /// <remarks>
    /// Relative, because the exemption is for what a sweep <em>finds</em> and never for what it is
    /// <em>given</em>: <c>kronikol merge Reports/runs/gh_1_1</c> names a retained run on purpose, and from
    /// that root its report has no <c>runs</c> segment. For the same reason a <c>runs</c> directory above
    /// the root — somebody's own folder name — reserves nothing, and neither does a file outside the root.
    /// </remarks>
    public static bool IsReserved(string root, string file)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(file);

        var relative = Path.GetRelativePath(root, file);
        if (Path.IsPathRooted(relative))
            return false;

        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length > 0 && segments[0] == "..")
            return false;

        return segments.Any(segment =>
            string.Equals(segment, RunsFolderName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, BaselineFolderName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The runs retained under <paramref name="reportsDirectory"/>, newest first: every directory directly
    /// under <c>runs/</c> that holds a readable <c>Run.json</c>. Staging directories
    /// (<see cref="IncomingPrefix"/>) are never included, whatever they hold. Never throws; a directory
    /// that cannot be listed retains nothing.
    /// </summary>
    /// <remarks>
    /// Ordered by the manifest's <see cref="RunManifest.At"/> — what the run said, not what the file
    /// system says about a directory that has since been renamed, copied out of an artifact or checked
    /// out. Two runs of one second are told apart by name, descending, which puts the <c>-2</c> a second
    /// rotation of one CI run id took ahead of the run it followed.
    /// </remarks>
    public static IReadOnlyList<RetainedRun> RetainedRuns(string reportsDirectory)
    {
        ArgumentNullException.ThrowIfNull(reportsDirectory);

        var retained = new List<RetainedRun>();
        foreach (var directory in RunDirectories(reportsDirectory))
        {
            if (IsIncoming(directory))
                continue;
            if (RunManifest.TryRead(Path.Combine(directory, RunManifest.FileName)) is { } manifest)
                retained.Add(new RetainedRun(directory, manifest));
        }

        return retained
            .OrderByDescending(run => run.Manifest.At)
            .ThenByDescending(run => Path.GetFileName(run.Directory), StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// What an interrupted rotation left under <c>runs/</c>, as full paths: staging directories
    /// (<see cref="IncomingPrefix"/>), and directories with no readable <c>Run.json</c>. Nothing prunes
    /// these and nothing offers them to <c>--run</c>; <c>kronikol history doctor</c> names them so that a
    /// person decides.
    /// </summary>
    public static IReadOnlyList<string> Unfinished(string reportsDirectory)
    {
        ArgumentNullException.ThrowIfNull(reportsDirectory);

        return RunDirectories(reportsDirectory)
            .Where(directory => IsIncoming(directory) || RunManifest.TryRead(Path.Combine(directory, RunManifest.FileName)) is null)
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool IsIncoming(string directory) =>
        Path.GetFileName(directory).StartsWith(IncomingPrefix, StringComparison.OrdinalIgnoreCase);

    private static string[] RunDirectories(string reportsDirectory)
    {
        try
        {
            var runs = Path.Combine(reportsDirectory, RunsFolderName);
            return Directory.Exists(runs) ? Directory.GetDirectories(runs) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }
}
