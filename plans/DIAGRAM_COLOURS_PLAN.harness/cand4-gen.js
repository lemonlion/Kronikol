'use strict';
// Second P3 audit: the escape forms proposed for each hole, in every context the emitter writes them into.
// [context, escaped text, what must paint]. Writes cand4.json for cand-probe.js.
const ch = n => String.fromCharCode(n);
const BS = ch(92), ZW = '<U+200B>', LS = ch(0x2028), PS = ch(0x2029), NEL = ch(0x85);
const cases = [];
const all = ['body', 'label', 'link', 'doc', 'bar', 'hnote'];
// A. A captured literal code point: the `<` as a code point and a zero-width space before the U.
for (const ctx of all) {
    cases.push([ctx, '<U+003C>' + ZW + 'U+0041>', '<U+0041>']);
    cases.push([ctx, 'R says <U+003C>' + ZW + 'U+00E9>t', 'R says <U+00E9>t']);
    cases.push([ctx, '<U+003C>' + ZW + 'U+000A>', '<U+000A>']);
}
// Lower-case hex: does the engine decode it at all? (The readers do.)
for (const ctx of all) cases.push([ctx, '~<U+00e9>', '<U+00e9>']);
for (const ctx of all) cases.push([ctx, '<U+003C>U+00e9>', '<U+00e9>']);
// B. Backslash-t, and a backslash before a tilde escape: a zero-width space after the backslash.
cases.push(['body', 'C:' + BS + ZW + 'temp' + BS + 'new', 'C:' + BS + 'temp' + BS + 'new']);
cases.push(['body', 'C:' + BS + BS + ZW + 'temp', 'C:' + BS + BS + 'temp']);
cases.push(['body', '"line1' + BS + 'nline2' + BS + ZW + 'tend"', '"line1' + BS + 'nline2' + BS + 'tend"']);
cases.push(['body', "grep '" + BS + ZW + '~<word' + BS + ">'", "grep '" + BS + '<word' + BS + ">'"]);
cases.push(['body', BS + ZW + '~[~[x]]', BS + '[[x]]']);
cases.push(['body', 'a' + BS + ZW + '~*~*b~*~*', 'a' + BS + '**b**']);
cases.push(['hnote', 'C:' + BS + ZW + 'temp', 'C:' + BS + 'temp']);
// C. A line opening with |_ (creole's tree item).
cases.push(['body', '|_x', '|_x']);
cases.push(['body', '<U+007C>_x', '|_x']);
cases.push(['body', '  <U+007C>_ nested', '|_ nested']);
cases.push(['body', '<U+007C>_', '|_']);
cases.push(['body', '|_', '|_']);
cases.push(['body', '| _x', '| _x']);
cases.push(['body', '|x', '|x']);
cases.push(['body', '|', '|']);
// D. Line separators where the statement is one line: as code points.
for (const [name, c] of [['LS', '<U+2028>'], ['PS', '<U+2029>'], ['NEL', '<U+0085>']])
    for (const ctx of ['label', 'link', 'doc', 'bar'])
        cases.push([ctx, 'a' + c + 'b', 'a' + { LS, PS, NEL }[name] + 'b']);
// The test delimiter's own form.
cases.push(['bar', 'Test Name<U+2028>more', 'Test Name' + LS + 'more']);
require('fs').writeFileSync(require('path').join(__dirname, 'cand4.json'), JSON.stringify(cases));
console.log(cases.length + ' cases');
