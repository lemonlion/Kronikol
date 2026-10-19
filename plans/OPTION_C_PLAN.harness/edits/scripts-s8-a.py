# S8 scripts: a toggle keeps its word and says whether it is on (D32); the note's width button speaks of the box
# (D37); the diagram tabs say which is selected, move with the arrow keys and step the settings aside off the
# sequence diagrams (D29, D33); the phone's settings button is written by the generator, on the section's title (D34);
# and Enter on a button presses it while the arrow keys have picked a scenario (a 4.x bug). Run from anywhere.
R = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/'

def edit(name, pairs):
    p = R + name
    s = open(p, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (name, s.count(old), old[:80])
        s = s.replace(old, new)
    open(p, 'w', encoding='utf-8', newline='').write(s)

edit('collapsible-notes-script.js', [
    (r'''    function syncToggleBtn(parent, toggleName, shown) {
        parent.querySelectorAll('.toggle-btn[data-toggle="' + toggleName + '"]').forEach(function(b) {
            b.setAttribute('data-shown', shown ? 'true' : 'false');
            if (shown) b.classList.add('details-active');
            else b.classList.remove('details-active');
            var label = toggleName.charAt(0).toUpperCase() + toggleName.slice(1);
            b.textContent = label + (shown ? ' Shown' : ' Hidden');
        });
    }''', r'''    // A toggle says its word alone (5.0.0): whether it is on is its state, aria-pressed, never its label, which stays
    // in the span the generator wrote (it holds the bold width, so switching moves nothing beside it).
    function syncToggleBtn(parent, toggleName, shown) {
        parent.querySelectorAll('.toggle-btn[data-toggle="' + toggleName + '"]').forEach(function(b) {
            b.setAttribute('data-shown', shown ? 'true' : 'false');
            b.setAttribute('aria-pressed', shown ? 'true' : 'false');
            if (shown) b.classList.add('details-active');
            else b.classList.remove('details-active');
        });
    }'''),
    (r'''appearanceInfo.width === 'full' ? 'Back to the default note width' : 'Widen this note to fill the diagram',''',
     r'''appearanceInfo.width === 'full' ? 'Back to the default width' : 'Widen this box to fill the diagram','''),
])

edit('toggle-script.js', [
    (r'''    var dtype = btn.getAttribute('data-dtype');
    toggle.querySelectorAll('.diagram-toggle-btn').forEach(function(b) {
        b.classList.toggle('diagram-toggle-active', b === btn);
    });''', r'''    var dtype = btn.getAttribute('data-dtype');
    toggle.querySelectorAll('.diagram-toggle-btn').forEach(function(b) {
        var on = b === btn;
        b.classList.toggle('diagram-toggle-active', on);
        b.setAttribute('aria-selected', on ? 'true' : 'false');
        b.tabIndex = on ? 0 : -1;
    });
    // the scenario's settings act on its sequence diagrams, so they step aside while another view shows
    if (container.querySelector(':scope > .scenario-settings')) container.classList.toggle('diagrams-not-seq', dtype !== 'seq');'''),
    (r'''        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(target);
    }
});
</script>''', r'''        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(target);
    }
});
// The views are tabs: the arrow keys, Home and End move between them, showing each as they go.
document.addEventListener('keydown', function(e) {
    var btn = e.target.closest ? e.target.closest('.diagram-toggle-tabs > .diagram-toggle-btn') : null;
    if (!btn) return;
    var all = Array.prototype.slice.call(btn.parentElement.querySelectorAll(':scope > .diagram-toggle-btn'));
    var i = all.indexOf(btn), j = -1;
    if (e.key === 'ArrowRight') j = (i + 1) % all.length;
    else if (e.key === 'ArrowLeft') j = (i - 1 + all.length) % all.length;
    else if (e.key === 'Home') j = 0;
    else if (e.key === 'End') j = all.length - 1;
    if (j < 0) return;
    e.preventDefault();
    all[j].click();
    all[j].focus();
});
</script>'''),
])

edit('report-init-script.js', [
    (r'''    // #4 Per-scenario diagram controls toggle on mobile
    if (isMobile) {
        document.querySelectorAll('.diagram-toggle').forEach(function(controls) {
            controls.classList.add('scenario-diagram-controls');
            controls.style.display = 'none';
            var btn = document.createElement('button');
            btn.className = 'scenario-diagram-controls-toggle';
            btn.textContent = '\u2699 Diagram Settings';
            btn.style.display = 'block';
            controls.parentNode.insertBefore(btn, controls);
            btn.addEventListener('click', function() {
                var showing = controls.style.display === 'flex';
                controls.style.display = showing ? 'none' : 'flex';
                btn.textContent = showing ? '\u2699 Diagram Settings' : '\u2699 Hide Settings';
            });
        });
    }

''', ''),
])

edit('report-keyboard-navigation-function.js', [
    (r'''    } else if (e.key === 'Enter' && focused) {''',
     r'''    } else if (e.key === 'Enter' && focused && (e.target === document.body || e.target === document.documentElement)) {
        // only while nothing has the keyboard's focus: Enter on a button presses it (until 5.0.0 it folded the
        // scenario the arrow keys had picked, and the button did nothing)'''),
])
print('ok')
