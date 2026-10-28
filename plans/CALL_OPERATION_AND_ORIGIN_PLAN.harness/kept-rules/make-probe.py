"""
Builds a probe that runs ShapeGuardTests' corpus through Kronikol 5.16.4's rules 6 and 7, in the form 5.16.4's trackers
recorded each capture: a capture marked RecordedBefore8 takes the marked method and address, one marked NotRecorded is
not there at all. Its output is compared, line for line, with what the new code computes for rules 6 and 7.

Usage: python make-probe.py Program.cs [new]; without "new", each capture as 5.16.4 recorded it.
"""
import os, re, sys

source = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'tests', 'Kronikol.Tests', 'History', 'ShapeGuardTests.cs')
out = sys.argv[1]
keep = len(sys.argv) > 2 and sys.argv[2] == 'new'
s = open(source, encoding='utf-8').read()

start = s.index('    private static readonly (string Name, RequestResponseLog[] Logs)[] Corpus =')
end = s.index('    ];', start) + len('    ];')
corpus = s[start:end]

helper_start = s.index('    private static IEnumerable<RequestResponseLog> Call(')
helper_end = s.index('    /// <summary>One capture of each form', helper_start)
helper = s[helper_start:helper_end]

# Each entry is one ("name", [.. Call(...)]) tuple; split on the entry starts.
opening = corpus.index('[', corpus.index('Corpus ='))
body = corpus[opening + 1: corpus.rindex(']')]
entries = re.split(r'\n(?=        \(")', '\n' + body.strip('\n'))
entries = [e for e in entries if e.strip()]
converted = []
for e in entries:
    e = e.rstrip().rstrip(',')
    if keep:
        converted.append(e)
        continue
    if 'RecordedBefore.NotRecorded' in e:
        name = re.match(r'\s*\("([^"]+)"', e).group(1)
        converted.append(f'        ("{name}", [])')
        continue
    m = re.search(r'r => r with \{ RecordedBefore8 = new RecordedBefore\((?P<method>"[^"]*"), new Uri\((?P<uri>"[^"]*")\)\) \}', e)
    if m:
        call = re.search(r'Call\("c", (?P<method>"[^"]*"|HttpMethod\.\w+), (?P<uri>"[^"]*")', e)
        assert call, e
        e = e[:call.start('method')] + m.group('method') + e[call.end('method'):call.start('uri')] + m.group('uri') + e[call.end('uri'):]
        m = re.search(r'r => r with \{ RecordedBefore8 = new RecordedBefore\("[^"]*", new Uri\("[^"]*"\)\) \}', e)
        e = e[:m.start()] + 'null' + e[m.end():]
    assert 'RecordedBefore' not in e, e
    converted.append(e)

program = '''using System.Net;
using Kronikol;
using Kronikol.History;
using Kronikol.Tracking;
using DependencyCategories = Kronikol.Constants.DependencyCategories;

var lines = new List<string>();
// Patterns of the kinds a consumer writes: an escaped segment, a table a statement names, words of a path, a key prefix.
var patterns = HistoryShapeRules.Create(
[
    new HistoryShapeTemplate(@"c%20d", "{dir}"),
    new HistoryShapeTemplate(@"caf%C3%A9", "{cafe}"),
    new HistoryShapeTemplate(@"breakfasts", "{table}"),
    new HistoryShapeTemplate(@"\\[Users\\]", "{users}"),
    new HistoryShapeTemplate(@"orders", "{o}"),
    new HistoryShapeTemplate(@"user:\\d+", "user:{id}"),
    new HistoryShapeTemplate(@"sidekick", "{sk}"),
    new HistoryShapeTemplate(@"Trial", "{trial}"),
    new HistoryShapeTemplate(@"INSERT INTO \\[Orders\\]", "INSERT INTO {t}"),
]);
foreach (var version in new[] { 6, 7 })
    foreach (var (label, rules) in new[] { ("none", (HistoryShapeRules?)null), ("patterns", patterns) })
    {
        lines.Add("== " + version + " " + label);
        foreach (var (name, logs) in OldGuard.Corpus)
            lines.Add(name + " :: " + string.Join(" | ", InteractionShape.Calls(logs, rules, version).Select(call => call.ToString())));
    }
File.WriteAllLines(args[0], lines, new System.Text.UTF8Encoding(false));

static class OldGuard
{
''' + helper + '''    public static readonly (string Name, RequestResponseLog[] Logs)[] Corpus =
    [
''' + ',\n'.join(converted) + '''
    ];
}
'''
open(out, 'w', encoding='utf-8', newline='\n').write(program)
print(len(converted), 'entries')
