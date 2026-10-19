// Round 17 items 13-15: the note buttons of the first long payload in a scenario, in each state.
// Prints, per state, the visible buttons (name, x after the transform, width) with the gaps between top-right
// neighbours, and each triangle's clearance from its button's top and bottom edges; then clicks the truncated note's
// bottom pair and reports the step each click leaves. Optional screenshot of the hovered note per state.
// usage: node r17-buttons-probe.js <page> <chromium|firefox> <scenario text> [shot prefix]
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [page, browserName, pick, shot] = process.argv.slice(2);
  const b = await launchInstalled(pw[browserName], browserName === 'chromium' ? { channel: 'chrome' } : { executablePath: 'C:/Users/cex/AppData/Local/ms-playwright/firefox-1511/firefox/firefox.exe' });
  const ctx = await b.newContext(browserName === 'firefox' ? { viewport: null } : { viewport: { width: 1400, height: 900 } });
  await ctx.addInitScript(() => { try { localStorage.setItem('kronikol.theme', 'light'); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} });
  const p = await ctx.newPage();
  const errors = []; p.on('pageerror', e => errors.push(e.message));
  await p.goto('file:///' + path.resolve(page).split(path.sep).join('/')); await p.waitForTimeout(3000);
  await p.evaluate(pick => {
    const s = Array.from(document.querySelectorAll('details.scenario')).find(x => x.querySelector(':scope > summary').textContent.toLowerCase().includes(pick));
    s.closest('details.feature').open = true; s.open = true; s.setAttribute('data-probe', '1'); s.querySelectorAll('details').forEach(d => d.open = true);
    const seq = Array.from(s.querySelectorAll('.diagram-view-seq .plantuml-browser')).find(e => e.getBoundingClientRect().height > 0);
    seq.setAttribute('data-probe-seq', '1'); seq.scrollIntoView();
  }, pick);
  const settle = async () => {
    await p.waitForTimeout(500);
    await p.waitForFunction(() => { const c = document.querySelector('[data-probe-seq]'); return c && !c._noteRendering && !window._plantumlRendering && c.querySelector('svg .note-hover-rect'); }, null, { polling: 200, timeout: 30000 });
    await p.waitForTimeout(500);
  };
  await settle();
  // the longest note (by its hover rect's height)
  const target = () => p.evaluate(() => {
    document.querySelectorAll('[data-probe-target]').forEach(e => e.removeAttribute('data-probe-target'));
    const rects = Array.from(document.querySelectorAll('[data-probe-seq] svg .note-hover-rect'));
    let best = null, bh = 0;
    rects.forEach(r => { const h = r.getBBox().height; if (h > bh) { bh = h; best = r; } });
    best.setAttribute('data-probe-target', '1');
    return Math.round(bh);
  });
  const look = async (label) => {
    const h = await target();
    const r = await p.evaluate(async () => {
      const t = document.querySelector('[data-probe-target]');
      t.dispatchEvent(new MouseEvent('mouseenter'));
      await new Promise(res => setTimeout(res, 400));
      const nb = t.getBBox();
      const svg = t.ownerSVGElement;
      const vis = Array.from(svg.querySelectorAll('.note-toggle-icon')).filter(g => +getComputedStyle(g).opacity > 0.5 && getComputedStyle(g).display !== 'none').filter(g => {
        const rb = g.querySelector('rect').getBBox(); return rb.x >= nb.x - 2 && rb.x <= nb.x + nb.width + 2 && rb.y >= nb.y - 2 && rb.y <= nb.y + nb.height + 2;
      });
      const step = (() => { const c = document.querySelector('[data-probe-seq]'); const s = c._noteSteps || {}; return JSON.stringify(s).slice(0, 80); })();
      return vis.map(g => {
        const rect = g.querySelector('rect'), rb = rect.getBBox();
        const m = /translate\(([-\d.]+),([-\d.]+)\)/.exec(g.getAttribute('transform') || '') || [0, 0, 0];
        const poly = g.querySelector('polygon');
        let tri = '';
        if (poly) { const pb = poly.getBBox(); tri = ' tri top+' + (pb.y - rb.y).toFixed(2) + ' bottom+' + (rb.y + rb.height - pb.y - pb.height).toFixed(2); }
        const txt = g.querySelector('text');
        return { name: g.getAttribute('data-note-btn') || '(none)', x: +(rb.x + +m[1]).toFixed(2), y: +(rb.y + +m[2]).toFixed(2), w: rb.width, h: rb.height, glyph: (txt ? txt.textContent : '') + tri };
      }).concat([{ steps: step }]);
    });
    const top = r.filter(x => x.name && x.y !== undefined && x.h === 14).sort((a, c) => c.x - a.x);
    const gaps = top.slice(1).map((x, i) => +(top[i].x - (x.x + x.w)).toFixed(2));
    console.log('== ' + label + ' (note ' + h + ' tall)');
    r.forEach(x => console.log('  ' + JSON.stringify(x)));
    console.log('  top-right order ' + top.map(x => x.name).join(' < ') + ' gaps ' + JSON.stringify(gaps));
    if (shot) {
      const bb = await p.evaluate(() => { const t = document.querySelector('[data-probe-target]'); t.scrollIntoView({ block: 'center' }); const r = t.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
      await p.evaluate(() => document.querySelector('[data-probe-target]').dispatchEvent(new MouseEvent('mouseenter')));
      await p.waitForTimeout(400);
      const clipH = Math.min(bb.h, 700);
      await p.screenshot({ path: shot + '-' + label + '.png', clip: { x: Math.max(0, bb.x - 10), y: Math.max(0, bb.y - 10), width: Math.min(bb.w + 20, 1300), height: clipH + 20 } });
    }
  };
  const radio = async st => { await p.evaluate(st => document.querySelector('[data-probe] .details-radio-btn[data-state="' + st + '"]').click(), st); await settle(); };
  const clickBtn = async name => {
    await target();
    await p.evaluate(name => {
      const t = document.querySelector('[data-probe-target]'); t.dispatchEvent(new MouseEvent('mouseenter'));
      const nb = t.getBBox();
      const g = Array.from(t.ownerSVGElement.querySelectorAll('.note-toggle-icon[data-note-btn="' + name + '"]')).find(g => { const rb = g.querySelector('rect').getBBox(); return rb.x >= nb.x - 2 && rb.x <= nb.x + nb.width + 2 && rb.y >= nb.y - 2 && rb.y <= nb.y + nb.height + 2; });
      g.querySelector('rect').dispatchEvent(new MouseEvent('click', { bubbles: true }));
    }, name);
    await settle();
  };
  await radio('truncated'); await look('truncated');
  await clickBtn('collapse-bottom'); await look('after-bottom-up');
  await radio('truncated'); await clickBtn('expand-bottom'); await look('after-bottom-down');
  await radio('expanded'); await look('expanded');
  await radio('collapsed'); await look('collapsed');
  console.log('errors: ' + (errors.slice(0, 3).join(' | ') || 'none'));
  await b.close();
})();
