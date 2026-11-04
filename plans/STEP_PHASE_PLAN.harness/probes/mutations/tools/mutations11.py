"""The mutants of cut 11's delta (S/rr/prototype/cut11/r1-r2-r3.patch): the coordinator's p01 to p08. p09 and on are mine."""
import mutations as cut9
import mutations10 as cut10

SC = cut9.SC
H = cut9.H
W = cut9.W
BD = cut9.BD
PATCHED_FILES = cut9.PATCHED_FILES

MAIN_EXTRA = ("main", "mut-extra")
R3_PLAIN = ("review", "rev3-plain")
R5_A = ("rev5", "rev5-a")
R5_BDDFY = ("rev5", "rev5-bddfy")
R6_A = ("rev6", "rev6-a")
R6_BDDFY = ("rev6", "rev6-bddfy")
R6_FETCH = ("rev6", "rev6-fetch")
R6_REQ = ("rev6", "rev6-reqnroll")
R6_REQ3 = ("rev6", "rev6-reqnroll-x3")
R6_LBD = ("rev6", "rev6-lightbdd-x2")

MUTANTS = {}


def m(mutant_id, desc, edits, extra=(), expect="", **more):
    MUTANTS[mutant_id] = dict(desc=desc, edits=edits, extra=list(extra), expect=expect, **more)


LOAD = "            preamble.Add(il.Create(OpCodes.Ldloc, ReadSkipMemberWithTheStepOpen(method, il, preamble, skipIf, stepMethods.CompleteStepFailed)));\n"
START_COMMENT = "        // Call StepCollector.StartStep(keyword, text, paramNames, paramValues)\n"
HELPER_TAIL = """        preamble.Add(il.Create(OpCodes.Stloc, skipLocal));
        var after = il.Create(OpCodes.Nop);
        preamble.Add(il.Create(OpCodes.Leave, after));
        var tryStart = preamble[first];
        var catchStart = il.Create(OpCodes.Stloc, exceptionLocal);
        preamble.Add(catchStart);
        preamble.Add(il.Create(OpCodes.Ldc_I4_0));
        preamble.Add(il.Create(OpCodes.Ldloc, exceptionLocal));
        preamble.Add(il.Create(OpCodes.Callvirt, getMessage));
        preamble.Add(il.Create(OpCodes.Call, completeStepFailed));
        preamble.Add(il.Create(OpCodes.Rethrow));
        preamble.Add(after);
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = after,
            CatchType = exceptionType
        });
        return skipLocal;
"""

m("p01", "the getter read before StartStep, as cut 10 had it", [
    (W, LOAD, "            preamble.Add(il.Create(OpCodes.Ldloc, skipLocal!));\n", 2),
    (W, START_COMMENT, cut10.SKIP_READ + START_COMMENT, 2)], extra=[R6_A, R5_A, R3_PLAIN], expect="review6 S01 to S11")
m("p02", "the getter's catch removed: read with the step open and nothing to close it", [
    (W, HELPER_TAIL, "        preamble.Add(il.Create(OpCodes.Stloc, skipLocal));\n        return skipLocal;\n", 1)], extra=[R5_A, R3_PLAIN, R6_A], expect="review5 H05, review G14")
m("p03", "BDDfy's keyword is null && !underKeyword write removed", [
    (BD, "if (testId is null || (keyword is null && !underKeyword))", "if (testId is null)", 1)], extra=[R6_BDDFY, R5_BDDFY], expect="review6 B18, B20 to B23")
m("p04", "Without back to node.Parent", [
    (SC, "CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);", "CurrentFlowStep.Value = node.Parent;", 1)], extra=[R6_A], expect="review6 I02, I03")
m("p05", "the ActionStartDrawn = false at the first framework step removed", [
    (SC, "                        if (!state.ExplicitBoundaryDrawn && !state.ClientDrewBoundary)\n                            state.ActionStartDrawn = false;\n", "", 1)], extra=[R6_REQ, R6_REQ3, R6_BDDFY], expect="review6 R41, B24")
m("p06", "!FlowHolds(boundary) removed", [
    (SC, "(state.BoundaryStep is { } boundary && (boundary.EndTime > 0 || !FlowHolds(boundary)))", "(state.BoundaryStep is { } boundary && boundary.EndTime > 0)", 1)], extra=[R6_REQ, R6_REQ3, R6_LBD, R6_BDDFY], expect="review6 R40, L40, B32")
m("p07", "catch (Exception) back to InvalidOperationException in the handler's fetcher guard", [
    (H, "        catch (Exception)\n        {\n            return;\n        }\n", "        catch (InvalidOperationException)\n        {\n            return;\n        }\n", 1)], extra=[R6_FETCH], expect="review6 F2 in rev6-fetch")
m("p08", "the Action write at ActionStartDrawn removed", [
    (SC, "                TestPhaseContext.Current = TestPhase.Action;\n                return;\n", "                return;\n", 1)], extra=[R6_FETCH], expect="review6 F1 in rev6-fetch")

CHECK_C = cut10.CHECK_C
CHECK_D = cut10.CHECK_D
CHECK_E = cut10.CHECK_E
m("p09", "all three Bypassed checks dropped together, as n15cde on cut 10", [(SC, *CHECK_C, 1), (SC, *CHECK_D, 1), (SC, *CHECK_E, 1)], extra=[MAIN_EXTRA, R3_PLAIN], expect="whether the plan can still lose all three on cut 11", full=True)
m("p10", "OutlivedStepSaysPhase's !Bypassed dropped alone, as nm15e on cut 10", [(SC, *CHECK_E, 1)], extra=[MAIN_EXTRA], expect="X92 X93 X99: a pair with the walk's check", families=[])
m("p11", "BypassStep's own-node removal dropped, as n12 on cut 10", [(SC, *cut10.OWN_NODE, 1)], extra=[MAIN_EXTRA, R3_PLAIN], expect="whether the other way to lose code still holds on cut 11", full=True)

_N10 = cut10.MUTANTS
m("p12", "the plan's three deletions together: TrackingIgnore, OpenStepSaysPhase's !Bypassed, BypassStep's own-node removal", list(_N10["nm27"]["edits"]) + [(SC, *CHECK_D, 1), (SC, *cut10.OWN_NODE, 1)], extra=[MAIN_EXTRA, R3_PLAIN], expect="nothing moves but X89's direct call", full=True)
m("p13", "nm16b on cut 11: every Task stub gets the catch", list(_N10["nm16b"]["edits"]), extra=[MAIN_EXTRA], expect="whether review 6's probes kill it", full=True)
m("p14", "nm20 on cut 11: ActionStartDrawn dropped from the boundary", list(_N10["nm20"]["edits"]), extra=[R5_BDDFY], expect="B17", full=True)
m("p15", "nm32 on cut 11: IsDetached dropped", list(_N10["nm32"]["edits"]), extra=[("rev4", "rev4-b")], expect="G03", full=True)
m("p16", "n08a on cut 11: the sync bypass's ret filter removed", list(_N10["n08a"]["edits"]), extra=[MAIN_EXTRA], expect="X90, X91", full=True)

ORDER = list(MUTANTS)
