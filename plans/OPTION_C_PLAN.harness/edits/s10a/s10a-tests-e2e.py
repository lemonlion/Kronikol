# S10a (Playwright): the step table bar keeps its blank line above the table and paints none below the last row (P22,
# the owner's round 17 item 6). usage: python s10a-tests-e2e.py <worktree root>
import sys
P = sys.argv[1].rstrip('/') + '/tests/Kronikol.Tests.EndToEnd/StepTableBarTests.cs'
s = open(P, encoding='utf-8', newline='').read()


def sub(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


sub('    public async Task Tables_and_doc_strings_get_a_blank_line_of_breathing_room_in_the_bar()',
    '    public async Task Tables_and_doc_strings_get_a_blank_line_above_and_none_below_in_the_bar()')
sub('''        // padded vs 17px unpadded on the pinned engine), the trailing blank line paints as a
        // whitespace-only text line just below the last data row, and the doc-string bar gets the
        // same gap between its label and first payload line.''',
    '''        // padded vs 17px unpadded on the pinned engine), and the doc-string bar gets the same gap
        // between its label and first payload line. No blank line is painted below the last data row
        // from 5.0.0, as the owner asked (plans/OPTION_C_PLAN.md R17.6).''')
sub('''        Assert.True(spacerBelow == 1, "expected a blank spacer line painted below the last table row");''',
    '''        Assert.True(spacerBelow == 0, "expected no blank line painted below the last table row");''')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
