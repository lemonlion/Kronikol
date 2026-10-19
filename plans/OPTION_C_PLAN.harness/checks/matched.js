// The rules that set one property on the first element matching a selector (Chrome DevTools protocol).
// usage: node matched.js <page> <selector> <property>
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require('../../../tools/playwright/installed-browser.js');
const path = require('path');
(async () => {
  const [file, selector, prop] = process.argv.slice(2);
  const b = await launchInstalled(pw.chromium, { channel: 'chrome' });
  const ctx = await b.newContext({ viewport: { width: 1400, height: 900 } });
  const p = await ctx.newPage();
  await p.goto('file:///' + path.resolve(file).split(path.sep).join('/')); await p.waitForTimeout(2000);
  const s = await ctx.newCDPSession(p);
  await s.send('DOM.enable'); await s.send('CSS.enable');
  const { root } = await s.send('DOM.getDocument', { depth: -1 });
  const { nodeId } = await s.send('DOM.querySelector', { nodeId: root.nodeId, selector });
  if (!nodeId) { console.log('no node'); await b.close(); return; }
  const m = await s.send('CSS.getMatchedStylesForNode', { nodeId });
  const inline = (m.inlineStyle && m.inlineStyle.cssProperties || []).filter(x => x.name === prop);
  if (inline.length) console.log('inline:', inline.map(x => x.value).join(', '));
  for (const r of m.matchedCSSRules || []) {
    const hits = r.rule.style.cssProperties.filter(x => x.name === prop && x.value);
    if (hits.length) console.log(r.rule.selectorList.text.slice(0, 160), '=>', hits.map(h => h.value).join(', '), r.rule.media && r.rule.media.length ? '[' + r.rule.media.map(x => x.text).join(' ') + ']' : '');
  }
  for (const inh of m.inherited || []) for (const r of inh.matchedCSSRules || []) {
    const hits = r.rule.style.cssProperties.filter(x => x.name === prop && x.value);
    if (hits.length) console.log('inherited from', r.rule.selectorList.text.slice(0, 100), '=>', hits.map(h => h.value).join(', '));
  }
  await b.close();
})();
