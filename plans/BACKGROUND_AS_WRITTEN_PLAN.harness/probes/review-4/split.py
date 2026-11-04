import os, sys
name = None; lines = []
def flush():
    if name:
        open(os.path.join(sys.argv[2], name), "w", encoding="utf-8", newline="\n").write("".join(lines).strip() + "\n")
for line in open(sys.argv[1], encoding="utf-8"):
    if line.startswith("#file "):
        flush(); name = line[6:].strip(); lines = []
    else:
        lines.append(line)
flush()
