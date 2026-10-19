# S10a (the code): a step bar's table or doc string ends at its last row, with no blank display line under it (P22,
# the owner's round 17 item 6: "no gap under them"). usage: python s10a-edits-stepbar.py <worktree root>
import sys
P = sys.argv[1].rstrip('/') + '/src/Kronikol/PlantUml/StepBarPlantUml.cs'
s = open(P, encoding='utf-8', newline='').read()


def sub(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


sub(r'''/// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;&lt;&lt;stepBody&gt;&gt;: label\n\n|= … |\n</c>, still one''',
    r'''/// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;&lt;&lt;stepBody&gt;&gt;: label\n\n|= … |</c>, still one''')
sub(r'''/// with <c>\n</c> escapes between display lines. Every table/doc-string block is padded with one
/// blank display line above and below (adjacent blocks share the one between them; multi-line
/// marker text is step text and gets none) — measured on the pinned engine, an empty display line
/// renders as a blank note line even as a bare trailing <c>\n</c>. Colours come from the <c>.stepBody</c> style''',
    r'''/// with <c>\n</c> escapes between display lines. Every table/doc-string block has one blank display
/// line above it (multi-line marker text is step text and gets none) and none below the last, which
/// the owner asked to lose in 5.0.0 (plans/OPTION_C_PLAN.md R17.6): measured on the pinned engine, a
/// bare trailing <c>\n</c> renders as an empty bottom line. Colours come from the <c>.stepBody</c> style''')
sub('''        // Each table/doc-string block gets one blank display line above and one below — butted
        // directly against the step text and the note border they read cramped. Adjacent blocks
        // share the blank line between them. Measured on the pinned engine: an empty body entry
        // renders as a blank note line, including as a bare trailing \\n at the end of the statement.
        var padded = false;

''',
    '''        // Each table/doc-string block gets one blank display line above it: butted directly against
        // the step text it reads cramped. None below the last block from 5.0.0 (R17.6): measured on the
        // pinned engine, a bare trailing \\n at the end of the statement renders as an empty bottom line.
''')
sub('''                body.Add("");
                padded = true;
                if (named''', '''                body.Add("");
                if (named''')
sub('''            body.Add("");
            padded = true;
            foreach (var line in docString!''', '''            body.Add("");
            foreach (var line in docString!''')
sub('''        if (padded)
            body.Add("");

''', '')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
