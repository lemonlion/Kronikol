// Third P3 audit: does the splitter keep alt/else blocks whole across fragments? Counts, over heights 200 to 1200 px,
// the fragments with an `end` that closes no block, an `else` outside its `alt`, or a block left open.
// Usage: node split-else-probe.js [page, default split-page.html (split-page.fsx writes it)]
const path = require('path'), url = require('url');
const pw = require(path.join(path.resolve(__dirname, '..', '..'), 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await (await browser.newContext()).newPage();
    await page.goto(url.pathToFileURL(path.resolve(__dirname, process.argv[2] || 'split-page.html')).href);
    await page.waitForFunction(() => typeof window._splitDiagramSource === 'function');
    const r = await page.evaluate(() => {
        var src = ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', '', 'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'];
        for (var i = 0; i < 40; i++) src.push('alt stock ' + i, 'Caller -[#438DD5]> OrdersAPI: GET: /stock/' + i, 'OrdersAPI -[#438DD5]-> Caller: 200', 'else no stock', 'Caller -[#438DD5]> OrdersAPI: POST: /backorder/' + i, 'OrdersAPI -[#438DD5]-> Caller: 202', 'end');
        src.push('@enduml');
        var bad = 0, total = 0, sample = null;
        for (var h = 200; h <= 1200; h += 50) {
            window._splitDiagramSource(src.join('\n'), h).forEach(function (f, k) {
                total++;
                var depth = 0, why = null;
                f.split('\n').forEach(function (l) { var t = l.trim(); if (/^alt\b/.test(t)) depth++; else if (t === 'end') { depth--; if (depth < 0) why = why || 'end with no block'; } else if (/^else\b/.test(t) && depth === 0) why = why || 'else outside alt'; });
                if (!why && depth) why = depth + ' open';
                if (why) { bad++; if (!sample) sample = h + 'px frag ' + (k + 1) + ': ' + why + ' | tail ' + JSON.stringify(f.split('\n').slice(-4)); }
            });
        }
        return bad + ' of ' + total + ' fragments bad; first: ' + sample;
    });
    console.log(r);
    await browser.close();
})();
