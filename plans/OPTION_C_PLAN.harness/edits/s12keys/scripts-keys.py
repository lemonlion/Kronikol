# S12, found while running D48: the keyboard reaches an internal-flow link the browser engine draws. That engine writes
# no <a> for a link, so the render script's bindLink makes the link's first text its one Tab stop, a link named from all
# its texts, opening the popup on Enter or Space and, where links show only on hover, showing it on focus. A link the
# Java engine drew keeps its <a> as the stop. usage: python scripts-keys.py <worktree root>
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
P = ROOT + 'src/Kronikol/Reports/plantuml-browser-render-script.js'
s = open(P, encoding='utf-8', newline='').read()
assert '_iflowKeys' not in s, 'the keyboard links are in already'
old = '''                    textEl.addEventListener('click', function(ev) {
                        ev.preventDefault();
                        ev.stopPropagation();
                        if (window._iflowShowPopup) window._iflowShowPopup(segId);
                    });
                });
            }
'''
new = '''                    textEl.addEventListener('click', function(ev) {
                        ev.preventDefault();
                        ev.stopPropagation();
                        if (window._iflowShowPopup) window._iflowShowPopup(segId);
                    });
                });
                // The keyboard reaches the link too (5.0.0, S12): the browser engine draws no <a>, so the link's first
                // text is its one Tab stop, a link named from all its texts, and Enter or Space opens the popup; where
                // links show only on hover, focus shows it. A link the Java engine drew keeps its <a> as the stop.
                var first = groupEls[0], anchor = first.closest ? first.closest('a') : null;
                var anchored = !!anchor && (anchor.hasAttribute('href') || anchor.hasAttributeNS('http://www.w3.org/1999/xlink', 'href'));
                if (!anchored && !first._iflowKeys) {
                    first._iflowKeys = true;
                    first.setAttribute('tabindex', '0');
                    first.setAttribute('role', 'link');
                    first.setAttribute('aria-label', groupEls.map(function(el) { return el.textContent; }).join(' '));
                    first.addEventListener('keydown', function(ev) {
                        if (ev.key !== 'Enter' && ev.key !== ' ') return;
                        ev.preventDefault();
                        if (window._iflowShowPopup) window._iflowShowPopup(segId);
                    });
                    if (hoverOnly) {
                        first.addEventListener('focus', function() {
                            groupEls.forEach(function(el, k) {
                                el.setAttribute('fill', linkFills[k]);
                                el.setAttribute('text-decoration', 'underline');
                            });
                        });
                        first.addEventListener('blur', atRest);
                    }
                }
            }
'''
assert s.count(old) == 1, s.count(old)
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok: plantuml-browser-render-script.js')
