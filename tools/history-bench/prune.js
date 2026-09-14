const fs = require('fs');
eval(fs.readFileSync('src/Kronikol/Reports/advanced-search.js', 'utf8'));
const files = ['report-search-index.js', 'report-search-function.js']
  .map(f => fs.readFileSync('src/Kronikol/Reports/' + f, 'utf8').replace(/\r\n/g, '\n'));
function grab(name) {                       // slice from the decl to the first column-0 '}'
  for (const s of files) {
    const i = s.indexOf('\nfunction ' + name + '(');
    if (i < 0) continue;
    const j = s.indexOf('\n}', i);
    return s.slice(i + 1, j + 2);
  }
  console.log('MISS', name); return '';
}
for (const fn of ['kronEndsWithOwnMarker','kronRejoinMarkedBreaks','kronNormalizeForSearch','kronNormalizeQueryText','kronTrigramBuckets','kronRowIntoBitset',
                  'kronCandidateBitsetForTerm','kronCandidateDocsForQuery','kronIsDeepEligible',
                  'splitLegacyTagExpression','parseSearchTokensIncludingQuotes','evaluateTagExpression'])
  eval(grab(fn));
const ix = { bitsetBytes: 3, docCount: 24, buckets: 64, offsets: new Array(64).fill(-1), bytes: new Uint8Array(0) }; // every bucket empty
console.log('query'.padEnd(22), 'deep-eligible?'.padEnd(16), 'candidates of 24');
for (const q of ['$flaky', '@slow', '$flaky && @slow', 'checkout', 'checkout && $flaky', '$flaky || checkout', 'ab'])
  console.log(q.padEnd(22), String(kronIsDeepEligible(q)).padEnd(16), kronCandidateDocsForQuery(ix, q).length);
