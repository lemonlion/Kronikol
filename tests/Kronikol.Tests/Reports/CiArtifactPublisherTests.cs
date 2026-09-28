using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class CiArtifactPublisherTests
{
    /// <summary>
    /// What a run and a merge hand the upload: the report outputs by extension, and <c>query.cs</c> by name.
    /// Picked by extension alone, <c>query.cs</c> never reached an Azure DevOps artifact, where every file is
    /// named to the agent, while GitHub Actions uploads the whole directory and so carried it. Only that name:
    /// any other <c>.cs</c> file in the folder is somebody else's.
    /// </summary>
    [Fact]
    public void ReportFiles_takes_the_report_outputs_and_query_cs_and_nothing_else()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-artifact-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "runs"));
        try
        {
            foreach (var name in new[] { "TestRunReport.html", "TestRunReport.json", "Failures.jsonl", "CLAUDE.md", "query.cs", "Helper.cs", "notes.txt", "runs/inner.html" })
                File.WriteAllText(Path.Combine(dir, name), "x");

            var files = CiArtifactPublisher.ReportFiles(dir).Select(Path.GetFileName).ToArray();

            Assert.Equal(["CLAUDE.md", "Failures.jsonl", "TestRunReport.html", "TestRunReport.json", "query.cs"], files);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Publish_AzureDevOps_emits_vso_upload_for_each_file()
    {
        var lines = new List<string>();

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html", "/reports/Specs.yml"],
            CiEnvironment.AzureDevOps,
            artifactName: "TestReports",
            retentionDays: 1,
            getEnvVar: _ => null,
            appendFile: (_, _) => { },
            writeLine: line => lines.Add(line),
            fileExists: _ => true);

        Assert.Equal(2, lines.Count);
        Assert.Contains("##vso[artifact.upload containerfolder=TestReports;artifactname=TestReports]/reports/TestRunReport.html", lines[0]);
        Assert.Contains("##vso[artifact.upload containerfolder=TestReports;artifactname=TestReports]/reports/Specs.yml", lines[1]);
    }

    [Fact]
    public void Publish_AzureDevOps_uploads_a_kept_run_under_the_folder_it_has_on_disk()
    {
        // A retried job keeps its failing attempt under runs/. Left out of the artifact, the evidence
        // stays on a runner nobody can reach.
        var directory = Directory.CreateTempSubdirectory("kronikol-ado-retained").FullName;
        try
        {
            var kept = Path.Combine(directory, "runs", "gh_7_1");
            Directory.CreateDirectory(Path.Combine(kept, "attachments"));
            File.WriteAllText(Path.Combine(kept, RunManifest.FileName),
                """{"runManifestVersion":1,"run":"gh:7:1","at":"2026-09-18T10:16:11Z","suite":"Suite","scenarios":3,"failed":1,"files":["Failures.md"],"attachments":["attachments/shot.png"]}""");
            File.WriteAllText(Path.Combine(kept, "Failures.md"), "# Failures");
            File.WriteAllText(Path.Combine(kept, "attachments", "shot.png"), "png");
            Directory.CreateDirectory(Path.Combine(directory, "runs", ".incoming-gh_8_1"));
            File.WriteAllText(Path.Combine(directory, "runs", ".incoming-gh_8_1", "Failures.md"), "half a rotation");
            var lines = new List<string>();

            CiArtifactPublisher.Publish(
                [Path.Combine(directory, "TestRunReport.html")],
                CiEnvironment.AzureDevOps, "TestReports", 1, _ => null, (_, _) => { }, lines.Add, _ => true,
                CiArtifactPublisher.RetainedFiles(directory));

            Assert.Equal(4, lines.Count);
            Assert.StartsWith("##vso[artifact.upload containerfolder=TestReports;artifactname=TestReports]", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("##vso[artifact.upload containerfolder=TestReports/runs/gh_7_1;artifactname=TestReports]", StringComparison.Ordinal) && l.EndsWith("Failures.md", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("##vso[artifact.upload containerfolder=TestReports/runs/gh_7_1/attachments;artifactname=TestReports]", StringComparison.Ordinal) && l.EndsWith("shot.png", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains(".incoming-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Publish_AzureDevOps_uses_custom_artifact_name()
    {
        var lines = new List<string>();

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html"],
            CiEnvironment.AzureDevOps,
            artifactName: "MyReports",
            retentionDays: 1,
            getEnvVar: _ => null,
            appendFile: (_, _) => { },
            writeLine: line => lines.Add(line),
            fileExists: _ => true);

        Assert.Single(lines);
        Assert.Contains("artifactname=MyReports", lines[0]);
        Assert.Contains("containerfolder=MyReports", lines[0]);
    }

    [Fact]
    public void Publish_GitHubActions_writes_reports_path_to_github_output()
    {
        string? writtenPath = null;
        var writtenContents = new List<string>();

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html"],
            CiEnvironment.GitHubActions,
            artifactName: "TestReports",
            retentionDays: 1,
            getEnvVar: name => name == "GITHUB_OUTPUT" ? "/tmp/output" : null,
            appendFile: (path, content) => { writtenPath = path; writtenContents.Add(content); },
            writeLine: _ => { },
            fileExists: _ => true);

        Assert.Equal("/tmp/output", writtenPath);
        Assert.Contains(writtenContents, c => c.StartsWith("reports-path=") && c.TrimEnd('\n').EndsWith("reports"));
    }

    [Fact]
    public void Publish_GitHubActions_does_nothing_when_output_path_missing()
    {
        var called = false;

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html"],
            CiEnvironment.GitHubActions,
            artifactName: "TestReports",
            retentionDays: 1,
            getEnvVar: _ => null,
            appendFile: (_, _) => called = true,
            writeLine: _ => called = true,
            fileExists: _ => true);

        Assert.False(called);
    }

    [Fact]
    public void Publish_None_does_nothing()
    {
        var called = false;

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html"],
            CiEnvironment.None,
            artifactName: "TestReports",
            retentionDays: 1,
            getEnvVar: _ => null,
            appendFile: (_, _) => called = true,
            writeLine: _ => called = true,
            fileExists: _ => true);

        Assert.False(called);
    }

    [Fact]
    public void Publish_AzureDevOps_skips_files_that_do_not_exist()
    {
        var lines = new List<string>();

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html", "/reports/Missing.html"],
            CiEnvironment.AzureDevOps,
            artifactName: "TestReports",
            retentionDays: 1,
            getEnvVar: _ => null,
            appendFile: (_, _) => { },
            writeLine: line => lines.Add(line),
            fileExists: path => path == "/reports/TestRunReport.html");

        Assert.Single(lines);
        Assert.Contains("TestRunReport.html", lines[0]);
    }

    [Fact]
    public void Publish_GitHubActions_writes_retention_days_output()
    {
        var writtenContents = new List<string>();

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html"],
            CiEnvironment.GitHubActions,
            artifactName: "TestReports",
            retentionDays: 1,
            getEnvVar: name => name == "GITHUB_OUTPUT" ? "/tmp/output" : null,
            appendFile: (_, content) => writtenContents.Add(content),
            writeLine: _ => { },
            fileExists: _ => true);

        Assert.Contains(writtenContents, c => c.Contains("reports-retention-days=1"));
    }

    [Fact]
    public void Publish_GitHubActions_uses_custom_retention_days()
    {
        var writtenContents = new List<string>();

        CiArtifactPublisher.Publish(
            ["/reports/TestRunReport.html"],
            CiEnvironment.GitHubActions,
            artifactName: "TestReports",
            retentionDays: 7,
            getEnvVar: name => name == "GITHUB_OUTPUT" ? "/tmp/output" : null,
            appendFile: (_, content) => writtenContents.Add(content),
            writeLine: _ => { },
            fileExists: _ => true);

        Assert.Contains(writtenContents, c => c.Contains("reports-retention-days=7"));
    }
}
