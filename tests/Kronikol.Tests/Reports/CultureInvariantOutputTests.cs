using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What a run writes does not depend on the culture of the machine that ran it (plans/WARM_UP_PLAN.md F12, R0). Until
/// 4.7.3 the data files wrote durations with the machine's decimal separator (<c>1,234</c>, which no XML or YAML reader
/// takes as a number) and the run's start and end with its calendar and time separator (<c>2569-01-01</c> on a Thai
/// machine, <c>10.05.07</c> on a Finnish one), the Scenario Timeline wrote <c>width:12,3%</c>, which a browser drops,
/// and the pie chart wrote SVG lengths a browser cannot read. Each fact runs under every culture of
/// <see cref="CultureRun.Cultures"/> and reads the value a reader would.
/// </summary>
public class CultureInvariantOutputTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 5, 7, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(2);

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "culture-1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1260),
                    Steps =
                    [
                        new ScenarioStep { Keyword = "When", Text = "I place an order", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(120) },
                        new ScenarioStep { Keyword = "Then", Text = "it is accepted", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(4) }
                    ]
                },
                new Scenario { Id = "culture-2", DisplayName = "Cancel an order", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(123), ErrorMessage = "Expected 2 but found 3" },
                new Scenario { Id = "culture-3", DisplayName = "Refund an order", Result = ExecutionResult.Skipped }
            ]
        }
    ];

    private static string Data(string culture, DataFormat format) => CultureRun.Under(culture, () =>
        File.ReadAllText(ReportGenerator.GenerateTestRunReportData(Features(), Start, End,
            $"Culture_{Guid.NewGuid():N}.{(format == DataFormat.Json ? "json" : format == DataFormat.Xml ? "xml" : "yml")}", format, null, null)));

    private static string Html(string culture) => CultureRun.Under(culture, () =>
        File.ReadAllText(ReportGenerator.GenerateHtmlReport([], Features(), Start, End, null, $"Culture_{Guid.NewGuid():N}.html", "Culture", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs)));

    // ─── The data files ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Xml_durations_are_numbers_written_with_a_point(string culture)
    {
        var document = XDocument.Parse(Data(culture, DataFormat.Xml));

        var durations = document.Descendants().Where(e => e.Name.LocalName == "DurationSeconds").Select(e => e.Value).ToArray();

        // The scenarios' (unknown is 0.000) and the steps'.
        Assert.Equal(["1.260", "0.120", "0.004", "0.123", "0.000"], durations);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Yaml_durations_are_numbers_written_with_a_point(string culture)
    {
        var yaml = Data(culture, DataFormat.Yaml);

        var durations = Regex.Matches(yaml, @"DurationSeconds: (\S+)").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["1.260", "0.120", "0.004", "0.123", "0.000"], durations);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Every_data_format_writes_the_start_and_end_as_gregorian_iso_8601(string culture)
    {
        using var json = JsonDocument.Parse(Data(culture, DataFormat.Json));
        Assert.Equal("2026-01-01T10:05:07Z", json.RootElement.GetProperty("startTime").GetString());
        Assert.Equal("2026-01-01T10:07:07Z", json.RootElement.GetProperty("endTime").GetString());

        var xml = XDocument.Parse(Data(culture, DataFormat.Xml));
        Assert.Equal("2026-01-01T10:05:07Z", xml.Descendants().Single(e => e.Name.LocalName == "StartTime").Value);
        Assert.Equal("2026-01-01T10:07:07Z", xml.Descendants().Single(e => e.Name.LocalName == "EndTime").Value);

        var yaml = Data(culture, DataFormat.Yaml);
        Assert.Contains("StartTime: \"2026-01-01T10:05:07Z\"\n", yaml.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("EndTime: \"2026-01-01T10:07:07Z\"\n", yaml.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    public static TheoryData<string, DataFormat> CulturesAndFormats()
    {
        var data = new TheoryData<string, DataFormat>();
        foreach (var culture in CultureRun.Cultures)
            foreach (var format in new[] { DataFormat.Json, DataFormat.Xml, DataFormat.Yaml })
                data.Add(culture, format);
        return data;
    }

    [Theory]
    [MemberData(nameof(CulturesAndFormats))]
    public void Every_data_format_writes_the_same_bytes_under_the_culture_as_under_en_us(string culture, DataFormat format)
    {
        // The schema contract's fixture touches every emitter; its calls are made once, so both runs write the same ids.
        var features = TestRunReportSchemaContractTests.RichFeatures();
        var logs = TestRunReportSchemaContractTests.Logs();
        var extension = format == DataFormat.Json ? "json" : format == DataFormat.Xml ? "xml" : "yml";
        string Write(string under) => CultureRun.Under(under, () => File.ReadAllText(ReportGenerator.GenerateTestRunReportData(features,
            TestRunReportSchemaContractTests.Start, TestRunReportSchemaContractTests.End, $"Culture_{Guid.NewGuid():N}.{extension}", format,
            TestRunReportSchemaContractTests.Diagrams(), logs, TestRunReportSchemaContractTests.Diagnostics(), suite: "culture",
            environment: RunEnvironment.Unrecorded)));

        Assert.Equal(Write("en-US"), Write(culture));
    }

    // ─── The HTML report ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_timeline_writes_each_bar_width_with_a_point(string culture)
    {
        var html = Html(culture);

        var widths = Regex.Matches(html, "<div class=\"timeline-bar [^\"]*\" style=\"width:([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

        // 1,260 ms is the longest; 123 ms is 9.76% of it.
        Assert.Equal(["100.0%", "9.8%"], widths);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_duration_badge_writes_seconds_with_a_point(string culture)
    {
        var html = Html(culture);

        Assert.Contains(">1.3s</span>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"timeline-duration\">1.3s</div>", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_pie_chart_writes_svg_numbers_a_browser_reads(string culture)
    {
        var svg = CultureRun.Under(culture, () => ReportGenerator.GeneratePieChartSvg(passed: 2, failed: 1, skipped: 1, bypassed: 0));

        var numbers = Regex.Matches(svg, "(?: r| stroke-dasharray| stroke-dashoffset)=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

        // Three segments, each a radius, a dash and a gap, and an offset: 50%, then 25% after it, then 25% after 75%.
        Assert.Equal(
        [
            "40.0", "125.66 125.66", "-0.00",
            "40.0", "62.83 188.50", "-125.66",
            "40.0", "62.83 188.50", "-188.50"
        ], numbers);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_summary_writes_the_start_as_a_gregorian_date_and_times_with_colons(string culture)
    {
        var html = Html(culture);

        Assert.Contains("<tr><td>Start Date:</td><td>2026-01-01 (UTC)</td>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>Start Time:</td><td>10:05:07 (UTC)</td>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td>End Time:</td><td>10:07:07 (UTC)</td>", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_diagnostic_report_writes_first_and_last_seen_as_gregorian_times_with_colons(string culture)
    {
        var at = new DateTimeOffset(2026, 1, 1, 10, 5, 7, TimeSpan.Zero);
        RequestResponseLog[] logs =
        [
            new(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, null, new Uri("http://orders/poll"), [], "orders", "host",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = at },
            new(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, null, new Uri("http://orders/poll"), [], "orders", "host",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = at.AddMinutes(3) }
        ];

        var html = CultureRun.Under(culture, () => DiagnosticReportGenerator.BuildHtml(logs, Features(), new ReportConfigurationOptions()));

        Assert.Contains("<td>2026-01-01 10:05:07</td><td>2026-01-01 10:08:07</td>", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_console_summary_writes_file_sizes_with_a_point(string culture)
    {
        Assert.Equal("1.5 KB", CultureRun.Under(culture, () => RunSummaryConsoleWriter.Size(1536)));
        Assert.Equal("3.1 MB", CultureRun.Under(culture, () => RunSummaryConsoleWriter.Size(3_250_586)));
    }

    // ─── The public flame chart renderers ──────────────────────

    private static InternalFlowSegment FlameSegment()
    {
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        const string trace = "4bf92f3577b34da6a3ce929d0e0e4736";
        FlowSpan[] spans =
        [
            new(trace, "00f067aa0ba902b7", null, "POST /orders", "Microsoft.AspNetCore", start, TimeSpan.FromMilliseconds(300)),
            new(trace, "00f067aa0ba902b8", "00f067aa0ba902b7", "INSERT orders", "Npgsql", start.AddMilliseconds(100.5), TimeSpan.FromMilliseconds(33.3))
        ];
        return new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "flame-1", start, start.AddMilliseconds(300), []) { FlowSpans = spans };
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void The_flame_chart_renderers_write_their_percentages_with_a_point(string culture)
    {
        var segment = FlameSegment();
        var markerAt = new DateTimeOffset(segment.FlowSpans[0].StartTimeUtc.AddMilliseconds(150.25), TimeSpan.Zero);

        var plain = CultureRun.Under(culture, () => InternalFlowRenderer.RenderFlameChart(segment));
        var marked = CultureRun.Under(culture, () => InternalFlowRenderer.RenderFlameChartWithBoundaryMarkers(segment, [("POST /orders", markerAt)]));
        var sequential = CultureRun.Under(culture, () => InternalFlowRenderer.RenderSequentialTestFlameChart(new Dictionary<string, InternalFlowSegment> { ["flame-1"] = segment }));

        foreach (var html in new[] { plain, marked, sequential })
        {
            var bars = Regex.Matches(html, "style=\"margin-left:([^;]*);width:([^;]*);").Select(m => m.Groups[1].Value + " " + m.Groups[2].Value).ToArray();
            // The INSERT starts 100.5 ms into a 300 ms chart and takes 33.3 ms of it.
            Assert.Equal(["0.00% 100.00%", "33.50% 11.10%"], bars);
        }

        Assert.Contains("<div class=\"iflow-boundary-marker\" style=\"left:50.08%\"", marked, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flame_bar_has_the_colour_the_browser_gives_its_source_in_every_process()
    {
        // The browser's flame chart hashes a source with Java's string hash; the C# renderer used string.GetHashCode,
        // which .NET randomises per process, so the same source changed colour from one run to the next.
        static int JavaHash(string text)
        {
            var hash = 0;
            foreach (var c in text)
                hash = unchecked(hash * 31 + c);
            return hash;
        }

        var html = InternalFlowRenderer.RenderFlameChart(FlameSegment());

        foreach (var source in new[] { "Microsoft.AspNetCore", "Npgsql" })
        {
            var hue = Math.Abs((long)JavaHash(source)) % 360;
            Assert.Contains($"background:hsl({hue}, 60%, ", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_cultures_differ_from_en_us_where_each_fact_says()
    {
        // The control: under each culture the formats the writers used do produce the separator, the calendar or the
        // minus sign the facts above rule out, so a fact that passes under it is not passing by accident.
        string Format(string culture, Func<string> body) => CultureRun.Under(culture, body);

        Assert.Equal("1,250", Format("de-DE", () => 1.25.ToString("F3")));
        Assert.Equal("10.05.07", Format("fi-FI", () => Start.ToString("HH:mm:ss")));
        Assert.Equal("−0,00", Format("fi-FI", () => (-0.0001).ToString("F2")));
        Assert.Equal("2569-01-01", Format("th-TH", () => Start.ToString("yyyy-MM-dd")));
        Assert.StartsWith("1447-", Format("ar-SA", () => Start.ToString("yyyy-MM-dd")), StringComparison.Ordinal);
        Assert.Equal("1٫250", Format("ar-SA", () => 1.25.ToString("F3")));
    }
}
