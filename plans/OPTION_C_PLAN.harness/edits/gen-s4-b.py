# S4, after gen-s4.py: a feature's name in the table as a button (a row opens its feature from the keyboard), the
# render script drawing only what shows when a tab or a view asks (shownOnly) and telling a closed <details> from a
# shown one (checkVisibility), and the scenario keys leaving a dropdown's arrow keys alone. Run from the worktree root.
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode('utf-8')
    assert s.count(old) == count, (path, s.count(old), old[:100])
    open(path, 'wb').write(s.replace(old, new).encode('utf-8'))

G = 'src/Kronikol/Reports/ReportGenerator.cs'
sub(G, '''                featureTable.Append($"<td>{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</td>");''',
       '''                // the name is a button: a row opens its feature in the Full view, from the keyboard as well
                featureTable.Append($"<td><button type=\\"button\\" class=\\"feature-open\\">{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</button></td>");''')

R = 'src/Kronikol/Reports/plantuml-browser-render-script.js'
sub(R, '''        window._renderDiagramsInContainer = function(container) {
            if (!container) return;
            container.querySelectorAll('.plantuml-browser').forEach(function(el) {
                if (el.dataset.queued) return;
                if (_onDemand && !isShown(el)) return;''', '''        // shownOnly (5.0.0): draw only what shows, as a page that draws on demand always does. A tab or a view coming
        // into sight asks for it: drawing everything it holds would queue every diagram in every closed scenario.
        window._renderDiagramsInContainer = function(container, shownOnly) {
            if (!container) return;
            container.querySelectorAll('.plantuml-browser').forEach(function(el) {
                if (el.dataset.queued) return;
                if ((_onDemand || shownOnly) && !isShown(el)) return;''')
sub(R, "        function isShown(el) { return el.getClientRects().length > 0; }", """        // checkVisibility first: Chrome hides a closed <details>' content with content-visibility, where getClientRects
        // still reports a box, so a diagram in a closed scenario read as shown (5.0.0)
        function isShown(el) { return typeof el.checkVisibility === 'function' ? el.checkVisibility() : el.getClientRects().length > 0; }""")

T = 'src/Kronikol/Reports/report-tabs-script.js'
sub(T, '''        // a diagram in a panel that was hidden is drawn when the panel shows (the engine is not fetched for a hidden tab)
        if (typeof window._renderDiagramsInContainer === 'function') window._renderDiagramsInContainer(target);''',
       '''        // a diagram in a panel that was hidden is drawn when the panel shows (the engine is not fetched for a hidden tab),
        // and only one that shows: the Features tab holds every scenario's diagrams, most of them in closed scenarios
        if (typeof window._renderDiagramsInContainer === 'function') window._renderDiagramsInContainer(target, true);''')

K = 'src/Kronikol/Reports/report-keyboard-navigation-function.js'
sub(K, '''    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;''',
       '''    // a field keeps its keys: until 5.0.0 a dropdown's arrow keys moved between scenarios instead of changing its value
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;''')
print('ok')
