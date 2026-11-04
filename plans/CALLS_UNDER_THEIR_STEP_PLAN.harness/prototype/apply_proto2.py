"""Second cut, on top of apply_proto.py: a flow that knows it is outside any step, and calls between steps keep the bar above them."""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, pairs):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        count = s.count(old)
        if count != 1:
            raise SystemExit(f"{path}: expected one match, found {count}:\n{old[:200]}")
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


edit("src/Kronikol/Tracking/StepCollector.cs", [
    ("""            var parent = CurrentOf(state);
            step.Parent = parent;
            step.Ordinal = ++state.LastOrdinal;
""", """            var parent = CurrentOf(state);
            // A top-level step's parent is the test's root, so a flow leaving it knows it is outside every step.
            step.Parent = parent ?? state.Root;
            step.Ordinal = ++state.LastOrdinal;
            if (parent is null)
                state.LastTopLevel = step;
"""),
    ("""        state.Open.Remove(step);
        if (step.Parent is null)
            state.CompletedSteps.Add(step);
""", """        state.Open.Remove(step);
        if (ReferenceEquals(step.Parent, state.Root))
            state.CompletedSteps.Add(step);
"""),
    ("""    /// The step a call or a nested step of this test belongs to: this flow's, or its nearest ancestor still open; with no
    /// such flow (a hook, a service's own thread), the step last opened, as a stack would have it. Call under the lock.
    /// </summary>
    private static CollectedStep? CurrentOf(TestStepState state)
    {
        for (var step = FlowStep.Value; step is not null; step = step.Parent)
            if (ReferenceEquals(step.State, state) && step.IsOpen)
                return step;

        return state.Open.Count > 0 ? state.Open[^1] : null;
    }
""", """    /// The step a call or a nested step of this test belongs to: this flow's, or its nearest ancestor still open; null when
    /// the flow has left every step of the test. With no such flow (a hook, a service's own thread), the step last opened,
    /// as a stack would have it. Call under the lock.
    /// </summary>
    private static CollectedStep? CurrentOf(TestStepState state)
    {
        for (var step = FlowStep.Value; step is not null; step = step.Parent)
        {
            if (!ReferenceEquals(step.State, state))
                break;
            if (ReferenceEquals(step, state.Root))
                return null;
            if (step.IsOpen)
                return step;
        }

        return state.Open.Count > 0 ? state.Open[^1] : null;
    }
"""),
    ("""    /// The step a record logged now was made under, as a number the step keeps when the report is drawn (null: none). A
    /// flow inside one of this test's steps answers for itself, even once that step has ended: it is the step that started
    /// the work. A record from elsewhere, a service's own thread, reads what is open: the deepest step when one chain is
    /// open, and the nearest step they share when several run at once, which is the most that can be said of it.
    /// </summary>
""", """    /// The step a record logged now was made under, as a number the step keeps when the report is drawn (null: none). A
    /// flow inside one of this test's steps answers for itself, even once that step has ended: it is the step that started
    /// the work. A record from elsewhere, a service's own thread, reads what is open: the deepest step when one chain is
    /// open, and the nearest step they share when several run at once, which is the most that can be said of it. With
    /// nothing open it stays with the last top-level step, the bar the diagram draws above it.
    /// </summary>
"""),
    ("""        if (FlowStep.Value is { } flow && ReferenceEquals(flow.State, state))
            return flow.Ordinal;

        lock (state)
        {
            CollectedStep? shared = null;
""", """        if (FlowStep.Value is { } flow && ReferenceEquals(flow.State, state) && !ReferenceEquals(flow, state.Root))
            return flow.Ordinal;

        lock (state)
        {
            if (state.Open.Count == 0 || FlowStep.Value is { } outside && ReferenceEquals(outside, state.Root))
                return state.LastTopLevel?.Ordinal;

            CollectedStep? shared = null;
"""),
    ("""                shared = shared is null ? step : NearestShared(shared, step);
                if (shared is null)
                    return null;
            }
            return shared?.Ordinal;
""", """                shared = shared is null ? step : NearestShared(shared, step);
            }
            return shared is null || ReferenceEquals(shared, state.Root) ? state.LastTopLevel?.Ordinal : shared.Ordinal;
"""),
    ("""        public List<CollectedStep> Open { get; } = new();
        public int LastOrdinal { get; set; }
""", """        public TestStepState() => Root = new CollectedStep { State = this };

        public CollectedStep Root { get; }
        public CollectedStep? LastTopLevel { get; set; }
        public List<CollectedStep> Open { get; } = new();
        public int LastOrdinal { get; set; }
"""),
])
print("applied second cut")
