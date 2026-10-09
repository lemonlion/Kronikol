'use strict';
// Experimental fixes applied IN MEMORY to an UNOBFUSCATED TeaVM build (core-oct-master-noobf.js), to test the two
// candidate upstream fixes. Selected by env: PATCH_LABELS=1 (PlantUML side) and/or PATCH_TEAVM=1 (TeaVM classlib side).

// PlantUML side: Labels.init runs its three quote-splitting regexes only when the label's %g characters
// (" U+201C U+201D U+E121) sit where that regex's anchors need them. Results are unchanged; the recursion is skipped.
const isQ = (c) => c === '"' || c === '“' || c === '”' || c === '';
globalThis.__labelsGuard = (s) => {
  let q = 0; for (let i = 0; i < s.length; i++) if (isQ(s[i])) q++;
  const f = s.length > 0 && isQ(s[0]), l = s.length > 0 && isQ(s[s.length - 1]);
  return (q === 4 && f && l) || (q === 2 && f && !l) || (q === 2 && !f && l);
};

// TeaVM side: a GroupQuantifierSet (X* / the tail of X+) whose X is a character class (TCompositeRangeSet or
// TSupplRangeSet) runs as a loop, the way TLeafQuantifierSet and OpenJDK's CharPropertyGreedy do: consume code points
// while they are in the class, then offer the continuation each end position from the longest down. Same attempts in
// the same order as the recursive version. A lone surrogate falls back to the original code (prototype limitation).
let GQS_STATS = { iter: 0, fallback: 0 };
globalThis.__gqsStats = GQS_STATS;
globalThis.__iterGQS = (q, i, s, mr, GQS, CRS, SRS) => {
  if (q.constructor !== GQS) return undefined;
  const inner = q.$innerSet;
  const srs = inner instanceof CRS ? inner.$withoutSurrogates : inner;
  if (!(srs instanceof SRS)) return undefined;
  const right = mr.$rightBound;
  const pos = [i]; let j = i;
  while (j < right) {
    const c = s.$charAt(j);
    if (c >= 0xD800 && c <= 0xDFFF) {
      if (c <= 0xDBFF && j + 1 < right) {
        const d = s.$charAt(j + 1);
        if (d >= 0xDC00 && d <= 0xDFFF && srs.$contains2(((c - 0xD800) << 10) + (d - 0xDC00) + 0x10000)) { j += 2; pos.push(j); continue; }
      }
      GQS_STATS.fallback++;
      return undefined;
    }
    if (!srs.$contains2(c)) break;
    j++; pos.push(j);
  }
  GQS_STATS.iter++;
  for (let k = pos.length - 1; k >= 0; k--) { const r = q.$next6.$matches1(pos[k], s, mr); if (r >= 0) return r; }
  return -1;
};

// TeaVM side, lazy counterpart (PATCH_TEAVM_LAZY=1): a TReluctantGroupQuantifierSet (X*? / tail of X+?) whose X is
// a TDotSet runs as a loop: offer the continuation at the current position, else consume one code point that is not
// a line terminator and repeat, the way OpenJDK's lazy Curly does. Same attempts in the same order as the recursion.
const RGQS_STATS = { iter: 0 };
globalThis.__rgqsStats = RGQS_STATS;
globalThis.__iterRGQS = (q, i, s, mr, RGQS, DOT) => {
  if (q.constructor !== RGQS) return undefined;
  const inner = q.$innerSet;
  if (!inner || inner.constructor !== DOT || inner.$next6 !== q) return undefined;
  RGQS_STATS.iter++;
  const right = mr.$rightBound;
  for (;;) {
    const r = q.$next6.$matches1(i, s, mr);
    if (r >= 0) return r;
    if (i + 1 > right) { mr.$hitEnd = 1; return -1; }
    const hi = s.$charAt(i);
    if (hi >= 0xD800 && hi <= 0xDBFF && i + 2 <= right) {
      const lo = s.$charAt(i + 1);
      if (lo >= 0xDC00 && lo <= 0xDFFF) {
        if (inner.$lt0.$isLineTerminator(((hi - 0xD800) << 10) + (lo - 0xDC00) + 0x10000)) return -1;
        i += 2; continue;
      }
    }
    if (inner.$lt0.$isLineTerminator(hi)) return -1;
    i += 1;
  }
};

function transform(code) {
  if (process.env.PATCH_TEAVM_LAZY) {
    const site = 'jur_ReluctantGroupQuantifierSet_matches = ($this, $stringIndex, $testString, $matchResult) => {\n    let $res;';
    if (code.split(site).length !== 2) throw new Error('PATCH_TEAVM_LAZY needs the unobfuscated build');
    code = code.replace(site, site + '\n    { const __r = globalThis.__iterRGQS($this, $stringIndex, $testString, $matchResult, jur_ReluctantGroupQuantifierSet, jur_DotSet); if (__r !== undefined) return __r; }');
  }
  if (process.env.PATCH_LABELS) {
    const site = 'if ($this.$firstLabel === null && $this.$secondLabel === null) {';
    if (code.split(site).length !== 2) throw new Error('PATCH_LABELS needs the unobfuscated build');
    code = code.replace(site, 'if ($this.$firstLabel === null && $this.$secondLabel === null && globalThis.__labelsGuard(String($labelLink.$nativeString))) {');
  }
  if (process.env.PATCH_TEAVM) {
    const site = 'jur_GroupQuantifierSet_matches = ($this, $stringIndex, $testString, $matchResult) => {\n    let $nextIndex;';
    if (code.split(site).length !== 2) throw new Error('PATCH_TEAVM needs the unobfuscated build');
    code = code.replace(site, site + '\n    { const __r = globalThis.__iterGQS($this, $stringIndex, $testString, $matchResult, jur_GroupQuantifierSet, jur_CompositeRangeSet, jur_SupplRangeSet); if (__r !== undefined) return __r; }');
  }
  return code;
}
module.exports = { transform };
