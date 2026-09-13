namespace Kronikol.Constants;

/// <summary>
/// Well-known dependency category strings used by <see cref="DependencyPalette"/> and all tracking extensions
/// to classify dependencies in sequence diagrams. Each value maps to a <see cref="DependencyType"/>
/// which determines the participant shape and color in rendered diagrams.
/// </summary>
public static class DependencyCategories
{
    // ─── Databases ───────────────────────────────────────────
    /// <summary>Azure Cosmos DB.</summary>
    public const string CosmosDB = "CosmosDB";
    /// <summary>Generic SQL database (used by EF Core and Dapper extensions).</summary>
    public const string SQL = "SQL";
    /// <summary>Google Cloud BigQuery.</summary>
    public const string BigQuery = "BigQuery";
    /// <summary>MongoDB.</summary>
    public const string MongoDB = "MongoDB";
    /// <summary>Amazon DynamoDB.</summary>
    public const string DynamoDB = "DynamoDB";
    /// <summary>Elasticsearch / OpenSearch.</summary>
    public const string Elasticsearch = "Elasticsearch";
    /// <summary>Google Cloud Spanner.</summary>
    public const string Spanner = "Spanner";
    /// <summary>Google Cloud Bigtable.</summary>
    public const string Bigtable = "Bigtable";
    /// <summary>Generic database (fallback).</summary>
    public const string Database = "Database";
    /// <summary>PostgreSQL (via Npgsql).</summary>
    public const string PostgreSQL = "PostgreSQL";
    /// <summary>SQL Server (via Microsoft.Data.SqlClient).</summary>
    public const string SqlServer = "SqlServer";
    /// <summary>MySQL (via MySqlConnector).</summary>
    public const string MySQL = "MySQL";
    /// <summary>SQLite.</summary>
    public const string SQLite = "SQLite";
    /// <summary>Oracle Database.</summary>
    public const string Oracle = "Oracle";
    /// <summary>ClickHouse (via ClickHouse.Client or Octonica.ClickHouseClient).</summary>
    public const string ClickHouse = "ClickHouse";

    // ─── Caches ──────────────────────────────────────────────
    /// <summary>Redis cache.</summary>
    public const string Redis = "Redis";

    // ─── Message Queues ──────────────────────────────────────
    /// <summary>Generic message queue / event broker.</summary>
    public const string MessageQueue = "MessageQueue";
    /// <summary>Azure Service Bus.</summary>
    public const string ServiceBus = "ServiceBus";

    // ─── Storage ─────────────────────────────────────────────
    /// <summary>Azure Blob Storage.</summary>
    public const string BlobStorage = "BlobStorage";
    /// <summary>Amazon S3.</summary>
    public const string S3 = "S3";
    /// <summary>Google Cloud Storage.</summary>
    public const string CloudStorage = "CloudStorage";

    // ─── HTTP / RPC ──────────────────────────────────────────
    /// <summary>Plain HTTP API dependency.</summary>
    public const string HTTP = "HTTP";
    /// <summary>MediatR in-process mediator.</summary>
    public const string MediatR = "MediatR";
    /// <summary>gRPC service.</summary>
    public const string Grpc = "gRPC";

    // ─── AI / LLM ────────────────────────────────────────────
    /// <summary>A large-language-model / AI provider call (Gemini, OpenAI, Ollama, Bedrock, …).</summary>
    public const string AI = "AI";

    // ─── People ──────────────────────────────────────────────
    /// <summary>A human (or the test driving a browser as one) acting on the system — the caller of UI actions. Renders as an actor.</summary>
    public const string User = "User";

    // ─── Extension-specific (not in DependencyPalette) ───────
    /// <summary>MongoDB Atlas Data API (REST-based MongoDB access).</summary>
    public const string AtlasDataApi = "AtlasDataApi";

    /// <summary>
    /// The categories whose captured content is the call's own identity — a query, a statement, a command —
    /// rather than a payload it carried.
    ///
    /// <para>It decides whether a surface may quote the content inline. For a database call the statement
    /// IS the call and a URI like <c>sql://OrdersDb/Orders</c> says nothing; for a broker publish the
    /// content is a business message, and quoting it puts a payload where a reader expected a call. That
    /// was measured: <c>Failures.md</c> showed a <c>MessageQueue</c> send as 120 characters of its own
    /// serialised body, beside calls shown as <c>GET /milk</c>, in a file whose contract is that bodies are
    /// addresses rather than content.</para>
    ///
    /// <para>A positive list on purpose, so the safe answer is the default: a category added later and not
    /// named here shows its target rather than its content, which loses detail instead of leaking a body.
    /// A call with NO category is not decided here at all — the caller falls back to reading the method,
    /// because taps that predate categories still write real statements.</para>
    /// </summary>
    public static bool IsStatementShaped(string? dependencyCategory) =>
        dependencyCategory is not null && StatementShaped.Contains(dependencyCategory);

    private static readonly HashSet<string> StatementShaped = new(StringComparer.OrdinalIgnoreCase)
    {
        SQL, Database, PostgreSQL, SqlServer, MySQL, SQLite, Oracle, ClickHouse, Spanner, BigQuery,
        CosmosDB, MongoDB, DynamoDB, Elasticsearch, Bigtable, Redis, AtlasDataApi,
    };
}
