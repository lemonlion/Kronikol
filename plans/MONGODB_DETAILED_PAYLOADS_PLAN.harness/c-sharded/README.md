# c-sharded/

Measured on 2026-10-13 with the `mongo:8.0` image (server 8.0.32, FCV 8.0, the build the replica-set runs in
`../b-wire-shapes/` used) in the podman WSL distro (`podman-machine-default`, as root), host networking, reached from
Windows on 127.0.0.1. Drivers 3.12.0 and 2.30.0; `wire-shapes.cs` calls Kronikol through the published 4.18.1
packages, as in `b-wire-shapes/`.

The cluster `start-sharded.sh` builds: config server replica set `cfg` (one member, container `kronikol168-sh-cfg`,
port 27231), shard replica set `sh0` (one member, `kronikol168-sh-sh0`, 27232) and `mongos` (`kronikol168-sh-mongos`,
27230). `kronikol168sh.sharded` is sharded on `{ _id: "hashed" }` (one chunk); `kronikol168sh.plain` is unsharded and
not in `config.collections` (`results/start-sharded.txt`).

| Script | How to run | Output |
|---|---|---|
| `start-sharded.sh`, `stop-sharded.sh` | `MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168s/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/c-sharded/start-sharded.sh` (`stop-sharded.sh` the same way) | `results/start-sharded.txt` |
| `run-wire-shapes-sharded.sh`, `drop-probe-db.sh` | `sh run-wire-shapes-sharded.sh mongos8 'mongodb://127.0.0.1:27230/?directConnection=false'`, then `drop-probe-db.sh` (run as `start-sharded.sh` is), then `sh run-wire-shapes-sharded.sh shard8 'mongodb://127.0.0.1:27232/?directConnection=true'` | `results/wire-shapes-driver-<v>-<mongos8\|shard8>.txt` |
| `sharded-extras.cs`, `run-sharded-extras.sh` | `sh run-sharded-extras.sh` | `results/sharded-extras-driver-<v>-mongos8.txt` |
| `compare-fields.py` | `python compare-fields.py > results/field-diff.txt` | `results/field-diff.txt` |

- `run-wire-shapes-sharded.sh` runs `../b-wire-shapes/wire-shapes.cs` with only `MONGO_URL` changed (the 2.30.0 copy's
  header is rewritten as `run-wire-shapes.sh` does it). The mongos URL needs a query string because the probe appends
  `&w=majority...` to it. The probe's `directConnection=true` section replaces `replicaSet=rs0`, which neither URL
  has: on mongos that section uses the same discovered connection as the rest, and on the shard every section is direct.
- `drop-probe-db.sh` drops `kronikol168` through mongos between the mongos and the shard runs, so the shard runs start
  from a database the cluster no longer lists.
- Each wire-shapes output has one line the replica-set outputs lack: a stack frame under "Kronikol deferred
  findAndModify". The probe prints frames whose text contains "Kronikol", and the work directory's path
  (`...\c--Code-Kronikol\...`) does.
- `stop-sharded.sh` passes `-v`, so the containers' anonymous volumes (`/data/db`, `/data/configdb`) go too. The
  2026-10-13 run used a version without it; its six volumes were removed by hand.

`sharded-extras.cs` (mongos only) prints every command's and reply's top-level fields with their BSON types, a
command's `recoveryToken`, and each reply as relaxed JSON, all read inside the handler, for: (a) transactions (insert
and update on the sharded collection, then commit; insert into the unsharded collection and update on the sharded one,
then commit; insert, then abort; a find, then commit); (b) one insert into each collection outside a transaction; (c)
a find with no shard key in the filter and BatchSize 2 (find, then three getMores), and a find by `_id`; (d) an
insert of a duplicate `_id` into the sharded collection.

What it printed. Both drivers give the same field sets; they differ only in the handshake's `backpressure`, as on
the replica set.

Through mongos:

- Replies never carry `electionId` or `opTime`. A write reply has `n`, `nModified` (update), `upserted` and
  `writeErrors` when set, `ok`, `$clusterTime` and `operationTime`. `$configServerState`, `$topologyTime`,
  `lastCommittedOpTime`, `$gleStats` and `$configTime` are in no output, at the top level or nested.
- Reply fields the replica set never returned: `recoveryToken` on every reply inside a transaction (insert, update,
  find) and on the commitTransaction and abortTransaction replies, `{ "recoveryShardId" : "sh0" }` after a write and
  `{ }` in a read-only transaction; `shards` on count (`{ "sh0" : 11 }`); `raw` on createIndexes (`{
  "sh0/localhost:27232" : { "numIndexesBefore" : 1, ..., "ok" : 1.0 } }`); `retriedStmtIds: []` on the client
  `bulkWrite` (3.12.0); `msg: "isdbgrid"` on hello and isMaster.
- Both drivers add `recoveryToken` to commitTransaction and abortTransaction, with the value the last reply carried.
  It is the only command field the replica set did not show.
- A duplicate `_id` on the sharded collection: `n: 0`, `writeErrors [{ index, code: 11000, errmsg, keyPattern,
  keyValue }]`, `ok: 1.0`, `$clusterTime`, `operationTime`; the API throws `MongoWriteException`.
- Find with Limit(5) and BatchSize 2 ends with a `killCursors` the replica set did not need: the last getMore's cursor
  id is not 0. The exceptions match the replica-set runs.

Directly on the shard's primary (`directConnection=true`):

- Commands are the replica set's, field for field.
- Replies add `readOnly: false` to every reply to a command that carries `txnNumber` (retryable insert, update,
  delete, findAndModify and bulkWrite, transaction statements, commitTransaction, abortTransaction; not UpdateMany or
  DeleteMany, which carry none), `$replData: { term: 1 }` to transaction statements and to the commitTransaction and
  abortTransaction replies, and `isImplicitDefaultMajorityWC` to hello and isMaster. `electionId` and `opTime` are
  there as on the replica set; the four names the plan strips for mongos, and `$configTime`, are not.
- The events and exceptions match the replica-set runs one for one.

Against the plan's lists (`results/field-diff.txt`): `apiStrict` and `apiDeprecationErrors` are in no run (the
probe's ServerApi V1 leaves Strict and DeprecationErrors unset); `$configServerState`, `$topologyTime`,
`lastCommittedOpTime` and `$gleStats` are in no reply through mongos or from the shard; `recoveryToken` (command and
reply), `readOnly` and `$replData` are on neither list.

nuget.org, checked 2026-10-13: the newest MongoDB.Driver and MongoDB.Bson are 3.12.0 (published 2026-09-17), with
nothing later (no prerelease, no 4.x), so `../a-json-modes/json-modes.cs` was not run again.
