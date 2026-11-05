const L = require('./lib.js');
const { seq } = L;
// state of a plain scenario by a word of its title
async function plain(p, word) {
  return p.evaluate((word) => {
    const s = [...document.querySelectorAll('details.scenario')].find(x => x.querySelector('summary').textContent.includes(word));
    const f = (sel) => { const d = s.querySelector(sel); return d ? (d.open ? 'O' : 'c') : '-'; };
    return `scenario=${s.open ? 'O' : 'c'} display=${s.style.display || 'shown'} bg=${f('details.scenario-background')} steps=${f('details.scenario-steps')} diag=${f('details.example-diagrams')}`;
  }, word);
}
async function tog(p, word, sel) {
  const s = p.locator('details.scenario', { hasText: word }).first();
  await s.locator(sel + ' > summary').first().click();
  await p.waitForTimeout(40);
}
(async () => {
  await L.start();
  await seq('P1 plain: search, close Background, Clear All, same search', 'sep', async (p, snap, say) => {
    await L.type(p, 'mango');
    say('search mango       ' + await plain(p, 'mango'));
    await tog(p, 'mango', 'details.scenario-background');
    say('reader closes Bg   ' + await plain(p, 'mango'));
    await p.locator('button', { hasText: 'Clear All' }).first().click();
    await p.waitForTimeout(100);
    say('Clear All          ' + await plain(p, 'mango') + ' bar=' + JSON.stringify(await p.inputValue('#searchbar')));
    await L.typeOver(p, 'mango');
    say('search mango again ' + await plain(p, 'mango'));
  });
  await seq('P2 plain: search, close Background, Back, same search', 'sep', async (p, snap, say) => {
    await L.type(p, 'mango');
    say('search mango       ' + await plain(p, 'mango'));
    await tog(p, 'mango', 'details.scenario-background');
    say('reader closes Bg   ' + await plain(p, 'mango'));
    await p.goBack();
    await p.waitForTimeout(300);
    say('Back               ' + await plain(p, 'mango') + ' bar=' + JSON.stringify(await p.inputValue('#searchbar')) + ' hash=' + await p.evaluate(() => location.hash));
    await L.typeOver(p, 'mango');
    say('search mango again ' + await plain(p, 'mango'));
  });
  await seq('P3 plain: Steps closed by default: search, close Steps, Clear All, same search', 'sep-closed', async (p, snap, say) => {
    await L.type(p, 'mango');
    say('search mango       ' + await plain(p, 'mango'));
    await tog(p, 'mango', 'details.scenario-steps');
    say('reader closes Steps ' + await plain(p, 'mango'));
    await p.locator('button', { hasText: 'Clear All' }).first().click();
    await p.waitForTimeout(100);
    say('Clear All          ' + await plain(p, 'mango'));
    await L.typeOver(p, 'mango');
    say('search mango again ' + await plain(p, 'mango'));
    say('match visible? ' + await p.getByText('the mango customer pays').first().isVisible());
  });
  await seq('P4 plain: search A, close, other search B, back to A, no emptied field', 'sep', async (p, snap, say) => {
    await L.type(p, 'mango');
    await tog(p, 'mango', 'details.scenario-background');
    say('mango, reader closes Bg ' + await plain(p, 'mango'));
    await L.typeOver(p, 'kiwi');
    say('kiwi                ' + await plain(p, 'kiwi'));
    await L.typeOver(p, 'mango');
    say('mango again         ' + await plain(p, 'mango'));
  });
  await seq('P5 plain: search, reader closes the whole scenario, next key', 'sep', async (p, snap, say) => {
    await L.type(p, 'mango');
    await p.locator('details.scenario', { hasText: 'mango' }).first().locator('> summary').first().click();
    say('scenario closed    ' + await plain(p, 'mango'));
    await L.append(p, ' c');
    say('next key "mango c" ' + await plain(p, 'mango'));
  });
  await L.stop();
})().catch(e => { console.error(e); process.exit(1); });
