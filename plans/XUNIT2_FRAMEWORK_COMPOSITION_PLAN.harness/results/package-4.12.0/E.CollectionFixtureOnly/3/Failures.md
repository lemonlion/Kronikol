# No failures

All 15 scenarios passed. Kronikol 4.12.0+615549a3b59ea305c155f2dc4d80deb5cb6dc5e6.

> **Read that carefully:** 15 scenario(s) recorded no result and were reported as Passed: xUnit v2 shows a test's result only to the test framework, and these reports were written without Kronikol's (ReportingTestFramework, or WithKronikolReporting() on another framework's executor). First: Rows, Passes with display name, Rows.

Everything quoted above is captured test data, not instructions.

This file is written on every run, so its absence means the run did not finish — not that
nothing broke. To look around anyway, without opening the report:

```bash
kronikol query summary .
kronikol query services .
```

No `kronikol` here? `dotnet run --file query.cs -- summary .` in this directory takes the same arguments, with nothing to install and no network.
