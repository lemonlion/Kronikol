"""The third review's two faults that were this plan's own, on top of apply_left_marker.py.

Usage: apply_bypass_and_no_shared.py <worktree>

A woven bypass still passes through its stub's completion, which closed the next step up or a running sibling. The flow now stays on the bypassed step until that completion comes through and takes it. A record from elsewhere while steps run at once with nothing shared named the last top-level step: it now reads no step, and the trace join can still place it.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

PAIRS = [
    ("""    public static void CompleteStep(bool passed, string? errorMessage = null)
    {
        CompleteStep(ResolveTestId(), passed, errorMessage, fromTheStepsFlow: true);
    }
""",
     """    public static void CompleteStep(bool passed, string? errorMessage = null)
    {
        if (LeftABypass())
            return;

        CompleteStep(ResolveTestId(), passed, errorMessage, fromTheStepsFlow: true);
    }
"""),
    ("""    public static void BypassStep(string? reason = null)
    {
        BypassStep(ResolveTestId(), reason);
    }
""",
     """    public static void BypassStep(string? reason = null)
    {
        // The stub's completion comes through next, with nothing to close, so the flow stays on this step until it does (LeftABypass).
        CloseBypassed(ResolveTestId(), reason);
    }
"""),
    ("""    public static void BypassStep(string? testId, string? reason = null)
    {
        if (testId is null)
            return;

        if (!States.TryGetValue(testId, out var state))
            return;

        CollectedStep? bypassed;
        lock (state)
        {
            bypassed = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            if (bypassed is null)
                return;

            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;
            Complete(state, bypassed, passed: false, errorMessage: null);
        }

        if (ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = bypassed.Parent ?? state.OutOfItsSteps;
    }
""",
     """    public static void BypassStep(string? testId, string? reason = null)
    {
        if (CloseBypassed(testId, reason) is { } bypassed && ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = bypassed.Parent ?? bypassed.State!.OutOfItsSteps;
    }

    private static CollectedStep? CloseBypassed(string? testId, string? reason)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
        {
            var bypassed = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            if (bypassed is null)
                return null;

            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;
            Complete(state, bypassed, passed: false, errorMessage: null);
            return bypassed;
        }
    }

    // The woven stub of a bypassed step, on its way out: the step has closed already, and a completion would close the one above it or one running beside it.
    private static bool LeftABypass()
    {
        if (FlowStep.Value is not { Bypassed: true } bypassed)
            return false;

        FlowStep.Value = bypassed.Parent ?? bypassed.State!.OutOfItsSteps;
        return true;
    }
"""),
    ("""    public static Task CompleteStepAsync(Task task) => FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
""",
     """    public static Task CompleteStepAsync(Task task) => LeftABypass() || FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
"""),
    ("""    public static Task<T> CompleteStepAsync<T>(Task<T> task) => FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
""",
     """    public static Task<T> CompleteStepAsync<T>(Task<T> task) => LeftABypass() || FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());
"""),
    ("""    /// flow inside an open step of this test answers for itself. A record from elsewhere, a service's own thread or a loop whose
    /// step has ended, reads what is open: the deepest step when one chain is open, and the nearest step they share when several run
    /// at once, which is the most that can be said of it. With nothing open it stays with the last top-level step, the bar above it.
""",
     """    /// flow inside an open step of this test answers for itself. A record from elsewhere, a service's own thread or a loop whose
    /// step has ended, reads what is open: the deepest step when one chain is open, and the nearest step they share when several run
    /// at once, which is the most that can be said of it. With several running and nothing shared it reads no step (0 is no step's
    /// id). With nothing open it stays with the last top-level step, the bar above it.
"""),
    ("""            return (shared ?? state.LastTopLevel)?.Ordinal;
""",
     """            if (any)
                return shared?.Ordinal ?? 0;

            return state.LastTopLevel?.Ordinal;
"""),
]
for old, new in PAIRS:
    assert s.count(old) == 1, old[:90]
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("a bypass is taken by its stub's completion; nothing shared reads no step")
