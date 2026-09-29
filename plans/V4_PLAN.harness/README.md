# V4_PLAN harness

The scripts behind `V4_PLAN.md` §1 (written as `JSON_SIZE_PLAN.md`). They print sizes and counts only, never report content, so their output
is safe to read in an agent session.

| File | What it prints |
|---|---|
| `composition.py` | Bytes per top-level key, string bytes by the key that holds them (any depth), the size of `internalFlowSegments` by field when present, #85's option 1 (with and without the segment map), and the R1 model: the same data re-written indent 2 with quotes as `\"` and LF line ends, then indent 1, no indent, and option 1 on top |
| `encoder_saving.py` | What the relaxed encoder (R1) would save: every escape it would not write, counted in the file; the indentation's share of what is left; whether the file is mergeable and the size of its segment map |
| `escapes.py` | Every `\uXXXX` escape in the file by kind, and the CRLF count |
| `encoders.cs` | What the default encoder and the relaxed one write for quotes, `<`, `&`, `'`, `+` and a non-ASCII character |
| `threshold.py` | R2: for each size threshold, how many payloads a report would hold compressed and the bytes saved at gzip levels 6 and 9 (the source of `PAYLOAD_COMPRESSION_PLAN.md`'s 512) |
| `mutate_r2.py` | R2: breaks each of twelve guards in turn in a checkout, runs the facts that should catch it and restores the file; prints CAUGHT or SURVIVED per guard |

```bash
PYTHONUTF8=1 python composition.py <TestRunReport.json>...
PYTHONUTF8=1 python encoder_saving.py <TestRunReport.json>...
PYTHONUTF8=1 python escapes.py <TestRunReport.json>...
dotnet run encoders.cs
```

Measured 2026-09-29 on BreakfastProvider reports copied from earlier sessions (the xUnit lane at 3.32.4, the Docker
lane at 3.27.1, and for `encoder_saving.py` five more lanes at 3.0.83 and 3.30.0); the results are in the plan's §1. Before and after numbers for R1 and R2 come
from rerunning these on the same lane.
