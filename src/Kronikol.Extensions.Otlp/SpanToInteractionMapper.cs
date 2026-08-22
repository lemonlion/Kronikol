using System.Globalization;
using Kronikol.Constants;

namespace Kronikol.Extensions.Otlp;

/// <summary>
/// Turns an OpenTelemetry span into the Kronikol call it stands for. Pure and deterministic: the same
/// span always maps to the same <see cref="MappedSpan"/>, which is what makes it testable against golden
/// OTLP payloads from the Java agent, the Node auto-instrumentations and the .NET SDK.
/// </summary>
/// <remarks>
/// <para><strong>Semantic conventions.</strong> Both the deprecated and the stable attribute names are
/// accepted, stable first: <c>db.query.text</c>←<c>db.statement</c>, <c>db.operation.name</c>←<c>db.operation</c>,
/// <c>db.collection.name</c>←<c>db.mongodb.collection</c>, <c>db.namespace</c>←<c>db.name</c>/<c>db.redis.database_index</c>,
/// <c>server.address</c>/<c>server.port</c>←<c>net.peer.name</c>/<c>net.peer.port</c>,
/// <c>http.request.method</c>←<c>http.method</c>, <c>url.full</c>←<c>http.url</c>,
/// <c>http.response.status_code</c>←<c>http.status_code</c>, <c>db.system.name</c>←<c>db.system</c>.</para>
/// <para><strong>Labels.</strong> Mongo uses the same directional arrows as
/// <c>Kronikol.Extensions.MongoDB</c> (<c>Find ← Trial</c>, <c>Insert → Trial</c>,
/// <c>FindAndModify ↔ Trial</c>) so a span-sourced arrow reads identically to an in-process one; the
/// <c>(×N)</c> document count is not derivable from a span and is therefore never shown. Redis uses the
/// command verb (the <em>Raw</em> label of <c>Kronikol.Extensions.Redis</c>), with <c>(Hit)</c>/<c>(Miss)</c>
/// only when the producer supplied a result attribute — instrumentations usually do not.</para>
/// <para><strong>Attribution.</strong> <c>TestId</c> is the span's trace id (browser-driven suites mint the
/// trace id as the test id), and <c>ActivityTraceId</c>/<c>ActivitySpanId</c> are always set so the report
/// can cross-link to Tempo/Jaeger (observability invariant D4).</para>
/// </remarks>
public static class SpanToInteractionMapper
{
    /// <summary>
    /// Maps one span, or returns null when it is not a call this tap captures (wrong kind, a server or
    /// internal span, a span family excluded by <see cref="OtlpTapOptions.CaptureKinds"/>, or a span with no
    /// usable identity).
    /// </summary>
    public static MappedSpan? Map(OtlpSpan span, OtlpTapOptions options)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(span.TraceId) || string.IsNullOrEmpty(span.SpanId))
            return null;

        var isServer = span.Kind is OtlpSpanKind.Server;
        if (isServer && !options.IncludeServerSpans)
            return null;
        if (span.Kind is OtlpSpanKind.Internal or OtlpSpanKind.Unspecified && !HasDependencyAttributes(span))
            return null;

        var dbSystem = span.Attribute("db.system.name", "db.system");
        if (dbSystem is not null)
            return options.CaptureKinds.Contains(OtlpCaptureKinds.Db) ? MapDatabase(span, options, dbSystem) : null;

        var messagingSystem = span.Attribute("messaging.system");
        if (messagingSystem is not null && span.Kind is OtlpSpanKind.Producer or OtlpSpanKind.Consumer)
            return options.CaptureKinds.Contains(OtlpCaptureKinds.Messaging) ? MapMessaging(span, options, messagingSystem) : null;

        var rpcSystem = span.Attribute("rpc.system");
        if (rpcSystem is not null)
            return options.CaptureKinds.Contains(OtlpCaptureKinds.Rpc) ? MapRpc(span, options, rpcSystem) : null;

        if (messagingSystem is not null)
            return options.CaptureKinds.Contains(OtlpCaptureKinds.Messaging) ? MapMessaging(span, options, messagingSystem) : null;

        var httpMethod = span.Attribute("http.request.method", "http.method");
        if (httpMethod is not null)
            return options.CaptureKinds.Contains(OtlpCaptureKinds.Http) ? MapHttp(span, options, httpMethod, isServer) : null;

        return null;
    }

    /// <summary>Maps every span of an export, dropping the ones that are not captured. Order is preserved.</summary>
    public static IReadOnlyList<MappedSpan> MapAll(IEnumerable<OtlpSpan> spans, OtlpTapOptions options)
    {
        ArgumentNullException.ThrowIfNull(spans);
        var mapped = new List<MappedSpan>();
        foreach (var span in spans)
        {
            if (Map(span, options) is { } result)
                mapped.Add(result);
        }

        return mapped;
    }

    private static bool HasDependencyAttributes(OtlpSpan span) =>
        span.HasAttribute("db.system.name", "db.system", "messaging.system", "rpc.system", "http.request.method", "http.method");

    // ------------------------------------------------------------------ database

    private static MappedSpan? MapDatabase(OtlpSpan span, OtlpTapOptions options, string dbSystem)
    {
        var system = dbSystem.Trim().ToLowerInvariant();
        var statement = Cap(span.Attribute("db.query.text", "db.statement"), options.ContentCapBytes);
        var operation = span.Attribute("db.operation.name", "db.operation");
        var collection = span.Attribute("db.collection.name", "db.mongodb.collection");
        var db = span.Attribute("db.namespace", "db.name") ?? span.Attribute("db.redis.database_index");
        var (host, port) = Peer(span);
        var category = CategoryFor(system);

        string method;
        string uri;
        switch (system)
        {
            case "redis" or "valkey":
            {
                var command = (operation ?? FirstWord(statement) ?? FirstWord(span.Name) ?? "COMMAND").ToUpperInvariant();
                method = command + HitMissSuffix(span);
                var index = db is not null && int.TryParse(db, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                var key = RedisKey(statement, command);
                uri = key is null ? $"redis://db{index}/" : $"redis://db{index}/{Escape(key)}";
                break;
            }

            case "mongodb":
            {
                var mongoOperation = operation ?? MongoOperationFromSpanName(span.Name) ?? "Command";
                // Handshake, monitoring and authentication commands are connection plumbing, not
                // calls the test made — the wire tap (MongoTapOptions.ExcludedCommands) never records
                // them, and the span view must agree or a diagram grows `IsMaster → mongodb` arrows
                // from every connection the driver opens.
                if (MongoPlumbingCommands.Contains(mongoOperation))
                    return null;
                var (name, arrow) = MongoOperation(mongoOperation);
                var mongoCollection = collection ?? MongoCollectionFromSpanName(span.Name);
                method = mongoCollection is null ? name : $"{name} {arrow} {mongoCollection}";
                var database = db ?? "unknown";
                uri = mongoCollection is null ? $"mongodb:///{Escape(database)}" : $"mongodb:///{Escape(database)}/{Escape(mongoCollection)}";
                break;
            }

            default:
            {
                method = operation ?? FirstWord(statement)?.ToUpperInvariant() ?? FirstWord(span.Name) ?? "Query";
                var authority = host is null ? "" : port is null ? host : $"{host}:{port}";
                var scheme = SchemeFor(system);
                uri = db is null ? $"{scheme}://{authority}/" : $"{scheme}://{authority}/{Escape(db)}";
                break;
            }
        }

        var serviceName = ResolveDependencyName(span, options, host, port, system);
        return Build(span, options, method, uri, serviceName, statement, category);
    }

    private static string HitMissSuffix(OtlpSpan span)
    {
        var hit = span.Attribute("db.redis.cache.hit", "cache.hit", "db.cache.hit");
        if (hit is not null)
        {
            if (bool.TryParse(hit, out var flag))
                return flag ? " (Hit)" : " (Miss)";
        }

        var rows = span.Attribute("db.response.returned_rows");
        if (rows is not null && long.TryParse(rows, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return count > 0 ? " (Hit)" : " (Miss)";

        return "";
    }

    /// <summary>The key of a Redis statement such as <c>get insights:abc</c>; null when the producer elided the arguments.</summary>
    internal static string? RedisKey(string? statement, string command)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return null;
        var parts = statement.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;
        var first = parts[0];
        var index = first.Equals(command, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (index >= parts.Length)
            return null;
        var key = parts[index];
        // "get [1 other arguments]" — the ioredis/redis instrumentations elide values by default.
        return key.StartsWith('[') || key == "?" ? null : key;
    }

    /// <summary>
    /// Mongo commands that are connection plumbing rather than application calls (handshake, server
    /// monitoring, SCRAM authentication, session bookkeeping). Mirrors the wire tap's default
    /// <c>MongoTapOptions.ExcludedCommands</c> so the two capture paths agree on what is a call.
    /// </summary>
    internal static readonly HashSet<string> MongoPlumbingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "isMaster", "ismaster", "saslStart", "saslContinue", "saslSupportedMechs",
        "ping", "buildInfo", "getParameter", "getLastError", "killCursors", "endSessions",
        "logout", "authenticate", "getnonce", "whatsmyuri", "connectionStatus",
    };

    /// <summary>The display name and directional arrow for a Mongo operation, matching <c>Kronikol.Extensions.MongoDB</c>.</summary>
    internal static (string Name, string Arrow) MongoOperation(string operation)
    {
        var name = operation.Trim().ToLowerInvariant() switch
        {
            "find" => "Find",
            "insert" => "Insert",
            "update" => "Update",
            "delete" => "Delete",
            "aggregate" => "Aggregate",
            "count" or "countdocuments" => "Count",
            "findandmodify" => "FindAndModify",
            "distinct" => "Distinct",
            "bulkwrite" => "BulkWrite",
            "createindexes" => "CreateIndex",
            "dropindexes" => "DropIndex",
            "create" => "CreateCollection",
            "drop" => "DropCollection",
            "listcollections" => "ListCollections",
            "listdatabases" => "ListDatabases",
            "getmore" => "GetMore",
            "mapreduce" => "MapReduce",
            "committransaction" => "CommitTransaction",
            "aborttransaction" => "AbortTransaction",
            "dropdatabase" => "DropDatabase",
            "renamecollection" => "RenameCollection",
            "listindexes" => "ListIndexes",
            _ => Capitalise(operation.Trim()),
        };

        var arrow = name switch
        {
            "Find" or "Aggregate" or "Watch" or "Count" or "Distinct" or "GetMore" or "MapReduce" or "ListIndexes"
                or "ListCollections" or "ListDatabases" => "←",
            "FindAndModify" => "↔",
            _ => "→",
        };

        return (name, arrow);
    }

    /// <summary>The Java agent names Mongo spans <c>{collection}.{operation}</c> (e.g. <c>Trial.find</c>).</summary>
    internal static string? MongoOperationFromSpanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    internal static string? MongoCollectionFromSpanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : null;
    }

    private static string SchemeFor(string system) => system switch
    {
        "postgresql" or "postgres" => "postgresql",
        "mysql" or "mariadb" => "mysql",
        "mssql" or "microsoft.sql_server" or "sqlserver" => "mssql",
        "bigquery" or "gcp.bigquery" => "bigquery",
        _ => system.Replace('.', '-'),
    };

    private static string? CategoryFor(string system) => system switch
    {
        "redis" or "valkey" => DependencyCategories.Redis,
        "mongodb" => DependencyCategories.MongoDB,
        "bigquery" or "gcp.bigquery" => DependencyCategories.BigQuery,
        "postgresql" or "postgres" => DependencyCategories.PostgreSQL,
        "mysql" or "mariadb" => DependencyCategories.MySQL,
        "mssql" or "microsoft.sql_server" or "sqlserver" => DependencyCategories.SqlServer,
        "sqlite" => DependencyCategories.SQLite,
        "oracle" or "oracle.db" => DependencyCategories.Oracle,
        "elasticsearch" => DependencyCategories.Elasticsearch,
        "cosmosdb" or "azure.cosmosdb" => DependencyCategories.CosmosDB,
        "dynamodb" or "aws.dynamodb" => DependencyCategories.DynamoDB,
        "spanner" or "gcp.spanner" => DependencyCategories.Spanner,
        "bigtable" or "gcp.bigtable" => DependencyCategories.Bigtable,
        "clickhouse" => DependencyCategories.ClickHouse,
        _ => DependencyCategories.Database,
    };

    // ------------------------------------------------------------------ http

    private static MappedSpan MapHttp(OtlpSpan span, OtlpTapOptions options, string httpMethod, bool isServer)
    {
        var (host, port) = Peer(span);
        var url = span.Attribute("url.full", "http.url");
        if (url is null)
        {
            var path = span.Attribute("url.path", "http.target", "http.route") ?? "/";
            var query = span.Attribute("url.query");
            var scheme = span.Attribute("url.scheme", "http.scheme") ?? "http";
            var authority = host is null ? "unknown" : port is null ? host : $"{host}:{port}";
            url = $"{scheme}://{authority}{(path.StartsWith('/') ? path : "/" + path)}{(query is null ? "" : "?" + query)}";
        }

        if (host is null && Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            host = parsed.Host;
            port = parsed.IsDefaultPort ? null : parsed.Port.ToString(CultureInfo.InvariantCulture);
        }

        var status = span.Attribute("http.response.status_code", "http.status_code");
        var caller = MapName(span.ServiceName ?? options.DefaultCallerName, options);
        var service = ResolveDependencyName(span, options, host, port, host ?? "http");

        if (isServer)
        {
            // The receiving side: the participant is this service, the caller is whoever dialled it.
            var peerName = span.Attribute("client.address", "net.peer.name", "net.sock.peer.addr");
            service = MapName(span.ServiceName ?? options.DefaultCallerName, options);
            caller = peerName is null ? "client" : MapName(peerName, options);
        }

        return Build(span, options, httpMethod.ToUpperInvariant(), url, service, requestContent: null,
            dependencyCategory: null, explicitStatus: status, callerOverride: caller);
    }

    // ------------------------------------------------------------------ messaging / rpc

    private static MappedSpan MapMessaging(OtlpSpan span, OtlpTapOptions options, string system)
    {
        var destination = span.Attribute("messaging.destination.name", "messaging.destination", "messaging.destination_kind") ?? "queue";
        var operation = span.Attribute("messaging.operation.name", "messaging.operation")
                        ?? (span.Kind == OtlpSpanKind.Consumer ? "receive" : "publish");
        var (host, port) = Peer(span);
        var method = $"{Capitalise(operation)} {destination}";
        var uri = $"{system.ToLowerInvariant()}://{(host ?? "broker")}/{Escape(destination)}";
        var service = ResolveDependencyName(span, options, host, port, system);
        var body = Cap(span.Attribute("messaging.message.body.size", "messaging.message.id"), options.ContentCapBytes);
        return Build(span, options, method, uri, service, body, DependencyCategories.MessageQueue);
    }

    private static MappedSpan MapRpc(OtlpSpan span, OtlpTapOptions options, string system)
    {
        var service = span.Attribute("rpc.service") ?? "";
        var method = span.Attribute("rpc.method") ?? span.Name;
        var (host, port) = Peer(span);
        var label = string.IsNullOrEmpty(service) ? method : $"{service}/{method}";
        var authority = host is null ? "unknown" : port is null ? host : $"{host}:{port}";
        var uri = $"{system.ToLowerInvariant()}://{authority}/{label.TrimStart('/')}";
        var participant = ResolveDependencyName(span, options, host, port, system);
        var category = system.Equals("grpc", StringComparison.OrdinalIgnoreCase) ? DependencyCategories.Grpc : null;
        return Build(span, options, label, uri, participant, requestContent: null, category);
    }

    // ------------------------------------------------------------------ shared

    private static MappedSpan Build(
        OtlpSpan span, OtlpTapOptions options, string method, string uri, string serviceName,
        string? requestContent, string? dependencyCategory, string? explicitStatus = null, string? callerOverride = null)
    {
        var failed = span.StatusCode == OtlpStatusCode.Error;
        var status = explicitStatus ?? (failed ? "500" : "OK");
        var responseContent = failed ? Cap(span.StatusMessage, options.ContentCapBytes) : null;
        var caller = callerOverride ?? MapName(span.ServiceName ?? options.DefaultCallerName, options);

        return new MappedSpan(
            ResolveTestId(span.TraceId, options),
            options.FallbackTestName,
            method,
            uri,
            serviceName,
            caller,
            requestContent,
            responseContent,
            status,
            dependencyCategory,
            span.StartTime,
            span.EndTime,
            span.TraceId,
            span.SpanId);
    }

    /// <summary>The test id a trace belongs to: the trace id itself on the exact-attribution path, else the fallback.</summary>
    internal static string ResolveTestId(string traceId, OtlpTapOptions options)
    {
        if (options.AttributeByTraceId && (options.KnownTestIds is null || options.KnownTestIds(traceId)))
            return traceId;
        return options.FallbackTestId ?? traceId;
    }

    private static (string? Host, string? Port) Peer(OtlpSpan span)
    {
        var host = span.Attribute("server.address", "net.peer.name", "net.sock.peer.name", "net.sock.peer.addr", "peer.hostname");
        var port = span.Attribute("server.port", "net.peer.port", "net.sock.peer.port");
        if (host is null && span.Attribute("peer.service") is { } peerService)
        {
            var colon = peerService.LastIndexOf(':');
            if (colon > 0 && int.TryParse(peerService[(colon + 1)..], out _))
                return (peerService[..colon], peerService[(colon + 1)..]);
            return (peerService, port);
        }

        return (host, port);
    }

    /// <summary>Participant name for the receiving side: the first <see cref="OtlpTapOptions.ServiceNameMap"/> hit among <c>peer.service</c>, <c>host:port</c>, <c>host</c> and the system name; else <c>host:port</c>, else the system name.</summary>
    private static string ResolveDependencyName(OtlpSpan span, OtlpTapOptions options, string? host, string? port, string fallback)
    {
        var candidates = new List<string>(4);
        if (span.Attribute("peer.service") is { } peerService)
            candidates.Add(peerService);
        if (host is not null && port is not null)
            candidates.Add($"{host}:{port}");
        if (host is not null)
            candidates.Add(host);
        candidates.Add(fallback);

        foreach (var candidate in candidates)
        {
            if (options.ServiceNameMap.TryGetValue(candidate, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
                return mapped;
        }

        if (host is not null)
            return port is null ? host : $"{host}:{port}";
        return fallback;
    }

    private static string MapName(string name, OtlpTapOptions options) =>
        options.ServiceNameMap.TryGetValue(name, out var mapped) && !string.IsNullOrWhiteSpace(mapped) ? mapped : name;

    private static string? Cap(string? value, int? cap)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        if (cap is not { } limit || value.Length <= limit)
            return value;
        return $"{value[..limit]}\n\n…truncated ({value.Length} chars total)";
    }

    private static string? FirstWord(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.TrimStart();
        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n', '(']);
        var word = end < 0 ? trimmed : trimmed[..end];
        return string.IsNullOrWhiteSpace(word) ? null : word;
    }

    private static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Escape(string value) => value.Replace(" ", "%20");
}
