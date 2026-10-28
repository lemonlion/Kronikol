using Kronikol.History;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Kronikol.CallGraph;
using Kronikol.Constants;
using Kronikol.Parties;
using Kronikol.Query;

namespace Kronikol.Reports;

/// <summary>
/// A report data file's <c>service.json</c> (plans/SERVICE_CARD_PLAN.md 4.3 to 4.5): the scanned file mapped into
/// <see cref="CallRecord"/>s, built into a <see cref="Graph"/> and written by <see cref="ServiceCardWriter"/>. The card's
/// <c>behaviour</c> is a function of the data file alone (Q15): the parties are the file's block as written, else the
/// inference over its calls, and what the run observed and did not write never reaches it, so a card rebuilt from a kept
/// report is the card its run wrote.
/// </summary>
internal static class ServiceCard
{
    /// <summary>The card of a scanned report, as the file's text. <paramref name="runId"/> and <paramref name="runAt"/> are its run's, or null.</summary>
    internal static string Build(ReportIndex index, string? runId, string? runAt) =>
        ServiceCardWriter.Write(GraphOf(index), RunOf(index, runId, runAt));

    /// <summary>The call graph of a scanned report: every scenario's calls and the root background block's.</summary>
    internal static Graph GraphOf(ReportIndex index)
    {
        var scenarios = index.Scenarios.Select(s => new CallScenario
        {
            Ordinal = s.Ordinal,
            StableId = s.StableId,
            Feature = s.FeatureName,
            Name = s.Name,
            Tags = s.EffectiveTags,
            SourceFile = Trimmed(s.SourceFile),
            SourceLine = s.SourceLine,
            FeatureSourceFile = Trimmed(s.FeatureSourceFile),
            IsTest = !s.NotATest,
            Result = s.Result
        }).ToList();

        var records = new List<CallRecord>();
        Stream? bodies = null;
        try
        {
            string? ContentOf(InteractionEntry entry)
            {
                if (!IsRequest(entry) || !entry.Body.Exists || !ReadsContent(entry))
                    return null;
                bodies ??= PayloadReader.Open(index);
                return PayloadReader.Read(bodies, entry.Body);
            }

            for (var i = 0; i < index.Scenarios.Count; i++)
                foreach (var entry in index.Scenarios[i].Interactions)
                    records.Add(Map(entry, scenarios[i], ContentOf(entry)));
            foreach (var entry in index.BackgroundInteractions)
                records.Add(Map(entry, null, ContentOf(entry)));
        }
        finally
        {
            bodies?.Dispose();
        }

        var parties = index.HasParties
            ? new PartiesInput([.. index.Parties.Select(Row)], index.ServiceId)
            : PartiesInput.Inferred;
        // The rules the report recorded, read as route reads them; a report without the block says none from 5.19.0 and
        // cannot say before it.
        var rules = HistoryShapeRules.ForReading(index.ShapeTemplates);
        var templateRules = rules?.Hash ?? (QueryCommand.PredatesShapeTemplates(index.KronikolVersion) ? null : "none");
        return CallGraphBuilder.Build(scenarios, records, parties, rules, templateRules, callsRecorded: ReportScanner.CarriesInteractions(index));
    }

    /// <summary>
    /// The run's id and end, from a <c>Run.json</c> beside the report that lists it, by file name, and whose <c>at</c> is
    /// not before the report's <c>endTime</c>: both are the run's end to the second, so a run's own manifest passes and an
    /// earlier run's beside a later report, as after a merge into a run's folder, does not. Else nulls: no id is minted,
    /// since a local run's id is salted with what no report holds and a host's own id overrides any run's.
    /// </summary>
    internal static (string? Id, string? At) RunBeside(ReportIndex index)
    {
        if (RunManifest.TryRead(Path.Combine(index.Directory, RunManifest.FileName)) is not { } manifest
            || !manifest.Files.Contains(Path.GetFileName(index.Path), StringComparer.OrdinalIgnoreCase)
            || !DateTimeOffset.TryParse(index.EndTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end)
            || manifest.At < end)
            return (null, null);
        return (manifest.Run, Instant(manifest.At));
    }

    /// <summary>The run's facts the report holds, and the card writer's own version.</summary>
    private static ServiceCardRun RunOf(ReportIndex index, string? runId, string? runAt)
    {
        var tests = index.Scenarios.Where(s => !s.NotATest).ToList();
        int Count(params string[] results) => tests.Count(s => results.Any(r => s.Result.Equals(r, StringComparison.OrdinalIgnoreCase)));
        var passed = Count(nameof(ExecutionResult.Passed));
        var failed = Count(nameof(ExecutionResult.Failed));
        var skipped = Count(nameof(ExecutionResult.Skipped), nameof(ExecutionResult.SkippedAfterFailure));
        return new ServiceCardRun
        {
            Id = runId,
            At = runAt,
            Repository = index.OnCi ? index.CiRepository : null,
            Commit = index.OnCi ? index.CiCommitSha : null,
            Branch = index.OnCi ? index.CiBranch : null,
            Suite = Trimmed(index.Suite),
            KronikolVersion = Version,
            ReportFile = Path.GetFileName(index.Path),
            ReportFormatVersion = index.FormatVersion,
            ReportKronikolVersion = index.KronikolVersion,
            ReportSha256 = Sha256(index.Path),
            Os = index.EnvironmentOs,
            Runtime = index.EnvironmentRuntime,
            Scenarios = tests.Count,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            // a result no build writes reads as bypassed, as ctrf reads it, so the four sum to the scenarios
            Bypassed = tests.Count - passed - failed - skipped,
            Degraded = [.. index.Diagnostics.Where(d => ServiceCardWriter.DegradedKinds.Contains(d.Kind, StringComparer.Ordinal))
                .GroupBy(d => d.Kind, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => KeyValuePair.Create(g.Key, g.Count()))]
        };
    }

    /// <summary>The Kronikol that writes the card, without its build metadata.</summary>
    internal static string Version { get; } =
        (typeof(ServiceCard).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(ServiceCard).Assembly.GetName().Version?.ToString(3) ?? "").Split('+')[0];

    private static CallRecord Map(InteractionEntry entry, CallScenario? scenario, string? content)
    {
        var (code, text) = InteractionStatus.Read(entry.StatusCode, entry.StatusText);
        return new CallRecord
        {
            Scenario = scenario,
            Position = entry.Ordinal,
            IsRequest = IsRequest(entry),
            Caller = entry.CallerName,
            Service = entry.ServiceName,
            Method = entry.Method,
            Uri = entry.Uri,
            Category = entry.DependencyCategory,
            CallerCategory = entry.CallerDependencyCategory,
            IsEvent = string.Equals(entry.MetaType, nameof(Tracking.RequestResponseMetaType.Event), StringComparison.Ordinal),
            Phase = entry.Phase,
            AttributionSource = entry.AttributionSource,
            RequestResponseId = entry.RequestResponseId,
            TraceId = entry.TraceId,
            Status = code?.ToString(CultureInfo.InvariantCulture) ?? text,
            IsError = InteractionStatus.IsError(entry.StatusCode, entry.StatusText),
            IsUserAction = entry.IsUserAction,
            TestDouble = entry.TestDouble,
            Content = content
        };
    }

    private static bool IsRequest(InteractionEntry entry) => entry.Type.Equals("Request", StringComparison.OrdinalIgnoreCase);

    /// <summary>The bodies the operation key reads: a statement-shaped call's, for its statement head, and a POST's, a GraphQL candidate.</summary>
    private static bool ReadsContent(InteractionEntry entry) =>
        DependencyCategories.IsStatementShaped(entry.DependencyCategory) || string.Equals(entry.Method, "POST", StringComparison.OrdinalIgnoreCase);

    private static Party Row(ReportPartyEntry entry)
    {
        var kind = entry.Kind is { } written && PartiesFile.TryParseKind(written, out var parsed) ? parsed : PartyKind.Unknown;
        PartyKindSource? source = PartiesReport.TryParseSource(entry.KindSource, out var how) ? how : null;
        return new Party(entry.Name, entry.Id, kind, kind == PartyKind.Unknown ? null : source, entry.Note);
    }

    private static string? Sha256(string path)
    {
        try
        {
            using var stream = ReportScanner.OpenShared(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Instant(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? Trimmed(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
