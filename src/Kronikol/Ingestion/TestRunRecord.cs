using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kronikol.Ingestion;

/// <summary>
/// One line of the companion <em>tests</em> NDJSON file that supplies scenario outcome, timing, structure
/// and artefacts to <c>kronikol ingest</c> / <see cref="IngestPipeline"/>. Five event kinds:
/// <list type="bullet">
/// <item><c>start</c> — the test began (<c>testId</c>, <c>testName</c>, optional <c>feature</c>,
/// <c>featureDescription</c>, <c>description</c>, <c>rule</c>, <c>tags</c>, <c>outlineId</c>,
/// <c>exampleValues</c>, <c>timestamp</c>).</item>
/// <item><c>step</c> — a named step inside the test (<c>text</c>, optional <c>keyword</c>,
/// <c>keywordType</c>, <c>status</c>, <c>durationMs</c>, <c>error</c>, <c>stackTrace</c>,
/// <c>bypassReason</c>, <c>level</c>, <c>background</c>, <c>docString</c>, <c>docStringMediaType</c>,
/// <c>table</c>).
/// Top-level steps (<c>level</c> 0 or absent) also draw a step delimiter bar in the sequence diagram at their
/// <c>timestamp</c>, exactly like Kronikol's step tracking; nested steps (<c>level</c> &gt; 0) appear as sub-steps in the step list.</item>
/// <item><c>assertion</c> — an assertion the test made (<c>text</c>, <c>status</c>: passed | failed, optional <c>error</c>).
/// Draws a green ✓ / red ✗ assertion note in the sequence diagram at its <c>timestamp</c>, exactly like Kronikol's
/// assertion tracking, and appears as a sub-step of the enclosing step.</item>
/// <item><c>attachment</c> — a file or link produced by the test (<c>name</c>, <c>path</c>, optional
/// <c>mediaType</c> and <c>step</c>): a screenshot, a trace archive, a link to another report. Without
/// <c>step</c> it belongs to the scenario; with it, to that 0-based top-level step.</item>
/// <item><c>end</c> — the verdict (<c>status</c>: passed | failed | skipped | timedOut | interrupted | bypassed; <c>durationMs</c>; optional <c>error</c>, <c>stackTrace</c>).</item>
/// </list>
/// <c>testId</c> must equal the <c>testId</c> stamped on the interaction records for attribution to work
/// (or use <see cref="IngestRequest.AttributeByTestWindow"/> to attribute by time instead).
/// </summary>
public sealed record TestRunRecord
{
    /// <summary><c>start</c>, <c>step</c>, <c>assertion</c>, <c>attachment</c> or <c>end</c>.</summary>
    [JsonPropertyName("event")] public required string Event { get; init; }

    /// <summary>The scenario id — the same value the interaction records carry as <c>testId</c>.</summary>
    [JsonPropertyName("testId")] public required string TestId { get; init; }

    /// <summary>Display name of the test.</summary>
    [JsonPropertyName("testName")] public string? TestName { get; init; }

    /// <summary>Grouping for the report (a spec file, a class, a suite). Scenarios with the same feature render under one heading.</summary>
    [JsonPropertyName("feature")] public string? Feature { get; init; }

    /// <summary>When the event happened (ISO-8601).</summary>
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Outcome for <c>end</c> (and optionally <c>step</c>) events.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>Duration in milliseconds for <c>end</c> (and optionally <c>step</c>) events.</summary>
    [JsonPropertyName("durationMs")] public double? DurationMs { get; init; }

    /// <summary>Failure message for a failed <c>end</c> event.</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>Failure stack trace for a failed <c>end</c> event (or a failed <c>step</c>, where it becomes a step comment).</summary>
    [JsonPropertyName("stackTrace")] public string? StackTrace { get; init; }

    /// <summary>Step text for <c>step</c> events.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>Optional Gherkin-style keyword for <c>step</c> events (<c>Given</c>, <c>When</c>, …). Rendered verbatim.</summary>
    [JsonPropertyName("keyword")] public string? Keyword { get; init; }

    /// <summary>
    /// The <em>meaning</em> of a step's keyword when the literal keyword cannot be trusted for it —
    /// <c>Context</c> (Given), <c>Action</c> (When), <c>Outcome</c> (Then), <c>Conjunction</c> (And/But)
    /// or <c>Unknown</c>. Cucumber's <c>PickleStepType</c> vocabulary. Used for phase assignment
    /// (<see cref="IngestRequest.PhaseFromSteps"/>); <c>Conjunction</c> inherits the previous step's
    /// meaning, exactly like <c>And</c>/<c>But</c> do.
    /// </summary>
    [JsonPropertyName("keywordType")] public string? KeywordType { get; init; }

    /// <summary>Steps only: nesting depth. 0 / absent = top level (draws a delimiter); &gt; 0 = sub-step of the preceding top-level step.</summary>
    [JsonPropertyName("level")] public int? Level { get; init; }

    /// <summary>
    /// Steps only: this step came from the feature's <c>Background</c>. Background steps are collected
    /// into <see cref="Kronikol.Reports.Scenario.BackgroundSteps"/> and never draw a delimiter bar. When
    /// any scenario supplies an explicit background, the heuristic
    /// <see cref="Kronikol.Reports.BackgroundStepsDetector"/> is not run.
    /// </summary>
    [JsonPropertyName("background")] public bool? Background { get; init; }

    /// <summary>Steps only: a Gherkin doc-string argument, rendered as a code block under the step.</summary>
    [JsonPropertyName("docString")] public string? DocString { get; init; }

    /// <summary>Steps only: the doc-string's content type (<c>json</c>, <c>xml</c>, …) for syntax highlighting.</summary>
    [JsonPropertyName("docStringMediaType")] public string? DocStringMediaType { get; init; }

    /// <summary>Steps only: a Gherkin data table — the first row is the header. Rendered as the step's <c>table</c> parameter.</summary>
    [JsonPropertyName("table")] public string[][]? Table { get; init; }

    /// <summary>Steps only: why the step was skipped, when <c>status</c> is <c>bypassed</c>.</summary>
    [JsonPropertyName("bypassReason")] public string? BypassReason { get; init; }

    /// <summary><c>start</c> only: the feature's free-text description (the prose under <c>Feature:</c>).</summary>
    [JsonPropertyName("featureDescription")] public string? FeatureDescription { get; init; }

    /// <summary><c>start</c> only: the scenario's own free-text description (the prose under <c>Scenario:</c>).</summary>
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary><c>start</c> only: the Gherkin <c>Rule</c> this scenario sits under; scenarios group by it in the report.</summary>
    [JsonPropertyName("rule")] public string? Rule { get; init; }

    /// <summary>
    /// <c>start</c> only: the scenario's tags, with or without a leading <c>@</c>. The ReqNRoll
    /// conventions apply: <c>@category:x</c> becomes a category, <c>@endpoint:x</c> the feature's
    /// endpoint, <c>@happy-path</c> (also <c>happy_path</c>, <c>happypath</c>) marks the happy path, and
    /// everything else is a label. Tags carried by every scenario of a feature also become the feature's labels.
    /// </summary>
    [JsonPropertyName("tags")] public string[]? Tags { get; init; }

    /// <summary><c>start</c> only: the name of the scenario outline this row came from — rows sharing it render as one parameterised group.</summary>
    [JsonPropertyName("outlineId")] public string? OutlineId { get; init; }

    /// <summary><c>start</c> only: this row's example values (the outline's <c>Examples</c> columns), used for the pivot table and the row's display name.</summary>
    [JsonPropertyName("exampleValues")] public Dictionary<string, string>? ExampleValues { get; init; }

    /// <summary><c>attachment</c> only: the display name (<c>screenshot-start.png</c>, <c>Grafana trace</c>).</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>
    /// <c>attachment</c> only: where the artefact is. An absolute path, a path relative to
    /// <see cref="IngestRequest.AttachmentsBase"/>, or a URL (<c>http://…</c>/<c>https://…</c>) — a URL is
    /// rendered as a link and never copied.
    /// </summary>
    [JsonPropertyName("path")] public string? Path { get; init; }

    /// <summary><c>attachment</c> only: the media type (<c>image/png</c>, <c>application/zip</c>). An <c>image/*</c> attachment renders inline with a lightbox; anything else renders as a link.</summary>
    [JsonPropertyName("mediaType")] public string? MediaType { get; init; }

    /// <summary><c>attachment</c> only: the 0-based index of the top-level step the artefact belongs to. Absent = the scenario itself.</summary>
    [JsonPropertyName("step")] public int? Step { get; init; }

    /// <summary>Event names (<see cref="Event"/>).</summary>
    public static class Events
    {
        /// <summary>The test began.</summary>
        public const string Start = "start";
        /// <summary>A step inside the test.</summary>
        public const string Step = "step";
        /// <summary>An assertion the test made.</summary>
        public const string Assertion = "assertion";
        /// <summary>A file or link the test produced.</summary>
        public const string Attachment = "attachment";
        /// <summary>The verdict.</summary>
        public const string End = "end";

        /// <summary>Every event kind the synthesiser understands.</summary>
        public static readonly IReadOnlyList<string> All = [Start, Step, Assertion, Attachment, End];
    }

    /// <summary>Whether <paramref name="eventName"/> is one of <see cref="Events.All"/> (case-insensitive). Unknown events are ignored by the synthesiser.</summary>
    public static bool IsKnownEvent(string? eventName) =>
        eventName is not null && Events.All.Contains(eventName.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <see cref="Event"/> is <paramref name="name"/> (case-insensitively).</summary>
    public bool Is(string name) => string.Equals(Event, name, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>true</c> for <c>step</c> and <c>assertion</c> events that carry a timestamp — the ones that draw something in the diagram. Background steps never do.</summary>
    [JsonIgnore] public bool IsDiagramMarker =>
        Timestamp is not null
        && (Is(Events.Assertion)
            || (Is(Events.Step) && (Level ?? 0) == 0 && Background != true));

    /// <summary>Serialises this record as one NDJSON line.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, InteractionRecord.JsonOptions);

    /// <summary>Parses one NDJSON line.</summary>
    public static TestRunRecord FromJson(string json) =>
        JsonSerializer.Deserialize<TestRunRecord>(json, InteractionRecord.JsonOptions)
        ?? throw new JsonException("The line did not contain a test-run object.");
}

/// <summary>
/// Reads <see cref="TestRunRecord"/> lines from NDJSON. Like
/// <see cref="NdjsonInteractionReader"/>, a malformed line throws unless the caller supplies a
/// <see cref="MalformedLine"/> collector, in which case it is skipped and recorded.
/// </summary>
public static class NdjsonTestRunReader
{
    /// <summary>Reads every record in <paramref name="path"/>, throwing on the first malformed line.</summary>
    public static List<TestRunRecord> ReadFile(string path) => ReadFile(path, malformed: null);

    /// <summary>
    /// Reads every record in <paramref name="path"/>. When <paramref name="malformed"/> is given,
    /// unparsable lines are skipped and recorded there instead of throwing.
    /// </summary>
    public static List<TestRunRecord> ReadFile(string path, ICollection<MalformedLine>? malformed)
    {
        // FileShare.ReadWrite: captures are tailed while a writer (a proxy tap, a fixture) still holds them open.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return Read(reader, path, malformed);
    }

    /// <summary>
    /// Reads every record from <paramref name="reader"/>. When <paramref name="malformed"/> is given,
    /// unparsable lines are skipped and recorded there instead of throwing.
    /// </summary>
    public static List<TestRunRecord> Read(TextReader reader, string? sourceName = null, ICollection<MalformedLine>? malformed = null) =>
        NdjsonLineReader.Read(reader, sourceName ?? "tests NDJSON", TestRunRecord.FromJson, malformed);
}
