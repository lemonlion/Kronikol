"""S2's mutation check (plan §6.7): break one clause of the rule or of the verb at a time, rebuild, run
`FlowTests` and `CallNestingTests`, and report which facts failed. A guard fact that passed on the release
before S2 (a pin, or a fact that must not print something) proves itself here: the breakage it guards
against must fail it. Every file is restored after each mutation.

    python s2_mutations.py [M1 M4 ...]      # from anywhere; the repository is two directories up
"""
import pathlib, re, subprocess, sys

WT = str(pathlib.Path(__file__).resolve().parents[2])
NESTING = str(pathlib.Path(WT, "src", "Kronikol.Tool", "Query", "CallNesting.cs"))
FLOW = str(pathlib.Path(WT, "src", "Kronikol.Tool", "QueryCommand.Narrative.cs"))

MUTATIONS = [
    ("M1 same-caller exclusion dropped", NESTING,
     """            if (!string.Equals(candidate.CallerName, request.CallerName, StringComparison.Ordinal)
                && request.TraceId is not null""",
     """            if (request.TraceId is not null""",
     {"CallNestingTests.Two_calls_from_one_caller_are_never_parent_and_child"}),
    ("M2 every request with an id opens", NESTING,
     "if (entry.RequestResponseId is { } id && answeredAt.TryGetValue(id, out var answer) && answer > i)",
     "if (entry.RequestResponseId is { } id)",
     {"CallNestingTests.A_request_never_answered_is_never_a_parent", "FlowTests.A_request_never_answered_holds_no_calls"}),
    ("M3 a request without an id opens, closed by proximity", NESTING,
     """                if (entry.RequestResponseId is { } id && answeredAt.TryGetValue(id, out var answer) && answer > i)
                    open.Add(i);
            }
            else if (IsResponse(entry) && entry.RequestResponseId is { } id)""",
     """                if (entry.RequestResponseId is null || (answeredAt.TryGetValue(entry.RequestResponseId, out var answer) && answer > i))
                    open.Add(i);
            }
            else if (IsResponse(entry) && entry.RequestResponseId is null)
            {
                var k = open.FindLastIndex(o => interactions[o].RequestResponseId is null && interactions[o].ServiceName == entry.ServiceName);
                if (k >= 0) open.RemoveAt(k);
            }
            else if (IsResponse(entry) && entry.RequestResponseId is { } id)""",
     {"CallNestingTests.A_request_without_a_requestResponseId_is_never_a_parent", "FlowTests.A_report_without_requestResponseIds_prints_flat"}),
    ("M4 the plain rule R1", NESTING,
     """    private static int? InnermostParent(IReadOnlyList<InteractionEntry> interactions, List<int> open, InteractionEntry request)
    {""",
     """    private static int? InnermostParent(IReadOnlyList<InteractionEntry> interactions, List<int> open, InteractionEntry request)
    {
        if (open.Count > 0) return open[^1];""",
     {"CallNestingTests.A_call_from_another_caller_on_another_trace_stays_top_level",
      "CallNestingTests.Calls_a_service_makes_at_once_are_siblings_whatever_order_they_answer_in"}),
    ("M5 open when answered anywhere (the prototype)", NESTING,
     "answer > i)", "answer >= 0)",
     {"CallNestingTests.A_request_answered_before_it_was_recorded_is_never_a_parent"}),
    ("M6 a response closes only the innermost", NESTING,
     "open.RemoveAll(o => interactions[o].RequestResponseId == id);",
     "if (open.Count > 0 && interactions[open[^1]].RequestResponseId == id) open.RemoveAt(open.Count - 1);",
     {"CallNestingTests.A_response_closes_its_request_wherever_it_sits"}),
    ("M7a no response without an id", FLOW,
     "var status = response is null && interaction.RequestResponseId is not null && !interaction.IsUserAction",
     "var status = response is null && !interaction.IsUserAction",
     {"FlowTests.A_request_without_a_pairing_id_is_not_said_to_have_had_no_response"}),
    ("M7b no response for a user action", FLOW,
     "var status = response is null && interaction.RequestResponseId is not null && !interaction.IsUserAction",
     "var status = response is null && interaction.RequestResponseId is not null",
     {"FlowTests.A_user_action_is_not_said_to_have_had_no_response"}),
    ("M8 no response for an answered call without a status", FLOW,
     "var status = response is null && interaction.RequestResponseId is not null && !interaction.IsUserAction",
     "var status = (response is null && interaction.RequestResponseId is not null && !interaction.IsUserAction) || (response is not null && StatusOf(response).Text is null)",
     {"FlowTests.A_call_answered_without_a_status_is_not_said_to_have_had_no_response"}),
    ("M9 implied parent = nearest line exactly one level up (the prototype)", FLOW,
     "var nearest = section.FindLastIndex(line => line.Depth < depth);",
     "var nearest = section.FindLastIndex(line => line.Depth == depth - 1);",
     {"FlowTests.A_line_below_a_shallower_line_that_is_not_its_parent_names_it"}),
    ("M10 depth counts ancestors a filter dropped", FLOW,
     """                if (shownOrdinals.Contains(shownOrNot))
                    depth++;""",
     """                depth++;""",
     {"FlowTests.A_line_whose_parent_is_filtered_out_names_it", "FlowTests.Errors_only_shows_a_failure_with_the_failure_inside_it"}),
    ("M11 legend always", FLOW,
     '(indented ? " · indented calls ran inside the call above them" : "")',
     '" · indented calls ran inside the call above them"',
     {"FlowTests.The_legend_is_printed_only_when_a_line_is_indented"}),
]


def run(cmd):
    return subprocess.run(cmd, cwd=WT, capture_output=True, text=True, encoding="utf-8", errors="replace", shell=True)


only = set(sys.argv[1:])
for name, path, old, new, expected in MUTATIONS:
    if only and name.split()[0] not in only:
        continue
    original = open(path, encoding="utf-8", newline="").read()
    assert original.count(old) == 1, (name, original.count(old))
    try:
        open(path, "w", encoding="utf-8", newline="").write(original.replace(old, new))
        build = run("dotnet build tests/Kronikol.Tests -c Debug -v q -nologo")
        if " error " in build.stdout or build.returncode != 0:
            print(f"{name}: BUILD FAILED\n{build.stdout[-1500:]}")
            continue
        test = run('dotnet test tests/Kronikol.Tests --no-build --filter "FullyQualifiedName~Kronikol.Tests.Tool.FlowTests|FullyQualifiedName~Kronikol.Tests.Tool.CallNestingTests" -- RunConfiguration.TreatNoTestsAsError=true')
        failed = set(re.findall(r"^\s+Failed Kronikol\.Tests\.Tool\.(\S+?)(?:\(.*)? \[", test.stdout, re.M))
        missing = {e for e in expected if e not in failed}
        verdict = "CAUGHT" if not missing else f"NOT CAUGHT: {sorted(missing)}"
        print(f"{name}: {verdict}; failed {len(failed)}: {sorted(failed)}")
    finally:
        open(path, "w", encoding="utf-8", newline="").write(original)
print("restored")
