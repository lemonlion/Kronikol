# S4: the seeded-expanded toggle defaults test, for the fold buttons (D22).
P = 'tests/Kronikol.Tests.EndToEnd/ToggleDefaultsTests.cs'
s = open(P, 'rb').read().decode('utf-8')
i = s.index('and the expand-all buttons carry the flip labels')
i = s.rindex('\n', 0, i) + 1
end_text = '''"() => Array.from(document.querySelectorAll('details.feature')).every(d => !d.open)"));
    }'''
j = s.index(end_text, i) + len(end_text)
s = s[:i] + '''        // … and the fold buttons start as that leaves them (5.0.0, plans/OPTION_C_PLAN.md S4): expanding has nothing
        // to do, so it is dimmed, and the first click on collapse works.
        var expandFeatures = Page.Locator("button.fold-expand[data-fold='features']");
        var expandScenarios = Page.Locator("button.fold-expand[data-fold='scenarios']");
        var collapseScenarios = Page.Locator("button.fold-collapse[data-fold='scenarios']");
        await Expect(expandFeatures).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(expandScenarios).ToHaveAttributeAsync("aria-disabled", "true");

        await collapseScenarios.ClickAsync();
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.scenario')).every(d => !d.open)"));
        await Expect(expandScenarios).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(collapseScenarios).ToHaveAttributeAsync("aria-disabled", "true");

        await Page.Locator("button.fold-collapse[data-fold='features']").ClickAsync();
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => Array.from(document.querySelectorAll('details.feature')).every(d => !d.open)"));
    }''' + s[j:]
open(P, 'wb').write(s.encode('utf-8'))
print('ok')
