using System.Text.Json.Serialization;

namespace Kronikol.Ingestion.Cucumber;

// The subset of the Cucumber Messages protocol (schema 32.x) Kronikol needs. Every producer of the
// protocol writes these envelopes: playwright-bdd's `cucumberReporter('message')`, cucumber-js
// `--format message`, Cucumber-JVM `--plugin message:…`. Fields Kronikol does not use are simply not
// declared — System.Text.Json ignores them, which is what keeps the reader tolerant of version drift.

/// <summary>A Cucumber Messages timestamp / duration: whole seconds plus nanoseconds.</summary>
public sealed record CucumberTimestamp
{
    /// <summary>Whole seconds (Unix epoch for timestamps, elapsed seconds for durations).</summary>
    [JsonPropertyName("seconds")] public long Seconds { get; init; }

    /// <summary>Nanosecond part.</summary>
    [JsonPropertyName("nanos")] public long Nanos { get; init; }

    /// <summary>As an absolute instant (UTC).</summary>
    public DateTimeOffset ToInstant() => DateTimeOffset.FromUnixTimeSeconds(Seconds).AddTicks(Nanos / 100);

    /// <summary>As an elapsed duration.</summary>
    public TimeSpan ToDuration() => TimeSpan.FromSeconds(Seconds) + TimeSpan.FromTicks(Nanos / 100);
}

/// <summary>Producer metadata (<c>meta</c>) — who wrote the file and which protocol version.</summary>
public sealed record CucumberMeta
{
    /// <summary>Cucumber Messages schema version, e.g. <c>32.2.0</c>.</summary>
    [JsonPropertyName("protocolVersion")] public string? ProtocolVersion { get; init; }

    /// <summary>The producing tool.</summary>
    [JsonPropertyName("implementation")] public CucumberProduct? Implementation { get; init; }
}

/// <summary>A named, versioned product in <see cref="CucumberMeta"/>.</summary>
public sealed record CucumberProduct
{
    /// <summary>Product name, e.g. <c>playwright-bdd</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Product version.</summary>
    [JsonPropertyName("version")] public string? Version { get; init; }
}

/// <summary>A tag on a Gherkin node (<c>@happy-path</c>) — the name keeps its leading <c>@</c>.</summary>
public sealed record CucumberTag
{
    /// <summary>The tag id, referenced by <see cref="CucumberPickleTag.AstNodeId"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>The tag text including the leading <c>@</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
}

/// <summary>A source location inside a feature file.</summary>
public sealed record CucumberLocation
{
    /// <summary>1-based line.</summary>
    [JsonPropertyName("line")] public int Line { get; init; }

    /// <summary>1-based column.</summary>
    [JsonPropertyName("column")] public int Column { get; init; }
}

/// <summary>The <c>gherkinDocument</c> envelope: one parsed feature file.</summary>
public sealed record CucumberGherkinDocument
{
    /// <summary>Path of the feature file, as the producer saw it.</summary>
    [JsonPropertyName("uri")] public string? Uri { get; init; }

    /// <summary>The feature, when the file contains one.</summary>
    [JsonPropertyName("feature")] public CucumberFeatureNode? Feature { get; init; }
}

/// <summary>The <c>feature</c> node of a Gherkin document.</summary>
public sealed record CucumberFeatureNode
{
    /// <summary>Feature-level tags.</summary>
    [JsonPropertyName("tags")] public CucumberTag[]? Tags { get; init; }

    /// <summary>The localised keyword (<c>Feature</c>).</summary>
    [JsonPropertyName("keyword")] public string? Keyword { get; init; }

    /// <summary>Feature name.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Free text under the <c>Feature:</c> line.</summary>
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>Backgrounds, rules and scenarios, in file order.</summary>
    [JsonPropertyName("children")] public CucumberFeatureChild[]? Children { get; init; }
}

/// <summary>One child of a feature or rule — exactly one of the three properties is set.</summary>
public sealed record CucumberFeatureChild
{
    /// <summary>A <c>Background:</c> block.</summary>
    [JsonPropertyName("background")] public CucumberScenarioNode? Background { get; init; }

    /// <summary>A <c>Rule:</c> block.</summary>
    [JsonPropertyName("rule")] public CucumberRuleNode? Rule { get; init; }

    /// <summary>A <c>Scenario:</c> / <c>Scenario Outline:</c> block.</summary>
    [JsonPropertyName("scenario")] public CucumberScenarioNode? Scenario { get; init; }
}

/// <summary>A <c>Rule:</c> node.</summary>
public sealed record CucumberRuleNode
{
    /// <summary>Node id.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Rule name — the text after <c>Rule:</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Free text under the <c>Rule:</c> line.</summary>
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>Rule-level tags.</summary>
    [JsonPropertyName("tags")] public CucumberTag[]? Tags { get; init; }

    /// <summary>Backgrounds and scenarios belonging to the rule.</summary>
    [JsonPropertyName("children")] public CucumberFeatureChild[]? Children { get; init; }
}

/// <summary>A scenario, scenario outline or background node.</summary>
public sealed record CucumberScenarioNode
{
    /// <summary>Node id — the first entry of <see cref="CucumberPickle.AstNodeIds"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Own tags (feature/rule tags are <em>not</em> included).</summary>
    [JsonPropertyName("tags")] public CucumberTag[]? Tags { get; init; }

    /// <summary>The localised keyword (<c>Scenario</c>, <c>Scenario Outline</c>, <c>Background</c>).</summary>
    [JsonPropertyName("keyword")] public string? Keyword { get; init; }

    /// <summary>Scenario name.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Free text under the scenario line.</summary>
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>The authored steps (placeholders still unexpanded for an outline).</summary>
    [JsonPropertyName("steps")] public CucumberGherkinStep[]? Steps { get; init; }

    /// <summary><c>Examples:</c> blocks of a scenario outline.</summary>
    [JsonPropertyName("examples")] public CucumberExamples[]? Examples { get; init; }
}

/// <summary>An <c>Examples:</c> block of a scenario outline.</summary>
public sealed record CucumberExamples
{
    /// <summary>Node id.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Examples-level tags.</summary>
    [JsonPropertyName("tags")] public CucumberTag[]? Tags { get; init; }

    /// <summary>Optional name of the examples block.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>The header row — the placeholder names.</summary>
    [JsonPropertyName("tableHeader")] public CucumberTableRow? TableHeader { get; init; }

    /// <summary>The data rows — one pickle per row.</summary>
    [JsonPropertyName("tableBody")] public CucumberTableRow[]? TableBody { get; init; }
}

/// <summary>A row of a Gherkin table (data table or examples table).</summary>
public sealed record CucumberTableRow
{
    /// <summary>Row id — the second entry of <see cref="CucumberPickle.AstNodeIds"/> for an outline row.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>The cells, left to right.</summary>
    [JsonPropertyName("cells")] public CucumberTableCell[]? Cells { get; init; }
}

/// <summary>A cell of a Gherkin table.</summary>
public sealed record CucumberTableCell
{
    /// <summary>Cell text.</summary>
    [JsonPropertyName("value")] public string? Value { get; init; }
}

/// <summary>An authored step in the Gherkin document (before placeholder expansion).</summary>
public sealed record CucumberGherkinStep
{
    /// <summary>Step id — the first entry of <see cref="CucumberPickleStep.AstNodeIds"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Where the step sits in the feature file.</summary>
    [JsonPropertyName("location")] public CucumberLocation? Location { get; init; }

    /// <summary>The literal keyword <em>with its trailing space</em>: <c>"Given "</c>, <c>"And "</c>.</summary>
    [JsonPropertyName("keyword")] public string? Keyword { get; init; }

    /// <summary><c>Context</c>, <c>Action</c>, <c>Outcome</c>, <c>Conjunction</c> or <c>Unknown</c>.</summary>
    [JsonPropertyName("keywordType")] public string? KeywordType { get; init; }

    /// <summary>Step text with <c>&lt;placeholders&gt;</c> still in place for an outline.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>The step's data table, when it has one.</summary>
    [JsonPropertyName("dataTable")] public CucumberDataTable? DataTable { get; init; }

    /// <summary>The step's doc string, when it has one.</summary>
    [JsonPropertyName("docString")] public CucumberDocString? DocString { get; init; }
}

/// <summary>A step data table.</summary>
public sealed record CucumberDataTable
{
    /// <summary>All rows — the first is treated as the header.</summary>
    [JsonPropertyName("rows")] public CucumberTableRow[]? Rows { get; init; }
}

/// <summary>A step doc string (<c>"""</c> block).</summary>
public sealed record CucumberDocString
{
    /// <summary>The content between the delimiters.</summary>
    [JsonPropertyName("content")] public string? Content { get; init; }

    /// <summary>The optional media type written after the opening delimiter (<c>json</c>, …).</summary>
    [JsonPropertyName("mediaType")] public string? MediaType { get; init; }
}

/// <summary>The <c>pickle</c> envelope: one compiled, runnable scenario (one row of an outline).</summary>
public sealed record CucumberPickle
{
    /// <summary>Pickle id — referenced by <see cref="CucumberTestCase.PickleId"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Feature file path.</summary>
    [JsonPropertyName("uri")] public string? Uri { get; init; }

    /// <summary>Scenario name. For an outline row this is the outline name, <em>unexpanded</em>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>
    /// The Gherkin nodes this pickle came from: <c>[scenarioId]</c>, or <c>[outlineId, exampleRowId]</c>
    /// for a scenario outline row.
    /// </summary>
    [JsonPropertyName("astNodeIds")] public string[]? AstNodeIds { get; init; }

    /// <summary>The expanded steps (background steps first).</summary>
    [JsonPropertyName("steps")] public CucumberPickleStep[]? Steps { get; init; }

    /// <summary>All tags in scope: feature + rule + scenario + examples.</summary>
    [JsonPropertyName("tags")] public CucumberPickleTag[]? Tags { get; init; }
}

/// <summary>A tag on a pickle, pointing back at the Gherkin node that declared it.</summary>
public sealed record CucumberPickleTag
{
    /// <summary>The tag text including the leading <c>@</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Id of the <see cref="CucumberTag"/> that declared it.</summary>
    [JsonPropertyName("astNodeId")] public string? AstNodeId { get; init; }
}

/// <summary>A compiled step of a pickle — placeholders expanded, arguments attached.</summary>
public sealed record CucumberPickleStep
{
    /// <summary>Pickle step id — referenced by <see cref="CucumberTestStep.PickleStepId"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>The expanded step text (no keyword).</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>
    /// The <em>resolved</em> phase: <c>Context</c>, <c>Action</c>, <c>Outcome</c> or <c>Unknown</c>.
    /// An <c>And</c>/<c>But</c> step carries the phase of the step it continues, not <c>Conjunction</c>.
    /// </summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>The Gherkin step (and, for an outline row, the example row) this step came from.</summary>
    [JsonPropertyName("astNodeIds")] public string[]? AstNodeIds { get; init; }
}

/// <summary>The <c>hook</c> envelope: a declared before/after hook.</summary>
public sealed record CucumberHook
{
    /// <summary>Hook id — referenced by <see cref="CucumberTestStep.HookId"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary><c>BEFORE_TEST_CASE</c>, <c>AFTER_TEST_CASE</c>, <c>BEFORE_TEST_RUN</c>, <c>AFTER_TEST_RUN</c>, …</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>Hook display name, e.g. <c>BeforeEach hook</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
}

/// <summary>The <c>testCase</c> envelope: the plan for running one pickle.</summary>
public sealed record CucumberTestCase
{
    /// <summary>Test case id — referenced by <see cref="CucumberTestCaseStarted.TestCaseId"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>The pickle this test case runs.</summary>
    [JsonPropertyName("pickleId")] public string? PickleId { get; init; }

    /// <summary>Hook steps and pickle steps, in execution order.</summary>
    [JsonPropertyName("testSteps")] public CucumberTestStep[]? TestSteps { get; init; }
}

/// <summary>One planned step of a test case — either a pickle step or a hook.</summary>
public sealed record CucumberTestStep
{
    /// <summary>Test step id — referenced by <see cref="CucumberTestStepStarted.TestStepId"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Set when this is a Gherkin step.</summary>
    [JsonPropertyName("pickleStepId")] public string? PickleStepId { get; init; }

    /// <summary>Set when this is a hook.</summary>
    [JsonPropertyName("hookId")] public string? HookId { get; init; }
}

/// <summary>The <c>testRunStarted</c> envelope.</summary>
public sealed record CucumberTestRunStarted
{
    /// <summary>Run id.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>When the run started.</summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}

/// <summary>The <c>testRunFinished</c> envelope.</summary>
public sealed record CucumberTestRunFinished
{
    /// <summary>Whether every test case passed.</summary>
    [JsonPropertyName("success")] public bool Success { get; init; }

    /// <summary>When the run finished.</summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}

/// <summary>The <c>testCaseStarted</c> envelope: one <em>attempt</em> at a test case.</summary>
public sealed record CucumberTestCaseStarted
{
    /// <summary>Attempt id — what every step and attachment of this attempt refers back to.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>0 for the first run, 1 for the first retry, …</summary>
    [JsonPropertyName("attempt")] public int Attempt { get; init; }

    /// <summary>The test case being attempted.</summary>
    [JsonPropertyName("testCaseId")] public string? TestCaseId { get; init; }

    /// <summary>When the attempt started.</summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}

/// <summary>The <c>testCaseFinished</c> envelope.</summary>
public sealed record CucumberTestCaseFinished
{
    /// <summary>The attempt that finished.</summary>
    [JsonPropertyName("testCaseStartedId")] public string? TestCaseStartedId { get; init; }

    /// <summary>True when the runner will retry — i.e. this attempt is not the verdict.</summary>
    [JsonPropertyName("willBeRetried")] public bool WillBeRetried { get; init; }

    /// <summary>
    /// When the attempt finished. Not every producer emits a trustworthy value here (playwright-bdd 9.2
    /// stamps it before the last step finishes), so the synthesiser takes the later of this and the last
    /// <see cref="CucumberTestStepFinished"/>.
    /// </summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}

/// <summary>The <c>testStepStarted</c> envelope — the instant a step's delimiter bar is drawn at.</summary>
public sealed record CucumberTestStepStarted
{
    /// <summary>The attempt this step belongs to.</summary>
    [JsonPropertyName("testCaseStartedId")] public string? TestCaseStartedId { get; init; }

    /// <summary>The planned step that started.</summary>
    [JsonPropertyName("testStepId")] public string? TestStepId { get; init; }

    /// <summary>When it started.</summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}

/// <summary>The <c>testStepFinished</c> envelope.</summary>
public sealed record CucumberTestStepFinished
{
    /// <summary>The attempt this step belongs to.</summary>
    [JsonPropertyName("testCaseStartedId")] public string? TestCaseStartedId { get; init; }

    /// <summary>The planned step that finished.</summary>
    [JsonPropertyName("testStepId")] public string? TestStepId { get; init; }

    /// <summary>Status, duration and (on failure) the exception.</summary>
    [JsonPropertyName("testStepResult")] public CucumberTestStepResult? TestStepResult { get; init; }

    /// <summary>When it finished.</summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}

/// <summary>The outcome of one step.</summary>
public sealed record CucumberTestStepResult
{
    /// <summary><c>PASSED</c>, <c>FAILED</c>, <c>SKIPPED</c>, <c>PENDING</c>, <c>UNDEFINED</c> or <c>AMBIGUOUS</c>.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>How long the step took.</summary>
    [JsonPropertyName("duration")] public CucumberTimestamp? Duration { get; init; }

    /// <summary>Free-text failure message (older producers put the whole stack here).</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    /// <summary>Structured failure detail (schema 24+).</summary>
    [JsonPropertyName("exception")] public CucumberException? Exception { get; init; }
}

/// <summary>Structured failure detail on a step result.</summary>
public sealed record CucumberException
{
    /// <summary>Exception type name.</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>Failure message.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    /// <summary>Stack trace.</summary>
    [JsonPropertyName("stackTrace")] public string? StackTrace { get; init; }
}

/// <summary>The <c>attachment</c> envelope: a file, image or text a step produced.</summary>
public sealed record CucumberAttachment
{
    /// <summary>The attempt the attachment belongs to.</summary>
    [JsonPropertyName("testCaseStartedId")] public string? TestCaseStartedId { get; init; }

    /// <summary>The step the attachment belongs to (may be a hook step).</summary>
    [JsonPropertyName("testStepId")] public string? TestStepId { get; init; }

    /// <summary>MIME type, e.g. <c>image/png</c>, <c>text/plain</c>.</summary>
    [JsonPropertyName("mediaType")] public string? MediaType { get; init; }

    /// <summary>Display name — for playwright-bdd, the name passed to <c>testInfo.attach()</c>.</summary>
    [JsonPropertyName("fileName")] public string? FileName { get; init; }

    /// <summary>Inline content: raw text when <see cref="ContentEncoding"/> is <c>IDENTITY</c>, base64 when <c>BASE64</c>.</summary>
    [JsonPropertyName("body")] public string? Body { get; init; }

    /// <summary><c>IDENTITY</c> or <c>BASE64</c>.</summary>
    [JsonPropertyName("contentEncoding")] public string? ContentEncoding { get; init; }

    /// <summary>When the producer kept the file on disk instead of inlining it, its location.</summary>
    [JsonPropertyName("url")] public string? Url { get; init; }

    /// <summary>When it was attached.</summary>
    [JsonPropertyName("timestamp")] public CucumberTimestamp? Timestamp { get; init; }
}
