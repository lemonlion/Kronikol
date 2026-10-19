# S9a, step 1: one model for the diagram filters (F5). A scenario's toggle records its choice on the scenario, the
# report's toggle clears every scenario's choice, and every reader resolves a diagram's state as its own, else its
# scenario's choice, else the report's. usage: python scripts-s9-a.py   (from anywhere)
ROOT = 'C:/Code/Kronikol-v5/'
P = ROOT + 'src/Kronikol/Reports/collapsible-notes-script.js'

s = open(P, encoding='utf-8', newline='').read()
assert '\r\n' not in s


def once(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


once("""    // A container's own filter state, or the report's for one drawn without any (the pre-processor never saw it).
    // Every reader resolves a missing flag this one way: the Databases toggle read it as shown and every other
    // reader as hidden, so changing such a diagram's notes hid its databases and steps (4.26.8).
    function filterShown(container, flag) {
        return container[flag] === undefined ? !!window[flag] : !!container[flag];
    }""",
     """    // A container's own filter state, else its scenario's own choice, else the report's (one drawn without any state:
    // the pre-processor never saw it). Every reader resolves a missing flag this one way: the Databases toggle read it
    // as shown and every other reader as hidden, so changing such a diagram's notes hid its databases and steps
    // (4.26.8).
    function filterShown(container, flag) {
        return container[flag] === undefined ? scenarioChoice(container, flag) : !!container[flag];
    }

    // The state a diagram takes when it is drawn: its scenario's own choice where the scenario's toggle made one, else
    // the report's (5.0.0, plans/OPTION_C_PLAN.md F5). A diagram is given its source only when it is drawn, so a
    // scenario's toggle reaches the diagrams already drawn; until 5.0.0 one drawn after it took the report's state.
    function scenarioChoice(el, flag) {
        var scenario = el && el.closest ? el.closest('details.scenario') : null;
        return scenario && scenario[flag] !== undefined ? !!scenario[flag] : !!window[flag];
    }""")

once("""        el._assertionsVisible = window._assertionsVisible;
        el._stepsVisible = window._stepsVisible;
        el._databasesVisible = window._databasesVisible;""",
     """        el._assertionsVisible = scenarioChoice(el, '_assertionsVisible');
        el._stepsVisible = scenarioChoice(el, '_stepsVisible');
        el._databasesVisible = scenarioChoice(el, '_databasesVisible');""")

MODEL = """    // One model for every diagram filter (5.0.0, F5): the report's toggle sets the report's state and clears every
    // scenario's own choice, as the report's choice holds everywhere; a scenario's toggle records its choice on the
    // scenario, so the diagrams it draws later take it (scenarioChoice).
    function toggleReportFilter(name, flag, btn, buildQueue) {
        var shown = btn.getAttribute('data-shown') !== 'true';
        window[flag] = shown;
        syncToggleBtn(document.querySelector('.toolbar-right'), name, shown);
        document.querySelectorAll('details.scenario').forEach(function(sc) {
            sc[flag] = undefined;
            syncToggleBtn(sc, name, shown);
        });
        renderWithPending(buildQueue(document.querySelectorAll('[data-plantuml]'), shown),
            document.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]'));
    }

    function toggleScenarioFilter(name, flag, btn, buildQueue) {
        var shown = btn.getAttribute('data-shown') !== 'true';
        var scenario = btn.closest('details.scenario');
        if (!scenario) return;
        scenario[flag] = shown;
        syncToggleBtn(scenario, name, shown);
        renderWithPending(buildQueue(scenario.querySelectorAll('[data-plantuml]'), shown),
            scenario.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]'));
    }

"""

first = True
for N in ['Assertions', 'Steps', 'Databases']:
    n = N.lower()
    report = f"""    window._toggle{N} = function(btn) {{
        var shown = btn.getAttribute('data-shown') !== 'true';
        window._{n}Visible = shown;
        syncToggleBtn(document.querySelector('.toolbar-right'), '{n}', shown);
        document.querySelectorAll('details.scenario').forEach(function(sc) {{
            syncToggleBtn(sc, '{n}', shown);
        }});
        var containers = document.querySelectorAll('[data-plantuml]');
        renderWithPending(build{N}Queue(containers, shown), document.querySelectorAll('.toggle-btn[data-toggle="{n}"]'));
    }};"""
    scenario = f"""    window._toggleScenario{N} = function(btn) {{
        var shown = btn.getAttribute('data-shown') !== 'true';
        var scenario = btn.closest('details.scenario');
        if (!scenario) return;
        syncToggleBtn(scenario, '{n}', shown);
        var containers = scenario.querySelectorAll('[data-plantuml]');
        renderWithPending(build{N}Queue(containers, shown), scenario.querySelectorAll('.toggle-btn[data-toggle="{n}"]'));
    }};"""
    newReport = f"    window._toggle{N} = function(btn) {{ toggleReportFilter('{n}', '_{n}Visible', btn, build{N}Queue); }};"
    newScenario = f"    window._toggleScenario{N} = function(btn) {{ toggleScenarioFilter('{n}', '_{n}Visible', btn, build{N}Queue); }};"
    # the model's two functions go before the first report toggle, with its comment
    once(report, (MODEL if first else '') + newReport)
    once(scenario, newScenario)
    first = False

open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
