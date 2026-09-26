"""S1's acceptance on real reports: the built `flow` against the prototype's nested mode with the nesting taken out.

    python s1_acceptance.py <Kronikol.Tool.exe> <report.json>...

S1 (plan §4.4) is the prototype's header and annotation placement without S2's indentation, so the two
must agree once the prototype's indentation, `inside` references and legend are removed and trailing
whitespace is ignored (the prototype trims call lines; S1 does not). Every scenario of each report is
compared unfiltered, with `--service CosmosDB` and with `--step 1`; `--errors-only` on s26 and s59 only,
because the prototype's error test approximates `InteractionStatus.IsError` (plan §7.5).

The real tool's empty-view line for a scenario with no calls, `(no tracked calls in this scenario)`, is
S1's own finding and is compared as the prototype's `(nothing matched the filters)`. Provenance notes
(`! ...`) are the tool's header, not the flow, and are dropped. Prints counts and any differing lines; the
reports are read with json.load and no body or header is printed.
"""
import concurrent.futures, json, pathlib, re, subprocess, sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from flow_prototype import render  # noqa: E402

CALL = re.compile(r"^\s+(s\d+/i\d+)")
INSIDE = re.compile(r"  inside s\d+/i\d+$")
LEGEND = " · indented calls ran inside the call above them"


def normalise(text, real):
    lines = []
    for line in text.replace("\r\n", "\n").split("\n"):
        if real and line.startswith("! "):
            continue
        line = CALL.sub(r"  \1", line.rstrip())
        line = INSIDE.sub("", line).replace(LEGEND, "")
        if real and line == "  (no tracked calls in this scenario)":
            line = "  (nothing matched the filters)"
        lines.append(line)
    while lines and lines[-1] == "":
        lines.pop()
    return lines


def main():
    tool, reports = sys.argv[1], sys.argv[2:]
    total = differing = 0
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
            real = subprocess.run([tool, "query", "flow", report, f"s{ordinal}", "--max-bytes", "0", *args],
                                  capture_output=True, encoding="utf-8", check=False)
            if real.returncode != 0:
                return job, [f"exit {real.returncode}: {real.stderr.strip()[:200]}"], []
            return job, normalise(real.stdout, True), normalise(render(data, ordinal, "nested", **kwargs), False)

        lane = pathlib.Path(report).parts[-6] if len(pathlib.Path(report).parts) >= 6 else report
        lane_differing = 0
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            for (ordinal, args, _), real, proto in pool.map(run, jobs):
                total += 1
                if real != proto:
                    differing += 1
                    lane_differing += 1
                    if lane_differing <= 5:
                        print(f"DIFFERS {lane} s{ordinal} {' '.join(args)}")
                        for a, b in zip(real + [""] * len(proto), proto + [""] * len(real)):
                            if a != b:
                                print(f"    real:  {a[:150]}\n    proto: {b[:150]}")
                                break
        print(f"{lane}: {len(jobs)} views, {lane_differing} differ")
    print(f"total: {total} views, {differing} differ")
    return 1 if differing else 0


if __name__ == "__main__":
    sys.exit(main())
