using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

public class MergeableReportTests
{
    private static Feature[] FeaturesA() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Endpoint = "/orders",
            Description = "Order flows",
            Tags = ["api"],
            Scenarios =
            [
                new Scenario { Id = "a1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2), IsHappyPath = true, Tags = ["smoke"] },
                new Scenario { Id = "a2", DisplayName = "Cancel order", Result = ExecutionResult.Failed, ErrorMessage = "boom", Duration = TimeSpan.FromSeconds(1),
                    Steps = [ new ScenarioStep { Keyword = "Given", Text = "an order", Status = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.5) } ] }
            ]
        }
    ];

    private static DiagramAsCode[] DiagramsA() =>
    [
        new DiagramAsCode("a1", "", "@startuml\nA->B\n@enduml"),
        new DiagramAsCode("a2", "", "@startuml\nC->D\n@enduml")
    ];

    private static string SerializeA() =>
        ReportGenerator.GenerateMergeableReportJson(
            FeaturesA(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            DiagramsA().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships:
            [
                new ComponentRelationship("Test", "OrdersApi", "HTTP", new HashSet<string> { "POST /orders" }, 3, 2, "http")
            ],
            internalFlowSegmentData: new Dictionary<string, object> { ["iflow-x"] = new { title = "t", content = "<div>flow</div>" } },
            wholeTestFlow: new Dictionary<string, WholeTestFlowFragment> { ["a1"] = new("<div>act</div>", "<div>flame</div>", 4) },
            WholeTestFlowVisualization.Both,
            new CiMetadata(CiEnvironment.GitHubActions, "42", "main", "abc123", "https://gh/run/1", "owner/repo", "1", "2"));

    [Fact]
    public void A_4x_file_s_labels_and_categories_read_as_one_tag_list_less_its_generated_retry_labels()
    {
        // plans/TAGS_PLAN.md §4.13: what a 4.x writer wrote, the same report with labels and categories in place of tags
        var json = System.Text.Json.Nodes.JsonNode.Parse(SerializeA())!;
        var feature = json["features"]![0]!.AsObject();
        feature.Remove("tags");
        feature["labels"] = new System.Text.Json.Nodes.JsonArray("api");
        var scenario = feature["scenarios"]!.AsArray().Single(s => (string?)s!["id"] == "a1")!.AsObject();
        scenario.Remove("tags");
        // a second attempt: 4.x's ingest lanes wrote retry 1 for the first
        scenario["attempt"] = 2;
        scenario["labels"] = new System.Text.Json.Nodes.JsonArray("retry 1", "fast");
        scenario["categories"] = new System.Text.Json.Nodes.JsonArray("smoke", "FAST");

        var report = MergeableReportReader.Parse(json.ToJsonString());

        var f = Assert.Single(report.Features);
        Assert.Equal(["api"], f.Tags);
        Assert.Equal(["fast", "smoke"], f.Scenarios.Single(s => s.Id == "a1").Tags);
        Assert.Null(f.Scenarios.Single(s => s.Id == "a2").Tags);
    }

    [Fact]
    public void A_4x_file_s_Happy_Path_label_reads_as_the_happy_path_and_not_as_a_tag()
    {
        // 4.x's LightBDD wrote [HappyPath] as the label Happy Path beside isHappyPath; 5.0.0 writes the flag alone, so a
        // shard of either kind merges the same (5.2.7). A happy-path name 4.x kept as a label sets the flag as 5.0.0 does.
        var json = System.Text.Json.Nodes.JsonNode.Parse(SerializeA())!;
        var feature = json["features"]![0]!.AsObject();
        var placed = feature["scenarios"]!.AsArray().Single(s => (string?)s!["id"] == "a1")!.AsObject();
        placed.Remove("tags");
        placed["labels"] = new System.Text.Json.Nodes.JsonArray("Happy Path", "fast");
        var cancelled = feature["scenarios"]!.AsArray().Single(s => (string?)s!["id"] == "a2")!.AsObject();
        cancelled.Remove("isHappyPath");
        cancelled["labels"] = new System.Text.Json.Nodes.JsonArray("happy path");
        // a category was written as one, @category:happy-path, which 4.x and 5.0.0 read as a tag
        cancelled["categories"] = new System.Text.Json.Nodes.JsonArray("happy-path");

        var report = MergeableReportReader.Parse(json.ToJsonString());

        var f = Assert.Single(report.Features);
        var a1 = f.Scenarios.Single(s => s.Id == "a1");
        Assert.True(a1.IsHappyPath);
        Assert.Equal(["fast"], a1.Tags);
        var a2 = f.Scenarios.Single(s => s.Id == "a2");
        Assert.True(a2.IsHappyPath);
        Assert.Equal(["happy-path"], a2.Tags);
    }

    // ─── Tags across shards (plans/TAGS_PLAN.md §4.13, F22) ──────────

    private static MergeableReport Shard(params Feature[] features) =>
        MergeableReportReader.Parse(ReportGenerator.GenerateMergeableReportJson(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            Array.Empty<DiagramAsCode>().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships: [],
            internalFlowSegmentData: [],
            wholeTestFlow: [],
            WholeTestFlowVisualization.None,
            ciMetadata: null));

    private static string[] EffectiveTags(Feature feature, string id) =>
        ScenarioTags.Effective(feature, feature.Scenarios.Single(s => s.Id == id));

    [Fact]
    public void Two_shards_where_only_one_lifts_a_tag_give_the_merged_feature_no_such_tag_and_every_scenario_keeps_its_own()
    {
        // the tests NDJSON lane lifts per shard the tags that shard's scenarios share, and they stay on the scenarios
        var lifted = Shard(new Feature
        {
            DisplayName = "Checkout", Tags = ["nightly"],
            Scenarios =
            [
                new Scenario { Id = "s1", DisplayName = "Pay", Result = ExecutionResult.Passed, Tags = ["nightly", "card"] },
                new Scenario { Id = "s2", DisplayName = "Refund", Result = ExecutionResult.Passed, Tags = ["nightly"] }
            ]
        });
        var other = Shard(new Feature
        {
            DisplayName = "Checkout",
            Scenarios = [new Scenario { Id = "s3", DisplayName = "Browse", Result = ExecutionResult.Passed, Tags = ["slow"] }]
        });

        foreach (MergeableReport[] shards in new[] { new[] { lifted, other }, new[] { other, lifted } })
        {
            var feature = Assert.Single(MergeableReportMerger.Merge(shards).Features);

            Assert.Null(feature.Tags);
            Assert.Equal(["nightly", "card"], EffectiveTags(feature, "s1"));
            Assert.Equal(["nightly"], EffectiveTags(feature, "s2"));
            Assert.Equal(["slow"], EffectiveTags(feature, "s3"));
        }
    }

    [Fact]
    public void A_merged_feature_keeps_the_tags_every_shard_gives_it_and_each_shard_s_others_move_onto_its_scenarios()
    {
        // shards of a class-level lane whose class's tags changed between them: no scenario gains or loses a tag
        var first = Shard(new Feature
        {
            DisplayName = "Orders", Tags = ["api", "smoke"],
            Scenarios = [new Scenario { Id = "a1", DisplayName = "Place", Result = ExecutionResult.Passed, Tags = ["fast"] }]
        });
        var second = Shard(new Feature
        {
            DisplayName = "Orders", Tags = ["Smoke", "v2"],
            Scenarios = [new Scenario { Id = "b1", DisplayName = "Cancel", Result = ExecutionResult.Passed }]
        });
        var third = Shard(new Feature
        {
            DisplayName = "Orders", Tags = ["SMOKE", "api"],
            Scenarios = [new Scenario { Id = "c1", DisplayName = "Track", Result = ExecutionResult.Passed }]
        });

        var feature = Assert.Single(MergeableReportMerger.Merge([first, second, third]).Features);

        // the first shard's spelling, as a chip takes the first met, where each shard spells it its own way
        Assert.Equal(["smoke"], feature.Tags);
        Assert.Equal(["api", "fast"], feature.Scenarios.Single(s => s.Id == "a1").Tags);
        Assert.Equal(["v2"], feature.Scenarios.Single(s => s.Id == "b1").Tags);
        Assert.Equal(["api"], feature.Scenarios.Single(s => s.Id == "c1").Tags);
        Assert.Equal(["smoke", "api", "fast"], EffectiveTags(feature, "a1"));
    }

    [Fact]
    public void Shards_that_agree_on_a_feature_s_tags_keep_them_on_the_feature()
    {
        var first = Shard(new Feature { DisplayName = "Orders", Tags = ["api"], Scenarios = [new Scenario { Id = "a1", DisplayName = "Place", Result = ExecutionResult.Passed }] });
        var second = Shard(new Feature { DisplayName = "Orders", Tags = ["api"], Scenarios = [new Scenario { Id = "b1", DisplayName = "Cancel", Result = ExecutionResult.Passed }] });

        var feature = Assert.Single(MergeableReportMerger.Merge([first, second]).Features);

        Assert.Equal(["api"], feature.Tags);
        Assert.All(feature.Scenarios, s => Assert.Null(s.Tags));
    }

    [Fact]
    public void Roundtrip_preserves_features_scenarios_and_steps()
    {
        var report = MergeableReportReader.Parse(SerializeA());

        Assert.Single(report.Features);
        var f = report.Features[0];
        Assert.Equal("Orders", f.DisplayName);
        Assert.Equal("/orders", f.Endpoint);
        Assert.Equal("Order flows", f.Description);
        Assert.Equal(["api"], f.Tags);
        Assert.Equal(2, f.Scenarios.Length);

        var s1 = f.Scenarios.Single(s => s.Id == "a1");
        Assert.Equal("Place order", s1.DisplayName);
        Assert.Equal(ExecutionResult.Passed, s1.Result);
        Assert.True(s1.IsHappyPath);
        Assert.Equal(TimeSpan.FromSeconds(2), s1.Duration);
        Assert.Equal(["smoke"], s1.Tags);

        var s2 = f.Scenarios.Single(s => s.Id == "a2");
        Assert.Equal(ExecutionResult.Failed, s2.Result);
        Assert.Equal("boom", s2.ErrorMessage);
        Assert.NotNull(s2.Steps);
        Assert.Equal("Given", s2.Steps![0].Keyword);
        Assert.Equal("an order", s2.Steps[0].Text);
        Assert.Equal(ExecutionResult.Passed, s2.Steps[0].Status);
    }

    [Fact]
    public void Roundtrip_preserves_diagrams_relationships_flow_and_ci()
    {
        var report = MergeableReportReader.Parse(SerializeA());

        Assert.Equal(2, report.Diagrams.Length);
        Assert.Contains(report.Diagrams, d => d.TestRuntimeId == "a1" && d.CodeBehind.Contains("A->B"));

        var rel = Assert.Single(report.ComponentRelationships);
        Assert.Equal("Test", rel.Caller);
        Assert.Equal("OrdersApi", rel.Service);
        Assert.Equal(3, rel.CallCount);
        Assert.Contains("POST /orders", rel.Methods);

        Assert.True(report.InternalFlowSegments.ContainsKey("iflow-x"));
        Assert.Equal(WholeTestFlowVisualization.Both, report.WholeTestVisualization);
        Assert.True(report.WholeTestFlow.ContainsKey("a1"));
        Assert.Equal(4, report.WholeTestFlow["a1"].SpanCount);

        Assert.NotNull(report.CiMetadata);
        Assert.Equal(CiEnvironment.GitHubActions, report.CiMetadata!.Provider);
        Assert.Equal("main", report.CiMetadata.Branch);
        Assert.Equal("abc123", report.CiMetadata.CommitSha);
        // Run identity survives the round trip. The reader is the half that has silently dropped
        // fields before - a written-but-never-read field is invisible until a merged report is used.
        Assert.Equal("1", report.CiMetadata.RunId);
        Assert.Equal("2", report.CiMetadata.RunAttempt);
    }

    [Fact]
    public void Reader_rejects_non_mergeable_report()
    {
        var standard = ReportGenerator.GenerateTestRunReportData(
            FeaturesA(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "standard_for_merge_reject.json",
            DataFormat.Json);
        var content = File.ReadAllText(standard);

        Assert.Throws<FormatException>(() => MergeableReportReader.Parse(content));
    }

    [Fact]
    public void Merge_combines_features_diagrams_relationships_and_times()
    {
        var reportA = MergeableReportReader.Parse(SerializeA());

        // Report B: same feature name "Orders" with a new scenario + a second feature, overlapping relationship.
        var featuresB = new[]
        {
            new Feature { DisplayName = "Orders", Scenarios = [ new Scenario { Id = "b1", DisplayName = "Refund order", Result = ExecutionResult.Passed } ] },
            new Feature { DisplayName = "Inventory", Scenarios = [ new Scenario { Id = "b2", DisplayName = "Adjust stock", Result = ExecutionResult.Passed } ] }
        };
        var jsonB = ReportGenerator.GenerateMergeableReportJson(
            featuresB,
            new DateTime(2026, 1, 1, 9, 50, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 10, 0, DateTimeKind.Utc),
            new[] { new DiagramAsCode("b1", "", "@startuml\nE->F\n@enduml") }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships:
            [
                new ComponentRelationship("Test", "OrdersApi", "HTTP", new HashSet<string> { "DELETE /orders" }, 2, 1, "http"),
                new ComponentRelationship("OrdersApi", "InventoryDb", "SQL", new HashSet<string> { "SELECT" }, 5, 1, "sql")
            ],
            internalFlowSegmentData: new Dictionary<string, object> { ["iflow-y"] = new { title = "t2", content = "<div>flow2</div>" } },
            wholeTestFlow: new Dictionary<string, WholeTestFlowFragment> { ["b1"] = new("<div>act2</div>", "", 2) },
            WholeTestFlowVisualization.None,
            ciMetadata: null);
        var reportB = MergeableReportReader.Parse(jsonB);

        var merged = MergeableReportMerger.Merge([reportA, reportB]);

        // Features: "Orders" recombined (a1, a2, b1) + "Inventory" (b2), alphabetical order.
        Assert.Equal(["Inventory", "Orders"], merged.Features.Select(f => f.DisplayName).ToArray());
        var orders = merged.Features.Single(f => f.DisplayName == "Orders");
        Assert.Equal(["a1", "a2", "b1"], orders.Scenarios.Select(s => s.Id).OrderBy(x => x).ToArray());

        // Diagrams unioned.
        Assert.Equal(3, merged.Diagrams.Length);

        // Relationships: OrdersApi caller "Test" merged (callCount 3+2=5, methods unioned), InventoryDb added.
        var ordersRel = merged.ComponentRelationships.Single(r => r.Service == "OrdersApi");
        Assert.Equal(5, ordersRel.CallCount);
        Assert.Equal(3, ordersRel.TestCount);
        Assert.Contains("POST /orders", ordersRel.Methods);
        Assert.Contains("DELETE /orders", ordersRel.Methods);
        Assert.Contains(merged.ComponentRelationships, r => r.Service == "InventoryDb");

        // Times min/max.
        Assert.Equal(new DateTime(2026, 1, 1, 9, 50, 0, DateTimeKind.Utc), merged.StartTime);
        Assert.Equal(new DateTime(2026, 1, 1, 10, 10, 0, DateTimeKind.Utc), merged.EndTime);

        // Flow maps unioned, visualization taken from the report that had one.
        Assert.True(merged.InternalFlowSegments.ContainsKey("iflow-x"));
        Assert.True(merged.InternalFlowSegments.ContainsKey("iflow-y"));
        Assert.True(merged.WholeTestFlow.ContainsKey("a1"));
        Assert.True(merged.WholeTestFlow.ContainsKey("b1"));
        Assert.Equal(WholeTestFlowVisualization.Both, merged.WholeTestVisualization);

        // CI metadata taken from the report that captured it.
        Assert.NotNull(merged.CiMetadata);
        Assert.Equal("main", merged.CiMetadata!.Branch);
    }

    [Fact]
    public void A_shard_written_off_ci_reads_back_as_no_ci_at_all()
    {
        // Since 3.1.0 every data file writes the `ciMetadata` object whether or not there was a CI
        // to read, so a consumer never has to handle a missing shape. Null is still what "no CI"
        // means downstream, though - the HTML summary gates its CI table on `ciMetadata is not null`
        // - so a provider of None has to collapse back to null here, or every merged report built on
        // a laptop grows a "CI (None)" table.
        var json = ReportGenerator.GenerateMergeableReportJson(
            FeaturesA(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null,
            componentRelationships: [],
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null);

        using (var document = JsonDocument.Parse(json))
        {
            var ci = document.RootElement.GetProperty("ciMetadata");
            Assert.Equal(JsonValueKind.Object, ci.ValueKind);
            Assert.Equal("None", ci.GetProperty("provider").GetString());
            // The superset claim: everything the standard file carries is carried here too.
            Assert.Contains(".NET", document.RootElement.GetProperty("environment").GetProperty("runtime").GetString());
        }

        Assert.Null(MergeableReportReader.Parse(json).CiMetadata);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("cs-CZ")]
    public void A_relationships_methods_are_written_in_one_order_whatever_the_machines_culture(string culture)
    {
        // The file listed them in the machine culture's order (Czech sorts "ch" after "h"), so two shards of one run
        // written on different machines disagreed, as did the component diagram drawn from either.
        string[] methods = ["hset orders", "chunk orders", "Get orders", "delete orders"];
        var json = CultureRun.Under(culture, () => ReportGenerator.GenerateMergeableReportJson(
            FeaturesA(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null,
            componentRelationships: [new ComponentRelationship("OrdersApi", "Cache", "Redis", [.. methods], 4, 1, "redis")],
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null));

        using var document = JsonDocument.Parse(json);
        var relationship = Assert.Single(document.RootElement.GetProperty("componentRelationships").EnumerateArray());
        var written = relationship.EnumerateObject().Single(p => p.Name.Equals("methods", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Equal(methods.Order(StringComparer.Ordinal), written.EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public void Merge_requires_at_least_one_report()
    {
        Assert.Throws<ArgumentException>(() => MergeableReportMerger.Merge([]));
    }

    [Fact]
    public void Roundtrip_preserves_full_step_detail()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a muffin recipe",
            Status = ExecutionResult.Bypassed,
            BypassReason = "not implemented",
            Duration = TimeSpan.FromSeconds(0.25),
            DocString = "{ \"k\": 1 }",
            DocStringMediaType = "application/json",
            Comments = ["note one", "note two"],
            TextSegments =
            [
                StepTextSegment.Literal("a muffin "),
                StepTextSegment.Param("size", new InlineParameterValue("large", "small", VerificationStatus.Failure)),
                StepTextSegment.TableRef("recipe", "see table")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "qty",
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue("2", null, VerificationStatus.Success)
                },
                new StepParameter
                {
                    Name = "recipe",
                    Kind = StepParameterKind.Tabular,
                    TabularValue = new TabularParameterValue(
                        [new TabularColumn("Name", true), new TabularColumn("Flour", false)],
                        [new TabularRow(TableRowType.Matching,
                            [new TabularCell("Classic", null, VerificationStatus.NotApplicable),
                             new TabularCell("Plain", "Plain", VerificationStatus.Success)])],
                        IsLinkedOutput: true)
                },
                new StepParameter
                {
                    Name = "tree",
                    Kind = StepParameterKind.Tree,
                    TreeValue = new TreeParameterValue(
                        new TreeNode("$", "root", "", null, VerificationStatus.NotApplicable,
                            [new TreeNode("$.a", "a", "1", "1", VerificationStatus.Success, null)]))
                }
            ],
            SubSteps =
            [
                new ScenarioStep { Keyword = "And", Text = "sugar", Status = ExecutionResult.Passed,
                    TextSegments = [StepTextSegment.Literal("sugar")] }
            ]
        };

        var features = new[]
        {
            new Feature { DisplayName = "Baking", Scenarios = [ new Scenario { Id = "b1", DisplayName = "Bake", Result = ExecutionResult.Passed, Steps = [step] } ] }
        };

        var json = ReportGenerator.GenerateMergeableReportJson(
            features, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
            diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);

        var report = MergeableReportReader.Parse(json);
        var s = report.Features[0].Scenarios[0].Steps![0];

        Assert.Equal(ExecutionResult.Bypassed, s.Status);
        Assert.Equal("not implemented", s.BypassReason);
        Assert.Equal("{ \"k\": 1 }", s.DocString);
        Assert.Equal("application/json", s.DocStringMediaType);
        Assert.Equal(["note one", "note two"], s.Comments!);

        // Text segments
        Assert.NotNull(s.TextSegments);
        Assert.Equal(3, s.TextSegments!.Length);
        Assert.Equal("a muffin ", s.TextSegments[0].Text);
        Assert.Equal("size", s.TextSegments[1].ParameterName);
        Assert.Equal("large", s.TextSegments[1].Parameter!.Value);
        Assert.Equal(VerificationStatus.Failure, s.TextSegments[1].Parameter!.Status);
        Assert.Equal("recipe", s.TextSegments[2].TableReference);
        Assert.Equal("see table", s.TextSegments[2].TableReferenceFormattedValue);

        // Parameters: inline, tabular, tree
        Assert.NotNull(s.Parameters);
        Assert.Equal(3, s.Parameters!.Length);

        var inline = s.Parameters.Single(p => p.Name == "qty");
        Assert.Equal(StepParameterKind.Inline, inline.Kind);
        Assert.Equal("2", inline.InlineValue!.Value);
        Assert.Equal(VerificationStatus.Success, inline.InlineValue.Status);

        var tabular = s.Parameters.Single(p => p.Name == "recipe");
        Assert.True(tabular.TabularValue!.IsLinkedOutput);
        Assert.Equal(2, tabular.TabularValue.Columns.Length);
        Assert.True(tabular.TabularValue.Columns[0].IsKey);
        Assert.Equal(TableRowType.Matching, tabular.TabularValue.Rows[0].Type);
        Assert.Equal("Classic", tabular.TabularValue.Rows[0].Values[0].Value);

        var tree = s.Parameters.Single(p => p.Name == "tree");
        Assert.Equal("root", tree.TreeValue!.Root.Node);
        Assert.Single(tree.TreeValue.Root.Children!);
        Assert.Equal("1", tree.TreeValue.Root.Children![0].Value);

        // Sub-step detail preserved
        Assert.NotNull(s.SubSteps);
        Assert.Equal("sugar", s.SubSteps![0].TextSegments![0].Text);
    }

    [Fact]
    public void Render_produces_combined_html_with_features_component_diagram_and_flow()
    {
        var reportA = MergeableReportReader.Parse(SerializeA());
        var featuresB = new[]
        {
            new Feature { DisplayName = "Inventory", Scenarios = [ new Scenario { Id = "b2", DisplayName = "Adjust stock", Result = ExecutionResult.Passed } ] }
        };
        var jsonB = ReportGenerator.GenerateMergeableReportJson(
            featuresB,
            new DateTime(2026, 1, 1, 9, 50, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 10, 0, DateTimeKind.Utc),
            new[] { new DiagramAsCode("b2", "", "@startuml\nE->F\n@enduml") }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships: [ new ComponentRelationship("OrdersApi", "InventoryDb", "SQL", new HashSet<string> { "SELECT" }, 5, 1, "sql") ],
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null);
        var merged = MergeableReportMerger.Merge([reportA, MergeableReportReader.Parse(jsonB)]);

        var outputPath = Path.Combine(Path.GetTempPath(), "kronikol-merge-test", "Combined.html");
        var written = MergeableReportRenderer.Render(merged, outputPath, title: "Combined Report");
        var html = File.ReadAllText(written);

        Assert.Contains("Combined Report", html);
        Assert.Contains("Orders", html);
        Assert.Contains("Inventory", html);
        Assert.Contains("Place order", html);
        Assert.Contains("Adjust stock", html);
        // Component diagram embedded (since 5.0.0 its tab, rendered when component PlantUML is supplied).
        Assert.Contains("<button class=\"report-tab\" id=\"tab-components\"", html);
        // Internal-flow popup data present, as the element the popup decodes: the popup script itself names the old
        // global (it still reads one a page sets), so the bare name would pass with no data at all (the 3.0.82 rule).
        Assert.Contains("<script id=\"iflow-segments\" type=\"application/json\">{", html);
        // Whole-test-flow fragment injected verbatim.
        Assert.Contains("<div>flame</div>", html);

        File.Delete(written);
    }

    [Fact]
    public void A_merged_reports_component_edge_is_held_to_the_label_cap()
    {
        // kronikol merge draws the component panel from the union of each shard's relationships, so two shards of 33
        // tables each make one edge of 66: its label, as the page renders it, is cut by whole entries to the edge's
        // measured cap and keeps its counts (#162, plans/LONG_COMPONENT_EDGE_PLAN.md R1).
        static string Shard(int first) => ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = "Archive", Scenarios = [new Scenario { Id = $"s{first}", DisplayName = $"Archive orders from {first}", Result = ExecutionResult.Passed }] }],
            new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 10, 1, 0, DateTimeKind.Utc),
            new[] { new DiagramAsCode($"s{first}", "", "@startuml\nA->B\n@enduml") }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships:
            [
                new ComponentRelationship("Caller", "Warehouse", "ClickHouse",
                    [.. Enumerable.Range(first, 33).Select(i => $"INSERT INTO orders_archive_{i:000}")], 33, 1, "ClickHouse")
            ],
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null);
        var merged = MergeableReportMerger.Merge([MergeableReportReader.Parse(Shard(1)), MergeableReportReader.Parse(Shard(34))]);

        var outputPath = Path.Combine(Path.GetTempPath(), "kronikol-merge-edge-test", "Combined.html");
        var written = MergeableReportRenderer.Render(merged, outputPath, title: "Combined Report");
        var html = File.ReadAllText(written);
        File.Delete(written);

        // The page's diagram sources, gzipped in its puml-data map, the component panel's among them.
        var data = System.Text.RegularExpressions.Regex.Match(html, "<script id=\"puml-data\" type=\"application/json\">(.*?)</script>");
        Assert.True(data.Success, "the merged report carries no diagram data");
        var sources = JsonDocument.Parse(data.Groups[1].Value).RootElement.EnumerateObject().Select(p =>
        {
            using var gzip = new System.IO.Compression.GZipStream(
                new MemoryStream(Convert.FromBase64String(p.Value.GetString()!)), System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return reader.ReadToEnd();
        });
        var edge = sources.SelectMany(src => src.Split('\n')).Select(l => l.TrimEnd('\r'))
            .Single(l => l.StartsWith("caller -[", StringComparison.Ordinal));
        var label = edge[(edge.IndexOf(" : \"", StringComparison.Ordinal) + 4)..^1];
        Assert.True(label.Length <= Kronikol.PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars, $"{label.Length} characters");
        Assert.Contains("INSERT INTO orders_archive_001", label, StringComparison.Ordinal);
        Assert.EndsWith(" more - 66 calls across 2 tests", label.Replace("\\n", " ", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_reports_component_edges_take_the_reports_dependency_colours()
    {
        // The run report and ComponentDiagram.html colour an edge from ReportConfigurationOptions.DependencyColors when
        // the component options name no colours of their own; a merged report drew the default palette for the same
        // options (plans/LONG_COMPONENT_EDGE_PLAN.md §8).
        var json = ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = "Inventory", Scenarios = [new Scenario { Id = "c1", DisplayName = "Count stock", Result = ExecutionResult.Passed }] }],
            new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 12, 10, 1, 0, DateTimeKind.Utc),
            new[] { new DiagramAsCode("c1", "", "@startuml\nA->B\n@enduml") }.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships: [new ComponentRelationship("OrdersApi", "InventoryDb", "SQL", new HashSet<string> { "SELECT" }, 5, 1, "sql")],
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null);
        var merged = MergeableReportMerger.Merge([MergeableReportReader.Parse(json)]);

        var outputPath = Path.Combine(Path.GetTempPath(), "kronikol-merge-colour-test", "Combined.html");
        var written = MergeableReportRenderer.Render(merged, outputPath, title: "Combined Report",
            options: new ReportConfigurationOptions { DependencyColors = new() { ["sql"] = "#123456" } });
        var html = File.ReadAllText(written);
        File.Delete(written);

        var edge = ComponentSources(html).SelectMany(src => src.Split('\n')).Select(l => l.TrimEnd('\r'))
            .Single(l => l.StartsWith("ordersApi -", StringComparison.Ordinal));
        Assert.StartsWith("ordersApi -[#123456]-> inventoryDb", edge, StringComparison.Ordinal);
    }

    /// <summary>The diagram sources a report page carries, gunzipped from its puml-data map.</summary>
    private static IEnumerable<string> ComponentSources(string html)
    {
        var data = System.Text.RegularExpressions.Regex.Match(html, "<script id=\"puml-data\" type=\"application/json\">(.*?)</script>");
        Assert.True(data.Success, "the report carries no diagram data");
        return JsonDocument.Parse(data.Groups[1].Value).RootElement.EnumerateObject().Select(p =>
        {
            using var gzip = new System.IO.Compression.GZipStream(
                new MemoryStream(Convert.FromBase64String(p.Value.GetString()!)), System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return reader.ReadToEnd();
        }).ToList();
    }
}
