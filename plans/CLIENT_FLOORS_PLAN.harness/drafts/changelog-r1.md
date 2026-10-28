<!-- Draft CHANGELOG entry for R1, in the 4.20.2 entry's shape. <version> and <date> at release. Assumes Q1 = 3.0.0,
     Q3 = yes; edit if the owner picks otherwise. -->

## [<version>] - <date>

**Patch - the BigQuery package asks for Google.Cloud.BigQuery.V2 3.0.0 or later, not the newest of the day (#205,
#97; `plans/CLIENT_FLOORS_PLAN.md` R1).** A packaging fix with no new option and no new public member, so the patch
part moves.

### Fixed

- **Each release raised the BigQuery client every consumer had to take.** `Kronikol.Extensions.BigQuery` referenced
  Google.Cloud.BigQuery.V2 `3.*`, which NuGet packs as the newest version of the day: 3.11.0 from 3.0.0 to 3.0.27 and
  3.12.0 from 3.0.28 (2026-06-06) on. A project that pinned 3.11.0 failed restore (NU1109 under central package
  management, NU1605 without it), and a suite that moved to 3.12.0 to restore ran its services on a client they do not
  ship with. 3.12.0 also runs `ExecuteQuery` through a different REST call. The package now asks for 3.0.0: the
  extension's suite passes on every 3.x from 3.0.0 to 3.12.0, restore reports no advisory at 3.0.0, the client's
  assemblies are strong-named, and a project on 3.12.0 builds and runs against the extension built at 3.0.0.

Behaviour change: a project that takes the client only through Kronikol now resolves 3.0.0, where it resolved the
newest; reference Google.Cloud.BigQuery.V2 yourself for a newer one.

### Tests

- `BigQueryPackageTests` packs the extension and reads the floor back from the nuspec.
- The BigQuery suite runs on the newest 3.x through a direct reference, and CI runs it again on 3.0.0.
- `Kronikol.Tests` and the example's integration tests take AngleSharp 1.5.0, past GHSA-pgww-w46g-26qg (moderate,
  test projects only).

### Docs

- The wiki's BigQuery page gains "Client versions and emulators": the floor, the 3.12.0 `jobs.query` path, the
  emulator release that reads its options (0.6.1), Testcontainers' default image, and three gaps in emulator 0.8.1.
