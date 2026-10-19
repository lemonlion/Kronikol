# S8: a toggle says its bare word, and whether it is on is aria-pressed (D32). The Playwright tests that read the
# label for the state read both: the word, unchanged, and aria-pressed.
import re

E = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/'
ON = {'Shown': 'true', 'Hidden': 'false'}

def fix(name, extra=()):
    p = E + name
    s = open(p, encoding='utf-8', newline='').read()
    before = s
    # Assert.Equal("W Shown", (await X.TextContentAsync())!.Trim());
    s = re.sub(r'Assert\.Equal\("(\w+) (Shown|Hidden)", \(await (.+?)\.TextContentAsync\(\)\)!\.Trim\(\)\);',
               lambda m: f'Assert.Equal("{m[1]}", (await {m[3]}.TextContentAsync())!.Trim());\n'
                         f'{" " * 8}Assert.Equal("{ON[m[2]]}", await {m[3]}.GetAttributeAsync("aria-pressed"));', s)
    # Assert.Equal("W Shown", await X.TextContentAsync());
    s = re.sub(r'Assert\.Equal\("(\w+) (Shown|Hidden)", await (\w+)\.TextContentAsync\(\)\);',
               lambda m: f'Assert.Equal("{m[1]}", await {m[3]}.TextContentAsync());\n'
                         f'{" " * 8}Assert.Equal("{ON[m[2]]}", await {m[3]}.GetAttributeAsync("aria-pressed"));', s)
    # await Expect(X).ToHaveTextAsync("W Shown");
    s = re.sub(r'await Expect\((\w+)\)\.ToHaveTextAsync\("(\w+) (Shown|Hidden)"\);',
               lambda m: f'await Expect({m[1]}).ToHaveAttributeAsync("aria-pressed", "{ON[m[3]]}");\n'
                         f'{" " * 8}await Expect({m[1]}).ToHaveTextAsync("{m[2]}");', s)
    for old, new in extra:
        assert s.count(old) == 1, (name, old[:70])
        s = s.replace(old, new)
    assert s != before, name
    open(p, 'w', encoding='utf-8', newline='').write(s)

fix('DatabaseToggleTests.cs', [
    ('/// Tests that verify the "Databases Shown/Hidden" toggle removes database', '/// Tests that verify the Databases toggle removes database'),
    ('''        var text = await btn.TextContentAsync();
        Assert.Equal("Databases Shown", text);''', '''        Assert.Equal("Databases", await btn.TextContentAsync());
        Assert.Equal("true", await btn.GetAttributeAsync("aria-pressed"));'''),
    ('''                return btn && btn.textContent === 'Databases Hidden';''', '''                return btn && btn.getAttribute('aria-pressed') === 'false';'''),
    ('''        // Click "Databases Shown" to hide''', '''        // Click Databases, on, to hide them'''),
    ('''        var text = await btn.TextContentAsync();
        Assert.Contains("Shown", text);''', '''        Assert.Equal("Databases", await btn.TextContentAsync());
        Assert.Equal("true", await btn.GetAttributeAsync("aria-pressed"));'''),
])
fix('DiagramNoteBasicTests.cs')
fix('ReportToolbarTests.cs')
fix('SpecificationsToggleInheritanceTests.cs')
fix('ToggleDefaultsTests.cs')
for name, old, new in [
        ('DiagramNoteStepsToggleTests.cs', '/// when the steps toggle is set to "Steps Hidden".', '/// when the Steps toggle is off.'),
        ('DiagramNoteStepsToggleTests.cs', '        // Click "Steps Shown" button to hide steps', '        // Click Steps, on, to hide the steps'),
        ('NoteButtonIndexTests.cs', '(e.g., header-only notes with "Headers: Hidden")', '(e.g., header-only notes with Headers off)')]:
    p = E + name
    s = open(p, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, old)
    open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok')
