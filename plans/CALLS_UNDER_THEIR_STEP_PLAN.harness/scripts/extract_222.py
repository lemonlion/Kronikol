"""Writes the suite in #222's <details> block to a folder, each file under the path its heading names."""
import json
import pathlib
import re
import subprocess
import sys

target = pathlib.Path(sys.argv[1])
body = json.loads(subprocess.check_output(
    ["gh", "issue", "view", "222", "--repo", "lemonlion/Kronikol", "--json", "body"],
    encoding="utf-8"))["body"].replace("\r\n", "\n")
details = body[body.index("<details>"):]
written = 0
for match in re.finditer(r"^`([^`\n]+\.[A-Za-z]+)`\n\n```[a-z]*\n(.*?)^```$", details, re.S | re.M):
    path = target / match.group(1)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(match.group(2), encoding="utf-8")
    written += 1
    print(match.group(1), len(match.group(2)))
print("files", written)
