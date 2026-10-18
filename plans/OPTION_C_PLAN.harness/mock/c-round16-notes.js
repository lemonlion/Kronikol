    // ── Round 16 (preview): the Events and Setup toggles ─────────────────────
    // Two filters beside assertions, steps and databases, applied wherever the report composes a source (first render,
    // a payload's own re-render, a toggle's queue) and replayed where it maps the notes drawn back to the source's.
    // Events: the calls to and from message brokers (the participants declared `queue`: Kafka, Event Hubs, Pub/Sub,
    // Service Bus) and their payloads, cut as the databases filter cuts a database's (stripEventCalls is that filter
    // with `queue` for `database|collections`). Setup: see stripSetup. Each is shown unless turned off; a scenario's
    // own choice holds for the diagrams it draws later.
    function kronFlag(el, name) {
        var sc = el && el.closest ? el.closest('details.scenario') : null;
        if (sc && sc[name] !== undefined) return sc[name];
        return window[name] !== false;
    }
    // Setup goes first: hiding the steps takes out the When bar it is found by
    function kronSetupFilter(source, el) { return el && el._setupVisible === false ? stripSetup(source) : source; }
    function kronEventsFilter(source, el) { return el && el._eventsVisible === false ? stripEventCalls(source) : source; }

    // The notes a filtered source still draws, as indexes into the source's own notes: each note carries a mark
    // through the same filters, so two notes that read alike ("1 row" in the setup and again later) cannot be
    // confused, as matching by their text confuses them. Null when a note lost its mark.
    function kronExactNoteMap(owner) {
        var src = owner._noteOriginalSource;
        var blocks = parseNoteBlocks(src);
        if (!blocks.length) return null;
        var lines = src.split('\n'), out = [], b = 0;
        for (var i = 0; i < lines.length; i++) {
            out.push(lines[i]);
            if (b < blocks.length && blocks[b].start === i) { out.push('kron-note-' + b); b++; }
        }
        var marked = kronSetupFilter(out.join('\n'), owner);
        if (!owner._assertionsVisible) marked = stripAssertionNotes(marked);
        if (!owner._stepsVisible) marked = stripStepDelimiters(marked);
        if (!owner._databasesVisible) marked = stripDatabaseCalls(marked);
        marked = kronEventsFilter(marked, owner);
        var left = parseNoteBlocks(marked), map = [];
        for (var k = 0; k < left.length; k++) {
            var m = /^kron-note-(\d+)$/.exec((left[k].contentLines[0] || '').trim());
            if (!m) return null;
            map.push(+m[1]);
        }
        return map;
    }

    // Round 16 (preview): a collapsed payload already shows all of itself when it has one line to show (its headers
    // hidden, or none) and its one-line preview is that line in full ("1 row"). Its + and ▾ would open nothing.
    function kronCollapsedIsWhole(contentLines, hdrHidden) {
        if (!contentLines) return false;
        var shown = [];
        for (var i = 0; i < contentLines.length; i++) {
            var t = contentLines[i].trim();
            if (NOTE_HEADER_TAG.test(t)) { if (!hdrHidden) return false; continue; }
            if (t !== '') shown.push(t);
        }
        if (shown.length > 1) return false;
        return !shown.length || getNotePreview(contentLines) === shown[0];
    }

    function buildKronQueue(containers, flag, showing) {
        var queue = [];
        containers.forEach(function(container) {
            if (container.classList.contains('puml-fragment')) return;
            if (isComponentDiagramContainer(container)) return;
            if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
            var wasVisible = container[flag] !== false;
            if (wasVisible === showing) return;
            container[flag] = showing;
            if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
            if (!container._noteSteps) container._noteSteps = {};
            queue.push({ container: container, noteBlocks: parseNoteBlocks(container._noteOriginalSource) });
        });
        return queue;
    }
    function kronToggle(name, flag, btn, scenarioLevel) {
        var shown = btn.getAttribute('data-shown') !== 'true';
        if (scenarioLevel) {
            var scenario = btn.closest('details.scenario');
            if (!scenario) return;
            scenario[flag] = shown;
            syncToggleBtn(scenario, name, shown);
            renderWithPending(buildKronQueue(scenario.querySelectorAll('[data-plantuml]'), flag, shown),
                scenario.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]'));
            return;
        }
        // the report's choice holds everywhere, over a scenario's own
        window[flag] = shown;
        syncToggleBtn(document.querySelector('.toolbar-right'), name, shown);
        document.querySelectorAll('details.scenario').forEach(function(sc) {
            sc[flag] = undefined;
            syncToggleBtn(sc, name, shown);
        });
        renderWithPending(buildKronQueue(document.querySelectorAll('[data-plantuml]'), flag, shown),
            document.querySelectorAll('.toggle-btn[data-toggle="' + name + '"]'));
    }
    window._toggleEvents = function(btn) { kronToggle('events', '_eventsVisible', btn, false); };
    window._toggleSetup = function(btn) { kronToggle('setup', '_setupVisible', btn, false); };
    window._toggleScenarioEvents = function(btn) { kronToggle('events', '_eventsVisible', btn, true); };
    window._toggleScenarioSetup = function(btn) { kronToggle('setup', '_setupVisible', btn, true); };

