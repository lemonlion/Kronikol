"""The issue's own suggestion and nothing else, to measure what it does: python -I apply_suggestion_only.py <checkout>."""
import pathlib, sys
p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/BackgroundStepsDetector.cs"
t = p.read_text(encoding="utf-8")
old = """            var backgroundSteps = members[0].Steps!.Take(commonPrefixLength).ToArray();

            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = backgroundSteps;
"""
new = """            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();
"""
assert old in t
p.write_text(t.replace(old, new), encoding="utf-8", newline="\n")
print("applied")
