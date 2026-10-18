// Round-6 checks: P90 shows exactly the scenarios whose own time (duration less warm-up) is at least its threshold;
// the label and every Duration button carry a warm-up tooltip; each Clear is hidden until its group has a chip set,
// clears it (filters and URL hash), and hides again.
// usage: node r6-check.js <page.local.html> <browser>
const path = require('path');
const { launch } = require('../mw/browsers');
(async () => {
  const [page, browserName] = process.argv.slice(2);
  const b = await launch(browserName);
  const p = await b.newPage();
  const raw = p.raw;
  if (raw.setViewportSize) await raw.setViewportSize({ width: 1400, height: 1000 }); else await raw.setViewport({ width: 1400, height: 1000 });
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/'));
  await new Promise(r => setTimeout(r, 2000));
  await p.evaluate(() => { const m = document.querySelector('.more-filters'); if (m && m.getAttribute('aria-expanded') !== 'true') m.click(); }); await new Promise(r => setTimeout(r, 300)); // round 9: the filter rows start folded
  const out = await p.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms));
    const o = {};
    const g = document.querySelector('.filtering-box .duration-filters');
    o.titles = [g.querySelector('.duration-filters-label').title].concat(Array.from(g.querySelectorAll('.percentile-btn')).map(x => x.textContent.trim() + ' = ' + x.title));
    const p90 = Array.from(g.querySelectorAll('.percentile-btn')).find(x => x.getAttribute('data-threshold-ms') === g.getAttribute('data-p90'));
    p90.click(); await wait(300);
    const thr = parseFloat(g.getAttribute('data-p90'));
    const scen = Array.from(document.querySelectorAll('#report-content details.scenario[data-duration-ms]'));
    const shown = s => s.style.display !== 'none' && !(s.closest('details.feature') && s.closest('details.feature').style.display === 'none');
    const own = s => parseFloat(s.getAttribute('data-duration-ms')) - parseFloat(s.getAttribute('data-warmup-ms') || '0');
    o.p90 = { threshold: thr, shown: scen.filter(shown).length, shownBelow: scen.filter(s => shown(s) && own(s) < thr).length,
      hiddenAtOrAbove: scen.filter(s => !shown(s) && own(s) >= thr).length,
      wallAboveOwnBelow: scen.filter(s => parseFloat(s.getAttribute('data-duration-ms')) >= thr && own(s) < thr).length };
    p90.click(); await wait(300);
    o.clear = {};
    for (const [sel, chipSel] of [['.dependency-filters', '.dependency-toggle'], ['.tag-filters', '.tag-toggle']]) {
      const box = document.querySelector('.filtering-box ' + sel), clr = box.querySelector('.group-clear');
      const before = clr.hidden;
      const chips = box.querySelectorAll(chipSel);
      chips[0].click(); await wait(200);
      chips[1].dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true })); await wait(200);
      const set = Array.from(chips).filter(c => c.classList.contains('dependency-active') || c.classList.contains('tag-active') || c.classList.contains('tagf-out')).length;
      const visible = !clr.hidden && clr.getBoundingClientRect().width > 0;
      const hashSet = location.hash;
      clr.click(); await wait(300);
      const left = Array.from(chips).filter(c => c.classList.contains('dependency-active') || c.classList.contains('tag-active') || c.classList.contains('tagf-out')).length;
      o.clear[sel] = { hiddenAtStart: before, setAfterClicks: set, visibleWhenSet: visible, hashWhenSet: hashSet.slice(0, 80), leftAfterClear: left, hiddenAfter: clr.hidden, hashAfter: location.hash.slice(0, 80), shownScenarios: scen.filter(shown).length };
    }
    return o;
  });
  console.log(browserName, JSON.stringify(out, null, 1));
  await b.close();
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.stack || e); process.exit(1); });
