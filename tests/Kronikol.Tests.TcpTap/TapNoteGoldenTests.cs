using System.Runtime.CompilerServices;
using Kronikol.Extensions.TcpTap;
using MongoDB.Bson;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// The tap compiles against MongoDB.Bson 2.x and runs on whichever Bson its host resolves; its notes must not depend on
/// which (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, F1). This file runs in Kronikol.Tests.TcpTap, on Bson 2.30, and in
/// Kronikol.Tests.TcpTap.Bson3, on Bson 3.x, each against the one golden below.
/// </summary>
/// <remarks>
/// The golden holds both sides: the label and the request note (Kronikol's writer since 4.20.0, the Raw label's filter
/// included), and the reply's note, status and error.
/// </remarks>
public class TapNoteGoldenTests
{
    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "MongoTapNotes.golden.txt");

    private static string OneLine(string? text) => text is null ? "-" : text.Replace("\n", "\\n");

    [Fact]
    public void The_tap_writes_the_same_bytes_on_bson_2_and_3()
    {
        var lines = new List<string>();
        foreach (var shape in MongoWireShapes.All)
        {
            foreach (var verbosity in new[] { TapVerbosity.Detailed, TapVerbosity.Raw })
            {
                var (request, response) = MongoWireShapes.Tap(shape, verbosity);
                lines.Add($"{verbosity} | {shape.Section} | {shape.CommandName}");
                lines.Add($"  label {OneLine(request.Method.Value?.ToString())}");
                lines.Add($"  request {OneLine(request.Content)}");
                lines.Add($"  status {response.StatusCode?.Value} | error {OneLine(response.Error)}");
                lines.Add($"  reply {OneLine(response.Content)}");
            }
        }

        var bson = typeof(BsonDocument).Assembly.GetName().Version!;
        if (Environment.GetEnvironmentVariable("KRONIKOL_WRITE_MONGO_TAP_GOLDEN") == "1")
            File.WriteAllText(GoldenPath(), string.Join("\n", lines) + "\n");

        var golden = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "MongoTapNotes.golden.txt"));
        Assert.True(golden.Length > 0, "the golden is empty");
        var firstDifference = Enumerable.Range(0, Math.Max(golden.Length, lines.Count))
            .FirstOrDefault(i => i >= golden.Length || i >= lines.Count || golden[i] != lines[i], -1);
        if (firstDifference >= 0)
        {
            Assert.Fail(
                $"On MongoDB.Bson {bson} line {firstDifference + 1} differs from the golden:\n" +
                $"  golden: {(firstDifference < golden.Length ? golden[firstDifference] : "(none)")}\n" +
                $"  ours:   {(firstDifference < lines.Count ? lines[firstDifference] : "(none)")}");
        }
    }
}
