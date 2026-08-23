using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// The narrative layer — steps, assertions, failures, the flow of one scenario. It is 0.4% of a report by
/// size, which is why these commands hand back whole trees rather than pages of them: the expensive thing
/// in a report is never the story, it is the payloads.
/// </summary>
internal static partial class QueryCommand
{
    private static int Failures(ReportIndex index, QueryOptions options, QueryWriter writer)
    {
        var failed = index.Scenarios.Where(s => s.Failed).ToList();

        if (options.Count)
        {
            writer.Line(failed.Count.ToString());
            return 0;
        }

        if (failed.Count == 0)
        {
            writer.Line("nothing failed");
            writer.Footer($"{index.Scenarios.Count} scenarios, all passed · next: scenarios · services");
            return 0;
        }

        foreach (var scenario in failed.Skip(options.Offset).Take(Math.Min(options.Limit, 25)))
        {
            writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}");
            if (scenario.ExampleValues.Count > 0)
                writer.Line("  example: " + string.Join(", ", scenario.ExampleValues.Select(e => $"{e.Key}={e.Value}")));
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
        }

        writer.Footer(failed.Count > options.Offset + 25
            ? $"failures: {options.Offset + 1}-{options.Offset + 25} of {failed.Count} · next: --offset {options.Offset + 25}"
            : $"{failed.Count} failed · steps s? for the whole tree · grep \"<value>\" --values to trace a number");
        return 0;
    }

    private static int Steps(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario))
            return 2;

        writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}  [{scenario.Result}]  {scenario.DurationSeconds:0.##}s");
        writer.Line($"stableId {scenario.StableId}");
        if (scenario.ExampleValues.Count > 0)
            writer.Line("example: " + string.Join(", ", scenario.ExampleValues.Select(e => $"{e.Key}={e.Value}")));
        writer.Line();

        var byStep = scenario.Interactions
            .Where(i => i.Type == "Request")
            .GroupBy(i => i.StepPath)
            .ToDictionary(g => g.Key ?? "", g => g.ToArray());

        foreach (var (path, depth, step) in scenario.AllSteps())
        {
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

        var unattributed = byStep.TryGetValue("", out var loose) ? loose.Length : 0;
        if (unattributed > 0)
            writer.Line($"\n  {unattributed} calls belong to no step"
                        + (index.Enriched ? " (before the first step, or attribution was not trusted)" : " (this report has no step attribution)"));

        writer.Footer($"{scenario.Interactions.Count(i => i.Type == "Request")} calls · interactions {scenario.Address} · flow {scenario.Address}");
        return 0;
    }

    private static int Assertions(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        var scope = index.Scenarios.AsEnumerable();
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var one))
                return 2;
            scope = [one];
        }

        var rows = new List<(ScenarioEntry Scenario, string Path, StepEntry Step)>();
        foreach (var scenario in scope)
        foreach (var (path, _, step) in scenario.AllSteps())
            if (step.IsAssertion && (!options.Failed || step.Failed))
                rows.Add((scenario, path, step));

        if (options.Count)
        {
            writer.Line(rows.Count.ToString());
            return 0;
        }

        if (rows.Count == 0)
        {
            writer.Line(options.Failed ? "no assertions failed" : "no tracked assertions in this report");
            writer.Footer("assertions reach the data file only when IncludeTrackedAssertionsInStepList is on");
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
        }, options.RerunPrefix());

        return 0;
    }

    private static int Annotations(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario))
            return 2;

        if (options.Count)
        {
            writer.Line(scenario.Annotations.Count.ToString());
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
        if (!TryScenario(index, options, error, out var scenario))
            return 2;

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

            if (options.Step is { } wanted && interaction.StepPath != wanted)
                continue;
            if (options.Service is { } service && !interaction.ServiceName.Contains(service, StringComparison.OrdinalIgnoreCase))
                continue;

            var response = FindResponse(scenario, interaction);
            var status = response?.StatusCode ?? "";
            if (options.ErrorsOnly && !LooksLikeError(status))
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
        // The response half sits after its request and is the next entry that is a response of the same
        // service; the report writes pairs adjacently, so this rarely scans more than one entry.
        for (var i = request.Ordinal + 1; i < scenario.Interactions.Count && i <= request.Ordinal + 4; i++)
        {
            var candidate = scenario.Interactions[i];
            if (candidate.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)
                && candidate.ServiceName == request.ServiceName)
                return candidate;
        }
        return null;
    }

    private static bool LooksLikeError(string status) =>
        status.Length > 0 && (!int.TryParse(status, out var numeric) ? !status.StartsWith("OK", StringComparison.OrdinalIgnoreCase) : numeric >= 400);

    private static bool TryScenario(ReportIndex index, QueryOptions options, TextWriter error, out ScenarioEntry scenario)
    {
        scenario = null!;

        if (options.Positional.Count == 0)
        {
            error.WriteLine("Which scenario? Pass an address like s3 — 'scenarios' lists them.");
            return false;
        }

        if (!Address.TryParse(options.Positional[0], out var address))
        {
            error.WriteLine($"Not an address: {options.Positional[0]}");
            return false;
        }

        if (index.Scenario(address.Scenario) is not { } found)
        {
            error.WriteLine($"No scenario s{address.Scenario} — the report has {index.Scenarios.Count} (s0-s{index.Scenarios.Count - 1}).");
            return false;
        }

        scenario = found;
        return true;
    }
}
