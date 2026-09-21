# QUERY_FALLBACK_PLAN.md — replace the Python fallback with the CLI itself

**Date:** 2026-09-13 · **Repo version:** 3.3.0 (released) · **Target:** 3.4.0 (MINOR)
· **Status: investigation complete and measured, nothing implemented, NOT green-lit.**

**The short version.** The skill ships `scripts/query.py`, a 413-line reimplementation of six of the
tool's eighteen verbs, advertised as what to use "if the tool is genuinely unavailable". It is the
wrong fallback on three counts: its runtime (Python) is *rarer* on a .NET machine than the one it
backs up, it has already silently drifted a release behind the copy this repo uses, and reaching
parity would mean maintaining a second implementation of a 6,568-line query engine forever.

**Replace it with a .NET 10 file-based app emitted beside the report.** `dotnet run query.cs`
needs no install, no admin, no PATH entry and **no network** — the `#:package` reference resolves
out of the NuGet cache that the test run itself populated. Because it calls the real
`QueryCommand.Run`, parity is not a thing to maintain; it is the same code.

Everything in §1 was measured on this machine, not reasoned about. §9 is the assumption ledger and
says which claims are measured and which are still assumptions.

---

## 1. Measured evidence

All on SDK 10.0.300 / runtime 10.0.11, Windows 11, in the session scratchpad.

| Claim | Method | Result |
|---|---|---|
| File-based apps run | `dotnet run hello.cs -- a b` | works; `args.Length == 2`, runtime 10.0.11 |
| `#:package` works | `#:package Newtonsoft.Json@13.*` | resolves and runs |
| **Resolves with no network** | `NuGet.config` with `<packageSources><clear /></packageSources>`, `#:package Kronikol@3.0.83` | **resolves from `~/.nuget/packages`** |
| A version not in cache fails closed | same, `@3.0.86` (not cached) | `NU1101` — loud, not silent |
| Cold cost | first `dotnet run` of that file | **2.9 s** |
| Warm cost | second run | **0.25 s** |
| Works inside the report directory | `query.cs` in `bin/Debug/net10.0/Reports/` under a parent `.csproj` | works |
| **SDK 10 is required** | `global.json` pinned `9.0.314`, `rollForward: disable` | **fails**: "Couldn't find a project to run" |
| Cache holds every TFM asset | `ls ~/.nuget/packages/kronikol/3.0.83/lib/` | `net8.0`, `net9.0`, `net10.0` |

The last row is load-bearing and was the one genuine risk to the design. NuGet caches the **whole
package**, so a test project targeting `net8.0` still leaves `lib/net10.0/Kronikol.dll` on disk —
and the net10.0 file-based app resolves that asset offline. **A `net8.0` test project still gets a
working fallback.**

### 1.1 The dependency direction, which decides the whole shape

The obvious design — a new `Kronikol.Query` package — **does not work**, and it is worth recording
why so nobody re-proposes it.

The query engine's only non-BCL dependency is `Kronikol.Reports` (measured: `grep` over
`src/Kronikol.Tool/Query/*.cs` and `QueryCommand*.cs` yields `Kronikol.Reports`,
`Kronikol.Reports.FailureText`, `Feature`, `InteractionStatus`,
`Merge.MergeableReportReader`, `ReportGenerator.ReportFormatVersion`, and nothing else — no OTLP,
nothing from the rest of the tool). Those types live in `Kronikol`. So `Kronikol.Query` would have
to depend on `Kronikol`, not the reverse — which means a test project referencing `Kronikol` would
**not** pull `Kronikol.Query`, the cache would be cold, and the offline property would be gone.

So the engine goes **into the `Kronikol` package itself**, under a new `Kronikol.Query` namespace.
The template's chain is `Kronikol.BDDfy.xUnit3` → `Kronikol.xUnit3` → `Kronikol` (verified), so
`Kronikol` is in every consumer's cache at the exact version already.

### 1.2 The size cost, and why it is near zero in practice

`Kronikol.dll` is 1.94 MB; the whole of `Kronikol.Tool.dll` — query *plus* ingest, export, merge,
ctrf and init-agents — is 398 KB. The query slice is a fraction of that.

It costs nothing at all on `net8.0`/`net9.0`: compile the engine under `#if NET10_0_OR_GREATER`.
`Kronikol` multi-targets `net8.0;net9.0;net10.0` (`Directory.Build.props:6`), the engine already
compiles only for `net10.0` today, and the file-based app always resolves the `net10.0` asset. So
the two older TFMs are byte-unchanged and no net8.0 port of 6,568 lines is needed.

---

## 2. What gets built

### 2.1 The engine moves

`src/Kronikol.Tool/Query/*.cs` and `src/Kronikol.Tool/QueryCommand*.cs` (6,568 lines, 20 files)
move to `src/Kronikol/Query/`, namespace `Kronikol.Tool.Query` → `Kronikol.Query`, wrapped in
`#if NET10_0_OR_GREATER`.

- **One new public type**: `Kronikol.Query.QueryCommand` with `Run(IReadOnlyList<string>, TextWriter,
  TextWriter, Func<string,string?>?)` and `PrintUsage(TextWriter)` — today's signatures
  ([QueryCommand.cs:25](src/Kronikol.Tool/QueryCommand.cs#L25),
  [:372](src/Kronikol.Tool/QueryCommand.cs#L372)), which is why this is a MINOR and not a rewrite.
  Everything else stays `internal`.
- `src/Kronikol/Kronikol.csproj` gains `<InternalsVisibleTo Include="Kronikol.Tool" />`. It already
  has `Kronikol.Tests` (line 12), so `SkillDriftTests`' reach into `Verbs`, `FlagsByVerb`,
  `JsonCommands` survives unchanged. **Add it inside the existing `ItemGroup`, not a
  `PropertyGroup`** — IVT in a `PropertyGroup` is a silent no-op and has bitten this repo before.
- `Commands.cs:29-31` keeps dispatching `query`; the lambda now calls `Kronikol.Query.QueryCommand.Run`.
  The `kronikol query` CLI is behaviourally untouched.

### 2.2 The report writes its own query tool

A new output in `ReportGenerator`'s isolated list
([ReportGenerator.cs:370-418](src/Kronikol/Reports/ReportGenerator.cs#L370-L418)), beside
`Failures.md` and `CLAUDE.md`, gated on a new `ReportConfigurationOptions.WriteQueryScript`
(default `true`):

```csharp
#:package Kronikol@3.4.0
return Kronikol.Query.QueryCommand.Run(args, Console.Out, Console.Error);
```

The version is `KronikolVersion` — the exact version that wrote the report, which is by
construction the version already in the cache. Three properties fall out of that:

- **Offline.** Exact-version cache hit, no source list consulted.
- **Honest failure.** A report carried to a machine that never restored Kronikol fails `NU1101`
  loudly rather than answering from a mismatched engine.
- **No skew.** The engine that reads the report is the engine that wrote it, so a report from an
  older Kronikol gets that Kronikol's query surface — which is more correct than today, where a
  newer global tool reads an older report and prints `! report predates step attribution`.

Register it in the same `Add(...)` pattern so a write failure is isolated and it is named in the
run summary only if it was actually written (the `written` set at
[ReportGenerator.cs:419](src/Kronikol/Reports/ReportGenerator.cs#L419)).

### 2.3 The Python script is deleted

`templates/skills/kronikol-test-debugging/scripts/query.py`,
`.claude/skills/kronikol-test-debugging/scripts/query.py`,
`tests/Kronikol.Tests/Tool/FallbackScriptTests.cs` and `tests/Kronikol.Tests/PythonProbe.cs`
(no other consumer — verified by grep). `InitAgentsCommand.cs:80`'s file list drops
`scripts/query.py`.

---

## 3. Every surface that advertises the CLI

Ten places say "install the tool", and the fallback sentence lives in two of them. All were located
by grep; all must move together or the advice contradicts itself.

| # | File | What changes |
|---|---|---|
| 1 | [src/Kronikol/Reports/agent-instructions.md:33](src/Kronikol/Reports/agent-instructions.md#L33) | The embedded resource written as `CLAUDE.md`/`AGENTS.md` beside every report. Lead with `dotnet run query.cs -- summary .`; keep the global install as the second line for people who want `kronikol` on PATH. |
| 2 | [src/Kronikol/Reports/ReportGenerator.cs:5236](src/Kronikol/Reports/ReportGenerator.cs#L5236) | The JSON-schema `$comment`. |
| 3 | [src/Kronikol/Reports/RunSummaryConsoleWriter.cs:208](src/Kronikol/Reports/RunSummaryConsoleWriter.cs#L208) | The CI summary markdown. |
| 4 | [src/Kronikol/Reports/FailuresDigestGenerator.cs:465](src/Kronikol/Reports/FailuresDigestGenerator.cs#L465), [:477-480](src/Kronikol/Reports/FailuresDigestGenerator.cs#L477-L480) | The bash blocks inside `Failures.md`. |
| 5 | [templates/agents/CLAUDE.md:24](templates/agents/CLAUDE.md#L24) | Scaffolded repo-root instructions. |
| 6 | `templates/skills/kronikol-test-debugging/` | The skill — **its own section, §4**. |
| 7 | `.claude/skills/kronikol-test-debugging/` | This repo's own copy; §6's sync test makes it a copy rather than a fork. |
| 8 | [README.md:147](README.md#L147) | Keep the global install as the primary story for humans; add the zero-install line. |
| 9 | [templates/README.md:55](templates/README.md#L55) | "a Python fallback for machines without the CLI" → describe `query.cs`. |
| 10 | Repo-root `CLAUDE.md` | The "Debugging a test run (Kronikol)" block. |

Note #1 and #5 are **different texts** (verified by diff), so neither can be regenerated from the
other; both are edited by hand.

---

## 4. The `kronikol-test-debugging` skill

The skill is the surface an agent actually reads, and it is shipped three ways — scaffolded by the
templates, installed by `kronikol init-agents`, and vendored in this repo under `.claude/skills/`.
It is 600 lines across two files. Most of it needs no change, and knowing *which* part is the point
of this section.

### 4.1 What does not change, and why

`SKILL.md`'s ladder (§The ladder), recipe table (§Recipes), §Budget discipline, §Traps and §A worked
run all write verbs bare — `failures`, `interactions s1`, `grep "4173" --number` — with no
`kronikol` prefix (verified: only 4 lines in 220 name the executable). They are invocation-agnostic
already and stay byte-identical. That is what keeps this change small: the skill teaches a *query
surface*, and the query surface is not changing.

### 4.2 `SKILL.md` — three edits

1. **§The rule, lines 24-26.** The block that currently reads

   ```
   dotnet tool install -g Kronikol.Tool      # once, if `kronikol` is not on PATH
   kronikol query summary .logs/kronikol/TestRunReport.json
   ```

   leads with the zero-install form instead, with the global install kept as the second option:

   ```
   dotnet run query.cs -- summary .          # in any reports directory; no install, no network
   kronikol query summary .                  # if you installed the tool
   ```

2. **§The rule, lines 29-31.** The fallback sentence, replaced by §5's text.

3. **§Rung zero, lines 33-47.** This is the substantive addition. The section enumerates what a
   reports directory carries — `Failures.md`, `Failures.jsonl`, `CLAUDE.md`/`AGENTS.md` — and after
   M2 that directory carries a fourth thing. A new bullet, in the same voice:

   > - **`query.cs`** — the query tool itself, pinned to the Kronikol that wrote this report.
   >   `dotnet run query.cs -- <command> .` from this directory runs the same engine as
   >   `kronikol query`, off the NuGet cache the test run already populated. First run takes a few
   >   seconds to build; after that it is instant. Needs the .NET 10 SDK.

   Placing it in Rung zero rather than in §The rule matters: an agent that lists the directory sees
   an unexplained `.cs` file, and the section whose job is "read the directory before querying it"
   is where that file gets explained.

### 4.3 `references/commands.md` — one preamble

The reference opens `kronikol query <command> <report> [args]` (line 3) and spells that prefix out
in 10 of its 380 lines (172-174, 192, 328-332). **Do not rewrite those ten.** They are correct for
the installed tool, and a find-and-replace would double the reference's width for no gain. Add a
two-line note under the line-3 invocation instead:

> Both invocations take the same commands, flags and addresses: `kronikol query <command> <report>`
> with the tool installed, or `dotnet run query.cs -- <command> <report>` from a reports directory
> without installing anything. Every example below is written the first way; the second works
> identically.

### 4.4 Both copies, and the `scripts/` directory

Every edit above lands in `templates/skills/kronikol-test-debugging/` **and**
`.claude/skills/kronikol-test-debugging/`, which M0's sync test (§6.3) then holds together. The
`scripts/` directory is deleted from both, and drops out of `InitAgentsCommand.cs:80`'s file list —
after M4 the skill ships two files, not three.

### 4.5 Held by tests

`SkillDriftTests` already holds the skill to the tool's own tables — every verb demonstrated, every
flag documented, every documented flag legal for its verb, every banner explained (14 cases). Those
must stay green across the move, unchanged, which is the regression signal that §4.1's claim is
true. §6.4's new `InstallAdviceTests` extends the same idiom to the new invocation form: any
`dotnet run query.cs --` line in either copy must name a verb `QueryCommand.Verbs` knows, with flags
`FlagsByVerb` accepts.

---

## 5. The fallback sentence, rewritten

Today ([SKILL.md:29-31](templates/skills/kronikol-test-debugging/SKILL.md#L29-L31)):

> If the tool is genuinely unavailable, use `scripts/query.py` in this skill — it degrades to a
> smaller set of commands, not to reading the file.

Replacement, which states the *reason* the tool is absent rather than implying a permissions
problem it almost never is (`dotnet tool install -g` writes to `~/.dotnet/tools`; no admin, no sudo):

> **No install needed.** Every reports directory carries a `query.cs` beside the report. `dotnet run
> query.cs -- summary .` runs the same engine as `kronikol query`, off the NuGet cache the test run
> already populated — no install, no network. First run builds in a few seconds; after that it is
> instant.
>
> Install the tool when you want `kronikol` on PATH across many repositories. Reach for `query.cs`
> when you are reading a report on a machine that did not produce it, or when `kronikol` is not
> found. Both need the .NET 10 SDK.

The last clause is the honest caveat from §1: this fixes *not installed*, *no network*, *not on
PATH* and *no admin* — it does not fix *no .NET 10 SDK*. Since the tool needs the .NET 10 runtime
anyway, that machine has no option under any design, which is what makes deleting the Python script
defensible rather than a regression.

---

## 6. Tests, red first

Per the repo's TDD rule, each milestone below writes the failing test before the change.

**New:**

1. `QueryScriptTests` — the emitted `query.cs` exists in the reports directory, pins
   `KronikolVersion` exactly (not a range), and compiles. Assert on the emitted text, not a
   substring of the whole report — the bare-substring trap this repo has hit before.
2. `QueryScriptEndToEndTests` — actually `dotnet run query.cs -- summary <dir>` against a fixture
   report and assert the output equals `kronikol query summary` byte for byte. Skips when no SDK 10
   is present, and **the skip must be visible** (the `PythonProbe` lesson: a silently skipped smoke
   test is indistinguishable from one that never worked).
3. `SkillCopySyncTests` — `.claude/skills/kronikol-test-debugging/` byte-identical to
   `templates/skills/kronikol-test-debugging/`. **This is the drift bug**, see §7.
4. `InstallAdviceTests` — no shipped surface in §3's table still names `query.py`, and every one
   that shows a `dotnet run query.cs` line uses a verb `QueryCommand.Verbs` knows. This is the
   `SkillDriftTests` idiom extended to the new command form.

**Changed:** `SkillDriftTests` (all 14 cases) must keep passing against the moved types — they read
`QueryCommand.Verbs`, `FlagsByVerb`, `JsonCommands` through IVT, so this is the regression signal
that the move was behaviour-preserving. `InitAgentsCommandTests:29` file list.
`Packaging/ProjectAssetTrackingTests` for the new `Kronikol` content.

**Deleted:** `FallbackScriptTests`, `PythonProbe`.

---

## 7. The bug this turned up, fixable now and independently

`templates/skills/.../query.py` and `.claude/skills/.../query.py` have **drifted**. Commit
`a25f02a4` ("Release 3.2.0") added the `failureCause` → `cause:` line to the repo's own copy and
never touched the shipped one:

```
163a164,167
>         if scenario.get("failureCause"):
>             lines.append("  cause: " + one_line(scenario["failureCause"], 120))
```

`SKILL.md` and `references/commands.md` are identical between the two copies; only the script
slipped. Nothing guards it: `FallbackScriptTests.cs:31`, `SkillDriftTests.cs:33` and
`InitAgentsCommandTests.cs:29` all point at `templates/`, and no test compares the trees. So users'
fallback is a release behind and silently drops a field `Failures.md` has been emitting since 3.1.0.

M0 below fixes it in its own commit, because it is a real user-facing bug whose fix should not wait
on a green light for the rest of this plan, and because the sync test it adds is what stops the same
class of drift once `query.cs` is the thing being shipped.

---

## 8. Milestones

| M | Work | Version |
|---|---|---|
| **M0** | Sync the two `query.py` copies; add `SkillCopySyncTests` (§6.3). Independently shippable. | patch — 3.3.1 |
| **M1** | Move the engine into `Kronikol` under `#if NET10_0_OR_GREATER`; make `QueryCommand` public; IVT for `Kronikol.Tool`; `Commands.cs` calls the new home. `SkillDriftTests` + `QueryCommandTests` + `QueryJsonTests` + `QueryStreamingTests` green with no edits = the move was clean. | — |
| **M2** | `WriteQueryScript` option and the emitted `query.cs`; tests §6.1, §6.2. | — |
| **M3** | Rewrite the eight non-skill surfaces in §3, then the skill itself per §4 — `SKILL.md` ×3 edits, `references/commands.md` preamble, both copies; test §6.4. | — |
| **M4** | Delete `query.py` ×2, `FallbackScriptTests`, `PythonProbe`; drop it from `InitAgentsCommand.cs:80`. | — |
| **M5** | Wiki (`../Kronikol.wiki`), README, changelog. Record the report-output change in the Kronikol4J divergence ledger — a new sidecar file is new output. | — |
| **M6** | Full suite, then version bump across **all** packages, changelog stating *minor, because `Kronikol.Query.QueryCommand` is new public surface and `WriteQueryScript` is a new option*, tag `v3.4.0`, push. | **minor — 3.4.0** |

M1–M6 ship as one release.

**Bump rationale against the repo rule.** New public type + new configuration option + new generated
output = MINOR, even though no behaviour changes for anyone who does not call them. Per CLAUDE.md a
new option is minor "even when its default preserves today's behaviour" — and here the default does
*not*, since `WriteQueryScript` defaults to `true` and adds a file. Not major: nothing is removed or
renamed, and `kronikol query`'s surface is unchanged.

---

## 9. Assumption ledger

| # | Claim | Status |
|---|---|---|
| A1 | File-based apps run on SDK 10 | **measured** (§1) |
| A2 | `#:package` resolves with all sources cleared | **measured** (§1) |
| A3 | Cache holds every TFM asset, so net8.0 consumers get a net10.0 fallback | **measured** (§1) |
| A4 | Cold 2.9 s / warm 0.25 s | **measured**, single machine, warm SDK. Not measured on a cold CI runner. |
| A5 | SDK 9 cannot run it | **measured** (§1) |
| A6 | Query engine depends only on `Kronikol.Reports` | **measured** by grep over all 20 files |
| A7 | Template chain reaches `Kronikol` | **measured** (`BDDfy.xUnit3` → `xUnit3` → `Kronikol`) |
| A8 | The two `query.py` copies have drifted | **measured** (§6) |
| A9 | The engine compiles unchanged inside `Kronikol` under `#if NET10_0_OR_GREATER` | **assumed** — M1 is where this is found out. Risk: a name collision between `Kronikol.Query` internals and existing `Kronikol` internals. Cheap to discover, cheap to fix. |
| A10 | `dotnet run query.cs` output is byte-identical to `kronikol query` | **assumed** — §6.2 is the test that proves or breaks it. Most likely divergence: stdout encoding, which `Program.cs` sets explicitly for the tool and the file-based app does not. **If it diverges, the emitted `query.cs` sets `Console.OutputEncoding` the same way** — the tool's own comment explains why it matters (byte budget counted in UTF-8, `·`/`›` in addresses). |
| A11 | A cold CI runner has `Kronikol` in `~/.nuget/packages` by the time the report is read | **assumed** for the common case, **false** for a job that downloads the report as an artifact into a fresh container. That job needs network — same as today, and it fails loudly (`NU1101`), which is the point of pinning the exact version. |

**A10 is the one that can make M2 bigger than it looks.** Everything else is either measured or
cheap.

---

## 10. What this plan deliberately does not do

- **No `Kronikol.Query` package.** §1.1 — the dependency direction kills the offline property.
- **No self-contained binary in the release.** Needs a download and a per-RID matrix; `query.cs`
  needs neither.
- **No local tool manifest** (`.config/dotnet-tools.json` + `dotnet tool restore`). Viable, zero
  refactor, exact parity — but `kronikol.tool` is not in the cache from a normal test run (verified:
  it is in this machine's cache only because this repo builds it), so it needs a network fetch.
  Strictly worse than `query.cs` on the axis that matters. Worth revisiting only if A9 or A10 make
  M1/M2 expensive.
- **No Node or PowerShell fallback.** Same inverted premise as Python: a runtime rarer than the one
  it backs up.
- **No extra pre-rendered digests.** A good idea on its own — `Failures.md` proves the shape — but
  it answers fixed questions, not `grep this value` or `fetch that body by path`. Separate plan.
