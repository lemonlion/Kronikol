using Kronikol.Reports;

namespace Kronikol.Tests.Compression;

/// <summary>
/// What a run says about a compressed data file (plans/REPORT_FORMAT_PLAN.md 4.6, F11 and F26; section 5, R3 item 8): the
/// run-end pointer names it as queryable, without a size on disk that says nothing about reading it, the CI summary's
/// rule gives the new reason, a query is pointed at its folder as for <c>TestRunReport.json</c>, and Azure DevOps
/// uploads it.
/// </summary>
public class KrklRunOutputTests
{
    private static readonly CiReader GitHub = CiReader.Detect(name => name switch
    {
        "GITHUB_ACTIONS" => "true",
        "GITHUB_SERVER_URL" => "https://github.com",
        "GITHUB_REPOSITORY" => "acme/shop",
        "GITHUB_RUN_ID" => "123456789",
        "GITHUB_RUN_ATTEMPT" => "1",
        "GITHUB_WORKFLOW" => "CI",
        _ => null,
    }, new ReportConfigurationOptions());

    private static RunSummary Summary(string data) =>
        new("/home/runner/work/shop/shop/tests/bin/Release/net10.0/Reports",
            [new RunSummaryFile("TestRunReport.html", 1_200_000), new RunSummaryFile(data, 50_540_000), new RunSummaryFile("Failures.md", 12_000)],
            ScenarioCount: 42,
            Failures: [new RunSummaryFailure("00000000000000a1", "Checkout", "Pay")],
            AgentInstructionsWritten: true);

    [Fact]
    public void The_pointer_names_a_krkl_without_a_size_and_queries_it_as_it_queries_the_json()
    {
        var krkl = RunSummaryConsoleWriter.Build(Summary("TestRunReport.krkl"));
        var json = RunSummaryConsoleWriter.Build(Summary("TestRunReport.json"));

        Assert.Contains("TestRunReport.krkl", krkl, StringComparison.Ordinal);
        Assert.DoesNotContain("TestRunReport.krkl 4", krkl, StringComparison.Ordinal);
        Assert.Contains("TestRunReport.json 4", json, StringComparison.Ordinal);
        // Every command the pointer prints for the JSON, it prints for the compressed file.
        Assert.Equal(Commands(json), Commands(krkl));
        Assert.NotEmpty(Commands(krkl));
    }

    [Theory]
    [InlineData("TestRunReport.krkl", true)]
    [InlineData("TestRunReport.json", true)]
    [InlineData("Shard.krkl", false)]
    public void A_query_is_pointed_at_the_folder_of_a_data_file_with_the_default_name(string name, bool folder)
    {
        var target = RunSummaryConsoleWriter.QueryTargetFor(Path.Combine(Path.GetTempPath(), name));

        Assert.Equal(folder, target is null);
    }

    [Fact]
    public void The_CI_summary_s_rule_says_a_compressed_data_file_needs_the_tool()
    {
        var krkl = RunSummaryConsoleWriter.BuildCiSummarySection(Summary("TestRunReport.krkl"), GitHub);
        var json = RunSummaryConsoleWriter.BuildCiSummarySection(Summary("TestRunReport.json"), GitHub);

        Assert.Contains("compressed: use `kronikol query`", krkl, StringComparison.Ordinal);
        Assert.DoesNotContain("compressed", json, StringComparison.Ordinal);
        Assert.Contains("`TestRunReport.krkl`", krkl, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_DevOps_uploads_a_krkl()
    {
        // The files Azure DevOps is told to upload one by one; GitHub Actions takes the whole folder.
        var directory = Directory.CreateTempSubdirectory("kronikol-krkl-publish").FullName;
        try
        {
            foreach (var name in new[] { "TestRunReport.html", "TestRunReport.krkl", "Failures.md" })
                File.WriteAllText(Path.Combine(directory, name), "x");

            var files = CiArtifactPublisher.ReportFiles(directory).Select(Path.GetFileName).ToArray();

            Assert.Contains("TestRunReport.krkl", files);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] Commands(string pointer) =>
        [.. pointer.Split('\n').Where(line => line.Contains("kronikol query", StringComparison.Ordinal)).Select(line => line.Trim())];
}
