"""R2's matcher, for an ingest lane, which has no stamp and keeps the walk (§7.2). On a copy of the two prototypes built as one.

Usage: apply_r2_matcher.py <worktree>

What StepBarPlantUml.Build does to a label: it takes the first line alone, escapes it (EscapeOneLineMarkup), wraps it with a literal \\n at a
space or inside a long word, and cuts the whole at the bar's cap. So both sides drop the breaks and the spaces, the step's text is
escaped the way the bar's was, and no more of it is asked for than any bar has room for.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

old = """        var marker = plantUml.Replace('\\n', ' ').Trim();
        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
    }
"""
new = """        var firstLine = stepText.Replace("\\r", "").Trim().Split('\\n')[0];
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
"""
assert s.count(old) == 1, s.count(old)
s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("matcher applied")
