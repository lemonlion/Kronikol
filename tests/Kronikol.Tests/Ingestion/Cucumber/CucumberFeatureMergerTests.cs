using Kronikol.Ingestion;
using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion.Cucumber;

public class CucumberFeatureMergerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merger-" + Guid.NewGuid().ToString("N"));

    public CucumberFeatureMergerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static CucumberSynthesisResult BuildCucumber(string testId, params (string Keyword, string Text, int StartSeconds)[] steps)
    {
        var windows = new List<CucumberStepWindow>();
        var scenarioSteps = new List<ScenarioStep>();
        foreach (var (keyword, text, start) in steps)
        {
            var step = new ScenarioStep { Keyword = keyword, Text = text, Status = ExecutionResult.Passed };
            scenarioSteps.Add(step);
            windows.Add(new CucumberStepWindow(step, T0.AddSeconds(start), T0.AddSeconds(start + 1)));
        }

        var scenario = new Scenario
        {
            Id = testId,
            DisplayName = "A gherkin scenario",
            Result = ExecutionResult.Passed,
            Steps = scenarioSteps.ToArray(),
        };
        var feature = new Feature { DisplayName = "Gherkin feature", Scenarios = [scenario] };
        return new CucumberSynthesisResult(
            [feature],
            T0.UtcDateTime,
            T0.AddSeconds(10).UtcDateTime,
            [],
            new Dictionary<string, string> { [testId] = scenario.DisplayName },
            new Dictionary<string, IReadOnlyList<CucumberStepWindow>> { [testId] = windows },
            new HashSet<string> { testId },
            []);
    }

    [Fact]
    public void With_nothing_to_own_the_tests_file_model_is_returned_unchanged()
    {
        var fromTests = FeatureSynthesizer.Build(
            [new TestRunRecord { Event = "start", TestId = "t1", TestName = "plain", Timestamp = T0 }], null);
        var empty = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader("")));

        var merged = CucumberFeatureMerger.Merge(empty, fromTests);

        Assert.Same(fromTests, merged);
    }

    [Fact]
    public void Assertions_nest_under_the_step_that_was_running()
    {
        const string testId = "t1";
        var cucumber = BuildCucumber(testId, ("Given", "a customer", 0), ("When", "an order is placed", 5));
        var records = new[]
        {
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the customer exists", Status = "passed", Timestamp = T0.AddSeconds(0.5) },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the order is confirmed", Status = "failed", Error = "nope", Timestamp = T0.AddSeconds(5.5) },
            // Between the two steps: belongs to the step whose bar was last drawn.
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "in between", Status = "passed", Timestamp = T0.AddSeconds(3) },
        };
        var fromTests = FeatureSynthesizer.Build(records, null);

        var merged = CucumberFeatureMerger.Merge(cucumber, fromTests, records);

        var scenario = Assert.Single(merged.Features.SelectMany(f => f.Scenarios));
        Assert.Equal(
            new[] { "the customer exists", "in between" }.Select(t => $"{Track.PassSymbol} {t}"),
            scenario.Steps![0].SubSteps!.Select(s => s.Text));
        var failed = Assert.Single(scenario.Steps![1].SubSteps!);
        Assert.Equal($"{Track.FailSymbol} the order is confirmed", failed.Text);
        Assert.Equal(ExecutionResult.Failed, failed.Status);
        Assert.Contains("nope", failed.Comments!);

        // Each carries the data file's assertion mark (#145); the Gherkin steps do not.
        Assert.All(scenario.Steps!.SelectMany(s => s.SubSteps!), step => Assert.True(step.IsAssertion));
        Assert.All(scenario.Steps!, step => Assert.False(step.IsAssertion));
    }

    [Fact]
    public void An_assertion_before_every_step_becomes_a_top_level_row()
    {
        const string testId = "t1";
        var cucumber = BuildCucumber(testId, ("Given", "a customer", 5));
        var records = new[]
        {
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "before anything ran", Status = "passed", Timestamp = T0 },
        };

        var merged = CucumberFeatureMerger.Merge(cucumber, FeatureSynthesizer.Build(records, null), records);

        var scenario = Assert.Single(merged.Features.SelectMany(f => f.Scenarios));
        Assert.Equal(2, scenario.Steps!.Length);
        Assert.Equal($"{Track.PassSymbol} before anything ran", scenario.Steps[^1].Text);
    }

    [Fact]
    public void Reporter_step_events_of_an_owned_scenario_are_the_ones_the_messages_replace()
    {
        var cucumber = BuildCucumber("t1", ("Given", "a customer", 0));

        Assert.True(CucumberFeatureMerger.IsReplacedStep(
            new TestRunRecord { Event = "step", TestId = "t1", Text = "reporter step" }, cucumber));
        Assert.False(CucumberFeatureMerger.IsReplacedStep(
            new TestRunRecord { Event = "step", TestId = "other", Text = "reporter step" }, cucumber));
        Assert.False(CucumberFeatureMerger.IsReplacedStep(
            new TestRunRecord { Event = "assertion", TestId = "t1", Text = "an assertion" }, cucumber));
    }

    [Fact]
    public void The_tests_file_still_contributes_attachments_and_a_failure_the_gherkin_steps_never_saw()
    {
        const string testId = "t1";
        var cucumber = BuildCucumber(testId, ("Given", "a customer", 0));
        var fromTests = FeatureSynthesizer.Build(
            [new TestRunRecord { Event = "end", TestId = testId, Status = "failed", Error = "Test timeout of 30000ms exceeded", Timestamp = T0.AddSeconds(30) }],
            null);
        fromTests.Features[0].Scenarios[0].Attachments = [new FileAttachment("trace.zip", "/tmp/trace.zip", "application/zip")];

        var merged = CucumberFeatureMerger.Merge(cucumber, fromTests);

        var scenario = Assert.Single(merged.Features.SelectMany(f => f.Scenarios));
        Assert.Equal("Test timeout of 30000ms exceeded", scenario.ErrorMessage);
        Assert.Equal("trace.zip", Assert.Single(scenario.Attachments!).Name);
    }

    [Fact]
    public void Unowned_scenarios_are_kept_and_ordered_after_the_gherkin_features()
    {
        var cucumber = BuildCucumber("t1", ("Given", "a customer", 0));
        var fromTests = FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "t1", TestName = "reporter name", Feature = "Gherkin feature", Timestamp = T0 },
            new TestRunRecord { Event = "start", TestId = "t2", TestName = "a unit test", Feature = "Units", Timestamp = T0 },
        ], null);

        var merged = CucumberFeatureMerger.Merge(cucumber, fromTests);

        Assert.Equal(["Gherkin feature", "Units"], merged.Features.Select(f => f.DisplayName));
        // The scenario the messages own keeps the Gherkin display name, not the reporter's.
        Assert.Equal("A gherkin scenario", merged.Features[0].Scenarios[0].DisplayName);
        Assert.Equal("a unit test", merged.Features[1].Scenarios[0].DisplayName);
        Assert.Equal("A gherkin scenario", merged.TestNames["t1"]);
    }

    // ---- attachments (#175, plans/ATTACHMENT_MERGE_PLAN.md section 4) -------------------------------

    /// <summary>What the reporter recorded for one attachment: a file with these bytes, a file that is gone (no bytes), or a link.</summary>
    private sealed record Reported(string Name, byte[]? Bytes, string? MediaType = null, string? Url = null);

    private string Decoded => Path.Combine(_dir, "decoded");

    /// <summary>The messages' side: one scenario (the test id <c>bypass-test-1</c>), its bodies decoded under <see cref="Decoded"/>.</summary>
    private CucumberSynthesisResult Messages(params CucumberBypassTests.Step[] steps) =>
        CucumberBypassTests.Synthesise(CucumberBypassTests.Stream(steps), new CucumberSynthesisOptions { AttachmentsDirectory = Decoded });

    /// <summary>The tests file's side: the reporter's records of the same test, each file written here.</summary>
    private FeatureSynthesizer.Result Reporter(params Reported[] attachments)
    {
        var at = DateTimeOffset.Parse("2026-09-28T14:23:30Z");
        var records = new List<TestRunRecord> { new() { Event = "start", TestId = "bypass-test-1", Timestamp = at } };
        for (var i = 0; i < attachments.Length; i++)
        {
            var (name, bytes, mediaType, url) = attachments[i];
            var path = url ?? Path.Combine(_dir, "reporter", $"{i}-{name}");
            if (url is null && bytes is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            records.Add(new TestRunRecord { Event = "attachment", TestId = "bypass-test-1", Name = name, Path = path, MediaType = mediaType, Timestamp = at.AddSeconds(i + 1) });
        }

        records.Add(new TestRunRecord { Event = "end", TestId = "bypass-test-1", Status = "failed", Timestamp = at.AddSeconds(attachments.Length + 1) });
        return FeatureSynthesizer.Build(records, null);
    }

    private static Scenario Only(FeatureSynthesizer.Result result) => result.Features.SelectMany(f => f.Scenarios).Single();

    private static CucumberBypassTests.Attachment Base64(string name, byte[] bytes, string mediaType = "application/zip") =>
        new(name, Convert.ToBase64String(bytes), "BASE64", mediaType);

    [Fact]
    public void The_reporters_attachments_win_over_the_messages_copies_of_the_same_artefact()
    {
        // playwright-bdd inlines every attachment as BASE64 into the messages, so a screenshot the reporter already
        // materialised arrived twice (seen live: 58 files for 29 screenshots). The reporter's file and name are kept, a
        // link is kept as it is, and what only the messages carry is kept after the reporter's entries.
        byte[] shot = [137, 80, 78, 71];
        var messages = Messages(
            new CucumberBypassTests.Step("the page is open", "PASSED"),
            new CucumberBypassTests.Step("After Hooks", "PASSED", Hook: true, Attachments:
            [
                Base64("screenshot-start.png", shot, "image/png"),
                Base64("only-in-messages.txt", "only here"u8.ToArray(), "text/plain"),
            ]));
        var reporter = Reporter(
            new Reported("screenshot-start.png", shot, "image/png"),
            new Reported("Grafana trace", null, "text/uri-list", Url: "http://localhost:3900/explore"));
        var reportersFile = Only(reporter).Attachments![0].RelativePath;

        var scenario = Only(CucumberFeatureMerger.Merge(messages, reporter));

        Assert.Equal(["screenshot-start.png", "Grafana trace", "only-in-messages.txt"], scenario.Attachments!.Select(a => a.Name));
        Assert.Equal(reportersFile, scenario.Attachments![0].RelativePath);
        Assert.Equal("http://localhost:3900/explore", scenario.Attachments[1].RelativePath);
    }

    [Fact]
    public void Two_artefacts_with_one_name_stay_where_each_was_attached()
    {
        // The issue's scale: the failing step attaches its own trace, and Playwright adds its trace on failure. A pair
        // made by the name alone gave each the other's file once the steps' lists were compared (measured: the 58 MB
        // trace moved to the scenario and Playwright's 7.5 MB one to the step).
        byte[] own = [1, 1, 1], playwrights = [2, 2, 2, 2];
        var messages = Messages(
            new CucumberBypassTests.Step("the big check fails with a big trace", "FAILED", "boom", Attachments: [Base64("trace", own)]),
            new CucumberBypassTests.Step("After Hooks", "PASSED", Hook: true, Attachments: [Base64("trace", playwrights)]));

        var scenario = Only(CucumberFeatureMerger.Merge(messages,
            Reporter(new Reported("trace", own, "application/zip"), new Reported("trace", playwrights, "application/zip"))));

        Assert.Equal(own, File.ReadAllBytes(Assert.Single(scenario.Steps![0].Attachments!).RelativePath));
        Assert.Equal(playwrights, File.ReadAllBytes(Assert.Single(scenario.Attachments!).RelativePath));
    }

    [Fact]
    public void A_copy_from_the_messages_keeps_its_own_media_type()
    {
        // With the reporter's files gone, two attachments with one name pair by order alone and may cross: each entry is
        // then the messages' copy in its own place, with that copy's media type, so an image is never labelled a zip.
        byte[] picture = [137, 80, 78, 71], archive = [80, 75, 3, 4];
        var messages = Messages(
            new CucumberBypassTests.Step("the page is open", "FAILED", "boom", Attachments: [Base64("evidence", archive)]),
            new CucumberBypassTests.Step("After Hooks", "PASSED", Hook: true, Attachments: [Base64("evidence", picture, "image/png")]));

        var scenario = Only(CucumberFeatureMerger.Merge(messages,
            Reporter(new Reported("evidence", null, "application/zip"), new Reported("evidence", null, "image/png"))));

        var image = Assert.Single(scenario.Attachments!);
        Assert.Equal(picture, File.ReadAllBytes(image.RelativePath));
        Assert.Equal("image/png", image.MediaType);
        Assert.True(image.IsInlineImage);
        var zip = Assert.Single(scenario.Steps![0].Attachments!);
        Assert.Equal(archive, File.ReadAllBytes(zip.RelativePath));
        Assert.Equal("application/zip", zip.MediaType);
    }

    [Fact]
    public void A_reporters_file_keeps_the_reporters_media_type()
    {
        // The entry links the reporter's file, so it says what the reporter said that file is.
        byte[] trace = [80, 75, 3, 4];
        var messages = Messages(new CucumberBypassTests.Step("After Hooks", "PASSED", Hook: true, Attachments: [Base64("trace", trace, "application/zip")]));

        var scenario = Only(CucumberFeatureMerger.Merge(messages,
            Reporter(new Reported("trace", trace, "application/vnd.playwright.trace+zip"))));

        var entry = Assert.Single(scenario.Attachments!);
        Assert.StartsWith(Path.Combine(_dir, "reporter"), entry.RelativePath, StringComparison.Ordinal);
        Assert.Equal("application/vnd.playwright.trace+zip", entry.MediaType);
    }

    [Fact]
    public void A_link_is_never_paired()
    {
        // A link names something that lives elsewhere, never a copy of a file, whatever it is called.
        byte[] shot = [137, 80, 78, 71];
        var messages = Messages(new CucumberBypassTests.Step("the page is open", "PASSED", Attachments: [Base64("screenshot.png", shot, "image/png")]));

        var scenario = Only(CucumberFeatureMerger.Merge(messages,
            Reporter(new Reported("screenshot.png", null, "text/uri-list", Url: "http://localhost:9323/report/screenshot.png"))));

        Assert.Equal("http://localhost:9323/report/screenshot.png", Assert.Single(scenario.Attachments!).RelativePath);
        Assert.Equal(shot, File.ReadAllBytes(Assert.Single(scenario.Steps![0].Attachments!).RelativePath));
    }

    [Fact]
    public void The_scenario_list_keeps_the_reporters_order_then_the_messages_own()
    {
        byte[] shared = [1, 2, 3], own = [4, 5, 6], theirs = [7, 8, 9];
        var messages = Messages(
            new CucumberBypassTests.Step("the page is open", "PASSED"),
            new CucumberBypassTests.Step("After Hooks", "PASSED", Hook: true, Attachments: [Base64("only-in-messages.zip", theirs), Base64("shared.zip", shared)]));

        var scenario = Only(CucumberFeatureMerger.Merge(messages,
            Reporter(new Reported("only-in-tests.zip", own, "application/zip"), new Reported("shared.zip", shared, "application/zip"))));

        Assert.Equal(["only-in-tests.zip", "shared.zip", "only-in-messages.zip"], scenario.Attachments!.Select(a => a.Name));
    }

    [Fact]
    public void The_decoded_copies_a_reporters_file_replaced_are_deleted()
    {
        byte[] shot = [137, 80, 78, 71], other = [1, 2, 3];
        var messages = Messages(
            new CucumberBypassTests.Step("the page is open", "PASSED"),
            new CucumberBypassTests.Step("After Hooks", "PASSED", Hook: true, Attachments:
            [
                Base64("shot.png", shot, "image/png"),
                Base64("only-in-messages.png", other, "image/png"),
            ]));
        Assert.Equal(2, Directory.GetFiles(Decoded).Length);

        var scenario = Only(CucumberFeatureMerger.Merge(messages, Reporter(new Reported("shot.png", shot, "image/png"))));

        // What is left in the folder is what the result links, and nothing else.
        var linked = scenario.Attachments!.Select(a => a.RelativePath).Where(p => p.StartsWith(Decoded, StringComparison.Ordinal));
        Assert.Equal(linked.Order(), Directory.GetFiles(Decoded).Order());
        Assert.Equal("only-in-messages.png", Path.GetFileName(Assert.Single(Directory.GetFiles(Decoded)))[5..]);
    }
}
