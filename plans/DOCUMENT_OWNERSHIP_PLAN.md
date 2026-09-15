# Attribution by document ownership

**Status:** written 2026-09-15; green-lit the same day ("Ok, implement all of that"); planned as **3.19.0**. §5 is
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

- 2026-09-15: written after 3.17.0; execution follows 3.18.0.
