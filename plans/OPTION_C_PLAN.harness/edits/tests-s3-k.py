# S3: the component diagram database tests use the Features tab's toolbar (hidden on the other tabs since 5.0.0).
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

C = 'tests/Kronikol.Tests.EndToEnd/ComponentDiagramDatabaseToggleTests.cs'
BACK = '''        // the toolbar is the Features tab's since 5.0.0 (plans/OPTION_C_PLAN.md S3); the drawn overview stays in its panel
        await OpenReportTabAsync("features");
'''
sub(C, '''        await Page.EvaluateAsync(
            "() => document.querySelector('#component-diagram .plantuml-browser svg').dataset.pinned = '1'");

        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();''', '''        await Page.EvaluateAsync(
            "() => document.querySelector('#component-diagram .plantuml-browser svg').dataset.pinned = '1'");

''' + BACK + '''        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();''')
sub(C, '''            o.TestRunReportToggleDefaults.ComponentDiagramVisible = true;
        }));
        await Page.Locator("details.feature").First.WaitForAsync();''', '''            o.TestRunReportToggleDefaults.ComponentDiagramVisible = true;
        }));
        // the page opens on the Components tab (5.0.0), its features hidden
        await Page.Locator(".report-tabs").WaitForAsync();''')
sub(C, '''        // The scenario sequence diagrams DO honour the hidden default.
        await ExpandFirstScenarioWithDiagram();''', '''        // The scenario sequence diagrams DO honour the hidden default.
        await OpenReportTabAsync("features");
        await ExpandFirstScenarioWithDiagram();''')
sub(C, '''        // Hide databases, then toggle steps and assertions — their queue builders re-apply
        // the databases filter to every container they process and must skip the overview.
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();''', '''        // Hide databases, then toggle steps and assertions — their queue builders re-apply
        // the databases filter to every container they process and must skip the overview.
''' + BACK + '''        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();''')

D = 'tests/Kronikol.Tests.EndToEnd/DependencyColoringTests.cs'
sub(D, '''        var section = Page.Locator("#component-diagram");
        var display = await section.EvaluateAsync<string>("e => window.getComputedStyle(e).display");
        Assert.Equal("none", display);''', '''        // since 5.0.0 hidden as its tab's panel until the tab is chosen (plans/OPTION_C_PLAN.md S3)
        await Expect(Page.Locator("#component-diagram")).ToBeHiddenAsync();
        await Expect(Page.Locator("#panel-components")).ToHaveAttributeAsync("hidden", "");''')
print('ok')
