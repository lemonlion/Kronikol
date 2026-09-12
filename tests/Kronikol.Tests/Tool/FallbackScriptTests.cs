using System.Net;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The skill ships <c>scripts/query.py</c> for machines with no .NET tool. It is a fallback, and the
/// failure mode of a fallback is that nobody exercises it: it can rot for a year and only be found by the
/// one person who reaches for it, at the moment they had already run out of other options.
///
/// <para>So it is run against the same report as the real tool and made to agree. Not on formatting - the
/// two deliberately print differently - but on the numbers, which is what "degrades to fewer answers, not
/// to wrong ones" has to mean.</para>
/// </summary>
public class FallbackScriptTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-fallback").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ScriptPath => Path.Combine(
        RepoRoot, "templates", "skills", "kronikol-test-debugging", "scripts", "query.py");

    // ─── The script's own documentation ────────────────────────

    [Fact]
    public void The_docstring_names_exactly_the_commands_the_script_registers()
    {
        var source = File.ReadAllText(ScriptPath);

        var registered = Regex.Matches(source, @"^    ""([a-z]+)"": cmd_", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(registered);

        var docstring = source[..source.IndexOf("\"\"\"", source.IndexOf("\"\"\"", StringComparison.Ordinal) + 3,
            StringComparison.Ordinal)];

        var advertised = Regex.Matches(docstring, @"^    python query\.py ([a-z]+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(registered, advertised);
    }

    [Fact]
    public void The_docstring_does_not_promise_a_command_the_script_lacks()
    {
        var source = File.ReadAllText(ScriptPath);
        var registered = Regex.Matches(source, @"^    ""([a-z]+)"": cmd_", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // The "only in the real tool" list is the script telling the reader where its edges are. A verb on
        // both sides of that line is worse than a missing one: it sends them to the tool they do not have.
        var missing = Regex.Match(source, @"missing from this fallback\):\s*\n((?:\s{4}.+\n)+)");
        Assert.True(missing.Success, "query.py no longer says which commands it does not implement.");

        var claimed = Regex.Matches(missing.Groups[1].Value, @"[a-z]+")
            .Select(m => m.Value)
            .Where(registered.Contains)
            .ToList();

        Assert.True(claimed.Count == 0,
            "query.py lists as unimplemented commands it actually has: " + string.Join(", ", claimed));
    }

    // ─── Agreement with the real tool ──────────────────────────

    [Fact]
    public void It_counts_the_same_scenarios_and_failures_as_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        // query.py has no --count, so its numbers are parsed out of the header it prints.
        var summary = PythonProbe.Run(ScriptPath, "summary", report);
        var counts = Regex.Match(summary, @"(\d+) scenarios . (\d+) failed");
        Assert.True(counts.Success, "query.py summary no longer prints 'N scenarios · N failed':\n" + summary);

        Assert.Equal(Tool("summary", report, "--count").Trim(), counts.Groups[1].Value);
        Assert.Equal(Tool("failures", report, "--count").Trim(), counts.Groups[2].Value);
    }

    [Fact]
    public void Its_failures_footer_agrees_with_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var failures = PythonProbe.Run(ScriptPath, "failures", report);
        var footer = Regex.Match(failures, @"(\d+) failed");
        Assert.True(footer.Success, "query.py failures no longer prints an 'N failed' footer:\n" + failures);

        Assert.Equal(Tool("failures", report, "--count").Trim(), footer.Groups[1].Value);
    }

    [Fact]
    public void It_addresses_scenarios_the_same_way_the_tool_does()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        // An address the fallback prints has to be one the real tool accepts, or the two cannot be used in
        // the same session - and the whole point of the fallback is that you reach for it mid-investigation.
        var address = Regex.Match(PythonProbe.Run(ScriptPath, "summary", report), @"^  (s\d+)\s", RegexOptions.Multiline);
        Assert.True(address.Success, "query.py summary printed no scenario address");

        var scoped = Tool("steps", report, address.Groups[1].Value);
        Assert.Contains("the total is right", scoped, StringComparison.Ordinal);
        Assert.Contains(address.Groups[1].Value, PythonProbe.Run(ScriptPath, "steps", report, address.Groups[1].Value),
            StringComparison.Ordinal);
    }

    [Fact]
    public void It_names_the_same_services_as_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var report = WriteReport();

        var fallback = PythonProbe.Run(ScriptPath, "services", report);
        foreach (var service in new[] { "Pricing", "Inventory" })
            Assert.Contains(service, fallback, StringComparison.Ordinal);
    }

    // ─── Harness ───────────────────────────────────────────────

    private string Tool(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private string WriteReport()
    {
        var written = ReportGenerator.GenerateTestRunReportData(
            Features(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Fallback_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, Logs());

        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Browse the catalogue", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1.2),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "browsing", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t1", DisplayName = "Checkout fails on a wrong total", Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(3.5), ErrorMessage = "Assert.Equal() Failure",
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Failed,
                            FailureMessage = "Expected 4173 but found 3902",
                            SourceFile = "OverviewTests.cs", SourceLine = 142
                        }
                    ]
                }
            ]
        }
    ];

    /// <summary>One request and one response per service, paired by RequestResponseId, so `services` has
    /// something to count on both sides.</summary>
    private static RequestResponseLog[] Logs()
    {
        var start = new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();

        foreach (var (service, uri, offset) in new[]
                 {
                     ("Pricing", "http://pricing/total", 0),
                     ("Inventory", "http://inventory/stock", 1)
                 })
        {
            var pairId = Guid.NewGuid();
            logs.Add(new RequestResponseLog("Orders", "t1", HttpMethod.Get, "{}", new Uri(uri), [],
                service, "Checkout fails on a wrong total", RequestResponseType.Request, Guid.NewGuid(), pairId, false)
            { Timestamp = start.AddSeconds(offset) });
            logs.Add(new RequestResponseLog("Orders", "t1", HttpMethod.Get, "{\"total\":3902}", new Uri(uri), [],
                service, "Checkout fails on a wrong total", RequestResponseType.Response, Guid.NewGuid(), pairId, false,
                HttpStatusCode.OK)
            { Timestamp = start.AddSeconds(offset).AddMilliseconds(20) });
        }

        return [.. logs];
    }
}
