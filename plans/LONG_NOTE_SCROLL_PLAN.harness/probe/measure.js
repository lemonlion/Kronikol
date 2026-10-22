// How long the cap's measuring takes in each engine: 7,800 canvas measureText calls on JSON-like lines in the note's
// face, the work clearWidthRow does for T21's 16 capped notes. A bare page. Usage: node measure.js
const path = require('path');
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

(async () => {
    for (const name of ['chromium', 'firefox', 'webkit']) {
        const browser = await launchInstalled(pw[name], { headless: true });
        const page = await (await browser.newContext()).newPage();
        await page.setContent('<!doctype html><html><body></body></html>');
        const ms = await page.evaluate(() => {
            const lines = [];
            for (let i = 0; i < 7800; i++) lines.push('  "r1500_' + String(i).padStart(4, '0') + '": "value ' + i + '",');
            const ctx = document.createElement('canvas').getContext('2d');
            const runs = [];
            for (let r = 0; r < 3; r++) {
                const t0 = performance.now();
                let w = 0;
                for (const l of lines) { ctx.font = '13px system-ui, sans-serif'; w += ctx.measureText(l).width; }
                runs.push(Math.round(performance.now() - t0));
            }
            return runs;
        });
        console.log(name, JSON.stringify(ms));
        await browser.close();
    }
})().catch(e => { console.log('ERROR', String(e).slice(0, 300)); process.exit(1); });
