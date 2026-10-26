'use strict';
// I2: does a redraw show a picture in the engine's own colours for a frame? Watches every frame after the trigger (the
// next frame asked for from inside the frame callback) and, frame-independently, the end of every task that changes the
// diagram's box (a MutationObserver: an IntersectionObserver cannot run before the next paint, so a picture shown untagged
// at that point is painted raw). Counts, among the NEW pictures that show in the window: those the role pass has not
// tagged, note bodies still computing the engine's #FEFFDD, and step bars still computing black. Also records the paints
// as a CDP screencast. That premise holds for the page as 5.8.0 and 5.9.0 write it. Once the role pass waits on a
// ResizeObserver (section 4.2, F3), a picture can be untagged when a frame callback samples it and tagged before that
// frame paints, so on F3's paths the screencast, not the frame count, says what was painted (F5).
// usage: node probe/i2-flash.js <main.html> <light|dark> <note|payloads> <outDir>
//   note      a double-click on a note (a note's own redraw: rerenderWithNoteStates)
//   payloads  the report's Payloads: Expanded (the queue: processRenderQueue), the control
const path = require('path');
const fs = require('fs');
const { open, frames, openScenario, write } = require('./common');

(async () => {
  const [file, theme, trigger, outDir] = process.argv.slice(2);
  const { browser, context, page } = await open(file, 1600, 1000, theme);
  try {
    await openScenario(page, 'Bake a large breakfast order');
    const premise = await page.evaluate(() => {
      const box = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      return { parts: box.querySelectorAll(':scope > .puml-fragment').length, notes: box.querySelectorAll('.note-hover-rect').length };
    });
    if (premise.parts < 2) throw new Error('the diagram is not drawn in parts: ' + JSON.stringify(premise));
    await page.evaluate(() => document.querySelector('details.scenario[data-probe] .note-hover-rect').scrollIntoView({ block: 'center' }));
    await frames(page, 10);

    await page.evaluate(() => {
      const box = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      const before = new Set(box.querySelectorAll('svg'));
      const inView = (el) => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0 && r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth; };
      const sample = () => {
        const svgs = Array.from(box.querySelectorAll('svg')).filter((s) => !before.has(s) && s.getClientRects().length && inView(s));
        let untagged = 0, rawNotes = 0, rawBars = 0;
        for (const s of svgs) {
          if (!s.getAttribute('data-k-roles')) untagged++;
          for (const p of Array.from(s.querySelectorAll('path[fill="#FEFFDD" i]')).slice(0, 60)) { const cs = getComputedStyle(p); if (inView(p) && cs.visibility !== 'hidden' && cs.fill === 'rgb(254, 255, 221)') rawNotes++; }
          for (const r of Array.from(s.querySelectorAll('rect[fill="#000000" i]')).slice(0, 60)) if (inView(r) && getComputedStyle(r).fill === 'rgb(0, 0, 0)') rawBars++;
        }
        return { t: Math.round(performance.now()), shown: svgs.length, untagged, rawNotes, rawBars, rendering: !!box._noteRendering || !!window._plantumlRendering };
      };
      window.__flash = { frames: [], tasks: [], done: false, capped: false, sample };
      let n = 0;
      // 600 frames is about 10 s; a slow machine can still be drawing then, so the cap is recorded and fails the run below
      const tick = () => { window.__flash.frames.push(sample()); if (window.__flash.done) return; if (++n < 600) requestAnimationFrame(tick); else window.__flash.capped = !!(box._noteRendering || window._plantumlRendering); };
      requestAnimationFrame(tick);
      new MutationObserver(() => { const s = sample(); if (s.shown) window.__flash.tasks.push(s); })
        .observe(box, { attributes: true, childList: true, subtree: true, attributeFilter: ['style', 'class', 'data-rendered'] });
    });

    const cdp = await context.newCDPSession(page);
    const shots = [];
    cdp.on('Page.screencastFrame', async (f) => { shots.push({ ts: f.metadata.timestamp, data: f.data }); try { await cdp.send('Page.screencastFrameAck', { sessionId: f.sessionId }); } catch (e) { /* closed */ } });
    await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 70, everyNthFrame: 1 });

    if (trigger === 'note') {
      await page.evaluate(() => {
        const rects = Array.from(document.querySelectorAll('details.scenario[data-probe] .note-hover-rect'));
        const r = rects.find((x) => { const b = x.getBoundingClientRect(); return b.top > 0 && b.bottom < innerHeight; }) || rects[0];
        r.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
      });
    } else {
      await page.evaluate(() => {
        const btn = Array.from(document.querySelectorAll('#display-settings button')).find((b) => /expanded/i.test(b.textContent));
        if (!btn) throw new Error('no Expanded button in #display-settings');
        btn.click();
      });
    }
    await page.waitForFunction(() => {
      const box = document.querySelector('details.scenario[data-probe] [data-diagram-type="plantuml"]');
      return window.__flash.frames.some((f) => f.rendering) && !box._noteRendering && !window._plantumlRendering;
    }, null, { polling: 200, timeout: 120000 }).catch(() => {});
    await frames(page, 30);
    await cdp.send('Page.stopScreencast');
    const result = await page.evaluate(() => { window.__flash.done = true; return { frames: window.__flash.frames, tasks: window.__flash.tasks, capped: window.__flash.capped }; });
    if (result.capped) throw new Error('the frame watch reached its 600-frame cap while the diagram was still drawing: the count would miss late frames');

    const shownFrames = result.frames.filter((f) => f.shown > 0);
    const raw = shownFrames.filter((f) => f.untagged || f.rawNotes || f.rawBars);
    const summary = {
      file, theme, trigger, premise,
      framesWatched: result.frames.length, framesShowingNewPictures: shownFrames.length,
      framesWithRawPaint: raw.length, firstRawFrame: raw[0] || null,
      tasksEndingWithAnUntaggedPictureShown: result.tasks.filter((s) => s.untagged).length,
      worst: raw.reduce((m, f) => (f.rawNotes + f.rawBars > (m ? m.rawNotes + m.rawBars : -1) ? f : m), null),
      screencastFrames: shots.length
    };
    const stem = path.join(outDir, `i2-${theme}-${trigger}`);
    write(stem + '.json', { summary, frames: result.frames, tasks: result.tasks });
    fs.mkdirSync(stem, { recursive: true });
    shots.slice(0, 60).forEach((s, i) => fs.writeFileSync(path.join(stem, `f${String(i).padStart(2, '0')}.jpg`), Buffer.from(s.data, 'base64')));
    console.log(JSON.stringify(summary, null, 2));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
