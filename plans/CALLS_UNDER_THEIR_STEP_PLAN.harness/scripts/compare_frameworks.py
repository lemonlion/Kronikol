"""Compares the reviewer's framework probes between base and a prototype: is each tree the same, and does each call keep its top-level step?"""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])


def read(fw, copy):
    best = None
    for f in (root / f"{fw}-{copy}").rglob("fw-out.txt"):
        if "Release" in str(f) and (best is None or f.stat().st_mtime > best.stat().st_mtime):
            best = f
    return best


def sections(text):
    out = {}
    current = None
    for line in re.sub(r"\b\d+(\.\d+)? ?(ms|s)\b", "T", text).splitlines():
        match = re.match(r"=== \S+ (variant \S+)", line)
        if match:
            current = match.group(1)
            out[current] = ([], {}, [])
        elif current:
            call = re.match(r"\s*call (\S+)\s+-> (\S+)", line)
            if call:
                out[current][1].setdefault(call.group(1), []).append(call.group(2))
            elif "scenario attachments" in line:
                out[current][2].append(line.strip())
            else:
                out[current][0].append(line.rstrip())
    return out


total = deep = moved = trees = 0
for fw in ["xunit3", "xunit2", "nunit", "mstest", "tunit"]:
    a, b = read(fw, "base"), read(fw, "proto")
    if not a or not b:
        print(fw, "no output", a is not None, b is not None)
        continue
    sa = sections(a.read_text(encoding="utf-8", errors="replace"))
    sb = sections(b.read_text(encoding="utf-8", errors="replace"))
    for variant in sorted(sa):
        ta, ca, xa = sa[variant]
        tb, cb, xb = sb.get(variant, ([], {}, []))
        same_tree = ta == tb and xa == xb
        trees += 1 if same_tree else 0
        differences = []
        for path, values in ca.items():
            for x, y in zip(values, cb.get(path, [])):
                total += 1
                if "." in y:
                    deep += 1
                if x.split(".")[0] != y.split(".")[0]:
                    moved += 1
                    differences.append(f"{path} {x} -> {y}")
        print(f"{fw:7} {variant:10} tree {'same' if same_tree else 'DIFFERS'}; calls moved between top-level steps: {differences or 'none'}")
        if not same_tree:
            print("   base  :", " | ".join(x.strip() for x in ta + xa)[:300])
            print("   proto :", " | ".join(x.strip() for x in tb + xb)[:300])
print(f"variants with the same tree {trees}; calls {total}, on a sub-step {deep}, moved {moved}")
