'use strict';
// Q7: can the parts of a diagram be laid out alike? Draws one scenario's diagram in parts, then renders each part's
// source again through the page's own engine (window.plantuml.render) and measures where its lifelines stand: as
// drawn, and with one row of hidden messages added after the participants, one between each pair of neighbours, whose
// labels are sized so that every gap is the widest that gap is in any part. Lifelines are found as the page's role
// pass finds them (plantuml-browser-render-script.js tagRoles): vertical lines covering a third of the picture.
// usage: node probe-align.js <report.html> <scenarioIndex> [labelStyle: plain|transparent]
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
  const [file, siArg, labelStyle = 'transparent'] = process.argv.slice(2);
  const browser = await launchInstalled(chromium, { headless: true });
  try {
    const page = await (await browser.newContext({ viewport: { width: 1920, height: 1000 } })).newPage();
    page.on('pageerror', (e) => console.log('pageerror', e.message));
    await page.goto('file:///' + path.resolve(file).split(path.sep).join('/'), { timeout: 600000 });
    await page.evaluate((si) => {
      const sc = document.querySelectorAll('details.scenario')[si];
      sc.setAttribute('data-probe', '1');
      sc.closest('details.feature').open = true; sc.open = true;
      window._renderDiagramsInContainer(sc);
    }, +siArg);
    await page.waitForFunction(() => {
      const parts = document.querySelectorAll('details.scenario[data-probe] [data-diagram-type="plantuml"] > .puml-fragment');
      return parts.length > 1 && Array.from(parts).every((p) => p.querySelector(':scope > svg'));
    }, null, { timeout: 180000, polling: 200 });

    // PROBE_CAP=1 measures how wide one label pushes its gap, at the lengths PROBE_CAP_STEPS lists
    if (process.env.PROBE_CAP) await page.evaluate((steps) => { window.__probeCap = steps; },
      (process.env.PROBE_CAP_STEPS || '100,500,1000,2000,4000,8000').split(',').map(Number));
    const result = await page.evaluate(async (labelStyle) => {
      const sources = Array.from(document.querySelectorAll('details.scenario[data-probe] [data-diagram-type="plantuml"] > .puml-fragment'))
        .map((p) => p.getAttribute('data-plantuml'));
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
          div.id = 'probe-align-' + (seq++);
          div.style.cssText = 'position:absolute;left:-300000px;top:0;visibility:hidden';
          document.body.appendChild(div);
          const t0 = performance.now();
          const done = () => {
            const svg = div.querySelector('svg');
            if (!svg && !div.textContent.trim()) return false;
            mo.disconnect();
            const r = svg ? { xs: lifelinesOf(svg), w: svg.width.baseVal.value, h: svg.height.baseVal.value, ms: Math.round(performance.now() - t0),
              texts: Array.from(svg.querySelectorAll('text')).map((t) => t.textContent), shapes: 'polygons ' + svg.querySelectorAll('polygon').length + ', horizontal lines ' + Array.from(svg.querySelectorAll('line')).filter((e) => Math.abs(num(e, 'y1') - num(e, 'y2')) < 0.5).length + ', vertical lines ' + Array.from(svg.querySelectorAll('line')).filter((e) => Math.abs(num(e, 'x1') - num(e, 'x2')) < 0.5).length } : { error: div.textContent.trim().slice(0, 200) };
            div.remove();
            resolve(r);
            return true;
          };
          const mo = new MutationObserver(done);
          mo.observe(div, { childList: true, subtree: true, characterData: true });
          window.plantuml.render(source.split('\n'), div.id);
          done();
        });
      }
      const decl = /^\s*(actor|participant|database|collections|queue|entity|boundary|control)\s+(?:"[^"]*"|\S+)(?:\s+as\s+(\S+))?/;
      function withSpacers(source, labels) {
        const lines = source.split('\n');
        let last = -1; const aliases = [];
        lines.forEach((l, i) => { const m = l.match(decl); if (m) { last = i; aliases.push(m[2] || l.trim().split(/\s+/)[1]); } });
        const auto = source.match(/^\s*autonumber\s+(\d+)/m);
        const label = (n) => { const t = '_'.repeat(n); return labelStyle === 'plain' ? t : '<color:transparent>' + t + '</color>'; };
        // the hidden messages take no number: a number is part of a message's label, and one of two digits is wider
        // than one of one, which made the gaps of a part numbered from 38 wider than those of a part numbered from 1
        const row = auto ? ['autonumber stop'] : [];
        for (let i = 0; i + 1 < aliases.length; i++) row.push((i ? '& ' : '') + aliases[i] + ' -[hidden]> ' + aliases[i + 1] + ' : ' + label(labels[i]));
        if (auto) row.push('autonumber ' + auto[1]);
        return { source: lines.slice(0, last + 1).concat(row, lines.slice(last + 1)).join('\n'), aliases };
      }
      const out = { parts: [] };
      // 1. each part as drawn
      for (const s of sources) out.parts.push({ asDrawn: await render(s) });
      const gapsOf = (xs) => xs.slice(1).map((x, i) => Math.round((x - xs[i]) * 10) / 10);
      const n = out.parts[0].asDrawn.xs.length;
      if (!out.parts.every((p) => p.asDrawn.xs && p.asDrawn.xs.length === n)) return { error: 'parts differ in lifeline count', out };
      const target = gapsOf(out.parts[0].asDrawn.xs).map((_, i) => Math.max(...out.parts.map((p) => gapsOf(p.asDrawn.xs)[i])));
      out.target = target;
      // 2. calibrate on the participants alone (the first part's lines up to its last declaration, then the spacer row):
      // labels grown until every gap reaches the target
      const head = (() => {
        const lines = sources[0].split('\n'); let last = -1;
        lines.forEach((l, i) => { if (decl.test(l)) last = i; });
        // a tall empty space, so the lifelines cover a third of the picture as the role pass's rule asks
        return lines.slice(0, last + 1).concat(['||400||', '@enduml']).join('\n');
      })();
      if (window.__probeCap) {
        // how wide one label can push its gap: the first gap's label at growing lengths, the others at 1
        const cap = [];
        for (const k of window.__probeCap) {
          const r = await render(withSpacers(head, target.map((_, i) => i ? 1 : k)).source);
          cap.push({ k, gap: r.xs && r.xs.length > 1 ? Math.round((r.xs[1] - r.xs[0]) * 10) / 10 : null, h: r.h, w: r.w, lifelines: r.xs ? r.xs.length : 0, ms: r.ms, error: r.error });
        }
        return { cap };
      }
      let labels = target.map(() => 1);
      const steps = [];
      for (let iter = 0; iter < 8; iter++) {
        const r = await render(withSpacers(head, labels).source);
        if (r.error) return { error: r.error, labels, out };
        const g = gapsOf(r.xs);
        steps.push({ labels: labels.slice(), gaps: g });
        if (g.length !== target.length) return { error: 'calibration drew ' + r.xs.length + ' lifelines', labels, out };
        if (g.every((x, i) => x >= target[i] - 0.05)) break;
        // underscores: a width per character from the last two tries, 7 px before there are two
        labels = labels.map((k, i) => {
          if (g[i] >= target[i] - 0.05) return k;
          const prev = steps.length > 1 ? steps[steps.length - 2] : null;
          const per = prev && prev.labels[i] !== k && prev.gaps[i] !== g[i] ? Math.max(1, (g[i] - prev.gaps[i]) / (k - prev.labels[i])) : 7;
          return k + Math.max(1, Math.ceil((target[i] - g[i]) / per));
        });
      }
      out.calibration = steps;
      // 3. every part with the same spacer row
      for (let p = 0; p < sources.length; p++) {
        const sp = withSpacers(sources[p], labels);
        const r = await render(sp.source);
        out.parts[p].aligned = r.error ? r : { xs: r.xs, w: r.w, h: r.h, ms: r.ms, shapes: r.shapes, underscoresDrawn: r.texts.filter((t) => /_{3,}/.test(t)).length };
        if (p === 0) out.spacerRowExample = sp.source.split('\n').filter((l) => /-\[hidden\]>|^autonumber/.test(l)).map((l) => l.replace(/_+/g, (m) => '_x' + m.length));
      }
      out.labels = labels;
      return out;
    }, labelStyle);
    if (result.cap) { result.cap.forEach((c) => console.log(`label of ${c.k} underscores: first gap ${c.gap} px, ${c.lifelines} lifelines, picture ${c.w} x ${c.h}, ${c.ms} ms${c.error ? ' ERROR ' + c.error : ''}`)); return; }
    if (result.error) { console.log('ERROR', result.error, JSON.stringify(result.labels || '')); }
    const o = result.out || result;
    (o.parts || []).forEach((p, i) => {
      const d = p.asDrawn, a = p.aligned;
      console.log(`part ${i + 1}: as drawn ${d.w}x${d.h} in ${d.ms} ms, lifelines ${d.xs.join(' ')} (${d.shapes})`);
      if (a) console.log(`        aligned ${a.error ? 'ERROR ' + a.error : a.w + 'x' + a.h + ' in ' + a.ms + ' ms, lifelines ' + a.xs.join(' ') + ', underscores drawn ' + a.underscoresDrawn + ' (' + a.shapes + ')'}`);
    });
    if (o.target) console.log('target gaps', o.target.join(' '), '| labels', (o.labels || []).join(' '));
    if (o.calibration) o.calibration.forEach((s, i) => console.log(`calibration ${i}: labels ${s.labels.join(' ')} -> gaps ${s.gaps.join(' ')}`));
    if (o.spacerRowExample) console.log('spacer row:', o.spacerRowExample.join(' | '));
  } finally {
    await browser.close();
  }
})();
