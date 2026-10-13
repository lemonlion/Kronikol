using System.Text.Json;
using Kronikol.Extensions.TcpTap;
using Kronikol.Tracking;
using MongoDB.Bson;

namespace Kronikol.Tests.TcpTap;

/// <summary>A command and its reply as the driver raised them.</summary>
internal sealed record WireShape(string Section, string CommandName, string Database, BsonDocument Command, BsonDocument Reply, bool Failed);

/// <summary>
/// Real commands and replies: what MongoDB.Driver 3.12.0 raised against mongo 8.0.32 for every operation in the plan
/// harness's capture (plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/b-wire-shapes, extracted into MongoWireShapes.json),
/// with the driver's own fields, the server's bookkeeping, write errors and a failed command.
/// </summary>
internal static class MongoWireShapes
{
    public static readonly IReadOnlyList<WireShape> All = Load();

    private static IReadOnlyList<WireShape> Load()
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MongoWireShapes.json")));
        return file.RootElement.GetProperty("pairs").EnumerateArray()
            .Select(pair => new WireShape(
                pair.GetProperty("section").GetString()!,
                pair.GetProperty("commandName").GetString()!,
                pair.GetProperty("database").GetString()!,
                BsonDocument.Parse(pair.GetProperty("command").GetString()!),
                BsonDocument.Parse(pair.GetProperty("reply").GetString()!),
                pair.GetProperty("failed").GetBoolean()))
            .ToArray();
    }

    /// <summary>The fields the driver sends as OP_MSG kind-1 document sequences, which its event folds into the command.</summary>
    private static readonly string[] DocumentSequences = ["documents", "updates", "deletes", "ops", "nsInfo"];

    /// <summary>Runs a tap over a shape, its document sequences sent as kind-1 sections as the driver sends them.</summary>
    public static (RequestResponseLog Request, RequestResponseLog Response) Tap(WireShape shape, TapVerbosity verbosity)
    {
        var body = new BsonDocument();
        var sequences = new List<(string, BsonDocument[])>();
        foreach (var element in shape.Command)
        {
            if (DocumentSequences.Contains(element.Name) && element.Value is BsonArray documents)
                sequences.Add((element.Name, documents.Select(d => d.AsBsonDocument).ToArray()));
            else
                body.Add(element.Name, element.Value);
        }

        using var harness = DecoderHarness.Mongo(o =>
        {
            o.Verbosity = verbosity;
            o.TrackGetMore = true;
        });
        harness.ClientToServer(MongoWire.Msg(1, 0, body, 0, [.. sequences]));
        harness.ServerToClient(MongoWire.Msg(2, 1, shape.Reply));
        return (Assert.Single(harness.Sink.Requests), Assert.Single(harness.Sink.Responses));
    }
}
