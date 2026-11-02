using Kronikol.Reports.Merge;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// The merge could not read a shard from any tracker that is not HTTP.
///
/// <para>`method` in the report is a **label**, not a verb — <c>DiagramMethod</c>'s own documentation says
/// so, and names <c>"Cache Get (Hit)"</c> as an example. Every non-HTTP tracker in the repo writes one:
/// <c>SELECT FROM CUSTOMERS</c> from SQL, <c>Get (Hit)</c> from Redis, <c>/orders.Orders/Place [UnaryCall]</c> from
/// gRPC at Raw, and an **empty string** on a SQL response. The reader handed all of them to
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
    [InlineData("Get (Hit)", "Redis")]
    [InlineData("/orders.Orders/Place [UnaryCall]", "gRPC at Raw")]
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

        Assert.Contains(label, ReportHtml.Read(Path.Combine(_dir, "Combined.json")), StringComparison.Ordinal);
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

        Assert.DoesNotContain("\"method\": \"CALL\"", ReportHtml.Read(Path.Combine(_dir, "Combined.json")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A label that is a verb in another casing is still a label: Cosmos DB's <c>Delete</c> and <c>Patch</c>, EF Core's and
    /// BigQuery's <c>Delete</c>. Read as a verb it is written back in capitals, so the merged report names another call than
    /// the run did, and history reads the difference as changed behaviour (plans/CALLS_AND_EVENTS_PLAN.md F1).
    /// </summary>
    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Delete")]
    [InlineData("Patch")]
    [InlineData("Head")]
    [InlineData("Options")]
    [InlineData("Trace")]
    [InlineData("Connect")]
    [InlineData("get")]
    public void A_label_that_is_a_verb_in_another_casing_keeps_its_casing(string label)
    {
        WriteShard("runner1.json", label);

        Assert.Equal(0, Merge().Exit);

        var report = MergeableReportReader.Parse(ReportHtml.Read(Path.Combine(_dir, "Combined.json")));
        Assert.Equal(label, report.Interactions.First().Method.Value);
    }

    /// <summary>The file holds a verb as it was built: history keeps it so, and a merge compares with history.</summary>
    [Theory]
    [InlineData("Patch")]
    [InlineData("patch")]
    [InlineData("Purge")]
    [InlineData("PURGE")]
    [InlineData("GET")]
    public void A_verb_built_by_hand_is_written_as_it_was_built(string verb) =>
        Assert.Equal(verb, Kronikol.Reports.ReportGenerator.MethodText(new HttpMethod(verb)));

    /// <summary>A verb is written in capitals, whatever else the call is, so capitals are what the reader asks for.</summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void Each_verb_in_capitals_is_read_as_a_verb(string verb)
    {
        WriteShard("runner1.json", verb);

        Assert.Equal(0, Merge().Exit);

        var report = MergeableReportReader.Parse(ReportHtml.Read(Path.Combine(_dir, "Combined.json")));
        Assert.Equal(new HttpMethod(verb), Assert.IsType<HttpMethod>(report.Interactions.First().Method.Value));
    }

    /// <summary>A record's kind says whether it moves a message, and nothing about that says how its method is written.</summary>
    [Theory]
    [InlineData("DELETE", "Default", true)]
    [InlineData("DELETE", "Event", true)]
    [InlineData("Delete", "Default", false)]
    [InlineData("Delete", "Event", false)]
    public void The_kind_of_a_call_has_no_say_in_how_its_method_is_read(string method, string kind, bool isVerb)
    {
        WriteShard("runner1.json", method, kind);

        Assert.Equal(0, Merge().Exit);

        var read = MergeableReportReader.Parse(ReportHtml.Read(Path.Combine(_dir, "Combined.json"))).Interactions.First();
        Assert.Equal(Enum.Parse<Kronikol.Tracking.RequestResponseMetaType>(kind), read.MetaType);
        if (isVerb)
            Assert.Equal(new HttpMethod(method), Assert.IsType<HttpMethod>(read.Method.Value));
        else
            Assert.Equal(method, Assert.IsType<string>(read.Method.Value));
    }

    /// <summary>A genuine HTTP verb still becomes an <c>HttpMethod</c>, not a lookalike string.</summary>
    [Fact]
    public void A_real_verb_is_still_read_as_a_verb()
    {
        WriteShard("runner1.json", "POST");

        Assert.Equal(0, Merge().Exit);

        var report = MergeableReportReader.Parse(ReportHtml.Read(Path.Combine(_dir, "Combined.json")));
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
    private void WriteShard(string name, string method, string kind = "Default")
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
                      { "type": "Request", "metaType": "{{kind}}", "method": {{encoded}},
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
