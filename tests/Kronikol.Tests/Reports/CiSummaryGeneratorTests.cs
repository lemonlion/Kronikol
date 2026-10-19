using Kronikol.PlantUml;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class CiSummaryGeneratorTests
{
    private static readonly DateTime Start = new(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 4, 1, 10, 2, 34, DateTimeKind.Utc);

    private static Feature MakeFeature(string name, params Scenario[] scenarios) =>
        new() { DisplayName = name, Scenarios = scenarios };

    private static Scenario Passed(string name, string id = "") =>
        new() { Id = id == "" ? Guid.NewGuid().ToString() : id, DisplayName = name, Result = ExecutionResult.Passed };

    private static Scenario Failed(string name, string error = "Assertion failed", string? stack = "at Test.cs:line 1", string id = "") =>
        new() { Id = id == "" ? Guid.NewGuid().ToString() : id, DisplayName = name, Result = ExecutionResult.Failed, ErrorMessage = error, ErrorStackTrace = stack };

    private static Scenario Skipped(string name) =>
        new() { Id = Guid.NewGuid().ToString(), DisplayName = name, Result = ExecutionResult.Skipped };

    private static DefaultDiagramsFetcher.DiagramAsCode Diagram(string testId, string codeBehind = "@startuml\nA -> B\n@enduml") =>
        new(testId, "", codeBehind);

    private static string Encoded(string plantUml) => PlantUmlTextEncoder.Encode(plantUml);

    [Fact]
    public void GenerateMarkdown_all_passed_shows_passed_status()
    {
        var features = new[] { MakeFeature("Orders", Passed("Create order")) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        Assert.Contains("✅ Passed", markdown);
        Assert.DoesNotContain("❌", markdown);
    }

    [Fact]
    public void GenerateMarkdown_all_passed_includes_diagram_images_for_first_N_scenarios()
    {
        var id1 = "test-1";
        var id2 = "test-2";
        var features = new[]
        {
            MakeFeature("Orders", Passed("Create order", id1), Passed("Delete order", id2))
        };
        var code1 = "@startuml\nA -> B : create\n@enduml";
        var code2 = "@startuml\nC -> D : delete\n@enduml";
        var diagrams = new[] { Diagram(id1, code1), Diagram(id2, code2) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End);

        Assert.Contains("![diagram]", markdown);
        Assert.Contains(Encoded(code1), markdown);
        Assert.Contains(Encoded(code2), markdown);
    }

    [Fact]
    public void GenerateMarkdown_all_passed_respects_maxDiagrams_limit()
    {
        var scenarios = Enumerable.Range(1, 5).Select(i =>
        {
            var id = $"test-{i}";
            return (scenario: Passed($"Scenario {i}", id), diagram: Diagram(id, $"@startuml\nA -> B : step{i}\n@enduml"));
        }).ToArray();

        var features = new[] { MakeFeature("Feature", scenarios.Select(s => s.scenario).ToArray()) };
        var diagrams = scenarios.Select(s => s.diagram).ToArray();

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End, maxDiagrams: 2);

        Assert.Contains(Encoded("@startuml\nA -> B : step1\n@enduml"), markdown);
        Assert.Contains(Encoded("@startuml\nA -> B : step2\n@enduml"), markdown);
        Assert.DoesNotContain(Encoded("@startuml\nA -> B : step3\n@enduml"), markdown);
    }

    [Fact]
    public void GenerateMarkdown_all_passed_shows_truncation_notice_when_more_scenarios_than_max()
    {
        var scenarios = Enumerable.Range(1, 5).Select(i =>
        {
            var id = $"test-{i}";
            return (scenario: Passed($"Scenario {i}", id), diagram: Diagram(id));
        }).ToArray();

        var features = new[] { MakeFeature("Feature", scenarios.Select(s => s.scenario).ToArray()) };
        var diagrams = scenarios.Select(s => s.diagram).ToArray();

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End, maxDiagrams: 2);

        Assert.Contains("3 more", markdown);
        Assert.Contains("full report", markdown.ToLower());
    }

    [Fact]
    public void GenerateMarkdown_all_passed_counts_only_the_diagrams_it_did_not_show()
    {
        // Forty scenarios, three with a diagram, all three shown: the line said "37 more scenario(s) not shown", counting
        // every scenario that had no diagram to show (plans/LIST_CAPS_PLAN.md 3.10).
        var drawn = Enumerable.Range(1, 3).Select(i => (scenario: Passed($"Drawn {i}", $"d-{i}"), diagram: Diagram($"d-{i}"))).ToArray();
        var plain = Enumerable.Range(1, 37).Select(i => Passed($"Plain {i}", $"p-{i}")).ToArray();
        var features = new[] { MakeFeature("Feature", [.. drawn.Select(s => s.scenario), .. plain]) };
        var diagrams = drawn.Select(s => s.diagram).ToArray();

        var all = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End, maxDiagrams: 10);
        Assert.DoesNotContain("more scenario(s) not shown", all);

        // Past the cap, it counts the diagrams it did not show.
        var cut = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End, maxDiagrams: 2);
        Assert.Contains("*1 more scenario(s) not shown", cut);
    }

    [Fact]
    public void GenerateMarkdown_with_failures_shows_failed_status()
    {
        var features = new[] { MakeFeature("Orders", Failed("Bad order")) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        Assert.Contains("❌ Failed", markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_failures_shows_only_failed_scenario_diagrams()
    {
        var passedId = "passed-1";
        var failedId = "failed-1";
        var passedCode = "@startuml\nA -> B : pass\n@enduml";
        var failedCode = "@startuml\nA -> B : fail\n@enduml";
        var features = new[]
        {
            MakeFeature("Orders", Passed("Good order", passedId), Failed("Bad order", id: failedId))
        };
        var diagrams = new[] { Diagram(passedId, passedCode), Diagram(failedId, failedCode) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End);

        Assert.Contains(Encoded(failedCode), markdown);
        Assert.DoesNotContain(Encoded(passedCode), markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_failures_includes_error_message_and_stack_trace()
    {
        var features = new[] { MakeFeature("Orders", Failed("Bad order", "Expected 200 but got 500", "at OrderTests.cs:line 42")) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        Assert.Contains("Expected 200 but got 500", markdown);
        Assert.Contains("at OrderTests.cs:line 42", markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_failures_respects_maxDiagrams_cap()
    {
        var scenarios = Enumerable.Range(1, 5).Select(i =>
        {
            var id = $"fail-{i}";
            return (scenario: Failed($"Failure {i}", id: id), diagram: Diagram(id, $"@startuml\nA -> B : fail{i}\n@enduml"));
        }).ToArray();

        var features = new[] { MakeFeature("Feature", scenarios.Select(s => s.scenario).ToArray()) };
        var diagrams = scenarios.Select(s => s.diagram).ToArray();

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End, maxDiagrams: 2);

        Assert.Contains(Encoded("@startuml\nA -> B : fail1\n@enduml"), markdown);
        Assert.Contains(Encoded("@startuml\nA -> B : fail2\n@enduml"), markdown);
        Assert.DoesNotContain(Encoded("@startuml\nA -> B : fail3\n@enduml"), markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_failures_does_not_include_passing_diagrams()
    {
        var passedId = "passed-1";
        var failedId = "failed-1";
        var passedCode = "@startuml\nA -> B : passed\n@enduml";
        var failedCode = "@startuml\nA -> B : failed\n@enduml";
        var features = new[]
        {
            MakeFeature("Orders", Passed("Good", passedId), Failed("Bad", id: failedId))
        };
        var diagrams = new[] { Diagram(passedId, passedCode), Diagram(failedId, failedCode) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End);

        Assert.DoesNotContain(Encoded(passedCode), markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_failures_shows_truncation_notice_when_more_failures_than_max()
    {
        var scenarios = Enumerable.Range(1, 5).Select(i =>
        {
            var id = $"fail-{i}";
            return (scenario: Failed($"Failure {i}", id: id), diagram: Diagram(id));
        }).ToArray();

        var features = new[] { MakeFeature("Feature", scenarios.Select(s => s.scenario).ToArray()) };
        var diagrams = scenarios.Select(s => s.diagram).ToArray();

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End, maxDiagrams: 2);

        Assert.Contains("3 more", markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_no_features_returns_minimal_summary()
    {
        var markdown = CiSummaryGenerator.GenerateMarkdown([], [], [], Start, End);

        Assert.Contains("# Diagrammed Test Run Summary", markdown);
        Assert.Contains("0", markdown);
    }

    [Fact]
    public void GenerateMarkdown_formats_duration_seconds_correctly()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 45, DateTimeKind.Utc);
        var features = new[] { MakeFeature("F", Passed("S")) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], start, end);

        Assert.Contains("45s", markdown);
    }

    [Fact]
    public void GenerateMarkdown_formats_duration_minutes_correctly()
    {
        var markdown = CiSummaryGenerator.GenerateMarkdown(new[] { MakeFeature("F", Passed("S")) }, [], [], Start, End);

        Assert.Contains("2m 34s", markdown);
    }

    [Fact]
    public void GenerateMarkdown_escapes_pipe_characters_in_scenario_names()
    {
        var features = new[] { MakeFeature("Orders", Passed("Scenario with | pipe")) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        // Pipe characters must be escaped in Markdown tables
        Assert.DoesNotContain("| pipe |", markdown);
    }

    [Fact]
    public void GenerateMarkdown_scenario_with_no_matching_diagram_omits_image()
    {
        var features = new[] { MakeFeature("Orders", Passed("No diagram scenario", "orphan-id")) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        Assert.DoesNotContain("![", markdown);
    }

    [Fact]
    public void GenerateMarkdown_with_skipped_shows_correct_counts()
    {
        var features = new[]
        {
            MakeFeature("Orders", Passed("Good"), Skipped("WIP"), Failed("Bad"))
        };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        Assert.Contains("1", markdown); // passed
        Assert.Contains("1", markdown); // failed
        Assert.Contains("1", markdown); // skipped
        Assert.Contains("3", markdown); // total
    }

    [Fact]
    public void GenerateMarkdown_failed_scenario_has_red_cross_prefix()
    {
        var features = new[] { MakeFeature("Orders", Failed("Bad order")) };
        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        // Failed scenarios should have ❌ prefix in the summary title
        Assert.Contains("<details><summary>❌ <strong>Orders — Bad order</strong></summary>", markdown);
        // Should NOT use div/style approach (GitHub strips style attrs)
        Assert.DoesNotContain("color: darkred", markdown);
    }

    [Fact]
    public void GenerateMarkdown_failed_scenario_stack_trace_is_open_by_default()
    {
        var features = new[] { MakeFeature("Orders", Failed("Bad order", "err", "at Test.cs:1")) };
        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        Assert.Contains("<details open><summary>Stack Trace</summary>", markdown);
    }

    [Fact]
    public void GenerateMarkdown_failed_scenario_is_collapsed_by_default()
    {
        var features = new[] { MakeFeature("Orders", Failed("Bad order")) };
        var markdown = CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, End);

        // Should NOT have <details open> for the outer scenario
        Assert.DoesNotContain("<details open><summary>❌", markdown);
        Assert.Contains("<details><summary>❌", markdown);
    }

    [Fact]
    public void DeactivateUrls_breaks_http_and_https_url_patterns()
    {
        var input = "@startuml\nnote left\nhttps://example.com/path\nhttp://other.com\nend note\n@enduml";
        var result = CiSummaryGenerator.DeactivateUrls(input);

        Assert.DoesNotContain("https://", result);
        Assert.DoesNotContain("http://", result);
        Assert.Contains("https&#58;//example.com/path", result);
        Assert.Contains("http&#58;//other.com", result);
    }

    [Fact]
    public void DeactivateUrls_preserves_non_url_content()
    {
        var input = "@startuml\nA -> B : hello\n@enduml";
        var result = CiSummaryGenerator.DeactivateUrls(input);
        Assert.Equal(input, result);
    }

    // ─── PlantUML source text blocks ────────────────────────

    [Fact]
    public void GenerateMarkdown_truncated_diagram_includes_plantuml_source_after_full_image()
    {
        var id = "test-1";
        var code = "@startuml\nA -> B : hello\n@enduml";
        var truncatedCode = "@startuml\nA -> B\n@enduml";
        var features = new[] { MakeFeature("Orders", Passed("Create order", id)) };
        var fullDiagrams = new[] { Diagram(id, code) };
        var truncatedDiagrams = new[] { Diagram(id, truncatedCode) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, Start, End);

        // Should have a PlantUML source section after the full diagram image
        Assert.Contains("Full Sequence Diagram - PlantUML", markdown);
        Assert.Contains("```plantuml", markdown);
        Assert.Contains(code, markdown);
        Assert.Contains("```", markdown);
    }

    [Fact]
    public void GenerateMarkdown_truncated_multipart_includes_plantuml_source_for_each_part()
    {
        var id = "test-1";
        var code1 = "@startuml\nA -> B : part1\n@enduml";
        var code2 = "@startuml\nC -> D : part2\n@enduml";
        var trunc1 = "@startuml\nA -> B\n@enduml";
        var trunc2 = "@startuml\nC -> D\n@enduml";
        var features = new[] { MakeFeature("Orders", Passed("Create order", id)) };
        var fullDiagrams = new[] { Diagram(id, code1), Diagram(id, code2) };
        var truncatedDiagrams = new[] { Diagram(id, trunc1), Diagram(id, trunc2) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, Start, End);

        Assert.Contains("Full Sequence Diagram (Part 1) - PlantUML", markdown);
        Assert.Contains("Full Sequence Diagram (Part 2) - PlantUML", markdown);
        Assert.Contains(code1, markdown);
        Assert.Contains(code2, markdown);
    }

    [Fact]
    public void GenerateMarkdown_non_truncated_includes_plantuml_source()
    {
        var id = "test-1";
        var code = "@startuml\nA -> B : hello\n@enduml";
        var features = new[] { MakeFeature("Orders", Passed("Create order", id)) };
        var diagrams = new[] { Diagram(id, code) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End);

        Assert.Contains("Sequence Diagram - PlantUML", markdown);
        Assert.Contains("```plantuml", markdown);
        Assert.Contains(code, markdown);
    }

    [Fact]
    public void GenerateMarkdown_non_truncated_multipart_includes_plantuml_source_for_each_part()
    {
        var id = "test-1";
        var code1 = "@startuml\nA -> B : part1\n@enduml";
        var code2 = "@startuml\nC -> D : part2\n@enduml";
        var features = new[] { MakeFeature("Orders", Passed("Create order", id)) };
        var diagrams = new[] { Diagram(id, code1), Diagram(id, code2) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, End);

        Assert.Contains("Sequence Diagram (Part 1) - PlantUML", markdown);
        Assert.Contains("Sequence Diagram (Part 2) - PlantUML", markdown);
        Assert.Contains(code1, markdown);
        Assert.Contains(code2, markdown);
    }

    [Fact]
    public void GenerateMarkdown_plantuml_source_is_inside_collapsed_details()
    {
        var id = "test-1";
        var code = "@startuml\nA -> B : hello\n@enduml";
        var truncatedCode = "@startuml\nA -> B\n@enduml";
        var features = new[] { MakeFeature("Orders", Passed("Create order", id)) };
        var fullDiagrams = new[] { Diagram(id, code) };
        var truncatedDiagrams = new[] { Diagram(id, truncatedCode) };

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, Start, End);

        // PlantUML source block should be in a collapsed <details> (no "open")
        Assert.Contains("<details><summary>Full Sequence Diagram - PlantUML</summary>", markdown);
    }
}
