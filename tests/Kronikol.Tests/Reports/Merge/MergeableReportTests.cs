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
            Labels = ["api"],
            Scenarios =
            [
                new Scenario { Id = "a1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2), IsHappyPath = true, Categories = ["smoke"] },
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
            new CiMetadata(CiEnvironment.GitHubActions, "42", "main", "abc123", "https://gh/run/1", "owner/repo", "1"));

    [Fact]
    public void Roundtrip_preserves_features_scenarios_and_steps()
    {
        var report = MergeableReportReader.Parse(SerializeA());

        Assert.Single(report.Features);
        var f = report.Features[0];
        Assert.Equal("Orders", f.DisplayName);
        Assert.Equal("/orders", f.Endpoint);
        Assert.Equal("Order flows", f.Description);
        Assert.Equal(["api"], f.Labels!);
        Assert.Equal(2, f.Scenarios.Length);

        var s1 = f.Scenarios.Single(s => s.Id == "a1");
        Assert.Equal("Place order", s1.DisplayName);
        Assert.Equal(ExecutionResult.Passed, s1.Result);
        Assert.True(s1.IsHappyPath);
        Assert.Equal(TimeSpan.FromSeconds(2), s1.Duration);
        Assert.Equal(["smoke"], s1.Categories!);

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
        // Component diagram embedded (toggle button rendered when component PlantUML is supplied).
        Assert.Contains("toggle_component_diagram", html);
        // Internal-flow popup data present.
        Assert.Contains("__iflowSegments", html);
        // Whole-test-flow fragment injected verbatim.
        Assert.Contains("<div>flame</div>", html);

        File.Delete(written);
    }
}
