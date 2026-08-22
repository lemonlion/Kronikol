using Kronikol.Ingestion.Cucumber;

namespace Kronikol.Tests.Ingestion.Cucumber;

public class CucumberMessagesReaderTests
{
    [Fact]
    public void Reads_every_envelope_kind_of_the_golden_playwright_bdd_fixture()
    {
        var messages = CucumberFixtures.Read();

        Assert.Equal(2, messages.GherkinDocuments.Count);
        Assert.Equal(6, messages.Pickles.Count);          // 4 scenarios + 2 outline rows
        Assert.Equal(6, messages.TestCases.Count);
        Assert.Equal(8, messages.TestCaseStarted.Count);  // 6 first attempts + 2 retries
        Assert.Equal(8, messages.TestCaseFinished.Count);
        Assert.NotEmpty(messages.TestStepStarted);
        Assert.NotEmpty(messages.TestStepFinished);
        Assert.NotEmpty(messages.Attachments);
        Assert.NotNull(messages.TestRunStarted);
        Assert.NotNull(messages.TestRunFinished);
        Assert.Equal(0, messages.MalformedLines);
        Assert.False(messages.IsEmpty);
    }

    [Fact]
    public void Reads_the_producer_metadata()
    {
        var messages = CucumberFixtures.Read();

        Assert.Equal("playwright-bdd", messages.Meta?.Implementation?.Name);
        Assert.StartsWith("32.", messages.Meta?.ProtocolVersion);
    }

    [Fact]
    public void Counts_source_envelopes_as_unknown_rather_than_failing()
    {
        // The fixture carries two `source` envelopes (the raw feature text) that this reader does not consume.
        var messages = CucumberFixtures.Read();

        Assert.Equal(2, messages.UnknownEnvelopes);
        // The count is what the synthesiser turns into a diagnostic; the reader itself never fails.
        Assert.Contains(CucumberFeatureSynthesizer.Build(messages).Warnings,
            w => w.Contains("unknown type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unknown_envelope_type_is_ignored()
    {
        var messages = CucumberMessagesReader.Read(new StringReader(
            """
            {"someFutureEnvelope":{"id":"1","whatever":true}}
            {"pickle":{"id":"p1","name":"Only pickle","steps":[]}}
            """));

        Assert.Single(messages.Pickles);
        Assert.Equal(1, messages.UnknownEnvelopes);
        Assert.Equal(0, messages.MalformedLines);
    }

    [Fact]
    public void Unknown_properties_inside_a_known_envelope_are_ignored()
    {
        var messages = CucumberMessagesReader.Read(new StringReader(
            """{"pickle":{"id":"p1","name":"With extras","brandNewField":{"nested":[1,2]},"steps":[]}}"""));

        Assert.Equal("With extras", Assert.Single(messages.Pickles).Name);
        Assert.Equal(0, messages.MalformedLines);
    }

    [Fact]
    public void A_malformed_line_is_counted_and_skipped()
    {
        var messages = CucumberMessagesReader.Read(new StringReader(
            """
            {"pickle":{"id":"p1","name":"Good","steps":[]}}
            {"pickle": not json at all
            42
            {"pickle":{"id":"p2","name":"Also good","steps":[]}}
            """), "fixture.ndjson");

        Assert.Equal(2, messages.Pickles.Count);
        Assert.Equal(2, messages.MalformedLines);
        Assert.Contains(messages.Warnings, w => w.StartsWith("fixture.ndjson:2"));
        Assert.Contains(messages.Warnings, w => w.StartsWith("fixture.ndjson:3"));
    }

    [Fact]
    public void An_empty_file_reads_as_empty()
    {
        var messages = CucumberMessagesReader.Read(new StringReader("\n   \n"));

        Assert.True(messages.IsEmpty);
        Assert.Equal(0, messages.MalformedLines);
        Assert.Empty(messages.Warnings);
    }

    [Fact]
    public void Reading_several_files_merges_them()
    {
        var messages = CucumberMessagesReader.ReadFiles([CucumberFixtures.MessagesPath, CucumberFixtures.MessagesPath]);

        Assert.Equal(12, messages.Pickles.Count);
        Assert.Equal(4, messages.GherkinDocuments.Count);
    }

    [Fact]
    public void A_missing_file_throws_FileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            CucumberMessagesReader.ReadFile(Path.Combine(Path.GetTempPath(), "no-such-messages-" + Guid.NewGuid().ToString("N") + ".ndjson")));
    }

    [Fact]
    public void Timestamps_convert_seconds_and_nanos_to_an_instant()
    {
        var timestamp = new CucumberTimestamp { Seconds = 1_700_000_000, Nanos = 250_000_000 };

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddMilliseconds(250), timestamp.ToInstant());
        Assert.Equal(TimeSpan.FromMilliseconds(250), new CucumberTimestamp { Nanos = 250_000_000 }.ToDuration());
    }

    [Fact]
    public void The_generated_spec_fixture_still_carries_the_bdd_data_block()
    {
        // The sidekick reporter parses this block as the fallback source when no messages file exists;
        // the fixture pins its shape for playwright-bdd 9.2.
        var spec = File.ReadAllText(CucumberFixtures.GeneratedSpecPath);

        Assert.Contains("const bddFileData = [ // bdd-data-start", spec);
        Assert.Contains("]; // bdd-data-end", spec);
        foreach (var key in new[] { "pwTestLine", "pickleLine", "tags", "steps", "pwStepLine", "gherkinStepLine", "keywordType", "textWithKeyword", "isBg", "stepMatchArguments" })
            Assert.Contains($"\"{key}\"", spec);
    }
}
