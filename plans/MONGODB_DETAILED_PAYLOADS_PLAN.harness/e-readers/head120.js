// What the first 120 characters (Failures.md Call column; the history head's cut) hold in each form.
const doc = { insert: 'orders', ordered: true, documents: [{ _id: { $oid: '65a1b2c3d4e5f6a7b8c9d0e1' }, status: 'Ready', amount: { $numberDecimal: '1000.50' }, at: { $date: '2024-01-01T00:00:00Z' } }] };
const compact = JSON.stringify(doc);
// The MongoDB driver's spacing: `{ "a" : 1, "b" : [{ "c" : 2 }] }`
let spaced = '';
let inStr = false;
for (let i = 0; i < compact.length; i++) {
  const c = compact[i];
  if (inStr) { spaced += c; if (c === '\\') { spaced += compact[++i]; } else if (c === '"') inStr = false; continue; }
  if (c === '"') { inStr = true; spaced += c; continue; }
  if (c === ':') spaced += ' : ';
  else if (c === ',') spaced += ', ';
  else if (c === '{') spaced += (compact[i + 1] === '}' ? '{' : '{ ');
  else if (c === '}') spaced += (compact[i - 1] === '{' ? '}' : ' }');
  else spaced += c;
}
const cut = t => (t.length <= 120 ? t : t.slice(0, 120) + '…');
console.log('compact', compact.length, cut(compact));
console.log('spaced ', spaced.length, cut(spaced));
