module.exports = [
    ['CallRows', 'G1 diagrams: search, emptied', ['q:alphaword', 'q:']],
    ['CallRows', 'G2 reader closes the diagrams section, next keystroke, emptied', ['q:alphawor', 'd:example-diagrams', 'q:alphaword', 'q:']],
    ['CallPlain', 'G3 plain: reader closes the diagrams section, next keystroke, emptied', ['q:plainwor', 'd:example-diagrams', 'q:plainword', 'q:']],
    ['CallPlain', 'G4 plain: reader closes then opens the diagrams section, emptied', ['q:plainword', 'd:example-diagrams', 'd:example-diagrams', 'q:']],
    ['CallRows', 'G5 reader has the diagrams section open by hand, search, emptied', ['open', 'd:example-diagrams', 'q:alphaword', 'q:']],
    ['CallRows', 'G6 reader closes Background, next keystroke; reader closes diagrams, next keystroke', ['q:alphawo', 's:scenario-background', 'q:alphawor', 'd:example-diagrams', 'q:alphaword', 'q:']],
];
