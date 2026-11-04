"""A flow that has left its steps says so, on a copy with apply_end_state.py applied.

Usage: apply_left_marker.py <worktree>

A new step's flow holding nothing was two things: a caller whose step has moved on, which passes over a step left running, and a thread that never had
any of this test's steps, which should go under what is running now, as a stack had it. They read the same, so a [Step] on a thread with no context
became a top-level row with a bar of its own, on a sequential test. Moving up out of a top-level step now leaves a marker in the flow, in place of nothing.
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

PAIRS = [
    # Every place the flow moves up.
    ("""        if (ReferenceEquals(FlowStep.Value, frame))
            FlowStep.Value = frame.Parent;
""",
     """        if (ReferenceEquals(FlowStep.Value, frame))
            FlowStep.Value = frame.Parent ?? state.OutOfItsSteps;
"""),
    ("""        if (ReferenceEquals(FlowStep.Value, completed))
            FlowStep.Value = completed.Parent;
""",
     """        if (ReferenceEquals(FlowStep.Value, completed))
            FlowStep.Value = completed.Parent ?? state.OutOfItsSteps;
"""),
    ("""        if (ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = bypassed.Parent;
""",
     """        if (ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = bypassed.Parent ?? state.OutOfItsSteps;
"""),
    ("""        step.Left = true;
        FlowStep.Value = step.Parent;
        return step;
""",
     """        step.Left = true;
        FlowStep.Value = step.Parent ?? state.OutOfItsSteps;
        return step;
"""),
    # The decision.
    ("""    /// <summary>
    /// The parent of a step starting now. A flow inside an open step: that step. A flow whose steps have all ended is a straggler, and
    /// goes under whatever is running now, as a stack had it. A flow that was never in one, or has left them, passes over a step left
    /// running. Call under the lock.
    /// </summary>
    private static CollectedStep? ParentOfNewStep(TestStepState state)
    {
        if (NearestOpenInFlow(state) is { } own)
            return own;

        var straggler = FlowStep.Value is { } flow && ReferenceEquals(flow.State, state);
        return straggler ? (state.Open.Count > 0 ? state.Open[^1] : null) : LastOpenInItsCaller(state);
    }
""",
     """    /// <summary>
    /// The parent of a step starting now. A flow inside an open step: that step. A flow that has left its steps passes over a step left
    /// running. A flow whose steps have all ended is a straggler, and goes under whatever is running now, as a stack had it. A flow that was
    /// never in one of this test's, a thread with no context for one, knows nothing and does the same, passing over a step left running
    /// only when something else is open. Call under the lock.
    /// </summary>
    private static CollectedStep? ParentOfNewStep(TestStepState state)
    {
        if (NearestOpenInFlow(state) is { } own)
            return own;

        var running = state.Open.Count > 0 ? state.Open[^1] : null;
        if (FlowStep.Value is { } flow && ReferenceEquals(flow.State, state))
            return ReferenceEquals(flow, state.OutOfItsSteps) ? LastOpenInItsCaller(state) : running;

        return LastOpenInItsCaller(state) ?? running;
    }
"""),
    # The marker: closed from the start, so it is never an open step, and it belongs to the test.
    ("""    private class TestStepState
    {
""",
     """    private class TestStepState
    {
        public TestStepState() => OutOfItsSteps = new CollectedStep { State = this, EndTime = 1 };

        /// <summary>What a flow holds once it has moved up out of a top-level step: closed from the start, with no parent.</summary>
        public CollectedStep OutOfItsSteps { get; }

"""),
]
for old, new in PAIRS:
    assert s.count(old) == 1, old[:80]
    s = s.replace(old, new)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("the marker applied")
