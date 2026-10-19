# S10a (the tests): no blank line under a step bar's table or doc string (P22, the owner's round 17 item 6: "no gap
# under them"). The six pins that end a bar in a line break end it at its last row. usage: python s10a-tests-stepbar.py <root>
import sys
P = sys.argv[1].rstrip('/') + '/tests/Kronikol.Tests/PlantUml/StepBarPlantUmlTests.cs'
s = open(P, encoding='utf-8', newline='').read()


def sub(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


sub(r'''        // One blank display line above the table and one below — butted directly against the step
        // text and the note border the table reads cramped. Measured on the pinned engine: the \n\n
        // renders a blank line, and a bare trailing \n renders an empty bottom line (each +15px).''',
    r'''        // One blank display line above the table, butted against the step text it reads cramped, and none
        // below it from 5.0.0, as the owner asked (plans/OPTION_C_PLAN.md R17.6). Measured on the pinned
        // engine: the \n\n renders a blank line, and a bare trailing \n an empty bottom line (each +15px).''')
sub(r'''            RichPrefix + @"Given muffins\n\n|= name |= price |\n| Blueberry | 3.50 |\n",''',
    r'''            RichPrefix + @"Given muffins\n\n|= name |= price |\n| Blueberry | 3.50 |",''')
sub(r'''            RichPrefix + "Given the request body\\n\\n{ \"a\": 1,\\n  \"b\": 2 }\\n",''',
    r'''            RichPrefix + "Given the request body\\n\\n{ \"a\": 1,\\n  \"b\": 2 }",''')
sub(r'''        Assert.Equal(RichPrefix + @"Given columns\n\n|= a |= b |\n", bar);''',
    r'''        Assert.Equal(RichPrefix + @"Given columns\n\n|= a |= b |", bar);''')
sub(r'''        Assert.Equal(RichPrefix + @"Given a payload\nwith detail\n\n|= a |\n| 1 |\n", bar);''',
    r'''        Assert.Equal(RichPrefix + @"Given a payload\nwith detail\n\n|= a |\n| 1 |", bar);''')
sub(r'''        // Adjacent blocks share a single blank line — the "after" of one is the "before" of the next.''',
    r'''        // Adjacent blocks keep a single blank line between them: the one before the next block.''')
sub(r'''        Assert.EndsWith(@"| 2 |\n", two);''',
    r'''        Assert.EndsWith(@"| 2 |", two);''')
sub(r'''        Assert.Equal(RichPrefix + @"Given the muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n", bar);''',
    r'''        Assert.Equal(RichPrefix + @"Given the muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |", bar);''')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
