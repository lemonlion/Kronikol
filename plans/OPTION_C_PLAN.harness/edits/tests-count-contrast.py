# v5 fix found by S13's preparation: More filters' count is white on the accent, and the dark accent is a light blue,
# so in either dark block the count reads at about 2.4:1. The fact resolves the count's ink and ground through the
# token sheet's three blocks and requires AA. usage: python tests-count-contrast.py <root>
import sys

p = sys.argv[1].rstrip('/') + '/tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs'
raw = open(p, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
s = raw.decode('utf-8-sig')
assert 'The_more_filters_count_reads_at_AA_in_every_theme' not in s, 'in already'
a = '''    [GeneratedRegex(@"(min|max)-width:\\s*([\\d.]+)px")]
    private static partial Regex WidthBound();
'''
b = '''    /// <summary>More filters' count is text on the accent: in every block of the token sheet (light, the dark one the
    /// switch sets, and the dark one the system's setting gives a page with no pick) its ink and its ground, resolved
    /// through the tokens as the cascade does, read at AA (4.5:1). The dark accent is a light blue, so white on it
    /// read at 2.4:1.</summary>
    [Theory]
    [InlineData(":root[data-theme=\\"dark\\"]", null)]
    [InlineData(":root:not([data-theme])", "(prefers-color-scheme: dark)")]
    [InlineData(":root", null)]
    public void The_more_filters_count_reads_at_AA_in_every_theme(string block, string? media)
    {
        var tokens = Stylesheets.ReportTokensStyleSheet;
        string Resolve(string? value)
        {
            var m = TokenReference().Match(value ?? "");
            Assert.True(m.Success, $"'{value}' is not one token");
            return CssRules.Value(tokens, block, m.Groups[1].Value, media) ?? CssRules.Value(tokens, ":root", m.Groups[1].Value)
                ?? throw new InvalidOperationException(m.Groups[1].Value + " is not in the token sheet");
        }
        var ink = Resolve(CssRules.Value(Base, ".more-filters-count", "color"));
        var ground = Resolve(CssRules.Value(Base, ".more-filters-count", "background"));

        var ratio = Contrast(ink, ground);
        Assert.True(ratio >= 4.5, $"{block}: the count is {ink} on {ground}, {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
    }

    // WCAG 2.2's contrast ratio of two #rrggbb colours
    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int i)
            {
                var c = Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [GeneratedRegex(@"^var\\((--[\\w-]+)\\)$")]
    private static partial Regex TokenReference();
'''
assert s.count(a) == 1, 'WidthBound anchor'
s = s.replace(a, a + '\n' + b)
open(p, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)
print('ok: StylesheetRulesTests.cs')
