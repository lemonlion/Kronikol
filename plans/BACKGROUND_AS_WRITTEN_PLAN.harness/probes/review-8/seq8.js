module.exports = [
    // the reader switches rows by hand between keystrokes
    ['Move', 'A1 hand switch between keystrokes, the search chooses its row again, emptied', ['q:alphawor', 2, 'q:alphaword', 'q:', 1, 2, 3]],
    ['Move', 'A2 hand switch during the search, emptied, tour', ['q:alphaword', 2, 'q:', 1, 0]],
    ['Move', 'A3 match on the row the reader is on, hand switch, emptied, tour', ['q:firstword', 1, 'q:', 0, 2]],
    ['Zanzibar', 'A4 word in the name, no row chosen, hand switch, typing on, emptied, back', ['q:zanziba', 1, 'q:zanzibar', 'q:', 0]],
    ['Move', 'A5 hand switch and hand back, emptied', ['q:alphaword', 2, 1, 'q:', 0]],
    // a section the reader closes, typing on
    ['Move', 'B1 reader closes Background, next keystroke, same row', ['q:alphawor', 's:scenario-background', 'q:alphaword', 'q:']],
    ['Narrow', 'B2 reader closes on b, match moves to c, moves back to b', ['q:sku12', 's:scenario-background', 'q:sku123', 'q:sku12', 'q:']],
    ['Move', 'B3 reader closes, hand switch, typing on picks the row again', ['q:alphawor', 's:scenario-background', 2, 'q:alphaword', 'q:']],
    ['Move', 'B4 reader closes then opens by hand, emptied', ['q:alphaword', 's:scenario-background', 's:scenario-background', 'q:']],
    ['Plain', 'B5 plain: reader closes then opens by hand, emptied', ['q:plainword', 's:scenario-background', 's:scenario-background', 'q:']],
    ['Plain', 'B6 plain: reader closes, widened to two, back to one', ['q:plainword', 's:scenario-background', 'q:plain', 'q:plainword', 'q:']],
    ['Plain', 'B7 plain: reader closes, match moves to Plain2 and back', ['q:plainword', 's:scenario-background', 'q:plaintwoword', 'q:plainword', 'q:']],
    ['Move', 'B8 reader closes the diagrams section, next keystroke', ['q:alphawor', 'd:example-diagrams', 'q:alphaword', 'q:']],
    ['Plain', 'B9 plain: reader closes the diagrams section, next keystroke', ['q:plainwor', 'd:example-diagrams', 'q:plainword', 'q:']],
    ['Plain', 'B10 plain: reader shuts the scenario, next keystroke', ['q:plainwor', 'shut', 'q:plainword', 'q:']],
    // widening: another outline's row choice
    ['PairA', 'C1 one match, reader closes Background, widened so PairB chooses a row, narrowed', ['q:pairword onlya', 's:scenario-background', 'q:pairword', 'q:pairword onlya', 'q:', 'read:PairB']],
    ['PairA', 'C2 one match, widened, reader reads Background on PairA, PairB row change closes it', ['q:pairword onlya', 'q:pairword', 'read:PairB', 'q:']],
    ['PairB', 'C3 one match, widened then narrowed to the other row', ['q:latterb', 'q:pairword', 'q:latterb', 'q:']],
    // rows that differ
    ['Diff', 'D1 match moves across rows that differ, emptied, tour', ['q:awordone', 'q:bwordone', 'q:cwordone', 'q:dwordone', 'q:', 1, 2, 3, 0]],
    ['Diff', 'D2 reader closes Steps first; match to c, to d; emptied, tour', ['open', 's:scenario-steps', 'q:cwordone', 'q:dwordone', 'q:', 1, 2, 3, 0]],
    ['Diff', 'D3 reader opens the composite on a, match to c, emptied', ['open', 'c:0', 'q:cwordone', 'q:', 2, 0]],
    ['Diff', 'D4 reader closes Background on a row, hand to c with none, hand to d', ['q:awordone', 's:scenario-background', 2, 3, 'q:', 0]],
    ['Diff', 'D5 search on b, the held row; hand to a', ['q:bwordone', 0, 'q:', 1]],
    ['Diff', 'D6 Failure Result: reader closes it on d, match to b', ['q:dwordone', 's:failure-result', 'q:bwordone', 'q:', 3]],
    // Clear All and Back leave the search's state behind
    ['Move', 'E1 reveal, Clear All, reader closes Background, same word again', ['q:alphaword', 'clearall', 's:scenario-background', 'q:alphaword', 'q:']],
    ['Move', 'E2 reveal, Clear All, tour rows, emptied by another search', ['q:alphaword', 'clearall', 2, 0, 'q:betaword', 'q:']],
    ['Plain', 'E3 plain: reveal, Clear All, reader shuts scenario and closes Background, same word', ['q:plainword', 'clearall', 's:scenario-background', 'shut', 'q:plainword', 'q:']],
    ['Plain', 'E4 plain: reveal, Clear All, reader keeps reading; another search moves away', ['q:plainword', 'clearall', 'q:plaintwoword', 'q:', 'read:Plain']],
    ['Move', 'E5 reveal, Back, reader closes Background, Forward', ['q:alphaword', 'back', 's:scenario-background', 'forward', 'q:']],
    // reader's own opening before the search
    ['Move', 'F1 reader opens Background on a, match on b, emptied', ['open', 's:scenario-background', 'q:alphaword', 'q:', 1]],
    ['Move', 'F2 reader opens Background on a, match on a, emptied', ['open', 's:scenario-background', 'q:firstword', 'q:']],
    ['Move', 'F3 reader on c, closes Steps, match on b, emptied, a', ['open', 2, 's:scenario-steps', 'q:alphaword', 'q:', 0]],
];
