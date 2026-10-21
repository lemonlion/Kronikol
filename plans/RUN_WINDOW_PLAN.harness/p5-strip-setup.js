// P5: what the page's Setup filter (stripSetup, collapsible-notes-script.js:2490-2564 at 9d89b793) does to the start-up
// scenario's diagram, which is all inside the Setup partition (results/p3f-setup.puml).
//   sed -n '2490,2564p' src/Kronikol/Reports/collapsible-notes-script.js > stripSetup.js
//   node p5-strip-setup.js stripSetup.js results/p3f-setup.puml
const fs = require('fs');
const stripSetup = new Function(fs.readFileSync(process.argv[2], 'utf8') + '\nreturn stripSetup;')();
const source = fs.readFileSync(process.argv[3], 'utf8').replace(/\r\n/g, '\n');
const out = stripSetup(source);
console.log(out === source ? '(returned unchanged)' : out.split('\n').filter(l => !/^(skinparam|sequenceDiagram|<\/?style|!pragma)/.test(l.trim()) && l.trim()).join('\n'));
