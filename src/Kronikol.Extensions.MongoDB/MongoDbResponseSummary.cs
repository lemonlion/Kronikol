using global::MongoDB.Bson;
using global::MongoDB.Bson.IO;

// Shared source: also compiled into Kronikol.Extensions.TcpTap (and Kronikol.Extensions.MongoDB.V2) as a
// linked file, so the wire tap renders reply notes byte-identically to the in-process subscriber. The tap
// compiles it into its own namespace so a host can reference both packages.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// Renders the note text for a MongoDB reply document: the write metadata (<c>n</c>, <c>nModified</c>,
/// <c>nUpserted</c>) and, optionally, the documents in <c>cursor.firstBatch</c>.
/// </summary>
/// <remarks>
/// Pure — it needs only <c>MongoDB.Bson</c>, so both the driver-event subscriber
/// (<c>MongoDbTrackingSubscriber</c>) and the out-of-process wire tap (<c>MongoTap</c>) produce the same text.
/// </remarks>
public static class MongoDbResponseSummary
{
    /// <summary>Write metadata as <c>n=1, nModified=1</c>, or null when the reply carries none.</summary>
    public static string? ExtractMetadata(BsonDocument? reply)
    {
        if (reply is null) return null;

        var parts = new List<string>();

        if (reply.TryGetValue("n", out var n) && n.IsInt32)
            parts.Add($"n={n.AsInt32}");
        if (reply.TryGetValue("nModified", out var nModified) && nModified.IsInt32)
            parts.Add($"nModified={nModified.AsInt32}");
        if (reply.TryGetValue("nUpserted", out var nUpserted) && nUpserted.IsInt32 && nUpserted.AsInt32 > 0)
            parts.Add($"nUpserted={nUpserted.AsInt32}");

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    /// <summary>
    /// Metadata plus (when <paramref name="logResponseContent"/> is true) up to
    /// <paramref name="maxResponseDocuments"/> pretty-printed documents from <c>cursor.firstBatch</c>.
    /// </summary>
    public static string? ExtractDetailed(BsonDocument? reply, bool logResponseContent, int maxResponseDocuments)
    {
        var metadata = ExtractMetadata(reply);

        if (!logResponseContent || reply is null)
            return metadata;

        // Extract documents from cursor.firstBatch (find, aggregate, listCollections, etc.)
        if (reply.TryGetValue("cursor", out var cursor) && cursor.IsBsonDocument)
        {
            var cursorDoc = cursor.AsBsonDocument;
            if (cursorDoc.TryGetValue("firstBatch", out var firstBatch) && firstBatch.IsBsonArray)
            {
                var docs = firstBatch.AsBsonArray;
                if (docs.Count > 0)
                {
                    var jsonSettings = new JsonWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n" };
                    var formattedDocs = docs.Take(maxResponseDocuments)
                        .Select(d =>
                        {
                            var json = d.ToJson(jsonSettings);
                            return "  " + json.Replace("\n", "\n  ");
                        })
                        .ToList();
                    var formattedJson = "[\n" + string.Join(",\n", formattedDocs) + "\n]";

                    var docText = formattedJson;
                    if (docs.Count > maxResponseDocuments)
                        docText += $"\n... ({docs.Count - maxResponseDocuments} more documents not shown)";

                    return metadata is not null ? $"{metadata}\n{docText}" : docText;
                }

                var emptyText = "0 documents";
                return metadata is not null ? $"{metadata}\n{emptyText}" : emptyText;
            }
        }

        return metadata;
    }
}
