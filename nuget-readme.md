# Kronikol

Effortlessly autogenerate **PlantUML sequence diagrams** from your component and acceptance tests. Tracks interactions between your test caller, your Service Under Test (SUT), and its dependencies — including HTTP calls, Azure Cosmos DB operations, SQL queries (via EF Core), Redis commands, events/messages, and arbitrary method calls — then converts them into diagrams embedded in searchable HTML reports and structured data files. Diagrams render client-side in the browser (on Web Workers), and payload notes flip between JSON and YAML on hover.

## Example Output

![Example sequence diagram](https://github.com/user-attachments/assets/43d48a00-ba37-4951-945c-dd75de64c2bb)

Each test that uses tracked dependencies automatically produces a sequence diagram showing the full request/response flow between services.

## How It Works

1. **Intercept** — Dedicated tracking mechanisms intercept each type of dependency: `TestTrackingMessageHandler` for HTTP, `CosmosTrackingMessageHandler` for Cosmos DB, `SqlTrackingInterceptor` for EF Core SQL, `RedisTrackingDatabase` for Redis, `TrackingProxy<T>` for arbitrary interfaces, and `MessageTracker` for events/messages.
2. **Collect** — All logged entries are held in the static `RequestResponseLogger`, capturing operation details, service names, and trace IDs.
3. **Generate** — At the end of the test run, `PlantUmlCreator` groups logs by test ID and converts them into sequence diagram code.
4. **Report** — `ReportGenerator` combines the diagrams with test metadata to produce HTML reports and structured data files.

## Quick Start

```
dotnet add package Kronikol.xUnit3
```

See the [Quick Start guide](https://github.com/lemonlion/Kronikol/wiki/Quick-Start-(xUnit)) for full setup instructions.

## Supported Frameworks

| Framework | Package |
|---|---|
| Core library | `Kronikol` |
| xUnit v3 | `Kronikol.xUnit3` |
| xUnit v2 | `Kronikol.xUnit2` |
| NUnit v4 | `Kronikol.NUnit4` |
| MSTest v3 | `Kronikol.MSTest` |
| TUnit | `Kronikol.TUnit` |
| BDDfy | `Kronikol.BDDfy.xUnit3` |
| LightBDD | `Kronikol.LightBDD.xUnit3` / `.xUnit2` / `.TUnit` |
| ReqNRoll | `Kronikol.ReqNRoll.xUnit3` / `.xUnit2` / `.TUnit` |

### Extensions

| Extension | Package |
|---|---|
| Azure Cosmos DB | `Kronikol.Extensions.CosmosDB` |
| EF Core (Relational) | `Kronikol.Extensions.EfCore.Relational` |
| Redis | `Kronikol.Extensions.Redis` |
| Local PlantUML (IKVM) | `Kronikol.PlantUml.Ikvm` |
| Proxy tap (out-of-process capture for uninstrumentable backends) | `Kronikol.Extensions.ProxyTap` |
| OTLP tap + export (OTel spans in, Kronikol captures out as spans) | `Kronikol.Extensions.Otlp` |
| Playwright (browser-driven E2E identity) | `Kronikol.Playwright` |
| CLI (`kronikol merge`, `kronikol ingest`, `kronikol query`, `kronikol export`) | `Kronikol.Tool` |

## Use Cases

- **Debugging failed tests** — see the exact interaction that returned an unexpected result, and interrogate the run with `kronikol query` instead of reading the raw report files
- **Living documentation** — HTML reports and data files that stay in sync with your tests
- **AI-assisted analysis** — feed deterministic PlantUML to AI tools for accurate reasoning
- **PR reviews** — sequence diagrams make interaction changes immediately visible
- **Onboarding** — new team members can browse reports to understand service interactions
- **CI integration** — surface results in GitHub Actions / Azure DevOps job summaries

## Query the report from the terminal

On a real suite `TestRunReport.json` runs to megabytes; the `Kronikol.Tool` CLI answers questions about it without loading it:

```bash
kronikol query summary  ./Reports              # the run, its failures, the slowest scenarios
kronikol query failures ./Reports              # why each one failed, with assertion messages
kronikol query trace    ./Reports 4bf92f3577b34da6  # follow one W3C trace across scenarios, in order
kronikol query grep     ./Reports "4173" --values   # where a wrong value entered the system
```

## Documentation

For full documentation, see the **[Wiki](https://github.com/lemonlion/Kronikol/wiki)**.

Key pages:

- [Quick Start (xUnit)](https://github.com/lemonlion/Kronikol/wiki/Quick-Start-(xUnit))
- [Framework Integration Guides](https://github.com/lemonlion/Kronikol/wiki/Framework-Integration-Guides)
- [HTTP Tracking Setup](https://github.com/lemonlion/Kronikol/wiki/HTTP-Tracking-Setup)
- [Diagram Customisation](https://github.com/lemonlion/Kronikol/wiki/Diagram-Customisation)
- [Report Configuration](https://github.com/lemonlion/Kronikol/wiki/Report-Configuration)
- [Querying Reports](https://github.com/lemonlion/Kronikol/wiki/Querying-Reports)
- [API Reference](https://github.com/lemonlion/Kronikol/wiki/API-Reference)
- [Example Project](https://github.com/lemonlion/Kronikol/wiki/Example-Project)
