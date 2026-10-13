using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace Bson2Writer;

/// <summary>Writers compiled against MongoDB.Bson 2.30.0; the calling app decides which Bson runs them.</summary>
public static class Writers
{
    /// <summary>The integer the compiler baked in for JsonOutputMode.RelaxedExtendedJson (2.x: 3).</summary>
    public static int RelaxedConstantAsCompiled => (int)JsonOutputMode.RelaxedExtendedJson;

    /// <summary>Today's MongoDbResponseSummary settings: no OutputMode.</summary>
    public static string Today(BsonDocument d) =>
        d.ToJson(new JsonWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n" });

    /// <summary>A fix that names RelaxedExtendedJson in source compiled against 2.x.</summary>
    public static string RelaxedByEnum(BsonDocument d) =>
        d.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson, Indent = true, IndentChars = "  ", NewLineChars = "\n" });

    /// <summary>A fix that also sets the 2.x-only GuidRepresentation property (absent in 3.x).</summary>
    public static string RelaxedWithUnspecifiedGuid(BsonDocument d)
    {
        var s = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson, Indent = true, IndentChars = "  ", NewLineChars = "\n" };
        s.GuidRepresentation = GuidRepresentation.Unspecified;
        return d.ToJson(s);
    }

    /// <summary>A fix that resolves the member by name at run time.</summary>
    public static string RelaxedByName(BsonDocument d)
    {
        var mode = (JsonOutputMode)Enum.Parse(typeof(JsonOutputMode), "RelaxedExtendedJson");
        return d.ToJson(new JsonWriterSettings { OutputMode = mode, Indent = true, IndentChars = "  ", NewLineChars = "\n" });
    }
}
