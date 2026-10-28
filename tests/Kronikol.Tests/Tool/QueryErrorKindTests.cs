using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #184 R3 (plans/QUERY_FOR_PROGRAMS_PLAN.md 5.3): <c>error.kind</c> in the <c>--json</c> envelope, so a program tells
/// "that is not in the report", "there is no report" and "you used me wrong" apart without reading prose. Three of the
/// kinds share exit 2 and two share exit 1, and the exit codes do not change.
/// </summary>
public class QueryErrorKindTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-error-kind").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // s0 and s1 share a name, so a stableId; s1 holds no diagram.
    private string Write()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "t-1", DisplayName = "Lists orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario { Id = "t-2", DisplayName = "Lists orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }
                ]
            }
        ];
        var written = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Kinds_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, [new("t-1", "d", "@startuml\nA -> A\n@enduml")], []);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static (JsonElement Error, int Exit, string Stderr) Refused(Func<string, string?> getEnv, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([.. args, "--json"], output, error, getEnv);
        Assert.NotEqual(0, exit);
        using var envelope = JsonDocument.Parse(output.ToString());
        return (envelope.RootElement.GetProperty("error").Clone(), exit, error.ToString());
    }

    private static (JsonElement Error, int Exit, string Stderr) Refused(params string[] args) =>
        Refused(_ => null, args);

    private static string Kind(JsonElement error) => error.GetProperty("kind").GetString()!;

    [Fact]
    public void Bad_usage_is_usage()
    {
        var report = Write();

        Assert.Equal("usage", Kind(Refused("nonsense", report).Error));
        Assert.Equal("usage", Kind(Refused("summary", report, "s0").Error));
        Assert.Equal("usage", Kind(Refused("steps", report, "not-an-address").Error));
        Assert.Equal("usage", Kind(Refused("steps", report, "s0", "--sort", "calls").Error));
    }

    [Fact]
    public void A_well_formed_name_the_report_lacks_is_not_found()
    {
        var report = Write();

        // Verbs that answer --json; one that does not refuses --json first, as usage.
        var (error, exit, _) = Refused("failures", report, "s9");
        Assert.Equal(2, exit);
        Assert.Equal("notFound", Kind(error));
        Assert.Equal("notFound", Kind(Refused("failures", report, "id:nope").Error));
        Assert.Equal("notFound", Kind(Refused("interactions", report, "s9").Error));
        Assert.Equal("notFound", Kind(Refused("diagram", report, "s1/d0", "--out", Path.Combine(_directory, "x.puml")).Error));
    }

    [Fact]
    public void A_name_several_things_carry_is_ambiguous()
    {
        var report = Write();
        var sid = JsonDocument.Parse(Run("scenarios", report, "--json")).RootElement.GetProperty("items")[0].GetProperty("stableId").GetString();

        var (error, exit, _) = Refused("failures", report, "sid:" + sid);

        Assert.Equal(2, exit);
        Assert.Equal("ambiguous", Kind(error));
    }

    [Fact]
    public void A_directory_holding_several_reports_is_ambiguous()
    {
        var report = Write();
        var parent = Path.Combine(_directory, "shards");
        foreach (var shard in new[] { "a", "b" })
        {
            Directory.CreateDirectory(Path.Combine(parent, shard));
            File.Copy(report, Path.Combine(parent, shard, "TestRunReport.json"));
        }

        Assert.Equal("ambiguous", Kind(Refused("summary", parent).Error));
    }

    [Fact]
    public void A_path_with_no_report_is_no_report()
    {
        var (missing, exit, _) = Refused("summary", Path.Combine(_directory, "nowhere", "TestRunReport.json"));
        Assert.Equal(2, exit);
        Assert.Equal("noReport", Kind(missing));

        var empty = Directory.CreateDirectory(Path.Combine(_directory, "empty")).FullName;
        Assert.Equal("noReport", Kind(Refused("summary", empty).Error));
    }

    [Fact]
    public void History_switched_off_is_unsupported()
    {
        var report = Write();

        var (error, exit, _) = Refused(name => name == "KRONIKOL_HISTORY" ? "off" : null, "history", report);

        Assert.Equal(2, exit);
        Assert.Equal("unsupported", Kind(error));
    }

    [Fact]
    public void A_report_that_cannot_be_read_is_unreadable()
    {
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.WriteAllText(path, "{ this is not json");

        var (error, exit, _) = Refused("summary", path);

        Assert.Equal(1, exit);
        Assert.Equal("unreadable", Kind(error));
    }

    [Fact]
    public void An_out_that_cannot_be_written_is_write_failed()
    {
        var report = Write();

        var (error, exit, _) = Refused("diagram", report, "s0/d0", "--out", " ");

        Assert.Equal(1, exit);
        Assert.Equal("writeFailed", Kind(error));
    }

    [Fact]
    public void A_refusal_that_names_no_kind_takes_its_exit_codes()
    {
        using var two = JsonDocument.Parse(QueryErrorEnvelope.Build("steps", null, null, 2, "Something went wrong.\nA hint."));
        using var one = JsonDocument.Parse(QueryErrorEnvelope.Build("steps", null, null, 1, "Something could not be read."));

        Assert.Equal("usage", two.RootElement.GetProperty("error").GetProperty("kind").GetString());
        Assert.Equal("unreadable", one.RootElement.GetProperty("error").GetProperty("kind").GetString());
    }

    [Fact]
    public void A_note_said_before_the_refusal_is_hint_and_not_the_message()
    {
        // Two reports in one folder, or a pairing problem, are said before the refusal; the message was the first line.
        const string said = "Reports holds TestRunReport.json and TestRunReport.krkl: reading TestRunReport.krkl\nNo scenario s9 — the report has 2 (s0-s1).\nscenarios lists them.";

        using var envelope = JsonDocument.Parse(QueryErrorEnvelope.Build("failures", null, null, 2, said, "notFound", "No scenario s9 — the report has 2 (s0-s1)."));

        var error = envelope.RootElement.GetProperty("error");
        Assert.Equal("No scenario s9 — the report has 2 (s0-s1).", error.GetProperty("message").GetString());
        Assert.Equal("Reports holds TestRunReport.json and TestRunReport.krkl: reading TestRunReport.krkl\nscenarios lists them.", error.GetProperty("hint").GetString());
        Assert.Equal("notFound", error.GetProperty("kind").GetString());
    }

    [Fact]
    public void The_envelope_keeps_every_other_member_and_the_text_answer_is_unchanged()
    {
        var report = Write();

        var (error, exit, stderr) = Refused("failures", report, "s9");
        Assert.Equal(["exitCode", "message", "hint", "kind"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, error.GetProperty("exitCode").GetInt32());
        Assert.StartsWith("No scenario s9", error.GetProperty("message").GetString(), StringComparison.Ordinal);

        var text = new StringWriter();
        var prose = new StringWriter();
        Assert.Equal(exit, QueryCommand.Run(["failures", report, "s9"], text, prose));
        Assert.Equal("", text.ToString());
        Assert.Equal(stderr, prose.ToString());
    }

    [Fact]
    public void Describe_lists_every_kind_the_engine_uses()
    {
        using var describe = JsonDocument.Parse(Run("--describe"));
        var described = describe.RootElement.GetProperty("errorKinds").EnumerateArray().Select(k => k.GetProperty("kind").GetString()).ToList();

        Assert.Equal(["usage", "notFound", "ambiguous", "noReport", "unsupported", "unreadable", "writeFailed"], described);
        foreach (var used in UsedKinds())
            Assert.Contains(used, described);
    }

    /// <summary>The RefusalKinds members the engine's source refuses with.</summary>
    private static IEnumerable<string> UsedKinds() =>
        QuerySources().SelectMany(file => Regex.Matches(File.ReadAllText(file), @"RefusalKinds\.(\w+)").Select(m => m.Groups[1].Value))
            .Where(name => name != "ByExit")
            .Distinct()
            .Select(name => (string)typeof(QueryCommand).Assembly.GetType("Kronikol.Query.RefusalKinds")!.GetField(name)!.GetValue(null)!);

    private static string[] QuerySources()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src", "Kronikol", "Query")))
            directory = Path.GetDirectoryName(directory);
        Assert.NotNull(directory);
        return Directory.GetFiles(Path.Combine(directory!, "src", "Kronikol", "Query"), "*.cs");
    }

    [Fact]
    public void Every_refusal_that_says_something_is_missing_names_its_kind()
    {
        // The source walker (plan 5.3): a refusal written straight to the error writer takes its exit code's kind, so a
        // "No ..." refusal that skips Refuse would report a missing thing as a usage error.
        var raw = QuerySources()
            .SelectMany(file => File.ReadAllLines(file).Select((line, i) => (file, line, i)))
            .Where(l => Regex.IsMatch(l.line, MissingThing))
            .Select(l => $"{Path.GetFileName(l.file)}:{l.i + 1}: {l.line.Trim()}")
            .ToList();

        Assert.True(raw.Count == 0, string.Join("\n", raw));
    }

    /// <summary>A refusal that says something is not there, as the engine words one, written straight to the error writer.</summary>
    private const string MissingThing = @"error\.WriteLine\(\$?""(No |[^""]*(carries no |is not in |is not retained|: no run))";

    [Theory]
    [InlineData("        error.WriteLine($\"No scenario s{n}.\");")]
    [InlineData("        error.WriteLine($\"{label} {address} carries no body, so there is nothing to compare.\");")]
    [InlineData("        error.WriteLine($\"run {wanted} is not retained under {directory}.\");")]
    public void The_source_walker_finds_a_raw_refusal(string line)
    {
        Assert.Matches(MissingThing, line);
        Assert.DoesNotMatch(MissingThing, line.Replace("error.WriteLine(", "Refuse(error, RefusalKinds.NotFound, "));
    }

    private static string Run(params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(args, output, new StringWriter()));
        return output.ToString();
    }
}
