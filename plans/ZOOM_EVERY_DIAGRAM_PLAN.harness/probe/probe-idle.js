'use strict';
// How wide a lined-up diagram would be if a part left out the participants it has no message for, so that its notes
// could run on past where their lifelines stand (the page would draw those lifelines in itself). For each diagram drawn
// in parts: each part drawn as it is and drawn without its idle participants, and the participants alone; then, from
// the gaps as drawn, the narrowest placement of every participant that gives each part the room it takes (the longest
// path over the gaps between neighbours, and between a part's own neighbours when it leaves some out), against its
// widest part today. "full" uses every part as it is, so it should come out as wide as R2's lined-up drawing.
// usage: node probe-idle.js <report.html> [width] [height]
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
      let seq = 0;
      function render(source) {
        return new Promise((resolve) => {
          const div = document.createElement('div');
          div.id = 'probe-idle-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { xs: lifelinesOf(svg), w: svg.width.baseVal.value } : { error: div.textContent.trim().slice(0, 120) };
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
      const msgRe = /^\s*([\w.]+)\s*(<?[-.]*(?:\[[^\]]*\])?[-.]*>?>?)\s*([\w.]+)\s*(?::(.*))?$/;
      // the declared participants in order, and those a part names outside its declarations
      function cast(source) {
        const lines = source.split('\n'), declared = [], used = new Set();
        let inNote = false;
        for (const l of lines) {
          const t = l.trim();
          if (inNote) { if (/^end\s*[rh]?note$/.test(t)) inNote = false; continue; }
          const d = l.match(decl);
          if (d) { declared.push({ alias: d[2] || t.split(/\s+/)[1], line: l }); continue; }
          if (/^[rh]?note\b/.test(t)) {
            const over = t.match(/^[rh]?note(?:<<[^>]*>>)?\s+(?:over|left of|right of)\s+([^:]+)/);
            if (over) over[1].split(',').forEach((a) => used.add(a.trim()));
            if (!/:/.test(t)) inNote = true;
            continue;
          }
          const m = t.match(msgRe);
          if (m && m[2].indexOf('-') >= 0) { used.add(m[1]); used.add(m[3]); continue; }
          const act = t.match(/^(?:activate|deactivate|destroy)\s+(\S+)/);
          if (act) used.add(act[1]);
        }
        return { declared, used };
      }
      const diagrams = Array.from(document.querySelectorAll('details.scenario [data-diagram-type="plantuml"]'))
        .filter((c) => c.querySelectorAll(':scope > .puml-fragment > svg').length > 1);
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      const rows = [];
      for (const c of diagrams) {
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        const sources = parts.map((p) => p.getAttribute('data-plantuml'));
        const row = { si: scenarios.indexOf(c.closest('details.scenario')), parts: parts.length };
        const casts = sources.map(cast);
        const n = casts[0].declared.length;
        if (casts.some((k) => k.declared.length !== n) || n < 2) { row.skip = 'cast'; rows.push(row); continue; }
        const full = await Promise.all(sources.map((s) => render(bust(s, 'full'))));
        if (full.some((r) => r.error || r.xs.length !== n)) { row.skip = 'full'; rows.push(row); continue; }
        // each part without its idle participants: the indices it keeps, and its drawing
        const keepIdx = casts.map((k) => k.declared.map((d, i) => (k.used.has(d.alias) ? i : -1)).filter((i) => i >= 0));
        const reduced = await Promise.all(sources.map((s, p) => {
          const drop = new Set(casts[p].declared.filter((d) => !casts[p].used.has(d.alias)).map((d) => d.line));
          return drop.size ? render(bust(s.split('\n').filter((l) => !drop.has(l)).join('\n'), 'reduced')) : Promise.resolve(full[p]);
        }));
        if (reduced.some((r, p) => r.error || r.xs.length !== keepIdx[p].length)) { row.skip = 'reduced'; rows.push(row); continue; }
        // the participants alone: the least gap between two neighbours, from their heads
        const prefix = sources[0].split('\n');
        let lastDecl = -1; prefix.forEach((l, i) => { if (decl.test(l)) lastDecl = i; });
        const heads = await render(bust(prefix.slice(0, lastDecl + 1).concat(['||400||', '@enduml']).join('\n'), 'heads'));
        if (heads.error || heads.xs.length !== n) { row.skip = 'heads'; rows.push(row); continue; }
        // the narrowest placement: x[0] = 0, each x the largest its constraints ask; then the part edges
        function place(drawings, idx) {
          const x = new Array(n).fill(0);
          for (let j = 1; j < n; j++) {
            x[j] = x[j - 1] + (heads.xs[j] - heads.xs[j - 1]);
            drawings.forEach((r, p) => {
              const k = idx[p].indexOf(j);
              if (k > 0) x[j] = Math.max(x[j], x[idx[p][k - 1]] + (r.xs[k] - r.xs[k - 1]));
            });
          }
          let left = Infinity, right = -Infinity;
          drawings.forEach((r, p) => {
            const f = idx[p][0], l = idx[p][idx[p].length - 1];
            left = Math.min(left, x[f] - r.xs[0]);
            right = Math.max(right, x[l] + (r.w - r.xs[r.xs.length - 1]));
          });
          return right - left;
        }
        const all = parts.map(() => Array.from({ length: n }, (_, i) => i));
        row.widest = Math.max(...full.map((r) => r.w));
        row.full = place(full, all);
        row.idle = place(reduced, keepIdx);
        row.idleParts = keepIdx.filter((k) => k.length < n).length;
        row.idleLifelines = keepIdx.reduce((s, k) => s + (n - k.length), 0);
        row.n = n;
        rows.push(row);
      }
      return rows;
    });
    const pc = (x) => (x >= 0 ? '+' : '') + (100 * x).toFixed(1) + '%';
    console.log('scenario parts participants | widest today | full (over widest) | idle left out (over widest) | parts with idle participants, idle lifelines');
    for (const r of out) {
      if (r.skip) { console.log(`${r.si} ${r.parts} skipped (${r.skip})`); continue; }
      console.log(`${r.si} ${r.parts} ${r.n} | ${Math.round(r.widest)} | ${Math.round(r.full)} (${pc(r.full / r.widest - 1)}) | ${Math.round(r.idle)} (${pc(r.idle / r.widest - 1)}) | ${r.idleParts}, ${r.idleLifelines}`);
    }
    const ok = out.filter((r) => !r.skip);
    const md = (a) => a.slice().sort((x, y) => x - y)[a.length >> 1];
    const f = ok.map((r) => r.full / r.widest - 1), i = ok.map((r) => r.idle / r.widest - 1);
    console.log(`${ok.length} diagrams: full median ${pc(md(f))} max ${pc(Math.max(...f))} (over 7%: ${f.filter((x) => x > 0.07).length}); ` +
      `idle left out median ${pc(md(i))} max ${pc(Math.max(...i))} (over 7%: ${i.filter((x) => x > 0.07).length}); ` +
      `diagrams with a part leaving someone out: ${ok.filter((r) => r.idleParts).length}`);
  } finally {
    await browser.close();
  }
})();
