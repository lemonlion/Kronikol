# S8 follow-up: taking a marker out of a diagram's source keeps the lines around it apart, and the "nothing to draw"
# message names the toggles by their 5.0.0 words. usage: python scripts-s8-b.py   (from anywhere)
ROOT = 'C:/Code/Kronikol-v5/'
R = ROOT + 'src/Kronikol/Reports/'

OLD_MSG = ("Nothing to draw with the current filters \u2014 this diagram is only assertion notes and/or step bars "
           "(use Assertions Shown / Steps Shown to see them).")
NEW_MSG = ("Nothing to draw with the current filters: this diagram holds only assertion notes or step bars. "
           "Turn on Assertions or Steps to see them.")

EDITS = {
    R + 'collapsible-notes-script.js': [
        (r"""    function stripAssertionNotes(source) {
        return source.replace(/\n?hnote across <<assertionNote>>[^\n]*\n(?:[^\n]*\n)*?end note(?=\n|$)\n?/g, '');
    }

    function stripStepDelimiters(source) {
        return source.replace(/\n?hnote across <<stepDelimiter>>[^\n]*\n?/g, '');
    }""",
         r"""    // A marker goes with its own lines and the one line break after them, so the statements around it stay on lines
    // of their own (the m flag lets a marker that follows another start where that one ended). Taking the break on
    // both sides once ran the statement before a marker into the one after it wherever lines end in LF.
    function stripAssertionNotes(source) {
        return source.replace(/^[ \t]*hnote across <<assertionNote>>[^\n]*\n(?:[^\n]*\n)*?[ \t]*end note\r?(?:\n|$)/gm, '');
    }

    function stripStepDelimiters(source) {
        return source.replace(/^[ \t]*hnote across <<stepDelimiter>>[^\n]*(?:\n|$)/gm, '');
    }"""),
        (OLD_MSG, NEW_MSG),
    ],
    R + 'plantuml-browser-render-script.js': [(OLD_MSG, NEW_MSG)],
    R + 'ReportGenerator.cs': [
        ("""    /// One Shown/Hidden filter toggle button (headers / assertions / steps / databases) with the
    /// configured start state computed into class, <c>data-shown</c> and label — the label must
    /// match what <c>syncToggleBtn</c> derives from the toggle name on every later click.""",
         """    /// One filter toggle (headers / assertions / steps / databases): its bare word, with the configured
    /// start state in its class, <c>data-shown</c> and <c>aria-pressed</c>, which <c>syncToggleBtn</c>
    /// keeps on every later click (5.0.0; the label no longer changes)."""),
    ],
    ROOT + 'tests/Kronikol.Tests/Reports/HeadersDetailsInterferenceReportTests.cs': [
        ("(Assertions Shown/Hidden, etc.)", "(Assertions, Steps, etc.)"),
    ],
}

for path, edits in EDITS.items():
    s = open(path, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    for old, new in edits:
        if crlf:
            old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
        assert s.count(old) == 1, (path, old[:60], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)
    print('edited', path)
