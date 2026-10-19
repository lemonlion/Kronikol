using System.Globalization;
using System.Text;

namespace Kronikol.Reports;

/// <summary>One output file the run-end pointer names, and how big it is on disk.</summary>
/// <param name="Name">File name only — the pointer prints the directory once.</param>
/// <param name="Bytes">Size on disk; the pointer shows it only for the machine-readable data file.</param>
public sealed record RunSummaryFile(string Name, long Bytes);

/// <summary>
/// One failing scenario, reduced to what is safe to print on a console: an id, a feature and a name.
/// </summary>
/// <param name="StableId">The cross-run identifier, so the line is an address rather than a label.</param>
/// <param name="FeatureName">The feature the scenario belongs to.</param>
/// <param name="ScenarioName">The scenario's display name.</param>
public sealed record RunSummaryFailure(string StableId, string FeatureName, string ScenarioName);

/// <summary>What the run-end pointer says, gathered before anything is formatted.</summary>
/// <param name="Directory">The directory the reports were written to.</param>
/// <param name="Files">The files worth naming, in the order they should be printed.</param>
/// <param name="ScenarioCount">How many scenarios the run held.</param>
/// <param name="Failures">The failing scenarios, in report order.</param>
/// <param name="AgentInstructionsWritten">Whether <c>CLAUDE.md</c> / <c>AGENTS.md</c> sit in the directory.</param>
/// <param name="QueryTarget">
/// What to hand <c>kronikol query</c> when the directory alone would not do. A directory lookup finds a
/// <c>TestRunReport.json</c> and nothing else, and a merge names its data file after its <c>-o</c>, as a run does
/// after a custom <see cref="ReportConfigurationOptions.HtmlTestRunReportFileName"/> - so a pointer that printed
/// the directory for <c>Combined.json</c> printed a command that fails. Null means the directory is enough.
/// </param>
/// <param name="History">
/// The one line the cross-run ledger has to say about this run — what broke, what was fixed, what flips —
/// or null when there is nothing worth a line. Printed as given, on both channels.
/// </param>
public sealed record RunSummary(
    string Directory,
    IReadOnlyList<RunSummaryFile> Files,
    int ScenarioCount,
    IReadOnlyList<RunSummaryFailure> Failures,
    bool AgentInstructionsWritten,
    string? QueryTarget = null,
    string? History = null)
{
    /// <summary>
    /// The run that was on top before this one, when it had failures and was moved to
    /// <c>runs/&lt;run&gt;/</c> rather than overwritten; null otherwise. A property rather than one more
    /// positional parameter, so the constructor every caller already compiles against does not change.
    /// </summary>
    public RunSummaryPreviousRun? PreviousRun { get; init; }

    /// <summary>
    /// Whether this run wrote <c>query.cs</c> into <see cref="Directory"/>: <c>kronikol query</c> with nothing
    /// installed, which a failing run's pointer and the CI summary then name beside the tool
    /// (<see cref="ReportConfigurationOptions.WriteQueryScript"/>). A property for the same reason as
    /// <see cref="PreviousRun"/>.
    /// </summary>
    public bool QueryScriptWritten { get; init; }
}

/// <summary>The previous run, kept: where it went and how many of its scenarios had failed.</summary>
/// <param name="Directory">The retained run's directory, as a full path.</param>
/// <param name="Failed">How many scenarios failed in it.</param>
public sealed record RunSummaryPreviousRun(string Directory, int Failed);

/// <summary>
/// The last thing a run says: where the reports are, how big the data file is, what failed, and the one
/// command that explains it.
///
/// <para>Report generation has never been silent — <see cref="ReportDiagnostics.Analyse(Kronikol.Tracking.RequestResponseLog[], Feature[], bool, bool)"/>'s lines print on
/// every run — but nothing ever pointed at the reports, so an agent watching a failing build had no signal
/// that <c>kronikol query failures</c> exists. This is that signal.</para>
///
/// <para><b>Nothing captured is ever printed here.</b> The console is the widest-read surface a test run
/// touches: CI logs are visible to more people than artifacts, are copied into chat, and are pasted into
/// issues. So the pointer carries paths, counts, stableIds and scenario names — never a message, a URI, a
/// SQL statement or a body. The one place captured content is written is
/// <see cref="FailuresDigestGenerator">the digest</see>, which is a file in the reports directory, subject
/// to the same exposure as the report it derives from.</para>
///
/// <para><b>The console is not a reliable channel</b> and this is not the only one. Measured on .NET 10
/// (2026-09-12): under <c>dotnet test</c> every VSTest-hosted runner — xUnit v2/v3, NUnit, MSTest —
/// swallows what the library writes from a run-end hook at every verbosity, and TUnit's own runner
/// suppresses it too; only a directly executed MTP host (<c>dotnet run --project</c>, xUnit v3) shows it.
/// The durable channels are the files this release adds next to the report — <c>Failures.md</c> and
/// <c>CLAUDE.md</c>/<c>AGENTS.md</c> — plus GitHub's job summary, which is a file the runner reads rather
/// than stdout and therefore survives. Azure DevOps takes its summary as a logging command on stdout, so
/// under <c>dotnet test</c> that one is swallowed with the rest (measured again on 2026-10-13,
/// plans/CI_SUMMARY_AUDIENCE_PLAN.md F10). The pointer is free where it lands and harmless where it does
/// not.</para>
///
/// <para><b>Who reads the CI text</b> (plans/CI_SUMMARY_AUDIENCE_PLAN.md): a GitHub job summary is read by
/// people only, in a browser, since no API, <c>gh</c> command or MCP tool returns one; the annotation by
/// people and by agents through <c>gh run view</c>, where it arrives (a workflow command on stdout, it does not
/// under <c>dotnet test</c>); <c>CiSummary.md</c> by whoever downloads the artifact.
/// None of them is on the runner. So the CI section speaks to people first, labels its rule for AI agents,
/// and names what exists where its readers are (<see cref="CiReader"/>).</para>
/// </summary>
public static class RunSummaryConsoleWriter
{
    /// <summary>Failure lines printed before the rest are elided; a wall of names helps nobody.</summary>
    internal const int MaxFailureLines = 20;

    private const string DigestFileName = "Failures.md";

    /// <summary>
    /// Gathers the pointer's facts. <paramref name="candidateFiles"/> are the names it should mention;
    /// those absent from disk are dropped, so the pointer never claims a file an output failure prevented.
    /// </summary>
    public static RunSummary Summarise(Feature[] features, string directory, IEnumerable<string> candidateFiles, bool agentInstructionsWritten, string? suite = null, string? queryTarget = null, string? history = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(candidateFiles);

        var files = new List<RunSummaryFile>();
        foreach (var name in candidateFiles)
        {
            long length;
            try
            {
                var info = new FileInfo(Path.Combine(directory, name));
                if (!info.Exists)
                    continue;
                length = info.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            files.Add(new RunSummaryFile(name, length));
        }

        var failures = new List<RunSummaryFailure>();
        var scenarioCount = 0;
        foreach (var feature in features)
        {
            foreach (var scenario in feature.Scenarios ?? [])
            {
                scenarioCount++;
                if (scenario.Result != ExecutionResult.Failed)
                    continue;
                failures.Add(new RunSummaryFailure(
                    ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues),
                    feature.DisplayName,
                    scenario.DisplayName));
            }
        }

        return new RunSummary(directory, files, scenarioCount, failures, agentInstructionsWritten, queryTarget, history);
    }

    /// <summary>Formats the pointer. Ends with a newline; a run with nothing failing is one line.</summary>
    public static string Build(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var text = new StringBuilder();
        var files = summary.Files.Count == 0
            ? ""
            : "  (" + string.Join(" · ", summary.Files.Select(Describe)) + ")";
        text.Append("Kronikol: reports written to ").Append(OneLine(summary.Directory)).Append(files).Append('\n');

        // What the last runs say about this one, when they say anything: the reader of a failing build
        // wants "1 broke, 2 flaky" before the list of names.
        if (summary.History is { Length: > 0 } history)
            text.Append("  ").Append(OneLine(history)).Append('\n');

        // `kronikol query` reads a JSON data file and nothing else, so a run that wrote XML, YAML or no data
        // file gets no command that would answer "No TestRunReport.json under" (plans/CI_SUMMARY_AUDIENCE_PLAN.md F11).
        var queryable = QueryableDataFile(summary) is not null;

        // "Nothing warned me" (#80): the failing report that was on top a moment ago is no longer the one
        // `kronikol query failures <dir>` opens, so the run that replaced it says where it went. Paths and
        // a count, like everything else here — never anything the previous run captured.
        if (summary.PreviousRun is { } previousRun)
            text.Append(queryable
                ? $"  previous run ({previousRun.Failed} failed) kept: {Quote(previousRun.Directory)} — kronikol query failures {QueryTarget(summary)} --run {KeptRunArgument(previousRun)}\n"
                : $"  previous run ({previousRun.Failed} failed) kept: {Quote(previousRun.Directory)}\n");

        if (summary.Failures.Count > 0)
        {
            var target = QueryTarget(summary);
            text.Append(queryable ? $"  {summary.Failures.Count} failed — kronikol query failures {target}\n"
                : HasDigest(summary) ? $"  {summary.Failures.Count} failed — see {DigestFileName}\n"
                : $"  {summary.Failures.Count} failed\n");
            foreach (var failure in summary.Failures.Take(MaxFailureLines))
                text.Append($"    {OneLine(failure.StableId)}  {OneLine(failure.FeatureName)} › {OneLine(failure.ScenarioName)}\n");

            var remaining = summary.Failures.Count - MaxFailureLines;
            if (remaining > 0)
                text.Append(HasDigest(summary) ? $"    … and {remaining} more (see {DigestFileName})\n"
                    : queryable ? $"    … and {remaining} more (kronikol query failures {target})\n"
                    : $"    … and {remaining} more\n");

            // The bait for the nested instruction file: an agent that reads anything in this directory
            // loads the CLAUDE.md sitting beside it, and that file is what teaches it never to open the
            // JSON. Only printed when something failed — a green run has nothing to debug, and a pointer
            // that speaks on every run is a pointer people learn to skip.
            var neverOpen = NeverOpen(summary) is { } file ? $"; never open {OneLine(file)}" : "";
            if (summary.AgentInstructionsWritten)
                text.Append($"  agents: read {Quote(Path.Combine(summary.Directory, "CLAUDE.md"))} first{neverOpen}\n");
            else if (queryable)
                text.Append($"  agents: run kronikol query --help{neverOpen}\n");
            else if (neverOpen.Length > 0)
                text.Append($"  agents: {neverOpen[2..]}\n");

            // The line above names a tool an agent that may not install anything cannot run. The query.cs
            // this run wrote is the same engine with nothing installed and no network.
            if (summary.QueryScriptWritten && queryable)
                text.Append($"  no tool: dotnet run --file {Quote(Path.Combine(summary.Directory, QueryScriptGenerator.FileName))} -- failures {QueryTarget(summary)}\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// The GitHub Actions annotation for a failing run, or null when nothing failed. One line, same content
    /// rules as the pointer. Built for the CI the process runs on, read from its environment.
    /// </summary>
    public static string? BuildGitHubNotice(RunSummary summary) => BuildGitHubNotice(summary, CiReader.FromProcess());

    /// <summary>
    /// The annotation for <paramref name="reader"/>: the one CI channel people and agents both read, and the
    /// only one an agent reads through <c>gh run view</c>, so it is complete without the job summary. Where the
    /// run can be downloaded it names the folder relative to the workspace, the files for people, and the
    /// download and the query for agents, never the runner's folder; elsewhere the query on the folder. It is a
    /// workflow command on stdout, so it reaches a run only where the run's stdout does: from <c>kronikol merge</c>,
    /// <c>kronikol ingest</c> or a directly run MTP host, never under <c>dotnet test</c>.
    /// </summary>
    internal static string? BuildGitHubNotice(RunSummary summary, CiReader reader)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(reader);
        if (summary.Failures.Count == 0)
            return null;

        var head = $"::notice title=Kronikol::{summary.Failures.Count} failed of {summary.ScenarioCount} scenarios";
        var queryable = QueryableDataFile(summary) is not null;
        if (!reader.CanDownload)
        {
            var digest = HasDigest(summary) ? $" · {DigestFileName}" : "";
            return queryable ? $"{head} — kronikol query failures {QueryTarget(summary)}{digest}"
                : HasDigest(summary) ? $"{head} — {DigestFileName}"
                : head;
        }

        var download = $"gh run download {ShellWord(reader.RunId)} --repo {ShellWord(RepositoryArgument(reader))} --dir {DownloadFolder(reader)}";
        // A download only where an agent has something in it: the data file to query, or the digest to read.
        var agents = queryable ? $"agents: {download}, then kronikol query failures {DownloadTarget(summary, DownloadFolder(reader))}"
            : HasDigest(summary) ? $"agents: {download}, then read {DigestFileName} in it"
            : null;
        var forPeople = new[] { HtmlFileName(summary), HasDigest(summary) ? DigestFileName : null }.OfType<string>().Select(OneLine).ToArray();
        var readers = string.Join("; ", new[] { forPeople.Length > 0 ? $"people: {string.Join(" or ", forPeople)}" : null, agents }.OfType<string>());
        var folder = FolderForPeople(summary.Directory, reader.Workspace) is { } relative ? OneLine(relative) : CheckoutRoot;
        return readers.Length > 0 ? $"{head}. Reports in {folder} ({readers})" : $"{head}. Reports in {folder}";
    }

    /// <summary>
    /// Writes the pointer through <paramref name="write"/> (one call per line), and on GitHub Actions the
    /// <c>::notice</c> annotation after it.
    /// </summary>
    public static void Write(RunSummary summary, CiEnvironment environment, Action<string> write)
        => Write(summary, environment, write, write);

    /// <summary>
    /// The pointer through <paramref name="write"/> and the GitHub annotation through
    /// <paramref name="writeAnnotation"/>.
    ///
    /// <para>Two sinks because the two have different requirements and only one of them is negotiable. The
    /// pointer is a diagnostic and could reasonably go to stderr; the <c>::notice</c> is a <b>workflow
    /// command</b>, which GitHub parses from stdout and nowhere else, so routing it anywhere else does not
    /// make it quieter — it makes it disappear. Sharing one sink meant a future decision about the pointer
    /// would silently take the annotation with it.</para>
    ///
    /// <para>The decision itself was measured and went the other way, which is why the default still sends
    /// both to the same place: on .NET&#160;10, <c>dotnet test</c> under NUnit&#160;4 let a library's
    /// stderr through while swallowing its stdout, and under xUnit&#160;2 swallowed both. No console stream
    /// survives every runner, so choosing a different one does not make the channel reliable. The
    /// separation stands anyway, because the constraint is real whatever the default is.</para>
    /// </summary>
    public static void Write(RunSummary summary, CiEnvironment environment, Action<string> write, Action<string> writeAnnotation)
        => Write(summary, environment, write, writeAnnotation,
            // The annotation is the only part that reads the CI, and only GitHub's.
            environment == CiEnvironment.GitHubActions ? CiReader.FromProcess() : CiReader.None);

    /// <summary>The pointer, and on GitHub Actions the annotation for <paramref name="reader"/>.</summary>
    internal static void Write(RunSummary summary, CiEnvironment environment, Action<string> write, Action<string> writeAnnotation, CiReader reader)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(writeAnnotation);
        ArgumentNullException.ThrowIfNull(reader);

        foreach (var line in Build(summary).TrimEnd('\n').Split('\n'))
            write(line);

        if (environment == CiEnvironment.GitHubActions && BuildGitHubNotice(summary, reader) is { } notice)
            writeAnnotation(notice);
    }

    /// <summary>
    /// The section appended to <c>CiSummary.md</c> and the CI job summary, for the CI the process runs on (read
    /// from its environment, with default options): "Debug this run" when something failed, "The report" when
    /// nothing did. See <see cref="ReportConfigurationOptions.WriteCiDebugSection"/> for who reads it.
    /// </summary>
    public static string BuildCiSummarySection(RunSummary summary) => BuildCiSummarySection(summary, CiReader.FromProcess());

    /// <summary>
    /// The section for <paramref name="reader"/>. People first: a GitHub job summary is read by people only, in a
    /// browser, so it says which report to open, where this job wrote it and where the run's artifacts are. Then,
    /// labelled for AI agents, the rule not to open the report and the commands that query it where an agent is:
    /// a download of the run's or the build's artifacts, or the folder off CI. It names only the files the run wrote, and an
    /// upload only where Kronikol made one (plans/CI_SUMMARY_AUDIENCE_PLAN.md R1, R2).
    ///
    /// <para>A green run gets no part for agents, under "The report" (R3): there is nothing to debug, and a
    /// block that speaks on every run is a block people learn to skip, while the full summary's diagrams are a
    /// preview of the report it names. The history and previous-run lines stay, each only when it has something
    /// to say.</para>
    ///
    /// <para>On GitHub the job summary is a file the runner reads, so it survives the runners that swallow
    /// console output. Azure DevOps takes it as a logging command on stdout, which <c>dotnet test</c> swallows
    /// (F10): there it arrives from <c>kronikol merge</c>, <c>kronikol ingest</c> or a directly run MTP host.</para>
    /// </summary>
    internal static string BuildCiSummarySection(RunSummary summary, CiReader reader)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(reader);

        var markdown = new StringBuilder();
        if (summary.Failures.Count == 0)
        {
            markdown.Append("## The report\n\n").Append(PeoplePart(summary, reader)).Append("\n\n");
            AppendRunLines(markdown, summary, reader);
            return markdown.ToString();
        }

        markdown.Append("## Debug this run\n\n").Append(PeoplePart(summary, reader)).Append("\n\n");
        AppendAgentPart(markdown, summary, reader);
        AppendRunLines(markdown, summary, reader);
        return markdown.ToString();
    }

    /// <summary>
    /// What the cross-run ledger says of the run and where the previous run is kept, each only when it has something
    /// to say: facts about the run for every reader, so a green run's section carries them too. A green run after a
    /// failing one is the case #80 is about, the failure no longer on top. The console is the channel most runners
    /// swallow, and on CI the run this is about may be a retry's first attempt: the job summary is where it has to be
    /// said to be read.
    /// </summary>
    private static void AppendRunLines(StringBuilder markdown, RunSummary summary, CiReader reader)
    {
        if (summary.History is { Length: > 0 } history)
            markdown.Append(OneLine(history)).Append("\n\n");

        if (summary.PreviousRun is { } previousRun)
            markdown.Append(PreviousRunLine(summary, previousRun, reader)).Append("\n\n");
    }

    /// <summary>
    /// What a person opens, where this job wrote it, and where the run's artifacts are: only the files the run
    /// wrote (F6), and an upload stated only where Kronikol made one. Kronikol cannot see a GitHub job's upload
    /// step, so there it says what has to be true (<see cref="UploadClause"/>).
    /// </summary>
    private static string PeoplePart(RunSummary summary, CiReader reader)
    {
        var html = HtmlFileName(summary);
        // Failures.md says "No failures" on a green run: named for people only when something is in it.
        var digest = summary.Failures.Count > 0 && HasDigest(summary);
        var folder = FolderForPeople(summary.Directory, reader.Workspace) is { } relative ? $"`{OneLine(relative)}`" : CheckoutRoot;
        var writer = reader.Provider == CiEnvironment.None ? "run" : "job";

        var text = new StringBuilder("**People:** ");
        bool several;
        if (html is not null)
        {
            text.Append($"open `{OneLine(html)}` in a browser to read the whole run, diagrams included");
            if (digest)
                text.Append($", or `{DigestFileName}` for every failure in context");
            several = digest;
            text.Append($". This {writer} wrote {(several ? "them" : "it")} to {folder}");
        }
        else if (digest)
        {
            text.Append($"open `{DigestFileName}` for every failure in context. This {writer} wrote it to {folder}");
            several = false;
        }
        else
        {
            text.Append($"this {writer} wrote its reports to {folder}");
            several = true;
        }

        return text.Append(UploadClause(reader, several)).Append('.').ToString();
    }

    /// <summary>
    /// Where the files are after the job, by what Kronikol knows of the upload (plans/CI_SUMMARY_AUDIENCE_PLAN.md
    /// §4.3): on GitHub nothing, or that it handed the folder to the job's upload step; on Azure DevOps the artifact
    /// it asked the agent to upload, or nothing. Off CI, nothing to say.
    /// </summary>
    private static string UploadClause(CiReader reader, bool several)
    {
        var theyAre = several ? "they are" : "it is";
        switch (reader.Provider)
        {
            case CiEnvironment.GitHubActions:
            {
                var artifacts = reader.RunUrl is { } run ? $"[artifacts]({LinkTarget(run)}#artifacts)" : "artifacts";
                // reports-path holds one folder, the last a step wrote: with several test projects in one step only the
                // last one's is uploaded (wiki CI-Artifact-Upload, "One reports directory per step"), so none claims it.
                return reader.HandsFolderToUpload
                    ? $"; this run named the folder in `reports-path` for the job's upload step, which uploads the last folder named there to this run's {artifacts}"
                    : $"; {theyAre} in this run's {artifacts} when the job uploads that folder";
            }
            case CiEnvironment.AzureDevOps:
            {
                var build = reader.RunUrl is { } run ? $" ([build]({LinkTarget(run)}))" : "";
                return reader.ArtifactName is { } artifact
                    ? $"; {theyAre} in this build's `{OneLine(artifact)}` artifact{build}"
                    : $"; {theyAre} in this build's artifacts{build} when the pipeline publishes that folder";
            }
            default:
                return "";
        }
    }

    /// <summary>
    /// The agent part: the rule, then the commands that answer where an agent is. On GitHub with a run id and a
    /// repository, a guarded download of the run's artifacts and queries on it, since a GitHub job summary has no
    /// agent readers and the runner's folder is gone; on Azure DevOps with an artifact, queries on its download, and
    /// without one, queries on a download of what the pipeline published; elsewhere today's queries on the folder, for
    /// a reader on that machine. A query only for a JSON data file, the one format the tool reads (F11). Every word of
    /// the block is quoted as bash reads it (<see cref="ShellWord"/>).
    /// </summary>
    private static void AppendAgentPart(StringBuilder markdown, RunSummary summary, CiReader reader)
    {
        var queryable = QueryableDataFile(summary) is not null;
        var digest = HasDigest(summary);
        var artifact = reader.Provider == CiEnvironment.AzureDevOps && !reader.CanDownload ? reader.ArtifactName : null;

        var named = new[] { DataFile(summary), HtmlFileName(summary) }.OfType<string>().Select(n => $"`{OneLine(n)}`").ToArray();
        // A compressed data file cannot be read without the tool at all, which is the whole reason for it (F26).
        var rule = named.Length > 0
            ? IsCompressed(DataFile(summary))
                ? $"do not open {string.Join(" or ", named)} — the data file is compressed: use `kronikol query`, and a real report reaches megabytes, a single embedded diagram larger than a context window."
                : $"do not open {string.Join(" or ", named)} — a real report reaches megabytes, and a single embedded diagram can be larger than a context window."
            : null;
        // "instead" only says something beside the rule, and the label is followed by lower case.
        string Lead(string what, string end) =>
            rule is not null ? $"{what} instead{end}" : char.ToLowerInvariant(what[0]) + what[1..] + end;

        var block = new List<string>();
        var after = new List<string>();
        string? lead = null;

        if (reader.CanDownload)
        {
            var folder = DownloadFolder(reader);
            var guard = $"test -d {folder} || gh run download {ShellWord(reader.RunId)} --repo {ShellWord(RepositoryArgument(reader))} --dir {folder}";
            if (queryable)
            {
                var target = DownloadTarget(summary, folder);
                lead = Lead("Download the run's artifacts and query them", ":");
                block.AddRange([guard, "dotnet tool install -g Kronikol.Tool", $"kronikol query summary {target}", $"kronikol query failures {target}"]);
                if (digest)
                    after.Add($"Read `{DigestFileName}` in the download first.");
                after.Add(SeveralInTheDownload(summary, folder));
            }
            else if (digest)
            {
                lead = Lead($"Download the run's artifacts and read `{DigestFileName}` in them", ":");
                block.Add(guard);
            }

            // The download's folder carries the attempt (DownloadFolder), so a block pasted after a re-run downloads again.
            if (reader.Attempt > 1)
                after.Add($"This is attempt {reader.Attempt}. After a re-run, `gh run download` keeps one upload of each artifact name, not always this attempt's"
                          + (queryable ? ": `kronikol query summary` names the report's attempt." : "."));
            if (queryable && summary.QueryScriptWritten)
                after.Add(QueryScriptOffTheRunner(summary, reader));
            after.Add($"A later step of this job finds the folder at `{OneLine(summary.Directory)}`.");
        }
        else if (artifact is not null)
        {
            var folder = ShellWord(artifact);
            if (queryable)
            {
                var target = DownloadTarget(summary, folder);
                lead = Lead($"Download this build's `{OneLine(artifact)}` artifact and query it", ":");
                block.AddRange(["dotnet tool install -g Kronikol.Tool", $"kronikol query summary {target}", $"kronikol query failures {target}"]);
                if (digest)
                    after.Add($"Read `{DigestFileName}` in the artifact first.");
                if (summary.QueryScriptWritten)
                    after.Add(QueryScriptOffTheRunner(summary, reader));
            }
            else if (digest)
            {
                lead = Lead($"Download this build's `{OneLine(artifact)}` artifact and read `{DigestFileName}` in it", ".");
            }

            after.Add($"A later step of this job finds the folder at `{OneLine(summary.Directory)}`.");
        }
        else if (PublishedFolderDownload(reader) is { } published)
        {
            // Azure DevOps with no upload of Kronikol's own. Under dotnet test that upload never arrives (F10), so the
            // wiki has the pipeline publish the folder, and the summary is read on the build's page, off the agent: the
            // commands query a download of what the pipeline published, in a folder named by the build.
            var folder = ShellWord(published);
            if (queryable)
            {
                var target = DownloadTarget(summary, folder);
                lead = Lead($"When the pipeline publishes that folder, download it from this build's artifacts into `{OneLine(published)}` and query it", ":");
                block.AddRange(["dotnet tool install -g Kronikol.Tool", $"kronikol query summary {target}", $"kronikol query failures {target}"]);
                if (digest)
                    after.Add($"Read `{DigestFileName}` in the download first.");
                after.Add(SeveralInTheDownload(summary, folder));
                if (summary.QueryScriptWritten)
                    after.Add(QueryScriptOffTheRunner(summary, reader));
            }
            else if (digest)
            {
                lead = Lead($"When the pipeline publishes that folder, download it from this build's artifacts and read `{DigestFileName}` in it", ".");
            }

            after.Add($"A later step of this job finds the folder at `{OneLine(summary.Directory)}`.");
        }
        else if (queryable)
        {
            var target = QueryTarget(summary);
            var word = ShellWord(summary.QueryTarget ?? summary.Directory);
            lead = Lead("Query it", ":");
            block.AddRange(["dotnet tool install -g Kronikol.Tool", $"kronikol query summary {word}", $"kronikol query failures {word}"]);
            if (digest)
                after.Add($"Read `{DigestFileName}` beside the report first.");
            if (summary.QueryScriptWritten)
                after.Add($"Nothing to install and no network needed: `dotnet run --file {Quote(Path.Combine(summary.Directory, QueryScriptGenerator.FileName))} -- summary {target}` "
                          + "runs the same engine from the `query.cs` written beside the report, given the .NET 10 SDK.");
        }
        else if (digest)
        {
            lead = Lead($"Read `{DigestFileName}` beside the report", ".");
        }

        var opening = string.Join(" ", new[] { rule, lead }.OfType<string>());
        if (opening.Length > 0)
            markdown.Append("**AI agents:** ").Append(opening).Append("\n\n");
        if (block.Count > 0)
            markdown.Append("```bash\n").Append(string.Join("\n", block)).Append("\n```\n\n");
        if (opening.Length > 0 && after.Count > 0)
            markdown.Append(string.Join(" ", after)).Append("\n\n");
    }

    /// <summary>
    /// <c>query.cs</c> as it is read off the runner: the test run's own build is not there, so it runs only with the
    /// Kronikol that wrote the report in the reader's NuGet cache, and "no network needed" is true on the runner alone.
    /// </summary>
    private static string QueryScriptOffTheRunner(RunSummary summary, CiReader reader)
    {
        var argument = summary.QueryTarget is { } file ? Quote(Path.GetFileName(file)) : ".";
        var engine = reader.KronikolVersion is { Length: > 0 } version ? $"Kronikol {OneLine(version)}" : "the Kronikol that wrote it";
        return $"With nothing installed, `dotnet run --file {QueryScriptGenerator.FileName} -- failures {argument}` in the folder that holds the report runs the same engine, "
               + $"given the .NET 10 SDK and {engine} in the NuGet cache.";
    }

    /// <summary>
    /// The run kept under <c>runs/</c>: beside the report where a reader has a copy of it, by its folder on this
    /// machine otherwise.
    /// </summary>
    private static string PreviousRunLine(RunSummary summary, RunSummaryPreviousRun previousRun, CiReader reader)
    {
        var queryable = QueryableDataFile(summary) is not null;
        var run = KeptRunArgument(previousRun);
        if (ReadsADownload(reader))
        {
            var where = Relative(summary.Directory, previousRun.Directory) is { } kept
                ? $"is kept beside the report in `{OneLine(kept)}`"
                : $"is kept in `{OneLine(previousRun.Directory)}`";
            return $"The previous run ({previousRun.Failed} failed) {where}" + (queryable ? $": `--run {run}` opens it in any `kronikol query` of the report." : ".");
        }

        return queryable
            ? $"The previous run ({previousRun.Failed} failed) is kept in `{OneLine(previousRun.Directory)}` — `kronikol query failures {QueryTarget(summary)} --run {run}`."
            : $"The previous run ({previousRun.Failed} failed) is kept in `{OneLine(previousRun.Directory)}`.";
    }

    /// <summary>
    /// What <c>--run</c> is handed to open the run kept under <c>runs/</c>: its folder's name. <c>last-failed</c> is the
    /// newest run with a failure, the one on top included, so on a failing run, which is when anything is debugged, it
    /// opened this run again; and the folder tells apart two kept runs that share a CI run's id.
    /// </summary>
    private static string KeptRunArgument(RunSummaryPreviousRun previousRun) =>
        Quote(Path.GetFileName(previousRun.Directory.TrimEnd('/', '\\')));

    /// <summary>
    /// The folder <c>gh run download</c> is told to fill, as a bash word: named by the run, so two runs' downloads never
    /// meet, and by the attempt after a re-run. The guard skips a download into a folder that exists, so a folder named
    /// by the run alone kept whatever the first paste fetched: the second attempt's block queried the first's download.
    /// </summary>
    private static string DownloadFolder(CiReader reader) =>
        ShellWord(reader.Attempt > 1 ? $"kronikol-{reader.RunId}-{reader.Attempt.ToString(CultureInfo.InvariantCulture)}" : $"kronikol-{reader.RunId}");

    /// <summary>
    /// On Azure DevOps with no upload of Kronikol's own, the folder a reader downloads what the pipeline published into,
    /// named by the build; null anywhere else, and where the build has no id to name it by.
    /// </summary>
    private static string? PublishedFolderDownload(CiReader reader) =>
        reader.Provider == CiEnvironment.AzureDevOps && reader.ArtifactName is null && !string.IsNullOrWhiteSpace(reader.RunId)
            ? $"kronikol-{reader.RunId}"
            : null;

    /// <summary>Whether the reader reads the report from a download, beside which the kept runs sit under <c>runs/</c>.</summary>
    private static bool ReadsADownload(CiReader reader) =>
        reader.CanDownload || (reader.Provider == CiEnvironment.AzureDevOps && reader.ArtifactName is not null) || PublishedFolderDownload(reader) is not null;

    /// <summary>What a download of several reports means for the query after the block, and for a data file found by name.</summary>
    private static string SeveralInTheDownload(RunSummary summary, string folder) =>
        summary.QueryTarget is { } file
            ? $"The `find` takes the first `{OneLine(Path.GetFileName(file))}` in the download: if the run uploaded several (a matrix, or shards), `find {folder} -name {PatternWord(Path.GetFileName(file))}` lists them, and a query takes the path of the one you mean."
            : "If the run uploaded several reports (a matrix, or shards beside their merge), the query lists them with their counts: pass the one you mean.";

    /// <summary><c>gh</c>'s <c>--repo</c>: <c>owner/name</c> on github.com, <c>HOST/owner/name</c> on GitHub Enterprise.</summary>
    private static string RepositoryArgument(CiReader reader) =>
        reader.Host is { } host ? $"{host}/{reader.Repository}" : reader.Repository ?? "";

    /// <summary>
    /// What a query on a download is handed: the folder, whose lookup finds a <c>TestRunReport.json</c> anywhere in
    /// it, or, for a data file named otherwise, that file as <c>find</c> finds it outside the earlier runs and the
    /// baseline, which a folder lookup skips too (ReportFolders.IsReserved).
    /// </summary>
    private static string DownloadTarget(RunSummary summary, string folder) =>
        summary.QueryTarget is { } file
            ? $"\"$(find {folder} -name {PatternWord(Path.GetFileName(file))} -not -path '*/runs/*' -not -path '*/baseline/*' -print -quit)\""
            : folder;

    /// <summary>
    /// The reports folder as a person finds it: relative to the workspace, with <c>/</c>, when it lies inside it; null
    /// when it is the workspace itself, which a person knows as <see cref="CheckoutRoot"/> (a merge run in the
    /// repository's own folder said it wrote its reports to <c>.</c>); otherwise, or with no workspace, as written.
    /// </summary>
    private static string? FolderForPeople(string directory, string? workspace) =>
        workspace is null ? directory
        : Relative(workspace, directory) is { } relative ? (relative == "." ? null : relative)
        : directory;

    /// <summary>How the people part and the annotation name a reports folder that is the workspace itself.</summary>
    private const string CheckoutRoot = "the root of the checkout";

    /// <summary><paramref name="path"/> relative to <paramref name="root"/> with <c>/</c>, or null when it is not inside it.</summary>
    private static string? Relative(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative))
                return null;
            var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            return first == ".." ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// A URL as a markdown link's target. A space ends the target and a parenthesis can close it, and Azure DevOps'
    /// build URL carries <c>SYSTEM_TEAMPROJECT</c> as the agent gives it (<see cref="CiMetadataDetector"/>), so a project
    /// called "My Project" broke the link.
    /// </summary>
    private static string LinkTarget(string url) =>
        OneLine(url).Replace(" ", "%20", StringComparison.Ordinal).Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);

    /// <summary>
    /// The file <c>kronikol query</c> has to be handed when the directory would not find it, or null when the
    /// directory is enough: a directory lookup finds a <c>TestRunReport.json</c> and nothing else, and a merge, or a
    /// run with a custom <see cref="ReportConfigurationOptions.HtmlTestRunReportFileName"/>, names its data file
    /// otherwise (plans/CI_SUMMARY_AUDIENCE_PLAN.md F11).
    /// </summary>
    internal static string? QueryTargetFor(string? dataFilePath)
    {
        if (dataFilePath is null)
            return null;

        var full = Path.GetFullPath(dataFilePath);
        return Path.GetFileName(full) is var name
               && (string.Equals(name, "TestRunReport.json", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "TestRunReport.krkl", StringComparison.OrdinalIgnoreCase))
            ? null
            : full;
    }

    /// <summary>
    /// A run-derived string flattened onto the one line that holds it.
    ///
    /// <para>The pointer is a one-line-per-thing channel — <see cref="Write(RunSummary, CiEnvironment, Action{string})"/> splits on newlines and
    /// writes one line per call — and three of the strings in it come from the run rather than from
    /// Kronikol: the reports directory, and every failing feature and scenario name. Producers take those
    /// names from feature files, theory arguments and parameterised titles, so a line ending in one is a
    /// line the pointer never composed. On GitHub Actions that is not cosmetic: a line the run controls,
    /// starting at column zero, is a workflow command, and a scenario named <c>"a\n::error::x"</c> emits a
    /// failing annotation attributed to Kronikol. A bare CR does not even split the string — it returns
    /// the cursor to column zero and overwrites what was already printed.</para>
    ///
    /// <para><see cref="string.ReplaceLineEndings(string)"/> rather than a <c>\r\n</c> replace, because it
    /// knows the whole set: CR, LF, CRLF, FF, VT, NEL and the two Unicode separators. Nothing is dropped —
    /// each becomes a space, so the name is still reported in full.</para>
    ///
    /// <para>A line start is not the only place a command is read. GitHub's runner still reads its older
    /// <c>##[command]</c> form anywhere in a line, and the Azure Pipelines agent reads <c>##vso[</c> anywhere in a
    /// line, in any case (<c>##vso[task.setvariable]</c> sets a pipeline variable). A space after the first
    /// <c>#</c> of each leaves the name readable and no longer a command.</para>
    /// </summary>
    private static string OneLine(string? text) => BreakLoggingCommands((text ?? "").ReplaceLineEndings(" "));

    private static readonly string[] LoggingCommandMarkers = ["##[", "##vso["];

    private static string BreakLoggingCommands(string text)
    {
        foreach (var marker in LoggingCommandMarkers)
        {
            for (var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(marker, at + 2, StringComparison.OrdinalIgnoreCase))
                text = text.Insert(at + 1, " ");
        }

        return text;
    }

    /// <summary>
    /// A path as an argument on a command line the reader is meant to copy. Every one of these lines ends
    /// in a command, and an unquoted path with a space in it is a command that fails — which is the whole
    /// value of printing it. <c>C:\Program Files</c> and a CI workspace under a user's Documents are not
    /// exotic.
    ///
    /// <para>Quoted only when it needs to be: quoting every path would make every pointer harder to read
    /// for the one in a hundred that has a space. Double quotes, because the pointer is pasted into any shell
    /// and cmd takes no other; a Windows path cannot contain a quote at all, so the shells that spell that
    /// escape differently never see one. The CI section's <c>```bash</c> block declares bash, which reads a
    /// backslash or a <c>$</c> inside double quotes, so its words take <see cref="ShellWord"/>.</para>
    /// </summary>
    private static string Quote(string? path)
    {
        var flat = OneLine(path);
        return flat.Contains(' ', StringComparison.Ordinal) || flat.Contains('"', StringComparison.Ordinal)
            ? "\"" + flat.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : flat;
    }

    /// <summary>
    /// One word of the CI section's <c>```bash</c> block, as bash reads it. <see cref="Quote"/> serves the console,
    /// where cmd takes double quotes and nothing else; inside them bash still reads a backslash, a <c>$</c> and a
    /// backtick, so a Windows folder lost its separators there (<c>C:UsersmeReports</c>) and a <c>$</c> expanded.
    /// Single quotes keep every character but the single quote itself, which is closed, escaped and reopened.
    /// </summary>
    internal static string ShellWord(string? text)
    {
        var flat = OneLine(text);
        return flat.Length > 0 && flat.All(c => char.IsAsciiLetterOrDigit(c) || "_./:=@%+,-".Contains(c, StringComparison.Ordinal))
            ? flat
            : "'" + flat.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// A <c>find -name</c> pattern that matches <paramref name="name"/> and nothing else: <c>-name</c> reads <c>*</c>,
    /// <c>?</c> and <c>[</c> as a pattern, so <c>Report[linux].json</c> matched nothing and the query was handed <c>""</c>.
    /// </summary>
    private static string PatternWord(string name)
    {
        var escaped = new StringBuilder(name.Length + 4);
        foreach (var c in OneLine(name))
        {
            if (c is '*' or '?' or '[' or ']' or '\\')
                escaped.Append('\\');
            escaped.Append(c);
        }

        return ShellWord(escaped.ToString());
    }

    private static bool HasDigest(RunSummary summary) =>
        summary.Files.Any(f => string.Equals(f.Name, DigestFileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The argument every `kronikol query` line hands over: the directory, unless the run said that would not find its file.</summary>
    private static string QueryTarget(RunSummary summary) => Quote(summary.QueryTarget ?? summary.Directory);

    /// <summary>
    /// The data file this run wrote, by name, in whatever format, or null when it wrote none: <c>TestRunReport.json</c>
    /// unless the run named it otherwise, as a merge does after its <c>-o</c>. The "never open" rule has to name the
    /// file that exists, and never one that does not.
    /// </summary>
    private static string? DataFile(RunSummary summary) =>
        summary.Files.FirstOrDefault(f => IsDataFile(f.Name))?.Name;

    /// <summary>The data file when <c>kronikol query</c> reads it: JSON, or the compressed <c>.krkl</c>; null otherwise.</summary>
    private static string? QueryableDataFile(RunSummary summary) =>
        DataFile(summary) is { } name && (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || IsCompressed(name)) ? name : null;

    private static bool IsCompressed(string? name) => name?.EndsWith(".krkl", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>What the pointer tells an agent never to open: the data file, or the report when the run wrote no data file.</summary>
    private static string? NeverOpen(RunSummary summary) => DataFile(summary) ?? HtmlFileName(summary);

    /// <summary>The report this run wrote, never the labs page beside it, or null when it wrote none.</summary>
    private static string? HtmlFileName(RunSummary summary) =>
        summary.Files.FirstOrDefault(f => f.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                                          && !f.Name.EndsWith(LabsReportGenerator.Suffix, StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>
    /// A file name, plus its size when the size is the point: the data file is the one an agent is tempted
    /// to read and the one that makes reading impossible, so its megabytes are stated at the moment it is
    /// written rather than discovered later.
    /// </summary>
    private static string Describe(RunSummaryFile file) =>
        // A compressed file's size on disk says nothing about what reading it would cost, so it gets none (4.6).
        IsDataFile(file.Name) && !IsCompressed(file.Name) ? $"{file.Name} {Size(file.Bytes)}" : file.Name;

    private static bool IsDataFile(string name) =>
        !name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith(".schema.xsd", StringComparison.OrdinalIgnoreCase)
        && (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".krkl", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));

    // With a point on every machine, as the rest of the line's English is: a comma-decimal culture wrote "1,5 KB".
    internal static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");
}
