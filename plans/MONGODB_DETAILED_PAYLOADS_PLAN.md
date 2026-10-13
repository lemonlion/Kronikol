# MongoDB at Detailed: what a command sent and what came back, as JSON (#168)

**Date:** 2026-10-13 · **Repo version:** 4.18.1 (`main` at `34145c44`; line numbers are for that commit, and the
issue's, at `98c4806d`, point at the same lines) · **Status:** **DRAFT, not green-lit.** Drafted from #168 (filed by the
owner on 2026-10-11, found in #132's multi-host suite on 4.14.6 with `Kronikol.Extensions.MongoDB.V2` and MongoDB.Driver
2.30.0) at the owner's ask of 2026-10-13 to analyse the issue critically and plan a fix for the underlying problem.

Evidence labels: **RUN** (measured on 2026-10-13 on Windows 11 with the published 4.18.1 packages: MongoDB.Driver 2.30.0
and 3.12.0 against `mongo:7.0` (7.0.43) and `mongo:8.0` (8.0.32), each a single-node replica set in podman, and
MongoDB.Bson 2.10.0, 2.19.0, 2.30.0 and 3.12.0 in `dotnet run` apps; scripts and output in
[`MONGODB_DETAILED_PAYLOADS_PLAN.harness/`](MONGODB_DETAILED_PAYLOADS_PLAN.harness/README.md)), **READ** (in Kronikol's
source, tests, changelog or wiki at that commit, the wiki at `origin/master` `85abe19`, or Kronikol4J at its
`origin/main`), **INFERRED** (reasoned from two facts, stated by neither), **ESTIMATED** (not measured).

> **At `Detailed`, a write is drawn with no payload on either side,** so the diagram shows that something was written
> and not what. An insert's request is empty (the label counts the documents) and its reply is `n=3`. An update's
> request is empty, not even the filter, and its reply `n=1, nModified=0`. A findAndModify has nothing on either side.
> Only a find shows its filter and up to 10 documents. **The bodies it does record aren't JSON on driver 2.x**
> (`NumberDecimal("1000")`, `ObjectId("…")`, `ISODate("…")`), and a write's count is a line before them, so the YAML
> view and `kronikol query values`/`--where` can't read them. **Suggestion:** at `Detailed`, record what a write sends
> (an insert's documents, capped like `MaxResponseDocuments`; each update's `q` and `u`; each delete's `q`; a
> findAndModify's `query` and `update`; without the driver's session fields) and a findAndModify's `value`. Write
> every body as relaxed extended JSON, and move the counts out of the body.

## 0. Summary

The symptom is real: every row of the issue's table reproduces on both drivers and both servers (§1). The cause the
issue names is right as far as it goes. The underlying problem is wider, in five ways that change the fix.

1. **The shell syntax is a driver 2.x default, and the obvious fix breaks the wire tap.** On driver 3.x, which the main
   package targets, the documents are already relaxed extended JSON (3.0 changed the default of `JsonWriterSettings`).
   On 3.x, what stops a body being JSON is Kronikol's own text: the `n=` line, `0 documents`, and (for the query tool
   only) the cap footnote. Setting `OutputMode = RelaxedExtendedJson` in the shared source would fix the V2 package and
   break `Kronikol.Extensions.TcpTap`, which compiles against MongoDB.Bson 2.x: the enum's value is 3 there and 2 on
   3.x, so on a 3.x host the tap would write shell syntax and extended JSON mixed in one body (RUN, §2.2). Kronikol
   needs a BSON-to-JSON writer of its own, which writes the same bytes on both drivers and in the tap.
2. **What came back is wrong as well as what was sent.**
   - A duplicate-key insert is drawn `OK` with `n=0` while the application throws `MongoWriteException`.
   - An upsert's id is never shown: the code reads `nUpserted`, which no reply carries.
   - A findAndModify, a distinct and a getMore record no reply at all.
   - A server error is recorded as `find command failed`, while the reply's `errmsg` says `unknown operator: $bogus`.

   All four are RUN (§3).
3. **At `Raw`, Kronikol makes the application's own `FindOneAndUpdate` throw.** The claim-by-filter path keeps the
   driver's command document until the reply, and the driver disposes that document when the started handler returns.
   At `Raw` the reply handler reads it. The exception reaches the application's call after the server has applied the
   update (RUN, F6). Any fix that reads the command at reply time, as recording a findAndModify's `query` and `update`
   naively would, hits the same exception.
4. **The issue's own query could not have worked, and would not after the fix.**
   - `--path '$..CurrentAmount'` is read as `$.CurrentAmount`, because the path parser skips every dot. That path
     never matches an array body.
   - Once bodies are extended JSON, `--where '$.CurrentAmount > 500'` is true for every decimal, because an object is
     ordered against a number as text.

   Both are READ (F7).
5. **The history fingerprint reads a MongoDB call's request body.** Its templated first line is part of the call's
   shape (`InteractionShape.Target`). So recording write payloads moves every MongoDB scenario's shape once, and an
   indented body would collapse every MongoDB call's shape to `{` (READ, F9).

**The plan** is three releases, split by side so each body changes once:
- **R1 (patch): what came back.** A Kronikol-owned writer (relaxed extended JSON, compact, the same in the V2 package
  and the tap). The reply note is the cursor's batch as a JSON array, or the reply without the server's bookkeeping as
  a JSON object: counts, `upserted`, `lastErrorObject` and `value`, `values`, `writeErrors`. R1 also:
  - draws write errors and write-concern errors as failures;
  - keeps a server error's `errmsg`, `code` and `codeName`;
  - draws a getMore against its collection;
  - stops any exception reaching the driver, and renders, when the command starts, every note the deferred path needs.
- **R2 (minor): what was sent.** At `Detailed` the request note is the command without its name and without the
  driver's own fields. An insert shows its documents, an update its `q` and `u`, a findAndModify its `query` and
  `update`, a find its filter, sort and limit. The documents are capped by a new `MaxRequestDocuments`, a large binary
  is written as a placeholder, and the history fingerprint's version moves.
- **R3 (patch): the query tool reads what the report reads.** `..` is refused with a pointer to `[*]`, an object is
  never ordered against a number, the capture footnote is taken off before a body is parsed, and the content cap also
  caps phase variants.

**Where the plan departs from the issue's suggestion** (each is a question in §9):
- The counts stay in the body, as JSON. For an update or a delete they are the whole reply, and the label counts the
  request, not the reply (Q4).
- The request note is the command minus the driver's fields for every command, find included, not a per-command
  extract (Q3).
- A subtype-4 GUID is written `{"$uuid": "…"}`, so a test's id can be found in a note (Q2).

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | `Insert (×3) → Funding`: request none, response `n=3` | **Confirmed** on 2.30.0 and 3.12.0, mongo 7 and 8 (`Insert (×3) → loans`, request `null`, response `n=3`) | RUN |
| 2 | `Update → Funding`: request none, not even the filter; response `n=1, nModified=0` | **Confirmed**, also for `UpdateMany`, `ReplaceOne` and a pipeline update. An upsert that inserts also reads `n=1, nModified=0`, with its `upserted[]` dropped (F4) | RUN |
| 3 | `FindAndModify ↔ LeadDetails`: none, none | **Confirmed** for `FindOneAndUpdate`, `FindOneAndReplace`, `FindOneAndDelete`, one matching nothing, and an upsert | RUN |
| 4 | `Find ← LoanDetails`: the filter, up to 10 documents | **Confirmed** | RUN |
| 5 | The bodies are not JSON on driver 2.x: `NumberDecimal(`, `ObjectId(`, `ISODate(` | **Confirmed** for the V2 package and for the tap on a 2.x host, with `CSUUID(` and `NumberLong(` as well. **On 3.x the documents are already relaxed extended JSON:** `new JsonWriterSettings()` defaults to `Shell` on 2.10.0, 2.19.0 and 2.30.0, and to `RelaxedExtendedJson` on 3.12.0 | RUN |
| 6 | "a write's count is a line before them" | **Partly.** No real reply has both a count and a cursor: a write's reply has counts, a read's has documents. So a write's whole body is the count line, a read's is its documents, and an empty read's is `0 documents`. None of the three is JSON | RUN, READ |
| 7 | The YAML view and `query values`/`--where` can't read them | **Confirmed** on 2.x. On 3.x the report reads a capped batch as JSON, because it strips the `... (N more documents not shown)` footnote first ([PlantUmlCreator.cs:1623](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L1623), [collapsible-notes-script.js:1011](../src/Kronikol/Reports/collapsible-notes-script.js#L1011)). The query tool does not ([BodyCache.cs:47](../src/Kronikol/Query/BodyCache.cs#L47) parses the whole body). So the two disagree on every capped body (F8) | READ |
| 8 | `values ... --path '$..CurrentAmount'` answered "538 bodies were not JSON" | **The message is right; the path could never match.** The parser skips every `.` ([PathEngine.cs:33](../src/Kronikol/Query/PathEngine.cs#L33)), so `$..CurrentAmount` is `$.CurrentAmount`, which an array body never has. The working path is `$[*].CurrentAmount`. With valid JSON, the issue's command would print nothing (F7) | READ |
| 9 | `Raw` records the whole command and reply, with `lsid` and `$db`, in the shell's syntax, and labels `Insert db.Collection` | **Confirmed** on 2.30.0. On 3.12.0 the same, in relaxed extended JSON | RUN |
| 10 | The request body at `Detailed` is `opInfo.FilterText`, and `ExtractFilter` reads only a top-level `filter` | **Confirmed** ([MongoDbTrackingSubscriber.cs:228](../src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs#L228), [MongoDbOperationClassifier.cs:148-155](../src/Kronikol.Extensions.MongoDB/MongoDbOperationClassifier.cs#L148)). The wire shapes are as the issue says: `updates[].q` and `u`, `deletes[].q`, and a findAndModify's and a distinct's `query`. An aggregate's `pipeline` is not shown at `Detailed` either | READ, RUN |
| 11 | `ExtractDetailed` reads `n`, `nModified` and `nUpserted`, then `cursor.firstBatch`; a findAndModify reply keeps its counts in `lastErrorObject` and its document in `value` | **Confirmed**, with three more. No reply carries `nUpserted` (an update's reply has `upserted[]`). `cursor.nextBatch` is never read. A distinct's `values` are never read | READ, RUN |
| 12 | `new JsonWriterSettings { Indent = true }` takes the driver's default, `Shell` on 2.x, and `ExtractFilter`'s `ToString()` does the same | **Confirmed, with a difference.** `ToString()` follows the host-wide `JsonWriterSettings.Defaults`, which a host can set; `new JsonWriterSettings()` does not. So the filter and the documents can come out in different dialects (F18) | RUN |
| 13 | Suggestion: write every body as relaxed extended JSON | **Agreed on the dialect, not on the means.** In the shared source, `OutputMode = JsonOutputMode.RelaxedExtendedJson` compiles to the integer 3 against 2.x, a value 3.x does not define. The tap on a 3.x host then writes `ObjectId(…)`, `ISODate(…)`, `NumberLong(5)` and `NaN` next to `{ "$numberDecimal" : … }` (§2.2) | RUN |
| 14 | Suggestion: move the counts out of the body | **Not recommended** (§4.2, Q4) | READ |

## 2. Where it stands today

### 2.1 The code (READ)

- **Three captures, one source.**
  - `Kronikol.Extensions.MongoDB` (MongoDB.Driver `3.*`, so 3.12.0 or later) subscribes to the driver's command events
    ([MongoDbTrackingSubscriber.cs](../src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs)).
  - `Kronikol.Extensions.MongoDB.V2` compiles the same ten files against MongoDB.Driver `2.*`
    ([Kronikol.Extensions.MongoDB.V2.csproj:22-31](../src/Kronikol.Extensions.MongoDB.V2/Kronikol.Extensions.MongoDB.V2.csproj#L22)).
  - `Kronikol.Extensions.TcpTap`'s `MongoTap` decodes the wire itself. It links the classifier, the verbosity enum and
    `MongoDbResponseSummary` against MongoDB.Bson `2.*`, so at run time it uses whichever Bson the host resolves
    ([Kronikol.Extensions.TcpTap.csproj:20-24, :38-42](../src/Kronikol.Extensions.TcpTap/Kronikol.Extensions.TcpTap.csproj#L20)).
    `InProcessParityTests` asserts that the tap's labels, URIs and notes equal the subscriber's (CHANGELOG 3.0.45).
- **The request note.**
  - At `Detailed` it is `LogFilterText ? opInfo.FilterText : null`
    ([MongoDbTrackingSubscriber.cs:228, :249](../src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs#L228)),
    where `FilterText` is `command["filter"].ToString()`.
  - At `Raw` it is `command?.ToString()` (:227, :248).
  - At `Summarised` it is null.

  The tap does the same
  ([MongoProtocolDecoder.cs:379-384](../src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs#L379)).
- **The reply note.** At `Detailed`, and at `Summarised` while `LogResponseContent` is on (the default), it is
  `MongoDbResponseSummary.ExtractDetailed`
  ([MongoDbResponseSummary.cs:44-83](../src/Kronikol.Extensions.MongoDB/MongoDbResponseSummary.cs#L44)):
  - `n=…, nModified=…, nUpserted=…`, from top-level Int32 fields;
  - then, while `LogResponseContent` is on, the documents of `cursor.firstBatch`, each written with
    `ToJson(new JsonWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n" })` (:60) and joined into
    an array;
  - `... (N more documents not shown)` after `MaxResponseDocuments` (default 10), or `0 documents` when the batch is
    empty.

  At `Raw` it is `e.Reply?.ToString()`.
- **The status.**
  - A `CommandSucceededEvent` is `200 OK` (:165).
  - A `CommandFailedEvent` is `500`, with `e.Failure?.Message` as its note (:201-207).
  - The tap calls a reply failed when its `ok` is 0 or it has a top-level `errmsg`, and then writes the `errmsg`
    (MongoProtocolDecoder.cs:345-358).
- **The deferred claim.** `AttributeByDocumentOwner` has been on by default since 3.19.0. A findAndModify that names no
  `_id` and resolves no scenario defers its request half until the reply names the document it took.
  `DeferredRequest` keeps `e.Command` (:114, :290), and `LogRequest` renders it inside `OnCommandSucceeded`
  (:139-148).
- What the report, the query tool and the history do with a body is in §2.3.

### 2.2 The measurements (RUN)

**The JSON dialects** (`a-json-modes/`):

| MongoDB.Bson | `new JsonWriterSettings().OutputMode` | `RelaxedExtendedJson`'s value |
|---|---|---|
| 2.10.0 | `Shell` | not defined (added in 2.11.0) |
| 2.19.0, 2.30.0 | `Shell` | 3 |
| 3.12.0 | `RelaxedExtendedJson` | 2 (`Shell` is 0) |

- **What parses as JSON.** Relaxed and canonical output parsed with `JsonDocument.Parse` for every value tested. Shell
  output failed for Decimal128, ObjectId, dates, Int64, NaN and infinities, regular expressions, timestamps, binaries,
  MinKey/MaxKey and undefined.
- **What every resolvable version supports.** The published nuspecs pin MongoDB.Driver >= 2.30.0 (V2), >= 3.12.0
  (main) and MongoDB.Bson >= 2.30.0 (TcpTap), so every version a package can resolve has relaxed mode.
- **Dates and binaries.** A date from 1970 to 9999 is written `{ "$date" : "2024-01-01T00:00:00Z" }`, and any other
  date `{ "$date" : { "$numberLong" : "…" } }`. A 300 KB binary is 409,698 characters.
- **GUIDs.**
  - On 2.x, relaxed mode rewrites a subtype-4 GUID as subtype `03` in C# legacy byte order. That is avoided only by
    setting the obsolete `JsonWriterSettings.GuidRepresentation` to `Unspecified`. That setter throws once the host
    uses the V3 GUID mode, and it does not exist on 3.x.
  - On 3.x a subtype-4 GUID is `{ "$binary" : { "base64" : "D4+tW9nLRp+hZXCGdyiVDg==", "subType" : "04" } }`, in which
    the GUID `0f8fad5b-d9cb-469f-a165-70867728950e` cannot be found by its text.
- **Running across versions** (`a-json-modes/cross-version/`): code compiled against 2.30.0, run on 3.12.0.
  - Its `JsonOutputMode.RelaxedExtendedJson` is the integer 3, which 3.x does not define. The output mixes
    `{ "$numberDecimal" : "1000.50" }` with `ObjectId("…")`, `ISODate("…")`, `NumberLong(5)`, `NaN`, `UUID("…")` and
    `/^loan-/i`.
  - Looking the mode up by name (`Enum.Parse`) works on both.
  - The 4.18.1 tap writes shell syntax on a host with Bson 2.30.0 and relaxed JSON on a host with 3.12.0.
- **The host-wide default.** `JsonWriterSettings.Defaults` is public and settable on every version. It changes what
  `ToString()` writes (so it changes `FilterText`), and it never changes `new JsonWriterSettings()`.

**The wire shapes** (`b-wire-shapes/`; the same field sets on both drivers and both servers):

| Command | Its payload | Its reply's payload |
|---|---|---|
| `insert` | `documents` | `n`, `writeErrors[]` |
| `update` | `updates[]` of `q`, `u` (an operator document, a replacement or a pipeline), with `multi` and `upsert` when set | `n`, `nModified`, `upserted[]` of `index` and `_id`, `writeErrors[]` |
| `delete` | `deletes[]` of `q` and `limit` (1 or 0) | `n`, `writeErrors[]` |
| `findAndModify` | `query`, `update` or `remove`, with `new` and `upsert` when set | `lastErrorObject` of `n`, `updatedExisting` and `upserted`; `value` (the document, or null) |
| `find` | `filter`, with `sort`, `limit`, `batchSize`… when set | `cursor.firstBatch` |
| `aggregate` | `pipeline`, `cursor` | `cursor.firstBatch` |
| `getMore` | `getMore` (the cursor id, an Int64), `collection`, `batchSize` | `cursor.nextBatch` |
| `distinct` | `key`, `query` | `values` |
| any, failed (`ok: 0`) | | `errmsg`, `code`, `codeName` |

- **Commands that are something else on the wire.**
  - `CountDocuments` is an `aggregate` with `$match` and `$group`.
  - A collection-level `BulkWrite` arrives as separate `insert`, `update` and `delete` commands.
  - 3.x's client-level `IMongoClient.BulkWrite` is a `bulkWrite` command against `admin`, with `ops` and `nsInfo`.
    Server 7.0 refuses it (code 40415). Server 8.0 answers with `cursor.firstBatch` and the counts `nInserted`,
    `nMatched`, `nModified`, `nUpserted`, `nDeleted` and `nErrors`.
- **The driver adds to a command:**
  - `$db` and `lsid`;
  - `txnNumber` on a retryable write;
  - `$clusterTime`, which is absent on a new client's first command;
  - `startTransaction` and `autocommit` inside a transaction;
  - `writeConcern`, `readConcern`, `$readPreference` and `apiVersion`, from the client's settings;
  - `ordered` on every insert, update and delete.
- **The server adds to a reply:** `ok`, `$clusterTime` and `operationTime`, plus `electionId` and `opTime` on a write.
- **What the events hold.** An insert's documents are in `CommandStartedEvent.Command`: the driver appends the OP_MSG
  kind-1 section as a `BsonArray` of `RawBsonDocument`. Every other command, and every reply, is a `RawBsonDocument`.
- **All of them are disposed when the handler returns.**
  - 116 of 116 documents held past their event threw `ObjectDisposedException`.
  - So did the started command when it was read again in the succeeded handler, 58 times out of 58.
  - So did `MongoCommandException.Result`.

  `DeepClone()` survives disposal. `Clone()` survives only for a `RawBsonDocument`, and `new BsonDocument(doc)` never
  does (`hold-copy-driver-*.txt`).

**What Kronikol writes today** at `Detailed`, on both drivers (`wire-shapes-driver-*.txt`):

| Operation | Request note | Reply note | Status |
|---|---|---|---|
| InsertOne, InsertMany of 3 | null | `n=1`, `n=3` | OK |
| UpdateOne, ReplaceOne, a pipeline update; UpdateMany of two | null | `n=1, nModified=1`; `n=2, nModified=2` | OK |
| An upsert that inserts | null | `n=1, nModified=0`, with no id | OK |
| DeleteOne, DeleteMany of two | null | `n=1`, `n=2` | OK |
| FindOneAndUpdate, …Replace, …Delete, one matching nothing, an upsert | null | null | OK |
| Find | the filter | the documents (shell syntax on 2.x); `0 documents` when none | OK |
| Aggregate (and CountDocuments) | null | the documents | OK |
| EstimatedDocumentCount (a `count` command) | null | `n=11` | OK |
| Distinct | null | null | OK |
| GetMore (with `TrackGetMore`) | null | null; labelled `GetMore` at `mongodb:///db`, with no collection | OK |
| commitTransaction, abortTransaction | null | null | OK |
| An insert hitting a unique index (the API throws `MongoWriteException`) | null | `n=0` | **OK** |
| An unordered InsertMany, one of three a duplicate (the API throws `MongoBulkWriteException`) | null | `n=2` | **OK** |
| An update making a duplicate key (the API throws `MongoWriteException`) | null | `n=0, nModified=0` | **OK** |
| A find with `$bogus` (server `ok: 0`, `errmsg` `unknown operator: $bogus`) | the filter | `find command failed` | 500 |
| A GridFS upload's routine `listIndexes` on a new collection | | `listIndexes command failed` | **500** (the driver treats `NamespaceNotFound` as no indexes) |

**Sizes.** A 600 KB GridFS upload is one `insert` of three chunk documents. Its `Raw` request note is 820,141
characters. At `Detailed` today that note is null.

**The `Raw` crash** (`deferred-claim-driver-*.txt`). The setup is a claim by filter (a `FindOneAndUpdate` on a status,
not an `_id`) that resolves no scenario, at `Raw`, with `AttributeByDocumentOwner` at its default.
- On 3.12.0 the application's call throws `ObjectDisposedException`.
- On 2.30.0 it throws `OperationCanceledException: The BinaryConnection operation has been cancelled.`, wrapping the
  same exception.
- The stack runs from `MongoDbTrackingSubscriber.OnCommandSucceeded` into `LogRequest`.
- The server has applied the update (`status = claimed`), and the call is not logged.
- At `Detailed`, and at `Raw` with `AttributeByDocumentOwner = false`, nothing throws.

### 2.3 How a body is read (READ)

- **The diagram note**, in every renderer. `FormatNoteContent` first tries `TryFormatAsJson`
  ([PlantUmlCreator.cs:1575](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L1575)). That needs the body to start with
  `{` or `[` and to parse whole; it then re-indents the body and drops null properties. Next it tries
  `TryFormatTruncatedJson` (:1610). That strips one trailing marker using the regex at :1623 (`…truncated (N chars
  total)`, the bulk-string marker, or `... (N more)` / `... (N more rows|documents not shown)`) and re-indents a JSON
  prefix. Anything else is drawn as plain text. **So the stored indentation never reaches a reader:** a compact body is
  drawn indented.
- **The YAML view** (BrowserJs; `NotePayloadFormat` has defaulted to YAML since 4.0.0).
  - `splitNoteCapFootnote` ([collapsible-notes-script.js:1011](../src/Kronikol/Reports/collapsible-notes-script.js#L1011))
    takes the footnote off.
  - `reconstructNoteJson` rebuilds the payload, and `JSON.parse` must accept all of it (:1074).
  - `n=1, nModified=0`, `0 documents` and a shell-syntax batch are not offered the toggle.
- **The query tool.**
  - `BodyCache` parses the whole body with `JsonDocument.Parse` (:47) and strips no footnote. A body that fails is
    counted (`N bodies were not JSON — counted, not evaluated`,
    [QueryCommand.Values.cs:186](../src/Kronikol/Query/QueryCommand.Values.cs#L186)), and it fails a `--where` clause.
  - `--path` knows `.name`, `[n]`, `[*]`, `['a.b']` and a final `.length()`. A `.` is skipped wherever it appears
    ([PathEngine.cs:33](../src/Kronikol/Query/PathEngine.cs#L33)), so `..` is read as `.`.
  - `--where` compares numerically only when the value is a JSON number
    ([WhereClause.cs:126](../src/Kronikol/Query/WhereClause.cs#L126)); anything else it compares as case-insensitive
    text. So `{ "$numberDecimal" : "1000" }` sorts above every number, because its first character, `{`, comes after
    every digit.
- **The history fingerprint.**
  - For a statement-shaped category, MongoDB among them
    ([DependencyCategories.cs:99-103](../src/Kronikol/Constants/DependencyCategories.cs#L99)), a call's target is its
    templated path plus the templated first line of its request body, cut to 120 characters
    ([InteractionShape.cs:219-236](../src/Kronikol/History/InteractionShape.cs#L219)).
  - The templater keeps a document's keys and makes its values `"{v}"`, its numbers `{n}` and its ids `{id}`, with or
    without spaces around the colon (:144-148).
  - The rule's version is 4 (:48). Across a change of version the analyzer reads no behaviour verdict and says why.
  - The warm-up rule uses the same head.
- **The capture-time hooks.** `RequestResponseLogger.Log` runs `CaptureRedaction` (a header denylist,
  `ContentPatterns` and a `Custom` hook), and then `MaxContentLength` (default none), on every entry, MongoDB's
  included ([RequestResponseLogger.cs:46-55](../src/Kronikol/Tracking/RequestResponseLogger.cs#L46)). The tap has its
  own `DocumentRedaction`, which runs before its sink.

### 2.4 The same gap elsewhere (READ)

- **Kronikol4J** (`kronikol4j-mongodb`, read at its `origin/main`) records what .NET records.
  - The request note is a top-level `filter` (`MongoDbOperationClassifier.java:103-107`), and the reply note is the
    counts and the `firstBatch` preview.
  - Its golden, `parity-harness/dotnet-capture/fixtures/mongo-interactions.txt`, was captured from the .NET 3.x
    package. It has `~null~` for every insert, update and delete request, and `MongoInteractionParityTest.java:120-123`
    holds every non-find line to it.
  - `docs/REMAINING_PARITY.md` says at :38-44 that the find dialects differ and at :665-669 that they match.
- **The Atlas Data API extension** is MongoDB over HTTP. At `Detailed` it records the whole request body, including an
  `insertOne`'s document and an `updateOne`'s filter and update, and the whole reply with its counts in it
  ([AtlasDataApiTrackingMessageHandler.cs:146-158](../src/Kronikol.Extensions.AtlasDataApi/AtlasDataApiTrackingMessageHandler.cs#L146)).
- **The other database extensions keep counts in the body.**
  - The SQL family writes `N rows affected`, and rows as a compact JSON array followed by `... (N more rows not shown)`
    ([SqlRowFormatter.cs:61-85](../src/Kronikol/Sql/SqlRowFormatter.cs#L61)).
  - BigQuery, Elasticsearch and Cosmos DB record the service's own JSON.
  - DynamoDB's bodies are type-wrapped JSON (`{"N": "1000"}`), with the same `--where` trap as an extended-JSON
    decimal.
  - None of them invents a count header.

## 3. Findings the issue does not state

- **F1. The obvious dialect fix breaks the tap** (RUN, §2.2). Today the tap's dialect follows the host's Bson (shell
  syntax on 2.30.0, relaxed JSON on 3.12.0). An explicit `OutputMode` in the shared source turns that into a mix of both
  on a 3.x host. Only a writer that does not go through the driver's `JsonOutputMode` writes the same bytes in the V2
  package, in the main package, and in the tap on either host.
- **F2. Write errors are drawn as successes, in both captures** (RUN).
  - A duplicate key is `ok: 1` with `writeErrors` (code 11000), so the driver raises `CommandSucceededEvent`. Kronikol
    draws `200` and `n=0` while the application throws `MongoWriteException`.
  - An unordered insert of three with one duplicate draws `200` and `n=2`.
  - An update that makes a duplicate key draws `200` and `n=0, nModified=0`.
  - The tap's test for a failure (`ok` is 0, or a top-level `errmsg`) misses all three.

  Nothing counts these calls as errors, so `query services`, `flow --errors-only` and the failures digest do not see
  them.
- **F3. A server error's words are lost in-process, and the two captures differ** (RUN).
  - `CommandFailedEvent.Failure.Message` is `find command failed`. The server's `errmsg`, `code` and `codeName` are only
    in `MongoCommandException.Result`.
  - The tap writes the `errmsg`.
  - `AMongoFailureIsA500InBothCaptures`
    ([InProcessParityTests.cs:311-338](../tests/Kronikol.Tests.TcpTap/InProcessParityTests.cs#L311)) passes because it
    builds the in-process failure as `new Exception("not authorized on app")`, a message no driver raises.
- **F4. An upsert's id is never shown** (RUN). The reply holds `upserted: [{index, _id}]`, but the code reads
  `nUpserted` ([MongoDbResponseSummary.cs:34](../src/Kronikol.Extensions.MongoDB/MongoDbResponseSummary.cs#L34)), a
  field no command reply carries. The 2.26.0 changelog promised it (CHANGELOG.md:9767).
- **F5. findAndModify, distinct and getMore replies are blank, and a getMore has no collection** (RUN). A getMore's
  first element is the cursor id, not the collection, so `ExtractCollectionName` (MongoDbOperationClassifier.cs:129-146)
  finds none. The label is `GetMore` and the URI `mongodb:///db`.
- **F6. At `Raw`, Kronikol fails the application's call** (RUN, §2.2). Two defects meet.
  - `DeferredRequest` keeps a document the driver disposes.
  - Nothing in the subscriber stops an exception reaching the driver, which runs the subscriber inside the operation.

  The second is general. A `CaptureRedaction.Custom` hook that throws, or any defect in rendering a note, fails the
  application's MongoDB call. The tap, by contrast, catches and writes `<reply could not be rendered: …>`
  (MongoProtocolDecoder.cs:400-404).
- **F7. The issue's own query could not have worked, and extended JSON adds a trap** (READ, §2.3).
  - `$..CurrentAmount` is read as `$.CurrentAmount`.
  - Once a decimal is `{"$numberDecimal": "1000"}`, `values` prints the object and `--stats` counts it as
    non-numeric.
  - `--where '$.CurrentAmount > 500'` is true for every decimal and `< 500` false for every one. That is a wrong
    answer, not a refusal.

  DynamoDB's bodies have the same trap today.
- **F8. The report and the query tool disagree on a capped body** (READ). The page strips `... (N more documents not
  shown)` and reads the rest as JSON; `BodyCache` parses the whole body and calls it not JSON. Every capped find, and
  every capped SQL rows body (`... (N more rows not shown)`), is unreadable to `values` and `--where`.
- **F9. The history fingerprint moves with the request body** (READ, §2.3).
  - Recording a body for every write moves the shape of every scenario that writes to MongoDB, once.
  - Indented, every MongoDB body's first line would be `{`. Finds with different filters would then share a shape,
    which today they do not.
- **F10. No test could see any of this** (READ).
  - No fixture holds a Decimal128, an ObjectId, a BsonDateTime or a GUID.
  - The one "date" fact builds `new BsonDocument("$date", "2026-…")`, which is a key holding a string
    ([MongoDbTrackingSubscriberTests.cs:846-868](../tests/Kronikol.Tests.MongoDB/MongoDbTrackingSubscriberTests.cs#L846)).
  - The parity theory gives every command a top-level `filter`
    ([InProcessParityTests.cs:199-241](../tests/Kronikol.Tests.TcpTap/InProcessParityTests.cs#L199)), a shape no write
    carries, and it compares no insert's request note.
  - The V2 test project links the same files, so it passes on 2.x for the same reasons.
- **F11. A GUID cannot be found in a note by its text** (RUN). On 3.x a subtype-4 GUID is written as base64. On 2.x,
  shell syntax wrote it as `CSUUID("…")`, which was readable but was not JSON. A reader searches a note for the test's
  own ids.
- **F12. Write payloads without a size rule would put a GridFS upload into a default note** (INFERRED from RUN). The
  600 KB upload's `Raw` note is 820,141 characters, almost all of it chunk data. A find on `fs.chunks` already writes
  the chunks' base64 at `Detailed` today.
- **F13. A routine GridFS call is drawn as a failure** (RUN). `GridFSBucket` lists a new collection's indexes. The
  server answers `NamespaceNotFound` (26), which the driver treats as no indexes, and Kronikol draws a `500`.
- **F14. `MaxContentLength` does not cap a phase variant** (READ). It replaces `log.Content` only
  ([RequestResponseLogger.cs:54](../src/Kronikol/Tracking/RequestResponseLogger.cs#L54)). When `SetupVerbosity` or
  `ActionVerbosity` is set, the diagram draws the variant's content instead, uncapped.
- **F15. A client-level `bulkWrite` is drawn against `admin`, with no collection** (RUN, 3.x on server 8.0).
- **F16. The wiki contradicts itself** (READ, `85abe19`).
  - Integration-MongoDB-Extension draws a find as `Find → users` (:1, :42), while its arrow table says `Find ← users`
    (:72).
  - Integration-TcpTap-Extension says `Summarised` has no notes (:176-184). The MongoDB page says metadata and a
    document preview (:43), which is what the code does.
- **F17. Kronikol4J has every gap here, and its golden pins the empty write bodies** (READ, §2.4).
- **F18. A host can change the filter's dialect** (RUN). `ToString()` follows `JsonWriterSettings.Defaults`, which is
  public and settable on every version. The documents, written with `new JsonWriterSettings()`, do not.

## 4. The design

### 4.1 One writer (built in R1, used by R2)

An internal static class, `MongoJson`, lives in `src/Kronikol.Extensions.MongoDB/` and is linked into the V2 package and
the tap the way the classifier is. It writes a `BsonValue` as MongoDB Extended JSON v2, relaxed form, with
`System.Text.Json`'s `Utf8JsonWriter`:

| BSON | Written as |
|---|---|
| String, Boolean, Null, Int32, Int64, document, array | plain JSON |
| Double | a number (`1.0` keeps its point); NaN and the infinities as `{"$numberDouble": "NaN"}` |
| Decimal128 | `{"$numberDecimal": "1000.50"}` |
| ObjectId | `{"$oid": "65a1b2c3d4e5f60718293a4b"}` |
| DateTime | `{"$date": "2024-01-01T00:00:00Z"}` from 1970 to 9999 (with milliseconds when they are not zero), otherwise `{"$date": {"$numberLong": "…"}}` |
| Binary, subtype 4, 16 bytes | `{"$uuid": "0f8fad5b-d9cb-469f-a165-70867728950e"}` (Q2) |
| Binary, any other | `{"$binary": {"base64": "…", "subType": "03"}}`; at `Detailed`, over 256 bytes, `"[binary: 261,120 bytes, subtype 00]"` (Q5) |
| Regular expression, Timestamp, JavaScript, Symbol, MinKey, MaxKey, Undefined, DBPointer | the spec's `$regularExpression`, `$timestamp`, `$code` (with `$scope`), `$symbol`, `$minKey`, `$maxKey`, `$undefined`, `$dbPointer` |

- **Compact, with no indentation.**
  - The page and the query tool indent whatever they show (§2.3).
  - The data file is smaller.
  - A request body's first line is the whole body, so the history head keeps what tells one statement from another
    (F9).
- **One implementation in three assemblies.** It reads only `BsonValue` members present on both 2.x and 3.x:
  `BsonType`, the `As…` accessors, `BsonBinaryData.Bytes` and `SubType`, `BsonDateTime.MillisecondsSinceEpoch`, and
  `Decimal128.ToString()`. It never reads `JsonWriterSettings`, so neither the host's Bson version nor
  `JsonWriterSettings.Defaults` reaches it (F1, F18). A test runs the tap's copy compiled against 2.x on 3.x (§5).
- **It never throws.** A value it cannot read is written `"[unreadable BSON: <type>]"`, and each handler catches
  whatever else is thrown (§4.2).

### 4.2 R1 (patch): what came back

1. **The reply note**, at `Detailed`, and at `Summarised` while `LogResponseContent` is on (as today).
   - A reply whose content is a cursor (`find`, `aggregate`, `getMore`, `listIndexes`, `listCollections`, a change
     stream) is its batch, `firstBatch` or `nextBatch`, as a JSON array. It holds up to `MaxResponseDocuments`
     entries, followed by today's footnote. An empty batch is `[]`, not `0 documents`.
   - Any other reply is the reply without the server's bookkeeping, as a JSON object (Appendix A names the fields). A
     cursor inside it, as in the client-level `bulkWrite`, is replaced by its batch. A reply with nothing left is
     null.

   Examples:
   - an update: `{"n":1,"nModified":0}`;
   - an upsert adds `"upserted":[{"index":0,"_id":{"$oid":"…"}}]`;
   - a findAndModify: `{"lastErrorObject":{"n":1,"updatedExisting":true},"value":{…}}`;
   - a distinct: `{"values":[…]}`;
   - a duplicate key: `{"n":0,"writeErrors":[{"index":0,"code":11000,"errmsg":"E11000 …","keyPattern":{…},"keyValue":{…}}]}`.

   With `LogResponseContent` off, the documents (the batch, `value`, `values`) are left out and the rest stays.
   Replies to `serverStatus`, `dbStats` and `collStats` stay out of the note at `Detailed`, since they are server state
   running to tens of kilobytes; `Raw` keeps them.
2. **Write errors are failures.** A reply with `writeErrors` or `writeConcernError` is drawn as a `500`, with the note
   above, in both captures (F2, Q7).
3. **A server error keeps its words.** When a `CommandFailedEvent`'s failure is a `MongoCommandException`, its `Result`
   goes through the rule above, read inside the handler:
   `{"errmsg":"unknown operator: $bogus","code":2,"codeName":"BadValue"}`. Any other failure keeps `Failure.Message`.
   The tap's `ok: 0` reply takes the same rule, so the two captures write the same note (F3).
4. **`Raw` replies** go through the writer whole: nothing stripped, and no binary placeholder. They are relaxed
   extended JSON on both drivers and in the tap.
5. **No exception reaches the driver** (F6).
   - Every handler catches. A note that cannot be rendered is written `<command could not be rendered: …>` or `<reply
     could not be rendered: …>`, as the tap writes its own.
   - The deferred claim renders its request note when the command starts, for every verbosity its log can need (its
     own and its phase variants'), and keeps those strings instead of `e.Command`.
6. **A getMore names its collection** (F5). The classifier reads `collection` for `getMore`, so the label is
   `GetMore ← loans` and the URI carries the collection.
7. **Unchanged in R1:**
   - every request note, so that request bodies and the history fingerprint move once, in R2;
   - every label;
   - `Summarised`'s rules.

   `MongoDbResponseSummary`'s public methods keep their signatures and their documented output; Kronikol stops calling
   them (Q14).

### 4.3 R2 (minor): what was sent

1. **The request note at `Detailed`** is the command without its first element (its name, which the label carries) and
   without the driver's own fields:
   - `$db`, `lsid`, `txnNumber`, `$clusterTime`;
   - `startTransaction`, `autocommit`;
   - `writeConcern`, `readConcern`, `$readPreference`;
   - `apiVersion`, `apiStrict`, `apiDeprecationErrors`;
   - `ordered` when it is `true`;
   - an `aggregate`'s `cursor`.

   It is written compact by `MongoJson`, with the large-binary placeholder:

| Operation | Request note |
|---|---|
| Find | `{"filter":{"loanNumber":"L-1001"}}`; with a sort and a limit, `{"filter":{…},"sort":{…},"limit":5,"batchSize":2}` |
| InsertMany | `{"documents":[{…},{…},{…}]}` |
| An upsert | `{"updates":[{"q":{"loanNumber":"L-2000"},"u":{"$set":{"status":"Draft"},"$setOnInsert":{"principal":{"$numberDecimal":"5000.00"}}},"upsert":true}]}` |
| DeleteOne | `{"deletes":[{"q":{"loanNumber":"L-1001"},"limit":1}]}` |
| FindOneAndUpdate | `{"query":{"status":"Ready"},"update":{"$set":{"status":"claimed"}},"new":true}` |
| Aggregate | `{"pipeline":[{"$match":{…}},{"$group":{…}}]}` |
| Distinct | `{"key":"status","query":{}}` |
| GetMore | `{"collection":"loans","batchSize":2}` |
| commitTransaction | null (nothing is left) |

2. **Caps.** `documents`, `updates`, `deletes` and `ops` keep their first `MaxRequestDocuments` entries (a new option
   on `MongoDbTrackingOptions` and on `MongoTapOptions`, default 10, Q5).
   - A note that was cut is followed by a footnote: `... (N more documents not shown)` for documents, and `... (N
     more)` for statements.
   - The page's patterns accept both footnotes, so the page reads the rest as JSON. After R3, so does the query tool.
3. **`LogFilterText = false` keeps its effect:** no request note at `Detailed` (Q9).
4. **`Raw` requests and `FilterText` go through the writer.** They are relaxed JSON on both drivers, with nothing
   stripped, and the `Raw` label keeps its `filter=…`, now in the same dialect.
5. **The history rule's version moves to 5** (Q6). The first run after the upgrade then reads no behaviour verdict, and
   says why, instead of calling every scenario that writes to MongoDB changed.
6. **The note is built when the command starts**, never later, because the command is disposed when the handler
   returns (§2.2).

### 4.4 R3 (patch): the core reads and caps what the extensions write

- `--path` refuses `..` with: "recursive descent (`..`) is not supported; `[*]` walks an array:
  `$[*].CurrentAmount`". Implementing `..` instead is part of Q8.
- `--where` with `<`, `>`, `<=` or `>=` and a numeric literal compares numbers only. A value that is not a number fails
  the clause and is counted ("N values were not numbers"); it is never ordered as text.
- `BodyCache` takes off the capture footnotes the page takes off before it parses. Today's pattern is
  `PlantUmlCreator.cs:1623`, and one shared definition will serve both readers. It counts such a body as capped, as it
  already counts `…truncated`.
- `MaxContentLength` caps a phase variant's content as it caps `Content` (F14).

### 4.5 Not in this plan

- **The query tool reading extended JSON (Q8).** With this, `{"$numberDecimal": …}`, `{"$numberLong": …}`,
  `{"$date": …}` and DynamoDB's `{"N": …}` would be read as numbers and dates by `--where`, `--stats` and `values`. It
  is a feature, and it is what lets `--where '$[*].CurrentAmount > 500'` answer the issue's question.
- **Smaller items, each with its own question:**
  - counts as headers or in the label (Q4);
  - a `LogRequestContent` option (Q9);
  - GridFS's `NamespaceNotFound` (Q10);
  - a client-level `bulkWrite`'s namespace (Q13).
- **Kronikol4J's port**, which happens in its own repository (Q11).

## 5. Tests, red first

Each new fact is run against 4.18.1 in a worktree, with the tests copied in and new helpers stubbed, and fails there on
its own assertion. MongoDB facts go in files that `Kronikol.Tests.MongoDB.V2` links, so each runs on both drivers.

**R1, unit** (`MongoDbTrackingSubscriberTests`, and a new `MongoJsonTests`):
- `Every_bson_type_is_written_as_relaxed_extended_json`: a theory over every `BsonType`. Each output is parsed with
  `JsonDocument.Parse`, compared with the spec's relaxed form, and read back by `BsonDocument.Parse` on the test's
  driver. New.
- `A_reply_batch_is_json_on_both_drivers`: Decimal128, ObjectId, BsonDateTime, a subtype-4 GUID and an Int64 in
  `firstBatch`. Red on V2 (shell syntax). On V3 it is red on the GUID (`$uuid`).
- `An_update_reply_is_its_counts_as_json`, `An_upsert_reply_names_its_id`, `A_findAndModify_reply_holds_its_document`,
  `A_distinct_reply_holds_its_values`, `A_getMore_reply_holds_its_next_batch`, `An_empty_batch_is_an_empty_array`:
  replies taken from §2.2's captures. Red: `n=1, nModified=0`; no id; null; null; null; `0 documents`.
- `A_capped_batch_parses_once_its_footnote_is_off`: green on V3 (a pin), red on V2.
- `A_write_error_reply_is_a_failure` and `A_write_concern_error_reply_is_a_failure`. Red: 200.
- `A_server_error_keeps_errmsg_code_and_codeName`: a `MongoCommandException` built with its `Result`. Red: `find
  command failed`.
- `A_getMore_is_drawn_against_its_collection`. Red: `GetMore`, `mongodb:///db`.
- `A_large_binary_is_a_placeholder_at_Detailed_and_whole_at_Raw`. Red.
- `No_handler_exception_reaches_the_driver`: a `CaptureRedaction.Custom` hook that throws, through all three handlers.
  Red: it throws.
- `A_deferred_claim_is_logged_at_Raw_after_its_command_is_disposed`: a `RawBsonDocument` command disposed after
  `OnCommandStarted`. Red: `ObjectDisposedException`.
- The facts that pin today's text are rewritten, not deleted:
  - `MongoDbTrackingSubscriberTests` (7 facts);
  - `MongoDecoderTests.cs:69, :94, :132, :142, :436`;
  - `NoteCollapseAfterFilterTests.cs:212`;
  - `ReportTestHelper.cs:3150`.

  The `$date` fact is replaced by one holding a real `BsonDateTime`.

**R1, real server** (`MongoServerFixture`: `KRONIKOL_TEST_MONGO`, otherwise Testcontainers; CI's Linux runners run
them):
- `A_raw_claim_by_filter_does_not_fail_the_applications_call`: §2.2's repro. Red: the application's call throws.
- `A_duplicate_key_insert_is_drawn_failed`. Red: 200.
- `Every_detailed_reply_note_parses_as_json`: every operation in §2.2, against the server. Red on V2.

**R1, the tap** (`Kronikol.Tests.TcpTap`):
- The parity theory is rebuilt from §2.2's real commands and replies, with the kind-1 sections on the wire. It compares
  request notes, reply notes and status, failures and write errors included, and drops the old theory's top-level
  `filter` on every command.
- `The_tap_writes_the_same_bytes_on_bson_2_and_3`: a small test project referencing the tap and MongoDB.Bson `3.*`
  checks the tap's notes against the golden the 2.x project checks. It is red today: shell syntax on one, relaxed
  JSON on the other. CI discovers test projects by itself; the project goes on no skip list (`ci-invisible-projects`).

**R1, report** (Playwright, a new `MongoNoteTests`):
- A MongoDB find reply and an update reply, in a generated report, each offer the YAML toggle and draw YAML. This is
  checked at paint level.
- A write error's arrow counts as an error in `query services`.
- Red: there is no toggle on the update's `n=` note, and 0 errors are counted.

**R2:**
- **Unit:**
  - one fact per row of §4.3's table, built from §2.2's captured commands;
  - `The_driver_fields_are_not_in_the_note`, covering each listed field, with `ordered: false` kept;
  - `Request_documents_are_capped_with_a_footnote_the_page_strips`;
  - `LogFilterText_off_leaves_no_request_note`;
  - `Two_finds_with_different_filters_have_different_shapes` and `The_shape_version_is_5` (`InteractionShapeTests`).

  Red on 4.18.1: null notes, and version 4.
- **The tap:** request-note parity for every row.
- **Playwright:** an insert's request note draws its documents as YAML.

**R3, unit** (`QueryCommandTests`, `RequestResponseLoggerTests`):
- `$..x` is refused with the message;
- `--where` with `>` and a number, run on an object, excludes it and counts it;
- a capped MongoDB batch and a capped SQL rows body are read as JSON and counted as capped;
- a variant's content is capped by `MaxContentLength`.

All red on 4.18.1.

**Mutations** (per release, in a `git stash create` snapshot worktree):
- `opTime` dropped from the bookkeeping list;
- `nextBatch` left unread;
- a write error left at 200;
- the deferred note rendered at the reply;
- the `$uuid` branch removed;
- `ordered: false` stripped;
- the footnote not taken off in `BodyCache`;
- `..` accepted.

## 6. Releases and records

### 6.1 Releases

| Release | Bump | Why |
|---|---|---|
| R1 | patch | Bug fixes: reply notes that are not JSON, write errors drawn as successes, lost upsert ids and error messages, blank findAndModify, distinct and getMore replies, getMore's collection, and the `Raw` crash. Report output changes: MongoDB reply notes, and a failure status for write errors |
| R2 | minor | New: write payloads at `Detailed`, `MaxRequestDocuments` on two options types, and `[Obsolete]` on the old summary methods (Q14) |
| R3 | patch | The query tool and the logger |

Release numbers are taken at release time ("taking X now"). Each release runs:
- the MongoDB suites on both drivers, including the real-server facts (`KRONIKOL_TEST_MONGO`, or Docker);
- the TcpTap suites;
- the core suite;
- the full Playwright suite when the page's input changes (R1, R2);
- `release.slnf` built in Release for every target.

Each also has a consumer check: BreakfastProvider's lane on local packages, on both its MongoDB.Driver 3.4.0 path and
its InMemoryEmulator path, with every MongoDB note parsing as JSON. The R2 check also measures the report's size before
and after. That size is ESTIMATED today, and the budget is the owner's to set (Q5).

### 6.2 Changelog draft (R1)

> **Patch: MongoDB reply notes are JSON, write errors are failures, and a `Raw` claim no longer fails the application's
> call (#168, R1).**
> - At `Detailed`, a MongoDB reply note is JSON on both drivers and in `MongoTap`.
>   - A cursor's batch (`firstBatch`, or a getMore's `nextBatch`) is an array, `[]` when it is empty.
>   - Any other reply is the reply without `ok`, `$clusterTime`, `operationTime`, `electionId` and `opTime`, so an
>     update is `{"n":1,"nModified":0}`, an upsert names its `_id`, and a findAndModify holds its `lastErrorObject` and
>     `value`.
>   - Before, a write's note was `n=…` text. An upsert's id, a findAndModify's document and a distinct's values were
>     not recorded, and on driver 2.x documents were written in the mongo shell's syntax (`NumberDecimal(…)`), which
>     the YAML view and `kronikol query` could not read.
>   - Values are MongoDB relaxed extended JSON, written by Kronikol, so the V2 package, the main package and the tap
>     write the same bytes on either Bson. A GUID is written `{"$uuid":"…"}`, and a binary over 256 bytes is a
>     placeholder at `Detailed`.
> - A reply carrying `writeErrors` or `writeConcernError` is drawn as a failure (500). A duplicate key was drawn
>   `OK` with `n=0`.
> - A failed command records the server's `errmsg`, `code` and `codeName`. Before, it recorded the driver's `find
>   command failed`.
> - At `Raw`, a findAndModify claimed by filter with no scenario resolved threw `ObjectDisposedException` into the
>   application's own call after the server had applied it. No exception from the subscriber reaches the driver any
>   more.
> - A getMore is drawn against its collection.
> - Behaviour change: MongoDB reply notes change form; request notes do not change until R2.

### 6.3 The wiki

Edits go in a worktree of the wiki of its own. Line numbers are on `origin/master` `85abe19`.
- **Integration-MongoDB-Extension:**
  - the verbosity table (:39-45): `Detailed`'s response becomes "the reply as JSON" at R1, and its request "the
    command, without the driver's fields" at R2;
  - `LogFilterText` (:214), `LogResponseContent` and `MaxResponseDocuments` (:217-218);
  - the response payload section (:268-285);
  - the find's arrow (:1, :42; F16);
  - `MaxRequestDocuments` (R2).
- **Integration-TcpTap-Extension:** :114, the options table (:165-174), and the verbosity table (:176-184; F16).
- **Content-Formatting:380:** the footnote, and the YAML toggle on MongoDB notes.
- **Ingesting-External-Captures:468:** content claims match a body's text, and a MongoDB body's text changes at R1 and
  at R2.
- **Cross-Run-History:443-450:** version 5 (R2).
- **For R3:** the query pages, and the debugging skill's `references/commands.md` (both copies).

### 6.4 Doc comments (they ship in the packages)

- `MongoDbTrackingVerbosity` (`Detailed`, `Raw`).
- `MongoDbTrackingOptions.LogFilterText`, `LogResponseContent` and `MaxResponseDocuments`.
- `MongoDbResponseSummary`, the class and its methods.
- `MongoTapOptions.LogFilterText`, `LogResponseContent` and `MaxResponseDocuments`.
- The remarks on `MongoTap` and `MongoProtocolDecoder` ("the filter as the request note").
- `MongoDbTrackingSubscriber`'s summary.
- `RequestResponseLogger.MaxContentLength` (R3).

### 6.5 Plans, roadmap and Kronikol4J

- This plan's row in `PLANS_STATUS.md` (this commit). The roadmap is in §7.
- Kronikol4J's ledger (`docs/REMAINING_PARITY.md`, the divergence ledger):
  - R1 and R2: "Worth porting". `kronikol4j-mongodb` has every gap (F17). Its golden will be recaptured from the .NET
    package, which turns `MongoInteractionParityTest` red until the change is ported or the difference pinned.
  - R3: "Nothing to port", since the query tool is .NET's.

## 7. Where it sits in the roadmap

The recommended place is stage 1 (defects in shipped features), as the next free row (1.28 when this was written), with
no decision number. The placement is the owner's. The other plans written on 2026-10-13 leave theirs the same way, so
no row is taken here.

## 8. Found on the way, not in this plan

- **F13, GridFS's `NamespaceNotFound`:** Q10.
- **F15, a client-level `bulkWrite` against `admin`:** the classifier could take the database and collection from
  `nsInfo` when it names one namespace (Q13).
- **The POCO `decimal` default.** 2.30.0 writes a C# `decimal` as the string `"1000.50"`; 3.12.0 writes a Decimal128.
  That is the application's data, not Kronikol's, but it decides what `--where` can compare, and it is worth a line on
  the wiki's MongoDB page.
- **Kronikol4J's two parity claims about the find dialect** (F17): for its own repository.

## 9. Questions, with recommendations

- **Q1. The dialect.** Relaxed extended JSON, as the issue asks, or plain JSON, where a decimal is a number, an
  ObjectId a string and a date an ISO string? **Recommended: relaxed extended JSON.**
  - It loses nothing.
  - It is what MongoDB's own tools and the Atlas Data API write, and what 3.x already writes for documents today.
  - It reads back into BSON.

  Plain JSON would make `--where` work on decimals with no change to the query tool, but it drops the type, and the
  tap and the Java port would have to agree on a Kronikol-invented mapping. Q8 closes the query gap instead.
- **Q2. GUIDs.**
  - Subtype 4 as `{"$uuid": "…"}` (readable, searchable, and accepted by extended-JSON parsers) or as the spec's
    `$binary`? **Recommended: `$uuid`.** Readers search notes for the test's ids (F11). The spec reserves `$uuid` for
    parsers, which is the one deviation.
  - Subtype 3 (C# legacy) as `$binary`, or decoded in C# legacy byte order? **Recommended: `$binary`.** The tap cannot
    know which legacy order a service used. The cost is that V2 users lose shell syntax's `CSUUID("…")`.
- **Q3. The request note's shape.** The command without its name and the driver's fields, for every command (§4.3), or
  per-command extracts that keep a find's bare filter? **Recommended: the command.**
  - It is one rule, and it covers commands Kronikol does not classify.
  - It keeps the flags a reader needs: `upsert`, `multi`, `limit`, `sort`.

  The cost is that a find's note moves from `{…filter…}` to `{"filter":{…}}`, so a `--where '$.status = …'` written
  against today's 3.x find notes needs `$.filter.status`. The changelog says so.
- **Q4. The counts.** Keep them in the body as JSON, or move them out as the issue suggests? **Recommended: keep
  them.**
  - For an update or a delete, the counts are the whole reply.
  - The label counts the request's documents, not the reply's `n` (an insert of three with one duplicate is
    `Insert (×3)` and `n` 2).
  - Every other database extension keeps its counts in the body.
  - As JSON they are queryable: `--where '$.nModified = 0'` finds the updates that changed nothing.
- **Q5. Size.**
  - A new `MaxRequestDocuments` (default 10), or reuse `MaxResponseDocuments`, whose documented meaning is the reply's
    batch? **Recommended: the new option.**
  - A binary over 256 bytes written as a placeholder at `Detailed`, on both sides? **Recommended: yes.** A GUID is 16
    bytes, and a GridFS chunk 255 KB (F12). `Raw` stays whole.
  - The owner sets a budget for R2's growth of BreakfastProvider's report, measured before R2 ships.
- **Q6. History.** Move `InteractionShape.Version` to 5 with R2, or let every scenario that writes to MongoDB read
  "behaviour changed" once? **Recommended: version 5.** It is the mechanism the rule's own doc names. The cost is one
  run without a behaviour verdict for every scenario, which the analyzer says.
- **Q7. Write errors.** `500` or a text status such as `Write error`? **Recommended: `500`.** It is what a failed
  command and the tap's `ok: 0` already draw, and every reader of `IsError` counts it. The body says what failed.
- **Q8. The query tool.**
  - R3, a patch, refuses `..` and stops ordering text against numbers. **Recommended: yes**, since both are silent
    wrong answers today.
  - R4, a minor (not in this plan): read extended JSON's and DynamoDB's wrapped scalars as numbers and dates, and
    implement `..` as recursive descent. **Recommended as the next plan.** It is what answers the issue's question
    (`values --path '$[*].CurrentAmount' --stats`), and `$..x` is what people, and models, type.
- **Q9. `LogFilterText`.** Keep it, with its wider meaning ("the request note at `Detailed`"), or add
  `LogRequestContent` and obsolete it? **Recommended: keep it.** Turning it off still leaves no request note, which is
  the safe reading for anyone who turned it off to keep filters out of reports.
- **Q10. GridFS's `listIndexes` on a new collection** (F13). Draw it as the driver treats it, or as the wire says?
  **Recommended: as the wire says** (500), as a handled HTTP 404 is drawn. After R1 its note reads `NamespaceNotFound`,
  which explains it.
- **Q11. Kronikol4J.** Port R1 and R2 there? **Recommended: yes**, as ledger entries marked "Worth porting", with the
  golden recaptured.
- **Q12. Three releases, or R1 and R2 as one minor?** **Recommended: three.** Each side's note changes once. R1, the
  fixes, can ship as a patch without waiting for R2's design questions. R3 belongs to another subsystem.
- **Q13. A client-level `bulkWrite`'s namespace** taken from `nsInfo` when it names one (F15)? **Recommended: yes, in
  R2**, since it is small and touches only 3.x on server 8.0.
- **Q14. `MongoDbResponseSummary`'s public methods.** Leave them unchanged in R1, mark them `[Obsolete]` in R2, and
  remove them at v5 (an Appendix C row)? **Recommended: yes.**
- **Q15. Roadmap.** Stage 1, as the next free row, with no decision number? **Recommended: yes** (§7).

## 10. Assumption ledger

| Assumption | Status |
|---|---|
| Driver 3.x after 3.12.0 still writes relaxed by default | RUN on 3.12.0; later 3.x ESTIMATED |
| The wire shapes and the reply's bookkeeping are the same on a sharded cluster | ESTIMATED. Measured on a replica set only; mongos adds fields (`$configServerState`, `$topologyTime`, `lastCommittedOpTime`) that Appendix A strips by name, unmeasured |
| InMemoryEmulator.MongoDB raises the same command shapes | ESTIMATED. BreakfastProvider's acceptance checks it |
| The page and the query tool indent a compact body | READ (§2.3) |
| The `BsonValue` members the writer reads are binary-compatible from 2.30 to 3.x | INFERRED. R1's cross-version project checks it |
| `$uuid` is read back by `BsonDocument.Parse` on both drivers | ESTIMATED. R1's writer fact checks it |
| The report grows by less than the owner's budget after R2 | ESTIMATED. Measured on BreakfastProvider before R2 |
| The driver disposes event documents on later 3.x versions too | RUN on 2.30.0 and 3.12.0 |
| The history templater keeps a compact body's keys and drops its values | READ (InteractionShape.cs:144-148) |

## 11. Log

- **2026-10-13.** Drafted from #168 at 4.18.1, by kronikol-cf.
  - The issue's claims were checked against a real server on both drivers (§1, §2.2).
  - The dialects were measured on four Bson versions, plus the cross-version case.
  - The report, the query tool and the history were read for how they use a body (§2.3).
  - The `Raw` crash (F6) and the write errors drawn as successes (F2) were found while measuring.
  - No source changes. The harness is `MONGODB_DETAILED_PAYLOADS_PLAN.harness/`; its podman containers were removed
    and its image deleted.

## Appendix A. Edit sites at `34145c44`

**R1:**
- `src/Kronikol.Extensions.MongoDB/MongoJson.cs` (new, internal): the writer.
- `src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs` (new, internal): the reply rule. The bookkeeping it strips:
  - measured: `ok`, `$clusterTime`, `operationTime`, `electionId`, `opTime`;
  - by name, for mongos: `$configServerState`, `$topologyTime`, `lastCommittedOpTime`, `$gleStats`;
  - in R2, the request rule.
- Both new files are linked into `Kronikol.Extensions.MongoDB.V2.csproj` (:22-31) and
  `Kronikol.Extensions.TcpTap.csproj` (:38-42), under the `KRONIKOL_TCPTAP_SHARED` namespace switch.
- `MongoDbTrackingSubscriber.cs`:
  - :77-131, :133-185 and :187-218: catch in every handler;
  - :110-116 and :290: the deferred request keeps strings, not `e.Command`;
  - :139-148: the deferred request is rendered at start;
  - :152-157 and :171-178: the reply rule;
  - :187-218: `MongoCommandException.Result`, and the status for write errors;
  - :259-260: the reply helper.
- `MongoDbOperationClassifier.cs:129-146`: getMore's `collection`.
- `src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs`:
  - :341-366 (`RecordPair`): the failure rule, with write errors, and the note rule;
  - :386-405 (`ResponseContent`) and :407-417 (`Stringify`): `Raw` through the writer.
- Tests:
  - `tests/Kronikol.Tests.MongoDB/*`, linked into V2;
  - `tests/Kronikol.Tests.TcpTap/InProcessParityTests.cs` and `MongoDecoderTests.cs`;
  - a new Bson-3 tap test project;
  - `tests/Kronikol.Tests.EndToEnd/MongoNoteTests.cs`.

**R2:**
- `MongoDbTrackingSubscriber.cs:221-257` (`LogRequest`): the request rule, built at `OnCommandStarted`.
- `MongoDbOperationClassifier.cs:148-155`: `FilterText` through the writer.
- `MongoDbTrackingOptions.cs`: `MaxRequestDocuments`, plus the doc comments at :86-88 and :110-122.
- `MongoTapOptions.cs`: `MaxRequestDocuments`, and `CopyTo`.
- `MongoProtocolDecoder.cs:379-384` (`RequestContent`).
- `MongoDbResponseSummary.cs`: `[Obsolete]` (Q14).
- `src/Kronikol/History/InteractionShape.cs:36-48`: version 5, with its line in the doc comment.

**R3:**
- `src/Kronikol/Query/PathEngine.cs:33`.
- `WhereClause.cs:126-152`.
- `BodyCache.cs:37-55`.
- `QueryCommand.Values.cs:135-141, :185-190`.
- The footnote pattern, shared with `PlantUmlCreator.cs:1623`.
- `src/Kronikol/Tracking/RequestResponseLogger.cs:54-55`.
