using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// Everything <c>kronikol query</c> knows about a report without having read its payloads.
///
/// <para>A report is four layers and three of them are tiny: the narrative (features, scenarios, steps,
/// assertions), the topology (who called whom, in what order, with what status), the artifacts — and then
/// the payloads, which are the other 90-odd percent of the file. The index holds the first three in full
/// and, for the fourth, holds only a content hash, a length and a byte offset. Asking for a payload seeks
/// to that offset; nothing else ever pays for it.</para>
/// </summary>
internal sealed class ReportIndex
{
    public required string Path { get; init; }
    public long FileLength { get; init; }
    public string? KronikolVersion { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    /// <summary>
    /// Run identity, from the report's <c>ciMetadata</c> and <c>environment</c> blocks (3.1.0+).
    /// <see cref="CiProvider"/> is <c>None</c> on a report generated off CI and null on a file written
    /// by an older Kronikol - the two are different answers, and only the first is a fact about the run.
    /// This is what a downloaded artifact is identified by, and what a baseline comparison keys on.
    /// </summary>
    public string? CiProvider { get; set; }
    public string? CiBranch { get; set; }
    public string? CiCommitSha { get; set; }
    public string? CiBuildNumber { get; set; }
    public string? CiRunId { get; set; }

    /// <summary>Which try of <see cref="CiRunId"/> this run is - the only field that tells a re-run apart.</summary>
    public string? CiRunAttempt { get; set; }
    public string? CiRepository { get; set; }
    public string? CiPipelineUrl { get; set; }
    public string? EnvironmentOs { get; set; }
    public string? EnvironmentRuntime { get; set; }

    /// <summary>The run was on CI and said so - not merely "the file declares the block".</summary>
    public bool OnCi => CiProvider is not null and not "None";

    public List<ScenarioEntry> Scenarios { get; } = [];
    public List<DiagnosticEntry> Diagnostics { get; } = [];

    /// <summary>Every distinct body in the file, keyed by its <c>b:</c> address.</summary>
    public Dictionary<string, BodyEntry> Bodies { get; } = [];

    /// <summary>
    /// Whether this report was produced by a Kronikol new enough to carry step attribution and assertion
    /// failure detail. An older file answers the same questions through the slower path of parsing its
    /// diagrams, and every command says so rather than quietly returning less.
    /// </summary>
    public bool Enriched { get; set; }

    /// <summary>True when the file is the mergeable superset rather than the standard report.</summary>
    public bool Mergeable { get; set; }

    /// <summary>
    /// How many payload <see cref="FileStream"/>s have been opened over this report so far — every
    /// payload open goes through <see cref="PayloadReader.Open"/>, which counts here. The deterministic
    /// observable the perf tests assert on instead of wall-clock (plans/QUERY_PERF_PLAN.md §4.2): a bulk
    /// command reads its thousands of bodies over one shared handle, and a regression to
    /// open-per-body fails those tests rather than just showing up as a slow report. Instance state,
    /// not static, so parallel test runs cannot race it.
    /// </summary>
    public int PayloadOpens { get; set; }

    /// <summary>The mergeable format version, when the file declares one. An unknown value is fatal, not ignored.</summary>
    public int? MergeableFormatVersion { get; set; }

    /// <summary>
    /// The report's own shape version, from the root <c>formatVersion</c> key (3.1.0+). Null on every
    /// report written before it, which is a legitimate state; <see cref="ReportScanner.UnreadableVersion"/>
    /// when the key is there but is not an integer, which is not.
    /// </summary>
    public int? FormatVersion { get; set; }

    /// <summary>The suite every stableId in this report is scoped to. Null on a report written before 3.1.0.</summary>
    public string? Suite { get; set; }

    /// <summary>
    /// Whether the file carried a root <c>features</c> ARRAY — the one marker every Kronikol writer has
    /// always emitted, on every format version, which is why the identity gate keys on it rather than on
    /// <c>formatVersion</c> (absent on everything before 3.1.0, and legitimately so).
    /// </summary>
    /// <remarks>
    /// It has to be the array and not merely the key, so that this agrees with
    /// <c>MergeableReportReader</c>, which tests presence on a parsed document, about exactly the
    /// malformed inputs the gate exists to catch. <see cref="Scenarios"/> cannot stand in for it: an empty
    /// list is also what a real run that discovered nothing produces.
    /// </remarks>
    public bool HasFeatures { get; set; }

    public string Directory => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".";

    public ScenarioEntry? Scenario(int ordinal) =>
        ordinal >= 0 && ordinal < Scenarios.Count ? Scenarios[ordinal] : null;
}

internal sealed class ScenarioEntry
{
    public int Ordinal { get; init; }
    public string FeatureName { get; set; } = "";
    public string[] FeatureLabels { get; set; } = [];

    /// <summary>
    /// Where the feature is written, when the lane supplied it - a project-relative path, the contract
    /// <see cref="Kronikol.Reports.Feature.SourceFile"/> makes. Carried on the scenario because that is
    /// the only shape the scanner produces, and because a scenario with no source of its own still
    /// belongs to a file somebody can open.
    /// </summary>
    public string? FeatureSourceFile { get; set; }
    public string Id { get; set; } = "";
    public string StableId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Result { get; set; } = "";
    public double DurationSeconds { get; set; }
    public bool IsHappyPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public string? Rule { get; set; }

    /// <summary>
    /// Where the scenario is written, when the runner knew: a project-relative path and the line of the
    /// <c>Scenario:</c> keyword. Named like <c>StepEntry</c>'s pair, but a different contract - a step's
    /// is a bare file name (see the schema).
    /// </summary>
    public string? SourceFile { get; set; }
    public int? SourceLine { get; set; }

    /// <summary>Which run of this scenario produced the result, 1-based; null when the runner said nothing.</summary>
    public int? Attempt { get; set; }
    public List<string> Labels { get; } = [];
    public List<string> Categories { get; } = [];
    public Dictionary<string, string> ExampleValues { get; } = [];
    public List<StepEntry> BackgroundSteps { get; } = [];
    public List<StepEntry> Steps { get; } = [];
    public List<InteractionEntry> Interactions { get; } = [];
    public List<AnnotationEntry> Annotations { get; } = [];
    public List<AttachmentEntry> Attachments { get; } = [];
    public List<Slice> Diagrams { get; } = [];

    public string Address => "s" + Ordinal;
    public bool Failed => Result.Equals("Failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Background steps then scenario steps, addressed the way <c>stepPath</c> addresses them.</summary>
    public IEnumerable<(string Path, StepEntry Step)> TopLevelSteps()
    {
        for (var i = 0; i < BackgroundSteps.Count; i++)
            yield return ("b" + i, BackgroundSteps[i]);
        for (var i = 0; i < Steps.Count; i++)
            yield return (i.ToString(), Steps[i]);
    }

    /// <summary>Every step and sub-step, depth first, each with its dotted address.</summary>
    public IEnumerable<(string Path, int Depth, StepEntry Step)> AllSteps()
    {
        foreach (var (path, step) in TopLevelSteps())
            foreach (var found in Walk(path, 0, step))
                yield return found;

        static IEnumerable<(string, int, StepEntry)> Walk(string path, int depth, StepEntry step)
        {
            yield return (path, depth, step);
            for (var i = 0; i < step.SubSteps.Count; i++)
                foreach (var found in Walk($"{path}.{i}", depth + 1, step.SubSteps[i]))
                    yield return found;
        }
    }
}

internal sealed class StepEntry
{
    public string? Keyword { get; set; }
    public string Text { get; set; } = "";
    public string? Status { get; set; }
    public double? DurationSeconds { get; set; }
    public string? FailureMessage { get; set; }
    public string? SourceFile { get; set; }
    public int? SourceLine { get; set; }
    public string? BypassReason { get; set; }
    public string? DocString { get; set; }
    public List<string> Comments { get; } = [];
    public List<StepEntry> SubSteps { get; } = [];
    public List<AttachmentEntry> Attachments { get; } = [];

    /// <summary>
    /// Parameters rendered flat: one line per inline value, one block per table. Held as text because the
    /// only consumer prints them, and the structured form costs several times the tokens.
    /// </summary>
    public List<string> Parameters { get; } = [];

    public bool Failed => Status is not null && Status.Equals("Failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>An assertion sub-step is one with no keyword — <c>Track.That</c> records them that way.</summary>
    public bool IsAssertion => Keyword is null;

    public string Display => Keyword is null ? Text : $"{Keyword} {Text}";
}

internal sealed class InteractionEntry
{
    public int Ordinal { get; init; }
    public string Type { get; set; } = "";
    public string? Method { get; set; }
    public string Uri { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string CallerName { get; set; } = "";
    public string? StatusCode { get; set; }

    /// <summary>
    /// The label beside the code (3.1.0+). On an older report this is null and <see cref="StatusCode"/>
    /// holds the name instead, which is why every consumer goes through
    /// <c>InteractionStatus.Read</c> rather than reading either field directly.
    /// </summary>
    public string? StatusText { get; set; }
    public string? Timestamp { get; set; }

    /// <summary>
    /// The exact pairing key: both halves of one call carry the same id. Null when the entry has none
    /// (markers, user actions, genuinely unpaired captures) — the empty Guid is normalized away at scan.
    /// </summary>
    public string? RequestResponseId { get; set; }
    public string? TraceId { get; set; }
    public double? DurationMs { get; set; }
    public string? StepPath { get; set; }
    public string? Phase { get; set; }
    public string? MetaType { get; set; }
    public string? DependencyCategory { get; set; }
    public string? ActivityTraceId { get; set; }
    public string? ActivitySpanId { get; set; }
    public string? CapturedBy { get; set; }
    public bool IsUserAction { get; set; }

    /// <summary>The <c>b:</c> address of this interaction's body, or null when it carried none.</summary>
    public string? BodyHash { get; set; }
    public int BodyLength { get; set; }
    public Slice Body { get; set; }
    public Slice Headers { get; set; }
    public int HeaderCount { get; set; }

    public string Address(ScenarioEntry scenario) => $"{scenario.Address}/i{Ordinal}";

    /// <summary>
    /// What this call is, in as few characters as carry the meaning: <c>GET /api/x</c>, or the status when
    /// it is the response half.
    /// </summary>
    public string Summary()
    {
        var target = ShortUri();
        return Method is { Length: > 0 } ? $"{Method} {target}" : target;
    }

    public string ShortUri()
    {
        if (!System.Uri.TryCreate(Uri, UriKind.Absolute, out var uri))
            return Uri;
        var tail = uri.PathAndQuery;
        return tail is "/" or "" ? uri.Host : tail;
    }
}

internal sealed class AnnotationEntry
{
    public int Index { get; set; }
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
}

internal sealed class AttachmentEntry
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string? MediaType { get; set; }

    /// <summary>
    /// Absolute path, resolved against the report's own directory. The HTML uses the relative form as an
    /// href; an agent needs the absolute one so it can open a screenshot without reconstructing anything.
    /// </summary>
    public string Resolve(string reportDirectory) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(reportDirectory, RelativePath));
}

internal sealed class DiagnosticEntry
{
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ScenarioId { get; set; }
}

/// <summary>Every address a given body occurs at, and how big it is. The hash is the identity.</summary>
internal sealed class BodyEntry
{
    public required string Hash { get; init; }
    public int Length { get; set; }
    public Slice First { get; set; }
    public List<string> Occurrences { get; } = [];
}

/// <summary>A byte range in the report file holding one JSON value, to be re-read on demand.</summary>
internal readonly record struct Slice(long Offset, int Length)
{
    public bool Exists => Length > 0;
}
