'use strict';
// I5: where the zoom slider stands under the pinned report bar, and under the open "This scenario" settings stuck below
// the bar. Measures the slider input's box against the bar's bottom and the settings' bottom, asks the page what is drawn
// at the slider's centre, walks the pointer from the diagram up to the slider reading the slider's opacity, and crops a
// picture of each place.
// usage: node probe/i5-slider.js <main.html> <outDir>
const path = require('path');
const { open, frames, openScenario, write } = require('./common');

(async () => {
  const [file, outDir] = process.argv.slice(2);
  const { browser, page } = await open(file, 1600, 900, 'light');
  const out = {};
  try {
    await openScenario(page, 'Bake a large breakfast order');
    // well inside the tall diagram, so the bar is pinned over it and the slider follows the window; the page keeps its
    // own place while far parts are sized (5.7.x), so scroll again until the box's top stays 1,200 px above the window
    let boxTop = 0;
    for (let k = 0; k < 20 && boxTop > -1100; k++) {
      await page.evaluate(() => {
        const box = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
        window.scrollTo(0, box.getBoundingClientRect().top + window.scrollY + 1200);
      });
      await frames(page, 20);
      boxTop = await page.evaluate(() => document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]').getBoundingClientRect().top);
    }
    out.boxTop = Math.round(boxTop);
    if (boxTop > -1100) throw new Error('the window did not stay inside the diagram: box top ' + boxTop);
    const centre = await page.evaluate(() => {
      const r = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]').getBoundingClientRect();
      return { x: Math.round(r.left + Math.min(r.width / 2, 400)), y: Math.round(innerHeight * 0.6) };
    });
    const measure = () => page.evaluate(() => {
      const box = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      const bar = document.querySelector('.toolbar-row.report-nav').getBoundingClientRect();
      const controls = box.querySelector(':scope > .diagram-zoom-controls');
      const input = controls && controls.querySelector('.diagram-zoom-slider');
      const ir = input ? input.getBoundingClientRect() : null;
      const settings = document.querySelector('details.scenario[data-probe] .scenario-settings.is-open');
      const sr = settings ? settings.getBoundingClientRect() : null;
      const hit = ir ? document.elementFromPoint(ir.left + ir.width / 2, ir.top + ir.height / 2) : null;
      return {
        navH: getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h').trim(),
        barBottom: +bar.bottom.toFixed(2),
        input: ir && { top: +ir.top.toFixed(2), bottom: +ir.bottom.toFixed(2), left: +ir.left.toFixed(2), width: +ir.width.toFixed(2), height: +ir.height.toFixed(2) },
        opacity: controls ? getComputedStyle(controls).opacity : null,
        transform: controls ? controls.style.transform : null,
        settings: sr && { top: +sr.top.toFixed(2), bottom: +sr.bottom.toFixed(2), position: getComputedStyle(settings).position, z: getComputedStyle(settings).zIndex },
        gapToBar: ir ? +(ir.top - bar.bottom).toFixed(2) : null,
        gapToSettings: ir && sr ? +(ir.top - sr.bottom).toFixed(2) : null,
        hitIsSlider: hit ? hit === input || (controls && controls.contains(hit)) : null,
        hit: hit ? hit.tagName.toLowerCase() + (hit.className && typeof hit.className === 'string' ? '.' + hit.className.split(' ').join('.') : '') : null
      };
    });
    const crop = async (name, m) => {
      if (!m.input) return;
      await page.screenshot({ path: path.join(outDir, `i5-${name}.png`),
        clip: { x: Math.max(0, m.input.left - 40), y: Math.max(0, m.barBottom - 40), width: 260, height: (m.settings ? m.settings.bottom - m.barBottom : 0) + 90 } });
    };

    // 1. hovered, the bar pinned, the settings closed
    await page.mouse.move(centre.x, centre.y);
    await frames(page, 6);
    out.hovered = await measure();
    await crop('hovered', out.hovered);

    // 2. the settings opened from their gear, stuck under the bar; hovered again
    await page.evaluate(() => {
      const gear = document.querySelector('details.scenario[data-probe] .scenario-settings-gear');
      if (!gear) throw new Error('no settings gear');
      gear.click();
    });
    await frames(page, 6);
    await page.mouse.move(centre.x + 5, centre.y + 5);
    await frames(page, 6);
    out.hoveredWithSettings = await measure();
    await crop('hovered-settings', out.hoveredWithSettings);

    // 3. the pointer walked from the diagram up to the slider: is it still shown when the pointer reaches it?
    const target = out.hoveredWithSettings.input;
    if (target) {
      const steps = 12, walk = [];
      const tx = target.left + target.width / 2, ty = target.top + target.height / 2;
      for (let i = 1; i <= steps; i++) {
        const x = centre.x + (tx - centre.x) * i / steps, y = centre.y + (ty - centre.y) * i / steps;
        await page.mouse.move(x, y);
        await frames(page, 2);
        walk.push(await page.evaluate(([x, y]) => {
          const c = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"] > .diagram-zoom-controls');
          const hit = document.elementFromPoint(x, y);
          return { y: Math.round(y), opacity: getComputedStyle(c).opacity, pointerEvents: getComputedStyle(c).pointerEvents, over: hit ? hit.tagName.toLowerCase() + '.' + String(hit.className && hit.className.baseVal !== undefined ? hit.className.baseVal : hit.className).split(' ').join('.') : null };
        }, [x, y]));
      }
      out.walkToSlider = walk;
    }
    write(path.join(outDir, 'i5-slider.json'), out);
    console.log(JSON.stringify(out, null, 2));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
