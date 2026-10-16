"""Applies the audit's mutation P5 (a request tracked only once its handler runs) to a tree, for the Linux run.

    python apply_p5.py <tree>
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src" / "Kronikol.Extensions.ProxyTap" / "ProxyTap.cs"
s = p.read_bytes().decode("utf-8")
old = ("            Interlocked.Increment(ref _inflight);\n"
       "            var answer = new CallerAnswer(context.Response) { BeforeLetGo = BeforeCallerLetGo };\n"
       "            _answers.TryAdd(answer, 0);\n"
       "            Received?.Invoke(context);\n")
new = ("            Received?.Invoke(context);\n"
       "            Interlocked.Increment(ref _inflight);\n"
       "            var answer = new CallerAnswer(context.Response) { BeforeLetGo = BeforeCallerLetGo };\n"
       "            _answers.TryAdd(answer, 0);\n")
assert s.count(old) == 1, s.count(old)
p.write_bytes(s.replace(old, new).encode("utf-8"))
print("P5 applied")
