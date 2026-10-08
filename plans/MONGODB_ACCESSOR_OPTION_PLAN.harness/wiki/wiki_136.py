# The plan's wiki edits (section 6.4, and R2's), applied after publication by exact-string replacement:
#   python wiki_136.py <wiki checkout> <R1 version> <R2 version>
# Each anchor must appear exactly once, or already be replaced (a second run changes nothing).
import pathlib, sys

wiki, R1, R2 = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
FIVE = "Kafka's, Bigtable's, Spanner's, MediatR's and EF Core's (`SqlTrackingInterceptorOptions`)"
HOST = ("Pass the host's own (`sp.GetService<IHttpContextAccessor>()`): ASP.NET Core fills an accessor only in a host "
        "that registers one (`AddHttpContextAccessor()`, which `TrackDependenciesForDiagrams` calls)")


def row(what, extra):
    return (f"| `HttpContextAccessor` | `IHttpContextAccessor?` | `null` | {R2}+. Resolves the scenario from the test-tracking "
            f"request headers when {what} inside a host's request pipeline. {HOST}. {extra} |\n")


def fetcher_row(old_tail, new_tail):
    return (old_tail, new_tail)


EDITS = {
    "Integration-MongoDB-Extension.md": [
        ("## Install\n\n```bash\ndotnet add package Kronikol.Extensions.MongoDB\n```\n",
         "## Install\n\nFor MongoDB.Driver 3.x:\n\n```bash\ndotnet add package Kronikol.Extensions.MongoDB\n```\n\n"
         "For MongoDB.Driver 2.x, `Kronikol.Extensions.MongoDB.V2` has the same namespace and API, built against the 2.x driver:\n\n"
         "```bash\ndotnet add package Kronikol.Extensions.MongoDB.V2\n```\n"),
        ("            Verbosity = MongoDbTrackingVerbosity.Detailed,\n            CurrentTestInfoFetcher = CurrentTestInfo.Fetcher\n"
         "        };\n\n        var settings = MongoClientSettings.FromConnectionString(\"mongodb://localhost:27017\");\n"
         "        settings.WithTestTracking(trackingOptions);\n\n        return new MongoClient(settings);\n    });\n});\n```\n",
         "            Verbosity = MongoDbTrackingVerbosity.Detailed,\n            CurrentTestInfoFetcher = CurrentTestInfo.Fetcher,\n"
         "            HttpContextAccessor = sp.GetService<IHttpContextAccessor>()\n"
         "        };\n\n        var settings = MongoClientSettings.FromConnectionString(\"mongodb://localhost:27017\");\n"
         "        settings.WithTestTracking(trackingOptions);\n\n        return new MongoClient(settings);\n    });\n});\n```\n\n"
         f"The host runs its commands in its own request pipeline, where TestServer does not carry the test's async context, so "
         f"`CurrentTestInfo.Fetcher` cannot name the scenario there. `HttpContextAccessor` ({R1}+) lets each command take the "
         f"scenario from the test-tracking headers of the request it serves. {HOST}. On earlier versions, build a "
         "`MongoDbTrackingSubscriber` with the accessor and subscribe it yourself, as Option B does: "
         "`new MongoDbTrackingSubscriber(trackingOptions, sp.GetService<IHttpContextAccessor>())`.\n"),
        ("The simplest approach — registers a `MongoDbTrackingSubscriber` singleton with `IHttpContextAccessor` auto-resolved from DI:\n\n"
         "```csharp\nbuilder.ConfigureTestServices(services =>\n{\n    services.AddMongoDbTestTracking(options =>\n    {\n"
         "        options.ServiceName = \"MongoDB\";\n        options.Verbosity = MongoDbTrackingVerbosity.Detailed;\n    });\n});\n```\n",
         f"`AddMongoDbTestTracking` registers a `MongoDbTrackingSubscriber` singleton, with the options' `HttpContextAccessor` "
         f"({R1}+) or else the container's. It records nothing until a client subscribes it, so the client's registration "
         "must do that:\n\n"
         "```csharp\nbuilder.ConfigureTestServices(services =>\n{\n    services.AddMongoDbTestTracking(options =>\n    {\n"
         "        options.ServiceName = \"MongoDB\";\n        options.Verbosity = MongoDbTrackingVerbosity.Detailed;\n    });\n\n"
         "    services.AddSingleton<IMongoClient>(sp =>\n    {\n"
         "        var subscriber = sp.GetRequiredService<MongoDbTrackingSubscriber>();\n"
         "        var settings = MongoClientSettings.FromConnectionString(\"mongodb://localhost:27017\");\n"
         "        var existing = settings.ClusterConfigurator;\n"
         "        settings.ClusterConfigurator = cb => { existing?.Invoke(cb); subscriber.Subscribe(cb); };\n"
         "        return new MongoClient(settings);\n    });\n});\n```\n\n"
         "Option C does the same in one place.\n"),
        ("| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Returns the current test's name and ID. **Required** — if `null`, commands are monitored but not logged |\n"
         "| `CurrentStepTypeFetcher` | `Func<string?>?` | `null` | Optional — returns the current BDD step type (Given/When/Then) |\n",
         "| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Returns the current test's name and ID. The request headers come first when `HttpContextAccessor` sees a request; a command that nothing names a scenario for is not recorded |\n"
         "| `CurrentStepTypeFetcher` | `Func<string?>?` | `null` | Not read by the MongoDB subscriber: the Setup and Action options act on the phase the framework adapter sets |\n"
         f"| `HttpContextAccessor` | `IHttpContextAccessor?` | `null` | {R1}+. Resolves the scenario from the test-tracking request headers when a command runs inside a host's request pipeline. {HOST}. An accessor passed to the `MongoDbTrackingSubscriber` constructor takes precedence, and `AddMongoDbTestTracking` uses the container's when this is null |\n"),
        ("> **v2.23.0+ Dual-Resolution:** `MongoDbTrackingSubscriber` accepts an optional `IHttpContextAccessor? httpContextAccessor` constructor parameter for resolving test identity from HTTP request headers when running inside the SUT's request pipeline.",
         f"> **Dual-Resolution:** the subscriber resolves test identity from the HTTP request headers when a command runs inside the SUT's request pipeline, through an `IHttpContextAccessor`: the one passed to the `MongoDbTrackingSubscriber` constructor (v2.23.0+), else `MongoDbTrackingOptions.HttpContextAccessor` ({R1}+). `AddMongoDbTestTracking` passes the options' accessor, else the container's."),
    ],
    "HTTP-Tracking-Setup.md": [
        ("**v2.26.3+:** All extension options classes now have an `HttpContextAccessor` property. Convenience methods,",
         f"Every extension options class has an `HttpContextAccessor` property: most since v2.26.3, MongoDB's since {R1}, and {FIVE} since {R2}. Convenience methods,"),
        ("> The constructor `httpContextAccessor` parameter still works and takes precedence over the options property.\n",
         "> The constructor `httpContextAccessor` parameter still works and takes precedence over the options property. "
         f"`KafkaTrackingOptions.HttpContextAccessor` is {R2}+; before it, pass the accessor to the constructor: `new KafkaTracker(options, accessor)`.\n"),
        ("**v2.26.3+:** All options classes now expose an `HttpContextAccessor` property, and convenience methods / DI extensions auto-resolve it from DI:",
         f"**v2.26.3+:** options classes expose an `HttpContextAccessor` property (MongoDB's from {R1}; {FIVE} from {R2}), and convenience methods / DI extensions auto-resolve it from DI:"),
        ("| v2.26.2 (ServiceBus DI extension); others via options property |",
         f"| v2.26.2 (ServiceBus DI extension); others via options property (Kafka's from {R2}) |"),
        ("| v2.26.2 via options property; MongoDB/BigQuery/Bigtable/Spanner/EF Core/PubSub/Kafka already auto-resolved |",
         f"| v2.26.2 via options property (MongoDB's from {R1}; Bigtable's, Spanner's and EF Core's from {R2}). The BigQuery, Bigtable, Spanner, EF Core, PubSub, Kafka and MongoDB DI registrations use the container's accessor when the options carry none; the subscriber `AddMongoDbTestTracking` registers records nothing until a client subscribes it ([[MongoDB|Integration-MongoDB-Extension]]) |"),
    ],
    "Diagnostics-and-Debugging.md": [
        ("**Fix (v2.26.3+):** All extension options classes now have an `HttpContextAccessor` property, and DI extensions",
         f"**Fix:** every extension options class has an `HttpContextAccessor` property (most since v2.26.3, MongoDB's since {R1}, and {FIVE} since {R2}), and DI extensions"),
    ],
    "Integration-Kafka-Extension.md": [
        ("| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Returns the current test's name and ID. **Required** — if `null`, messages are not logged |\n",
         "| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Returns the current test's name and ID. The request headers come first when `HttpContextAccessor` sees a request |\n"),
        ("| `ConsumeKeyExtractor` | `Func<string, string, string>?` | `null` | Optional custom key extractor for consume correlation |\n",
         "| `ConsumeKeyExtractor` | `Func<string, string, string>?` | `null` | Optional custom key extractor for consume correlation |\n"
         + row("a message is produced or consumed",
               "An accessor passed to the `KafkaTracker` constructor or to `KafkaTrackingInterceptor` takes precedence, and the `Add*TestTracking` registrations use the container's when this is null. With `PropagateTestIdentity`, a produced message carries the scenario its produce is recorded under")),
    ],
    "Integration-Bigtable-Extension.md": [
        ("| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Required: provides test context for log correlation |\n"
         "| `CurrentStepTypeFetcher` | `Func<string?>?` | `null` | Optional — returns the current BDD step type |\n",
         "| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Provides test context for log correlation. The request headers come first when `HttpContextAccessor` sees a request |\n"
         "| `CurrentStepTypeFetcher` | `Func<string?>?` | `null` | Optional — returns the current BDD step type |\n"),
        ("| `TrackDuringAction` | `bool` | `true` | When `false`, tracking is suppressed during Action |\n\n---\n",
         "| `TrackDuringAction` | `bool` | `true` | When `false`, tracking is suppressed during Action |\n"
         + row("a call is tracked", "An accessor passed to the `BigtableTracker` constructor takes precedence, and `AddBigtableTestTracking` uses the container's when this is null")
         + "\n---\n"),
    ],
    "Integration-Spanner-Extension.md": [
        ("| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Required: provides test context for log correlation |\n",
         "| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Provides test context for log correlation. The request headers come first when `HttpContextAccessor` sees a request |\n"),
        ("For **non-WebApplicationFactory** scenarios (e.g. direct test usage), the simpler form without `IHttpContextAccessor` works:\n",
         f"For **non-WebApplicationFactory** scenarios (e.g. direct test usage), the simpler form without `IHttpContextAccessor` works. "
         f"From {R2} it also reads `SpannerTrackingOptions.HttpContextAccessor`, as does `connection.WithTestTracking(options)`:\n"),
    ],
    "Integration-MediatR-Extension.md": [
        ("| `TrackDuringAction` | `bool` | `true` | When `false`, tracking is suppressed during Action. See [[Phase-Aware Tracking]] |\n\n> **v2.23.0+ Auto-Resolution:**",
         "| `TrackDuringAction` | `bool` | `true` | When `false`, tracking is suppressed during Action. See [[Phase-Aware Tracking]] |\n"
         + row("a request or notification is sent", "`TrackMediatorForDiagrams` uses the container's when this is null")
         + "\n> **v2.23.0+ Auto-Resolution:**"),
        ("> **v2.23.0+ Auto-Resolution:** `TrackMediatorForDiagrams` automatically resolves `IHttpContextAccessor` from DI (if available) and passes it to the underlying `TrackingProxy<IMediator>`.",
         f"> **v2.23.0+ Auto-Resolution:** `TrackMediatorForDiagrams` passes `MediatorTrackingOptions.HttpContextAccessor` ({R2}+), or else the `IHttpContextAccessor` it resolves from DI (if available), to the underlying `TrackingProxy<IMediator>`."),
    ],
    "Integration-EF-Core-Relational-Extension.md": [
        ("| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Returns the current test's name and ID. **Required** — if `null`, commands are executed but not logged |\n",
         "| `CurrentTestInfoFetcher` | `Func<(string Name, string Id)>?` | `null` | Returns the current test's name and ID. The request headers come first when `HttpContextAccessor` sees a request |\n"),
        ("| `ResponseDetail` | `SqlResponseDetail` | *(follows verbosity)* | Level of detail for response content: `RowCountOnly`, `RowCountAndColumns`, or `FullRows`; unset = `FullRows` at Raw/Detailed, `RowCountAndColumns` at Summarised |\n",
         "| `ResponseDetail` | `SqlResponseDetail` | *(follows verbosity)* | Level of detail for response content: `RowCountOnly`, `RowCountAndColumns`, or `FullRows`; unset = `FullRows` at Raw/Detailed, `RowCountAndColumns` at Summarised |\n"
         + row("a command runs", "An accessor passed to the `SqlTrackingInterceptor` constructor takes precedence, `AddSqlTestTracking` uses the container's when this is null, and `WithTestInfoFrom` copies the HTTP options' when this is null. `WithSqlTestTracking(options)` reads it too")),
        ("Adds a `SqlTrackingInterceptor` to the `DbContextOptionsBuilder`. This is equivalent to calling `builder.AddInterceptors(new SqlTrackingInterceptor(options))`.\n",
         "Adds a `SqlTrackingInterceptor` to the `DbContextOptionsBuilder`. This is equivalent to calling `builder.AddInterceptors(new SqlTrackingInterceptor(options))`. "
         f"In a host, set `options.HttpContextAccessor` ({R2}+) to the host's accessor, so that a command run while the host serves a request lands in the scenario the request names.\n"),
        ("This copies three properties from the HTTP options:\n- `CurrentTestInfoFetcher` — the test name/ID delegate\n"
         "- `CurrentStepTypeFetcher` — the BDD step type delegate (Given/When/Then)\n- `CallerName` — the calling service display name\n",
         "This copies three properties from the HTTP options:\n- `CurrentTestInfoFetcher` — the test name/ID delegate\n"
         "- `CurrentStepTypeFetcher` — the BDD step type delegate (Given/When/Then)\n- `CallerName` — the calling service display name\n\n"
         f"From {R2} it also copies `HttpContextAccessor` when the SQL options carry none.\n"),
        ("**Manual fix (for non-DI usage or pre-v2.5.0):** If you're constructing the interceptor manually without DI, pass an `IHttpContextAccessor` to the constructor:\n",
         f"**Manual fix (for non-DI usage or pre-v2.5.0):** If you're constructing the interceptor manually without DI, set `options.HttpContextAccessor` ({R2}+), or pass an `IHttpContextAccessor` to the constructor:\n"),
    ],
}

changed = []
for page, edits in EDITS.items():
    path = wiki / page
    text = path.read_text(encoding="utf-8")
    original = text
    for old, new in edits:
        if text.count(new) == 1 and old not in text.replace(new, ""):
            continue  # applied already
        assert text.count(old) == 1, f"{page}: anchor found {text.count(old)} times: {old[:80]!r}"
        text = text.replace(old, new)
    if text != original:
        path.write_text(text, encoding="utf-8", newline="\n")
        changed.append(page)
print("changed:", ", ".join(changed) or "nothing")
