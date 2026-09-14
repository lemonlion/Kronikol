namespace Kronikol.Reports.Merge;

/// <summary>
/// Everything a finished run writes beside its report, written beside a merged one.
/// </summary>
/// <remarks>
/// <para>A test run ends in a tail of outputs that are not the report: the failures digest, the
/// instruction files, the schema, the CI job summary, the console pointer, the artifact publish. A merge
/// rendered the HTML and the data file and stopped - so a sharded suite, the one shape of run that is
/// always on CI, had none of the things the rest of this library builds for CI. No <c>Failures.md</c> to
/// read first, no <c>CLAUDE.md</c> to load, no pointer in the job log saying where the merged report went
/// or how big its data file is.</para>
///
/// <para>This is a separate writer rather than the run's own tail extracted, on a measured obstacle: that
/// tail reads process-ambient state - the in-process request log, the memoised diagram cache, the
/// diagnostics scope - and a merged report has none of it. Sharing the path would regenerate a merged
/// report's diagrams from an empty log and lose the summed component counts. Everything here arrives as a
/// parameter, which is also what lets a test drive the CI branches without a CI.</para>
///
/// <para>Each output is written on its own, so one that fails costs one file and not the tail; the run's
/// own outputs are isolated the same way.</para>
/// </remarks>
public static class MergedRunOutputs
{
    /// <summary>The report-shaped files an artifact publish picks up from the output directory.</summary>
    private static readonly string[] PublishedExtensions = [".html", ".yml", ".md", ".json", ".jsonl", ".xml"];

    /// <summary>
    /// Writes the run outputs beside <paramref name="htmlPath"/>, then says where it all is. Returns the
    /// names of the files written, so a caller can tell a partial tail from a full one.
    /// </summary>
    /// <param name="report">The merged report.</param>
    /// <param name="htmlPath">Where the merged HTML was written. Every other file lands in its directory, and takes its base name wherever a name is derived from the report's.</param>
    /// <param name="dataFilePath">Where the merged data file was written, or null when it was not - in which case there is nothing for a schema to describe and nothing for <c>kronikol query</c> to open.</param>
    /// <param name="options">The switches a run honours for the same outputs: <see cref="ReportConfigurationOptions.GenerateFailuresDigest"/>, <see cref="ReportConfigurationOptions.WriteAgentInstructions"/>, <see cref="ReportConfigurationOptions.GenerateTestRunReportSchema"/>, <see cref="ReportConfigurationOptions.WriteCiSummary"/>, <see cref="ReportConfigurationOptions.WriteCiDebugSection"/>, <see cref="ReportConfigurationOptions.PublishCiArtifacts"/> and <see cref="ReportConfigurationOptions.WriteRunSummaryToConsole"/>.</param>
    /// <param name="output">Where the pointer and any CI workflow commands go: the merge's stdout, which a CI step owns in a way the library under a test runner never does.</param>
    /// <param name="error">Where an output that could not be written is reported.</param>
    /// <param name="getEnvironmentVariable">The environment the CI detection and the CI writers read; the process's own when null.</param>
    /// <param name="history">The merged run read against a cross-run ledger, when <c>merge --history</c> named one: the digest works through regressions first and carries a history line per failure, and the pointer says what changed.</param>
    public static IReadOnlyCollection<string> Write(MergeableReport report, string htmlPath, string? dataFilePath,
        ReportConfigurationOptions options, TextWriter output, TextWriter error, Func<string, string?>? getEnvironmentVariable = null,
        History.HistoryVerdicts? history = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var getEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var html = Path.GetFullPath(htmlPath);
        var directory = Path.GetDirectoryName(html) ?? Directory.GetCurrentDirectory();
        var baseName = Path.GetFileNameWithoutExtension(html);
        var written = new List<string>();

        void Attempt(string name, Action write)
        {
            try
            {
                write();
                written.Add(name);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error.WriteLine($"Could not write {name}: {exception.Message}");
            }
        }

        if (options.GenerateFailuresDigest)
        {
            // The step paths are carried, not derived: the derivation walks diagram markers the shards no
            // longer have, and without them every call in the digest was attributed to the scenario rather
            // than to the step that made it.
            var digest = new Lazy<FailuresDigest>(() => FailuresDigestGenerator.Generate(
                report.Features,
                report.Interactions.Length > 0 ? report.Interactions : null,
                baseName,
                report.KronikolVersion,
                report.Diagnostics,
                report.Suite,
                report.StepPaths,
                history: history));

            Attempt(ReportGenerator.FailuresDigestFileName,
                () => File.WriteAllText(Path.Combine(directory, ReportGenerator.FailuresDigestFileName), digest.Value.Markdown));
            Attempt(ReportGenerator.FailuresDigestJsonlFileName,
                () => File.WriteAllText(Path.Combine(directory, ReportGenerator.FailuresDigestJsonlFileName), digest.Value.Jsonl));
        }

        if (options.WriteAgentInstructions)
        {
            var block = AgentInstructionsBlock.Wrap(AgentInstructionsGenerator.Build(baseName));
            foreach (var name in new[] { AgentInstructionsGenerator.ClaudeFileName, AgentInstructionsGenerator.AgentsFileName })
                Attempt(name, () => WriteInstructionFile(Path.Combine(directory, name), block));
        }

        if (options.GenerateTestRunReportSchema && dataFilePath is not null)
        {
            var schemaName = $"{baseName}.schema.json";
            Attempt(schemaName, () =>
            {
                using var scope = ReportGenerator.ScopeReportsDirectory(directory);
                ReportGenerator.GenerateTestRunReportSchema(schemaName, DataFormat.Json);
            });
        }

        // Gathered from what THIS merge wrote, as the run's own pointer is: a file the previous merge left
        // behind exists, and naming it would hand the reader another run's bytes under this run's heading.
        var candidates = new List<string> { Path.GetFileName(html) };
        if (dataFilePath is not null)
            candidates.Add(Path.GetFileName(dataFilePath));
        if (written.Contains(ReportGenerator.FailuresDigestFileName))
            candidates.Add(ReportGenerator.FailuresDigestFileName);

        var summary = RunSummaryConsoleWriter.Summarise(
            report.Features,
            directory,
            candidates,
            agentInstructionsWritten: written.Contains(AgentInstructionsGenerator.ClaudeFileName),
            suite: report.Suite,
            queryTarget: QueryTarget(dataFilePath),
            history: history is not null && (history.HasAnything || report.Features.Any(f => (f.Scenarios ?? []).Any(s => s.Result == ExecutionResult.Failed)))
                ? "history: " + History.HistorySummary.Line(history)
                : null);

        var ci = CiEnvironmentDetector.Detect(getEnv);

        if (options.WriteCiSummary)
        {
            Attempt("CiSummary.md", () =>
            {
                // The merged report carries each scenario's diagram source whole, so the summary shows the
                // one form rather than a truncated preview beside a full one.
                var markdown = CiSummaryGenerator.GenerateMarkdown(
                        report.Features, report.Diagrams, report.Diagrams, report.StartTime, report.EndTime,
                        options.MaxCiSummaryDiagrams, options.DiagramFormat, options.PlantUmlServerBaseUrl, options.LocalDiagramRenderer)
                    + RunSummaryConsoleWriter.BuildCiSummarySection(summary);

                File.WriteAllText(Path.Combine(directory, "CiSummary.md"), markdown);
                CiSummaryWriter.Write(markdown, ci, getEnv, File.AppendAllText, output.WriteLine);
            });
        }
        else if (options.WriteCiDebugSection && summary.Failures.Count > 0 && ci != CiEnvironment.None)
        {
            // A failing merge on CI says how to debug itself on both channels, exactly as a failing run
            // does: the job summary survives the runners that swallow stdout, and the log is what
            // `gh run view --log` shows.
            var section = RunSummaryConsoleWriter.BuildCiSummarySection(summary);
            output.WriteLine(section);
            CiSummaryWriter.Write(section, ci, getEnv, File.AppendAllText, output.WriteLine);
        }

        if (options.PublishCiArtifacts)
        {
            var files = Directory.GetFiles(directory)
                .Where(f => PublishedExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
            CiArtifactPublisher.Publish(files, ci, options.CiArtifactName, options.CiArtifactRetentionDays,
                getEnv, File.AppendAllText, output.WriteLine, File.Exists);
        }

        // Last, so it is the final thing the merge says.
        if (options.WriteRunSummaryToConsole)
            RunSummaryConsoleWriter.Write(summary, ci, output.WriteLine);

        return written;
    }

    /// <summary>
    /// The file <c>kronikol query</c> has to be handed, when the directory would not find it: a directory
    /// lookup finds <c>TestRunReport.json</c> and nothing else, and a merge names its data file after
    /// <c>-o</c>.
    /// </summary>
    private static string? QueryTarget(string? dataFilePath)
    {
        if (dataFilePath is null)
            return null;

        var full = Path.GetFullPath(dataFilePath);
        return string.Equals(Path.GetFileName(full), "TestRunReport.json", StringComparison.OrdinalIgnoreCase) ? null : full;
    }

    /// <summary>
    /// The protocol the run uses for the same two files: the block is spliced into a file that already
    /// exists, and a file the protocol cannot read safely - a block opened and never closed, two blocks -
    /// is left exactly as it is and reported, because every repair for those is a guess and a wrong guess
    /// deletes text nobody can get back.
    /// </summary>
    private static void WriteInstructionFile(string path, string block)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, block + "\n");
            return;
        }

        var existing = File.ReadAllText(path);
        if (AgentInstructionsBlock.Merge(existing, block, out var problem) is { } merged)
        {
            File.WriteAllText(path, merged);
            return;
        }

        throw new InvalidOperationException($"left it alone - it {problem}");
    }
}
