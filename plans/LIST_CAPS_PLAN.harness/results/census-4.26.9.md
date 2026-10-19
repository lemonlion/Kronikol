| Lane | Scenario | In the data (own + steps) | `failures` text | said how many more | `failures --json` (own + steps) | `--json --out` | `steps sN` | Failures.md |
|---|---|---|---|---|---|---|---|---|
| recipe | s0 | 10 + 0 | 4 | nothing | 4 + 0 | 4 + 0 | 0 | 10 (worked through) |
| recipe | s4 | 10 + 0 | 4 | nothing | 4 + 0 | 4 + 0 | 0 | table row, none |
| messages | s0 | 6 + 4 | 5 | nothing | 4 + 1 | 4 + 1 | 4 | 10 (worked through) |
| messages | s4 | 6 + 4 | 5 | nothing | record absent | 4 + 1 | 4 | table row, none |
| hooks | s0 | 0 + 10 | 1 | nothing | 0 + 1 | 0 + 1 | 10 | 10 (worked through) |
| hooks | s4 | 0 + 10 | 1 | nothing | 0 + 1 | 0 + 1 | 10 | table row, none |
