"""The fluent BDDfy probe's project for a worktree: the third review's, compiling this probe, against that worktree. Usage: bddfy_fluent_project.py <model> <out> <probe dir> <worktree>"""
import pathlib
import re
import sys

model, out, probe, worktree = sys.argv[1:5]
s = pathlib.Path(model).read_text(encoding="utf-8")
item = '<Compile Include="' + probe + "\\*.cs" + '" />'
s = re.sub(r'<Compile Include="[^"]*"\s*/>', lambda m: item, s)
s = re.sub(r"C:\\Code\\[A-Za-z0-9-]+\\src", lambda m: worktree + "\\src", s)
pathlib.Path(out).write_text(s, encoding="utf-8")
