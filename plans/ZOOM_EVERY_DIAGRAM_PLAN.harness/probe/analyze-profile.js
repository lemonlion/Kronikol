'use strict';
// Summarises a CPU profile and a trace written by float-bench.js --profile=<toggle>.
// usage: node analyze.js <name> [top]
const fs = require('fs');
const path = require('path');
const [name, topArg = '25'] = process.argv.slice(2);
const top = Number(topArg);
const profile = JSON.parse(fs.readFileSync(path.join(__dirname, 'out', 'profile-' + name + '.cpuprofile'), 'utf8'));
const events = JSON.parse(fs.readFileSync(path.join(__dirname, 'out', 'trace-' + name + '.json'), 'utf8'));

const byId = new Map(profile.nodes.map((n) => [n.id, n]));
const parent = new Map();
profile.nodes.forEach((n) => (n.children || []).forEach((c) => parent.set(c, n.id)));
const self = new Map();
for (let i = 0; i < profile.samples.length; i++) self.set(profile.samples[i], (self.get(profile.samples[i]) || 0) + (profile.timeDeltas[i] || 0) / 1000);
const label = (cf) => (cf.functionName || '(anon)') + ' ' + (cf.url ? cf.url.split('/').pop().slice(0, 20) : '') + ':' + (cf.lineNumber + 1);
const fnSelf = new Map(), fnTotal = new Map();
for (const [id, ms] of self) {
  const k = label(byId.get(id).callFrame);
  fnSelf.set(k, (fnSelf.get(k) || 0) + ms);
  // inclusive: every distinct function on the stack
  const seen = new Set();
  for (let x = id; x !== undefined; x = parent.get(x)) {
    const kk = label(byId.get(x).callFrame);
    if (seen.has(kk)) continue; seen.add(kk);
    fnTotal.set(kk, (fnTotal.get(kk) || 0) + ms);
  }
}
console.log('top self time:');
[...fnSelf].sort((a, b) => b[1] - a[1]).slice(0, top).forEach(([k, ms]) => console.log('  ' + ms.toFixed(1).padStart(8) + ' ms  ' + k));
console.log('top inclusive time:');
[...fnTotal].sort((a, b) => b[1] - a[1]).filter(([k]) => !/^\((root|program|idle)\)/.test(k)).slice(0, top).forEach(([k, ms]) => console.log('  ' + ms.toFixed(1).padStart(8) + ' ms  ' + k));
const stackOf = (id) => { const s = []; for (let x = id; x !== undefined; x = parent.get(x)) { const cf = byId.get(x).callFrame; if (cf.functionName !== '(root)') s.push((cf.functionName || '(anon)') + ':' + (cf.lineNumber + 1)); } return s.reverse().slice(-9).join(' > '); };
console.log('heaviest stacks:');
[...self].sort((a, b) => b[1] - a[1]).slice(0, 12).forEach(([id, ms]) => console.log('  ' + ms.toFixed(1).padStart(8) + ' ms  ' + stackOf(id)));

const names = new Map();
for (const e of events) {
  if (e.ph !== 'X' || !e.dur) continue;
  if (!/^(RecalculateStyles|UpdateLayoutTree|Layout|Paint|PrePaint|Layerize|UpdateLayer|HitTest|ParseHTML|FunctionCall|EventDispatch|TimerFire|FireAnimationFrame|IntersectionObserverController::computeIntersections|Commit|v8.run|EvaluateScript|MajorGC|MinorGC|V8.GC_.*|ResizeObserver.*|RunTask|ThreadControllerImpl::RunTask|ParseAuthorStyleSheet|UpdateLayoutTree|InvalidateLayout|ScheduleStyleRecalculation|Decode.*|RasterTask|v8.callFunction)$/.test(e.name)) continue;
  const r = names.get(e.name) || { n: 0, ms: 0, max: 0 };
  r.n++; r.ms += e.dur / 1000; r.max = Math.max(r.max, e.dur / 1000);
  names.set(e.name, r);
}
console.log('trace events:');
[...names].sort((a, b) => b[1].ms - a[1].ms).slice(0, 18).forEach(([k, r]) => console.log('  ' + r.ms.toFixed(1).padStart(8) + ' ms  x' + String(r.n).padEnd(6) + ' max ' + r.max.toFixed(1).padStart(7) + '  ' + k));
// the long RunTasks on the main thread: what is inside each (by top-level event names)
const mainTid = (() => { const c = new Map(); events.forEach((e) => { if (e.name === 'FunctionCall') c.set(e.tid, (c.get(e.tid) || 0) + 1); }); return [...c].sort((a, b) => b[1] - a[1])[0][0]; })();
const tasks = events.filter((e) => e.tid === mainTid && e.ph === 'X' && /RunTask/.test(e.name) && e.dur > 50000).sort((a, b) => a.ts - b.ts);
console.log('main-thread tasks over 50 ms: ' + tasks.length);
tasks.forEach((t) => {
  const inside = events.filter((e) => e.tid === mainTid && e.ph === 'X' && e.ts >= t.ts && e.ts + (e.dur || 0) <= t.ts + t.dur && e !== t && e.dur > 2000);
  const agg = new Map();
  inside.forEach((e) => { if (/RunTask/.test(e.name)) return; const k = e.name + (e.args && e.args.data && e.args.data.functionName ? '(' + e.args.data.functionName + ')' : ''); const r = agg.get(k) || { n: 0, ms: 0 }; r.n++; r.ms += e.dur / 1000; agg.set(k, r); });
  console.log('  task ' + (t.dur / 1000).toFixed(0) + ' ms: ' + [...agg].sort((a, b) => b[1].ms - a[1].ms).slice(0, 8).map(([k, r]) => k + ' ' + r.ms.toFixed(0) + 'ms x' + r.n).join(' | '));
});
