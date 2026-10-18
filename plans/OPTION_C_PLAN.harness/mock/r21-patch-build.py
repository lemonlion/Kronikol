# Round 17 item 21: build.js rewrites the diagram menu's "open in new tab" items to open the tab during the click
# (c-round17.js helpers). Edits build.js once, before the note-buttons swap block.
import io
p = 'build.js'
s = io.open(p, encoding='utf-8', newline='').read()
if 'item 21' in s:
    print('already patched'); raise SystemExit
anchor = "// Round 17 (owner, items 13-15): the page's note buttons"
assert s.count(anchor) == 1
block = r"""// Round 17 (owner, item 21): the diagram menu's "open in new tab" items open the tab while the click is handled and
// write the picture or source into it (c-round17.js), so a popup blocker lets it through and no blob: URL is navigated.
{
  let n = 0;
  const swaps = [
    [/svgToCanvas\(svg, function\(canvas\) \{\s*canvas\.toBlob\(function\(blob\) \{\s*window\.open\(URL\.createObjectURL\(blob\)\);\s*\}, 'image\/png'\);\s*\}\);/g,
      "(function (w) { svgToCanvas(svg, function(canvas) { window.__kOpenPng(w, canvas); }); })(window.__kOpenTab());"],
    [/svgToCanvasWithBg\(svg, function\(canvas\) \{\s*canvas\.toBlob\(function\(blob\) \{\s*window\.open\(URL\.createObjectURL\(blob\)\);\s*\}, 'image\/png'\);\s*\}\);/g,
      "(function (w) { svgToCanvasWithBg(svg, function(canvas) { window.__kOpenPng(w, canvas); }); })(window.__kOpenTab());"],
    [/var blob = new Blob\(\[serializeSvg\(svg\)\], \{ type: 'image\/svg\+xml' \}\);\s*window\.open\(URL\.createObjectURL\(blob\)\);/g,
      "window.__kOpenSvg(serializeSvg(svg));"],
    [/var blob = new Blob\(\[(\w+)\], \{ type: 'text\/plain;charset=utf-8' \}\);\s*window\.open\(URL\.createObjectURL\(blob\)\);/g,
      "window.__kOpenText($1);"],
  ];
  const apply = text => { for (const [rx, to] of swaps) text = text.replace(rx, () => { n++; return to.replace('$1', '__ARG__'); }); return text; };
  // the text swap keeps its argument
  const applyAll = text => {
    for (const [rx, to] of swaps) text = text.replace(rx, (m, a) => { n++; return a ? to.replace('$1', a) : to; });
    return text;
  };
  headPart = applyAll(headPart); bodyPart = applyAll(bodyPart);
  if (n < 6) throw new Error('open-in-new-tab items found ' + n + ' times');
  console.log('open-in-new-tab items swapped ' + n);
}

"""
s = s.replace(anchor, block + anchor)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
