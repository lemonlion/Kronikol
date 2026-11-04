"""The mutants run on cut 10 (S/rr/prototype/cut10/r1-r2-r3.patch).
n01 to n11 are the coordinator's N1 to N11. A letter is one piece of a listed one, n12 and n13 are pieces of cut 10 no listed mutation touches.
nm* are cut 9's mutations whose result there was not conclusive: a race (X09) ended their escalation early, or only an order moved."""
import mutations as cut9

SC = cut9.SC
PC = cut9.PC
H = cut9.H
UML = cut9.UML
W = cut9.W
BD = cut9.BD
LD = cut9.LD
PATCHED_FILES = cut9.PATCHED_FILES

R5_A = ("rev5", "rev5-a")
R5_BDDFY = ("rev5", "rev5-bddfy")
R5_REQ = ("rev5", "rev5-reqnroll")
R5_LBD = ("rev5", "rev5-lightbdd-x2")
R5_LEGACY = ("rev5", "rev5-lightbdd-legacy")
R3_LEGACY = ("review", "rev3-lightbdd-legacy")
R3_PLAIN = ("review", "rev3-plain")
R3_MORE = ("review", "rev3-more")
R3_COLLAPSE = ("review", "rev3-collapse")
R3_BDDFY = ("review", "rev3-bddfy")
R4_A = ("rev4", "rev4-a")
R4_B = ("rev4", "rev4-b")
R4_BDDFY = ("rev4", "rev4-bddfy")
R4_WEAVE = ("rev4", "rev4-weave")
R4_WEAVE_REL = ("rev4", "rev4-weave@Release")
R4_FSHARP = ("rev4", "rev4-fsharp")
R5_WEAVE = ("rev5", "rev5-weave")
R5_WEAVE_REL = ("rev5", "rev5-weave@Release")
MUT_EXTRA = ("main", "mut-extra")

MUTANTS = {}


def m(mutant_id, desc, edits, extra=(), expect="", **more):
    MUTANTS[mutant_id] = dict(desc=desc, edits=edits, extra=list(extra), expect=expect, **more)


SYNC_RETS = "var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret && body.Instructions.IndexOf(i) >= syncBodyStart).ToList();"
TASK_RETS = "var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret && body.Instructions.IndexOf(i) >= bodyStartAll).ToList();"
NO_FILTER = "var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();"
PHASE_BEFORE = "var phaseBefore = testId is not null && StepCollector.UnderOpenKeyword(testId) ? TestPhaseContext.Current : TestPhase.Unknown;"
SAYS = """            var says = phaseKeyword is not null
                ? PhaseConfiguration.PhaseOfStep(phaseKeyword, flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)
                : Options.WhenTriggersAction ? PhaseConfiguration.PhaseOfAttribute(keyword) : null;
"""
SAYS_CUT9 = """            var says = PhaseConfiguration.PhaseOfStep(phaseKeyword ?? (Options.WhenTriggersAction ? keyword : null),
                flowPhase != TestPhase.Unknown ? flowPhase : state.Phase);
"""
COMPLETED_LOOP = """            completed.ErrorMessage = errorMessage;

            for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)
                if (ReferenceEquals(node.Step, completed))
                {
                    CurrentFlowStep.Value = node.Parent;
                    break;
                }
"""

m("n01", "N1: (frameworkStep || !state.StepsSetPhase) dropped from the decision", [(SC, "if (named && !OutlivedStepSaysPhase(testId) && (frameworkStep || !state.StepsSetPhase))", "if (named && !OutlivedStepSaysPhase(testId))", 1)], extra=[R5_REQ, R5_BDDFY], expect="T1, finding 3: R04 R05 B02 B03 B16")
m("n02", "N2: the ActionStepSeen = false reset dropped", [(SC, "                    if (frameworkStep && !state.StepsSetPhase)\n                        state.ActionStepSeen = false;\n\n", "", 1)], extra=[R5_REQ, R5_BDDFY], expect="T1, finding 3: R04 R05 B02 B03 B16")
m("n03", "N3: superseded dropped in NoteExplicitBoundary", [(SC, "var first = !state.ExplicitBoundaryDrawn && !superseded;", "var first = !state.ExplicitBoundaryDrawn;", 1)], extra=[R5_REQ, R5_BDDFY, R5_LBD], expect="T2, finding 1: R01 R02 R03 B11 L12 L13")
m("n04", "N4: BDDfy's phaseBefore always TestPhaseContext.Current", [(BD, PHASE_BEFORE, "var phaseBefore = TestPhaseContext.Current;", 1)], extra=[R5_BDDFY], expect="T3, finding 2: B08 B15 B16 B17")
m("n05", "N5: BDDfy's phaseBefore always Unknown", [(BD, PHASE_BEFORE, "var phaseBefore = TestPhase.Unknown;", 1)], extra=[R5_BDDFY], expect="T3: B09")
m("n06", "N6: NeedsStepTypeFromHandler ignores ClientsPastBoundary", [(SC, "return !state.StepsSetPhase && !state.ClientsPastBoundary.Contains(client);", "return !state.StepsSetPhase;", 1)], extra=[R5_LEGACY, R3_LEGACY], expect="T4b, finding 4")
m("n06b", "N6 by cut 9's form: NeedsStepTypeFromHandler stops every client at the first boundary", [(SC, "return !state.StepsSetPhase && !state.ClientsPastBoundary.Contains(client);", "return !state.StepsSetPhase && !state.ActionStartDrawn;", 1)], extra=[R5_LEGACY, R3_LEGACY], expect="T4b, finding 4: L01 L07 N02 to N04")
m("n07", "N7: frameworkStep && dropped from CallInSetupStep", [(SC, "(state.Phase == TestPhase.Setup || (frameworkStep && state.CallInSetupStep))", "(state.Phase == TestPhase.Setup || state.CallInSetupStep)", 1)], extra=[R5_LEGACY, R3_LEGACY, R3_MORE], expect="T4c, finding 4: L07")
m("n08", "N8: the weaver's bypass ret filter back, both paths: a bypass goes through its stub's completion", [(W, SYNC_RETS, NO_FILTER, 1), (W, TASK_RETS, NO_FILTER, 1)], extra=[R5_A, R3_COLLAPSE], expect="T6, finding 7: H01 H02 H13 K04, X30 X36 X37")
m("n08a", "N8, the sync path alone", [(W, SYNC_RETS, NO_FILTER, 1)], extra=[R5_A, R3_COLLAPSE], expect="X36")
m("n08b", "N8, the Task path alone", [(W, TASK_RETS, NO_FILTER, 1)], extra=[R5_A, R3_COLLAPSE], expect="X30 X37")
m("n09", "N9: the ReferenceEquals(node.Step, completed) loop dropped", [(SC, COMPLETED_LOOP, "            completed.ErrorMessage = errorMessage;\n", 1)], extra=[R5_REQ], expect="T8, finding 10: R09b")
m("n10", "N10: ChainOf(testId) back in EnterFlow", [(SC, "CurrentFlowStep.Value = new FlowStep(testId, step, CurrentFlowStep.Value);", "CurrentFlowStep.Value = new FlowStep(testId, step, ChainOf(testId));", 1)], extra=[R4_A, R5_A], expect="T9: rev4-a F05, rev5-a H12")
m("n11", "N11: keyword through PhaseOfStep for the public overloads, as in cut 9", [(SC, SAYS, SAYS_CUT9, 1)], extra=[R3_PLAIN, R5_A], expect="rev3-plain G03, rev5-a A01")
m("n12", "BypassStep no longer takes its own node off the chain (no listed mutation touches it)", [(SC, "            if (CurrentFlowStep.Value is { } head && ReferenceEquals(head.Step, bypassed))\n                CurrentFlowStep.Value = head.Parent;\n\n", "", 1)], extra=[R5_A, R3_PLAIN, R3_COLLAPSE], expect="T6: H01 H13 K04, X30 X36")
m("n13", "BDDfy with no test id no longer writes its keyword's phase (no listed mutation touches it)", [(BD, "        if (testId is null)\n            TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(keyword);\n\n", "", 1)], extra=[R5_BDDFY], expect="T3, finding 5: F01")

SKIP_READ = '''        // A getter that throws must find no step open.
        VariableDefinition? skipLocal = null;
        if (skipIf != null)
        {
            skipLocal = new VariableDefinition(method.Module.TypeSystem.Boolean);
            body.Variables.Add(skipLocal);
            if (skipIf.IsField)
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0));
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, method.Module.ImportReference(skipIf.FieldRef!)));
            }
            else
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0));
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method.Module.ImportReference(skipIf.PropertyGetterRef!)));
            }
            preamble.Add(il.Create(OpCodes.Stloc, skipLocal));
        }

'''
SKIP_READ_AFTER = '''            if (skipIf.IsField)
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0));
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, method.Module.ImportReference(skipIf.FieldRef!)));
            }
            else
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0));
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method.Module.ImportReference(skipIf.PropertyGetterRef!)));
            }
'''
m("n14", "T7 undone: the SkipIf member read after StartStep again, on both paths", [
    (W, SKIP_READ, "", 2),
    (W, "            preamble.Add(il.Create(OpCodes.Ldloc, skipLocal!));\n", SKIP_READ_AFTER, 2)], extra=[R5_A, R3_PLAIN], expect="T7, finding 8: rev5-a H05 (Task path), rev3-plain G14 (sync path)")

CHECK_C = ("while (node is not null && (node.TestId != testId || node.Step.Bypassed))", "while (node is not null && node.TestId != testId)")
CHECK_D = ("node.Step.SaysPhase && !node.Step.Bypassed && node.Step.EndTime == 0", "node.Step.SaysPhase && node.Step.EndTime == 0")
CHECK_E = ("node.Step.SaysPhase && !node.Step.Bypassed && node.Step.EndTime != 0", "node.Step.SaysPhase && node.Step.EndTime != 0")
OWN_NODE = ("            if (CurrentFlowStep.Value is { } head && ReferenceEquals(head.Step, bypassed))\n                CurrentFlowStep.Value = head.Parent;\n\n", "")
m("n15ce", "two Bypassed checks dropped together: LeaveCompletedStep's walk and OutlivedStepSaysPhase", [(SC, *CHECK_C, 1), (SC, *CHECK_E, 1)], extra=[MUT_EXTRA], expect="whether they stand or fall together", full=True)
m("n15cde", "all three Bypassed checks dropped together", [(SC, *CHECK_C, 1), (SC, *CHECK_D, 1), (SC, *CHECK_E, 1)], extra=[MUT_EXTRA], expect="whether the plan can lose all three", full=True)
m("n12cde", "BypassStep's own-node removal dropped and all three Bypassed checks dropped: does each stand in for the other", [(SC, *OWN_NODE, 1), (SC, *CHECK_C, 1), (SC, *CHECK_D, 1), (SC, *CHECK_E, 1)], extra=[MUT_EXTRA, R5_A, R3_PLAIN, R3_COLLAPSE], expect="X30 X36 X37, H01 H13 K04")

for old_id in ("m15c", "m15d", "m15e", "m16b", "m20", "m27", "m31b", "m32"):
    source = cut9.MUTANTS[old_id]
    m("n" + old_id, source["desc"] + " (carried from cut 9, inconclusive there)", source["edits"], extra=source["extra"], expect=source["expect"])
MUTANTS["nm27"]["extra"] = [R3_PLAIN, MUT_EXTRA]
MUTANTS["nm27"]["full"] = True
# The BDDfy adapter's own code: nothing but the BDDfy suites can reach it, and the matrix has no baseline on cut 10.
MUTANTS["nm31b"]["extra"] = [R3_BDDFY, R4_BDDFY, R5_BDDFY]
MUTANTS["nm31b"]["families"] = []
MUTANTS["n13"]["extra"] = [R3_BDDFY, R4_BDDFY, R5_BDDFY]
MUTANTS["n13"]["families"] = []
MUTANTS["nm16b"]["extra"] = [R4_WEAVE, R4_WEAVE_REL, R5_WEAVE, R5_WEAVE_REL, R4_FSHARP]
MUTANTS["nm32"]["extra"] = [R4_B]
for old_id in ("nm15c", "nm15d", "nm15e"):
    MUTANTS[old_id]["extra"] = [MUT_EXTRA]
    MUTANTS[old_id]["full"] = True

ORDER = list(MUTANTS)
