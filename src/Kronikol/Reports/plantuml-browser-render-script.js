<script defer src="__PLANTUML_CDN_BASE__/viz-global.js"></script>
<script defer src="__PLANTUML_CDN_BASE__/plantuml.js"></script>
<script>
    document.addEventListener('DOMContentLoaded', function() {
        document.body.classList.add('plantuml-ready');
        plantumlLoad();
        var renderQueue = [];
        var rendering = false;
        // Expose rendering lock globally so processRenderQueue (note state changes)
        // can avoid calling plantuml.render() concurrently — TeaVM uses global state.
        window._plantumlRendering = false;
        var _pumlData = null;
        var _maxDiagramHeight = 12000;
        var _maxNoteChars = 15000;
        var _estimatedArrowHeight = 45;
        var _estimatedNoteLineHeight = 18;
        window._splitDiagramSource = splitDiagramSource;
        window._chunkLargeNotes = chunkLargeNotes;
        window._countArrows = function(lines) { return countArrows(lines); };
        // Regex arrow detection: matches ->, -->, -[#color]>, -[#color]->
        var _arrowRx = /-(?:\[[^\]]*\])?-?>/;
        // Regex return arrow detection: matches --> and -[#color]->
        var _returnArrowRx = /-(?:\[[^\]]*\])?->/;
        function isArrowLine(trimmed) { return _arrowRx.test(trimmed); }
        function isReturnArrow(trimmed) { return _returnArrowRx.test(trimmed); }
        function getPumlZ(el) {
            if (!_pumlData) {
                var s = document.getElementById('puml-data');
                _pumlData = s ? JSON.parse(s.textContent) : {};
            }
            return _pumlData[el.id] || el.getAttribute('data-plantuml-z') || null;
        }
        window._getPumlZ = getPumlZ;
        function extractIflowMap(source) {
            var map = {};
            var re = /\[\[#(iflow-[^\s\]]+)\s+([^\]]+)\]\]/g;
            var m;
            while ((m = re.exec(source)) !== null) {
                var key = m[2].split('\\n').join('').replace(/\s+/g, '');
                map[key] = m[1];
            }
            return map;
        }

        // --- Client-side diagram splitting ---

        // Parse PlantUML source into prefix (header/participants), body lines, and find trace boundaries
        function parseDiagramStructure(source) {
            var lines = source.split('\n');
            var prefixEnd = -1;
            var bodyStart = -1;
            var endumlIdx = lines.length - 1;

            // Find end of prefix: after last participant/actor/entity/database/queue/collections/boundary declaration
            // and after autonumber, skinparam, !pragma, style blocks
            var inStyle = false;
            var _styleOpen = '<' + 'style>';
            var _styleClose = '</' + 'style>';
            for (var i = 0; i < lines.length; i++) {
                var trimmed = lines[i].trim();
                if (trimmed === '@enduml') { endumlIdx = i; break; }
                if (trimmed === _styleOpen) { inStyle = true; continue; }
                if (trimmed === _styleClose) { inStyle = false; prefixEnd = i; continue; }
                if (inStyle) continue;
                if (trimmed === '' || trimmed.startsWith('@startuml') || trimmed.startsWith('!pragma') ||
                    trimmed.startsWith('skinparam') || trimmed.startsWith('autonumber') ||
                    trimmed.startsWith('participant ') || trimmed.startsWith('actor ') ||
                    trimmed.startsWith('entity ') || trimmed.startsWith('database ') ||
                    trimmed.startsWith('queue ') || trimmed.startsWith('collections ') ||
                    trimmed.startsWith('boundary ') || trimmed.startsWith('control ') ||
                    trimmed.startsWith('!theme ')) {
                    prefixEnd = i;
                } else {
                    bodyStart = i;
                    break;
                }
            }

            if (bodyStart < 0) bodyStart = prefixEnd + 1;
            var prefix = lines.slice(0, bodyStart).join('\n');
            var body = lines.slice(bodyStart, endumlIdx).join('\n');
            return { prefix: prefix, body: body, lines: lines, bodyStart: bodyStart, endumlIdx: endumlIdx };
        }

        // Parse body into trace units (a request arrow + notes + response arrow + notes)
        function parseTraceUnits(bodyText) {
            var lines = bodyText.split('\n');
            var units = [];
            var currentUnit = [];
            var currentUnitHasArrow = false;
            var inNote = false;

            for (var i = 0; i < lines.length; i++) {
                var trimmed = lines[i].trim();

                if (trimmed.startsWith('note') && (trimmed.indexOf(' left') >= 0 || trimmed.indexOf(' right') >= 0) && !trimmed.startsWith('note over')) {
                    inNote = true;
                    currentUnit.push(lines[i]);
                } else if (trimmed === 'end note') {
                    inNote = false;
                    currentUnit.push(lines[i]);
                } else if (inNote) {
                    currentUnit.push(lines[i]);
                } else if (isArrowLine(trimmed)) {
                    // Arrow line — this starts a new trace unit if we have response from previous
                    // Heuristic: arrows with -> (request) or --> (return) alternate
                    // Start new unit on request arrows (not return arrows). A unit that so far holds
                    // only a block opener (`loop …`, `alt …`, `partition …`) is kept: the opener belongs
                    // with the pair it wraps, never with the previous unit.
                    var isReturn = isReturnArrow(trimmed);
                    if (!isReturn && currentUnitHasArrow) {
                        units.push(currentUnit);
                        currentUnit = [];
                        currentUnitHasArrow = false;
                    }
                    currentUnit.push(lines[i]);
                    currentUnitHasArrow = true;
                } else if (isBlockOpener(trimmed)) {
                    // Block opener (loop/alt/opt/group/par/partition) — starts a fresh unit so a split
                    // boundary never separates it from the first pair inside it
                    if (currentUnitHasArrow) {
                        units.push(currentUnit);
                        currentUnit = [];
                        currentUnitHasArrow = false;
                    }
                    currentUnit.push(lines[i]);
                } else if (trimmed === 'end') {
                    // Block close — attach to current unit (the last pair inside the block)
                    currentUnit.push(lines[i]);
                } else {
                    currentUnit.push(lines[i]);
                }
            }
            if (currentUnit.length > 0) units.push(currentUnit);
            return units;
        }

        // `partition X`, `loop ×3 · 12–40 ms`, `alt …`, `opt …`, `group …`, `par …`, `critical …` — the
        // block statements that are closed by a bare `end` and must be closed/re-opened when a diagram
        // is split into height-bounded fragments (the server-side builder does the same).
        var _blockOpenerRx = /^(partition\s|loop\b|alt\b|opt\b|group\b|par\b|critical\b|break\b)/;
        function isBlockOpener(trimmed) { return _blockOpenerRx.test(trimmed); }
        window._isBlockOpener = isBlockOpener;

        // Estimate height of a trace unit
        function estimateUnitHeight(unitLines) {
            var height = 0;
            var inNote = false;
            for (var i = 0; i < unitLines.length; i++) {
                var trimmed = unitLines[i].trim();
                if (isArrowLine(trimmed)) {
                    height += _estimatedArrowHeight;
                } else if (trimmed.startsWith('note') && (trimmed.indexOf(' left') >= 0 || trimmed.indexOf(' right') >= 0)) {
                    inNote = true;
                    height += _estimatedArrowHeight; // note header
                } else if (trimmed === 'end note') {
                    inNote = false;
                } else if (inNote) {
                    height += _estimatedNoteLineHeight;
                }
            }
            return height;
        }

        // Split diagram source into fragments based on estimated height
        function splitDiagramSource(source, maxHeight) {
            if (!maxHeight) maxHeight = _maxDiagramHeight;
            var structure = parseDiagramStructure(source);
            if (!structure.body.trim()) return [source];

            var units = parseTraceUnits(structure.body);
            if (units.length === 0) return [source];

            var fragments = [];
            var currentLines = [];
            var currentHeight = 0;
            var stepCount = 0;
            // Blocks (partition / loop / alt / …) open at the current position. A fragment boundary
            // inside a block closes it (`end`) and the next fragment re-opens it with the same line,
            // so every fragment is a complete diagram — a stranded `end` or a never-closed `loop` is
            // a PlantUML syntax error.
            var openBlocks = [];

            // Extract the autonumber start from prefix
            var autoMatch = structure.prefix.match(/autonumber\s+(\d+)/);
            var baseStep = autoMatch ? parseInt(autoMatch[1], 10) : 1;

            for (var u = 0; u < units.length; u++) {
                var unitHeight = estimateUnitHeight(units[u]);

                // If adding this unit exceeds max and we have content, split here
                if (currentHeight > 0 && currentHeight + unitHeight > maxHeight) {
                    for (var ob = openBlocks.length - 1; ob >= 0; ob--) currentLines.push('end');
                    fragments.push({ lines: currentLines, startStep: baseStep + stepCount - countArrows(currentLines) });
                    currentLines = [];
                    currentHeight = 0;
                    for (var rb = 0; rb < openBlocks.length; rb++) currentLines.push(openBlocks[rb]);
                }

                // Track block state through this unit
                var inNoteTrack = false;
                for (var li = 0; li < units[u].length; li++) {
                    var t = units[u][li].trim();
                    if (!inNoteTrack && t.startsWith('note') && (t.indexOf(' left') >= 0 || t.indexOf(' right') >= 0 || t.indexOf(' over') >= 0) && t.indexOf(':') < 0) { inNoteTrack = true; continue; }
                    if (inNoteTrack) { if (t === 'end note') inNoteTrack = false; continue; }
                    if (isBlockOpener(t)) openBlocks.push(units[u][li]);
                    else if (t === 'end' && openBlocks.length > 0) openBlocks.pop();
                }

                for (var li2 = 0; li2 < units[u].length; li2++) {
                    currentLines.push(units[u][li2]);
                }
                currentHeight += unitHeight;
                stepCount += countArrowsInUnit(units[u]);
            }

            // Final fragment
            if (currentLines.length > 0) {
                for (var fb = openBlocks.length - 1; fb >= 0; fb--) currentLines.push('end');
                fragments.push({ lines: currentLines, startStep: baseStep + stepCount - countArrowsInLines(currentLines) });
            }

            if (fragments.length <= 1) return [source];

            // Build complete PlantUML sources for each fragment
            var result = [];
            var cumulativeSteps = baseStep;
            for (var f = 0; f < fragments.length; f++) {
                var fragPrefix = structure.prefix.replace(/autonumber\s+\d+/, 'autonumber ' + cumulativeSteps);
                result.push(fragPrefix + '\n' + fragments[f].lines.join('\n') + '\n@enduml');
                cumulativeSteps += countArrowsInLines(fragments[f].lines);
            }
            return result;
        }

        function countArrows(lines) {
            var c = 0;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].trim();
                if (isArrowLine(t) && !t.startsWith('note') && !t.startsWith('end note')) c++;
            }
            return c;
        }
        function countArrowsInUnit(unitLines) {
            var c = 0;
            for (var i = 0; i < unitLines.length; i++) {
                var t = unitLines[i].trim();
                if (isArrowLine(t) && !t.startsWith('note')) c++;
            }
            return c;
        }
        function countArrowsInLines(lines) {
            return countArrows(lines);
        }

        // Chunk large notes in PlantUML source — returns modified source with forced split markers
        function chunkLargeNotes(source, maxChars) {
            if (!maxChars) maxChars = _maxNoteChars;
            var lines = source.split('\n');
            var result = [];
            var inNote = false;
            var noteLines = [];
            var noteHeader = '';

            for (var i = 0; i < lines.length; i++) {
                var trimmed = lines[i].trim();
                if (!inNote && (trimmed.startsWith('note') && (trimmed.indexOf(' left') >= 0 || trimmed.indexOf(' right') >= 0) && !trimmed.startsWith('note over'))) {
                    inNote = true;
                    noteHeader = lines[i];
                    noteLines = [];
                } else if (inNote && trimmed === 'end note') {
                    inNote = false;
                    var noteContent = noteLines.join('\n');
                    if (noteContent.length > maxChars) {
                        // Find the last arrow before this note to determine the anchor participant
                        var anchorParticipant = '';
                        var noteDir = /\bright\b/.test(noteHeader) ? 'right' : 'left';
                        for (var ra = result.length - 1; ra >= 0; ra--) {
                            if (isArrowLine(result[ra].trim())) {
                                var am = result[ra].match(/^\s*(\S+)\s+.*?>\s*([^\s:]+)/);
                                if (am) {
                                    // 'note right' anchors to target; 'note left' anchors to source
                                    anchorParticipant = noteDir === 'right' ? am[2] : am[1];
                                }
                                break;
                            }
                        }
                        // Chunk the note content
                        var chunks = chunkString(noteContent, maxChars);
                        for (var ci = 0; ci < chunks.length; ci++) {
                            var chunk = chunks[ci];
                            if (ci > 0) chunk = '..Continued From Previous Diagram..\n' + chunk;
                            if (ci < chunks.length - 1) chunk = chunk + '\n..Continued On Next Diagram..';
                            // For continuation chunks, anchor note to participant so
                            // PlantUML renders it even without a preceding message
                            if (ci > 0 && anchorParticipant) {
                                result.push(noteHeader.replace(/\b(left|right)\b(?!\s+of\b)/, '$1 of ' + anchorParticipant));
                            } else {
                                result.push(noteHeader);
                            }
                            var chunkLines = chunk.split('\n');
                            for (var cl = 0; cl < chunkLines.length; cl++) result.push(chunkLines[cl]);
                            result.push('end note');
                            if (ci < chunks.length - 1) {
                                result.push('== __SPLIT_BOUNDARY__ ==');
                            }
                        }
                    } else {
                        result.push(noteHeader);
                        for (var nl = 0; nl < noteLines.length; nl++) result.push(noteLines[nl]);
                        result.push('end note');
                    }
                } else if (inNote) {
                    noteLines.push(lines[i]);
                } else {
                    result.push(lines[i]);
                }
            }
            return result.join('\n');
        }

        function chunkString(str, maxLen) {
            var chunks = [];
            var lines = str.split('\n');
            var current = '';
            for (var i = 0; i < lines.length; i++) {
                var candidate = current ? current + '\n' + lines[i] : lines[i];
                if (candidate.length > maxLen && current.length > 0) {
                    chunks.push(current);
                    current = lines[i];
                } else {
                    current = candidate;
                }
            }
            if (current) chunks.push(current);
            return chunks.length > 0 ? chunks : [str];
        }

        // Enhanced split that handles forced split boundaries from chunkLargeNotes
        // Walk lines (skipping note bodies) and push block openers / pop on bare `end`, so a caller
        // knows which blocks are still open at the end of a chunk of diagram text.
        function scanOpenBlocks(lines, stack) {
            var inNote = false;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].trim();
                if (!inNote && /^h?note\b/.test(t) && t.indexOf(':') < 0) { inNote = true; continue; }
                if (inNote) { if (t === 'end note' || t === 'endhnote' || t === 'endrnote') inNote = false; continue; }
                if (isBlockOpener(t)) stack.push(lines[i]);
                else if (t === 'end' && stack.length > 0) stack.pop();
            }
            return stack;
        }

        function splitWithChunkedNotes(source, maxHeight) {
            // First chunk any oversized notes
            var chunked = chunkLargeNotes(source, _maxNoteChars);
            // Check for forced split boundaries
            if (chunked.indexOf('__SPLIT_BOUNDARY__') >= 0) {
                var parts = chunked.split(/\n== __SPLIT_BOUNDARY__ ==\n/);
                var allFragments = [];
                // Blocks (loop/alt/partition) open when a forced boundary cut the text: the part
                // after the boundary re-opens them, the part before closes them, so each part is a
                // complete diagram before it is split further by height.
                var carried = [];
                for (var p = 0; p < parts.length; p++) {
                    // Each part gets wrapped as complete PlantUML and further split by height
                    var partSource = parts[p].trim();
                    if (carried.length > 0) {
                        var reopen = carried.join('\n');
                        if (partSource.indexOf('@startuml') < 0) partSource = reopen + '\n' + partSource;
                    }
                    var openAfter = scanOpenBlocks(partSource.split('\n'), []);
                    if (openAfter.length > 0) {
                        var closers = '';
                        for (var oc = 0; oc < openAfter.length; oc++) closers += '\nend';
                        partSource = partSource.indexOf('@enduml') >= 0
                            ? partSource.replace(/\n@enduml\s*$/, closers + '\n@enduml')
                            : partSource + closers;
                    }
                    carried = openAfter;
                    // Ensure it has @startuml/@enduml
                    if (partSource.indexOf('@startuml') < 0) {
                        var structure = parseDiagramStructure(source);
                        // Count steps from previous fragments
                        var prevSteps = 1;
                        for (var pf = 0; pf < allFragments.length; pf++) {
                            prevSteps += countArrows(allFragments[pf].split('\n'));
                        }
                        partSource = structure.prefix.replace(/autonumber\s+\d+/, 'autonumber ' + prevSteps) + '\n' + partSource + '\n@enduml';
                    } else if (partSource.indexOf('@enduml') < 0) {
                        // Part has @startuml but no @enduml (first part in chunked split).
                        // Without @enduml, parseDiagramStructure treats the last line as
                        // the end marker and excludes it from the body, which breaks
                        // note blocks whose 'end note' happens to be on the last line.
                        partSource = partSource + '\n@enduml';
                    }
                    var heightFrags = splitDiagramSource(partSource, maxHeight);
                    for (var hf = 0; hf < heightFrags.length; hf++) {
                        allFragments.push(heightFrags[hf]);
                    }
                }
                return allFragments;
            }
            // No forced boundaries — just split by height
            return splitDiagramSource(chunked, maxHeight);
        }
        window._splitWithChunkedNotes = splitWithChunkedNotes;

        // Render fragments into a container, creating child divs as needed
        function renderFragments(el, source) {
            var fragments = splitWithChunkedNotes(source);
            el._fragments = fragments;
            el._fullSource = source;

            if (fragments.length <= 1) {
                // Single fragment — render directly into container (existing behavior)
                renderQueue.push({ el: el, source: fragments[0] || source, isFragment: false });
            } else {
                // Multiple fragments — create child divs
                el.innerHTML = '';
                el.dataset.rendered = '1';
                for (var f = 0; f < fragments.length; f++) {
                    var fragDiv = document.createElement('div');
                    fragDiv.className = 'puml-fragment';
                    fragDiv.id = el.id + '-frag-' + f;
                    fragDiv.dataset.fragment = f;
                    fragDiv.setAttribute('data-plantuml', fragments[f]);
                    el.appendChild(fragDiv);
                    renderQueue.push({ el: fragDiv, source: fragments[f], isFragment: true, parentEl: el });
                }
            }
            processQueue();
        }

        // Header-only lines: what a diagram is made of once every arrow, participant and note has been
        // filtered away (assertion notes / step bars hidden). plantuml.js cannot draw an empty body —
        // it answers "Syntax Error? (Assumed diagram type: class)" — so such a fragment is shown as a
        // note instead of being sent to the engine.
        var _headerOnlyRx = /^(@startuml|@enduml|!pragma\b|skinparam\b|autonumber\b|hide\b|scale\b|title\b)/;
        // The one participant the generator declares for a markers-only diagram (so `hnote across` has a
        // lifeline to span — see PlantUmlCreator.MarkerOnlyParticipant); on its own it is nothing to draw.
        var _markerOnlyParticipantRx = /^participant "\(no interactions\)" as noInteractions$/;
        function hasDrawableBody(lines) {
            var inStyle = false;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].trim();
                if (!t) continue;
                if (/^<\/?style>$/i.test(t)) { inStyle = t.charAt(1) !== '/'; continue; }
                if (inStyle) continue;
                if (_headerOnlyRx.test(t)) continue;
                if (_markerOnlyParticipantRx.test(t)) continue;
                return true;
            }
            return false;
        }
        window._hasDrawableBody = hasDrawableBody;

        function processQueue() {
            if (rendering || window._plantumlRendering || renderQueue.length === 0) return;
            rendering = true;
            window._plantumlRendering = true;
            var item = renderQueue.shift();
            var lines = item.source.split('\n');
            var queueDone = false;
            if (!hasDrawableBody(lines)) {
                item.el.innerHTML = '<div class="no-interactions" data-nothing-to-draw="true">Nothing to draw with the current filters — this diagram is only assertion notes and/or step bars (use Assertions Shown / Steps Shown to see them).</div>';
                item.el.dataset.rendered = '1';
                rendering = false;
                window._plantumlRendering = false;
                processQueue();
                return;
            }
            function onQueueItemDone() {
                if (queueDone) return;
                queueDone = true;
                item.el.dataset.rendered = '1';
                var hookTarget = item.isFragment ? item.el : item.el;
                var iflowSource = item.parentEl ? item.parentEl._fullSource || item.source : item.source;
                try {
                    bindIflowLinks(hookTarget, iflowSource);
                    if (window._makeNotesCollapsible) window._makeNotesCollapsible(hookTarget);
                    if (window._addAssertionTooltips) window._addAssertionTooltips(hookTarget);
                    requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(hookTarget); });
                } catch(hookErr) { console.error('Post-render hook error:', hookErr); }
                rendering = false;
                window._plantumlRendering = false;
                processQueue();
            }
            var mo = new MutationObserver(function() {
                mo.disconnect();
                onQueueItemDone();
            });
            mo.observe(item.el, { childList: true, subtree: true });
            // Timeout: if TeaVM render doesn't produce output within 15s, force-reset and continue
            var qPollCount = 0;
            var qPoll = setInterval(function() {
                qPollCount++;
                if (queueDone) { clearInterval(qPoll); return; }
                if (qPollCount > 60) { clearInterval(qPoll); mo.disconnect(); queueDone = true; rendering = false; window._plantumlRendering = false; processQueue(); }
            }, 250);
            try {
                window.plantuml.render(lines, item.el.id);
            } catch(e) {
                mo.disconnect();
                item.el.dataset.rendered = '1';
                rendering = false;
                window._plantumlRendering = false;
                var msg = (e && e.message) ? e.message : String(e);
                if (msg.indexOf('too large') >= 0) {
                    // Try re-splitting with a smaller max height
                    if (!item._retried && !item.isFragment) {
                        item._retried = true;
                        var smallerFrags = splitWithChunkedNotes(item.source, _maxDiagramHeight / 2);
                        if (smallerFrags.length > 1) {
                            item.el.innerHTML = '';
                            item.el.dataset.rendered = '1';
                            for (var rf = 0; rf < smallerFrags.length; rf++) {
                                var rDiv = document.createElement('div');
                                rDiv.className = 'puml-fragment';
                                rDiv.id = item.el.id + '-frag-' + rf;
                                rDiv.dataset.fragment = rf;
                                rDiv.setAttribute('data-plantuml', smallerFrags[rf]);
                                item.el.appendChild(rDiv);
                                renderQueue.unshift({ el: rDiv, source: smallerFrags[rf], isFragment: true, parentEl: item.el, _retried: true });
                            }
                            processQueue();
                            return;
                        }
                    }
                    item.el.innerHTML = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px;margin:0.5em 0;">'
                        + '<strong>Diagram too large for client-side rendering.</strong><br>'
                        + 'Use <code>PlantUmlRendering.Server</code> or <code>PlantUmlRendering.Local</code> for large diagrams.'
                        + '<details style="margin-top:0.5em"><summary>Raw PlantUML</summary><pre style="white-space:pre-wrap">'
                        + item.source.replace(/</g,'&lt;') + '</pre></details></div>';
                } else {
                    item.el.textContent = 'Render error: ' + msg;
                }
                processQueue();
            }
        }
        window._iflowBindLinks = function(container, source) { bindIflowLinks(container, source); };
        function bindIflowLinks(container, source) {
            if (!container) return;
            var iflowData = window.__iflowSegments || {};
            var config = window.__iflowConfig || {};
            var hoverOnly = config.hasDataBehavior === 'showLinkOnHover';
            var bound = 0;
            container.querySelectorAll('a').forEach(function(a) {
                var href = a.getAttribute('xlink:href') || a.getAttribute('href') || '';
                if (href.indexOf('#iflow-') !== 0) return;
                var segId = href.substring(1);
                if (!iflowData[segId]) return;
                if (hoverOnly) {
                    a.removeAttribute('xlink:href');
                    a.removeAttribute('href');
                    a.classList.add('iflow-link-hover');
                } else {
                    a.style.cursor = 'pointer';
                }
                a.addEventListener('click', function(ev) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    if (window._iflowShowPopup) window._iflowShowPopup(segId);
                });
                bound++;
            });
            if (bound > 0) return;
            if (!source) return;
            var iflowMap = extractIflowMap(source);
            if (Object.keys(iflowMap).length === 0) return;
            var allTexts = Array.from(container.querySelectorAll('text'));
            var blueIndices = new Set();
            allTexts.forEach(function(t, idx) {
                if ((t.getAttribute('fill') || '').toLowerCase() === '#0000ff') {
                    blueIndices.add(idx);
                    t.setAttribute('fill', '#000000');
                    t.removeAttribute('text-decoration');
                }
            });
            var groups = [];
            var curGrp = [];
            var sorted = Array.from(blueIndices).sort(function(a, b) { return a - b; });
            for (var gi = 0; gi < sorted.length; gi++) {
                if (curGrp.length === 0 || sorted[gi] === curGrp[curGrp.length - 1] + 1) {
                    curGrp.push(sorted[gi]);
                } else {
                    groups.push(curGrp);
                    curGrp = [sorted[gi]];
                }
            }
            if (curGrp.length > 0) groups.push(curGrp);
            groups.forEach(function(group) {
                var combined = group.map(function(idx) { return allTexts[idx].textContent; }).join('');
                var key = combined.replace(/\s+/g, '');
                var segId = iflowMap[key] || null;
                if (!segId || !iflowData[segId]) return;
                var groupEls = group.map(function(idx) { return allTexts[idx]; });
                groupEls.forEach(function(textEl) {
                    textEl.style.pointerEvents = 'all';
                    if (hoverOnly) {
                        textEl.style.cursor = 'default';
                        textEl.addEventListener('mouseenter', function() {
                            groupEls.forEach(function(el) {
                                el.setAttribute('fill', '#0000FF');
                                el.setAttribute('text-decoration', 'underline');
                                el.style.cursor = 'pointer';
                            });
                        });
                        textEl.addEventListener('mouseleave', function() {
                            groupEls.forEach(function(el) {
                                el.setAttribute('fill', '#000000');
                                el.removeAttribute('text-decoration');
                                el.style.cursor = 'default';
                            });
                        });
                    } else {
                        textEl.setAttribute('fill', '#0000FF');
                        textEl.setAttribute('text-decoration', 'underline');
                        textEl.style.cursor = 'pointer';
                    }
                    textEl.addEventListener('click', function(ev) {
                        ev.preventDefault();
                        ev.stopPropagation();
                        if (window._iflowShowPopup) window._iflowShowPopup(segId);
                    });
                });
                bound++;
            });
        }
        function enqueueElement(el) {
            var source = el.getAttribute('data-plantuml');
            if (source) {
                if (window._preProcessSource) source = window._preProcessSource(el, source);
                el.setAttribute('data-plantuml', source);
                renderFragments(el, source);
            } else {
                var pumlZ = getPumlZ(el);
                if (pumlZ) {
                    decompressGzipBase64(pumlZ).then(function(decoded) {
                        el.setAttribute('data-plantuml', decoded);
                        var src = decoded;
                        if (window._preProcessSource) src = window._preProcessSource(el, decoded);
                        el.setAttribute('data-plantuml', src);
                        renderFragments(el, src);
                    }).catch(function() { el.textContent = 'Decompression error'; });
                }
            }
        }
        var observer = new IntersectionObserver(function(entries) {
            entries.forEach(function(entry) {
                if (!entry.isIntersecting) return;
                var el = entry.target;
                if (el.dataset.queued) return;
                el.dataset.queued = '1';
                observer.unobserve(el);
                enqueueElement(el);
            });
        }, { rootMargin: '200px' });
        function decompressGzipBase64(base64) {
            var raw = atob(base64);
            var bytes = new Uint8Array(raw.length);
            for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
            var stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
            return new Response(stream).text();
        }
        window.decompressGzipBase64 = decompressGzipBase64;
        window._renderDiagramsInContainer = function(container) {
            if (!container) return;
            container.querySelectorAll('.plantuml-browser').forEach(function(el) {
                if (el.dataset.queued) return;
                el.dataset.queued = '1';
                observer.unobserve(el);
                enqueueElement(el);
            });
        };
        document.querySelectorAll('.plantuml-browser').forEach(function(el) {
            observer.observe(el);
        });
        // Preload first scenario's diagrams immediately
        var firstScenario = document.querySelector('.scenario');
        if (firstScenario) {
            firstScenario.querySelectorAll('.plantuml-browser').forEach(function(el) {
                if (el.dataset.queued) return;
                el.dataset.queued = '1';
                observer.unobserve(el);
                enqueueElement(el);
            });
            // Also render first scenario's flame charts
            if (window._renderFlameCharts) window._renderFlameCharts(firstScenario);
        }
    });
</script>