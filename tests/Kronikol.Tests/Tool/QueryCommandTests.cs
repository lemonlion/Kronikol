using System.Net;
using System.Text;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The contract <c>kronikol query</c> makes with an agent: every answer fits in a budget, every truncation
/// says how to resume, every listing hands back addresses that work as input, and no payload is ever
/// printed unless it was named.
/// </summary>
public class QueryCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── Overview ──────────────────────────────────────────────

    [Fact]
    public void Summary_names_the_run_the_failures_and_where_to_go_next()
    {
        var output = Run("summary", Report());

        Assert.Contains("3 scenarios", output);
        Assert.Contains("1 failed", output);
        Assert.Contains("s2", output);
        Assert.Contains("next: failures", output);
    }

    [Fact]
    public void Summary_stays_small()
    {
        // The whole point: a run that would be megabytes as JSON answers in a couple of kilobytes.
        Assert.True(Encoding.UTF8.GetByteCount(Run("summary", Report())) < 2000);
    }

    [Fact]
    public void Scenarios_filters_by_result()
    {
        var output = Run("scenarios", Report(), "--result", "Failed");

        Assert.Contains("Checkout fails", output);
        Assert.DoesNotContain("Browse the catalogue", output);
    }

    [Fact]
    public void Count_prints_a_number_and_nothing_else()
    {
        Assert.Equal("1", Run("scenarios", Report(), "--result", "Failed", "--count").Trim());
    }

    [Fact]
    public void Services_answers_the_negative_question()
    {
        var output = Run("services", Report());

        Assert.Contains("payments", output);
        Assert.DoesNotContain("bigquery", output);
        Assert.Contains("a service missing here was never called", output);
    }

    [Fact]
    public void Services_counts_errors_and_bytes()
    {
        var output = Run("services", Report());

        Assert.Matches(@"payments\s+\d+\s+1", output);
    }

    // ─── Narrative ─────────────────────────────────────────────

    [Fact]
    public void Failures_says_why_without_being_asked_for_a_payload()
    {
        var output = Run("failures", Report());

        Assert.Contains("Checkout fails", output);
        Assert.Contains("Expected 4173 but found 3902", output);
        Assert.Contains("OverviewTests.cs:142", output);
        Assert.DoesNotContain("4173, \"currency\"", output);
    }

    [Fact]
    public void Failures_on_a_green_run_says_so_rather_than_printing_nothing()
    {
        var output = Run("failures", Report(allPassing: true));

        Assert.Contains("nothing failed", output);
        Assert.Contains("next: scenarios", output);
    }

    [Fact]
    public void Steps_shows_the_tree_with_interaction_ranges()
    {
        var output = Run("steps", Report(), "s2");

        Assert.Contains("Given a basket", output);
        Assert.Contains("[i0", output);
        Assert.Contains("✗", output);
    }

    [Fact]
    public void Assertions_lists_them_flat_with_their_source()
    {
        var output = Run("assertions", Report(), "--failed");

        Assert.Contains("total == 4173", output);
        Assert.Contains("OverviewTests.cs:142", output);
    }

    [Fact]
    public void Flow_replaces_reading_the_diagram()
    {
        var output = Run("flow", Report(), "s2");

        Assert.Contains("payments", output);
        Assert.Contains("→", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 3000);
    }

    [Fact]
    public void Annotations_surface_the_example_row_marker()
    {
        var output = Run("annotations", Report(), "s2");

        Assert.Contains("Row 3", output);
    }

    // ─── Payloads ──────────────────────────────────────────────

    [Fact]
    public void Interactions_prints_body_pointers_never_bodies()
    {
        var output = Run("interactions", Report(), "s2");

        Assert.Contains("b:", output);
        Assert.DoesNotContain("3902", output);
    }

    [Fact]
    public void Http_without_a_payload_flag_describes_the_body_and_offers_the_cheap_views()
    {
        var output = Run("http", Report(), "s2/i0");

        Assert.Contains("body:", output);
        Assert.Contains("--keys", output);
        Assert.DoesNotContain("\"total\"", output);
    }

    [Fact]
    public void Http_keys_shows_the_shape_for_a_fraction_of_the_payload()
    {
        var output = Run("http", Report(), "s2/i1", "--keys");

        Assert.Contains("$.total", output);
        Assert.Contains("number", output);
    }

    [Fact]
    public void Http_path_pulls_one_value()
    {
        var lines = Run("http", Report(), "s2/i1", "--path", "$.total").Trim().ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("3902", lines[^1].Trim());
    }

    [Fact]
    public void A_missing_path_is_an_answer_not_an_error()
    {
        var output = Run("http", Report(), "s2/i1", "--path", "$.nope");

        Assert.Contains("is not in this body", output);
        Assert.Contains("--keys", output);
    }

    [Fact]
    public void Identical_bodies_share_one_address()
    {
        var output = Run("body", Report(), BodyHashOf(Report(), "s0/i1"));

        Assert.Contains("address(es)", output);
    }

    [Fact]
    public void Out_writes_the_payload_and_costs_almost_no_output()
    {
        var target = Path.Combine(_directory, "body.json");

        var output = Run("http", Report(), "s2/i1", "--body", "--out", target);

        Assert.True(File.Exists(target));
        Assert.Contains("3902", File.ReadAllText(target));
        Assert.DoesNotContain("3902", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 300);
    }

    [Fact]
    public void A_capture_truncated_body_says_so()
    {
        var output = Run("http", Report(), "s0/i18", "--body");

        Assert.Contains("capped at capture time", output);
    }

    [Fact]
    public void Diagram_refuses_stdout_and_says_what_to_do_instead()
    {
        var (output, error, exit) = RunFull("diagram", Report(), "s0/d0");

        Assert.Equal(2, exit);
        Assert.Contains("--out", error);
        Assert.Contains("flow s0", error);
        Assert.DoesNotContain("@startuml", output);
    }

    [Fact]
    public void Diagram_out_writes_the_plantuml()
    {
        var target = Path.Combine(_directory, "d.puml");

        Run("diagram", Report(), "s0/d0", "--out", target);

        Assert.Contains("@startuml", File.ReadAllText(target));
    }

    [Fact]
    public void Note_lists_a_diagram_and_warns_that_a_note_is_a_rendering()
    {
        Assert.Contains("notes", Run("note", Report(), "s0/d0"));
        Assert.Contains("not the captured content", Run("note", Report(), "s0/d0/n0"));
    }

    // ─── Search and comparison ─────────────────────────────────

    [Fact]
    public void Grep_returns_addresses_not_content()
    {
        var output = Run("grep", Report(), "3902");

        Assert.Contains("s2/i1", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 1500);
    }

    [Fact]
    public void Grep_values_names_the_json_path_a_number_came_from()
    {
        var output = Run("grep", Report(), "3902", "--values");

        Assert.Contains("$.total", output);
    }

    [Fact]
    public void Grep_that_finds_nothing_says_where_it_looked()
    {
        var output = Run("grep", Report(), "zzz-not-here");

        Assert.Contains("is not in", output);
        Assert.Contains("--in", output);
    }

    [Fact]
    public void Compare_puts_two_scenarios_side_by_side()
    {
        var output = Run("compare", Report(), "s0", "s2");

        Assert.Contains("steps:", output);
        Assert.Contains("calls:", output);
    }

    [Fact]
    public void Diff_matches_on_stable_id_and_reports_what_broke()
    {
        var older = Report(allPassing: true, fileName: "Old.json");

        var output = Run("diff", older, Report());

        Assert.Contains("BROKE", output);
        Assert.Contains("stableId", output);
    }

    // ─── The invariants ────────────────────────────────────────

    [Theory]
    [InlineData("summary")]
    [InlineData("scenarios")]
    [InlineData("failures")]
    [InlineData("services")]
    public void No_overview_command_emits_a_payload(string command)
    {
        var output = Run(command, Report());

        Assert.DoesNotContain("customerReference", output);
        Assert.DoesNotContain("@startuml", output);
    }

    [Theory]
    [InlineData("steps")]
    [InlineData("flow")]
    [InlineData("interactions")]
    [InlineData("annotations")]
    public void No_scenario_command_emits_a_payload(string command)
    {
        var output = Run(command, Report(), "s2");

        Assert.DoesNotContain("customerReference", output);
        Assert.DoesNotContain("@startuml", output);
    }

    [Fact]
    public void Every_command_stays_under_the_budget()
    {
        foreach (var (command, args) in new (string, string[])[]
                 {
                     ("summary", []), ("scenarios", []), ("failures", []), ("services", []),
                     ("steps", ["s2"]), ("flow", ["s2"]), ("interactions", ["s2"]), ("assertions", []),
                     ("annotations", ["s2"]), ("grep", ["a"])
                 })
        {
            var output = Run(command, Report(), args);
            Assert.True(Encoding.UTF8.GetByteCount(output) <= 6400,
                $"{command} produced {Encoding.UTF8.GetByteCount(output)} bytes");
        }
    }

    [Fact]
    public void A_truncated_listing_says_how_to_resume()
    {
        var output = Run("interactions", Report(), "s0", "--limit", "2");

        Assert.Contains("--offset 2", output);
    }

    [Fact]
    public void Offset_resumes_where_the_footer_said()
    {
        var first = Run("interactions", Report(), "s0", "--limit", "2");
        var second = Run("interactions", Report(), "s0", "--limit", "2", "--offset", "2");

        Assert.NotEqual(first, second);
        Assert.Contains("of ", second);
    }

    [Fact]
    public void Grouping_collapses_repeated_calls_into_one_row()
    {
        var ungrouped = Run("interactions", Report(), "s0", "--limit", "500");
        var grouped = Run("interactions", Report(), "s0", "--group", "--limit", "500");

        Assert.True(grouped.Split('\n').Length < ungrouped.Split('\n').Length);
        Assert.Contains("×", grouped);
    }

    // ─── Addressing and errors ─────────────────────────────────

    [Fact]
    public void An_address_printed_by_one_command_is_accepted_by_the_next()
    {
        var listing = Run("interactions", Report(), "s2");
        var address = listing.Split('\n').First(l => l.StartsWith("s2/i", StringComparison.Ordinal)).Split(' ')[0];

        var (_, _, exit) = RunFull("http", Report(), address);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void An_out_of_range_scenario_says_what_the_range_is()
    {
        var (_, error, exit) = RunFull("steps", Report(), "s99");

        Assert.Equal(2, exit);
        Assert.Contains("the report has 3", error);
    }

    [Fact]
    public void A_directory_is_accepted_when_it_holds_one_report()
    {
        Report();

        Assert.Contains("scenarios", Run("summary", _directory));
    }

    [Fact]
    public void A_current_report_with_nothing_to_attribute_is_not_mistaken_for_an_old_one()
    {
        // Enrichment is detected by the presence of the stepPath key, not of a value: a current report
        // writes it on every interaction and null is a legitimate answer — before the first step, or
        // where attribution could not be trusted.
        var output = Run("summary", Report(allPassing: true, fileName: "NoAttribution.json"));

        Assert.DoesNotContain("predates step attribution", output);
    }

    [Fact]
    public void An_unenriched_report_still_works_and_says_it_is_one()
    {
        var output = Run("summary", UnenrichedReport());

        Assert.Contains("predates step attribution", output);
        Assert.Contains("scenarios", output);
    }

    [Fact]
    public void Steps_on_an_unenriched_report_still_lists_the_tree()
    {
        var output = Run("steps", UnenrichedReport(), "s0");

        Assert.Contains("Given a basket", output);
        Assert.Contains("no step attribution", output);
    }

    [Fact]
    public void Unknown_command_and_unknown_flag_both_explain_themselves()
    {
        Assert.Equal(2, RunFull("nope", Report()).Exit);
        Assert.Contains("Unknown option", RunFull("summary", Report(), "--nope").Error);
    }

    // ─── The large-file path ───────────────────────────────────

    [Fact]
    public void A_report_with_a_diagram_larger_than_the_read_window_is_still_indexed()
    {
        // The reader works on a window that is refilled as it advances, so a single token bigger than the
        // window has to grow it. A diagram is one JSON string and the real ones reach 663 KB.
        var path = BigDiagramReport();

        var output = Run("summary", path);

        Assert.Contains("1 scenarios", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 2000);
    }

    [Fact]
    public void A_big_body_is_indexed_and_fetched_by_address_without_being_printed()
    {
        var path = BigDiagramReport();

        var listing = Run("interactions", path, "s0");
        Assert.Contains("b:", listing);
        Assert.DoesNotContain("filler", listing);

        var target = Path.Combine(_directory, "big.json");
        Run("http", path, "s0/i0", "--body", "--out", target);
        Assert.Contains("filler", File.ReadAllText(target));
    }

    // ─── Harness ───────────────────────────────────────────────

    private string Run(string command, string report, params string[] args)
    {
        var (output, error, exit) = RunFull(command, report, args);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output;
    }

    private (string Output, string Error, int Exit) RunFull(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private string BodyHashOf(string report, string address)
    {
        var line = Run("http", report, address).Split('\n').First(l => l.StartsWith("body:", StringComparison.Ordinal));
        return line.Split("· ")[1].Split(' ')[0].Trim();
    }

    // ─── Fixtures ──────────────────────────────────────────────

    private string? _report;
    private string? _unenriched;

    private string Report(bool allPassing = false, string fileName = "TestRunReport.json")
    {
        if (!allPassing && fileName == "TestRunReport.json" && _report is not null)
            return _report;

        var path = Write(fileName, BuildFeatures(allPassing), BuildLogs(), BuildDiagrams());
        if (!allPassing && fileName == "TestRunReport.json")
            _report = path;
        return path;
    }

    private string UnenrichedReport()
    {
        if (_unenriched is not null)
            return _unenriched;

        // A report as an older Kronikol wrote it — no stepPath key, no failureMessage, no annotations.
        // Written out literally rather than generated, because the current generator cannot produce the
        // old shape and a simulation of it would not be the thing under test.
        var path = Path.Combine(_directory, "Unenriched.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.0.44",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                {
                  "name": "Orders",
                  "labels": [],
                  "scenarios": [
                    {
                      "id": "old-1",
                      "stableId": "aaaabbbbccccdddd",
                      "name": "Checkout",
                      "result": "Passed",
                      "durationSeconds": 1.0,
                      "isHappyPath": true,
                      "errorMessage": null,
                      "labels": [],
                      "categories": [],
                      "steps": [
                        { "keyword": "Given", "text": "a basket", "status": "Passed", "durationSeconds": 0.1, "subSteps": [], "attachments": [] }
                      ],
                      "backgroundSteps": [],
                      "attachments": [],
                      "httpInteractions": [
                        {
                          "type": "Request",
                          "method": "GET",
                          "uri": "http://api/x",
                          "serviceName": "api",
                          "callerName": "test",
                          "content": "{}",
                          "headers": [],
                          "statusCode": null,
                          "traceId": "00000000-0000-0000-0000-000000000001",
                          "requestResponseId": "00000000-0000-0000-0000-000000000002",
                          "timestamp": "2026-01-01T10:00:01.000Z"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        return _unenriched = path;
    }

    private string BigDiagramReport()
    {
        var diagram = "@startuml\n" + string.Join("\n", Enumerable.Range(0, 12000).Select(i => $"note over api : filler line {i} padding padding padding")) + "\n@enduml";
        var body = "{\"filler\":\"" + new string('x', 400_000) + "\"}";

        var features = new[]
        {
            new Feature { DisplayName = "Big", Scenarios = [new Scenario { Id = "big-1", DisplayName = "One big scenario", Result = ExecutionResult.Passed }] }
        };

        var logs = new[]
        {
            new RequestResponseLog("Big", "big-1", HttpMethod.Post, body, new Uri("http://api/big"), [], "api", "test",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = DateTimeOffset.UtcNow }
        };

        return Write("Big.json", features, logs, [new DefaultDiagramsFetcher.DiagramAsCode("big-1", "Big", diagram)]);
    }

    private string Write(string fileName, Feature[] features, RequestResponseLog[]? logs, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null)
    {
        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Query_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams, logs);

        var path = Path.Combine(_directory, fileName);
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static Feature[] BuildFeatures(bool allPassing) =>
    [
        new Feature
        {
            DisplayName = "Catalogue",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Browse the catalogue", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1.2),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "browsing", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t1", DisplayName = "Search the catalogue", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.4),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "searching", Status = ExecutionResult.Passed }]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t2",
                    DisplayName = "Checkout fails on a wrong total",
                    Result = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(3.5),
                    ErrorMessage = allPassing ? null : "Assert.Equal() Failure",
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed },
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "the total is right",
                            Status = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                            FailureMessage = allPassing ? null : "Expected 4173 but found 3902",
                            SourceFile = "OverviewTests.cs", SourceLine = 142,
                            SubSteps =
                            [
                                new ScenarioStep
                                {
                                    Text = "total == 4173",
                                    Status = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                                    FailureMessage = allPassing ? null : "Expected 4173 but found 3902",
                                    SourceFile = "OverviewTests.cs", SourceLine = 142
                                }
                            ]
                        }
                    ]
                }
            ]
        }
    ];

    private static RequestResponseLog[] BuildLogs()
    {
        var logs = new List<RequestResponseLog>();
        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        // A scenario whose calls repeat, so grouping and paging have something real to fold.
        logs.Add(Marker("t0", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>browsing"));
        for (var i = 0; i < 8; i++)
            logs.AddRange(Pair("t0", "redis", "GET", "http://redis/catalogue:v1:page", "{\"page\":1}", HttpStatusCode.OK, start.AddMilliseconds(i * 10)));
        logs.AddRange(Pair("t0", "api", "GET", "http://api/catalogue", "{\"customerReference\":\"abc\"}", HttpStatusCode.OK, start.AddSeconds(1)));
        logs.Add(new RequestResponseLog("Catalogue", "t0", HttpMethod.Get, "start of a body\n\n…truncated (900000 chars total)",
            new Uri("http://api/huge"), [], "api", "test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { Timestamp = start.AddSeconds(2) });

        logs.Add(Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>searching"));
        logs.AddRange(Pair("t1", "search", "POST", "http://search/query", "{\"q\":\"muffin\"}", HttpStatusCode.OK, start.AddSeconds(3)));

        logs.Add(Marker("t2", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"));
        logs.Add(Marker("t2", DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3"));
        logs.AddRange(Pair("t2", "payments", "POST", "http://payments/charge", "{\"amount\":4173,\"currency\":\"GBP\"}",
            HttpStatusCode.InternalServerError, start.AddSeconds(4), "{\"total\":3902,\"currency\":\"GBP\"}"));
        logs.Add(Marker("t2", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>the total is right"));
        logs.AddRange(Pair("t2", "api", "GET", "http://api/order/9", "{\"customerReference\":\"abc\"}", HttpStatusCode.OK, start.AddSeconds(5)));

        return logs.ToArray();
    }

    private static DefaultDiagramsFetcher.DiagramAsCode[] BuildDiagrams() =>
    [
        new("t0", "Catalogue", """
                               @startuml
                               participant redis
                               note over redis : catalogue page 1 loaded from cache
                               note over redis
                               {
                                 "page": 1
                               }
                               end note
                               @enduml
                               """),
        new("t2", "Orders", """
                            @startuml
                            participant payments
                            note over payments : charge rejected
                            @enduml
                            """)
    ];

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string plantUml) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };

    private static RequestResponseLog[] Pair(string testId, string service, string method, string uri, string requestBody,
        HttpStatusCode status, DateTimeOffset at, string? responseBody = null)
    {
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, method, requestBody, new Uri(uri), [("accept", "application/json")],
                service, "test", RequestResponseType.Request, traceId, pairId, false)
            { Timestamp = at, DependencyCategory = service == "redis" ? "cache" : null },
            new RequestResponseLog(testId, testId, method, responseBody ?? "{\"ok\":true}", new Uri(uri), [],
                service, "test", RequestResponseType.Response, traceId, pairId, false, status)
            { Timestamp = at.AddMilliseconds(35) }
        ];
    }
}
