// The bar's filters (round 8, reworked: the box goes INTO the bar). Per theme at 1400x900: the readout and Clear All
// follow the box; scrolled deep, Filters puts the box in the pinned bar and what was under the bar stays under it;
// filtering down to nothing keeps the bar pinned at the list's start with the empty state right under it (the owner's
// case: no jump to the top, no gap, no squashed box); the empty state's Clear All brings the list back; Next Failure
// lands below the taller bar; Escape puts the box back with focus on the toggle; opened at the top, no gap above the
// bar and the whole box in view. Then: a short window, the Timeline tab, and widths. Prints JSON.
// usage: node nav-check.js <page.local.html> <out dir> [chrome|firefox]
const PW = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path'), fs = require('fs');
(async () => {
  const [page, outDir, which = 'chrome'] = process.argv.slice(2);
  fs.mkdirSync(outDir, { recursive: true });
  const url = 'file:///' + path.resolve(page).split(path.sep).join('/');
  const b = which === 'firefox' ? null : await launchInstalled(PW.chromium, { channel: 'chrome' });
  const out = { browser: which };
  // Firefox through the Puppeteer launcher (the Playwright Firefox build here takes no viewport): a browser per page
  async function fresh(w, h, theme) {
    if (which === 'firefox') {
      const fb = await require('../mw/browsers').launch('firefox');
      const p = (await fb.newPage()).raw;
      await p.setViewport({ width: w, height: h });
      p.waitForTimeout = ms => new Promise(r => setTimeout(r, ms));
      await p.evaluateOnNewDocument(t => { try { localStorage.setItem('kronikol.theme', t); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} }, theme || 'light');
      const errors = []; p.on('pageerror', e => errors.push(e.message));
      await p.goto(url); await p.waitForTimeout(2200);
      await p.evaluate(() => { const m = document.querySelector('.more-filters'); if (m && m.getAttribute('aria-expanded') !== 'true') m.click(); }); await new Promise(r => setTimeout(r, 300)); // round 9: the filter rows start folded
      return { p, errors, close: () => fb.close() };
    }
    const ctx = await b.newContext({ viewport: { width: w, height: h } });
    await ctx.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} }, theme || 'light');
    const p = await ctx.newPage();
    const errors = []; p.on('pageerror', e => errors.push(e.message));
    await p.goto(url); await p.waitForTimeout(1800);
    await p.evaluate(() => { const m = document.querySelector('.more-filters'); if (m && m.getAttribute('aria-expanded') !== 'true') m.click(); }); await new Promise(r => setTimeout(r, 300)); // round 9: the filter rows start folded
    return { p, errors, close: () => ctx.close() };
  }
  // the state of the bar, the box and the view
  const state = p => p.evaluate(() => {
    const nav = document.querySelector('.toolbar-row.report-nav'), box = document.querySelector('.filtering-box');
    const t = document.querySelector('.nav-readout'), c = document.querySelector('.nav-clear'), tog = document.querySelector('.nav-filters-toggle');
    const empty = document.querySelector('.filters-empty'), mark = document.querySelector('.nav-mark');
    const vis = e => !!e && getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0;
    const n = nav.getBoundingClientRect(), bx = box.getBoundingClientRect();
    const prev = (() => { let e = mark.previousElementSibling; while (e && !(e.getBoundingClientRect().height > 0)) e = e.previousElementSibling; return e; })();
    const a = document.activeElement;
    return {
      readout: vis(t) ? t.innerText.replace(/\s+/g, ' ').trim() : null, boxReadout: box.querySelector('.filter-readout').innerText.replace(/\s+/g, ' ').trim(),
      barClear: vis(c), expanded: tog.getAttribute('aria-expanded'), inBar: nav.contains(box),
      navTop: Math.round(n.top), navH: Math.round(n.height), navHVar: getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h').trim(),
      boxTop: Math.round(bx.top), boxBottom: Math.round(bx.bottom), boxCapped: !!box.style.maxHeight, boxScrolls: box.scrollHeight > box.clientHeight + 1,
      rowClass: document.querySelector('.filter-row').className.replace('filter-row', '').trim(),
      // the gap above the bar: from the last thing drawn before it to the bar's top
      gapAbove: prev ? Math.round(n.top - prev.getBoundingClientRect().bottom) : null, prevIs: prev ? prev.className.split(' ')[0] : null,
      empty: vis(empty), emptyBelowBar: vis(empty) ? Math.round(empty.getBoundingClientRect().top - n.bottom) : null,
      scrollY: Math.round(scrollY), pinAt: Math.round(mark.getBoundingClientRect().top + scrollY), focus: a && (a.id || (a.className && a.className.baseVal === undefined ? a.className : a.tagName)),
      windowH: innerHeight
    };
  });
  // a real pointer where the control is drawn: Playwright's click first scrolls the element 'into view', which for a
  // control in a pinned bar moves the page by itself
  const tap = async (p, sel) => { const r = await p.evaluate(s => { const e = document.querySelector(s); if (!e) return null; const b = e.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2 }; }, sel); if (!r) throw new Error('no ' + sel); await p.mouse.click(r.x, r.y); };
  const markUnder = p => p.evaluate(() => { const r = document.querySelector('.toolbar-row.report-nav').getBoundingClientRect(); window.__u = document.elementFromPoint(Math.round(r.left + r.width / 2), Math.round(r.bottom + 8)); return window.__u ? Math.round(window.__u.getBoundingClientRect().top - r.bottom) : null; });
  const underNow = p => p.evaluate(() => { const r = document.querySelector('.toolbar-row.report-nav').getBoundingClientRect(); return window.__u && window.__u.isConnected ? Math.round(window.__u.getBoundingClientRect().top - r.bottom) : null; });
  for (const theme of ['light', 'dark']) {
    const { p, errors, close } = await fresh(1400, 900, theme);
    const o = {};
    o.start = await state(p);
    await p.click('.filtering-box .status-toggle[data-status="Failed"]'); await p.waitForTimeout(400);
    o.filteredDocked = await state(p);
    await tap(p, '.nav-clear'); await p.waitForTimeout(400);
    o.clearedByBar = await state(p);
    // deep in the list: open, what was under the bar stays under it
    await p.evaluate(() => window.scrollTo(0, 3000)); await p.waitForTimeout(400);
    const offBefore = await markUnder(p);
    await tap(p, '.nav-filters-toggle'); await p.waitForTimeout(500);
    o.openDeep = { ...(await state(p)), underBefore: offBefore, underAfter: await underNow(p) };
    await p.screenshot({ path: path.join(outDir, `deep-open-${theme}.png`) });
    // the owner's case: filter down to nothing from the bar
    await tap(p, '.report-nav .status-toggle[data-status="Failed"]'); await p.waitForTimeout(400);
    o.failedInBar = await state(p);
    // round 17 (item 11): a short result list brings the bar to rest and the box back above it; its own toggle is used there
    await tap(p, '.filtering-box .dependency-toggle'); await p.waitForTimeout(500);
    o.nothingLeft = await state(p);
    await p.screenshot({ path: path.join(outDir, `nothing-${theme}.png`) });
    // the empty state's Clear All
    await tap(p, '.filters-empty-clear'); await p.waitForTimeout(500);
    o.emptyCleared = await state(p);
    // Next Failure lands below the taller bar
    await tap(p, '.jump-to-failure'); await p.waitForTimeout(1200);
    o.nextFailure = await p.evaluate(() => { const nav = document.querySelector('.toolbar-row.report-nav').getBoundingClientRect(); const n = (document.getElementById('failure-counter') || {}).textContent || ''; const i = (/\((\d+)\//.exec(n) || [])[1]; const f = document.querySelectorAll('details.scenario[data-status="Failed"]')[(+i || 1) - 1]; const s = f && (f.querySelector(':scope > summary') || f); return s ? { counter: n, summaryTop: Math.round(s.getBoundingClientRect().top), navBottom: Math.round(nav.bottom) } : 'none'; });
    // Escape puts it back, focus on the toggle, what was under the bar stays under it
    await p.focus('#searchbar');
    await markUnder(p);
    const offB = await underNow(p);
    await p.keyboard.press('Escape'); await p.waitForTimeout(500);
    o.escape = { ...(await state(p)), underBefore: offB, underAfter: await underNow(p) };
    // at the top: open, no gap above the bar, the whole box in view
    // round 17 (item 11): at the top the bar is at rest and has no Filters button (the box is in view above it);
    // deep in the list it opens at the bar's top and its X puts it back
    await p.evaluate(() => window.scrollTo(0, 0)); await p.waitForTimeout(300);
    o.openTop = { toggleHidden: await p.evaluate(() => getComputedStyle(document.querySelector('.nav-filters-toggle')).display === 'none'), ...(await state(p)) };
    await p.evaluate(() => window.scrollTo(0, 3000)); await p.waitForTimeout(400);
    await tap(p, '.nav-filters-toggle'); await p.waitForTimeout(500);
    o.openDeep2 = await state(p);
    await p.screenshot({ path: path.join(outDir, `deep-open2-${theme}.png`) });
    await tap(p, '.filters-close'); await p.waitForTimeout(400);
    o.closedByX = await state(p);
    o.errors = errors;
    out[theme] = o;
    await close();
  }
  // the owner's case through the search field: deep, the box in the bar, typing a search that matches nothing
  {
    const { p, errors, close } = await fresh(1400, 900);
    await p.evaluate(() => window.scrollTo(0, 3000)); await p.waitForTimeout(400);
    await tap(p, '.nav-filters-toggle'); await p.waitForTimeout(500);
    await p.keyboard.type('zzqqxx'); await p.waitForTimeout(1200);
    out.searchNothing = { ...(await state(p)), errors };
    await p.screenshot({ path: path.join(outDir, 'search-nothing.png') });
    for (let i = 0; i < 6; i++) await p.keyboard.press('Backspace');
    await p.waitForTimeout(1200);
    out.searchCleared = await state(p);
    // a search the deep pass widens (10 scenarios, then 24): the bar stays pinned at the list's start for both
    await p.keyboard.type('muffin'); await p.waitForTimeout(2500);
    out.searchTwoPass = await state(p);
    await close();
  }
  // a short window: opened at the top, the box is capped and scrolled into view
  {
    const { p, errors, close } = await fresh(1400, 560);
    // round 17 (item 11): the bar has its Filters button only while it floats
    await p.evaluate(() => window.scrollTo(0, 3000)); await p.waitForTimeout(400);
    await tap(p, '.nav-filters-toggle'); await p.waitForTimeout(500);
    out.short = await state(p);
    await p.screenshot({ path: path.join(outDir, 'short.png') });
    await p.click('.report-nav .status-toggle[data-status="Failed"]'); await p.waitForTimeout(300);
    await p.click('.filtering-box .dependency-toggle'); await p.waitForTimeout(500);
    out.shortNothing = await state(p);
    await p.screenshot({ path: path.join(outDir, 'short-nothing.png') });
    out.short.errors = errors;
    await close();
  }
  // the Timeline tab: in the bar under the tabs alone, and filtering works
  {
    const { p, errors, close } = await fresh(1400, 900);
    await p.click('.report-tab[data-tab="timeline"]'); await p.waitForTimeout(500);
    // round 17 (item 11): at rest the box is in view above the bar, and it filters the timeline from there
    await p.click('.filtering-box .status-toggle[data-status="Failed"]'); await p.waitForTimeout(500);
    out.timeline = { ...(await state(p)), rows: await p.evaluate(() => { const t = document.querySelector('#scenario-timeline'); return t ? Array.from(t.querySelectorAll('[data-scenario-id], .timeline-row, tr')).filter(e => e.getBoundingClientRect().height > 0).length : null; }), errors };
    await p.screenshot({ path: path.join(outDir, 'timeline.png') });
    await close();
  }
  // widths: the group fits beside the tabs; the box in the bar keeps the docked layout; phones hide the group
  out.widths = {};
  for (const w of [1400, 1100, 1000, 900, 800, 400]) {
    const { p, close } = await fresh(w, 900);
    if (w > 768) await p.click('.filtering-box .status-toggle[data-status="Failed"]').catch(() => {});
    await p.waitForTimeout(300);
    const docked = await p.evaluate(() => ({ h: Math.round(document.querySelector('.filtering-box').getBoundingClientRect().height), rows: document.querySelector('.filter-row').className.replace('filter-row', '').trim() }));
    out.widths[w] = await p.evaluate(() => {
      const row = document.querySelector('.nav-row'), g = document.querySelector('.nav-filters'), tabs = document.querySelector('.report-tabs');
      if (!g || getComputedStyle(g).display === 'none') return 'hidden';
      const rg = g.getBoundingClientRect(), rt = tabs.getBoundingClientRect(), rr = row.getBoundingClientRect();
      return { form: row.classList.contains('nav-minimal') ? 'minimal' : row.classList.contains('nav-compact') ? 'short' : 'long', gap: Math.round(rg.left - (rt.left + tabs.scrollWidth)), tabsScroll: tabs.scrollWidth > tabs.clientWidth + 1, inside: rg.right <= rr.right + 0.5 };
    });
    if (out.widths[w] !== 'hidden') {
      // opened directly: with the bar at the window's bottom edge, Next Failure covers the bar's right end
      // the Filters button's own click (the preview's window.__kronikolNavFilters.open() is not in the product, D50)
      await p.evaluate(() => document.querySelector('.nav-filters-toggle').click()); await p.waitForTimeout(500);
      const s = await state(p);
      out.widths[w].box = { dockedH: docked.h, dockedRows: docked.rows, inBarH: s.boxBottom - s.boxTop, inBarRows: s.rowClass, capped: s.boxCapped, bottom: s.boxBottom, gapAbove: s.gapAbove };
      if (w === 900) await p.screenshot({ path: path.join(outDir, 'open-900.png') });
    }
    await close();
  }
  console.log(JSON.stringify(out));
  if (b) await b.close();
})();
