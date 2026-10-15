# MongoDB at Detailed: what a command sent and what came back, as JSON (#168)

**Date:** 2026-10-13 · **Repo version:** 4.18.1 (`main` at `34145c44`; line numbers are for that commit, and the
issue's, at `98c4806d`, point at the same lines) · **Status:** **Green-lit in full by the owner on 2026-10-13**, every §9 question as recommended. **R1 executed as 4.19.6, R2 as 4.20.0, R3 as 4.20.1 and the driver floor (Q18) as 4.20.2, all published; audited as 4.20.5** (§11). Drafted from #168 (filed by the
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
issue names is right as far as it goes. The underlying problem is wider, in six ways that change the fix.

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
6. **Other readers of a body change with it.**
   - The drawn note drops a null property, so a filter `{ "deletedAt" : null }` is drawn as `{}`, which matches every
     document (RUN, F22).
   - `Failures.md` quotes a MongoDB call's note in place of the call (READ, F23).
   - Users' redaction patterns match the stored text, so a change of spacing would stop them silently (READ, F20).

   Each needs a rule of its own, in R1 or R2.

**The plan** is three releases, split by side so each body changes once:
- **R1 (patch): what came back.** A Kronikol-owned writer: relaxed extended JSON on one line, in the driver's
  spacing, so users' redaction patterns keep matching, and the same in the V2 package and the tap. The reply note is
  the cursor's batch as a JSON array, or the reply without the server's bookkeeping as a JSON object: counts,
  `upserted`, `lastErrorObject` and `value`, `values`, `writeErrors`. R1 also:
  - draws write errors and write-concern errors as failures;
  - keeps a server error's `errmsg`, `code` and `codeName`;
  - draws a getMore against its collection;
  - stops any exception reaching the driver, and renders, when the command starts, every note the deferred path needs;
  - sets a failed call's `Error`, so `Failures.md` says why it failed;
  - keeps a null in a statement's drawn note, where today `{ "deletedAt" : null }` is drawn as `{}`.
- **R2 (minor): what was sent.** At `Detailed` the request note is the command without its name and without the
  driver's own fields. An insert shows its documents, an update its `q` and `u`, a findAndModify its `query` and
  `update`, a find its filter, sort and limit. The documents are capped by a new `MaxRequestDocuments`, a large binary
  is written as a placeholder, and the history fingerprint's version moves. `Failures.md` lists a MongoDB call by its
  label, so it does not quote the documents a write sent.
- **R3 (patch): the query tool reads what the report reads.** `..` is refused with a pointer to `[*]`, an object is
  never ordered against a number, the capture footnote is taken off before a body is parsed, `diff --body` compares
  integers exactly, and the content cap also caps phase variants.

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
| 6 | "a write's count is a line before them" | **Partly.** Only one reply has both. 3.x's client-level `bulkWrite` on server 8.0 is drawn `nModified=1` followed by its per-operation results. Every other write's reply has counts and no cursor, and a read's has a cursor and no counts. So a write's whole body is the count line, a read's is its documents, and an empty read's is `0 documents`. None of these is JSON | RUN, READ |
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
    `InProcessParityTests`, added with the tap in 3.0.45, asserts that the tap's labels, URIs, request notes and reply
    notes equal the subscriber's (InProcessParityTests.cs:238, :309).
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
- **What is never recorded.**
  - Both captures skip, by default, the handshake, the authentication and the session chatter: `hello`, `saslStart`,
    `ping`, `killCursors`, `endSessions` and others (`IgnoredCommands`, MongoDbTrackingOptions.cs:53-58;
    `ExcludedCommands`, MongoTapOptions.cs:31-36).
  - The tap always skips the authentication family, whatever its option holds, `createUser` and `updateUser` included
    (MongoProtocolDecoder.cs:34-38).
  - The driver's own events carry a security-sensitive command with its contents removed.

  So recording a whole command at `Detailed` (R2) does not record a credential. The one edge: a user who takes `hello`
  out of the tap's set records its `speculativeAuthenticate`, a SCRAM first message (a user name and a nonce, no
  password; INFERRED from the drivers' authentication spec, not measured). A getMore is recorded only with
  `TrackGetMore` on, which is off by default in both captures (MongoDbTrackingOptions.cs:64, MongoTapOptions.cs:39).
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
- **A sharded cluster adds others** (`c-sharded/`: one shard on 8.0.32, both drivers, through mongos and on the
  shard's member directly).
  - Through mongos, a write's reply has no `electionId` or `opTime`.
  - Every reply inside a transaction carries `recoveryToken`: `{ "recoveryShardId" : "sh0" }` after a write, `{ }`
    in a transaction that only reads. The driver copies it onto `commitTransaction` and `abortTransaction`.
  - 3.12.0's client-level `bulkWrite` reply adds `retriedStmtIds`.
  - A count's reply adds `shards` (`{ "sh0" : 11 }`) and a createIndexes's adds `raw`. These are per-shard results.
  - On the shard's member, a reply to a command carrying `txnNumber` adds `readOnly`, and one inside a transaction
    adds `$replData`.
  - `$configServerState`, `$topologyTime`, `lastCommittedOpTime`, `$gleStats` and `$configTime` appear in no reply.
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
  - The templater keeps a document's keys and makes its values `"{v}"` (:144-148), its numbers `{n}` and its ids
    `{id}` (:157). It finds a key with or without spaces around the colon (`IsKey`, :160-165).
  - The rule's version is 4 (:48). Across a change of version the analyzer reads no behaviour verdict and says why.
  - A call's line in the fingerprint is its caller, service, label, templated URI and status code, plus that head
    (:16-19, :201-226). The reply body is never read. So a change of label, URI or status moves a scenario's
    fingerprint as surely as a change of head.
- **The warm-up rule** groups a test's own calls by service, label and the same target, head included
  (WarmUpCalls.cs:88, :245-251). After R2, MongoDB calls with different heads are different groups. That gives fewer
  first-call marks, and different ones, and so moves the time each mark leaves, which `diff`'s Slower section reads
  (ReportIndex.cs:144).
- **`diff --body`** pairs calls on service, label and path, with no body in the key (CallPairing.cs:122-157). It
  refuses a call whose old note is null before pairing, "carries no body", with exit 2 (QueryCommand.Diff.cs:168-183).
  - So a write's request cannot be compared with a report from before R2.
  - A findAndModify's, a distinct's or a getMore's reply cannot be compared with one from before R1.
  - `diff` without `--body` compares no status.
- **Ingest.**
  - A content claim matches an ordinal substring of a record's URI or body (IngestAttribution.cs:374-376). R2 makes
    a write's documents claimable, and `$uuid` makes a GUID claim match.
  - `--merge-duplicates` pairs a wire record with its span twin, and a record with content and no span id counts as
    wire (InteractionMerger.cs:165-185). So R1 and R2 let more MongoTap records merge, and the wire's status (a write
    error's `500`) wins.
- **Raw text.** These match the stored text as written:
  - `kronikol query grep` (QueryCommand.Search.cs:116-141);
  - `--where` on an object (WhereClause.cs:128-131);
  - an OTLP export's body attribute, when `IncludeBodies` is on (OtlpSpanMapper.cs:233-238), cut at 8,192
    characters.

  Every page surface starts from the re-indented note: the drawn note, the YAML view, the copy menu and the search
  index. So the stored spacing never reaches them (`e-readers/`). The search box reads an unquoted `$oid` as a
  status filter (advanced-search.js:9-11); quoted, it is found.
- **`Failures.md`** quotes a statement's note in place of the call, and prints a call's `Error` (F23, F24).
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
    holds every non-find line to it. The test reads a copy of the golden under
    `kronikol4j-mongodb/src/test/resources/parity/`, runs against a live `mongo:7`, and skips without Docker.
  - `docs/REMAINING_PARITY.md` says at :38-44 that the find dialects differ and at :665-669 that they match.
  - It has no wire tap. Its MongoDB capture is a command listener (`KronikolMongoCommandListener`) only, so the
    tap's half of this plan has nothing to port.
- **The Atlas Data API extension** is MongoDB over HTTP. At `Detailed` it records the whole request body, including an
  `insertOne`'s document and an `updateOne`'s filter and update, and the whole reply with its counts in it
  ([AtlasDataApiTrackingMessageHandler.cs:146-158](../src/Kronikol.Extensions.AtlasDataApi/AtlasDataApiTrackingMessageHandler.cs#L146)).
- **The other database extensions keep counts in the body.**
  - The SQL family writes `N rows affected` for a statement that returns no rows (Dapper's `TrackingDbCommand.cs:276`,
    EF Core's `SqlTrackingInterceptor.cs:125`, `SqlDiagnosticTracker.cs:125`). It writes rows as a compact JSON array,
    followed by `... (N more rows not shown)` past the cap, or `N rows` when none were kept
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
  field no command reply carries. The 2.26.0 changelog promised an `nUpserted` count (CHANGELOG.md:9767), which
  has never appeared.
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
  - No fact checks a note holding a Decimal128, an ObjectId, a BsonDateTime or a GUID.
    `MongoDbOperationClassifierTests.cs:369` builds an ObjectId, but checks only the document id it yields.
  - The one "date" fact builds `new BsonDocument("$date", "2026-…")`, which is a key holding a string
    ([MongoDbTrackingSubscriberTests.cs:848-869](../tests/Kronikol.Tests.MongoDB/MongoDbTrackingSubscriberTests.cs#L848)).
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
- **F15. A client-level `bulkWrite` is drawn against `admin`, with no collection** (RUN, 3.x on server 8.0). Its `Raw`
  label is `BulkWrite admin.`, with a trailing dot where the collection would be. Its `Detailed` reply note is
  `nModified=1` and the batch, without `nInserted`, `nMatched` or `nDeleted`
  (`wire-shapes-driver-3.12.0-mongo8.txt`).
- **F16. The wiki contradicts itself** (READ, `85abe19`).
  - Integration-MongoDB-Extension draws a find as `Find → users` (:1, :42), while its arrow table says `Find ← users`
    (:72).
  - Integration-TcpTap-Extension says `Summarised` has no notes (:180). The MongoDB page says metadata and a
    document preview (:43), which is what the code does (MongoProtocolDecoder.cs:395-397).
  - The MongoDB page's arrow table lists `GetMore ← users` (:72), a label the code cannot draw today. With no
    collection the classifier draws plain `GetMore` (MongoDbOperationClassifier.cs:77-78). R1 makes it true.
  - Integration-TcpTap-Extension writes the footnote with `…` (:173). The code writes `...`
    (MongoDbResponseSummary.cs:72).
- **F17. Kronikol4J has every gap here, and its golden pins the empty write bodies** (READ, §2.4).
- **F18. A host can change the filter's dialect** (RUN). `ToString()` follows `JsonWriterSettings.Defaults`, which is
  public and settable on every version. The documents, written with `new JsonWriterSettings()`, do not.
- **F19. `System.Text.Json`'s writer, the obvious tool for the new writer, gets two rules wrong with its defaults**
  (RUN, `d-writer/`, the same on Bson 2.30.0 and 3.12.0).
  - `Utf8JsonWriter.WriteNumberValue` writes the double 1.0 as `1`, 123456789.0 as `123456789` and -0.0 as `-0`. Each
    reads back as an Int32, and the spec's relaxed form requires a fraction or an exponent (its corpus writes
    `1.0` and `-0.0`).
  - The default encoder writes `é` as `é`, `<` as `<`, and `"` as `"`. A reader, a search and a
    redaction pattern looking for the text would miss it. The driver writes `é`, `<` and `\"`.
- **F20. Users' redaction hooks read a note's text** (READ).
  - `CaptureRedaction.ContentPatterns` is a list of regular expressions run over the content
    (`CaptureRedaction.cs:62`, :138-142), and `Custom` receives the whole entry (:68).
  - The tap's `DocumentRedaction` is a `Func<string, string>` run over each note's text
    (`MongoTapOptions.cs:50-51`, `MongoProtocolDecoder.cs:419-430`), which writes `[REDACTION FAILED]` when it throws.

  A change to how a value is written is a change to what those hooks see. A hook that stops matching hides nothing
  and says nothing. What the wiki shows (READ, `e5c5a5b`):
  - No example targets a MongoDB note.
  - The capture-time examples (`Bearer …`, `Password=…`; Capture-Time-Redaction.md:43-44) match either spacing.
  - The tap's one example, in a test, replaces a value (`MongoDecoderTests.cs:359`).
  - The examples that match a JSON key are render-time processors, which run on the page's re-indented JSON
    (`"email": "`; Content-Formatting.md:577, :602-606). They reach a note only once it parses as JSON. So after R1
    and R2 they start to apply to MongoDB writes, which they could not before.

  So the hooks at risk are a user's own capture-time patterns, written against today's stored text
  (`"email" : "…"`).
- **F21. The main package's driver floor rises with each release** (RUN, nuget.org). `MongoDB.Driver` `3.*` in the
  project file is packed as the newest 3.x of the day:
  - 3.8.1 at Kronikol 3.0.0;
  - 3.9.0 at 3.0.28, 3.11.0 at 3.0.46, 3.11.1 at 3.0.61 and 3.11.2 at 3.0.84;
  - 3.12.0 from 3.22.0 to 4.18.2.

  No changelog line says so. BreakfastProvider's service pins MongoDB.Driver 3.4.0. Its test project references the
  extension and the service, and restored 3.12.0 (its local `project.assets.json`, from a restore at Kronikol 3.29.0,
  which has the same floor). So its tests run the service on a driver it does not ship with.
- **F22. The diagram drops a JSON note's null properties, which changes a MongoDB statement's meaning** (READ, and
  RUN on a verbatim copy of the formatter, `e-readers/`).
  - `WriteElementWithoutNulls` (PlantUmlCreator.cs:1817-1842) skips every property whose value is null.
  - So `{ "filter" : { "deletedAt" : null } }`, which matches the documents where `deletedAt` is null or missing, is
    drawn `"filter": {}`, which matches every document.
  - `{ "$set" : { "closedAt" : null } }` is drawn without `closedAt`.
  - A findAndModify that matched nothing loses `"value" : null`.

  Today this already redraws a 3.x find's filter, and the Atlas Data API's bodies. R1 adds the findAndModify replies
  and R2 every write. The YAML view and the copy menu start from the drawn note, so they lose the null too.
- **F23. `Failures.md` quotes a MongoDB call's note in place of the call** (READ).
  - For a statement-shaped category its Call column is the first 120 characters of the request note
    (FailuresDigestGenerator.cs:410-413, :428-431).
  - Today a failing step's find is listed as its bare filter (`{ "status" : "Ready" }`), with no operation and no
    collection, and every other MongoDB call as its label and path.
  - After R2 an insert would be listed as the first 120 characters of the documents it sent. That is the payload the
    category list's own comment says the digest must not quote: "bodies are addresses rather than content"
    (DependencyCategories.cs:80-92).
- **F24. A failed MongoDB call never says why in `Failures.md`** (READ). The digest prints the reason of a call whose
  `Error` is set (FailuresDigestGenerator.cs:741-745; `RequestResponseLog.Error`, RequestResponseLog.cs:68). Neither
  capture sets it, so a failed command, and after R1 a write error, is listed with its status alone.
- **F25. `diff --body` calls two different large integers equal** (READ). It compares JSON numbers as doubles
  (`ScalarEquals`, QueryCommand.Diff.cs:513-518), so Int64 values past 2^53 that differ, such as
  9007199254740993 and 9007199254740992, compare equal. Relaxed extended JSON writes every Int64 as a plain number.

## 4. The design

### 4.1 One writer (built in R1, used by R2)

An internal static class, `MongoJson`, lives in `src/Kronikol.Extensions.MongoDB/` and is linked into the V2 package and
the tap the way the classifier is. It writes a `BsonValue` as MongoDB Extended JSON v2, relaxed form, on one line, with
the driver's spacing (`{ "status" : "Ready", "n" : 1 }`, Q16). It is a small writer of its own. `System.Text.Json`'s
`Utf8JsonWriter` would get two rules wrong with its defaults (F19).

| BSON | Written as |
|---|---|
| String, Boolean, Null, Int32, Int64, document, array | plain JSON |
| Double | a number with a fraction or an exponent, as the spec's relaxed form requires: the shortest text that reads back as the same double, with `.0` added when it has neither (`1.0`, `0.1`, `-0.0`, `1E+300`). NaN and the infinities are `{ "$numberDouble" : "NaN" }` |
| Decimal128 | `{ "$numberDecimal" : "1000.50" }` |
| ObjectId | `{ "$oid" : "65a1b2c3d4e5f60718293a4b" }` |
| DateTime | `{ "$date" : "2024-01-01T00:00:00Z" }` from 1970 to 9999, with three digits of milliseconds when they are not zero; otherwise `{ "$date" : { "$numberLong" : "…" } }` |
| Binary, subtype 4, 16 bytes | `{ "$uuid" : "0f8fad5b-d9cb-469f-a165-70867728950e" }` (Q2) |
| Binary, any other | `{ "$binary" : { "base64" : "…", "subType" : "03" } }`; at `Detailed`, over 256 bytes, `"[binary: 261,120 bytes, subtype 00]"` (Q5) |
| Regular expression, Timestamp, JavaScript, JavaScript with scope, Symbol, MinKey, MaxKey, Undefined | the spec's `$regularExpression`, `$timestamp`, `$code`, `$code` with `$scope`, `$symbol`, `$minKey`, `$maxKey`, `$undefined` |

A DBPointer (BSON type 12, deprecated) never reaches the writer. MongoDB.Bson 2.30.0 and 3.12.0 throw
`FormatException` when they read one (RUN, `d-writer/`).

- **Strings are escaped as MongoDB.Bson 3.x escapes them.**
  - `é`, `<`, `&` and a non-breaking space are written as themselves.
  - `"` and `\` take a backslash.
  - A control character, U+2028 and an emoji's surrogate pair are written as `\u` escapes, in lowercase hex.

  `Utf8JsonWriter`'s default encoder would write `é` as `é` and `<` as `<` (F19).
- **The driver's spacing, on one line** (Q16).
  - It is the text users' redaction hooks see today. `CaptureRedaction.ContentPatterns` and `Custom` match a note's
    text, and so does the tap's `DocumentRedaction` (F20).
  - On 3.x, a field and its value come out byte for byte as the driver's relaxed `ToJson()` writes them today, so a
    hook that matches `"email" : "…"` keeps matching. The exceptions are a GUID, a large binary and some doubles: the
    driver writes 17 significant digits (`0.10000000000000001`, `1.0000000000000001E+300`). (This said the driver
    writes `-0.0` as `0.0`. It does not: Bson's value cache reads a reply's `-0.0` as `0.0` before either writer sees
    it; §11, R1.)
  - The page and the query tool re-indent whatever they show (§2.3), so a reader sees no difference.
  - On one line, a request body's first line is the whole body, so the history head keeps what tells one statement
    from another (F9).
  - It costs 19% more characters than compact JSON, and 6% more once gzipped. One insert of ten documents is 2,658
    characters against 2,225, and 332 gzipped bytes against 314 (RUN, `d-writer/`).
- **One implementation in three assemblies.** It reads only `BsonValue` members present on both 2.x and 3.x:
  `BsonType`, the `As…` accessors, `BsonBinaryData.Bytes` and `SubType`, `BsonDateTime.MillisecondsSinceEpoch`, and
  `Decimal128.ToString()`. It never reads `JsonWriterSettings`, so neither the host's Bson version nor
  `JsonWriterSettings.Defaults` reaches it (F1, F18). A test runs the tap's copy compiled against 2.x on 3.x (§5).
- **It never throws.** A value it cannot read is written `"[unreadable BSON: <type>]"`, and each handler catches
  whatever else is thrown (§4.2).

The examples in §4.2, §4.3 and §6.2 are written compact to fit the page. The writer spaces them as above.

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
7. **The diagram keeps a statement's nulls** (F22, Q19). `FormatNoteContent` keeps null properties in the note of a
   call whose category is statement-shaped, where the content is the call's identity (DependencyCategories.cs:80-92).
   Every other note keeps today's rule. It is a change to the report's output, so it is a Kronikol4J ledger line.
8. **A failed call says why** (F24).
   - A failed command's `Error` is the server's `errmsg`, or `Failure.Message` when there is no reply.
   - A write error's is its first `writeErrors[].errmsg`, or `writeConcernError.errmsg`.

   So `Failures.md` prints the reason under the call.
9. **Unchanged in R1:**
   - every request note, so that request bodies, and the history heads built from them, move once, in R2;
   - every label but a getMore's;
   - `Summarised`'s rules.

   R1 still moves some scenarios' history fingerprints, because the fingerprint holds each call's label, URI and status
   (§2.3). These are a scenario whose write error is now a `500`, one whose getMore now names its collection, and one
   whose `Raw` claim is now logged. Each reads one behaviour verdict after R1, which is a true one: the calls recorded
   changed (Q6).

   `MongoDbResponseSummary`'s public methods keep their signatures and their documented output; Kronikol stops calling
   them (Q14).

### 4.3 R2 (minor): what was sent

1. **The request note at `Detailed`** is the command without its first element and without the driver's own fields.
   The first element is the command's name, and it is dropped when its value is a string or a number (a collection,
   `1`, or a getMore's cursor id), which the label and URI carry. Any other first element is kept: `explain`'s holds
   the command it explains. The driver's fields are:
   - `$db`, `lsid`, `txnNumber`, `$clusterTime`;
   - `startTransaction`, `autocommit`, and `recoveryToken` (which the driver copies onto a commit or an abort through
     mongos);
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
7. **`Failures.md` lists a MongoDB call by its label and path** (F23, Q20), as it lists an HTTP call:
   `Insert (×3) → loans /app/loans`. The note is now a payload, and its address is beside it. The history and the
   warm-up rule keep reading the templated head, whose values are placeholders.

### 4.4 R3 (patch): the core reads and caps what the extensions write

- `--path` refuses `..` with: "recursive descent (`..`) is not supported; `[*]` walks an array:
  `$[*].CurrentAmount`". Implementing `..` instead is part of Q8.
- `--where` with `<`, `>`, `<=` or `>=` and a numeric literal compares numbers only. A value that is not a number fails
  the clause and is counted ("N values were not numbers"); it is never ordered as text.
- `BodyCache` takes off the capture footnotes the page takes off before it parses. Today's pattern is
  `PlantUmlCreator.cs:1623`, and one shared definition will serve both readers. It counts such a body as capped, as it
  already counts `…truncated`.
- `MaxContentLength` caps a phase variant's content as it caps `Content` (F14).
- `diff --body` compares two integers by their digits, so Int64 values past 2^53 that differ are reported (F25).
  A number with a fraction or an exponent is still compared as a double.

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
- `The_writer_matches_the_driver_on_bson_3`: a corpus of documents without GUIDs, large binaries or doubles is written
  byte for byte as MongoDB.Bson 3.x's relaxed `ToJson()` writes it (in the V3 project; the V2 project checks the same
  golden). New (F20, Q16).
- `Doubles_keep_a_fraction_and_read_back_as_doubles` (1.0, -0.0, 123456789.0, 0.1, 1E+300) and
  `Strings_are_escaped_as_the_driver_escapes_them` (`é`, `<`, `&`, a non-breaking space, U+2028, an emoji, DEL,
  U+0001). New (F19).
- `A_content_pattern_written_for_todays_text_still_redacts`: a `CaptureRedaction` pattern for `"email" : "…"` hides
  the address in a find's reply note. Green on 4.18.1 on driver 3.x, a pin: it fails if the writer goes compact (F20).
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
- `A_failed_command_sets_Error_to_the_servers_errmsg` and `A_write_error_sets_Error_to_its_errmsg`, and in
  `FailuresDigestGeneratorTests` the reason printed under the call. Red: `Error` is null (F24).
- `A_null_in_a_statement_note_is_drawn` (`PlantUmlCreatorTests`): a MongoDB note `{ "filter" : { "deletedAt" : null } }`
  and an Atlas Data API body keep their null when drawn, and an HTTP body still drops its own. Red: `"filter": {}`
  (F22).
- The facts that pin today's text are rewritten, not deleted:
  - at R1, nine `MongoDbTrackingSubscriberTests` facts (:551, :665, :696, :738, :756, :774, :787, :809, :822). They
    pin the `n=` line, the indented documents and `0 documents`.
  - at R1, `MongoDecoderTests.cs:69`, :94, :142 and :436 (the same texts) and :154 (`not authorized on app`, which
    becomes the reply's `errmsg` and `code`);
  - at R2, the request notes at `MongoDecoderTests.cs:42` and `ContainerEndToEndTests.cs:221`, a bare filter that
    becomes `{ "filter" : … }`.

  These stay green, all but the last only because the writer keeps the driver's spacing (Q16):
  - `MongoDecoderTests.cs:43` and `ContainerEndToEndTests.cs:220`, which look for `"status" : "active"` in a reply;
  - :328-329 (the `Raw` label `filter={ "a" : 1 }` and `"$db" : "app"`);
  - :132 (the footnote).
  `NoteCollapseAfterFilterTests.cs:212` pins a hand-written PlantUML fixture (`ReportTestHelper.cs:3153-3211`), not the
  extension's output, so nothing here moves it. The `$date` fact (:848-869) is replaced by one holding a real
  `BsonDateTime`.

**R1, real server.** These run on `MongoServerFixture` (`tests/Kronikol.Tests.MongoDB`, since 4.10.0), which uses
`KRONIKOL_TEST_MONGO`, otherwise Testcontainers `mongo:7.0`.
- The V2 project links it, and CI runs both projects on ubuntu-latest in its lane for the remaining test projects.
- It starts a standalone server, which refuses a transaction, so the fixture gains a replica set (Testcontainers'
  `WithReplicaSet`, or a replica-set URL in `KRONIKOL_TEST_MONGO`).

The facts:
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
  - `Two_finds_with_different_filters_have_different_shapes` and `The_shape_version_is_5` (`InteractionShapeTests`);
  - `JsonWriterSettings_Defaults_do_not_change_a_note`: with `Defaults` set to another mode, the filter and the
    documents are written as before. It sets a process-wide static, so it restores it in `finally` and runs in a
    collection that is not parallel (F18);
  - `An_explain_keeps_the_command_it_explains`;
  - `A_client_bulkWrite_is_drawn_against_its_namespace`, if Q13 is yes (server 8.0 only; F15);
  - `Failures_md_lists_a_MongoDB_call_by_its_label` (`FailuresDigestGeneratorTests`): an insert with its documents in
    its note is listed as `Insert (×3) → loans /app/loans`, and a find with its filter as
    `Find ← loans /app/loans`. Red on 4.18.1 for the find, which is listed as its bare filter (F23).

  Red on 4.18.1: null notes, and version 4.
- **The tap:** request-note parity for every row.
- **Playwright:** an insert's request note draws its documents as YAML.

**R3, unit** (`QueryCommandTests`, `RequestResponseLoggerTests`):
- `$..x` is refused with the message;
- `--where` with `>` and a number, run on an object, excludes it and counts it;
- a capped MongoDB batch and a capped SQL rows body are read as JSON and counted as capped;
- a variant's content is capped by `MaxContentLength`;
- `diff --body` reports 9007199254740993 against 9007199254740992 as changed (F25).

All red on 4.18.1.

**Mutations** (per release, in a `git stash create` snapshot worktree):
- `opTime` dropped from the bookkeeping list, or `recoveryToken` (a fact on a reply captured through mongos);
- `nextBatch` left unread;
- a write error left at 200;
- the deferred note rendered at the reply;
- the `$uuid` branch removed;
- the writer made compact, or `.0` not added to an integral double;
- `ordered: false` stripped;
- the footnote not taken off in `BodyCache`;
- `..` accepted;
- a null dropped again from a statement's note;
- `Error` left unset on a write error;
- `Failures.md` quoting a MongoDB note again;
- integers compared as doubles in `diff --body`.

**Each finding, its fix and the fact that fails without it:**

| Finding | Fixed by | Fact |
|---|---|---|
| F1, the dialect across Bson majors | §4.1 | `The_tap_writes_the_same_bytes_on_bson_2_and_3` |
| F2, write errors drawn OK | R1 item 2 | `A_write_error_reply_is_a_failure`, `A_write_concern_error_reply_is_a_failure`, `A_duplicate_key_insert_is_drawn_failed` |
| F3, a server error's words | R1 item 3 | `A_server_error_keeps_errmsg_code_and_codeName`; the rebuilt parity theory |
| F4, an upsert's id | R1 item 1 | `An_upsert_reply_names_its_id` |
| F5, blank replies and getMore's collection | R1 items 1 and 6 | `A_findAndModify_reply_holds_its_document`, `A_distinct_reply_holds_its_values`, `A_getMore_reply_holds_its_next_batch`, `A_getMore_is_drawn_against_its_collection` |
| F6, the `Raw` crash | R1 item 5 | `A_deferred_claim_is_logged_at_Raw_after_its_command_is_disposed`, `No_handler_exception_reaches_the_driver`, `A_raw_claim_by_filter_does_not_fail_the_applications_call` |
| F7, `..` and text ordering | R3 | `$..x` refused; `--where` on an object excluded and counted |
| F8, the footnote | R3 | a capped batch and a capped rows body read as JSON |
| F9, the history head | §4.1 (one line), R2 item 5 | `Two_finds_with_different_filters_have_different_shapes`, `The_shape_version_is_5` |
| F10, tests that could not see | R1 and R2 | the facts in this section; the `$date` fact replaced |
| F11, GUIDs | §4.1 (`$uuid`) | `A_reply_batch_is_json_on_both_drivers` |
| F12, GridFS's size | §4.1 (placeholder), R2 item 2 | `A_large_binary_is_a_placeholder_at_Detailed_and_whole_at_Raw`, `Request_documents_are_capped_with_a_footnote_the_page_strips` |
| F13, `NamespaceNotFound` drawn as a failure | none, by design (Q10) | none; after R1 its note names `NamespaceNotFound` |
| F14, variants uncapped | R3 | a variant's content is capped |
| F15, a client `bulkWrite` against `admin` | R2, if Q13 is yes | `A_client_bulkWrite_is_drawn_against_its_namespace` |
| F16, the wiki | §6.3 | none (wiki) |
| F17, Kronikol4J | §6.5 | its `MongoInteractionParityTest`, on the recaptured golden |
| F18, the host's defaults | R2 item 4 | `JsonWriterSettings_Defaults_do_not_change_a_note` |
| F19, `Utf8JsonWriter`'s defaults | §4.1 | `Doubles_keep_a_fraction_and_read_back_as_doubles`, `Strings_are_escaped_as_the_driver_escapes_them` |
| F20, hooks that read the text | §4.1 (spacing), Q16 | `A_content_pattern_written_for_todays_text_still_redacts`, `The_writer_matches_the_driver_on_bson_3` |
| F21, the driver floor | Q18, its own patch | not in this plan |
| F22, nulls dropped | R1 item 7 | `A_null_in_a_statement_note_is_drawn` |
| F23, `Failures.md` quoting a note | R2 item 7 | `Failures_md_lists_a_MongoDB_call_by_its_label` |
| F24, `Error` unset | R1 item 8 | `A_failed_command_sets_Error_to_the_servers_errmsg`, `A_write_error_sets_Error_to_its_errmsg` |
| F25, large integers in `diff --body` | R3 | `diff --body` reports two integers that differ past 2^53 |

## 6. Releases and records

### 6.1 Releases

| Release | Bump | Why |
|---|---|---|
| R1 | patch | Bug fixes: reply notes that are not JSON, write errors drawn as successes, lost upsert ids and error messages, blank findAndModify, distinct and getMore replies, getMore's collection, the `Raw` crash, failures that never say why, and nulls dropped from a statement's drawn note. Report output changes: MongoDB reply notes, a failure status for write errors, and nulls kept in statement notes |
| R2 | minor (Q17) | New: write payloads at `Detailed`, `MaxRequestDocuments` on two options types, and `[Obsolete]` on the old summary methods (Q14). `Failures.md` lists a MongoDB call by its label |
| R3 | patch | The query tool (with `diff --body`'s integers) and the logger |

Release numbers are taken at release time ("taking X now"). Each release runs:
- the MongoDB suites on both drivers, including the real-server facts (`KRONIKOL_TEST_MONGO`, or Docker);
- the TcpTap suites;
- the core suite;
- the full Playwright suite when the page's input changes (R1, R2);
- `release.slnf` built in Release for every target.

Each also has a consumer check: BreakfastProvider's lane on local packages, on both of its MongoDB paths, with every
MongoDB note parsing as JSON. Its in-memory lane runs `InMemoryEmulator.MongoDB`, which raises the driver's events
through `ClusterConfigurator`. Its Docker mode runs a real server (`UseTrackedMongoClient`). Both run on the driver
the test project restores, 3.12.0, not the 3.4.0 its service pins (F21). The R2 check also measures the report's
size before and after. That size is ESTIMATED today, and the budget is the owner's to set (Q5).

### 6.2 Changelog draft (R1)

> **Patch: MongoDB reply notes are JSON, write errors are failures, and a `Raw` claim no longer fails the application's
> call (#168, R1).**
> - At `Detailed`, a MongoDB reply note is JSON on both drivers and in `MongoTap`.
>   - A cursor's batch (`firstBatch`, or a getMore's `nextBatch`) is an array, `[]` when it is empty.
>   - Any other reply is the reply without the server's bookkeeping, so an update is `{ "n" : 1, "nModified" : 0 }`,
>     an upsert names its `_id`, and a findAndModify holds its `lastErrorObject` and `value`. The bookkeeping is `ok`,
>     `$clusterTime`, `operationTime`, `electionId` and `opTime`, and a sharded cluster's `recoveryToken`,
>     `retriedStmtIds`, `readOnly` and `$replData`.
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
> - A failed MongoDB call carries the server's message as its error, so `Failures.md` says why it failed.
> - The diagram keeps a null in the note of a database call. `{ "deletedAt" : null }`, which matches the documents
>   with no `deletedAt`, was drawn as `{}`, which matches every document. An HTTP body still drops its nulls.
> - Behaviour change: MongoDB reply notes change form; request notes do not change until R2. A scenario whose write
>   error is now drawn as a failure, or whose getMore now names its collection, reads one "behaviour changed" verdict
>   in the history, since the calls it recorded changed. A redaction pattern written for today's text
>   (`"email" : "…"`) keeps matching on driver 3.x. On driver 2.x the text changes from the shell's syntax to
>   extended JSON, so check such patterns.

### 6.3 The wiki

Edits go in a worktree of the wiki of its own. Line numbers are on `origin/master` `85abe19`.
- **Integration-MongoDB-Extension:**
  - the verbosity table (:39-45): `Detailed`'s response becomes "the reply as JSON" at R1, and its request "the
    command, without the driver's fields" at R2;
  - `LogFilterText` (:214), `LogResponseContent` and `MaxResponseDocuments` (:217-218);
  - the response payload section (:268-285);
  - the find's arrow (:1, :42; F16);
  - `MaxRequestDocuments` (R2).
- **Integration-TcpTap-Extension:** :114, the options table (:165-174, with the footnote's `...` at :173), and the
  verbosity table (:176-184; F16).
- **Content-Formatting:380:** the footnote, and the YAML toggle on MongoDB notes (and the full stop missing after
  "MongoDB" there).
- **Ingesting-External-Captures:468:** content claims match a body's text, and a MongoDB body's text changes at R1 and
  at R2.
- **Cross-Run-History:443-450:** a line that R2 moves the rule's version, so the first run after it reads no behaviour
  verdict. The page does not state the version's number, and need not.
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
  - R1 and R2: "Worth porting". `kronikol4j-mongodb` has every gap (F17). Its golden, both copies, will be recaptured
    from the .NET package, which turns `MongoInteractionParityTest` red until the change is ported or the difference
    pinned. The ledger is the section headed ".NET-side features shipped after this audit" (:1877), one line per
    release.
  - R1 also changes the drawn note: a statement keeps its nulls (F22). Kronikol4J renders its own notes, so the ledger
    line says so.
  - R3: "Nothing to port", since the query tool is .NET's.

## 7. Where it sits in the roadmap

The recommended place is stage 1 (defects in shipped features), as the next free row (1.28 when this was written), with
no decision number. The placement is the owner's. The other plans written on 2026-10-13 leave theirs the same way, so
no row is taken here.

**Placed 2026-10-13** as row 1.29 (1.28 went to #170), with no decision number, when the owner green-lit the plan
(Q15).

## 8. Found on the way, not in this plan

- **F13, GridFS's `NamespaceNotFound`:** Q10.
- **F15, a client-level `bulkWrite` against `admin`:** the classifier could take the database and collection from
  `nsInfo` when it names one namespace (Q13).
- **The POCO `decimal` default.** 2.30.0 writes a C# `decimal` as the string `"1000.50"`; 3.12.0 writes a Decimal128.
  That is the application's data, not Kronikol's, but it decides what `--where` can compare, and it is worth a line on
  the wiki's MongoDB page.
- **Kronikol4J's two parity claims about the find dialect** (F17): for its own repository.
- **F21, the driver floor that rises with each release:** Q18, as its own patch. It is packaging, not notes, and it
  touches every consumer of the main package, so it does not ride in R1.
- **Outside MongoDB, found while reading the body's readers.** Each is for a patch of its own:
  - **`merge --history` ignores a suite's `HistoryShapeTemplates`.** It fingerprints with
    `new HistoryBuildOptions()` (MergeCommand.cs:190-191), so a merged run is templated without the suite's rules
    (READ; what a suite's verdicts then read depends on where its ledger lines come from, not measured).
  - **The search index loses payload text that looks like markup.** The normalizer decodes `~<` before it strips
    `<b>`, `<color …>` and the like, so a note holding the text `José <b>` is indexed as `josé `. It folds case for
    ASCII only, while the search box lowercases every letter, so `ÉLODIE` is indexed as `Élodie` and a search for it
    finds nothing. RUN on `tools/search-bench/normalize.js` (`e-readers/results/normalize-tags.txt`); the box is
    READ (report-search-function.js:35).
  - **The debugging skill's `query.py` prints `é` as `é`.** Its `pretty` calls `json.dumps` without
    `ensure_ascii=False` (READ, query.py:424-428).
  - **The OTLP export's `BodyAttributeCapBytes` counts characters, not bytes, and can cut a surrogate pair**
    (READ, OtlpSpanMapper.cs:371-378).
  - **`kronikol query failures` may list no calls for a failing nested step.** It matches calls to a step by its exact
    path, and calls are attributed to top-level steps only (READ, QueryCommand.Narrative.cs:122, :190;
    ReportGenerator.cs:4602-4610; not reproduced).

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
    `$binary`? **Recommended: `$uuid`.** Readers search notes for the test's ids (F11). Since 2020 the Extended
    JSON spec has said parsers MUST read `$uuid` as a Binary of subtype 4. Its conversion table, which generators
    follow, still writes every binary as `$binary`, so writing `$uuid` is the writer's one departure from the spec.
    MongoDB.Bson reads it back on 2.30.0 and 3.12.0 (RUN, `d-writer/`).
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
  - No other database extension invents a count header, and none puts a reply's count in its label. The SQL family,
    the Atlas Data API, BigQuery and Elasticsearch keep their counts in the body. Redis's label carries a cache
    result (`(Hit)`, `(Miss)`; RedisOperationClassifier.cs:99-100). Bigtable's, like MongoDB's, counts the request:
    `MutateRows (×3)` (BigtableOperationClassifier.cs:36).
  - As JSON they are queryable: `--where '$.nModified = 0'` finds the updates that changed nothing.
- **Q5. Size.**
  - A new `MaxRequestDocuments` (default 10), or reuse `MaxResponseDocuments`, whose documented meaning is the reply's
    batch? **Recommended: the new option.**
  - A binary over 256 bytes written as a placeholder at `Detailed`, on both sides? **Recommended: yes.** A GUID is 16
    bytes, and a GridFS chunk 255 KB (F12). `Raw` stays whole.
  - The owner sets a budget for R2's growth of BreakfastProvider's report, measured before R2 ships.
- **Q6. History.** Move `InteractionShape.Version` to 5 with R2, or let every scenario that calls MongoDB read
  "behaviour changed" once? **Recommended: version 5.**
  - The doc says to move the version "when a rule changes" (InteractionShape.cs:36-47). R2 changes the rule's input
    for one category, not the rule, but the doc's reason holds: "rather than calling every scenario changed once".
  - The cost is the one each of the four earlier moves had. Every scenario of every suite, MongoDB or not, reads no
    behaviour verdict for one run, and the analyzer says why (HistoryAnalyzer.cs:483-486).
  - `merge --history` fingerprints with the tool's version (MergeCommand.cs:190-191). A merged run whose tool and
    packages are on opposite sides of the move reads no behaviour verdict until both move. That is true of every
    move, and R2's changelog says so.

  R1 moves no version. The fingerprints it moves are calls that now fail or now name their collection (§4.2 item 9),
  and a verdict saying so is true.
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
- **Q16. Spacing.** The driver's `{ "status" : "Ready" }` on one line, or compact `{"status":"Ready"}`?
  **Recommended: the driver's** (§4.1).
  - A user's redaction hook matches a note's text (F20). Compact JSON would silently stop a pattern written for
    `"email" : "…"` from matching, and the data it hid would reach the report.
  - The cost is 19% more characters, 6% gzipped.

  Compact JSON is what the drafted plan had. Either way, on driver 2.x the dialect changes from shell syntax to
  extended JSON, and the changelog tells a 2.x user to check their hooks.
- **Q17. R2's bump.** CLAUDE.md makes "changing a default so that existing code behaves differently without being
  touched" a major change. With today's defaults (`Detailed`, `LogFilterText` on), R2 records what a write sends: an
  insert's documents and an update's `u`. `LogFilterText` is documented as "filter BSON text", and `Detailed` as
  "classified labels with relevant context". So a report from untouched code holds data it did not hold before.
  - **Recommended: a minor, with the changelog's first line saying so.**
    - A find's reply at `Detailed` already records up to ten documents. That is the same data, once a test reads back
      what it wrote.
    - Every other database extension records what a call sends at its default.
    - It is #168's ask, and `LogFilterText = false` turns it off.
  - The alternative is to keep the documents (`documents`, `u`, `update`, `ops`) behind a new option, off until v5
    (D30), and record only the statements' filters (`q`, `query`) by default. That ships the issue's ask turned off.
  - The plan does not bump MAJOR without the owner's word. R1's new reply data (a findAndModify's document, a
    distinct's values) is a find's data for another command, and stays a patch.
- **Q18. The driver floor (found on the way, not this plan's).** `Kronikol.Extensions.MongoDB` references MongoDB.Driver
  `3.*`, so each release's floor is whatever 3.x was newest when it was packed (F21). Pin it, for example to the oldest
  3.x the suite passes on? **Recommended: yes, as its own patch**, with a fact that reads the packed nuspec.
- **Q19. Nulls in a drawn note** (F22). Keep null properties in the note of every statement-shaped category, or only
  MongoDB's? **Recommended: every statement-shaped category, in R1.**
  - The category list's own comment says the content of those calls is the call's identity, and a null changes what a
    query means.
  - The Atlas Data API's drawn bodies have the same defect today.
  - An HTTP body keeps today's rule, where a null field is noise.

  It changes the report's output: a golden that pins a dropped null moves, and Kronikol4J's ledger gets a line.
- **Q20. `Failures.md`'s Call column for MongoDB** (F23). After R2, list a MongoDB call by its label and path, or by
  its templated head? **Recommended: label and path**, as an HTTP call is listed. The label names the operation and
  the collection. A head would be 120 more characters of shape, the address is one step away, and a find loses only
  the bare filter it shows today.

## 10. Assumption ledger

| Assumption | Status |
|---|---|
| Driver 3.x after 3.12.0 still writes relaxed by default | RUN on 3.12.0, the newest MongoDB.Driver and MongoDB.Bson on nuget.org when checked (published 2026-09-17; checked 2026-10-13). A later one is ESTIMATED; the writer does not depend on it |
| The wire shapes and the reply's bookkeeping are the same on a sharded cluster | RUN on one shard, 8.0.32, both drivers (`c-sharded/`). The commands and payloads are the same; the bookkeeping differs, and Appendix A strips what was measured. Several shards and a mongos on 7.0 are ESTIMATED |
| InMemoryEmulator.MongoDB raises the same command shapes | The commands are the driver's own: BreakfastProvider subscribes through `ClusterConfigurator` (READ). Its replies, and whether its event documents are disposed, are ESTIMATED. BreakfastProvider's acceptance checks them |
| The page and the query tool indent a compact body | READ (§2.3) |
| The `BsonValue` members the writer reads are binary-compatible from 2.30 to 3.x | INFERRED. R1's cross-version project checks it |
| `$uuid` is read back by `BsonDocument.Parse` on both drivers | RUN on 2.30.0 and 3.12.0: a Binary of subtype 4 (`d-writer/`). R1's writer fact keeps it |
| The report grows by less than the owner's budget after R2 | ESTIMATED. Measured on BreakfastProvider before R2 |
| The driver disposes event documents on later 3.x versions too | RUN on 2.30.0 and 3.12.0 |
| The history templater keeps a one-line body's keys and drops its values, spaced or compact | READ (InteractionShape.cs:144-165) |

## 11. Log

- **2026-10-13.** Drafted from #168 at 4.18.1, by kronikol-cf.
  - The issue's claims were checked against a real server on both drivers (§1, §2.2).
  - The dialects were measured on four Bson versions, plus the cross-version case.
  - The report, the query tool and the history were read for how they use a body (§2.3).
  - The `Raw` crash (F6) and the write errors drawn as successes (F2) were found while measuring.
  - No source changes. The harness is `MONGODB_DETAILED_PAYLOADS_PLAN.harness/`; its podman containers were removed
    and its image deleted.
- **2026-10-13, review** (at the owner's ask to check the plan for accuracy and completeness, and to do any deep dive
  that would change it), by kronikol-cf, on `main` at `f03452fc`.
  - **Citations.** Every citation was checked. Seven were fixed: the SQL file that writes "rows affected", the
    `nUpserted` promise, the date fact's lines, the templater's lines, the 3.0.45 entry, the facts that pin today's
    text (nine, not seven, plus four tap pins the draft missed), and two fixtures that cannot turn red. The wiki's
    `origin/master` moved to `e5c5a5b`, and no cited line moved.
  - **Measured.**
    - A one-shard cluster (`c-sharded/`): the four bookkeeping names the draft stripped "for mongos" never appear,
      and `recoveryToken`, `retriedStmtIds`, `readOnly` and `$replData` do.
    - The writer's rules (`d-writer/`): `Utf8JsonWriter`'s defaults get doubles and escaping wrong (F19); `$uuid`
      reads back on both drivers; a DBPointer cannot be read.
    - The page's readers (`e-readers/`): nulls dropped (F22); both footnote patterns already accept `... (N more)`.
    - The package's driver floor (F21).
    - The spec's text on `$uuid`, doubles and dates.
  - **Read.** The fingerprint's fields, the warm-up rule, `diff --body`, `merge`, ingest's claims and merge, the
    search index, the OTLP export, `Failures.md` (F23, F24), `diff`'s integers (F25), and the hooks that read a note's
    text (F20).
  - **Changed.**
    - The writer: the driver's spacing, its own number and escape rules.
    - The strip lists.
    - R1: nulls and `Error`. R2: `Failures.md`, `explain`. R3: integers.
    - Q6's reasoning, and Q16 to Q20.
    - Corrections to §1 row 6, Q2, Q4, F15, F16 and the BreakfastProvider check.
    - A finding, fix and fact table.

- **2026-10-13, R1 executed as 4.19.6**, by kronikol-cf, green-lit in full by the owner the same day ("implement the
  plan in full", every §9 question as recommended), in a worktree of its own.
  - **Built as §4.2 says**, with three changes found while building it:
    - a client `bulkWrite` whose reply counts `nErrors` is a write failure too, and its `Error` is the first failed
      operation's `errmsg` (the reply is `ok: 1` with the failures in its cursor; F2's rule did not name it);
    - Bson stores a regular expression's options sorted, on 2.30 and 3.12 alike: its constructor sorts them and every
      read (wire bytes included) goes through it, so the writer writes them as stored (the mutation that dropped the
      writer's own sort could not change the output);
    - the tap's test helper (`TestSupport.ToBson`) wrote a subtype-4 GUID as legacy subtype 3 on Bson 2.x (the 2.x
      writer's V2 mode); it writes with `GuidRepresentation.Unspecified` there, and the tap's notes are pinned on both
      Bson versions by one golden (`Kronikol.Tests.TcpTap.Bson3`, a new test project).
  - **Corrected:** §4.1 said the driver writes `-0.0` as `0.0`. Its writer keeps the sign (`new BsonDouble(-0.0)` is
    written `-0.0`). It is Bson's value cache that turns `-0.0` into `0.0` (`BsonDouble.Create`, the implicit
    conversion `d-writer/` used, `BsonDocument.Parse` and `RawBsonDocument`), so a reply's `-0.0` reaches both writers
    as `0.0` and they agree. Probed on Bson 3.12.0.
  - **Proofs** (`r1/`): `red-4.19.0.txt`, on 4.19.0 with `MongoJson` stubbed by the driver's `ToJson()`: 54 facts fail
    on driver 3.x, 73 on 2.x, 17 in the tap, 1 on Bson 3, 4 in the core and 2 in Playwright, each on its own assertion.
    `mutations.txt`: 46 mutations, 43 caught on the first run, two caught once a fact was added for each (a reply
    carrying an `errmsg` and no `ok` is failed in the tap; a 64-bit `nErrors`), and the 46th equivalent (above).
  - **Checks:** After the version bump the core suite passes 7,467 with 2 skipped; the MongoDB suites pass 264 of 264 on
    driver 3.x and 264 of 264 on 2.x, their real-server facts included (a mongod 7.0 replica set); the TcpTap suite
    passes 398 with 4 skipped (its Docker-only container facts) and its new Bson 3 project 1 of 1; ClickHouse passes 120
    with 9 skipped (its real-server facts), SearchEngine 214 and the IKVM renderer 55; the full Playwright suite passes
    1,039 with 28 skipped when run alone; and release.slnf builds in Release for every target.
  - **Consumer:** BreakfastProvider's xUnit lane (a clone at its 4.9.1 commit, every Kronikol reference moved to local packages at
    4.19.6-bp168r1; its test project restores MongoDB.Driver 3.12.0) passes 212 of 212 in memory
    (`InMemoryEmulator.MongoDB`) and against a mongod 7.0 replica set (`UseTrackedMongoClient`). Its 16 MongoDB calls
    have 16 reply notes that parse as JSON on both paths, where the published 4.19.2 had 4 of 16 in memory; its 6
    request notes parse, and its 10 writes have none until R2. A script counted them and printed counts only. The
    lane's fixture adds `appsettings.componenttests.json` after the environment, so its real-server mode is set in
    that file: a first run with environment variables kept the file's `localhost:27017` and failed 11 scenarios on
    the driver's 30-second server selection.
- **2026-10-13, R1 published.** Release run 38443005513, CI 38443001989 and CodeQL 38443001902 passed for 4.19.6.
  CI's remaining-tests job first failed at restore, on Microsoft's half-published 10.0.13 servicing packages, and
  passed when rerun once nuget.org listed them. nuget.org listed all 62 ids at 19:18 UTC. The wiki has R1's edits
  (`e73a5ab`), Kronikol4J's ledger its two lines (`233baf7`), and #168 a comment naming the release.

- **2026-10-13, R2 executed as 4.20.0**, by kronikol-cf, in the same worktree as R1.
  - **Built as §4.3 says.** `MongoDbNotes.Request`, shared by the subscriber and the tap, writes the command without its
    name and the driver's fields; `MaxRequestDocuments` is on both options types; `Raw` and a label's `filter=…` go
    through `MongoJson`; a client bulkWrite takes its namespace from `nsInfo` when it names one (Q13); the shape
    version is 5 (Q6); `Failures.md` lists a MongoDB call by its label and path (Q20); `MongoDbResponseSummary`'s
    methods are obsolete (Q14), with an Appendix C row for their removal in 5.0.
  - **Found on the way:**
    - R1's fact that sets `JsonWriterSettings.Defaults` ran in a parallel class, beside captures that still read the
      driver's `ToString()`; `MongoJsonTests` now runs in the process-wide collection that runs alone, as §5 asked;
    - `MongoJson`'s remarks still said the driver writes `-0.0` as `0.0`, the claim R1's entry corrects; they no
      longer do;
    - `CopyToCarriesEveryProtocolOption` listed the tap's options by hand and missed `LogResponseContent` and
      `DocumentRedaction` as well as the new option (mutation 21); it now checks all three;
    - R1's Playwright update fact clicked the first note's format button, which R2 makes the request's.
  - **Proofs** (`r2/`): `red-4.19.6.txt`, on 4.19.6 with `MaxRequestDocuments` stubbed: 35 facts fail on driver 3.x,
    35 on 2.x, 1 in the tap, 1 in the core and 2 in Playwright, each on its own assertion; `mutations.txt`, 25 of 25
    caught (one after the CopyTo fact was extended).
  - **Checks:** After the version bump the core suite passes 7,519 with 2 skipped; the MongoDB suites pass 302 of 302 on
    driver 3.x and 302 of 302 on 2.x, their real-server facts included (a mongod 7.0 replica set); the TcpTap suite
    passes 398 with 4 skipped (its Docker-only container facts) and its Bson 3 project 1 of 1; ClickHouse passes 120
    with 9 skipped (its real-server facts), SearchEngine 214 and the IKVM renderer 55; the full Playwright suite passes
    1,040 with 28 skipped when run alone; and release.slnf builds in Release for every target.
  - **Consumer:** BreakfastProvider's xUnit lane (the same clone as R1's check, every Kronikol reference on local packages at
    4.20.0-bp168r2, MongoDB.Driver 3.12.0) passes 212 of 212 in memory and against the mongod 7.0 replica set. All 16 of
    its MongoDB request notes parse as JSON on both paths, where 4.19.6 recorded 6 (its finds' filters) and left its
    10 writes without one; its 16 reply notes still parse. A script counted them and printed counts only. The report
    the lane writes grows by 0.2%: 4,681,138 to 4,692,053 bytes in memory and 4,676,641 to 4,689,272 against the server
    (Q5 left the budget to the owner; this is the measurement it asked for).
- **2026-10-13, R2 published.** Release run 38457681824, CI 38457676182, CodeQL 38457676619 and CI Summary Preview
  38457676440 passed for 4.20.0, each on its first attempt. nuget.org listed all 62 ids at 20:15 UTC. The wiki has R2's
  edits (`1307e7c`), Kronikol4J's ledger its line (`1eb7001`), and #168 a comment naming the release.

- **2026-10-13, R3 executed as 4.20.1**, by kronikol-cf, in the same worktree as R1 and R2.
  - **Built as §4.4 says.** `--path` refuses `..` and names the `[*]` path; an ordering against a number compares
    numbers only and the footer counts what was not one (`WhereClause.NotNumbers`); one footnote definition
    (`CaptureFootnote`) serves the page's `TruncationMarkerRegex` and the query tool, whose `BodyCache` reads a capped
    body as its document and counts it as capped; `MaxContentLength` caps a phase variant's content (F14); `diff --body`
    compares two integers by their digits (F25).
  - **Found on the way:** the plan named `BodyCache`, but five other readers of a body parsed it themselves and called a
    capped batch text: `--keys`, `grep --values`, `grep --number`, the pretty-printer behind `--body`, `--lines` and
    `--out`, and `diff --body`, which fell back to a line diff that printed both bodies' lines and whose capped note
    only knew the `…truncated` marker. All of them now parse through `BodyCache.ParseJson`; `--body` prints the document
    indented with its footnote after it, and `diff --body`'s note names both footnotes when they differ. The variant
    fact capped a long setup variant beside a short action variant and never gave the action variant a long body, so
    leaving it whole survived (mutation 20); it now checks each variant on its own. The first `--where` fact said an
    address was absent that the listing never prints, so on 4.20.0 it failed only at its note; it now checks the count
    first, beside a second fact with the issue's own `{ "$numberDecimal" : … }`, and both fail on 4.20.0 at the count.
  - **Proofs** (`r3/`): `red-4.20.0.txt`, the facts copied onto 4.20.0 with no stubs: 12 fail, each on its own
    assertion, and the inequality guard passes; `mutations.txt`, 21 of 21 caught: 19 and 20 were rewritten after their
    first form did not compile, and 20 then survived until the variant fact gave each variant a long body on its own.
  - **Checks:** After the version bump the core suite passes 7,533 with 2 skipped; the MongoDB suites pass 302 of 302 on
    driver 3.x and 302 of 302 on 2.x, their real-server facts included (a mongod 7.0 replica set); the TcpTap suite
    passes 398 with 4 skipped (its Docker-only container facts) and its Bson 3 project 1 of 1; ClickHouse passes 120
    with 9 skipped (its real-server facts); the full Playwright suite passes 1,040 with 28 skipped when run alone;
    release.slnf builds in Release for every target, and restores with --force-evaluate.

- **2026-10-13, R3 published.** CI 38468447111, CodeQL 38468446852 and CI Summary Preview 38468446704 passed for
  4.20.1, and Release 38468450102 on its second attempt: nuget.org answered the first push with 500 after one of the 62
  packages (its status page had shown publishing degraded from 19:30 to 20:28 UTC), and the rerun, which skips a package
  already there, pushed the rest. nuget.org listed all 62 ids at 21:52 UTC. The wiki has R3's edits (`c0aea0c`),
  Kronikol4J's ledger its line (`17cb305`, nothing to port), and #168 a comment naming the release. The listing times
  in R1's and R2's entries were the watcher's clock, an hour ahead of UTC; they now read UTC.

- **2026-10-13, the driver floor (Q18) executed as 4.20.2**, by kronikol-cf, in a worktree of its own.
  - **Measured, not taken from the example.** Q18 suggested the oldest 3.x the suite passes on. It passes on every 3.x
    tried from 3.0.0, but a driver before 3.9.0 lets a project that takes its driver from Kronikol resolve Snappier
    1.0.0 (a high advisory) or SharpCompress 0.30.1 (a moderate one), so `Kronikol.Extensions.MongoDB` asks for 3.9.0,
    the oldest 3.x whose dependencies carry no advisory, and its stale `NoWarn NU1902` is gone.
  - **The V2 package as well**, the same defect: `2.*` packed 2.30.0, and a consumer pinning 2.28 or 2.29 met NU1605.
    The V2 suite builds from 2.21.0 (older drivers lack event constructors the tests use), but 2.x assemblies are
    strong-named only from 2.28.0: with the extension built on an unsigned 2.x the suite, on 2.30.0, does not compile
    (CS0012), and neither would a consumer's call into the extension. So V2 asks for 2.28.0.
  - **The suites keep the newest driver** through a direct reference of their own, so the floor is what the extension
    compiles against; `Kronikol.Tests.EndToEnd`, which takes the extension alone, runs on 3.9.0.
  - **Not done here, for the owner:** TcpTap's `MongoDB.Bson` `2.*` and about fifteen other packages float their client
    libraries the same way (F21's class); each needs its floor measured on its own suite and advisories.
  - **Proofs** (`q18/`): `floors.txt` (the suite on each driver tried, the advisories, what each 3.x driver asks for,
    the strong names, the red runs of `MongoDbPackageTests`) and `floor_probe.sh`.
  - **Checks:** After the version bump the core suite passes 7,533 with 2 skipped; the MongoDB suites pass 303 of 303 on
    driver 3.x and 303 of 303 on 2.x, their real-server facts included (a mongod 7.0 replica set); the TcpTap suite
    passes 398 with 4 skipped (its Docker-only container facts) and its Bson 3 project 1 of 1; the full Playwright suite
    passes 1,040 with 28 skipped when run alone, on driver 3.9.0; release.slnf builds in Release for every target, and
    restores with --force-evaluate.

- **2026-10-13, the driver floor published.** Release 38476808232, CI 38476808218, CodeQL 38476808295 and CI Summary
  Preview 38476808324 passed for 4.20.2 on their first attempt, and nuget.org listed all 62 ids at 22:37 UTC. The wiki
  has the edit (`60daf39`), Kronikol4J's ledger its line (`818b010`, nothing to port: its MongoDB module compiles
  against the driver and publishes no dependency on it), #168 a comment closing out the plan, and #97, the issue for
  every package's floating client reference, a comment naming the MongoDB floors, how they were chosen and the 21
  projects that still float. #168 stays open for Q8. The plan is complete.

- **2026-10-15, audited as 4.20.5**, by kronikol-77 (kronikol-cf until a restart), in a worktree of its own, at the
  owner's ask to make sure nothing was missed from the plan and that the implementation had no bugs.
  - **How.** Five read-only reviews: R1's code, R2's code, R3's code with every reader of a query body, every reader of
    a MongoDB note (the page, the history, Failures.md, ingest, OTLP, the fallback script), and a tick of §5, §6.3,
    §6.4, §6.5 and §7 against what shipped. Each finding was checked in the code before anything was done about it,
    and each fix was proved red on v4.20.2 first.
  - **Defects in what the releases shipped, fixed in 4.20.5:**
    - R1: the server's message, kept as the call's `Error` since 4.19.6, went round `CaptureRedaction`, the tap's
      `DocumentRedaction` and `CaptureReplies`, and a duplicate key's message quotes the key. Item 9's "the
      `Summarised` rules are unchanged" did not hold for a failed command with `LogResponseContent` off. A reply's
      arrays other than a cursor's batch were whole. A client bulkWrite lost its failed operations with
      `LogResponseContent` off, and drew a 500 with no error when they were past its first batch.
    - R2: the history's templater kept a document's booleans, signs and list lengths, so the documents R2 put in the
      head changed a scenario's shape on data alone, and §10's claim did not hold; the shape version moves to 6. An
      unclassified command lost its name in its note, a `Raw` label without a collection ended in a dot, Failures.md
      printed a `Raw` label whole, and a tap whose user took `hello` out of `ExcludedCommands` recorded the start of an
      authentication.
    - R3: eleven defects in the query tool (notes lost past the budget, under `--count` and under `--group-by`; a
      capped body read in part without a word; a changed footnote not counted; a quoted literal read as a number; the
      `..` suggestion; an empty key's path; the Redis marker; footnotes on text bodies; numbers compared as doubles;
      mixed line endings), and the fallback script read no capped body.
  - **Older defects on the same paths, fixed with them:** a write the server refused claimed its document; a lone
    surrogate failed the whole diagram; a negative `MaxContentLength` failed every call, and a cap could split a
    surrogate pair; Failures.jsonl and `query failures` did not say why a call failed (F24's readers); OTLP exported
    no status message for a 500; an Atlas Data API call was quoted in Failures.md (F23's sibling).
  - **What the records missed, done in 4.20.5:** six wiki pages (TcpTap's request note and failed reply,
    Search-Syntax's nulls, the footnotes on Content-Formatting and Querying-Reports, Ingesting-External-Captures' R2
    half, and §8's POCO `decimal` line on the MongoDB page), the doc comments of nine files (§6.4 named
    `MaxContentLength`), the 4.20.0 entry's Q3 and Q6 notes, two stale `NoWarn NU1902` suppressions (the V2 package's
    hid SharpCompress 0.30.1, which 4.20.2's entry did not name), the tap's golden (it held no request note, so the
    Bson 3 project never checked one), this plan's PLANS_STATUS row (R1 only), and the roadmap's rows 1.29 and 1.11,
    its plans index and Appendix C.
  - **Left for the owner, each a row in the roadmap's Appendix C:** Kronikol4J's golden (§6.5, Q11) was not
    recaptured; its ledger line says `MongoInteractionParityTest` turns red when it is, until R1 and R2 are ported, and
    its `REMAINING_PARITY.md` says both that the find dialects differ (:38-44) and that they match (:665-669). The tap
    and the subscriber still differ on a reply of `{ ok: 0 }` alone and on `{ ok: 1, errmsg }`. `SequenceCollapser`,
    which a suite turns on, folds calls whose payloads differ. A `Raw` label holds the filter unredacted, as it did
    before the plan. DynamoDB, Elasticsearch and Cosmos bodies are still quoted in Failures.md, where MongoDB's are not.
  - **Proofs** (`audit/`): `red-4.20.2.txt` (the audit's facts on v4.20.2 with no product change: 59 fail, each on
    its own assertion, and 16 guards pass) and `mutations.txt` (46 of 46 caught, by `mutations/mutate_audit.py`).
  - **Checks:** After the version bump the core suite passes 7,587 with 2 skipped; the MongoDB suites pass 317 of 317 on
    driver 3.x and 317 of 317 on 2.x, their real-server facts included (a mongod 7.0 replica set); the TcpTap suite
    passes 414 with 4 skipped (its Docker-only container facts) and its Bson 3 project 1 of 1; the Otlp suite passes
    148; the full Playwright suite passes 1,040 with 28 skipped when run alone; release.slnf builds in Release for every
    target, and restores with --force-evaluate.

- **2026-10-15, the audit published.** Release 38722321576, CI 38722318406, CodeQL 38722318418 and CI Summary
  Preview 38722318315 passed for 4.20.5 on their first attempt, and nuget.org listed all 62 ids at 11:11 UTC
  (`Kronikol.Extensions.EventBridge`, pushed at 10:51, was the last to list). The wiki has the edits (`e703abf`),
  Kronikol4J's ledger its line (`b57564f`, worth porting in part: a negative `setMaxContentLength` and the surrogate
  cut, the `admin.null` label, and, once 4.19.6's write errors are ported, a refused write's claim), and #168 a comment
  naming the release. #168 stays open for Q8.

## Appendix A. Edit sites at `34145c44`

**R1:**
- `src/Kronikol.Extensions.MongoDB/MongoJson.cs` (new, internal): the writer.
- `src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs` (new, internal): the reply rule. The bookkeeping it strips, every
  name measured (§2.2):
  - from a replica set: `ok`, `$clusterTime`, `operationTime`, `electionId`, `opTime`;
  - through mongos: `recoveryToken`, `retriedStmtIds`;
  - from a shard's member: `readOnly`, `$replData`.

  A count's `shards` and a createIndexes's `raw` are results and stay. The four names the first draft stripped "for
  mongos" (`$configServerState`, `$topologyTime`, `lastCommittedOpTime`, `$gleStats`) appear in no reply and are
  dropped from the list. In R2, the file also holds the request rule.
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
- `MongoDbTrackingSubscriber.cs:187-218` and `MongoProtocolDecoder.cs:341-366`: `Error` on a failed command and a
  write error (F24).
- `src/Kronikol/PlantUml/PlantUmlCreator.cs`: `FormatNoteContent` (:1468) is told the call's category at its two call
  sites (:348, :424), and `TryFormatAsJson` (:1575) keeps nulls for a statement-shaped one (`WriteElementWithoutNulls`,
  :1817-1842; F22).
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
- `src/Kronikol/Reports/FailuresDigestGenerator.cs:428-431` (`ShowsStatement`): a MongoDB call is listed by its label
  and path (F23).

**R3:**
- `src/Kronikol/Query/PathEngine.cs:33`.
- `WhereClause.cs:126-152`.
- `BodyCache.cs:37-55`.
- `QueryCommand.Values.cs:135-141, :185-190`.
- The footnote pattern, shared with `PlantUmlCreator.cs:1623`.
- `src/Kronikol/Tracking/RequestResponseLogger.cs:54-55`.
- `QueryCommand.Diff.cs:513-518` (`ScalarEquals`): integers by their digits (F25).
