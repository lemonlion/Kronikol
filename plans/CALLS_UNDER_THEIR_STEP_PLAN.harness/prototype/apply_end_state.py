"""What the two prototypes built as one still lacked of what the plan describes, on a copy of them.

Usage: apply_end_state.py <worktree>

- F9: a response takes its request's path.
- A call a service makes while the call it serves is still waiting takes that call's path, by the Kronikol trace the two share, where its
  own stamp could only read what was open (the limit §10 had stated).
- The readers §6.5 lists that the prototype stopped short of: failures --json and query.py.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, pairs):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        assert s.count(old) == 1, (path, s.count(old), old[:80])
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


edit("src/Kronikol/Tracking/RequestResponseLog.cs", [(
    """    internal int? OpenStep { get; init; }
""",
    """    internal int? OpenStep { get; init; }

    /// <summary>
    /// The flow that logged this was inside <see cref="OpenStep"/>. False: the record came from elsewhere, a service's own thread for one, and
    /// its step was read from what was open then.
    /// </summary>
    internal bool OpenStepIsItsFlows { get; init; }
""")])

edit("src/Kronikol/Tracking/RequestResponseLogger.cs", [(
    """        if (log.OpenStep is null && StepCollector.OpenStepOf(log.TestId) is { } openStep)
            log = log with { OpenStep = openStep };
""",
    """        if (log.OpenStep is null && StepCollector.OpenStepOf(log.TestId, out var isItsFlows) is { } openStep)
            log = log with { OpenStep = openStep, OpenStepIsItsFlows = isItsFlows };
""")])

edit("src/Kronikol/Tracking/StepCollector.cs", [(
    """    internal static int? OpenStepOf(string? testId) => OrdinalOfOpenStep(testId);
""",
    """    internal static int? OpenStepOf(string? testId, out bool isItsFlows)
    {
        isItsFlows = false;
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
            if (NearestOpenInFlow(state) is { } own)
            {
                isItsFlows = true;
                return own.Ordinal;
            }

        return OrdinalOfOpenStep(testId);
    }
""")])

# Only CompleteFrame closes a frame. A bypass runs in the stub's frame and moves the flow to the step's parent, and the stub's own ret then reaches
# CompleteStepAsync, so a skipped async sub-step directly under a frame would close the frame and turn what follows into top-level rows.
edit("src/Kronikol/Tracking/StepCollector.cs", [
    ("""    public static Task CompleteStepAsync(Task task) => AwaitStep(task, LeaveFlowStep());
""",
     """    public static Task CompleteStepAsync(Task task) => FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
"""),
    ("""    public static Task<T> CompleteStepAsync<T>(Task<T> task) => AwaitStep(task, LeaveFlowStep());
""",
     """    public static Task<T> CompleteStepAsync<T>(Task<T> task) => FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
"""),
    ("""    // Not async, on purpose: the caller's flow must leave the step before the caller carries on, so that the next
""",
     """    // A woven stub's own step would be on top of it, so this stub has none to complete: it was bypassed, or never opened.
    private static bool FlowIsInAFrame() => FlowStep.Value is { IsFrame: true, IsOpen: true };

    // Not async, on purpose: the caller's flow must leave the step before the caller carries on, so that the next
"""),
])

edit("src/Kronikol/Reports/ReportGenerator.cs", [
    ("""            stepPaths[perTest.Key] = paths;
            annotations[perTest.Key] = found;
""",
     """            ThroughTheirCalls(perTest.Where(l => !l.IsDiagramMarker).ToArray(), paths);
            stepPaths[perTest.Key] = paths;
            annotations[perTest.Key] = found;
"""),
    ("""    /// <summary>
    /// Every step of a scenario in the order its marker will arrive, paired with the address it gets in the
""",
     """    /// <summary>
    /// What a record's own stamp cannot say, read from the call it belongs to. A response takes its request's path, so a call in flight
    /// across a step is not split. A request logged from no flow of the test's takes the path of the call it ran inside, where it knows
    /// which that is: the innermost request still waiting for its answer on the same Kronikol trace, from another party. That is what
    /// a service's onward call is, and the trace is carried by header (TestTrackingMessageHandler), so two calls at once are told apart.
    /// A call whose request has been answered is no longer inside it, and stays with what was open when it was logged.
    /// </summary>
    private static void ThroughTheirCalls(RequestResponseLog[] records, List<string?> paths)
    {
        var answered = new HashSet<Guid>();
        foreach (var record in records)
            if (record.Type == RequestResponseType.Response)
                answered.Add(record.RequestResponseId);

        var requestAt = new Dictionary<Guid, int>();
        var waiting = new List<int>();
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Type == RequestResponseType.Request)
            {
                if (record.OpenStep is not null && !record.OpenStepIsItsFlows)
                    for (var k = waiting.Count - 1; k >= 0; k--)
                    {
                        var candidate = records[waiting[k]];
                        if (candidate.TraceId != record.TraceId || string.Equals(candidate.CallerName, record.CallerName, StringComparison.Ordinal))
                            continue;
                        if (paths[waiting[k]] is { } theirs)
                            paths[i] = theirs;
                        break;
                    }

                requestAt.TryAdd(record.RequestResponseId, i);
                if (answered.Contains(record.RequestResponseId))
                    waiting.Add(i);
            }
            else
            {
                if (requestAt.TryGetValue(record.RequestResponseId, out var request))
                    paths[i] = paths[request];
                waiting.RemoveAll(w => records[w].RequestResponseId == record.RequestResponseId);
            }
        }
    }

    /// <summary>
    /// Every step of a scenario in the order its marker will arrive, paired with the address it gets in the
"""),
])

edit("src/Kronikol/Query/QueryCommand.Narrative.cs", [
    ("""        var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
        var thrown = FailureText.ThrownAt(scenario.ErrorStackTrace, scenario.SourceFile);
""",
     """        var failingSteps = scenario.AllSteps().Where(s => s.Step.Failed).ToArray();
        var thrown = FailureText.ThrownAt(scenario.ErrorStackTrace, scenario.SourceFile);
        // As the text lists them: each call under the deepest failing step that covers it.
        bool InFailingStep(InteractionEntry i, string failing) =>
            Address.PathCoveredBy(i.StepPath, failing)
            && !failingSteps.Any(deeper => deeper.Path != failing
                                           && Address.PathCoveredBy(deeper.Path, failing)
                                           && Address.PathCoveredBy(i.StepPath, deeper.Path));
"""),
    ("""                calls = scenario.Interactions
                    .Where(i => i.StepPath == s.Path && i.Type == "Request")
""",
     """                calls = scenario.Interactions
                    .Where(i => InFailingStep(i, s.Path) && i.Type == "Request")
"""),
    ("""                callsTotal = scenario.Interactions.Count(i => i.StepPath == s.Path && i.Type == "Request"),
""",
     """                callsTotal = scenario.Interactions.Count(i => InFailingStep(i, s.Path) && i.Type == "Request"),
"""),
])

for copy in ["templates/skills/kronikol-test-debugging/scripts/query.py", ".claude/skills/kronikol-test-debugging/scripts/query.py"]:
    edit(copy, [(
        """        calls = by_step.get(path, [])
""",
        """        calls = sorted(i for key, found in by_step.items() if key is not None and covered_by(key, path) for i in found)
""")])

print("end state applied")
