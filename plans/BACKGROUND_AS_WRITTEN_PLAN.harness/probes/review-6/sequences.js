module.exports = [
    // Deep rows: 0 a pass, 1 b inner fails, 2 c pass, 3 d outer fails itself, 4 e no composite, 5 f pass
    ['Deep', 'no reader, each row', [1, 2, 3, 4, 5, 0]],
    ['Deep', 'reader opens bg and the composites on a, on through the rows', ['s:scenario-background', 'c:b.0', 'c:b.0.0', 2, 1, 5, 0]],
    ['Deep', 'reader opens the outer on a, passes the row without it, then f', ['s:scenario-background', 'c:b.0', 4, 5]],
    ['Deep', 'reader closes the held inner on b, on to c, back to b', [1, 'c:b.0.0', 2, 1]],
    ['Deep', 'reader closes the held outer on b, then c, then d (another failure), back to b', [1, 'c:b.0', 2, 3, 1]],
    ['Deep', 'on b the reader opens the passing second inner, on to c and opens the outer there', [1, 'c:b.0.1', 2, 's:scenario-background', 'c:b.0']],
    ['Deep', 'on d the reader closes the second-level, on to a', [3, 'c:b.0.0', 'c:b.0.1', 0, 's:scenario-background', 'c:b.0']],
    ['Deep', 'reader opens outer on a, comes to b, then closes the Background section on b, then c', ['s:scenario-background', 'c:b.0', 1, 's:scenario-background', 2]],
    ['Deep', 'reader opens outer on a, inner too; on c closes inner; on f, then a', ['s:scenario-background', 'c:b.0', 'c:b.0.0', 2, 'c:b.0.0', 5, 0]],
    ['Deep', 'click the same row twice, then open a composite that draws', ['s:scenario-background', 'c:b.0', 'c:b.0.0', 2, 2, 'c:b.0', 'c:b.0']],
    // Own steps
    ['Own', 'reader opens on a, through b to c', ['c:0', 'c:0.0', 1, 2, 0]],
    ['Own', 'reader closes Steps on a, through the failure', ['s:scenario-steps', 1, 2]],
    ['Own', 'reader closes Steps on b, Failure Result, to c, back', [1, 's:scenario-steps', 's:failure-result', 2, 1]],
    // Blocks
    ['Blocks', 'each row', [1, 2, 3, 0]],
    ['Blocks', 'reader opens Background on a, through the failure, across the block', ['s:scenario-background', 1, 2, 3]],
    ['Blocks', 'reader closes Steps in the first block, to the second block', ['s:scenario-steps', 2, 'read:', 3]],
    ['Blocks', 'all panels after a tour', ['s:scenario-background', 2, 'all:', 1, 'all:', 3, 'all:']],
    // Search
    ['Search', 'word in row b with a first, emptied', ['q:zebraword', 'all:', 'q:', 'all:', 'read:']],
    ['Search', 'on row c the reader leaves Background closed; a search; emptied', [2, 'q:zebraword', 'all:', 'q:', 'all:', 'read:']],
    ['Search', 'on row c the reader closes Steps, a search, emptied, then row a', [2, 's:scenario-steps', 'q:zebraword', 'all:', 'q:', 'all:', 'read:', 0]],
    ['Search', 'reader closes Background and Steps on chosen row, empties', ['q:zebraword', 's:scenario-background', 's:scenario-steps', 'q:', 'read:']],
    ['TwoA', 'word in two scenarios, no reveal; emptied', ['q:lynxword', 'all:', 'q:', 'all:']],
    ['TwoA', 'narrowed to one, widened, emptied', ['q:lynxword twoa', 'all:', 'q:lynxword', 'all:', 'q:', 'all:']],
    ['AllFail', 'every row holds: reader closes on a; b; back', ['s:scenario-background', 1, 0]],
    ['Plain fails', 'a plain scenario', ['read:']],
];
