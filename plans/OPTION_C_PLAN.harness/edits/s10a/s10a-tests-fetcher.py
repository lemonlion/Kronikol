# S10a (the tests): the fetcher sets the system font only where a browser engine draws (P17), and the "not Mermaid"
# fact reads Mermaid's header line, now that the PlantUML source's style names its diagram type too.
# usage: python s10a-tests-fetcher.py <worktree root>
import sys
ROOT = sys.argv[1].rstrip('/') + '/'
P = ROOT + 'tests/Kronikol.Tests/PlantUmlBrowser/PlantUmlBrowserDiagramFetcherTests.cs'
s = open(P, encoding='utf-8', newline='').read()
old = '''        Assert.DoesNotContain("sequenceDiagram", diagram.CodeBehind);
    }
'''
new = '''        // PlantUML, not Mermaid: Mermaid opens with a line holding only its diagram type. From 5.0.0 the PlantUML
        // source's style names the type too ("sequenceDiagram { note { ... } }", plans/OPTION_C_PLAN.md S10).
        Assert.StartsWith("@startuml", diagram.CodeBehind.TrimStart());
        Assert.DoesNotMatch(@"(?m)^\\s*sequenceDiagram\\s*$", diagram.CodeBehind);
    }

    // The engine measures and paints the same face only when it is the system's own, and the JVM knows no system-ui
    // (5.0.0, plans/OPTION_C_PLAN.md S10, P17).
    [Theory]
    [InlineData(PlantUmlRendering.BrowserJs, true)]
    [InlineData(PlantUmlRendering.Server, false)]
    public void The_system_font_is_set_where_a_browser_engine_draws(PlantUmlRendering rendering, bool systemFont)
    {
        var testId = SeedLog();
        var fetcher = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions
        {
            DiagramFormat = DiagramFormat.PlantUml, PlantUmlRendering = rendering
        });
        var diagram = fetcher().Single(d => d.TestRuntimeId == testId);

        Assert.Equal(systemFont, diagram.CodeBehind.Contains("skinparam defaultFontName system-ui", StringComparison.Ordinal));
    }
'''
assert s.count(old) == 1, s.count(old)
s = s.replace(old, new)
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
