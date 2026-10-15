using global::MongoDB.Bson;
using global::MongoDB.Bson.IO;

// Shared source: also compiled into Kronikol.Extensions.TcpTap (and Kronikol.Extensions.MongoDB.V2) as a
// linked file. Neither capture has called it since 4.19.6 (MongoDbNotes writes every reply note); it stays, obsolete
// since 4.20.0, in all three packages until 5.0 removes it, the tap's public copy
// (Kronikol.Extensions.TcpTap.Protocols.MongoDbResponseSummary) included. The tap compiles it into its own namespace so
// a host can reference both packages.
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
/// Pure: it needs only <c>MongoDB.Bson</c>. Kronikol's own captures (<c>MongoDbTrackingSubscriber</c> and the wire
/// tap's <c>MongoTap</c>) no longer use it: their reply notes are relaxed extended JSON, with a write's counts in the
/// JSON, and a reply's documents written the same way on MongoDB.Bson 2.x and 3.x. It stays, unchanged, for code that
/// calls it, marked obsolete, and is removed in 5.0.
/// </remarks>
public static class MongoDbResponseSummary
{
    /// <summary>Write metadata as <c>n=1, nModified=1</c>, or null when the reply carries none.</summary>
    [Obsolete("Kronikol's MongoDB notes are relaxed extended JSON since 4.19.6 (MongoDbTrackingSubscriber and MongoTap), with a write's counts in the JSON; this text form is no longer used, and is removed in 5.0.")]
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
    [Obsolete("Kronikol's MongoDB notes are relaxed extended JSON since 4.19.6 (MongoDbTrackingSubscriber and MongoTap), with a write's counts in the JSON; this text form is no longer used, and is removed in 5.0.")]
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
