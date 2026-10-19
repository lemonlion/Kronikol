using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Ingestion.Cucumber;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// What <c>kronikol query</c> does with a list of attachments too long to print whole (#161, plans/LIST_CAPS_PLAN.md R1).
/// <c>failures</c> printed the first four of a scenario's own attachments and said nothing of the rest, so on Playwright's
/// capture the failure screenshot, the video, the error context and the trace were the ones cut. A capped list now keeps its
/// first entry and its last five, names what it cut and the view that lists it, and counts what it does not list; and
/// <c>attachments sN</c> is that view, paged.
/// </summary>
/// <remarks>
/// Three lanes ingest the repo's real Playwright 1.62.1 capture (<see cref="AttachmentFixture"/>) as the plan's harness does:
/// the wiki's reporter recipe (the tests file alone, every artefact the scenario's own), with playwright-bdd's messages (step
/// screenshots on their steps), and with the hooks kept (Playwright's own artefacts on the after-hook and fixture steps).
/// The built report holds more than any cap: <c>s0</c> has 60 attachments of its own, its passing step <c>s0/0</c> 9 (4 of
/// them on its sub-step <c>s0/0.0</c>), and its failing step <c>s0/1</c> 60 and 47 calls; <c>s1</c> fails with the same
/// message and holds none.
/// </remarks>
// The lanes run an ingest, which clears the process-global request log other classes read back.
[Collection("DiagramsFetcher")]
public class QueryAttachmentListTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-list-caps").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── failures: both ends, and what was cut ─────────────────

    [Fact]
    public void Failures_keeps_both_ends_of_a_scenarios_attachments_and_names_what_it_cut()
    {
        // The recipe lane: all ten of Playwright's artefacts are the scenario's own, in the order they were attached. The
        // first four were printed; the failure screenshot, the end screenshot, the video, the error context and the trace
        // were cut, and nothing said so.
        var lines = FailureLines(Answer("failures", Lane("recipe")), AttachmentFixture.FailingScenario, out var address);

        Assert.Equal(
            [
                "screenshot-start.png",
                $"… 4 more attachment(s) (after-step.png ×3, step-shot.png): attachments {address}",
                "screenshot", "screenshot-end.png", "video", "error-context", "trace"
            ],
            OwnList(lines));
    }

    [Fact]
    public void Failures_cuts_a_long_list_at_whole_names_and_names_the_scenario_or_the_step()
    {
        var lines = FailureLines(Answer("failures", Built()), "Pay with a card the bank declines", out _);

        // The scenario's own sixty: the first and the last five. Seven names fit in a hundred characters, the eighth
        // would not.
        Assert.Equal(
            [
                "page-00.png",
                "… 54 more attachment(s) (page-01.png, page-02.png, page-03.png, page-04.png, page-05.png, page-06.png, page-07.png, …): attachments s0",
                "page-55.png", "page-56.png", "page-57.png", "page-58.png", "page-59.png",
                "… 9 more attachment(s) on other steps (basket-0.png, basket-1.png, basket-2.png, basket-3.png, basket-4.png, coupon-0.png, coupon-1.png, …): attachments s0"
            ],
            OwnList(lines));

        // The failing step's sixty, under it, cut the same way and naming the step: six names are exactly a hundred
        // characters, so they all fit.
        Assert.Equal(
            [
                "declined-00.png",
                "… 54 more attachment(s) (declined-01.png, declined-02.png, declined-03.png, declined-04.png, declined-05.png, declined-06.png, …): attachments s0/1",
                "declined-55.png", "declined-56.png", "declined-57.png", "declined-58.png", "declined-59.png"
            ],
            StepList(lines));
    }

    [Fact]
    public void Failures_counts_attachments_on_steps_that_did_not_fail()
    {
        // With the hooks kept, Playwright's artefacts sit on the after-hook and fixture steps and the scenario's own list
        // is empty: failures showed the one attachment of the failing step and said nothing of the other nine. Six names
        // fit; screenshot-end.png would take the list past a hundred characters.
        var hooks = FailureLines(Answer("failures", Lane("hooks")), AttachmentFixture.FailingScenario, out var hooksAddress);
        Assert.Equal(
            [$"… 9 more attachment(s) on other steps (after-step.png ×2, step-shot.png, screenshot, screenshot-start.png, error-context, trace, …): attachments {hooksAddress}"],
            OwnList(hooks));

        // With the messages, the scenario's six all fit, and the step screenshots of the steps that passed are counted.
        var messages = FailureLines(Answer("failures", Lane("messages")), AttachmentFixture.FailingScenario, out var messagesAddress);
        Assert.Equal(
            [
                "screenshot-start.png", "screenshot", "screenshot-end.png", "video", "error-context", "trace",
                $"… 3 more attachment(s) on other steps (after-step.png ×2, step-shot.png): attachments {messagesAddress}"
            ],
            OwnList(messages));
    }

    [Fact]
    public void Failures_names_the_view_for_a_steps_cut_calls()
    {
        var lines = FailureLines(Answer("failures", Built()), "Pay with a card the bank declines", out _);

        Assert.Contains("    47 calls in this step:", lines);
        Assert.Equal(6, lines.Count(l => Regex.IsMatch(l, @"^      s0/i\d+ ")));
        Assert.Contains("      … 41 more call(s): flow s0/1", lines);

        // The view it names lists every one of them, with no budget in the way (flow does not page).
        var flow = Answer("flow", Built(), "s0/1", "--max-bytes", "0");
        Assert.Equal(47, Regex.Matches(flow, @"s0/i\d+\b").Select(m => m.Value).Distinct().Count());
    }

    [Fact]
    public void Every_attachment_cut_names_a_view_that_prints_every_entry()
    {
        var report = Built();
        var lines = FailureLines(Answer("failures", report), "Pay with a card the bank declines", out _);
        var cuts = lines.Where(l => l.TrimStart().StartsWith("… ", StringComparison.Ordinal) && l.Contains("attachment(s)", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, cuts.Length);

        foreach (var cut in cuts)
        {
            var command = cut[(cut.LastIndexOf("): ", StringComparison.Ordinal) is var at and >= 0 ? at + 3 : cut.LastIndexOf(": ", StringComparison.Ordinal) + 2)..].Split(' ');
            Assert.Equal("attachments", command[0]);
            var listed = Follow(command[0], report, command[1]).Select(e => e.Path).ToHashSet();

            var expected = cut.Contains("on other steps", StringComparison.Ordinal)
                ? Paths("basket", 5).Concat(Paths("coupon", 4))
                : command[1] == "s0/1" ? Paths("declined", 60) : Paths("page", 60);
            foreach (var path in expected)
                Assert.True(listed.Contains(path), $"{cut}: {path} is not in `{string.Join(' ', command)}`");
        }
    }

    // ─── failures --json: the same entries, and every total ──────

    [Fact]
    public void Failures_json_carries_the_texts_entries_and_every_lists_total()
    {
        var report = Built();
        var text = FailureLines(Answer("failures", report), "Pay with a card the bank declines", out _);
        var item = Items(Answer("failures", report, "--json", "--max-bytes", "0")).Single(i => i.GetProperty("address").GetString() == "s0");

        Assert.Equal(OwnList(text).Where(l => !l.StartsWith('…')), Names(item.GetProperty("attachments")));
        Assert.Equal(60, item.GetProperty("attachmentsTotal").GetInt32());
        Assert.Equal(9 + 60, item.GetProperty("stepAttachmentsTotal").GetInt32());

        var step = item.GetProperty("failingSteps").EnumerateArray().Single();
        Assert.Equal(StepList(text).Where(l => !l.StartsWith('…')), Names(step.GetProperty("attachments")));
        Assert.Equal(60, step.GetProperty("attachmentsTotal").GetInt32());
        Assert.Equal(6, step.GetProperty("calls").GetArrayLength());
        Assert.Equal(47, step.GetProperty("callsTotal").GetInt32());

        // A failure with nothing attached still carries its totals: present and zero, never absent.
        var other = Items(Answer("failures", report, "--json", "--max-bytes", "0")).Single(i => i.GetProperty("address").GetString() == "s1");
        Assert.Equal(0, other.GetProperty("attachmentsTotal").GetInt32());
        Assert.Equal(0, other.GetProperty("stepAttachmentsTotal").GetInt32());
    }

    [Theory]
    [InlineData("recipe")]
    [InlineData("messages")]
    [InlineData("hooks")]
    public void Failures_json_totals_add_up_to_what_Failures_jsonl_lists(string lane)
    {
        var report = Lane(lane);
        var text = Answer("failures", report);
        var digest = Digest(report);

        foreach (var item in Items(Answer("failures", report, "--json", "--max-bytes", "0")))
        {
            var address = item.GetProperty("address").GetString()!;
            var lines = FailureLines(text, item.GetProperty("scenario").GetString()!, out _);
            Assert.Equal(OwnList(lines).Where(l => !l.StartsWith('…')), Names(item.GetProperty("attachments")));
            Assert.Equal(digest[address].Length,
                item.GetProperty("attachmentsTotal").GetInt32() + item.GetProperty("stepAttachmentsTotal").GetInt32());
            Assert.Equal(digest[address].Count(e => e.Step is null), item.GetProperty("attachmentsTotal").GetInt32());
        }
    }

    [Fact]
    public void A_failure_with_sixty_attachments_still_fits_the_default_budget()
    {
        // Scoped to s0, so s1 cannot take the budget. Listing every attachment in the record would push it past 6,000
        // bytes, and a record that does not fit is dropped whole: the failure would vanish from the answer.
        var envelope = JsonDocument.Parse(Answer("failures", Built(), "s0", "--json")).RootElement;

        Assert.Single(envelope.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("truncated").ValueKind);
    }

    // ─── attachments: every one, paged ─────────────────────────

    [Theory]
    [InlineData("recipe")]
    [InlineData("messages")]
    [InlineData("hooks")]
    public void Attachments_lists_every_attachment_in_the_digests_order(string lane)
    {
        var report = Lane(lane);

        foreach (var (address, expected) in Digest(report))
        {
            var text = Follow("attachments", report, address);
            var json = Items(Answer("attachments", report, address, "--json", "--max-bytes", "0"));

            Assert.Equal(expected.Select(e => (e.Step ?? address, e.Name)), text.Select(e => (e.Address, e.Name)));
            Assert.Equal(expected.Select(e => (e.Step, e.Name)), json.Select(i => (i.GetProperty("step").GetString(), i.GetProperty("name").GetString()!)));
            // The digest keeps the path relative to the report; the tool resolves it, as failures does.
            Assert.All(expected.Zip(text), pair => Assert.EndsWith(Native(pair.First.Path), pair.Second.Path, StringComparison.Ordinal));
            Assert.Equal(text.Select(e => e.Path), json.Select(i => i.GetProperty("path").GetString()!));
        }
    }

    [Fact]
    public void Attachments_lists_a_built_scenarios_own_then_each_steps_in_step_order()
    {
        var entries = Follow("attachments", Built(), "s0");

        Assert.Equal(
            [
                .. Enumerable.Range(0, 60).Select(i => ("s0", $"page-{i:D2}.png")),
                .. Enumerable.Range(0, 5).Select(i => ("s0/0", $"basket-{i}.png")),
                .. Enumerable.Range(0, 4).Select(i => ("s0/0.0", $"coupon-{i}.png")),
                .. Enumerable.Range(0, 60).Select(i => ("s0/1", $"declined-{i:D2}.png"))
            ],
            entries.Select(e => (e.Address, e.Name)));
    }

    [Fact]
    public void A_step_address_lists_that_steps_attachments_and_its_sub_steps()
    {
        var entries = Follow("attachments", Built(), "s0/0");

        Assert.Equal(
            [.. Enumerable.Range(0, 5).Select(i => ("s0/0", $"basket-{i}.png")), .. Enumerable.Range(0, 4).Select(i => ("s0/0.0", $"coupon-{i}.png"))],
            entries.Select(e => (e.Address, e.Name)));
        Assert.Equal(4, Follow("attachments", Built(), "s0/0.0").Count);
    }

    [Fact]
    public void Attachments_pages_a_long_list_and_resumes_exactly()
    {
        var report = Built();

        var first = Answer("attachments", report, "s0");
        Assert.Matches(@"\nattachments: 1-\d+ of 129 · next: --offset \d+\n$", first);

        var all = Follow("attachments", report, "s0");
        Assert.Equal(129, all.Count);
        Assert.Equal(129, all.Distinct().Count());
        Assert.Equal(Entries(Answer("attachments", report, "s0", "--max-bytes", "0")), all);
        Assert.EndsWith("\n129 attachments\n", Answer("attachments", report, "s0", "--max-bytes", "0"), StringComparison.Ordinal);

        var envelope = JsonDocument.Parse(Answer("attachments", report, "s0", "--json")).RootElement;
        Assert.Equal(129, envelope.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Array, envelope.GetProperty("next").ValueKind);

        Assert.Equal("129\n", Answer("attachments", report, "s0", "--count"));
        Assert.Equal("9\n", Answer("attachments", report, "s0/0", "--count"));
    }

    [Fact]
    public void Attachments_says_so_when_a_scenario_holds_none()
    {
        var output = Answer("attachments", Built(), "s1");

        Assert.Contains("no attachments", output, StringComparison.Ordinal);
        Assert.Equal("0\n", Answer("attachments", Built(), "s1", "--count"));
    }

    [Fact]
    public void Attachments_refuses_what_is_not_a_scenario_or_a_step()
    {
        var (_, error, exit) = Query("attachments", Built(), "s9");
        Assert.Equal(2, exit);
        Assert.Contains("No scenario s9", error, StringComparison.Ordinal);

        (_, error, exit) = Query("attachments", Built(), "s0/7");
        Assert.Equal(2, exit);
        Assert.Contains("No step 7 in s0", error, StringComparison.Ordinal);

        (_, error, exit) = Query("attachments", Built());
        Assert.Equal(2, exit);
        Assert.Contains("Which scenario?", error, StringComparison.Ordinal);
    }

    // ─── steps: says they exist ────────────────────────────────

    [Fact]
    public void Steps_says_a_scenario_has_attachments_and_names_the_view()
    {
        Assert.Contains("attachments: 10 of its own · attachments s0", Header(Answer("steps", Lane("recipe"), "s0")));
        Assert.Contains("attachments: 6 of its own, 4 on its steps · attachments s0", Header(Answer("steps", Lane("messages"), "s0")));
        Assert.Contains("attachments: 10 on its steps · attachments s0", Header(Answer("steps", Lane("hooks"), "s0")));
        Assert.Contains("attachments: 60 of its own, 69 on its steps · attachments s0", Header(Answer("steps", Built(), "s0")));

        // A scoped tree prints no line about the scenario, and a scenario without attachments none at all.
        Assert.DoesNotContain(Answer("steps", Built(), "s0/1").Split('\n'), l => l.StartsWith("attachments:", StringComparison.Ordinal));
        Assert.DoesNotContain(Answer("steps", Built(), "s1").Split('\n'), l => l.StartsWith("attachments:", StringComparison.Ordinal));
    }

    // ─── Failures.md: counts what it does not work through ─────

    [Theory]
    [InlineData("recipe")]
    [InlineData("messages")]
    [InlineData("hooks")]
    public void Failures_md_counts_the_attachments_of_a_failure_it_does_not_work_through(string lane)
    {
        // The background scenario fails with the same message, so it is clustered with the failing one and gets a row of
        // the further-failures table: its ten artefacts were in no list meant for a person. The count is its own and every
        // step's, as Failures.jsonl lists them; with the hooks kept, every one of them is on a step.
        var report = Lane(lane);
        var markdown = File.ReadAllText(Path.Combine(report, "Failures.md")).Replace("\r\n", "\n");
        var here = Regex.Match(markdown, @"kronikol query failures (\S+) +# this file, live").Groups[1].Value;
        FailureLines(Answer("failures", report), AttachmentFixture.BackgroundScenario, out var address);
        Assert.Equal(10, Digest(report)[address].Length);

        var further = markdown[markdown.IndexOf(" further failures\n", StringComparison.Ordinal)..];
        Assert.Contains($"`kronikol query attachments {here} sN` lists a row's attachments", further, StringComparison.Ordinal);
        Assert.Contains("| Address | stableId | Scenario | Error | Attachments |\n|---|---|---|---|---|\n", further, StringComparison.Ordinal);
        Assert.Matches(new Regex($@"^\| `{address}` \|.* \| 10 \|$", RegexOptions.Multiline), further);
    }

    // ─── Harness ───────────────────────────────────────────────

    private static (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"), exit);
    }

    private static string Answer(params string[] args)
    {
        var (output, error, exit) = Query(args);
        Assert.True(exit == 0, $"exit {exit} from {string.Join(' ', args)}: {error}");
        return output;
    }

    private static JsonElement[] Items(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray().ToArray();

    private static string[] Names(JsonElement attachments) =>
        attachments.EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToArray();

    /// <summary>One failure of <c>failures</c>' text, from its heading to the blank line after it.</summary>
    private static string[] FailureLines(string failures, string scenario, out string address)
    {
        var lines = failures.Split('\n');
        var start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^s\d+  ") && l.EndsWith("› " + scenario, StringComparison.Ordinal));
        Assert.True(start >= 0, $"no failure named {scenario} in:\n{failures}");
        address = lines[start][..lines[start].IndexOf(' ')];
        var end = Array.FindIndex(lines, start, l => l.Length == 0);
        return lines[start..end];
    }

    /// <summary>The scenario's own list as <c>failures</c> prints it, two spaces in: each attachment's name, and each cut or count line whole.</summary>
    private static string[] OwnList(string[] lines) => ListAt(lines, "  ");

    /// <summary>The failing step's list, four spaces in.</summary>
    private static string[] StepList(string[] lines) => ListAt(lines, "    ");

    private static string[] ListAt(string[] lines, string indent) =>
        lines.Where(l => l.StartsWith(indent + "attachment: ", StringComparison.Ordinal)
                         || (l.StartsWith(indent + "… ", StringComparison.Ordinal) && l.Contains("attachment(s)", StringComparison.Ordinal)))
            .Select(l => l.StartsWith(indent + "attachment: ", StringComparison.Ordinal)
                ? l[(indent.Length + "attachment: ".Length)..l.IndexOf(" → ", StringComparison.Ordinal)]
                : l[indent.Length..])
            .ToArray();

    /// <summary>The lines of <c>steps</c> before its first blank line: the scenario's header.</summary>
    private static string[] Header(string steps) => steps.Split('\n').TakeWhile(l => l.Length > 0).ToArray();

    private sealed record Entry(string Address, string Name, string Path);

    private static List<Entry> Entries(string text) =>
        text.Split('\n')
            .Select(l => Regex.Match(l, @"^(s\d+(?:/\S+)?) +(.+?) → (.+)$"))
            .Where(m => m.Success)
            .Select(m => new Entry(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value))
            .ToList();

    /// <summary>Every entry of a listing, page by page: each page's <c>next:</c> line is followed until there is none.</summary>
    private static List<Entry> Follow(string verb, string report, string address)
    {
        var entries = new List<Entry>();
        string[] extra = [];
        for (var pages = 0; pages < 100; pages++)
        {
            var page = Answer([verb, report, address, .. extra]);
            entries.AddRange(Entries(page));
            var next = Regex.Match(page, @"· next: (.*)--offset (\d+)\n$");
            if (!next.Success)
                return entries;
            extra = [.. next.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--offset", next.Groups[2].Value];
        }

        throw new InvalidOperationException($"{verb} {address} did not end in 100 pages");
    }

    private sealed record DigestEntry(string Name, string Path, string? Step);

    /// <summary>Each failure's attachments as <c>Failures.jsonl</c> lists them, by address.</summary>
    private static Dictionary<string, DigestEntry[]> Digest(string report) =>
        File.ReadAllLines(Path.Combine(report, "Failures.jsonl"))
            .Where(l => l.Length > 0)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .Where(f => f.TryGetProperty("address", out _))
            .ToDictionary(
                f => f.GetProperty("address").GetString()!,
                f => f.TryGetProperty("attachments", out var list)
                    ? list.EnumerateArray().Select(a => new DigestEntry(
                        a.GetProperty("name").GetString()!,
                        a.GetProperty("path").GetString()!,
                        a.TryGetProperty("step", out var step) ? step.GetString() : null)).ToArray()
                    : []);

    private static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private string Lane(string lane)
    {
        var output = Path.Combine(_dir, lane);
        if (Directory.Exists(output))
            return output;

        string[] inputs = lane switch
        {
            "recipe" => ["--tests", AttachmentFixture.TestsPath],
            "messages" => ["--tests", AttachmentFixture.TestsPath, "--cucumber-messages", AttachmentFixture.MessagesPath],
            "hooks" => ["--tests", AttachmentFixture.TestsPath, "--cucumber-messages", AttachmentFixture.MessagesPath, "--include-hooks"],
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
        };
        // As the plan's harness runs it: an empty capture file, since the tests file alone is not an input.
        var captures = Path.Combine(_dir, "captures.ndjson");
        File.WriteAllText(captures, "");
        var error = new StringWriter();
        var exit = IngestCommand.Run(
            [captures, .. inputs, "--allow-empty", "--no-component-diagram", "--attachments-base", AttachmentFixture.Directory, "-o", output],
            new StringWriter(), error);
        Assert.True(exit == 0, $"ingest {lane}: exit {exit}: {error}");
        return output;
    }

    // ─── The built report ──────────────────────────────────────

    private string? _built;

    private string Built() => _built ??= WriteBuilt();

    private IEnumerable<string> Paths(string stem, int count) =>
        Files(stem, count).Select(file => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Built())!, file.RelativePath)));

    private static FileAttachment[] Files(string stem, int count) =>
        Enumerable.Range(0, count)
            .Select(i => count > 10 ? $"{stem}-{i:D2}.png" : $"{stem}-{i}.png")
            .Select(name => new FileAttachment(name, "attachments/" + name, "image/png"))
            .ToArray();

    private string WriteBuilt()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "c0", DisplayName = "Pay with a card the bank declines", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromSeconds(3), ErrorMessage = "The card was declined",
                        Attachments = Files("page", 60),
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed, Attachments = Files("basket", 5),
                                SubSteps = [new ScenarioStep { Keyword = "And", Text = "a coupon", Status = ExecutionResult.Passed, Attachments = Files("coupon", 4) }]
                            },
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "the card is charged", Status = ExecutionResult.Failed,
                                FailureMessage = "The card was declined", Attachments = Files("declined", 60)
                            }
                        ]
                    },
                    new Scenario
                    {
                        Id = "c1", DisplayName = "Pay again with the same card", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromSeconds(1), ErrorMessage = "The card was declined"
                    }
                ]
            }
        ];

        // Calls are given to a step by its marker: the n-th step marker of a test opens its n-th top-level step.
        var at = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        RequestResponseLog[] logs =
        [
            Marker("c0", "Given", "a basket", isStart: true, at),
            Marker("c0", null, null, isStart: false, at.AddMilliseconds(500)),
            Marker("c0", "When", "the card is charged", isStart: true, at.AddSeconds(1)),
            .. Enumerable.Range(0, 47).SelectMany(i => Call("c0", at.AddSeconds(2).AddMilliseconds(i * 10), i)),
            Marker("c0", null, null, isStart: false, at.AddSeconds(3))
        ];

        var written = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 19, 10, 5, 0, DateTimeKind.Utc),
            "ListCaps_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs);
        var path = Path.Combine(_dir, "built", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static RequestResponseLog Marker(string testId, string? keyword, string? text, bool isStart, DateTimeOffset at) =>
        new("Checkout", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = isStart, IsOverrideEnd = !isStart, MarkerKind = DiagramMarkerKind.Step, Timestamp = at,
            PlantUml = isStart ? $"\n{InteractionRecord.StepDelimiterPlantUml(keyword, text)}\n\n" : null,
        };

    private static RequestResponseLog[] Call(string testId, DateTimeOffset at, int number)
    {
        var id = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var uri = new Uri($"http://bank/cards/{number}");
        return
        [
            new RequestResponseLog(testId, testId, HttpMethod.Post, "{\"amount\":12}", uri, [], "bank", "Checkout", RequestResponseType.Request, trace, id, false)
                { Timestamp = at },
            new RequestResponseLog(testId, testId, HttpMethod.Post, "{\"declined\":true}", uri, [], "bank", "Checkout", RequestResponseType.Response, trace, id, false, System.Net.HttpStatusCode.PaymentRequired)
                { Timestamp = at.AddMilliseconds(5) }
        ];
    }
}
