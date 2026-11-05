// Prints every scenario of a report as a tree: its result, sections, steps, each step's status and duration.
const L = require('./lib.js');
const path = require('path');
const R = L.R;
(async () => {
  await L.start();
  const res = {};
  for (const b of ['old', 'new']) {
    const ctx = await (await require('<tree>/tools/playwright/installed-browser.js').launchInstalled(L.pw.chromium, { headless: true })).newContext();
    const p = await ctx.newPage();
    await p.goto('file:///' + path.join(R, `hook-${b}/bin/Debug/net10.0/Reports/${process.argv[2] || 'TestRunReport.html'}`).replace(/\\/g, '/'));
    await p.locator('details.feature').first().waitFor();
    res[b] = await p.evaluate(() => {
      document.querySelectorAll('details.feature, details.scenario, details.step-collapsible').forEach(d => d.setAttribute('open', ''));
      function steps(root, indent) {
        const out = [];
        root.querySelectorAll(':scope > .step').forEach(s => {
          const st = s.querySelector('.step-status');
          const stat = st ? st.className.replace('step-status', '').trim() : '?';
          const kw = (s.querySelector('.step-keyword') || {}).textContent || '';
          const tx = (s.querySelector('.step-text') || {}).textContent || '';
          const dur = (s.querySelector(':scope > summary > .step-duration, :scope > .step-duration') || {}).textContent ? 'dur' : '';
          const doc = s.querySelector(':scope > summary > .step-docstring, :scope > .step-docstring') ? ' doc' : '';
          const att = s.querySelector(':scope > summary a, :scope > a') ? ' att' : '';
          out.push(indent + (s.classList.contains('step-background') ? '[bg] ' : '') + stat + ' ' + kw + ' ' + tx + (dur ? ' (t)' : '') + doc + att);
          const sub = s.querySelector(':scope > .sub-steps');
          if (sub) out.push(...steps(sub, indent + '    '));
        });
        return out;
      }
      const lines = {};
      document.querySelectorAll('details.scenario').forEach(s => {
        const title = s.querySelector(':scope > summary').textContent.replace(/\s+/g, ' ').trim().slice(0, 64);
        const status = s.getAttribute('data-status') || '';
        const blocks = [];
        const panels = s.querySelectorAll('.param-detail-panel');
        (panels.length ? [...panels] : [s]).forEach((panel, i) => {
          const pre = panels.length ? 'row ' + i + ' ' : '';
          const list = panel.querySelector('details.scenario-steps');
          const bg = panel.querySelector('details.scenario-background');
          if (bg) blocks.push(pre + 'BG-SECTION' + (bg.open ? ' open' : '') , ...steps(bg, '  '));
          if (list) blocks.push(pre + 'STEPS', ...steps(list, '  '));
          const att = panel.querySelector(':scope > .scenario-attachments a');
          if (att) blocks.push(pre + 'scenario attachment');
          if (panel.querySelector('details.failure-result')) blocks.push(pre + 'failure: ' + panel.querySelector('details.failure-result pre').textContent.split('\n')[0].slice(0, 70));
        });
        lines[title] = [status, ...blocks];
      });
      return lines;
    });
    await ctx.browser().close();
  }
  const titles = [...new Set([...Object.keys(res.old), ...Object.keys(res.new)])].sort();
  for (const t of titles) {
    const o = (res.old[t] || ['(absent)']).join('\n      ');
    const n = (res.new[t] || ['(absent)']).join('\n      ');
    console.log('\n### ' + t + (o === n ? '  (same)' : ''));
    if (o === n) { console.log('      ' + n); continue; }
    console.log('  OLD ' + o);
    console.log('  NEW ' + n);
  }
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
