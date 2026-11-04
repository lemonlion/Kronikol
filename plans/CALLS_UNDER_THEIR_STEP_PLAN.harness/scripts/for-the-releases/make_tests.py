"""Copies the plan's kept ingest cases into the test project, and expands StepWalkTests.template.cs into StepWalkTests.cs.

A token @@set/ID in the template becomes [InlineData("set/case-name", "what R2 reads")], from the result file of that set.
Reads as written in those files: `s0 /a=0 /b=-`, scenarios joined by `; `. Also writes cases.tsv: set/case, published's read, R2's.
The red proof reads cases.tsv: on the old code exactly the inline rows whose two reads differ must fail.
"""
import pathlib
import re
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8")

H = pathlib.Path(r"C:/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness")
M = pathlib.Path(r"C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/84bdf88c-b056-4543-a3d5-5d3d039eba20/scratchpad/m129")
HERE = pathlib.Path(__file__).parent
WT = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else r"C:/Code/Kronikol-225r2")
DATA = WT / "tests/Kronikol.Tests/TestData/StepWalk"
N = H / "probes-matcher/narrow-check-cases"

SETS = {
    "peer": ([M / "cases", H / "probes-matcher/peer-case-af"], "r2-peer-cases.txt"),
    "position": ([M / "cases-pos", M / "cases-pos2"], "r2-position.txt"),
    "store": ([H / "probes-matcher/store-cases"], "r2-stores.txt"),
    "review": ([M / "cases-rv/cases", M / "cases-rv/cases2", M / "cases-rv/cases3", M / "cases-rv/cases4", M / "cases-rv/cases5"], "r2-review-cases.txt"),
    "fresh": ([H / "probes-matcher/fresh-check-cases"], None),
    "narrow": ([N / "cases", N / "cases2"], "r2-narrow-check-cases.txt"),
    "second": ([N / "second/cases", N / "second/cases2", N / "second/cases3"], "r2-second-narrow-check-cases.txt"),
    "text": ([N / "third"], "r2-text-check-cases.txt"),
}


def parse(path):
    """case -> (published's read, R2's read); a case whose builds agree has one block."""
    out, case, label, blocks = {}, None, None, {}

    def flush():
        if case:
            pub = "; ".join(blocks.get("pub", []))
            out[case] = (pub, "; ".join(blocks["r2"]) if "r2" in blocks else pub)

    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("=== "):
            flush()
            case, label, blocks = line[4:].strip(), None, {}
        elif re.match(r"  (\w+): flags", line):
            label = line.split(":", 1)[0].strip()
            blocks[label] = []
        elif line.startswith("    ") and label:
            blocks[label].append(line.strip())
    flush()
    return out


reads, names = {}, {}
for name, (sources, result) in SETS.items():
    parsed = parse(H / "results" / result) if result else {}
    for source in sources:
        for case in sorted(p for p in source.iterdir() if (p / "tests.ndjson").exists()):
            key = f"{name}/{case.name}"
            assert key not in names, key
            names[key] = case
            if case.name in parsed:
                reads[key] = parsed[case.name]

if DATA.exists():
    shutil.rmtree(DATA)
for key, case in names.items():
    to = DATA / key
    (to / "captures").mkdir(parents=True)
    shutil.copyfile(case / "tests.ndjson", to / "tests.ndjson")
    for f in (case / "captures").glob("*"):
        shutil.copyfile(f, to / "captures" / f.name)
    flags = case / "flags.txt"
    if flags.exists() and flags.read_text(encoding="utf-8").strip():
        shutil.copyfile(flags, to / "flags.txt")
labels = DATA / "labels/all"
(labels / "captures").mkdir(parents=True)
shutil.copyfile(H / "probes-ingest/tests.ndjson", labels / "tests.ndjson")
shutil.copyfile(H / "probes-ingest/interactions.ndjson", labels / "captures/calls.ndjson")

(HERE / "cases.tsv").write_text("".join(f"{k}\t{p}\t{r}\n" for k, (p, r) in sorted(reads.items())), encoding="utf-8", newline="\n")
print(len(names), "cases copied,", len(reads), "with reads")

template = HERE / "StepWalkTests.template.cs"
if template.exists():
    text = template.read_text(encoding="utf-8")

    def expand(match):
        indent, s, ident, override = match.group(1), match.group(2), match.group(3), match.group(4)
        hits = [k for k in names if k.startswith(f"{s}/{ident}-") or k == f"{s}/{ident}"]
        assert len(hits) == 1, (s, ident, hits)
        key = hits[0]
        read = override.strip() if override else reads[key][1]
        return f'{indent}[InlineData("{key}", "{read}")]'

    used = set()
    original = expand

    def expand(match, original=original):
        line = original(match)
        used.add(line.split('"')[1])
        return line

    text = re.sub(r"^( *)@@(\w+)/([\w]+)(?: => (.*))?$", expand, text, flags=re.M)
    # A case that writes no report has nothing to read (peer/W, a fault #222's plan names).
    rest = [k for k in sorted(reads) if k not in used and reads[k][1] and not k.startswith("peer/W-")]
    text = text.replace("    @@REST", chr(10).join(f'    [InlineData("{k}", "{reads[k][1]}")]' for k in rest))
    print(len(used), "named,", len(rest), "in the rest")
    out = WT / "tests/Kronikol.Tests/Ingestion/StepWalkTests.cs"
    out.write_text(text, encoding="utf-8", newline="\n")
    print("wrote", out)
