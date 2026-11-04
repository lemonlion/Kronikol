"""On a copy of #223's prototype, resolve a stamp to the whole path rather than the top-level step: R3's one line, to test the pairing at every depth."""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """            foreach (var sub in step.SubSteps ?? [])
                Add(sub, path);
"""
new = """            var index = 0;
            foreach (var sub in step.SubSteps ?? [])
                Add(sub, $"{path}.{index++}");
"""
assert s.count(old) == 1
s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("whole path on #223's prototype")
