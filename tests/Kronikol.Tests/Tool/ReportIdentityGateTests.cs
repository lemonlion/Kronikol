using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The tool answered questions about files that were not reports.
///
/// <para>Between opening a file and answering, <c>query</c> had three checks: the JSON parses, and two
/// version gates written <c>is { }</c> so they fire only when the key is present. A file declaring
/// neither key passed all three, and the answer came back <c>0 scenarios, all passed</c>, exit 0 — the
/// same words a genuinely green run produces. The most embarrassing instance is
/// <c>TestRunReport.schema.json</c>, which sits in the reports directory beside the report it
/// describes: naming it produced a confident report about nothing.</para>
///
/// <para>The silence was manufactured, not incidental. <c>ReportScanner.Walker.Finish()</c> set
/// <c>Enriched = true</c> precisely when zero scenarios were found, which suppressed the one provenance
/// note that would otherwise have fired. And the hole was in three commands, not one: <c>ctrf</c> shares
/// the scanner but had neither version gate, and <c>diff</c> reaches the scanner by a third path that had
/// no gate and no <c>ResolveReport</c> — while also being the one verb that returns early from
/// <c>WriteProvenance</c>, so it was doubly silent about a file it had never checked.</para>
/// </summary>
public class ReportIdentityGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-gate-" + Guid.NewGuid().ToString("N"));

    public ReportIdentityGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>A real report, minimal but genuine: the three root markers a Kronikol writer always emits.</summary>
    private string AReport(string name = "TestRunReport.json") => Write(name, """
        { "formatVersion": 1, "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
          "features": [ { "name": "Orders", "scenarios": [
            { "id": "t0", "name": "Place an order", "result": "Passed" } ] } ] }
        """);

    // ─── Not a report at all ────────────────────────────────────

    [Theory]
    [InlineData("summary")]
    [InlineData("failures")]
    [InlineData("scenarios")]
    public void Query_refuses_a_file_that_is_not_a_report(string verb)
    {
        // The schema file's own shape: every Kronikol root key it mentions is nested under `properties`,
        // or is a string inside `required`, so the scanner sees none of them at the root.
        var notAReport = Write("TestRunReport.schema.json", """
            { "$schema": "https://json-schema.org/draft/2020-12/schema", "title": "Kronikol test run report",
              "type": "object", "required": ["formatVersion", "startTime", "endTime", "features"],
              "properties": { "formatVersion": { "type": "integer" }, "features": { "type": "array" } } }
            """);

        var (output, error, exit) = Run(verb, notAReport);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("all passed", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TestRunReport.schema.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrf_refuses_a_file_that_is_not_a_report()
    {
        var notAReport = Write("package.json", """{ "name": "widgets", "version": "1.0.0" }""");

        var error = new StringWriter();
        var exit = CtrfCommand.Run([notAReport], new StringWriter(), error);

        Assert.Equal(1, exit);
        Assert.Contains("package.json", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// `diff` takes two files and only the first went through <see cref="QueryCommand.ResolveReport"/>.
    /// The second was scanned raw, so an arbitrary JSON became "the new run" and every scenario in the
    /// real one was reported as removed — a whole-suite regression, from a file that was never a run.
    /// </summary>
    [Fact]
    public void Diff_refuses_a_second_file_that_is_not_a_report()
    {
        var real = AReport();
        var notAReport = Write("tsconfig.json", """{ "compilerOptions": { "strict": true } }""");

        var (output, error, exit) = Run("diff", real, notAReport);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("scenarios", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tsconfig.json", error, StringComparison.Ordinal);
    }

    // ─── A report is still a report ─────────────────────────────

    [Fact]
    public void A_run_with_no_scenarios_is_still_a_report()
    {
        // The gate must key on the contract, not on the content. An empty `features` array is a run that
        // discovered nothing — rare, but a real answer, and the one case the naive "no scenarios means not
        // a report" shortcut would refuse.
        var empty = Write("TestRunReport.json", """
            { "formatVersion": 1, "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
              "features": [] }
            """);

        var (_, error, exit) = Run("summary", empty);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
    }

    [Fact]
    public void A_features_key_that_is_not_an_array_is_not_a_report()
    {
        // `merge` tests key presence on a parsed document; the scanner has to agree with it on exactly the
        // malformed inputs the gate exists to catch, so presence alone is not enough — it must be an array.
        var wrongShape = Write("TestRunReport.json", """
            { "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z", "features": "Orders" }
            """);

        var (_, _, exit) = Run("summary", wrongShape);

        Assert.Equal(1, exit);
    }

    [Fact]
    public void A_report_written_before_the_shape_was_versioned_is_still_read()
    {
        // formatVersion is absent on everything before 3.1.0, and absent is a legitimate state. Gating on
        // it would refuse six hand-written fixtures and eight of the fifteen example reports on disk.
        var old = Write("TestRunReport.json", """
            { "kronikolVersion": "3.0.84", "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Orders", "scenarios": [
                { "id": "t0", "name": "Place an order", "result": "Passed" } ] } ] }
            """);

        var (_, error, exit) = Run("summary", old);

        Assert.Equal(0, exit);
        Assert.Equal("", error);
    }

    // ─── The version gates, which had no negative-path test ─────

    [Fact]
    public void Query_refuses_an_unknown_formatVersion()
    {
        var future = Write("TestRunReport.json", """
            { "formatVersion": 99, "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
              "features": [] }
            """);

        var (_, error, exit) = Run("summary", future);

        Assert.Equal(1, exit);
        Assert.Contains("99", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_refuses_a_formatVersion_that_is_not_a_number()
    {
        var nonsense = Write("TestRunReport.json", """
            { "formatVersion": "one", "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
              "features": [] }
            """);

        var (_, error, exit) = Run("summary", nonsense);

        Assert.Equal(1, exit);
        Assert.Contains("not a number", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ctrf_refuses_an_unknown_formatVersion()
    {
        var future = Write("TestRunReport.json", """
            { "formatVersion": 99, "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:05:00Z",
              "features": [] }
            """);

        var error = new StringWriter();
        var exit = CtrfCommand.Run([future], new StringWriter(), error);

        Assert.Equal(1, exit);
        Assert.Contains("99", error.ToString(), StringComparison.Ordinal);
    }

    private (string Output, string Error, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }
}
