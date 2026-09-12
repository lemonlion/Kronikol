using System.Text.Json;
using System.Xml.Linq;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Run identity in the standard data file (LLM_FRIENDLY_PLAN M2.3): who built it, from which commit,
/// on what. Until now `ciMetadata` reached only the mergeable superset and the HTML summary table, so a
/// `TestRunReport.json` downloaded from an artifact store could not say which commit it belonged to —
/// which is precisely what a baseline index has to key on. Both keys are emitted <em>unconditionally</em>:
/// a shape that appears only on CI is a shape no consumer can rely on, so off CI the same key set comes
/// back with <c>provider: "None"</c> and nulls beneath it.
/// </summary>
public class RunIdentityDataTests
{
    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it charges", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static readonly CiMetadata OnGitHub = new(
        CiEnvironment.GitHubActions, "412", "main", "abc1234def5678",
        "https://github.com/acme/shop/actions/runs/99", "acme/shop", "99", "2");

    private static string Write(DataFormat format, CiMetadata? ciMetadata = null)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        var path = ReportGenerator.GenerateTestRunReportData(
            Features(), DateTime.UtcNow, DateTime.UtcNow,
            $"RunIdentity_{Guid.NewGuid():N}.{extension}", format, ciMetadata: ciMetadata);
        return File.ReadAllText(path);
    }

    // ─── JSON ───────────────────────────────────────────────────

    private static readonly string[] CiKeys =
        ["provider", "buildNumber", "branch", "commitSha", "pipelineUrl", "repository", "runId", "runAttempt"];

    [Fact]
    public void Off_ci_the_keys_are_all_there_and_say_None()
    {
        using var document = JsonDocument.Parse(Write(DataFormat.Json));
        var ci = document.RootElement.GetProperty("ciMetadata");

        Assert.Equal(CiKeys, ci.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("None", ci.GetProperty("provider").GetString());
        foreach (var key in CiKeys.Skip(1))
            Assert.Equal(JsonValueKind.Null, ci.GetProperty(key).ValueKind);
    }

    [Fact]
    public void On_ci_the_same_keys_carry_the_values()
    {
        using var document = JsonDocument.Parse(Write(DataFormat.Json, OnGitHub));
        var ci = document.RootElement.GetProperty("ciMetadata");

        // Identical key set in both branches — the whole point of emitting it unconditionally.
        Assert.Equal(CiKeys, ci.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("GitHubActions", ci.GetProperty("provider").GetString());
        Assert.Equal("abc1234def5678", ci.GetProperty("commitSha").GetString());
        Assert.Equal("main", ci.GetProperty("branch").GetString());
        Assert.Equal("acme/shop", ci.GetProperty("repository").GetString());
        // A re-run: same runId, second attempt. Without this the two runs are indistinguishable.
        Assert.Equal("99", ci.GetProperty("runId").GetString());
        Assert.Equal("2", ci.GetProperty("runAttempt").GetString());
    }

    [Fact]
    public void The_environment_says_what_it_ran_on_and_nothing_about_who()
    {
        using var document = JsonDocument.Parse(Write(DataFormat.Json));
        var environment = document.RootElement.GetProperty("environment");

        Assert.Equal(["os", "runtime"], environment.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(environment.GetProperty("os").GetString()));
        Assert.Contains(".NET", environment.GetProperty("runtime").GetString());

        // Never the machine name or the user: a report is an artifact other people download.
        var text = Write(DataFormat.Json);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_identity_comes_before_the_features()
    {
        // A real report is megabytes of `features`. Identity in the first page is the point: a
        // streaming reader — and a person running `head` — sees it without the rest.
        using var document = JsonDocument.Parse(Write(DataFormat.Json));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.True(names.IndexOf("ciMetadata") < names.IndexOf("features"));
        Assert.True(names.IndexOf("environment") < names.IndexOf("features"));
    }

    // ─── XML and YAML ───────────────────────────────────────────

    [Fact]
    public void The_xml_carries_the_same_two_blocks()
    {
        var document = XDocument.Parse(Write(DataFormat.Xml, OnGitHub));
        var root = document.Root!;

        var ci = root.Element("CiMetadata")!;
        Assert.Equal("GitHubActions", ci.Element("Provider")!.Value);
        Assert.Equal("abc1234def5678", ci.Element("CommitSha")!.Value);
        Assert.Equal("main", ci.Element("Branch")!.Value);

        var environment = root.Element("Environment")!;
        Assert.False(string.IsNullOrWhiteSpace(environment.Element("Os")!.Value));
        Assert.Contains(".NET", environment.Element("Runtime")!.Value);

        // The XML writer omits what carries nothing rather than writing empty elements — its own
        // convention, and the reason the JSON shape is the one a consumer keys on.
        var offCi = XDocument.Parse(Write(DataFormat.Xml)).Root!.Element("CiMetadata")!;
        Assert.Equal("None", offCi.Element("Provider")!.Value);
        Assert.Null(offCi.Element("CommitSha"));
    }

    [Fact]
    public void The_yaml_carries_the_same_two_blocks()
    {
        var yaml = Write(DataFormat.Yaml, OnGitHub);

        Assert.Contains("CiMetadata:", yaml);
        Assert.Contains("  Provider: GitHubActions", yaml);
        Assert.Contains("  CommitSha: abc1234def5678", yaml);
        Assert.Contains("Environment:", yaml);
        Assert.Contains("  Runtime: ", yaml);
    }

    // ─── The report the reader sees ─────────────────────────────

    [Fact]
    public void A_local_report_grows_no_CI_table()
    {
        // The trap this closes: the shortcut of making the detector return a None record would put a
        // "CI (None)" table on every report generated on a laptop.
        var path = ReportGenerator.GenerateHtmlReport(
            [], Features(), DateTime.UtcNow, DateTime.UtcNow,
            null, $"RunIdentity_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            ciMetadata: null);

        // Anchored on the emitted markup, not on the class name: the stylesheet declares
        // `.ci-metadata` in every report whether or not the table is drawn.
        var html = File.ReadAllText(path);
        Assert.DoesNotContain("<div class=\"ci-metadata\">", html);
        Assert.DoesNotContain("<div class=\"ci-chart-group\">", html);
        Assert.DoesNotContain("CI (None)", html);
    }

    [Fact]
    public void Detection_still_returns_null_off_ci()
    {
        // The null contract is what the HTML gate depends on; the unconditional shape lives in the
        // writers, not in the detector.
        Assert.Null(CiMetadataDetector.Detect(_ => null));
    }
}
