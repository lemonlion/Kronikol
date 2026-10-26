'use strict';
// Shared by the probes of plans/REPORT_UI_FIXES_PLAN.md section 3. Chromium is Playwright's bundled one (no installed
// Chrome or Edge is ever launched: CLAUDE.md, "Installed Chrome or Edge: never on a fresh profile").
//   PLAYWRIGHT_CORE=$USERPROFILE/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package node probe/<probe>.js <args>
//   (the playwright-core the .NET package ships, which matches the browsers the E2E suite installs; a newer one looks for
//   browser builds that are not installed)
const path = require('path');
const fs = require('fs');
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const { launchInstalled } = require(path.join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

async function open(file, width, height, theme) {
  const browser = await launchInstalled(chromium, { headless: true });
  const context = await browser.newContext({ viewport: { width, height }, colorScheme: theme === 'dark' ? 'dark' : 'light' });
  const page = await context.newPage();
  await page.goto('file:///' + path.resolve(file).replace(/\\/g, '/'));
  await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
  if (theme) await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), theme);
  return { browser, context, page };
}

// asks for each next frame from inside the frame callback, so no frame passes unwatched
async function frames(page, n) {
  await page.evaluate((n) => new Promise((done) => {
    let left = n;
    const tick = () => (--left <= 0 ? setTimeout(done, 0) : requestAnimationFrame(tick));
    requestAnimationFrame(tick);
  }), n);
}

// opens the scenario by its name and waits until its first sequence diagram is drawn whole and tagged
async function openScenario(page, name) {
  await page.evaluate((name) => {
    const sc = Array.from(document.querySelectorAll('details.scenario')).find((s) => s.querySelector('summary') && s.querySelector('summary').textContent.includes(name));
    if (!sc) throw new Error('no scenario named ' + name);
    document.querySelectorAll('[data-probe]').forEach((e) => e.removeAttribute('data-probe'));
    sc.setAttribute('data-probe', '1');
    sc.closest('details.feature').open = true; sc.open = true;
    sc.querySelectorAll('details').forEach((d) => { d.open = true; });
    sc.scrollIntoView();
    if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(sc);
  }, name);
  await page.waitForFunction(() => {
    const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
    if (!c || c._noteRendering || window._plantumlRendering) return false;
    const parts = Array.from(c.querySelectorAll(':scope > .puml-fragment'));
    const svgs = parts.length ? parts.map((p) => p.querySelector(':scope > svg')) : [c.querySelector(':scope > svg')];
    return svgs.every((s) => s && s.getAttribute('data-k-roles')) && c.querySelectorAll('.note-hover-rect').length > 0;
  }, null, { polling: 200, timeout: 120000 });
}

function write(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, typeof data === 'string' ? data : JSON.stringify(data, null, 2));
}

module.exports = { open, frames, openScenario, write };
