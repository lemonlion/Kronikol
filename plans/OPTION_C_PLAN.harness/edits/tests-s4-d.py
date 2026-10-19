# S4: the remaining tests that found the 4.x Expand All buttons by class or by label (D22).
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode('utf-8')
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode('utf-8'))

E = 'tests/Kronikol.Tests.EndToEnd/'
sub(E + 'ShowcaseReportTests.cs', '''        try
        {
            var expandFeatures = Page.Locator(".collapse-expand-all", new() { HasTextString = "Expand All Features" });
            if (await expandFeatures.CountAsync() > 0) { await JsClick(".collapse-expand-all"); await Task.Delay(300); }
        }
        catch { }
        try
        {
            var expandScenarios = Page.Locator(".collapse-expand-all", new() { HasTextString = "Expand All Scenarios" });
            if (await expandScenarios.CountAsync() > 0)
            {
                await Page.EvaluateAsync("""
                    (() => {
                        var btns = document.querySelectorAll('.collapse-expand-all');
                        for (var b of btns) { if (b.textContent.includes('Expand All Scenarios')) { b.click(); break; } }
                    })()
                """);
                await Task.Delay(400);
            }
        }''', '''        // 5.0.0: an expand button per level (plans/OPTION_C_PLAN.md S4)
        try { await ExpandAllFeaturesAsync(); await Task.Delay(300); }
        catch { }
        try
        {
            await ExpandAllScenariosAsync();
            await Task.Delay(400);
        }''')

sub(E + 'ToolbarStylingTests.cs', '''        { true, Tint, TintBorder, ".collapse-expand-all" },''', '''        { true, Tint, TintBorder, ".fold-btn" },''')
sub(E + 'ToolbarStylingTests.cs', '''        { false, BlueTint, BlueTintBorder, ".collapse-expand-all" },''',
    '''        // 5.0.0: expand and collapse are icon buttons with a transparent edge (plans/OPTION_C_PLAN.md S4)
        { false, Hover, "rgba(0, 0, 0, 0)", ".fold-btn" },''')

W = E + 'WikiGifTests.cs'
sub(W, '''        await JsClick(".collapse-expand-all"); await Hold(1.5);''',
    '''        await JsClick("button.fold-expand[data-fold='features']"); await Hold(1.5); // 5.0.0: OPTION_C_PLAN S4''')
sub(W, '''                    var btns = document.querySelectorAll('button');
                    for (var b of btns) {
                        if (b.textContent.includes('Expand All Features')) {
                            b.scrollIntoView({block:'start', behavior:'instant'});
                            window.scrollBy(0, -10);
                            break;
                        }
                    }''', '''                    var b = document.querySelector("button.fold-expand[data-fold='features']");
                    if (b) {
                        b.scrollIntoView({block:'start', behavior:'instant'});
                        window.scrollBy(0, -10);
                    }''')

sub(E + 'BackgroundRenderingTests.cs', '''        // Expand All Scenarios is wired to details.feature and details.scenario only, so the old''',
    '''        // Expanding all scenarios opens features, rules and scenarios only, so the old''')
print('ok')
