# Round 17 item 10: the flame chart's bars take a hue from a curated palette (one per source, no fading with depth) as
# a custom property; c-round17.css colours them. Edits build.js once, before the note-buttons swap block.
import io
p = 'build.js'
s = io.open(p, encoding='utf-8', newline='').read()
if 'item 10' in s:
    print('already patched'); raise SystemExit
anchor = "// Round 17 (owner, items 13-15): the page's note buttons"
assert s.count(anchor) == 1
block = r"""// Round 17 (owner, item 10): flame bars carry a palette hue (--fh) in place of a random hue faded by depth.
{
  let n = 0;
  const from = "background:hsl(' + hue + ', 60%, ' + lightness\n            + '%)\"";
  const swap = text => {
    const at = text.indexOf(from);
    if (at < 0) return text;
    n++;
    text = text.slice(0, at) + "--fh:' + FLAME_HUES[Math.abs(hashCode(source)) % FLAME_HUES.length] + '\"" + text.slice(at + from.length);
    return text.replace('function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {',
      '// Round 17 (owner, item 10): eight hues that sit with the diagrams (blue, violet, teal, rose, green, amber, purple, coral)\n' +
      '    var FLAME_HUES = [212, 262, 188, 338, 152, 32, 290, 8];\n' +
      '    function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {');
  };
  headPart = swap(headPart); bodyPart = swap(bodyPart);
  if (n !== 1) throw new Error('flame bar colour found ' + n + ' times');
  console.log('flame palette swapped');
}

"""
s = s.replace(anchor, block + anchor)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
