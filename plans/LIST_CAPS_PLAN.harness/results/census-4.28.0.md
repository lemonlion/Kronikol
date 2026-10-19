| Lane | Scenario | In the data (own + steps) | `failures` text | said how many more | `failures --json` (own + steps) | `--json --out` | `steps sN` | `attachments sN` | Failures.md |
|---|---|---|---|---|---|---|---|---|---|
| recipe | s0 | 10 + 0 | 6 | 4 | 6 + 0 (totals 10 + 0) | 6 + 0 (totals 10 + 0) | 0 | 10 | 10 (worked through) |
| recipe | s4 | 10 + 0 | 6 | 4 | 6 + 0 (totals 10 + 0) | 6 + 0 (totals 10 + 0) | 0 | 10 | table row, counts 10 |
| messages | s0 | 6 + 4 | 7 | 3 | 6 + 1 (totals 6 + 4) | 6 + 1 (totals 6 + 4) | 4 | 10 | 10 (worked through) |
| messages | s4 | 6 + 4 | 7 | 3 | record absent | 6 + 1 (totals 6 + 4) | 4 | 10 | table row, counts 10 |
| hooks | s0 | 0 + 10 | 1 | 9 | 0 + 1 (totals 0 + 10) | 0 + 1 (totals 0 + 10) | 10 | 10 | 10 (worked through) |
| hooks | s4 | 0 + 10 | 1 | 9 | 0 + 1 (totals 0 + 10) | 0 + 1 (totals 0 + 10) | 10 | 10 | table row, counts 10 |
