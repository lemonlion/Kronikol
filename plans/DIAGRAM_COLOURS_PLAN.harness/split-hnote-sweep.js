// Third P3 audit: sweeps the fragment height from 60 to 3000 px over twenty calls, each followed by an assertion note
// whose message holds `->`, and lists the fragments that end inside a note. Usage: node split-hnote-sweep.js
const path = require('path'), url = require('url');
const pw = require(path.join(path.resolve(__dirname, '..', '..'), 'tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package'));
const PREFIX = ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', '', 'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'];
function seq(n) {
    const body = [];
    for (let i = 0; i < n; i++) {
        body.push(`Caller -[#438DD5]> OrdersAPI: GET: /orders/${i}`, 'note left', '{', '  "id": 1', '}', 'end note', 'OrdersAPI -[#438DD5]-> Caller: 200');
        body.push('hnote across <<assertionNote>> #F8D7DA', `✗ Expected order ${i} -> shipped`, 'line two', 'line three', 'end note');
    }
    return [...PREFIX, ...body, '@enduml'].join('\n');
}
(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await (await browser.newContext()).newPage();
    await page.goto(url.pathToFileURL(path.resolve(__dirname, 'split-page.html')).href);
    await page.waitForFunction(() => typeof window._splitDiagramSource === 'function');
    const bad = [];
    for (let h = 60; h <= 3000; h += 20) {
        const frags = await page.evaluate(([s, h]) => window._splitDiagramSource(s, h), [seq(20), h]);
        frags.forEach((f, k) => {
            const lines = f.split('\n').map(l => l.trim());
            let open = 0;
            for (const t of lines) { if (/^hnote across/.test(t) || t === 'note left') open++; if (t === 'end note') open--; }
            if (open !== 0) bad.push(`h=${h} fragment ${k + 1}/${frags.length}: open=${open}, ends ${JSON.stringify(lines.slice(-3))}`);
        });
    }
    console.log(bad.length ? bad.slice(0, 6).join('\n') + `\n(${bad.length} unbalanced fragments in total)` : 'no unbalanced fragment at any height');
    await browser.close();
})();
