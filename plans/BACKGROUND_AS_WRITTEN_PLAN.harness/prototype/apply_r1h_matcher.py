# R1 takes the matcher of #225's R2, and its facts and mutations follow.
import pathlib

P = pathlib.Path(r"<tree>p")
H = pathlib.Path(r"<tree>\plans\BACKGROUND_AS_WRITTEN_PLAN.harness")


def edit(path, *pairs):
    text = path.read_bytes().decode("utf-8")
    for old, new in pairs:
        assert text.count(old) == 1, old[:80]
        text = text.replace(old, new)
    path.write_bytes(text.encode("utf-8"))


edit(
    P / "src/Kronikol/Reports/ReportGenerator.cs",
    (
        """        // The bar escapes its label and wraps a long one (StepBarPlantUml.Build), so what it drew is what is compared, breaks aside.
        var marker = WithoutBreaks(plantUml.Replace("\\\\n", ""));
        return marker.Contains(Drawn(stepText), StringComparison.OrdinalIgnoreCase)
               || marker.Contains(Drawn(StepText.CapitaliseIfEnabled(stepText) ?? stepText), StringComparison.OrdinalIgnoreCase);

        static string Drawn(string text) => WithoutBreaks(PlantUml.PlantUmlCreator.EscapeOneLineMarkup(text));
        static string WithoutBreaks(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
    }
""",
        """        // What StepBarPlantUml.Build does to a label: the first line alone, escaped, wrapped with a literal \\n, and cut at the
        // bar's cap. So the step's text has the same done to it, and no more of it is asked for than any bar has room for.
        var firstLine = stepText.Replace("\\r", "").Trim().Split('\\n')[0];
        var wanted = FoldStepBar(PlantUml.PlantUmlCreator.EscapeOneLineMarkup(firstLine));
        if (wanted.Length > StepMarkerMatchChars)
            wanted = wanted[..StepMarkerMatchChars];

        return FoldStepBar(plantUml).Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>As much of a step's text as the matcher asks a bar for: well inside the shortest cap a bar has.</summary>
    private const int StepMarkerMatchChars = 400;

    /// <summary>A bar's text with what wrapping adds or moves taken out: a literal <c>\\n</c>, a line feed and a space.</summary>
    private static string FoldStepBar(string text) =>
        text.Replace("\\\\n", "", StringComparison.Ordinal).Replace("\\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
""",
    ),
)

edit(
    P / "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    (
        """    [Fact]
    public void A_step_is_not_recognised_in_another_steps_bar() =>
""",
        """    [Fact]
    public void A_step_is_recognised_by_its_first_line_as_the_bar_draws_no_other()
    {
        const string text = "first line\\n<b>second</b> ~line";

        Assert.True(ReportGenerator.StepMarkerMatches(StepBarPlantUml.Build("Given " + text), text));
    }

    [Fact]
    public void A_step_is_recognised_by_its_opening_when_the_bar_cuts_its_text()
    {
        var text = string.Join(" ", Enumerable.Repeat("a long step text", 100));

        Assert.True(ReportGenerator.StepMarkerMatches(StepBarPlantUml.Build("Given " + text), text));
    }

    [Fact]
    public void A_step_is_not_recognised_in_another_steps_bar() =>
""",
    ),
)

edit(
    H / "prototype/mutations.py",
    (
        """     "WithoutBreaks(PlantUml.PlantUmlCreator.EscapeOneLineMarkup(text))", "WithoutBreaks(text)"),""",
        """     "FoldStepBar(PlantUml.PlantUmlCreator.EscapeOneLineMarkup(firstLine))", "FoldStepBar(firstLine)"),""",
    ),
    (
        """     'WithoutBreaks(plantUml.Replace("\\\\\\\\n", ""))', "WithoutBreaks(plantUml)"),""",
        """     'text.Replace("\\\\\\\\n", "", StringComparison.Ordinal).Replace', "text.Replace"),
    ("M27 the matcher asks a bar for a line it never draws", "src/Kronikol/Reports/ReportGenerator.cs",
     """ + '''"""stepText.Replace("\\\\r", "").Trim().Split('\\\\n')[0]"""''' + """, "stepText"),
    ("M28 the matcher asks a bar for more than it has room for", "src/Kronikol/Reports/ReportGenerator.cs",
     "if (wanted.Length > StepMarkerMatchChars)", "if (wanted.Length > int.MaxValue - 1)"),""",
    ),
)
print("edited")
