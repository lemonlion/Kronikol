'use strict';
// Where a lined-up diagram's extra width comes from, and what would take it away. For each diagram drawn in parts:
//   today   its parts drawn as they are; the widest sets the diagram's level under R1
//   lined   its parts lined up as R2 lines them (the lean outline, then the row; probe-compact.js with PROBE_LEAN=1)
//   whole   up to 8 parts, the whole diagram drawn as one picture, which any lined-up drawing has to be as wide as
// The extra width is split three ways against the widest part: the left side (left of the first lifeline), the span
// from the first lifeline to the last, and the right side. Then the same again with every source changed one way, the
// parts, the outline and the whole alike (PROBE_VARIANTS, a comma list, default all):
//   reqright  a request's note on the right of its arrow, beside the service (`note left` -> `note right`)
//   respleft  a reply's note on the left of its arrow, beside the caller (`note right` -> `note left`)
//   wrap600   notes wrapped at 600 px for 800 (`skinparam wrapWidth`)
//   overspan  a note beside its message drawn under it instead, across its two participants (`note over A, B`)
// usage: node probe-excess.js <report.html> [width] [height]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, vw = '1920', vh = '1000'] = process.argv.slice(2);
  const variants = ['base'].concat((process.env.PROBE_VARIANTS || 'reqright,respleft,wrap600,overspan').split(',').filter(Boolean));
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

    await page.evaluate((t) => { window.__truenum = t.truenum; window.__rowfix = t.rowfix; }, { truenum: !!process.env.PROBE_TRUENUM, rowfix: process.env.PROBE_ROWFIX !== '0' });
    const out = await page.evaluate(async (variants) => {
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
          div.id = 'probe-excess-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const mo = new MutationObserver(() => done());
          function done() {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return;
            mo.disconnect();
            const r = svg ? { xs: lifelinesOf(svg), w: svg.width.baseVal.value, h: svg.height.baseVal.value }
              : { error: div.textContent.trim().slice(0, 120) };
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
        // a gap given no label (0) gets no hidden message: it is as wide as the part makes it
        const pairs = [];
        for (let i = 0; i + 1 < aliases.length; i++)
          if (labels[i] > 0) pairs.push((pairs.length ? '& ' : '') + aliases[i] + ' -[hidden]> ' + aliases[i + 1] + ' : <color:transparent>' + '_'.repeat(labels[i]) + '</color>');
        if (!pairs.length) return source;
        const row = (auto ? ['autonumber stop'] : []).concat(pairs, auto ? ['autonumber ' + auto[1]] : []);
        return lines.slice(0, lastDecl + 1).concat(row, lines.slice(lastDecl + 1)).join('\n');
      }
      const headOf = (source) => {
        const ls = source.split('\n'); let lastDecl = -1;
        ls.forEach((l, i) => { if (decl.test(l)) lastDecl = i; });
        return ls.slice(0, lastDecl + 1).concat(['||400||', '@enduml']).join('\n');
      };
      const ctx = document.createElement('canvas').getContext('2d');
      ctx.font = '13px system-ui';
      const width = (text) => Math.max(0, ...String(text).split(/\\n/).map((s) => ctx.measureText(s.replace(/<[^>]+>/g, '')).width));
      const msgRe = /^\s*([\w.]+)\s*(<?[-.]*(?:\[[^\]]*\])?[-.]*>?>?)\s*([\w.]+)\s*(?::(.*))?$/;
      // the lean outline of probe-compact.js (PROBE_LEAN=1)
      function cutNote(block) {
        if (block.length <= 5) return block;
        const body = block.slice(1, -1);
        const widest = new Set(body.map((s, k) => ({ k, w: width(s) })).sort((x, y) => y.w - x.w).slice(0, 2).map((o) => o.k));
        return [block[0]].concat(body.filter((s, k) => k === 0 || widest.has(k) || /^\s*\|/.test(s)), [block[block.length - 1]]);
      }
      function compact(source) {
        const lines = source.split('\n');
        let lastDecl = -1; lines.forEach((l, i) => { if (decl.test(l)) lastDecl = i; });
        const head = lines.slice(0, lastDecl + 1), rest = lines.slice(lastDecl + 1);
        const units = []; let messages = 0;
        const auto = source.match(/^\s*autonumber\s+(\d+)/m), start = auto ? +auto[1] : 0;
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
          if (m && m[2].indexOf('-') >= 0) {
            // PROBE_TRUENUM=1: a message keeps its own number in the outline, so it is ranked and drawn as wide as it is
            const num = start + messages++;
            units.push({ kind: 'msg', from: m[1], arrow: m[2].replace(/\[[^\]]*\]/, ''), to: m[3], lines: [l], num,
              w: width(m[4] || '') + (window.__truenum && auto ? width(num + ' ') : 0) });
            continue;
          }
          units.push({ kind: 'other', lines: [l] });
        }
        units.forEach((u) => { if (u.kind === 'msg') u.key = 'msg|' + u.from + '|' + u.arrow + '|' + u.to + '|' + (u.side || ''); });
        const byKey = new Map();
        units.forEach((u, i) => { if (!u.key) return; if (!byKey.has(u.key)) byKey.set(u.key, []); byKey.get(u.key).push(i); });
        const keep = new Set(units.map((u, i) => (u.key ? -1 : i)).filter((i) => i >= 0));
        for (const [, idx] of byKey) {
          idx.slice().sort((a, b) => units[b].w - units[a].w).slice(0, 1).forEach((i) => keep.add(i));
          idx.slice().sort((a, b) => (units[b].wNote || 0) - (units[a].wNote || 0)).slice(0, 1).forEach((i) => keep.add(i));
        }
        const lastNumber = auto ? +auto[1] + messages - 1 : 0;
        const outHead = head.map((l) => (auto && /^\s*autonumber\s+\d+/.test(l) ? 'autonumber ' + Math.max(+auto[1], Math.pow(10, String(lastNumber).length - 1)) : l));
        const body = [];
        units.forEach((u, i) => { if (keep.has(i)) { if (window.__truenum && auto && u.kind === 'msg') body.push('autonumber ' + u.num); body.push(...(u.kind === 'msg' ? u.lines : cutNote(u.lines))); if (u.note) body.push(...cutNote(u.note)); } });
        return outHead.concat(body, ['@enduml']).join('\n');
      }
      const change = {
        base: (s) => s,
        reqright: (s) => s.replace(/^(\s*note(?:<<[^>]*>>)?\s+)left(\s*)$/gm, '$1right$2'),
        respleft: (s) => s.replace(/^(\s*note(?:<<[^>]*>>)?\s+)right(\s*)$/gm, '$1left$2'),
        wrap600: (s) => s.replace(/^(\s*skinparam\s+wrapWidth\s+)\d+/m, '$1600'),
        // a note beside its message drawn under it instead, across the message's two participants
        overspan: (s) => {
          const lines = s.split('\n'); let msg = null;
          for (let i = 0; i < lines.length; i++) {
            const t = lines[i].trim();
            const n = t.match(/^note(<<[^>]*>>)?\s+(left|right)\s*$/);
            if (n) {
              if (msg && msg[1] !== msg[3]) lines[i] = lines[i].replace(/^(\s*)note(<<[^>]*>>)?\s+(left|right)\s*$/, '$1note$2 over ' + msg[1] + ', ' + msg[3]);
              while (i + 1 < lines.length && !/^\s*end\s*note\s*$/.test(lines[i + 1])) i++;
              i++; msg = null; continue;
            }
            const m = t.match(msgRe);
            msg = m && m[2].indexOf('-') >= 0 ? m : (t && !t.startsWith("'") ? null : msg);
          }
          return lines.join('\n');
        },
      };
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
      // PROBE_ROWFIX (on unless 0): the row drawn over the participants alone first, each label then lengthened or
      // shortened by how far its gap came out from its target (the push of an underscore is the same in every gap, the
      // gap a label starts from is not), up to three times; a gap the participants alone make wide enough gets no label
      async function rowFor(source, target) {
        if (!window.__rowfix) return labelsFor(target);
        const h = headOf(source);
        const alone = gapsOf((await render(bust(h, 'heads'))).xs);
        const labels = labelsFor(target).map((n, i) => (target[i] <= alone[i] + 0.5 ? 0 : n));
        for (let k = 0; k < 3; k++) {
          const got = gapsOf((await render(bust(withRow(h, labels), 'rowfix'))).xs);
          let moved = false;
          labels.forEach((n, i) => {
            if (!n) return;
            const err = got[i] - (target[i] + 0.5);
            if (Math.abs(err) < 0.6) return;
            const m = Math.max(1, n - Math.round(err / slope));
            if (m !== n) { labels[i] = m; moved = true; }
          });
          if (!moved) break;
        }
        return labels;
      }
      // the left side, the span and the right side of a drawing
      const sides = (r) => ({ L: r.xs[0], S: r.xs[r.xs.length - 1] - r.xs[0], R: r.w - r.xs[r.xs.length - 1], W: r.w });
      const scenarios = Array.from(document.querySelectorAll('details.scenario'));
      const rows = [];
      for (const c of diagrams) {
        const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
        const row = { si: scenarios.indexOf(c.closest('details.scenario')), parts: parts.length, v: {} };
        const full = c._fullSource || c.getAttribute('data-plantuml');
        for (const v of variants) {
          const f = change[v];
          const sources = parts.map((p) => f(p.getAttribute('data-plantuml')));
          const today = await Promise.all(sources.map((s) => render(bust(s, 'today'))));
          if (today.some((r) => r.error || r.xs.length !== today[0].xs.length) || today[0].xs.length < 2) { row.v[v] = { skip: 'today' }; continue; }
          const o = await render(bust(compact(f(full)), 'outline'));
          if (o.error || o.xs.length !== today[0].xs.length) { row.v[v] = { skip: 'outline' }; continue; }
          const labels = await rowFor(sources[0], gapsOf(o.xs));
          const lined = await Promise.all(sources.map((s) => render(bust(withRow(s, labels), 'lined'))));
          if (lined.some((r) => r.error || r.xs.length !== today[0].xs.length)) { row.v[v] = { skip: 'lined' }; continue; }
          const wi = today.reduce((k, r, i) => (r.w > today[k].w ? i : k), 0);
          const first = Math.max(...lined.map((r) => r.xs[0]));
          const lg = lined.map((r) => gapsOf(r.xs));
          const res = {
            widest: sides(today[wi]),
            lined: { L: first, S: Math.max(...lined.map((r) => sides(r).S)), R: Math.max(...lined.map((r) => sides(r).R)),
              W: Math.max(...lined.map((r) => first - r.xs[0] + r.w)) },
            spread: Math.max(...lg[0].map((_, i) => Math.max(...lg.map((g) => g[i])) - Math.min(...lg.map((g) => g[i])))),
            height: today.reduce((s, r) => s + r.h, 0),
          };
          if (parts.length <= 8) {
            const w = await render(bust(f(full), 'whole'));
            res.whole = w.error ? { error: w.error } : sides(w);
          }
          row.v[v] = res;
        }
        rows.push(row);
      }
      return { rows, slope, base };
    }, variants);

    const pc = (x) => (x >= 0 ? '+' : '') + (100 * x).toFixed(1) + '%';
    const r0 = (x) => Math.round(x);
    console.log(`underscore: ${out.slope.toFixed(4)} px each over ${out.base.toFixed(1)}`);
    console.log('scenario parts | widest today L S R = W | lined L S R = W (over widest) | whole W | spread');
    for (const r of out.rows) {
      const b = r.v.base;
      if (!b || b.skip) { console.log(`${r.si} ${r.parts} skipped (${b ? b.skip : '-'})`); continue; }
      console.log(`${r.si} ${r.parts} | ${r0(b.widest.L)} ${r0(b.widest.S)} ${r0(b.widest.R)} = ${r0(b.widest.W)} | ` +
        `${r0(b.lined.L)} ${r0(b.lined.S)} ${r0(b.lined.R)} = ${r0(b.lined.W)} (${pc(b.lined.W / b.widest.W - 1)}) | ` +
        `${b.whole ? (b.whole.error ? 'ERROR' : r0(b.whole.W)) : '-'} | ${b.spread.toFixed(1)}`);
    }
    // the extra width by side, summed over the diagrams
    const ok = out.rows.filter((r) => r.v.base && !r.v.base.skip);
    const by = (k) => ok.reduce((s, r) => s + (r.v.base.lined[k] - r.v.base.widest[k]), 0);
    console.log(`extra width over ${ok.length} diagrams, by side: left ${r0(by('L'))} px, span ${r0(by('S'))} px, right ${r0(by('R'))} px, total ${r0(by('W'))} px`);
    const wh = ok.filter((r) => r.v.base.whole && !r.v.base.whole.error);
    const dw = wh.map((r) => r.v.base.lined.W - r.v.base.whole.W).sort((x, y) => x - y);
    if (dw.length) console.log(`lined minus whole, ${dw.length} diagrams of up to 8 parts: median ${dw[dw.length >> 1].toFixed(1)} px, min ${dw[0].toFixed(1)}, max ${dw[dw.length - 1].toFixed(1)}`);
    const md = (a) => a.slice().sort((x, y) => x - y)[a.length >> 1];
    const mx = (a) => Math.max(...a);
    console.log('variant | diagrams | lined over its own widest: median max (over 7%) | lined over today\'s widest: median max | whole over today\'s widest: median max (over 7%) | height over today | spread max');
    for (const v of variants) {
      const rs = out.rows.filter((r) => r.v[v] && !r.v[v].skip && r.v.base && !r.v.base.skip);
      const own = rs.map((r) => r.v[v].lined.W / r.v[v].widest.W - 1);
      const vsToday = rs.map((r) => r.v[v].lined.W / r.v.base.widest.W - 1);
      const tall = rs.map((r) => r.v[v].height / r.v.base.height - 1);
      const whole = rs.filter((r) => r.v[v].whole && !r.v[v].whole.error).map((r) => r.v[v].whole.W / r.v.base.widest.W - 1);
      console.log(`${v} | ${rs.length} | ${pc(md(own))} ${pc(mx(own))} (${own.filter((x) => x > 0.07).length}) | ${pc(md(vsToday))} ${pc(mx(vsToday))} | ` +
        `${whole.length ? pc(md(whole)) + ' ' + pc(mx(whole)) + ' (' + whole.filter((x) => x > 0.07).length + ')' : '-'} | ` +
        `${pc(md(tall))} ${pc(mx(tall))} | ${mx(rs.map((r) => r.v[v].spread)).toFixed(1)}`);
    }
    console.log('per diagram, lined over today\'s widest: ' + variants.join(' / '));
    console.log('per diagram, widths: the widest part today | the whole, ' + variants.join(' / '));
    for (const r of ok) console.log(`${r.si} ${r.parts}: ${Math.round(r.v.base.widest.W)} | ` + variants.map((v) => (r.v[v] && r.v[v].whole && !r.v[v].whole.error ? Math.round(r.v[v].whole.W) : '-')).join(' / '));
    for (const r of ok) console.log(`${r.si} ${r.parts}: ` + variants.map((v) => (r.v[v] && !r.v[v].skip ? pc(r.v[v].lined.W / r.v.base.widest.W - 1) : '-')).join(' / '));
  } finally {
    await browser.close();
  }
})();
