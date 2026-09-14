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
/// <c>TestRunReport.json</c> and nothing else, and a merge names its data file after its <c>-o</c> - so a
/// pointer that printed the directory for <c>Combined.json</c> printed a command that fails. Null means
/// the directory is enough.
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
    string? History = null);

/// <summary>
/// The last thing a run says: where the reports are, how big the data file is, what failed, and the one
/// command that explains it.
///
/// <para>Report generation has never been silent — <see cref="ReportDiagnostics.Analyse"/>'s lines print on
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
/// <c>CLAUDE.md</c>/<c>AGENTS.md</c> — plus the CI job summary, which is written to a file descriptor
/// rather than to stdout and therefore survives. The pointer is free where it lands and harmless where it
/// does not.</para>
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

        if (summary.Failures.Count > 0)
        {
            var target = QueryTarget(summary);
            text.Append($"  {summary.Failures.Count} failed — kronikol query failures {target}\n");
            foreach (var failure in summary.Failures.Take(MaxFailureLines))
                text.Append($"    {OneLine(failure.StableId)}  {OneLine(failure.FeatureName)} › {OneLine(failure.ScenarioName)}\n");

            var remaining = summary.Failures.Count - MaxFailureLines;
            if (remaining > 0)
                text.Append(HasDigest(summary)
                    ? $"    … and {remaining} more (see {DigestFileName})\n"
                    : $"    … and {remaining} more (kronikol query failures {target})\n");

            // The bait for the nested instruction file: an agent that reads anything in this directory
            // loads the CLAUDE.md sitting beside it, and that file is what teaches it never to open the
            // JSON. Only printed when something failed — a green run has nothing to debug, and a pointer
            // that speaks on every run is a pointer people learn to skip.
            text.Append(summary.AgentInstructionsWritten
                ? $"  agents: read {Quote(Path.Combine(summary.Directory, "CLAUDE.md"))} first; never open {DataFileName(summary)}\n"
                : $"  agents: run kronikol query --help; never open {DataFileName(summary)}\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// The GitHub Actions annotation for a failing run, or null when nothing failed or the run is not on
    /// GitHub. One line, same content rules as the pointer.
    /// </summary>
    public static string? BuildGitHubNotice(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Failures.Count == 0)
            return null;

        var digest = HasDigest(summary) ? $" · {DigestFileName}" : "";
        return $"::notice title=Kronikol::{summary.Failures.Count} failed of {summary.ScenarioCount} scenarios "
               + $"— kronikol query failures {QueryTarget(summary)}{digest}";
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
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(writeAnnotation);

        foreach (var line in Build(summary).TrimEnd('\n').Split('\n'))
            write(line);

        if (environment == CiEnvironment.GitHubActions && BuildGitHubNotice(summary) is { } notice)
            writeAnnotation(notice);
    }

    /// <summary>
    /// The "Debug this run" section appended to <c>CiSummary.md</c>: the CI job summary is written to a
    /// file descriptor rather than to stdout, so it survives the runners that swallow console output.
    /// </summary>
    public static string BuildCiSummarySection(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var target = QueryTarget(summary);
        var markdown = new StringBuilder();
        markdown.Append("## Debug this run\n\n");
        markdown.Append($"Do not open `{DataFileName(summary)}` or `{HtmlFileName(summary)}` — a real report reaches megabytes, ");
        markdown.Append("and a single embedded diagram can be larger than a context window. Query it instead:\n\n");
        markdown.Append("```bash\ndotnet tool install -g Kronikol.Tool\n");
        markdown.Append($"kronikol query summary {target}\n");
        if (summary.Failures.Count > 0)
            markdown.Append($"kronikol query failures {target}\n");
        markdown.Append("```\n\n");

        if (summary.History is { Length: > 0 } history)
            markdown.Append(OneLine(history)).Append("\n\n");

        if (HasDigest(summary))
            markdown.Append($"The artifact also carries `{DigestFileName}` — every failure in context, ready to read.\n\n");

        return markdown.ToString();
    }

    /// <summary>
    /// A run-derived string flattened onto the one line that holds it.
    ///
    /// <para>The pointer is a one-line-per-thing channel — <see cref="Write"/> splits on newlines and
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
    /// </summary>
    private static string OneLine(string? text) => (text ?? "").ReplaceLineEndings(" ");

    /// <summary>
    /// A path as an argument on a command line the reader is meant to copy. Every one of these lines ends
    /// in a command, and an unquoted path with a space in it is a command that fails — which is the whole
    /// value of printing it. <c>C:\Program Files</c> and a CI workspace under a user's Documents are not
    /// exotic.
    ///
    /// <para>Quoted only when it needs to be: quoting every path would make every pointer harder to read
    /// for the one in a hundred that has a space. The escape targets POSIX shells, which is what the
    /// <c>```bash</c> block in the CI summary declares and what every CI runner uses; a Windows path
    /// cannot contain a quote at all, so the shells that spell that escape differently never see one.</para>
    /// </summary>
    private static string Quote(string? path)
    {
        var flat = OneLine(path);
        return flat.Contains(' ', StringComparison.Ordinal) || flat.Contains('"', StringComparison.Ordinal)
            ? "\"" + flat.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : flat;
    }

    private static bool HasDigest(RunSummary summary) =>
        summary.Files.Any(f => string.Equals(f.Name, DigestFileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The argument every `kronikol query` line hands over: the directory, unless the run said that would not find its file.</summary>
    private static string QueryTarget(RunSummary summary) => Quote(summary.QueryTarget ?? summary.Directory);

    /// <summary>
    /// The data file this run wrote, by name: <c>TestRunReport.json</c> unless the run named it otherwise,
    /// as a merge does after its <c>-o</c>. The "never open" rule has to name the file that exists.
    /// </summary>
    private static string DataFileName(RunSummary summary) =>
        summary.Files.FirstOrDefault(f => IsDataFile(f.Name))?.Name ?? "TestRunReport.json";

    private static string HtmlFileName(RunSummary summary) =>
        summary.Files.FirstOrDefault(f => f.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))?.Name ?? "TestRunReport.html";

    /// <summary>
    /// A file name, plus its size when the size is the point: the data file is the one an agent is tempted
    /// to read and the one that makes reading impossible, so its megabytes are stated at the moment it is
    /// written rather than discovered later.
    /// </summary>
    private static string Describe(RunSummaryFile file) =>
        IsDataFile(file.Name) ? $"{file.Name} {Size(file.Bytes)}" : file.Name;

    private static bool IsDataFile(string name) =>
        !name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith(".schema.xsd", StringComparison.OrdinalIgnoreCase)
        && (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));

    internal static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB"
        : $"{bytes / (1024.0 * 1024.0):0.#} MB";
}
