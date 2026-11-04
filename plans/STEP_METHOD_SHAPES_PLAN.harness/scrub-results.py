#!/usr/bin/env python3
# Takes this machine's home folder out of the result files, so they read the same on any machine.
import re
from pathlib import Path

results = Path(__file__).resolve().parent / "results"
for path in results.iterdir():
    if not path.is_file():
        continue
    text = path.read_text(encoding="utf-8", errors="replace")
    scrubbed = re.sub(r"[A-Za-z]:[\\/]Users[\\/][^\\/\s]+[\\/]", "<home>/", text)
    if scrubbed != text:
        path.write_text(scrubbed, encoding="utf-8", newline="\n")
