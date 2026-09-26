'use strict';
// Second P3 audit (search): which form of a captured backslash a step bar can carry on both engines. The bar is one
// statement whose display lines are joined by a literal \n; the shipped escaper puts <U+200B> after each backslash,
// which the Java engine reads as `\<` (it paints "U+200B>"). Writes cand7.json for cand-probe.js.
const ch = n => String.fromCharCode(n);
const BS = ch(92), ZWC = ch(0x200b);
// [captured, the rest of the escaper's output around it]: < is <U+003C>, ~ is <U+007E>, | in a cell is <U+007C>.
const inputs = [
    ['C:' + BS + 'temp' + BS + 'new', b => 'C:' + b + 'temp' + b + 'new'],
    ['a' + BS + 'nb', b => 'a' + b + 'nb'],
    ['a' + BS + BS + 'b', b => 'a' + b + b + 'b'],
    ['x' + BS, b => 'x' + b],
    [BS + '<b>', b => b + '<U+003C>b>'],
    [BS + '~', b => b + '<U+007E>'],
    [BS + '&#39;', b => b + '<U+0026><U+200B>#39;'],
];
const forms = {
    codePointEsc: '<U+005C><U+200B>',
    codePointZw: '<U+005C>' + ZWC,
};
const cases = [];
for (const ctx of ['doc', 'cell', 'bar'])
    for (const [form, b] of Object.entries(forms))
        for (const [raw, write] of inputs)
            cases.push([ctx, write(b), raw, form]);
require('fs').writeFileSync(require('path').join(__dirname, 'cand7.json'), JSON.stringify(cases));
console.log(cases.length + ' cases');
