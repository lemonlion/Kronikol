# How each tracker names its two parties (sweep of 2026-10-26, at 44bcc62a)

A read-only sweep of every capturer in `src/`, done by a subagent and spot-checked by hand where the plan relies on a
line (the HTTP handler, the defaults, the comparisons). Paths: `K/` is `src/Kronikol/`, `X.Name/` is
`src/Kronikol.Extensions.Name/`.

**The rule everywhere:** the caller's name is a fixed option of the tracker (default `"Caller"`,
`K/Constants/TrackingDefaults.cs:12`). Nothing works out who is calling when a call happens. The callee's name is fixed
too, except for `MessageTracker` (an argument per call), gRPC with `UseProtoServiceNameInDiagram`, the HTTP handler's
port fallback, the OTLP tap, and the direction swap on Kafka and MassTransit consumes.

| Tracker | Callee name, default | Caller name, default | Uri the record carries | What it knew and did not record |
|---|---|---|---|---|
| HTTP handler (`TestTrackingMessageHandler`, also inside the service through `TrackDependenciesForDiagrams`' builder filter) | `FixedNameForReceivingService` > `ClientNamesToServiceNames` > `PortsToServiceNames` > `localhost:{port}` (`K/Tracking/TestTrackingMessageHandler.cs:67-114`, port only at :239). The filter passes the client name (`K/Tracking/TrackingHttpMessageHandlerBuilderFilter.cs:22-23`) | `CallerName`, `"Caller"` (`TestTrackingMessageHandlerOptions.cs:26`), copied once (:51) | the request's, verbatim; no category | the client name (:48); the transport at the end of its chain, socket or not (:387-393) |
| `CreateTestTrackingClient<T>` | no client name passed (`K/WebApplicationFactoryExtensions.cs:27`, :46), so Fixed > ports > fallback | `"Caller"` | as above | `T`, the application's entry point; that the transport is TestServer's |
| `TestTrackingHttpClientFactory` | drops the client name (`K/TestTrackingHttpClientFactory.cs:15`) | `"Caller"` | as above | as above |
| Server side (`TestTrackingServerBridge`, `TestTrackingContextMiddleware`) | writes no record | reads only the test name and id headers (`K/Tracking/TestTrackingContextMiddleware.cs:29-33`) | none | the whole `HttpContext` |
| `MessageTracker` | per call (`destinationName`, or `consumerName` on consume, `K/Tracking/MessageTracker.cs:106`, :130, :326-330); `MessageTrackerOptions.ServiceName` (`"MessageBus"`) reaches only `ComponentName` (:91) | `"Caller"` (`MessageTrackerOptions.cs:21`); plays the broker on consume (:318) | the caller's `destinationUri`; category `MessageQueue` | whether the current request is its host's (:343-351) |
| `TrackingProxy<T>`, DispatchProxy, MediatR | `TrackingProxyOptions.ServiceName` (required); MediatR `"Application"` | `"Caller"`; DispatchProxy's shorthand cannot set it | `mock://<name>/<interface>/<method>` (`K/Tracking/TrackingProxy.cs:26`, :96): `mock` names a proxied interface, not a double | the implementation it wraps |
| SqlClient, Npgsql, MySqlConnector, Oracle, Sqlite, ClickHouse | `"SQL Server"`, `"PostgreSQL"`, `"MySQL"`, `"Oracle"`, `"SQLite"`, `"ClickHouse"` | `"Caller"` (`K/Sql/SqlTrackingOptionsBase.cs:14`) | `<scheme>://<host>/<db>`, the host made from the connection's DataSource (by `K/Sql/SqlEndpoint.cs` since 5.11.1; at 5.11.0 the DataSource went in as it was); the host dropped at Summarised and on every response | the connection string; the three listener trackers subscribe to every `DiagnosticListener` in the process (`X.SqlClient/SqlClientDiagnosticTracker.cs:25-28`, `X.Npgsql/NpgsqlDiagnosticTracker.cs:29-32`, `X.MySqlConnector/MySqlDiagnosticTracker.cs:25-28`), so a connection the test opens gets the service's caller name |
| EF Core (`SqlTrackingInterceptor`), Dapper | `"Database"` | `"Caller"`; `WithTestInfoFrom` copies the HTTP options' caller (`X.EfCore.Relational/SqlTrackingInterceptorOptionsExtensions.cs:24`) | `sql://...`; category hard-coded `SQL` whatever the provider | the `DbContext` |
| MongoDB (and V2) | `"MongoDB"` | `"Caller"` | `mongodb:///<db>/<coll>`, never a host | the command event's connection |
| Cosmos DB | `"CosmosDB"` | `"Caller"` | the endpoint's scheme, host and port at every verbosity | whether it is the emulator |
| Redis | `"Redis"` | `"Caller"` | the first endpoint at Raw only, else `redis://db<n>/` | the other endpoints |
| Kafka | `"Kafka"` | `"Caller"`, swapped on consume (`KafkaTracker.cs:385`) | `kafka:///<topic>` | the bootstrap servers |
| Service Bus, Event Hubs, Pub/Sub, SQS, SNS, EventBridge, Storage Queues | the product name | `"Caller"`, not swapped on receive | the queue or topic in the host position, or the endpoint at Raw only | the namespace or client config |
| S3, DynamoDB, Blob Storage, Cloud Storage, BigQuery, Atlas Data API, Elasticsearch, Spanner, Bigtable | the product name | `"Caller"` | the endpoint at Raw (Blob Storage and BigQuery's handler keep it at every verbosity) | the client config |
| gRPC | `"GrpcService"`, or per call the proto service (`UseProtoServiceNameInDiagram`) | `"Caller"` | `grpc:///<svc>/<method>`, no host | `context.Host`; that `factory.Server.CreateHandler()` is in memory |
| MassTransit | `"MassTransit"` | `"Caller"`, swapped on consume | the transport address at Raw | `SourceAddress` |
| ProxyTap | required, default `""` | required, default `""` | `ForwardBaseUri` + the path | the caller's socket |
| TcpTap, RedisTap, MongoTap | required; `"redis"`, `"mongo"` (lower case, unlike the in-process `"Redis"`, `"MongoDB"`) | required | per decoder | the caller's socket (`TcpTap.cs:360`) |
| OTLP tap | per span: `ServiceNameMap`, `peer.service`, `host:port`, host, system | per span: the span's `service.name`, default `"unknown-service"` (`OtlpTapOptions.cs:153`) | from the span; `CapturedBy = "span"` | resource attributes but `service.name` |
| Playwright | writes no record | stamps the caller-name header `"Browser"` (`src/Kronikol.Playwright/TestTrackingIdentity.cs:23`) | none | none |
| NDJSON ingest | `serviceName`, required, verbatim (`K/Ingestion/InteractionRecord.cs:46`) | `callerName`, required, verbatim (:49) | as supplied | as supplied |

**Seeding through the service's own client** (`factory.Services`): the record gets the caller name of that client's
tracker options, the service's own. Nothing on the path looks at who called; only `AttributionSource` differs
(`RequestHeader` for the service's calls made inside a request, `TestContext` or `Scope` for a call on the test's
thread), and `TestContext` also covers work that inherited the test's context (`K/Tracking/AttributionSource.cs:14`).

**Found on the way:**
- The HTTP fallback ignores the host (`TestTrackingMessageHandler.cs:69`); RUN in `probe-names/results.txt`.
- The caller-name header is written (the handler :237, gRPC, Playwright, ProxyTap, which writes the receiver's name)
  and read by nothing in `src/`.
- One party, several default names: `"Redis"` and `"redis"`, `"MongoDB"` and `"mongo"`, `"SQL Server"` (SqlClient)
  and `"Database"` (EF Core, Dapper); callers `"Caller"`, `"unknown-service"`, `"Browser"`, `""`.
- Consumes swap direction on Kafka, MassTransit and `MessageTracker.TrackConsumeEvent`, and do not on Service Bus,
  Event Hubs, Pub/Sub and SQS.
- `CapturedBy` is documented as `wire` for the taps (`K/Tracking/RequestResponseLog.cs:118-123`); only the OTLP mapper
  sets a value, and merge's inference covers the taps.
- `MessageTrackerOptions.ServiceName` is documented as the destination default and reaches no record.
- `CorrelationKeys` embed the service name (`K/Tracking/CorrelationKeys.cs:14-15`), so names that merge also merge
  correlation keys.
- Not verified from the repo (framework facts): a `WebApplicationFactory` client's base address is `http://localhost`,
  so an unnamed in-memory host would be `localhost:80`.
