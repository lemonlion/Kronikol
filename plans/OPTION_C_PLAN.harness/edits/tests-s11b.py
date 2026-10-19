# S11b's test edits: the renderer's flame bar carries its source's palette hue as --fh (the core fact that pinned
# background:hsl(h, 60%, ...) for the same hash), and the Playwright facts are copied in. usage: python tests-s11b.py <root>
import shutil, sys
R = sys.argv[1].rstrip('/') + '/tests/'
P = R + 'Kronikol.Tests/Reports/CultureInvariantOutputTests.cs'
s = open(P, encoding='utf-8', newline='').read()
old = '''            var hue = Math.Abs((long)JavaHash(source)) % 360;
            Assert.Contains($"background:hsl({hue}, 60%, ", html, StringComparison.Ordinal);
'''
new = '''            // one of the flame chart's eight hues, as flame-chart-render-script.js picks it (5.0.0)
            int[] hues = [212, 262, 188, 338, 152, 32, 290, 8];
            var hue = hues[Math.Abs((long)JavaHash(source)) % hues.Length];
            Assert.Contains($"--fh:{hue}\\"", html, StringComparison.Ordinal);
'''
assert s.count(old) == 1
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
here = __file__.replace('\\', '/').rsplit('/', 1)[0]
shutil.copy(here + '/FlameChartPaletteTests.cs', R + 'Kronikol.Tests.EndToEnd/FlameChartPaletteTests.cs')
print('ok')
