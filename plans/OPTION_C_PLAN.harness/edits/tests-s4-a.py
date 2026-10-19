# S4: the shared fold helpers in PlaywrightTestBase, and the mechanical conversion of every test that clicked the 4.x
# Expand All Features / Expand All Scenarios buttons (D22). Run from the v5 worktree root.
import glob

B = 'tests/Kronikol.Tests.EndToEnd/PlaywrightTestBase.cs'
s = open(B, 'rb').read().decode('utf-8')
old_helper = '''    protected async Task ExpandFirstScenarioWithDiagram()
    {
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }'''
assert s.count(old_helper) == 1
s = s.replace(old_helper, '''    protected async Task ExpandFirstScenarioWithDiagram()
    {
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
    }''')
anchor = '''    /// <summary>Opens a tab of the report bar on the test's page (<see cref="OpenReportTabAsync(IPage, string)"/>).</summary>
    protected Task OpenReportTabAsync(string key) => OpenReportTabAsync(Page, key);
'''
assert s.count(anchor) == 1
s = s.replace(anchor, anchor + '''
    /// <summary>
    /// Clicks the Features tools' expand or collapse button for <c>features</c> or <c>scenarios</c>. Since 5.0.0 each
    /// level has an icon pair in place of the 4.x Expand All button whose label flipped (plans/OPTION_C_PLAN.md S4). A
    /// button with nothing to do is dimmed with <c>aria-disabled</c>, which Playwright waits on before a click, so a
    /// level already as asked (expanding the scenarios opens their features too) is left as it is.
    /// </summary>
    internal static async Task FoldAllAsync(IPage page, string noun, bool expand)
    {
        var button = page.Locator($"button.fold-{(expand ? "expand" : "collapse")}[data-fold='{noun}']");
        if (await button.GetAttributeAsync("aria-disabled") == "true") return;
        await button.ClickAsync();
    }

    protected Task ExpandAllFeaturesAsync() => FoldAllAsync(Page, "features", expand: true);
    protected Task ExpandAllScenariosAsync() => FoldAllAsync(Page, "scenarios", expand: true);
    protected Task CollapseAllFeaturesAsync() => FoldAllAsync(Page, "features", expand: false);
    protected Task CollapseAllScenariosAsync() => FoldAllAsync(Page, "scenarios", expand: false);
''')
open(B, 'wb').write(s.encode('utf-8'))

changed = 0
for f in glob.glob('tests/Kronikol.Tests.EndToEnd/**/*.cs', recursive=True):
    g = f.replace(chr(92), '/')
    if '/bin/' in g or '/obj/' in g:
        continue
    t = open(f, 'rb').read().decode('utf-8')
    o = t
    for noun, Noun in (('features', 'Features'), ('scenarios', 'Scenarios')):
        # the test's own page, in a class built on PlaywrightTestBase
        t = t.replace('await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All ' + Noun + '" }).ClickAsync();',
                      'await ExpandAll' + Noun + 'Async();')
        for var in ('page', '_page'):
            t = t.replace('await ' + var + '.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All ' + Noun + '" }).ClickAsync();',
                          'await PlaywrightTestBase.FoldAllAsync(' + var + ', "' + noun + '", expand: true);')
        t = t.replace('await JsClick("button:has-text(\'Expand All ' + Noun + '\')");', 'await ExpandAll' + Noun + 'Async();')
    if t != o:
        changed += 1
        open(f, 'wb').write(t.encode('utf-8'))
print('files changed', changed)
