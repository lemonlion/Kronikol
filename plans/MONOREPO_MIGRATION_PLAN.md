# Polyglot monorepo migration — Kronikol4J into Kronikol

**Goal:** one repository holding the .NET, Java and (later) Node implementations in language-scoped
subtrees, with the cross-language parity harness promoted to a first-class shared asset.

**Primary motivation, restated:** `Kronikol4J/parity-harness/dotnet-capture/Capture.csproj` carries 12
`ProjectReference`s of the form `..\..\..\Kronikol\src\<Project>\<Project>.csproj`. That path resolves only
because the two repos happen to sit side by side on one machine. It cannot resolve in the Java CI, so
`ci.yml` there never regenerates goldens — it asserts against fixtures checked in by hand. .NET output can
therefore drift away from the Java port and nothing fails. Every "port is complete" claim to date has been
corrected by the next manual re-audit; this is the structural reason why.

After the move that reference becomes `../../dotnet/src/Kronikol/Kronikol.csproj`, resolvable in any clone,
and golden regeneration becomes a CI job that fails the Java build in the same PR that changes .NET output.

---

## 0. Target layout

```
Kronikol/
├── dotnet/                      # everything .NET, moved down wholesale
│   ├── src/  tests/  examples/  templates/  prototypes/  tools/
│   ├── Kronikol.sln  release.slnf
│   ├── Directory.Build.props  global.json
│   ├── nuget-readme.md
│   └── CHANGELOG.md             # .NET release history
│
├── java/                        # Kronikol4J, moved down wholesale
│   ├── kronikol4j-*/            # 35 modules
│   ├── build.gradle.kts  settings.gradle.kts  gradle.properties
│   ├── gradlew  gradlew.bat  gradle/  build-logic/
│   ├── playwright/  templates/  jbang-catalog.json
│   └── CHANGELOG.md             # Java release history
│
├── js/                          # Kronikol.js lands here later, same shape (docs/NODE_PORT_PLAN.md)
│
├── parity/                      # shared: belongs to no single language
│   ├── dotnet-capture/          # was java/parity-harness/dotnet-capture
│   └── fixtures/                # goldens, consumed by java/ and later js/
│
├── docs/                        # JAVA_PORT_PLAN, NODE_PORT_PLAN, BROWSER_RENDER_WORKER_PLAN, wiki source
├── .github/workflows/           # dotnet-ci, java-ci, parity, dotnet-release, java-release, codeql
├── CLAUDE.md                    # root rules + delegation to dotnet/CLAUDE.md, java/CLAUDE.md
├── README.md  LICENSE  CODE_OF_CONDUCT.md  CONTRIBUTING.md  SECURITY.md
└── icon.png  icon.svg  .editorconfig  .gitattributes  .gitignore
```

### The rule that decides where a file goes

**Nothing language-specific at the root.** `Directory.Build.props` is MSBuild-only → `dotnet/`.
`gradle.properties` is Gradle-only → `java/`. What stays at the root is genuinely shared: licence, conduct,
security policy, contribution guide, icons, and one delegating `CLAUDE.md`.

Applying that to the current 28 tracked root entries of this repo:

| Move to `dotnet/` | Stays at root | Move to `docs/` |
|---|---|---|
| `src/` `tests/` `examples/` `templates/` `prototypes/` `tools/` | `.github/` `.gitignore` `.gitattributes` `.editorconfig` `.claude/` | `JAVA_PORT_PLAN.md` |
| `Kronikol.sln` `release.slnf` | `LICENSE` `README.md` `SECURITY.md` | `NODE_PORT_PLAN.md` |
| `Directory.Build.props` `global.json` | `CODE_OF_CONDUCT.md` `CONTRIBUTING.md` | `BROWSER_RENDER_WORKER_PLAN.md` |
| `nuget-readme.md` `CHANGELOG.md` | `CLAUDE.md` (rewritten) `icon.png` `icon.svg` | |

---

## 1. Preconditions

Do not start until all of these hold.

1. **The working tree is quiet.** This touches every tracked path in the repo; a concurrent session editing
   `src/Kronikol/Reports/` will conflict with essentially everything. At time of writing there *is* active
   uncommitted work in `Reports/` (`DiagramContextMenu.cs`, `ReportGenerator.cs`, `stylesheets.css`,
   `REPORT_QUERY_PLAN.md`, …). Land or abandon it first.
2. **No open worktrees or feature branches.** Already true: `git worktree list` shows only `main`, and the
   only other branches are the abandoned `setup-action` / `setup-action-basic-feature` (840 behind). Delete
   those two before starting so nothing has to be rebased across the move.
3. **Both repos pushed and green.** `origin/main` current for Kronikol; Kronikol4J likewise.
4. **A release has just gone out**, so the move sits at a clean version boundary rather than mid-cycle.
5. **Decide the tag scheme** (§5) before touching `release.yml` — retagging conventions are hard to walk back.

---

## 2. Phase 1 — .NET tree into `dotnet/`

One mechanical commit that changes paths and nothing else. Use `git mv` so renames are detected and
`git log --follow` keeps working.

```bash
mkdir dotnet docs
git mv src tests examples templates prototypes tools dotnet/
git mv Kronikol.sln release.slnf Directory.Build.props global.json nuget-readme.md CHANGELOG.md dotnet/
git mv JAVA_PORT_PLAN.md NODE_PORT_PLAN.md BROWSER_RENDER_WORKER_PLAN.md docs/
```

### What survives the move untouched

- **`Kronikol.sln` / `release.slnf`** — solution paths are relative to the solution file. Everything moves
  together, so they stay valid with no edit.
- **`Directory.Build.props`** — MSBuild walks up from each project and finds `dotnet/Directory.Build.props`.
- **Test path resolution** — audited: `PlaywrightTestBase`, `WikiScreenshotTests` and `WikiGifTests` resolve
  from `Assembly.Location`, not from a repo-root walk-up. No `../../..` traversal exists anywhere in `src/`
  or `tests/`. Nothing to fix here.

### What needs an edit

- **`global.json`** — the SDK pin is found by walking up from the *current directory*. At `dotnet/global.json`
  it no longer applies to commands run from the repo root. Either run all .NET commands with
  `working-directory: dotnet`, or keep a root copy. Recommend the former; it is explicit and CI already
  supports `working-directory`.
- **`.github/workflows/*.yml`** — 28 distinct path literals need a `dotnet/` prefix (`tests/Kronikol.Tests`,
  `examples/Example.Api/...`, `release.slnf`, …). Cleaner alternative: leave the literals alone and add
  `defaults: run: working-directory: dotnet` to each .NET job. Fewer edits, less to get wrong.
- **`.gitignore`** — currently .NET-shaped at the root. Split: keep genuinely global entries at the root, move
  `bin/ obj/` style rules into `dotnet/.gitignore` and add `java/.gitignore` for `build/ .gradle/`.

**Verify before committing:** `dotnet build dotnet/release.slnf -c Release` and the Core Tests project both
pass, and `git status` shows only renames.

---

## 3. Phase 2 — Java tree into `java/`

Bring Kronikol4J in **with its history**, as a second parent, rather than copying files.

```bash
git remote add kronikol4j c:/Code/Kronikol4J
git fetch kronikol4j
git subtree add --prefix=java kronikol4j main
```

This costs ~33 MB of git objects on top of the existing 45 MB — history size is a non-issue. `git log java/`
then shows the full Java history and blame keeps working.

### Edits after the subtree lands

- **`java/settings.gradle.kts`** needs no change: module includes are relative to the Gradle root, which is
  now `java/`.
- **Gradle invocation** — every Java CI job runs `./gradlew` from `java/`, i.e. `working-directory: java`.
- **`java/CLAUDE.md`** stays as the Java-specific rules; the root `CLAUDE.md` delegates to it.
- **`java/CHANGELOG.md`** stays as the Java release history. Two changelogs is correct here — do not merge
  them; 3.0.46 and 0.1.25 are unrelated release trains.
- **Collision check.** These exist in both repos with different content and must *not* be merged into one
  file: `CHANGELOG.md`, `CLAUDE.md`, `README.md`, `LICENSE`, `.gitattributes`, `.gitignore`, `templates/`.
  The subtree prefix keeps them naturally separate — this is precisely what a flat `git subtree` into the
  root would have destroyed.

---

## 4. Phase 3 — promote `parity/` (the payoff)

```bash
git mv java/parity-harness/dotnet-capture parity/dotnet-capture
```

Then rewrite the 12 `ProjectReference`s in `parity/dotnet-capture/Capture.csproj`:

```diff
-<ProjectReference Include="..\..\..\Kronikol\src\Kronikol\Kronikol.csproj" />
+<ProjectReference Include="..\..\dotnet\src\Kronikol\Kronikol.csproj" />
```

…and the same for the 11 extension projects (`Redis`, `Npgsql`, `MongoDB`, `Elasticsearch`,
`MySqlConnector`, `ClickHouse`, `S3`, `SQS`, `SNS`, `DynamoDB`, `BigQuery`).

Point the fixture-copy step at `parity/fixtures/`, consumed by
`java/kronikol4j-report/src/test/resources/parity/`. When KronikolJS arrives it reads the same fixtures
rather than growing a second harness.

**This is the step that makes the whole migration worth doing.** Everything else is filing.

---

## 5. Phase 4 — CI and release rework

### 5a. Tags — the one hard blocker

Both `release.yml` files trigger on `tags: ['v*']`. In one repo, tagging `v3.0.47` would fire the Gradle
publish to Maven Central as well as the NuGet publish. Must be fixed **before** the Java tree lands.

| | Now | After |
|---|---|---|
| .NET | `v3.0.46` | `dotnet-v3.0.47` |
| Java | `v0.1.24` | `java-v0.1.25` |

Update each `release.yml` trigger to its own prefix and its version-extraction step to strip it. Note this
breaks the `v{version}` convention recorded in `CLAUDE.md` — update that text in the same commit. Existing
`v*` tags stay as historical artefacts; do not rewrite them.

### 5b. Versions stay independent

.NET is at 3.0.46, Java at 0.1.25-SNAPSHOT. The `CLAUDE.md` rule "all packages must use the same version
number" becomes **all packages within a language stack**. Rewrite that line explicitly or a future session
will lockstep-bump them and publish a 3.0.47 Java artifact.

### 5c. Job layout

| Workflow | Trigger paths | Runs |
|---|---|---|
| `dotnet-ci.yml` | `dotnet/**`, `parity/**` | existing 22-job matrix + `Release Build (packable projects)` |
| `java-ci.yml` | `java/**`, `parity/**` | `./gradlew build` on JDK 17/21/25 |
| `parity.yml` | `dotnet/**`, `java/**`, `parity/**` | regenerate goldens from .NET, diff against the Java fixtures, fail on drift |
| `codeql.yml` | — | one config, both languages |
| `dotnet-release.yml` / `java-release.yml` | `dotnet-v*` / `java-v*` | as today |

`parity.yml` is the new capability and the reason for the migration. It needs both toolchains on one runner
(`setup-dotnet` + `setup-java`), which is exactly what is impossible while the repos are split.

**Known friction:** path-filtered workflows that are also *required* status checks never report on a skipped
run, so PRs can hang waiting on a check that will never arrive. Either make the filtered jobs non-required
and keep `parity.yml` (unfiltered) as the required one, or add a no-op fallback job.

---

## 6. Phase 5 — docs and wiki

GitHub allows **one wiki per repository**. Two exist: `Kronikol.wiki` and `Kronikol4J.wiki`.

Recommended: keep `Kronikol.wiki` as the single wiki and fold the Java pages in under a `Kronikol4J` section,
with a page-name prefix to avoid collisions. Archive `Kronikol4J.wiki` with a redirect note.

`CLAUDE.md` currently instructs updating `../Kronikol.wiki`; that path is unchanged by this migration, so the
instruction stays valid — but it should be extended to say which section Java changes go in.

---

## 7. Phase 6 — decommission the Kronikol4J repo

**Do not delete it.** The Maven Central artifacts already published (0.1.24) carry
`github.com/lemonlion/Kronikol4J` in their POM `scm` and `url` metadata. That is permanent in shipped
artifacts.

Instead: strip the repo to a README pointing at the monorepo, then **archive** it. Archiving keeps the URL,
the issue history and any inbound links alive and read-only. GitHub also redirects renamed/transferred repos,
but a delete breaks every one of those POM links.

Open the Maven Central publishing question separately: the `io.github.lemonlion` namespace is verified via
the GitHub account, not the repo, so publishing from the monorepo should continue to work — **verify this
before the first post-migration Java release** rather than discovering it at tag time.

---

## 8. Rollback

Each phase is one commit. Phases 1 and 3 are pure `git mv` + text edits — `git revert` restores them
cleanly. Phase 2 is a subtree merge commit; reverting it removes `java/` while leaving the history objects
harmlessly in place. Nothing here is destructive to either repo's history, and Kronikol4J stays intact and
pushed throughout — the migration only becomes one-way at Phase 6, which is deliberately last and
independently reversible (un-archiving is a click).

---

## 9. Open decisions

1. ~~**`js/` now or later?**~~ **Resolved.** [NODE_PORT_PLAN.md](NODE_PORT_PLAN.md) has been amended: its
   locked decisions now place Kronikol.js in the `js/` subtree of this monorepo rather than a separate
   `KronikolJS` repo, with goldens from the shared `parity/`, `js-v*` release tags, a path-filtered
   `js-ci.yml`, and its wiki as a section of `Kronikol.wiki`. No third repo will need merging later.
2. **Root `CLAUDE.md` structure** — one delegating file plus per-language files, versus one large file with
   language sections. Recommend delegation; the TDD/versioning rules genuinely differ per stack.
3. **`.editorconfig`** — currently .NET-flavoured at the root. Keep shared at the root, or split per subtree.
4. **The 29 floating `X.*` package references** in `dotnet/src/` are unrelated to this migration but will
   surface in `parity.yml` runs as spurious failures when an upstream release drifts. Consider narrowing
   them before adding a job that builds both stacks on every push.

---

## 10. Sequencing summary

| # | Phase | Reversible | Blocked by |
|---|---|---|---|
| 0 | Preconditions: quiet tree, branches deleted, both repos green | — | active work in `Reports/` |
| 1 | .NET → `dotnet/`, plans → `docs/` | yes | 0 |
| 2 | Tag scheme split in both `release.yml` | yes | decision in §5a |
| 3 | Java → `java/` via `git subtree add` | yes | 1, 2 |
| 4 | `parity/` promoted, 12 `ProjectReference`s rewritten | yes | 3 |
| 5 | CI split: `dotnet-ci`, `java-ci`, **`parity`** | yes | 4 |
| 6 | Wiki consolidation | yes | 5 |
| 7 | Kronikol4J archived with redirect README | yes (un-archive) | 6, and a green `parity.yml` |

Phases 1–5 are a day's careful work on a quiet tree. Phase 7 should wait until `parity.yml` has caught at
least one real drift, proving the migration delivered the thing it was for.
