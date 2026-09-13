using Kronikol.Reports.Merge;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// The merge could not read a shard from any tracker that is not HTTP.
///
/// <para>`method` in the report is a **label**, not a verb — <c>DiagramMethod</c>'s own documentation says
/// so, and names <c>"Cache Get (Hit)"</c> as an example. Every non-HTTP tracker in the repo writes one:
/// <c>SELECT FROM CUSTOMERS</c> from SQL, <c>GET (Hit)</c> from Redis, <c>Sub/Method [unary]</c> from
/// gRPC, and an **empty string** on a SQL response. The reader handed all of them to
/// <c>HttpMethod.Parse</c>, which is total only over RFC-7230 tokens.</para>
///
/// <para>Two different failures came out of that, neither of which named the file it happened in. A
/// spaced label threw <c>FormatException</c>, caught one level up and reported as
/// <c>Failed to read a report: The format of the HTTP method is invalid.</c> — out of N shards, with no
/// clue which. An empty method threw <c>ArgumentException</c>, which no catch clause covered at all: an
/// unhandled exception, a stack trace and exit 127.</para>
///
/// <para>It survived because only one interaction shape escaped: an <c>Event</c>, which the reader
/// exempted explicitly. Every fixture in this repo that carries a spaced method carries it on an event,
/// so the corpus never exercised the other branch.</para>
/// </summary>
public class MergeReadsEveryTrackersMethodTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-method-" + Guid.NewGuid().ToString("N"));

    public MergeReadsEveryTrackersMethodTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("SELECT FROM CUSTOMERS", "SQL, Summarised verbosity")]
    [InlineData("GET (Hit)", "Redis")]
    [InlineData("Orders/Place [unary]", "gRPC")]
    [InlineData("Blob Upload", "cloud storage")]
    [InlineData("", "a SQL response, which carries no label at all")]
    public void A_shard_from_a_non_http_tracker_merges(string method, string tracker)
    {
        WriteShard("runner1.json", method);

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, $"{tracker}: exit {exit}: {error}");
        Assert.True(File.Exists(Path.Combine(_dir, "Combined.json")), tracker);
    }

    /// <summary>
    /// The label has to survive the round trip exactly: it is what the diagram renders, and it is the
    /// tracker's own text.
    /// </summary>
    /// <remarks>
    /// It did not, for a second reason that only showed once the crash was fixed: all three data writers
    /// upper-cased the whole method slot, so <c>GET (Hit)</c> came back <c>GET (HIT)</c>. For a real verb
    /// that is a no-op; for a label it is the writer rewriting content the run supplied. The schema's own
    /// description of the field names <c>Publish</c> as an example value, in mixed case.
    /// </remarks>
    [Theory]
    [InlineData("GET (Hit)")]
    [InlineData("Cache Get (Miss)")]
    [InlineData("Publish")]
    public void The_label_reaches_the_merged_file_unchanged(string label)
    {
        WriteShard("runner1.json", label);

        Assert.Equal(0, Merge().Exit);

        Assert.Contains(label, File.ReadAllText(Path.Combine(_dir, "Combined.json")), StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty method is what a SQL response legitimately carries, and it must round-trip as empty
    /// rather than being substituted. The ingestion path's <c>ParseMethod</c> maps it to <c>"CALL"</c>,
    /// which is right when synthesising a record from a capture and wrong when reading back a file that
    /// already holds the answer.
    /// </summary>
    [Fact]
    public void An_empty_method_round_trips_as_empty_rather_than_being_invented()
    {
        WriteShard("runner1.json", "");

        Assert.Equal(0, Merge().Exit);

        Assert.DoesNotContain("\"method\": \"CALL\"", File.ReadAllText(Path.Combine(_dir, "Combined.json")), StringComparison.Ordinal);
    }

    /// <summary>A genuine HTTP verb still becomes an <c>HttpMethod</c>, not a lookalike string.</summary>
    [Fact]
    public void A_real_verb_is_still_read_as_a_verb()
    {
        WriteShard("runner1.json", "POST");

        Assert.Equal(0, Merge().Exit);

        var report = MergeableReportReader.Parse(File.ReadAllText(Path.Combine(_dir, "Combined.json")));
        var interaction = report.Interactions.First();
        Assert.IsType<HttpMethod>(interaction.Method.Value);
    }

    /// <summary>
    /// Whatever else it does, a failure has to name the file. `Failed to read a report: The format of the
    /// HTTP method is invalid.` out of fifteen shards is not a diagnosis.
    /// </summary>
    [Fact]
    public void A_shard_that_cannot_be_read_names_itself()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.json"), """
            { "mergeableFormatVersion": 999, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z", "features": [] }
            """);

        var (exit, _, error) = Merge();

        Assert.NotEqual(0, exit);
        Assert.Contains("broken.json", error, StringComparison.Ordinal);
    }

    private (int Exit, string Output, string Error) Merge()
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var exit = Commands.Dispatch(["merge", _dir, "-o", Path.Combine(_dir, "Combined.html")], outWriter, errWriter);
        return (exit, outWriter.ToString(), errWriter.ToString());
    }

    /// <summary>
    /// A shard written by hand, because the point is the file a third-party or non-HTTP tracker produces
    /// and the .NET writer would not necessarily produce this exact shape from a constructed run.
    /// </summary>
    private void WriteShard(string name, string method)
    {
        var encoded = System.Text.Json.JsonSerializer.Serialize(method);
        File.WriteAllText(Path.Combine(_dir, name), $$"""
            {
              "mergeableFormatVersion": 1,
              "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:01:00Z",
              "features": [
                { "name": "Orders", "scenarios": [
                  { "id": "r1s1", "name": "Place order", "result": "Passed", "durationSeconds": 1.0,
                    "httpInteractions": [
                      { "type": "Request", "metaType": "Default", "method": {{encoded}},
                        "uri": "sqlserver://orders/customers", "serviceName": "CustomersDb",
                        "callerName": "Tests", "requestResponseId": "8c96e4b8-8eb0-47f7-a357-b37d8bbb8f07",
                        "timestamp": "2026-01-01T10:00:01Z" }
                    ] }
                ] }
              ]
            }
            """);
    }
}
