// Round 17 item 9: expand a scenario's payloads, bring the tallest note's bottom near the window's bottom, click its
// bottom button with the mouse (the one that makes it shorter), and report where the note and the reader end up.
// usage: node r9-keep.js <page> <chromium|firefox> <scenario text> [button name, default truncate-bottom] [shot]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path');
(async () => {
  const [page, browserName, pick, btnName, shot] = process.argv.slice(2);
  const name = btnName || 'truncate-bottom';
  const b = await pw[browserName].launch(browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  await p.evaluate(pick => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick));
    s.closest('details.feature').open = true; s.open = true; s.setAttribute('data-probe', '1');
    const seq = Array.from(s.querySelectorAll('.diagram-view-seq .plantuml-browser')).find(e => e.getBoundingClientRect().height > 0) || s.querySelector('.diagram-view-seq .plantuml-browser');
    seq.setAttribute('data-probe-seq', '1'); seq.scrollIntoView();
  }, pick);
  const settle = async () => {
    await p.waitForTimeout(500);
    await p.waitForFunction(() => { const c = document.querySelector('[data-probe-seq]'); return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg .note-hover-rect'); }, null, { polling: 200, timeout: 30000 });
    await p.waitForTimeout(500);
  };
  await settle();
  await p.evaluate(() => document.querySelector('[data-probe] .details-radio-btn[data-state="expanded"]').click());
  await settle();
  const pre = await p.evaluate(name => {
    const rects = Array.from(document.querySelectorAll('[data-probe-seq] svg .note-hover-rect'));
    let best = null, bh = 0; rects.forEach(r => { const h = r.getBoundingClientRect().height; if (h > bh) { bh = h; best = r; } });
    const idx = rects.indexOf(best);
    const nb = best.getBoundingClientRect();
    window.scrollBy(0, nb.bottom - (innerHeight - 120));
    return { idx, h: Math.round(bh) };
  }, name);
  await p.waitForTimeout(400);
  const pt = await p.evaluate(([name, idx]) => {
    const r = document.querySelectorAll('[data-probe-seq] svg .note-hover-rect')[idx];
    r.dispatchEvent(new MouseEvent('mouseenter'));
    const nb = r.getBBox();
    const g = Array.from(r.ownerSVGElement.querySelectorAll('.note-toggle-icon[data-note-btn="' + name + '"]')).find(g => { const rb = g.querySelector('rect').getBBox(); return rb.x >= nb.x - 2 && rb.x <= nb.x + nb.width + 2 && rb.y >= nb.y - 2 && rb.y <= nb.y + nb.height + 2; });
    if (!g) return null;
    const bb = g.querySelector('rect').getBoundingClientRect();
    return { x: bb.left + bb.width / 2, y: bb.top + bb.height / 2, scrollY: Math.round(scrollY) };
  }, [name, pre.idx]);
  if (!pt) { console.log('button ' + name + ' not found on the note'); await b.close(); return; }
  await p.waitForTimeout(450);
  await p.mouse.move(pt.x, pt.y); await p.waitForTimeout(150);
  await p.mouse.click(pt.x, pt.y);
  await settle(); await p.waitForTimeout(600);
  const post = await p.evaluate(idx => {
    const r = document.querySelectorAll('[data-probe-seq] svg .note-hover-rect')[idx];
    const nb = r ? r.getBoundingClientRect() : null;
    const s = document.querySelector('[data-probe]').getBoundingClientRect();
    return { scrollY: Math.round(scrollY), note: nb ? Math.round(nb.top) + '..' + Math.round(nb.bottom) + ' (h ' + Math.round(nb.height) + ')' : '-', scenarioOnScreen: s.bottom > 0 && s.top < innerHeight, inner: innerHeight };
  }, pre.idx);
  console.log('note #' + pre.idx + ' was ' + pre.h + 'px tall; clicked ' + name + ' at y ' + Math.round(pt.y) + ' (scrollY ' + pt.scrollY + ')');
  console.log('after: scrollY ' + post.scrollY + ', note ' + post.note + ' of window ' + post.inner + ', scenario on screen ' + post.scenarioOnScreen);
  if (shot) await p.screenshot({ path: shot });
  console.log('errors', errs.slice(0, 3).join(' | ') || 'none');
  await b.close();
})();
