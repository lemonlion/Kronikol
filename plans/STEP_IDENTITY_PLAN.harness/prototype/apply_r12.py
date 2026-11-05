#!/usr/bin/env python
# R12, on R11 (c7252f6c). usage: apply_r12.py <worktree>
# R11's pass-over took any `b` row: a Cucumber scenario under a background with hooks kept has a bar arriving out of order for
# that row, and it broke CucumberAttachmentIngestTests.With_hooks_kept_the_step_markers_match_the_steps (expected 2, actual 5).
# So a row now says it draws no bar, which only the tests file's reader knows. And the matcher is #225's R2's, in its words:
# R11's lost 4 of that plan's 18 labels (results/matchers-compared.txt in its harness).
import pathlib, sys

root = pathlib.Path(sys.argv[1])


def sub(path, old, new):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    t = raw.replace("\r\n", "\n")
    assert t.count(old) == 1, (path, t.count(old), old[:90])
    t = t.replace(old, new)
    p.write_bytes((t.replace("\n", "\r\n") if crlf else t).encode("utf-8"))


g = "src/Kronikol/Reports/ReportGenerator.cs"
sub(g, """                        // A tests file's background step draws no bar (TestRunRecord.IsDiagramMarker), so a bar that is not
                        // its own passes over it.
                        while (stepMarkerCount < ordered.Count && ordered[stepMarkerCount].Path.StartsWith('b')
                               && !StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Text))
                            stepMarkerCount++;
""", """                        // A tests file's step that draws no bar: a bar that is not its own passes over it. A capture can have
                        // drawn one after all, as a store projected from an in-process run has, and then it matches.
                        while (stepMarkerCount < ordered.Count && ordered[stepMarkerCount].DrawsNoBar
                               && !StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Text))
                            stepMarkerCount++;
""")
sub(g, """    private static List<(string Path, string Text)> OrderedStepPaths(Scenario scenario)
    {
        var ordered = new List<(string, string)>();
        for (var i = 0; i < (scenario.BackgroundSteps?.Length ?? 0); i++)
            ordered.Add(($"b{i}", scenario.BackgroundSteps![i].Text));
        for (var i = 0; i < (scenario.Steps?.Length ?? 0); i++)
            ordered.Add(($"{i}", scenario.Steps![i].Text));
        return ordered;
""", """    private static List<(string Path, string Text, bool DrawsNoBar)> OrderedStepPaths(Scenario scenario)
    {
        var ordered = new List<(string, string, bool)>();
        for (var i = 0; i < (scenario.BackgroundSteps?.Length ?? 0); i++)
            ordered.Add(($"b{i}", scenario.BackgroundSteps![i].Text, scenario.BackgroundSteps[i].DrawsNoBar));
        for (var i = 0; i < (scenario.Steps?.Length ?? 0); i++)
            ordered.Add(($"{i}", scenario.Steps![i].Text, scenario.Steps[i].DrawsNoBar));
        return ordered;
""")
sub(g, """        // A bar wraps a long text, writes a break as the two characters \\n, can mark a character up and cuts what is left, so
        // what both are sure to hold is the text's opening with the breaks taken out.
        var marker = Comparable(plantUml);
        var text = Comparable(stepText);
        if (text.Length > StepMarkerOpeningChars)
            text = text[..StepMarkerOpeningChars];
        return marker.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private const int StepMarkerOpeningChars = 60;

    private static string Comparable(string text) =>
        string.Concat(text.Replace("\\\\n", "", StringComparison.Ordinal).Replace("<U+200B>", "", StringComparison.Ordinal)
            .Where(c => !char.IsWhiteSpace(c) && c != '~'));
""", """        var firstLine = stepText.Replace("\\r", "").Trim().Split('\\n')[0];
        var wanted = FoldStepBar(Kronikol.PlantUml.PlantUmlCreator.EscapeOneLineMarkup(firstLine));
        if (wanted.Length > StepMarkerMatchChars)
            wanted = wanted[..StepMarkerMatchChars];

        return FoldStepBar(plantUml).Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>As much of a step's text as the matcher asks a bar for: well inside the shortest cap a bar has.</summary>
    private const int StepMarkerMatchChars = 400;

    /// <summary>A bar's text with what wrapping adds or moves taken out: a literal <c>\\n</c>, a line feed and a space.</summary>
    private static string FoldStepBar(string text) =>
        text.Replace("\\\\n", "", StringComparison.Ordinal).Replace("\\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
""")
sub("src/Kronikol/Reports/ScenarioStep.cs", """    internal bool DidNotRun { get; init; }
""", """    internal bool DidNotRun { get; init; }

    /// <summary>A tests file's step the replay draws no bar for (<see cref="Ingestion.TestRunRecord.IsDiagramMarker"/>); never written to a report.</summary>
    internal bool DrawsNoBar { get; init; }
""")
sub("src/Kronikol/Ingestion/FeatureSynthesizer.cs", """        return new ScenarioStep
        {
            Text = text,
            Keyword = record.Keyword,
            Status = record.Status is null ? null : MapStatus(record.Status),
""", """        return new ScenarioStep
        {
            DrawsNoBar = !record.IsDiagramMarker,
            Text = text,
            Keyword = record.Keyword,
            Status = record.Status is null ? null : MapStatus(record.Status),
""")
sub("tests/Kronikol.Tests/Reports/ReportAttributionTests.cs",
    """BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a shop" }, new ScenarioStep { Keyword = "And", Text = "a customer" }],""",
    """BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a shop", DrawsNoBar = true }, new ScenarioStep { Keyword = "And", Text = "a customer", DrawsNoBar = true }],""")
print("applied r12")
