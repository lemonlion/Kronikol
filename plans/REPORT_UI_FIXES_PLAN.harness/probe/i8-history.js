'use strict';
// I8: the History section's two trend cards. "today": how many rows the rings take, whether the bars scroll, which run
// each card opens on, whether the page scrolls sideways. "proto": the same after injecting the design of section 4 as
// CSS and markup only (no script: the labs page runs none): the rings in a scroller like the bars', both lists written in
// both orders, the order chosen by two native radios through :has(), newest first checked. Measured in both orders.
// usage: node probe/i8-history.js <history-N.html> <today|proto> <width> <outDir> [chromium|firefox|webkit]
const path = require('path');
const pw = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const { write } = require('./common');

const PROTO_CSS = `
.history-order { display: flex; justify-content: flex-end; margin: 0 0 8px; }
.history-order input { position: absolute; opacity: 0; width: 1px; height: 1px; pointer-events: none; }
.history-order label { cursor: pointer; }
.history-order input:checked + label { background: var(--kron-selected); color: var(--kron-selected-ink); border-color: var(--kron-selected-edge); position: relative; z-index: 1; }
.history-order input:focus-visible + label { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
.history-rings-scroll { overflow-x: auto; overflow-y: hidden; }
.history-rings { display: flex !important; gap: 10px; width: max-content; min-width: 100%; grid-template-columns: none !important; }
.history-ring { flex: 1 0 auto; display: flex; justify-content: center; }
.history-section:has(.k-order-newest:checked) .k-oldest-first { display: none; }
.history-section:has(.k-order-oldest:checked) .k-newest-first { display: none; }
.k-oldest-first.history-rings-scroll, .k-oldest-first.history-bars-scroll { direction: rtl; }
.k-oldest-first > .history-rings, .k-oldest-first > .history-bars { direction: ltr; }
.k-newest-first.history-rings-scroll, .k-newest-first.history-bars-scroll { direction: ltr; }
`;

function inject() {
  const section = document.querySelector('.history-section');
  const charts = section.querySelector('.history-charts');
  // the rings get a scroller of their own, as the bars have
  const rings = charts.querySelector('.history-rings');
  const rs = document.createElement('div'); rs.className = 'history-rings-scroll';
  rings.parentNode.insertBefore(rs, rings); rs.appendChild(rings);
  // each scroller written twice: the oldest-first one as today, and a newest-first copy with its runs in reverse
  for (const scroller of Array.from(charts.querySelectorAll('.history-rings-scroll, .history-bars-scroll'))) {
    const copy = scroller.cloneNode(true);
    const list = copy.firstElementChild;
    Array.from(list.children).reverse().forEach((c) => list.appendChild(c));
    list.setAttribute('aria-label', list.getAttribute('aria-label').replace('oldest run first', 'newest run first'));
    scroller.classList.add('k-oldest-first');
    copy.classList.add('k-newest-first');
    scroller.parentNode.insertBefore(copy, scroller);
  }
  charts.querySelectorAll('.history-chart-title').forEach((t) => { t.textContent = t.textContent.replace(/, oldest run first/i, ''); });
  const order = document.createElement('div');
  order.className = 'history-order';
  order.innerHTML = '<span class="tl-seg" role="radiogroup" aria-label="Order of the runs"><span class="tl-seg-label">Order</span>'
    + '<input type="radio" name="k-history-order" id="k-ho-new" class="k-order-newest" checked><label for="k-ho-new" class="tl-seg-btn" title="The newest run first, at the left">Newest first</label>'
    + '<input type="radio" name="k-history-order" id="k-ho-old" class="k-order-oldest"><label for="k-ho-old" class="tl-seg-btn" title="The oldest run first, at the left">Oldest first</label></span>';
  charts.parentNode.insertBefore(order, charts);
}

function measure() {
  const out = { pageScrollsSideways: document.documentElement.scrollWidth - document.documentElement.clientWidth };
  const shown = (el) => el.getClientRects().length > 0;
  for (const [name, sel, item] of [['rings', '.history-rings', '.history-ring'], ['bars', '.history-bars', '.history-col']]) {
    const list = Array.from(document.querySelectorAll(sel)).find(shown);
    const scroller = list.parentElement;
    const items = Array.from(list.querySelectorAll(item));
    const tops = new Set(items.map((i) => Math.round(i.getBoundingClientRect().top)));
    const view = scroller.getBoundingClientRect();
    const byRun = (i) => +items[i].title.split(':')[1];
    const newestIndex = items.reduce((m, _, i) => (byRun(i) > byRun(m) ? i : m), 0);
    const r = items[newestIndex].getBoundingClientRect();
    out[name] = {
      items: items.length, rows: tops.size, scrolls: scroller.scrollWidth - scroller.clientWidth,
      firstShownRun: byRun(0), newestInView: r.left >= view.left - 0.5 && r.right <= view.right + 0.5,
      label: list.getAttribute('aria-label')
    };
  }
  return out;
}

(async () => {
  const [file, mode, w, outDir, engine] = process.argv.slice(2);
  const browser = await launchInstalled(pw[engine || 'chromium'], { headless: true });
  const out = { file: path.basename(file), mode, width: +w, engine: engine || 'chromium' };
  try {
    const page = await (await browser.newContext({ viewport: { width: +w, height: 900 } })).newPage();
    await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'));
    if (mode === 'proto') {
      await page.addStyleTag({ content: PROTO_CSS });
      await page.evaluate(inject);
      await page.waitForTimeout(100);
      out.newestFirst = await page.evaluate(measure);
      await page.screenshot({ path: path.join(outDir, `i8-proto-newest-${w}-${out.engine}.png`), fullPage: true });
      await page.click('label[for="k-ho-old"]');
      await page.waitForTimeout(100);
      out.oldestFirst = await page.evaluate(measure);
      await page.screenshot({ path: path.join(outDir, `i8-proto-oldest-${w}-${out.engine}.png`), fullPage: true });
    } else {
      out.today = await page.evaluate(measure);
      await page.screenshot({ path: path.join(outDir, `i8-today-${w}-${out.engine}.png`), fullPage: true });
    }
    write(path.join(outDir, `i8-${mode}-${w}-${out.engine}.json`), out);
    console.log(JSON.stringify(out, null, 2));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
