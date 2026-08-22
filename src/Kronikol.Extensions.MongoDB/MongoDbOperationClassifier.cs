using global::MongoDB.Bson;

// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// Classifies MongoDB HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
public static class MongoDbOperationClassifier
{
    public static MongoDbOperationInfo Classify(
        string commandName, string? databaseName, BsonDocument? command)
    {
        var operation = commandName.ToLowerInvariant() switch
        {
            "find" => MongoDbOperation.Find,
            "insert" => MongoDbOperation.Insert,
            "update" => MongoDbOperation.Update,
            "delete" => MongoDbOperation.Delete,
            "aggregate" => DetectChangeStream(command) ? MongoDbOperation.Watch : MongoDbOperation.Aggregate,
            "count" or "countdocuments" => MongoDbOperation.Count,
            "findandmodify" => MongoDbOperation.FindAndModify,
            "distinct" => MongoDbOperation.Distinct,
            "bulkwrite" => MongoDbOperation.BulkWrite,
            "createindexes" => MongoDbOperation.CreateIndex,
            "dropindexes" => MongoDbOperation.DropIndex,
            "create" => MongoDbOperation.CreateCollection,
            "drop" => MongoDbOperation.DropCollection,
            "listcollections" => MongoDbOperation.ListCollections,
            "listdatabases" => MongoDbOperation.ListDatabases,
            "getmore" => MongoDbOperation.GetMore,
            "mapreduce" => MongoDbOperation.MapReduce,
            "committransaction" => MongoDbOperation.CommitTransaction,
            "aborttransaction" => MongoDbOperation.AbortTransaction,
            "dropdatabase" => MongoDbOperation.DropDatabase,
            "renamecollection" => MongoDbOperation.RenameCollection,
            "listindexes" => MongoDbOperation.ListIndexes,
            "serverstatus" => MongoDbOperation.ServerStatus,
            "dbstats" => MongoDbOperation.DbStats,
            "collstats" => MongoDbOperation.CollStats,
            _ => MongoDbOperation.Other
        };

        var collectionName = ExtractCollectionName(commandName, command);
        var filterText = ExtractFilter(command);
        var documentCount = ExtractDocumentCount(operation, command);
        var documentId = ExtractDocumentId(operation, command);
        var pipelineStages = ExtractPipelineStages(operation, command);
        var isGridFs = DetectGridFs(collectionName);

        return new MongoDbOperationInfo(operation, databaseName, collectionName, filterText,
            documentCount, documentId, pipelineStages, isGridFs);
    }

    public static string GetDiagramLabel(MongoDbOperationInfo op, MongoDbTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            MongoDbTrackingVerbosity.Raw =>
                $"{op.Operation} {op.DatabaseName}.{op.CollectionName}" +
                (op.FilterText != null ? $" filter={op.FilterText}" : ""),
            MongoDbTrackingVerbosity.Detailed => GetDetailedLabel(op),
            MongoDbTrackingVerbosity.Summarised =>
                op.Operation.ToString(),
            _ => op.Operation.ToString()
        };
    }

    private static string GetDetailedLabel(MongoDbOperationInfo op)
    {
        if (op.CollectionName is null)
            return op.Operation.ToString();

        var arrow = GetDirectionalArrow(op.Operation);
        var name = op.Operation.ToString();

        // Add count annotation like PubSub's batch count
        if (op.DocumentCount > 1)
            name = $"{name} (×{op.DocumentCount})";

        // Add pipeline stages for aggregation
        if (op.PipelineStages is not null)
            name = $"{name} ({op.PipelineStages})";

        var label = $"{name} {arrow} {op.CollectionName}";

        // Add GridFS annotation
        if (op.IsGridFs)
            label = $"{label} (GridFS)";

        return label;
    }

    private static string GetDirectionalArrow(MongoDbOperation operation) => operation switch
    {
        // Read operations
        MongoDbOperation.Find or
        MongoDbOperation.Aggregate or
        MongoDbOperation.Watch or
        MongoDbOperation.Count or
        MongoDbOperation.Distinct or
        MongoDbOperation.GetMore or
        MongoDbOperation.MapReduce or
        MongoDbOperation.ListIndexes => "←",

        // Read-modify-write
        MongoDbOperation.FindAndModify => "↔",

        // Write / schema operations
        MongoDbOperation.Insert or
        MongoDbOperation.Update or
        MongoDbOperation.Delete or
        MongoDbOperation.BulkWrite or
        MongoDbOperation.CreateIndex or
        MongoDbOperation.DropIndex or
        MongoDbOperation.CreateCollection or
        MongoDbOperation.DropCollection or
        MongoDbOperation.RenameCollection => "→",

        _ => "→"
    };

    private static string? ExtractCollectionName(string commandName, BsonDocument? command)
    {
        if (command is null) return null;

        // The collection name is the value of the command element itself
        // e.g., { "find": "users", "filter": {...} }
        var key = commandName.ToLowerInvariant();
        foreach (var element in command)
        {
            if (element.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase) &&
                element.Value.IsString)
            {
                return element.Value.AsString;
            }
        }

        return null;
    }

    private static string? ExtractFilter(BsonDocument? command)
    {
        if (command is null) return null;

        return command.TryGetValue("filter", out var filter)
            ? filter.ToString()!
            : null;
    }

    private static bool DetectChangeStream(BsonDocument? command)
    {
        if (command is null) return false;
        if (!command.TryGetValue("pipeline", out var pipelineValue)) return false;
        if (pipelineValue is not BsonArray pipeline || pipeline.Count == 0) return false;

        var firstStage = pipeline[0];
        return firstStage is BsonDocument doc && doc.Contains("$changeStream");
    }

    private static int? ExtractDocumentCount(MongoDbOperation operation, BsonDocument? command)
    {
        if (command is null) return null;

        // Insert commands have a "documents" array
        if (operation == MongoDbOperation.Insert &&
            command.TryGetValue("documents", out var docs) && docs is BsonArray docsArray)
            return docsArray.Count;

        return null;
    }

    private static string? ExtractDocumentId(MongoDbOperation operation, BsonDocument? command)
    {
        if (command is null) return null;

        // Direct filter with _id field
        if (command.TryGetValue("filter", out var filterValue) && filterValue is BsonDocument filter)
        {
            if (filter.ElementCount == 1 && filter.Contains("_id"))
                return filter["_id"].ToString();
        }

        // Delete commands use "deletes" array with "q" sub-documents
        if (operation == MongoDbOperation.Delete &&
            command.TryGetValue("deletes", out var deletes) && deletes is BsonArray deletesArray &&
            deletesArray.Count == 1 && deletesArray[0] is BsonDocument singleDelete &&
            singleDelete.TryGetValue("q", out var q) && q is BsonDocument qDoc &&
            qDoc.ElementCount == 1 && qDoc.Contains("_id"))
        {
            return qDoc["_id"].ToString();
        }

        return null;
    }

    private static string? ExtractPipelineStages(MongoDbOperation operation, BsonDocument? command)
    {
        if (operation is not (MongoDbOperation.Aggregate or MongoDbOperation.Watch)) return null;
        if (command is null) return null;
        if (!command.TryGetValue("pipeline", out var pipelineValue)) return null;
        if (pipelineValue is not BsonArray pipeline || pipeline.Count == 0) return null;

        var stages = pipeline
            .OfType<BsonDocument>()
            .Select(stage => stage.Names.FirstOrDefault())
            .Where(name => name is not null)
            .ToList();

        return stages.Count > 0 ? string.Join(", ", stages) : null;
    }

    private static bool DetectGridFs(string? collectionName)
    {
        if (collectionName is null) return false;
        return collectionName.EndsWith(".files") || collectionName.EndsWith(".chunks");
    }
}