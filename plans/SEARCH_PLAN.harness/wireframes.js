// Builds Appendix B of SEARCH_PLAN.md with every box the same width, and writes it over the old appendix.
const fs = require('fs');
const file = process.argv[2];

const pad = (s, n) => s + ' '.repeat(Math.max(0, n - s.length));
// a row of a box: left text and right text inside |...|, inner width w
const row = (w, left, right = '', edge = '|') => {
    const gap = w - left.length - right.length;
    if (gap < 1) throw new Error('too long: ' + left + ' / ' + right);
    return edge + left + ' '.repeat(gap) + right + '|';
};
const rule = (w, ch = '-', corner = '+') => corner + ch.repeat(w) + corner;
const out = [];
const block = lines => { out.push('```', ...lines.map(l => l.replace(/\s+$/, '')), '```', ''); };

out.push('## Appendix B. Wireframes', '');
out.push('At 1280, light. The title, the totals (214 scenarios, 73 features) and the tab names are the measured report\'s;');
out.push('the other names and counts are made up to show each state. Sizes in CSS pixels. Key: `(o)` the magnifier, `(x)` a');
out.push('clear button, `[ C ]` the theme switch, `[/]` a key cap, `[failed]` a value on the selected tint, `**word**` a match');
out.push('at 700, `==word==` a match in a `<mark>`, `||` at a row\'s left the 4 px accent bar, `v` and `x` a passed and a failed');
out.push('glyph, `#` a feature, `(*)` a status dot, `(+)` a plus-circle row, `[...]` the kebab.', '');

// B.1
out.push('**B.1 The title line, nothing searched**', '');
block([
    ' Breakfast Provider (ReqNRoll) Test Run Report             [ (o)  Type [/] to search           ] [ C ]',
    ' h1 22 px, weight 600                                      search box 384 x 34, 8 px corners   gap  switch',
    '                                                                                               8 px 34 x 34',
]);

// B.2
out.push('**B.2 The title line, a query applied; and at 390**', '');
block([
    ' Breakfast Provider (ReqNRoll) Test Run Report             [ (o)  pancake status:[failed]  | (x) ] [ C ]',
    '',
    ' Breakfast Provider            [(o)] [ C ]   390 px: two 34 x 34 buttons; (o) takes the selected look',
    ' (ReqNRoll) Test Run Report                  while a query is applied, and its clear is in the builder',
]);

// B.3 and B.4: the builder
const W = 80, ind = '            ';
out.push('**B.3 The builder, text typed** (its input over the search box, the page dimmed behind)', '');
block([
    ind + rule(W),
    ind + row(W, ' [ (o) pancake', '(x) ] '),
    ind + row(W, ''),
    ind.slice(1) + row(W, ' (o) pancake', 'Search ', '||') + '   <- the active row: the hover',
    ind + row(W, '  (o) feature:"Pancakes Creation" pancake', 'Search in this feature ') + '      ground and the 4 px bar',
    ind + row(W, '  JUMP TO'),
    ind + row(W, '  v  A valid **pan**cake request returns a batch   Pancakes Creation', 'Jump to '),
    ind + row(W, '  x  **Pan**cakes over the daily limit are refused   Pancakes Creation', 'Jump to '),
    ind + row(W, '  #  **Pan**cakes Creation', 'Jump to '),
    ind + rule(W, '-', '|'),
    ind + row(W, '  Search syntax tips'),
    ind + rule(W),
]);
out.push('The jump rows mark the letters typed at 600 (here only "pan" shown, for width).', '');

out.push('**B.4 The builder, a key\'s values**', '');
block([
    ind + rule(W),
    ind + row(W, ' [ (o) pancake status:', '(x) ] '),
    ind + row(W, ''),
    ind + row(W, '  STATUSES'),
    ind + row(W, '  (*) passed      208', 'Autocomplete '),
    ind + row(W, '  (*) failed        3', 'Autocomplete '),
    ind + row(W, '  (*) skipped       2', 'Autocomplete '),
    ind + row(W, '  (*) bypassed      1', 'Autocomplete '),
    ind + rule(W, '-', '|'),
    ind + row(W, '  Search syntax tips'),
    ind + rule(W),
]);

// B.5: the Search tab, two columns zipped
out.push('**B.5 The Search tab**', '');
const S = 28;
const side = [
    '+-- SHOW ' + '-'.repeat(S - 8) + '+',
    row(S - 1, 'Scenarios', '[12] ', '||'),
    row(S, ' Features', '[4] '),
    row(S, ' MATCHED IN'),
    row(S, ' Names', '3 '),
    row(S, ' Steps', '9 '),
    row(S, ' Diagrams', '7 '),
    row(S, ' Errors', '1 '),
    rule(S, '-', '|'),
    row(S, ' STATUS'),
    row(S, ' (*) Passed', '11 '),
    row(S, ' (*) Failed', '1 '),
    row(S, ' TAGS'),
    row(S, ' smoke', '8 '),
    row(S, ' api', '5 '),
    row(S, ' (+) More tags...'),
    row(S, ' DEPENDENCIES'),
    row(S, ' Payment Service', '3 '),
    row(S, ' ADVANCED'),
    row(S, ' (+) Feature'),
    row(S, ' (+) Tag'),
    row(S, ' (+) ...'),
    row(S, ' Search syntax tips'),
    rule(S),
    '  296 px, sticks under the bar',
];
const R = 74, M = R - 6;
const res = [
    pad(' 12 results (85 ms)', R - 28) + 'SORT [ Best match v ]  [...]',
    rule(R),
    row(R, ' v Pancakes Creation / A valid **pancake** request returns', '1.2 s '),
    row(R, '   [smoke] [api] · Happy path'),
    row(R, '  ' + rule(M), ' '),
    row(R, '  ' + row(M, ' STEP     Given a valid **pancake** request'), ' '),
    row(R, '  ' + row(M, ' DIAGRAM  POST /api/==pancake==s {"flour": 200, ...'), ' '),
    row(R, '  ' + row(M, ' DIAGRAM  {"id": 42, "type": "==pancake==", ...'), ' '),
    row(R, '  ' + '|' + ' -'.repeat(M / 2) + '|', ' '),
    row(R, '  ' + row(M, ' Show 4 more matches'), ' '),
    row(R, '  ' + rule(M), ' '),
    rule(R),
    '  ... nine more results ...',
    '',
    pad('', 22) + '< Previous  [1]  2  Next >',
    '',
    ' Press / to search again and change the query.',
    '',
    '',
    '',
    '',
    '',
    '  fluid, 24 px from the sidebar',
];
const lines5 = [' [ Features | Timeline | Components | History | (o) Search 12 ]       Filtered 12 of 214  Clear All  [ Filters ]'];
for (let i = 0; i < Math.max(side.length, res.length); i++)
    lines5.push(' ' + pad(side[i] || '', S + 2) + '  ' + (res[i] || ''));
block(lines5);

// B.6: at 390
out.push('**B.6 The Search tab at 390**', '');
const P = 40;
block([
    ' [ …onents | History | (o) Search 12 ]   <- the row scrolled to the current tab (4.5.9)',
    ' [ Filters ]  Filtered 12 of 214  Clear All',
    ' [ Scenarios 12 v ]  [...]          <- types and places; the facets are the box\'s (Q17)',
    ' 12 results (85 ms)',
    ' ' + rule(P),
    ' ' + row(P, ' v A valid **pancake** request', '1.2 s '),
    ' ' + row(P, '   Pancakes Creation'),
    ' ' + row(P, '   STEP'),
    ' ' + row(P, '   Given a valid **pancake** request'),
    ' ' + rule(P),
    '',
    ' Further down the page:                [(o)]   <- floating search button (B15, Q7); its',
    '                                       [ ^ ]      builder holds Filters at its foot',
]);

// B.7: the filters
out.push('**B.7 The filters**', '');
const F = 100;
block([
    ' At rest, closed:',
    ' [ Features 73 | Timeline | Components | History ]          All 214 scenarios, 73 features  [ Filters ]',
    '',
    ' Open (two filters on), at rest or pinned:',
    ' +-- FILTERING ' + '-'.repeat(F - 13) + '+',
    ' ' + row(F, '', 'Filtered 12 of 214 scenarios · Clear All · Export Filtered HTML · Export Filtered CSV  (x) '),
    ' ' + row(F, ' Search: pancake  (x)            <- the search applied, with a clear of its own (F10)'),
    ' ' + row(F, ' STATUS  [Passed] Failed Skipped Bypassed   DURATION >= P50 P90 P95 P99 Custom   HAPPY PATHS ...'),
    ' ' + row(F, ' TAGS    ANY  [@smoke 12]  @api 40  ...'),
    ' ' + row(F, ' DEPENDENCIES  ANY  Payment Service 30  ...'),
    ' ' + rule(F),
    ' [ Features 73 | Timeline | Components | History ]' + pad('', 39) + '[ Filters (2) ]',
    '   the bar\'s readout and Clear All give way to the box\'s header (4.7, F6); pinned, the box takes at most',
    '   60% of the window and scrolls inside',
    '',
    ' Phone, closed:',
    ' [ Features 73 | Timeline | Compo… ]',
    ' [ Filters (2) ]  Filtered 12 of 214  Clear All     <- its own line under the tabs',
]);

// B.8: a line followed to its match
out.push('**B.8 A line followed to its match** (a database call\'s SQL; Databases was off, the note collapsed)', '');
const G = 88;
block([
    ' v A valid pancake request returns a batch                                         1.2 s',
    '   [ Sequence | Activity | Flame ]',
    '   Turned on to show this match: Databases. The note is expanded.      <- until the reader leaves the scenario',
    '   ' + rule(G),
    '   ' + row(G, '  Pancakes API                    Inventory DB'),
    '   ' + row(G, '       |--------- SELECT -------------->|'),
    '   ' + row(G, '       |  #===============================================#'),
    '   ' + row(G, '       |  # SELECT id, flour FROM ==pancake==_batches      #  <- the place: 2 px'),
    '   ' + row(G, '       |  # WHERE id = @id                                 #     accent outline, it'),
    '   ' + row(G, '       |  #===============================================#     pulses on arrival'),
    '   ' + row(G, '       |<-------- 1 row ----------------|'),
    '   ' + rule(G),
    '   status line: "Match in the query of SELECT, in the diagram. Databases turned on, the note expanded."',
]);

const text = fs.readFileSync(file, 'utf8');
const at = text.indexOf('## Appendix B. Wireframes');
if (at < 0) throw new Error('no appendix');
const next = out.join('\n').replace(/\n+$/, '\n');
fs.writeFileSync(file, text.slice(0, at) + next);
console.log(next);
