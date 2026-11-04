"""The mutants, on cut 9 (S/rr/prototype/cut9/r1-r2-r3.patch): each a list of exact edits on the patched source.
An edit that does not match the expected number of times stops the run. Numbers are the coordinator's; 6, 7, 8 and 9 mutated code cut 9 removed.
A letter after a number is one piece of a listed mutation that has several."""

SC = "src/Kronikol/Tracking/StepCollector.cs"
PC = "src/Kronikol/Tracking/PhaseConfiguration.cs"
H = "src/Kronikol/Tracking/TestTrackingMessageHandler.cs"
UML = "src/Kronikol/PlantUml/PlantUmlCreator.cs"
W = "src/Kronikol.StepTracking/StepWeaver.cs"
BD = "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs"
LD = "src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs"
LO = "src/Kronikol.LightBDD.Core/LightBddTestTrackingMessageHandlerOptions.cs"
TDO = "src/Kronikol/TrackingDiagramOverride.cs"

PATCHED_FILES = [
    BD, LO, LD, "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs", W, UML, PC, SC,
    "src/Kronikol/Tracking/StepTrackingOptions.cs", H, TDO,
]

R_PLAIN = ("review", "rev3-plain")
R_REQ = ("review", "rev3-reqnroll")
R_MORE = ("review", "rev3-more")
R_BDDFY = ("review", "rev3-bddfy")
R_COLLAPSE = ("review", "rev3-collapse")
X_XUNIT3 = ("matrix", "xunit3")
OLD_LBD = ("old", "lightbdd")
R4_A = ("rev4", "rev4-a")
R4_B = ("rev4", "rev4-b")
R4_C = ("rev4", "rev4-c")
R4_DIAGRAM = ("rev4", "rev4-diagram")
R4_WEAVE = ("rev4", "rev4-weave")
R4_WEAVE_REL = ("rev4", "rev4-weave@Release")
R4_FSHARP = ("rev4", "rev4-fsharp")

MUTANTS = {}


def m(mutant_id, desc, edits, extra=(), expect=""):
    MUTANTS[mutant_id] = dict(desc=desc, edits=edits, extra=list(extra), expect=expect)


LEAVE_FLOW_RETURN = "return node is not { Step: { Bypassed: true, StartedByStub: true } };"
LEAVE_FLOW_WALK = "while (node is not null && (node.TestId != testId || (node.Step.Bypassed && !node.Step.StartedByStub)))"
STUB_GUARD_HEAD = "if (CurrentFlowStep.Value is { Step: { Bypassed: true, StartedByStub: true } } bypassed)"
STUB_GUARD = """        // A sync stub's success path comes here after its own bypass too, and the bypass has closed the step: this would close the one above it.
        if (CurrentFlowStep.Value is { Step: { Bypassed: true, StartedByStub: true } } bypassed)
        {
            CurrentFlowStep.Value = bypassed.Parent;
            return;
        }

"""
PHASE_ARG = "PhaseConfiguration.PhaseOfStep(phaseKeyword ?? (Options.WhenTriggersAction ? keyword : null),"
STARTS_ACTION_TAIL = "&& !state.ActionStartDrawn && !state.ActionStepSeen;"
IS_ASYNC = 'var isAsyncMethod = method.CustomAttributes.Any(a => a.AttributeType.Name == "AsyncStateMachineAttribute");'
PHASE_TABLE = """        "GIVEN" or "SETUP" or "BUT" => TestPhase.Setup,
        "WHEN" or "THEN" or "BUTWHEN" => TestPhase.Action,
        "AND" => previous,
"""
BOUNDARY_AFTER_BAR = """        // After the step's bar, so the bar closes the Setup partition as the first bar past the setup calls always has.
        if (startsAction)
            DefaultTrackingDiagramOverride.DrawBoundary(testId);
"""
BAR_COMMENT = "        // Emit step delimiter hnote for top-level steps\n"
DECORATOR_TAIL = """            throw;
        }

        // Not in a finally: after the catch above has closed this step, it would close the step above it.
        if (testId is not null && StepCollector.HasActiveStep(testId))
            StepCollector.CompleteStep(testId, passed: true);
"""
DECORATOR_FINALLY = """            throw;
        }
        finally
        {
            if (testId is not null && StepCollector.HasActiveStep(testId))
                StepCollector.CompleteStep(testId, passed: true);
        }
"""
FETCHER_GUARD = """        string? currentStepType;
        try
        {
            currentStepType = _currentStepTypeFetcher();
        }
        catch (InvalidOperationException)
        {
            return;
        }
"""
BDDFY_WRAP = """            if (testId is not null && Configurator.AsyncVoidSupportEnabled && result is Task task)
            {
                // Its own flow leaves it now, and its task closes it.
                closed = true;
                StepCollector.LeaveCompletedStep(testId);
                return CloseWhenDone(testId, task);
            }
"""
TAIL_LOOP = """        foreach (var prefix in body.Instructions.Where(i => i.OpCode == OpCodes.Tail).ToList())
        {
            prefix.OpCode = OpCodes.Nop;
            prefix.Operand = null;
        }
"""
SYNC_COMMENT = "        // A tail call cannot sit in a try, and a handler the method ended in would swallow what is appended.\n"
HANDLER_END_LOOP = """        foreach (var inner in body.ExceptionHandlers.Where(h => h.HandlerEnd == null))
            inner.HandlerEnd = catchStart;
"""
SYNC_TAIL = SYNC_COMMENT + TAIL_LOOP + "\n"
SYNC_HANDLER_END = HANDLER_END_LOOP + "\n        // Append catch block after the body\n"
TASK_TAIL = "        // A tail call hands the frame on, and this frame still has its catch and its end to run.\n" + TAIL_LOOP + "\n"
TASK_HANDLER_END = "        // Cecil reads a null HandlerEnd as the method's end: a handler the method ended in would swallow all that is appended here.\n" + HANDLER_END_LOOP + "\n"
HELD_VARIANT_CHECK = " && !tracesForTest.Any(t => t.SetupVariant is not null || t.ActionVariant is not null)"
HELD_BOUNDARY = """        var heldBoundary = !separateSetup && collapseConsecutiveIdenticalCalls && !tracesForTest.Any(t => t.SetupVariant is not null || t.ActionVariant is not null)
            ? tracesForTest.FindLastIndex(t => t.IsActionStart) : -1;
"""

m("m01", "OpenStepSaysPhase dropped", [(SC, "if (step.SaysPhase && !OpenStepSaysPhase(testId))", "if (step.SaysPhase)", 1)], expect="X01")
m("m02", "LeaveFlow dropped from each CompleteStepAsync", [(SC, "return LeaveFlow() ? CompleteWhenDoneAsync(task) : task;", "return CompleteWhenDoneAsync(task);", 2)], expect="X09 X10 X21")
m("m03a", "LeaveCompletedStep dropped from CompleteStep", [(SC, "        LeaveCompletedStep(testId);\n\n        if (!States.TryGetValue(testId, out var state))\n            return;\n\n        CollectedStep? completed;", "        if (!States.TryGetValue(testId, out var state))\n            return;\n\n        CollectedStep? completed;", 1)], extra=[R_PLAIN], expect="review B01")
m("m03b", "LeaveCompletedStep dropped from BDDfy's wrap", [(BD, "                StepCollector.LeaveCompletedStep(testId);\n", "", 1)], extra=[R_BDDFY], expect="B04")
m("m04", "OutlivedStepSaysPhase dropped", [(SC, "if (named && !OutlivedStepSaysPhase(testId))", "if (named)", 1)], extra=[R_REQ, R_PLAIN], expect="review Q05 C01")
m("m05", "FrameworkNamesThisFlow dropped: named always true", [(SC, "var named = phaseKeyword is not null || FrameworkNamesThisFlow();", "var named = true;", 1)], extra=[X_XUNIT3], expect="matrix T01")
m("m10a", "ToUpperInvariant dropped in PhaseOfStep, table as it is", [(PC, "keyword?.ToUpperInvariant() switch", "keyword switch", 1)], expect="LightBDD")
m("m10b", "ToUpperInvariant dropped in PhaseOfStep, table re-cased to the attributes' words, so only a differently cased keyword misses", [
    (PC, "keyword?.ToUpperInvariant() switch", "keyword switch", 1),
    (PC, PHASE_TABLE, '        "Given" or "Setup" or "But" => TestPhase.Setup,\n        "When" or "Then" or "ButWhen" => TestPhase.Action,\n        "And" => previous,\n', 1)], expect="LightBDD")
m("m11", '"SETUP" dropped from PhaseOfStep', [(PC, '"GIVEN" or "SETUP" or "BUT" => TestPhase.Setup,', '"GIVEN" or "BUT" => TestPhase.Setup,', 1)], expect="L13")
m("m12", "Unknown dropped from SaysPhase", [(SC, "step.SaysPhase = says is not (null or TestPhase.Unknown);", "step.SaysPhase = says is not null;", 1)], expect="L15")
m("m13", "flowPhase dropped, always state.Phase", [
    (SC, "        var flowPhase = TestPhaseContext.Current;\n", "", 1),
    (SC, "flowPhase != TestPhase.Unknown ? flowPhase : state.Phase);", "state.Phase);", 1)], extra=[R_PLAIN], expect="review G04")
m("m14", "StartedByStub dropped from the guards: any bypassed step counts as its stub's", [
    (SC, STUB_GUARD_HEAD, "if (CurrentFlowStep.Value is { Step: { Bypassed: true } } bypassed)", 1),
    (SC, LEAVE_FLOW_WALK, "while (node is not null && node.TestId != testId)", 1),
    (SC, LEAVE_FLOW_RETURN, "return node is not { Step: { Bypassed: true } };", 1)], extra=[R_PLAIN], expect="review A01")
m("m15a", "bypass check dropped: CompleteStep(bool) guard for a sync stub", [(SC, STUB_GUARD, "", 1)], expect="X30 X36")
m("m15b", "bypass check dropped: LeaveFlow always answers true, so a Task stub completes after its own bypass", [(SC, LEAVE_FLOW_RETURN, "return true;", 1)], expect="X30 X37")
m("m15c", "bypass check dropped: LeaveCompletedStep's walk no longer skips a bypassed step", [(SC, "while (node is not null && (node.TestId != testId || node.Step.Bypassed))", "while (node is not null && node.TestId != testId)", 1)], expect="X30 X36 X37")
m("m15d", "bypass check dropped: OpenStepSaysPhase's !Bypassed", [(SC, "node.Step.SaysPhase && !node.Step.Bypassed && node.Step.EndTime == 0", "node.Step.SaysPhase && node.Step.EndTime == 0", 1)], expect="X30 X36 X37 (redundant by reading: a bypass sets EndTime)")
m("m15e", "bypass check dropped: OutlivedStepSaysPhase's !Bypassed", [(SC, "node.Step.SaysPhase && !node.Step.Bypassed && node.Step.EndTime != 0", "node.Step.SaysPhase && node.Step.EndTime != 0", 1)], expect="X30 X36 X37")
m("m15f", "bypass check dropped: LeaveFlow's walk no longer skips a step bypassed by hand", [(SC, LEAVE_FLOW_WALK, "while (node is not null && node.TestId != testId)", 1)], extra=[R_PLAIN], expect="review A02")
m("m16a", "the weaver's catch dropped: every Task stub as if async", [(W, IS_ASYNC, "var isAsyncMethod = true;", 1)], expect="X25 X33")
m("m16b", "the weaver's async test dropped: every Task stub gets the catch", [(W, IS_ASYNC, "var isAsyncMethod = false;", 1)], expect="an async method's IL")
m("m16c", "the handler's real end dropped on the Task path", [(W, TASK_HANDLER_END, "", 1)], expect="X32")
m("m16d", "the Task path's tail loop dropped", [(W, TASK_TAIL, "", 1)], extra=[R4_FSHARP], expect="rev4-fsharp task-tail")
m("m17", "CallInSetupStep dropped from the boundary", [(SC, "(state.Phase == TestPhase.Setup || state.CallInSetupStep)", "state.Phase == TestPhase.Setup", 1)], expect="L16")
m("m18", "phaseKeyword ?? dropped: a framework's word is not used", [(SC, PHASE_ARG, "PhaseConfiguration.PhaseOfStep(Options.WhenTriggersAction ? keyword : null,", 1)], expect="R06")
m("m19", "WhenTriggersAction gate dropped", [(SC, PHASE_ARG, "PhaseConfiguration.PhaseOfStep(phaseKeyword ?? keyword,", 1)], expect="X20 R21")
m("m20", "state.ActionStartDrawn dropped from the boundary", [(SC, STARTS_ACTION_TAIL, "&& !state.ActionStepSeen;", 1)], expect="X19 X07 X08")
m("m21", "ActionStepSeen dropped from the boundary", [(SC, STARTS_ACTION_TAIL, "&& !state.ActionStartDrawn;", 1)], extra=[R_PLAIN], expect="review E05")
m("m22", "frameworkStep dropped: every step sets StepsSetPhase", [(SC, "state.StepsSetPhase |= frameworkStep;", "state.StepsSetPhase = true;", 1)], extra=[R_MORE], expect="review H21")
m("m23", "the handler's state back to per instance (four lines: a field, an argument, a parameter, the state's key)", [
    (H, "    private bool _listenerStarted;\n", "    private bool _listenerStarted;\n    private readonly string _perInstanceState = Guid.NewGuid().ToString();\n", 1),
    (H, "StepCollector.NoteCallInStepType(currentTestInfo.Id, currentStepType);", "StepCollector.NoteCallInStepType(currentTestInfo.Id, currentStepType, _perInstanceState);", 1),
    (SC, "internal static void NoteCallInStepType(string testId, string stepType)", "internal static void NoteCallInStepType(string testId, string stepType, string stateKey)", 1),
    (SC, '&& !stepType.StartsWith("BUTWHEN", StringComparison.OrdinalIgnoreCase));\n\n        var state = States.GetOrAdd(testId, _ => new TestStepState());', '&& !stepType.StartsWith("BUTWHEN", StringComparison.OrdinalIgnoreCase));\n\n        var state = States.GetOrAdd(stateKey, _ => new TestStepState());', 1)], expect="R11 L07 B07")
m("m24", "bar and boundary swapped", [
    (SC, BOUNDARY_AFTER_BAR, "", 1),
    (SC, BAR_COMMENT, "        if (startsAction)\n            DefaultTrackingDiagramOverride.DrawBoundary(testId);\n\n" + BAR_COMMENT, 1)], expect="R-suite step bars")
m("m25", "FindLastIndex back to FindIndex", [(UML, "var actionStartIndex = tracesForTest.FindLastIndex(t => t.IsActionStart);", "var actionStartIndex = tracesForTest.FindIndex(t => t.IsActionStart);", 1)], extra=[R_PLAIN], expect="review E04")
m("m26", "the held boundary dropped", [(UML, HELD_BOUNDARY, "        var heldBoundary = -1;\n", 1)], extra=[R_COLLAPSE], expect="review K01")
m("m27", "TrackingIgnore dropped from hasSetupTraces and lastSetupTraceIndex", [
    (UML, ".Any(t => !t.IsOverrideStart && !t.IsOverrideEnd && !t.IsActionStart && !t.TrackingIgnore);", ".Any(t => !t.IsOverrideStart && !t.IsOverrideEnd && !t.IsActionStart);", 1),
    (UML, "if (!t.IsOverrideStart && !t.IsOverrideEnd && !t.IsActionStart && !t.TrackingIgnore)", "if (!t.IsOverrideStart && !t.IsOverrideEnd && !t.IsActionStart)", 1)], extra=[R_PLAIN], expect="review E90")
m("m28", "the LightBDD decorator's finally back", [(LD, DECORATOR_TAIL, DECORATOR_FINALLY, 1)], expect="L14")
m("m29", "BDDfy's closed dropped from the finally", [(BD, "if (!closed && testId is not null && StepCollector.HasActiveStep(testId))", "if (testId is not null && StepCollector.HasActiveStep(testId))", 1)], extra=[R_BDDFY], expect="review Z15")
m("m29a", "BDDfy's closed: the catch's write dropped", [(BD, "            // The finally would close the step above it.\n            closed = true;\n", "", 1)], extra=[R_BDDFY], expect="review Z15")
m("m29b", "BDDfy's closed: the wrap's write dropped", [(BD, "                // Its own flow leaves it now, and its task closes it.\n                closed = true;\n", "", 1)], extra=[R_BDDFY], expect="B04")
m("m30", "the handler's guard of the fetcher dropped", [(H, FETCHER_GUARD, "        var currentStepType = _currentStepTypeFetcher();\n", 1)], extra=[OLD_LBD], expect="F16 outside-a-scenario")
m("m31a", "R3's wrap dropped", [(BD, BDDFY_WRAP, "", 1)], expect="B04")
m("m31b", "R3's AsyncVoidSupportEnabled gate dropped", [(BD, "if (testId is not null && Configurator.AsyncVoidSupportEnabled && result is Task task)", "if (testId is not null && result is Task task)", 1)], expect="B11")
m("m32", "the IsDetached gate dropped from FrameworkNamesThisFlow", [(SC, "        // A detached flow inherits the framework's context and is nobody's test, as TestInfoResolver reads it.\n        if (TestIdentityScope.IsDetached)\n            return false;\n\n", "", 1)], extra=[R4_B], expect="rev4-b G03 H01")
m("m33", "NoteExplicitBoundary's answer ignored: every explicit StartAction logs", [(TDO, "        if (StepCollector.NoteExplicitBoundary(testId))\n            LogBoundary(testId);\n", "        StepCollector.NoteExplicitBoundary(testId);\n        LogBoundary(testId);\n", 1)], extra=[R4_A], expect="rev4-a M01")
m("m34", "the variant check dropped from heldBoundary", [(UML, HELD_VARIANT_CHECK, "", 1)], extra=[R4_C, R4_DIAGRAM], expect="rev4-c C02, rev4-diagram D1 D2 D3")
m("m35", "lastTrace back to tracesForTest[^1]", [(UML, "var lastTrace = tracesForTest.Last(t => !t.IsActionStart);", "var lastTrace = tracesForTest[^1];", 1)], extra=[R4_C], expect="rev4-c C04")
m("m36", "the weaver's new sync-path loops dropped, both", [(W, SYNC_TAIL, "", 1), (W, SYNC_HANDLER_END, "        // Append catch block after the body\n", 1)], extra=[R4_WEAVE, R4_WEAVE_REL, R4_FSHARP], expect="rev4-weave S02")
m("m36a", "the sync path's tail loop dropped", [(W, SYNC_TAIL, "", 1)], extra=[R4_WEAVE, R4_WEAVE_REL, R4_FSHARP], expect="rev4-fsharp unit-tail")
m("m36b", "the sync path's HandlerEnd loop dropped", [(W, SYNC_HANDLER_END, "        // Append catch block after the body\n", 1)], extra=[R4_WEAVE, R4_WEAVE_REL, R4_FSHARP], expect="rev4-weave S02")

m("m37", "BDDfy's restore of the phase dropped from the finally", [(BD, "            TestPhaseContext.Current = phaseBefore;\n", "", 1)], extra=[("rev4", "rev4-bddfy")], expect="rev4-bddfy B01 B02")

ORDER = list(MUTANTS)
