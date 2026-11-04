// With SeparateBackgroundSteps, does a section holding a failed step start open? node paint-separate-section.js <reports> <sid> <out.png>
// And, with the accent the stylesheet's comment describes forced on, what the inline list would look like: node ... <reports> <sid> <out.png> accent
const path = require('path');
const { chromium } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');
const { launchInstalled } = require(path.join(__dirname, '../../../tools/playwright/installed-browser.js'));
(async () => {
    const [reports, sid, out, mode] = process.argv.slice(2);
    const browser = await launchInstalled(chromium);
    const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
    await page.goto('file:///' + path.resolve(reports, 'TestRunReport.html').replace(/\\/g, '/') + '#sid-' + sid);
    await page.waitForFunction(() => document.querySelector('details.feature') !== null, null, { polling: 200, timeout: 60000 });
    if (mode === 'accent')
        await page.addStyleTag({ content: '.scenario-steps > .step-background, .scenario-steps > .step-collapsible.step-background { border-left: 3px solid var(--kron-hairline); padding-left: 0.5em; margin-left: 25px; }' });
    const seen = await page.evaluate(id => {
        const anchor = document.getElementById('sid-' + id) || document.querySelector('[data-stable-id="' + id + '"]');
        const scenario = anchor.closest('details.scenario') || anchor;
        const background = scenario.querySelector('details.scenario-background');
        const report = background && {
            startsOpen: background.hasAttribute('open'),
            holdsFailedStep: !!background.querySelector('.step-status.failed'),
            summary: background.querySelector(':scope > summary').textContent,
            summaryTitle: background.querySelector(':scope > summary').getAttribute('title'),
        };
        const steps = scenario.querySelector('details.scenario-steps');
        for (let e = scenario; e; e = e.parentElement) if (e.tagName === 'DETAILS') e.open = true;
        scenario.querySelectorAll('details.failure-result').forEach(d => { d.open = false; });
        return { background: report, stepsListPresent: !!steps };
    }, sid);
    console.log(JSON.stringify(seen, null, 1));
    await page.waitForTimeout(300);
    const handle = await page.evaluateHandle(id => {
        const anchor = document.getElementById('sid-' + id) || document.querySelector('[data-stable-id="' + id + '"]');
        const scenario = anchor.closest('details.scenario') || anchor;
        return scenario.querySelector('details.scenario-background') || scenario.querySelector('details.scenario-steps');
    }, sid);
    await handle.asElement().screenshot({ path: out });
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
