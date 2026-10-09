'use strict';
// Runs the measurement matrix one step at a time (never two browsers at once): node run-matrix.js <phase> [...]
// Each step is `node measure.js <cmd> <json>`; its output goes to stdout and logs/<label>.jsonl.
const { spawnSync } = require('child_process');
const path = require('path');
const M = path.join(__dirname, 'measure.js');
const OFF = '--no-opt --no-maglev';
function run(cmd, cfg) {
  const t0 = Date.now();
  console.log(`\n=== ${cmd} ${JSON.stringify(cfg)}`);
  const r = spawnSync(process.execPath, [M, cmd, JSON.stringify(cfg)], { stdio: 'inherit' });
  console.log(`=== done in ${Math.round((Date.now() - t0) / 1000)} s, exit ${r.status}`);
}
const CHROMIUMS = (process.env.BROWSERS || 'chrome,chromium').split(',');
const phases = {
  // 1a, 1c, 1d: cold, the two main shapes.
  A() {
    for (const b of CHROMIUMS) {
      run('bisect', { label: `A-${b}-w4-jit`, browser: b, page: 'w4', mode: 'cold', shape: 'unbroken', lo: 300, hi: 1979, reps: 3 });
      run('bisect', { label: `A-${b}-w4-jit`, browser: b, page: 'w4', mode: 'cold', shape: 'real', lo: 300, hi: 1979, reps: 3 });
      run('bisect', { label: `A-${b}-w4-off`, browser: b, page: 'w4', mode: 'cold', jsflags: OFF, shape: 'unbroken', lo: 300, hi: 1979, reps: 2 });
      run('bisect', { label: `A-${b}-w4-off`, browser: b, page: 'w4', mode: 'cold', jsflags: OFF, shape: 'real', lo: 300, hi: 1979, reps: 2 });
      run('bisect', { label: `A-${b}-w0-jit`, browser: b, page: 'w0', mode: 'cold', shape: 'unbroken', lo: 300, hi: 2000, reps: 3 });
      run('bisect', { label: `A-${b}-w0-jit`, browser: b, page: 'w0', mode: 'cold', shape: 'real', lo: 300, hi: 1979, reps: 3 });
      run('bisect', { label: `A-${b}-w0-off`, browser: b, page: 'w0', mode: 'cold', jsflags: OFF, shape: 'unbroken', lo: 300, hi: 1979, reps: 2 });
      run('bisect', { label: `A-${b}-w0-off`, browser: b, page: 'w0', mode: 'cold', jsflags: OFF, shape: 'real', lo: 300, hi: 1979, reps: 2 });
    }
  },
  // The main thread with the JIT on, every 100 characters (a bisection only reads its two ends).
  A2() {
    for (const b of CHROMIUMS) {
      run('scan', { label: `A2-${b}-w0-jit-scan`, browser: b, page: 'w0', mode: 'cold', shape: 'unbroken', from: 300, to: 2000, step: 100, reps: 1 });
      run('scan', { label: `A2-${b}-w0-jit-scan`, browser: b, page: 'w0', mode: 'cold', shape: 'real', from: 300, to: 1979, step: 100, reps: 1 });
    }
  },
  // 3: the shapes (arrow form, wrapping, content) in workers, cold, JIT on and optimizers off.
  B() {
    for (const b of CHROMIUMS) {
      for (const [tag, jsflags, reps] of [['jit', '', 3], ['off', OFF, 2]]) {
        run('bisect', { label: `B-${b}-w4-${tag}`, browser: b, page: 'w4', mode: 'cold', jsflags, shape: 'plain-unbroken', lo: 300, hi: 1970, reps });
        run('bisect', { label: `B-${b}-w4-${tag}`, browser: b, page: 'w4', mode: 'cold', jsflags, shape: 'plain-wrapped', lo: 300, hi: 1970, reps });
        run('bisect', { label: `B-${b}-w4-${tag}`, browser: b, page: 'w4', mode: 'cold', jsflags, shape: 'xfill', lo: 300, hi: 1979, reps });
        run('cases', { label: `B-${b}-w4-${tag}-short`, browser: b, page: 'w4', mode: 'cold', jsflags, cases: [{ shape: 'short-lines', L: 832 }], reps });
      }
      run('cases', { label: `B-${b}-w0-off-short`, browser: b, page: 'w0', mode: 'cold', jsflags: OFF, cases: [{ shape: 'short-lines', L: 832 }], reps: 2 });
    }
  },
  // 5: the emitter's own pages for N statements.
  C() {
    const ns = [10, 15, 20, 30, 40, 44, 48, 56, 66].map(n => ({ n }));
    for (const b of CHROMIUMS) {
      run('cases', { label: `C-${b}-w4-jit-realN`, browser: b, page: 'w4', mode: 'cold', cases: ns, reps: 3 });
      run('cases', { label: `C-${b}-w4-off-realN`, browser: b, page: 'w4', mode: 'cold', jsflags: OFF, cases: ns, reps: 2 });
      run('cases', { label: `C-${b}-w0-jit-realN`, browser: b, page: 'w0', mode: 'cold', cases: ns, reps: 1 });
      run('cases', { label: `C-${b}-w0-off-realN`, browser: b, page: 'w0', mode: 'cold', jsflags: OFF, cases: ns, reps: 2 });
    }
  },
  // 1b: warm workers.
  D() {
    for (const b of CHROMIUMS) {
      run('bisect', { label: `D-${b}-w4-warm`, browser: b, page: 'w4', mode: 'warm', shape: 'unbroken', lo: 1000, hi: 1979, reps: 3 });
      run('bisect', { label: `D-${b}-w4-warm`, browser: b, page: 'w4', mode: 'warm', shape: 'real', lo: 1000, hi: 1979, reps: 3 });
    }
  },
  // 1b, again: the warm edge was mixed, so every length near it five more times.
  D2() {
    const near = { chrome: [1550, 1580, 1590, 1600, 1610, 1620, 1630, 1650, 1700, 1750], chromium: [1600, 1620, 1630, 1640, 1650, 1660, 1680, 1700, 1750, 1800] };
    for (const b of CHROMIUMS)
      run('cases', { label: `D2-${b}-w4-warm-near`, browser: b, page: 'w4', mode: 'warm', cases: near[b].map(L => ({ shape: 'real', L })), reps: 5 });
  },
  // 2: Firefox and WebKit.
  E() {
    for (const b of ['firefox', 'webkit']) {
      for (const pg of ['w4', 'w0']) {
        run('bisect', { label: `E-${b}-${pg}-jit`, browser: b, page: pg, mode: 'cold', shape: 'unbroken', lo: 300, hi: 2000, reps: 3 });
        run('bisect', { label: `E-${b}-${pg}-jit`, browser: b, page: pg, mode: 'cold', shape: 'real', lo: 300, hi: 1979, reps: 3 });
        run('scan', { label: `E-${b}-${pg}-jit-scan`, browser: b, page: pg, mode: 'cold', shape: 'real', from: 300, to: 1979, step: 100, reps: 1 });
      }
    }
  },
  // 2, bonus: the JITs off where a pref or an environment variable can turn them off.
  E2() {
    const ffOff = { 'javascript.options.blinterp': false, 'javascript.options.baselinejit': false, 'javascript.options.ion': false, 'javascript.options.native_regexp': false };
    for (const pg of ['w4', 'w0']) {
      run('bisect', { label: `E2-firefox-${pg}-nojit`, browser: 'firefox', page: pg, mode: 'cold', prefs: ffOff, shape: 'unbroken', lo: 300, hi: 2000, reps: 2 });
      run('bisect', { label: `E2-firefox-${pg}-nojit`, browser: 'firefox', page: pg, mode: 'cold', prefs: ffOff, shape: 'real', lo: 300, hi: 1979, reps: 2 });
    }
    run('bisect', { label: 'E2-webkit-w0-nojit', browser: 'webkit', page: 'w0', mode: 'cold', env: { JSC_useJIT: 'false' }, shape: 'unbroken', lo: 300, hi: 2000, reps: 2 });
    run('bisect', { label: 'E2-webkit-w0-nojit', browser: 'webkit', page: 'w0', mode: 'cold', env: { JSC_useJIT: 'false' }, shape: 'real', lo: 300, hi: 1979, reps: 2 });
  },
  // 4: stack depth, worker against main thread.
  F() {
    for (const b of CHROMIUMS) {
      run('stack', { label: 'F-stack', browser: b });
      run('stack', { label: 'F-stack', browser: b, jsflags: OFF });
      run('stack', { label: 'F-stack', browser: b, jsflags: '--jitless' });
    }
    run('stack', { label: 'F-stack', browser: 'firefox' });
    run('stack', { label: 'F-stack', browser: 'firefox', prefs: { 'javascript.options.blinterp': false, 'javascript.options.baselinejit': false, 'javascript.options.ion': false, 'javascript.options.native_regexp': false } });
    run('stack', { label: 'F-stack', browser: 'webkit' });
    run('stack', { label: 'F-stack', browser: 'webkit', env: { JSC_useJIT: 'false' } });
  }
};
for (const p of process.argv.slice(2)) {
  console.log(`\n##### phase ${p} ${new Date().toISOString()}`);
  phases[p]();
}
