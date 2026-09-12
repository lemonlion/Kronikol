# Kronikol Project Templates

Project templates for creating test projects pre-configured with [Kronikol](https://github.com/lemonlion/Kronikol) dependency tracking and automatic report generation.

## Installation

```bash
dotnet new install Kronikol.Templates
```

## Available Templates

| Template | Short Name | Description |
|----------|-----------|-------------|
| TTD Component Tests (xUnit v3) | `kronikol-xunit3` | xUnit v3 test project with TTD |
| TTD Component Tests (xUnit v2) | `kronikol-xunit2` | xUnit v2 test project with TTD |
| TTD Component Tests (TUnit) | `kronikol-tunit` | TUnit test project with TTD |
| TTD Component Tests (NUnit 4) | `kronikol-nunit4` | NUnit 4 test project with TTD |
| TTD Component Tests (MSTest) | `kronikol-mstest` | MSTest test project with TTD |
| TTD Component Tests (LightBDD + xUnit v3) | `kronikol-lightbdd-xunit3` | LightBDD with xUnit v3 and TTD |
| TTD Component Tests (LightBDD + xUnit v2) | `kronikol-lightbdd-xunit2` | LightBDD with xUnit v2 and TTD |
| TTD Component Tests (LightBDD + TUnit) | `kronikol-lightbdd-tunit` | LightBDD with TUnit and TTD |
| TTD Component Tests (BDDfy + xUnit v3) | `kronikol-bddfy-xunit3` | BDDfy with xUnit v3 and TTD |
| TTD Component Tests (ReqNRoll + xUnit v3) | `kronikol-reqnroll-xunit3` | ReqNRoll (Gherkin) with xUnit v3 and TTD |
| TTD Component Tests (ReqNRoll + xUnit v2) | `kronikol-reqnroll-xunit2` | ReqNRoll (Gherkin) with xUnit v2 and TTD |
| TTD Component Tests (ReqNRoll + TUnit) | `kronikol-reqnroll-tunit` | ReqNRoll (Gherkin) with TUnit and TTD |

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
| `.claude/skills/kronikol-test-debugging/` | The skill: the command ladder, the recipes, the full flag reference, and a Python fallback for machines without the CLI |
| `CLAUDE.md`, `AGENTS.md` | Byte-identical instruction files pointing at the reports directory and the skill — two names because Claude Code reads one and Codex, Cursor and Copilot read the other |

They need no setup. An agent that opens the project reads them and knows to run `kronikol query` rather
than open a report that does not fit in its context. To add the same thing to a repository that predates
the templates, install `Kronikol.Tool` and run `kronikol init-agents .`.

## After Scaffolding

1. Add a `<ProjectReference>` to your API project in the generated `.csproj`
2. Remove the placeholder `Program.cs` file
3. Update `BaseFixture.cs` to configure services specific to your API
4. Run tests: `dotnet test`
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
