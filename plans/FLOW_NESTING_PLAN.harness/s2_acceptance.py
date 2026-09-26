"""S2's acceptance on real reports (plan §7.5): the built `flow` against the prototype's nested mode, line for line.

    python s2_acceptance.py <Kronikol.Tool.exe or .dll> <report.json>...

Every scenario of each report is compared unfiltered, with `--service CosmosDB` and with `--step 1`;
`--errors-only` on s26 and s59 only, because the prototype's error test approximates
`InteractionStatus.IsError`. Nothing is stripped: indentation, `inside` references, the legend, `no
response` and the line ends are all compared. Two things are mapped: provenance notes (`! ...`) are the
tool's header, not the flow, and the tool's `(no tracked calls in this scenario)` (plan F14, 3.30.3) is the
prototype's `(nothing matched the filters)`.

Prints, per lane, the views compared and how many differ (the first lines of the first five), whether each
differing view differs only by what S2 changed (indentation, `inside`, the legend, `no response`, the gaps
an empty field leaves) or by something else, which is what a control run's differences are, then what
the real output holds: indented lines, the deepest level, `inside` references in unfiltered views and in
all views, and every address that says `no response`. Reports are read with json.load; no body or header is
printed.
"""
import collections, concurrent.futures, json, pathlib, re, subprocess, sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from flow_prototype import render  # noqa: E402

CALL = re.compile(r"^( *)(s\d+/i\d+) ")


def normalise(text, real):
    lines = []
    for line in text.replace("\r\n", "\n").split("\n"):
        if real and line.startswith("! "):
            continue
        if real and line == "  (no tracked calls in this scenario)":
            line = "  (nothing matched the filters)"
        lines.append(line)
    while lines and lines[-1] == "":
        lines.pop()
    return lines


INSIDE = re.compile(r"  inside s\d+/i\d+$")
LEGEND = " · indented calls ran inside the call above them"


def s2_only(real, proto):
    """True when two views differ only by what S2 changed: indentation, `inside`, the legend, `no response`
    and the gaps an empty field leaves. What a control run's differences are, before they are quoted."""
    def fold(lines):
        out = []
        for line in lines:
            line = INSIDE.sub("", line.rstrip()).replace(LEGEND, "")
            match = CALL.match(line)
            if match:
                line = "  " + line.lstrip(" ")
                line = re.sub(r" {3,}", "  ", line.replace("  no response", ""))
            out.append(line)
        return out
    return fold(real) == fold(proto)


def main():
    tool, reports = sys.argv[1], sys.argv[2:]
    command = ["dotnet", tool] if tool.endswith(".dll") else [tool]
    total = differing = 0
    seen = collections.Counter()
    no_response = set()
    kinds = collections.Counter()
    for report in reports:
        with open(report, encoding="utf-8-sig") as f:
            data = json.load(f)
        count = sum(len(feature.get("scenarios") or []) for feature in data.get("features") or [])
        jobs = []
        for ordinal in range(count):
            jobs += [(ordinal, [], {}), (ordinal, ["--service", "CosmosDB"], {"service": "CosmosDB"}),
                     (ordinal, ["--step", "1"], {"step": "1"})]
        jobs += [(o, ["--errors-only"], {"errors_only": True}) for o in (26, 59) if o < count]

        def run(job):
            ordinal, args, kwargs = job
            real = subprocess.run([*command, "query", "flow", report, f"s{ordinal}", "--max-bytes", "0", *args],
                                  capture_output=True, encoding="utf-8", check=False)
            if real.returncode != 0:
                return job, [f"exit {real.returncode}: {real.stderr.strip()[:200]}"], []
            return job, normalise(real.stdout, True), normalise(render(data, ordinal, "nested", **kwargs), False)

        lane = pathlib.Path(report).parts[-6] if len(pathlib.Path(report).parts) >= 6 else report
        lane_differing = 0
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            for (ordinal, args, _), real, proto in pool.map(run, jobs):
                total += 1
                for line in real:
                    match = CALL.match(line)
                    if match:
                        depth = (len(match.group(1)) - 2) // 2
                        seen["call lines"] += 1
                        seen["indented lines"] += depth > 0
                        seen[f"depth {depth}"] += 1
                        seen["inside references"] += " inside s" in line
                        seen["inside references, unfiltered"] += not args and " inside s" in line
                        if "  no response" in line:
                            no_response.add(f"{lane.split('.')[-1]} {match.group(2)}")
                    seen["legends"] += line.endswith("indented calls ran inside the call above them")
                if real != proto:
                    differing += 1
                    lane_differing += 1
                    kinds["only S2's changes" if s2_only(real, proto) else "something else"] += 1
                    if lane_differing <= 5:
                        print(f"DIFFERS {lane} s{ordinal} {' '.join(args)}")
                        for a, b in zip(real + [""] * len(proto), proto + [""] * len(real)):
                            if a != b:
                                print(f"    real:  {a[:160]}\n    proto: {b[:160]}")
                                break
        print(f"{lane}: {len(jobs)} views, {lane_differing} differ")
    print(f"total: {total} views, {differing} differ")
    if kinds:
        print("the differences: " + ", ".join(f"{k} {v:,}" for k, v in sorted(kinds.items())))
    print("real output: " + ", ".join(f"{k} {v:,}" for k, v in sorted(seen.items())))
    print(f"no response: {len(no_response)} calls: {', '.join(sorted(no_response))}")
    return 1 if differing else 0


if __name__ == "__main__":
    sys.exit(main())
