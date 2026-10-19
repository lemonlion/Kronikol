# S8: the phone tests of a scenario's settings (D34). 4.x's init script hid every scenario toolbar behind a
# "Diagram Settings" button it made on a phone; 5.0.0 writes the gear on the section's title, and the settings it
# opens are an options bar under the views' tabs.
p = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/MobileResponsiveTests.cs'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new):
    global s
    assert s.count(old) == 1, (s.count(old), old[:90])
    s = s.replace(old, new)

sub('''        // On a phone the init script hides every scenario toolbar behind a "Diagram Settings" button, so
        // the toolbar is measured after the tap that shows it: read off the hidden box, flex-wrap says
        // nothing about what a reader sees.
        await Page.Locator(".scenario-diagram-controls-toggle").First.ClickAsync();
        var toggle = Page.Locator(".diagram-toggle").First;
        await Expect(toggle).ToBeVisibleAsync();
        Assert.Equal("flex", await GetComputedStyle(toggle, "display"));
        Assert.Equal("wrap", await GetComputedStyle(toggle, "flex-wrap"));
        Assert.Equal("none", await GetComputedStyle(Page.Locator(".diagram-toggle-spacer").First, "display"));
''', '''        // On a phone a scenario's settings open from the gear on its diagrams' title (5.0.0), so the bar is
        // measured after the tap that shows it: read off the hidden box, flex-wrap says nothing about what a
        // reader sees.
        await Page.Locator(".gear-in-summary").First.ClickAsync();
        var toggle = Page.Locator(".diagram-options-bar").First;
        await Expect(toggle).ToBeVisibleAsync();
        Assert.Equal("flex", await GetComputedStyle(toggle, "display"));
        Assert.Equal("wrap", await GetComputedStyle(toggle, "flex-wrap"));
''')

sub('''        // The toggle button should be visible
        var toggleBtn = Page.Locator(".scenario-diagram-controls-toggle").First;
        await Expect(toggleBtn).ToBeVisibleAsync();

        // The controls should be hidden initially
        var controlsHidden = await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.scenario-diagram-controls'); return c && c.style.display === 'none'; }");
        Assert.True(controlsHidden, "Diagram controls should be hidden by default on mobile");
''', '''        // The gear on the diagrams' title is visible, the one at the diagram's corner is not (5.0.0)
        var toggleBtn = Page.Locator(".gear-in-summary").First;
        await Expect(toggleBtn).ToBeVisibleAsync();
        Assert.Equal("Diagram settings", await toggleBtn.GetAttributeAsync("aria-label"));
        await Expect(Page.Locator(".scenario-settings-gear").First).ToBeHiddenAsync();

        // The settings are closed until it is pressed
        await Expect(Page.Locator(".diagram-options-bar").First).ToBeHiddenAsync();
''')

sub('''        var toggleBtn = Page.Locator(".scenario-diagram-controls-toggle").First;

        // Click to show controls
        await toggleBtn.ClickAsync();
        var shown = await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.scenario-diagram-controls'); return c && c.style.display === 'flex'; }");
        Assert.True(shown, "Controls should be visible after clicking toggle");

        // Click to hide controls
        await toggleBtn.ClickAsync();
        var hidden = await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.scenario-diagram-controls'); return c && c.style.display === 'none'; }");
        Assert.True(hidden, "Controls should be hidden after clicking toggle again");
''', '''        var toggleBtn = Page.Locator(".gear-in-summary").First;
        var bar = Page.Locator(".diagram-options-bar").First;

        // Click to show the settings: the section stays open, the gear says it is
        await toggleBtn.ClickAsync();
        await Expect(bar).ToBeVisibleAsync();
        Assert.Equal("true", await toggleBtn.GetAttributeAsync("aria-expanded"));
        Assert.True(await Page.Locator("details.example-diagrams").First.EvaluateAsync<bool>("d => d.open"));

        // Click to hide them
        await toggleBtn.ClickAsync();
        await Expect(bar).ToBeHiddenAsync();
        Assert.Equal("false", await toggleBtn.GetAttributeAsync("aria-expanded"));
''')

sub('''        var toggleBtn = Page.Locator(".scenario-diagram-controls-toggle").First;
        await Expect(toggleBtn).ToBeVisibleAsync();

        var display = await GetComputedStyle(toggleBtn, "display");
        Assert.Equal("block", display);
''', '''        // 5.0.0: the gear sits at the right end of the diagrams' title, floated, so it computes as a block
        var toggleBtn = Page.Locator(".gear-in-summary").First;
        await Expect(toggleBtn).ToBeVisibleAsync();

        Assert.Equal("block", await GetComputedStyle(toggleBtn, "display"));
        Assert.Equal("right", await GetComputedStyle(toggleBtn, "float"));
''')
open(p, 'w', encoding='utf-8', newline='').write(s)

p = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/ViewportSweepTests.cs'
s = open(p, encoding='utf-8', newline='').read()
sub('''                await page.Locator(".scenario-diagram-controls-toggle").First.ClickAsync();''',
    '''                await page.Locator(".gear-in-summary").First.ClickAsync();''')
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
