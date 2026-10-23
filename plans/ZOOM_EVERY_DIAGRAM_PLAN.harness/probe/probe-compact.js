'use strict';
// Q7 without a second drawing: a compact outline of each diagram in parts, drawn first, sizes the row of hidden
// messages so that every part is drawn once, already lined up. The outline keeps the participants and, of the body,
// for each kind of message (its two participants and its arrow) with each side a note may be attached on, the three
// widest labels and the three widest attached notes, and the three widest step bars (`hnote across`); its numbering
// starts at a number as long as the diagram's last, so the numbers are as wide as the diagram's. For every diagram:
// the outline's drawing time and size, whether its gaps reach every part's gaps as drawn ("covers"), and the parts drawn
// with the row sized from it: how far apart their gaps are ("spread") and how much wider the lined-up diagram is than
// its widest part today. usage: node probe-compact.js <report.html> [width] [height]
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
    await page.evaluate((lean) => { window.__lean = lean; }, !!process.env.PROBE_LEAN);

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
          div.id = 'probe-compact-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const t0 = performance.now();
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { xs: lifelinesOf(svg), ms: performance.now() - t0, texts: svg.querySelectorAll('text').length, w: svg.width.baseVal.value }
              : { error: div.textContent.trim().slice(0, 120), ms: performance.now() - t0 };
            div.remove();
            resolve(r);
          }
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id);
          done();
        });
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
      const ctx = document.createElement('canvas').getContext('2d');
      ctx.font = '13px system-ui';
      const width = (text) => Math.max(0, ...String(text).split(/\\n/).map((s) => ctx.measureText(s.replace(/<[^>]+>/g, '')).width));
      const msgRe = /^\s*([\w.]+)\s*(<?[-.]*(?:\[[^\]]*\])?[-.]*>?>?)\s*([\w.]+)\s*(?::(.*))?$/;
      // PROBE_LEAN=1: the widest one of each kind, and a kept note cut to its first line, its two widest lines and its
      // table rows (a note is as wide as its widest line); else the three widest of each kind, notes whole
      const TOP = window.__lean ? 1 : 3;
      function cutNote(block) {
        if (!window.__lean || block.length <= 5) return block;
        const body = block.slice(1, -1);
        const widest = new Set(body.map((s, k) => ({ k, w: width(s) })).sort((x, y) => y.w - x.w).slice(0, 2).map((o) => o.k));
        return [block[0]].concat(body.filter((s, k) => k === 0 || widest.has(k) || /^\s*\|/.test(s)), [block[block.length - 1]]);
      }
      function compact(source) {
        const lines = source.split('\n');
        let lastDecl = -1; lines.forEach((l, i) => { if (decl.test(l)) lastDecl = i; });
        const head = lines.slice(0, lastDecl + 1), rest = lines.slice(lastDecl + 1);
        const units = []; let messages = 0;
        for (let i = 0; i < rest.length; i++) {
          const l = rest[i], t = l.trim();
          if (!t || t.startsWith("'")) continue;
          if (/^@enduml/.test(t)) break;
          if (/^[rh]?note\b/.test(t)) {
            const block = [l]; let j = i;
            if (!/:/.test(t)) {
              j++;
              while (j < rest.length && !/^\s*end\s*[rh]?note\s*$/.test(rest[j])) block.push(rest[j++]);
              if (j < rest.length) block.push(rest[j]);
            }
            i = j;
            const w = /:/.test(t) ? width(t.slice(t.indexOf(':') + 1)) : Math.max(0, ...block.slice(1, -1).map(width));
            const prev = units[units.length - 1];
            const side = (t.match(/^note\s+(left|right)\s*$/) || [])[1];
            if (side && prev && prev.kind === 'msg' && !prev.note) { prev.note = block; prev.wNote = w; prev.side = side; }
            else units.push({ kind: /^hnote\s+across/.test(t) ? 'across' : 'note', key: /^hnote\s+across/.test(t) ? 'across' : 'note|' + t.replace(/\s+/g, ' '), lines: block, w });
            continue;
          }
          const m = t.match(msgRe);
          if (m && m[2].indexOf('-') >= 0) { messages++; units.push({ kind: 'msg', from: m[1], arrow: m[2].replace(/\[[^\]]*\]/, ''), to: m[3], lines: [l], w: width(m[4] || '') }); continue; }
          units.push({ kind: 'other', lines: [l] });
        }
        units.forEach((u) => { if (u.kind === 'msg') u.key = 'msg|' + u.from + '|' + u.arrow + '|' + u.to + '|' + (u.side || ''); });
        const byKey = new Map();
        units.forEach((u, i) => { if (!u.key) return; if (!byKey.has(u.key)) byKey.set(u.key, []); byKey.get(u.key).push(i); });
        const keep = new Set(units.map((u, i) => (u.key ? -1 : i)).filter((i) => i >= 0));
        for (const [, idx] of byKey) {
          idx.slice().sort((a, b) => units[b].w - units[a].w).slice(0, TOP).forEach((i) => keep.add(i));
          idx.slice().sort((a, b) => (units[b].wNote || 0) - (units[a].wNote || 0)).slice(0, TOP).forEach((i) => keep.add(i));
        }
        // the numbers as long as the diagram's last one
        const auto = source.match(/^\s*autonumber\s+(\d+)/m);
        const lastNumber = auto ? +auto[1] + messages - 1 : 0;
        const outHead = head.map((l) => (auto && /^\s*autonumber\s+\d+/.test(l) ? 'autonumber ' + Math.max(+auto[1], Math.pow(10, String(lastNumber).length - 1)) : l));
        const body = [];
        units.forEach((u, i) => { if (keep.has(i)) { body.push(...(u.kind === "msg" ? u.lines : cutNote(u.lines))); if (u.note) body.push(...cutNote(u.note)); } });
        return { source: outHead.concat(body, ['@enduml']).join('\n'), units: units.length, kept: keep.size };
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
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      const rows = [];
      for (const c of diagrams) {
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        const sources = parts.map((p) => p.getAttribute('data-plantuml'));
        const svgs = parts.map((p) => p.querySelector(':scope > svg'));
        const drawnXs = svgs.map(lifelinesOf);
        const row = { si: scenarios.indexOf(c.closest('details.scenario')), parts: parts.length, widest: Math.round(Math.max(...svgs.map((s) => s.width.baseVal.value))) };
        if (drawnXs.some((x) => x.length !== drawnXs[0].length) || drawnXs[0].length < 2) { row.skip = 'lifelines ' + drawnXs.map((x) => x.length).join('/'); rows.push(row); continue; }
        const target = gapsOf(drawnXs[0]).map((_, i) => Math.max(...drawnXs.map((x) => gapsOf(x)[i])));
        const full = c._fullSource || c.getAttribute('data-plantuml');
        const cp = compact(full);
        row.units = cp.units; row.kept = cp.kept;
        const o = await render(bust(cp.source, 'compact'));
        row.ms = o.ms; row.texts = o.texts; row.error = o.error || null;
        if (o.error || o.xs.length !== drawnXs[0].length) { rows.push(row); continue; }
        const og = gapsOf(o.xs);
        row.short = Math.max(0, ...og.map((g, i) => target[i] - g));   // how far the outline falls short of a part's gap
        const lined = await Promise.all(sources.map((s) => render(bust(withRow(s, labelsFor(og)), 'lined'))));
        if (lined.some((r) => r.error || r.xs.length !== drawnXs[0].length)) { row.linedError = true; rows.push(row); continue; }
        const lg = lined.map((r) => gapsOf(r.xs));
        row.spread = Math.max(...lg[0].map((_, i) => Math.max(...lg.map((g) => g[i])) - Math.min(...lg.map((g) => g[i]))));
        const first = Math.max(...lined.map((r) => r.xs[0]));
        row.linedWidth = Math.round(Math.max(...lined.map((r) => first - r.xs[0] + r.w)));
        // against today: the parts drawn as they are, and R2 (the outline, then the parts with the row), each from the
        // first send to the last picture, in turn, three rounds, every drawing missing the cache
        row.base = []; row.r2 = [];
        for (let k = 0; k < 3; k++) {
          let t0 = performance.now();
          await Promise.all(sources.map((s) => render(bust(s, 'base'))));
          row.base.push(performance.now() - t0);
          t0 = performance.now();
          const oo = await render(bust(cp.source, 'r2'));
          const g2 = oo.xs && oo.xs.length > 1 ? gapsOf(oo.xs) : og;
          await Promise.all(sources.map((s) => render(bust(withRow(s, labelsFor(g2)), 'r2'))));
          row.r2.push(performance.now() - t0);
        }
        rows.push(row);
      }
      return { rows, slope, base };
    });
    const r1 = (x) => (x == null ? '-' : Math.round(x));
    console.log('scenario parts | outline: units kept ms texts short | lined: spread width (widest part today)');
    out.rows.forEach((r) => console.log(r.skip ? `${r.si} ${r.parts} skipped (${r.skip})` :
      `${r.si} ${r.parts} | ${r.units} ${r.kept} ${r1(r.ms)} ${r.texts || '-'} ${r.error ? 'ERROR ' + r.error : (r.short == null ? '-' : r.short.toFixed(1))} | ` +
      `${r.spread == null ? (r.linedError ? 'ERROR' : '-') : r.spread.toFixed(1)} ${r1(r.linedWidth)} (${r.widest})`));
    const md = (a) => a.slice().sort((x, y) => x - y)[Math.floor(a.length / 2)];
    console.log('against today (median of 3, ms): scenario parts | today | R2 | R2 over today');
    out.rows.filter((r) => r.base && r.base.length).forEach((r) => console.log(`${r.si} ${r.parts} | ${Math.round(md(r.base))} | ${Math.round(md(r.r2))} | ${(100 * (md(r.r2) / md(r.base) - 1)).toFixed(0)}%`));
    const both = out.rows.filter((r) => r.base && r.base.length);
    const tb = both.reduce((a, r) => a + md(r.base), 0), tr = both.reduce((a, r) => a + md(r.r2), 0);
    const over = both.map((r) => md(r.r2) - md(r.base)).sort((x, y) => x - y);
    console.log(`against today, ${both.length} diagrams: today ${Math.round(tb)} ms, R2 ${Math.round(tr)} ms (+${(100 * (tr / tb - 1)).toFixed(1)}%); R2 adds median ${Math.round(over[Math.floor(over.length / 2)])} ms, min ${Math.round(over[0])}, max ${Math.round(over[over.length - 1])}`);
    const ok = out.rows.filter((r) => r.spread != null);
    const ms = out.rows.filter((r) => r.ms != null).map((r) => r.ms).sort((x, y) => x - y);
    const grow = ok.map((r) => r.linedWidth / r.widest - 1).sort((x, y) => x - y);
    console.log(`${out.rows.length} diagrams: outline drawn in median ${r1(ms[Math.floor(ms.length / 2)])} ms, max ${r1(ms[ms.length - 1])} ms; ` +
      `short of a part's gap by over 0.5 px in ${out.rows.filter((r) => r.short > 0.5).length}; lined to 1 px ${ok.filter((r) => r.spread < 1).length} of ${ok.length}; ` +
      `width over the widest part: median ${(100 * grow[Math.floor(grow.length / 2)]).toFixed(1)}%, max ${(100 * grow[grow.length - 1]).toFixed(1)}%`);
  } finally {
    await browser.close();
  }
})();
