// Does an IntersectionObserver see an SVG <path>, the outer <svg> and an HTML div in a foreignObject come into view? A
// bare page with an unscaled svg, in all three engines. WebKit reads the path with no height (its top right); in the
// report, under a scaled svg, it read every note at the svg's corner and never saw one come near (T43).
// Usage: node io-svg.js
const path = require('path');
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));

const html = `<!doctype html><html><body style="margin:0">
<div style="height:3000px">spacer</div>
<svg id="s" width="400" height="400" style="display:block">
  <g><path id="p" d="M10 10 L390 10 L390 390 L10 390 Z" fill="#eee"/></g>
  <foreignObject x="20" y="20" width="200" height="100"><div id="d" xmlns="http://www.w3.org/1999/xhtml" style="height:100px">box</div></foreignObject>
</svg>
<div style="height:3000px">spacer</div></body></html>`;

(async () => {
    for (const name of ['chromium', 'firefox', 'webkit']) {
        const browser = await launchInstalled(pw[name], { headless: true });
        const page = await (await browser.newContext({ viewport: { width: 800, height: 600 } })).newPage();
        await page.setContent(html);
        await page.evaluate(() => {
            window.__seen = [];
            const io = new IntersectionObserver(es => es.forEach(e => window.__seen.push(e.target.id + ':' + (e.isIntersecting ? 'in' : 'out')
                + '@' + Math.round(e.boundingClientRect.top) + '/' + Math.round(e.boundingClientRect.height))), { rootMargin: '100% 0px' });
            ['p', 's', 'd'].forEach(id => io.observe(document.getElementById(id)));
        });
        await page.waitForTimeout(300);
        await page.evaluate(() => document.getElementById('s').scrollIntoView({ block: 'center' }));
        await page.waitForTimeout(600);
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.waitForTimeout(600);
        const seen = await page.evaluate(() => window.__seen);
        const rect = await page.evaluate(() => { const r = document.getElementById('p').getBoundingClientRect(); return [Math.round(r.top), Math.round(r.height)]; });
        console.log(name, JSON.stringify(seen), 'path rect at top', JSON.stringify(rect));
        await browser.close();
    }
})().catch(e => { console.log('ERROR', String(e).slice(0, 300)); process.exit(1); });
