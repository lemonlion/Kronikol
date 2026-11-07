# Where each tracker's tests build it over a fake inner

Read at `896ddad3` (5.31.1's `src/` and `tests/`), 2026-11-07, for the contract facts of `FAILED_CALLS_PLAN.md` §6.2. A row says
how the package's tests already drive the shipped tracker, and which test, if any, already drives a call that fails. Paths are
under `tests/`.

| Package | How its tests build the tracker | A test that drives a failing call |
|---|---|---|
| SQS, SNS, EventBridge, S3, DynamoDB | a private `StubInnerHandler : HttpMessageHandler` returning `ResponseToReturn`, under `new HttpMessageInvoker(new XTrackingMessageHandler(options, stub))` (`Kronikol.Tests.S3/S3TrackingMessageHandlerTests.cs:12`, `:34`; `Kronikol.Tests.DynamoDB/DynamoDbTrackingMessageHandlerTests.cs:10`, `:38`; `Kronikol.Tests.SNS/SnsTrackingMessageHandlerTests.cs:10`, `:38`; `Kronikol.Tests.EventBridge/EventBridgeTrackingMessageHandlerTests.cs:10`, `:38`) | none |
| Atlas Data API | `FakeInnerHandler(response)` under `new AtlasDataApiTrackingMessageHandler(CreateOptions(), inner)` in an `HttpClient` (`Kronikol.Tests.AtlasDataApi/AtlasDataApiTrackingMessageHandlerTests.cs:22-24`, `:352`) | none |
| Cosmos DB | `StubInnerHandler` and `CreateInvoker` (`Kronikol.Tests.CosmosDB/CosmosTrackingMessageHandlerTests.cs:13`, `:35`) | none, so its catch is untested |
| BigQuery | `StubInnerHandler.cs` with `BigQueryTrackingMessageHandlerTests.cs:21` | `RecordedBefore9Tests.cs:90`, its `ThrowingHandler` at `:131-135` |
| Storage Queues | the SDK's `QueueClientOptions().WithTestTracking(options, inner)` | its `RecordedBefore11` facts and `HistoryAcrossTheFixTests.cs` |
| Pub/Sub | `FakePublisherClient` (`Kronikol.Tests.PubSub/TrackingPublisherClientTests.cs:55`); `OneMessageSubscriberClient`, whose `StartAsync` calls the handler once (`TrackingClientOverloadTests.cs:121`, test at `:67`) | none |
| Event Hubs | `OneEventConsumer(exception)`, which throws after one event (`Kronikol.Tests.EventHubs/TrackingEventHubsWrapperCoverageTests.cs:203`) | `:157` asserts the note is the message; nothing for the producer, nothing for a reader that cannot be made |
| Service Bus | hand-written SDK subclasses `RecordingSender` and `RecordingReceiver` under `new TrackingServiceBusSender(inner, new ServiceBusTracker(Options()), Options())` (`Kronikol.Tests.ServiceBus/TrackingServiceBusWrapperCoverageTests.cs:181`, `:203`) | none |
| Kafka | `new TrackingKafkaProducer<,>(new FakeProducer<,> { ProduceAsyncResult / ThrowOnNextProduce }, new KafkaTracker(o), o)` (`Kronikol.Tests.Kafka/KafkaFakes.cs:11`); `ScriptedConsumer.Throws` (`:130`) | `TrackingKafkaProducerFailureTests.cs:89`, `:113`, `:131`, `:153`, `:170`, `:252`, `:265`; `TrackingKafkaConsumerIdentityTests.cs:106`; none for a consume scope whose handler throws |
| MassTransit | `new TrackingSendObserver(new MassTransitTracker(o), o)` (`Kronikol.Tests.MassTransit/TrackingObserverIdentityTests.cs:31`) | the tracker's fault methods called directly (`MassTransitTrackerTests.cs:246`, `:259`, `:272`, `:286`) |
| MediatR | real DI with `AddMediatR` and `TrackMediatorForDiagrams`, `PingHandler` (`Kronikol.Tests.MediatR/MediatorTrackingExtensionsTests.cs:22`, `:32`, `:99`) | none |
| DispatchProxy | `ReplaceWithTracked<IFakeService>(new FakeService(), ...)` (`Kronikol.Tests.DispatchProxy/ReplaceWithTrackedTests.cs:26`, `:117`) | none |
| Core `TrackingProxy` | `TrackingProxy<ICalculator>.Create(new FakeCalculator(), ...)` | `Kronikol.Tests/Tracking/TrackingProxyTests.cs:106` (sync), `:393` (ValueTask, asserts `500`) |
| gRPC | `new GrpcTrackingInterceptor(Options()).AsyncUnaryCall("request", CallKinds.Context(kind), (_, _) => ...)` with a continuation that builds a fake call or throws | `CallEndingTests.cs:180`, `:220`, `:256`, `:276`; `GrpcTrackingInterceptorTests.cs:413`, `:454`; `StreamingOutcomeTests.cs:35`, `:124`, `:140`, `:183` |
| Redis | `StubDatabase.CreateProxy(s => ...)`, a `DispatchProxy` `IDatabase` (`Kronikol.Tests.Redis/StubDatabase.cs:17`), then `.WithRedisTestTracking(MakeOptions())` (`DatabaseExtensionsTests.cs:66-67`) | none |
| Spanner | `new TrackingSpannerConnection(new FailureFakeDbConnection(), _options)` with `FailureFakeDbCommand(ex)` (`Kronikol.Tests.Spanner/TrackingSpannerCommandFailureTests.cs:22`, `:112`) | `:55` (the six command paths); `SpannerTrackingInterceptorTests.cs:749` (unary, an `RpcException`), `:933`, `:968` (streams); none for a non-gRPC exception or a sync throw |
| Elasticsearch | the internal `LogOperation` through `CallHandler` (`Kronikol.Tests.Elasticsearch/ElasticsearchTrackingCallbackHandlerTests.cs:29-43`); its `statusCode` is an `int` defaulting to 200 | none; no test uses a real client or a null status |
| Bigtable, BigQuery's hand-called tracker | `new BigtableTracker(MakeOptions())`, `LogRequest` and `LogResponse` by hand (`Kronikol.Tests.Bigtable/BigtableTrackerTests.cs:37-40`) | n/a: the consumer writes the answer |
| SQL (SqlClient, Npgsql, MySqlConnector, Oracle) | fakes that throw | `Tracking*CommandResponseDetailTests.cs:79`, `:100` |
| Sqlite | real SQLite | `TrackingSqliteResponseCaptureTests.cs:82`, `:102` |
| Dapper | | `TrackingDbCommandTests.cs:103`, `TrackingDbCommandFidelityTests.cs:224` |
| EF Core | real SQLite | `SqlTrackingInterceptorFailureTests.cs:36`; none for a cancelled command |
| ClickHouse (all four packages) | | `TrackingClickHouseCommandTests.cs:84`, `:105`; `TrackingClickHouseClientTests.cs:350`, `:364`; `ClickHouseDriverClientRealServerTests.cs:190`, `:206` |
| MongoDB (and V2, which links the same files) | | `MongoDbTrackingSubscriberTests.cs:93`, `:1209`, `:1223` (a made-up Started and Failed pair the real driver never sends for a server-selection timeout); `MongoDbRealServerTests.cs:76`; `MongoDbFailuresDigestTests.cs:135` |
| TcpTap | | `TcpTapConnectFailureTests.cs:173`; `RedisDecoderTests.cs:170`, `:860`; `MongoDecoderTests.cs:147`, `:688` |
| ProxyTap | | `ProxyTapFailureTests.cs:216`, `:245`, `:280`; `ProxyTapHangUpTests.cs:261` |
| OTLP | | `SpanToInteractionMapperTests.cs:334`; `OtlpSpanMapperTests.cs:173`, `:193`, `:210` |
