namespace Kronikol.Reports;

/// <summary>
/// Publishes generated report files as CI artifacts using platform-specific mechanisms
/// (GitHub Actions artifact upload commands or Azure DevOps artifact publishing).
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

                // The runs kept under runs/ (an earlier attempt of this same run, on CI): each file under
                // the folder it has on disk, so the artifact opens with --run exactly as the directory
                // does. GitHub Actions is handed the directory, and uploads them with it.
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
