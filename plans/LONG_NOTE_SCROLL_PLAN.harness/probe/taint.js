// Does an SVG holding a foreignObject survive the report's PNG path (serialize -> <img> -> canvas -> toDataURL)?
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
(async () => {
    for (const name of ['chromium', 'firefox', 'webkit']) {
        const browser = await launchInstalled(pw[name]);
        const page = await browser.newPage();
        await page.setContent('<svg id="s" xmlns="http://www.w3.org/2000/svg" width="200" height="100"><rect width="200" height="100" fill="#ffc"/>'
            + '<foreignObject x="10" y="10" width="180" height="80"><div xmlns="http://www.w3.org/1999/xhtml" style="height:100%;overflow:auto;font:12px monospace">'
            + 'line 1<br/>line 2<br/>line 3<br/>line 4<br/>line 5<br/>line 6<br/>line 7<br/>line 8</div></foreignObject></svg>');
        const r = await page.evaluate(() => new Promise(res => {
            const xml = new XMLSerializer().serializeToString(document.getElementById('s'));
            const img = new Image();
            img.onload = () => {
                const c = document.createElement('canvas'); c.width = 200; c.height = 100;
                const ctx = c.getContext('2d'); ctx.drawImage(img, 0, 0);
                try {
                    const url = c.toDataURL('image/png');
                    // did the HTML text paint? count dark pixels inside the box
                    const d = ctx.getImageData(10, 10, 180, 80).data; let dark = 0;
                    for (let i = 0; i < d.length; i += 4) if (d[i] < 100) dark++;
                    res({ toDataURL: 'ok', bytes: url.length, darkPixels: dark });
                } catch (e) { res({ toDataURL: e.name + ': ' + e.message }); }
            };
            img.onerror = () => res({ img: 'failed to load' });
            img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);
        }));
        console.log(name, JSON.stringify(r));
        await browser.close();
    }
})().catch(e => { console.error(e); process.exit(1); });
