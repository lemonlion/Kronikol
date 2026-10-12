# Harness: KAFKA_INTERCEPTION_AND_TRACE_PLAN (#135)

Measured on 2026-10-07 at 4.9.0 (`417c8e58`) on Windows 11, .NET 8.0.27 and 10.0.11, Confluent.Kafka 2.15.1,
Lib.Harmony 2.4.2, with no Kafka broker (every probe builds real clients against `localhost:9092`, which needs none).
Each probe is a console app outside the solution. The `s0/shipped` and `s0/kronikol` probes reference the
**published** 4.9.0 packages from nuget.org; the others replicate `KafkaBuildInterceptor`'s mechanism verbatim
(`src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs:123-153`) around the published
`Kronikol.Extensions.Kafka` 4.9.0.

## Files

| Path | What it shows |
|---|---|
| `s0/shipped/` | The shipped `KafkaBuildInterceptor` 4.9.0 end to end: a `DependentProducerBuilder` producer comes back untracked (C1); `ProducerBuilder<Null,string>` returns a `TrackingKafkaProducer<String,String>` whose `Produce` throws `EntryPointNotFoundException`, also after `Reset()` (F1); a produce logged under an ambient `Activity` carries no W3C ids (C2). `results.txt` is net8.0 and net10.0 |
| `s0/kronikol/` | Modes `pairs`, `tierup`, `attribution`, `dr-disabled` against the published packages (F1, F2, F4 to F7). `out-*-net8.txt` |
| `s0/harmony/probe-harmony*` | The mechanism behind F1: the closed `<string,string>` replacement runs for other reference-type pairs (1), a no-op postfix already breaks them (2), the last enabled pair wins (3), patching `DependentProducerBuilder` the same way breaks it too (4), unpatching never restores (6) |
| `prototype/` | The dispatching prefix of section 4.1: one non-generic `Prefix(object __instance, ref object __result)` that builds `Producer<K,V>` through the internal constructor for the builder's runtime type and returns false. Correct types for every pair, owned and dependent |
| `tiermit/` | F2's mitigations on a plain generic class (no Kafka): `PrepareMethod`, warming before the patch, re-patching, an enable-time warm-up, `DOTNET_TC_QuickJit=0`, `DOTNET_TieredCompilation=0`, and the MSBuild property (`probe-tiermit-quickjit-off.csproj`). `results.txt` |
| `research/` | The three research notes the plan cites as `research-*.md §n`: Confluent internals and Harmony (`research-confluent.md`), Kronikol's readers of trace ids and content (`research-kronikol.md`), tests, docs, Kronikol4J and plan conventions (`research-tests-docs.md`) |

## Re-running

```sh
# any probe directory (net8.0 and net10.0 both build)
dotnet run -c Release -f net10.0                      # s0/shipped, prototype, s0/harmony/*
dotnet run -c Release -f net8.0 -- pairs              # s0/kronikol: pairs | tierup | attribution | dr-disabled
dotnet run -c Release -f net8.0 -- M0                 # tiermit: M0 .. M5
DOTNET_TC_QuickJit=0 dotnet bin/Release/net8.0/probe-tiermit.dll M0
```

The tier-up probes are timing-dependent: the first bypass came after 116 to 151 builds of the shipped package and
after 201 to 601 calls in the generic probes. No run with tiering on went without one. The audit (below) measured
shorter windows on 4.14.6: 47 to 251 builds in a busy loop and about 30 to 40 with pauses between builds.

## The acceptance it serves

R0's facts (plan section 5) are the red-first versions of `s0/`: each must fail on the release published before R0
for the reason its probe shows. The `prototype/` is the executable spec of section 4.1's mechanism; `tiermit/results.txt` is the evidence for
Q1 and for the remedy the wiki gives.

## The audit (2026-10-12, `audit/`)

The plan was audited on 2026-10-12 against 4.14.6 (`d59843fa`) and checked against 4.14.9 (`5b2c3a32`); plan section
11's last entry lists what changed. Nothing in the plan had been built. The notes:

| Path | What it holds |
|---|---|
| `audit/A1-citations.md` | Every citation and claim in the plan re-checked at 4.14.6, with the lines that moved |
| `audit/A2-docs.md` | The wiki, Kronikol4J, roadmap and CI claims re-checked (wiki at `8a57576`) |
| `audit/test-kafka.txt`, `test-bi.txt`, `test-timing.txt` | Baseline at 4.14.6: `Kronikol.Tests.Kafka` 139 of 139 and `Kronikol.Tests.Kafka.BuildInterception` 17 of 17 pass (net10.0) |
| `audit/B/B-results.md` | The probes above re-run on the published 4.14.6 packages (Confluent.Kafka 2.16.0, Lib.Harmony 2.4.2, and 2.4.3 in the `*-h243` folders), and experiments E1 to E8 on the proposed design: exception wrapping (E1), code shared by value-type arguments (E2), concurrent enabling (E3), the disable path (E4), tiering detection and a `buildTransitive` props (E5), Confluent's delivery-reports path (E6), the latest Confluent (E7), duplicate trackers (E8) |
| `audit/B/exp/` | The experiments' console app: `dotnet run -c Release -f net8.0 -- <mode>`, the modes listed at the top of `Program.cs`; outputs in `out/`, named by mode, Harmony version and framework |
| `audit/B/e5/` | The tiering probes (`tierdetect-*`, `tierdetect-parsing.txt`, `tierdetect-results.txt`) and a probe package carrying a `buildTransitive` props (`pkg/`), with consumers that reference it directly, transitively, under xunit.v3 and VSTest, and with an override (`props-results.txt`). The package is not kept: `dotnet pack` `pkg/` into `feed/` before running the consumers |
| `audit/B/s0-*`, `tiermit/`, `prototype/`, `recorded/` | The plan's probes as re-run, and the 4.9.0 outputs they were compared with |
| `audit/C/C-review.md` | An adversarial review of sections 4.2 to 4.6 and 5 (H4 to H14) |
| `audit/C/probe/`, `probe-output.txt` | Probes P1 to P8 on 4.14.6 (attribution, internal flow, R2's span lifetime, `flow` nesting). The project references `lib/Kronikol.dll` and `lib/Kronikol.Extensions.Kafka.dll`, which are not kept: build both from `d59843fa` in Release and copy them into `lib/` |
