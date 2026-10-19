# S8: the core tests' pins of the toolbars' markup, moved to 5.0.0's: the payload buttons say Expanded, Collapsed and
# Truncated in a span that holds their bold width, a toggle its bare word with aria-pressed, the line count "N lines",
# the width select "Payload width", the view tabs role=tab with aria-selected and a roving tabindex.
import re

T = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests/'
Q = '\\"'  # a quote as a C# string literal writes it

def label(word):
    return f'<span class={Q}btn-label{Q} data-label={Q}{word}{Q}>{word}</span>'

def fix(path, extra=()):
    p = T + path
    s = open(p, encoding='utf-8', newline='').read()
    before = s
    for old, new in (('>Expand</button>', '>' + label('Expanded') + '</button>'),
                     ('>Collapse</button>', '>' + label('Collapsed') + '</button>'),
                     ('>Truncate</button>', '>' + label('Truncated') + '</button>')):
        s = s.replace(old, new)
    # a toggle: data-shown, then aria-pressed the same, then its handler and its bare word
    s = re.sub(r'data-shown=\\"(true|false)\\" onclick=\\"([^"\\]*)\\">(\w+) (?:Shown|Hidden)</button>',
               lambda m: f'data-shown={Q}{m[1]}{Q} aria-pressed={Q}{m[1]}{Q} onclick={Q}{m[2]}{Q}>{label(m[3])}</button>', s)
    # the line count's options
    s = re.sub(r'<option value=\\"(\d+)\\"( selected)?>(\d+)</option>', lambda m: f'<option value={Q}{m[1]}{Q}{m[2] or ""}>{m[3]} lines</option>', s)
    # the view tabs
    s = re.sub(r'<button class=\\"diagram-toggle-btn( diagram-toggle-active)?\\" data-dtype=',
               lambda m: f'<button class={Q}diagram-toggle-btn{m[1] or ""}{Q} role={Q}tab{Q} aria-selected={Q}{"true" if m[1] else "false"}{Q} tabindex={Q}{"0" if m[1] else "-1"}{Q} data-dtype=', s)
    for old, new in extra:
        assert s.count(old) >= 1, (path, old[:70])
        s = s.replace(old, new)
    assert s != before, path
    open(p, 'w', encoding='utf-8', newline='').write(s)

fix('Reports/ToggleDefaultsMarkupTests.cs', [
    (f'<optgroup label={Q}Note width{Q}>', f'<optgroup label={Q}Payload width{Q}>'),
    (f'title={Q}Note width. Wrap: notes wrap at the report\'s note width. Wide: notes widen to fill the diagram.{Q}',
     f'title={Q}Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.{Q}'),
    (f'aria-label={Q}Note width{Q}', f'aria-label={Q}Payload width{Q}'),
    ('Assert.Contains("Headers Hidden</button>", content);',
     f'Assert.Contains("data-toggle={Q}headers{Q} data-shown={Q}false{Q} aria-pressed={Q}false{Q}", content);'),
])
fix('Reports/ParameterizedGroupRenderTests.cs', [
    (f'onclick={Q}window._toggleScenarioAssertions(this){Q}>Assertions Hidden</button>',
     f'aria-pressed={Q}false{Q} onclick={Q}window._toggleScenarioAssertions(this){Q}>' + label('Assertions') + '</button>'),
    (f'onclick={Q}window._toggleScenarioSteps(this){Q}>Steps Shown</button>',
     f'aria-pressed={Q}true{Q} onclick={Q}window._toggleScenarioSteps(this){Q}>' + label('Steps') + '</button>'),
])
fix('Reports/ReportToggleTests.cs', [
    ('Assert.Contains(">Headers Hidden</button>", testRun);',
     f'Assert.Contains("data-toggle={Q}headers{Q} data-shown={Q}false{Q} aria-pressed={Q}false{Q}", testRun);'),
    ('Assert.Contains(">Headers Hidden</button>", specs);',
     f'Assert.Contains("data-toggle={Q}headers{Q} data-shown={Q}false{Q} aria-pressed={Q}false{Q}", specs);'),
])
fix('Reports/DatabaseToggleReportTests.cs', [
    ('Assert.Contains("Databases Shown", content);', f'Assert.Contains("data-toggle={Q}databases{Q} data-shown={Q}true{Q} aria-pressed={Q}true{Q}", content);'),
    ('Assert.DoesNotContain("Databases Shown", ReportMarkup.Only(content));', f'Assert.DoesNotContain("data-toggle={Q}databases{Q}", ReportMarkup.Only(content));'),
])
fix('Tracking/TrackThatIntegrationTests.cs', [
    ('Assert.Contains("Assertions Hidden", content);', f'Assert.Contains("data-toggle={Q}assertions{Q} data-shown={Q}false{Q} aria-pressed={Q}false{Q}", content);'),
])
fix('Reports/NoteAppearanceScriptTests.cs', [
    ('Assert.Contains(".note-format-select,\\n.note-width-select {\\n    padding: 0.2em 0.3em;", css);',
     'Assert.Contains(".truncate-lines-select, .note-format-select, .note-width-select {\\n    appearance: none;", css);'),
    ('''            ".note-format-select.details-pending,\\n.note-width-select.details-pending {\\n    position: relative;",''',
     '''            ".truncate-lines-select.details-pending, .note-format-select.details-pending, .note-width-select.details-pending {\\n    animation:",'''),
])
fix('Reports/StylesheetRulesTests.cs', [
    ('''        ".diagram-toggle-active", ".diagram-toggle-active:hover", ".diagram-toggle-spacer",''',
     '''        ".diagram-toggle-active", ".diagram-toggle-active:hover", ".diagram-toggle[data-layout=\\"inline\\"]",'''),
])
print('ok')
