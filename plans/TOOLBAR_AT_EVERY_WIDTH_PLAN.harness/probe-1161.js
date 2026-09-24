const pw = require(require('path').resolve(__dirname, '../../tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const TS = '*{line-height:1.5 !important;letter-spacing:0.12em !important;word-spacing:0.16em !important}p{margin-bottom:2em !important}';
(async () => {
  const b = await pw.chromium.launch({ ignoreDefaultArgs: ['--hide-scrollbars'] });
  for (const w of [1150, 1160, 1161, 1165, 1170, 1175, 1180]) {
    const p = await b.newPage({ viewport: { width: w, height: 900 } });
    await p.goto('file:///' + process.argv[2]);
    await p.evaluate(() => document.querySelectorAll('details').forEach(d => d.open = true));
    await p.addStyleTag({ content: TS });
    await p.waitForTimeout(100);
    console.log(w, await p.evaluate(() => {
      const de = document.documentElement, box = document.querySelector('.filtering-box'), cl = document.querySelector('.filtering-box-export');
      const btns = [...cl.querySelectorAll('button')].map(b => `${b.textContent.trim()}:${Math.round(b.getBoundingClientRect().width)}`).join(' ');
      return `page +${de.scrollWidth - de.clientWidth} | box ${Math.round(box.getBoundingClientRect().width)} cluster past box ${Math.round(cl.getBoundingClientRect().right - box.getBoundingClientRect().right)} | ${btns}`;
    }));
    await p.close();
  }
  await b.close();
})();
