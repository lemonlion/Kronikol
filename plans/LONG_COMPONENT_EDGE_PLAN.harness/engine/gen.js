'use strict';
// Sources shaped like Kronikol's emitters (ComponentDiagramGenerator.GeneratePlantUml, useC4 false/true).
const BR = '\\n'; // the two-character escape

// Kronikol's label for one caller -> ClickHouse edge with n distinct statements.
function realLabel(n) {
  const methods = [];
  for (let k = 1; k <= n; k++) methods.push('INSERT INTO orders_archive_' + String(k).padStart(3, '0'));
  methods.sort();
  return 'ClickHouse: ' + methods.join(', ') + ' - ' + n + ' calls across 1 tests';
}

// Word wrap at `budget` characters between atoms (spaces), joined by the literal \n escape.
function wrap(text, budget = 100) {
  if (text.length <= budget) return text;
  const words = text.split(' '); const lines = []; let cur = '';
  for (const w of words) {
    if (cur.length === 0) cur = w;
    else if (cur.length + 1 + w.length <= budget) cur += ' ' + w;
    else { lines.push(cur); cur = w; }
  }
  if (cur) lines.push(cur);
  return lines.join(BR);
}

// A label of exactly `len` characters made of `unit` repeated (cut to length), optionally wrapped every `every` chars
// by inserting the escape (the escape counts toward len).
function fillLabel(len, unit = 'INSERT INTO orders_archive_001, ', every = 0) {
  let s = '';
  if (!every) { while (s.length < len) s += unit; return s.slice(0, len); }
  let line = '';
  while (s.length + line.length < len) {
    if (line.length === every) { s += line + BR; line = ''; continue; }
    line += unit[(s.length + line.length) % unit.length];
  }
  s += line;
  s = s.slice(0, len);
  if (s.endsWith('\\')) s = s.slice(0, -1) + 'x';
  return s;
}

const SKIN = [
  'skinparam defaultTextAlignment center', 'skinparam wrapWidth 200', 'skinparam shadowing false',
  'skinparam rectangle<<person>> {', '  BackgroundColor #08427B', '  FontColor #FFFFFF', '  BorderColor #073B6F', '  RoundCorner 25', '  StereotypeFontColor #08427B', '  StereotypeFontSize 0', '}',
  'skinparam rectangle<<system>> {', '  BackgroundColor #438DD5', '  FontColor #FFFFFF', '  BorderColor #3C7FC0', '  RoundCorner 25', '  StereotypeFontColor #438DD5', '  StereotypeFontSize 0', '}',
  'skinparam database {', '  BackgroundColor #E74C3C', '  FontColor #FFFFFF', '  BorderColor #C0392B', '}',
  'skinparam arrow {', '  Color #666666', '  FontColor #666666', '  FontSize 11', '}'
];

// Non-C4 component diagram, Kronikol's header, with `edgeLine` as the one edge.
function component(edgeLine, opts = {}) {
  const a = opts.callerAlias || 'caller', b = opts.serviceAlias || 'warehouse';
  return ['@startuml', 'left to right direction', ...(opts.noSkin ? [] : SKIN), '', 'title Component Diagram', '',
    `rectangle "**Caller**${BR}<size:10>[Person]</size>" as ${a} <<person>>`,
    `database "Warehouse" as ${b}`, '', edgeLine, '', '@enduml'].join('\n');
}

function c4(edgeLine, opts = {}) {
  const a = opts.callerAlias || 'caller', b = opts.serviceAlias || 'warehouse';
  return ['@startuml', 'left to right direction', '!include <C4/C4_Context>', '', 'title Component Diagram', '',
    `Person(${a}, "Caller")`, `SystemDb(${b}, "Warehouse")`, '', edgeLine, '', '@enduml'].join('\n');
}

function sequence(msgLine) {
  return ['@startuml', '!pragma teoz true', 'participant a', 'participant b', msgLine, '@enduml'].join('\n');
}

function klass(linkLine) {
  return ['@startuml', 'class a', 'class b', linkLine, '@enduml'].join('\n');
}

// Cut like Kronikol's TruncateLabel (no stranded backslash), no marker.
function cut(s, n) { if (s.length <= n) return s; let c = n; let k = 0; while (c - k > 0 && s[c - k - 1] === '\\') k++; if (k % 2 === 1) c--; return s.slice(0, c); }

// A label of length n from a generator of an arbitrarily long label.
function sized(n, make) { let s = make(); while (s.length < n) s += make(); return cut(s, n); }

const LONG_A = 'svc' + 'a'.repeat(197), LONG_B = 'svc' + 'b'.repeat(197); // 200-char aliases

// Kronikol-shaped sequence diagram (PlantUmlCreator.CreatePlantUmlPrefix, no theme) with `stmt` placed after one
// message; `block` adds a message and `end` after it (for block openers); `style` adds the stepBody style block.
function kseq(stmt, opts = {}) {
  const head = ['@startuml', '!pragma teoz true'];
  if (opts.style) head.push('<style>', ' .stepBody {', '     BackgroundColor black', '     FontColor white', '     LineColor white', ' }', '</style>');
  head.push('skinparam wrapWidth 800', 'autonumber 1', '');
  const parts = opts.participants || ['actor "Caller" as caller', 'participant "Orders Api" as ordersApi'];
  const body = [opts.first || 'caller -> ordersApi : GET /orders', stmt];
  if (opts.block) body.push('caller -> ordersApi : GET /orders/1', 'end');
  return [...head, ...parts, '', ...body, '@enduml'].join('\n');
}

// Kronikol-shaped activity diagram (InternalFlowRenderer.RenderActivityDiagram).
function kactivity(stmt) {
  return ['@startuml', 'skinparam ActivityBackgroundColor #f0f4ff', 'skinparam ActivityBorderColor #666', 'skinparam SwimlaneBorderColor #ccc',
    'skinparam wrapWidth 800', '|Orders.Api|', ':HTTP GET /orders (3ms);', stmt, '@enduml'].join('\n');
}

// Kronikol's name form: wrap every 80 with \n, bold re-opened per display line.
function boldPerLine(name) { return '**' + name.split('\\n').join('**\\n**') + '**'; }

// Each shape: { pre, post, wrapLabel(len) → label of that length, build(stmt) → source }.
// len is the length of the whole (trimmed) statement line, as Kronikol's caps count it.
const SHAPES = {
  plain:    { pre: 'caller --> warehouse : "', post: '"', label: (n) => fillLabel(n), build: (st) => component(st) },
  unquoted: { pre: 'caller --> warehouse : ', post: '', label: (n) => fillLabel(n), build: (st) => component(st) },
  coloured: { pre: 'caller -[#E74C3C]-> warehouse : "', post: '"', label: (n) => fillLabel(n), build: (st) => component(st) },
  wrapped:  { pre: 'caller --> warehouse : "', post: '"', label: (n) => fillLabel(n, 'INSERT INTO orders_archive_001, ', 100), build: (st) => component(st) },
  real:     { pre: 'caller --> warehouse : "', post: '"', label: (n) => cut(wrap(realLabel(Math.ceil(n / 25) + 2)), n), build: (st) => component(st) },
  realunwrapped: { pre: 'caller --> warehouse : "', post: '"', label: (n) => cut(realLabel(Math.ceil(n / 25) + 2), n), build: (st) => component(st) },
  quotes:   { pre: 'caller --> warehouse : "', post: '"', label: (n) => fillLabel(n, 'INSERT INTO "orders_archive_001", '), build: (st) => component(st) },
  escapes:  { pre: 'caller --> warehouse : "', post: '"', label: (n) => cut('x' + '\\n'.repeat(Math.ceil(n / 2)), n), build: (st) => component(st) },
  commas:   { pre: 'caller --> warehouse : "', post: '"', label: (n) => 'x' + ','.repeat(n - 1), build: (st) => component(st) },
  longalias:{ pre: LONG_A + ' --> ' + LONG_B + ' : "', post: '"', label: (n) => fillLabel(n), build: (st) => component(st, { callerAlias: LONG_A, serviceAlias: LONG_B }) },
  noskin:   { pre: 'caller --> warehouse : "', post: '"', label: (n) => fillLabel(n), build: (st) => component(st, { noSkin: true }) },
  seq:      { pre: 'a -> b : ', post: '', label: (n) => fillLabel(n), build: (st) => sequence(st) },
  seqlink:  { pre: 'a -> b : [[#iflow-1 ', post: ']]', label: (n) => fillLabel(n), build: (st) => sequence(st) },
  class:    { pre: 'a --> b : "', post: '"', label: (n) => fillLabel(n), build: (st) => klass(st) },
  classunq: { pre: 'a --> b : ', post: '', label: (n) => fillLabel(n), build: (st) => klass(st) },
  c4:       { pre: 'Rel(caller, warehouse, "', post: '")', label: (n) => fillLabel(n), build: (st) => c4(st) },
  // block openers, Kronikol's collapsed-run label `loop ×N · summary`
  loop:     { pre: 'loop ×12 · ', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st, { block: true }) },
  alt:      { pre: 'alt ', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st, { block: true }) },
  group:    { pre: 'group ', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st, { block: true }) },
  opt:      { pre: 'opt ', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st, { block: true }) },
  // step bars: the legacy coloured one-line bar, the same without the colour tag, and the styled (rich) form
  colourbar:{ pre: 'hnote across <<stepDelimiter>> #black:<color:white>', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st) },
  bar:      { pre: 'hnote across <<stepDelimiter>> #black:', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st) },
  richbar:  { pre: 'hnote across <<stepDelimiter>><<stepBody>>: ', post: '', label: (n) => fillLabel(n), build: (st) => kseq(st, { style: true }) },
  // the Kronikol sequence message with its internal-flow link
  kseqlink: { pre: 'caller -> ordersApi : [[#iflow-0f8fad5b-d9cb-469f-a165-70867728950e ', post: ']]', label: (n) => fillLabel(n), build: (st) => kseq(st) },
  // participant declarations and titles (one long unbroken name; `wrapped` forms break every 80/100 like Kronikol)
  seqparticipant: { pre: 'participant "', post: '" as ordersApi', label: (n) => fillLabel(n), build: (st) => kseq('caller -> ordersApi : GET /orders/1', { participants: ['actor "Caller" as caller', st] }) },
  seqdatabase:    { pre: 'database "', post: '" as ordersDb', label: (n) => fillLabel(n), build: (st) => kseq('caller -> ordersDb : SELECT 1', { participants: ['actor "Caller" as caller', 'participant "Orders Api" as ordersApi', st] }) },
  comprect:       { pre: 'rectangle "**', post: '**\\n<size:10>[Software System]</size>" as ordersApi <<system>>', label: (n) => fillLabel(n), build: (st) => compWith(st, 'ordersApi') },
  comprectwrapped:{ pre: 'rectangle "', post: '\\n<size:10>[Software System]</size>" as ordersApi <<system>>', label: (n) => cut(boldPerLine(wrap(fillLabel(n * 2), 80)), n), build: (st) => compWith(st, 'ordersApi') },
  compdb:         { pre: 'database "', post: '" as ordersDb', label: (n) => fillLabel(n), build: (st) => compWith(st, 'ordersDb') },
  comptitle:      { pre: 'title ', post: '', label: (n) => fillLabel(n), build: (st) => component('caller --> warehouse : "ClickHouse: SELECT - 1 calls across 1 tests"').replace('title Component Diagram', st) },
  comptitlewrapped:{ pre: 'title ', post: '', label: (n) => fillLabel(n, 'INSERT INTO orders_archive_001, ', 100), build: (st) => component('caller --> warehouse : "ClickHouse: SELECT - 1 calls across 1 tests"').replace('title Component Diagram', st) },
  activity:       { pre: ':', post: ' (12ms);', label: (n) => fillLabel(n), build: (st) => kactivity(st) },
  activitywrapped:{ pre: ':', post: ' (12ms);', label: (n) => fillLabel(n, 'INSERT INTO orders_archive_001, ', 100), build: (st) => kactivity(st) },
};

// A component diagram whose participant `alias` is declared by `decl` (in place of Kronikol's usual line).
function compWith(decl, alias) {
  return ['@startuml', 'left to right direction', ...SKIN, '', 'title Component Diagram', '',
    `rectangle "**Caller**${BR}<size:10>[Person]</size>" as caller <<person>>`, decl, '',
    `caller --> ${alias} : "HTTP: GET - 1 calls across 1 tests"`, '', '@enduml'].join('\n');
}

// The source whose shape-statement is exactly `len` characters long (or as close as the label allows).
function shapeSource(name, len) {
  const sh = SHAPES[name]; if (!sh) throw new Error('unknown shape ' + name);
  const n = Math.max(1, len - sh.pre.length - sh.post.length);
  const st = sh.pre + sh.label(n) + sh.post;
  return { statement: st, source: sh.build(st) };
}

module.exports = { realLabel, wrap, fillLabel, component, c4, sequence, klass, BR, SHAPES, shapeSource, cut };
