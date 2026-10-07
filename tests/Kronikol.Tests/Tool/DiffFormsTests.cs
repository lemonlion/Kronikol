using System.Text.Json;
using Kronikol.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// What each form of <c>diff</c> reads, and the refusal of everything else.
///
/// <para><c>diff</c> picks its form from its first positional - two bodies in one report, two runs, or a
/// body across two runs - and read nothing the other forms take, so a flag or a positional legal for the
/// verb but not for the form it chose was dropped at exit 0. <c>diff old.json new.json s3/i47</c>, which is
/// #115's question with <c>--body</c> forgotten, printed the whole run diff and never mentioned the call. The
/// per-verb flag check could not see it: every one of those flags is <c>diff</c>'s. Each form now refuses
/// what it does not read, before a line is printed, and names the form that would.</para>
/// </summary>
public class DiffFormsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-diff-forms").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── Two bodies in one report ──────────────────────────────

    [Fact]
    public void Two_bodies_in_one_report_refuse_a_baseline()
    {
        var (output, error, exit) = Query("diff", Old(), "s0/i1", "s0/i3", "--baseline");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--baseline", error);
    }

    [Fact]
    public void Two_bodies_in_one_report_refuse_a_body_flag_with_a_call()
    {
        var (output, error, exit) = Query("diff", Old(), "s0/i1", "s0/i3", "--body", "s0/i1");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--body", error);
    }

    [Fact]
    public void Two_bodies_in_one_report_refuse_a_bare_body_flag()
    {
        var (output, error, exit) = Query("diff", Old(), "s0/i1", "s0/i3", "--body");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--body", error);
    }

    [Fact]
    public void Two_bodies_in_one_report_refuse_a_third_address()
    {
        var (output, error, exit) = Query("diff", Old(), "s0/i1", "s0/i3", "s1/i1");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("s1/i1", error);
    }

    // ─── Two runs ──────────────────────────────────────────────

    [Fact]
    public void A_call_address_beside_two_reports_is_refused_with_the_body_form_it_meant()
    {
        // #115's own question with --body forgotten: the whole run diff, at exit 0, about no call at all.
        var old = Old();
        var @new = New();

        var (output, error, exit) = Query("diff", old, @new, "s0/i1");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--body s0/i1", error);
        Assert.Contains($"diff {old} {@new} --body s0/i1", error);
    }

    [Fact]
    public void A_third_report_is_refused()
    {
        var third = Write("Third.json", ("Checkout", "aaaabbbbccccdddd", 3));

        var (output, error, exit) = Query("diff", Old(), New(), third);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains(third, error);
    }

    [Fact]
    public void A_bare_body_flag_beside_two_reports_is_refused_with_the_form_that_takes_a_call()
    {
        var (output, error, exit) = Query("diff", Old(), New(), "--body");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--body s3/i47", error);
    }

    [Theory]
    [InlineData("--offset", "1")]
    [InlineData("--limit", "1")]
    public void The_run_diff_refuses_paging_it_does_not_do(string flag, string value)
    {
        var (output, error, exit) = Query("diff", Old(), New(), flag, value);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains(flag, error);
        Assert.Contains("--json", error);
    }

    [Fact]
    public void A_cut_section_of_the_run_diff_says_where_the_rest_is()
    {
        // Gone was cut at ten rows with no line at all, the other sections at fifteen with "… N more" and
        // no way to the rest.
        var gone = Enumerable.Range(0, 12).Select(i => ($"Gone {i}", $"{i:x16}", 1)).ToArray();
        var fresh = Enumerable.Range(0, 18).Select(i => ($"New {i}", $"{i + 100:x16}", 1)).ToArray();
        var old = Write("ManyOld.json", gone);
        var @new = Write("ManyNew.json", fresh);

        var (output, error, exit) = Query("diff", old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("New (18):", output);
        Assert.Contains("  … 3 more (--json lists every row)", output);
        Assert.Contains("Gone (12):", output);
        Assert.Contains("  … 2 more (--json lists every row)", output);
    }

    [Fact]
    public void The_run_diff_counts_the_changes_its_json_lists()
    {
        // --count was declared for diff and read only by the body forms: the run diff printed itself whole.
        var old = Write("ManyOld.json", [.. Enumerable.Range(0, 12).Select(i => ($"Gone {i}", (string?)$"{i:x16}", 1))]);
        var @new = Write("ManyNew.json", [.. Enumerable.Range(0, 18).Select(i => ($"New {i}", (string?)$"{i + 100:x16}", 1))]);

        var (output, error, exit) = Query("diff", old, @new, "--count");
        var (json, _, _) = Query("diff", old, @new, "--json");

        Assert.True(exit == 0, error);
        Assert.Equal("30\n", output);
        Assert.Equal(30, JsonDocument.Parse(json).RootElement.GetProperty("items").GetArrayLength());
    }

    // ─── --baseline ────────────────────────────────────────────

    [Fact]
    public void A_report_beside_baseline_is_refused_rather_than_the_baseline_diffed_in_its_place()
    {
        var folder = Path.Combine(_directory, "current");
        var current = Write(Path.Combine("current", "TestRunReport.json"), ("Checkout", "aaaabbbbccccdddd", 2));
        Directory.CreateDirectory(Path.Combine(folder, "baseline"));
        Write(Path.Combine("current", "baseline", "TestRunReport.json"), ("Checkout", "aaaabbbbccccdddd", 1));

        var (output, error, exit) = Query("diff", current, "--baseline", New());

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--baseline names the old side itself", error);
    }

    // ─── --body's value ────────────────────────────────────────

    [Fact]
    public void A_body_hash_given_to_body_is_refused_with_the_reason()
    {
        var (output, error, exit) = Query("diff", Old(), New(), "--body", "b:4bdea521");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("names bytes, not a call", error);
        Assert.Contains("b:4bdea521 b:", error);
    }

    [Fact]
    public void A_malformed_body_value_is_refused_with_the_one_form_it_takes()
    {
        var (_, error, exit) = Query("diff", Old(), New(), "--body", "nope");

        Assert.Equal(2, exit);
        Assert.Contains("s3/i47", error);
        Assert.DoesNotContain("b:", error);
    }

    [Fact]
    public void Body_across_two_reports_without_stableIds_pairs_the_scenario_by_position_as_the_run_diff_does()
    {
        var old = Write("NoIdsOld.json", ("Checkout", null, 1));
        var @new = Write("NoIdsNew.json", ("Checkout", null, 2));

        var (output, error, exit) = Query("diff", old, @new, "--body", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("! this report has no stableIds (written before 3.0.47) — scenarios matched by position", output);
        Assert.Contains("$.v: 1 → 2", output);
    }

    // ─── http ──────────────────────────────────────────────────

    [Fact]
    public void Http_reads_body_as_its_own_flag_wherever_it_is_written()
    {
        // --body took the next token as diff's value whatever the verb, so this answered "Which interaction?".
        var (output, error, exit) = Query("http", Old(), "--body", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("\"v\"", output);
    }

    [Fact]
    public void Http_refuses_a_second_address()
    {
        var (output, error, exit) = Query("http", Old(), "s0/i0", "s0/i1");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("s0/i1", error);
    }

    [Fact]
    public void Http_refuses_a_second_address_after_its_body_flag()
    {
        var (output, error, exit) = Query("http", Old(), "s0/i0", "--body", "s0/i1");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("s0/i1", error);
    }

    // ─── Harness ───────────────────────────────────────────────

    private static (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private string Old() => Write("Old.json", ("Checkout", "aaaabbbbccccdddd", 1), ("Refund", "eeeeffff00001111", 1));

    private string New() => Write("New.json", ("Checkout", "aaaabbbbccccdddd", 2), ("Refund", "eeeeffff00001111", 1));

    /// <summary>
    /// A report of the scenarios given, each making two calls whose responses carry <c>{"v": N}</c> and
    /// <c>{"w": 1}</c>: s0/i1 and s0/i3 are the two response bodies. A null stableId writes none.
    /// </summary>
    private string Write(string fileName, params (string Name, string? StableId, int V)[] scenarios)
    {
        var body = string.Join(",\n", scenarios.Select((s, i) =>
        {
            var stableId = s.StableId is null ? "" : $"\"stableId\": \"{s.StableId}\",";
            return $$"""
                { "id": "t{{i}}", {{stableId}} "name": {{JsonSerializer.Serialize(s.Name)}}, "result": "Passed",
                  "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [],
                  "httpInteractions": [ {{Call("http://api/a", $"{{\"v\":{s.V}}}")}}, {{Call("http://api/b", "{\"w\":1}")}} ] }
                """;
        }));

        var path = Path.Combine(_directory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Orders", "labels": [], "scenarios": [ {{body}} ] } ]
            }
            """);
        return path;
    }

    private static string Call(string uri, string response)
    {
        var id = Guid.NewGuid();
        return $$"""
            { "type": "Request", "method": "GET", "uri": "{{uri}}", "serviceName": "api", "callerName": "test", "content": "{\"q\":1}", "headers": [], "requestResponseId": "{{id}}" },
            { "type": "Response", "method": "GET", "uri": "{{uri}}", "serviceName": "api", "callerName": "test", "content": {{JsonSerializer.Serialize(response)}}, "headers": [], "statusCode": "OK", "requestResponseId": "{{id}}" }
            """;
    }
}
