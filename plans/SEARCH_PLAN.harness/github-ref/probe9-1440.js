// Probe 9: the issues results page at 1440 px (the width where the logged-out trigger shows its label and kbd),
// an issue label token, the quick search at 1440, and Enter submitting a typed query. 1 load + 1 navigation.
const { launch, newContext, save, installMeasure, sleep, OUT } = require('./lib');
const path = require('path');
const shot = (page, name, opts = {}) => page.screenshot({ path: path.join(OUT, name), ...opts });

(async () => {
  const R = {};
  const browser = await launch();
  const ctx = await newContext(browser, { width: 1440, height: 900 });
  const page = await ctx.newPage();
  await page.goto('https://github.com/search?q=playwright+label%3Abug&type=issues', { waitUntil: 'load', timeout: 60000 });
  await sleep(3500);
  await installMeasure(page);
  R.trigger = await page.evaluate(() => {
    const b = document.querySelector('[class*="searchSlot"] button');
    return { slot: window.__measure(b.parentElement), button: window.__measure(b), label: window.__measure(b.querySelector('[class^="label__"], [class*=" label__"]')), kbd: window.__measure(b.querySelector('kbd')), icon: window.__measure(b.querySelector('svg')) };
  });
  await shot(page, 'header-loggedout-1440-light.png', { clip: { x: 0, y: 0, width: 1440, height: 80 } });
  // Issue label tokens.
  R.issueLabels = await page.evaluate(() => Array.from(document.querySelectorAll('[data-testid="results-list"] a[href*="label"], [data-testid="results-list"] [class*="IssueLabel"], [data-testid="results-list"] [data-component="Label"], [data-testid="results-list"] [class*="label__"]')).slice(0, 4).map((e) => { const r = window.__measure(e); r.style = e.getAttribute('style'); r.href = e.getAttribute('href'); return r; }));
  R.layout = await page.evaluate(() => ({ pane: window.__measure('[data-component="PageLayout.Pane"]'), main: window.__measure('[class*="mainColumn"]'), cols: Array.from(document.querySelector('[class*="twoColumnOuter"]').children).map((c) => window.__measure(c)) }));
  await shot(page, 'results-issues-label-1440-light.png');
  // Quick search at 1440, opened with a click.
  await page.click('[class*="searchSlot"] button');
  await sleep(1200);
  R.dialog = await page.evaluate(() => { const d = Array.from(document.querySelectorAll('[role="dialog"][data-component="Dialog"]')).find((e) => e.getBoundingClientRect().width > 0); return d ? { box: window.__measure(d), vars: d.getAttribute('style'), input: window.__measure(d.querySelector('.TextInput-wrapper')), value: d.querySelector('input').value } : null; });
  await shot(page, 'qb-empty-1440-light.png', { clip: { x: 700, y: 0, width: 740, height: 340 } });
  // Type and press Enter.
  await page.keyboard.type('playwright is:open', { delay: 50 });
  await sleep(800);
  await shot(page, 'qb-typed-enter-before-1440-light.png', { clip: { x: 700, y: 0, width: 740, height: 200 } });
  await Promise.all([page.waitForURL(/search/, { timeout: 30000 }).catch(() => {}), page.keyboard.press('Enter')]);
  await sleep(4000);
  R.afterEnter = { url: page.url(), title: await page.title() };
  await shot(page, 'results-after-enter-1440-light.png');
  save('probe9-1440.json', R);
  console.log(JSON.stringify({ trigger: { slot: [R.trigger.slot.x, R.trigger.slot.w], button: [R.trigger.button.x, R.trigger.button.y, R.trigger.button.w, R.trigger.button.h, R.trigger.button.radius, R.trigger.button.border, R.trigger.button.bg, R.trigger.button.color, R.trigger.button.padding], label: R.trigger.label && [R.trigger.label.display, R.trigger.label.text, R.trigger.label.fontSize, R.trigger.label.fontWeight, R.trigger.label.color], kbd: R.trigger.kbd && [R.trigger.kbd.display, R.trigger.kbd.w, R.trigger.kbd.h, R.trigger.kbd.fontSize, R.trigger.kbd.radius, R.trigger.kbd.border, R.trigger.kbd.padding, R.trigger.kbd.boxShadow] }, labels: R.issueLabels.map((l) => [l.text, l.fontSize, l.fontWeight, l.color, l.bg, l.border, l.radius, l.padding, l.h, l.style]), layout: { pane: R.layout.pane.w, main: R.layout.main.w, cols: R.layout.cols.map((c) => [c.x, c.w]) }, dialog: R.dialog && [R.dialog.box.x, R.dialog.box.y, R.dialog.box.w, R.dialog.box.h, R.dialog.vars, R.dialog.value], afterEnter: R.afterEnter }, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
