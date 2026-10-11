# Harness for `PROXY_TAP_TRACE_CONTEXT_PLAN.md`

Run on 2026-10-11 at Kronikol 4.14.6 (`main` at `d59843fa`) on macOS with the .NET 10.0.401 SDK. Paths are arguments;
the scratch worktrees the runs used were made with `git worktree add --detach <dir> <commit>`.

| Path | What it is |
|---|---|
| `red.sh` | The red proof (plan §4): copies the two new test files onto a worktree at the previous release, adds three compile stubs that do nothing (the option as a plain property, the two counters answering 0), builds and runs the new facts. `red.sh <worktree at v4.14.6> <this release's tree> <out>` |
| `failures.py` | Reads the console logger's normal-verbosity output and prints each row's verdict, and for a failure its message, expected and actual, and the line of the test file that failed |
| `red-v4.14.6.txt` | `red.sh`'s output: 21 of the 26 new rows fail, each on its own assertion, and the 5 guard rows pass |
| `mutate.py` | The 26 mutations of plan §4: each edits one file in a scratch worktree at the release's tree, builds and runs the whole ProxyTap test project, and restores the file. `python3 mutate.py <worktree> <out.jsonl> [M1 ...]` |
| `mutations.jsonl` | One line per mutation (the facts it turned red), and a last line with the restored tree's run and the count caught |
| `wiki/Integration-ProxyTap-Extension.patch` | The wiki edit, against the wiki at `8a57576`, to apply after publication (`git apply` in a clone of the wiki) |
