"""The walk over #223's R1: ThroughTheirCalls already gives a response its request's path, so the walk's own copy goes with the map R1's layer removed."""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
a = s.index("                    // A call in flight across a bar is one call: a response takes the step of the request it answers.\n")
b = s.index("                    interactionIndex++;\n", a)
print(s[a:b][:300])
s = s[:a] + "                    paths.Add(read);\n" + s[b:]
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
