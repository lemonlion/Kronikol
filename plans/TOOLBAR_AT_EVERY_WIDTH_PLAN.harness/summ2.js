// Summarises sweep2-results-*.json: one line per page and run. Usage: node summ2.js [filter-substring]
const fs = require('fs');
const path = require('path');
const filter = process.argv[2] || '';
const files = fs.readdirSync(__dirname).filter(f => f.startsWith('sweep2-results-') && f.endsWith('.json') && f.includes(filter)).sort();
const bands = (rows, step, pred) => {
  const out = [];
  for (const r of rows.filter(pred)) { const b = out[out.length - 1]; if (b && r.w === b.to + step) b.to = r.w; else out.push({ from: r.w, to: r.w }); }
  return out.length ? out.map(b => b.from === b.to ? `${b.from}` : `${b.from}-${b.to}`).join(',') : '-';
};
for (const f of files) {
  const res = JSON.parse(fs.readFileSync(path.join(__dirname, f), 'utf8'));
  const label = f.replace('sweep2-results-', '').replace('.json', '');
  for (const [page, rowsRaw] of Object.entries(res.pages)) {
    const rows = [...rowsRaw].sort((a, b) => a.w - b.w);
    const maxOver = Math.max(0, ...rows.map(r => r.overflow));
    const sb = [...new Set(rows.map(r => r.inner - r.client))].join('/');
    const minSearch = rows.filter(r => r.searchWidth !== null).reduce((m, r) => (!m || r.searchWidth < m.searchWidth) ? r : m, null);
    console.log(`${label.padEnd(44)} ${page.padEnd(34)} sb=${sb.padEnd(5)} scroll=${bands(rows, res.step, r => r.overflow > 1)}${maxOver > 1 ? ` (+${maxOver})` : ''}  export=${bands(rows, res.step, r => r.exportWrapped > 0)}  top=${bands(rows, res.step, r => r.topBarWrapped > 0)}  scen=${bands(rows, res.step, r => r.scenarioWrapped > 0)}  cluster=${bands(rows, res.step, r => r.clusterEscapes > 1)}  clipped=${bands(rows, res.step, r => r.clippedControls > 0)}  contain=${bands(rows, res.step, r => r.containOverflow > 0)}  search>=${minSearch ? minSearch.searchWidth + '@' + minSearch.w : '-'}`);
  }
}
