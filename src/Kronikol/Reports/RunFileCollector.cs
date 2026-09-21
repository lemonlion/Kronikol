namespace Kronikol.Reports;

/// <summary>
/// The files one run wrote into its reports directory, collected where the bytes reach disk, for the
/// run's <see cref="RunManifest"/> (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6.2).
///
/// <para><b>Not from the list of outputs.</b> <c>RunOutputs</c> returns the <em>labels</em> of the outputs
/// that completed, and two of those are not file names (<c>"TestRunReport schema"</c>;
/// <c>"ComponentDiagram.html"</c>, which also writes an image), while <c>CiSummary.md</c>,
/// <c>DiagnosticReport.html</c> and the attachment copies never pass through it at all. A manifest built
/// from what was planned would also name the file an output failure prevented — leaving the previous
/// run's bytes listed as this run's, the mistake <c>StaleOutputTests</c> exists to catch. So the record is
/// made by the code that wrote the file, after the write returned.</para>
///
/// <para>An <see cref="AsyncLocal{T}"/>, for the reason <c>ReportGenerator</c>'s active reports directory
/// and <see cref="ReportDiagnosticsScope"/> are: it flows into the <c>Parallel.Invoke</c> workers that do
/// the writing, and keeps two generations running at once in one process apart. Recording when nothing is
/// scoped is a no-op, which is what <c>kronikol merge</c> relies on — it reaches <c>WriteFile</c> without
/// being a run, and writes no manifest.</para>
/// </summary>
internal sealed class RunFileCollector
{
    private static readonly AsyncLocal<RunFileCollector?> Active = new();

    private readonly string _directory;
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _attachments = new(StringComparer.OrdinalIgnoreCase);

    private RunFileCollector(string directory) => _directory = Path.GetFullPath(directory);

    /// <summary>The collector for the run in flight, or null when none is scoped.</summary>
    internal static RunFileCollector? Current => Active.Value;

    /// <summary>Scopes a collector for a run writing into <paramref name="directory"/> until the handle is disposed.</summary>
    internal static IDisposable Begin(string directory)
    {
        var previous = Active.Value;
        Active.Value = new RunFileCollector(directory);
        return new Scope(previous);
    }

    /// <summary>
    /// Records a file that was just written, by its full path. Kept when it is at the top level of the
    /// run's directory (<see cref="RunManifest.Files"/>) or under its <c>attachments/</c>
    /// (<see cref="RunManifest.Attachments"/>); anything else is not the manifest's to list. Two writers
    /// resolve the directory themselves rather than from the ambient one, so where the file actually went
    /// is what is asked, not where it was meant to go.
    /// </summary>
    internal static void Record(string fullPath)
    {
        if (Active.Value is not { } collector)
            return;

        string relative;
        try
        {
            relative = Path.GetRelativePath(collector._directory, Path.GetFullPath(fullPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        lock (collector._files)
        {
            if (segments.Length == 1 && segments[0] is not (".." or "."))
                collector._files.Add(segments[0]);
            else if (segments.Length == 2 && string.Equals(segments[0], AttachmentsFolderName, StringComparison.OrdinalIgnoreCase))
                collector._attachments.Add(AttachmentsFolderName + "/" + segments[1]);
        }
    }

    /// <summary>The folder attachment copies go to, and the first segment of every <see cref="RunManifest.Attachments"/> entry.</summary>
    internal const string AttachmentsFolderName = "attachments";

    /// <summary>
    /// What was recorded and is on disk now, sorted. A file recorded and since replaced by something that
    /// is not a file is dropped: the manifest's promise is that everything it names is there.
    /// </summary>
    internal (string[] Files, string[] Attachments) Snapshot()
    {
        lock (_files)
        {
            return (
                _files.Where(name => File.Exists(Path.Combine(_directory, name))).Order(StringComparer.Ordinal).ToArray(),
                _attachments.Where(name => File.Exists(Path.Combine(_directory, name.Replace('/', Path.DirectorySeparatorChar)))).Order(StringComparer.Ordinal).ToArray());
        }
    }

    private sealed class Scope(RunFileCollector? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}
