# The candidates for R1, in a copy of the prototype's source. Never in proto-r1.
import sys
root = sys.argv[1]


def patch(rel, old, new):
    path = root + '/' + rel
    text = open(path, 'rb').read().decode('utf-8')
    crlf = '\r\n' in text
    if crlf:
        old = old.replace('\n', '\r\n')
        new = new.replace('\n', '\r\n')
    assert text.count(old) == 1, (rel, old, text.count(old))
    open(path, 'wb').write(text.replace(old, new).encode('utf-8'))


# A: the file holds a verb as the run held it.
patch('src/Kronikol/Reports/ReportGenerator.cs', 'HttpMethod verb => verb.ToString().ToUpperInvariant(),', 'HttpMethod verb => verb.Method,')

# B: a fragment carries only what the host said about partial; the fold's line is judged when it is appended.
patch('src/Kronikol/History/HistoryRunContext.cs',
      'public string Fragment() => HistoryFragment.Write(Roster, Run, Generator, Shapes, _rawCalls);',
      'public string Fragment() => HistoryFragment.Write(Roster, Run with { Partial = HostPartial }, Generator, Shapes, _rawCalls);\n\n    internal bool? HostPartial { get; init; }')
text = open(root + '/src/Kronikol/History/HistoryRunContext.cs', 'rb').read().decode('utf-8')
start = text.index('return new HistoryRunContext(location, roster, run, shapes, verdicts, ledger')
end = text.index(';', start)
text = text[:end] + ' { HostPartial = options.HistoryPartialRun }' + text[end:]
open(root + '/src/Kronikol/History/HistoryRunContext.cs', 'wb').write(text.encode('utf-8'))

# C: a space at the end of a uri survives the parse.
patch('src/Kronikol/Reports/Merge/MergeableReportReader.cs',
      'Uri: Uri.TryCreate(GetString(element, "uri"), UriKind.RelativeOrAbsolute, out var uri) ? uri : new Uri("about:blank"),',
      'Uri: Uri.TryCreate(EndSpacesEscaped(GetString(element, "uri")), UriKind.RelativeOrAbsolute, out var uri) ? uri : new Uri("about:blank"),')
patch('src/Kronikol/Reports/Merge/MergeableReportReader.cs',
      '    private static void ReadInteractions(JsonElement se,',
      '''    private static string? EndSpacesEscaped(string? text)
    {
        if (text is null)
            return null;
        var kept = text.TrimEnd(' ');
        return kept.Length == text.Length ? text : kept + string.Concat(Enumerable.Repeat("%20", text.Length - kept.Length));
    }

    private static void ReadInteractions(JsonElement se,''')

# D: what was already supplied is what an EARLIER report supplied; a report's own two rows are two rows.
patch('src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
      'var seen = new HashSet<string>(StringComparer.Ordinal);\n        var shardsThatRan',
      'var seen = new Dictionary<string, int>(StringComparer.Ordinal);\n        var shardsThatRan')
patch('src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
      '''if (!seen.Add(Identity(report.Suite, feature, scenario)))
                    {
                        duplicateIds.Add(scenario.Id);
                        continue;
                    }
''',
      '''var identity = Identity(report.Suite, feature, scenario);
                    if (seen.TryGetValue(identity, out var firstIn) && firstIn != index)
                    {
                        duplicateIds.Add(scenario.Id);
                        continue;
                    }
                    seen[identity] = index;
''')

# E: a result the run never saw stays one.
patch('src/Kronikol/Reports/ReportGenerator.cs',
      '''                if (diagramLookup != null)
                    scenario["diagrams"] = diagramLookup[s.Id].Select(d => payloads is null ? d : payloads.Write(d)).ToArray();
''',
      '''                if (s.ResultDefaulted)
                    scenario["resultDefaulted"] = true;

                if (diagramLookup != null)
                    scenario["diagrams"] = diagramLookup[s.Id].Select(d => payloads is null ? d : payloads.Write(d)).ToArray();
''')
patch('src/Kronikol/Reports/Merge/MergeableReportReader.cs',
      '''        Result = ReadEnum(GetString(se, "result"), ExecutionResult.Passed),
''',
      '''        Result = ReadEnum(GetString(se, "result"), ExecutionResult.Passed),
        ResultDefaulted = se.TryGetProperty("resultDefaulted", out var resultDefaulted) && resultDefaulted.ValueKind == JsonValueKind.True,
''')
print('patched')
