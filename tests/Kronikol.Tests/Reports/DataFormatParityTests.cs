using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The three data formats are three spellings of one report, so a thing recorded about the run belongs
/// in all of them. Two things were not: <c>diagnostics</c> and <c>annotations</c> reached the JSON
/// writer and nowhere else.
///
/// <para>The mechanism is one line each. <see cref="ReportGenerator.GenerateTestRunReportData"/> resolves
/// both and then passes them on the JSON arm of its switch only — the XML and YAML writers do not take
/// the parameters, so there was nothing to drop: a reader could not tell a run with no diagnostics from
/// a format that cannot carry them. <c>diagnostics</c> is where a degraded capture, a diagram that
/// failed to render and a step whose interactions could not be attributed are recorded, so the formats
/// that could not carry it were the formats that reported a broken run as a clean one.</para>
/// </summary>
public class DataFormatParityTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static string Write(DataFormat format, string name) =>
        ReportGenerator.GenerateTestRunReportData(
            TestRunReportSchemaContractTests.RichFeatures(), Start, End,
            name, format,
            TestRunReportSchemaContractTests.Diagrams(),
            TestRunReportSchemaContractTests.Logs(),
            TestRunReportSchemaContractTests.Diagnostics());

    [Fact]
    public void Xml_carries_the_run_diagnostics()
    {
        var doc = XDocument.Load(Write(DataFormat.Xml, $"Parity_diag_{Guid.NewGuid():N}.xml"));

        var entries = doc.Root!.Element("Diagnostics")?.Elements("Diagnostic").ToArray() ?? [];

        Assert.Equal(2, entries.Length);
        Assert.Equal("RenderFailure", entries[0].Element("Kind")!.Value);
        Assert.Contains("TimeoutException", entries[0].Element("Message")!.Value, StringComparison.Ordinal);
        Assert.Equal(TestRunReportSchemaContractTests.TestId, entries[0].Element("ScenarioId")!.Value);
        // The second entry is report-level, so it has no scenario. Omitted, not blank.
        Assert.Null(entries[1].Element("ScenarioId"));
    }

    [Fact]
    public void Yaml_carries_the_run_diagnostics()
    {
        var yaml = File.ReadAllText(Write(DataFormat.Yaml, $"Parity_diag_{Guid.NewGuid():N}.yml"));
        var root = (System.Text.Json.Nodes.JsonObject)YamlSchemaValidationTests.YamlToJson(yaml);

        var entries = (System.Text.Json.Nodes.JsonArray)root["Diagnostics"]!;

        Assert.Equal(2, entries.Count);
        Assert.Equal("RenderFailure", (string?)entries[0]!["Kind"]);
        Assert.Equal(TestRunReportSchemaContractTests.TestId, (string?)entries[0]!["ScenarioId"]);
        Assert.Null((string?)entries[1]!["ScenarioId"]);
    }

    /// <summary>
    /// An annotation is a diagram marker carrying text that is recorded nowhere else in the file, which
    /// is the whole reason it is exported. It was exported to one format out of three.
    /// </summary>
    [Fact]
    public void Xml_carries_the_scenario_annotations()
    {
        var doc = XDocument.Load(Write(DataFormat.Xml, $"Parity_annot_{Guid.NewGuid():N}.xml"));

        var annotations = doc.Descendants("Annotation").ToArray();

        Assert.NotEmpty(annotations);
        Assert.Contains(annotations, a => a.Element("Kind")!.Value == "Custom");
        Assert.Contains(annotations, a => a.Element("Text")!.Value.Contains("custom", StringComparison.Ordinal));
    }

    [Fact]
    public void Yaml_carries_the_scenario_annotations()
    {
        var yaml = File.ReadAllText(Write(DataFormat.Yaml, $"Parity_annot_{Guid.NewGuid():N}.yml"));

        Assert.Contains("Annotations:", yaml, StringComparison.Ordinal);
        Assert.Contains("Kind: Custom", yaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The XSD is generated from the same source as the writer, so it has to move with it. This is the
    /// only test that feeds the rich fixture — diagnostics, a null step status, a null header value —
    /// through the XML writer and its own schema; the two that existed passed no diagnostics at all, so
    /// a writer and XSD that disagreed about them would both have stayed green.
    /// </summary>
    [Fact]
    public void A_generated_xml_report_validates_against_its_own_xsd()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dataPath = Write(DataFormat.Xml, $"Parity_xsd_{suffix}.xml");
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"Parity_xsd_{suffix}.schema.xsd", DataFormat.Xml);

        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(schemaPath))));

        var errors = new List<string>();
        XDocument.Load(dataPath).Validate(schemaSet, (_, e) => errors.Add(e.Message));

        Assert.True(errors.Count == 0, string.Join("\n  ", errors));
    }

    /// <summary>
    /// The XML writer omits an element it has no value for — every line of it does, except the two that
    /// did not. A blank <c>&lt;Method /&gt;</c> is not "no method": it is the empty string, which is a
    /// different claim, and it is the claim a reader gets for a bare event or a header sent with no
    /// value.
    /// </summary>
    [Fact]
    public void Xml_omits_an_element_it_has_no_value_for_rather_than_blanking_it()
    {
        var doc = XDocument.Load(Write(DataFormat.Xml, $"Parity_blank_{Guid.NewGuid():N}.xml"));

        var blanks = doc.Descendants()
            .Where(e => !e.HasElements && e.Value.Length == 0)
            .Select(e => e.Name.LocalName)
            .Distinct()
            .ToArray();

        Assert.True(blanks.Length == 0, "empty elements written instead of omitted: " + string.Join(", ", blanks));
    }
}
