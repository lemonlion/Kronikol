namespace Kronikol.Reports;

/// <summary>
/// Hands generated report files to the CI's artifact store: on Azure DevOps an <c>##vso[artifact.upload]</c>
/// logging command per file, written to stdout; on GitHub Actions, which has no such command, the reports
/// folder and retention written to <c>$GITHUB_OUTPUT</c> for the job's own upload step.
/// </summary>
public static class CiArtifactPublisher
{
    public static void Publish(
        string[] reportFilePaths,
        CiEnvironment environment,
        string artifactName = "TestReports",
        int retentionDays = 1)
        => Publish(reportFilePaths, environment, artifactName, retentionDays,
            Environment.GetEnvironmentVariable, File.AppendAllText, Console.WriteLine, File.Exists);

    private static readonly string[] PublishedExtensions = [".html", ".yml", ".md", ".json", ".jsonl", ".xml", ".krkl"];

    /// <summary>
    /// The files at the top of a reports directory that a run or a merge hands the upload: the report outputs
    /// by extension, and <c>query.cs</c> by name. An Azure DevOps upload names every file, so a file this
    /// leaves out is missing from the artifact there, while GitHub Actions uploads the whole directory. Any
    /// other <c>.cs</c> file in the folder is somebody else's.
    /// </summary>
    internal static string[] ReportFiles(string reportsDirectory) =>
        Directory.GetFiles(reportsDirectory)
            .Where(f => PublishedExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                || Path.GetFileName(f) == QueryScriptGenerator.FileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every file of the runs kept under <c>runs/</c>, with the folder it sits in relative to the reports
    /// directory (<c>runs/gh_7_1</c>, <c>runs/gh_7_1/attachments</c>). Without them a retried job's failing
    /// attempt stays on a runner nobody can reach, which is the evidence the folder exists to keep.
    /// </summary>
    internal static IReadOnlyList<(string Path, string Folder)> RetainedFiles(string reportsDirectory) =>
        ReportFolders.RetainedRuns(reportsDirectory)
            .SelectMany(run => Directory.EnumerateFiles(run.Directory, "*", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (path, Path.GetRelativePath(reportsDirectory, Path.GetDirectoryName(path)!).Replace('\\', '/')))
            .ToArray();

    /// <summary>
    /// The report's own attachment copies, from the links the report holds (<c>attachments/shot.png</c>), each with the
    /// folder it sits in relative to the reports directory, as <see cref="RetainedFiles"/> gives a kept run's. An Azure
    /// DevOps upload names every file, and it named neither these nor any other file under <c>attachments/</c>, so every
    /// copied screenshot was a dead link in the artifact (#148 F7); GitHub Actions uploads the whole folder. A link
    /// elsewhere, or a path the report does not hold, names no file of the report's and is left out.
    /// </summary>
    internal static IReadOnlyList<(string Path, string Folder)> AttachmentFiles(string reportsDirectory, IEnumerable<string> links) =>
        links.Where(AttachmentPlacement.IsReportRelative)
            .Select(link => link.Replace('\\', '/'))
            .Distinct(StringComparer.FromComparison(AttachmentPlacement.PathComparison))
            .Select(link => (Path.GetFullPath(Path.Combine(reportsDirectory, link)), link[..link.LastIndexOf('/')]))
            .OrderBy(file => file.Item1, StringComparer.Ordinal)
            .ToArray();

    internal static void Publish(
        string[] reportFilePaths,
        CiEnvironment environment,
        string artifactName,
        int retentionDays,
        Func<string, string?> getEnvVar,
        Action<string, string> appendFile,
        Action<string> writeLine,
        Func<string, bool> fileExists,
        IReadOnlyList<(string Path, string Folder)>? retained = null)
    {
        switch (environment)
        {
            case CiEnvironment.AzureDevOps:
                foreach (var path in reportFilePaths)
                {
                    if (!fileExists(path)) continue;
                    writeLine($"##vso[artifact.upload containerfolder={artifactName};artifactname={artifactName}]{path}");
                }

                // The runs kept under runs/ (an earlier attempt of this same run, on CI), and the report's own
                // attachment copies: each file under the folder it has on disk, so the artifact opens with --run
                // and links its screenshots exactly as the directory does. GitHub Actions is handed the directory,
                // and uploads them with it.
                foreach (var (path, folder) in retained ?? [])
                {
                    if (!fileExists(path)) continue;
                    writeLine($"##vso[artifact.upload containerfolder={artifactName}/{folder};artifactname={artifactName}]{path}");
                }
                break;

            case CiEnvironment.GitHubActions:
            {
                var outputPath = getEnvVar("GITHUB_OUTPUT");
                if (string.IsNullOrEmpty(outputPath)) return;
                var reportsDir = reportFilePaths.Length > 0
                    ? Path.GetDirectoryName(reportFilePaths[0]) ?? ""
                    : "";
                appendFile(outputPath, $"reports-path={reportsDir}\n");
                appendFile(outputPath, $"reports-retention-days={retentionDays}\n");
                break;
            }

            case CiEnvironment.None:
            default:
                break;
        }
    }
}
