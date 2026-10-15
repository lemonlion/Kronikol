using System.Globalization;
using System.Text;
using global::MongoDB.Bson;

// Shared source: also compiled into Kronikol.Extensions.TcpTap (and Kronikol.Extensions.MongoDB.V2) as a
// linked file, so a command tapped on the wire gets the same notes, status and error as one the in-process
// subscriber saw. The tap compiles it into its own namespace so a host can reference both packages.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// What a MongoDB reply says, as a note: the documents it returned, or the reply without the server's bookkeeping,
/// written by <see cref="MongoJson"/>; whether it failed; and why.
/// </summary>
internal static class MongoDbNotes
{
    /// <summary>
    /// The fields a server adds to a reply about itself rather than the command: a replica set's <c>ok</c>,
    /// <c>$clusterTime</c>, <c>operationTime</c>, <c>electionId</c> and <c>opTime</c>; mongos's <c>recoveryToken</c>
    /// and <c>retriedStmtIds</c>; a shard member's <c>readOnly</c> and <c>$replData</c>. Each was seen in a reply
    /// (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, section 2.2). A count's <c>shards</c> and a createIndexes's
    /// <c>raw</c> are results, and stay.
    /// </summary>
    internal static readonly IReadOnlyCollection<string> ReplyBookkeeping = new HashSet<string>(StringComparer.Ordinal)
    {
        "ok", "$clusterTime", "operationTime", "electionId", "opTime",
        "recoveryToken", "retriedStmtIds",
        "readOnly", "$replData",
    };

    /// <summary>
    /// The fields the driver adds to a command about the session, the transaction, the topology or the API rather
    /// than the statement: <c>$db</c>, <c>lsid</c>, <c>txnNumber</c>, <c>$clusterTime</c>, <c>startTransaction</c>,
    /// <c>autocommit</c>, <c>recoveryToken</c> (copied onto a commit or an abort through mongos),
    /// <c>writeConcern</c>, <c>readConcern</c>, <c>$readPreference</c>, <c>apiVersion</c>, <c>apiStrict</c> and
    /// <c>apiDeprecationErrors</c> (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, section 4.3).
    /// </summary>
    internal static readonly IReadOnlyCollection<string> CommandBookkeeping = new HashSet<string>(StringComparer.Ordinal)
    {
        "$db", "lsid", "txnNumber", "$clusterTime",
        "startTransaction", "autocommit", "recoveryToken",
        "writeConcern", "readConcern", "$readPreference",
        "apiVersion", "apiStrict", "apiDeprecationErrors",
    };

    /// <summary>The arrays of a write that hold what it writes: documents, then statements.</summary>
    private static readonly string[] RequestDocuments = ["documents"];
    private static readonly string[] RequestStatements = ["updates", "deletes", "ops"];

    /// <summary>
    /// The note for a command at <c>Detailed</c>: the command without its name and without the driver's own fields
    /// (<see cref="CommandBookkeeping"/>, <c>ordered</c> when it is true, and an aggregate's <c>cursor</c>). The first
    /// element is dropped when its value is a string, a number or a boolean (a collection, <c>1</c>, a getMore's cursor
    /// id) and the command is <paramref name="classified"/>, since the label and the URI then carry it; any other first
    /// element stays, as <c>explain</c>'s command does, and so does an unclassified command's name, which its label
    /// (<c>Other</c>) does not carry. An insert's
    /// <c>documents</c>, and an update's, a delete's or a client bulkWrite's statements, keep their first
    /// <paramref name="maxRequestDocuments"/> entries, and a note that dropped some ends with
    /// <c>... (N more documents not shown)</c>, or <c>... (N more)</c> for statements. A binary over 256 bytes is a
    /// placeholder. Null when nothing is left, as for a commitTransaction.
    /// </summary>
    internal static string? Request(BsonDocument command, int maxRequestDocuments, bool classified = true)
    {
        var kept = new List<BsonElement>();
        var isAggregate = false;
        for (var i = 0; i < command.ElementCount; i++)
        {
            var element = command.GetElement(i);
            if (i == 0)
            {
                isAggregate = element.Name == "aggregate";
                if (classified && (element.Value.IsString || element.Value.IsNumeric || element.Value.IsBoolean))
                    continue;
            }
            if (CommandBookkeeping.Contains(element.Name)
                || (element.Name == "ordered" && element.Value is BsonBoolean { Value: true })
                || (isAggregate && element.Name == "cursor"))
                continue;
            kept.Add(element);
        }
        if (kept.Count == 0)
            return null;

        var max = Math.Max(0, maxRequestDocuments);
        var builder = new StringBuilder();
        int documentsNotShown = 0, statementsNotShown = 0;
        builder.Append('{');
        for (var i = 0; i < kept.Count; i++)
        {
            builder.Append(i == 0 ? " " : ", ");
            MongoJson.WriteString(builder, kept[i].Name);
            builder.Append(" : ");
            if (kept[i].Value is BsonArray entries && Array.IndexOf(RequestDocuments, kept[i].Name) >= 0)
                documentsNotShown += WriteBatch(builder, entries, max);
            else if (kept[i].Value is BsonArray statements && Array.IndexOf(RequestStatements, kept[i].Name) >= 0)
                statementsNotShown += WriteBatch(builder, statements, max);
            else
                MongoJson.Write(builder, kept[i].Value, binaryPlaceholders: true);
        }
        builder.Append(" }");

        if (documentsNotShown > 0)
            builder.Append('\n').Append(string.Create(CultureInfo.InvariantCulture, $"... ({documentsNotShown} more documents not shown)"));
        if (statementsNotShown > 0)
            builder.Append('\n').Append(string.Create(CultureInfo.InvariantCulture, $"... ({statementsNotShown} more)"));
        return builder.ToString();
    }

    /// <summary>
    /// The note for a reply at <c>Detailed</c>. A cursor's reply is its batch as an array (<c>[]</c> when empty); any
    /// other reply is an object without the server's bookkeeping, a cursor inside it replaced by its batch. A batch
    /// keeps its first <paramref name="maxResponseDocuments"/> documents, and a note that dropped some ends with
    /// <c>... (N more documents not shown)</c>. A distinct's <c>values</c>, and a write's <c>writeErrors</c> and
    /// <c>upserted</c>, keep as many entries, and a note that dropped some of those ends with <c>... (N more)</c>, N
    /// counting everything left out. Without <paramref name="logResponseContent"/>, the documents (a batch, a
    /// findAndModify's <c>value</c>, a distinct's <c>values</c>) are left out; a client bulkWrite's batch keeps the
    /// operations that failed, which are its write errors. Null when nothing is left, and for <c>serverStatus</c>,
    /// <c>dbStats</c> and <c>collStats</c>, whose replies are server state.
    /// </summary>
    internal static string? Reply(BsonDocument? reply, MongoDbOperation operation, bool logResponseContent, int maxResponseDocuments)
    {
        if (reply is null || operation is MongoDbOperation.ServerStatus or MongoDbOperation.DbStats or MongoDbOperation.CollStats)
            return null;
        return Write(reply, operation, logResponseContent, maxResponseDocuments);
    }

    /// <summary>The note for a failed command's reply: the reply without the server's bookkeeping (its <c>errmsg</c>, <c>code</c> and <c>codeName</c>), or null when nothing is left.</summary>
    internal static string? Failure(BsonDocument reply) =>
        Write(reply, MongoDbOperation.Other, logResponseContent: true, maxResponseDocuments: int.MaxValue);

    /// <summary>A whole document, as <c>Raw</c> records it: nothing left out, no binary placeholder.</summary>
    internal static string? Whole(BsonDocument? document) => document is null ? null : MongoJson.Write(document);

    /// <summary>Whether a reply says the command failed: <c>ok</c> is 0, or it carries an <c>errmsg</c>.</summary>
    internal static bool IsCommandFailure(BsonDocument reply) =>
        (reply.TryGetValue("ok", out var ok) && Number(ok) == 0d) || reply.Contains("errmsg");

    /// <summary>
    /// Whether a reply the server sent as a success says a write failed: a <c>writeErrors</c> entry, a
    /// <c>writeConcernError</c>, or a client-level bulkWrite's <c>nErrors</c>. The driver raises each as an exception
    /// in the application.
    /// </summary>
    internal static bool IsWriteFailure(BsonDocument reply) =>
        IsStatementFailure(reply) || (reply.TryGetValue("writeConcernError", out var concern) && concern is BsonDocument);

    /// <summary>
    /// Whether a reply says a statement was not applied: a <c>writeErrors</c> entry, or a client-level bulkWrite's
    /// <c>nErrors</c>. A <c>writeConcernError</c> is not one: the write was applied, and only its replication failed.
    /// </summary>
    internal static bool IsStatementFailure(BsonDocument reply) =>
        (reply.TryGetValue("writeErrors", out var writeErrors) && writeErrors is BsonArray { Count: > 0 })
        || (reply.TryGetValue("nErrors", out var errors) && Number(errors) > 0d);

    /// <summary>
    /// Why a reply failed, in the server's words: its <c>errmsg</c>, the first <c>writeErrors</c> entry's, the
    /// <c>writeConcernError</c>'s, or the first failed operation's in a client-level bulkWrite's batch; a bulkWrite
    /// whose failed operations are past its first batch says how many failed. Null when the reply names none.
    /// </summary>
    internal static string? FailureReason(BsonDocument reply)
    {
        if (Text(reply, "errmsg") is { } errmsg)
            return errmsg;
        if (reply.TryGetValue("writeErrors", out var writeErrors) && writeErrors is BsonArray { Count: > 0 } entries
            && entries[0] is BsonDocument first && Text(first, "errmsg") is { } writeError)
            return writeError;
        if (reply.TryGetValue("writeConcernError", out var concern) && concern is BsonDocument concernError
            && Text(concernError, "errmsg") is { } concernMessage)
            return concernMessage;
        if (reply.TryGetValue("cursor", out var cursor) && cursor is BsonDocument cursorDocument && Batch(cursorDocument) is { } batch)
        {
            foreach (var result in batch)
            {
                if (result is BsonDocument operation && operation.TryGetValue("ok", out var ok) && Number(ok) == 0d
                    && Text(operation, "errmsg") is { } operationError)
                    return operationError;
            }
        }
        if (reply.TryGetValue("nErrors", out var errors) && Number(errors) is double count && count > 0d)
            return string.Create(CultureInfo.InvariantCulture, $"{count:0} operation{(count == 1d ? "" : "s")} failed");
        return null;
    }

    private static string? Write(BsonDocument reply, MongoDbOperation operation, bool logResponseContent, int maxResponseDocuments)
    {
        var kept = new List<BsonElement>();
        foreach (var element in reply)
        {
            if (ReplyBookkeeping.Contains(element.Name))
                continue;
            if (!logResponseContent && IsDocuments(element, operation))
            {
                // A client bulkWrite's write errors are entries of its batch: an error is not a document, so it stays.
                if (operation == MongoDbOperation.BulkWrite && BatchOf(element) is { } results
                    && FailedOperations(results) is { Count: > 0 } failed)
                    kept.Add(new BsonElement(element.Name, new BsonDocument("firstBatch", failed)));
                continue;
            }
            kept.Add(element);
        }
        if (kept.Count == 0)
            return null;

        var max = Math.Max(0, maxResponseDocuments);
        var builder = new StringBuilder();
        int notShown = 0, entriesNotShown = 0;
        if (kept.Count == 1 && BatchOf(kept[0]) is { } only)
        {
            notShown = WriteBatch(builder, only, max);
        }
        else
        {
            builder.Append('{');
            for (var i = 0; i < kept.Count; i++)
            {
                builder.Append(i == 0 ? " " : ", ");
                MongoJson.WriteString(builder, kept[i].Name);
                builder.Append(" : ");
                if (BatchOf(kept[i]) is { } batch)
                    notShown += WriteBatch(builder, batch, max);
                else if (CappedEntries(kept[i], operation) is { } entries)
                    entriesNotShown += WriteBatch(builder, entries, max);
                else
                    MongoJson.Write(builder, kept[i].Value, binaryPlaceholders: true);
            }
            builder.Append(" }");
        }

        // One footnote, which the page and the query tool take off whole: documents when only documents were left out.
        if (entriesNotShown > 0)
            builder.Append('\n').Append(string.Create(CultureInfo.InvariantCulture, $"... ({notShown + entriesNotShown} more)"));
        else if (notShown > 0)
            builder.Append('\n').Append(string.Create(CultureInfo.InvariantCulture, $"... ({notShown} more documents not shown)"));
        return builder.ToString();
    }

    /// <summary>The arrays of a reply other than a batch that can run long: a distinct's values, a write's errors and upserted ids.</summary>
    private static BsonArray? CappedEntries(BsonElement element, MongoDbOperation operation) =>
        element.Value is BsonArray entries
        && (element.Name is "writeErrors" or "upserted" || (operation == MongoDbOperation.Distinct && element.Name == "values"))
            ? entries
            : null;

    /// <summary>The entries of a client bulkWrite's batch that say an operation failed (<c>ok</c> is 0).</summary>
    private static BsonArray FailedOperations(BsonArray results) =>
        new(results.Where(result => result is BsonDocument operation && operation.TryGetValue("ok", out var ok) && Number(ok) == 0d));

    /// <summary>Writes the batch's first <paramref name="max"/> documents as an array; returns how many it left out.</summary>
    private static int WriteBatch(StringBuilder builder, BsonArray batch, int max)
    {
        builder.Append('[');
        var written = 0;
        foreach (var document in batch)
        {
            if (written == max)
                break;
            if (written > 0)
                builder.Append(", ");
            MongoJson.Write(builder, document, binaryPlaceholders: true);
            written++;
        }
        builder.Append(']');
        return batch.Count - written;
    }

    /// <summary>The documents a reply returned: a cursor's batch, a findAndModify's <c>value</c>, a distinct's <c>values</c>.</summary>
    private static bool IsDocuments(BsonElement element, MongoDbOperation operation) =>
        BatchOf(element) is not null
        || (operation == MongoDbOperation.FindAndModify && element.Name == "value")
        || (operation == MongoDbOperation.Distinct && element.Name == "values");

    private static BsonArray? BatchOf(BsonElement element) =>
        element.Name == "cursor" && element.Value is BsonDocument cursor ? Batch(cursor) : null;

    private static BsonArray? Batch(BsonDocument cursor) =>
        cursor.TryGetValue("firstBatch", out var first) && first is BsonArray firstBatch ? firstBatch
        : cursor.TryGetValue("nextBatch", out var next) && next is BsonArray nextBatch ? nextBatch
        : null;

    private static string? Text(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && value.IsString ? value.AsString : null;

    /// <summary>A number the server wrote as a double, an integer or a boolean; null for anything else, which no rule reads as a failure.</summary>
    private static double? Number(BsonValue value) => value.BsonType switch
    {
        BsonType.Double => value.AsDouble,
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Boolean => value.AsBoolean ? 1d : 0d,
        _ => null,
    };
}
