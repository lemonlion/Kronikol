'use strict';
// Q7's cost on a whole report. Draws every diagram (as probe-widths.js does), then for each diagram drawn in parts:
//   first   its parts drawn again as they are (a line added so the engine's cache misses): what a drawing costs today
//   twice   its parts drawn with the row of hidden messages, labels sized to each gap's widest part: R2's second drawing
//   outline the whole diagram drawn once with each note cut to its longest lines (and every table row): what it costs,
//           and whether its gaps are at least every part's, so the row could be sized before the parts are drawn
//   lined   its parts drawn with the row sized from the outline: are they lined up on that one drawing
// Parts of one diagram are sent to the engine together, as the page's queue sends them; the times are wall-clock from
// the first send to the last picture, and the sum of each part's own time.
// usage: node probe-twice.js <report.html> [width] [height]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, vw = '1920', vh = '1000'] = process.argv.slice(2);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: +vw, height: +vh } })).newPage();
    page.on('pageerror', (e) => console.log('pageerror', e.message));
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    await page.evaluate(() => {
      document.querySelectorAll('details.feature, details.scenario').forEach((d) => { d.open = true; });
      window._renderDiagramsInContainer(document.body);
    });
    let last = -1, since = Date.now();
    for (;;) {
      const s = await page.evaluate(() => {
        const boxes = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'));
        let drawn = 0;
        boxes.forEach((c) => {
          const parts = c.querySelectorAll(':scope > .puml-fragment');
          if (parts.length ? Array.from(parts).every((p) => p.querySelector(':scope > svg')) : c.querySelector(':scope > svg')) drawn++;
        });
        return { total: boxes.length, drawn, busy: !!window._plantumlRendering };
      });
      if (s.drawn !== last) { last = s.drawn; since = Date.now(); }
      if ((s.drawn === s.total && !s.busy) || Date.now() - since > 60000) { console.log(`drawn ${s.drawn} of ${s.total}`); break; }
      await page.waitForTimeout(1000);
    }
    await page.waitForTimeout(3000);

    const out = await page.evaluate(async () => {
      function num(e, a) { return parseFloat(e.getAttribute(a)) || 0; }
      function lifelinesOf(svg) {
        const vb = svg.viewBox && svg.viewBox.baseVal;
        const height = vb && vb.height ? vb.height : 0;
        const cover = {};
        svg.querySelectorAll('line').forEach((e) => {
          if (Math.abs(num(e, 'x1') - num(e, 'x2')) < 0.5 && Math.abs(num(e, 'y2') - num(e, 'y1')) > 4) {
            const x = Math.round(num(e, 'x1') * 10) / 10;
            cover[x] = (cover[x] || 0) + Math.abs(num(e, 'y2') - num(e, 'y1'));
          }
        });
        return Object.keys(cover).filter((x) => cover[x] > height / 3).map(Number).sort((a, b) => a - b);
      }
      const gapsOf = (xs) => xs.slice(1).map((x, i) => x - xs[i]);
      let seq = 0;
      function render(source) {
        return new Promise((resolve) => {
          const div = document.createElement('div');
          div.id = 'probe-twice-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const t0 = performance.now();
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { xs: lifelinesOf(svg), ms: performance.now() - t0, texts: svg.querySelectorAll('text').length }
              : { error: div.textContent.trim().slice(0, 120), ms: performance.now() - t0 };
            div.remove();
            resolve(r);
          }
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id);
          done();
        });
      }
      async function renderAll(sources) {
        const t0 = performance.now();
        const rs = await Promise.all(sources.map(render));
        return { wall: performance.now() - t0, sum: rs.reduce((a, r) => a + r.ms, 0), rs };
      }
      const bust = (src, tag) => src.replace(/^@startuml.*$/m, (m) => m + "\n' probe " + tag + ' ' + Math.random().toString(36).slice(2));
      const decl = /^\s*(actor|participant|database|collections|queue|entity|boundary|control)\s+(?:"[^"]*"|\S+)(?:\s+as\s+(\S+))?/;
      function withRow(source, labels) {
        const lines = source.split('\n');
        let lastDecl = -1; const aliases = [];
        lines.forEach((l, i) => { const m = l.match(decl); if (m) { lastDecl = i; aliases.push(m[2] || l.trim().split(/\s+/)[1]); } });
        const auto = source.match(/^\s*autonumber\s+(\d+)/m);
        const row = auto ? ['autonumber stop'] : [];
        for (let i = 0; i + 1 < aliases.length; i++)
          row.push((i ? '& ' : '') + aliases[i] + ' -[hidden]> ' + aliases[i + 1] + ' : <color:transparent>' + '_'.repeat(labels[i]) + '</color>');
        if (auto) row.push('autonumber ' + auto[1]);
        return lines.slice(0, lastDecl + 1).concat(row, lines.slice(lastDecl + 1)).join('\n');
      }
      // a note cut to its longest lines: its header, its three longest lines without markup, and every table row
      function outline(source) {
        const lines = source.split('\n'), res = [];
        for (let i = 0; i < lines.length; i++) {
          const l = lines[i];
          const open = /^\s*[rh]?note\b(?!.*:)/.test(l);
          if (!open) { res.push(l); continue; }
          const body = []; let j = i + 1;
          while (j < lines.length && !/^\s*end\s*[rh]?note\s*$/.test(lines[j]) && !/^\s*end[rh]note\s*$/.test(lines[j])) body.push(lines[j++]);
          const strip = (s) => s.replace(/<[^>]+>/g, '').length;
          const keep = new Set(body.map((s, k) => ({ k, n: strip(s) })).sort((a, b) => b.n - a.n).slice(0, 3).map((o) => o.k));
          body.forEach((s, k) => { if (k === 0 || keep.has(k) || /^\s*\|/.test(s)) keep.add(k); });
          res.push(l); body.forEach((s, k) => { if (keep.has(k)) res.push(s); });
          if (j < lines.length) res.push(lines[j]);
          i = j;
        }
        return res.join('\n');
      }
      const diagrams = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'))
        .filter((c) => c.querySelectorAll(':scope > .puml-fragment > svg').length > 1);
      // the underscore's push, from two drawings of the first diagram's participants alone
      const head = (() => {
        const ls = diagrams[0].querySelector(':scope > .puml-fragment').getAttribute('data-plantuml').split('\n'); let lastDecl = -1;
        ls.forEach((l, i) => { if (decl.test(l)) lastDecl = i; });
        return ls.slice(0, lastDecl + 1).concat(['||400||', '@enduml']).join('\n');
      })();
      const n0 = (head.match(new RegExp(decl.source, 'gm')) || []).length;
      const a = await render(withRow(head, Array(n0).fill(100)));
      const b = await render(withRow(head, Array(n0).fill(1000)));
      const slope = (gapsOf(b.xs)[0] - gapsOf(a.xs)[0]) / 900, base = gapsOf(a.xs)[0] - 100 * slope;
      const labelsFor = (target) => target.map((g) => Math.max(1, Math.ceil((g + 0.5 - base) / slope)));
      const spread = (parts) => { // the widest disagreement between parts on any gap, in px
        const gs = parts.map((r) => gapsOf(r.xs)); if (gs.some((g) => g.length !== gs[0].length)) return null;
        return Math.max(...gs[0].map((_, i) => Math.max(...gs.map((g) => g[i])) - Math.min(...gs.map((g) => g[i]))));
      };
      const rows = [];
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      for (const c of diagrams) {
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        const sources = parts.map((p) => p.getAttribute('data-plantuml'));
        const drawnXs = parts.map((p) => lifelinesOf(p.querySelector(':scope > svg')));
        const row = { si: scenarios.indexOf(c.closest('details.scenario')), parts: parts.length, laid: c.clientWidth > 0,
          texts: parts.reduce((s, p) => s + p.querySelectorAll('svg text').length, 0) };
        if (drawnXs.some((x) => x.length !== drawnXs[0].length) || drawnXs[0].length < 2) { row.skip = 'lifelines ' + drawnXs.map((x) => x.length).join('/'); rows.push(row); continue; }
        const target = gapsOf(drawnXs[0]).map((_, i) => Math.max(...drawnXs.map((x) => gapsOf(x)[i])));
        row.spreadToday = spread(drawnXs.map((xs) => ({ xs })));
        const first = await renderAll(sources.map((s) => bust(s, 'first')));
        row.firstWall = first.wall; row.firstSum = first.sum;
        const twice = await renderAll(sources.map((s) => bust(withRow(s, labelsFor(target)), 'twice')));
        row.twiceWall = twice.wall; row.twiceSum = twice.sum; row.twiceSpread = spread(twice.rs);
        const full = c._fullSource || c.getAttribute('data-plantuml');
        if (full) {
          const o = await render(bust(outline(full), 'outline'));
          row.outlineMs = o.ms; row.outlineTexts = o.texts; row.outlineError = o.error || null;
          if (!o.error && o.xs.length === drawnXs[0].length) {
            const og = gapsOf(o.xs);
            row.outlineCovers = og.every((g, i) => g >= target[i] - 0.5);
            const lined = await renderAll(sources.map((s) => bust(withRow(s, labelsFor(og.map((g, i) => Math.max(g, 0)))), 'lined')));
            row.linedWall = lined.wall; row.linedSpread = spread(lined.rs);
          }
        }
        rows.push(row);
      }
      return { rows, workers: (window.__kronikolRender || {}).workers, maxParallel: window.plantuml.maxParallel, slope, base };
    });
    const r1 = (x) => (x == null ? '-' : Math.round(x));
    console.log(`workers ${out.workers}, in flight at most ${out.maxParallel}; an underscore pushes ${out.slope.toFixed(4)} px, base ${out.base.toFixed(1)}`);
    console.log('scenario parts texts | today: spread | first: wall sum | twice: wall sum spread | outline: ms texts covers | lined: wall spread');
    out.rows.forEach((r) => console.log(r.skip ? `${r.si} ${r.parts} skipped (${r.skip})` :
      `${r.si} ${r.parts} ${r.texts} | ${r1(r.spreadToday)} | ${r1(r.firstWall)} ${r1(r.firstSum)} | ${r1(r.twiceWall)} ${r1(r.twiceSum)} ${r.twiceSpread == null ? '-' : r.twiceSpread.toFixed(1)}` +
      ` | ${r1(r.outlineMs)} ${r.outlineTexts || '-'} ${r.outlineError ? 'ERROR ' + r.outlineError : r.outlineCovers} | ${r1(r.linedWall)} ${r.linedSpread == null ? '-' : r.linedSpread.toFixed(1)}`));
    const ok = out.rows.filter((r) => !r.skip);
    const sum = (k) => ok.reduce((a, r) => a + (r[k] || 0), 0);
    console.log(`totals over ${ok.length} diagrams: first wall ${r1(sum('firstWall'))} ms (sum ${r1(sum('firstSum'))}), twice wall ${r1(sum('twiceWall'))} ms (sum ${r1(sum('twiceSum'))}), outline ${r1(sum('outlineMs'))} ms; outline covers ${ok.filter((r) => r.outlineCovers).length}; lined to 1 px ${ok.filter((r) => r.linedSpread != null && r.linedSpread < 1).length}; twice to 1 px ${ok.filter((r) => r.twiceSpread != null && r.twiceSpread < 1).length}`);
  } finally {
    await browser.close();
  }
})();
