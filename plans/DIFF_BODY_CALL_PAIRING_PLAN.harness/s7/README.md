# S7: the pairing over two runs from each other writer of the records it reads

Run 2026-10-07 with the worktree build of `fix/115-diff-body-pairing` (the engine s6 measured). For each writer, two
runs of the same tests, then `research/pairing.py --design` for the rule's answer at every address with a body and
`s6/acceptance.cs` for the engine's. The finding the slice looks for is anything other than "pairs as the rule pairs
it" or "refuses, exit 2, with its reason". None was found.

| Writer | How the two runs were made | Addresses | As the rule pairs them | Refused, with the reason |
|---|---|---|---|---|
| `kronikol ingest` | `producers.py`: capture feeds of three tests, the second run's health checks in another order, its order created under a new id, three queries where the first made four | 15 | **15** (11 on the URI, 2 on its shape) | 2: `s2 makes 3 CosmosDB QUERY /orders calls; … s2/i6 is the 4th of 4.` |
| `kronikol merge` | `producers.py`: the two mergeable shards in `tests/Kronikol.Tests/TestData/Reports`, merged; then merged as a second run would have written them (the first two calls of every scenario swapped, new pairing ids, new GUIDs in URIs) | 81 | **81**, all on the URI; 4.6.0's ordinal pairs 31 of them with another call | none |
| Kronikol4J | `Kronikol4JRuns.java`: the same three tests through Kronikol4J's `ReportDataSerializer.toJson` (0.1.23-SNAPSHOT jars of a local build) | 15 | **15** (11 on the URI, 2 on its shape) | 2, as for ingest |

Read on the way (READ, Kronikol4J `ReportDataSerializer.java:290-310, 851-869`): the Java writer records `type`,
`method`, `uri`, `serviceName` and `requestResponseId` under the names the .NET writer uses, and upper-cases the method
(`Set` in a .NET report is `SET` in a Java one), which the key's case-insensitive method comparison absorbs. A Java run
and a .NET run of the same tests share no `stableId` (the Java ids are scoped to no suite), so `--body` across them is
refused as the run diff refuses them, before any call is paired.

Files: `*-engine.tsv`, one line per address as in s6 (old address, the engine's partner or `-`, the rule's, its tier,
what the engine did).
