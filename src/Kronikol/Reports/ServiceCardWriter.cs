using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kronikol.CallGraph;
using Kronikol.Parties;

namespace Kronikol.Reports;

/// <summary>The facts of one run a card's <c>run</c> part holds, beside what the graph gives (plans/SERVICE_CARD_PLAN.md 4.3).</summary>
internal sealed record ServiceCardRun
{
    /// <summary>The run's id and end, as its <c>Run.json</c> writes them; null when the run's manifest is not known.</summary>
    public string? Id { get; init; }
    public string? At { get; init; }

    /// <summary>From the report's <c>ciMetadata</c>; null off CI.</summary>
    public string? Repository { get; init; }
    public string? Commit { get; init; }
    public string? Branch { get; init; }

    public string? Suite { get; init; }

    /// <summary>The Kronikol that wrote the card, which for <c>kronikol card</c> may differ from the report's.</summary>
    public string KronikolVersion { get; init; } = "";

    public string? ReportFile { get; init; }
    public int? ReportFormatVersion { get; init; }
    public string? ReportKronikolVersion { get; init; }
    public string? ReportSha256 { get; init; }

    /// <summary>The report's <c>environment</c>; both null when it records none.</summary>
    public string? Os { get; init; }
    public string? Runtime { get; init; }

    /// <summary>The scenarios that are tests, by result: skipped counts <c>SkippedAfterFailure</c> too, and the four sum to <see cref="Scenarios"/>.</summary>
    public int Scenarios { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Bypassed { get; init; }

    /// <summary>Per kind, how many of the report's diagnostics say the capture lost or misplaced calls, by kind.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Degraded { get; init; } = [];

    internal static ServiceCardRun Empty { get; } = new();
}

/// <summary>
/// The one writer of <c>service.json</c> (plans/SERVICE_CARD_PLAN.md 4.3): a projection of a <see cref="Graph"/> in two
/// parts. <c>behaviour</c> holds sets only, in a total order, and is byte-identical whenever the behaviour is; <c>run</c>
/// holds what changes when the same code runs again.
///
/// <para><b>The hashed bytes.</b> <c>behaviourHash</c> is the SHA-256 of the <c>behaviour</c> member's bytes exactly as the
/// file holds them. The file is compact, UTF-8, LF, no BOM, one line and a final LF, written by <see cref="Utf8JsonWriter"/>
/// with its default encoder, which writes a <c>"</c> inside a string as <c>"</c>; <c>behaviour</c> is the last member,
/// so its bytes run from the <c>{</c> after the last <c>"behaviour":</c> to the byte before the file's closing <c>}</c>,
/// and a reader in any language checks the hash over that range without a JSON serializer. Re-serialising changes the
/// escapes and so the bytes.</para>
/// </summary>
internal static class ServiceCardWriter
{
    /// <summary>The file's fixed name, in a reports directory and wherever <c>kronikol card</c> is pointed.</summary>
    internal const string FileName = "service.json";

    /// <summary>The card's shape. Additive members do not move it; the schema is <c>schemas/service-card.v1.schema.json</c>.</summary>
    internal const int FormatVersion = 1;

    /// <summary>The seven diagnostic kinds that say a capture lost or misplaced calls, which <c>run.degraded</c> counts.</summary>
    internal static readonly string[] DegradedKinds =
    [
        nameof(DiagnosticKind.BackgroundCalls),
        nameof(DiagnosticKind.CaptureDegraded),
        nameof(DiagnosticKind.DroppedOutsideRunWindow),
        nameof(DiagnosticKind.DroppedUnattributed),
        nameof(DiagnosticKind.MalformedLine),
        nameof(DiagnosticKind.StepAttributionMismatch),
        nameof(DiagnosticKind.UnattributedInteractions)
    ];

    /// <summary>The file's text.</summary>
    internal static string Write(Graph graph, ServiceCardRun run)
    {
        var behaviour = Behaviour(graph);
        var hash = Convert.ToHexStringLower(SHA256.HashData(behaviour));

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WritePropertyName("run");
            Run(writer, graph, run, hash);
            writer.WritePropertyName("behaviour");
            writer.WriteRawValue(behaviour, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>The <c>behaviour</c> member's bytes, as the file holds them.</summary>
    private static byte[] Behaviour(Graph graph)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("rules");
            writer.WriteNumber("operationKey", graph.Rules.OperationKey);
            writer.WriteString("templateRules", graph.Rules.TemplateRules);
            if (graph.Rules.Inference is { } inference)
                writer.WriteNumber("inference", inference);
            else
                writer.WriteNull("inference");
            writer.WriteEndObject();

            if (graph.Subject is { } subject)
            {
                writer.WriteStartObject("subject");
                writer.WriteString("name", subject.Name);
                writer.WriteString("id", subject.Id);
                writer.WriteString("source", subject.KindSource is { } source ? PartiesReport.SourceName(source) : null);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("subject");
            }

            writer.WriteStartArray("parties");
            foreach (var party in graph.Parties)
            {
                writer.WriteStartObject();
                writer.WriteString("name", party.Name);
                writer.WriteString("id", party.Id);
                writer.WriteString("kind", PartiesReport.KindName(party.Kind));
                writer.WriteString("kindSource", party.KindSource is { } source ? PartiesReport.SourceName(source) : null);
                writer.WriteString("note", party.Note);
                Strings(writer, "categories", party.Categories);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("edges");
            foreach (var edge in graph.Edges)
            {
                writer.WriteStartObject();
                writer.WriteString("from", edge.From);
                writer.WriteString("to", edge.To);
                writer.WriteString("protocol", edge.Protocol);
                writer.WriteStartArray("operations");
                foreach (var operation in edge.Operations)
                {
                    writer.WriteStartObject();
                    writer.WriteString("op", operation.Op);
                    writer.WriteString("method", operation.Method);
                    writer.WriteString("target", operation.Target);
                    writer.WriteString("graphqlOperation", operation.GraphqlOperation);
                    Strings(writer, "statuses", operation.Statuses);
                    Strings(writer, "origins", operation.Origins);
                    Strings(writer, "causes", operation.Causes);
                    Strings(writer, "effectivePhases", operation.EffectivePhases);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("exposes");
            writer.WriteString("basis", "tested");
            Strings(writer, "ops", graph.Exposes);
            writer.WriteEndObject();

            writer.WriteStartArray("messages");
            foreach (var message in graph.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("op", message.Op);
                writer.WriteString("direction", message.Direction);
                writer.WriteString("party", message.Party);
                writer.WriteString("topic", message.Topic);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("scenarios");
            foreach (var row in graph.Scenarios)
            {
                var scenario = row.Scenario;
                writer.WriteStartObject();
                writer.WriteString("sid", scenario.StableId);
                writer.WriteString("feature", scenario.Feature);
                writer.WriteString("scenario", scenario.Name);
                // sorted like every other list: a tag moved from the feature to its scenarios changes no behaviour
                Strings(writer, "tags", scenario.Tags.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                if (!string.IsNullOrEmpty(scenario.SourceFile))
                {
                    writer.WriteStartObject("source");
                    writer.WriteString("file", scenario.SourceFile);
                    if (scenario.SourceLine is { } line)
                        writer.WriteNumber("line", line);
                    else
                        writer.WriteNull("line");
                    writer.WriteEndObject();
                }
                else if (!string.IsNullOrEmpty(scenario.FeatureSourceFile))
                {
                    writer.WriteStartObject("source");
                    writer.WriteString("file", scenario.FeatureSourceFile);
                    writer.WriteNull("line");
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNull("source");
                }
                Strings(writer, "ops", row.Ops);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            Strings(writer, "unresolved", graph.Parties.Where(p => p.Id is null).Select(p => p.Name).ToList());

            writer.WriteStartObject("capture");
            writer.WriteString("calls", graph.CallsRecorded ? "recorded" : "notRecorded");
            writer.WriteString("subjectCalls", graph.SubjectCallsRecorded ? "recorded" : "notRecorded");
            Strings(writer, "emptyTargets", graph.EmptyTargets);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void Run(Utf8JsonWriter writer, Graph graph, ServiceCardRun run, string behaviourHash)
    {
        writer.WriteStartObject();
        writer.WriteString("id", run.Id);
        writer.WriteString("at", run.At);
        writer.WriteString("repository", run.Repository);
        writer.WriteString("commit", run.Commit);
        writer.WriteString("branch", run.Branch);
        writer.WriteString("suite", run.Suite);
        // No capturer records its test framework yet (F13, Q12).
        writer.WriteNull("framework");
        writer.WriteString("kronikolVersion", run.KronikolVersion);

        writer.WriteStartObject("report");
        writer.WriteString("file", run.ReportFile);
        if (run.ReportFormatVersion is { } formatVersion)
            writer.WriteNumber("formatVersion", formatVersion);
        else
            writer.WriteNull("formatVersion");
        writer.WriteString("kronikolVersion", run.ReportKronikolVersion);
        writer.WriteString("sha256", run.ReportSha256);
        writer.WriteEndObject();

        if (run.Os is null && run.Runtime is null)
        {
            writer.WriteNull("environment");
        }
        else
        {
            writer.WriteStartObject("environment");
            writer.WriteString("os", run.Os);
            writer.WriteString("runtime", run.Runtime);
            writer.WriteEndObject();
        }

        writer.WriteStartObject("results");
        writer.WriteNumber("scenarios", run.Scenarios);
        writer.WriteNumber("passed", run.Passed);
        writer.WriteNumber("failed", run.Failed);
        writer.WriteNumber("skipped", run.Skipped);
        writer.WriteNumber("bypassed", run.Bypassed);
        writer.WriteEndObject();

        writer.WriteString("behaviourHash", behaviourHash);

        writer.WriteStartArray("parties");
        foreach (var party in graph.Parties)
        {
            writer.WriteStartObject();
            writer.WriteString("name", party.Name);
            Strings(writer, "hosts", party.Hosts);
            writer.WriteNumber("doubleCalls", party.DoubleCalls);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("counts");
        foreach (var operation in graph.Operations)
        {
            writer.WriteStartObject();
            writer.WriteString("op", operation.Op);
            writer.WriteNumber("calls", operation.Calls);
            writer.WriteNumber("errors", operation.Errors);
            writer.WriteStartObject("statuses");
            foreach (var (status, count) in operation.StatusCounts)
                writer.WriteNumber(status, count);
            writer.WriteEndObject();
            writer.WriteStartObject("phases");
            writer.WriteNumber("setup", operation.Setup);
            writer.WriteNumber("action", operation.Action);
            writer.WriteNumber("unknown", operation.Unknown);
            writer.WriteEndObject();
            writer.WriteNumber("derivedPhases", operation.DerivedPhases);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("examples");
        foreach (var operation in graph.Operations)
        {
            writer.WriteStartObject();
            writer.WriteString("op", operation.Op);
            writer.WriteString("address", operation.Example);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("scenarios");
        foreach (var row in graph.Scenarios)
        {
            writer.WriteStartObject();
            writer.WriteString("sid", row.Scenario.StableId);
            Strings(writer, "ops", row.AllOps);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartObject("degraded");
        foreach (var (kind, count) in run.Degraded)
            writer.WriteNumber(kind, count);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }
}
