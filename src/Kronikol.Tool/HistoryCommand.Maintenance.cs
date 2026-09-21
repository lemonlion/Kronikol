using System.Globalization;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// The 3.10.0 verbs of <c>kronikol history</c>: the history-aware gate, quarantine, rename aliases, the
/// doctor, and imports from reports the run did not write (plans/CROSS_RUN_HISTORY_PLAN.md §7.3-§7.6).
/// </summary>
internal static partial class HistoryCommand
{
    /// <summary>Everything the verbs read from the command line, parsed once.</summary>
    private sealed class Args
    {
        public string? Ledger;
        public string? Suite;
        public int? Window;
        public string? Reason;
        public string? By;
        public DateOnly? Until;
        public bool Release;
        public bool List;
        public string? FailOn;
        public int MaxNewFailures;
        public double? MinPassRate;
        public double? FlakyThreshold;
        public double? SlowerBy;
        public int? SlowerMinMs;
        public int? AlternatingRuns;
        public int? CountRuns;
        public double? DegradedBy;
        public string? Branch;
        public int? MinRuns;
        public bool FromCtrf;
        public bool FromAllure;
        public string? RunId;
        public bool AcceptRenames;
        public readonly List<string> Inputs = [];
    }

    private static readonly string[] GateCategories = ["new-failures", "flaky", "duration-regression", "behaviour-change"];

    // ─── gate ──────────────────────────────────────────────────

    /// <summary>
    /// The gate: exit 1 on what the ledger says is <i>new</i>, and a reading of everything else. A gate
    /// that fails on any red cannot tell a regression from the test that has flipped for a month, and a
    /// team that cannot tell them apart learns to ignore red. Below the minimum runs the statistical
    /// verdicts are advisory - printed, never tripped on - because the ledger does not have them yet.
    /// </summary>
    private static int Gate(Args args, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        if (args.Inputs.Count != 1)
        {
            error.WriteLine("gate takes one report: kronikol history gate <report|dir> [--fail-on new-failures,flaky,duration-regression,behaviour-change]");
            return 2;
        }

        var categories = (args.FailOn ?? "new-failures").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToLowerInvariant()).Distinct().ToArray();
        var unknown = categories.Where(c => !GateCategories.Contains(c, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
        {
            error.WriteLine($"--fail-on does not know {string.Join(", ", unknown)}. It takes a comma list of: {string.Join(", ", GateCategories)}.");
            return 2;
        }

        if (LoadReport(args.Inputs[0], error) is not { } index)
            return 2;

        var ledgerPath = Locate(args.Ledger, index.Directory, getEnv, error, mustExist: true);
        if (ledgerPath is null)
            return 2;

        var read = HistoryLedgerReader.Read(ledgerPath, args.Window ?? DefaultWindow);
        if (read.Ledger is not { } ledger)
        {
            error.WriteLine(read.Message);
            return 1;
        }

        if (!ReportHistory.TryBuild(index, args.Suite, error, out var roster, out var run, out var fromFragment, out var shapes))
            return 2;

        var quarantine = LoadQuarantineOrNull(ledgerPath);
        var aliases = LoadAliasesOrNull(ledgerPath);
        var defaults = new HistoryAnalysisOptions();
        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions
        {
            Window = args.Window ?? DefaultWindow,
            MinRuns = args.MinRuns ?? defaults.MinRuns,
            FlakyRate = args.FlakyThreshold ?? defaults.FlakyRate,
            SlowerBy = args.SlowerBy ?? defaults.SlowerBy,
            SlowerMinMs = args.SlowerMinMs ?? defaults.SlowerMinMs,
            AlternatingRuns = args.AlternatingRuns ?? defaults.AlternatingRuns,
            CountRuns = args.CountRuns ?? defaults.CountRuns,
            DegradedBy = args.DegradedBy ?? defaults.DegradedBy,
            // A pull request build reads against the branch it targets, as the run itself did.
            Branch = args.Branch ?? CiMetadataDetector.PullRequestTarget(getEnv)
        }, quarantine, aliases, shapes: shapes);

        var rows = index.Scenarios.Select(s => (Scenario: s, Entry: verdicts.At(s.Ordinal, s.StableId))).Where(p => p.Entry is not null).ToList();
        bool Free(ScenarioHistory e) => !e.Has(HistoryVerdictKind.Quarantined);
        var newFailures = rows.Where(r => r.Entry!.Current == HistoryFormat.Failed && Free(r.Entry) && !r.Entry.Has(HistoryVerdictKind.Flaky)
                                          && (r.Entry.Has(HistoryVerdictKind.Broke) || r.Entry.Has(HistoryVerdictKind.New) || r.Entry.Has(HistoryVerdictKind.Unknown))).ToList();
        var flaky = rows.Where(r => Free(r.Entry!) && r.Entry!.Has(HistoryVerdictKind.Flaky)).ToList();
        var slower = rows.Where(r => Free(r.Entry!) && r.Entry!.Has(HistoryVerdictKind.Slower)).ToList();
        var changed = rows.Where(r => Free(r.Entry!) && r.Entry!.Has(HistoryVerdictKind.BehaviourChanged)).ToList();
        var quarantined = rows.Where(r => !Free(r.Entry!)).ToList();
        var passed = index.Scenarios.Count(s => s.Result.Equals("Passed", StringComparison.OrdinalIgnoreCase));
        var failed = index.Scenarios.Count(s => s.Failed);
        var passRate = passed + failed == 0 ? 1.0 : (double)passed / (passed + failed);

        @out.WriteLine($"history: {HistorySummary.Line(verdicts)}");
        @out.WriteLine($"ledger: {ledgerPath} · stream {verdicts.Stream} · run {verdicts.RunId}" + (fromFragment ? "" : " · no History.run.json beside the report, behaviour verdicts off"));
        if (verdicts.ColdStart)
            @out.WriteLine($"advisory: {verdicts.ColdStartMessage}; flaky, duration-regression and behaviour-change do not trip the gate yet");
        // A build must not fail for the machine's bad day, and must be told that the gate looked.
        if (HistorySummary.Degraded(verdicts.Runs[^1]) is { } degraded)
            @out.WriteLine($"advisory: this run is {degraded}; no duration-regression is read in a degraded run (--degraded-by 0 switches that off)");

        void Section(string name, List<(ScenarioEntry Scenario, ScenarioHistory? Entry)> list)
        {
            @out.WriteLine($"{name}: {list.Count}");
            foreach (var (scenario, entry) in list.Take(50))
                @out.WriteLine($"  {scenario.Address}  {scenario.FeatureName} › {scenario.Name}  {HistoryVerdictNames.Name(entry!.Primary)} — {QueryWriter.OneLine(entry.Evidence, 160)}");
            if (list.Count > 50)
                @out.WriteLine($"  … and {list.Count - 50} more");
        }

        Section("new-failures", newFailures);
        Section("flaky", flaky);
        Section("duration-regression", slower);
        Section("behaviour-change", changed);
        Section("quarantined", quarantined);
        var otherFailures = rows.Where(r => r.Entry!.Current == HistoryFormat.Failed && !newFailures.Contains(r) && !flaky.Contains(r) && !quarantined.Contains(r)).ToList();
        if (otherFailures.Count > 0)
            Section("already-failing", otherFailures);
        @out.WriteLine($"pass rate {passRate.ToString("0.00", CultureInfo.InvariantCulture)}" + (args.MinPassRate is { } min ? $" (min {min.ToString("0.00", CultureInfo.InvariantCulture)})" : ""));

        var tripped = new List<string>();
        if (categories.Contains("new-failures") && newFailures.Count > args.MaxNewFailures)
            tripped.Add($"new-failures {newFailures.Count} > {args.MaxNewFailures}");
        if (args.MinPassRate is { } minimum && passRate < minimum)
            tripped.Add($"pass rate {passRate.ToString("0.00", CultureInfo.InvariantCulture)} < {minimum.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (!verdicts.ColdStart)
        {
            if (categories.Contains("flaky") && flaky.Count > 0) tripped.Add($"flaky {flaky.Count}");
            if (categories.Contains("duration-regression") && slower.Count > 0) tripped.Add($"duration-regression {slower.Count}");
            if (categories.Contains("behaviour-change") && changed.Count > 0) tripped.Add($"behaviour-change {changed.Count}");
        }

        if (tripped.Count == 0)
        {
            @out.WriteLine("gate: passed");
            return 0;
        }

        @out.WriteLine($"gate: FAILED ({string.Join("; ", tripped)})");
        return 1;
    }

    private static ReportIndex? LoadReport(string path, TextWriter error)
    {
        if (QueryCommand.ResolveReport(path, error) is not { } resolved)
            return null;
        ReportIndex index;
        try
        {
            index = ReportScanner.Scan(resolved);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error.WriteLine($"Could not read {resolved}: {exception.Message}");
            return null;
        }
        return ReportGate.Refuse(index, resolved, error) is null ? index : null;
    }

    private static HistoryQuarantineList? LoadQuarantineOrNull(string ledgerPath)
    {
        try { return HistoryQuarantineList.Load(HistoryQuarantineList.PathBeside(ledgerPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException) { return null; }
    }

    private static HistoryAliases? LoadAliasesOrNull(string ledgerPath)
    {
        try { return HistoryAliases.Load(HistoryAliases.PathBeside(ledgerPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException) { return null; }
    }

    // ─── quarantine ────────────────────────────────────────────

    private static int Quarantine(Args args, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        var ledgerPath = Locate(args.Ledger, null, getEnv, error, mustExist: false);
        if (ledgerPath is null)
            return 2;
        var path = HistoryQuarantineList.PathBeside(ledgerPath);

        HistoryQuarantineList list;
        try
        {
            list = HistoryQuarantineList.Load(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            error.WriteLine($"Could not read {path}: {exception.Message}");
            return 1;
        }

        if (args.List)
        {
            if (list.Entries.Count == 0)
            {
                @out.WriteLine("nothing is quarantined");
                return 0;
            }
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var entry in list.Entries)
                @out.WriteLine($"{entry.StableId}  {entry.Reason}" + (entry.AddedBy is { } by ? $"  by {by}" : "") + $"  since {entry.AddedOn:yyyy-MM-dd}"
                               + (entry.Until is { } until ? $"  until {until:yyyy-MM-dd}" + (entry.AppliesOn(today) ? "" : "  (expired)") : ""));
            return 0;
        }

        if (args.Inputs.Count != 1 || ParseStableId(args.Inputs[0]) is not { } id)
        {
            error.WriteLine("Which scenario? Pass its stableId (16 hex, with or without the sid: prefix) - kronikol query history <report> s3 prints it. Or --list.");
            return 2;
        }

        if (args.Release)
        {
            var removed = list.Release(id);
            list.Save(path);
            @out.WriteLine(removed ? $"released {id}" : $"{id} was not quarantined");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(args.Reason))
        {
            error.WriteLine("A quarantine needs --reason: the ticket, the sentence a reviewer will read in the diff.");
            return 2;
        }

        list.Add(id, args.Reason.Trim(), args.By, DateOnly.FromDateTime(DateTime.UtcNow), args.Until);
        list.Save(path);
        @out.WriteLine($"quarantined {id}: {args.Reason.Trim()}" + (args.Until is { } u ? $" until {u:yyyy-MM-dd}" : "") + $" → {path}");
        @out.WriteLine("It still runs and still records; it carries the quarantined verdict and does not trip the gate. Commit the file.");
        return 0;
    }

    private static string? ParseStableId(string text)
    {
        var id = text.StartsWith("sid:", StringComparison.OrdinalIgnoreCase) ? text[4..] : text;
        return id.Length == 16 && id.All(char.IsAsciiHexDigitLower) ? id : null;
    }

    // ─── rename ────────────────────────────────────────────────

    private static int Rename(Args args, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        if (args.Inputs.Count != 2 || ParseStableId(args.Inputs[0]) is not { } oldId || ParseStableId(args.Inputs[1]) is not { } newId)
        {
            error.WriteLine("rename takes two stableIds: kronikol history rename <old> <new>");
            return 2;
        }

        var ledgerPath = Locate(args.Ledger, null, getEnv, error, mustExist: false);
        if (ledgerPath is null)
            return 2;
        var path = HistoryAliases.PathBeside(ledgerPath);
        try
        {
            var aliases = HistoryAliases.Load(path);
            aliases.Add(oldId, newId);
            aliases.Save(path);
            @out.WriteLine($"aliased {oldId} → {newId} in {path}; history recorded under the old id now reads as the new one. Commit the file.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            error.WriteLine($"Could not update {path}: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Scenarios that look renamed between the previous full roster and a new one: an id present only
    /// on one side whose feature and name match an id present only on the other. Returned as
    /// (old, new) pairs; ambiguous names are left alone.
    /// </summary>
    internal static IReadOnlyList<(string OldId, string NewId, string Name)> SuggestRenames(HistoryRoster previous, HistoryRoster current)
    {
        var currentIds = new HashSet<string>(current.Ids, StringComparer.Ordinal);
        var previousIds = new HashSet<string>(previous.Ids, StringComparer.Ordinal);
        var gone = previous.Entries().Where(e => !currentIds.Contains(e.StableId)).GroupBy(e => (e.Feature, e.Name)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var arrived = current.Entries().Where(e => !previousIds.Contains(e.StableId)).GroupBy(e => (e.Feature, e.Name)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        return gone.Where(g => arrived.ContainsKey(g.Key))
            .Select(g => (g.Value.StableId, arrived[g.Key].StableId, $"{g.Key.Feature} › {g.Key.Name}"))
            .OrderBy(p => p.Item3, StringComparer.Ordinal)
            .ToArray();
    }

    // ─── doctor ────────────────────────────────────────────────

    private static int Doctor(Args args, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        var problems = 0;
        void Line(bool ok, string text)
        {
            if (!ok) problems++;
            @out.WriteLine((ok ? "ok       " : "problem  ") + text);
        }

        var env = getEnv(HistoryFormat.EnvironmentVariable);
        @out.WriteLine($"env      {HistoryFormat.EnvironmentVariable}={(string.IsNullOrEmpty(env) ? "(unset)" : env)}");

        // Asked about a reports directory, the kept runs are answered for whatever the ledger's state:
        // they do not depend on it, and KeepRuns works with history switched off.
        var askedAboutReports = args.Inputs.Any(Directory.Exists);
        var ledgerPath = Locate(args.Ledger, null, getEnv, askedAboutReports ? TextWriter.Null : error, mustExist: false);
        if (ledgerPath is null)
        {
            if (!askedAboutReports)
                return 2;
            @out.WriteLine("         no ledger to check: history is switched off here, or nothing names one");
            ReportsDirectories();
            @out.WriteLine(problems == 0 ? "doctor: healthy" : $"doctor: {problems} problem(s)");
            return problems == 0 ? 0 : 1;
        }
        if (!File.Exists(ledgerPath))
        {
            Line(false, $"ledger {ledgerPath} does not exist yet - run kronikol history init, or record a run");
            return 1;
        }

        var read = HistoryLedgerReader.Read(ledgerPath, 0);
        if (read.Ledger is not { } ledger)
        {
            Line(false, $"ledger {ledgerPath}: {read.Message}");
            return 1;
        }

        Line(true, $"ledger {ledgerPath} · format v{ledger.Version?.ToString(CultureInfo.InvariantCulture) ?? "?"} · written by Kronikol {ledger.Generator ?? "?"}");
        Line(ledger.Stats.DamagedLines == 0, ledger.Stats.DamagedLines == 0
            ? $"{ledger.Stats.LinesScanned} lines, none damaged"
            : $"{ledger.Stats.DamagedLines} damaged line(s) of {ledger.Stats.LinesScanned} - kronikol history verify names them, compact rewrites without them");
        var suites = ledger.Suites;
        var runs = suites.Sum(s => ledger.Runs(s).Count);
        Line(true, $"{suites.Count} suite{(suites.Count == 1 ? "" : "s")}, {runs} runs, {ledger.Rosters.Count} rosters");
        foreach (var suite in suites)
        {
            var list = ledger.Runs(suite);
            var partial = list.Count(r => r.Partial == true);
            var streams = list.Select(r => r.Stream).Distinct(StringComparer.Ordinal).ToArray();
            @out.WriteLine($"         {suite ?? "(no suite)"}: {list.Count} runs, streams {string.Join(", ", streams)}" + (partial > 0 ? $", {partial} partial" : ""));
        }
        if (suites.Contains(null))
            Line(false, "runs recorded with no suite: set ReportConfigurationOptions.SuiteName so ids stay distinguishable");

        var findings = HistoryLedgerReader.Verify(ledgerPath);
        Line(findings.Count == 0, findings.Count == 0 ? "structure verified" : $"{findings.Count} structural finding(s) - kronikol history verify");

        var directory = Path.GetDirectoryName(ledgerPath)!;
        var attributesDirectory = string.Equals(Path.GetFileName(directory), HistoryFormat.DirectoryName, StringComparison.Ordinal) ? Path.GetDirectoryName(directory) ?? directory : directory;
        var attributes = Path.Combine(attributesDirectory, ".gitattributes");
        var hasAttribute = File.Exists(attributes) && File.ReadAllText(attributes).Contains("history.jsonl", StringComparison.Ordinal);
        Line(hasAttribute, hasAttribute
            ? $"merge=union attribute present in {attributes}"
            : $"merge=union attribute missing from {attributes} - two branches that both appended will conflict; kronikol history init adds it");

        var quarantinePath = HistoryQuarantineList.PathBeside(ledgerPath);
        try
        {
            var quarantine = HistoryQuarantineList.Load(quarantinePath);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var expired = quarantine.Entries.Where(e => !e.AppliesOn(today)).ToList();
            Line(expired.Count == 0, quarantine.Entries.Count == 0
                ? "no quarantine list"
                : $"{quarantine.Entries.Count} quarantined" + (expired.Count == 0 ? "" : $", {expired.Count} expired - release them: " + string.Join(", ", expired.Select(e => e.StableId))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            Line(false, $"quarantine list {quarantinePath} could not be read: {exception.Message}");
        }

        var aliasPath = HistoryAliases.PathBeside(ledgerPath);
        try
        {
            var aliases = HistoryAliases.Load(aliasPath);
            Line(true, aliases.Mappings.Count == 0 ? "no rename aliases" : $"{aliases.Mappings.Count} rename alias(es)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            Line(false, $"alias file {aliasPath} could not be read: {exception.Message}");
        }

        // A reports directory, when one is named: the runs it keeps, and what a killed rotation left. The
        // rotation resumes on the next run (a staging folder holding Run.json is complete and is published;
        // one matching the newest run is carried on with), so what is named here is what neither rule
        // explains - and nothing prunes it or offers it to --run until a person has looked.
        ReportsDirectories();

        @out.WriteLine(problems == 0 ? "doctor: healthy" : $"doctor: {problems} problem(s)");
        return problems == 0 ? 0 : 1;

        void ReportsDirectories()
        {
            foreach (var reports in args.Inputs.Where(Directory.Exists))
            {
                var retained = Kronikol.Reports.ReportFolders.RetainedRuns(reports);
                var unfinished = Kronikol.Reports.ReportFolders.Unfinished(reports);
                Line(unfinished.Count == 0, $"{reports}: {retained.Count} run{(retained.Count == 1 ? "" : "s")} retained"
                    + (retained.FirstOrDefault(r => r.Manifest.Failed > 0) is { } failed ? $", the newest failing one {Path.GetFileName(failed.Directory)} ({failed.Manifest.Failed} failed)" : "")
                    + (unfinished.Count == 0 ? "" : $"; {unfinished.Count} left by an interrupted rotation, never pruned and never offered to --run: {string.Join(", ", unfinished.Select(Path.GetFileName))} - the next run resumes what it can, delete the rest"));
            }
        }
    }

    // ─── import ────────────────────────────────────────────────

    private static int Import(Args args, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        if (args.Inputs.Count == 0)
        {
            error.WriteLine("Nothing to import. Pass reports directories or files: kronikol history import <report|dir>... [--from-ctrf|--from-allure]");
            return 2;
        }
        if (args.FromCtrf && args.FromAllure)
        {
            error.WriteLine("--from-ctrf and --from-allure name two formats; pass one.");
            return 2;
        }

        var ledgerPath = Locate(args.Ledger, FirstInputDirectory(args.Inputs), getEnv, error, mustExist: false);
        if (ledgerPath is null)
            return 2;

        var runs = new List<(HistoryRoster Roster, HistoryRun Run, HistoryShapes? Shapes)>();
        var failed = 0;
        foreach (var input in args.Inputs)
        {
            try
            {
                if (args.FromCtrf) runs.AddRange(ImportCtrf(input, args, error).Select(p => (p.Item1, p.Item2, (HistoryShapes?)null)));
                else if (args.FromAllure) runs.AddRange(ImportAllure(input, args, error).Select(p => (p.Item1, p.Item2, (HistoryShapes?)null)));
                else runs.AddRange(ImportKronikol(input, args, error));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException)
            {
                error.WriteLine($"Skipped {input}: {exception.Message}");
                failed++;
            }
        }

        if (runs.Count == 0)
        {
            error.WriteLine(args.FromCtrf ? "No CTRF document found under the inputs (a *.json with reportFormat CTRF)."
                : args.FromAllure ? "No Allure results found under the inputs (*-result.json)."
                : "No TestRunReport.json found under the inputs.");
            return failed > 0 ? 1 : 2;
        }

        var imported = 0;
        var duplicates = 0;
        foreach (var (roster, run, shapes) in runs.OrderBy(r => r.Run.At))
        {
            var line = run.Partial is null ? run with { Partial = HistoryAnalyzer.IsPartial(roster, LastFullRoster(ledgerPath, run.Suite), 0.10) } : run;
            var result = HistoryLedgerWriter.Append(ledgerPath, roster, line, Commands.Version, shapes: shapes);
            var label = $"{run.Id}  {run.Suite ?? "(no suite)"}  {roster.Count} scenarios  {run.At:yyyy-MM-dd'T'HH:mm:ss'Z'}";
            switch (result.Outcome)
            {
                case HistoryAppendOutcome.Appended: imported++; @out.WriteLine($"imported   {label}"); break;
                case HistoryAppendOutcome.Duplicate: duplicates++; @out.WriteLine($"duplicate  {label}"); break;
                default: failed++; error.WriteLine($"not imported  {label}: {result.Message}"); break;
            }
        }

        @out.WriteLine($"{imported} run(s) imported, {duplicates} already there, {failed} failed → {ledgerPath}");
        @out.WriteLine("Imported runs carry results, attempts and durations; behaviour verdicts need the History.run.json a Kronikol run writes.");
        return failed > 0 ? 1 : 0;
    }

    private static IEnumerable<(HistoryRoster, HistoryRun, HistoryShapes?)> ImportKronikol(string input, Args args, TextWriter error)
    {
        var full = Path.GetFullPath(input);
        var files = File.Exists(full)
            ? [full]
            : Directory.Exists(full)
                ? Directory.GetFiles(full, "*.json", SearchOption.AllDirectories).Where(f => Path.GetFileName(f).EndsWith("TestRunReport.json", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : throw new FileNotFoundException($"not found: {input}");

        foreach (var file in files)
        {
            if (LoadReport(file, error) is not { } index)
                continue;
            if (!ReportHistory.TryBuild(index, args.Suite, error, out var roster, out var run, out _, out var shapes))
                continue;
            yield return (roster, run with { Branch = args.Branch ?? run.Branch, Id = args.RunId ?? run.Id }, shapes);
        }
    }

    private static IEnumerable<(HistoryRoster, HistoryRun)> ImportCtrf(string input, Args args, TextWriter error)
    {
        var full = Path.GetFullPath(input);
        var files = File.Exists(full) ? [full]
            : Directory.Exists(full) ? Directory.GetFiles(full, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : throw new FileNotFoundException($"not found: {input}");

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || !results.TryGetProperty("tests", out var tests) || tests.ValueKind != JsonValueKind.Array)
                continue;

            var summary = results.TryGetProperty("summary", out var s) ? s : default;
            long? start = summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("start", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt64() : null;
            long? stop = summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("stop", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt64() : null;
            var at = stop is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : start is { } sms ? DateTimeOffset.FromUnixTimeMilliseconds(sms) : DateTimeOffset.UtcNow;
            var environment = results.TryGetProperty("environment", out var env) && env.ValueKind == JsonValueKind.Object ? env : default;
            string? Env(string name) => environment.ValueKind == JsonValueKind.Object && environment.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var entries = new List<HistoryRosterEntry>();
            var chars = new List<char>();
            var attempts = new List<char>();
            var durations = new List<int?>();
            var errors = new List<string?>();
            var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var test in tests.EnumerateArray())
            {
                var name = test.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var suite = test.TryGetProperty("suite", out var su) && su.ValueKind == JsonValueKind.String ? su.GetString() ?? "" : "";
                var stableId = test.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object && extra.TryGetProperty("stableId", out var sid) && sid.ValueKind == JsonValueKind.String
                    ? sid.GetString()!
                    : ScenarioStableId.Compute(args.Suite, suite, name);
                entries.Add(new HistoryRosterEntry(stableId, name, suite, null));
                var status = test.TryGetProperty("status", out var stt) ? stt.GetString() : null;
                chars.Add(status switch { "passed" => HistoryFormat.Passed, "failed" => HistoryFormat.Failed, "skipped" => HistoryFormat.Skipped, _ => HistoryFormat.Unknown });
                var retries = test.TryGetProperty("retries", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
                attempts.Add(HistoryFormat.AttemptChar(retries > 0 ? retries + 1 : null));
                durations.Add(test.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? (int)Math.Round(d.GetDouble()) : null);
                var message = status == "failed" && test.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? FailureText.Truncate(FailureText.FirstLine(m.GetString()), HistoryFormat.ErrorKeyLimit - 1) : null;
                errors.Add(message is null ? null : Key(message, keys, errorText));
            }

            var roster = HistoryRoster.Create(args.Suite, entries);
            yield return (roster, new HistoryRun
            {
                Id = args.RunId ?? $"ctrf:{(start ?? stop ?? at.ToUnixTimeMilliseconds()).ToString(CultureInfo.InvariantCulture)}:1",
                Suite = args.Suite, Partial = null, At = at, Branch = args.Branch ?? Env("branchName"), Commit = Env("commit"),
                Provider = Env("buildName"), Url = Env("buildUrl"), Shards = 1, RosterHash = roster.Hash,
                Results = new string(chars.ToArray()), Attempts = new string(attempts.ToArray()), Durations = durations, Errors = errors, ErrorText = errorText
            });
        }
    }

    private static IEnumerable<(HistoryRoster, HistoryRun)> ImportAllure(string input, Args args, TextWriter error)
    {
        var full = Path.GetFullPath(input);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"--from-allure takes an allure-results directory: {input}");
        var files = Directory.GetFiles(full, "*-result.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
            yield break;

        var latest = new Dictionary<string, (JsonElement Result, int Attempts, long Stop)>(StringComparer.Ordinal);
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var file in files)
            {
                var document = JsonDocument.Parse(File.ReadAllText(file));
                documents.Add(document);
                var root = document.RootElement;
                var key = root.TryGetProperty("historyId", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString()!
                    : root.TryGetProperty("fullName", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()!
                    : root.TryGetProperty("name", out var nm) ? nm.GetString() ?? file : file;
                var stop = root.TryGetProperty("stop", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt64() : 0;
                if (latest.TryGetValue(key, out var existing))
                    latest[key] = stop >= existing.Stop ? (root, existing.Attempts + 1, stop) : (existing.Result, existing.Attempts + 1, existing.Stop);
                else
                    latest[key] = (root, 1, stop);
            }

            var entries = new List<HistoryRosterEntry>();
            var chars = new List<char>();
            var attempts = new List<char>();
            var durations = new List<int?>();
            var errors = new List<string?>();
            var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            long maxStop = 0;
            foreach (var (result, count, stop) in latest.Values.OrderBy(v => v.Result.TryGetProperty("start", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt64() : 0))
            {
                maxStop = Math.Max(maxStop, stop);
                var name = result.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string? Label(string which) => result.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
                    ? labels.EnumerateArray().Where(l => l.TryGetProperty("name", out var ln) && ln.GetString() == which).Select(l => l.TryGetProperty("value", out var lv) ? lv.GetString() : null).FirstOrDefault(v => !string.IsNullOrEmpty(v))
                    : null;
                var feature = Label("feature") ?? Label("suite") ?? "";
                entries.Add(new HistoryRosterEntry(ScenarioStableId.Compute(args.Suite, feature, name), name, feature, null));
                var status = result.TryGetProperty("status", out var stt) ? stt.GetString() : null;
                chars.Add(status switch { "passed" => HistoryFormat.Passed, "failed" or "broken" => HistoryFormat.Failed, "skipped" => HistoryFormat.Skipped, _ => HistoryFormat.Unknown });
                attempts.Add(HistoryFormat.AttemptChar(count > 1 ? count : null));
                var start = result.TryGetProperty("start", out var sta) && sta.ValueKind == JsonValueKind.Number ? sta.GetInt64() : 0;
                durations.Add(stop > start ? (int)Math.Min(int.MaxValue, stop - start) : null);
                var message = chars[^1] == HistoryFormat.Failed && result.TryGetProperty("statusDetails", out var details) && details.ValueKind == JsonValueKind.Object
                              && details.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? FailureText.Truncate(FailureText.FirstLine(m.GetString()), HistoryFormat.ErrorKeyLimit - 1) : null;
                errors.Add(message is null ? null : Key(message, keys, errorText));
            }

            var roster = HistoryRoster.Create(args.Suite, entries);
            var at = maxStop > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(maxStop) : DateTimeOffset.UtcNow;
            yield return (roster, new HistoryRun
            {
                Id = args.RunId ?? $"allure:{maxStop.ToString(CultureInfo.InvariantCulture)}:1",
                Suite = args.Suite, Partial = null, At = at, Branch = args.Branch, Commit = null, Provider = null, Url = null, Shards = 1,
                RosterHash = roster.Hash, Results = new string(chars.ToArray()), Attempts = new string(attempts.ToArray()), Durations = durations, Errors = errors, ErrorText = errorText
            });
        }
        finally
        {
            foreach (var document in documents) document.Dispose();
        }
    }

    private static string Key(string text, Dictionary<string, string> keys, Dictionary<string, string> errorText)
    {
        if (!keys.TryGetValue(text, out var key))
        {
            key = "e" + (keys.Count + 1).ToString(CultureInfo.InvariantCulture);
            keys[text] = key;
            errorText[key] = text;
        }
        return key;
    }
}
