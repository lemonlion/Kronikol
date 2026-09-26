// v8-code-cache-probe.js: what V8's code cache checks, and what the Node renderer does with a bad one.
//   node v8-code-cache-probe.js                         same-length source against a cache (no engine needed)
//   node v8-code-cache-probe.js damage <engine dir>      the shipped plantuml-render.js on a damaged plantuml.js.v8cache
//   node v8-code-cache-probe.js race <engine dir> [rounds=20] [concurrent=8]
//                                                        cold renders started together: can their writes tear the cache?
// <engine dir> holds plantuml.js and viz-global.js (see README.md); it is copied to a temp dir, never written.
// Measured on node 25.9 (plans/ENGINE_PIN_PLAN.md §1.12, §1.14; results/v8-code-cache-2026-09-25.txt):
// - V8 accepts cached data for a different source of the SAME length and runs the cached code: its sanity
//   check compares the source length and the flags, not the bytes. So the renderer must delete
//   plantuml.js.v8cache whenever it replaces plantuml.js (S3 item 4).
// - A release node verifies no checksum on the payload (--verify-snapshot-checksum is off outside debug
//   builds): one flipped byte in the payload crashes node before it prints anything, and the crash never
//   rewrites the cache, so every later render crashes too (S3 item 6).
// - The concurrent-write race did not tear a cache in 20 rounds of 8; the caches differ in length run to run.
const vm = require('vm'), fs = require('fs'), path = require('path'), cp = require('child_process'), os = require('os'), crypto = require('crypto');
const RENDER = path.resolve(__dirname, '../../src/Kronikol/PlantUml/plantuml-render.js');
const mode = process.argv[2] || 'length';

function copyEngine(dir, prefix) {
  const t = fs.mkdtempSync(path.join(os.tmpdir(), prefix));
  for (const f of ['plantuml.js', 'viz-global.js']) fs.copyFileSync(path.join(dir, f), path.join(t, f));
  return t;
}

if (mode === 'length') {
  const a = 'function f(){ return 111 } var out = f(); out;';
  const b = 'function g(){ return 222 } var out = g(); out;';
  const c = 'function h(){ return 333 } var out = h(); out; // longer';
  console.log('node', process.version, 'lengths', a.length, b.length, c.length);
  const sa = new vm.Script(a, { filename: 'engine.js' });
  console.log('a runs', sa.runInThisContext());
  const cache = sa.createCachedData();
  console.log('cache bytes', cache.length);
  const sb = new vm.Script(b, { filename: 'engine.js', cachedData: cache });
  console.log('same length, different source: cachedDataRejected =', sb.cachedDataRejected, '; runs ->', sb.runInThisContext(), '(source says 222)');
  const sc = new vm.Script(c, { filename: 'engine.js', cachedData: cache });
  console.log('different length: cachedDataRejected =', sc.cachedDataRejected, '; runs ->', sc.runInThisContext());
} else if (mode === 'damage') {
  const T = copyEngine(process.argv[3], 'v8c-');
  const src = '@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : hello\nnote right: {"a": 1}\nB --> A : ok\nloop 3\nA -> B : again\nend\n@enduml\n';
  const cachePath = path.join(T, 'plantuml.js.v8cache');
  const rows = [];
  const run = label => {
    const t0 = Date.now();
    const r = cp.spawnSync(process.execPath, [RENDER, path.join(T, 'viz-global.js'), path.join(T, 'plantuml.js')], { input: src, encoding: 'utf8', timeout: 60000 });
    const svg = r.stdout && r.stdout.includes('<svg') ? crypto.createHash('sha256').update(r.stdout).digest('hex').slice(0, 12) : null;
    const row = { label, exit: r.status, cache: (/code cache: (\w+)/.exec(r.stderr || '') || [])[1] || null, svg, ms: Date.now() - t0 };
    rows.push(row); console.log(JSON.stringify(row)); return row;
  };
  console.log('node', process.version, '--verify-snapshot-checksum default:',
    (/verify-snapshot-checksum[\s\S]*?default: (\S+)/.exec(cp.execFileSync(process.execPath, ['--v8-options'], { encoding: 'utf8' })) || [])[1]);
  run('no cache');
  const good = fs.readFileSync(cachePath);
  console.log('cache bytes', good.length);
  const ref = run('cache present');
  fs.unlinkSync(cachePath); run('cache deleted, rebuilt');
  console.log('rebuilt cache byte-identical to the first:', Buffer.compare(good, fs.readFileSync(cachePath)) === 0);
  const variant = (label, buf) => { fs.writeFileSync(cachePath, buf); const r = run(label); fs.writeFileSync(cachePath, good); return r; };
  for (const frac of [0.1, 0.25, 0.5, 0.75, 0.9]) { const b = Buffer.from(good); b[Math.floor(b.length * frac)] ^= 0xff; variant(`1 byte flipped at ${frac * 100}%`, b); }
  for (const frac of [0.25, 0.5, 0.75]) { const b = Buffer.from(good); const o = Math.floor(b.length * frac); for (let i = 0; i < 4096; i++) b[o + i] ^= 0x5a; variant(`4 KB scrambled at ${frac * 100}%`, b); }
  { const b = Buffer.from(good); b.fill(0, b.length >> 1, (b.length >> 1) + 65536); variant('64 KB zeroed at 50%', b); }
  variant('truncated to half', good.subarray(0, good.length >> 1));
  variant('64 KB of random bytes appended', Buffer.concat([good, crypto.randomBytes(65536)]));
  for (const off of [0, 4, 8, 12, 16, 20, 24, 28]) { const b = Buffer.from(good); b[off] ^= 0xff; variant(`header byte ${off} flipped`, b); }
  console.log('SUMMARY', JSON.stringify(rows.map(r => [r.label, r.exit, r.cache, r.svg === ref.svg ? 'same svg' : (r.svg ? 'DIFFERENT svg' : 'no svg')])));
  fs.rmSync(T, { recursive: true, force: true });
} else if (mode === 'race') {
  const [dir, rounds = 20, k = 8] = process.argv.slice(3);
  const src = '@startuml\nA -> B : hello\nB --> A : ok\n@enduml\n';
  const once = d => new Promise(resolve => {
    const p = cp.spawn(process.execPath, [RENDER, path.join(d, 'viz-global.js'), path.join(d, 'plantuml.js')]);
    let out = '', err = '';
    p.stdout.on('data', x => out += x); p.stderr.on('data', x => err += x);
    p.on('close', code => resolve({ code, cache: (/code cache: (\w+)/.exec(err) || [])[1] || null, svg: out.includes('<svg') }));
    p.stdin.end(src);
  });
  (async () => {
    const lengths = new Set(); let crashed = 0, hit = 0;
    for (let r = 0; r < +rounds; r++) {
      const d = copyEngine(dir, 'v8r-');
      const cold = await Promise.all(Array.from({ length: +k }, () => once(d)));
      const bytes = fs.readFileSync(path.join(d, 'plantuml.js.v8cache'));
      lengths.add(bytes.length);
      const next = await once(d);
      if (next.code !== 0) crashed++; else if (next.cache === 'hit') hit++;
      console.log(JSON.stringify({ round: r, cold: cold.map(x => x.cache + (x.code ? '!' + x.code : '')).join(','), cacheBytes: bytes.length, next }));
      fs.rmSync(d, { recursive: true, force: true });
    }
    console.log('SUMMARY', JSON.stringify({ rounds: +rounds, concurrent: +k, nextRunCrashed: crashed, nextRunHit: hit, distinctCacheLengths: [...lengths].sort() }));
  })();
} else {
  console.error('usage: node v8-code-cache-probe.js [length | damage <engine dir> | race <engine dir> [rounds] [concurrent]]');
  process.exit(2);
}
