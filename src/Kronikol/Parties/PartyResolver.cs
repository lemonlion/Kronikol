using Kronikol.Constants;
using Kronikol.Reports;

namespace Kronikol.Parties;

/// <summary>One call as the resolver reads it: its two parties, its categories and how it got its scenario.</summary>
internal readonly record struct PartyCall(string Caller, string Service, string? Category = null, string? CallerCategory = null, string? AttributionSource = null);

/// <summary>
/// What the run declares (<see cref="ReportConfigurationOptions.ServiceId"/>, <see cref="ReportConfigurationOptions.Parties"/>)
/// and what its parties file does, with what reading the file found wrong with it.
/// </summary>
internal sealed record PartyDeclarations(string? ServiceId, IReadOnlyList<PartyDescriptor> Options, IReadOnlyList<PartyDescriptor> File, IReadOnlyList<DiagnosticEntry> FileFindings)
{
    internal static PartyDeclarations None { get; } = new(null, [], [], []);

    /// <summary>
    /// The run declared something: a service id, a party, or a parties file, even one that could not be used, since its
    /// findings must reach the report.
    /// </summary>
    internal bool Any => !string.IsNullOrWhiteSpace(ServiceId) || Options.Count > 0 || File.Count > 0 || FileFindings.Count > 0;
}

/// <summary>A test client Kronikol built: the name its calls carry as their caller, whom it called, and the application it ran.</summary>
internal sealed record TestClientObservation(string Caller, IReadOnlyCollection<string> Callees, string? Application);

/// <summary>What Kronikol saw of a run's parties (O1, the test clients <c>CreateTestTrackingClient</c> built).</summary>
internal sealed record PartyObservations(IReadOnlyList<TestClientObservation> TestClients)
{
    internal static PartyObservations None { get; } = new([]);
}

/// <summary>
/// A run's parties: one row a participant, sorted by name, and what the checks found. The findings are the report's
/// diagnostics only when the run declared something, so a report that declares nothing keeps its bytes; the
/// <c>parties</c> verb prints them as notes either way.
/// </summary>
internal sealed record PartyTable(IReadOnlyList<Party> Parties, string? ServiceId, bool Declared, IReadOnlyList<DiagnosticEntry> Findings)
{
    internal IReadOnlyList<DiagnosticEntry> Diagnostics => Declared ? Findings : [];

    internal IReadOnlyList<DiagnosticEntry> Notes => Findings;
}

/// <summary>
/// Who each party is (#190, plans/PARTY_IDENTITY_PLAN.md §5.1-5.4): a pure function of the run's calls, its
/// declarations and Kronikol's observations, so the writer, merge and the query engine resolve a run alike. Each field
/// of a row is resolved on its own: the run's options, then the parties file, then an observation, then the inference,
/// then <see cref="PartyKind.Unknown"/>.
/// </summary>
internal static class PartyResolver
{
    private const string TestContext = nameof(Kronikol.Tracking.AttributionSource.TestContext);
    private const string RequestHeader = nameof(Kronikol.Tracking.AttributionSource.RequestHeader);

    internal static PartyTable Resolve(IEnumerable<PartyCall> calls, PartyDeclarations declarations, PartyObservations observations)
    {
        var all = calls.Where(c => !string.IsNullOrEmpty(c.Caller) && !string.IsNullOrEmpty(c.Service)).ToList();
        var names = all.SelectMany(c => new[] { c.Caller, c.Service }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var findings = new List<DiagnosticEntry>(declarations.FileFindings);

        var fromOptions = Level(declarations.Options, "ReportConfigurationOptions.Parties", duplicatesConflict: false, findings);
        var fromFile = Level(declarations.File, "The parties file", duplicatesConflict: true, findings);
        Unmatched(names, fromOptions.Keys, fromFile.Keys, findings);

        var observedTests = new HashSet<string>(observations.TestClients.Select(o => o.Caller), StringComparer.Ordinal);
        var observedSubjects = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var client in observations.TestClients)
            foreach (var callee in client.Callees)
                if (!observedSubjects.TryGetValue(callee, out var application) || application is null)
                    observedSubjects[callee] = client.Application;

        PartyKind? Declared(string name) => (fromOptions.TryGetValue(name, out var o) ? o.Kind : null) ?? (fromFile.TryGetValue(name, out var f) ? f.Kind : null);

        var kinds = new Dictionary<string, (PartyKind Kind, PartyKindSource? Source)>(StringComparer.Ordinal);
        var inferredTests = InferTests(all);
        foreach (var name in names)
        {
            kinds[name] = Declared(name) is { } declared ? (declared, PartyKindSource.Declared)
                : observedTests.Contains(name) ? (PartyKind.Test, PartyKindSource.Observed)
                : observedSubjects.ContainsKey(name) ? (PartyKind.UnderTest, PartyKindSource.Observed)
                : inferredTests.Contains(name) ? (PartyKind.Test, PartyKindSource.Inferred)
                : (PartyKind.Unknown, null);
        }

        // The subject is inferred only when nothing declared or observed names one: a declared subject (the in-process
        // case, where the test is the subject) leaves the inference's guess out.
        if (!kinds.Values.Any(k => k.Kind == PartyKind.UnderTest))
        {
            var tests = names.Where(n => kinds[n].Kind == PartyKind.Test).ToHashSet(StringComparer.Ordinal);
            foreach (var subject in InferSubjects(all, tests))
                if (kinds[subject] is (PartyKind.Unknown, null))
                    kinds[subject] = (PartyKind.UnderTest, PartyKindSource.Inferred);
        }

        var serviceId = string.IsNullOrWhiteSpace(declarations.ServiceId) ? null : declarations.ServiceId;
        var received = all.GroupBy(c => c.Service, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var subjectRow = names.Where(n => kinds[n].Kind == PartyKind.UnderTest)
            .OrderByDescending(n => received.GetValueOrDefault(n)).ThenBy(n => n, StringComparer.Ordinal).FirstOrDefault();

        var rows = new List<Party>(names.Count);
        foreach (var name in names)
        {
            fromOptions.TryGetValue(name, out var option);
            fromFile.TryGetValue(name, out var file);
            var id = option?.Id ?? file?.Id;
            if (serviceId is not null && name == subjectRow)
            {
                if (id is not null && !string.Equals(id, serviceId, StringComparison.Ordinal))
                    findings.Add(new DiagnosticEntry(DiagnosticKind.PartyDeclarationConflict,
                        $"ServiceId is \"{serviceId}\" and the subject \"{name}\" is declared with the id \"{id}\"; ServiceId is used."));
                id = serviceId;
            }

            var note = option?.Note ?? file?.Note ?? (kinds[name].Source == PartyKindSource.Observed && observedSubjects.TryGetValue(name, out var application) ? application : null);
            // A party declared unknown keeps the inference off it, and like any unknown carries no source.
            rows.Add(new Party(name, id, kinds[name].Kind, kinds[name].Kind == PartyKind.Unknown ? null : kinds[name].Source, note));
        }

        var hostCalls = all.Where(c => string.Equals(c.AttributionSource, RequestHeader, StringComparison.Ordinal) && kinds.TryGetValue(c.Caller, out var k) && k.Kind == PartyKind.Test).ToList();
        if (hostCalls.Count > 0)
        {
            var named = string.Join(", ", hostCalls.Select(c => c.Caller).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(n => $"\"{n}\""));
            findings.Add(new DiagnosticEntry(DiagnosticKind.TestPartyNameInsideHost,
                $"{hostCalls.Count} calls a service made while serving a request the test sent carry the test's name, {named}, as their caller: "
                + "the service's trackers are left at the test's name, and one name cannot be two parties. Set each tracker's CallerName to the service's name."));
        }

        return new PartyTable(rows, serviceId, declarations.Any, findings);
    }

    /// <summary>One level's rows by name. A name listed twice is merged field by field, the later winning, and is a conflict when two of its values differ, or always in the file.</summary>
    private static Dictionary<string, PartyDescriptor> Level(IReadOnlyList<PartyDescriptor> rows, string level, bool duplicatesConflict, List<DiagnosticEntry> findings)
    {
        var merged = new Dictionary<string, PartyDescriptor>(StringComparer.Ordinal);
        var conflicts = new SortedSet<string>(StringComparer.Ordinal);
        var unnamed = 0;
        foreach (var row in rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Name))
            {
                unnamed++;
                continue;
            }

            if (merged.TryGetValue(row.Name, out var before))
            {
                if (duplicatesConflict || Differ(before.Id, row.Id) || Differ(before.Kind, row.Kind) || Differ(before.Note, row.Note))
                    conflicts.Add(row.Name);
                merged[row.Name] = before with { Id = row.Id ?? before.Id, Kind = row.Kind ?? before.Kind, Note = row.Note ?? before.Note };
            }
            else
            {
                merged[row.Name] = row;
            }
        }

        if (unnamed > 0)
            findings.Add(new DiagnosticEntry(DiagnosticKind.PartyDeclarationInvalid, $"{level} declares {unnamed} part{(unnamed == 1 ? "y" : "ies")} with no name, which match no call and were left out."));
        if (conflicts.Count > 0)
            findings.Add(new DiagnosticEntry(DiagnosticKind.PartyDeclarationConflict,
                $"{level} declares {string.Join(", ", conflicts.Select(c => $"\"{c}\""))} more than once{(duplicatesConflict ? "" : ", with different values")}; the later declaration of each field is used."));
        return merged;
    }

    private static bool Differ<T>(T? a, T? b) => a is not null && b is not null && !EqualityComparer<T>.Default.Equals(a, b);

    /// <summary>A declared name no call carries: a near miss (case or surrounding white space) is named and never matched; the options' others are listed once, the file's are silent.</summary>
    private static void Unmatched(List<string> names, IEnumerable<string> fromOptions, IEnumerable<string> fromFile, List<DiagnosticEntry> findings)
    {
        var recorded = new HashSet<string>(names, StringComparer.Ordinal);
        var notSeen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (declared, isOption) in fromOptions.Select(n => (n, true)).Concat(fromFile.Select(n => (n, false))))
        {
            if (recorded.Contains(declared))
                continue;
            var near = names.FirstOrDefault(n => string.Equals(n.Trim(), declared.Trim(), StringComparison.OrdinalIgnoreCase));
            if (near is not null)
                findings.Add(new DiagnosticEntry(DiagnosticKind.PartyNameNearMiss,
                    $"The party declared as \"{declared}\" matches no call; the calls name \"{near}\", which differs only in case or white space. Names match exactly, so the declaration was not applied."));
            else if (isOption)
                notSeen.Add(declared);
        }

        if (notSeen.Count > 0)
            findings.Add(new DiagnosticEntry(DiagnosticKind.DeclaredPartyNotSeen,
                $"ReportConfigurationOptions.Parties declares {notSeen.Count} part{(notSeen.Count == 1 ? "y" : "ies")} no call in this run carries: "
                + string.Join(", ", notSeen.Select(n => $"\"{n}\"")) + ". A run of only some of the tests can cause it; otherwise check the name against the participant's."));
    }

    /// <summary>
    /// The test parties: names never called whose calls carry no caller category but User (a broker's delivery carries
    /// MessageQueue). Where the calls record how they got their scenario, a test party's are mostly TestContext, so a
    /// scheduler's (Scope, Detached, None) are left out, and when none qualifies the one with the most TestContext calls
    /// is kept; where nothing records it, as in an ingest, every candidate qualifies.
    /// </summary>
    private static HashSet<string> InferTests(List<PartyCall> calls)
    {
        var called = new HashSet<string>(calls.Select(c => c.Service), StringComparer.Ordinal);
        var candidates = calls.GroupBy(c => c.Caller, StringComparer.Ordinal)
            .Where(g => !called.Contains(g.Key)
                && g.All(c => string.IsNullOrEmpty(c.CallerCategory) || string.Equals(c.CallerCategory, DependencyCategories.User, StringComparison.OrdinalIgnoreCase)))
            .Select(g => (Name: g.Key, TestContext: g.Count(c => c.AttributionSource == TestContext), Total: g.Count(), Attributed: g.Count(c => !string.IsNullOrEmpty(c.AttributionSource))))
            .OrderByDescending(c => c.TestContext).ThenByDescending(c => c.Total).ThenByDescending(c => c.Name, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);
        if (!candidates.Any(c => c.Attributed > 0))
            return candidates.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        var tests = candidates.Where(c => c.Attributed > 0 && c.TestContext * 2 > c.Attributed).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        return tests.Count > 0 ? tests : new HashSet<string>([candidates[0].Name], StringComparer.Ordinal);
    }

    /// <summary>
    /// The subjects among the test parties' callees, stores left out: one that makes calls of its own is preferred, then
    /// the one with the most calls; a second is kept only when it too makes calls and takes a quarter of the tests' calls.
    /// </summary>
    private static List<string> InferSubjects(List<PartyCall> calls, HashSet<string> tests)
    {
        var made = calls.Where(c => tests.Contains(c.Caller)).ToList();
        var callers = new HashSet<string>(calls.Select(c => c.Caller), StringComparer.Ordinal);
        var eligible = made.GroupBy(c => c.Service, StringComparer.Ordinal)
            .Where(g => !g.Any(c => IsStore(c.Category)))
            .Select(g => (Name: g.Key, MakesCalls: callers.Contains(g.Key), Count: g.Count()))
            .OrderByDescending(e => e.MakesCalls).ThenByDescending(e => e.Count).ThenByDescending(e => e.Name, StringComparer.Ordinal)
            .ToList();
        if (eligible.Count == 0)
            return [];

        var subjects = new List<string> { eligible[0].Name };
        subjects.AddRange(eligible.Skip(1).Where(e => e.MakesCalls && e.Count * 4 >= made.Count).Select(e => e.Name));
        return subjects;
    }

    /// <summary>A database, cache, queue, storage or AI category, as the diagram's palette resolves it, or a statement-shaped one.</summary>
    internal static bool IsStore(string? category) =>
        DependencyPalette.Resolve(category) is DependencyType.Database or DependencyType.Cache or DependencyType.MessageQueue or DependencyType.Storage or DependencyType.AI
        || DependencyCategories.IsStatementShaped(category);
}
