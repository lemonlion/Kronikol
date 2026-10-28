"""plans/SERVICE_CARD_PLAN.md 5.6: apply each mutation alone, build, run the facts, record whether one failed, restore.

Usage: python -I mutate.py <repo> <out-file> [name ...]
A mutation that leaves every fact green SURVIVED: a fixture never reaches its case.
"""
import json
import subprocess
import sys
import time

repo = sys.argv[1]
out = sys.argv[2]
only = set(sys.argv[3:])

CORE = "tests/Kronikol.Tests/Kronikol.Tests.csproj"
CARD = ("FullyQualifiedName~Kronikol.Tests.CallGraph|FullyQualifiedName~CardCommandTests|FullyQualifiedName~InferenceVersionGuardTests"
        "|FullyQualifiedName~QueryCultureTests.Card|FullyQualifiedName~KrklLookupTests|FullyQualifiedName~CallNestingTests"
        "|FullyQualifiedName~FlowTreeTests|FullyQualifiedName~KeptReportCardTests|FullyQualifiedName~GraphPartiesTests")


def broker(name):
    return f"tests/Kronikol.Tests.{name}/Kronikol.Tests.{name}.csproj"


M = [
    ("phase: recorded phase only, no derivation", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     ': call.Cause != "background" && call.Parent is { } parent && calls[parent].EffectivePhase != "unknown" ? (calls[parent].EffectivePhase, true)',
     ': false && call.Parent is { } parent && calls[parent].EffectivePhase != "unknown" ? (calls[parent].EffectivePhase, true)', CORE, CARD),
    ("phase: a background call's phase from its parent", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     ': call.Cause != "background" && call.Parent is { } parent && calls[parent].EffectivePhase != "unknown" ? (calls[parent].EffectivePhase, true)',
     ': call.Parent is { } parent && calls[parent].EffectivePhase != "unknown" ? (calls[parent].EffectivePhase, true)', CORE, CARD),
    ("nesting: trace clause dropped", "src/Kronikol/CallGraph/NestingRule.cs",
     "&& request.TraceId is not null\n                && string.Equals(candidate.TraceId, request.TraceId, StringComparison.Ordinal))",
     "&& false)", CORE, CARD),
    ("sort: culture-sensitive", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     "private static List<string> Sorted(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];",
     "private static List<string> Sorted(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture)];", CORE, CARD),
    ("sort: an edge's operations left unsorted", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     "[.. operations.OrderBy(o => o.Method, StringComparer.Ordinal).ThenBy(o => o.Target, StringComparer.Ordinal).ThenBy(o => o.GraphqlOperation, StringComparer.Ordinal)];",
     "[.. operations];", CORE, CARD),
    ("behaviour: a count written into it", "src/Kronikol/Reports/ServiceCardWriter.cs",
     'writer.WriteString("graphqlOperation", operation.GraphqlOperation);',
     'writer.WriteString("graphqlOperation", operation.GraphqlOperation); writer.WriteNumber("calls", operation.Calls);', CORE, CARD),
    ("scenarios: background calls kept in a scenario's own operations", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     "c.Parent is null && c.Cause != \"background\").Select(c => c.Op);",
     "c.Parent is null).Select(c => c.Op);", CORE, CARD),
    ("scenarios: run.scenarios keeps background calls", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     'var all = own.Where(c => c.Cause != "background").Select(c => c.Op);',
     'var all = own.Select(c => c.Op);', CORE, CARD),
    ("background: the root background block skipped", "src/Kronikol/Reports/ServiceCard.cs",
     "            foreach (var entry in index.BackgroundInteractions)\n                records.Add(Map(entry, null, ContentOf(entry)));",
     "", CORE, CARD),
    ("parties: the block ignored by the tool", "src/Kronikol/CallGraph/GraphParties.cs",
     "if (input.Block is { } block)", "if (input.Block is { } block && false)", CORE, CARD),
    ("parties: the subject first by name", "src/Kronikol/CallGraph/GraphParties.cs",
     "underTest.OrderByDescending(r => received.GetValueOrDefault(r.Name)).ThenBy(r => r.Name, StringComparer.Ordinal)",
     "underTest.OrderBy(r => r.Name, StringComparer.Ordinal)", CORE, CARD),
    ("protocol: the category alone", "src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs",
     'return isEvent ? method ?? "Event" : DependencyCategories.HTTP;', 'return "";', CORE,
     CARD + "|FullyQualifiedName~ComponentDiagram"),
    ("ids: the empty Guid kept", "src/Kronikol/Query/ReportScanner.cs",
     'string.IsNullOrEmpty(id) || id == "00000000-0000-0000-0000-000000000000" ? null : id;',
     'string.IsNullOrEmpty(id) ? null : id;', CORE, CARD),
    ("messages: an unmapped label publishes", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     'MessageLabelKind.Receives => "consumes",\n                    _ => null', 'MessageLabelKind.Receives => "consumes",\n                    _ => "publishes"', CORE, CARD),
    ("messages: the destination from the last segment", "src/Kronikol/CallGraph/MessageTable.cs",
     "_ => segments.FirstOrDefault()", "_ => segments.LastOrDefault()", CORE, CARD),
    ("emptyTargets: keyed on a target of /", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     "if (MessageTable.IsEmptyDestination(request.Uri))", "if (request.Uri.EndsWith('/'))", CORE, CARD),
    ("inference: changed and InferenceVersion left", "src/Kronikol/Parties/PartyResolver.cs",
     "e.Count * 4 >= made.Count", "e.Count * 3 >= made.Count", CORE, CARD),
    ("capture: calls keyed on no call at all", "src/Kronikol/Reports/ServiceCard.cs",
     "callsRecorded: ReportScanner.CarriesInteractions(index)", "callsRecorded: records.Count > 0", CORE, CARD),
    ("hash: over another encoder's bytes", "src/Kronikol/Reports/ServiceCardWriter.cs",
     "var hash = Convert.ToHexStringLower(SHA256.HashData(behaviour));",
     "var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(System.Text.Json.JsonDocument.Parse(behaviour).RootElement, "
     "new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));", CORE, CARD),
    ("scanner: a background body's wrapper not recognised", "src/Kronikol/Query/ReportScanner.cs",
     'if (_interaction is not null && (At("httpInteractions", "#") || InBackgroundCall()) && Key == "content")',
     'if (_interaction is not null && At("httpInteractions", "#") && Key == "content")', CORE, CARD),
    ("delivery: a fault delivered reads consumes", "src/Kronikol/CallGraph/CallGraphBuilder.cs",
     "if (MessageTable.Read(scheme, request.Method) == MessageLabelKind.Neither)\n                    continue;\n                (direction, broker) = (\"consumes\", request.Caller);",
     "(direction, broker) = (\"consumes\", request.Caller);", broker("MassTransit"), "FullyQualifiedName~CardMessageTests"),
    ("kafka row: Subscribe forgotten", "src/Kronikol/CallGraph/MessageTable.cs",
     '["Subscribe", "Unsubscribe", "Commit",', '["Unsubscribe", "Commit",', broker("Kafka"), "FullyQualifiedName~CardMessageTests"),
    ("eventbridge row: PutPartnerEvents forgotten", "src/Kronikol/CallGraph/MessageTable.cs",
     '["PutEvents", "PutPartnerEvents"],', '["PutEvents"],', broker("EventBridge"), "FullyQualifiedName~CardMessageTests"),
    ("storagequeue row: Peek forgotten", "src/Kronikol/CallGraph/MessageTable.cs",
     '["ReceiveMessages", "PeekMessages", "Receive", "Peek"],', '["ReceiveMessages", "PeekMessages", "Receive"],', broker("StorageQueues"), "FullyQualifiedName~CardMessageTests"),
    ("pubsub row: first segment", "src/Kronikol/CallGraph/MessageTable.cs",
     '["pubsub"] = new(Place.LastSegment,', '["pubsub"] = new(Place.FirstSegment,', broker("PubSub"), "FullyQualifiedName~CardMessageTests"),
    ("servicebus row: no placeholder", "src/Kronikol/CallGraph/MessageTable.cs",
     '"GetSessionState", "SetSessionState", "StartProcessing", "StopProcessing"]), ["unknown"]),',
     '"GetSessionState", "SetSessionState", "StartProcessing", "StopProcessing"]), []),', broker("ServiceBus"), "FullyQualifiedName~CardMessageTests"),
    ("http message call: a destination from the URI", "src/Kronikol/CallGraph/MessageTable.cs",
     'if (scheme is "http" or "https")\n            return null;\n', "", broker("SQS"), "FullyQualifiedName~CardMessageTests"),
]


def run(args, timeout):
    p = subprocess.run(args, cwd=repo, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout + p.stderr


results = []
for name, path, old, new, project, filt in M:
    if only and name not in only:
        continue
    full = f"{repo}/{path}"
    with open(full, encoding="utf-8", newline="") as f:
        original = f.read()
    text = original.replace("\r\n", "\n") if "\r\n" not in old else original
    count = text.count(old)
    if count != 1:
        results.append({"mutation": name, "verdict": f"NOT APPLIED ({count} matches)"})
        continue
    mutated = text.replace(old, new, 1)
    if "\r\n" in original and "\r\n" not in old:
        mutated = mutated.replace("\n", "\r\n")
    started = time.time()
    try:
        with open(full, "w", encoding="utf-8", newline="") as f:
            f.write(mutated)
        code, log = run(["dotnet", "build", project, "-c", "Debug", "-v", "q"], 900)
        if code != 0:
            errors = [l for l in log.splitlines() if " error " in l][:3]
            results.append({"mutation": name, "verdict": "DID NOT BUILD", "errors": errors})
            continue
        code, log = run(["dotnet", "test", project, "--no-build", "--filter", filt], 900)
        summary = [l.strip() for l in log.splitlines() if "Passed!" in l or "Failed!" in l]
        failed = [l.strip() for l in log.splitlines() if "[FAIL]" in l][:4]
        killed = code != 0 and any("Failed!" in l for l in summary)
        results.append({"mutation": name, "verdict": "KILLED" if killed else "SURVIVED", "summary": summary, "failed": failed,
                        "seconds": round(time.time() - started)})
    finally:
        with open(full, "w", encoding="utf-8", newline="") as f:
            f.write(original)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=1, ensure_ascii=False)

with open(out, "w", encoding="utf-8") as f:
    json.dump(results, f, indent=1, ensure_ascii=False)
print("\n".join(f"{r['verdict']:>14}  {r['mutation']}" for r in results))
