// Contrast of every piece of diagram text against what it is painted on, in both themes: the painted fill of each
// <text> (computed, so the page's token mapping counts) against the smallest filled shape under its centre, or the
// diagram's own ground. WCAG: 4.5:1 for text, 3:1 from 18.66px bold / 24px. Covers sequence diagrams (two scenarios
// opened), the Components tab, and an internal-flow popup when one opens.
// usage: node svg-contrast.js <page.local.html> [out.json]
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const path = require('path'), fs = require('fs');
(async () => {
  const [page, outFile] = process.argv.slice(2);
  const b = await chromium.launch({ channel: 'chrome' });
  const all = {};
  for (const theme of ['light', 'dark']) {
    const ctx = await b.newContext({ viewport: { width: 1400, height: 1000 } });
    await ctx.addInitScript(t => { try { localStorage.setItem('kronikol.theme', t); localStorage.removeItem('kronikol.reportSettings'); } catch (e) {} }, theme);
    const p = await ctx.newPage();
    await p.goto('file:///' + path.resolve(page).split(path.sep).join('/'));
    await p.waitForTimeout(1500);
    await p.evaluate(() => {
      const feats = Array.from(document.querySelectorAll('#report-content > details.feature'));
      [feats[1], feats[3]].forEach(f => { if (!f) return; f.open = true; const s = f.querySelector('details.scenario'); s.open = true; s.querySelectorAll('details').forEach(d => d.open = true); });
    });
    for (let i = 0; i < 60; i++) { await p.waitForTimeout(300); if (await p.evaluate(() => document.querySelectorAll('#report-content .plantuml-browser svg').length >= 2)) break; }
    // ASSERTIONS=1 (round 12): the assertions drawn in every diagram, and the failed scenarios opened as well, so a
    // passed (green) and a failed (red) assertion are measured with the rest. A diagram draws when it comes into view.
    if (process.env.ASSERTIONS) {
      await p.evaluate(() => { const t = document.querySelector('.toggle-btn[data-toggle="assertions"]'); if (t) t.click(); });
      const failed = await p.evaluate(() => {
        const list = Array.from(document.querySelectorAll('details.scenario[data-status="Failed"]'));
        list.forEach(s => { s.closest('details.feature').open = true; s.open = true; s.querySelectorAll('details').forEach(d => d.open = true); });
        return list.length;
      });
      for (let i = 0; i < failed; i++) {
        await p.evaluate(i => { const s = document.querySelectorAll('details.scenario[data-status="Failed"]')[i]; const d = s.querySelector('.diagram-view-seq .plantuml-browser') || s.querySelector('.plantuml-browser:not(.iflow-diagram)'); if (d) d.scrollIntoView({ block: 'center' }); }, i);
        await p.waitForFunction(i => { const s = document.querySelectorAll('details.scenario[data-status="Failed"]')[i]; return !!(s.querySelector('.diagram-view-seq .plantuml-browser svg') || s.querySelector('.plantuml-browser:not(.iflow-diagram) svg')); }, i, { polling: 200, timeout: 20000 }).catch(() => {});
      }
      const red = await p.evaluate(() => Array.from(document.querySelectorAll('#report-content .plantuml-browser svg polygon')).filter(e => /#f8d7da/i.test(e.getAttribute('fill') || '')).length);
      console.log('failed scenarios ' + failed + ', failed assertions drawn ' + red);
      await p.evaluate(() => window.scrollTo(0, 0));
      await p.waitForTimeout(800);
    }
    await p.waitForTimeout(800);
    const measure = () => p.evaluate(() => {
      const lum = c => { const m = c.match(/[\d.]+/g); if (!m) return null; const [r, g, bl] = m.slice(0, 3).map(v => { v = v / 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }); return 0.2126 * r + 0.7152 * g + 0.0722 * bl; };
      const alpha = c => { const m = c.match(/[\d.]+/g); return m && m.length > 3 ? +m[3] : 1; };
      const ratio = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
      const groundOf = el => { for (let e = el; e; e = e.parentElement) { const c = getComputedStyle(e).backgroundColor; if (c && alpha(c) > 0.5) return c; } return 'rgb(255, 255, 255)'; };
      const out = [];
      document.querySelectorAll('.plantuml-browser svg, #component-diagram svg, .iflow-diagram svg').forEach(svg => {
        if (!svg.getBoundingClientRect().width) return;
        const kind = svg.closest('#component-diagram') ? 'component' : svg.closest('.iflow-diagram') ? 'iflow' : 'sequence';
        const ground = groundOf(svg);
        const shapes = Array.from(svg.querySelectorAll('rect, path, polygon, ellipse, circle')).map(s => ({ s, b: s.getBBox(), f: getComputedStyle(s).fill })).filter(o => o.f && o.f !== 'none' && alpha(o.f) > 0.5 && o.b.width > 4 && o.b.height > 4);
        svg.querySelectorAll('text').forEach(t => {
          const tb = t.getBBox(); if (!tb.width || !t.textContent.trim()) return;
          const cs = getComputedStyle(t);
          if (cs.visibility === 'hidden' || cs.display === 'none' || parseFloat(cs.fontSize) === 0) return;
          const cx = tb.x + tb.width / 2, cy = tb.y + tb.height / 2;
          const under = shapes.filter(o => o.b.x <= cx && cx <= o.b.x + o.b.width && o.b.y <= cy && cy <= o.b.y + o.b.height);
          // the shape painted last (document order) is the one on top
          const top = under.length ? under[under.length - 1].f : ground;
          const r = ratio(cs.fill, top);
          const size = parseFloat(cs.fontSize), bold = parseInt(cs.fontWeight, 10) >= 700;
          const need = (size >= 24 || (size >= 18.66 && bold)) ? 3 : 4.5;
          out.push({ kind, text: t.textContent.trim().slice(0, 30), fill: cs.fill, bg: top, r: +r.toFixed(2), need });
        });
      });
      return out;
    });
    let rows = await measure();
    const comp = await p.$('#tab-components');
    if (comp) { await comp.click(); await p.waitForTimeout(3500); rows = rows.concat((await measure()).filter(r => r.kind === 'component')); }
    const fails = rows.filter(r => r.r < r.need);
    const byPair = {};
    fails.forEach(f => { const k = f.kind + ' ' + f.fill + ' on ' + f.bg; byPair[k] = byPair[k] || { n: 0, r: f.r, ex: f.text }; byPair[k].n++; });
    const kinds = {}; rows.forEach(r => { kinds[r.kind] = (kinds[r.kind] || 0) + 1; });
    console.log(`${theme}: ${rows.length} texts ${JSON.stringify(kinds)}, ${fails.length} below their minimum, lowest ${rows.length ? Math.min(...rows.map(r => r.r)).toFixed(2) : '-'}`);
    Object.entries(byPair).sort((a, b) => a[1].r - b[1].r).slice(0, 12).forEach(([k, v]) => console.log(`   ${String(v.n).padStart(4)}  ${v.r.toFixed(2)}  ${k}  e.g. "${v.ex}"`));
    all[theme] = { count: rows.length, fails: byPair };
    await ctx.close();
  }
  if (outFile) fs.writeFileSync(outFile, JSON.stringify(all, null, 1));
  await b.close();
})();
