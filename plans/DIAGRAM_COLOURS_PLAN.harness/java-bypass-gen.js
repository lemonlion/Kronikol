'use strict';
// Second P3 audit: can a line separator inside one captured line hide a line-start hazard from the escaper (which
// splits on \n only)? Writes note sources for the Java engine: a separator, then a comment, a terminator, an
// !include of a file this script writes, or @enduml.
const fs = require('fs'), path = require('path'), os = require('os');
const out = path.join(__dirname, 'java-bypass');
fs.mkdirSync(out, { recursive: true });
const inc = path.join(os.tmpdir(), 'kronikol-preproc-include-sentinel.txt').split(path.sep).join('/');
fs.writeFileSync(inc, 'SENTINEL LINE 1\nSENTINEL LINE 2\n');
const T = { NEL: 0x85, LS: 0x2028, PS: 0x2029, CR: 13, VT: 11, FF: 12 };
const PREFIX = ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', '',
    'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'];
for (const [n, c] of Object.entries(T)) {
    const sep = String.fromCharCode(c);
    const bodies = { quote: 'x' + sep + "'hidden", endnote: 'x' + sep + 'end note', include: 'x' + sep + '!include ' + inc, enduml: 'x' + sep + '@enduml' };
    for (const [k, b] of Object.entries(bodies))
        fs.writeFileSync(path.join(out, n + '-' + k + '.puml'),
            [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: GET: /x', 'note left', 'BEFORE', b, 'AFTER', 'end note', 'OrdersAPI --> Caller: 200', '@enduml'].join('\n'));
}
console.log(fs.readdirSync(out).length + ' sources in ' + out);
