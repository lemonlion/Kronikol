using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What the data files record of the first-call warm-up (plans/WARM_UP_PLAN.md 4.4, T20, T21, T24): <c>warmUp</c> on a
/// marked call's request record and <c>warmUpSeconds</c> on a scenario that carries one, written only where there is
/// something to record, so a run with no warm-up writes what it wrote before.
/// </summary>
public class WarmUpDataTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-warm-up-data").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_marked_call_carries_its_mark_on_its_request_record_and_nowhere_else()
    {
        var (features, logs, ids) = Marked();

        using var json = JsonDocument.Parse(File.ReadAllText(Write(features, logs, DataFormat.Json)));
        var records = Records(json).ToList();

        var marked = records.Where(r => r.TryGetProperty("warmUp", out _)).ToList();
        Assert.Equal(2, marked.Count);
        Assert.All(marked, r => Assert.Equal("Request", r.GetProperty("type").GetString()));

        var first = marked.Single(r => r.GetProperty("requestResponseId").GetString() == ids.First.ToString());
        var mark = first.GetProperty("warmUp");
        Assert.Equal("first", mark.GetProperty("kind").GetString());
        Assert.Equal("POST /orders", mark.GetProperty("shape").GetString());
        Assert.Equal(5.0, mark.GetProperty("baselineMs").GetDouble(), 6);
        Assert.Equal(2, mark.GetProperty("baselineCalls").GetInt32());
        Assert.False(mark.TryGetProperty("first", out _));

        var waited = marked.Single(r => r.GetProperty("requestResponseId").GetString() == ids.Waiter.ToString()).GetProperty("warmUp");
        Assert.Equal("waited", waited.GetProperty("kind").GetString());
        Assert.Equal(ids.First.ToString(), waited.GetProperty("first").GetString());
    }

    [Fact]
    public void The_mark_sits_after_the_calls_duration()
    {
        var (features, logs, _) = Marked();

        using var json = JsonDocument.Parse(File.ReadAllText(Write(features, logs, DataFormat.Json)));
        var names = Records(json).First(r => r.TryGetProperty("warmUp", out _)).EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(names.IndexOf("durationMs") + 1, names.IndexOf("warmUp"));
    }

    [Fact]
    public void A_scenario_carries_its_warm_up_beside_its_duration_only_when_it_has_one()
    {
        var (features, logs, _) = Marked();

        using var json = JsonDocument.Parse(File.ReadAllText(Write(features, logs, DataFormat.Json)));
        var scenarios = Scenarios(json).ToDictionary(s => s.GetProperty("id").GetString()!);

        Assert.Equal(0.6, scenarios["s1"].GetProperty("warmUpSeconds").GetDouble(), 6);
        Assert.Equal(0.59, scenarios["s2"].GetProperty("warmUpSeconds").GetDouble(), 6);
        Assert.False(scenarios["s3"].TryGetProperty("warmUpSeconds", out _));
        var names = scenarios["s1"].EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(names.IndexOf("durationSeconds") + 1, names.IndexOf("warmUpSeconds"));
    }

    [Fact]
    public void A_run_with_no_warm_up_writes_no_warm_up_keys()
    {
        var (features, logs, _) = Marked(firstMs: 5);

        var text = File.ReadAllText(Write(features, logs, DataFormat.Json));

        Assert.DoesNotContain("\"warmUp", text);
    }

    [Fact]
    public void The_schema_declares_and_describes_both_keys_and_a_marked_report_validates_against_it()
    {
        var (features, logs, _) = Marked();
        var report = Write(features, logs, DataFormat.Json);
        var schemaPath = Path.Combine(_directory, "TestRunReport.schema.json");
        File.Move(ReportGenerator.GenerateTestRunReportSchema($"WarmUpSchema_{Guid.NewGuid():N}.schema.json", DataFormat.Json), schemaPath, overwrite: true);

        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var defs = schema.RootElement.GetProperty("$defs");
        var scenarioKey = schema.RootElement.GetProperty("properties").GetProperty("features").GetProperty("items").GetProperty("properties")
            .GetProperty("scenarios").GetProperty("items").GetProperty("properties").GetProperty("warmUpSeconds");
        var callKey = defs.GetProperty("httpInteraction").GetProperty("properties").GetProperty("warmUp");
        Assert.Contains("warm-up", scenarioKey.GetProperty("description").GetString());
        Assert.Contains("warm-up", callKey.GetProperty("description").GetString());
        Assert.Equal(["first", "waited"], callKey.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        Assert.Empty(SchemaValidationTests.Validate(schemaPath, report));
    }

    [Theory]
    [InlineData(DataFormat.Xml)]
    [InlineData(DataFormat.Yaml)]
    public void Xml_and_yaml_carry_the_same_marks(DataFormat format)
    {
        var (features, logs, ids) = Marked();

        var text = File.ReadAllText(Write(features, logs, format));

        if (format == DataFormat.Xml)
        {
            var document = XDocument.Parse(text);
            var marks = document.Descendants("HttpInteraction").Where(i => i.Element("WarmUp") is not null).ToList();
            Assert.Equal(2, marks.Count);
            var first = marks.Single(i => i.Element("RequestResponseId")!.Value == ids.First.ToString()).Element("WarmUp")!;
            Assert.Equal("first", first.Element("Kind")!.Value);
            Assert.Equal("POST /orders", first.Element("Shape")!.Value);
            Assert.Equal("5.000", first.Element("BaselineMs")!.Value);
            Assert.Equal("2", first.Element("BaselineCalls")!.Value);
            Assert.Null(first.Element("First"));
            Assert.Equal(ids.First.ToString(), marks.Single(i => i.Element("RequestResponseId")!.Value == ids.Waiter.ToString()).Element("WarmUp")!.Element("First")!.Value);
            var s1 = document.Descendants("Scenario").Single(s => s.Element("Id")!.Value == "s1");
            Assert.Equal("0.600", s1.Element("WarmUpSeconds")!.Value);
            Assert.Null(document.Descendants("Scenario").Single(s => s.Element("Id")!.Value == "s3").Element("WarmUpSeconds"));
        }
        else
        {
            Assert.Contains("WarmUpSeconds: 0.600\n", text);
            Assert.Contains("WarmUpSeconds: 0.590\n", text);
            Assert.Contains("WarmUp:\n", text);
            Assert.Contains("Kind: first\n", text);
            Assert.Contains("Kind: waited\n", text);
            Assert.Contains("First: " + ids.First + "\n", text);
            Assert.Contains("BaselineMs: 5.000\n", text);
        }
    }

    [Fact]
    public void The_xml_validates_against_the_schema_written_beside_it_with_and_without_marks()
    {
        var schemaPath = Path.Combine(_directory, "TestRunReport.xsd");
        File.Move(ReportGenerator.GenerateTestRunReportSchema($"WarmUpXsd_{Guid.NewGuid():N}.xsd", DataFormat.Xml), schemaPath, overwrite: true);
        var schemas = new System.Xml.Schema.XmlSchemaSet();
        schemas.Add(null, schemaPath);

        foreach (var firstMs in new[] { 600.0, 5.0 })
        {
            var (features, logs, _) = Marked(firstMs);
            var document = XDocument.Parse(File.ReadAllText(Write(features, logs, DataFormat.Xml)));
            var errors = new List<string>();
            document.Validate(schemas, (_, e) => errors.Add(e.Message));
            Assert.Empty(errors);
        }
    }

    [Theory]
    [InlineData(DataFormat.Xml)]
    [InlineData(DataFormat.Yaml)]
    public void The_new_numbers_are_written_with_a_point_under_a_decimal_comma_culture(DataFormat format)
    {
        var (features, logs, _) = Marked(firstMs: 600.25);

        var text = CultureRun.Under("de-DE", () => File.ReadAllText(Write(features, logs, format)));

        Assert.DoesNotContain("0,6", text);
        Assert.DoesNotContain("5,000", text);
        Assert.Contains(format == DataFormat.Xml ? "<WarmUpSeconds>0.600</WarmUpSeconds>" : "WarmUpSeconds: 0.600\n", text);
    }

    /// <summary>
    /// Three scenarios: s1 makes the run's first POST /orders (600 ms), s2's call waits for it (590 ms, released with it),
    /// and s3 makes two warm calls (5 ms) after both.
    /// </summary>
    private static (Feature[] Features, List<RequestResponseLog> Logs, (Guid First, Guid Waiter) Ids) Marked(double firstMs = 600)
    {
        var logs = new List<RequestResponseLog>();
        var first = Call(logs, "s1", 0, firstMs);
        // With a fast first call the "waiter" starts after it ended and is one more warm call.
        var waiter = Call(logs, "s2", 10, firstMs > 20 ? firstMs - 10 : firstMs);
        Call(logs, "s3", 1000, 5);
        Call(logs, "s3", 1100, 5);
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s2", DisplayName = "Place again", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s3", DisplayName = "Place twice", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
                ]
            }
        ];
        return (features, logs, (first, waiter));
    }

    private static Guid Call(List<RequestResponseLog> logs, string scenario, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = T0.AddTicks((long)Math.Round(startMs * TimeSpan.TicksPerMillisecond)) });
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = T0.AddTicks((long)Math.Round((startMs + durationMs) * TimeSpan.TicksPerMillisecond)) });
        return id;
    }

    private string Write(Feature[] features, List<RequestResponseLog> logs, DataFormat format)
    {
        var extension = format switch { DataFormat.Xml => "xml", DataFormat.Yaml => "yml", _ => "json" };
        var written = ReportGenerator.GenerateTestRunReportData(features, T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1),
            $"WarmUp_{Guid.NewGuid():N}.{extension}", format, null, logs.ToArray());
        var path = Path.Combine(_directory, $"TestRunReport.{extension}");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static IEnumerable<JsonElement> Scenarios(JsonDocument json) =>
        json.RootElement.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray());

    private static IEnumerable<JsonElement> Records(JsonDocument json) =>
        Scenarios(json).SelectMany(s => s.GetProperty("httpInteractions").EnumerateArray());
}
