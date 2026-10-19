// Round 16 filters: the ANY / ALL switches (labels, defaults, what they switch) and the search qualifiers (dep:,
// duration:, is:happy). Every qualifier search is checked scenario by scenario against the page's own data (the
// dependencies, the own time and the happy-path class the filters read), alone, combined with a word and with &&,
// and against the report's own result for the word alone. Then a link carrying a qualifier search is opened again.
// usage: node q-test.js <page.local.html>
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page] = process.argv.slice(2);
  const url = 'file:///' + path.resolve(page).split(path.sep).join('/');
  const b = await launchInstalled(chromium, { channel: 'chrome' });
  const ctx = await b.newContext({ viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errors = [];
  p.on('pageerror', e => errors.push(e.message));
  await p.goto(url); await p.waitForTimeout(2500);
  let fails = 0;
  const check = (name, ok, extra) => { if (!ok) fails++; console.log((ok ? 'ok   ' : 'FAIL ') + name + (extra ? ' ' + extra : '')); };

  // the switches
  const sw = await p.evaluate(() => {
    const dep = document.querySelector('.dep-mode-toggle'), tag = document.querySelector('.tag-mode-toggle');
    const r = { dep: dep && dep.textContent.trim(), tag: tag && tag.textContent.trim(), mode: window._depMode, def: window._depModeDefault, depTitle: dep && dep.title };
    dep.click(); r.depAfter = dep.textContent.trim(); r.modeAfter = window._depMode;
    dep.click(); r.depBack = dep.textContent.trim();
    tag.click(); r.tagAfter = tag.textContent.trim(); tag.click(); r.tagBack = tag.textContent.trim();
    return r;
  });
  await p.waitForTimeout(200);
  const sw2 = await p.evaluate(() => ({ dep: document.querySelector('.dep-mode-toggle').textContent.trim(), tag: document.querySelector('.tag-mode-toggle').textContent.trim() }));
  check('dependencies start on ANY', sw.dep === 'ANY' && sw.mode === 'OR' && sw.def === 'OR', JSON.stringify(sw));
  check('tags start on ANY', sw.tag === 'ANY');
  check('dependency switch flips to ALL and back', sw.modeAfter === 'AND' && sw2.dep === 'ANY');
  check('tag switch flips and comes back to ANY', sw2.tag === 'ANY');

  // the scenarios' own data, as the filters read it
  const facts = await p.evaluate(() => window.fc().items.map(d => ({ deps: Array.from(d.deps), own: d.el.hasAttribute('data-duration-ms') ? parseFloat(d.el.getAttribute('data-duration-ms')) - parseFloat(d.el.getAttribute('data-warmup-ms') || '0') : null, happy: d.isHappy })));
  const pct = await p.evaluate(() => { const d = document.querySelector('.duration-filters'); return d ? { p50: +d.getAttribute('data-p50'), p90: +d.getAttribute('data-p90') } : {}; });
  const norm = s => s.toLowerCase().replace(/[^a-z0-9]/g, '');
  const hasDep = (f, w) => f.deps.some(n => norm(n).includes(norm(w)));
  async function search(q) {
    await p.evaluate(q => { const bar = document.getElementById('searchbar'); bar.value = q; window.run_search_scenarios(); }, q);
    await p.waitForTimeout(q && !/^[a-z]+:|^!!/.test(q.trim()) ? 1800 : 400);
    return p.evaluate(() => window.fc().items.map(d => d.el.style.display !== 'none'));
  }
  const all = await search('');
  check('nothing hidden with an empty search', all.every(Boolean), all.filter(Boolean).length + ' of ' + all.length);
  const cases = [
    ['dep:cow', f => hasDep(f, 'cow')],
    ['dep:"cow service"', f => hasDep(f, 'cow service')],
    ['dep:cow,goat', f => hasDep(f, 'cow') || hasDep(f, 'goat')],
    ['dep:cow dep:goat', f => hasDep(f, 'cow') && hasDep(f, 'goat')],
    ['!!dep:kafka', f => !hasDep(f, 'kafka')],
    ['duration:>50ms', f => f.own !== null && f.own > 50],
    ['duration:<20ms', f => f.own !== null && f.own < 20],
    ['duration:>=0.1s', f => f.own !== null && f.own >= 100],
    ['duration:>p90', f => f.own !== null && f.own > pct.p90],
    ['duration:10ms..30ms', f => f.own !== null && f.own >= 10 && f.own <= 30],
    ['is:happy', f => f.happy],
    ['!!is:happy', f => !f.happy],
  ];
  for (const [q, want] of cases) {
    const vis = await search(q);
    const wrong = vis.map((v, i) => v !== want(facts[i]) ? i : -1).filter(i => i >= 0);
    check('search ' + q, wrong.length === 0 && vis.some(Boolean), vis.filter(Boolean).length + ' shown' + (wrong.length ? ', ' + wrong.length + ' wrong' : ''));
  }
  // with a word: the report's own result for the word, narrowed by the qualifier
  const word = await search('muffin');
  for (const q of ['muffin dep:cow', 'muffin && dep:cow', 'dep:cow && muffin', '(muffin || pancake) && duration:>20ms']) {
    const base = q.startsWith('(') ? await search('(muffin || pancake)') : word;
    const want = q.includes('duration') ? (f => f.own !== null && f.own > 20) : (f => hasDep(f, 'cow'));
    const vis = await search(q);
    const wrong = vis.map((v, i) => v !== (base[i] && want(facts[i])) ? i : -1).filter(i => i >= 0);
    check('search ' + q, wrong.length === 0, vis.filter(Boolean).length + ' shown, base ' + base.filter(Boolean).length + (wrong.length ? ', ' + wrong.length + ' wrong' : ''));
  }
  // text that only looks like a qualifier stays text; a qualifier that cannot be read says so
  const phrase = await p.evaluate(() => window.__kronikolQuery.split('"status: 400" milk:'));
  check('a phrase and an unknown key stay text', phrase.quals.length === 0 && phrase.rest === '"status: 400" milk:', JSON.stringify(phrase.rest));
  await search('duration:>soon');
  const bad = await p.evaluate(() => document.getElementById('searchbar').classList.contains('kron-q-bad'));
  check('an unreadable qualifier marks the field', bad);
  // the bar keeps what was typed; the link carries it
  await search('dep:cow');
  const st = await p.evaluate(() => ({ bar: document.getElementById('searchbar').value, hash: location.hash }));
  check('the bar keeps the typed search', st.bar === 'dep:cow');
  check('the link carries it', /q=dep%3Acow/.test(st.hash), st.hash.slice(0, 80));
  const shown = (await search('dep:cow')).filter(Boolean).length;
  const p2 = await ctx.newPage();
  p2.on('pageerror', e => errors.push('reload: ' + e.message));
  await p2.goto(url + st.hash); await p2.waitForTimeout(3000);
  const re = await p2.evaluate(() => ({ bar: document.getElementById('searchbar').value, shown: window.fc().items.filter(d => d.el.style.display !== 'none').length }));
  check('the link opens the same search', re.bar === 'dep:cow' && re.shown === shown, JSON.stringify(re) + ' vs ' + shown);
  // the syntax is explained where the report explains its own
  const help = await p.evaluate(() => document.querySelectorAll('.search-help-table .kron-q-row').length);
  check('help rows', help === 3);
  await search('');
  check('no page errors', errors.length === 0, errors.slice(0, 3).join(' | '));
  console.log(fails ? fails + ' FAILED' : 'ALL PASSED');
  await b.close();
})();
