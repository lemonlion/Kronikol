"""R2 as it would land, on a tree with published's walk: a response's step (F9), the walk's matcher (F12, #128), rows that draw no bar (F15), a step with no text, and identical rows' sub-steps (F16).

Usage: apply_r2.py <worktree> [--on-layer]
--on-layer: the tree holds this plan's layer, whose ThroughTheirCalls gives a response its request's step, so F9 is there and not in the walk.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
on_layer = "--on-layer" in sys.argv


def edit(relative, edits):
    p = root / relative
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in edits:
        assert s.count(old) == 1, (relative, old[:90], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


# --- F9: a response takes the step of the request it answers, the latest with that id.
if on_layer:
    edit("src/Kronikol/Reports/ReportGenerator.cs", [(
        """                requestAt.TryAdd(record.RequestResponseId, i);
""",
        """                requestAt[record.RequestResponseId] = i;
""")])
else:
    edit("src/Kronikol/Reports/ReportGenerator.cs", [(
        """                if (!log.IsDiagramMarker)
                {
                    paths.Add(current);
                    interactionIndex++;
                    continue;
                }
""",
        """                if (!log.IsDiagramMarker)
                {
                    // A call in flight across a bar is one call: a response takes the step of the request it answers.
                    if (log.Type == RequestResponseType.Request)
                        requestPaths[log.RequestResponseId] = current;
                    paths.Add(log.Type == RequestResponseType.Response && requestPaths.TryGetValue(log.RequestResponseId, out var ofRequest) ? ofRequest : current);
                    interactionIndex++;
                    continue;
                }
"""), (
        """            var stepMarkerCount = 0;
            var interactionIndex = 0;
""",
        """            var stepMarkerCount = 0;
            var interactionIndex = 0;
            var requestPaths = new Dictionary<Guid, string?>();
""")])

# --- The walk: the row's keyword reaches the matcher, and a barless row leaves the count.
edit("src/Kronikol/Reports/ReportGenerator.cs", [(
    """                    case DiagramMarkerKind.Step:
                        current = stepMarkerCount < ordered.Count && StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Text)
""",
    """                    case DiagramMarkerKind.Step:
                        current = stepMarkerCount < ordered.Count && StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Keyword, ordered[stepMarkerCount].Text)
"""), (
    """    /// Every step of a scenario in the order its marker will arrive, paired with the address it gets in the
    /// data file: <c>b0</c>, <c>b1</c> for background steps, then <c>0</c>, <c>1</c> for the scenario's own.
    /// Only top-level steps appear — a step delimiter is emitted for those alone.
    /// </summary>
    private static List<(string Path, string Text)> OrderedStepPaths(Scenario scenario)
    {
        var ordered = new List<(string, string)>();
        for (var i = 0; i < (scenario.BackgroundSteps?.Length ?? 0); i++)
            ordered.Add(($"b{i}", scenario.BackgroundSteps![i].Text));
        for (var i = 0; i < (scenario.Steps?.Length ?? 0); i++)
            ordered.Add(($"{i}", scenario.Steps![i].Text));
        return ordered;
    }
""",
    """    /// The steps of a scenario a marker can arrive for, in order, each with the address it gets in the data file:
    /// <c>b0</c>, <c>b1</c> for background steps, then <c>0</c>, <c>1</c> for the scenario's own. Only top-level steps
    /// appear, as a step delimiter is emitted for those alone, and not those <see cref="Scenario.BarlessRows"/> names.
    /// </summary>
    private static List<(string Path, string? Keyword, string Text)> OrderedStepPaths(Scenario scenario)
    {
        var ordered = new List<(string, string?, string)>();
        var row = 0;

        void Add(string prefix, ScenarioStep[]? steps)
        {
            for (var i = 0; i < (steps?.Length ?? 0); i++, row++)
                if (scenario.BarlessRows?.Contains(row) != true)
                    ordered.Add(($"{prefix}{i}", steps![i].Keyword, steps[i].Text));
        }

        Add("b", scenario.BackgroundSteps);
        Add("", scenario.Steps);
        return ordered;
    }
"""), (
    """    /// Whether a step delimiter's PlantUML belongs to a given step. The bar's label is the step text, but
    /// possibly with the keyword prepended and the first letter capitalised, so this compares loosely: the
    /// answer is only used to decide whether to trust positional attribution at all.
    /// </summary>
    private static bool StepMarkerMatches(string plantUml, string stepText)
""",
    """    /// Whether a step delimiter's PlantUML belongs to a given step. A bar written by hand, by another producer or by an
    /// older version holds the step's text with whatever is around it, so this compares loosely: the answer is only used
    /// to decide whether to trust positional attribution at all. A bar Kronikol draws changes the text, so it is also read
    /// as what <see cref="Kronikol.PlantUml.StepBarPlantUml.Build"/> draws for the step.
    /// </summary>
    private static bool StepMarkerMatches(string plantUml, string? keyword, string stepText)
"""), (
    """               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
    }
""",
    """               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase)
               || (stepText == ScenarioStep.NoText
                   ? Kronikol.PlantUml.StepBarPlantUml.DrawsStep(marker, keyword, ScenarioStep.NoTextOnABar, anyFirstWord: false)
                   : Kronikol.PlantUml.StepBarPlantUml.DrawsStep(marker, keyword, stepText));
    }
""")])

# --- The matcher's second reading, beside the bar it reads.
edit("src/Kronikol/PlantUml/StepBarPlantUml.cs", [(
    """        var labelLine = string.Join(@"\\n", DiagramWidth.WrapLines(PlantUmlCreator.EscapeOneLineMarkup(labelLines[0]), DiagramWidth.MaxNoteTextLineChars));
""",
    """        var labelLine = LabelLine(labelLines[0]);
"""), (
    """    /// <summary>A line break inside a table cell: the engine reads a doubled backslash and an n there as one.</summary>
""",
    """    /// <summary>A label's first line as a bar writes it: escaped, then wrapped.</summary>
    private static string LabelLine(string line) =>
        string.Join(@"\\n", DiagramWidth.WrapLines(PlantUmlCreator.EscapeOneLineMarkup(line), DiagramWidth.MaxNoteTextLineChars));

    /// <summary>How far short of its budget <see cref="PlantUmlStatementLimits.TruncateLabel"/> can stop, as it will not halve an escape.</summary>
    private const int MostACutFallsShort = 16;

    /// <summary>
    /// Whether <paramref name="bar"/> is what <see cref="Build"/> draws for this step: its label written again as Build writes
    /// it, wrapped at the same places, and compared with the bar, exactly, up to where a cut ended the bar. So a step whose text
    /// the bar escaped, wrapped or cut finds its bar, and one whose text differs from the bar's late on, by a space or by case
    /// does not. The bar's keyword is none, the step's own, or the bar's first word, as a collector writes And for a keyword it
    /// repeats, and that word can come before the step's own keyword, as BDDfy leaves an And or a But in a step's title and draws
    /// And And. The one difference it allows is the one <see cref="Reports.StepText"/> makes: it capitalises a step with no
    /// keyword, in its row and on its bar, under a switch for each. What it cannot see: the rest of a text past a cut; a first
    /// word of the text taken for a keyword, so a text with its first word gone reads as a bar with no keyword; the same text
    /// under another keyword or none; two steps with no keyword that StepText makes alike; and two texts Build draws alike. A
    /// row with no text says too little to spare the first word: <paramref name="anyFirstWord"/> keeps that to its own keyword.
    /// </summary>
    internal static bool DrawsStep(string bar, string? keyword, string text, bool anyFirstWord = true)
    {
        string content;
        int budget;
        if (bar.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            (content, budget) = (bar[LegacyPrefix.Length..], PlantUmlStatementLimits.MaxColouredNoteBarChars - LegacyPrefix.Length);
        else if (bar.StartsWith(RichPrefix, StringComparison.Ordinal))
            (content, budget) = (bar[RichPrefix.Length..], PlantUmlStatementLimits.MaxNoteLineChars - RichPrefix.Length);
        else
            return false;

        var hasKeyword = !string.IsNullOrWhiteSpace(keyword);
        if (DrawsText(content, budget, "", text) || (hasKeyword && DrawsLabel(content, budget, $"{keyword} {text}")))
            return true;
        if (!anyFirstWord)
            return false;

        var space = content.IndexOf(' ');
        var lineBreak = content.IndexOf(@"\\n", StringComparison.Ordinal);
        var firstWord = space >= 0 && (lineBreak < 0 || space < lineBreak) ? space : lineBreak;
        if (firstWord <= 0)
            return false;

        var before = $"{content[..firstWord]} ";
        return hasKeyword
            ? DrawsLabel(content, budget, before + text) || DrawsLabel(content, budget, $"{before}{keyword} {text}")
            : DrawsText(content, budget, before, text);
    }

    /// <summary>
    /// A text with no keyword of its own after this much of a label: as it is written, or as <see cref="Reports.StepText"/> would
    /// have it on the other side, capitalised, or the text it capitalises to this one.
    /// </summary>
    private static bool DrawsText(string content, int budget, string before, string text)
    {
        if (DrawsLabel(content, budget, before + text))
            return true;

        var capitalised = Reports.StepText.Capitalise(text) ?? text;
        if (capitalised != text)
            return DrawsLabel(content, budget, before + capitalised);

        var first = text.TakeWhile(c => char.ToLowerInvariant(c) == c).Count();
        if (first == text.Length)
            return false;

        var lowered = text[..first] + char.ToLowerInvariant(text[first]) + text[(first + 1)..];
        return Reports.StepText.Capitalise(lowered) == text && DrawsLabel(content, budget, before + lowered);
    }

    private static bool DrawsLabel(string content, int budget, string label)
    {
        var lines = label.Replace("\\r", "").Trim().Split('\\n');
        var drawn = string.Join(@"\\n", lines.Skip(1).SelectMany(EscapeWrappedBodyLine).Prepend(LabelLine(lines[0])));

        var cut = content.Length > budget - MostACutFallsShort && content.EndsWith(PlantUmlStatementLimits.TruncationMarker, StringComparison.Ordinal);
        var kept = cut ? content[..^PlantUmlStatementLimits.TruncationMarker.Length] : content;
        if (cut && drawn.StartsWith(kept, StringComparison.Ordinal))
            return true;

        return kept.StartsWith(drawn, StringComparison.Ordinal) && FollowsALabel(kept.AsSpan(drawn.Length), cut);
    }

    /// <summary>
    /// Whether this can come after a whole label: nothing, or what <see cref="Build"/> writes there, a table or a doc string under
    /// a blank line, with a table's name above it where a step has several. A cut can leave only part of that. A break alone is
    /// the label wrapping on past this text, and a later line is a longer text's, as a body line never opens with a pipe.
    /// </summary>
    private static bool FollowsALabel(ReadOnlySpan<char> rest, bool cut)
    {
        if (rest.IsEmpty)
            return true;
        if (!rest.StartsWith(@"\\n\\n", StringComparison.Ordinal))
            return cut && rest.SequenceEqual(@"\\n");

        rest = rest[4..];
        if (rest.IsEmpty)
            return cut;
        if (rest[0] == '|')
            return true;

        var nameEnd = rest.IndexOf(@"\\n", StringComparison.Ordinal);
        if (nameEnd < 0)
            return cut;
        if (nameEnd <= 0 || rest[nameEnd - 1] != ':')
            return false;

        var table = rest[(nameEnd + 2)..];
        return table.StartsWith("|", StringComparison.Ordinal) || (cut && table.IsEmpty);
    }

    /// <summary>A line break inside a table cell: the engine reads a doubled backslash and an n there as one.</summary>
""")])

# --- A row that draws no bar: known where the pipeline draws the bars, kept on each scenario, and no record is touched.
edit("src/Kronikol/Reports/Scenario.cs", [(
    """public record Scenario
{
    public required string Id { get; set; }
""",
    """public record Scenario
{
    /// <summary>
    /// The rows of this scenario the tests file listed and drew no bar for, by position in its background steps and then its
    /// steps, so the walk over its bars leaves them out: a background's, an assertion's, and one with no time. Null where
    /// the bars are not the tests file's. Kept here and not on the rows, as scenarios can share a background's rows, and the
    /// position holds when the background detector moves a scenario's opening rows.
    /// </summary>
    internal int[]? BarlessRows { get; init; }

    public required string Id { get; set; }
""")])

edit("src/Kronikol/Reports/ScenarioStep.cs", [(
    """    public bool IsAssertion { get; set; }
""",
    """    public bool IsAssertion { get; set; }

    /// <summary>What a step with no text of its own is listed as.</summary>
    internal const string NoText = "(step)";

    /// <summary>What a step with no text of its own is drawn as.</summary>
    internal const string NoTextOnABar = "step";
""")])

edit("src/Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs", [(
    """            var text = pickleStep.Text ?? gherkinStep?.Text ?? "(step)";
""",
    """            var text = pickleStep.Text ?? gherkinStep?.Text ?? ScenarioStep.NoText;
""")])

edit("src/Kronikol/Ingestion/Cucumber/CucumberFeatureMerger.cs", [(
    """            var children = new Dictionary<ScenarioStep, List<ScenarioStep>>();
""",
    """            var children = new Dictionary<ScenarioStep, List<ScenarioStep>>(ReferenceEqualityComparer.Instance);
""")])

edit("src/Kronikol/Ingestion/FeatureSynthesizer.cs", [(
    """        TestAttempts attempts,
        SetupWindow? setup = null)
    {
        var logList = (logs ?? []).ToList();
""",
    """        TestAttempts attempts,
        SetupWindow? setup = null,
        IReadOnlySet<string>? barsInCapture = null)
    {
        var logList = (logs ?? []).ToList();
"""), (
    """            var steps = BuildStepTree(acc.Steps);
            var backgroundSteps = BuildStepTree(acc.BackgroundSteps);
""",
    """            var steps = BuildStepTree(acc.Steps, out var barlessSteps);
            var backgroundSteps = BuildStepTree(acc.BackgroundSteps);
            var backgroundCount = backgroundSteps?.Length ?? 0;
"""), (
    """                Steps = steps,
                BackgroundSteps = backgroundSteps,
                Rule = acc.Rule,
""",
    """                Steps = steps,
                BackgroundSteps = backgroundSteps,
                // A null set is a caller that drew no bars, and one holding this test is a capture whose bars are its rows'.
                BarlessRows = barsInCapture is null || barsInCapture.Contains(testId)
                    ? null
                    : [.. Enumerable.Range(0, backgroundCount), .. barlessSteps.Select(row => backgroundCount + row)],
                Rule = acc.Rule,
"""), (
    """    public static ScenarioStep[]? BuildStepTree(IEnumerable<TestRunRecord> records)
    {
        var includeAssertions = StepCollector.Options.IncludeTrackedAssertionsInStepList;
        var roots = new List<ScenarioStep>();
        var children = new Dictionary<ScenarioStep, List<ScenarioStep>>();
""",
    """    public static ScenarioStep[]? BuildStepTree(IEnumerable<TestRunRecord> records) => BuildStepTree(records, out _);

    /// <inheritdoc cref="BuildStepTree(IEnumerable{TestRunRecord})"/>
    /// <param name="records">The records.</param>
    /// <param name="barless">The rows that draw no bar where the tests file draws them: an assertion's, one with no time, and a nested step that had no parent to go under.</param>
    private static ScenarioStep[]? BuildStepTree(IEnumerable<TestRunRecord> records, out List<int> barless)
    {
        barless = [];
        var includeAssertions = StepCollector.Options.IncludeTrackedAssertionsInStepList;
        var roots = new List<ScenarioStep>();
        // By reference: two rows that read alike are still two rows, each with its own children.
        var children = new Dictionary<ScenarioStep, List<ScenarioStep>>(ReferenceEqualityComparer.Instance);
"""), (
    """            else
            {
                roots.Add(step);
                if (!isAssertion)
                    currentTop = step;
            }
""",
    """            else
            {
                if (isAssertion || !record.IsDiagramMarker)
                    barless.Add(roots.Count);
                roots.Add(step);
                if (!isAssertion)
                    currentTop = step;
            }
"""), (
    """        var text = record.Text ?? "(step)";
""",
    """        var text = record.Text ?? ScenarioStep.NoText;
""")])

edit("src/Kronikol/Ingestion/IngestPipeline.cs", [(
    """        AddDiagramMarkers(records, current);
""",
    """        var barsInCapture = AddDiagramMarkers(records, current);
"""), (
    """            testRecords, logs, request.DefaultFeatureName, request.ResultWhenUnknown, attachmentsBase, attempts, setupKept);
""",
    """            testRecords, logs, request.DefaultFeatureName, request.ResultWhenUnknown, attachmentsBase, attempts, setupKept, barsInCapture);
"""), (
    """    /// reporter's own step events for the scenarios the messages own.
    /// </summary>
    private static void AddDiagramMarkers(List<InteractionRecord> records, List<TestRunRecord> testRecords)
    {
""",
    """    /// reporter's own step events for the scenarios the messages own.
    /// </summary>
    /// <returns>The tests whose capture holds step bars, so whose rows are not read as the tests file's bars.</returns>
    private static HashSet<string> AddDiagramMarkers(List<InteractionRecord> records, List<TestRunRecord> testRecords)
    {
"""), (
    """            StringComparer.Ordinal);

        foreach (var record in testRecords)
        {
            if (!record.IsDiagramMarker || string.IsNullOrWhiteSpace(record.TestId) || drawnByCapture.Contains(record.TestId))
                continue;
""",
    """            StringComparer.Ordinal);

        // A capture's kind: step record draws a bar as well, though not in place of the tests file's.
        var barsInCapture = new HashSet<string>(
            drawnByCapture.Concat(records.Where(r => string.Equals(r.Kind, InteractionRecord.Kinds.Step, StringComparison.OrdinalIgnoreCase)).Select(r => r.TestId)),
            StringComparer.Ordinal);

        foreach (var record in testRecords)
        {
            if (!record.IsDiagramMarker || string.IsNullOrWhiteSpace(record.TestId) || drawnByCapture.Contains(record.TestId))
                continue;
"""), (
    """                : InteractionRecord.StepMarker(record.TestId, record.Text ?? "step", record.Timestamp!.Value, record.Keyword,
                    table: record.Table, docString: record.DocString));
        }
    }
""",
    """                : InteractionRecord.StepMarker(record.TestId, record.Text ?? ScenarioStep.NoTextOnABar, record.Timestamp!.Value, record.Keyword,
                    table: record.Table, docString: record.DocString));
        }

        return barsInCapture;
    }
""")])
print("R2 applied")
