"""The wiki edits of #168 R1 (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md section 6.3), applied to a worktree of the wiki.

    python r1_wiki.py <wiki worktree> <version>

Each edit replaces text that must appear exactly once, so a page that moved on fails loudly instead of being edited
in the wrong place.
"""
import sys
from pathlib import Path

wiki = Path(sys.argv[1])
version = sys.argv[2]


def edit(page, old, new):
    path = wiki / page
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{page}: expected the text once, found it {count} times:\n{old[:200]}")
    path.write_text(text.replace(old, new), encoding="utf-8", newline="")


MONGO = "Integration-MongoDB-Extension.md"

# F16: a find is drawn with the read arrow, as the page's own arrow table says.
edit(MONGO,
     "your sequence diagrams show classified operations like `Find → users` with a clean",
     "your sequence diagrams show classified operations like `Find ← users` with a clean")

edit(MONGO,
     "| **Raw** | `Find mydb.users filter={...}` | `mongodb:///mydb/users` | Full BSON command | Full BSON reply |\n"
     "| **Detailed** | `Find → users` | `mongodb:///mydb/users` | Filter text only | Metadata + document preview |\n"
     "| **Summarised** | `Find` | `mongodb:///mydb` | None | Metadata + document preview |",
     "| **Raw** | `Find mydb.users filter={...}` | `mongodb:///mydb/users` | Full BSON command | The whole reply, as JSON |\n"
     "| **Detailed** | `Find ← users` | `mongodb:///mydb/users` | Filter text only | The reply as JSON: the documents, or the counts and ids (see [Response Payload Capture](#response-payload-capture-v2370)) |\n"
     "| **Summarised** | `Find` | `mongodb:///mydb` | None | As at **Detailed**; none when `LogResponseContent` is `false` |")

edit(MONGO,
     "- `{ \"aggregate\": 1, \"pipeline\": [...] }` → database-level aggregate, collection = null\n",
     "- `{ \"aggregate\": 1, \"pipeline\": [...] }` → database-level aggregate, collection = null\n"
     f"- `{{ \"getMore\": 8402291234, \"collection\": \"users\" }}` → collection = `users`. A getMore's own element holds the cursor id, so it names its collection in `collection` (read since {version}; before, a getMore was drawn against no collection)\n")

edit(MONGO,
     "| `LogResponseContent` | `bool` | `true` | Include response content (documents from `cursor.firstBatch`) in diagram response arrows at all verbosity levels (v2.37.3+). At Raw, the full reply is always shown regardless. Set to `false` to restore previous empty-arrow behaviour |\n"
     "| `MaxResponseDocuments` | `int` | `10` | Maximum number of documents to include from `cursor.firstBatch` in response content (v2.37.0+) |",
     f"| `LogResponseContent` | `bool` | `true` | Include the documents a reply returns (a cursor's batch, a findAndModify's `value`, a distinct's `values`) in its note (v2.37.3+). With `false` they are left out and the rest of the reply stays (since {version}; before, only `n`, `nModified` and `nUpserted` did), and at **Summarised** a reply has no note. At **Raw** the whole reply is always shown |\n"
     "| `MaxResponseDocuments` | `int` | `10` | How many documents of a cursor's batch (a find's `firstBatch`, a getMore's `nextBatch`) a note holds; `... (N more documents not shown)` follows on its own line (v2.37.0+) |")

edit(MONGO,
     "Response arrows in diagrams show document data from `cursor.firstBatch` instead of being empty. At **Detailed** and **Summarised** verbosity, response content includes metadata and document previews. At **Raw**, the full BSON reply is always shown.\n"
     "\n"
     "Response content is shown at **all** verbosity levels when `LogResponseContent = true` (the default). Set `LogResponseContent = false` to restore previous empty-arrow behaviour.\n",
     f"A reply's note is JSON (since {version}), on driver 2.x and 3.x alike:\n"
     "\n"
     "- A cursor's batch (a find's or an aggregate's `firstBatch`, a getMore's `nextBatch`) is the documents as an array, `[]` when there are none, held to `MaxResponseDocuments`.\n"
     "- Any other reply is the reply without the server's bookkeeping (`ok`, `$clusterTime`, `operationTime`, `electionId` and `opTime`, and on a sharded cluster `recoveryToken`, `retriedStmtIds`, `readOnly` and `$replData`). An update is `{ \"n\" : 1, \"nModified\" : 0 }`, an upsert names the new `_id` under `upserted`, a findAndModify holds `lastErrorObject` and `value` (`\"value\" : null` when it took nothing), and a distinct its `values`. A cursor beside other fields is replaced by its batch.\n"
     "- A reply with nothing but bookkeeping has no note, and `serverStatus`, `dbStats` and `collStats` replies are left out at **Detailed** and **Summarised**.\n"
     "- Values are MongoDB relaxed extended JSON, the same bytes whichever driver the host runs: `{ \"$oid\" : \"…\" }`, `{ \"$date\" : \"2024-01-01T00:00:00.123Z\" }`, a GUID as `{ \"$uuid\" : \"…\" }`, and a binary over 256 bytes as `\"[binary: 261,120 bytes, subtype 00]\"`. A document keeps the driver's spacing on one line, so a redaction pattern written for the driver's text (`\"email\" : \"…\"`) matches it. The diagram re-indents it and offers the YAML view.\n"
     "- At **Raw** the note is the whole reply, bookkeeping and large binaries included, in the same JSON.\n"
     "\n"
     f"Before {version} the note was `n=…, nModified=…` text with the batch after it in the driver's own syntax (the mongo shell's on driver 2.x), which the YAML view and `kronikol query` could not read.\n"
     "\n"
     "### Failures\n"
     "\n"
     "A command the server refused is drawn as a failure (`500`), and its note is the server's reply without the bookkeeping: `{ \"errmsg\" : \"unknown operator: $bogus\", \"code\" : 2, \"codeName\" : \"BadValue\" }`. "
     f"Since {version} so is a write error, which the server sends as `ok: 1` with `writeErrors` or a `writeConcernError`, and a client `bulkWrite` whose reply counts `nErrors`; before, a duplicate key was drawn `OK`. "
     "The call's error is the server's `errmsg` (for a write error, the first one), so `Failures.md` and `kronikol query` say why it failed. A failure that brought no reply, such as a timeout, keeps the driver's message.\n"
     "\n"
     f"No exception from the subscriber reaches the driver (since {version}). A command it cannot record is left out of the diagrams, and the report says so once, as a `CaptureDegraded` diagnostic.\n")

edit(MONGO,
     "    LogResponseContent = true,          // default — show response data\n",
     "    LogResponseContent = true,          // default: the documents a reply returns\n")

TAP = "Integration-TcpTap-Extension.md"

edit(TAP,
     "Notes carry the key/value and the filter/documents, capped at `BodyCapBytes`.",
     f"Notes carry the key and value, and a MongoDB command's filter and its reply as JSON (since {version}), capped at `BodyCapBytes`.")

edit(TAP,
     "| `LogResponseContent` | `true` | Include `cursor.firstBatch` documents in the response note. |\n"
     "| `MaxResponseDocuments` | `10` | How many of them (`… (N more documents not shown)` after that). |",
     "| `LogResponseContent` | `true` | Include the documents a reply returns (a cursor's batch, a findAndModify's `value`, a distinct's `values`) in its note. With `false` they are left out and the rest of the reply stays. |\n"
     "| `MaxResponseDocuments` | `10` | How many documents of a batch (`... (N more documents not shown)` after that). |")

edit(TAP,
     "| `Summarised` | `Get`, URI `redis://db0/`, no notes; unclassified verbs dropped entirely | `Find`, URI `mongodb:///{db}`, no notes |\n"
     "| `Detailed` *(default)* | `Get (Hit)`, URI `redis://db0/{key}`, value in the note | `Find ← Trial`, URI `mongodb:///{db}/{coll}`, filter in the request note, `n=…` / documents in the reply note |",
     "| `Summarised` | `Get`, URI `redis://db0/`, no notes; unclassified verbs dropped entirely | `Find`, URI `mongodb:///{db}`, no request note; the reply note as at `Detailed` unless `LogResponseContent` is off |\n"
     f"| `Detailed` *(default)* | `Get (Hit)`, URI `redis://db0/{{key}}`, value in the note | `Find ← Trial`, URI `mongodb:///{{db}}/{{coll}}`, filter in the request note, the reply as JSON in the reply note (since {version}: the documents, or the counts and ids, as [[Integration MongoDB Extension › Response Payload Capture|Integration-MongoDB-Extension#response-payload-capture-v2370]] describes); a refused command or a write error is a `500` whose error is the server's `errmsg` |")

FORMATTING = "Content-Formatting.md"

edit(FORMATTING,
     "The document is now parsed and indented on its own — nulls dropped, exactly as for the uncapped response beside it — and the footnote keeps its own line.",
     "The document is now parsed and indented on its own, exactly as the uncapped response beside it is, and the footnote keeps its own line.")

edit(FORMATTING,
     "Applies to every SQL tracker (SqlClient, Npgsql, MySqlConnector, Sqlite, Oracle, ClickHouse, Dapper, EF Core), plus Spanner and MongoDB The rows",
     f"Applies to every SQL tracker (SqlClient, Npgsql, MySqlConnector, Sqlite, Oracle, ClickHouse, Dapper, EF Core), plus Spanner and MongoDB. A MongoDB reply note is JSON since {version}, so it has the toggle too; before, a write's `n=…` line in front of its documents kept it from parsing. The rows")

edit(FORMATTING,
     "- Pretty-prints JSON bodies with indentation\n",
     "- Pretty-prints JSON bodies with indentation\n"
     f"- Drops a null property from a JSON body as noise, except in the note of a database call (SQL, MongoDB, Redis, Cosmos DB, DynamoDB and the other database trackers), where a null is part of the statement: `{{ \"deletedAt\" : null }}` matches the documents with no `deletedAt`, and `{{}}` matches every document. Until {version} those nulls were dropped too\n")

INGEST = "Ingesting-External-Captures.md"

edit(INGEST,
     "customer ids, cache-key fragments, anything a captured record's URI or body would literally contain.",
     f"customer ids, cache-key fragments, anything a captured record's URI or body would literally contain. A claim is matched against a body's text, so name a value (an id) rather than a note's syntax: a MongoDB reply's text changed in {version}, when it became JSON.")

print("edited", MONGO, TAP, FORMATTING, INGEST)
