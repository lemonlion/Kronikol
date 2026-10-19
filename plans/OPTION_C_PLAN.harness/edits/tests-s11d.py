# S11d's test edits (D27): the two new classes copied in (Playwright TimelineTabTests, core TimelineControlsTests), and
# the pins the change moves: the legend's bypassed colour (WarmUpHtmlTests), the one data-duration-ms of a merged report,
# which its timeline row now carries too (MergeReadsDurationsTests), and WarmUpMarkTests' report, opened to
# TimelineTabTests as WarmUpMarkTests.WriteOrders. Run it after scripts-s11d.py's product edits are in.
# usage: python tests-s11d.py <worktree root>
import os
import shutil
import sys

T = sys.argv[1].rstrip('/\\') + '/tests/'
HERE = os.path.dirname(os.path.abspath(__file__))

# All or nothing, as scripts-s11d.py: the files are written once every edit has been checked.
files = {}


def edit(path, old, new, count=1):
    if path not in files:
        files[path] = open(T + path, encoding='utf-8', newline='').read()
    n = files[path].count(old)
    assert n == count, (path, n, old[:80])
    files[path] = files[path].replace(old, new)


# 1. The legend names the bypassed bar's colour as 5.0.0 paints it.
edit('Kronikol.Tests/Reports/WarmUpHtmlTests.cs', '''        // Skipped bars are painted grey and bypassed ones orange (stylesheets.css .timeline-bar-skipped, -bypassed).
        Assert.Contains("green = passed, red = failed, grey = skipped, orange = bypassed.", info);
''', '''        // Skipped bars are painted grey and bypassed ones blue (stylesheets.css .timeline-bar-skipped, -bypassed): since
        // 5.0.0 on --kron-dot and --kron-bypass, as the run's ring draws them, where 4.x drew a bypassed bar orange.
        Assert.Contains("green = passed, red = failed, grey = skipped, blue = bypassed.", info);
        Assert.DoesNotContain("orange", info);
''')

# 2. A merged report's one timed scenario: its element, and since 5.0.0 its timeline row too.
edit('Kronikol.Tests/Reports/Merge/MergeReadsDurationsTests.cs', '''        Assert.Single(Regex.Matches(html, "data-duration-ms=\\""));
''', '''        // the timed scenario's element, and since 5.0.0 its row in the timeline (plans/OPTION_C_PLAN.md S11, D27)
        Assert.Single(Regex.Matches(html, "<details class=\\"scenario[^>]*data-duration-ms=\\""));
        Assert.Single(Regex.Matches(html, "<div class=\\"timeline-row[^>]*data-duration-ms=\\""));
''')

# 3. WarmUpMarkTests' report for TimelineTabTests: Generate keeps its callers, WriteOrders takes the folders.
W = 'Kronikol.Tests.EndToEnd/WarmUpMarkTests.cs'
edit(W, '''    private string Generate(string fileName, bool includeTestRunData = true, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null)
    {
        var logs = new List<RequestResponseLog>();
        Call(logs, "place", 0, 1500);
''', '''    private string Generate(string fileName, bool includeTestRunData = true, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null) =>
        WriteOrders(TempDir, OutputDir, fileName, includeTestRunData, diagrams);

    /// <summary>The same report for another class's facts (TimelineTabTests, 5.0.0): written in <paramref name="tempDir"/>,
    /// copied to <paramref name="outputDir"/>.</summary>
    internal static string WriteOrders(string tempDir, string outputDir, string fileName, bool includeTestRunData = true,
        DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null)
    {
        var logs = new List<RequestResponseLog>();
        Call(logs, "place", 0, 1500);
''')
edit(W, '''            Path.Combine(TempDir, fileName), "Warm-up", includeTestRunData,
''', '''            Path.Combine(tempDir, fileName), "Warm-up", includeTestRunData,
''')
edit(W, '''        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static Scenario Scenario(''', '''        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static Scenario Scenario(''')

# 4. The new classes.
copies = [(os.path.join(HERE, name), T + folder + '/' + name)
          for name, folder in [('TimelineTabTests.cs', 'Kronikol.Tests.EndToEnd'), ('TimelineControlsTests.cs', 'Kronikol.Tests/Reports')]]
for source, target in copies:
    assert os.path.exists(source), ('missing beside this script', source)
    assert not os.path.exists(target), ('already there', target)

for path, content in files.items():
    open(T + path, 'w', encoding='utf-8', newline='').write(content)
for source, target in copies:
    shutil.copyfile(source, target)
print('ok:', ', '.join(sorted(files) + [t[len(T):] for _, t in copies]))
