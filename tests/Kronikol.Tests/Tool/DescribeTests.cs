using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tool.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query --describe</c>: one JSON document naming every verb, the flags each one reads, the
/// address forms and the exit codes - generated from the same table the CLI dispatches and validates from.
///
/// <para>The verb set used to live in four hand-kept places plus a byte-identical dogfood copy of the
/// skill, and the guard that kept them in step recovered it by regexing the rendered help - a regex that
/// had already silently dropped a verb. These facts hold the document to the tool in both directions: a
/// verb it lists dispatches, a verb the tool dispatches is listed, and the same for every flag.</para>
/// </summary>
public class DescribeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-describe").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Describe_needs_no_report_and_is_one_json_object()
    {
        var (output, error, exit) = Query("--describe");

        Assert.True(exit == 0, error);
        Assert.Equal("", error);
        var document = JsonDocument.Parse(output).RootElement;
        Assert.Equal(JsonValueKind.Object, document.ValueKind);
        Assert.Equal(1, document.GetProperty("formatVersion").GetInt32());
        Assert.Equal("describe", document.GetProperty("command").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+", document.GetProperty("toolVersion").GetString());
        // formatVersion first, so a consumer checks it before reading anything else.
        Assert.StartsWith("{\n  \"formatVersion\"", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Describe_answers_wherever_the_flag_is_placed()
    {
        var (output, error, exit) = Query("summary", "--describe");

        Assert.True(exit == 0, error);
        Assert.Equal("describe", JsonDocument.Parse(output).RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void Every_verb_describe_lists_dispatches_and_every_verb_that_dispatches_is_listed()
    {
        var listed = Document().GetProperty("verbs").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToArray();

        Assert.Equal(VerbTable.Names, listed);
        Assert.Equal(QueryCommand.Verbs, listed);

        var report = Report();
        foreach (var verb in listed)
        {
            // Not "answers" - most verbs want an address the call does not give - but "is a verb": the one
            // refusal a listed name must never meet is the unknown-command one.
            var (_, error, _) = Query(verb, report);
            Assert.DoesNotContain("Unknown query command", error);
        }
    }

    [Fact]
    public void An_unknown_verb_is_refused_before_a_report_is_looked_for()
    {
        var (_, withReport, exit) = Query("bogus", Report());
        Assert.Equal(2, exit);
        Assert.Contains("Unknown query command: bogus", withReport);

        var (_, withoutReport, exitAlone) = Query("bogus");
        Assert.Equal(2, exitAlone);
        Assert.Contains("Unknown query command: bogus", withoutReport);
        Assert.DoesNotContain("No report given", withoutReport);
    }

    [Fact]
    public void The_flags_listed_for_a_verb_are_exactly_the_ones_it_reads()
    {
        foreach (var verb in Document().GetProperty("verbs").EnumerateArray())
        {
            var name = verb.GetProperty("name").GetString()!;
            var flags = verb.GetProperty("flags").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToArray();

            Assert.Equal(QueryCommand.FlagsByVerb[name], flags);
            Assert.DoesNotContain("--max-bytes", flags);
            Assert.DoesNotContain("--out", flags);
            Assert.Equal(QueryCommand.JsonCommands.Contains(name), verb.GetProperty("json").GetBoolean());
            Assert.Equal(flags.Contains("--offset"), verb.GetProperty("pagesWithOffset").GetBoolean());
        }
    }

    [Fact]
    public void Every_flag_the_parser_knows_is_described_and_no_flag_is_invented()
    {
        var described = VerbTable.Flags.Select(f => f.Name).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var known = QueryOptions.KnownFlags.OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.Equal(known, described);
        Assert.All(VerbTable.Flags, f => Assert.False(string.IsNullOrWhiteSpace(f.Summary), f.Name + " has no summary"));
    }

    [Fact]
    public void Every_flag_a_verb_names_exists_in_the_flag_table()
    {
        foreach (var verb in VerbTable.Verbs)
            foreach (var flag in verb.Flags)
                Assert.NotNull(VerbTable.Flag(flag));
    }

    [Fact]
    public void Every_address_example_parses_to_the_kind_it_claims_and_every_kind_has_one()
    {
        var addresses = Document().GetProperty("addresses").EnumerateArray().ToArray();

        foreach (var address in addresses)
        {
            var example = address.GetProperty("example").GetString()!;
            Assert.True(Address.TryParse(example, out var parsed), example + " does not parse");
            Assert.Equal(address.GetProperty("kind").GetString(), parsed.Kind.ToString());
        }

        var kinds = addresses.Select(a => a.GetProperty("kind").GetString()).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(Enum.GetNames<AddressKind>().OrderBy(k => k, StringComparer.Ordinal), kinds);
    }

    [Fact]
    public void The_exit_codes_are_the_three_the_tool_returns()
    {
        var codes = Document().GetProperty("exitCodes").EnumerateArray().Select(e => e.GetProperty("code").GetInt32()).ToArray();

        Assert.Equal([0, 1, 2], codes);
    }

    [Fact]
    public void The_universal_flags_are_the_ones_every_invocation_accepts()
    {
        var universal = Document().GetProperty("universalFlags").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray();

        // --run since retained runs (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md S4): any verb opens the run kept under runs/.
        Assert.Equal(["--max-bytes", "--out", "--describe", "--run"], universal);
        Assert.Equal(QueryCommand.UniversalFlags, universal);
    }

    [Fact]
    public void The_help_is_rendered_from_the_table_and_lists_every_verb_once()
    {
        var help = new StringWriter();
        QueryCommand.PrintUsage(help);
        var rendered = help.ToString();

        var listed = Regex.Matches(rendered, @"^  ([a-z][a-z-]*) +<", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(VerbTable.Names, listed);

        foreach (var (group, caption) in VerbTable.Groups)
            Assert.Contains(caption is null ? "\n" + group + "\n" : $"\n{group} ({caption})\n", rendered.ReplaceLineEndings("\n"));
        Assert.Contains("--describe", rendered);
    }

    private static (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static JsonElement Document()
    {
        var (output, error, exit) = Query("--describe");
        Assert.True(exit == 0, error);
        return JsonDocument.Parse(output).RootElement;
    }

    private string Report()
    {
        var written = ReportGenerator.GenerateTestRunReportData(
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "t0", DisplayName = "Place order", Result = ExecutionResult.Passed }] }],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Describe_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json);

        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }
}
