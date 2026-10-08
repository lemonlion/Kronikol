# No failures

All 15 scenarios passed. Kronikol 4.9.0+417c8e5855adb837099c9bb7f98b3a7606a79fbe.

This file is written on every run, so its absence means the run did not finish — not that
nothing broke. To look around anyway, without opening the report:

```bash
kronikol query summary .
kronikol query services .
```

No `kronikol` here? `dotnet run --file query.cs -- summary .` in this directory takes the same arguments, with nothing to install and no network.
