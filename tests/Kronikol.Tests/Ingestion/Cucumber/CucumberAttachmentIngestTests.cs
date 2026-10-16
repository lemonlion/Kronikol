using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;
using static Kronikol.Tests.Ingestion.Cucumber.CucumberBypassTests;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// #175 through <see cref="IngestPipeline"/> and <c>kronikol ingest</c>: a playwright-bdd run's tests file and its
/// Cucumber messages describe every artefact twice, and the report lists each once, under the run's own name, where the
/// messages put it, linked to the file that exists (<c>plans/ATTACHMENT_MERGE_PLAN.md</c> sections 4 and 6.2). The run is
/// <see cref="AttachmentFixture"/>.
/// </summary>
[Collection("DiagramsFetcher")]
public class CucumberAttachmentIngestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cucumber-attachments-" + Guid.NewGuid().ToString("N"));

    public CucumberAttachmentIngestTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    /// <summary>The folder each fact's ingest decodes the messages' attachment bodies under, so no fact sees another's.</summary>
    private string DecodedRoot => PathOf("decoded");

    private IngestResult Ingest(
        string output = "Reports", bool includeHooks = false, string? attachmentsBase = null, string? testsFile = null,
        string? messagesFile = null, bool withTestsFile = true)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = PathOf(output);
        options.HistoryFilePath = PathOf(output + ".history.ndjson");
        options.WriteRunSummaryToConsole = false;
        return IngestPipeline.Run(new IngestRequest
        {
            TestsFile = withTestsFile ? testsFile ?? AttachmentFixture.TestsPath : null,
            CucumberMessagesFiles = [messagesFile ?? AttachmentFixture.MessagesPath],
            AttachmentsBase = attachmentsBase ?? AttachmentFixture.Directory,
            IncludeHooks = includeHooks,
            DecodedAttachmentsRoot = DecodedRoot,
            Options = options,
        });
    }

    private static Scenario Named(IngestResult result, string name) =>
        result.Features.SelectMany(f => f.Scenarios).Single(s => s.DisplayName == name);

    /// <summary>Every attachment of a scenario with what holds it: <c>scenario</c>, or the step's text.</summary>
    private static IEnumerable<(string Holder, FileAttachment Attachment)> Entries(Scenario scenario)
    {
        foreach (var attachment in scenario.Attachments ?? [])
            yield return ("scenario", attachment);
        foreach (var step in Walk(scenario.BackgroundSteps).Concat(Walk(scenario.Steps)))
            foreach (var attachment in step.Attachments ?? [])
                yield return (step.Text, attachment);

        static IEnumerable<ScenarioStep> Walk(ScenarioStep[]? steps) =>
            (steps ?? []).SelectMany(step => new[] { step }.Concat(Walk(step.SubSteps)));
    }

    private static string FileName(FileAttachment attachment) => Path.GetFileName(attachment.RelativePath);

    /// <summary>A copy the synthesis decoded from the messages: its writer numbers every file it writes (<c>0007-trace.zip</c>).</summary>
    private static bool IsDecodedCopy(FileAttachment attachment) => Regex.IsMatch(FileName(attachment), @"^\d{4}-");

    private static string Describe(Scenario scenario) =>
        string.Join("; ", Entries(scenario).Select(e => $"{e.Holder}: {e.Attachment.Name} -> {FileName(e.Attachment)}"));

    /// <summary>The section of <c>Failures.md</c> that works through one failure: its heading to the next heading.</summary>
    internal static string DetailOf(string digest, string scenario)
    {
        var heading = Regex.Match(digest, $@"^### \d+\. .+? › {Regex.Escape(scenario)}$", RegexOptions.Multiline);
        Assert.True(heading.Success, $"Failures.md works through no failure named '{scenario}'");
        var rest = digest[(heading.Index + heading.Length)..];
        var next = Regex.Match(rest, @"^##", RegexOptions.Multiline);
        return next.Success ? rest[..next.Index] : rest;
    }

    // ---- one entry per artefact (F1, F2, F5) -----------------------------------------------------

    [Fact]
    public void Playwrights_own_artefacts_are_listed_once_under_the_runs_names()
    {
        var result = Ingest();
        var scenario = Named(result, AttachmentFixture.FailingScenario);
        var entries = Entries(scenario).ToArray();

        foreach (var builtIn in new[] { "screenshot", "video", "error-context", "trace" })
        {
            var entry = Assert.Single(entries, e => e.Attachment.Name == builtIn);
            // The reporter's file, not the copy the synthesis decoded from the messages.
            Assert.False(IsDecodedCopy(entry.Attachment), $"{builtIn} links {FileName(entry.Attachment)}");
        }

        Assert.DoesNotContain(entries, e => e.Attachment.Name is "screenshot.png" or "video.webm" or "error-context.md" or "trace.zip");
        Assert.Equal(10, entries.Length);
        // Every artefact paired: an entry only one side has (the passing scenario's notes) is not one kept twice.
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("kept twice", StringComparison.Ordinal));
    }

    [Fact]
    public void A_steps_attachment_is_listed_once_on_its_step()
    {
        var scenario = Named(Ingest(), AttachmentFixture.FailingScenario);

        var step = scenario.Steps!.Single(s => s.Text == AttachmentFixture.FailingSteps[1]);
        var shot = Assert.Single(step.Attachments!, a => a.Name == "step-shot.png");
        Assert.Equal("8-step-shot.png", FileName(shot));
        Assert.DoesNotContain(scenario.Attachments ?? [], a => a.Name == "step-shot.png");
    }

    [Fact]
    public void Per_step_screenshots_with_one_name_and_the_same_bytes_land_one_on_each_step()
    {
        // Every after-step.png of the run is the same 506 bytes: only the order can tell them apart, and a slot that took
        // its pair must not take the next one's (the prototype's first cut put all three on the first step).
        var scenario = Named(Ingest(), AttachmentFixture.FailingScenario);

        Assert.Equal(AttachmentFixture.FailingSteps, scenario.Steps!.Select(s => s.Text));
        Assert.Equal(
            ["7-after-step.png", "9-after-step.png", "10-after-step.png"],
            scenario.Steps!.Select(s => string.Join(", ", (s.Attachments ?? []).Where(a => a.Name == "after-step.png").Select(FileName))));
        Assert.DoesNotContain(scenario.Attachments ?? [], a => a.Name == "after-step.png");
    }

    [Fact]
    public void A_background_steps_attachment_is_listed_once_on_the_background_step()
    {
        var scenario = Named(Ingest(), AttachmentFixture.BackgroundScenario);

        var background = Assert.Single(scenario.BackgroundSteps!);
        Assert.Equal("39-after-step.png", FileName(Assert.Single(background.Attachments!, a => a.Name == "after-step.png")));
        Assert.DoesNotContain(scenario.Attachments ?? [], a => a.Name == "after-step.png");
        Assert.Equal(10, Entries(scenario).Count());
    }

    [Fact]
    public void With_hooks_kept_a_hooks_attachment_is_listed_once_on_its_hook_step()
    {
        var scenario = Named(Ingest(includeHooks: true), AttachmentFixture.FailingScenario);

        Assert.True(10 == Entries(scenario).Count(), Describe(scenario));
        Assert.Empty(scenario.Attachments ?? []);
        string[] On(string hook) => [.. scenario.Steps!.Single(s => s.Text == hook).Attachments!.Select(a => a.Name)];
        Assert.Equal(["screenshot", "screenshot-start.png", "error-context", "trace"], On("After Hooks"));
        Assert.Equal(["screenshot-end.png"], On("Fixture \"screenshots\""));
        Assert.Equal(["video"], On("Fixture \"context\""));
        Assert.All(Entries(scenario), e => Assert.False(IsDecodedCopy(e.Attachment), $"{e.Attachment.Name} links {FileName(e.Attachment)}"));
    }

    // ---- the reporter's file when it is there, the messages' copy when it is not (F3) --------------

    [Fact]
    public void When_the_reporters_file_is_gone_the_messages_copy_is_linked()
    {
        // Playwright empties test-results/ on its next run, and a CI job may keep only the two NDJSON files.
        var result = Ingest(attachmentsBase: PathOf("gone"));
        var entries = result.Features.SelectMany(f => f.Scenarios).SelectMany(Entries).ToArray();

        // The only entries the report could not copy are the two note bodies, which the messages carry as text and
        // the synthesis does not write out.
        Assert.Equal(["note", "note"], entries.Where(e => !e.Attachment.RelativePath.StartsWith("attachments/", StringComparison.Ordinal)).Select(e => e.Attachment.Name));
        Assert.All(entries.Where(e => e.Attachment.RelativePath.StartsWith("attachments/", StringComparison.Ordinal)),
            e => Assert.True(File.Exists(Path.Combine(result.ReportsDirectory, e.Attachment.RelativePath)), e.Attachment.RelativePath));

        // Under the reporter's names, where the messages put them.
        var failing = Named(result, AttachmentFixture.FailingScenario);
        Assert.Equal(["screenshot-start.png", "screenshot", "screenshot-end.png", "video", "error-context", "trace"],
            failing.Attachments!.Select(a => a.Name));
        Assert.Equal(["after-step.png", "step-shot.png", "after-step.png", "after-step.png"],
            failing.Steps!.SelectMany(s => s.Attachments ?? []).Select(a => a.Name));
        // Each with its own file's media type.
        Assert.Equal(["image/png", "image/png", "image/png", "video/webm", "text/markdown", "application/zip"],
            failing.Attachments!.Select(a => a.MediaType));
    }

    // ---- the signals (F7) -------------------------------------------------------------------------

    [Fact]
    public void The_signals_are_not_attachments_of_a_scenario_the_messages_own()
    {
        var scenario = Named(Ingest(), AttachmentFixture.BypassScenario);

        Assert.DoesNotContain(Entries(scenario), e => e.Attachment.Name.Equals("kronikol-bypass", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ExecutionResult.Bypassed, scenario.Steps![^1].Status);
    }

    [Fact]
    public void The_signals_stay_attachments_of_a_scenario_the_messages_do_not_own()
    {
        // The tests contract says an attachment is a file whatever its name, and the messages read nothing of a test
        // they do not own.
        var note = PathOf("kronikol-bypass.txt");
        File.WriteAllText(note, "a file of a test the messages never saw");
        var at = DateTimeOffset.Parse("2026-10-15T22:40:00Z");
        var tests = PathOf("tests.ndjson");
        File.WriteAllLines(tests, File.ReadAllLines(AttachmentFixture.TestsPath).Concat(
        [
            new TestRunRecord { Event = "start", TestId = "not-in-the-messages", TestName = "A unit test", Timestamp = at }.ToJson(),
            new TestRunRecord { Event = "attachment", TestId = "not-in-the-messages", Name = "kronikol-bypass", Path = note, MediaType = "text/plain", Timestamp = at.AddSeconds(1) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = "not-in-the-messages", Status = "passed", Timestamp = at.AddSeconds(2) }.ToJson(),
        ]));

        var scenario = Named(Ingest(testsFile: tests), "A unit test");

        Assert.Equal("kronikol-bypass", Assert.Single(scenario.Attachments!).Name);
    }

    // ---- guards -----------------------------------------------------------------------------------

    [Fact]
    public void Text_bodies_only_the_tests_file_has_are_kept_in_order()
    {
        var scenario = Named(Ingest(), AttachmentFixture.PassingScenario);

        Assert.Equal(["14-note", "15-note"], (scenario.Attachments ?? []).Where(a => a.Name == "note").Select(FileName));
    }

    [Fact]
    public void A_retried_scenario_pairs_its_last_attempts_attachments()
    {
        // The reporter writes both attempts; the scenario is the retry's. With or without attempt on the records (the wiki's
        // recipe writes none), and with neither attempt nor timestamp on the attachment records: such a record was placed
        // by the messages' starts, which come after every record of the file, so it was read as the first attempt's and
        // dropped, and the retry linked the decoded copies.
        var withoutAttempts = Rewritten("no-attempts.ndjson", node => node.Remove("attempt"));
        var untimed = Rewritten("untimed.ndjson", node =>
        {
            if ((string?)node["event"] != "attachment")
                return;
            node.Remove("attempt");
            node.Remove("timestamp");
        });

        foreach (var (tests, output) in new[] { (AttachmentFixture.TestsPath, "with"), (withoutAttempts, "without"), (untimed, "untimed") })
        {
            var scenario = Named(Ingest(output: output, testsFile: tests), AttachmentFixture.FlakyScenario);

            Assert.Equal(["23-screenshot-start.png", "24-after-step.png", "25-after-step.png", "26-screenshot-end.png"],
                Entries(scenario).Select(e => FileName(e.Attachment)).Order());
        }
    }

    /// <summary>The fixture's tests file with each record passed through <paramref name="change"/>, written to this fact's folder.</summary>
    private string Rewritten(string name, Action<JsonObject> change)
    {
        var path = PathOf(name);
        File.WriteAllLines(path, File.ReadAllLines(AttachmentFixture.TestsPath).Select(line =>
        {
            var node = JsonNode.Parse(line)!.AsObject();
            change(node);
            return node.ToJsonString();
        }));
        return path;
    }

    [Fact]
    public void A_step_index_on_the_reporters_records_changes_nothing()
    {
        // The messages' step markers give a scenario they own its steps in the tests file's model too, so a record's step
        // index put its attachment on a step there, and the merge, reading the scenario's own list, dropped it: the step
        // screenshots linked the decoded copies and the passing scenario's two notes, which only the tests file has, were
        // gone. The index is the step each artefact was made in.
        var made = new Dictionary<(string, string), int>();
        var indexed = Rewritten("indexed.ndjson", node =>
        {
            if ((string?)node["event"] != "attachment")
                return;
            var key = ((string)node["testId"]!, node["attempt"]?.ToJsonString() ?? "");
            switch ((string?)node["name"])
            {
                case "after-step.png":
                    node["step"] = made[key] = made.GetValueOrDefault(key, -1) + 1;
                    break;
                case "step-shot.png" or "note":
                    node["step"] = 1;
                    break;
            }
        });

        var plain = Ingest(output: "plain");
        var withIndexes = Ingest(output: "indexed", testsFile: indexed);

        // As a set: a step index may move an entry within the scenario's own list, which keeps the tests file's order.
        foreach (var name in new[] { AttachmentFixture.FailingScenario, AttachmentFixture.PassingScenario, AttachmentFixture.FlakyScenario })
            Assert.Equal(Lines(Named(plain, name)), Lines(Named(withIndexes, name)));
        Assert.Equal(["14-note", "15-note"],
            (Named(withIndexes, AttachmentFixture.PassingScenario).Attachments ?? []).Where(a => a.Name == "note").Select(FileName));

        static IEnumerable<string> Lines(Scenario scenario) =>
            Entries(scenario).Select(e => $"{e.Holder}: {e.Attachment.Name} -> {FileName(e.Attachment)}").Order(StringComparer.Ordinal);
    }

    [Fact]
    public void Two_scenarios_each_keeping_a_name_twice_are_counted_together()
    {
        // The count is the run's, not the last scenario's: the failing and the background scenario each keep their trace
        // twice when the reporter's file has other bytes than the messages' copy (here one byte changed).
        var changed = new List<string>();
        var tests = Rewritten("changed.ndjson", node =>
        {
            if ((string?)node["path"] is not { } path || !path.EndsWith("-retry1/trace.zip", StringComparison.Ordinal))
                return;
            var bytes = File.ReadAllBytes(Path.Combine(AttachmentFixture.Directory, path));
            bytes[^1] ^= 0xFF;
            var copy = PathOf($"{changed.Count}-trace.zip");
            File.WriteAllBytes(copy, bytes);
            changed.Add(copy);
            node["path"] = copy;
        });
        Assert.Equal(2, changed.Count);

        var result = Ingest(testsFile: tests);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Message.Contains("kept twice", StringComparison.Ordinal));
        Assert.StartsWith("2 attachment(s)", diagnostic.Message, StringComparison.Ordinal);
    }

    // ---- two copies with one name and different bytes (Q4) -----------------------------------------

    [Fact]
    public void A_name_pair_with_different_bytes_keeps_both_and_says_so()
    {
        // Neither copy can be known to be the stale one: a reporter that rewrote its file after the run, or a hook that
        // attached another artefact under the same name.
        var messages = PathOf("messages.ndjson");
        File.WriteAllText(messages, Stream(
            new Step("the page is open", "PASSED"),
            new Step("After Hooks", "PASSED", Hook: true, Attachments: [Zip("trace", [1, 2, 3, 4])])));
        var trace = PathOf("trace.zip");
        // As long as the copy: the bytes, not the length, tell them apart.
        File.WriteAllBytes(trace, [9, 9, 9, 9]);
        var tests = PathOf("tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "attachment", TestId = "bypass-test-1", Name = "trace", Path = trace, MediaType = "application/zip", Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:30Z") }.ToJson(),
        ]);

        var result = Ingest(testsFile: tests, messagesFile: messages);

        var scenario = result.Features.SelectMany(f => f.Scenarios).Single();
        Assert.Equal(["trace", "trace.zip"], scenario.Attachments!.Select(a => a.Name));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Message.Contains("kept twice", StringComparison.Ordinal));
        Assert.Equal(DiagnosticKind.Other, diagnostic.Kind);
        Assert.StartsWith("1 attachment(s)", diagnostic.Message, StringComparison.Ordinal);
    }

    internal static Attachment Zip(string name, byte[] bytes, string mediaType = "application/zip") =>
        new(name, Convert.ToBase64String(bytes), "BASE64", mediaType);

    // ---- the staging folder (F6) ------------------------------------------------------------------

    [Fact]
    public void An_ingest_leaves_no_staging_folder()
    {
        var result = Ingest();

        Assert.True(result.Generated);
        // The ingest decoded under this root (it made the root), and took its own folder away.
        Assert.True(Directory.Exists(DecodedRoot), DecodedRoot);
        Assert.Empty(Directory.EnumerateFileSystemEntries(DecodedRoot));
    }

    [Fact]
    public void An_ingest_leaves_no_staging_folder_unless_the_report_still_links_into_it()
    {
        // The generator could not copy the decoded file (here a folder already has its name in attachments/), so the
        // report links where the file is: deleting it would turn a recorded failure into a dead link.
        var messages = PathOf("messages.ndjson");
        File.WriteAllText(messages, Stream(new Step("the page is open", "PASSED", Attachments: [Zip("shot.png", [137, 80, 78, 71], "image/png")])));
        Directory.CreateDirectory(Path.Combine(PathOf("Reports"), "attachments", "0000-shot.png"));

        var result = Ingest(messagesFile: messages, withTestsFile: false);

        var shot = Assert.Single(result.Features.SelectMany(f => f.Scenarios).Single().Steps!.Single().Attachments!);
        Assert.StartsWith(DecodedRoot, shot.RelativePath, StringComparison.Ordinal);
        Assert.True(File.Exists(shot.RelativePath), shot.RelativePath);
        Assert.Contains(result.Diagnostics, d => d.Kind == DiagnosticKind.AttachmentFailure);
    }

    [Fact]
    public void A_background_steps_copy_the_report_still_links_keeps_the_staging_folder()
    {
        // The same rule for a copy on a background step, which the folder's check has to walk as well as the steps. The
        // first ingest names the copy; a folder of that name in the second's attachments/ makes its copy fail.
        static FileAttachment OnTheBackground(IngestResult result) =>
            Named(result, AttachmentFixture.BackgroundScenario).BackgroundSteps!.SelectMany(s => s.Attachments ?? []).First();
        var copied = OnTheBackground(Ingest(output: "first", withTestsFile: false));
        Assert.StartsWith("attachments/", copied.RelativePath, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(PathOf("second"), copied.RelativePath));

        var stuck = OnTheBackground(Ingest(output: "second", withTestsFile: false));

        Assert.StartsWith(DecodedRoot, stuck.RelativePath, StringComparison.Ordinal);
        Assert.True(File.Exists(stuck.RelativePath), stuck.RelativePath);
    }

    // ---- what the agent reads (F9), and the CLI ------------------------------------------------------

    [Fact]
    public void The_ingest_cli_writes_one_file_per_artefact()
    {
        var output = PathOf("cli");
        var exit = IngestCommand.Run(
        [
            "--tests", AttachmentFixture.TestsPath, "--cucumber-messages", AttachmentFixture.MessagesPath,
            "--attachments-base", AttachmentFixture.Directory, "-o", output,
        ], new StringWriter(), new StringWriter());
        Assert.Equal(0, exit);

        // Ten for each failing scenario, seven for the passing one (the two notes are files the reporter wrote), four
        // for the retry and four for the bypass, whose kronikol-bypass is a signal.
        var files = Directory.GetFiles(Path.Combine(output, "attachments")).Select(Path.GetFileName).ToArray();
        Assert.True(35 == files.Length, string.Join(", ", files));
        Assert.DoesNotContain(files, f => Regex.IsMatch(f!, @"^\d{4}-"));

        // Failures.md lists each artefact of the failure it works through once. The background scenario fails with the
        // same message, so it is clustered with this one and not worked through.
        var detail = DetailOf(File.ReadAllText(Path.Combine(output, "Failures.md")), AttachmentFixture.FailingScenario);
        var listed = Regex.Matches(detail, @"^- .+? — `([^`]+)`", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.True(10 == listed.Length, string.Join(", ", listed));
        Assert.Equal(listed.Length, listed.Distinct().Count());
        Assert.DoesNotContain(listed, path => Regex.IsMatch(Path.GetFileName(path), @"^\d{4}-"));

        // And `query failures` gives the failing scenario four different artefacts, where it gave a duplicate.
        var query = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", output], query, new StringWriter()));
        var block = query.ToString().Split("\n\n").First(b => b.Contains(AttachmentFixture.FailingScenario, StringComparison.Ordinal));
        var own = Regex.Matches(block, @"^  attachment: (.+?) →", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["screenshot-start.png", "screenshot", "screenshot-end.png", "video"], own);
    }

    [Fact]
    public void With_hooks_kept_the_digest_still_lists_the_failure_artefacts()
    {
        // Every pair sits on a hook step, and the scenario's own list is empty: a digest that read only that list said
        // nothing about the failure's screenshot, trace or video.
        var result = Ingest(includeHooks: true);
        var scenario = Named(result, AttachmentFixture.FailingScenario);
        var afterHooks = Array.FindIndex(scenario.Steps!, s => s.Text == "After Hooks");
        var context = Array.FindIndex(scenario.Steps!, s => s.Text == "Fixture \"context\"");

        var section = DetailOf(File.ReadAllText(Path.Combine(result.ReportsDirectory, "Failures.md")), AttachmentFixture.FailingScenario);

        foreach (var (name, step) in new[] { ("screenshot", afterHooks), ("trace", afterHooks), ("error-context", afterHooks), ("video", context) })
            Assert.Matches(new Regex($@"^- {Regex.Escape(name)} — `attachments/[^`]+` \(step `s0/{step}`\)$", RegexOptions.Multiline), section);
    }

    [Fact]
    public void With_hooks_kept_the_step_markers_match_the_steps()
    {
        // Every kept hook was listed after the Gherkin steps, while the step markers follow the order the steps ran: each
        // marker missed its step, and the calls made in a step lost their step. A scenario under a background still lists
        // its before hook after the background steps, the one place the order cannot follow the run: its first two
        // markers (the hook's and the background step's) miss, and the rest find their steps.
        var result = Ingest(includeHooks: true);

        var missed = result.Diagnostics.Where(d => d.Kind == DiagnosticKind.StepAttributionMismatch).ToArray();
        Assert.All(missed, d => Assert.Equal(Named(result, AttachmentFixture.BackgroundScenario).Id, d.ScenarioId));
        Assert.Equal(2, missed.Length);
    }
}
