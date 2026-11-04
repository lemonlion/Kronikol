"""R2 on its own, on clean main: the walk's matcher (apply_r2_matcher.py) and a response's step (F9). It needs no stamp, and so nothing from #223 or R1.

Usage: apply_r2_alone.py <worktree>, after apply_r2_matcher.py.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

old = """                if (!log.IsDiagramMarker)
                {
                    paths.Add(current);
                    interactionIndex++;
                    continue;
                }
"""
new = """                if (!log.IsDiagramMarker)
                {
                    // A call in flight across a bar is one call: a response takes its request's step.
                    if (log.Type == RequestResponseType.Request)
                        requestPaths.TryAdd(log.RequestResponseId, current);
                    paths.Add(log.Type == RequestResponseType.Response && requestPaths.TryGetValue(log.RequestResponseId, out var ofRequest) ? ofRequest : current);
                    interactionIndex++;
                    continue;
                }
"""
assert s.count(old) == 1, s.count(old)
s = s.replace(old, new)

old = """            var stepMarkerCount = 0;
            var interactionIndex = 0;
"""
new = """            var stepMarkerCount = 0;
            var interactionIndex = 0;
            var requestPaths = new Dictionary<Guid, string?>();
"""
assert s.count(old) == 1, s.count(old)
s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("a response's step applied")
