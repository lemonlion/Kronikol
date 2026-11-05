const fs = require('fs');
const file = '<tree>/tests/Kronikol.Tests/TestData/ReqNRoll/BackgroundShapes.ndjson';
const env = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map(l => JSON.parse(l));
const kinds = {};
env.forEach(e => Object.keys(e).forEach(k => kinds[k] = (kinds[k] || 0) + 1));
console.log(JSON.stringify(kinds));
const docs = env.filter(e => e.gherkinDocument).map(e => e.gherkinDocument);
const pickles = env.filter(e => e.pickle).map(e => e.pickle);
console.log('docs', docs.length, 'pickles', pickles.length, 'unique pickle names', new Set(pickles.map(p => p.name)).size);
const dup = {}; pickles.forEach(p => dup[p.name] = (dup[p.name] || 0) + 1);
console.log('duplicates', Object.entries(dup).filter(([k, v]) => v > 1));
for (const d of docs) {
  const f = d.feature;
  const bg = f.children.filter(c => c.background).map(c => c.background.steps.map(s => s.text));
  const scen = f.children.filter(c => c.scenario).length;
  const rules = f.children.filter(c => c.rule).map(c => ({ rule: c.rule.name, bg: c.rule.children.filter(x => x.background).map(x => x.background.steps.length), scen: c.rule.children.filter(x => x.scenario).length }));
  console.log(f.name.padEnd(46), 'bg', JSON.stringify(bg).slice(0, 80), 'scen', scen, rules.length ? JSON.stringify(rules).slice(0, 100) : '');
}
const names = pickles.map(p => p.name);
console.log(names.join(' | '));
// shape 11
const d11 = docs.find(d => d.feature.name.startsWith('11') || JSON.stringify(d).includes('"11 one"'));
if (d11) console.log(JSON.stringify(d11.feature.children.map(c => c.background ? { bg: c.background.steps.map(s => s.text) } : c.scenario ? { s: c.scenario.name, steps: c.scenario.steps.map(s => s.text) } : {})));
