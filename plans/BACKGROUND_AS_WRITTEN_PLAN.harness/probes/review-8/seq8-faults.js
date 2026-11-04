// The smallest shape of each fault the eighth review reports, on rows.html. Run: node drive8.js <pages-old> <pages-new> rows.html seq8-faults.js
module.exports = [
    ['Move', 'F1a search chooses b, reader clicks c, emptied, reader clicks b then a', ['q:alphaword', 2, 'q:', 1, 0]],
    ['Move', 'F1b search chooses b, reader clicks c, next keystroke chooses b again, emptied', ['q:alphawor', 2, 'q:alphaword', 'q:']],
    ['Move', 'F1c reader closes Steps on a, match on a, reader clicks b, emptied, reader back on a', ['open', 's:scenario-steps', 'q:firstword', 1, 'q:', 0]],
    ['Plain', 'F2a plain: reader closes Background, opens it again, emptied', ['q:plainword', 's:scenario-background', 's:scenario-background', 'q:']],
    ['Move', 'F2b row: reader closes Background, opens it again, emptied', ['q:alphaword', 's:scenario-background', 's:scenario-background', 'q:']],
    ['PairA', 'F3a one match on PairA, widened so PairB chooses a row: PairA stays on its row', ['q:pairword onlya', 'q:pairword', 'q:']],
    ['PairA', 'F3b reader closes Background on PairA, widened, narrowed', ['q:pairword onlya', 's:scenario-background', 'q:pairword', 'q:pairword onlya', 'q:']],
    ['Plain', 'F3 control: widened with no row chosen, the section stays', ['q:plainword', 'q:plain', 'q:']],
    ['Plain', 'F3 control: reader closes, widened, narrowed, stays closed', ['q:plainword', 's:scenario-background', 'q:plain', 'q:plainword', 'q:']],
    ['Narrow', 'F4a reader closes on b, match moves to c and back to b', ['q:sku12', 's:scenario-background', 'q:sku123', 'q:sku12', 'q:']],
    ['Plain', 'F4b plain: reader closes, match moves to Plain2 and back', ['q:plainword', 's:scenario-background', 'q:plaintwoword', 'q:plainword', 'q:']],
];
