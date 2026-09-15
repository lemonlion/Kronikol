# Attribution by document ownership

**Status:** written 2026-09-15; green-lit the same day ("Ok, implement all of that"); shipped as **3.19.0**. §5 is
the execution log.

## 1. The measured need

3.17.0 detached hosted services from the test that built their host, and the outbox processor's poll left the
21 scenarios it had been landing in. It also left the one scenario where the processor's work was the point:
*Outbox message should transition to failed after exhausting retries* creates an outbox document through the
SUT's repository and waits for the host's processor to claim it, fail the dispatch twice and mark it failed.
Those four `Replace` calls were attributed to the scenario only because its host inherited the test's
execution context; detached, they belong to no scenario, and the scenario's diagram now shows the seed and the
polling reads alone (9 calls to 4 on every lane; the LightBDD lanes, whose host is built outside the test,
already read 4). `RequestResponseLogger.CaptureBackground` keeps such calls in the Background calls section,
under "(no scenario)", which is a list and not a diagram.

## 2. The mechanism

A document that a scenario wrote is the scenario's. The Cosmos tracking handler already parses every request
into an operation on a container; point operations name the document (`/dbs/{db}/colls/{coll}/docs/{id}` with
the partition key in `x-ms-documentdb-partitionkey`), and a create names it in the body and the response.

- A run-level registry, `DocumentOwnership`, maps (store, container, partition key, id) to the test identity
  that last wrote the document under an attributed flow (Create, Upsert, Replace, Patch). The last attributed
  writer wins.
- When the resolver returns no identity for a document operation (a detached hosted service, a flow no
  context reached) the handler asks the registry. A match attributes that one call to the owner with the new
  provenance `AttributionSource.DocumentOwner`. Nothing ambient is established: the next call in the same flow
  resolves on its own again, so the poll that precedes a claim stays background while the claim, the retry
  updates and the failed-status update land where the seed did.
- A call attributed this way after its owner's scenario ended expires like an inherited one
  (`BackgroundAttribution.Expire` treats `DocumentOwner` with `TestContext` and `GlobalFallback`): the
  scenario keeps what happened during its life, the background section lists the rest with the scenario it
  expired from, and a call set no longer depends on whether the processor got to a row before the run ended.
- Only identity-less calls are candidates. A call that resolved by header, scope, context or fallback is never
  re-attributed, so the mechanism cannot take a call away from a flow that has an identity.
- Queries are not candidates: a query names no document.

Cosmos first, because that is where the need was measured and the request already carries the key. Mongo
(`_id` in an insert or a filter) and Spanner (key columns in a mutation) follow the same registry; whether
their captures expose the key cheaply decides whether they ship in the same release. What does not ship is
said in §5.

## 3. Outputs

`attributionSource` gains the value `DocumentOwner` in the JSON schema and the XSD. The provenance is visible
per call as it is for every other source. No new section.

## 4. Acceptance

- Unit: an attributed create registers the owner; an identity-less replace of that document is attributed to
  the owner with `DocumentOwner`; an identity-less query is not; an attributed replace by another scenario
  moves the ownership; a call that resolved by any other source is untouched; expiry treats `DocumentOwner`
  as inherited.
- BreakfastProvider on 3.19.0: the outbox retry exhaustion scenario carries the processor's claim and retry
  updates again, on every lane that runs the processor, and two consecutive runs read `nothing changed` on
  every docker and in-memory lane.

## 5. Execution log

- 2026-09-15: written after 3.17.0; executed the same day as 3.19.0, after 3.18.0:
  1. The registry in §2 already existed: `TestCorrelationStore`, filled by `AutoCorrelateWrites` in the Cosmos
     tracker (Create, Upsert, Replace; key `cosmos:{service}:{id}`, or `ChangeFeedKeyExtractor`) and the Mongo
     tracker (Insert, Update, FindAndModify; key `mongo:{service}:{id}`), the store the change-feed and
     change-stream decorators read. So the release is the read side inside the trackers: core
     `DocumentOwnership.Resolve(resolved, key)` answers the owner with `AttributionSource.DocumentOwner` only
     when the resolver answered nothing attributed and the store has the key; `TestCorrelationStore.Lookup`
     is `Resolve` without the miss report; `BackgroundAttribution` expires `DocumentOwner` with the inherited
     sources; Cosmos looks up after `ResolveWithSource` when the operation names a document, Mongo classifies
     before it resolves and looks up when the command names an `_id`; `AttributeByDocumentOwner` (true) on
     both option types. Nine tests across the three projects pin §4's unit acceptance.
  2. Found on the way: no Mongo write had ever reached the store. The classifier named a document only from a
     top-level `filter`, which no write command carries, so `AutoCorrelateWrites` recorded nothing and
     `ChangeStreamCorrelation` could not find a writer the tracker had seen. A single insert
     (`documents[0]._id`), a single update (`updates[0].q._id`) and a `findAndModify` (`query._id`) now name
     their document.
  3. Not shipped: Spanner. Its capture keeps a mutation's table and nothing of its key, so there is nothing to
     look up and nothing to register; it stays as it was.
  4. The consumer acceptance (§4), measured on BreakfastProvider commit 1969822 (pins to 3.19.0, `Bielu.AspNetCore.AsyncApi`
     1.0.2, the AsyncAPI document regenerated):
     - Locally (xUnit, 203 scenarios, two runs): against the last 3.17.0 run exactly one scenario changed, the outbox
       retry exhaustion scenario, 4 calls to 7 (`new: Breakfast Provider>CosmosDB Replace /orders 200`); the two 3.19.0
       runs differ in no call set, and only that scenario's count moves by one (7 and 8, the processor's timing), which
       the history reads out and never trips on.
     - Run 1, the push (34952494483, all 18 gates green): every docker and in-memory lane read that scenario's one-time
       change, and nothing else of ours; the lanes whose previous run had recorded the AsyncAPI first attempt read it
       gone (`calls 2 to 1; gone: Caller>Breakfast Provider GET /asyncapi/v1.json -`), the package bump at work.
     - Run 2, dispatched (34953266301): `behaviour-change: 0` on 17 of 18 lanes; the eighteenth is an external-SUT lane
       whose reporting scenario polled once more (5 calls to 6), outside the criterion. Nine docker and in-memory lanes
       read `nothing changed`, two read only `1 slower`, and xUnit in docker broke 11 scenarios: every one Mongo-backed
       (chef notes, reviews, customer feedback), every call answered 500 after 30 s, the Mongo server-selection
       timeout, and the job's container table shows `mongodb Exited (48)`, mongod's listener failure. xUnit external
       SUT failed at compose-up on the same container (`container mongodb exited (48)`), before any test ran.
     - Run 3, dispatched (34954851650): `behaviour-change: 0` on all 18 lanes; sixteen read `nothing changed` (xUnit
       in docker: `11 fixed`); TUnit in docker broke the same 11 Mongo-backed scenarios with the same `Exited (48)`.
     - Run 4, dispatched (34956216061, all 18 gates green): `behaviour-change: 0` on every docker and in-memory lane;
       fifteen lanes read `nothing changed`, TUnit in docker `11 fixed` (the Mongo container came up this time), and
       two external-SUT lanes read `1 slower` and one behaviour change of the deployed SUT's own, outside the criterion.
     The Mongo container loss is the consumer's runner infrastructure: five docker lanes passed the same code against
     the same image in each of those runs, the external-SUT case failed before the SUT started, and the `mongo:7` tag
     had moved on Docker Hub the night before (2026-09-14 21:07Z). The attribution change itself did what §2 says on
     every lane that ran, and no lane read a behaviour change from it after the first run.
