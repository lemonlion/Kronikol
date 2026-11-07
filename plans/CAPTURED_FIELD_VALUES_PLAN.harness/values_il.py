"""Prints every values array the weave builds in an `ilspycmd -il` dump: the method, the instructions from
`newarr [System.Runtime]System.Object` to the Track call that takes the array, and for each element the
instructions that load it.

    python values_il.py <dump.il> [method-name-substring ...]
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

path = sys.argv[1]
wanted = sys.argv[2:]
lines = open(path, encoding="utf-8").read().splitlines()

class_stack = []  # (name, depth at which it opened)
depth = 0
method = None
pending_class = None
pending_method = None
i = 0
out = []
while i < len(lines):
    line = lines[i]
    s = line.strip()
    m = re.match(r"\.class .*?(\S+)$", s)
    if m:
        pending_class = m.group(1).strip("'")
    if s.startswith(".method"):
        pending_method = ""
    if pending_method is not None and not s.startswith(".method"):
        # the method's name is on the line holding its parameter list
        mm = re.search(r"([^\s]+)\s*\(", s)
        if mm and "cil managed" in s or (mm and s.endswith(")")):
            pending_method = mm.group(1).strip("'")
    if s == "{":
        depth += 1
        if pending_class is not None:
            class_stack.append((pending_class, depth))
            pending_class = None
        elif pending_method:
            method = "/".join(c for c, _ in class_stack) + "::" + pending_method
            pending_method = None
    elif s.startswith("}"):
        if class_stack and class_stack[-1][1] == depth:
            class_stack.pop()
        depth -= 1
    if "newarr [System.Runtime]System.Object" in s and (not wanted or any(w in (method or "") for w in wanted)):
        block = []
        j = i
        while j < len(lines):
            block.append(lines[j].strip())
            if re.search(r"Kronikol\.Tracking\.Track::Assertion\w*", lines[j]):
                break
            j += 1
        out.append((method, block))
    i += 1

for method, block in out:
    print("=" * 100)
    print(method)
    print("-" * 100)
    for b in block:
        print("  " + b)
    # each element: the instructions between `dup; ldc.i4.N` and its stelem.ref, up to the array's stloc
    print("-" * 100)
    k = 1
    n = 0
    while k < len(block):
        if block[k].endswith(": dup") and k + 1 < len(block) and re.search(r"ldc\.i4", block[k + 1]):
            idx = block[k + 1].split(": ", 1)[1]
            loads = []
            k += 2
            while k < len(block) and not block[k].endswith("stelem.ref"):
                loads.append(block[k].split(": ", 1)[1] if ": " in block[k] else block[k])
                k += 1
            print(f"  element {n} ({idx}): " + " ; ".join(loads))
            n += 1
        elif re.search(r": stloc", block[k]):
            break
        k += 1
print("=" * 100)
print(f"{len(out)} values array(s)")
