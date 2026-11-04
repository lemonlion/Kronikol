"""Builds the two prototypes as one: this plan's final cut on #223's abf7b0a3, in a worktree where `git apply --3way fifth-cut.patch` has just run.

Usage: apply_one.py <worktree>

#223 keeps its pairing, its frames, its walk and its names (CollectedId, OpenStep). This plan keeps the flow, and takes over how a stamp is read.
"""
import pathlib
import subprocess
import sys

root = pathlib.Path(sys.argv[1])


def git(*args):
    subprocess.run(["git", "-C", str(root), *args], check=True)


# #223's side of each file where the two only named the same thing differently.
for f in [
    "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs",
    "src/Kronikol/Reports/BackgroundStepsDetector.cs",
    "src/Kronikol/Reports/ScenarioStep.cs",
    "src/Kronikol/Reports/ReportGenerator.cs",
    "src/Kronikol/Tracking/RequestResponseLogger.cs",
]:
    git("checkout", "--ours", "--", f)
# This plan's side of the collector: it is the part being merged, below.
git("checkout", "--theirs", "--", "src/Kronikol/Tracking/StepCollector.cs")
# Where the patch applied cleanly but only added its own name for the id.
for f in [
    "src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs",
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    "src/Kronikol/Tracking/RequestResponseLog.cs",
]:
    git("checkout", "abf7b0a3", "--", f)


def edit(path, pairs):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


# The whole path, which is R3's one line on #223's map.
edit("src/Kronikol/Reports/ReportGenerator.cs", [(
    """            foreach (var sub in step.SubSteps ?? [])
                Add(sub, path);
""",
    """            var index = 0;
            foreach (var sub in step.SubSteps ?? [])
                Add(sub, $"{path}.{index++}");
""")])

edit("src/Kronikol/Tracking/StepCollector.cs", [
    # A step and a frame open the same way, apart from who may be the parent.
    ("""    public static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString)
    {
        if (testId is null)
            return;
""",
     """    public static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString)
        => OpenStep(testId, keyword, text, paramNames, paramValues, table, docString);

    /// <summary>
    /// Starts a step that a framework's adapter opens for a step of the framework's own, and returns the id the adapter
    /// closes it by (<see cref="CompleteFrame"/>) and finds it by (<see cref="ScenarioStep.CollectedId"/>).
    /// </summary>
    internal static int? StartFrame(string? testId, string? keyword, string text, string[][]? table = null, string? docString = null)
        => OpenStep(testId, keyword, text, null, null, table, docString, isFrame: true);

    /// <summary>Closes the frame <paramref name="frameId"/>, by id. A step still running under it finishes by itself.</summary>
    internal static void CompleteFrame(string? testId, int? frameId, bool passed, string? errorMessage = null)
    {
        if (testId is null || frameId is null || !States.TryGetValue(testId, out var state))
            return;

        CollectedStep? frame;
        lock (state)
        {
            frame = state.Open.FirstOrDefault(s => s.Ordinal == frameId);
            if (frame is null)
                return;

            Complete(state, frame, passed, errorMessage);
        }

        if (ReferenceEquals(FlowStep.Value, frame))
            FlowStep.Value = frame.Parent;
    }

    /// <summary>The step a call logged now happened under.</summary>
    internal static int? OpenStepOf(string? testId) => OrdinalOfOpenStep(testId);

    /// <summary>The framework step open for this test now, if any: a step that did not run while one is open is that step's.</summary>
    internal static int? OpenFrameOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
            return LastOpenFrame(state)?.Ordinal;
    }

    /// <summary>The id of the step this test opened last, 0 if none; a step that did not run is placed after it.</summary>
    internal static int LastStepIdOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return 0;

        lock (state)
            return state.LastStepId;
    }

    private static CollectedStep? LastOpenFrame(TestStepState state) => state.Open.LastOrDefault(s => s.IsFrame);

    private static int? OpenStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString, bool isFrame = false)
    {
        if (testId is null)
            return null;
"""),
    # A framework step goes under the framework step open now, or none, and never under a step method. #223's rule: the flow cannot
    # replace it, since Reqnroll carries a sync binding's context on to the next, so a step opened by hand in a sync hook would hold every row under it.
    ("""            var parent = ParentOfNewStep(state);
            step.Parent = parent;
            step.Ordinal = Interlocked.Increment(ref _lastOrdinal);
""",
     """            var parent = isFrame ? LastOpenFrame(state) : ParentOfNewStep(state);
            step.Parent = parent;
            step.IsFrame = isFrame;
            step.Ordinal = Interlocked.Increment(ref _lastOrdinal);
            state.LastStepId = step.Ordinal;
"""),
    ("""                case "ButWhen":
                    TestPhaseContext.Current = TestPhase.Action;
                    break;
            }
        }
    }
""",
     """                case "ButWhen":
                    TestPhaseContext.Current = TestPhase.Action;
                    break;
            }
        }

        return step.Ordinal;
    }
"""),
    # Only CompleteFrame closes a frame.
    ("""            completed = fromTheStepsFlow ? NearestOpenInFlow(state) ?? LastOpenInItsCaller(state) : LastOpenInItsCaller(state);
            if (completed is null)
                return;
""",
     """            completed = fromTheStepsFlow ? NearestOpenInFlow(state) ?? LastOpenInItsCaller(state) : LastOpenInItsCaller(state);
            if (completed is null || completed.IsFrame)
                completed = state.Open.LastOrDefault(s => !s.IsFrame);
            if (completed is null)
                return;
"""),
    ("            StepOrdinal = step.Ordinal > 0 ? step.Ordinal : null,\n",
     "            CollectedId = step.Ordinal > 0 ? step.Ordinal : null,\n"),
    ("""        public CollectedStep? LastTopLevel { get; set; }
""",
     """        public CollectedStep? LastTopLevel { get; set; }
        public int LastStepId { get; set; }
"""),
    ("""        public int Ordinal { get; set; }
""",
     """        public int Ordinal { get; set; }
        public bool IsFrame { get; set; }
"""),
])

git("add", "-A", "src")
print("built as one")
