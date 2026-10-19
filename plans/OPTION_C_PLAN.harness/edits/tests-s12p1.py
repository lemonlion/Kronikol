# S12 phone, part 1: one fact holding the phone breakpoint to one value in every sheet and every script's matchMedia
# (the scripts were unchecked: StylesheetRulesTests read only the sheets' min-width bounds).
# usage: python tests-s12p1.py <root>
import sys

ROOT = sys.argv[1].rstrip('/') + '/'
P = ROOT + 'tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs'
s = open(P, encoding='utf-8-sig', newline='').read()
bom = open(P, 'rb').read(3) == b'\xef\xbb\xbf'
assert 'The_phone_breakpoint_is_one_in_every_sheet_and_every_script' not in s, 'in already'
a = '''    [GeneratedRegex(@"min-width:\\s*([\\d.]+)px")]
    private static partial Regex MinWidth();
'''
b = a + '''
    /// <summary>The phone layout has one breakpoint (5.0.0, plans/OPTION_C_PLAN.md S12): every width query near it, in
    /// every sheet and in every embedded script's <c>matchMedia</c>, is <c>max-width: 768px</c> for the phone or
    /// <c>min-width: 768.02px</c> above it, so a page 768.8 CSS px wide (Firefox at 125 %) is in exactly one of them and
    /// a script decides as the sheets do.</summary>
    [Fact]
    public void The_phone_breakpoint_is_one_in_every_sheet_and_every_script()
    {
        var queries = new List<(string Where, string Query)>();
        foreach (var (name, css) in AllSheets.Append(("report-tokens.css", Stylesheets.ReportTokensStyleSheet)))
            queries.AddRange(CssRules.MediaQueries(css).Select(q => (name, q)));
        var assembly = typeof(ReportGenerator).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            queries.AddRange(MatchMedia().Matches(reader.ReadToEnd()).Select(m => (name, m.Groups[1].Value)));
        }

        foreach (var (where, query) in queries)
        foreach (Match m in WidthBound().Matches(query))
        {
            var bound = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (bound is < 760 or > 780)
                continue;
            var expected = m.Groups[1].Value == "max" ? 768 : 768.02;
            Assert.True(bound == expected,
                $"{where}: '{query}' bounds the phone layout at {bound}px; write {m.Groups[1].Value}-width: {expected.ToString(CultureInfo.InvariantCulture)}px");
        }
        // a sweep that read nothing would pass: the base sheet's two sides and a script's question are among them
        Assert.Contains(queries, q => q.Where == "stylesheets.css" && q.Query.Contains("(max-width: 768px)", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Where == "stylesheets.css" && q.Query.Contains("(min-width: 768.02px)", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Where.EndsWith("report-filter-bar-script.js", StringComparison.Ordinal) && q.Query == "(max-width: 768px)");
    }

    [GeneratedRegex(@"(min|max)-width:\\s*([\\d.]+)px")]
    private static partial Regex WidthBound();

    [GeneratedRegex(@"matchMedia\\(\\s*'([^']*)'")]
    private static partial Regex MatchMedia();
'''
assert s.count(a) == 1, 'MinWidth anchor'
open(P, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s.replace(a, b))
print('ok: StylesheetRulesTests.cs')
