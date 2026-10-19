# S9a, step 3 (the page): the Events and Setup filters (D38, P4 to P8) on the one model, with one queue builder and one
# filter chain every reader applies in one order. stripSetup is the preview's (mock/c-round16-strips.js); the events
# filter is the databases filter with `queue` for its participants' keyword (P4's port note: parameterised, not a
# clone). usage: python scripts-s9-b.py
ROOT = 'C:/Code/Kronikol-v5/'
P = ROOT + 'src/Kronikol/Reports/collapsible-notes-script.js'
MOCK = ROOT + 'plans/OPTION_C_PLAN.harness/mock/c-round16-strips.js'

s = open(P, encoding='utf-8', newline='').read()
assert '\r\n' not in s


def once(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


# the start states
once("""    window._databasesVisible = __DATABASES_VISIBLE_DEFAULT__;
""", """    window._databasesVisible = __DATABASES_VISIBLE_DEFAULT__;
    window._eventsVisible = __EVENTS_VISIBLE_DEFAULT__;
    window._setupVisible = __SETUP_VISIBLE_DEFAULT__;
""")

# the databases filter, parameterised by its participants' keyword
start = s.index("    function stripDatabaseCalls(source) {")
end = s.index("        return result.join('\\n');\n    }\n", start) + len("        return result.join('\\n');\n    }\n")
body = s[start:end]
for old, new in [
    ("""    function stripDatabaseCalls(source) {
        // Find all database participant aliases
        var dbAliases = [];
        var dbDeclRe = /^(?:database|collections)\\s+"[^"]*"\\s+as\\s+(\\S+)/gm;""",
     """    // The databases filter: the calls to and from the participants declared database or collections.
    function stripDatabaseCalls(source) {
        return stripParticipantCalls(source, 'database|collections');
    }

    // The events filter (5.0.0, D38): the calls to and from message brokers, the participants declared queue (Kafka,
    // Event Hubs, Pub/Sub, Service Bus), cut as the databases filter cuts a database's.
    function stripEventCalls(source) {
        return stripParticipantCalls(source, 'queue');
    }

    // The calls to and from the participants declared as one of `kinds` (a regex alternation), with their
    // declarations, the notes that follow a removed call or name such a participant, and its activations.
    function stripParticipantCalls(source, kinds) {
        // Find all the aliases of participants of those kinds
        var aliases = [];
        var declRe = new RegExp('^(?:' + kinds + ')\\\\s+"[^"]*"\\\\s+as\\\\s+(\\\\S+)', 'gm');"""),
    ("""            return /^(?:database|collections)\\s+"[^"]*"\\s+as\\s+\\S+/.test(line);""",
     """            return new RegExp('^(?:' + kinds + ')\\\\s+"[^"]*"\\\\s+as\\\\s+\\\\S+').test(line);"""),
]:
    assert body.count(old) == 1, (old[:70], body.count(old))
    body = body.replace(old, new)
for old, new in [('dbAliases', 'aliases'), ('dbDeclRe', 'declRe'), ('isDbArrow', 'isCall'), ('isDbDecl', 'isDecl'),
                 ('isDbExplicitNote', 'isExplicitNote'), ('// Skip database declaration', '// Skip the declaration'),
                 ('// Skip database arrow and mark for trailing note removal', '// Skip the call and mark for trailing note removal'),
                 ('// Skip note block explicitly referencing database alias', '// Skip a note block naming the alias'),
                 ('// Remove positional notes that follow a removed database arrow', '// Remove positional notes that follow a removed call'),
                 ('// Activate/deactivate for database alias', '// Activate/deactivate for the alias')]:
    assert old in body, old
    body = body.replace(old, new)
assert 'atabase' not in body.split('function stripParticipantCalls')[1], 'a database word left in the generic filter'
s = s[:start] + body + s[end:]

# the setup filter, from the preview, and the chain every reader applies
mock = open(MOCK, encoding='utf-8', newline='').read()
setup = mock[mock.index('    function stripSetup(source) {'):]
assert setup.rstrip().endswith('}')
once("""    function applyDatabasesFilter(source, showing) {
        return showing ? source : stripDatabaseCalls(source);
    }
""", """    function applyDatabasesFilter(source, showing) {
        return showing ? source : stripDatabaseCalls(source);
    }

    function applyEventsFilter(source, showing) {
        return showing ? source : stripEventCalls(source);
    }

    function applySetupFilter(source, showing) {
        return showing ? source : stripSetup(source);
    }

    // Every filter, in the one order every reader applies them, each with the diagram's state (filterShown): the setup
    // first, as it is found by the When step bar the steps filter takes out.
    function applyFilters(source, container) {
        var s = applySetupFilter(source, filterShown(container, '_setupVisible'));
        s = applyAssertionFilter(s, filterShown(container, '_assertionsVisible'));
        s = applyStepsFilter(s, filterShown(container, '_stepsVisible'));
        s = applyDatabasesFilter(s, filterShown(container, '_databasesVisible'));
        return applyEventsFilter(s, filterShown(container, '_eventsVisible'));
    }

    // The setup filter (5.0.0, D38): a scenario's setup, taken out of its sequence diagram's source. The setup is the
    // Setup frame where the report keeps setup apart (partition ... Setup ... end, the SeparateSetup option), and
    // otherwise everything before the first When step: the Given steps and the calls they made. The participants only
    // the setup used go with it. A source with neither, or whose cut would leave a frame or a loop open, comes back as
    // it was. Note bodies are never read as statements.
""" + setup.rstrip() + "\n")

once("""        return applyDatabasesFilter(
            applyStepsFilter(
                applyAssertionFilter(built, filterShown(container, '_assertionsVisible')),
                filterShown(container, '_stepsVisible')),
            filterShown(container, '_databasesVisible'));""",
     """        return applyFilters(built, container);""")

once("""        // Strip assertion notes before note-block parsing when hidden
        el._assertionsVisible = scenarioChoice(el, '_assertionsVisible');
        el._stepsVisible = scenarioChoice(el, '_stepsVisible');
        el._databasesVisible = scenarioChoice(el, '_databasesVisible');
        var renderSource = !el._assertionsVisible ? stripAssertionNotes(source) : source;
        renderSource = !el._stepsVisible ? stripStepDelimiters(renderSource) : renderSource;
        renderSource = !el._databasesVisible ? stripDatabaseCalls(renderSource) : renderSource;""",
     """        // The filters apply before note-block parsing, each with the state the diagram is drawn in
        el._assertionsVisible = scenarioChoice(el, '_assertionsVisible');
        el._stepsVisible = scenarioChoice(el, '_stepsVisible');
        el._databasesVisible = scenarioChoice(el, '_databasesVisible');
        el._eventsVisible = scenarioChoice(el, '_eventsVisible');
        el._setupVisible = scenarioChoice(el, '_setupVisible');
        var renderSource = applyFilters(source, el);""")

once("""            var cleanFilt = owner._noteOriginalSource;
            if (!filterShown(owner, '_assertionsVisible')) cleanFilt = stripAssertionNotes(cleanFilt);
            if (!filterShown(owner, '_stepsVisible')) cleanFilt = stripStepDelimiters(cleanFilt);
            if (!filterShown(owner, '_databasesVisible')) cleanFilt = stripDatabaseCalls(cleanFilt);""",
     """            var cleanFilt = applyFilters(owner._noteOriginalSource, owner);""")

# one queue builder, the model's two toggles, and the ten toggles
start = s.index("    function buildAssertionsQueue(containers, showing) {")
mark = s.index("    window._toggleScenarioDatabases = function(btn)")
end = s.index("\n", mark) + 1
s = s[:start] + """    // One queue builder for every diagram filter (5.0.0): the five differ only in the flag they set.
    function buildFilterQueue(containers, flag, showing) {
        var queue = [];
        containers.forEach(function(container) {
            if (container.classList.contains('puml-fragment')) return;
            if (isComponentDiagramContainer(container)) return;
            if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
            // A container with no state of its own is drawn again with the new one.
            if (container[flag] !== undefined && !!container[flag] === showing) return;
            container[flag] = showing;
            var noteBlocks = parseNoteBlocks(applyFilters(container._noteOriginalSource, container));
            if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
            if (!container._noteSteps) container._noteSteps = {};
            queue.push({ container: container, noteBlocks: noteBlocks });
        });
        return queue;
    }

    // One model for every diagram filter (5.0.0, F5): the report's toggle sets the report's state and clears every
    // scenario's own choice, as the report's choice holds everywhere; a scenario's toggle records its choice on the
    // scenario, so the diagrams it draws later take it (scenarioChoice).
    function toggleReportFilter(name, flag, btn) {
        var shown = btn.getAttribute('data-shown') !== 'true';
        window[flag] = shown;
        syncToggleBtn(document.querySelector('.toolbar-right'), name, shown);
        document.querySelectorAll('details.scenario').forEach(function(sc) {
            sc[flag] = undefined;
            syncToggleBtn(sc, name, shown);
        });
        renderWithPending(buildFilterQueue(document.querySelectorAll('[data-plantuml]'), flag, shown),
            document.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]'));
    }

    function toggleScenarioFilter(name, flag, btn) {
        var shown = btn.getAttribute('data-shown') !== 'true';
        var scenario = btn.closest('details.scenario');
        if (!scenario) return;
        scenario[flag] = shown;
        syncToggleBtn(scenario, name, shown);
        renderWithPending(buildFilterQueue(scenario.querySelectorAll('[data-plantuml]'), flag, shown),
            scenario.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]'));
    }

    // The report's and each scenario's toggles for assertion notes, step bars, database calls, event calls and setup
    window._toggleAssertions = function(btn) { toggleReportFilter('assertions', '_assertionsVisible', btn); };
    window._toggleScenarioAssertions = function(btn) { toggleScenarioFilter('assertions', '_assertionsVisible', btn); };
    window._toggleSteps = function(btn) { toggleReportFilter('steps', '_stepsVisible', btn); };
    window._toggleScenarioSteps = function(btn) { toggleScenarioFilter('steps', '_stepsVisible', btn); };
    window._toggleDatabases = function(btn) { toggleReportFilter('databases', '_databasesVisible', btn); };
    window._toggleScenarioDatabases = function(btn) { toggleScenarioFilter('databases', '_databasesVisible', btn); };
    window._toggleEvents = function(btn) { toggleReportFilter('events', '_eventsVisible', btn); };
    window._toggleScenarioEvents = function(btn) { toggleScenarioFilter('events', '_eventsVisible', btn); };
    window._toggleSetup = function(btn) { toggleReportFilter('setup', '_setupVisible', btn); };
    window._toggleScenarioSetup = function(btn) { toggleScenarioFilter('setup', '_setupVisible', btn); };
""" + s[end:]

open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
