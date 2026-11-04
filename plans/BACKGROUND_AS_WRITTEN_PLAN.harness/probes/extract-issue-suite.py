"""Writes the suite from #222's <details> block into a folder, verbatim: python extract-issue-suite.py <issue-body.md> <out-dir>."""
import pathlib, re, sys
body = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").replace("\r\n", "\n")
out = pathlib.Path(sys.argv[2])
details = body[body.index("<details>"):]
written = 0
for m in re.finditer(r"^`([^`\n]+)`\n\n```[a-z]*\n(.*?)^```$", details, re.S | re.M):
    target = out / m.group(1)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(m.group(2), encoding="utf-8", newline="\n")
    written += 1
    print(m.group(1))
print(f"{written} files")
