# Kronikol Project Templates

**[See a live report](https://lemonlion.github.io/BreakfastProvider/)** from a sample service's test run.

Project templates for creating test projects pre-configured with [Kronikol](https://github.com/lemonlion/Kronikol) dependency tracking and automatic report generation.

## Installation

```bash
dotnet new install Kronikol.Templates
```

## Available Templates

| Template | Short Name | Description |
|----------|-----------|-------------|
| Kronikol Component Tests (xUnit v3) | `kronikol-xunit3` | xUnit v3 test project with Kronikol |
| Kronikol Component Tests (xUnit v2) | `kronikol-xunit2` | xUnit v2 test project with Kronikol |
| Kronikol Component Tests (TUnit) | `kronikol-tunit` | TUnit test project with Kronikol |
| Kronikol Component Tests (NUnit 4) | `kronikol-nunit4` | NUnit 4 test project with Kronikol |
| Kronikol Component Tests (MSTest) | `kronikol-mstest` | MSTest test project with Kronikol |
| Kronikol Component Tests (LightBDD + xUnit v3) | `kronikol-lightbdd-xunit3` | LightBDD with xUnit v3 and Kronikol |
| Kronikol Component Tests (LightBDD + xUnit v2) | `kronikol-lightbdd-xunit2` | LightBDD with xUnit v2 and Kronikol |
| Kronikol Component Tests (LightBDD + TUnit) | `kronikol-lightbdd-tunit` | LightBDD with TUnit and Kronikol |
| Kronikol Component Tests (BDDfy + xUnit v3) | `kronikol-bddfy-xunit3` | BDDfy with xUnit v3 and Kronikol |
| Kronikol Component Tests (ReqNRoll + xUnit v3) | `kronikol-reqnroll-xunit3` | ReqNRoll (Gherkin) with xUnit v3 and Kronikol |
| Kronikol Component Tests (ReqNRoll + xUnit v2) | `kronikol-reqnroll-xunit2` | ReqNRoll (Gherkin) with xUnit v2 and Kronikol |
| Kronikol Component Tests (ReqNRoll + TUnit) | `kronikol-reqnroll-tunit` | ReqNRoll (Gherkin) with TUnit and Kronikol |

## Usage

```bash
dotnet new kronikol-xunit3 --name MyService.Tests.Component \
  --service-name "Order Service" \
  --downstream-service "Payment Gateway" \
  --downstream-port 15060 \
  --framework net10.0
```

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `--name` | Current directory | Project name and root namespace |
| `--service-name` | MyApi | Name of your service (appears in diagrams) |
| `--downstream-service` | DownstreamService | Name of a downstream dependency |
| `--downstream-port` | 15050 | Port for downstream service HTTP fake |
| `--framework` | net10.0 | Target framework (net8.0/net9.0/net10.0) |

## What's Included

Alongside the test project itself, every template scaffolds the files that teach an AI agent how to debug
the runs it produces:

| Path | Purpose |
|---|---|
| `.claude/skills/kronikol-test-debugging/` | The skill: the command ladder, the recipes, the full flag reference, and a Python fallback for a machine without the .NET 10 SDK (with it, the `query.cs` every run writes beside its report needs nothing installed) |
| `CLAUDE.md`, `AGENTS.md` | Byte-identical instruction files pointing at the reports directory and the skill — two names because Claude Code reads one and Codex, Cursor and Copilot read the other |

They need no setup. An agent that opens the project reads them and knows to run `kronikol query`, or
`dotnet run --file query.cs` from the reports directory where it may not install the tool, rather than open
a report that does not fit in its context. To add the same thing to a repository that predates
the templates, install `Kronikol.Tool` and run `kronikol init-agents .`.

## GitHub Actions: link the report on the pull request

[`github-actions/kronikol-pr-report-link/`](https://github.com/lemonlion/Kronikol/tree/main/templates/github-actions/kronikol-pr-report-link) is a composite action, not
part of the `dotnet new` package. It keeps one comment on a pull request that links the Kronikol report
artifact from each workflow run, with the time it was uploaded and when it expires. Copy it into
`.github/actions/`. Its README has the workflow to call it from.

## GitHub Actions: keep cross-run history

[`github-actions/kronikol-history/`](https://github.com/lemonlion/Kronikol/tree/main/templates/github-actions/kronikol-history) is four composite actions, not
part of the `dotnet new` package. They keep the
[cross-run history](https://github.com/lemonlion/Kronikol/wiki/Cross-Run-History) ledger on a data branch of your
repository: `read` fetches it before the tests, so each report says what is new, `gate` can fail the job on what
is new, `save` uploads each run's fragment, and `record` folds the fragments into the ledger after every test job.
Copy the folder into `.github/actions/`, keeping its `.gitattributes`. Its README has the workflows to call it from.

## After Scaffolding

1. Add a `<ProjectReference>` to your API project in the generated `.csproj`
2. Remove the placeholder: delete `Program.cs` and the `PlaceholderApiFactory` class, and create the factory from your
   API's own entry point, `new WebApplicationFactory<YourApi.Program>()`. Until then the sample test runs against the
   placeholder, in a solution or not.
3. Update `BaseFixture.cs` (`Hooks/TestSetupHooks.cs` for ReqNRoll, `Infrastructure/BDDfyTestSetup.cs` for BDDfy) to
   configure services specific to your API
4. Run tests: `dotnet test`, or `dotnet run` for the TUnit templates, which run on Microsoft.Testing.Platform
5. Find reports in: `bin/Debug/{framework}/Reports/`

## Assertion Tracking (Beta — Opt-in)

Assertion tracking is available as a **beta** feature via the `Kronikol.AssertionTracking` package. It uses IL weaving to instrument `.Should()` calls at compile time, producing green/red assertion notes in your HTML reports. Because IL weaving can interfere with some build configurations, it is **not included by default** in the templates.

To opt in, add the package and assembly attribute manually:

```bash
dotnet add package Kronikol.AssertionTracking --prerelease
```

Then create an `AssertionTracking.cs` file in your test project:

```csharp
using Kronikol.Tracking;

[assembly: TrackAssertions]
```
