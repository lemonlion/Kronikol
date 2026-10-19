# S11e's tests: the two new classes, copied from this folder, and the one pin S11e breaks, rewritten.
#   tests/Kronikol.Tests/Reports/ComponentsTabHtmlTests.cs       (core, new)
#   tests/Kronikol.Tests.EndToEnd/ComponentsTabTests.cs          (Playwright, new)
#   tests/Kronikol.Tests/Reports/OnDemandRenderingReportTests.cs (one fact rewritten)
# The rewritten fact said the Server panel's image is the very address ComponentDiagram.html shows. Since S11e the run
# report's drawing leaves the title out (the tab's heading carries it) and ComponentDiagram.html keeps it, so the two
# addresses differ by exactly that line: the fact now reads both sources back out of their addresses and holds them equal
# but for the title line and the blank line after it.
# usage: python tests-s11e.py <worktree root>
import os
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))

copies = {
    'ComponentsTabHtmlTests.cs': 'tests/Kronikol.Tests/Reports/ComponentsTabHtmlTests.cs',
    'ComponentsTabTests.cs': 'tests/Kronikol.Tests.EndToEnd/ComponentsTabTests.cs',
}
for target in copies.values():
    assert not os.path.exists(ROOT + target), ('already there: S11e tests are in already', target)
    assert os.path.isdir(os.path.dirname(ROOT + target)), ('no such folder: is this a worktree root?', target)

ONDEMAND = ROOT + 'tests/Kronikol.Tests/Reports/OnDemandRenderingReportTests.cs'
s = open(ONDEMAND, encoding='utf-8', newline='').read()
old = r'''    public void Under_server_rendering_the_panel_is_the_servers_image_the_standalone_page_shows()
    {
        var html = RunReport(o =>
        {
            o.PlantUmlRendering = PlantUmlRendering.Server;
            o.PlantUmlServerBaseUrl = "https://plantuml.example/plantuml";
            o.PlantUmlImageFormat = PlantUmlImageFormat.Svg;
            o.InternalFlowTracking = false;
        });

        var src = Regex.Match(Panel(html), "<img src=\"([^\"]+)\" alt=\"Component diagram\"").Groups[1].Value;
        Assert.StartsWith("https://plantuml.example/plantuml/svg/", src);
        var standalone = File.ReadAllText(Path.Combine(_dir, "ComponentDiagram.html"));
        Assert.Contains($"<img src=\"{src}\"", standalone);
    }
'''
new = r'''    public void Under_server_rendering_the_panel_is_the_servers_image_of_the_standalone_pages_diagram_without_its_title()
    {
        var html = RunReport(o =>
        {
            o.PlantUmlRendering = PlantUmlRendering.Server;
            o.PlantUmlServerBaseUrl = "https://plantuml.example/plantuml";
            o.PlantUmlImageFormat = PlantUmlImageFormat.Svg;
            o.InternalFlowTracking = false;
        });

        var src = Regex.Match(Panel(html), "<img src=\"([^\"]+)\" alt=\"Component diagram\"").Groups[1].Value;
        Assert.StartsWith("https://plantuml.example/plantuml/svg/", src);
        var standalone = Regex.Match(File.ReadAllText(Path.Combine(_dir, "ComponentDiagram.html")),
            "<img src=\"(https://plantuml\\.example/plantuml/svg/[^\"]+)\"").Groups[1].Value;
        // Since 5.0.0 the Components tab's heading carries the diagram's title, so the panel's drawing leaves it out
        // (plans/OPTION_C_PLAN.md S11, D26): it is the standalone page's diagram, which keeps the title, but for that line.
        var drawn = ServerSource(src);
        var shown = ServerSource(standalone);
        Assert.DoesNotMatch(new Regex("^title ", RegexOptions.Multiline), drawn);
        Assert.Matches(new Regex("^title Component Diagram\r?$", RegexOptions.Multiline), shown);
        Assert.Equal(Regex.Replace(shown, @"^title [^\r\n]*\r?\n\r?\n", "", RegexOptions.Multiline), drawn);
    }

    /// <summary>The PlantUML source a server address carries: the encoder's alphabet read back to bytes
    /// (<see cref="Kronikol.PlantUml.PlantUmlTextEncoder"/>), then inflated.</summary>
    private static string ServerSource(string address)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";
        var digits = address[(address.LastIndexOf('/') + 1)..].Select(c => alphabet.IndexOf(c)).ToArray();
        Assert.DoesNotContain(-1, digits);
        var bytes = new List<byte>();
        for (var i = 0; i + 3 < digits.Length; i += 4)
        {
            bytes.Add((byte)(digits[i] << 2 | digits[i + 1] >> 4));
            bytes.Add((byte)((digits[i + 1] & 0xF) << 4 | digits[i + 2] >> 2));
            bytes.Add((byte)((digits[i + 2] & 0x3) << 6 | digits[i + 3]));
        }
        using var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(bytes.ToArray()), System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(inflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }
'''
assert s.count(old) == 1, ('OnDemandRenderingReportTests', s.count(old))
s = s.replace(old, new)

sources = {}
for name in copies:
    sources[name] = open(os.path.join(HERE, name), encoding='utf-8', newline='').read()

# written only once every check above has held
open(ONDEMAND, 'w', encoding='utf-8', newline='').write(s)
for name, target in copies.items():
    open(ROOT + target, 'w', encoding='utf-8', newline='').write(sources[name])
print('ok: tests/Kronikol.Tests/Reports/OnDemandRenderingReportTests.cs, ' + ', '.join(sorted(copies.values())))
