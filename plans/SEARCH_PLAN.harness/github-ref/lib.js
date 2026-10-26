// Shared helpers for the GitHub search reference measurements.
// Bundled Chromium only: never pass `channel`, never an executablePath.
const path = require('path');
const fs = require('fs');
const pw = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core');

const OUT = __dirname;
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function launch() {
  return pw.chromium.launch();
}

async function newContext(browser, { width = 1280, height = 900, colorScheme = 'light' } = {}) {
  return browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: 1,
    colorScheme,
    userAgent: UA,
    locale: 'en-US',
    timezoneId: 'UTC',
  });
}

function save(name, data) {
  const p = path.join(OUT, name);
  fs.writeFileSync(p, typeof data === 'string' ? data : JSON.stringify(data, null, 2));
  return p;
}

// Page-side: measure an element's box and the computed styles that matter.
const MEASURE_FN = `
(function(){
  function hex(c){
    if(!c) return c;
    const m = c.match(/rgba?\\(([^)]+)\\)/);
    if(!m) return c;
    const p = m[1].split(/[ ,\\/]+/).filter(Boolean).map(Number);
    const h = (n)=>Math.round(n).toString(16).padStart(2,'0');
    let s = '#'+h(p[0])+h(p[1])+h(p[2]);
    if(p.length>3 && p[3] < 1) s += h(p[3]*255);
    return s;
  }
  function hexAll(v){ return (v||'').replace(/rgba?\\([^)]+\\)/g, (m)=>hex(m)); }
  window.__hex = hex;
  window.__measure = function(el){
    if(typeof el === 'string') el = document.querySelector(el);
    if(!el) return null;
    const r = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return {
      tag: el.tagName.toLowerCase(),
      cls: (el.className && el.className.baseVal !== undefined) ? el.className.baseVal : el.className,
      text: (el.innerText||el.textContent||'').trim().replace(/\\s+/g,' ').slice(0,120),
      x: +r.x.toFixed(2), y: +r.y.toFixed(2), w: +r.width.toFixed(2), h: +r.height.toFixed(2),
      display: cs.display, visibility: cs.visibility,
      fontFamily: cs.fontFamily, fontSize: cs.fontSize, fontWeight: cs.fontWeight, lineHeight: cs.lineHeight,
      color: hex(cs.color), bg: hex(cs.backgroundColor),
      border: [cs.borderTopWidth, cs.borderTopStyle, hex(cs.borderTopColor)].join(' '),
      borderBottom: [cs.borderBottomWidth, cs.borderBottomStyle, hex(cs.borderBottomColor)].join(' '),
      borderLeft: [cs.borderLeftWidth, cs.borderLeftStyle, hex(cs.borderLeftColor)].join(' '),
      radius: cs.borderRadius,
      padding: cs.padding, margin: cs.margin, gap: cs.gap,
      boxShadow: hexAll(cs.boxShadow),
      outline: hexAll(cs.outline),
      textDecoration: cs.textDecorationLine,
    };
  };
  window.__measureAll = function(sel, limit){
    return Array.from(document.querySelectorAll(sel)).slice(0, limit||50).map(window.__measure);
  };
})();
`;

async function installMeasure(page) {
  await page.evaluate(MEASURE_FN);
}

module.exports = { pw, OUT, UA, sleep, launch, newContext, save, installMeasure, MEASURE_FN };
