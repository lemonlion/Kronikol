using Kronikol.Reports;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// The narrative layer — steps, assertions, failures, the flow of one scenario. It is 0.4% of a report by
/// size, which is why these commands hand back whole trees rather than pages of them: the expensive thing
/// in a report is never the story, it is the payloads.
/// </summary>
internal static partial class QueryCommand
{
    private static int Failures(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        // `failures` prints step addresses (`s1/1.1`) and used to refuse to take one back: the positional
        // was parsed and discarded, so the whole run came back looking like the narrowed answer.
        var scope = index.Scenarios.AsEnumerable();
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var one, out var step))
                return 2;

            scope = [one];
            if (step is not null)
                writer.Note($"! the failure digest is per scenario — scoped to {one.Address}; `steps {one.Address}/{step}` answers for the step");
        }

        var failed = scope.Where(s => s.Failed).ToList();

        if (options.Count)
        {
            writer.Count(failed.Count);
            return 0;
        }

        if (failed.Count == 0)
        {
            writer.Note("nothing failed");
            // Counted, not inferred from the absence of failures. A scenario that was skipped did not
            // pass, and saying it did is the one line here that ends an investigation.
            var passed = index.Scenarios.Count(s => s.Result.Equals("Passed", StringComparison.OrdinalIgnoreCase));
            writer.Footer(passed == index.Scenarios.Count
                ? $"{index.Scenarios.Count} scenarios, all passed · next: scenarios · services"
                : $"{index.Scenarios.Count} scenarios: {passed} passed, {index.Scenarios.Count - passed} did not run · next: scenarios · services");
            return 0;
        }

        var deepLink = DeepLinkPrefix(index);

        // Paged through the one pager rather than a hand-rolled Skip/Take with a hand-rolled footer. That
        // footer hard-coded the 25 cap and ignored --limit, so `--limit 2` on three failures printed two
        // and then said "3 failed" with no resume - the one shape the footer contract exists to prevent.
        writer.Page(failed, options.Offset, Math.Min(options.Limit, 25), "failures", scenario =>
        {
            writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}");
            if (scenario.ExampleValues.Count > 0)
                writer.Line("  example: " + string.Join(", ", scenario.ExampleValues.Select(e => $"{e.Key}={e.Value}")));
            if (scenario.SourceFile is { } source)
                writer.Line($"  at {source}" + (scenario.SourceLine is { } line ? $":{line}" : ""));
            if (deepLink is not null && scenario.StableId.Length > 0)
                writer.Line($"  open: {deepLink}{scenario.StableId}");
            if (scenario.ErrorMessage is { } message)
                writer.Line("  " + QueryWriter.OneLine(message, 240));

            var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
            foreach (var (path, depth, step) in failingSteps)
            {
                writer.Line($"  {new string(' ', depth * 2)}✗ {scenario.Address}/{path}  {QueryWriter.OneLine(step.Display, 100)}");
                if (step.FailureMessage is { } stepMessage)
                    writer.Line($"  {new string(' ', depth * 2)}  {QueryWriter.OneLine(stepMessage, 200)}");
                if (step.SourceFile is { } file)
                    writer.Line($"  {new string(' ', depth * 2)}  at {file}:{step.SourceLine}");

                var scoped = scenario.Interactions.Where(i => i.StepPath == path && i.Type == "Request").ToArray();
                if (scoped.Length > 0)
                {
                    writer.Line($"  {new string(' ', depth * 2)}  {scoped.Length} calls in this step:");
                    foreach (var interaction in scoped.Take(6))
                        writer.Line($"  {new string(' ', depth * 2)}    {interaction.Address(scenario)}  {interaction.ServiceName}  {QueryWriter.OneLine(interaction.Summary(), 70)}");
                }
            }

            if (failingSteps.Length == 0 && scenario.Steps.Count > 0)
                writer.Line("  (no step is marked failed — the failure was outside a tracked step)");

            if (scenario.Attachments.Count > 0)
                foreach (var attachment in scenario.Attachments.Take(4))
                    writer.Line($"  attachment: {attachment.Name} → {attachment.Resolve(index.Directory)}");

            writer.Line();
        }, options.RerunArgs(), scenario => FailureRecord(index, scenario, deepLink),
            $"{failed.Count} failed · steps s? for the whole tree · grep \"<value>\" --values to trace a number");

        if (NeedsParameterCaptureHint(index, failed))
            writer.Note("! " + ParameterCaptureHint.Message);

        return 0;
    }

    /// <summary>
    /// One failure as an object, in the field names <c>Failures.jsonl</c> already uses
    /// (<c>FailuresDigestGenerator.BuildJsonl</c>). The same concept must not reach a consumer under two
    /// shapes depending on whether it read the digest the run wrote or asked the tool for it afterwards.
    ///
    /// <para><b>They are not yet the same object, and this is the list.</b> The digest carries
    /// <c>expected</c>, <c>actual</c>, <c>cluster</c>, <c>stepsBefore</c>, <c>thrownAt</c>,
    /// <c>callsScope</c> and <c>truncated</c>; this does not. This nests <c>calls</c> under the failing
    /// step that made them and caps them at six per step; the digest lists them once per failure, with a
    /// <c>status</c>, capped at eight. Attachments are capped at four here and uncapped there. Closing
    /// that gap needs <c>ReportScanner</c> to read <c>errorStackTrace</c>, which it does not, so it is
    /// M7's work and not a line to add here — but the divergence is written down rather than left for a
    /// consumer to discover by diffing two files that claim to describe the same failure.</para>
    /// </summary>
    private static object FailureRecord(ReportIndex index, ScenarioEntry scenario, string? deepLink)
    {
        var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();

        return new
        {
            address = scenario.Address,
            stableId = scenario.StableId,
            feature = scenario.FeatureName,
            scenario = scenario.Name,
            exampleValues = scenario.ExampleValues,
            durationSeconds = scenario.DurationSeconds,
            errorMessage = scenario.ErrorMessage,
            deepLink = deepLink is not null && scenario.StableId.Length > 0 ? deepLink + scenario.StableId : null,
            sourceFile = scenario.SourceFile,
            sourceLine = scenario.SourceLine,
            failingSteps = failingSteps.Select(s => new
            {
                path = $"{scenario.Address}/{s.Path}",
                text = s.Step.Display,
                message = s.Step.FailureMessage,
                sourceFile = s.Step.SourceFile,
                sourceLine = s.Step.SourceLine,
                calls = scenario.Interactions
                    .Where(i => i.StepPath == s.Path && i.Type == "Request")
                    .Take(6)
                    .Select(i => new
                    {
                        address = i.Address(scenario),
                        service = i.ServiceName,
                        summary = i.Summary(),
                        durationMs = i.DurationMs
                    })
            }),
            attachments = scenario.Attachments.Take(4)
                .Select(a => new { name = a.Name, path = a.Resolve(index.Directory) })
        };
    }

    private static int Steps(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario, out var scope))
            return 2;

        writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}  [{scenario.Result}]  {scenario.DurationSeconds:0.##}s");
        writer.Line($"stableId {scenario.StableId}");
        if (scenario.SourceFile is { } scenarioSource)
            writer.Line($"at {scenarioSource}" + (scenario.SourceLine is { } scenarioLine ? $":{scenarioLine}" : ""));
        if (DeepLinkPrefix(index) is { } stepsLink && scenario.StableId.Length > 0)
            writer.Line($"open: {stepsLink}{scenario.StableId}");
        if (scenario.ExampleValues.Count > 0)
            writer.Line("example: " + string.Join(", ", scenario.ExampleValues.Select(e => $"{e.Key}={e.Value}")));
        writer.Line();

        var byStep = scenario.Interactions
            .Where(i => i.Type == "Request")
            .GroupBy(i => i.StepPath)
            .ToDictionary(g => g.Key ?? "", g => g.ToArray());

        // A step address answers for that step and everything under it. The depth is re-based on the
        // scope so a scoped tree reads as a tree rather than as an indented fragment of one.
        var rows = scenario.AllSteps().Where(row => scope is null || Address.PathCoveredBy(row.Path, scope)).ToList();
        var baseDepth = scope is null ? 0 : rows.Min(row => row.Depth);
        if (scope is not null)
            writer.Line($"scoped to step {scope} and its sub-steps — `steps {scenario.Address}` for the whole scenario\n");

        foreach (var (path, rawDepth, step) in rows)
        {
            var depth = rawDepth - baseDepth;
            var mark = step.Failed ? "✗" : step.Status is "Bypassed" ? "~" : step.IsAssertion ? "·" : " ";
            var indent = new string(' ', depth * 2);
            var duration = step.DurationSeconds is { } d and > 0.001 ? $"  {d:0.##}s" : "";

            var range = "";
            if (byStep.TryGetValue(path, out var calls) && calls.Length > 0)
                range = calls.Length == 1
                    ? $"  [i{calls[0].Ordinal}]"
                    : $"  [i{calls[0].Ordinal}-i{calls[^1].Ordinal}] {calls.Length} calls";

            writer.Line($"{indent}{mark} {path,-5} {QueryWriter.OneLine(step.Display, 90)}{duration}{range}");

            if (step.FailureMessage is { } message)
                writer.Line($"{indent}      {QueryWriter.OneLine(message, 180)}"
                            + (step.SourceFile is { } file ? $"   at {file}:{step.SourceLine}" : ""));
            if (step.BypassReason is { } bypass)
                writer.Line($"{indent}      bypassed: {QueryWriter.OneLine(bypass, 120)}");
            foreach (var parameter in step.Parameters.Take(8))
                writer.Line($"{indent}      · {QueryWriter.OneLine(parameter, 100)}");
            if (step.DocString is { } doc)
                writer.Line($"{indent}      \"\"\"{QueryWriter.OneLine(doc, 100)}\"\"\"");
            foreach (var attachment in step.Attachments)
                writer.Line($"{indent}      attachment: {attachment.Name} → {attachment.Resolve(index.Directory)}");
        }

        var unattributed = scope is null && byStep.TryGetValue("", out var loose) ? loose.Length : 0;
        if (unattributed > 0)
            writer.Line($"\n  {unattributed} calls belong to no step"
                        + (index.Enriched ? " (before the first step, or attribution was not trusted)" : " (this report has no step attribution)"));

        writer.Footer($"{scenario.Interactions.Count(i => i.Type == "Request")} calls · interactions {scenario.Address} · flow {scenario.Address}");
        return 0;
    }

    private static int Assertions(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        var scope = index.Scenarios.AsEnumerable();
        string? stepScope = null;
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var one, out stepScope))
                return 2;
            scope = [one];
        }

        // `assertions` prints one address per assertion, under a JSON field literally named `address`.
        // Feeding one back used to return every assertion in the scenario: the verb that printed the
        // address could not use it, and answered with a strictly wider set at exit 0.
        var rows = new List<(ScenarioEntry Scenario, string Path, StepEntry Step)>();
        foreach (var scenario in scope)
        foreach (var (path, _, step) in scenario.AllSteps())
            if (step.IsAssertion && (!options.Failed || step.Failed)
                && (stepScope is null || Address.PathCoveredBy(path, stepScope)))
                rows.Add((scenario, path, step));

        if (options.Count)
        {
            writer.Count(rows.Count);
            return 0;
        }

        if (rows.Count == 0)
        {
            // "no assertions failed" was printed over a run with fifteen failures, and the remedy named an
            // option that was already on. Two different states were being answered with one sentence, and
            // the one the reader is in decides what to do next: a report that tracks no assertions at all
            // (the option), against a report that tracks them and had none fail (the failure was not at an
            // assertion - the test threw first, which is a per-failure fact, not a per-project one).
            // Either way, if the report says scenarios failed, the answer must not read as "nothing did".
            var tracked = scope.Sum(s => s.AllSteps().Count(row => row.Step.IsAssertion
                                                                   && (stepScope is null || Address.PathCoveredBy(row.Path, stepScope))));
            var failedScenarios = index.Scenarios.Count(s => s.Failed);
            var still = failedScenarios > 0
                ? $" · {failedScenarios} scenario{(failedScenarios == 1 ? "" : "s")} failed — `failures` has the message and the failing step"
                : "";

            if (tracked == 0)
            {
                writer.Note("this report tracks no assertions");
                writer.Footer("assertions reach the data file only when IncludeTrackedAssertionsInStepList is on" + still);
            }
            else if (options.Failed)
            {
                writer.Note($"no assertion failed — {tracked} tracked, all passed");
                writer.Footer(failedScenarios > 0
                    ? $"{failedScenarios} scenario{(failedScenarios == 1 ? "" : "s")} failed without reaching a tracked assertion — `failures` has the message"
                    : "nothing failed");
            }
            else
            {
                writer.Note("no tracked assertions in this scope");
                writer.Footer($"the report holds {tracked}{still}");
            }

            return 0;
        }

        writer.Page(rows, options.Offset, Math.Min(options.Limit, 200), "assertions", row =>
        {
            var mark = row.Step.Failed ? "✗" : "✓";
            writer.Line($"{mark} {row.Scenario.Address}/{row.Path,-5} {QueryWriter.OneLine(row.Step.Text, 100)}");
            if (row.Step.FailureMessage is { } message)
                writer.Line($"     {QueryWriter.OneLine(message, 180)}");
            if (row.Step.SourceFile is { } file)
                writer.Line($"     at {file}:{row.Step.SourceLine}");
        }, options.RerunArgs(), row => new
        {
            address = $"{row.Scenario.Address}/{row.Path}",
            scenario = row.Scenario.Name,
            path = row.Path,
            text = row.Step.Text,
            passed = !row.Step.Failed,
            message = row.Step.FailureMessage,
            sourceFile = row.Step.SourceFile,
            sourceLine = row.Step.SourceLine
        });

        return 0;
    }

    private static int Annotations(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario, out var addressedStep))
            return 2;

        // Exported annotations are attributed to the scenario, not to a step: there is no narrower answer
        // to give, and giving the wider one silently is how a step address came to mean nothing at all.
        if (addressedStep is not null)
        {
            error.WriteLine($"Annotations are recorded per scenario, not per step — drop the /{addressedStep} and ask for {scenario.Address}.");
            return 2;
        }

        if (options.Count)
        {
            writer.Count(scenario.Annotations.Count);
            return 0;
        }

        if (scenario.Annotations.Count == 0)
        {
            writer.Line("no annotations");
            writer.Footer("annotations are example-row markers and fragments injected with Track / InsertPlantUml");
            return 0;
        }

        foreach (var annotation in scenario.Annotations)
            writer.Line($"before i{annotation.Index,-4} {annotation.Kind,-7} {QueryWriter.OneLine(annotation.Text, 120)}");

        writer.Footer($"{scenario.Annotations.Count} annotations · interactions {scenario.Address}");
        return 0;
    }

    private static int Flow(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario, out var addressedStep))
            return 2;

        // `flow s0/1` and `flow s0 --step 1` are the same question, and both cover the step's sub-steps.
        options.ScopeToStep(addressedStep);

        writer.Line($"{scenario.Address}  {QueryWriter.OneLine(scenario.Name, 90)}  [{scenario.Result}]");
        writer.Line();

        var responses = scenario.Interactions
            .Where(i => i.Type.Equals("Response", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Ordinal)
            .ToDictionary(g => g.Key, g => g.First());

        var annotationsByIndex = scenario.Annotations.ToLookup(a => a.Index);
        var stepsByPath = scenario.AllSteps().ToDictionary(s => s.Path, s => s.Step);
        string? currentStep = null;
        var shown = 0;

        for (var i = 0; i < scenario.Interactions.Count; i++)
        {
            var interaction = scenario.Interactions[i];

            foreach (var annotation in annotationsByIndex[i])
                writer.Line($"  ── {annotation.Text}");

            if (interaction.StepPath != currentStep)
            {
                currentStep = interaction.StepPath;
                if (currentStep is not null && stepsByPath.TryGetValue(currentStep, out var step))
                    writer.Line($"── {currentStep}  {QueryWriter.OneLine(step.Display, 90)}");
            }

            if (!interaction.Type.Equals("Request", StringComparison.OrdinalIgnoreCase))
                continue;

            if (options.Step is { } wanted && !Address.PathCoveredBy(interaction.StepPath, wanted))
                continue;
            if (options.Service is { } service && !interaction.ServiceName.Contains(service, StringComparison.OrdinalIgnoreCase))
                continue;

            var response = FindResponse(scenario, interaction);
            var status = StatusOf(response).Text ?? "";
            if (options.ErrorsOnly && !IsError(response?.StatusCode, response?.StatusText))
                continue;

            var payload = interaction.BodyHash is { } hash ? $"  {hash} {QueryWriter.Size(interaction.BodyLength)}" : "";
            var timing = interaction.DurationMs ?? response?.DurationMs;
            writer.Line($"  {interaction.Address(scenario),-9} {interaction.CallerName} → {interaction.ServiceName}  "
                        + $"{QueryWriter.OneLine(interaction.Summary(), 60)}  {status}  {QueryWriter.Duration(timing)}{payload}");
            shown++;
        }

        if (shown == 0)
            writer.Line("  (nothing matched the filters)");

        writer.Footer($"{shown} calls shown · http {scenario.Address}/iN --keys for a payload");
        return 0;
    }

    private static InteractionEntry? FindResponse(ScenarioEntry scenario, InteractionEntry request)
    {
        // The report carries the exact pairing key — requestResponseId, the same identity the diagram
        // pipeline groups on. Under interleaved parallel calls to one service the old proximity scan
        // attached the wrong response; the scan survives only for entries that carry no id.
        if (request.RequestResponseId is { } id)
        {
            foreach (var candidate in scenario.Interactions)
                if (candidate.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)
                    && candidate.RequestResponseId == id)
                    return candidate;
            return null;
        }

        for (var i = request.Ordinal + 1; i < scenario.Interactions.Count && i <= request.Ordinal + 4; i++)
        {
            var candidate = scenario.Interactions[i];
            if (candidate.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)
                && candidate.ServiceName == request.ServiceName)
                return candidate;
        }
        return null;
    }

    private static bool TryScenario(ReportIndex index, QueryOptions options, TextWriter error, out ScenarioEntry scenario) =>
        TryScenario(index, options, error, out scenario, out _);

    /// <summary>
    /// The one place an address on the command line becomes a scenario, and - when the address named a
    /// step - the path that scopes the answer.
    /// </summary>
    /// <remarks>
    /// <para>This never looked at <see cref="Address.Kind"/>. It read <see cref="Address.Scenario"/> and
    /// returned, so a step path was parsed and thrown away: <c>steps s0/99</c> was byte-identical to
    /// <c>steps s0</c>, and an address printed by <c>failures</c> or <c>assertions</c> came back as the
    /// whole scenario at exit 0. An answer that is strictly wider than the one that was asked for, with
    /// nothing said, is the failure mode the per-verb flag validator exists to prevent, arriving through
    /// the positional instead of through a flag.</para>
    ///
    /// <para><paramref name="stepPath"/> is null unless the address named a step, and the path is checked
    /// against the scenario here rather than by each caller, so a path that is in no scenario is refused
    /// once instead of being widened five different ways.</para>
    /// </remarks>
    private static bool TryScenario(ReportIndex index, QueryOptions options, TextWriter error, out ScenarioEntry scenario, out string? stepPath)
    {
        scenario = null!;
        stepPath = null;

        if (options.Positional.Count == 0)
        {
            error.WriteLine("Which scenario? Pass an address like s3, or sid:<stableId> — 'scenarios' lists them.");
            return false;
        }

        var text = options.Positional[0];
        if (!Address.TryParse(text, out var address))
        {
            error.WriteLine($"Not an address: {text}");
            return false;
        }

        if (address.Kind == AddressKind.StableId)
            return TryStableId(index, address.StableId!, error, out scenario);

        if (address.Kind is AddressKind.Body)
        {
            error.WriteLine($"{text} is a body address, not a scenario. `body <report> {text}` reads it.");
            return false;
        }

        if (index.Scenario(address.Scenario) is not { } found)
        {
            error.WriteLine(index.Scenarios.Count == 0
                ? $"No scenario s{address.Scenario} — the report has none."
                : $"No scenario s{address.Scenario} — the report has {index.Scenarios.Count} (s0-s{index.Scenarios.Count - 1}).");
            return false;
        }

        if (address.Kind == AddressKind.Step)
        {
            var wanted = address.StepPath!;
            if (!found.AllSteps().Any(step => Address.PathCoveredBy(step.Path, wanted)))
            {
                error.WriteLine($"No step {wanted} in {found.Address} — `steps {found.Address}` lists its paths.");
                return false;
            }

            stepPath = wanted;
        }

        scenario = found;
        return true;
    }

    /// <summary>
    /// A scenario by its <c>stableId</c>. Several scenarios can carry one - a repeated <c>[Theory]</c>
    /// row, the same <c>Examples:</c> row in two blocks, a retry - so an ambiguous id is reported with
    /// the ordinals that resolve it rather than silently resolved to the first match.
    /// </summary>
    private static bool TryStableId(ReportIndex index, string stableId, TextWriter error, out ScenarioEntry scenario)
    {
        scenario = null!;

        var matches = index.Scenarios
            .Where(s => string.Equals(s.StableId, stableId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        switch (matches.Count)
        {
            case 0:
                error.WriteLine($"No scenario with stableId {stableId} in {Path.GetFileName(index.Path)}.");
                error.WriteLine(index.Scenarios.Any(s => s.StableId.Length > 0)
                    ? "`scenarios --json` lists every stableId in this report."
                    : "This report carries no stableIds at all (written before 3.0.47) — address scenarios by ordinal.");
                return false;
            case 1:
                scenario = matches[0];
                return true;
            default:
                error.WriteLine($"{matches.Count} scenarios share stableId {stableId} (a repeated row, or a retry) — name the one you mean:");
                foreach (var match in matches)
                    error.WriteLine($"  {match.Address}  {QueryWriter.OneLine(match.Name, 70)}  [{match.Result}]");
                return false;
        }
    }

    /// <summary>
    /// Whether any failing scenario made a SQL call whose statement names placeholders it never fills in
    /// (<see cref="ParameterCaptureHint"/>). The index holds no content — only a hash, a length and an
    /// offset — so this is a payload read, and it is bounded on purpose: failing scenarios only,
    /// SQL-shaped request interactions only, short statements only, and it stops at the first hit. A
    /// green run opens nothing at all.
    /// </summary>
    private static bool NeedsParameterCaptureHint(ReportIndex index, IReadOnlyList<ScenarioEntry> failed)
    {
        const int LongestStatementWorthReading = 8192;

        foreach (var scenario in failed)
        {
            foreach (var interaction in scenario.Interactions)
            {
                if (interaction.Type != "Request"
                    || interaction.BodyLength is 0 or > LongestStatementWorthReading
                    || !ParameterCaptureHint.IsSqlShaped(interaction.DependencyCategory))
                {
                    continue;
                }

                if (ParameterCaptureHint.ContentLacksParameters(PayloadReader.Read(index, interaction.Body)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>TestRunReport.html#sid-</c> when that file is sitting next to the data file this command read,
    /// otherwise null. The HTML is optional output, and a link into a file that was never generated is
    /// worse than no link: it sends a reader looking for something that does not exist.
    /// </summary>
    private static string? DeepLinkPrefix(ReportIndex index)
    {
        var htmlName = Path.GetFileNameWithoutExtension(index.Path) + ".html";
        return File.Exists(Path.Combine(index.Directory, htmlName)) ? $"{htmlName}#sid-" : null;
    }
}
