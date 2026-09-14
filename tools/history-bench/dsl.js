const fs=require('fs');
eval(fs.readFileSync('src/Kronikol/Reports/advanced-search.js','utf8'));
const show=i=>JSON.stringify(advancedSearchTokenise(i));
console.log('--- CLAIM A: is:/age: fail silently (become text tokens) ---');
for (const q of ['is:new','age:>7d','is:new && @slow','flaky:true','status:failed'])
  console.log(q.padEnd(18), 'adv?', String(isAdvancedSearch(q)).padEnd(5), show(q));
console.log('\n--- CLAIM B: $verdict is purely additive ---');
for (const q of ['$flaky','$new','$broke','$failed'])
  console.log(q.padEnd(10), 'adv?', String(isAdvancedSearch(q)).padEnd(5), show(q));
console.log('\n--- What match() does with a verdict the status arg cannot carry ---');
const tags=new Set(['slow']);
console.log('$flaky vs status=Passed  ->', advancedSearchMatch('$flaky','pay with card',tags,'Passed'));
console.log('$failed vs status=Failed ->', advancedSearchMatch('$failed','pay with card',tags,'Failed'));
console.log('is:new vs anything       ->', advancedSearchMatch('is:new','pay with card',tags,'Passed'));
console.log('is:new when text matches ->', advancedSearchMatch('is:new','is:new appears in name',tags,'Passed'));
console.log('\n--- CLAIM C: unknown $ value throws? ---');
try { console.log('$zzz ->', advancedSearchMatch('$zzz','x',tags,'Passed')); } catch(e){ console.log('THREW', e.message); }
console.log('\n--- multi-arg shape: how many args does evaluate take? ---');
console.log('evaluate.length =', advancedSearchEvaluate.length, ' match.length =', advancedSearchMatch.length);
