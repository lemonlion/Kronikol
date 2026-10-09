'use strict';
// Second pass, one browser at a time: the legacy-bar controls, the half-steps of the name-driven shapes (10-character
// resolution on the declaration line), more repeats where the JIT-on edge was mixed, and the legacy cases with the JIT on.
const { spawnSync } = require('child_process');
const path = require('path');
const F = path.join(__dirname, 'follow.js');
const OFF = '--no-opt --no-maglev';
function run(cmd, cfg) {
  console.log(`\n=== ${cmd} ${JSON.stringify(cfg)}`);
  const r = spawnSync(process.execPath, [F, cmd, JSON.stringify(cfg)], { stdio: 'inherit' });
  console.log(`=== exit ${r.status}`);
}
// 1. Why the legacy bars drew: display breaks in the filler, or the stereotype?
run('once', { label: 'M-chromium-w4-off-barctl', browser: 'chromium', jsflags: OFF, shape: 'bar-legacy-label', values: [880, 1400], reps: 2 });
run('bisect', { label: 'M-chromium-w4-off-barctl', browser: 'chromium', jsflags: OFF, shape: 'bar-legacy-label-unbroken', lo: 100, hi: 1400, reps: 2 });
run('bisect', { label: 'M-chromium-w4-off-barctl', browser: 'chromium', jsflags: OFF, shape: 'bar-token-nostereo', lo: 100, hi: 1400, reps: 2 });
// 2. Half-steps on the name-driven shapes, and the mixed JIT-on edges again.
for (const b of ['chromium', 'chrome']) {
  const off = `K-${b}-w4-off`, jit = `K-${b}-w4-jit`;
  run('once', { label: off, browser: b, jsflags: OFF, shape: 'seq-entity', values: [275], reps: 2 });
  run('once', { label: off, browser: b, jsflags: OFF, shape: 'seq-database', values: [275], reps: 2 });
  run('once', { label: off, browser: b, jsflags: OFF, shape: 'comp-database', values: [335], reps: 2 });
  run('once', { label: off, browser: b, jsflags: OFF, shape: 'comp-system', values: [305], reps: 2 });
  run('once', { label: jit, browser: b, jsflags: '', shape: 'comp-database', values: [725], reps: 3 });
  run('once', { label: jit, browser: b, jsflags: '', shape: 'comp-system', values: [685], reps: 3 });
}
run('once', { label: 'K-chromium-w4-jit', browser: 'chromium', jsflags: '', shape: 'seq-entity', values: [535], reps: 3 });
run('once', { label: 'K-chromium-w4-jit', browser: 'chromium', jsflags: '', shape: 'seq-database', values: [510, 515, 520, 525, 530, 535, 540], reps: 3 });
run('once', { label: 'K-chrome-w4-jit', browser: 'chrome', jsflags: '', shape: 'seq-entity', values: [545], reps: 3 });
run('once', { label: 'K-chrome-w4-jit', browser: 'chrome', jsflags: '', shape: 'seq-database', values: [540, 545, 550, 560], reps: 3 });
run('once', { label: 'K-chrome-w4-jit', browser: 'chrome', jsflags: '', shape: 'loop', values: [990, 1000, 1010, 1020, 1030, 1040], reps: 3 });
run('once', { label: 'K-chrome-w4-jit', browser: 'chrome', jsflags: '', shape: 'partition', values: [990, 1000, 1010, 1020, 1030], reps: 3 });
// 3. The legacy cases with the JIT on (the 09-25 run had no flags).
for (const b of ['chromium', 'chrome']) run('legacy', { label: `L-${b}-jit`, browser: b, jsflags: '' });
console.log('follow2 finished');
