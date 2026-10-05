using System.Xml.Linq;

namespace Kronikol.Tests;

/// <summary>
/// The doc comments ship in the packages (<c>GenerateDocumentationFile</c>), so a summary stacked on the wrong
/// member is something a consumer reads: the member it belongs to shows nothing and the one it sits on shows
/// two. Until 4.5.1 eleven members had a summary that a later edit had left above another member: among them
/// <c>HistoryRunContext.Append</c>'s on the field above it, and on the public <c>Track.Attachment</c> the
/// summary of <c>Track.AssertionFailedWithValues</c>.
/// </summary>
public class DocCommentPlacementTests
{
    private static XDocument Docs(string assembly) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, assembly + ".xml"));

    // Types are left out: the compiler joins the summaries of a partial type's declarations, and QueryCommand
    // and HistoryCommand say what each of their files holds. A summary orphaned above a record still shows,
    // on the record's constructor.
    [Theory]
    [InlineData("Kronikol")]
    [InlineData("Kronikol.Tool")]
    public void No_member_carries_two_summaries(string assembly)
    {
        var doubled = Docs(assembly).Descendants("member")
            .Where(m => !((string)m.Attribute("name")!).StartsWith("T:", StringComparison.Ordinal))
            .Where(m => m.Elements("summary").Count() > 1)
            .Select(m => (string)m.Attribute("name")!)
            .ToArray();

        Assert.True(doubled.Length == 0, "two summaries on: " + string.Join(", ", doubled));
    }

    private static string Summary(string assembly, string member) =>
        Docs(assembly).Descendants("member").Single(m => (string)m.Attribute("name")! == member).Element("summary")!.Value;

    // Until 4.5.1 these five read as working options; nothing in the code base reads them.
    [Theory]
    [InlineData("ShowRelationshipFlows")]
    [InlineData("RelationshipFlowStyle")]
    [InlineData("ShowSystemFlameChart")]
    [InlineData("LowCoverageThreshold")]
    [InlineData("MaxFlameChartTests")]
    public void A_component_diagram_option_nothing_reads_says_so(string option) =>
        Assert.StartsWith("Has no effect: nothing reads it.",
            Summary("Kronikol", $"P:Kronikol.ComponentDiagram.ComponentDiagramOptions.{option}").Trim());

    // Until 4.5.1 it promised P95 colours, which a generated report never draws: no report passes the stats.
    [Fact]
    public void ArrowColorMode_Performance_says_it_needs_stats_a_report_never_passes() =>
        Assert.Contains("A generated report passes none",
            Summary("Kronikol", "F:Kronikol.ComponentDiagram.ArrowColorMode.Performance"));

    // Until 4.5.1 it said "enables diagnostic logging" and never named the file it writes.
    [Fact]
    public void DiagnosticMode_names_the_page_it_writes() =>
        Assert.Contains("DiagnosticReport.html", Summary("Kronikol", "P:Kronikol.ReportConfigurationOptions.DiagnosticMode"));

    [Fact]
    public void HistoryRunContext_Append_carries_its_own_summary()
    {
        var append = Docs("Kronikol").Descendants("member")
            .SingleOrDefault(m => (string)m.Attribute("name")! == "M:Kronikol.History.HistoryRunContext.Append");

        Assert.NotNull(append);
        Assert.StartsWith("Appends this run's line to the ledger", append.Element("summary")!.Value.Trim());
    }
}
