// What a Background step paints, failed and passed: node paint-failed-background.js <reports-dir> <sid> <out.png> [label]
// Bundled Chromium only, through launchInstalled (CLAUDE.md, "Installed Chrome or Edge: never on a fresh profile"): no channel
// and no executablePath. Prints each listed step's classes, its colours and whether its list starts open.
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.join(__dirname, '../../../tools/playwright/installed-browser.js'));

(async () => {
    const [reports, sid, out, label] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    const url = 'file:///' + path.resolve(reports, 'TestRunReport.html').replace(/\\/g, '/') + '#sid-' + sid;
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    const summary = await page.evaluate(id => {
        const anchor = document.getElementById('sid-' + id) || document.querySelector('[data-stable-id="' + id + '"]');
        const scenario = anchor && (anchor.closest('details.scenario') || anchor);
        if (!scenario) return { error: 'no scenario for ' + id };
        for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        scenario.scrollIntoView();
        const lists = [...scenario.querySelectorAll('details.scenario-background, details.scenario-steps')];
        return {
            lists: lists.map(l => ({ cls: l.className, startsOpen: l.hasAttribute('open') })),
            hasFailureBlock: !!scenario.querySelector('.failure-result, details.failure-result'),
            steps: [...scenario.querySelectorAll('details.scenario-background > .step, details.scenario-steps > .step, details.scenario-background > details.step, details.scenario-steps > details.step')].map(s => {
                const own = s.tagName === 'DETAILS' ? s.querySelector(':scope > summary') : s;
                const text = own.querySelector('.step-text');
                const keyword = own.querySelector('.step-keyword');
                const icon = own.querySelector('.step-status');
                const style = e => (e ? getComputedStyle(e).color : null);
                return {
                    cls: s.className,
                    keyword: keyword && keyword.textContent,
                    text: text && text.textContent.trim().slice(0, 60),
                    textColour: style(text),
                    keywordColour: style(keyword),
                    iconClass: icon && icon.className,
                    iconColour: style(icon),
                    borderLeft: getComputedStyle(s).borderLeftWidth + ' ' + getComputedStyle(s).borderLeftColor,
                };
            }),
        };
    }, sid);
    console.log(JSON.stringify({ label, sid, ...summary }, null, 1));
    // The step list alone: the failure block above it can be taller than the viewport.
    await page.evaluate(id => {
        const anchor = document.getElementById('sid-' + id) || document.querySelector('[data-stable-id="' + id + '"]');
        (anchor.closest('details.scenario') || anchor).querySelectorAll('details.failure-result').forEach(d => { d.open = false; });
    }, sid);
    await page.waitForTimeout(300);
    const list = page.locator('details.scenario[open] details.scenario-steps[open]').first();
    const handle = await page.evaluateHandle(id => {
        const anchor = document.getElementById('sid-' + id) || document.querySelector('[data-stable-id="' + id + '"]');
        return (anchor.closest('details.scenario') || anchor).querySelector('details.scenario-steps');
    }, sid);
    await handle.asElement().screenshot({ path: out });
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
