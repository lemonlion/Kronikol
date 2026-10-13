"""#168 Q18's wiki edit: each MongoDB package names the driver it asks for.

python q18_wiki.py <wiki worktree> <version>   (every replacement must match exactly once)
"""
import pathlib
import sys

wiki = pathlib.Path(sys.argv[1])
V = sys.argv[2]


def edit(page, pairs):
    path = wiki / f"{page}.md"
    raw = path.read_bytes()
    crlf = b"\r\n" in raw
    text = raw.decode("utf-8").replace("\r\n", "\n")
    for old, new in pairs:
        n = text.count(old)
        assert n == 1, f"{page}: {n} matches for {old[:70]!r}"
        text = text.replace(old, new)
    if crlf:
        text = text.replace("\n", "\r\n")
    path.write_bytes(text.encode("utf-8"))


edit("Integration-MongoDB-Extension", [
    ("```bash\ndotnet add package Kronikol.Extensions.MongoDB.V2\n```\n",
     "```bash\ndotnet add package Kronikol.Extensions.MongoDB.V2\n```\n\n"
     f"From {V} each package asks for a fixed driver version: `Kronikol.Extensions.MongoDB` asks for MongoDB.Driver\n"
     "3.9.0, the oldest 3.x whose own dependencies carry no known advisory (older ones allow Snappier 1.0.0 and\n"
     "SharpCompress 0.30.1), and `Kronikol.Extensions.MongoDB.V2` for 2.28.0, the first 2.x with strong-named\n"
     "assemblies, which code on a later 2.x can compile against. Before, each release asked for the newest driver of the\n"
     "day it was packed (3.12.0 and 2.30.0 by 4.20.1), so a project that pinned an older driver met NuGet's downgrade\n"
     "error (NU1605). A project that takes the driver only through Kronikol now resolves 3.9.0 or 2.28.0; reference\n"
     "MongoDB.Driver yourself for a newer one.\n"),
])
print("edited Integration-MongoDB-Extension.md")
