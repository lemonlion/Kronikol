"""Summarises red runs: python -I trx_red_summary.py <out.txt> <title> <trx>... (appends). Each failed fact with the
test file line it failed at and the first line of its message; then the passing facts' count."""
import re
import sys
import xml.etree.ElementTree as ET

out, title, files = sys.argv[1], sys.argv[2], sys.argv[3:]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
lines = [f"## {title}", ""]
for path in files:
    root = ET.parse(path).getroot()
    results = list(root.iterfind(".//t:UnitTestResult", ns))
    failed = [r for r in results if r.get("outcome") == "Failed"]
    lines.append(f"{path.split('/')[-1]}: {len(results)} facts, {len(failed)} failed, {len(results) - len(failed)} passed")
    for r in failed:
        message = " ".join((r.find(".//t:Message", ns).text or "").split())[:150]
        trace = r.find(".//t:StackTrace", ns).text or ""
        where = re.findall(r"([A-Za-z]+Tests\.cs):line (\d+)", trace)
        at = f"{where[0][0]}:{where[0][1]}" if where else "?"
        lines.append(f"  RED  {r.get('testName').split('.')[-1]}  ({at})  {message}")
    lines.append("")
with open(out, "a", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(lines) + "\n")
