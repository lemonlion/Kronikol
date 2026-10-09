'use strict';
// The follow-up matrix, one browser at a time: node run-follow.js [browsers] (default chromium,chrome)
const { spawnSync } = require('child_process');
const path = require('path');
const F = path.join(__dirname, 'follow.js');
const OFF = '--no-opt --no-maglev';
function run(cmd, cfg) {
  const t0 = Date.now();
  console.log(`\n=== ${cmd} ${JSON.stringify(cfg)}`);
  const r = spawnSync(process.execPath, [F, cmd, JSON.stringify(cfg)], { stdio: 'inherit' });
  console.log(`=== done in ${Math.round((Date.now() - t0) / 1000)} s, exit ${r.status}`);
}
const browsers = (process.argv[2] || 'chromium,chrome').split(',');
const shapes = [
  ['loop', 100, 1471], ['partition', 100, 1471], ['bar-token', 100, 1400], ['bar-rich', 100, 13600],
  ['seq-entity', 50, 2100], ['seq-database', 50, 2100], ['comp-database', 50, 2100], ['comp-system', 50, 2100],
  ['activity', 50, 2100], ['stats-edge', 50, 1500],
];
for (const b of browsers) {
  for (const [tag, jsflags, reps] of [['off', OFF, 2], ['jit', '', 3]]) {
    const label = `K-${b}-w4-${tag}`;
    run('once', { label, browser: b, jsflags, shape: 'stats-edge-asis', values: [0], reps });
    for (const [shape, lo, hi] of shapes) run('bisect', { label, browser: b, jsflags, shape, lo, hi, reps });
  }
}
for (const b of browsers) {
  run('legacy', { label: `L-${b}-jitless`, browser: b, jsflags: '--jitless' });
  run('legacy', { label: `L-${b}-off`, browser: b, jsflags: OFF });
}
console.log('follow matrix finished');
