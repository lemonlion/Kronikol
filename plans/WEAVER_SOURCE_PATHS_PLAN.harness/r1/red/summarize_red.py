import re, sys

# Prints each failed fact of a dotnet test log once (a theory's rows of one fact once, unless --all), with the first
# lines of its error message and the test file line it failed at.
path = sys.argv[1]
show_all = "--all" in sys.argv
t = open(path, encoding="utf-8", errors="replace").read()
blocks = re.split(r"\n  Failed ", t)
seen = set()
BS = chr(92)
for b in blocks[1:]:
    name = b.split(" [")[0]
    for prefix in ("Kronikol.Tests.AssertionTracking.", "Kronikol.Tests.Reports.", "Kronikol.Tests."):
        name = name.replace(prefix, "")
    base = name.split("(")[0]
    m = re.search(r"Error Message:\n(.*?)\n  Stack Trace:", b, re.S)
    msg = m.group(1).strip().replace("\n", " | ") if m else "?"
    line = re.search(r"(\w+Tests\.cs):line (\d+)", b)
    where = f"{line.group(1)}:{line.group(2)}" if line else "?"
    if base in seen and not show_all:
        continue
    seen.add(base)
    print(f"- {name[:100]} @ {where}\n    {msg[:300]}")
