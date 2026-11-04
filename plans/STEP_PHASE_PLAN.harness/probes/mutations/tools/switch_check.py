"""The worktree, with cut 11 applied by git, holds exactly the snapshot the mutants were checked against."""
import os
import sys

from dumps import MUT
from mutations11 import PATCHED_FILES

bad = []
for rel in PATCHED_FILES:
    with open(os.path.join(MUT, "wt", rel), "rb") as handle:
        tree = handle.read()
    with open(os.path.join(MUT, "patched11", rel), "rb") as handle:
        snapshot = handle.read()
    if tree != snapshot:
        bad.append(rel)
print("cut 11 snapshot differs: " + ", ".join(bad) if bad else f"cut 11 snapshot matches the worktree, {len(PATCHED_FILES)} files")
sys.exit(1 if bad else 0)
