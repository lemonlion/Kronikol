namespace Kronikol.Tests.Packaging;

/// <summary>
/// Every dependency a packable <c>src/</c> project hands its consumers, the version it asks for, and what that version
/// rests on (<c>plans/CLIENT_FLOORS_PLAN.md</c> 5.2). Kronikol's packages are test-time packages, but their dependencies
/// are the system under test's: a suite that hosts its services in-process runs them on whatever client version its
/// test project resolves, and NuGet packs a fixed version as a floor (<c>&gt;=</c>) that every consumer must meet. So
/// each entry is a floor someone measured, not the newest release of the day the package was packed (a float) or of the
/// day someone last moved the pin. <c>FloorLedgerTests</c>, in Kronikol.Tests, holds every packable csproj to this
/// list: no float unless it is marked still floating, every fixed pin here at its version, and the marks only go.
/// </summary>
internal static class FloorLedger
{
    /// <summary>What a version rests on.</summary>
    internal enum Basis
    {
        /// <summary>The method of plans/CLIENT_FLOORS_PLAN.md 5.1: the oldest version the suite passes on, past any
        /// advisory in its graph, that a consumer on a later version compiles against.</summary>
        Measured,

        /// <summary>A float, packed as the newest version of its range on the day; the batch that measures it.</summary>
        StillFloating,

        /// <summary>A fixed pin set at the newest version of the day someone last moved it; the batch that measures it.</summary>
        Unmeasured
    }

    /// <param name="Project">The packable project, by its file name without <c>.csproj</c>.</param>
    /// <param name="Package">The package it references.</param>
    /// <param name="Version">The <c>Version</c> the reference gives, exactly as the csproj writes it.</param>
    /// <param name="Basis">What the version rests on.</param>
    /// <param name="Evidence">For a measured floor, the plan or harness that measured it; otherwise the batch that will.</param>
    internal sealed record Entry(string Project, string Package, string Version, Basis Basis, string Evidence);

    /// <summary>No more entries may be marked still floating than this. Each batch lowers it; none raises it.</summary>
    internal const int StillFloatingAtMost = 0;

    /// <summary>No more fixed pins may be marked unmeasured than this. Each batch lowers it; none raises it.</summary>
    internal const int UnmeasuredAtMost = 5;

    private static Entry Measured(string project, string package, string version, string evidence) =>
        new(project, package, version, Basis.Measured, evidence);

    private static Entry Floating(string project, string package, string version, string batch) =>
        new(project, package, version, Basis.StillFloating, batch);

    private static Entry Unmeasured(string project, string package, string version, string batch) =>
        new(project, package, version, Basis.Unmeasured, batch);

    internal static readonly IReadOnlyList<Entry> Entries =
    [
        // The core package: every consumer takes it.
        Measured("Kronikol", "Microsoft.AspNetCore.Mvc.Testing", "10.0.0", "R3a: floor/r3a-core.txt"),

        // Test framework adapters.
        Measured("Kronikol.xUnit2", "xunit.extensibility.execution", "2.4.0", "R3a: floor/r3a-xunit2.txt"),
        Measured("Kronikol.xUnit3", "xunit.v3.extensibility.core", "3.0.0", "R3a: floor/r3a-xunit3.txt"),
        Measured("Kronikol.NUnit4", "NUnit", "4.6.0", "R3a: floor/r3a-nunit4.txt"),
        Measured("Kronikol.MSTest", "MSTest.TestFramework", "4.0.0", "R3a: floor/r3a-mstest.txt"),
        Measured("Kronikol.TUnit", "TUnit.Core", "1.40.0", "R3a: floor/r3a-tunit3.txt, floor/r3a-tunit.txt, probe-r3a/tunit-engine-floor.txt"),
        Measured("Kronikol.ReqNRoll.Core", "Reqnroll", "3.0.1", "R3a: floor/r3a-reqnroll.txt"),
        Measured("Kronikol.LightBDD.Core", "LightBDD.Framework", "3.10.0", "R3a: floor/r3a-lightbdd-xunit2.txt"),
        Measured("Kronikol.LightBDD.TUnit", "LightBDD.TUnit", "3.12.1", "R3a: floor/r3a-lightbdd-tunit.txt"),
        Measured("Kronikol.LightBDD.xUnit2", "LightBDD.XUnit2", "3.10.0", "R3a: floor/r3a-lightbdd-xunit2.txt"),
        Measured("Kronikol.LightBDD.xUnit2", "xunit", "2.4.2", "R3a: floor/r3a-lightbdd-xunit2-xunit.txt"),
        Measured("Kronikol.LightBDD.xUnit3", "LightBDD.XUnit3", "3.12.0", "R3a: floor/r3a-lightbdd-xunit3.txt"),
        Measured("Kronikol.BDDfy.xUnit3", "TestStack.BDDfy", "8.0.1.3", "R3a: floor/r3a-bddfy.txt, floor/r3a-bddfy-f32.txt"),
        Measured("Kronikol.Playwright", "Microsoft.Playwright", "1.12.1", "F37: probe-f37/results.txt, probe-f37/suite.txt; R3a: floor/r3a-playwright.txt"),

        // Clients.
        Measured("Kronikol.Extensions.BigQuery", "Google.Cloud.BigQuery.V2", "3.0.0", "CLIENT_FLOORS_PLAN.md R1"),
        Measured("Kronikol.Extensions.Bigtable", "Google.Cloud.Bigtable.V2", "3.0.0", "R2a: floor/r2a-bigtable.txt"),
        Measured("Kronikol.Extensions.BlobStorage", "Azure.Storage.Blobs", "12.13.0", "R2b: floor/r2b-blobstorage.txt"),
        Measured("Kronikol.Extensions.ClickHouse.Client", "ClickHouse.Client", "7.7.0", "R3b: floor/r3b-clickhouse-client.txt (the suite's limit)"),
        Measured("Kronikol.Extensions.ClickHouse.Driver", "ClickHouse.Driver", "1.3.0", "R3b: floor/r3b-clickhouse-driver.txt"),
        Measured("Kronikol.Extensions.ClickHouse.Octonica", "Octonica.ClickHouseClient", "3.1.1", "R3b: floor/r3b-clickhouse-octonica.txt"),
        Measured("Kronikol.Extensions.CloudStorage", "Google.Cloud.Storage.V1", "4.0.0", "R2a: floor/r2a-cloudstorage.txt"),
        Measured("Kronikol.Extensions.CosmosDB", "Microsoft.Azure.Cosmos", "3.46.0", "R2b: floor/r2b-cosmosdb.txt"),
        Measured("Kronikol.Extensions.DispatchProxy", "Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.0", "R3a: floor/r3a-di-dispatchproxy.txt"),
        Measured("Kronikol.Extensions.DynamoDB", "AWSSDK.DynamoDBv2", "4.0.10.1", "R3b: floor/r3b-dynamodb.txt, floor/r3b-aws-core.txt"),
        Measured("Kronikol.Extensions.EfCore.Relational", "Microsoft.EntityFrameworkCore.Relational", "10.0.0", "R2c: floor/r2c-efcore.txt, the first of the major, its own graph clean"),
        Measured("Kronikol.Extensions.Elasticsearch", "Elastic.Clients.Elasticsearch", "9.0.0", "R3b: floor/r3b-elasticsearch.txt"),
        Measured("Kronikol.Extensions.EventBridge", "AWSSDK.EventBridge", "4.0.5.7", "R3b: floor/r3b-eventbridge.txt, floor/r3b-aws-core.txt"),
        Measured("Kronikol.Extensions.EventHubs", "Azure.Messaging.EventHubs", "5.6.0", "R2b: floor/r2b-eventhubs.txt, the base's Identifier results/r2b-eventhubs-base.txt"),
        Measured("Kronikol.Extensions.Grpc", "Grpc.Net.Client", "2.45.0", "R2a: floor/r2a-grpc.txt"),
        Measured("Kronikol.Extensions.Grpc", "Google.Protobuf", "3.19.4", "R2a: floor/r2a-grpc.txt"),
        Measured("Kronikol.Extensions.Kafka", "Confluent.Kafka", "2.2.0", "R2a: floor/r2a-kafka.txt"),
        Measured("Kronikol.Extensions.Kafka.BuildInterception", "Lib.Harmony", "2.4.2", "R2c: floor/r2c-harmony.txt"),
        Measured("Kronikol.Extensions.MassTransit", "MassTransit", "9.0.0", "R3b: floor/r3b-masstransit.txt"),
        Measured("Kronikol.Extensions.MediatR", "MediatR", "14.0.0", "R3b: floor/r3b-mediatr.txt"),
        Measured("Kronikol.Extensions.MediatR", "Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.0", "R3a: floor/r3a-di-mediatr.txt"),
        // 3.9.0: older 3.x drivers allow Snappier 1.0.0 (a high advisory) or SharpCompress 0.30.1 (a moderate one).
        Measured("Kronikol.Extensions.MongoDB", "MongoDB.Driver", "3.9.0", "MONGODB_DETAILED_PAYLOADS_PLAN.md Q18"),
        // 2.28.0: the first strong-named 2.x, so a consumer on a later 2.x can compile against it (its suite, on the newest
        // 2.x, could not against an unsigned 2.21); every 2.x up to 2.18.0 carries a high advisory of its own.
        Measured("Kronikol.Extensions.MongoDB.V2", "MongoDB.Driver", "2.28.0", "MONGODB_DETAILED_PAYLOADS_PLAN.md Q18"),
        // 2.6.1, the first release past GHSA-473q-m89c-ghf8; moved in R2a ahead of its batch
        // (plans/CLIENT_FLOORS_PLAN.md F10).
        Measured("Kronikol.Extensions.MySqlConnector", "MySqlConnector", "2.6.1", "R3b: the first past GHSA-473q-m89c-ghf8 (F10); the suite passes from 2.0.0 (floor/r3b-mysqlconnector.txt)"),
        Measured("Kronikol.Extensions.Npgsql", "Npgsql", "10.0.0", "R3b: floor/r3b-npgsql.txt"),
        Measured("Kronikol.Extensions.OpenTelemetry", "OpenTelemetry", "1.15.3", "R2c: floor/r2c-opentelemetry.txt"),
        Measured("Kronikol.Extensions.OpenTelemetry", "OpenTelemetry.Exporter.InMemory", "1.15.3", "R2c: floor/r2c-opentelemetry.txt"),
        Measured("Kronikol.Extensions.Oracle", "Oracle.ManagedDataAccess.Core", "23.4.0", "R2c: floor/r2c-oracle.txt"),
        Measured("Kronikol.Extensions.Oracle", "System.Security.Cryptography.Pkcs", "8.0.2",
            "R2c: the first past GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7, under which every 23.x client's own Pkcs falls (floor/r2c-oracle.txt)"),
        Measured("Kronikol.Extensions.PubSub", "Google.Cloud.PubSub.V1", "3.35.0", "R2a: floor/r2a-pubsub.txt"),
        Measured("Kronikol.Extensions.Redis", "StackExchange.Redis", "2.6.80", "R2c: floor/r2c-redis.txt"),
        Measured("Kronikol.Extensions.S3", "AWSSDK.S3", "4.0.13.1", "R3b: floor/r3b-s3.txt, floor/r3b-aws-core.txt"),
        Measured("Kronikol.Extensions.SNS", "AWSSDK.SimpleNotificationService", "4.0.2.7", "R3b: floor/r3b-sns.txt, floor/r3b-aws-core.txt"),
        Measured("Kronikol.Extensions.SQS", "AWSSDK.SQS", "4.0.2.5", "R3b: floor/r3b-sqs.txt, floor/r3b-aws-core.txt"),
        Measured("Kronikol.Extensions.ServiceBus", "Azure.Messaging.ServiceBus", "7.11.0", "R2b: floor/r2b-servicebus.txt"),
        Measured("Kronikol.Extensions.ServiceBus", "Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.0", "R3a: floor/r3a-di-servicebus.txt"),
        Measured("Kronikol.Extensions.Spanner", "Google.Cloud.Spanner.Data", "5.0.0", "R2a: floor/r2a-spanner.txt"),
        Measured("Kronikol.Extensions.Spanner", "Google.Cloud.Spanner.V1", "5.0.0", "R2a: floor/r2a-spanner.txt"),
        Measured("Kronikol.Extensions.Spanner", "Grpc.Core.Api", "2.66.0", "R2a: floor/r2a-spanner.txt"),
        Measured("Kronikol.Extensions.SqlClient", "Microsoft.Data.SqlClient", "7.0.0", "R3b: floor/r3b-sqlclient.txt"),
        Measured("Kronikol.Extensions.SqlClient", "System.Security.Cryptography.Pkcs", "9.0.21",
            "R3b: the first 9.x past GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7, under which every 7.x client's own Pkcs falls (floor/r3b-sqlclient.txt)"),
        Measured("Kronikol.Extensions.Sqlite", "Microsoft.Data.Sqlite", "10.0.11", "R2c: floor/r2c-sqlite.txt"),
        Measured("Kronikol.Extensions.StorageQueues", "Azure.Storage.Queues", "12.11.0", "R2b: floor/r2b-storagequeues.txt"),
        Measured("Kronikol.Extensions.TcpTap", "MongoDB.Bson", "2.28.0", "R2c: floor/r2c-tcptap.txt, the oldest the suite runs on, its own graph clean"),

        // The Java PlantUML renderer, which consumers rarely reference themselves.
        Unmeasured("Kronikol.PlantUml.Ikvm", "IKVM", "8.15.0", "R3c"),
        Unmeasured("Kronikol.PlantUml.Ikvm", "IKVM.Image", "8.15.0", "R3c"),
        Unmeasured("Kronikol.PlantUml.Ikvm", "IKVM.Image.JDK", "8.15.0", "R3c"),
        Unmeasured("Kronikol.PlantUml.Ikvm", "IKVM.Image.JRE", "8.15.0", "R3c"),
        Unmeasured("Kronikol.PlantUml.Ikvm", "IKVM.MSBuild", "8.15.0", "R3c"),
    ];
}
