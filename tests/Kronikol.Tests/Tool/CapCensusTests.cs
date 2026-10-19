using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// Every list <c>kronikol query</c> or the digest cuts says what it cut and names a view that prints the rest (#161,
/// plans/LIST_CAPS_PLAN.md R2). One report holds a list over every cap; each row, one per cap
/// <see cref="ListCapRegistry"/> says the census checks, reads one cut, checks the number it gives, and runs the view it
/// names to find every entry. A cap that went silent again is the defect this release ends.
/// </summary>
public class CapCensusTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-cap-census").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── The census: one row per cap the registry says it checks ───

    public static IEnumerable<object[]> Announced() =>
        ListCapRegistry.Caps.Where(c => c.Census is not null).Select(c => c.Census!).Distinct().Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Announced))]
    public void Every_announced_cap_says_what_it_cut_and_names_a_view_that_prints_the_rest(string cap)
    {
        Assert.True(Checks.TryGetValue(cap, out var check), $"no census check for \"{cap}\"");
        check(this);
    }

    [Fact]
    public void Every_census_check_is_a_cap_in_the_registry() =>
        Assert.Equal(Checks.Keys.Order(StringComparer.Ordinal), Announced().Select(row => (string)row[0]).Order(StringComparer.Ordinal));

    [Fact]
    public void Every_list_in_the_census_report_is_over_its_cap()
    {
        // A row reads a cut, so a list at or under its cap would fail it rather than pass it; this says which list fell
        // short, against the cap the code holds today.
        var steps = Features()[0].Scenarios[0].Steps!;
        (string Cap, int Entries, string Constant)[] lists =
        [
            ("steps parameters", StepLines(Answer("steps", Report(), "s0/0"), "0").Count(l => l.StartsWith("· ", StringComparison.Ordinal)), "MaxParametersShown"),
            ("steps attachments", steps[0].Attachments!.Length, "MaxAttachmentsShown"),
            ("services statuses", Statuses.Length, "MaxStatusesShown"),
            ("sizes largest scenarios", Features()[0].Scenarios.Length, "LargestScenarios"),
            ("grep --values paths", Regex.Matches(LedgerAnswer, "4173").Count, "MaxPathsPerBody"),
            ("grep --number paths", Regex.Matches(LedgerAnswer, "4173").Count, "MaxPathsPerBody"),
            ("diff --body lines", NoteLines, "MaxLinesListed"),
            ("compare differences", Features()[0].Scenarios[1].Steps!.Length, "MaxDifferencesShown"),
            ("http b: carriers", Carriers(), "MaxCarriersListed"),
            ("body carriers", Carriers(), "MaxCarriersListed"),
            ("Failures.md steps before", steps.Length - 1, "StepsBeforeFailure"),
            ("Failures.md calls", Logs().Count(l => l.Type == RequestResponseType.Request), "MaxCallsPerFailure"),
        ];

        Assert.Equal(Checks.Keys.Order(StringComparer.Ordinal), lists.Select(l => l.Cap).Order(StringComparer.Ordinal));
        Assert.All(lists, l => Assert.True(l.Entries > Constant(l.Constant), $"{l.Cap}: {l.Entries} entries, and the cap is {Constant(l.Constant)}"));
    }

    private static readonly Dictionary<string, Action<CapCensusTests>> Checks = new(StringComparer.Ordinal)
    {
        ["steps parameters"] = t => t.StepsParameters(),
        ["steps attachments"] = t => t.StepsAttachments(),
        ["services statuses"] = t => t.ServicesStatuses(),
        ["sizes largest scenarios"] = t => t.SizesLargestScenarios(),
        ["grep --values paths"] = t => t.GrepPaths("--values"),
        ["grep --number paths"] = t => t.GrepPaths("--number"),
        ["diff --body lines"] = t => t.DiffBodyLines(),
        ["compare differences"] = t => t.CompareDifferences(),
        ["http b: carriers"] = t => t.Carriers("http"),
        ["body carriers"] = t => t.Carriers("body"),
        ["Failures.md steps before"] = t => t.DigestStepsBefore(),
        ["Failures.md calls"] = t => t.DigestCalls(),
    };

    /// <summary>A cap's value in the code: a constant of <c>QueryCommand</c> or of the digest.</summary>
    private static int Constant(string name) =>
        typeof(QueryCommand).Assembly.GetTypes()
            .Where(t => t.FullName!.StartsWith("Kronikol.Query.QueryCommand", StringComparison.Ordinal) || t == typeof(FailuresDigestGenerator))
            .SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .Where(f => f.IsLiteral && f.Name == name)
            .Select(f => (int)f.GetRawConstantValue()!)
            .Single();

    // ─── steps ─────────────────────────────────────────────────

    private void StepsParameters()
    {
        // Six inline parameters are twelve lines (a name and a value each). The whole tree printed eight and said nothing.
        var whole = StepLines(Answer("steps", Report(), "s0"), "0");
        Assert.Equal(8, whole.Count(l => l.StartsWith("· ", StringComparison.Ordinal)));
        Assert.Contains("… 4 more parameter(s): steps s0/0", whole);

        var scoped = StepLines(Answer("steps", Report(), "s0/0"), "0");
        Assert.Equal(12, scoped.Count(l => l.StartsWith("· ", StringComparison.Ordinal)));
        Assert.DoesNotContain(scoped, l => l.StartsWith("… ", StringComparison.Ordinal) && l.Contains("parameter", StringComparison.Ordinal));
    }

    private void StepsAttachments()
    {
        // A tree cannot page, and a step's attachments were printed whole: sixty filled the byte budget on their own.
        foreach (var address in new[] { "s0", "s0/0" })
        {
            var lines = StepLines(Answer("steps", Report(), address), "0");
            Assert.Equal(
                [
                    "attachment: shot-0.png",
                    "… 3 more attachment(s) (shot-1.png, shot-2.png, shot-3.png): attachments s0/0",
                    "attachment: shot-4.png", "attachment: shot-5.png", "attachment: shot-6.png", "attachment: shot-7.png", "attachment: shot-8.png"
                ],
                lines.Where(l => l.StartsWith("attachment: ", StringComparison.Ordinal) || l.Contains("attachment(s)", StringComparison.Ordinal))
                    .Select(l => l.StartsWith("attachment: ", StringComparison.Ordinal) ? l[..l.IndexOf(" → ", StringComparison.Ordinal)] : l));
        }

        Assert.Equal("9\n", Answer("attachments", Report(), "s0/0", "--count"));
    }

    // ─── services ──────────────────────────────────────────────

    private void ServicesStatuses()
    {
        // Six kinds of status from one service: the column showed four, with nothing said of the other two.
        var text = Answer("services", Report());
        var row = text.Split('\n').Single(l => l.StartsWith("bank ", StringComparison.Ordinal));
        Assert.Equal(4, Regex.Matches(row, @"\S+×\d+").Count);
        Assert.EndsWith(" +2 more", row);
        Assert.Contains("`services --json` lists every status", text, StringComparison.Ordinal);

        var bank = Items(Answer("services", Report(), "--json")).Single(i => i.GetProperty("service").GetString() == "bank");
        Assert.Equal(6, bank.GetProperty("statuses").EnumerateObject().Count());
    }

    // ─── sizes ─────────────────────────────────────────────────

    private void SizesLargestScenarios()
    {
        // The text said "The 10 largest of 12"; the JSON gave the ten and no total.
        var envelope = JsonDocument.Parse(Answer("sizes", Report(), "--json")).RootElement;
        Assert.Equal(10, envelope.GetProperty("largestScenarios").GetArrayLength());
        Assert.Equal(12, envelope.GetProperty("largestScenariosTotal").GetInt32());
        Assert.Contains("The 10 largest of 12 scenarios", Answer("sizes", Report()), StringComparison.Ordinal);
    }

    // ─── grep ──────────────────────────────────────────────────

    private void GrepPaths(string flag)
    {
        // The ledger's answer holds 4173 at six paths; grep printed four of them and said nothing of the other two. The
        // rows stay four, so --count and the footer count what was printed, and the last one says what it left out.
        var rows = Answer("grep", Report(), "4173", flag).Split('\n').Where(l => Regex.IsMatch(l, @" body +\$")).ToArray();
        Assert.Equal(4, rows.Length);
        var cut = Regex.Match(rows[^1], @" · 2 more path\(s\) in this body: body (b:[0-9a-f]+) --keys$");
        Assert.True(cut.Success, rows[^1]);
        Assert.Equal("4\n", Answer("grep", Report(), "4173", flag, "--count"));

        // The view it names prints the body's shape, where the other paths are.
        Assert.Contains("$.d", Answer("body", Report(), cut.Groups[1].Value, "--keys"), StringComparison.Ordinal);
    }

    // ─── diff --body ───────────────────────────────────────────

    private void DiffBodyLines()
    {
        // Two text bodies 25 lines apart. The line diff lists twenty, and its marker was a row: the footer, total and
        // --count said 21 whatever the number was.
        var notes = Items(Answer("interactions", Report(), "s0", "--service", "notes", "--json"))
            .Select(i => i.GetProperty("address").GetString()!).ToArray();
        Assert.Equal(2, notes.Length);

        var text = Answer("diff", Report(), notes[0], notes[1]);
        Assert.Equal(20, Regex.Matches(text, @"^line \d+:", RegexOptions.Multiline).Count);
        Assert.Contains("\n… 5 more line(s) differ: --out both bodies and diff the files\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n25 paths differ\n", text, StringComparison.Ordinal);
        Assert.Equal("25\n", Answer("diff", Report(), notes[0], notes[1], "--count"));

        var envelope = JsonDocument.Parse(Answer("diff", Report(), notes[0], notes[1], "--json")).RootElement;
        var items = envelope.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(20, items.Length);
        Assert.All(items, item => Assert.Equal("line", item.GetProperty("kind").GetString()));
        Assert.Equal(25, envelope.GetProperty("total").GetInt32());
        Assert.Contains(envelope.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("5 more line(s) differ", StringComparison.Ordinal));
    }

    // ─── compare ───────────────────────────────────────────────

    private void CompareDifferences()
    {
        // Two scenarios of 25 steps, every one different: compare printed twenty and its heading counted the steps.
        var text = Answer("compare", Report(), "s1", "s2");
        Assert.Contains("\nsteps: 25 vs 25, 25 differ\n", text, StringComparison.Ordinal);
        Assert.Equal(20, Regex.Matches(text, @"^ +\d+  - ", RegexOptions.Multiline).Count);
        Assert.Contains("\n  … 5 more difference(s): steps s1 · steps s2\n", text, StringComparison.Ordinal);
        Assert.Contains("\ncalls: 0 vs 0\n  identical\n", text, StringComparison.Ordinal);

        // Each view it names prints its scenario's 25 steps.
        Assert.Equal(25, Regex.Matches(Answer("steps", Report(), "s1"), "filler 1 step", RegexOptions.IgnoreCase).Count);
    }

    // ─── http b: and body ──────────────────────────────────────

    private void Carriers(string verb)
    {
        // {"ok":true} answers sixteen calls. The list of the addresses carrying it stopped at twelve with "… 4 more" and no
        // view; interactions --json gives every call's body hashes.
        var carriers = Carriers();
        Assert.Equal(16, carriers);

        var text = Answer(verb, Report(), OkHash());
        Assert.Contains($"\n  … {carriers - 12} more: interactions --json gives every call's request and response bodyHash\n", text, StringComparison.Ordinal);
    }

    /// <summary>How many calls answered <c>{"ok":true}</c>, counted from the view the cut line names.</summary>
    private int Carriers()
    {
        var hash = OkHash();
        return Items(Answer("interactions", Report(), "--json", "--max-bytes", "0"))
            .Count(i => i.GetProperty("response").ValueKind == JsonValueKind.Object && i.GetProperty("response").GetProperty("bodyHash").GetString() == hash);
    }

    private string OkHash() =>
        Items(Answer("interactions", Report(), "s0", "--service", "cache", "--json"))[0].GetProperty("response").GetProperty("bodyHash").GetString()!;

    // ─── Failures.md and Failures.jsonl ────────────────────────

    private void DigestStepsBefore()
    {
        // Five steps before the failing one, of which it lists the last three, with nothing said of the other two.
        var digest = FailuresDigestGenerator.Generate(Features(), Logs(), "TestRunReport", "4.29.0");
        Assert.Contains("Steps before it:\n\n- … 2 earlier step(s): `kronikol query steps . s0`\n- `s0/2` ", digest.Markdown.Replace("\r\n", "\n"), StringComparison.Ordinal);

        var record = FailuresJsonl.Failures(digest.Jsonl).Single(r => r.GetProperty("address").GetString() == "s0");
        Assert.Equal(3, record.GetProperty("stepsBefore").GetArrayLength());
        Assert.Equal(5, record.GetProperty("stepsBeforeTotal").GetInt32());

        // The view it names prints all five.
        var tree = Answer("steps", Report(), "s0");
        Assert.Contains("a step with a long list of everything", tree, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(tree, "a passing step").Count);
    }

    private void DigestCalls()
    {
        // Seventeen calls, none attributed to a step, of which the table holds eight, with nothing said of the rest.
        var digest = FailuresDigestGenerator.Generate(Features(), Logs(), "TestRunReport", "4.29.0");
        Assert.Contains("\n… 9 more call(s) not in this table: `kronikol query flow . s0`\n", digest.Markdown.Replace("\r\n", "\n"), StringComparison.Ordinal);

        var record = FailuresJsonl.Failures(digest.Jsonl).Single(r => r.GetProperty("address").GetString() == "s0");
        Assert.Equal(8, record.GetProperty("calls").GetArrayLength());
        Assert.Equal(17, record.GetProperty("callsTotal").GetInt32());

        // The view it names lists every one of them.
        Assert.Equal("17\n", Answer("interactions", Report(), "s0", "--count"));
    }

    // ─── Harness ───────────────────────────────────────────────

    private static JsonElement[] Items(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray().ToArray();

    private static string Answer(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit} from {string.Join(' ', args)}: {error}");
        return output.ToString().Replace("\r\n", "\n");
    }

    /// <summary>The lines <c>steps</c> prints under one step, trimmed: from its own line to the next step's.</summary>
    private static string[] StepLines(string steps, string path)
    {
        var lines = steps.Split('\n');
        var start = Array.FindIndex(lines, l => Regex.IsMatch(l, $@"^\s*[✗~· ] {Regex.Escape(path)}\s"));
        Assert.True(start >= 0, $"no step {path} in:\n{steps}");
        var end = Array.FindIndex(lines, start + 1, l => Regex.IsMatch(l, @"^\s*[✗~· ] [b\d][\d.]*\s") || l.Length == 0);
        return lines[(start + 1)..(end < 0 ? lines.Length : end)].Select(l => l.Trim()).ToArray();
    }

    // ─── The census report ─────────────────────────────────────

    private string? _report;

    private string Report() => _report ??= Write();

    private string Write()
    {
        var written = ReportGenerator.GenerateTestRunReportData(Features(),
            new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 19, 10, 5, 0, DateTimeKind.Utc),
            "Census_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, Logs());
        var path = Path.Combine(_dir, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static Feature[] Features() =>
        [
            new Feature
            {
                DisplayName = "Census",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "c0", DisplayName = "Every list over its cap", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromSeconds(2), ErrorMessage = "The census failed",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a step with a long list of everything", Status = ExecutionResult.Passed,
                                Parameters = [.. Enumerable.Range(0, 6).Select(i => new StepParameter
                                {
                                    Name = $"name{i}", Kind = StepParameterKind.Inline,
                                    InlineValue = new InlineParameterValue($"value{i}", null, VerificationStatus.NotApplicable)
                                })],
                                Attachments = [.. Enumerable.Range(0, 9).Select(i => new FileAttachment($"shot-{i}.png", $"attachments/shot-{i}.png", "image/png"))]
                            },
                            // Four more before the failing one, past the digest's three steps before it.
                            .. Enumerable.Range(1, 4).Select(k => new ScenarioStep { Keyword = "And", Text = $"a passing step {k}", Status = ExecutionResult.Passed }),
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "the census fails", Status = ExecutionResult.Failed, FailureMessage = "The census failed"
                            }
                        ]
                    },
                    // Eleven more, so that sizes ranks ten of twelve; the first two have 25 steps each, all different, for compare.
                    .. Enumerable.Range(1, 11).Select(i => new Scenario
                    {
                        Id = $"c{i}", DisplayName = $"Filler {i}", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps = i <= 2
                            ? [.. Enumerable.Range(0, 25).Select(j => new ScenarioStep { Keyword = "Given", Text = $"filler {i} step {j}", Status = ExecutionResult.Passed })]
                            : [new ScenarioStep { Keyword = "Given", Text = $"filler {i}", Status = ExecutionResult.Passed }]
                    })
                ]
            }
        ];

    /// <summary>The bank's six kinds of status, past services' four.</summary>
    private static readonly System.Net.HttpStatusCode[] Statuses =
    [
        System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.Created, System.Net.HttpStatusCode.BadRequest,
        System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.InternalServerError, System.Net.HttpStatusCode.ServiceUnavailable
    ];

    /// <summary>A number at six paths of one body, past grep's four.</summary>
    private const string LedgerAnswer = "{\"a\":4173,\"b\":4173,\"c\":[4173,4173],\"d\":{\"e\":4173,\"f\":4173}}";

    /// <summary>The lines of each notes body, every one different, past the line diff's twenty.</summary>
    private const int NoteLines = 25;

    private static RequestResponseLog[] Logs()
    {
        var at = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        return
        [
            .. Statuses.SelectMany((status, i) => Call("c0", "bank", $"/cards/{i}", at.AddSeconds(i), status, "{\"ok\":true}")),
            .. Call("c0", "ledger", "/totals", at.AddSeconds(10), System.Net.HttpStatusCode.OK, LedgerAnswer),
            // Two text bodies that differ on every one of their lines.
            .. Call("c0", "notes", "/a", at.AddSeconds(11), System.Net.HttpStatusCode.OK, "{\"ok\":true}",
                string.Join("\n", Enumerable.Range(0, NoteLines).Select(i => $"note {i}"))),
            .. Call("c0", "notes", "/b", at.AddSeconds(12), System.Net.HttpStatusCode.OK, "{\"ok\":true}",
                string.Join("\n", Enumerable.Range(0, NoteLines).Select(i => $"NOTE {i}"))),
            // Eight more answers of {"ok":true}: sixteen calls carry that body, past the twelve http b: and body list.
            .. Enumerable.Range(0, 8).SelectMany(i => Call("c0", "cache", $"/entries/{i}", at.AddSeconds(13 + i), System.Net.HttpStatusCode.OK, "{\"ok\":true}"))
        ];
    }

    private static RequestResponseLog[] Call(string testId, string service, string uri, DateTimeOffset at, System.Net.HttpStatusCode status, string answer,
        string request = "{\"amount\":12}")
    {
        var id = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var address = new Uri($"http://{service}{uri}");
        return
        [
            new RequestResponseLog(testId, testId, HttpMethod.Post, request, address, [], service, "Census", RequestResponseType.Request, trace, id, false)
                { Timestamp = at },
            new RequestResponseLog(testId, testId, HttpMethod.Post, answer, address, [], service, "Census", RequestResponseType.Response, trace, id, false, status)
                { Timestamp = at.AddMilliseconds(5) }
        ];
    }
}
