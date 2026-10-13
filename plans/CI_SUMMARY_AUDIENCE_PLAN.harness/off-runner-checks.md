# Off-runner checks (2026-10-13)

Run on Windows 11 with `gh` 2.92.0 against this repository's run 38400333756 (CI Summary Preview on `f03452fc`), and
with Kronikol.Tool 4.18.1 and 4.18.2 from nuget.org installed into an isolated `DOTNET_CLI_HOME` (the machine's own
global tools untouched). The plan cites these as RUN.

## `gh run download`

```bash
gh run download 38400333756 --repo lemonlion/Kronikol --dir kronikol-38400333756    # exit 0
```

Each of the run's four artifacts (history fragments, 1.4 to 2.1 KB) landed in `kronikol-38400333756/<artifact name>/`,
keeping the paths the upload gave them (`examples/Example.Api/...`). The REST listing returned them newest first
(`created_at` 12:52:40, 12:52:29, 12:52:17, 12:51:33).

The same command again, into the same folder:

```text
error downloading kronikol-history-ci-preview-1-0fcb8c8d: error extracting zip archive: error extracting
"examples/.../Reports/History.run.json": open ...\History.run.json: The file exists.
exit=1
```

It stopped at the first artifact.

```bash
gh run download 38400333756 --repo github.com/lemonlion/Kronikol --dir host-form --name kronikol-history-ci-preview-1-0fcb8c8d   # exit 0
```

`--repo` takes the host form on github.com too, and one `--name` with `--dir` extracts straight into the folder.

## `gh run view`

`gh run view 38400333756` lists annotations of every level: errors (`X`), warnings (`!`) and notices (`-`, here the
runner image's "The ubuntu-latest label will migrate to Ubuntu 26" notice). It lists no Kronikol notice for the three
failing legs, and their logs (`dotnet test --verbosity normal`; jobs 115306004321, 115306004479 and 115306004737, of
3,231, 3,790 and 3,114 lines) hold no `::notice`, no pointer line and no "Debug this run": under `dotnet test` the
run-end hook's stdout does not reach the log.

## `kronikol query` on what a download holds

A copy of a real report renamed `Checkout.json` (what `HtmlTestRunReportFileName = "Checkout"` writes), queried by its
folder:

```text
No TestRunReport.json under name.
exit=2
```

Two copies in two artifact folders of one download:

```text
Several reports under multi/kronikol-1 — name the one you mean:
  multi/kronikol-1\reports-a\TestRunReport.json
  multi/kronikol-1\reports-b\TestRunReport.json
exit=2
```

## `dotnet tool install -g` when the tool is there (.NET SDK 10.0.300)

| State before | Output | Exit |
|---|---|---|
| not installed | `Tool 'kronikol.tool' (version '4.18.1') was successfully installed.` (with `--version 4.18.1`) | 0 |
| 4.18.1 installed | `Tool 'kronikol.tool' was successfully updated from version '4.18.1' to version '4.18.2'.` | 0 |
| 4.18.2 installed | `Tool 'kronikol.tool' is already installed.` | 0 |
