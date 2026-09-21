'use strict';
// Offline slicing of the .cpuprofile files written by profile-save.js (UNOBFUSCATED TeaVM build, so names are readable).
// usage: node profile-analyze.js <profile-dir> [file-prefix ...]
// env: TOP=n rows per table (default 30); FOCUS=<fn>[,<fn>...] print caller chains for these functions (DEPTH=n frames, default 6);
//      OWNER=1 print, for every runtime/classlib function, which PlantUML frame it was running under.
// Sections: run summary, category split, top self, top inclusive, runtime cost attributed to the nearest PlantUML caller.
const fs = require('fs'), path = require('path');
const dir = process.argv[2];
const prefixes = process.argv.slice(3);
const TOP = Number(process.env.TOP || 30);
const DEPTH = Number(process.env.DEPTH || 6);
const FOCUS = (process.env.FOCUS || '').split(',').filter(Boolean);
if (!dir) { console.error('usage: node profile-analyze.js <profile-dir> [file-prefix ...]'); process.exit(2); }

// TeaVM mangles net.sourceforge.plantuml.x.y.Class_method to nspxy_Class_method; java.lang -> jl_, java.util -> ju_,
// java.util.regex -> jur_, org.teavm.classlib.impl.* -> otci*_; $rt_* and Long_* are the JS runtime.
function category(name, url) {
  if (name === '(garbage collector)') return 'GC';
  if (name === '(program)') return 'browser (program)';
  if (name === '(idle)') return null; // waiting on the MutationObserver, not render work
  if (!url) return 'browser native (DOM/canvas)';
  if (/viz-global/.test(url)) return 'viz.js';
  if (/^nsp/.test(name)) return 'PlantUML code';
  if (/^\$rt_|^Long_|^JavaArray$|^\$rt$|^TeaVM/.test(name)) return 'TeaVM runtime ($rt_, Long_)';
  if (/^jur_/.test(name)) return 'classlib java.util.regex';
  if (/^(jl|ju|jt|jm|jn|ji|jnc|juc|juf|jus|jtf|jlr|jli)_|^jl_Object$/.test(name)) return 'classlib java.*';
  if (/^otc|^otj|^otp|^otr|^oti/.test(name)) return 'classlib org.teavm.*';
  if (/\$js_body\$/.test(name)) return 'PlantUML code';
  return 'other JS (' + (name.startsWith('(') ? name : 'engine glue') + ')';
}
const isPlantUml = n => /^nsp/.test(n);

const self = new Map(), incl = new Map(), cats = new Map(), owner = new Map(), ownerDetail = new Map();
const focusChains = new Map(FOCUS.map(f => [f, new Map()]));
let total = 0, renders = 0, renderMs = 0;
const perFile = new Map();

const names = fs.readdirSync(dir).filter(f => f.endsWith('.cpuprofile') && (prefixes.length === 0 || prefixes.some(p => f.startsWith(p + '.'))));
for (const fn of names) {
  const { file, renderMs: ms, profile } = JSON.parse(fs.readFileSync(path.join(dir, fn), 'utf8'));
  renders++; renderMs += ms;
  const pf = perFile.get(file) || { n: 0, ms: 0 }; pf.n++; pf.ms += ms; perFile.set(file, pf);
  const byId = new Map(profile.nodes.map(n => [n.id, n]));
  const parentOf = new Map();
  for (const n of profile.nodes) for (const c of (n.children || [])) parentOf.set(c, n.id);
  for (const n of profile.nodes) {
    if (!n.hitCount) continue;
    const name = n.callFrame.functionName || '(anonymous)';
    const cat = category(name, n.callFrame.url);
    if (cat === null) continue;
    total += n.hitCount;
    self.set(name, (self.get(name) || 0) + n.hitCount);
    cats.set(cat, (cats.get(cat) || 0) + n.hitCount);
    const seen = new Set(); const chain = [];
    let id = n.id, own = null;
    while (id !== undefined) {
      const nn = byId.get(id); const nk = nn.callFrame.functionName || '(anonymous)';
      if (!seen.has(nk)) { seen.add(nk); incl.set(nk, (incl.get(nk) || 0) + n.hitCount); }
      if (id !== n.id && chain.length < DEPTH) chain.push(nk);
      if (own === null && isPlantUml(nk)) own = nk;
      id = parentOf.get(id);
    }
    if (!isPlantUml(name) && cat !== 'browser (program)') {
      const o = own || '(no PlantUML frame)';
      owner.set(o, (owner.get(o) || 0) + n.hitCount);
      const dk = o + '  <=  ' + name;
      ownerDetail.set(dk, (ownerDetail.get(dk) || 0) + n.hitCount);
    }
    if (focusChains.has(name)) { const m = focusChains.get(name); const ck = chain.join(' <- '); m.set(ck, (m.get(ck) || 0) + n.hitCount); }
  }
}

const pct = h => (100 * h / total).toFixed(1).padStart(5) + '%';
const msOf = h => (h / total * renderMs / renders).toFixed(1).padStart(7) + ' ms/render';
const table = (title, m, n = TOP) => { console.log('\n=== ' + title); for (const [k, h] of [...m].sort((a, b) => b[1] - a[1]).slice(0, n)) console.log(pct(h) + msOf(h) + '  ' + k.slice(0, 170)); };

console.log(JSON.stringify({ dir, prefixes, renders, avgRenderMs: Math.round(renderMs / renders), samples: total }));
for (const [f, v] of perFile) console.log('  ' + f.padEnd(28) + (v.ms / v.n).toFixed(0).padStart(6) + ' ms avg over ' + v.n);
table('category split (self time, idle excluded)', cats, 20);
table('top SELF time', self);
table('top INCLUSIVE time', incl);
table('non-PlantUML self time attributed to the nearest PlantUML caller', owner);
if (process.env.OWNER === '1') table('owner <= runtime function pairs', ownerDetail, TOP * 2);
for (const [f, m] of focusChains) table('caller chains of ' + f, m, 15);
