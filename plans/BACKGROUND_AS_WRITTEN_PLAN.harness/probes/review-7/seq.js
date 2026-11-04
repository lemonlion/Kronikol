module.exports = [
    // typed progressively, on the row it ends on
    ['Move', 'progressive to one row, backspaced to empty', ['q:alph', 'q:alphaword', 'q:alph', 'q:']],
    ['Move', 'one word, emptied', ['q:alphaword', 'q:']],
    // the match moves between rows
    ['Move', 'match moves b to c, emptied', ['q:alphaword', 'q:betaword', 'q:']],
    ['Move', 'match moves b to c to b, emptied', ['q:alphaword', 'q:betaword', 'q:alphaword', 'q:']],
    ['Move', 'match moves b to a, the row it started on, emptied', ['q:alphaword', 'q:firstword', 'q:']],
    ['Move', 'after b to c and emptied, the reader tours the rows', ['q:alphaword', 'q:betaword', 'q:', 1, 2, 3, 0]],
    ['Move', 'reader closes Steps on a; b; c; emptied', ['open', 's:scenario-steps', 'q:alphaword', 'q:betaword', 'q:']],
    ['Move', 'reader opens Background on a; b; emptied', ['open', 's:scenario-background', 'q:alphaword', 'q:']],
    ['Move', 'reader leaves row c, opens nothing; b; emptied, then a', ['open', 2, 'q:alphaword', 'q:', 0]],
    // the reader chooses a row during the search
    ['Move', 'search chooses b; reader picks c; emptied; b; a', ['q:alphaword', 2, 'q:', 1, 0]],
    ['Move', 'search chooses b; reader picks a; emptied; b', ['q:alphaword', 0, 'q:', 1]],
    // a section the reader closes during a search
    ['Move', 'reader closes Background on the row chosen; next keystroke; emptied', ['q:alphawor', 's:scenario-background', 'q:alphaword', 'q:']],
    ['Move', 'reader closes both on the row chosen; emptied; then that row', ['q:alphaword', 's:scenario-background', 's:scenario-steps', 'q:', 1]],
    ['Move', 'reader closes Background on the first row, the match; next keystroke', ['q:firstwor', 's:scenario-background', 'q:firstword', 'q:']],
    ['Move', 'reader closes, reopens Background on the row chosen; emptied', ['q:alphaword', 's:scenario-background', 's:scenario-background', 'q:']],
    // no row chosen: the word is the outline's name
    ['Zanzibar', 'word in the name alone, no reader', ['q:zanzibar', 'q:']],
    ['Zanzibar', 'word in the name alone, reader on row b', ['open', 1, 'q:zanzibar', 'q:', 0]],
    ['Zanzibar', 'reader on row b closes Background during the search; next keystroke', ['open', 1, 'q:zanziba', 's:scenario-background', 'q:zanzibar', 'q:']],
    ['Zanzibar', 'reader switches row during the search; next keystroke', ['q:zanziba', 1, 's:scenario-background', 0, 'q:zanzibar', 'q:']],
    // plain
    ['Plain', 'plain, emptied', ['q:plainword', 'q:']],
    ['Plain', 'plain, reader closed Steps first', ['open', 's:scenario-steps', 'q:plainword', 'q:']],
    ['Plain', 'plain, reader closes during, next keystroke', ['q:plainwor', 's:scenario-background', 'q:plainword', 'q:']],
    ['Plain', 'match moves to Plain2, emptied', ['q:plainword', 'read2:Plain2', 'q:plaintwoword', 'read', 'read2:Plain2', 'q:', 'read', 'read2:Plain2']],
    ['PlainHeld', 'plain with a held failure', ['q:solofailterm', 'q:']],
    ['PlainHeld', 'plain with a held failure, reader closed it, search, emptied', ['open', 's:scenario-background', 'q:solofailterm', 'q:']],
    // one match, several, one
    ['TwoA', 'one, several, one, emptied', ['q:lynxword onlytwoaword', 'read2:TwoB', 'q:lynxword', 'read2:TwoB', 'q:lynxword onlytwoaword', 'q:', 'read2:TwoB']],
    ['TwoB', 'several, then one in the other, emptied', ['q:lynxword', 'read2:TwoA', 'q:lynxword onlytwoaword', 'read2:TwoA', 'q:', 'read2:TwoA']],
    // back, forward, reload, the address
    ['Move', 'typed, Back, Forward, emptied', ['q:alphaword', 'back', 'forward', 'q:']],
    ['Move', 'typed, emptied, Back, Forward', ['q:alphaword', 'q:', 'back', 'forward']],
    ['Move', 'typed, Back, emptied via a new search', ['q:alphaword', 'back', 'q:betaword', 'q:']],
    ['Move', 'load with the row b word', ['goto:q=alphaword', 'q:']],
    ['Move', 'load with the first row word', ['goto:q=firstword', 'q:']],
    ['Move', 'typed, reload, emptied', ['q:alphaword', 'reload', 'q:']],
    ['Plain', 'load with the word', ['goto:q=plainword', 'q:']],
    ['Move', 'load, then Back to a blank page is no entry; b, c via address', ['goto:q=alphaword', 'q:betaword', 'back', 'forward', 'q:']],
    ['Move', 'typed, Clear All, then a new search emptied', ['q:alphaword', 'clearall', 'q:betaword', 'q:']],
    // a failure held
    ['HoldA', 'first row holds, word in b, emptied, b', ['q:holdword', 'q:', 1]],
    ['HoldB', 'word in the row that holds, emptied', ['q:heldword', 'q:', 1]],
    ['HoldB', 'reader closed the held section; search; emptied; that row', ['open', 1, 's:scenario-background', 0, 'q:heldword', 'q:', 1]],
    ['HoldB', 'reader closes the held section on the match; next keystroke; emptied', ['q:heldwor', 's:scenario-background', 'q:heldword', 'q:', 1]],
    // the word in the feature's name alone: no row is chosen, the reader's stays
    ['InFeature', 'word in the feature name; reader on row b', ['open', 1, 'q:zebrafeature', 'q:', 0]],
    ['InFeature', 'word in the feature name; reader on row b closes during; next keystroke', ['open', 1, 'q:zebrafeatur', 's:scenario-background', 'q:zebrafeature', 'q:', 0]],
    ['InFeature', 'word in the feature name; no reader', ['q:zebrafeature', 'q:']],
    // Enter makes an entry of the history
    ['Move', 'entered word, entered empty, Back, Back, Forward, Forward', ['e:alphaword', 'e:', 'back', 'back', 'forward', 'forward', 'q:']],
    ['Move', 'entered word, entered second word, Back (match moves), Back (no search), then a word emptied', ['e:alphaword', 'e:betaword', 'back', 'back', 'q:deltaword', 'q:']],
    // blocks
    ['Blocks', 'word in the second block', ['q:blockword', 'q:']],
];
