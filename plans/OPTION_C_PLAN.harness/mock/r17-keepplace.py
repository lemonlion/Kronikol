# Round 17 item 9: a note made shorter from its lower part keeps the reader's place. Post-processes
# c-round17-buttons.js (run after r17-make-buttons.py; idempotent).
import io

p = 'c-round17-buttons.js'
s = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in s else '\n'
if 'function keepPlace' in s:
    print('already patched')
    raise SystemExit

helper = """        var buttons = [];
        // Round 17 (owner, item 9): a note made shorter from its lower part (its bottom buttons, or a click low in a
        // tall note) pulled everything below it up past the window, so the reader landed somewhere else, often in
        // another scenario. Once the diagram is drawn again, the note's new bottom is brought to where the click was.
        function keepPlace(fn, ev) {
            var y = ev && typeof ev.clientY === 'number' ? ev.clientY : null;
            var rects = Array.prototype.slice.call(container.querySelectorAll('svg .note-hover-rect'));
            var idx = -1;
            for (var i = 0; i < rects.length; i++) {
                var b = rects[i].getBBox();
                if (bbox.x + 1 >= b.x - 1 && bbox.x + 1 <= b.x + b.width + 1 && bbox.y + 1 >= b.y - 1 && bbox.y + 1 <= b.y + b.height + 1) { idx = i; break; }
            }
            var before = idx >= 0 ? rects[idx].getBoundingClientRect() : null;
            var oldSvg = container.querySelector('svg');
            fn();
            if (!before || y === null || y < before.top + before.height / 2) return;
            var t0 = Date.now();
            (function wait() {
                var now = container.querySelector('svg');
                var ready = now && now !== oldSvg && !container._noteRendering && now.querySelector('.note-hover-rect');
                if (!ready) { if (Date.now() - t0 < 10000) setTimeout(wait, 50); return; }
                var r = container.querySelectorAll('svg .note-hover-rect')[idx];
                if (!r) return;
                var rb = r.getBoundingClientRect();
                if (rb.height >= before.height - 1) return;
                var want = Math.min(y + 8, window.innerHeight - 24);
                if (Math.abs(rb.bottom - want) > 1) window.scrollBy(0, rb.bottom - want);
            })();
        }"""


def rep(old, new, cnt):
    global s
    old = old.replace('\n', nl)
    new = new.replace('\n', nl)
    assert s.count(old) == cnt, (s.count(old), old[:70])
    s = s.replace(old, new)


rep('        var buttons = [];', helper, 1)
rep('ev.stopPropagation(); onContract(); });', 'ev.stopPropagation(); keepPlace(onContract, ev); });', 2)
rep('ev.stopPropagation(); onTruncate(); });', 'ev.stopPropagation(); keepPlace(onTruncate, ev); });', 2)
rep('ev.stopPropagation(); ev.preventDefault(); onCycle();', 'ev.stopPropagation(); ev.preventDefault(); keepPlace(onCycle, ev);', 2)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
