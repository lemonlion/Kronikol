"""R2 red proof: compile stubs in the red worktree (at v4.12.3, R2's tests copied in). Each stub is the member R2's
tests name, with nothing reading or writing it, so a fact fails on what R1 does, not on a missing symbol.
- ScenarioStep.IsAssertion, InteractionRecord.SourceFile/SourceLine, WeaveResult.Unwoven: declared, never set or read.
- StackAnalysisTests: the slot-typing fact reads the StackAnalysis class itself (R2's new code), so it is removed
  with its helpers; the two crafted-branch facts stay.
- WovenIl: RED_NO_IL_NET=1 turns the IL net off in Weave, so a run can show each fact's own reason; the net's own
  facts call NewErrors directly and still judge."""
import io, re

RED = 'C:/Code/Kronikol-shouldly-red/'

def edit(path, pairs):
    full = RED + path
    t = io.open(full, encoding='utf-8', newline='').read()
    for old, new in pairs:
        if isinstance(old, re.Pattern):
            t, n = old.subn(new, t)
            assert n == 1, (path, old.pattern[:80], n)
        else:
            assert t.count(old) == 1, (path, old[:80], t.count(old))
            t = t.replace(old, new)
    io.open(full, 'w', encoding='utf-8', newline='').write(t)
    print('stubbed', path)

edit('src/Kronikol/Reports/ScenarioStep.cs', [
    ('    public string? Keyword { get; set; }',
     '    public string? Keyword { get; set; }\n    public bool IsAssertion { get; set; } // RED STUB: never set or read'),
])
edit('src/Kronikol/Ingestion/InteractionRecord.cs', [
    ('    [JsonPropertyName("message")] public string? Message { get; init; }',
     '    [JsonPropertyName("message")] public string? Message { get; init; }\n'
     '    [JsonIgnore] public string? SourceFile { get; init; } // RED STUB: not read from the feed, not drawn\n'
     '    [JsonIgnore] public int? SourceLine { get; init; } // RED STUB'),
])
edit('src/Kronikol.AssertionTracking/AssertionWeaver.cs', [
    ('    public List<string> DiagMessages { get; set; } = new List<string>();\n}',
     '    public List<string> DiagMessages { get; set; } = new List<string>();\n'
     '    public List<string> Unwoven { get; set; } = new List<string>(); // RED STUB: never filled\n}'),
])
edit('tests/Kronikol.Tests.AssertionTracking/StackAnalysisTests.cs', [
    (re.compile(r'    private const string Source = """.*?(?=    private static string Fixture\()', re.S),
     '    // RED: the slot-typing fact and its helpers read StackAnalysis, which R1 does not have.\n\n'),
])
edit('tests/Kronikol.Tests.AssertionTracking/WovenIl.cs', [
    ('        AssertNoNewErrors(before, assemblyPath);\n        return result;',
     '        if (Environment.GetEnvironmentVariable("RED_NO_IL_NET") != "1") // RED: off for the own-reason pass\n'
     '            AssertNoNewErrors(before, assemblyPath);\n        return result;'),
])
