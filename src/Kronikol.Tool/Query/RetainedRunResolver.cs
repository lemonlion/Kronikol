using System.Globalization;
using Kronikol.Reports;

namespace Kronikol.Tool.Query;

/// <summary>How <c>--run</c> ended against a reports directory.</summary>
internal enum RetainedRunOutcome
{
    /// <summary>One run answers to the name; its report is the one to read.</summary>
    Found,

    /// <summary>The name fits more than one run, or the directory could not be resolved; the refusal is written.</summary>
    Refused,

    /// <summary>No run kept here answers to the name. Every verb but <c>history</c> refuses; <c>history</c> asks the ledger.</summary>
    NotRetained
}

/// <summary>
/// <c>--run</c> against a reports directory (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6.2): the newest run
/// is on top, the ones before it are under <c>runs/&lt;name&gt;/</c>, and each says what it is in its
/// <c>Run.json</c> - which is all this reads. The folder names are for people and shells; the id a run
/// answers to is the manifest's, and the folder name is accepted beside it because on CI two steps of one
/// workflow run share an id (F9) and the folder is then the only thing that tells them apart.
/// </summary>
internal static class RetainedRunResolver
{
    private sealed record Candidate(string Name, string Directory, RunManifest Manifest, bool OnTop);

    public static RetainedRunOutcome Resolve(string reportArgument, string wanted, TextWriter error, out string? report, out string? resolvedName)
    {
        report = null;
        resolvedName = null;
        // The directory the newest run is in: the argument, or - for a file, or a directory that holds the
        // reports directory somewhere beneath it - wherever the ordinary lookup lands.
        if (QueryCommand.ResolveReport(reportArgument, error) is not { } topReport)
            return RetainedRunOutcome.Refused;
        var directory = Path.GetDirectoryName(Path.GetFullPath(topReport))!;

        var candidates = new List<Candidate>();
        if (RunManifest.TryRead(Path.Combine(directory, RunManifest.FileName)) is { } top)
            candidates.Add(new Candidate("(the newest run)", directory, top, OnTop: true));
        candidates.AddRange(ReportFolders.RetainedRuns(directory).Select(r => new Candidate(Path.GetFileName(r.Directory), r.Directory, r.Manifest, OnTop: false)));

        List<Candidate> matches;
        if (string.Equals(wanted, ReportHistory.LastFailedAlias, StringComparison.OrdinalIgnoreCase))
            matches = candidates.Where(c => c.Manifest.Failed > 0).Take(1).ToList();
        else if (string.Equals(wanted, ReportHistory.PreviousAlias, StringComparison.OrdinalIgnoreCase))
            matches = candidates.Where(c => !c.OnTop).Take(1).ToList();
        else
        {
            matches = candidates.Where(c => string.Equals(c.Manifest.Run, wanted, StringComparison.Ordinal) || !c.OnTop && string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                matches = candidates.Where(c => c.Manifest.Run.Contains(wanted, StringComparison.OrdinalIgnoreCase) || !c.OnTop && c.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (matches.Count == 1)
        {
            var found = matches[0];
            // The folder for a kept run - two of them can share an id (F9) - and the id for the run on top.
            resolvedName = found.OnTop ? found.Manifest.Run : found.Name;
            report = found.OnTop ? topReport : QueryCommand.ResolveReport(found.Directory, error);
            return report is null ? RetainedRunOutcome.Refused : RetainedRunOutcome.Found;
        }

        if (matches.Count > 1)
        {
            error.WriteLine($"{matches.Count} runs kept under {directory} answer to {wanted} — name one by its folder:");
            foreach (var match in matches)
                error.WriteLine("  " + Describe(match));
            return RetainedRunOutcome.Refused;
        }

        return RetainedRunOutcome.NotRetained;
    }

    /// <summary>The refusal every verb but <c>history</c> gives for a run that is not kept: what is, and where its results still are.</summary>
    public static void RefuseNotRetained(string reportArgument, string wanted, TextWriter error)
    {
        var directory = Directory.Exists(reportArgument) && File.Exists(Path.Combine(reportArgument, "TestRunReport.json"))
            ? Path.GetFullPath(reportArgument)
            : QueryCommand.ResolveReport(reportArgument, TextWriter.Null) is { } top ? Path.GetDirectoryName(Path.GetFullPath(top))! : reportArgument;
        var retained = ReportFolders.RetainedRuns(directory);
        error.WriteLine($"run {wanted} is not retained under {directory} (retained: {(retained.Count == 0 ? "none" : string.Join(", ", retained.Select(r => r.Manifest.Run)))}).");
        error.WriteLine($"The ledger still has its results, the stored first line of each error included: kronikol query history --run {wanted}");
    }

    private static string Describe(Candidate candidate) =>
        $"{candidate.Name}  {candidate.Manifest.Run}  {candidate.Manifest.At.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}  {candidate.Manifest.Scenarios} scenarios, {candidate.Manifest.Failed} failed";
}
