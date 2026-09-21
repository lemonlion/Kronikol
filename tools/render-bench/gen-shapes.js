'use strict';
// W0 corpus: shape-targeted sequence diagram generators (plans/TEOZ_PERF_PLAN.md Workstream 0).
// Run: node gen-shapes.js   -> writes real/shape-*.puml and real/many-small/small-NN.puml
// No pragma in files (bench-real.js PRAGMA=1 injects it); sizes chosen so warm renders land
// in the hundreds-of-ms range on the patched master build.
const fs = require('fs'), path = require('path');
const R = path.join(__dirname, 'real');

function w(name, lines) {
  fs.writeFileSync(path.join(R, name), lines.join('\n') + '\n');
  console.log(name, lines.length, 'lines');
}

// 1. Nested groups: alt/opt/loop 5 deep, repeated blocks.
{
  const L = ['@startuml', 'participant "Test" as T', 'participant "Api" as A', 'database "Db" as D'];
  for (let b = 0; b < 12; b++) {
    L.push('alt case ' + b);
    L.push('T -> A: outer ' + b);
    L.push('opt config ' + b);
    L.push('A -> D: q1-' + b);
    L.push('loop retry');
    L.push('D --> A: rows ' + b);
    L.push('alt found');
    L.push('A -> D: q2-' + b);
    L.push('opt cached');
    L.push('D --> A: hit ' + b);
    L.push('A --> T: 200 (' + b + ')');
    L.push('end');
    L.push('else missing');
    L.push('A --> T: 404 (' + b + ')');
    L.push('end');
    L.push('end');
    L.push('end');
    L.push('else skip ' + b);
    L.push('T -> A: fallback ' + b);
    L.push('A --> T: 204');
    L.push('end');
  }
  L.push('@enduml');
  w('shape-groups.puml', L);
}

// 2. Parallel (&) messages: Teoz-only feature; Puma column will error, that is expected.
{
  const L = ['@startuml', 'participant "A" as A', 'participant "B" as B', 'participant "C" as C', 'participant "D" as D'];
  for (let i = 0; i < 80; i++) {
    L.push('A -> B: left ' + i);
    L.push('& C -> D: right ' + i);
    L.push('B --> A: ack ' + i);
    L.push('& D --> C: ack ' + i);
  }
  L.push('@enduml');
  w('shape-parallel.puml', L);
}

// 3. Wide: 24 participants, messages hop across the full width.
{
  const N = 24;
  const L = ['@startuml'];
  for (let p = 0; p < N; p++) L.push('participant "Svc' + String(p).padStart(2, '0') + '" as P' + p);
  for (let i = 0; i < 150; i++) {
    const a = i % N, b = (i * 7 + 3) % N;
    if (a === b) continue;
    L.push('P' + a + ' -> P' + b + ': call ' + i);
    if (i % 4 === 0) L.push('P' + b + ' --> P' + a + ': reply ' + i);
  }
  L.push('@enduml');
  w('shape-wide.puml', L);
}

// 4. Activation-heavy: activate/deactivate around every call, nested two deep.
{
  const L = ['@startuml', 'participant "T" as T', 'participant "A" as A', 'participant "B" as B'];
  for (let i = 0; i < 90; i++) {
    L.push('T -> A: req ' + i);
    L.push('activate A');
    L.push('A -> B: inner ' + i);
    L.push('activate B');
    L.push('B --> A: done ' + i);
    L.push('deactivate B');
    L.push('A --> T: resp ' + i);
    L.push('deactivate A');
    if (i % 6 === 0) {
      L.push('T -> A: nested ' + i);
      L.push('activate A');
      L.push('A -> A: recurse ' + i);
      L.push('activate A');
      L.push('A --> A: pop ' + i);
      L.push('deactivate A');
      L.push('A --> T: out ' + i);
      L.push('deactivate A');
    }
  }
  L.push('@enduml');
  w('shape-activation.puml', L);
}

// 5. Self-messages.
{
  const L = ['@startuml', 'participant "A" as A', 'participant "B" as B'];
  for (let i = 0; i < 120; i++) {
    L.push('A -> A: local step ' + i);
    if (i % 3 === 0) L.push('A -> B: sync ' + i);
    if (i % 3 === 1) L.push('B -> B: check ' + i);
  }
  L.push('@enduml');
  w('shape-self.puml', L);
}

// 6. Create/destroy participants.
{
  const L = ['@startuml', 'participant "Main" as M'];
  for (let i = 0; i < 40; i++) {
    L.push('create participant "Worker' + i + '" as W' + i);
    L.push('M -> W' + i + ': spawn ' + i);
    L.push('W' + i + ' --> M: ready ' + i);
    L.push('M -> W' + i + ': task ' + i);
    L.push('W' + i + ' --> M: result ' + i);
    L.push('destroy W' + i);
  }
  L.push('@enduml');
  w('shape-createdestroy.puml', L);
}

// 7. Many-small: 30 diagrams x 10 arrows (Kronikol reports are dominated by per-diagram fixed cost).
{
  const dir = path.join(R, 'many-small');
  fs.mkdirSync(dir, { recursive: true });
  for (let d = 0; d < 30; d++) {
    const L = ['@startuml', 'participant "Test" as T', 'participant "Api" as A', 'database "Db" as D'];
    for (let i = 0; i < 5; i++) {
      L.push('T -> A: op-' + d + '-' + i);
      L.push('A -> D: query ' + d + '.' + i);
      L.push('D --> A: rows');
      if (i === 2) { L.push('note right of A'); L.push('{ "d": ' + d + ', "i": ' + i + ' }'); L.push('end note'); }
      L.push('A --> T: ok');
    }
    L.push('@enduml');
    fs.writeFileSync(path.join(dir, 'small-' + String(d).padStart(2, '0') + '.puml'), L.join('\n') + '\n');
  }
  console.log('many-small/: 30 files x 20 arrows');
}
console.log('done');
