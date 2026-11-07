() => {
    const frames = (n) => new Promise((done) => { const t = () => (--n <= 0 ? setTimeout(done, 0) : requestAnimationFrame(t)); requestAnimationFrame(t); });
    // a scenario's diagram by the scenario's index, or a diagram by a selector
    const boxOf = (i) => typeof i === 'number' ? document.querySelectorAll('details.scenario')[i].querySelector('[data-diagram-type="plantuml"]')
        : document.querySelector(i);
    // the texts of the pictures a diagram shows: its own, or its drawn parts' (never one drawn out of sight)
    const textsOf = (c) => Array.from(c.querySelectorAll(':scope > svg text, :scope > .puml-fragment:not(.puml-fragment-new) > svg text'));
    const middleOf = (el) => { const r = el.getBoundingClientRect(); return r.height ? (r.top + r.bottom) / 2 : null; };
    // the middle of the window below the bar and any settings stuck under it
    const readingLine = () => {
        const t = document.elementFromPoint(innerWidth / 2, innerHeight / 2);
        return ((t && window.kronikol_freeTop ? window.kronikol_freeTop(t) : 0) + innerHeight) / 2;
    };
    const movedNow = () => {
        const m = window.__mark, same = textsOf(boxOf(m.i)).filter((t) => t.textContent === m.words);
        const y = same[m.k] ? middleOf(same[m.k]) : null;
        return y === null ? null : y - m.y0;
    };
    window.__k227 = {
        async zoom(i, level) {
            const c = boxOf(i);
            c.scrollIntoView({ block: 'center' });
            for (let n = 0; n < 150 && !c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider'); n++) await frames(2);
            const s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            if (!s) return null;
            s.value = level === 'max' ? '100' : String(Math.round((parseInt(s.min) + 100) / 2));
            s.dispatchEvent(new Event('input', { bubbles: true }));
            await frames(3);
            return { level: parseInt(s.value), min: parseInt(s.min), zoomedIn: c.classList.contains('diagram-natural-size'),
                zoomedOut: c.classList.contains('diagram-zoomed-out') };
        },
        // where: mid (the diagram across the window's middle), top (its top 80 px below the bar), deep:<px> (the
        // window's middle <px> down it), gap (the window's top edge 20 px below the diagram above, in that
        // scenario's foot), cut (the window's top edge 40 px above the bottom of the picture above); placed again
        // until it holds, as the page lays out late around a scroll (content-visibility)
        async place(i, where) {
            const c = boxOf(i), all = Array.from(document.querySelectorAll('[data-diagram-type="plantuml"]'));
            const prev = all[all.indexOf(c) - 1] || null, nav = document.querySelector('.toolbar-row.report-nav');
            c.scrollIntoView({ block: 'center' });
            await frames(4);
            const byPrev = where === 'gap' || where === 'cut';
            if (byPrev && prev) { prev.scrollIntoView({ block: 'end' }); await frames(4); }
            const off = () => {
                const r = c.getBoundingClientRect(), bar = nav ? nav.getBoundingClientRect().bottom : 0;
                if (where === 'top') return r.top - (bar + 80);
                if (where.startsWith('deep:')) return r.top + parseInt(where.slice(5), 10) - innerHeight / 2;
                if (where === 'gap') return prev.getBoundingClientRect().bottom + 20;
                if (where === 'cut') return prev.getBoundingClientRect().bottom - 40;
                return r.top + Math.min(r.height, innerHeight) / 2 - innerHeight / 2;
            };
            const consistent = () => !byPrev || prev.getBoundingClientRect().bottom <= c.getBoundingClientRect().top;
            // on a short page the window stops at the page's end: as near as it goes
            const stopped = (d) => d > 0 ? window.scrollY >= document.documentElement.scrollHeight - innerHeight - 1 : window.scrollY <= 1;
            for (let n = 0; n < 12; n++) {
                const d = off();
                if ((Math.abs(d) <= 1 || stopped(d)) && n > 0 && consistent()) return true;
                window.scrollBy(0, d);
                await frames(4);
            }
            return (Math.abs(off()) <= 1 || stopped(off())) && consistent();
        },
        // the reading line on a scenario's title row, which no diagram draws
        async placeOnScenario(i) {
            const s = document.querySelectorAll('details.scenario')[i].querySelector(':scope > summary');
            const off = () => { const r = s.getBoundingClientRect(); return (r.top + r.bottom) / 2 - readingLine(); };
            for (let n = 0; n < 12; n++) {
                if (Math.abs(off()) <= 1 && n > 0) return true;
                window.scrollBy(0, off());
                await frames(4);
            }
            return Math.abs(off()) <= 1;
        },
        mark(i) {
            const c = boxOf(i), y = readingLine(), texts = textsOf(c);
            let best = -1, bd = Infinity;
            texts.forEach((t, j) => { const m = middleOf(t); if (m !== null && Math.abs(m - y) < bd) { bd = Math.abs(m - y); best = j; } });
            if (best < 0) return null;
            const words = texts[best].textContent;
            // also the nearest text whose words the diagram draws once, which no other drawing of the same words can
            // stand in for when rows go away
            let once = null, od = Infinity;
            texts.forEach((t) => {
                const m = middleOf(t);
                if (m === null || Math.abs(m - y) >= od || texts.filter((u) => u.textContent === t.textContent).length !== 1) return;
                od = Math.abs(m - y); once = { words: t.textContent, y0: m, distance: od };
            });
            window.__mark = { i, words, k: texts.slice(0, best).filter((t) => t.textContent === words).length, y0: middleOf(texts[best]),
                once, old: c.querySelector('svg'), top0: c.getBoundingClientRect().top, frames: [] };
            window.__writes = [];
            const log = (what) => window.__writes.push({ what, stack: String(new Error().stack || '') });
            for (const name of ['scrollBy', 'scrollTo', 'scroll']) {
                const orig = window[name];
                window[name] = function () { log(name); return orig.apply(this, arguments); };
            }
            const st = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop');
            Object.defineProperty(Element.prototype, 'scrollTop', { configurable: true, get() { return st.get.call(this); },
                set(v) { if (this === document.documentElement || this === document.body) log('scrollTop'); st.set.call(this, v); } });
            const m = window.__mark;
            const tick = () => {
                if (window.__mark !== m) return;
                m.frames.push({ text: movedNow(), box: c.getBoundingClientRect().top - m.top0 });
                requestAnimationFrame(tick);
            };
            requestAnimationFrame(tick);
            return { words, distance: bd, line: y };
        },
        result() {
            const m = window.__mark, c = boxOf(m.i), r = c.getBoundingClientRect(), moved = movedNow();
            window.__mark = null;
            const zoomCode = (w) => /\bkeepPlace\b|\bapplyZoomLevel\b/.test(w.stack);
            const once = m.once ? textsOf(c).filter((t) => t.textContent === m.once.words) : [];
            return JSON.stringify({ Moved: moved, BoxMoved: r.top - m.top0, OnScreen: r.bottom > 0 && r.top < innerHeight,
                Redrawn: !document.contains(m.old), Words: m.words,
                Once: m.once ? m.once.words : null, OnceDistance: m.once ? m.once.distance : null,
                OnceMoved: once.length === 1 ? middleOf(once[0]) - m.once.y0 : null,
                ScrollY: window.scrollY, MaxScroll: document.documentElement.scrollHeight - innerHeight,
                FramesText: m.frames.map((f) => f.text), FramesBox: m.frames.map((f) => f.box),
                ZoomWrites: window.__writes.filter(zoomCode).map((w) => w.what + ' < ' + w.stack.split('\n').slice(2, 6).map((l) => l.trim()).join(' < ')) });
        }
    };
}