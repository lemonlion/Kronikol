// What the engine offers to keep a capped note as wide as the whole note: a transparent widest line, or a style's
// MinimumWidth. Renders small sources with the report's own engine and measures the note's drawn box.
// Usage: node enginewidth.js <report.html>
const pw = require(process.env.KRONIKOL_PW || require('path').join(__dirname, 'pw', 'package'));
const { launchInstalled } = require(require('path').join(__dirname, '..', '..', '..', 'tools', 'playwright', 'installed-browser.js'));
const path = require('path');

const head = '@startuml\nskinparam wrapWidth 800\nparticipant A\nparticipant B\nA -> B : GET /x\n';
const narrow = ['{', '  "a": 1,', '  "b": 2', '}'];
const long = '  "description": "' + 'a fairly long value that the narrow lines above never reach '.repeat(2) + '",';
const cases = {
    narrow: head + 'note right\n' + narrow.join('\n') + '\nend note\n@enduml',
    withLongLine: head + 'note right\n' + narrow.join('\n') + '\n' + long + '\nend note\n@enduml',
    transparentLongLine: head + 'note right\n' + narrow.join('\n') + '\n<color:#FFFFFF00>' + long + '\nend note\n@enduml',
    minimumWidthStyle: head.replace('participant A', '<style>\n.capw { MinimumWidth 600 }\n</style>\nparticipant A') + 'note<<capw>> right\n' + narrow.join('\n') + '\nend note\n@enduml',
    minimumWidthAndMax: head.replace('participant A', '<style>\n.capw { MinimumWidth 600\n MaximumWidth 600 }\n</style>\nparticipant A') + 'note<<capw>> right\n' + narrow.join('\n') + '\n' + long + '\nend note\n@enduml',
};

(async () => {
    const browser = await launchInstalled(pw.chromium);
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
    await page.goto('file:///' + path.resolve(process.argv[2]).replace(/\\/g, '/'));
    await page.waitForFunction(() => window.plantuml && window.plantuml.render && window._findNoteGroups, null, { polling: 200, timeout: 120000 });
    for (const [name, src] of Object.entries(cases)) {
        const r = await page.evaluate(async ({ name, src }) => {
            const host = document.createElement('div');
            host.id = 'w-' + name;
            document.body.prepend(host);
            window.plantuml.render(src.split('\n'), host.id);
            for (let i = 0; i < 300 && !host.querySelector('svg'); i++) await new Promise(r => setTimeout(r, 50));
            const svg = host.querySelector('svg');
            if (!svg) return { error: (host.textContent || '').slice(0, 200) };
            const groups = window._findNoteGroups(svg);
            if (!groups.length) return { error: 'no note found', text: svg.textContent.slice(0, 200) };
            const g = groups[0];
            const body = g.paths.reduce((a, p) => { const b = p.getBBox(); return b.width * b.height > a.width * a.height ? b : a; }, { width: 0, height: 0 });
            const fills = [...new Set(g.texts.map(t => t.getAttribute('fill')))];
            const rows = new Set(g.texts.map(t => t.getAttribute('y'))).size;
            host.remove();
            return { width: Math.round(body.width), height: Math.round(body.height), rows, fills };
        }, { name, src });
        console.log(name.padEnd(22), JSON.stringify(r));
    }
    await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
