# Two v5 test faults kronikol-89's full Playwright run found on 9bb77de6 (2026-10-19):
#   1. AttachmentRenderingTests: three helpers main added in 4.29.0 and 4.31.1, merged in 9bb77de6, click 4.x's
#      "Expand All Features" and "Expand All Scenarios" buttons, which v5 replaced with icon buttons (S6); v5's base
#      has ExpandAllFeaturesAsync and ExpandAllScenariosAsync for it. Six facts waited 30 s and failed.
#   2. ToolbarStylingTests.Popup_controls_and_search_matches_take_the_themes_colours, the default theme's case: S11c
#      put the internal-flow popup on the tokens and drew its view switch as a pair of tabs, and this case still held
#      4.x's literal colours (only its first check ever ran).
# usage: python tests-v5-fixes.py <root>
import sys

ROOT = sys.argv[1].rstrip('/') + '/'
ATT = ROOT + 'tests/Kronikol.Tests.EndToEnd/AttachmentRenderingTests.cs'
TS = ROOT + 'tests/Kronikol.Tests.EndToEnd/ToolbarStylingTests.cs'


def read(p):
    raw = open(p, 'rb').read()
    return raw.startswith(b'\xef\xbb\xbf'), raw.decode('utf-8-sig')


def write(p, bom, s):
    open(p, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)


bom1, att = read(ATT)
old1 = ('        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();\n'
        '        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();\n')
new1 = ('        await ExpandAllFeaturesAsync();\n'
        '        await ExpandAllScenariosAsync();\n')
assert '\r' not in att and att.count(old1) == 3, att.count(old1)

bom2, ts = read(TS)
old2 = [
    ('    [InlineData(true, Tint, Violet, VioletDark, Tint, Violet, "rgb(139, 92, 246) 4px 0px 0px 0px inset")]\n'
     '    [InlineData(false, "rgb(232, 240, 254)", Blue, "rgb(51, 103, 214)", "rgb(240, 244, 255)", Blue, "rgb(37, 89, 199) 2px 0px 0px 0px inset")] // 5.0.0: the accent\'s 2px edge (S1)\n'
     '    public async Task Popup_controls_and_search_matches_take_the_themes_colours(bool violet, string idleHovered, string active,\n'
     '        string activeHovered, string listHovered, string listHoveredBorder, string searchMatchShadow)\n',
     '    [InlineData(true, Tint, Violet, Violet, VioletDark, Tint, Violet, "rgb(139, 92, 246) 4px 0px 0px 0px inset")]\n'
     '    // 5.0.0: the popup\'s view switch is a pair of tabs on the theme\'s tokens, the picked one underlined in the accent\n'
     '    // (S11c), and the search match has the accent\'s 2px edge (S1)\n'
     '    [InlineData(false, Hover, None, Accent, None, Hover, Accent, "rgb(37, 89, 199) 2px 0px 0px 0px inset")]\n'
     '    public async Task Popup_controls_and_search_matches_take_the_themes_colours(bool violet, string idleHovered, string active,\n'
     '        string activeEdge, string activeHovered, string listHovered, string listHoveredBorder, string searchMatchShadow)\n'),
    ('        await Paints(toggle, active, active);\n',
     '        await Paints(toggle, active, activeEdge);\n'
     '        if (!violet)\n'
     '            await Expect(toggle).ToHaveCSSAsync("box-shadow", "rgb(37, 89, 199) 0px -2px 0px 0px inset");\n'),
]
assert '\r' not in ts
for o, n in old2:
    assert ts.count(o) == 1, o.splitlines()[0]
for o, n in old2:
    ts = ts.replace(o, n)
write(ATT, bom1, att.replace(old1, new1))
write(TS, bom2, ts)
print('ok: AttachmentRenderingTests.cs, ToolbarStylingTests.cs')
