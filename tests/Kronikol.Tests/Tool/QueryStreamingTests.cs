using System.Net;
using System.Text;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tool.Query;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The claim the whole tool rests on: a report is answered without being held in memory.
///
/// <para>Every other fact about <c>query</c> would still pass if <c>ReportScanner</c> called
/// <c>File.ReadAllText</c> — the answers would be identical and the tests green, right up to the real
/// 10 MB report that the plan says is the point, and past it to the 130 MB one a long CI run produces.
/// So the property under test here is not what the answers say, it is what it costs to produce them:
/// the index holds byte offsets, never payload text, and what it allocates tracks the number of
/// interactions rather than the size of the file.</para>
///
/// <para>Measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/> rather than working set:
/// these tests share a parallel xunit process with four thousand others, so process-wide memory says
/// more about the neighbours than about the scan. Allocation on the scanning thread is the scan's own.</para>
/// </summary>
public class QueryStreamingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-streaming").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The corpus <c>tools/query-bench</c> generates: about 143 MB, gitignored, and rebuilt with the one
    /// command named in the skip message. Null when it is not on this machine.
    /// </summary>
    private static string? BenchCorpus()
    {
        var path = Path.Combine(RepoRoot, "tools", "query-bench", "TestRunReport.query-bench.json");
        return File.Exists(path) && new FileInfo(path).Length >= 100L * 1024 * 1024 ? path : null;
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Kronikol.sln not found above the test output directory.");
        }
    }

    [Fact]
    public void Scanning_costs_a_fraction_of_the_file_rather_than_a_copy_of_it()
    {
        // Bodies dominate this file by design: 8 KB responses against an index entry of a few dozen bytes.
        // A scanner that materialised the file would allocate more than the file itself - a UTF-16 string
        // is two bytes per ASCII character before anything is parsed out of it.
        var report = Synthesize("Streamed.json", scenarios: 30, callsEach: 20);
        var size = new FileInfo(report).Length;
        Assert.True(size > 4L * 1024 * 1024, $"the fixture is only {size / 1024} KB - too small to tell a streamed scan from a loaded one");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var index = ReportScanner.Scan(report);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(30, index.Scenarios.Count);
        // Measured at about a sixth of the file. The bound is a third: loose enough not to fail on a GC
        // detail, tight enough that the defect this found - a UTF-16 copy of every body, which put the
        // figure at 2.2x the file - cannot come back unnoticed.
        Assert.True(allocated < size / 3,
            $"scanning a {size / (1024 * 1024)} MB report allocated {allocated / (1024 * 1024)} MB - the scan is not streaming");
    }

    [Fact]
    public void What_scanning_costs_tracks_the_number_of_interactions_not_the_size_of_the_bodies()
    {
        // The sharper form of the same claim, and the one a fixed ratio cannot fake: hold the interaction
        // count still, multiply the payload bytes, and the cost must barely move. A loading scanner's cost
        // would rise with the file.
        var small = Synthesize("Small.json", scenarios: 10, callsEach: 10, fillerFields: 10);
        var large = Synthesize("Large.json", scenarios: 10, callsEach: 10, fillerFields: 400);

        var grew = (double)new FileInfo(large).Length / new FileInfo(small).Length;
        Assert.True(grew > 8, $"the two fixtures differ by only {grew:0.#}x - the comparison proves nothing");

        var smallCost = CostOfScanning(small);
        var largeCost = CostOfScanning(large);

        Assert.True(largeCost < smallCost * 2,
            $"bodies grew {grew:0.#}x and the scan's cost grew {(double)largeCost / smallCost:0.#}x "
            + "- payload bytes are reaching the index");
    }

    [Fact]
    public void The_index_holds_offsets_rather_than_the_payloads_they_point_at()
    {
        // The mechanism behind both measurements, asserted directly so a regression names itself rather
        // than showing up as a number that drifted.
        var index = ReportScanner.Scan(Synthesize("Offsets.json", scenarios: 3, callsEach: 4));

        Assert.NotEmpty(index.Bodies);
        foreach (var body in index.Bodies.Values)
        {
            Assert.True(body.First.Exists);
            Assert.True(body.First.Offset > 0);
            Assert.True(body.Length > 0);
        }

        // And the payload is still reachable - streaming is only a virtue if the data is not lost.
        var first = index.Bodies.Values.First();
        Assert.Contains("\"unique\"", PayloadReader.Read(index, first.First));
    }

    [Fact]
    public void A_hundred_megabyte_report_answers_from_the_index_without_being_read_into_memory()
    {
        // REPORT_QUERY_PLAN section 3.6's >100 MB case. The corpus is gitignored because it is 143 MB;
        // when it is absent the three facts above still hold the streaming property at a smaller scale,
        // so this one confirms it at the size that motivated the design rather than being its only guard.
        if (BenchCorpus() is not { } corpus)
        {
            Assert.Skip("no query-bench corpus on this machine - generate it with: "
                        + "dotnet run -c Release --project tools/query-bench/gen");
            return;
        }

        var size = new FileInfo(corpus).Length;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var index = ReportScanner.Scan(corpus);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(index.Scenarios.Count >= 200);
        // 32 MB against a 142 MB file when this was written; 324 MB before the body-copy fix.
        Assert.True(allocated < size / 3,
            $"scanning {size / (1024 * 1024)} MB allocated {allocated / (1024 * 1024)} MB");

        // And the answers hold their budget on a file 20,000 times bigger than the budget.
        foreach (var command in new[] { "summary", "scenarios", "services", "failures" })
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = QueryCommand.Run([command, corpus], output, error);

            Assert.True(exit == 0, $"{command}: {error}");
            Assert.True(Encoding.UTF8.GetByteCount(output.ToString()) <= 6400,
                $"{command} produced {Encoding.UTF8.GetByteCount(output.ToString())} bytes");
        }
    }

    /// <summary>Allocation on this thread for one scan, with the JIT and the statics already warm.</summary>
    private static long CostOfScanning(string report)
    {
        ReportScanner.Scan(report);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ReportScanner.Scan(report);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// A report whose size is set by <paramref name="fillerFields"/> and whose index size is set by
    /// <paramref name="scenarios"/> and <paramref name="callsEach"/> — the two knobs the measurements
    /// need to move independently. Every body is unique, so dedup wins nothing and the file is as large
    /// as the numbers say.
    /// </summary>
    private string Synthesize(string fileName, int scenarios, int callsEach, int fillerFields = 100)
    {
        var filler = string.Join(",", Enumerable.Range(0, fillerFields).Select(i =>
            $"\"field{i:D3}\":\"value value value value value value value value {i:D4}\""));

        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var features = new List<Feature>();
        var logs = new List<RequestResponseLog>();

        for (var s = 0; s < scenarios; s++)
        {
            var id = $"t{s}";
            features.Add(new Feature
            {
                DisplayName = $"Feature {s / 5}",
                Scenarios = [new Scenario { Id = id, DisplayName = $"Scenario {s}", Result = ExecutionResult.Passed }]
            });

            for (var i = 0; i < callsEach; i++)
            {
                var n = s * callsEach + i;
                var at = start.AddMilliseconds(n * 3);
                var pair = Guid.NewGuid();
                var trace = Guid.NewGuid();

                logs.Add(new RequestResponseLog(id, id, HttpMethod.Post,
                    $"{{\"unique\":\"req-{n:D6}\"}}", new Uri("http://payments/charge"), [],
                    "payments", "test", RequestResponseType.Request, trace, pair, false) { Timestamp = at });
                logs.Add(new RequestResponseLog(id, id, HttpMethod.Post,
                    $"{{\"unique\":\"resp-{n:D6}\",{filler}}}", new Uri("http://payments/charge"), [],
                    "payments", "test", RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK)
                { Timestamp = at.AddMilliseconds(2) });
            }
        }

        var written = ReportGenerator.GenerateTestRunReportData(
            [.. features],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
            "Streaming_" + Guid.NewGuid().ToString("N")[..8] + ".json",
            DataFormat.Json, diagrams: null, trackedLogs: [.. logs]);

        var path = Path.Combine(_directory, fileName);
        File.Move(written, path, overwrite: true);
        return path;
    }
}
