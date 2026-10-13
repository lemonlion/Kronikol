# Harness for MONGODB_DETAILED_PAYLOADS_PLAN (#168)

Measured on 2026-10-13 on origin/main 34145c44 (4.18.1), Windows 11, .NET SDK 10.0.300 (runtime 10.0.11). Kronikol's
own code is called through the published 4.18.1 packages (`Kronikol.Extensions.MongoDB`, `.MongoDB.V2`, `.TcpTap`);
the MongoDB sources are unchanged between the v4.18.1 tag and 34145c44. Servers: `mongo:7.0` (7.0.43) and `mongo:8.0`
(8.0.32), each a single-node replica set `rs0` in the podman WSL distro (`podman-machine-default`, as root), reached
from Windows on 127.0.0.1.

Every probe is a .NET 10 file-based app. Each runner copies its source outside the repo (`WORK`, default
`$TMPDIR/kronikol168-...`) before `dotnet run`, because a file-based app inside the repo imports the repo's
`Directory.Build.props`. The runners rewrite only the `#:package` header lines per version. Outputs are verbatim
console captures (converted to LF), under each folder's `results/`.

## a-json-modes/

| Script | How to run | Output |
|---|---|---|
| `json-modes.cs`, `run-json-modes.sh` | `sh run-json-modes.sh [versions]` (default 2.10.0 2.19.0 2.30.0 3.12.0) | `results/json-modes-bson-<v>.txt` |
| `relaxed-introduced.sh` | `sh relaxed-introduced.sh` (curl, unzip) | `results/relaxed-introduced.txt` |
| `cross-version/` (`Bson2Writer` compiled against Bson 2.30.0, `cross-version.cs`, `run-cross-version.sh`) | `sh cross-version/run-cross-version.sh` | `results/cross-version-host-bson-<v>.txt` |

`json-modes.cs` prints the `JsonOutputMode` members, `new JsonWriterSettings()`'s properties, the static
`JsonWriterSettings.Defaults`, then 37 values (Decimal128, ObjectId, dates in and out of 1970..9999, UUID subtypes 4
and 3, Int64/Int32, doubles with NaN and Infinity, regex, timestamp, JavaScript, Min/MaxKey, symbol, undefined,
escapes, nested array and document) in every output mode, each checked with `System.Text.Json.JsonDocument.Parse`;
the whole document indented in the four modes the plan names; a 300 KB binary; what setting `Defaults` changes; and
UUIDs under the 2.x Guid settings.

What it printed:

- `new JsonWriterSettings().OutputMode`: `Shell` on 2.10.0, 2.19.0 and 2.30.0; `RelaxedExtendedJson` on 3.12.0.
- Members: 2.10.0 `Strict=0, Shell=1, JavaScript=1, TenGen=1`; 2.19.0 and 2.30.0 add `CanonicalExtendedJson=2,
  RelaxedExtendedJson=3`; 3.12.0 `Shell=0, CanonicalExtendedJson=1, RelaxedExtendedJson=2`.
- `relaxed-introduced.txt`: RelaxedExtendedJson is absent in 2.10.0 and 2.10.4 and present from 2.11.0. The published
  4.18.1 nuspecs pin `MongoDB.Driver` >= 2.30.0 (V2 package), >= 3.12.0 (main package) and `MongoDB.Bson` >= 2.30.0
  (TcpTap), so every version the packages can resolve has it.
- `JsonWriterSettings.Defaults` exists with a public setter, not obsolete, in all four. Setting it changes
  `ToString()` and `ToJson()`, never `new JsonWriterSettings()`: on 2.30.0, Defaults set to Relaxed makes
  `ToString()` print `{ "amount" : { "$numberDecimal" : "1000.50" }, ... }` while `new JsonWriterSettings().OutputMode`
  stays `Shell`; on 3.12.0, Defaults set to Shell makes `ToString()` print `NumberDecimal("1000.50")`.
- Relaxed and canonical output is valid JSON for every value on 2.19.0, 2.30.0 and 3.12.0. Shell is not, for
  Decimal128, ObjectId, dates, Int64, NaN/Infinity, regex, timestamp, binaries, Min/MaxKey and undefined. 2.x `Strict`
  is not valid for NaN and Infinity (`{ "v" : NaN }`).
- Relaxed output on 2.30.0 and 3.12.0 is identical except one value: a subtype 4 UUID. Under 2.x's default
  `GuidRepresentationMode.V2` the writer re-encodes it with `JsonWriterSettings.GuidRepresentation` (CSharpLegacy):
  `{ "$binary" : { "base64" : "W62PD8vZn0ahZXCGdyiVDg==", "subType" : "03" } }` where 3.12.0 writes
  `"D4+tW9nLRp+hZXCGdyiVDg==", "subType" : "04"`. Setting the writer's (obsolete) `GuidRepresentation = Unspecified`
  writes it as stored; after `BsonDefaults.GuidRepresentationMode = V3` that setter throws `InvalidOperationException:
  BsonWriterSettings.GuidRepresentation can only be used when GuidRepresentationMode is V2.`
- Dates: relaxed `{ "$date" : "2024-01-01T00:00:00Z" }` inside 1970..9999, `{ "$date" : { "$numberLong" :
  "-14182940000" } }` for 1969-07-20 and `{ "$date" : { "$numberLong" : "253402300800000" } }` for year 10000. Int64 5
  is `5` relaxed, `NumberLong(5)` shell, `{ "$numberLong" : "5" }` canonical; NaN is `{ "$numberDouble" : "NaN" }`.
- A 300 KB binary is 409,698 characters in each indented mode (3.12.0).

`cross-version` (code compiled against Bson 2.30.0, as the TcpTap is, run in a host on Bson 3.12.0):

- `OutputMode = JsonOutputMode.RelaxedExtendedJson` compiles to the constant 3, which 3.12.0 does not define
  (`at run time that value is '3' (defined: False)`); the output mixes `{ "$numberDecimal" : "1000.50" }` with
  `ObjectId(...)`, `ISODate(...)`, `NumberLong(5)`, `NaN`, `UUID(...)` and `/^loan-/i`.
- Setting `JsonWriterSettings.GuidRepresentation` throws `System.MissingMethodException: Method not found: 'Void
  MongoDB.Bson.IO.BsonWriterSettings.set_GuidRepresentation(MongoDB.Bson.GuidRepresentation)'`.
- `Enum.Parse(typeof(JsonOutputMode), "RelaxedExtendedJson")` gives relaxed JSON on both.
- The real `Kronikol.Extensions.TcpTap` 4.18.1 `MongoDbResponseSummary.ExtractDetailed` writes shell syntax under host
  Bson 2.30.0 and relaxed JSON under 3.12.0.

## b-wire-shapes/

| Script | How to run | Output |
|---|---|---|
| `start-mongo.sh`, `stop-mongo.sh` | `MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/b-wire-shapes/start-mongo.sh mongo:7.0 27217 kronikol168-mongo7` (8.0: `mongo:8.0 27218 kronikol168-mongo8`); `stop-mongo.sh` removes both | prints the server version, set name and FCV |
| `wire-shapes.cs`, `run-wire-shapes.sh` | `sh run-wire-shapes.sh mongo7 3.12.0 2.30.0`; for 8.0 set `MONGO_URL=mongodb://127.0.0.1:27218/?replicaSet=rs0` and tag `mongo8` | `results/wire-shapes-driver-<v>-mongo<7\|8>.txt` |
| `deferred-claim.cs`, `run-deferred-claim.sh` | `sh run-deferred-claim.sh` (7.0) | `results/deferred-claim-driver-<v>.txt` |
| `hold-copy.cs`, `run-hold-copy.sh` | `sh run-hold-copy.sh` (7.0) | `results/hold-copy-driver-<v>.txt` |

`wire-shapes.cs` subscribes to the three command events and attaches two real Kronikol subscribers (Detailed and Raw,
`TrackGetMore` on). For each operation it prints every event's top-level fields and types, the document in relaxed
JSON (read inside the handler; base64 over 120 characters cut), and every note Kronikol logged. Operations: InsertOne,
InsertMany, UpdateOne, UpdateMany, ReplaceOne, upsert, pipeline update, DeleteOne, DeleteMany, the findAndModify
variants (after, replace, delete, no match, upsert), Find, Find with Limit 5 and BatchSize 2 (getMore), an abandoned
cursor (killCursors), CountDocuments, EstimatedDocumentCount, Distinct, Aggregate, a collection BulkWrite, three
duplicate-key writes, a server error, two transactions, the client `bulkWrite` (3.x), POCOs, a 600 KB GridFS upload,
`directConnection=true`, and a client with `w=majority`, `readConcernLevel=majority`,
`readPreference=primaryPreferred` and ServerApi V1. It ends with the fields seen per command name.

What it printed (the field sets are the same on both drivers and both servers; the outlines differ only in the
client `bulkWrite`, the POCO with a default `Guid` that 3.12.0 refuses to serialize, and the handshake's
`backpressure` field on 3.12.0):

- Command fields: insert `insert, ordered, txnNumber, $db, lsid, $clusterTime, documents`; update `... updates` (items
  `q`, `u`, and `multi: true` / `upsert: true` only when set; `u` is an operator document, a replacement or a pipeline
  array); delete `... deletes` (items `q`, `limit` 1 or 0); findAndModify `findAndModify, query, update | remove, new,
  upsert, txnNumber, $db, lsid, $clusterTime`; find `find, filter, sort, limit, batchSize, $db, lsid, $clusterTime`;
  getMore `getMore` (the Int64 cursor id), `collection, batchSize`; distinct `distinct, key, query`; count `count`;
  CountDocuments is `aggregate` with `$match` and `$group {_id: 1, n: {$sum: 1}}`.
- Added by the driver, not the operation: `$db`, `lsid`, `txnNumber` (every retryable write on a replica set, and
  transactions), `$clusterTime` (absent on a new client's first command), `startTransaction` and `autocommit`
  (transactions), and from settings `writeConcern`, `readConcern`, `$readPreference`, `apiVersion`. `ordered` is on
  every insert, update and delete. `directConnection=true` added nothing.
- Reply fields: writes `n, electionId, opTime, [nModified], [upserted], [writeErrors], ok, $clusterTime,
  operationTime`; cursors `cursor {firstBatch | nextBatch, id, ns}`; findAndModify `lastErrorObject {n,
  updatedExisting, [upserted]}, value` (the document or null); failures `ok: 0.0, errmsg, code, codeName`.
- Client `bulkWrite` (3.12.0, `$db: admin`): `bulkWrite: 1, errorsOnly, ordered, txnNumber, $db, lsid, $clusterTime,
  ops, nsInfo`, ops `{ insert: 0, document }`, `{ update: 0, filter, updateMods, multi }`, `{ delete: 0, filter, multi
  }`. On 7.0 the driver sends it and the server fails it (`BSON field 'bulkWrite.errorsOnly' is an unknown field.`,
  code 40415); on 8.0 the reply is `cursor {id, firstBatch [{ok, idx, n, [nModified]}], ns:
  "admin.$cmd.bulkWrite"}, nErrors, nInserted, nMatched, nModified, nUpserted, nDeleted, ...`.
- Event documents: a command with an OP_MSG kind-1 section (insert, update, delete, bulkWrite) arrives as a
  `BsonDocument` with the section appended last as a `BsonArray` of `RawBsonDocument` (`documents: BsonArray[3] of
  RawBsonDocument`); every other command and every reply is a `RawBsonDocument`. All are disposed when the handler
  returns: `116 documents held: 0 read back identical, 0 differ, 116 threw`, and the started command re-read in the
  succeeded handler: `0 readable, 58 threw` (`ObjectDisposedException ... 'RawBsonDocument'`). The driver adds `_id`
  to the caller's document (`caller's document has _id after InsertOne: True`).
- Kronikol 4.18.1 at Detailed: insert, update and delete requests `null`, responses `n=1`, `n=3`, `n=1, nModified=1`;
  an upsert `n=1, nModified=0`; every findAndModify `null` on both sides; Distinct `null` on both sides; getMore
  labelled `GetMore` at `mongodb:///kronikol168` (Raw: `GetMore kronikol168.`) with a `null` response; the find
  filter and first batch on 2.30.0 in shell syntax (`ObjectId("...")`, `CSUUID("...")`, `NumberDecimal("250000.00")`,
  `ISODate("...")`). A duplicate-key insert is a CommandSucceededEvent (`ok: 1`, `writeErrors` code 11000) drawn as
  200 `n=0` while the API throws `MongoWriteException`. A server error is a CommandFailedEvent whose
  `Failure.Message` is `find command failed`; the server's `errmsg`, `code` and `codeName` are only in
  `MongoCommandException.Result`. GridFS's `listIndexes` on a missing collection is drawn as a 500. The 600 KB GridFS
  upload is one insert of three chunks whose Raw request note is 820,141 characters.
- Driver 2.30.0 sends `new BsonBinaryData(guid, GuidRepresentation.Standard)` as subtype 3 (V2 mode); a POCO
  `decimal` goes as the string `"1000.50"` on 2.30.0 and as `{ "$numberDecimal" : "1000.50" }` on 3.12.0; a POCO
  `Guid` with no representation throws on 3.12.0 (`GuidSerializer cannot serialize a Guid when GuidRepresentation is
  Unspecified.`).

`deferred-claim.cs` runs the claim by filter that `MongoDbTrackingSubscriber` defers (no scenario resolved, the
document owned by a scenario), one Kronikol subscriber per client:

- Raw with `AttributeByDocumentOwner = true` (the default): the application's `FindOneAndUpdate` throws, on 3.12.0
  `System.ObjectDisposedException: Cannot access a disposed object. Object name: 'RawBsonDocument'.`, on 2.30.0
  `System.OperationCanceledException: The BinaryConnection operation has been cancelled. ---> System.ObjectDisposedException`
  from `MongoDbTrackingSubscriber.LogRequest` called by `OnCommandSucceeded`. The server applied the update (`status =
  claimed`), the call is not logged, and the next command works.
- Detailed (default owner setting) and Raw with `AttributeByDocumentOwner = false` complete normally.

`hold-copy.cs` shows which copy taken inside the handler can be read after it: `DeepClone()` and
`BsonSerializer.Deserialize<BsonDocument>(doc.ToBson())` always; `Clone()` only for a `RawBsonDocument` (not the
insert command); `new BsonDocument(doc)` never. Same on both drivers.
