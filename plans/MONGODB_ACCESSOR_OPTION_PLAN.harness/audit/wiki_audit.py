# The audit's wiki edits (plan section 12, F14, F15 and F17, and the notes 4.18.2 makes true), applied after publication
# by exact-string replacement:
#   python wiki_audit.py <wiki checkout>
# Each anchor must appear exactly once, or already be replaced (a second run changes nothing). Measured on wiki 85abe19.
import pathlib, sys

wiki = pathlib.Path(sys.argv[1])
OPTIONAL = "| `HttpContextAccessor` | `IHttpContextAccessor?` | `null` | Optional — enables dual-resolution of test identity from HTTP headers"

EDITS = {
    "HTTP-Tracking-Setup.md": [
        ("2. **Fall back to the delegate** — If no `HttpContext` is available (e.g. the code is running directly on the test thread), use `CurrentTestInfoFetcher` as before",
         "2. **Fall back to the delegate** — If no `HttpContext` is available, or its request carries no tracking headers (e.g. the code is running directly on the test thread), use `CurrentTestInfoFetcher` as before"),
        ("Every extension options class has an `HttpContextAccessor` property: most since v2.26.3,",
         "Every extension options class with a `CurrentTestInfoFetcher` has an `HttpContextAccessor` property (the TcpTap, ProxyTap and OTLP taps take neither): most since v2.26.3,"),
        ("- **All other extensions:** Set `HttpContextAccessor` on the options class — the tracker reads it via `?? options.HttpContextAccessor`\n",
         "- **All other extensions:** Set `HttpContextAccessor` on the options class — the tracker reads it via `?? options.HttpContextAccessor` (BigQuery's and Pub/Sub's trackers since 4.18.2)\n"),
        ("The BigQuery, Bigtable, Spanner, EF Core, PubSub, Kafka and MongoDB DI registrations use the container's accessor when the options carry none;",
         "The BigQuery, Bigtable, Spanner, EF Core, PubSub, Kafka and MongoDB DI registrations use the options' accessor first and the container's when the options carry none (BigQuery's and Pub/Sub's tracker registrations since 4.18.2);"),
    ],
    "Diagnostics-and-Debugging.md": [
        ("**Fix:** every extension options class has an `HttpContextAccessor` property (most since v2.26.3,",
         "**Fix:** every extension options class with a `CurrentTestInfoFetcher` has an `HttpContextAccessor` property (most since v2.26.3,"),
    ],
    "Integration-MassTransit-Extension.md": [
        ("Optional — enables dual-resolution of test identity from HTTP headers. Auto-resolved by DI extensions (v2.26.3+). See ",
         "Optional — enables dual-resolution of test identity from HTTP headers. The package has no DI registration, so nothing "
         "resolves it for you: in a host, set it to the host's own accessor where you configure the bus. A sent or published "
         "message carries the scenario its send is recorded under. See "),
    ],
    "Integration-MediatR-Extension.md": [
        ("Pass the host's own (`sp.GetService<IHttpContextAccessor>()`): ASP.NET Core fills an accessor only in a host that "
         "registers one (`AddHttpContextAccessor()`, which `TrackDependenciesForDiagrams` calls). `TrackMediatorForDiagrams` "
         "uses the container's when this is null |",
         "In a host, leave it null: `TrackMediatorForDiagrams` takes these options before the host's services are built, and "
         "uses the container's accessor, the host's own, when this is null. ASP.NET Core fills an accessor only in a host that "
         "registers one (`AddHttpContextAccessor()`, which `TrackDependenciesForDiagrams` calls). Set it to read the headers "
         "through another accessor |"),
    ],
    "Integration-PubSub-Extension.md": [
        (OPTIONAL + " |\n",
         OPTIONAL + ". `AddPubSubTestTracking` uses the container's when this is null, and an accessor passed to a constructor "
         "takes precedence. `PubSubTracker` reads it since 4.18.2. A published message carries the scenario its publish is "
         "recorded under |\n"),
    ],
    "Integration-BigQuery-Extension.md": [
        (OPTIONAL + " |\n",
         OPTIONAL + ". `AddBigQueryTestTracking` uses the container's when this is null, and an accessor passed to a "
         "constructor takes precedence. `BigQueryTracker` reads it since 4.18.2 |\n"),
    ],
    "Integration-AtlasDataApi-Extension.md": [
        ("        .AddHttpMessageHandler(() => new AtlasDataApiTrackingMessageHandler(trackingOptions));\n});\n```\n",
         "        .ConfigurePrimaryHttpMessageHandler(() => new AtlasDataApiTrackingMessageHandler(trackingOptions));\n});\n```\n\n"
         "The handler brings its own inner handler (an `HttpClientHandler`), so it goes in as the primary handler. "
         "`AddHttpMessageHandler` refuses a handler whose inner handler is already set: creating the client throws.\n"),
        (OPTIONAL + " |\n",
         OPTIONAL + ". `AddAtlasDataApiTestTracking` uses the container's when this is null (before 4.18.2 the container's "
         "came first), and an accessor passed to the constructor takes precedence |\n"),
    ],
    "Tracking-Dependencies.md": [
        ("    handler.InnerHandler = null!; // Let the pipeline builder set InnerHandler\n",
         "    // The handler leaves InnerHandler unset, so the pipeline builder sets it\n"),
    ],
}

SPANNER_ROW = ("| `HttpContextAccessor` | `IHttpContextAccessor?` | `null` | 4.11.0+. Resolves the scenario from the test-tracking "
               "request headers when a statement runs inside a host's request pipeline. Pass the host's own "
               "(`sp.GetService<IHttpContextAccessor>()`): ASP.NET Core fills an accessor only in a host that registers one "
               "(`AddHttpContextAccessor()`, which `TrackDependenciesForDiagrams` calls). `AddSpannerTestTracking` uses the "
               "container's when this is null, and an accessor passed to a constructor takes precedence |")


def spanner_edits(text):
    # The row goes after ResponseDetail's, the property before it in SpannerTrackingOptions.
    line = next(l for l in text.split("\n") if l.startswith("| `ResponseDetail` | `SpannerResponseDetail`"))
    return [(line + "\n", line + "\n" + SPANNER_ROW + "\n")]


changed = []
pages = dict(EDITS)
pages["Integration-Spanner-Extension.md"] = None
for page, edits in pages.items():
    path = wiki / page
    text = path.read_text(encoding="utf-8")
    original = text
    for old, new in (edits if edits is not None else spanner_edits(text)):
        if text.count(new) == 1 and old not in text.replace(new, ""):
            continue  # applied already
        assert text.count(old) == 1, f"{page}: anchor found {text.count(old)} times: {old[:80]!r}"
        text = text.replace(old, new)
    if text != original:
        path.write_text(text, encoding="utf-8", newline="\n")
        changed.append(page)
print("changed:", ", ".join(changed) or "nothing")
