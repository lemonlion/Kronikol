// Runs report-focus-script.js against a small fake DOM and a fake clock, with no browser, for the paths a browser test
// cannot pin to the millisecond: which focus counts as the keyboard's (Tab only, the flag down again before any timer),
// the re-checks at 150, 400 and 800 ms and on every scroll, the end of the watch at 1 s or on a wheel, a touch, a pointer,
// a key or focus moving on, the pixel's grace, and the band: the pinned bar, the bar at rest, a scenario's open settings
// stuck under it, a control taller than the band, the bar's own controls, open settings and anything fixed, a phone.
// The layout is a page of boxes in document space: a box is in the flow, fixed (at a place in the window) or sticky
// (stuck at a line, inside its containing box); a box inside a sticky or fixed one moves with it.
// usage: node focus-script-check.js <report-focus-script.js> [--probe]   (--probe also runs the script's mutations)
'use strict';
const fs = require('fs');
const assert = require('assert');
const source = fs.readFileSync(process.argv[2], 'utf8');

function world(code, opts) {
    opts = Object.assign({ height: 900, sticky: true }, opts || {});
    // ---- a clock: timers and frames, run in time order (a frame's scroll events before its callbacks) ----
    let now = 0, seq = 0;
    const tasks = [];
    // rank: a frame's scroll events (0) before its callbacks (1); timers (0) in the order they were set
    function schedule(at, fn, rank) { const t = { at, fn, seq: seq++, rank: rank || 0, off: false }; tasks.push(t); return t; }
    function advance(ms) {
        const end = now + ms;
        for (;;) {
            tasks.sort((a, b) => a.at - b.at || a.rank - b.rank || a.seq - b.seq);
            const next = tasks.find(t => !t.off && t.at <= end);
            if (!next) break;
            now = next.at;
            next.off = true;
            tasks.splice(tasks.indexOf(next), 1);
            next.fn();
        }
        now = end;
    }
    const nextFrame = () => (Math.floor(now / 16) + 1) * 16;

    // ---- boxes ----
    let scrollY = 0;
    const all = [];
    class Box {
        constructor(name, o) {
            Object.assign(this, { name, nodeType: 1, parentElement: null, children: [], classes: [], position: 'static', top: 0, height: 20, line: 0, listeners: {} }, o);
            this.order = all.length;
            all.push(this);
        }
        add(child) { child.parentElement = this; this.children.push(child); return child; }
        contains(n) { for (let x = n; x; x = x.parentElement) if (x === this) return true; return false; }
        closest(sel) {
            if (sel !== '.scenario-settings.is-open') throw new Error('selector ' + sel);
            for (let x = this; x; x = x.parentElement) if (x.classes.includes('scenario-settings') && x.classes.includes('is-open')) return x;
            return null;
        }
        compareDocumentPosition(other) { return other.order > this.order && !this.contains(other) ? 4 : other.order < this.order ? 2 : 0; }
        // where the box is in the window
        place() {
            const holder = (() => { for (let x = this.parentElement; x; x = x.parentElement) if (x.position !== 'static') return x; return null; })();
            if (this.position === 'fixed') return this.top;
            if (this.position === 'sticky') {
                const flow = this.top - scrollY, box = this.parentElement;
                const limit = box ? box.top + box.height - this.height - scrollY : Infinity;
                return Math.min(Math.max(flow, this.line), limit);
            }
            if (holder) return holder.place() + (this.top - holder.top);
            return this.top - scrollY;
        }
        getBoundingClientRect() { const top = this.place(); return { top, bottom: top + this.height, height: this.height, left: 0, right: 100, width: 100 }; }
        addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
        removeEventListener(type, fn) { const l = this.listeners[type] || []; const i = l.indexOf(fn); if (i >= 0) l.splice(i, 1); }
        fire(type) { (this.listeners[type] || []).slice().forEach(f => f({ target: this, type })); }
    }

    const body = new Box('body', { top: 0, height: 6000 });
    const mark = body.add(new Box('mark', { top: 300, height: 0 }));
    const nav = body.add(new Box('nav', { top: 300, height: 135, position: opts.sticky ? 'sticky' : 'static', line: 0, classes: ['toolbar-row', 'report-nav'] }));
    const inBar = nav.add(new Box('in the bar', { top: 310, height: 28 }));
    const section = body.add(new Box('section', { top: 1000, height: 900 }));
    const summary = section.add(new Box('section summary', { top: 1000, height: 30 }));
    const settings = section.add(new Box('settings', { top: 1030, height: 40, position: 'sticky', line: 135, classes: ['scenario-settings'] }));
    const gear = settings.add(new Box('gear', { top: 1036, height: 28 }));
    const link = section.add(new Box('link under the settings', { top: 1120, height: 16 }));
    const control = body.add(new Box('control', { top: 2500, height: 40 }));
    const other = body.add(new Box('other control', { top: 2600, height: 40 }));
    const tall = body.add(new Box('tall control', { top: 3000, height: 1400 }));
    const above = body.add(new Box('above the bar', { top: 200, height: 30 }));
    const popup = body.add(new Box('popup', { top: 60, height: 300, position: 'fixed' }));
    const inPopup = popup.add(new Box('in the popup', { top: 70, height: 28 }));
    // `above` sits before the bar in the page
    above.order = mark.order - 0.5;

    // ---- the document and the window ----
    const winListeners = {}, docListeners = {};
    function on(map, type, fn, o) { (map[type] = map[type] || []).push({ fn, capture: o === true || !!(o && o.capture) }); }
    function off(map, type, fn, o) {
        const capture = o === true || !!(o && o.capture), l = map[type] || [];
        const i = l.findIndex(x => x.fn === fn && x.capture === capture);
        if (i >= 0) l.splice(i, 1);
    }
    function fireOn(map, type, ev) { (map[type] || []).slice().forEach(x => x.fn(ev)); }
    const document = {
        body, documentElement: { nodeType: 1 }, activeElement: body,
        querySelector(sel) {
            if (sel === '.toolbar-row.report-nav') return opts.noBar ? null : nav;
            if (sel === '.nav-mark') return mark;
            throw new Error('selector ' + sel);
        },
        querySelectorAll(sel) {
            if (sel === '.scenario-settings.is-open') return all.filter(b => b.classes.includes('scenario-settings') && b.classes.includes('is-open'));
            throw new Error('selector ' + sel);
        },
        addEventListener: (t, f, o) => on(docListeners, t, f, o)
    };
    let scrolls = 0;
    const window = {
        get innerHeight() { return opts.height; },
        addEventListener: (t, f, o) => on(winListeners, t, f, o),
        removeEventListener: (t, f, o) => off(winListeners, t, f, o),
        scrollBy(x, y) {
            const was = scrollY;
            scrollY = Math.max(0, Math.min(body.height - opts.height, scrollY + y));
            if (scrollY !== was) { scrolls++; schedule(nextFrame(), () => fireOn(winListeners, 'scroll', { type: 'scroll' }), 0); }
        }
    };
    const getComputedStyle = b => ({ position: b.position || 'static', top: (b.line || 0) + 'px' });
    const g = {
        document, window, getComputedStyle, Node: { DOCUMENT_POSITION_FOLLOWING: 4 },
        requestAnimationFrame: fn => schedule(nextFrame(), fn, 1),
        cancelAnimationFrame: t => { if (t) t.off = true; },
        setTimeout: (fn, ms) => schedule(now + (ms || 0), fn),
        clearTimeout: t => { if (t) t.off = true; }
    };
    new Function('document', 'window', 'getComputedStyle', 'Node', 'requestAnimationFrame', 'cancelAnimationFrame', 'setTimeout', 'clearTimeout', code)
        (g.document, g.window, g.getComputedStyle, g.Node, g.requestAnimationFrame, g.cancelAnimationFrame, g.setTimeout, g.clearTimeout);

    function focus(b) {
        const old = document.activeElement;
        if (old === b) return;
        document.activeElement = b;
        if (old && old.fire) old.fire('focusout');
        fireOn(docListeners, 'focusin', { target: b, type: 'focusin' });
    }
    // focus leaves for nothing (the page): focusout, and no focusin
    function blur() {
        const old = document.activeElement;
        document.activeElement = body;
        if (old && old.fire) old.fire('focusout');
    }
    const key = k => fireOn(winListeners, 'keydown', { key: k, type: 'keydown' });
    return {
        nav, inBar, mark, section, summary, settings, gear, link, control, other, tall, above, popup, inPopup,
        advance, focus, blur,
        // Tab: the key is handled, then focus moves while it is (the browser's own scroll, when it makes one, follows)
        tab(b, browserScroll) { key('Tab'); focus(b); if (browserScroll) window.scrollBy(0, browserScroll); },
        key, click(b) { fireOn(winListeners, 'pointerdown', { type: 'pointerdown' }); focus(b); },
        wheel() { fireOn(winListeners, 'wheel', { type: 'wheel' }); },
        touch() { fireOn(winListeners, 'touchstart', { type: 'touchstart' }); },
        scrollTo(y) { scrollY = y; },
        // the page scrolls by itself (anchoring, a script): a scroll event at the next frame
        pageScroll(dy) { window.scrollBy(0, dy); },
        get scrollY() { return scrollY; },
        get scrolls() { return scrolls; },
        rect: b => b.getBoundingClientRect(),
        openSettings(open) { settings.classes = open ? ['scenario-settings', 'is-open'] : ['scenario-settings']; },
        height: opts.height
    };
}

// pins the bar: the page scrolled so that `b`'s top is at `y` in the window, the bar's place gone past the top
function at(w, b, y) { w.scrollTo(b.top - y); assert.ok(w.rect(w.mark).top < 0, 'the bar is pinned'); }
const near = (a, b, msg) => assert.ok(Math.abs(a - b) < 0.01, msg + ' (' + a + ' against ' + b + ')');

const cases = [];
function check(name, fn) { cases.push({ name, fn }); }

check('Tab to a control half under the pinned bar brings it 12 px below the bar', code => {
    const w = world(code);
    at(w, w.control, 120);
    w.tab(w.control);
    w.advance(20);
    near(w.rect(w.control).top, w.rect(w.nav).bottom + 12, 'just below the bar');
});
check('Tab to a control past the foot of the window brings it 12 px above the foot', code => {
    const w = world(code);
    at(w, w.control, w.height - 10);
    w.tab(w.control);
    w.advance(20);
    near(w.rect(w.control).bottom, w.height - 12, 'just above the foot');
});
check('Tab to a control in the band moves nothing', code => {
    const w = world(code);
    at(w, w.control, 400);
    const y = w.scrollY;
    w.tab(w.control);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y);
});
check('the browser\'s own scroll lands first: the frame reads where it put the control', code => {
    const w = world(code);
    at(w, w.control, 100);
    // the browser scrolls it to 147 (its scroll margin), as Chrome does: nothing more to do
    w.tab(w.control, -47);
    w.advance(1200);
    near(w.rect(w.control).top, 147, 'where the browser put it');
    assert.strictEqual(w.scrolls, 1, 'the browser\'s scroll only');
});
check('focus a click gives is left where the reader pointed', code => {
    const w = world(code);
    at(w, w.control, 120);
    const y = w.scrollY;
    w.click(w.control);
    w.advance(1500);
    assert.strictEqual(w.scrollY, y);
});
check('focus a script gives is left where the script put it, however soon after a key', code => {
    const w = world(code);
    at(w, w.control, 120);
    const y = w.scrollY;
    w.focus(w.control);
    w.advance(1500);
    assert.strictEqual(w.scrollY, y, 'no key at all');
    w.focus(w.other);
    w.key('Tab');
    w.advance(1);
    w.focus(w.control);
    w.advance(1500);
    assert.strictEqual(w.scrollY, y, 'a Tab handled before: its flag is down by the next task');
    w.key('Enter');
    w.focus(w.other);
    w.focus(w.control);
    w.advance(1500);
    assert.strictEqual(w.scrollY, y, 'a key other than Tab');
});
check('a control in the bar, in open settings, or in anything fixed is left alone', code => {
    const w = world(code);
    at(w, w.control, 400);
    const y = w.scrollY;
    w.tab(w.inBar);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y, 'in the bar');
    w.tab(w.inPopup);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y, 'in the popup');
    w.openSettings(true);
    at(w, w.link, 135 + 40 - 6);
    const z = w.scrollY;
    near(w.rect(w.settings).top, 135, 'the settings stick under the bar');
    w.tab(w.gear);
    w.advance(1200);
    assert.strictEqual(w.scrollY, z, 'in the open settings');
});
check('a control taller than the band that shows in it is left alone; one that does not show is brought down', code => {
    const w = world(code);
    at(w, w.tall, 115);
    const y = w.scrollY;
    w.tab(w.tall);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y, 'it shows');
    w.focus(w.control);
    // the same control with only 15 px of it below the bar: under 24 px shows, so its top comes down
    at(w, w.tall, 135 + 15 - 1400);
    w.tab(w.tall);
    w.advance(20);
    near(w.rect(w.tall).top, 135 + 12, 'its top just below the bar');
});
check('the bar at rest covers nothing: a control above it stays, one cut by the window\'s top comes 12 px in', code => {
    const w = world(code);
    w.scrollTo(100);
    assert.ok(w.rect(w.mark).top > 0, 'at rest');
    w.tab(w.above);
    w.advance(1200);
    assert.strictEqual(w.scrollY, 100, 'a control above the bar at rest');
    w.focus(w.other);
    w.scrollTo(205);
    w.tab(w.above);
    w.advance(20);
    near(w.rect(w.above).top, 12, 'brought 12 px into the window');
});
check('on a phone the bar is in the page and nothing moves', code => {
    const w = world(code, { sticky: false });
    w.scrollTo(w.control.top - (w.height - 10));
    const y = w.scrollY;
    w.tab(w.control);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y);
});
check('a page without the bar moves nothing', code => {
    const w = world(code, { noBar: true });
    w.scrollTo(w.control.top - (w.height - 10));
    const y = w.scrollY;
    w.tab(w.control);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y);
});
check('open settings stuck under the bar: a control after them comes 12 px below them', code => {
    const w = world(code);
    w.openSettings(true);
    at(w, w.link, 135 + 40 - 6);
    near(w.rect(w.settings).top, 135, 'stuck');
    w.tab(w.link);
    w.advance(20);
    near(w.rect(w.link).top, w.rect(w.settings).bottom + 12, 'below the settings');
    near(w.rect(w.settings).top, 135, 'still stuck');
});
check('open settings pushed up by the end of their section move with the page: a gear under the bar comes out', code => {
    const w = world(code);
    w.openSettings(true);
    // the section's foot 30 px below the bar: the settings, 40 px tall, are pushed 10 px above their line
    w.scrollTo(w.section.top + w.section.height - 135 - 30);
    assert.ok(w.rect(w.mark).top < 0, 'the bar is pinned');
    assert.ok(w.rect(w.settings).top < 135 - 1, 'pushed above their line');
    assert.ok(w.rect(w.gear).top < 135, 'the gear starts under the bar');
    w.tab(w.gear);
    w.advance(1200);
    assert.ok(w.rect(w.gear).top >= 135, 'the gear is out from under the bar (' + w.rect(w.gear).top + ')');
    near(w.rect(w.settings).top, 135, 'the settings stick at their line again');
});
check('settings in the page, or closed, or before the control in the page, count for nothing', code => {
    const w = world(code);
    w.openSettings(true);
    // in the page, mid-window: they cover nothing, so the control 5 px below them is in the band
    w.link.top = w.settings.top + 45;
    at(w, w.settings, 400);
    let y = w.scrollY;
    w.tab(w.link);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y, 'settings in the page');
    w.link.top = 1120;
    // stuck, but the control comes before them: the section's summary, 20 px below the bar
    w.focus(w.other);
    w.scrollTo(w.settings.top - 135 + 50);
    w.summary.top = w.scrollY + 135 + 20;
    y = w.scrollY;
    w.tab(w.summary);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y, 'a control before the settings');
    // closed: not sticky in the page's sense (no open bar), the bar's band alone
    w.summary.top = 1000;
    w.openSettings(false);
    w.focus(w.other);
    at(w, w.link, 135 + 30);
    y = w.scrollY;
    w.tab(w.link);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y, 'closed settings');
});
check('a pixel\'s grace: the scroll margin\'s rounded bar height leaves no nudge', code => {
    const w = world(code);
    w.nav.height = 135.4;
    at(w, w.control, 147);
    const y = w.scrollY;
    w.tab(w.control);
    w.advance(1200);
    assert.strictEqual(w.scrollY, y);
});
check('for a second, a control the page moves is checked at 150, 400 and 800 ms', code => {
    for (const [shiftAt, caughtBy] of [[100, 150], [200, 400], [500, 800]]) {
        const w = world(code);
        at(w, w.control, 400);
        w.tab(w.control);
        w.advance(shiftAt);
        w.control.top -= 300;
        w.advance(caughtBy - shiftAt - 1);
        assert.ok(w.rect(w.control).top < 135, 'under the bar until ' + caughtBy + ' ms');
        w.advance(2);
        near(w.rect(w.control).top, w.rect(w.nav).bottom + 12, 'brought back at ' + caughtBy + ' ms');
    }
});
check('for a second, every scroll checks it; after the second, nothing does', code => {
    let w = world(code);
    at(w, w.control, 400);
    w.tab(w.control);
    w.advance(850);
    w.control.top -= 300;
    w.pageScroll(1);
    w.advance(40);
    near(w.rect(w.control).top, w.rect(w.nav).bottom + 12, 'a scroll at 850 ms');
    w = world(code);
    at(w, w.control, 400);
    w.tab(w.control);
    w.advance(1050);
    w.control.top -= 300;
    w.pageScroll(1);
    w.advance(500);
    assert.ok(w.rect(w.control).top < 135, 'a scroll after the second is the reader\'s');
});
check('the reader ends the watch: a wheel, a touch, a pointer, a key, or focus moving on', code => {
    // focus moving on: it leaves for the page, then a script gives it back, which is not the keyboard's focus
    for (const [name, act] of [['wheel', w => w.wheel()], ['touch', w => w.touch()], ['pointer', w => w.click(w.control)],
        ['key', w => w.key('PageDown')], ['focus moving on', w => { w.blur(); w.focus(w.control); }]]) {
        const w = world(code);
        at(w, w.control, 400);
        w.tab(w.control);
        w.advance(50);
        act(w);
        w.control.top -= 300;
        w.pageScroll(1);
        w.advance(1200);
        assert.ok(w.rect(w.control).top < 135, 'after the ' + name + ', nothing is brought back');
    }
});
check('a new Tab ends the last watch and starts its own', code => {
    const w = world(code);
    at(w, w.control, 400);
    w.tab(w.control);
    w.advance(50);
    w.tab(w.other);
    w.advance(50);
    // the first control under the bar: its watch is over (the second's checks the second only)
    w.control.top -= 300;
    w.advance(100);
    assert.ok(w.rect(w.control).top < 135, 'the first control is no longer watched');
    // the second under the bar, caught by its own 400 ms check (set at 50 ms)
    w.other.top -= 600;
    w.advance(260);
    near(w.rect(w.other).top, w.rect(w.nav).bottom + 12, 'the second is');
});

function runAll(code) {
    const failed = [];
    for (const c of cases) {
        try { c.fn(code); } catch (e) { failed.push(c.name + ': ' + e.message); }
    }
    return failed;
}

const failed = runAll(source);
failed.forEach(f => console.log('FAILED ' + f));
console.log((cases.length - failed.length) + ' of ' + cases.length + ' checks hold');
if (failed.length) process.exit(1);

if (process.argv.includes('--probe')) {
    // each probe undoes one behaviour; the checks must catch every one
    const probes = [
        ['every scroll checks again', "window.addEventListener('scroll', watch, { passive: true });", ''],
        ['a key ends the watch', "var ENDS = ['wheel', 'touchstart', 'pointerdown', 'keydown'];", "var ENDS = ['wheel', 'touchstart', 'pointerdown'];"],
        ['a pointer ends the watch', "var ENDS = ['wheel', 'touchstart', 'pointerdown', 'keydown'];", "var ENDS = ['wheel', 'touchstart', 'keydown'];"],
        ['the watch ends at 1 s', 'timers.push(setTimeout(stop, 1000));', ''],
        ['focus moving on ends the watch', "t.addEventListener('focusout', stop);", ''],
        ['a control taller than the band that shows stays', 'if (r.height > bottom - top && r.bottom > top + 24 && r.top < bottom - 24) return;', ''],
        ['anything fixed stays', "if (getComputedStyle(n).position === 'fixed') return true;", 'if (false) return true;'],
        ['a phone moves nothing', "function sticks(nav) { var p = getComputedStyle(nav).position; return p === 'sticky' || p === 'fixed'; }", 'function sticks(nav) { return true; }'],
        ['a pixel\'s grace', 'if (r.top < top - 1) window.scrollBy(0, r.top - top);', 'if (r.top < top) window.scrollBy(0, r.top - top);'],
        ['the Tab flag goes down', 'setTimeout(function () { tabbing = false; }, 0);', ''],
        ['Tab alone raises the flag', "if (e.key !== 'Tab') return;", ''],
        ['the bar\'s own controls stay', 'if (nav.contains(t)) return true;', ''],
        ['settings count for a control after them only', 'if (s.contains(t) || !(s.compareDocumentPosition(t) & Node.DOCUMENT_POSITION_FOLLOWING)) continue;', 'if (s.contains(t)) continue;'],
        ['settings count while stuck only', 'if (r.height && r.top <= top + 1 && r.bottom > top) top = r.bottom;', 'if (r.height) top = Math.max(top, r.bottom);'],
        ['open settings stay only while they stick at their line', "if (s && Math.abs(s.getBoundingClientRect().top - (parseFloat(getComputedStyle(s).top) || 0)) <= 1) return true;", 'if (s) return true;'],
        ['the bar covers only while pinned','var top = mark && mark.getBoundingClientRect().top < line - 0.5 ? nav.getBoundingClientRect().bottom : 0;', 'var top = nav.getBoundingClientRect().bottom;'],
    ];
    let caught = 0;
    for (const [name, from, to] of probes) {
        const n = source.split(from).length - 1;
        if (n !== 1) { console.log('PROBE ANCHOR NOT ONCE (' + n + '): ' + name); continue; }
        const f = runAll(source.replace(from, to));
        if (f.length) caught++;
        console.log((f.length ? 'caught   ' : 'MISSED   ') + name + (f.length ? ' (' + f.length + ' checks fail)' : ''));
    }
    console.log(caught + ' of ' + probes.length + ' probes caught');
    if (caught !== probes.length) process.exit(1);
}
