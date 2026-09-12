using System.Text.Json;
using System.Xml.Linq;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What a scenario says about its own failure, and what it must not say when nothing failed.
///
/// <para>Three consumers read <c>errorMessage</c> as evidence that something went wrong — the failures
/// digest, the HTML cluster panel and every exporter — so a field that is present-but-empty is not a
/// harmless default. xUnit v3 wrote <c>"\r\n"</c> onto every <em>passing</em> scenario for exactly that
/// reason: a separator with nothing to separate, shipped as a message.</para>
///
/// <para>The other half is that a framework's classification of a failure is not the failure. xUnit v3
/// spliced its <c>FailureCause</c> enum onto the front of the message, so every failure in a run began
/// with the same word and the digest's first-line cluster key took one of five values run-wide. The
/// category belongs in a field of its own, where nothing mistakes it for a cause.</para>
/// </summary>
public class FailureFieldsTests
{
    [Fact]
    public void A_scenario_that_threw_nothing_carries_no_message()
    {
        Assert.Null(FailureText.Join(null));
        Assert.Null(FailureText.Join([]));
        Assert.Null(FailureText.Join([""]));
        Assert.Null(FailureText.Join(["", null]));

        // The shape xUnit v3 actually shipped, measured on three passing scenarios of
        // Example.Api.Tests.Component.xUnit3: a line separator joining nothing to nothing.
        Assert.Null(FailureText.Join([Environment.NewLine]));
        Assert.Null(FailureText.Join(["   "]));
    }

    [Fact]
    public void The_messages_a_framework_reports_are_joined_one_per_line()
    {
        Assert.Equal(
            "Expected 5 but found 3" + Environment.NewLine + "---- inner",
            FailureText.Join(["Expected 5 but found 3", "---- inner"]));
    }

    [Fact]
    public void An_empty_message_between_two_real_ones_leaves_no_blank_line()
    {
        // A blank line inside the message is not cosmetic: the digest and the cluster panel both key on
        // the FIRST line, so an empty leading or interior entry decides what every failure is grouped by.
        Assert.Equal(
            "outer" + Environment.NewLine + "inner",
            FailureText.Join(["", "outer", null, "inner", ""]));
    }

    [Fact]
    public void The_first_line_of_a_joined_message_is_the_first_real_message()
    {
        var joined = FailureText.Join([null, "", "Assert.Equal() Failure: Values differ", "at Foo()"]);

        Assert.StartsWith("Assert.Equal() Failure", joined);
    }

    // ─── failureCause is a field, not a prefix ─────────────────

    private static Feature[] Features(string? cause) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                new Scenario
                {
                    Id = "s2",
                    DisplayName = "Cancel order",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = "Assert.Equal() Failure: Values differ",
                    FailureCause = cause,
                    Duration = TimeSpan.FromSeconds(1)
                }
            ]
        }
    ];

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void The_json_carries_the_failure_cause_beside_the_message_not_inside_it()
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features("Assertion"), Start, End, "FailureCause.json", DataFormat.Json);
        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        var scenario = root.GetProperty("features")[0].GetProperty("scenarios")[1];
        Assert.Equal("Assertion", scenario.GetProperty("failureCause").GetString());
        // The message keeps its own first line, which is the one thing every cluster key reads.
        Assert.StartsWith("Assert.Equal() Failure", scenario.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public void A_scenario_with_no_cause_reports_it_as_null_rather_than_omitting_the_key()
    {
        var path = ReportGenerator.GenerateTestRunReportData(Features(null), Start, End, "FailureCauseNull.json", DataFormat.Json);
        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        var scenario = root.GetProperty("features")[0].GetProperty("scenarios")[1];
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("failureCause").ValueKind);
    }

    [Fact]
    public void The_xml_and_yaml_carry_it_too()
    {
        var xml = XDocument.Load(ReportGenerator.GenerateTestRunReportData(Features("Timeout"), Start, End, "FailureCause.xml", DataFormat.Xml));
        var yaml = File.ReadAllText(ReportGenerator.GenerateTestRunReportData(Features("Timeout"), Start, End, "FailureCause.yml", DataFormat.Yaml));

        Assert.Contains("Timeout", xml.Descendants().Where(e => e.Name.LocalName == "FailureCause").Select(e => e.Value));
        Assert.Contains("FailureCause: Timeout", yaml);
    }

    [Fact]
    public void The_schema_declares_it()
    {
        var path = ReportGenerator.GenerateTestRunReportSchema("FailureCause_schema.json", DataFormat.Json);
        var scenario = JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("properties").GetProperty("features")
            .GetProperty("items").GetProperty("properties").GetProperty("scenarios")
            .GetProperty("items").GetProperty("properties");

        Assert.True(scenario.TryGetProperty("failureCause", out _),
            "the schema does not declare failureCause, so a consumer validating against it rejects the field the report writes");
    }
}
