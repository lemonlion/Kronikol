// node diffview.js a.png b.png out.png : writes b with unchanged pixels faded to near-white, changed pixels kept
const fs = require('fs');
const { PNG } = require('C:/Code/Kronikol4J/playwright/node_modules/playwright-core/lib/utilsBundle');
const [fa, fb, fo] = process.argv.slice(2);
const a = PNG.sync.read(fs.readFileSync(fa)), b = PNG.sync.read(fs.readFileSync(fb));
const o = new PNG({ width: b.width, height: b.height });
let n = 0;
for (let i = 0; i < b.data.length; i += 4) {
  const d = Math.max(Math.abs(a.data[i] - b.data[i]), Math.abs(a.data[i + 1] - b.data[i + 1]), Math.abs(a.data[i + 2] - b.data[i + 2]));
  if (d > 40) { n++; o.data[i] = b.data[i]; o.data[i + 1] = b.data[i + 1]; o.data[i + 2] = b.data[i + 2]; }
  else { o.data[i] = 235; o.data[i + 1] = 235; o.data[i + 2] = 235; }
  o.data[i + 3] = 255;
}
fs.writeFileSync(fo, PNG.sync.write(o));
console.log('changed', n);
