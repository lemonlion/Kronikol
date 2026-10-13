"""#168 R2's wiki edits: the request note at Detailed, MaxRequestDocuments, and the shape version.

python r2_wiki.py <wiki worktree> <version>   (after r1_wiki.py; every replacement must match exactly once)
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
    ("| **Raw** | `Find mydb.users filter={...}` | `mongodb:///mydb/users` | Full BSON command |",
     "| **Raw** | `Find mydb.users filter={...}` | `mongodb:///mydb/users` | The whole command, as JSON |"),
    ("| `mongodb:///mydb/users` | Filter text only |",
     f"| `mongodb:///mydb/users` | The command as JSON, without its name or the driver's fields: a find's filter, sort and limit, an insert's documents, an update's statements (since {V}; before, the filter only) |"),
    ("| `LogFilterText` | `bool` | `true` | When `true`, includes the filter document text in **Detailed** mode request content |",
     f"| `LogFilterText` | `bool` | `true` | When `true`, the **Detailed** request note is the command as JSON, without its name and without the driver's own fields (`$db`, `lsid`, `txnNumber`, `$clusterTime`, the transaction's and the read and write concerns' fields, the API version, `ordered` when it is `true`); before {V}, the filter alone. When `false`, there is no request note at **Detailed** |"),
    ("follows on its own line (v2.37.0+) |\n",
     "follows on its own line (v2.37.0+) |\n"
     f"| `MaxRequestDocuments` | `int` | `10` | How many entries of a request's documents (an insert's) or statements (an update's, a delete's, a client bulkWrite's) a request note holds; `... (N more documents not shown)` or `... (N more)` follows on its own line ({V}+) |\n"),
])

edit("Integration-TcpTap-Extension", [
    ("| `LogFilterText` | `true` | Include the command's `filter` as the request note at `Detailed`. |",
     f"| `LogFilterText` | `true` | Include the request note at `Detailed`: the command as JSON, without its name or the driver's own fields (before {V}, its `filter`). |"),
    ("| `MaxResponseDocuments` | `10` | How many documents of a batch (`... (N more documents not shown)` after that). |",
     "| `MaxResponseDocuments` | `10` | How many documents of a batch (`... (N more documents not shown)` after that). |\n"
     f"| `MaxRequestDocuments` | `10` | How many documents or statements a request note holds (`... (N more documents not shown)`, or `... (N more)` for statements, after that; {V}+). |"),
    ("filter in the request note,",
     f"the command without the driver's fields in the request note (before {V}, the filter),"),
])

edit("Cross-Run-History", [
    ("the first run after an upgrade that\nchanged the rule reads no behaviour verdict against the runs before it and says so.",
     "the first run after an upgrade that\nchanged the rule reads no behaviour verdict against the runs before it and says so. The version also moves\n"
     f"when what a call records moves under it: {V} records a MongoDB command without the driver's fields in place of\n"
     "its bare filter, so every MongoDB call's statement head changed, and the version went to 5."),
])
print("edited Integration-MongoDB-Extension.md Integration-TcpTap-Extension.md Cross-Run-History.md")
