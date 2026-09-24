// The complete colour census: every control the base sheets tint blue, read on the violet pages and
// on the blue report as the control, at HEAD (out) and on the prototype (out-patched).
// Usage: node cascade2.js [dirs, comma-separated] [pages, comma-separated]
// Synthetic elements carry the classes (a class paints the same on made markup as on emitted markup);
// `present` counts the real page's elements with those classes, so the table also says which
// controls a default Specifications.html actually shows.
const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const path = require('path');
const fs = require('fs');
const DIRS = (process.argv[2] || 'out,out-patched').split(',');
const PAGES = (process.argv[3] || 'Specifications.html,Specifications_iflow.html,TestRunReport.html').split(',');

// [label, mini-selector (tag.class>tag.class), state]; state: '' rest, 'hover' hover the element,
// 'hover:parent' hover its parent and read the element, 'hover:read-parent' hover the element and read
// its parent (a table row paints on the tr, hit-testing lands on the td), 'color' the text colour at rest.
const CONTROLS = [
  ['happy-path idle, hover',        'button.happy-path-toggle', 'hover'],
  ['happy-path active',             'button.happy-path-toggle.happy-path-active', ''],
  ['happy-path active, hover',      'button.happy-path-toggle.happy-path-active', 'hover'],
  ['dependency idle, hover',        'button.dependency-toggle', 'hover'],
  ['dependency active, hover',      'button.dependency-toggle.dependency-active', 'hover'],
  ['dep-mode, hover',               'button.dep-mode-toggle', 'hover'],
  ['status idle, hover',            'button.status-toggle', 'hover'],
  ['status active, hover',          'button.status-toggle.status-active', 'hover'],
  ['category idle, hover',          'button.category-toggle', 'hover'],
  ['category active, hover',        'button.category-toggle.category-active', 'hover'],
  ['cat-mode, hover',               'button.cat-mode-toggle', 'hover'],
  ['percentile idle, hover',        'button.percentile-btn', 'hover'],
  ['percentile active',             'button.percentile-btn.percentile-active', ''],
  ['percentile active, hover',      'button.percentile-btn.percentile-active', 'hover'],
  ['collapse-expand-all, hover',    'button.collapse-expand-all', 'hover'],
  ['timeline idle, hover',          'button.timeline-toggle', 'hover'],
  ['timeline active',               'button.timeline-toggle.timeline-toggle-active', ''],
  ['timeline active, hover',        'button.timeline-toggle.timeline-toggle-active', 'hover'],
  ['export, hover',                 'button.export-btn', 'hover'],
  ['details radio idle, hover',     'button.details-radio-btn', 'hover'],
  ['details radio active',          'button.details-radio-btn.details-active', ''],
  ['details radio active, hover',   'button.details-radio-btn.details-active', 'hover'],
  ['diagram tab idle',              'button.diagram-toggle-btn', ''],
  ['diagram tab idle, hover',       'button.diagram-toggle-btn', 'hover'],
  ['diagram tab active',            'button.diagram-toggle-btn.diagram-toggle-active', ''],
  ['diagram tab active, hover',     'button.diagram-toggle-btn.diagram-toggle-active', 'hover'],
  ['diagram settings (phone), hover', 'button.scenario-diagram-controls-toggle', 'hover'],
  ['flow toggle idle, hover',       'button.iflow-toggle-btn', 'hover'],
  ['flow toggle active',            'button.iflow-toggle-btn.iflow-toggle-active', ''],
  ['flow toggle active, hover',     'button.iflow-toggle-btn.iflow-toggle-active', 'hover'],
  ['related list item, hover',      'ul.iflow-rel-list>li', 'hover'],
  ['related summary row, hover',    'table.iflow-rel-summary-table>tbody>tr>td', 'hover:parent'],
  ['scenario link, hover',          'a.scenario-link', 'hover'],
  ['copy scenario name, hover',     'button.copy-scenario-name', 'hover'],
  ['failure cluster link (colour)', 'a.failure-cluster-scenario-link', 'color'],
  ['param table row, hover',        'table.param-test-table>tbody>tr>td', 'hover:read-parent'],
  ['param table row active',        'table.param-test-table>tbody>tr.row-active>td', 'read-parent'],
];

const NAMES = {
  'rgb(139, 92, 246)': 'violet', 'rgb(124, 58, 237)': 'violet-dk', 'rgb(237, 233, 254)': 'violet-tint', 'rgb(167, 139, 250)': 'violet-bd',
  'rgb(245, 243, 255)': 'violet-pale', 'rgb(221, 214, 254)': 'violet-bg', 'rgb(196, 181, 253)': 'violet-lbl',
  'rgb(66, 133, 244)': 'BLUE', 'rgb(51, 103, 214)': 'BLUE-dk', 'rgb(50, 110, 220)': 'BLUE-dk2', 'rgb(230, 240, 255)': 'BLUE-tint',
  'rgb(232, 240, 254)': 'BLUE-tint2', 'rgb(240, 244, 255)': 'BLUE-tint3', 'rgb(210, 227, 252)': 'BLUE-tint4', 'rgb(100, 150, 255)': 'BLUE-bd',
  'rgb(255, 255, 255)': 'white', 'rgb(245, 245, 245)': 'grey245', 'rgb(240, 240, 240)': 'ua-grey', 'rgba(0, 0, 0, 0)': 'none',
  'rgb(180, 180, 180)': 'grey180', 'rgb(204, 204, 204)': 'grey204', 'rgb(200, 200, 200)': 'grey200', 'rgb(224, 224, 224)': 'grey224',
  'rgb(118, 118, 118)': 'ua-bd', 'rgb(0, 0, 0)': 'black', 'rgb(0, 0, 238)': 'ua-link',
};
const nm = c => NAMES[c] || c;

const BUILD = `(specs) => {
  const out = [];
  specs.forEach(([label, mini, state], i) => {
    const parts = mini.split('>');
    const real = document.querySelectorAll(parts.map(p => { const [tag, ...cls] = p.split('.'); return tag + cls.map(c => '.' + c).join(''); }).join('>')).length;
    let parent = document.body, el = null;
    parts.forEach(p => { const [tag, ...cls] = p.split('.'); el = document.createElement(tag); el.className = cls.join(' '); if (tag === 'a') el.href = '#'; parent.appendChild(el); parent = el; });
    el.textContent = 'probe ' + i;
    el.id = 'c' + i;
    let hoverEl = state === 'hover:parent' ? el.parentElement : el;
    hoverEl.id = hoverEl.id || ('h' + i);
    const readEl = state.endsWith('read-parent') ? el.parentElement : el;
    readEl.id = readEl.id || ('r' + i);
    // A control the page shows only in some state (display: none at rest) still paints its class
    // colours; give it a box so it can be hovered.
    [el, hoverEl].forEach(x => { if (getComputedStyle(x).display === 'none') x.style.display = 'inline-block'; if (getComputedStyle(x).position === 'fixed') x.style.position = 'static'; });
    out.push({ i, label, state, real, readId: readEl.id, hoverId: hoverEl.id });
  });
  return out;
}`;

const READ = `(id) => { const el = document.getElementById(id); const cs = getComputedStyle(el); return [cs.backgroundColor, cs.borderTopColor, cs.color]; }`;

(async () => {
  const b = await pw.chromium.launch();
  const table = {}; // label -> { 'dir/page': cell }
  const cols = [];
  for (const dir of DIRS) for (const name of PAGES) {
    const file = path.join(__dirname, dir, name);
    if (!fs.existsSync(file)) { console.log('missing', file); continue; }
    const col = dir + '/' + name.replace('.html', '');
    cols.push(col);
    const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
    await p.route('**/*', r => r.request().url().startsWith('file:') ? r.continue() : r.abort());
    await p.goto('file:///' + file.replace(/\\/g, '/'), { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('details.feature');
    const specs = await p.evaluate(eval('(' + BUILD + ')'), CONTROLS);
    for (const s of specs) {
      await p.mouse.move(0, 0);
      if (s.state.startsWith('hover')) { await p.hover('#' + s.hoverId); await p.waitForTimeout(25); }
      const [bg, bd, fg] = await p.evaluate(eval('(' + READ + ')'), s.readId);
      const cell = s.state === 'color' ? nm(fg) : nm(bg) + '/' + nm(bd);
      (table[s.label] ||= {})[col] = { cell, real: s.real };
    }
    await p.close();
  }
  await b.close();
  console.log('| Control | ' + cols.join(' | ') + ' |');
  console.log('|---|' + cols.map(() => '---').join('|') + '|');
  for (const [label, row] of Object.entries(table)) {
    const present = Object.values(row).map(v => v.real).reduce((a, x) => Math.max(a, x), 0);
    console.log('| ' + label + (present ? '' : ' (not on these pages)') + ' | ' + cols.map(c => row[c] ? row[c].cell : '').join(' | ') + ' |');
  }
  fs.writeFileSync(path.join(__dirname, 'cascade2-results.json'), JSON.stringify({ cols, table }, null, 1));
})().catch(e => { console.error('ERR', e.stack || e.message); process.exit(1); });
