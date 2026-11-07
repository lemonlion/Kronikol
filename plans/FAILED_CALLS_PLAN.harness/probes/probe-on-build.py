"""Makes a copy of a FAILED_CALLS_PLAN harness probe that runs on this worktree's build: every `#:package Kronikol*@5.31.1` becomes
a `#:project` reference to the worktree's csproj, and each fingerprint line is printed under rules 11 and 12, so the rule-11 line
can be compared with the frozen 5.31.1 result. Usage: python probe-on-build.py <probe.cs> <out.cs> <worktree>"""
import re
import sys

src, out, root = sys.argv[1:4]
s = open(src, encoding="utf-8").read()


def project(m):
    name = m.group(1)
    path = f"{root}/src/{name}/{name}.csproj"
    return f"#:project {path}"


s, n = re.subn(r"^#:package (Kronikol(?:\.Extensions\.[A-Za-z0-9.]+)?)@5\.31\.1$", project, s, flags=re.M)
assert n > 0
old = """    foreach (var line in InteractionShape.Calls(logs))
        Console.WriteLine($"    fingerprint line: {line}");"""
if old in s:
    s = s.replace(old, """    foreach (var line in ProbeRules.Calls(logs, 11))
        Console.WriteLine($"    fingerprint line: {line}");
    foreach (var line in ProbeRules.Calls(logs, 12))
        Console.WriteLine($"    rule 12 line: {line}");""")
    # The rule-numbered reading is internal to Kronikol, so the probe reaches it the way a test cannot need to.
    s += """

static class ProbeRules
{
    private static readonly System.Reflection.MethodInfo Under = typeof(Kronikol.History.InteractionShape).GetMethod("Calls",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
        [typeof(IEnumerable<Kronikol.Tracking.RequestResponseLog?>), typeof(Kronikol.History.HistoryShapeRules), typeof(int)])!;

    public static IEnumerable<object> Calls(IEnumerable<Kronikol.Tracking.RequestResponseLog?> logs, int version) =>
        ((System.Collections.IEnumerable)Under.Invoke(null, [logs, null, version])!).Cast<object>();
}
"""
else:
    print("no fingerprint loop to widen in", src)
open(out, "w", encoding="utf-8").write(s)
print(f"{n} package references now point at {root}")
