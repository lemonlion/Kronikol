#!/usr/bin/env python3
"""Dump every scenario of a report as: its step tree, then each call's URL -> stepPath. Uses only the query tool."""
import json, subprocess, sys, re

TOOL = ["dotnet", "C:/Code/Kronikol-225z/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll", "query"]

def run(*args):
    return subprocess.run(TOOL + list(args), capture_output=True, text=True, encoding="utf-8", errors="replace").stdout

def main(reports, out):
    scen = run("scenarios", reports, "--json", "--max-bytes", "0")
    items = json.loads(scen)["items"]
    with open(out, "w", encoding="utf-8") as f:
        for s in items:
            addr = s.get("address") or s.get("scenario")
            f.write(f"=== {addr} {s.get('scenario')} [{s.get('result')}]\n")
            steps = run("steps", reports, addr, "--max-bytes", "0")
            lines = steps.splitlines()
            for l in lines[3:]:
                if l.strip():
                    l = re.sub(r"\s+[0-9.]+s(\s|$)", r"\1", l)  # durations out
                    if re.match(r".*\d+ calls? · interactions", l):
                        continue
                    f.write("  " + l.rstrip() + "\n")
            calls = json.loads(run("interactions", reports, addr, "--json", "--max-bytes", "0"))["items"]
            for c in calls:
                f.write(f"    {c.get('address'):10} {c.get('summary'):45} -> {c.get('stepPath')}\n")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
