"""Probe only, never for a release: writes, per record, what the marker walk says and what the stamp says."""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """                    paths.Add(log.StepOrdinal is { } stamped && byOrdinal.TryGetValue(stamped, out var path) ? path : current);
"""
new = """                    if (Environment.GetEnvironmentVariable("KRONIKOL_PROBE_ATTRIBUTION") is { Length: > 0 } probeFile)
                        File.AppendAllText(probeFile, $"{perTest.Key}\\t{log.Type}\\t{log.RequestResponseId}\\t{current ?? "-"}\\t{(log.StepOrdinal is { } probeStamp ? (byOrdinal.TryGetValue(probeStamp, out var probePath) ? probePath : "unmapped") : "unstamped")}\\t{log.Method}\\t{log.Uri}\\n");
                    paths.Add(log.StepOrdinal is { } stamped && byOrdinal.TryGetValue(stamped, out var path) ? path : current);
"""
assert s.count(old) == 1
s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("probe applied")
