using Kronikol.Reports;

namespace Kronikol.Query;

/// <summary>
/// The narrative layer — steps, assertions, failures, the flow of one scenario. It is 0.4% of a report by
/// size, which is why these commands hand back whole trees rather than pages of them: the expensive thing
/// in a report is never the story, it is the payloads.
/// </summary>
public static partial class QueryCommand
{
    /// <summary>
    /// How many of a list of attachments <c>failures</c> prints, the scenario's own or a failing step's: the first
    /// <see cref="AttachmentsHead"/> and the last five, with a line in place of the rest that names them and
    /// <c>attachments</c>, which lists every one (#161).
    /// </summary>
    private const int MaxAttachmentsShown = 6;

    /// <summary>How many of a capped list of attachments come before the cut: the test's own first, its starting state.</summary>
    private const int AttachmentsHead = 1;

    /// <summary>How many of the calls made in a failing step <c>failures</c> prints under it; <c>flow sN/P</c> lists them all.</summary>
    private const int MaxFailingStepCalls = 6;

    /// <summary>How many of a step's parameter lines the whole tree of <c>steps</c> prints; the tree scoped to the step prints them all.</summary>
    private const int MaxParametersShown = 8;

    private static int Failures(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error, Func<string, string?> getEnv)
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

        // a scenario that is not a test is no failure of the run's, as the report's band counts
        var failed = scope.Where(s => s.Failed && !s.NotATest).ToList();

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
            // The run's tests: a scenario that is not a test (an ingest's fold) is named apart (plans/RUN_WINDOW_PLAN.md R1).
            var tests = index.Scenarios.Where(s => !s.NotATest).ToList();
            var notATest = index.Scenarios.Count - tests.Count;
            var passed = tests.Count(s => s.Result.Equals("Passed", StringComparison.OrdinalIgnoreCase));
            // A bypassed scenario ran: a step of it was skipped over while the steps after it ran.
            var bypassed = tests.Count(s => s.Result.Equals("Bypassed", StringComparison.OrdinalIgnoreCase));
            var notRun = tests.Count - passed - bypassed;
            var notATestText = notATest > 0 ? $" · {notATest} not a test" : "";
            writer.Footer(passed == tests.Count
                ? $"{tests.Count} scenarios, all passed{notATestText} · next: scenarios · services"
                : $"{tests.Count} scenarios: {passed} passed"
                  + (notRun > 0 ? $", {notRun} did not run" : "")
                  + (bypassed > 0 ? $", {bypassed} bypassed" : "")
                  + notATestText
                  + " · next: scenarios · services");
            return 0;
        }

        var deepLink = DeepLinkPrefix(index);

        // What the last runs say about each failure, when a ledger resolves without being asked for: the
        // one line that tells a regression from the fifth day of the same red. Silent when nothing
        // resolves - this verb never mentioned history and must not start failing over it.
        var history = ReportHistory.TryVerdictsSilently(index, getEnv, _workingDirectory);

        // A list view cuts, keeps both ends (an assertion's verdict is at the end), and its footer names
        // the view that prints the text whole: `steps sN` for a message, `history sN` for evidence. The
        // notice names the first row of each kind that was cut. Head-only at 240 with no address, since
        // 3.0.47, was #82's shape on this verb.
        string? cutMessage = null, cutEvidence = null;
        void Notice() => writer.CutNotice("… marks cut text — " + string.Join(" · ", new[]
        {
            cutMessage is { } m ? $"steps {m} prints the message whole" : null,
            cutEvidence is { } e ? $"history {e} prints the evidence whole" : null
        }.Where(part => part is not null)));
        string CutMessage(string? text, int max, string address)
        {
            var cut = writer.Cut(text, max);
            if (cutMessage is null && cut.Length != QueryWriter.Flat(text).Length) { cutMessage = address; Notice(); }
            return cut;
        }
        string CutEvidence(string? text, int max, string address)
        {
            var cut = writer.Cut(text, max);
            if (cutEvidence is null && cut.Length != QueryWriter.Flat(text).Length) { cutEvidence = address; Notice(); }
            return cut;
        }

        // Paged through the one pager rather than a hand-rolled Skip/Take with a hand-rolled footer. That
        // footer hard-coded the 25 cap and ignored --limit, so `--limit 2` on three failures printed two
        // and then said "3 failed" with no resume - the one shape the footer contract exists to prevent.
        writer.Page(failed, options.Offset, options.PageSize(25, writer, "failures"), "failures", scenario =>
        {
            writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}");
            if (scenario.ExampleValues.Count > 0)
                writer.Line("  example: " + string.Join(", ", scenario.ExampleValues.Select(e => $"{e.Key}={e.Value}")));
            if (scenario.SourceFile is { } source)
                writer.Line($"  at {source}" + (scenario.SourceLine is { } line ? $":{line}" : ""));
            // Where it was THROWN, which is not where it was written, and the command that re-runs it -
            // both read out of errorStackTrace, which every other surface carried and this one dropped.
            if (FailureText.ThrownAt(scenario.ErrorStackTrace, scenario.SourceFile) is { } thrown)
            {
                writer.Line($"  thrown at {thrown.Method} — {thrown.File}:{thrown.Line}");
                if (FailureText.RerunCommand(thrown.Method) is { } rerun)
                    writer.Line($"  rerun: {rerun}");
            }
            if (deepLink is not null && scenario.StableId.Length > 0)
                writer.Line($"  open: {deepLink}{scenario.StableId}");
            if (history?.At(scenario.Ordinal, scenario.StableId) is { } verdict)
            {
                // A list view that cuts says so and names the view that does not (#82's rule): the
                // evidence of a scenario that broke across a fingerprint-rule change is longer than this.
                var evidence = CutEvidence(verdict.Evidence, 160, scenario.Address);
                writer.Line($"  history: {Kronikol.History.HistoryVerdictNames.Name(verdict.Primary)} — {evidence} · {verdict.Series}");
            }
            if (scenario.ErrorMessage is { } message)
                writer.Line("  " + CutMessage(message, 240, scenario.Address));

            var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
            foreach (var (path, depth, step) in failingSteps)
            {
                var indent = new string(' ', depth * 2);
                writer.Line($"  {indent}✗ {scenario.Address}/{path}  {QueryWriter.OneLine(step.Display, 100)}");
                if (step.FailureMessage is { } stepMessage)
                    writer.Line($"  {indent}  {CutMessage(stepMessage, 200, scenario.Address)}");
                if (step.SourceFile is { } file)
                    writer.Line($"  {indent}  at {file}:{step.SourceLine}");
                // What the step attached, as `steps` prints it (the scenario's own list below never held it, #175), capped so
                // one step's long list cannot push every other failure past the byte budget.
                WriteAttachments(writer, index, step.Attachments, $"  {indent}  ", $"attachments {scenario.Address}/{path}");

                var scoped = scenario.Interactions.Where(i => i.StepPath == path && i.Type == "Request").ToArray();
                if (scoped.Length > 0)
                {
                    writer.Line($"  {indent}  {scoped.Length} calls in this step:");
                    foreach (var interaction in scoped.Take(MaxFailingStepCalls))
                        writer.Line($"  {indent}    {interaction.Address(scenario)}  {interaction.ServiceName}  {QueryWriter.OneLine(interaction.Summary(), 70)}"
                            + Failure(FindResponse(scenario, interaction)));
                    // flow's step address covers the step's sub-steps too, so it lists every call cut here, and more.
                    if (scoped.Length > MaxFailingStepCalls)
                        writer.Line($"  {indent}    " + QueryWriter.CutLine(scoped.Length - MaxFailingStepCalls, "call(s)", null, $"flow {scenario.Address}/{path}"));
                }
            }

            if (failingSteps.Length == 0 && scenario.Steps.Count > 0)
                writer.Line("  (no step is marked failed — the failure was outside a tracked step)");

            WriteAttachments(writer, index, scenario.Attachments, "  ", $"attachments {scenario.Address}");

            // What the steps that did not fail attached is not listed here (the #175 plan's Q15 keeps a hook's artefacts to
            // Failures.md and steps), but it is counted: with the hooks kept, Playwright's failure screenshot, trace and video
            // are on the after-hook and fixture steps, and this said nothing of them.
            var failingPaths = failingSteps.Select(s => s.Path).ToHashSet(StringComparer.Ordinal);
            var elsewhere = scenario.AllSteps().Where(s => !failingPaths.Contains(s.Path)).SelectMany(s => s.Step.Attachments).ToList();
            if (elsewhere.Count > 0)
                writer.Line("  " + QueryWriter.CutLine(elsewhere.Count, "attachment(s) on other steps",
                    QueryWriter.NameList(elsewhere.Select(a => a.Name)), $"attachments {scenario.Address}"));

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
    /// <c>expected</c>, <c>actual</c>, <c>cluster</c>, <c>stepsBefore</c> (with <c>stepsBeforeTotal</c>),
    /// <c>callsScope</c> and <c>truncated</c>; this does not. Since 3.7.0 both carry <c>thrownAt</c>, <c>testName</c>
    /// and <c>rerun</c>, read from <c>errorStackTrace</c> by the same reader. This nests <c>calls</c> under
    /// the failing step that made them and caps them at six per step, with <c>callsTotal</c> beside them; the
    /// digest lists them once per failure, with a <c>status</c>, capped at eight, with its own <c>callsTotal</c>
    /// (4.31.0): the calls its list was drawn from, which are the scenario's when none is attributed to the
    /// failing step. The scenario's own attachments
    /// and a failing step's (nested under it) are capped here at the text's six, the first and the last five, each
    /// list with its total beside it (<c>attachmentsTotal</c>, and <c>stepAttachmentsTotal</c> for every step's);
    /// the digest lists every attachment of every step, each with a <c>step</c> address, and so does
    /// <c>attachments sN --json</c>, paged. The divergence is written down rather than left for a consumer to
    /// discover by diffing two files that claim to describe the same failure.</para>
    /// </summary>
    private static object FailureRecord(ReportIndex index, ScenarioEntry scenario, string? deepLink)
    {
        var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
        var thrown = FailureText.ThrownAt(scenario.ErrorStackTrace, scenario.SourceFile);

        return new
        {
            address = scenario.Address,
            id = NullIfEmpty(scenario.Id),
            stableId = scenario.StableId,
            feature = scenario.FeatureName,
            scenario = scenario.Name,
            exampleValues = scenario.ExampleValues,
            durationSeconds = scenario.DurationSeconds,
            errorMessage = scenario.ErrorMessage,
            deepLink = deepLink is not null && scenario.StableId.Length > 0 ? deepLink + scenario.StableId : null,
            sourceFile = scenario.SourceFile,
            sourceLine = scenario.SourceLine,
            thrownAt = thrown is { } frame ? new { method = frame.Method, file = frame.File, line = frame.Line } : null,
            testName = FailureText.TestFilter(thrown?.Method),
            rerun = FailureText.RerunCommand(thrown?.Method),
            failingSteps = failingSteps.Select(s => new
            {
                path = $"{scenario.Address}/{s.Path}",
                text = s.Step.Display,
                message = s.Step.FailureMessage,
                sourceFile = s.Step.SourceFile,
                sourceLine = s.Step.SourceLine,
                calls = scenario.Interactions
                    .Where(i => i.StepPath == s.Path && i.Type == "Request")
                    .Take(MaxFailingStepCalls)
                    .Select(i => (Request: i, Response: FindResponse(scenario, i)))
                    .Select(c => new
                    {
                        address = c.Request.Address(scenario),
                        service = c.Request.ServiceName,
                        summary = c.Request.Summary(),
                        durationMs = c.Request.DurationMs,
                        // Why a call failed, as Failures.md prints it: null for a call that did not.
                        status = Failed(c.Response) ? c.Response!.StatusCode : null,
                        error = Failed(c.Response) ? c.Response!.Error : null
                    }),
                // The calls made in the step itself, which the text counts; `flow sN/P` lists them all.
                callsTotal = scenario.Interactions.Count(i => i.StepPath == s.Path && i.Type == "Request"),
                // As the text prints them under the step, the first and the last five, so the two cannot disagree.
                attachments = QueryWriter.CutList(s.Step.Attachments, MaxAttachmentsShown, AttachmentsHead).Shown.Select(a => AttachmentObject(a, index)),
                attachmentsTotal = s.Step.Attachments.Count
            }),
            attachments = QueryWriter.CutList(scenario.Attachments, MaxAttachmentsShown, AttachmentsHead).Shown.Select(a => AttachmentObject(a, index)),
            // Totals are always present, by the envelope's rule for total: a consumer that branches on a key's presence
            // cannot tell "none" from "an older tool". The two add up to what Failures.jsonl lists for the failure.
            attachmentsTotal = scenario.Attachments.Count,
            stepAttachmentsTotal = scenario.AllSteps().Sum(s => s.Step.Attachments.Count)
        };
    }

    /// <summary>
    /// A list of attachments as <c>failures</c> prints it, each line opening with <paramref name="indent"/>: every one up to
    /// <see cref="MaxAttachmentsShown"/>, else the first and the last five around a line naming the cut entries and
    /// <paramref name="view"/>, which lists them all.
    /// </summary>
    private static void WriteAttachments(QueryWriter writer, ReportIndex index, IReadOnlyList<AttachmentEntry> attachments, string indent, string view)
    {
        var cut = QueryWriter.CutList(attachments, MaxAttachmentsShown, AttachmentsHead);
        foreach (var attachment in cut.Head)
            writer.Line($"{indent}attachment: {AttachmentText(attachment, index)}");
        if (cut.Cut.Count > 0)
            writer.Line(indent + QueryWriter.CutLine(cut.Cut.Count, "attachment(s)", QueryWriter.NameList(cut.Cut.Select(a => a.Name)), view));
        foreach (var attachment in cut.Tail)
            writer.Line($"{indent}attachment: {AttachmentText(attachment, index)}");
    }

    /// <summary>
    /// An attachment as every view prints it: its name, and where it is. <c>failures</c>, <c>steps</c> and <c>attachments</c>
    /// all print it through here, and <see cref="AttachmentObject"/> is its <c>--json</c> form, so anything said about an
    /// attachment is said in one place.
    /// </summary>
    private static string AttachmentText(AttachmentEntry attachment, ReportIndex index) => attachment.Problem is { } problem
        // One the report does not hold (#148): the path as it was given, and why; resolving it would invent a file.
        ? $"{attachment.Name} → {attachment.RelativePath} ({AttachmentProblemText.Of(problem)})"
        : $"{attachment.Name} → {attachment.Resolve(index.Directory)}";

    /// <summary>An attachment in <c>--json</c>, in the field names <c>Failures.jsonl</c> uses: <see cref="AttachmentText"/>'s object form.</summary>
    private static object AttachmentObject(AttachmentEntry attachment, ReportIndex index) => attachment.Problem is { } problem
        ? new { name = attachment.Name, path = attachment.RelativePath, problem }
        : new { name = attachment.Name, path = attachment.Resolve(index.Directory) };

    /// <summary>
    /// <c>repro &lt;report&gt; [s3]</c>: the one line that re-runs each failing test, read from the frame
    /// its failure was thrown in.
    /// </summary>
    /// <remarks>
    /// The fully qualified name is in <c>errorStackTrace</c> for every failing scenario in the exact shape
    /// <c>--filter</c> wants, and no agent-facing surface printed it: the agent read the failure and then
    /// had to find the test by hand. A tool-side reader retro-fits every report ever written, where a
    /// <c>testId</c> field would have needed every adapter to write it first. A passing scenario has no
    /// trace, and says so rather than guessing a name.
    /// </remarks>
    private static int Repro(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        IReadOnlyList<ScenarioEntry> scope;
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var one))
                return 2;
            scope = [one];
        }
        else
        {
            // the run's failed tests, as failures counts them: a scenario that is not a test is no failure of the run's
            scope = index.Scenarios.Where(s => s.Failed && !s.NotATest).ToList();
        }

        if (options.Count)
        {
            writer.Count(scope.Count);
            return 0;
        }

        if (scope.Count == 0)
        {
            writer.Note("nothing failed");
            var notATest = index.Scenarios.Count(s => s.NotATest);
            writer.Footer($"{index.Scenarios.Count - notATest} scenarios{(notATest > 0 ? $" · {notATest} not a test" : "")} · next: failures · scenarios");
            return 0;
        }

        writer.Page(scope, options.Offset, options.PageSize(25, writer, "repro"), "failures", scenario =>
        {
            var thrown = FailureText.ThrownAt(scenario.ErrorStackTrace, scenario.SourceFile);
            writer.Line($"{scenario.Address}  {scenario.FeatureName} › {QueryWriter.OneLine(scenario.Name, 80)}  [{scenario.Result}]");
            if (thrown is { } frame)
                writer.Line($"  thrown at {frame.Method} — {frame.File}:{frame.Line}");
            if (FailureText.RerunCommand(thrown?.Method) is { } rerun)
                writer.Line("  " + rerun);
            else
                writer.Line(scenario.ErrorStackTrace is null
                    ? "  no stack trace recorded — nothing here names the test method"
                    : "  no frame in the stack trace names a test method");
            writer.Line();
        }, options.RerunArgs(), ReproRecord,
            "one --filter per failure · FullyQualifiedName~ is a contains match, so a parameterised test re-runs with every row");

        return 0;
    }

    private static object ReproRecord(ScenarioEntry scenario)
    {
        var thrown = FailureText.ThrownAt(scenario.ErrorStackTrace, scenario.SourceFile);
        return new
        {
            address = scenario.Address,
            id = NullIfEmpty(scenario.Id),
            stableId = scenario.StableId,
            feature = scenario.FeatureName,
            scenario = scenario.Name,
            result = scenario.Result,
            thrownAt = thrown is { } frame ? new { method = frame.Method, file = frame.File, line = frame.Line } : null,
            testName = FailureText.TestFilter(thrown?.Method),
            rerun = FailureText.RerunCommand(thrown?.Method)
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
        // The tree prints what each step attached, and never printed the scenario's own: that the scenario holds them is
        // said here, and `attachments` lists them, paged, since a list of them can be longer than a tree (#161). A scoped
        // tree says nothing of the scenario, as before.
        if (scope is null)
        {
            var own = scenario.Attachments.Count;
            var onSteps = scenario.AllSteps().Sum(s => s.Step.Attachments.Count);
            if (own + onSteps > 0)
                writer.Line("attachments: "
                            + string.Join(", ", new[] { own > 0 ? $"{own} of its own" : null, onSteps > 0 ? $"{onSteps} on its steps" : null }.OfType<string>())
                            + $" · attachments {scenario.Address}");
        }
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
                // The detail view: the message whole, as history sN prints its error (#82's rule).
                writer.Line($"{indent}      {QueryWriter.Flat(message)}"
                            + (step.SourceFile is { } file ? $"   at {file}:{step.SourceLine}" : ""));
            if (step.BypassReason is { } bypass)
                writer.Line($"{indent}      bypassed: {QueryWriter.OneLine(bypass, 120)}");
            // The whole tree cuts a long parameter list, and names the tree scoped to the step, which prints it whole (#161).
            var parameters = scope is null
                ? QueryWriter.CutList(step.Parameters, MaxParametersShown, MaxParametersShown)
                : new ListCut<string>(step.Parameters, [], []);
            foreach (var parameter in parameters.Head)
                writer.Line($"{indent}      · {QueryWriter.OneLine(parameter, 100)}");
            if (parameters.Cut.Count > 0)
                writer.Line($"{indent}      " + QueryWriter.CutLine(parameters.Cut.Count, "parameter(s)", null, $"steps {scenario.Address}/{path}"));
            if (step.DocString is { } doc)
                writer.Line($"{indent}      \"\"\"{QueryWriter.OneLine(doc, 100)}\"\"\"");
            // Cut as failures cuts them, scoped or not: a tree cannot page, and sixty filled the byte budget on their own.
            WriteAttachments(writer, index, step.Attachments, $"{indent}      ", $"attachments {scenario.Address}/{path}");
        }

        var unattributed = scope is null && byStep.TryGetValue("", out var loose) ? loose.Length : 0;
        if (unattributed > 0)
            writer.Line($"\n  {unattributed} calls belong to no step"
                        + (index.Enriched ? " (before the first step, or attribution was not trusted)" : " (this report has no step attribution)"));

        writer.Footer($"{scenario.Interactions.Count(i => i.Type == "Request")} calls · interactions {scenario.Address} · flow {scenario.Address}");
        return 0;
    }

    /// <summary>
    /// <c>attachments &lt;report&gt; sN[/P]</c>: every attachment of one scenario, or of one step and its sub-steps, with the
    /// address of what holds it - the view every capped list of attachments names (#161).
    /// </summary>
    /// <remarks>
    /// <para><c>failures</c> printed the first four of a scenario's own attachments and no view printed the rest: <c>steps</c>
    /// prints only what a step attached. A list of attachments is not narrative-sized, so this pages, where <c>steps</c> and
    /// <c>flow</c> hand back a whole tree under the byte budget: an attachment line is 130 to 200 bytes, and 25 to 40 of them
    /// fill a page.</para>
    ///
    /// <para>The order is <c>Failures.jsonl</c>'s, entry for entry: the scenario's own, then each step's in the order
    /// <see cref="ScenarioEntry.AllSteps"/> walks them (background steps, then steps, each followed by its sub-steps), and an
    /// item carries its <c>step</c> as the digest does, null for the scenario's own.</para>
    /// </remarks>
    private static int Attachments(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (!TryScenario(index, options, error, out var scenario, out var scope))
            return 2;

        var rows = new List<(string Address, AttachmentEntry Attachment, string? Step)>();
        if (scope is null)
            rows.AddRange(scenario.Attachments.Select(a => (scenario.Address, a, (string?)null)));
        foreach (var (path, _, step) in scenario.AllSteps().Where(row => scope is null || Address.PathCoveredBy(row.Path, scope)))
            rows.AddRange(step.Attachments.Select(a => ($"{scenario.Address}/{path}", a, (string?)$"{scenario.Address}/{path}")));

        if (options.Count)
        {
            writer.Count(rows.Count);
            return 0;
        }

        if (rows.Count == 0)
        {
            writer.Note(scope is null
                ? $"{scenario.Address} has no attachments, of its own or on its steps"
                : $"{scenario.Address}/{scope} has no attachments, on the step or under it");
            return 0;
        }

        writer.Page(rows, options.Offset, options.PageSize(200, writer, "attachments"), "attachments",
            row => writer.Line($"{row.Address,-6} {AttachmentText(row.Attachment, index)}"),
            options.RerunArgs(),
            row => QueryWriter.WithMember(AttachmentObject(row.Attachment, index), "step", row.Step));
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

        var noticed = false;
        writer.Page(rows, options.Offset, options.PageSize(200, writer, "assertions"), "assertions", row =>
        {
            var mark = row.Step.Failed ? "✗" : "✓";
            writer.Line($"{mark} {row.Scenario.Address}/{row.Path,-5} {QueryWriter.OneLine(row.Step.Text, 100)}");
            if (row.Step.FailureMessage is { } message)
            {
                // A list: both ends kept, and the footer names the view that prints the message whole.
                var cut = writer.Cut(message, 180);
                if (!noticed && cut.Length != QueryWriter.Flat(message).Length)
                {
                    noticed = true;
                    writer.CutNotice($"… marks cut text — steps {row.Scenario.Address} prints the message whole");
                }
                writer.Line($"     {cut}");
            }
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
            Refuse(error, RefusalKinds.Usage, $"Annotations are recorded per scenario, not per step — drop the /{addressedStep} and ask for {scenario.Address}.");
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

        // Which calls are shown is settled before a line is printed. The step headers and annotations were
        // printed as the records went by, before any filter ran, so a filtered view headed every step that
        // made a call, with nothing under it, and a step address listed every step before its own.
        var shown = new List<(InteractionEntry Request, InteractionEntry? Response)>();
        foreach (var interaction in scenario.Interactions)
        {
            if (!interaction.Type.Equals("Request", StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.Step is { } wanted && !Address.PathCoveredBy(interaction.StepPath, wanted))
                continue;
            if (options.Service is { } service && !interaction.ServiceName.Contains(service, StringComparison.OrdinalIgnoreCase))
                continue;

            var response = FindResponse(scenario, interaction);
            if (options.ErrorsOnly && !IsError(response?.StatusCode, response?.StatusText))
                continue;

            shown.Add((interaction, response));
        }

        if (options.Count)
        {
            writer.Count(shown.Count);
            return 0;
        }

        writer.Line($"{scenario.Address}  {QueryWriter.OneLine(scenario.Name, 90)}  [{scenario.Result}]");
        writer.Line();

        // An annotation's index is the number of records before it, so it stands above the first shown call
        // recorded after it, ahead of that call's step header. One whose call a filter dropped moves down to
        // the next call shown, and one recorded after the last shown call - the scenario's last included,
        // whose index is the record count - closes the view.
        var annotations = scenario.Annotations.OrderBy(a => a.Index).ToList();
        var nextAnnotation = 0;
        var stepsByPath = scenario.AllSteps().ToDictionary(s => s.Path, s => s.Step);
        string? currentStep = null;

        // A call is indented under the call it ran inside (CallNesting), one level per shown ancestor, so an
        // ancestor a filter dropped costs no level. Indentation alone says a line's parent is the nearest line
        // above it with less indentation; where that is not the parent - a filtered parent, a parent in an
        // earlier step or above an annotation, two branches whose calls interleave - the line names it. A step
        // header and an annotation are such lines, so each starts a section: only a call line within it counts.
        var parents = CallNesting.Parents(scenario.Interactions);
        var shownOrdinals = shown.Select(s => s.Request.Ordinal).ToHashSet();
        var section = new List<(int Ordinal, int Depth)>();
        var indented = false;

        foreach (var (interaction, response) in shown)
        {
            for (; nextAnnotation < annotations.Count && annotations[nextAnnotation].Index <= interaction.Ordinal; nextAnnotation++)
            {
                writer.Line($"  ── {annotations[nextAnnotation].Text}".TrimEnd());
                section.Clear();
            }

            if (interaction.StepPath != currentStep)
            {
                currentStep = interaction.StepPath;
                section.Clear();
                if (currentStep is not null && stepsByPath.TryGetValue(currentStep, out var step))
                    writer.Line($"── {currentStep}  {QueryWriter.OneLine(step.Display, 90)}".TrimEnd());
            }

            var parent = parents[interaction.Ordinal];
            var depth = 0;
            for (var ancestor = parent; ancestor is { } shownOrNot; ancestor = parents[shownOrNot])
                if (shownOrdinals.Contains(shownOrNot))
                    depth++;

            int? implied = null;
            var nearest = section.FindLastIndex(line => line.Depth < depth);
            if (nearest >= 0 && section[nearest].Depth == depth - 1)
                implied = section[nearest].Ordinal;

            // A request never answered said nothing where the status goes, like an answered call that carried
            // no status. Only a call that carries a pairing id is known to have gone unanswered; a user action
            // is never answered at all.
            var status = response is null && interaction.RequestResponseId is not null && !interaction.IsUserAction
                ? "no response"
                : StatusOf(response).Text;
            var timing = interaction.DurationMs ?? response?.DurationMs;

            // Built from the fields that have something in them: an empty status or duration left its
            // separators behind, at the end of the line or as a double gap before the next field.
            string?[] fields =
            [
                $"{new string(' ', 2 + 2 * depth)}{interaction.Address(scenario),-9} {QueryWriter.OneLine(interaction.CallerName, 40)} → {QueryWriter.OneLine(interaction.ServiceName, 40)}",
                QueryWriter.OneLine(interaction.Summary(), 60),
                status,
                QueryWriter.Duration(timing),
                WarmUpField(index, interaction.WarmUp),
                interaction.BodyHash is { } hash ? $"{hash} {QueryWriter.Size(interaction.BodyLength)}" : null,
                parent is { } named && named != implied ? $"inside {scenario.Address}/i{named}" : null
            ];
            writer.Line(string.Join("  ", fields.Where(field => !string.IsNullOrEmpty(field))).TrimEnd());

            section.Add((interaction.Ordinal, depth));
            indented |= depth > 0;
        }

        if (shown.Count > 0)
        {
            for (; nextAnnotation < annotations.Count; nextAnnotation++)
                writer.Line($"  ── {annotations[nextAnnotation].Text}".TrimEnd());
        }
        else if (scenario.Interactions.Any(i => i.Type.Equals("Request", StringComparison.OrdinalIgnoreCase)))
        {
            writer.Line("  (nothing matched the filters)");
        }
        else
        {
            // Blaming a filter for a scenario that had nothing to filter names a cause that is not there, and
            // a file of the format that could not carry calls says nothing about what the run made.
            writer.Line(ReportScanner.CarriesInteractions(index)
                ? "  (no tracked calls in this scenario)"
                : "  (no calls: a mergeable file written before 3.1.0 carries none)");
        }

        writer.Footer($"{shown.Count} calls shown · http {scenario.Address}/iN --keys for a payload"
                      + (indented ? " · indented calls ran inside the call above them" : ""));
        return 0;
    }

    /// <summary>
    /// flow's field for a call the run's first-call warm-up slowed down, after its duration and before its body hash
    /// (plans/WARM_UP_PLAN.md 4.5), so neither the line's start nor its "inside" ending moves.
    /// </summary>
    private static string? WarmUpField(ReportIndex index, WarmUpEntry? warmUp)
    {
        if (warmUp is null)
            return null;
        var baseline = warmUp.BaselineMs < 1000 ? $"{warmUp.BaselineMs:0.#} ms" : $"{warmUp.BaselineMs / 1000:0.##} s";
        var later = $"later calls {baseline} median ({warmUp.BaselineCalls})";
        if (!warmUp.Waited)
            return $"first {warmUp.Shape} of the run: {later}";
        return FirstCallAddress(index, warmUp) is { } first
            ? $"waited for {first}, the run's first {warmUp.Shape}: {later}"
            : $"waited for the run's first {warmUp.Shape}: {later}";
    }

    /// <summary>For a call that waited, the address of the run's first call of its shape, the call it waited for; null for
    /// the first call itself, or when the report does not hold it.</summary>
    internal static string? FirstCallAddress(ReportIndex index, WarmUpEntry warmUp) =>
        warmUp.Waited && warmUp.First is { } id
            ? index.Scenarios.SelectMany(s => s.Interactions
                    .Where(i => i.RequestResponseId == id && i.Type.Equals("Request", StringComparison.OrdinalIgnoreCase))
                    .Select(i => i.Address(s)))
                .FirstOrDefault()
            : null;

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
            Refuse(error, RefusalKinds.Usage, "Which scenario? Pass an address like s3, sid:<stableId> or id:<test id> — 'scenarios' lists them.");
            return false;
        }

        var text = options.Positional[0];
        if (!Address.TryParse(text, out var address))
        {
            Refuse(error, RefusalKinds.Usage, $"Not an address: {text}");
            return false;
        }

        if (address.Kind == AddressKind.StableId)
            return TryStableId(index, address.StableId!, error, out scenario);

        if (address.Kind == AddressKind.TestId)
            return TryTestId(index, address.TestId!, error, out scenario);

        // A call, diagram or note address was read as its scenario, so `steps s0/i3` answered for all of s0 with
        // nothing said (#184's audit): the wider answer this method exists to refuse.
        if (NotAScenario(address, text) is { } refusal)
        {
            Refuse(error, RefusalKinds.Usage, refusal);
            return false;
        }

        if (index.Scenario(address.Scenario) is not { } found)
        {
            Refuse(error, RefusalKinds.NotFound, index.Scenarios.Count == 0
                ? $"No scenario s{address.Scenario} — the report has none."
                : $"No scenario s{address.Scenario} — the report has {index.Scenarios.Count} (s0-s{index.Scenarios.Count - 1}).");
            return false;
        }

        if (address.Kind == AddressKind.Step)
        {
            var wanted = address.StepPath!;
            if (!found.AllSteps().Any(step => Address.PathCoveredBy(step.Path, wanted)))
            {
                Refuse(error, RefusalKinds.NotFound, $"No step {wanted} in {found.Address} — `steps {found.Address}` lists its paths.");
                return false;
            }

            stepPath = wanted;
        }

        scenario = found;
        return true;
    }

    /// <summary>
    /// Why an address that names something other than a scenario is not one, with the verb that reads what it names;
    /// null for a scenario, a step, a <c>sid:</c> or an <c>id:</c>, which name a scenario.
    /// </summary>
    internal static string? NotAScenario(Address address, string text) => address.Kind switch
    {
        AddressKind.Body => $"{text} is a body address, not a scenario. `body <report> {text}` reads it.",
        AddressKind.Interaction => $"{text} is a call address, not a scenario. `http <report> {text}` reads the call; s{address.Scenario} is its scenario.",
        AddressKind.Diagram => $"{text} is a diagram address, not a scenario. `diagram <report> {text} --out F` writes it; s{address.Scenario} is its scenario.",
        AddressKind.Note => $"{text} is a note address, not a scenario. `note <report> {text}` reads it; s{address.Scenario} is its scenario.",
        _ => null
    };

    /// <summary>
    /// A scenario for a verb that answers for a whole scenario and has no per-step form (<c>services</c>, <c>parties</c>,
    /// <c>values</c>, <c>compare</c>): an ordinal, a <c>sid:</c> or an <c>id:</c>, resolved as every other verb resolves
    /// them. Each used to parse its address itself and take an ordinal only, so the forms the reference documents for
    /// every scenario were refused there, or, in <c>compare</c>, read as an ordinal they were not.
    /// </summary>
    private static bool TryWholeScenario(ReportIndex index, string text, TextWriter error, out ScenarioEntry scenario)
    {
        scenario = null!;
        if (!Address.TryParse(text, out var address))
        {
            Refuse(error, RefusalKinds.Usage, $"Not an address: {text}");
            return false;
        }

        switch (address.Kind)
        {
            case AddressKind.StableId:
                return TryStableId(index, address.StableId!, error, out scenario);
            case AddressKind.TestId:
                return TryTestId(index, address.TestId!, error, out scenario);
            case AddressKind.Step:
                Refuse(error, RefusalKinds.Usage, $"Not a scenario address: {text} (expected s3, sid:<stableId> or id:<test id>). `steps <report> {text}` reads the step.");
                return false;
            case AddressKind.Scenario:
                break;
            default:
                Refuse(error, RefusalKinds.Usage, NotAScenario(address, text));
                return false;
        }

        if (index.Scenario(address.Scenario) is not { } found)
        {
            Refuse(error, RefusalKinds.NotFound, index.Scenarios.Count == 0
                ? $"No scenario s{address.Scenario} — the report has none."
                : $"No scenario s{address.Scenario} — the report has {index.Scenarios.Count} (s0-s{index.Scenarios.Count - 1}).");
            return false;
        }

        scenario = found;
        return true;
    }

    /// <summary>
    /// A scenario by the id its test framework or host gave it (#184), matched exactly. The schema calls it unique within
    /// a run, and every producer keeps it so within one; a merged report is the exception. <c>kronikol merge</c> keeps a
    /// colliding id on the first shard's scenario and renames the later ones to <c>&lt;id&gt;#N</c>, so the id as the
    /// framework minted it names several tests there: it is refused with each of them listed, as is an id a scenario that
    /// ran in two shards keeps twice, and a <c>&lt;id&gt;#N</c> is the address of its own. Cucumber ingest mints
    /// <c>&lt;pickle id&gt;#&lt;attempt&gt;</c> the same way, so the listing says where <c>#N</c> comes from, not which.
    /// </summary>
    private static bool TryTestId(ReportIndex index, string id, TextWriter error, out ScenarioEntry scenario)
    {
        scenario = null!;

        var matches = index.Scenarios.Where(s => string.Equals(s.Id, id, StringComparison.Ordinal)).ToList();
        var numbered = index.Scenarios.Where(s => IsNumberedFrom(s.Id, id)).ToList();
        if (matches.Count == 1 && numbered.Count == 0)
        {
            scenario = matches[0];
            return true;
        }

        if (matches.Count == 0)
        {
            Refuse(error, RefusalKinds.NotFound, $"No scenario with id {id} in {Path.GetFileName(index.Path)}.");
            if (numbered.Count == 0)
            {
                error.WriteLine("`scenarios --json` lists every id in this report.");
                return false;
            }
        }
        else
        {
            Refuse(error, RefusalKinds.Ambiguous, $"{matches.Count + numbered.Count} scenarios answer to id {id} — name the one you mean:");
        }

        // #N is what kronikol merge gives the later of two colliding ids, and Cucumber ingest a scenario's attempt.
        foreach (var match in matches.Concat(numbered))
            error.WriteLine($"  {match.Address}  id:{match.Id}  {QueryWriter.OneLine(match.Name, 70)}  [{match.Result}]");
        if (numbered.Count > 0)
            error.WriteLine($"{id}#N is how kronikol merge renames the later of two colliding ids, and how a Cucumber ingest numbers a scenario's attempts.");
        return false;
    }

    /// <summary>Whether <paramref name="id"/> is <paramref name="original"/> numbered as a merge or an attempt numbers one: <c>original#N</c>.</summary>
    private static bool IsNumberedFrom(string id, string original) =>
        id.Length > original.Length + 1
        && id.StartsWith(original + "#", StringComparison.Ordinal)
        && id.AsSpan(original.Length + 1).IndexOfAnyExceptInRange('0', '9') < 0;

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
                // A report written before stableIds existed cannot answer sid: at all, which is not the same as not holding this one.
                Refuse(error, index.Scenarios.Any(s => s.StableId.Length > 0) ? RefusalKinds.NotFound : RefusalKinds.Unsupported,
                    $"No scenario with stableId {stableId} in {Path.GetFileName(index.Path)}.");
                error.WriteLine(index.Scenarios.Any(s => s.StableId.Length > 0)
                    ? "`scenarios --json` lists every stableId in this report."
                    : "This report carries no stableIds at all (written before 3.0.47) — address scenarios by ordinal.");
                return false;
            case 1:
                scenario = matches[0];
                return true;
            default:
                Refuse(error, RefusalKinds.Ambiguous, $"{matches.Count} scenarios share stableId {stableId} (a repeated row, or a retry) — name the one you mean:");
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

    /// <summary>Whether a call's response says it failed: an error status, or a reason its capturer read.</summary>
    private static bool Failed(InteractionEntry? response) =>
        response is not null && (IsError(response.StatusCode, response.StatusText) || !string.IsNullOrWhiteSpace(response.Error));

    /// <summary>A failed call's status and reason, after its line in a step's list, as Failures.md prints them.</summary>
    private static string Failure(InteractionEntry? response)
    {
        if (!Failed(response))
            return "";
        var (code, text) = StatusOf(response);
        var status = code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? text ?? "failed";
        return string.IsNullOrWhiteSpace(response!.Error) ? $"  → {status}" : $"  → {status} {QueryWriter.OneLine(response.Error, 120)}";
    }
}
