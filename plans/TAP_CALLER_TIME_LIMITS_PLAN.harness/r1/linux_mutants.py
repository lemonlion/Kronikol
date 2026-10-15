"""Writes R1's managed-listener-only mutants as overlay trees for the Linux run: one directory per mutation holding the
mutated file at its path in the tree.

    python linux_mutants.py <R1 tree> <out dir> <id> ...
"""
import importlib.util
import pathlib
import sys

spec = importlib.util.spec_from_file_location("r1", pathlib.Path(__file__).resolve().parent / "mutations_r1.py")
r1 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r1)

tree, out, ids = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), set(sys.argv[3:])
for name, rel, _, old, new in r1.runner.MUTATIONS:
    key = name.split()[0]
    if key not in ids:
        continue
    text = (tree / rel).read_bytes().decode("utf-8")
    assert text.count(old) == 1, (key, text.count(old))
    target = out / key / rel
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(text.replace(old, new).encode("utf-8"))
    print("wrote", key, rel)
