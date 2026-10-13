"""#168 R3's wiki edits: the query tool reads a capped body, orders numbers only, refuses `..`; MaxContentLength caps variants.

python r3_wiki.py <wiki worktree> <version>   (after r2_wiki.py; every replacement must match exactly once)
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


edit("Querying-Reports", [
    ("comparison; `~` is substring. Wildcard paths use\n",
     f"comparison; `~` is substring.\nFrom {V}, `<`, `>`, `<=` and `>=` against a number compare numbers only: a value that\n"
     "is not a number (an object such as `{ \"$numberDecimal\": \"1000.50\" }`, an array, a string) fails the clause,\n"
     "and the footer counts it (`1 value was not a number for --where \"$.total > 500\", and failed it`); before, it was\n"
     "ordered as text, so an object passed `> 5`. Wildcard paths use\n"),
    ("body is missing or not JSON fails the predicate, and the footer reports how many were excluded that way.\n",
     "body is missing or not JSON fails the predicate, and the footer reports how many were excluded that way.\n"
     f"From {V} a body a capture capped (a complete document, then `... (N more documents not shown)` or `... (N more\n"
     "rows not shown)` on its own line) is read as that document, as the page reads it, and counted as capped; so are\n"
     "`--path`, `--keys`, `--body`, `grep --values`, `grep --number` and `diff --body`, where each read such a body as\n"
     "text.\n"),
    ("**Single-quote paths** — `--path '$.items[*].price'`",
     f"`..` (recursive descent) is refused from {V}, and the error names the `[*]` path to use (`$..price` →\n"
     "`$[*].price`); before, each dot was skipped, so `$..price` was read as `$.price` and matched nothing in an array\n"
     "body.\n\n"
     "**Single-quote paths** — `--path '$.items[*].price'`"),
    ("with a pointer at `compare`. Capture-time truncation markers inside a body surface here the same way\n`body` surfaces them.",
     "with a pointer at `compare`. Capture-time truncation markers inside a body surface here the same way\n`body` surfaces them. "
     f"From {V} two integers are compared by their digits, so Int64 values past 2^53 that differ\n"
     "(`$.id: 9007199254740993 → 9007199254740992`) are reported, where both were read as one double; a number with a\n"
     "fraction or an exponent is still compared as a double. A capped batch is diffed as its document, and the note\n"
     "names both footnotes when they differ (`... (3 more documents not shown) → ... (4 more documents not shown)`)."),
])

edit("Tracking-Custom-Dependencies", [
    ("Set to `null` (the default) to disable truncation.\n",
     "Set to `null` (the default) to disable truncation.\n\n"
     f"From {V} the cap also applies to a call's phase variants, the notes the diagram draws for its setup and action\n"
     "phases at another verbosity, which it left whole.\n"),
])
print("edited Querying-Reports.md Tracking-Custom-Dependencies.md")
