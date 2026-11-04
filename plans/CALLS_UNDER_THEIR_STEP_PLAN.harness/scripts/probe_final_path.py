"""Probe only: moves the differential's line to after the join, so it compares the path a record ends with against published's walk.

Usage: probe_final_path.py <worktree>, after apply_probe_one.py and apply_end_state.py. Each line then holds: test, type, call, walk, stamp, method, uri, final path, caller, whether the record's own flow answered.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

PAIRS = [
    ("""                    lock (ProbeLock)
                        File.AppendAllText(probeFile, $"{perTest.Key}\\t{log.Type}\\t{log.RequestResponseId}\\t{probeWalk ?? "-"}\\t{probeStamp}\\t{log.Method.Value}\\t{log.Uri}\\n");
""",
     """                    probeLines.Add($"{perTest.Key}\\t{log.Type}\\t{log.RequestResponseId}\\t{probeWalk ?? "-"}\\t{probeStamp}\\t{log.Method.Value}\\t{log.Uri}");
"""),
    ("""            var probeFile = Environment.GetEnvironmentVariable("KRONIKOL_PROBE_ATTRIBUTION");
""",
     """            var probeFile = Environment.GetEnvironmentVariable("KRONIKOL_PROBE_ATTRIBUTION");
            var probeLines = new List<string>();
"""),
    ("""            ThroughTheirCalls(perTest.Where(l => !l.IsDiagramMarker).ToArray(), paths);
""",
     """            ThroughTheirCalls(perTest.Where(l => !l.IsDiagramMarker).ToArray(), paths);
            if (probeFile is { Length: > 0 })
            {
                var probeRecords = perTest.Where(l => !l.IsDiagramMarker).ToArray();
                lock (ProbeLock)
                    for (var i = 0; i < probeLines.Count; i++)
                        File.AppendAllText(probeFile, $"{probeLines[i]}\\t{paths[i] ?? "-"}\\t{probeRecords[i].CallerName}\\t{probeRecords[i].OpenStepIsItsFlows}\\n");
            }
"""),
]
for old, new in PAIRS:
    assert s.count(old) == 1, old[:80]
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("the probe reads the final path")
