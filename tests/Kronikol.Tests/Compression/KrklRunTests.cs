using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Compression;

/// <summary>
/// What else a run that writes <c>TestRunReport.krkl</c> changes (plans/REPORT_FORMAT_PLAN.md 4.5 and 4.6; section 5, R4
/// items 6, 7 and 8): the run before it moves into <c>runs/</c> with its JSON, the agent files and <c>Failures.md</c> name
/// the file the run wrote, and the file is a small fraction of the JSON.
/// </summary>
[Collection("DiagramsFetcher")]
public class KrklRunTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-krkl-run").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_run_before_the_first_krkl_moves_into_runs_with_its_json(bool withManifest)
    {
        var directory = Path.Combine(_root, withManifest ? "manifest" : "bare");
        Run(directory, compress: false, runId: "first");
        if (!withManifest)
            File.Delete(Path.Combine(directory, RunManifest.FileName));

        Run(directory, compress: true, runId: "second");

        Assert.True(File.Exists(Path.Combine(directory, "TestRunReport.krkl")));
        Assert.False(File.Exists(Path.Combine(directory, "TestRunReport.json")), "the earlier run's JSON stayed on top beside the new .krkl");
        Assert.Single(Directory.EnumerateFiles(Path.Combine(directory, ReportFolders.RunsFolderName), "TestRunReport.json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_run_after_the_last_krkl_moves_it_into_runs(bool withManifest)
    {
        var directory = Path.Combine(_root, withManifest ? "back-manifest" : "back-bare");
        Run(directory, compress: true, runId: "first");
        if (!withManifest)
            File.Delete(Path.Combine(directory, RunManifest.FileName));

        Run(directory, compress: false, runId: "second");

        Assert.True(File.Exists(Path.Combine(directory, "TestRunReport.json")));
        Assert.False(File.Exists(Path.Combine(directory, "TestRunReport.krkl")), "the earlier run's .krkl stayed on top beside the new JSON");
        Assert.Single(Directory.EnumerateFiles(Path.Combine(directory, ReportFolders.RunsFolderName), "TestRunReport.krkl", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_no_runs_kept_the_earlier_run_s_data_file_under_the_other_name_does_not_stay(bool krklFirst)
    {
        // KeepRuns = 0 rotates nothing, so until 5.2.7 the other name, a file the run replaces, stayed on top for good: every
        // lookup said the folder holds both, the CI upload took it, and a lookup took the newer by time, which a download of
        // the artifacts scrambles.
        var directory = Path.Combine(_root, krklFirst ? "zero-back" : "zero");
        Run(directory, compress: krklFirst, runId: "first", keepRuns: 0);

        Run(directory, compress: !krklFirst, runId: "second", keepRuns: 0);

        var (written, earlier) = krklFirst ? ("TestRunReport.json", "TestRunReport.krkl") : ("TestRunReport.krkl", "TestRunReport.json");
        Assert.True(File.Exists(Path.Combine(directory, written)));
        Assert.False(File.Exists(Path.Combine(directory, earlier)), $"the earlier run's {earlier} stayed on top beside the new {written}");
        Assert.False(Directory.Exists(Path.Combine(directory, ReportFolders.RunsFolderName)));
    }

    [Fact]
    public void With_no_runs_kept_another_report_s_data_file_stays()
    {
        // A host may write Checkout.* and Payments.* into one folder: only this report's own other name is this run's to replace.
        var directory = Path.Combine(_root, "zero-two-reports");
        Run(directory, compress: false, runId: "payments", name: "Payments", keepRuns: 0);

        Run(directory, compress: true, runId: "checkout", name: "Checkout", keepRuns: 0);

        Assert.True(File.Exists(Path.Combine(directory, "Payments.json")));
        Assert.True(File.Exists(Path.Combine(directory, "Checkout.krkl")));
    }

    [Fact]
    public void With_no_runs_kept_a_data_file_this_process_may_not_write_stays()
    {
        // The owner's read-only mark means do not touch, as the rotation reads it (RunRotation.Move).
        Assert.SkipUnless(ReadOnlyProbe.RefusesThisProcess, "a read-only file does not refuse this process (root)");
        var directory = Path.Combine(_root, "zero-read-only");
        Run(directory, compress: false, runId: "first", keepRuns: 0);
        var earlier = Path.Combine(directory, "TestRunReport.json");
        File.SetAttributes(earlier, FileAttributes.ReadOnly);
        try
        {
            Run(directory, compress: true, runId: "second", keepRuns: 0);

            Assert.True(File.Exists(earlier));
            Assert.True(File.Exists(Path.Combine(directory, "TestRunReport.krkl")));
        }
        finally
        {
            File.SetAttributes(earlier, FileAttributes.Normal);
        }
    }

    [Fact]
    public void An_earlier_run_without_a_manifest_is_named_for_its_data_file_under_the_other_name()
    {
        // Without Run.json the earlier run is named for its data file's time, else its HTML's (RunRotation.Reconstruct);
        // before a switch, that data file has the other name.
        var directory = Path.Combine(_root, "bare-time");
        Run(directory, compress: false, runId: "first");
        File.Delete(Path.Combine(directory, RunManifest.FileName));
        var dataTime = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(Path.Combine(directory, "TestRunReport.json"), dataTime.UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(directory, "TestRunReport.html"), new DateTime(2002, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        Run(directory, compress: true, runId: "second");

        var moved = Assert.Single(Directory.GetDirectories(Path.Combine(directory, ReportFolders.RunsFolderName)));
        Assert.Equal(HistoryRunId.DirectoryName(HistoryRunBuilder.RunId(null, dataTime)), Path.GetFileName(moved));
    }

    [Fact]
    public void The_agent_files_name_the_krkl_and_say_why_it_is_not_opened()
    {
        var directory = Path.Combine(_root, "agents");
        Run(directory, compress: true, runId: "agents");

        var instructions = File.ReadAllText(Path.Combine(directory, AgentInstructionsGenerator.ClaudeFileName));

        Assert.Contains("`TestRunReport.krkl`", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("`TestRunReport.json`", instructions, StringComparison.Ordinal);
        Assert.Contains("compressed: `kronikol query` reads it", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_md_names_a_krkl_the_folder_lookup_would_not_find()
    {
        var directory = Path.Combine(_root, "named");
        Run(directory, compress: true, runId: "named", name: "Checkout");

        var digest = File.ReadAllText(Path.Combine(directory, ReportGenerator.FailuresDigestFileName));

        Assert.True(File.Exists(Path.Combine(directory, "Checkout.krkl")));
        Assert.Contains("Checkout.krkl", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkout.json", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void A_krkl_is_under_a_tenth_of_the_JSON_the_same_run_writes()
    {
        var json = Path.Combine(_root, "size-json");
        var krkl = Path.Combine(_root, "size-krkl");
        using (RequestResponseLogger.IsolateForTests())
        {
            var ids = LogCalls(scenarios: 40);
            Write(json, compress: false, "size", "TestRunReport", ids);
            Write(krkl, compress: true, "size", "TestRunReport", ids);
        }

        var jsonBytes = new FileInfo(Path.Combine(json, "TestRunReport.json")).Length;
        var krklBytes = new FileInfo(Path.Combine(krkl, "TestRunReport.krkl")).Length;

        Assert.True(krklBytes * 10 < jsonBytes, $"{krklBytes:N0} bytes against {jsonBytes:N0}");
    }

    [Fact]
    public void An_absence_check_on_a_krkl_s_raw_bytes_passes_whatever_the_file_holds()
    {
        // The wiki's old recipe searched the data file's text for a secret (F13). On a .krkl that finds nothing, whether the
        // secret is there or not: a check that cannot fail checks nothing, which is why the recipe reads through the tool.
        var directory = Path.Combine(_root, "raw");
        using (RequestResponseLogger.IsolateForTests())
        {
            var id = "krkl-raw-" + Guid.NewGuid().ToString("N");
            RequestResponseLogger.LogPair("Place order", id, HttpMethod.Post, new Uri("http://orders/api/orders"), "OrdersApi", "Test",
                requestContent: "{\"password\":\"hunter2-on-disk\"}", statusCode: HttpStatusCode.Created);
            Write(directory, compress: true, "raw", "TestRunReport", [id]);
        }
        var path = Path.Combine(directory, "TestRunReport.krkl");

        Assert.DoesNotContain("hunter2-on-disk", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)), StringComparison.Ordinal);
        Assert.Contains("hunter2-on-disk", ReportFile.Read(path), StringComparison.Ordinal);
    }

    private static void Run(string directory, bool compress, string runId, string name = "TestRunReport", int keepRuns = 3)
    {
        using var _ = RequestResponseLogger.IsolateForTests();
        Write(directory, compress, runId, name, LogCalls(scenarios: 2), keepRuns);
    }

    private static void Write(string directory, bool compress, string runId, string name, string[] ids, int keepRuns = 3)
    {
        Directory.CreateDirectory(directory);
        // A process rotates a directory once; each write here stands for a run of its own.
        RunRotation.ForgetForTests(directory);
        ReportGenerator.CreateStandardReportsWithDiagrams(Features(ids), new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc), new ReportConfigurationOptions
            {
                ReportsFolderPath = directory,
                HtmlTestRunReportFileName = name,
                InternalFlowTracking = false,
                CompressTestRunReport = compress,
                HistoryRunId = runId,
                KeepRuns = keepRuns,
            });
    }

    private static string[] LogCalls(int scenarios)
    {
        var ids = Enumerable.Range(0, scenarios).Select(_ => "krkl-run-" + Guid.NewGuid().ToString("N")).ToArray();
        foreach (var (id, i) in ids.Select((id, i) => (id, i)))
        {
            RequestResponseLogger.LogPair("Place order", id, HttpMethod.Post, new Uri($"http://orders/api/orders/{i}"), "OrdersApi", "Test",
                requestContent: $"{{\"item\":\"widget-{i}\",\"quantity\":{i % 5 + 1},\"note\":null,\"tags\":[]}}",
                responseContent: $"{{\"id\":\"order-{i}\",\"status\":\"placed\",\"lines\":[{{\"item\":\"widget-{i}\",\"price\":12.50}}],\"links\":[]}}",
                statusCode: HttpStatusCode.Created);
            RequestResponseLogger.LogPair("Place order", id, HttpMethod.Get, new Uri($"http://stock/api/stock/{i}"), "StockApi", "OrdersApi",
                responseContent: "{\"inStock\":true,\"count\":12,\"warehouse\":null}", statusCode: HttpStatusCode.OK);
        }
        return ids;
    }

    private static Feature[] Features(string[] ids) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [.. ids.Select((id, i) => new Scenario
            {
                Id = id, DisplayName = $"Place order {i}", Result = i == 0 ? ExecutionResult.Failed : ExecutionResult.Passed,
                ErrorMessage = i == 0 ? "Expected 1 row but found 0" : null,
                Steps =
                [
                    new ScenarioStep { Keyword = "When", Text = $"order {i} is placed", Status = ExecutionResult.Passed },
                    new ScenarioStep
                    {
                        Keyword = "Then", Text = "the order is saved", Status = i == 0 ? ExecutionResult.Failed : ExecutionResult.Passed,
                        FailureMessage = i == 0 ? "Expected 1 row but found 0" : null
                    }
                ]
            })]
        }
    ];
}
