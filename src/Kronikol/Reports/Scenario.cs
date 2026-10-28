namespace Kronikol.Reports;

/// <summary>
/// Represents a single test scenario in the report, including execution result,
/// steps, tags, and parameterized example values.
/// </summary>
public record Scenario
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>
    /// The scenario's own free-text description — the prose a Gherkin author writes under
    /// <c>Scenario:</c>, before the first step. Rendered above the step list in the living documentation.
    /// </summary>
    public string? Description { get; set; }

    public bool IsHappyPath { get; set; }
    public ExecutionResult Result { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    /// The framework's own <em>classification</em> of the failure — xUnit v3's <c>FailureCause</c>
    /// (<c>Assertion</c>, <c>Exception</c>, <c>Timeout</c>, <c>Other</c>) — and null on the frameworks
    /// that do not classify, which is most of them.
    ///
    /// <para>It is a category, never a cause: it says an assertion failed, not which one, so a run of
    /// four unrelated assertion failures reports the same value four times. Until 3.1.0 the xUnit v3
    /// adapter spliced it onto the front of <see cref="ErrorMessage"/>, which made it the first line of
    /// every message and therefore the key the failures digest clustered on — one cluster per run, and a
    /// digest that worked through one failure in fifteen while claiming the rest were the same one.
    /// Nothing may group on this field.</para>
    /// </summary>
    public string? FailureCause { get; set; }
    public TimeSpan? Duration { get; set; }
    public ScenarioStep[]? Steps { get; set; }
    public ScenarioStep[]? BackgroundSteps { get; set; }
    public FileAttachment[]? Attachments { get; set; }

    /// <summary>
    /// The scenario's own tags: those written on it and, in Gherkin, on its rule and its Examples block, each once in
    /// the first spelling met (<see cref="ScenarioTags.Comparer"/>). The lanes leave out a tag its feature carries
    /// (<see cref="Feature.Tags"/>), except the tests NDJSON lane, which lifts the tags every scenario of a feature shares
    /// to the feature and leaves them here too. Kronikol's own markers, the endpoint and the happy path, are not tags.
    /// Null when there are none. The report filters and searches on the effective tags: its feature's, then these.
    /// </summary>
    public string[]? Tags { get; set; }
    public string? Rule { get; set; }
    public string? OutlineId { get; set; }
    public Dictionary<string, string>? ExampleValues { get; set; }
    public Dictionary<string, object?>? ExampleRawValues { get; set; }
    public Dictionary<string, string>? ExampleFlatValues { get; set; }
    public string? ExampleDisplayName { get; set; }

    /// <summary>Name of the <c>Examples:</c> block this outline row came from (e.g. "the merchant gained share").</summary>
    public string? ExamplesBlockName { get; set; }

    /// <summary>Free-text description under the <c>Examples:</c> header, when the author wrote one.</summary>
    public string? ExamplesBlockDescription { get; set; }

    /// <summary>0-based position of the <c>Examples:</c> block within the outline; orders and separates blocks (needed when blocks are unnamed).</summary>
    public int? ExamplesBlockIndex { get; set; }

    /// <summary>
    /// Which run of this scenario produced the result, when the runner retries: 1 for the first, 2 for
    /// the first retry. Null where the runner reports nothing about attempts: every in-process lane today;
    /// the ingest lanes set it from Cucumber Messages and from a tests NDJSON's <c>attempt</c> or its
    /// repeated <c>start</c> records. <b>1-based deliberately</b>: the report's retry badge reads <c>retry 1</c>
    /// for a second attempt, as the <c>retry N</c> label 4.x's ingest lanes wrote beside it did. Cucumber's own wire
    /// value is 0-based.
    /// </summary>
    public int? Attempt { get; set; }

    /// <summary>
    /// When the scenario finished, when the framework recorded it. A dependency call that inherited this
    /// scenario's context after this moment is background, not the scenario's: a web host started inside
    /// a test carries the test into every hosted service it runs for as long as it lives.
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// True when <see cref="Result"/> was not reported by the runner but defaulted — a scenario that
    /// started and never ended, given <c>IngestRequest.ResultWhenUnknown</c>. The report's
    /// <c>ResultDefaulted</c> diagnostic says how many; this says which. The cross-run ledger records such
    /// a scenario as unknown rather than as the default, because a crashed worker's scenarios written as
    /// passes would poison the trend permanently.
    /// </summary>
    public bool ResultDefaulted { get; set; }

    /// <summary>
    /// True for a scenario that is not a test: the one an ingest folds the traffic of unknown tests into
    /// (<c>IngestRequest.FoldUnknownTestsInto</c>), and the stack's start-up an ingest keeps
    /// (<c>IngestRequest.SetupWindow</c>). Nothing ran and nothing ended; it holds traffic, and comes and goes
    /// with it. The report draws it like any scenario, with a "not a test" badge, and counts it as no test:
    /// the run band, the ring, the features table, the timeline, the duration percentiles and filter, the CI
    /// summary, <c>Failures.md</c>, the console summary, <c>Run.json</c>, the CTRF report,
    /// <see cref="ReportConfigurationOptions.ExpectedTestCount"/> and <c>kronikol query</c> (its counts, its
    /// result and time filters, and the run diff) leave it out of their counts and times, and the
    /// specifications leave it out altogether. Its result is no verdict of the run's. The data files write it
    /// as <c>notATest</c> (only where it is true), so a merge and the query keep it. The cross-run ledger
    /// records it as <c>N</c>, so it reads no verdict, is never reported new or absent, makes no run partial,
    /// and is in no run's count, time or speed.
    /// </summary>
    public bool NotATest { get; set; }

    /// <summary>
    /// True for a scenario whose name a person wrote and the report shows as written: a Gherkin scenario (the Reqnroll
    /// lanes and the Cucumber ingest), and since 5.16.5 a DisplayName, a TestName or an MSTest DisplayName
    /// (plans/TITLES_AS_WRITTEN_PLAN.md 4.2). Its outline's grouped heading is its title as written, not the method-name
    /// rule's reading (#102), and a name ending in brackets is never read as a parameterised test's (5.9.1,
    /// plans/REPORT_UI_FIXES_PLAN.md N4, N5, E8). The data files write it as <c>nameAsWritten</c> (only where it is
    /// true), so a merge keeps it.
    /// </summary>
    internal bool NameAsWritten { get; set; }

    /// <summary>
    /// True for an outline row whose name Kronikol filled from its <c>Examples:</c> values, so that its
    /// <see cref="DisplayName"/> is no longer the title it was keyed by: a Reqnroll row since #101, which 5.4.2 and
    /// earlier named by the outline's title. Its stable id, its place in a feature and its in-page anchor key on
    /// <see cref="OutlineId"/> as they did then (<c>ScenarioStableId.KeyName</c>). The data files write it as
    /// <c>keyedByOutline</c> (only where it is true), so a merge computes the same ids.
    /// </summary>
    internal bool KeyedByOutline { get; set; }

    /// <summary>
    /// What the scenario's stable id is computed from, where its lane shows it otherwise than 5.16.4 did: the name 5.16.4
    /// showed (a title the rule now reads better, a name the test wrote now shown as written) and the outline and example
    /// values the lane read from it then, so the scenario keeps its id, its history and its anchor
    /// (plans/TITLES_AS_WRITTEN_PLAN.md 4.4). Null where the id keys on the scenario as it is. The data files write it
    /// as <c>stableIdName</c>, with <c>stableIdOutlineId</c> and <c>stableIdExampleValues</c> where those differ, so a
    /// merge computes the same ids.
    /// </summary>
    internal ScenarioIdKey? StableIdKey { get; set; }

    /// <summary>
    /// The title the scenario's lane gives the outline it is a row of, for a heading its rows' names do not share: a
    /// theory's DisplayName or its method's title by the rule, LightBDD's name format as it wrote it, an ingested
    /// outline as the input wrote it (plans/TITLES_AS_WRITTEN_PLAN.md 4.3). Null where the lane gives none. The data
    /// files write it as <c>outlineTitle</c> where it differs from the row's own name less its arguments, so a merge
    /// heads the group as the run did.
    /// </summary>
    internal string? OutlineTitle { get; set; }

    /// <summary>
    /// Where the scenario is written - a project-relative path with forward slashes, matching
    /// <see cref="Feature.SourceFile"/>. Null on the lanes that cannot supply one. Deliberately NOT the
    /// bare-file-name contract of <see cref="ScenarioStep.SourceFile"/>: a step's path comes from
    /// <c>[CallerFilePath]</c> on the build machine and is reduced to a name, a scenario's comes from the
    /// Gherkin document and is already relative, or from a tests NDJSON's <c>sourceFile</c>, made relative
    /// to <c>IngestRequest.SourceRoot</c>.
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// The line the scenario is declared on - the <c>Scenario:</c> or <c>Scenario Outline:</c> keyword,
    /// not the <c>Examples:</c> row, so every row of an outline points at the same declaration.
    /// <see cref="ExampleValues"/> is what says which row.
    /// </summary>
    public int? SourceLine { get; set; }
}