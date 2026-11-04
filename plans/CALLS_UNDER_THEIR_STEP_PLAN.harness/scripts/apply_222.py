"""#222's own suggestion, on the prototype, to see whether it removes the wrong answer. #222's plan owns the real fix."""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/BackgroundStepsDetector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """            // Use the first scenario's steps as the background template
            var backgroundSteps = members[0].Steps!.Take(commonPrefixLength).ToArray();

            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = backgroundSteps;
                scenario.Steps = scenario.Steps!.Skip(commonPrefixLength).ToArray();
"""
new = """            foreach (var scenario in members)
            {
                scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray();
                scenario.Steps = scenario.Steps!.Skip(commonPrefixLength).ToArray();
"""
assert s.count(old) == 1
s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("#222 suggestion applied")
