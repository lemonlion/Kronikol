// Does a PlantUML source survive a data-plantuml attribute set through innerHTML? (Q7's one browser fact)
const path = require('path');
const { chromium } = require(path.resolve(__dirname, '..', '..', 'tests', 'Kronikol.Tests.EndToEnd', 'bin', 'Debug', 'net10.0', '.playwright', 'package'));
(async () => {
    const browser = await chromium.launch();
    const page = await browser.newPage();
    await page.setContent('<!DOCTYPE html><div id="host"></div>');
    const r = await page.evaluate(() => {
        const src = '@startuml\n:GET /a?b=1&c="x" <y>;\n  :two  spaces\ttab;\n@enduml\n';
        const esc = s => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
        const host = document.getElementById('host');
        host.innerHTML = '<div id="a" data-plantuml="' + esc(src) + '"></div>' +
                         '<div id="b" data-plantuml="' + esc(src).replace(/\n/g, '&#10;') + '"></div>';
        return { literalNewlines: document.getElementById('a').getAttribute('data-plantuml') === src,
                 entityNewlines: document.getElementById('b').getAttribute('data-plantuml') === src };
    });
    console.log(JSON.stringify(r));
    await browser.close();
})();
