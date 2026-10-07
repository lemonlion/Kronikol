# S6: the built engine against the rule, on real reports

Run 2026-10-07. The engine under test is the worktree build of `fix/115-diff-body-pairing` (Kronikol.Tool, net10.0,
Debug), copied out before the run; the control is the 4.6.0 build (`--version` 4.6.0). The rule is
`research/pairing.py --design`, whose shape tier now ranks only the calls the exact key leaves unpaired on each side,
as `CallPairing` does (F19 in the plan). Re-measured with that change (`pairing-rerun-a.txt`, `pairing-rerun-b.txt`),
every count in the plan's section 2.2 is unchanged and no address moves: F19's case does not occur on these reports.

```
python ../research/pairing.py xunit-4.0.2-a.json xunit-4.0.2-b.json --design design-a.tsv
python ../research/pairing.py xunit-3.20.0.json xunit-3.27.0.json --design design-b.tsv
dotnet run acceptance.cs -- <engine dir> OLD.json NEW.json design-X.tsv out.tsv     # from outside the repository
```

| Pair | Addresses | Fixed engine: as the rule pairs them | 4.6.0: as the rule pairs them | 4.6.0 differs |
|---|---|---|---|---|
| a: one commit, two runs | 1,518 | **1,518** | 1,505 | 13 |
| b: 3.20.0 to 3.27.0 | 1,096 | **1,096** | 1,055 | 41 |

- The fixed engine diffs 2,612 addresses with the call the rule names and refuses the other 2, as the rule does:
  `s109/i14` and `s109/i15`, the old run's fourth `CosmosDB Query /orders` call, which the new run made three times
  (`No call in … matches …: s109 makes 3 CosmosDB Query /orders calls; … s109/i14 is the 4th of 4.`, exit 2).
- 4.6.0's differences are exactly the wrong pairs of section 2.2, 13 and 41: it diffed the entry at the same ordinal,
  another call (`control-a.tsv`, `control-b.tsv`, the `diffed` rows whose second column differs from the third), and
  refused 1 and 29 of them as `carries no body`, the other call having none. It refused the same 2 addresses as the
  rule, for another reason: `i14 is out of range`.
- Files: `fix-*.tsv` and `control-*.tsv` hold one line per address (old address, the engine's partner or `-`, the
  rule's, the rule's tier, what the engine did); `design-*.tsv` is the rule's answer.
