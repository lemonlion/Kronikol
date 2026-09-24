// Applies the candidate P2 CSS to copies of the generated pages. Usage: node patch.js [breakpoint] [outDir] [inDir]
// Env: CI_WRAP=1 adds the S1b cap; NO_SEARCH_MIN=1 leaves out `#searchbar { min-width: 0 }` (the variant
// that showed the rule does nothing: the input's `width: 100%` already caps its automatic minimum);
// NO_TOOLBAR_LEFT=1 leaves out `.toolbar-left { flex-wrap: wrap }` (the second pass's addition: with the
// default component diagram the top bar's four buttons wrap their labels at 500 to 580 px without it).
//   node patch.js 1100 out-patched          the prototype (in: out/)
//   node patch.js 0 out-noband              the D5 variant: everything but the band wrap
//   node patch.js 1100 out-stress-patched out-stress
// A: the width fixes, appended as a style block (no competing rules, so order is immaterial).
// B: the cascade fix, the custom (violet) stylesheet moved after the component sheets, plus the S4
//    hover rules the violet theme lacks, the idle hovers at the HEAD of the theme (before the active
//    rules they must not shadow at equal specificity) and the active rules at its tail. And the
//    theme's own four-toggle hover block moved above its active block, for the same reason.
// Done by string surgery on the HTML, as the generator would.
const fs = require('fs');
const path = require('path');
const IN = path.join(__dirname, process.argv[4] || 'out'), OUTP = path.join(__dirname, process.argv[3] || 'out-patched');
fs.mkdirSync(OUTP, { recursive: true });
const BREAK = parseInt(process.argv[2] || '1100', 10);
// The lower bound of the two row-layout blocks. 769 leaves fractional widths between the phone block
// (max-width: 768px) and these unmatched (768.8 px in Firefox at 125 %); 768.02 closes the gap.
const LO = process.env.BAND_MIN || "769";

const A = `
<style id="p2-width-fixes">
.export-btn { white-space: nowrap; }
.filtering-box-export { flex-wrap: wrap; }
.filtering-box-header { flex-wrap: wrap; }
${process.env.NO_SEARCH_MIN ? '' : '#searchbar { min-width: 0; }'}
${process.env.NO_TOOLBAR_LEFT ? '' : '.toolbar-left { flex-wrap: wrap; }'}
${process.env.CI_WRAP ? `@media (min-width: ${LO}px) { .ci-metadata { max-width: 20em; } } .ci-metadata table { max-width: 100%; } .ci-metadata td:first-child { white-space: nowrap; } .ci-metadata td:last-child { overflow-wrap: anywhere; }` : ''}
@media (min-width: ${LO}px) and (max-width: ${BREAK}px) {
    .header-row { flex-wrap: wrap; }
    .filtering-box { flex-basis: 100%; }
}
</style>`;

const BASE = `
/* F5: the toolbar base, always shipped (was internal-flow-popup-styles.css). F11: wraps at every width. */
.diagram-toggle { margin-top: 8px; margin-bottom: 8px; padding-left: 1em; padding-right: 1em; display: flex; align-items: center; width: 100%; box-sizing: border-box; flex-wrap: wrap; }
.diagram-toggle-btn { padding: 4px 14px; border: 1px solid #ccc; background: #f5f5f5; cursor: pointer; font-size: 13px; border-radius: 4px; margin-right: 4px; }
.diagram-toggle-btn:hover { background: #e8f0fe; }
.diagram-toggle-active { background: #4285f4; color: #fff; border-color: #4285f4; }
.diagram-toggle-active:hover { background: #3367d6; }
.diagram-toggle-spacer { flex: 1; }
`;
const VIOLET_START = '.feature { background-color: #DDD6FE; }';
const VIOLET_END = '.back-to-top:hover { background: #7C3AED; }';
// S4, head: idle hovers (0,2,0), placed before every active rule they could shadow.
const VIOLET_HEAD = `
                .export-btn:hover, .collapse-expand-all:hover, .percentile-btn:hover, .timeline-toggle:hover,
                .details-radio-btn:hover, .scenario-diagram-controls-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
                .diagram-toggle-btn:hover, .iflow-toggle-btn:hover { background: #EDE9FE; }
                .dep-mode-toggle:hover, .cat-mode-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
                .iflow-rel-summary-table tr:hover td { background: #EDE9FE; }
`;
// S4, tail: active states and their hovers.
const VIOLET_TAIL = `
                .percentile-btn.percentile-active { border-color: #8B5CF6; }
                .timeline-toggle-active { background: #8B5CF6; color: white; border-color: #8B5CF6; }
                .timeline-toggle-active:hover { background: #7C3AED; }
                .diagram-toggle-active:hover { background: #7C3AED; }
`;

for (const name of fs.readdirSync(IN).filter(f => f.endsWith('.html'))) {
  let html = fs.readFileSync(path.join(IN, name), 'utf8');
  const s = html.indexOf(VIOLET_START), e = html.indexOf(VIOLET_END);
  if (s >= 0 && e > s) {
    let block = html.slice(s, e + VIOLET_END.length);
    // The theme's four-toggle hover block above its active block.
    const hs = block.indexOf('.happy-path-toggle:hover,');
    const he = block.indexOf('}', hs) + 1;
    const as = block.indexOf('.happy-path-toggle.happy-path-active,');
    if (hs > as && as >= 0) {
      const hover = block.slice(hs, he);
      block = block.slice(0, hs) + block.slice(he);
      block = block.slice(0, as) + hover + '\n                ' + block.slice(as);
    }
    html = html.slice(0, s) + html.slice(e + VIOLET_END.length);
    const close = html.indexOf('</style>');
    html = html.slice(0, close) + VIOLET_HEAD + block + VIOLET_TAIL + html.slice(close);
  }
  // Base toolbar rules at the head of the main <style>, i.e. before the theme, as stylesheets.css will carry them.
  html = html.replace('<style>', '<style>' + BASE);
  // A: width fixes go where CustomCss would, after the main <style>.
  html = html.replace('</style>', '</style>' + A);
  fs.writeFileSync(path.join(OUTP, name), html);
  console.log('patched', name, s >= 0 ? '(violet moved)' : '');
}
