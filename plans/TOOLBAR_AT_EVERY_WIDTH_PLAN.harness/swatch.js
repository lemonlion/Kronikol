const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const rows = [
  ['passed', '#f0fff0', '#e3fae9', '#d5f5e3'],
  ['failed', '#fff0f0', '#fde5e4', '#fadbd8'],
  ['skipped A (derived)', '#fff8e1', '#faf0cf', '#f4e9be'],
  ['skipped B', '#fff8e1', '#fff1c9', '#ffe9b0'],
  ['skipped C', '#fff8e1', '#fef1cc', '#fce9b8'],
  ['skipped now', '#fff8e1', '#fff8e1', '#fef9e7'],
  ['bypassed', '#f0f0ff', '#e8e8fd', '#dfe0fb'],
  ['bypassed now', '#f0f0ff', '#f0f0ff', '#f0f0ff'],
];
const html = `<html><body style="font-family:sans-serif;font-size:14px;padding:12px">
<table style="border-collapse:collapse">${rows.map(([n, r, h, a]) => `<tr>
<td style="padding:6px 10px;width:150px">${n}</td>
${[['rest', r], ['hover', h], ['selected', a]].map(([k, c]) => `<td style="background:${c};border:1px solid #ddd;padding:6px 14px;width:170px;${k==='selected'?'font-weight:500':''}">${k} ${c}</td>`).join('')}</tr>`).join('')}
</table></body></html>`;
(async () => {
  const b = await pw.chromium.launch(); const p = await b.newPage({ viewport: { width: 760, height: 330 }, deviceScaleFactor: 2 });
  await p.setContent(html); await p.screenshot({ path: 'swatch.png' }); await b.close();
})();
