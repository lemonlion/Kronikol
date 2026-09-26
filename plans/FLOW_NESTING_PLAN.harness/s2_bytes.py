"""What nesting cost, taken from the real tool (plan §7.5): every scenario of each report, unfiltered, printed by
the release before S2 and by S2, and `flow_bytes.py`'s numbers taken again from those outputs.

    python s2_bytes.py <old Kronikol.Tool> <new Kronikol.Tool> <report.json>...

A tool is an .exe, or a .dll run through `dotnet`. Prints total bytes old and new, the largest relative
increase, indented lines, the deepest level and the `inside` references an unfiltered view needed. Reports
are read with json.load for their scenario count only; nothing from them is printed but addresses.
"""
import concurrent.futures, json, re, subprocess, sys


def command(tool):
    return ["dotnet", tool] if tool.endswith(".dll") else [tool]


def flow(tool, report, ordinal):
    result = subprocess.run([*command(tool), "query", "flow", report, f"s{ordinal}", "--max-bytes", "0"],
                            capture_output=True, check=False)
    if result.returncode != 0:
        raise SystemExit(f"exit {result.returncode} on {report} s{ordinal}: {result.stderr[:200]!r}")
    return result.stdout.replace(b"\r\n", b"\n")


def main():
    old, new, reports = sys.argv[1], sys.argv[2], sys.argv[3:]
    total_old = total_new = scenarios = indented = deepest = refs = 0
    worst = (0.0, "")
    for report in reports:
        with open(report, encoding="utf-8-sig") as f:
            data = json.load(f)
        count = sum(len(feature.get("scenarios") or []) for feature in data.get("features") or [])
        lane = report.replace("\\", "/").split("/tests/")[-1].split("/")[0]
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            outputs = list(pool.map(lambda o: (o, flow(old, report, o), flow(new, report, o)), range(count)))
        for ordinal, before, after in outputs:
            scenarios += 1
            total_old += len(before)
            total_new += len(after)
            if before and (len(after) - len(before)) / len(before) > worst[0]:
                worst = ((len(after) - len(before)) / len(before), f"{lane} s{ordinal}: {len(before)} -> {len(after)} B")
            text = after.decode("utf-8")
            refs += len(re.findall(r"  inside s\d+/i\d+$", text, re.M))
            for line in text.split("\n"):
                match = re.match(r"^( *)s\d+/i\d+ ", line)
                if match:
                    depth = (len(match.group(1)) - 2) // 2
                    deepest = max(deepest, depth)
                    indented += depth > 0
    print(f"{scenarios} scenarios: flow bytes {total_old:,} before -> {total_new:,} after "
          f"({(total_new - total_old) / total_old:+.1%})")
    print(f"largest relative increase: {worst[0]:+.1%} ({worst[1]})")
    print(f"indented lines {indented:,}, deepest level {deepest}, `inside` references in unfiltered views: {refs}")


if __name__ == "__main__":
    main()
