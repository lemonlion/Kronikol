Checked against the versions in the issue: Google.Cloud.BigQuery.V2 3.11.0 and 3.12.0 (and 3.0.0), against ghcr.io/goccy/bigquery-emulator 0.4 (0.4.4), 0.6.0, 0.6.1, 0.6.6 and 0.8.1, with and without the 5.12.0 tracking handler. Plan and runs: `plans/CLIENT_FLOORS_PLAN.md` and its harness.

What holds:

1. 3.12.0 runs `ExecuteQuery` as `jobs.query` with `formatOptions.useInt64Timestamp` in the body (google-cloud-dotnet 4c3b692945, "stateless query fast path"); 3.11.0 runs `jobs.insert` and then `getQueryResults` with the option in the URL.
2. Every Kronikol.Extensions.BigQuery from 3.0.28 (2026-06-06) asks for 3.12.0, because `3.*` is packed as the newest version of the day. A 3.11.0 pin fails restore with NU1109 under central package management and NU1605 without it. Nothing in the extension needs 3.12.0: its suite passes on every 3.x from 3.0.0.

What does not:

3. Emulator 0.8.1 reads `formatOptions` from the `jobs.query` body (`server/handler.go` L1991-1995 at v0.8.1) and answers the call in the issue with `1698364800000000`; the read passes on 3.12.0. The FormatException with `1698364800.0` reproduces on 0.4.4 and 0.6.0, which read the option only from the URL. 0.6.1 (April 2024, goccy/bigquery-emulator#276) fixed that, and 0.8.1 would write seconds as `1698364800.000000`. The call with parameters gives the same results. Testcontainers' `BigQueryBuilder()` without an image starts `:0.4` in every release of the module, and #125 calls the suite's image the one the module starts, so the suite is likely on 0.4.4. Naming an image of 0.6.1 or later fixes the read without a rewrite.

What changes in Kronikol:

4. 5.22.1 asks for Google.Cloud.BigQuery.V2 3.0.0 or later, so a 3.11.0 pin restores again, with central package management and without. The extension's suite now runs on the newest 3.x and again on 3.0.0 in CI.
5. No option to rewrite timestamps: the tracking handler records what the emulator sends and must not change what the application receives, and the emulator fixed this case in 0.6.1.
6. The `jobs.query` call drawn as `Other` is #125.

On 0.8.1 a TIMESTAMP inside a RECORD or a REPEATED field still comes back as text with either client, which the 130-line workaround covers only if it parses that form. That is goccy/bigquery-emulator#482; its open fix, goccy/bigquery-emulator#492, corrects `jobs.query` and `tabledata.list` but not `getQueryResults`. The wiki's BigQuery page now lists the emulator's gaps.
