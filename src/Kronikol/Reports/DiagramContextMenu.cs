using Kronikol.Constants;

namespace Kronikol.Reports;

/// <summary>
/// Generates the context menu HTML and JavaScript for diagram interactions in the report viewer.
/// </summary>
public static class DiagramContextMenu
{
    public static string GetStyles() => """
        .diagram-ctx-menu {
            position: fixed;
            background: #fff;
            border: 1px solid #ccc;
            border-radius: 4px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.15);
            padding: 4px 0;
            z-index: 20001;
            font: 13px -apple-system, 'Segoe UI', sans-serif;
            min-width: 200px;
        }
        .diagram-ctx-menu > div {
            padding: 6px 24px;
            cursor: pointer;
            white-space: nowrap;
        }
        .diagram-ctx-menu > div:hover {
            background: #e8f0fe;
        }
        .diagram-ctx-menu hr {
            margin: 4px 0;
            border: none;
            border-top: 1px solid #e0e0e0;
        }
        .diagram-ctx-menu .submenu-parent {
            position: relative;
            padding-right: 32px;
        }
        .diagram-ctx-menu .submenu-parent::after {
            content: '\25B6';
            position: absolute;
            right: 10px;
            top: 50%;
            transform: translateY(-50%);
            font-size: 8px;
            color: #666;
        }
        .diagram-ctx-menu .submenu {
            display: none;
            position: absolute;
            left: 100%;
            top: -4px;
            background: #fff;
            border: 1px solid #ccc;
            border-radius: 4px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.15);
            padding: 4px 0;
            min-width: 200px;
            z-index: 20002;
        }
        .diagram-ctx-menu .submenu-parent:hover > .submenu {
            display: block;
        }
        .diagram-ctx-menu .submenu.flip-left {
            left: auto;
            right: 100%;
        }
        .diagram-ctx-menu .submenu > div {
            padding: 6px 24px;
            cursor: pointer;
            white-space: nowrap;
        }
        .diagram-ctx-menu .submenu > div:hover {
            background: #e8f0fe;
        }
        @media (max-width: 768px) {
            .diagram-ctx-menu {
                position: fixed;
                left: 0 !important;
                right: 0 !important;
                bottom: 0 !important;
                top: auto !important;
                width: 100%;
                max-width: 100%;
                border-radius: 12px 12px 0 0;
                box-shadow: 0 -4px 16px rgba(0,0,0,0.2);
                padding: 8px 0 env(safe-area-inset-bottom, 8px);
                min-width: unset;
                animation: ctx-slide-up 0.2s ease-out;
            }
            @keyframes ctx-slide-up {
                from { transform: translateY(100%); }
                to { transform: translateY(0); }
            }
            .diagram-ctx-menu > div {
                padding: 12px 20px;
                font-size: 15px;
            }
            .diagram-ctx-menu .submenu-parent {
                padding-right: 40px;
            }
            .diagram-ctx-menu .submenu {
                position: fixed;
                left: 0 !important;
                right: 0 !important;
                bottom: 0 !important;
                top: auto !important;
                width: 100%;
                max-width: 100%;
                border-radius: 12px 12px 0 0;
                box-shadow: 0 -4px 16px rgba(0,0,0,0.2);
                padding: 8px 0 env(safe-area-inset-bottom, 8px);
                min-width: unset;
            }
            .diagram-ctx-menu .submenu > div {
                padding: 12px 20px;
                font-size: 15px;
            }
            .diagram-ctx-menu .submenu.flip-left {
                left: 0;
                right: 0;
            }
        }
        """;

    public static string GetInlineSvgStyles() => """
        .plantuml-inline-svg svg,
        .plantuml-browser svg,
        .puml-fragment svg {
            max-width: 100%;
            height: auto;
        }
        .plantuml-inline-svg,
        .plantuml-browser {
            overflow-x: auto;
            padding-left: 1em;
        }
        .puml-fragment {
            margin-bottom: 0.5em;
        }
        @keyframes pulse-loading { 0%,100% { opacity: 0.4; } 50% { opacity: 1; } }
        .plantuml-browser:not([data-rendered]) {
            min-height: 48px;
            display: flex;
            align-items: center;
            color: #999;
        }
        .plantuml-browser:not([data-rendered])::before {
            animation: pulse-loading 2s ease-in-out infinite;
        }
        .plantuml-browser:not([data-rendered])::before {
            content: 'Waiting for page load to complete\2026';
        }
        body.plantuml-ready .plantuml-browser:not([data-rendered])::before {
            content: 'Rendering diagram\2026';
        }
        .diagram-selected {
            box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.5);
            border-radius: 4px;
        }
        .diagram-zoom-controls {
            position: sticky;
            top: 2em;
            left: 2em;
            z-index: 10;
            display: flex;
            align-items: center;
            gap: 4px;
            opacity: 0;
            pointer-events: none;
            transition: opacity 0.15s;
            height: 0;
            overflow: visible;
        }
        [data-diagram-type]:hover > .diagram-zoom-controls {
            opacity: 0.4;
            pointer-events: auto;
        }
        .diagram-zoom-controls:hover {
            opacity: 1 !important;
        }
        .diagram-selected > .diagram-zoom-controls {
            opacity: 1;
            pointer-events: auto;
        }

        .diagram-zoom-slider {
            width: 100px;
            height: 6px;
            cursor: pointer;
            accent-color: rgb(59, 130, 246);
        }
        @media (max-width: 768px) {
            .diagram-zoom-controls {
                display: none !important;
            }
        }
        """;

    public static string GetCollapsibleNotesStyles() => """
        .details-radio { display: inline-flex; align-items: center; gap: 0.3em; }
        .details-radio-label { font-weight: bold; margin-right: 0.3em; font-size: 13px; }
        .details-radio-btn {
            padding: 0.25em 0.6em;
            border: 1px solid rgb(180, 180, 180);
            border-radius: 0.4em;
            background: white;
            cursor: pointer;
            font-size: 0.85em;
        }
        .details-radio-btn:hover { background: rgb(230, 240, 255); border-color: rgb(100, 150, 255); }
        .details-radio-btn.details-active { background: rgb(66, 133, 244); color: white; border-color: rgb(66, 133, 244); }
        .toggle-btn { margin-left: 0.5em; }
        .truncate-lines-select {
            padding: 0.2em 0.3em;
            border: 1px solid rgb(180, 180, 180);
            border-radius: 0.4em;
            font-size: 0.85em;
            margin-left: 0;
        }
        .truncate-lines-label { font-size: 0.85em; color: rgb(100, 100, 100); margin-left: 0; margin-right: 0.3em; }
        .note-toggle-icon { user-select: none; -webkit-user-select: none; }
        @media (max-width: 768px) {
            .details-radio { flex-wrap: wrap; }
            .toggle-btn { margin-left: 0; }
            .diagram-toggle { flex-wrap: wrap; gap: 0.3em; }
            .diagram-toggle-spacer { display: none; }
            .diagram-toggle-btn { text-align: center; }
        }
        """;

    public static string GetInternalFlowPopupStyles() => """
        .iflow-overlay {
            position: fixed;
            inset: 0;
            background: rgba(0,0,0,0.4);
            z-index: 20000;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .iflow-popup {
            background: #fff;
            border-radius: 8px;
            box-shadow: 0 8px 32px rgba(0,0,0,0.25);
            max-width: 90vw;
            max-height: 85vh;
            overflow: auto;
            padding: 20px;
            position: relative;
            font: 14px/1.5 -apple-system, 'Segoe UI', sans-serif;
        }
        .iflow-popup-close {
            position: absolute;
            top: 8px;
            right: 12px;
            cursor: pointer;
            font-size: 20px;
            color: #666;
            background: none;
            border: none;
            padding: 4px 8px;
        }
        .iflow-popup-close:hover {
            color: #000;
        }
        .iflow-popup h3 {
            margin: 0 0 12px 0;
            font-size: 15px;
            color: #333;
        }
        .iflow-popup .iflow-no-data {
            color: #888;
            font-style: italic;
            padding: 20px 0;
        }
        .iflow-popup .iflow-diagram {
            min-height: 60px;
        }
        .iflow-call-tree {
            list-style: none;
            padding-left: 0;
            font: 13px/1.6 monospace;
        }
        .iflow-call-tree ul {
            list-style: none;
            padding-left: 20px;
            border-left: 1px solid #ddd;
        }
        .iflow-call-tree li { padding: 2px 0; }
        .iflow-source { color: #888; font-size: 11px; }
        .iflow-duration { color: #666; font-size: 12px; }
        .iflow-toggle { margin-top: 8px; margin-bottom: 8px; }
        .iflow-toggle-btn {
            padding: 4px 14px;
            border: 1px solid #ccc;
            background: #f5f5f5;
            cursor: pointer;
            font-size: 13px;
            border-radius: 4px;
            margin-right: 4px;
        }
        .iflow-toggle-btn:hover { background: #e8f0fe; }
        .iflow-toggle-active { background: #4285f4; color: #fff; border-color: #4285f4; }
        .iflow-toggle-active:hover { background: #3367d6; }
        .iflow-flame { padding: 4px 0; }
        .iflow-flame-bar {
            position: relative;
            height: 22px;
            min-width: 4px;
            border-radius: 3px;
            margin: 1px 0;
            overflow: hidden;
            font: 11px/22px -apple-system, 'Segoe UI', sans-serif;
            color: #333;
            cursor: pointer;
            border: 1px solid rgba(0,0,0,0.08);
        }
        .iflow-flame-bar:hover { filter: brightness(0.92); }
        .iflow-flame-zoom-hint {
            font: 11px -apple-system, 'Segoe UI', sans-serif;
            color: #666;
            padding: 2px 4px;
            margin-bottom: 2px;
            cursor: default;
        }
        .iflow-flame-label {
            padding: 0 4px;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            display: block;
        }
        .iflow-boundary-marker {
            position: absolute;
            top: 0;
            bottom: 0;
            width: 0;
            border-left: 1px dashed rgba(0,0,0,0.3);
            z-index: 1;
            pointer-events: none;
        }
        .iflow-boundary-marker:hover { border-left-color: rgba(0,0,0,0.6); pointer-events: auto; }
        .whole-test-flow { margin-top: 8px; padding-top: 4px; }
        .whole-test-flow > summary { cursor: pointer; font-weight: 600; color: #555; }
        .diagram-toggle { margin-top: 8px; margin-bottom: 8px; padding-left: 1em; padding-right: 1em; display: flex; align-items: center; width: 100%; box-sizing: border-box; }
        .diagram-toggle-btn {
            padding: 4px 14px;
            border: 1px solid #ccc;
            background: #f5f5f5;
            cursor: pointer;
            font-size: 13px;
            border-radius: 4px;
            margin-right: 4px;
        }
        .diagram-toggle-btn:hover { background: #e8f0fe; }
        .diagram-toggle-active { background: #4285f4; color: #fff; border-color: #4285f4; }
        .diagram-toggle-active:hover { background: #3367d6; }
        .diagram-toggle-spacer { flex: 1; }
        .collapse-all-notes-btn, .toggle-headers-btn {
            padding: 4px 14px;
            border: 1px solid #ccc;
            background: #f5f5f5;
            cursor: pointer;
            font-size: 13px;
            border-radius: 4px;
            margin-right: 1em;
        }
        .collapse-all-notes-btn:hover, .toggle-headers-btn:hover { background: #e8f0fe; }
        .span-count-warning { color: #b30000; font-size: 12px; font-style: italic; margin-left: 8px; }
        .iflow-test-band { border-bottom: 1px solid #eee; padding: 4px 0; }
        .iflow-test-band-label { font: 11px/1.4 monospace; color: #888; padding: 2px 0; }
        .iflow-sequential-tests { padding: 0; }
        .iflow-rel-list { list-style: none; padding: 0; margin: 16px 0; }
        .iflow-rel-list li { padding: 6px 12px; border: 1px solid #e0e0e0; border-radius: 4px; margin: 4px 0; cursor: pointer; font: 13px/1.5 -apple-system, 'Segoe UI', sans-serif; }
        .iflow-rel-list li:hover { background: #f0f4ff; border-color: #4285f4; }
        .iflow-rel-summary-table { width: 100%; border-collapse: collapse; font: 12px/1.5 -apple-system, 'Segoe UI', sans-serif; margin-top: 12px; }
        .iflow-rel-summary-table th { text-align: left; padding: 4px 8px; border-bottom: 2px solid #ddd; color: #555; }
        .iflow-rel-summary-table td { padding: 4px 8px; border-bottom: 1px solid #eee; cursor: pointer; }
        .iflow-rel-summary-table tr:hover td { background: #f0f4ff; }
        svg a.iflow-link-hover, svg text.iflow-link-hover { cursor: default; }
        svg a.iflow-link-hover:hover, svg text.iflow-link-hover:hover { cursor: pointer; }
        svg a.iflow-link-hover:hover text { fill: #0000EE; text-decoration: underline; }
        svg text.iflow-link-hover:hover { fill: #0000EE; text-decoration: underline; }
        """;

    public static string GetInternalFlowConfigScript(InternalFlowHasDataBehavior hasDataBehavior) =>
        $"<script>window.__iflowConfig = {{ hasDataBehavior: '{(hasDataBehavior == InternalFlowHasDataBehavior.ShowLinkOnHover ? "showLinkOnHover" : "showLink")}' }};</script>";

    private const string PlantUmlJsCdnBase = TrackingDefaults.PlantUmlJsCdnBase;

    public static string GetPlantUmlBrowserRenderScript() => $$"""
        <script defer src="{{PlantUmlJsCdnBase}}/viz-global.js"></script>
        <script defer src="{{PlantUmlJsCdnBase}}/plantuml.js"></script>
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
                            // Start new unit on request arrows (not return arrows)
                            var isReturn = isReturnArrow(trimmed);
                            if (!isReturn && currentUnit.length > 0) {
                                units.push(currentUnit);
                                currentUnit = [];
                            }
                            currentUnit.push(lines[i]);
                        } else if (trimmed.startsWith('partition ') || trimmed === 'end') {
                            // Partition open/close — attach to current unit
                            currentUnit.push(lines[i]);
                        } else {
                            currentUnit.push(lines[i]);
                        }
                    }
                    if (currentUnit.length > 0) units.push(currentUnit);
                    return units;
                }

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
                    var openPartition = null;

                    // Extract the autonumber start from prefix
                    var autoMatch = structure.prefix.match(/autonumber\s+(\d+)/);
                    var baseStep = autoMatch ? parseInt(autoMatch[1], 10) : 1;

                    for (var u = 0; u < units.length; u++) {
                        var unitHeight = estimateUnitHeight(units[u]);

                        // If adding this unit exceeds max and we have content, split here
                        if (currentHeight > 0 && currentHeight + unitHeight > maxHeight) {
                            // Close open partition if any
                            if (openPartition) currentLines.push('end');
                            fragments.push({ lines: currentLines, startStep: baseStep + stepCount - countArrows(currentLines) });
                            currentLines = [];
                            currentHeight = 0;
                            // Re-open partition in new fragment
                            if (openPartition) currentLines.push(openPartition);
                        }

                        // Track partition state
                        for (var li = 0; li < units[u].length; li++) {
                            var t = units[u][li].trim();
                            if (t.startsWith('partition ')) openPartition = units[u][li];
                            else if (t === 'end' && openPartition) openPartition = null;
                        }

                        for (var li = 0; li < units[u].length; li++) {
                            currentLines.push(units[u][li]);
                        }
                        currentHeight += unitHeight;
                        stepCount += countArrowsInUnit(units[u]);
                    }

                    // Final fragment
                    if (currentLines.length > 0) {
                        if (openPartition) currentLines.push('end');
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
                function splitWithChunkedNotes(source, maxHeight) {
                    // First chunk any oversized notes
                    var chunked = chunkLargeNotes(source, _maxNoteChars);
                    // Check for forced split boundaries
                    if (chunked.indexOf('__SPLIT_BOUNDARY__') >= 0) {
                        var parts = chunked.split(/\n== __SPLIT_BOUNDARY__ ==\n/);
                        var allFragments = [];
                        for (var p = 0; p < parts.length; p++) {
                            // Each part gets wrapped as complete PlantUML and further split by height
                            var partSource = parts[p].trim();
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

                function processQueue() {
                    if (rendering || window._plantumlRendering || renderQueue.length === 0) return;
                    rendering = true;
                    window._plantumlRendering = true;
                    var item = renderQueue.shift();
                    var lines = item.source.split('\n');
                    var queueDone = false;
                    function onQueueItemDone() {
                        if (queueDone) return;
                        queueDone = true;
                        item.el.dataset.rendered = '1';
                        var hookTarget = item.isFragment ? item.el : item.el;
                        var iflowSource = item.parentEl ? item.parentEl._fullSource || item.source : item.source;
                        bindIflowLinks(hookTarget, iflowSource);
                        if (window._makeNotesCollapsible) window._makeNotesCollapsible(hookTarget);
                        if (window._addAssertionTooltips) window._addAssertionTooltips(hookTarget);
                        requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(hookTarget); });
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
        """;

    public static string GetContextMenuScript() => """
        <script>
        (function() {
            var menu = null;

            function findDiagramContainer(el) {
                while (el) {
                    if (el.dataset && el.dataset.diagramType) return el;
                    el = el.parentElement;
                }
                return null;
            }

            function getDiagramFilename(container, ext) {
                var scenario = container.closest('details.scenario');
                var baseName = 'diagram';
                if (scenario) {
                    var summary = scenario.querySelector(':scope > summary');
                    if (summary) {
                        var clone = summary.cloneNode(true);
                        clone.querySelectorAll('button, a, .endpoint, .label, .duration-badge').forEach(function(e) { e.remove(); });
                        baseName = (clone.textContent || '').trim();
                    }
                }
                baseName = baseName.toLowerCase()
                    .replace(/[\[\]]/g, '')
                    .replace(/["']/g, '')
                    .replace(/[^a-z0-9]+/g, '-')
                    .replace(/^-+|-+$/g, '');
                if (!baseName) baseName = 'diagram';
                var containers = scenario ? Array.from(scenario.querySelectorAll('[data-diagram-type]')) : [];
                if (containers.length > 1) {
                    var idx = containers.indexOf(container);
                    baseName += '-dg' + (idx + 1);
                }
                return baseName + '.' + ext;
            }

            function getSvg(container) {
                return container.querySelector('svg');
            }

            function getSource(container) {
                return container.getAttribute('data-plantuml') || '';
            }

            async function getSourceAsync(container) {
                var src = container.getAttribute('data-plantuml');
                if (src) return src;
                var pumlZ = window._getPumlZ ? window._getPumlZ(container) : container.getAttribute('data-plantuml-z');
                if (pumlZ) {
                    var decoded = await decompressGzipBase64(pumlZ);
                    container.setAttribute('data-plantuml', decoded);
                    return decoded;
                }
                return '';
            }

            function getTypeLabel(container) {
                return 'PlantUML';
            }

            function serializeSvg(svg) {
                return new XMLSerializer().serializeToString(svg);
            }

            function getBackgroundColor(svg) {
                // 1. Check SVG inline style (PlantUML sets background here)
                var svgStyle = svg.getAttribute('style') || '';
                var bgMatch = svgStyle.match(/background\s*:\s*([^;]+)/);
                if (bgMatch) {
                    var val = bgMatch[1].trim();
                    if (val && val !== 'none' && val !== 'transparent') return val;
                }
                // 2. Check computed style
                var computed = window.getComputedStyle(svg).backgroundColor;
                if (computed && computed !== 'rgba(0, 0, 0, 0)' && computed !== 'transparent') return computed;
                // 3. Only consider rects that cover the full SVG area (true background rects)
                var svgW = svg.width.baseVal.value || svg.getBoundingClientRect().width;
                var svgH = svg.height.baseVal.value || svg.getBoundingClientRect().height;
                var svgArea = svgW * svgH;
                if (svgArea > 0) {
                    var rects = svg.querySelectorAll('rect');
                    for (var i = 0; i < rects.length; i++) {
                        var rect = rects[i];
                        var rw = parseFloat(rect.getAttribute('width') || 0);
                        var rh = parseFloat(rect.getAttribute('height') || 0);
                        if ((rw * rh) / svgArea < 0.9) continue;
                        var fo = rect.getAttribute('fill-opacity');
                        if (fo !== null && parseFloat(fo) === 0) continue;
                        var rstyle = rect.getAttribute('style') || '';
                        var fom = rstyle.match(/fill-opacity\s*:\s*([^;]+)/);
                        if (fom && parseFloat(fom[1]) === 0) continue;
                        var fill = rect.getAttribute('fill');
                        if (fill) {
                            if (fill === 'none' || fill === 'transparent') continue;
                            if (/^#[0-9a-fA-F]{8}$/.test(fill) && fill.slice(7).toLowerCase() === '00') continue;
                            return fill;
                        }
                        var fm = rstyle.match(/fill\s*:\s*([^;]+)/);
                        if (fm && fm[1].trim() !== 'none' && fm[1].trim() !== 'transparent') return fm[1].trim();
                    }
                }
                return '#ffffff';
            }

            function svgToCanvas(svg, callback) {
                var svgData = serializeSvg(svg);
                var url = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgData)));
                var img = new Image();
                var scale = 2;
                img.onload = function() {
                    var canvas = document.createElement('canvas');
                    canvas.width = img.naturalWidth * scale;
                    canvas.height = img.naturalHeight * scale;
                    var ctx = canvas.getContext('2d');
                    ctx.scale(scale, scale);
                    ctx.drawImage(img, 0, 0);
                    callback(canvas);
                };
                img.src = url;
            }

            function svgToCanvasWithBg(svg, callback) {
                var bg = getBackgroundColor(svg);
                var clone = svg.cloneNode(true);
                var vb = clone.getAttribute('viewBox');
                var bx = '0', by = '0', bw, bh;
                if (vb) {
                    var parts = vb.split(/[\s,]+/);
                    bx = parts[0]; by = parts[1]; bw = parts[2]; bh = parts[3];
                } else {
                    bw = clone.getAttribute('width') || svg.getBoundingClientRect().width;
                    bh = clone.getAttribute('height') || svg.getBoundingClientRect().height;
                }
                var bgRect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                bgRect.setAttribute('x', bx);
                bgRect.setAttribute('y', by);
                bgRect.setAttribute('width', bw);
                bgRect.setAttribute('height', bh);
                bgRect.setAttribute('fill', bg);
                clone.insertBefore(bgRect, clone.firstChild);
                var svgData = serializeSvg(clone);
                var url = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgData)));
                var img = new Image();
                var scale = 2;
                img.onload = function() {
                    var canvas = document.createElement('canvas');
                    canvas.width = img.naturalWidth * scale;
                    canvas.height = img.naturalHeight * scale;
                    var ctx = canvas.getContext('2d');
                    ctx.scale(scale, scale);
                    ctx.drawImage(img, 0, 0);
                    callback(canvas);
                };
                img.src = url;
            }

            function htmlToCanvas(element, callback) {
                var rect = element.getBoundingClientRect();
                var scale = 2;
                var canvas = document.createElement('canvas');
                canvas.width = rect.width * scale;
                canvas.height = rect.height * scale;
                var ctx = canvas.getContext('2d');
                ctx.scale(scale, scale);
                var svgNs = 'http://www.w3.org/2000/svg';
                var fo = '<foreignObject width="' + rect.width + '" height="' + rect.height + '">'
                    + '<body xmlns="http://www.w3.org/1999/xhtml" style="margin:0">'
                    + element.outerHTML + '</body></foreignObject>';
                var svgMarkup = '<svg xmlns="' + svgNs + '" width="' + rect.width + '" height="' + rect.height + '">' + fo + '</svg>';
                var url = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgMarkup)));
                var img = new Image();
                img.onload = function() { ctx.drawImage(img, 0, 0); callback(canvas); };
                img.onerror = function() { callback(canvas); };
                img.src = url;
            }

            function closeMenu() {
                if (menu) { menu.remove(); menu = null; }
            }

            function createMenuItem(label, action) {
                var item = document.createElement('div');
                item.textContent = label;
                item.addEventListener('click', function(e) {
                    e.stopPropagation();
                    closeMenu();
                    action();
                });
                return item;
            }

            function createSeparator() {
                return document.createElement('hr');
            }

            function createSubMenu(label, items) {
                var parent = document.createElement('div');
                parent.className = 'submenu-parent';
                parent.textContent = label;
                var sub = document.createElement('div');
                sub.className = 'submenu';
                items.forEach(function(item) { sub.appendChild(item); });
                parent.appendChild(sub);
                parent.addEventListener('mouseenter', function() {
                    var rect = sub.getBoundingClientRect();
                    if (rect.right > window.innerWidth) sub.classList.add('flip-left');
                    else sub.classList.remove('flip-left');
                });
                parent.addEventListener('click', function(e) {
                    if (window.matchMedia('(max-width: 768px)').matches) {
                        e.stopPropagation();
                        sub.style.display = sub.style.display === 'block' ? '' : 'block';
                    }
                });
                return parent;
            }

            function extractCallerPayloads(source) {
                if (!source) return '';
                var lines = source.split('\n');
                var callerAlias = null;
                for (var i = 0; i < lines.length; i++) {
                    var m = lines[i].match(/^\s*actor\s+"[^"]*"\s+as\s+(\S+)/);
                    if (m) { callerAlias = m[1]; break; }
                }
                if (!callerAlias) return '';
                var payloads = [];
                var inNote = false;
                var noteLines = [];
                var afterCallerRequest = false;
                for (var i = 0; i < lines.length; i++) {
                    var line = lines[i];
                    if (!inNote) {
                        if (line.match(new RegExp('^\\s*' + callerAlias.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\s+->\\s+'))) {
                            afterCallerRequest = true;
                        } else if (line.match(/^\s*\S+\s+-->\s+/)) {
                            afterCallerRequest = false;
                        }
                        if (afterCallerRequest && line.match(/^\s*note\s+left/)) {
                            inNote = true;
                            noteLines = [];
                        }
                    } else {
                        if (line.match(/^\s*end\s+note/)) {
                            inNote = false;
                            afterCallerRequest = false;
                            var body = noteLines
                                .filter(function(l) { return !l.match(/^\s*<color:gray>/); })
                                .join('\n').trim();
                            if (body) payloads.push(body);
                        } else {
                            noteLines.push(line);
                        }
                    }
                }
                return payloads.join('\n\n');
            }

            function showToast(message) {
                var existing = document.querySelector('.diagram-ctx-toast');
                if (existing) existing.remove();
                var toast = document.createElement('div');
                toast.className = 'diagram-ctx-toast';
                toast.textContent = message;
                toast.style.cssText = 'position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:#333;color:#fff;padding:10px 20px;border-radius:6px;font:13px -apple-system,sans-serif;z-index:30000;opacity:1;transition:opacity 0.5s';
                document.body.appendChild(toast);
                setTimeout(function() { toast.style.opacity = '0'; }, 2500);
                setTimeout(function() { toast.remove(); }, 3000);
            }

            document.addEventListener('contextmenu', function(e) {
                var container = findDiagramContainer(e.target);
                if (!container) return;
                e.preventDefault();
                closeMenu();

                var diagramType = container.getAttribute('data-diagram-type');
                // Find the SVG that contains the click target (handles puml-fragment splits).
                // Use ownerSVGElement for SVG child elements, closest for HTML elements.
                var svg = e.target.ownerSVGElement || (e.target.closest ? e.target.closest('svg') : null) || (e.target.tagName === 'svg' ? e.target : null);
                if (!svg || !container.contains(svg)) svg = getSvg(container);
                var isHtmlContent = !svg && (diagramType === 'flamechart' || diagramType === 'calltree');

                // Need either an SVG or a recognized HTML content type
                if (!svg && !isHtmlContent) return;

                var source = getSource(container);
                var typeLabel = getTypeLabel(container);

                menu = document.createElement('div');
                menu.className = 'diagram-ctx-menu';

                // Check if right-click is on a note
                var clickedNoteIdx = -1;
                var _fullNoteText = null;
                var _currentNoteText = null;
                var _noteIsNotExpanded = false;
                if (svg && window._findNoteGroups) {
                    var noteGroups = window._findNoteGroups(svg);
                    for (var ni = 0; ni < noteGroups.length; ni++) {
                        var grp = noteGroups[ni];
                        var els = grp.paths.concat(grp.texts);
                        var found = false;
                        for (var ei = 0; ei < els.length; ei++) {
                            if (els[ei] === e.target || els[ei].contains(e.target)) {
                                found = true;
                                break;
                            }
                        }
                        if (!found) {
                            // Check by coordinate — handles transparent hoverRect overlays
                            try {
                                var bbox = window._getNoteBBox(grp);
                                var pt = svg.createSVGPoint();
                                pt.x = e.clientX; pt.y = e.clientY;
                                var svgPt = pt.matrixTransform(svg.getScreenCTM().inverse());
                                if (svgPt.x >= bbox.x && svgPt.x <= bbox.x + bbox.width &&
                                    svgPt.y >= bbox.y && svgPt.y <= bbox.y + bbox.height) {
                                    found = true;
                                }
                            } catch(ex) {}
                        }
                        if (found) { clickedNoteIdx = ni; break; }
                    }
                    if (clickedNoteIdx >= 0) {
                        // Resolve the note source and global index.
                        // For puml-fragment splits, use the fragment's source and
                        // compute the global index offset from preceding fragments.
                        var fragEl = svg.closest ? svg.closest('.puml-fragment') : null;
                        var origSrc = container._noteOriginalSource || getSource(container);
                        var noteSrc;
                        if (fragEl && fragEl !== container) {
                            noteSrc = fragEl.getAttribute('data-plantuml') || '';
                        } else {
                            noteSrc = origSrc;
                        }
                        var noteBlocks = window._parseNoteBlocks(noteSrc);

                        // When findNoteGroups returns more SVG groups than source
                        // note blocks (participant shapes misidentified as notes),
                        // match the clicked group to the correct block by content.
                        var resolvedBlockIdx = clickedNoteIdx;
                        if (noteGroups.length > noteBlocks.length && noteBlocks.length > 0) {
                            var grpText = noteGroups[clickedNoteIdx].texts.map(function(t) {
                                return t.textContent.trim();
                            }).join(' ').trim();
                            resolvedBlockIdx = -1;
                            for (var bi = 0; bi < noteBlocks.length; bi++) {
                                var blkText = noteBlocks[bi].contentLines.map(function(l) {
                                    return l.replace(/<[^>]*>/g, '').trim();
                                }).filter(function(l) { return l; }).join(' ').trim();
                                var blkStart = blkText.substring(0, Math.min(30, blkText.length));
                                if (blkStart && grpText.indexOf(blkStart) >= 0) {
                                    resolvedBlockIdx = bi;
                                    break;
                                }
                            }
                        }

                        // Compute global note index for _noteSteps lookup
                        var globalNoteIdx = resolvedBlockIdx >= 0 ? resolvedBlockIdx : clickedNoteIdx;
                        if (fragEl && fragEl !== container) {
                            var fragIdx = parseInt(fragEl.dataset.fragment || '0', 10);
                            var siblingFrags = container.querySelectorAll('.puml-fragment');
                            for (var ofi = 0; ofi < fragIdx && ofi < siblingFrags.length; ofi++) {
                                var sibSrc = siblingFrags[ofi].getAttribute('data-plantuml');
                                if (sibSrc) globalNoteIdx += window._parseNoteBlocks(sibSrc).length;
                            }
                        }

                        var noteText;
                        if (resolvedBlockIdx >= 0 && noteBlocks[resolvedBlockIdx]) {
                            noteText = noteBlocks[resolvedBlockIdx].contentLines.map(function(l) {
                                return l.replace(/^\s*<color:gray>/, '');
                            }).join('\n').trim();
                        } else {
                            noteText = noteGroups[clickedNoteIdx].texts.map(function(t) { return t.textContent; }).join('\n');
                        }

                        // Check if note is truncated or collapsed
                        var noteStep = container._noteSteps && container._noteSteps[globalNoteIdx];
                        var isNotExpanded = noteStep !== undefined && noteStep !== 2;
                        _fullNoteText = noteText;
                        _noteIsNotExpanded = isNotExpanded;

                        if (isNotExpanded) {
                            var currentSrc = fragEl ? (fragEl.getAttribute('data-plantuml') || '') : getSource(container);
                            var currentNoteBlocks = window._parseNoteBlocks(currentSrc);
                            var curBlockIdx = resolvedBlockIdx >= 0 ? resolvedBlockIdx : clickedNoteIdx;
                            var currentText;
                            if (currentNoteBlocks[curBlockIdx]) {
                                currentText = currentNoteBlocks[curBlockIdx].contentLines.map(function(l) {
                                    return l.replace(/^\s*<color:gray>/, '');
                                }).join('\n').trim();
                            } else {
                                currentText = noteGroups[clickedNoteIdx].texts.map(function(t) { return t.textContent; }).join('\n');
                            }
                            _currentNoteText = currentText;
                            menu.appendChild(createSubMenu('Copy box text', [
                                createMenuItem('Copy full box text', function() {
                                    navigator.clipboard.writeText(noteText);
                                }),
                                createMenuItem('Copy current box text', function() {
                                    navigator.clipboard.writeText(currentText);
                                })
                            ]));
                        } else {
                            menu.appendChild(createMenuItem('Copy box text', function() {
                                navigator.clipboard.writeText(noteText);
                            }));
                        }
                        menu.appendChild(createSeparator());
                    }
                }

                var selectedText = (window.getSelection() || '').toString().trim();
                // Normalize SVG text selection against original note source to remove
                // artificial newlines inserted by the browser at <text> element boundaries.
                if (selectedText && svg && clickedNoteIdx >= 0) {
                    var noteSource = _noteIsNotExpanded
                        ? (_currentNoteText || _fullNoteText)
                        : _fullNoteText;
                    if (noteSource) {
                        var selChars = selectedText.replace(/\s+/g, '');
                        var noteChars = noteSource.replace(/\s+/g, '');
                        var charIdx = noteChars.indexOf(selChars);
                        if (charIdx >= 0) {
                            var count = 0, start = -1, end = -1;
                            for (var k = 0; k < noteSource.length; k++) {
                                if (/\S/.test(noteSource[k])) {
                                    if (count === charIdx && start < 0) start = k;
                                    count++;
                                    if (count === charIdx + selChars.length) { end = k + 1; break; }
                                }
                            }
                            if (start >= 0 && end > start) {
                                selectedText = noteSource.substring(start, end).trim();
                            }
                        }
                    }
                }
                if (selectedText) {
                    menu.appendChild(createMenuItem('Copy Highlighted Text', function() {
                        navigator.clipboard.writeText(selectedText);
                    }));
                    menu.appendChild(createSeparator());
                }

                if (svg) {
                    // Full SVG menu — grouped into submenus
                    menu.appendChild(createSubMenu('Copy image', [
                        createMenuItem('Copy as PNG', function() {
                            svgToCanvas(svg, function(canvas) {
                                canvas.toBlob(function(blob) {
                                    navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                                }, 'image/png');
                            });
                        }),
                        createMenuItem('Copy as PNG (no transparency)', function() {
                            svgToCanvasWithBg(svg, function(canvas) {
                                canvas.toBlob(function(blob) {
                                    navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                                }, 'image/png');
                            });
                        }),
                        createMenuItem('Copy as SVG', function() {
                            navigator.clipboard.writeText(serializeSvg(svg));
                        })
                    ]));
                    if (source) {
                        var origSource = container._noteOriginalSource || source;
                        if (origSource !== source) {
                            menu.appendChild(createSubMenu('Copy ' + typeLabel + ' source', [
                                createMenuItem('Copy full ' + typeLabel + ' source', function() {
                                    navigator.clipboard.writeText(origSource);
                                }),
                                createMenuItem('Copy current ' + typeLabel + ' source', function() {
                                    navigator.clipboard.writeText(source);
                                })
                            ]));
                        } else {
                            menu.appendChild(createMenuItem('Copy ' + typeLabel + ' source', function() {
                                navigator.clipboard.writeText(source);
                            }));
                        }
                    }
                    menu.appendChild(createSeparator());
                    menu.appendChild(createSubMenu('Save image', [
                        createMenuItem('Save as PNG', function() {
                            svgToCanvas(svg, function(canvas) {
                                canvas.toBlob(function(blob) {
                                    var a = document.createElement('a');
                                    a.href = URL.createObjectURL(blob);
                                    a.download = getDiagramFilename(container, 'png');
                                    a.click();
                                    URL.revokeObjectURL(a.href);
                                }, 'image/png');
                            });
                        }),
                        createMenuItem('Save as PNG (no transparency)', function() {
                            svgToCanvasWithBg(svg, function(canvas) {
                                canvas.toBlob(function(blob) {
                                    var a = document.createElement('a');
                                    a.href = URL.createObjectURL(blob);
                                    a.download = getDiagramFilename(container, 'png');
                                    a.click();
                                    URL.revokeObjectURL(a.href);
                                }, 'image/png');
                            });
                        }),
                        createMenuItem('Save as SVG', function() {
                            var blob = new Blob([serializeSvg(svg)], { type: 'image/svg+xml' });
                            var a = document.createElement('a');
                            a.href = URL.createObjectURL(blob);
                            a.download = getDiagramFilename(container, 'svg');
                            a.click();
                            URL.revokeObjectURL(a.href);
                        })
                    ]));
                    menu.appendChild(createSubMenu('Open image in new tab', [
                        createMenuItem('Open as PNG image in new tab', function() {
                            svgToCanvas(svg, function(canvas) {
                                canvas.toBlob(function(blob) {
                                    window.open(URL.createObjectURL(blob));
                                }, 'image/png');
                            });
                        }),
                        createMenuItem('Open as PNG image (no transparency) in new tab', function() {
                            svgToCanvasWithBg(svg, function(canvas) {
                                canvas.toBlob(function(blob) {
                                    window.open(URL.createObjectURL(blob));
                                }, 'image/png');
                            });
                        }),
                        createMenuItem('Open as SVG image in new tab', function() {
                            var blob = new Blob([serializeSvg(svg)], { type: 'image/svg+xml' });
                            window.open(URL.createObjectURL(blob));
                        })
                    ]));
                    if (source) {
                        var origSource2 = container._noteOriginalSource || source;
                        if (origSource2 !== source) {
                            menu.appendChild(createSubMenu('Open ' + typeLabel + ' source in new tab', [
                                createMenuItem('Open full ' + typeLabel + ' in new tab', function() {
                                    var blob = new Blob([origSource2], { type: 'text/plain;charset=utf-8' });
                                    window.open(URL.createObjectURL(blob));
                                }),
                                createMenuItem('Open current ' + typeLabel + ' in new tab', function() {
                                    var blob = new Blob([source], { type: 'text/plain;charset=utf-8' });
                                    window.open(URL.createObjectURL(blob));
                                })
                            ]));
                        } else {
                            menu.appendChild(createMenuItem('Open ' + typeLabel + ' source in new tab', function() {
                                var blob = new Blob([source], { type: 'text/plain;charset=utf-8' });
                                window.open(URL.createObjectURL(blob));
                            }));
                        }
                    }
                    if (clickedNoteIdx >= 0 && _fullNoteText) {
                        if (_noteIsNotExpanded && _currentNoteText) {
                            menu.appendChild(createSubMenu('Open box text in new tab', [
                                createMenuItem('Open full box text in new tab', function() {
                                    var blob = new Blob([_fullNoteText], { type: 'text/plain;charset=utf-8' });
                                    window.open(URL.createObjectURL(blob));
                                }),
                                createMenuItem('Open current box text in new tab', function() {
                                    var blob = new Blob([_currentNoteText], { type: 'text/plain;charset=utf-8' });
                                    window.open(URL.createObjectURL(blob));
                                })
                            ]));
                        } else {
                            menu.appendChild(createMenuItem('Open box text in new tab', function() {
                                var blob = new Blob([_fullNoteText], { type: 'text/plain;charset=utf-8' });
                                window.open(URL.createObjectURL(blob));
                            }));
                        }
                    }
                } else {
                    // HTML content (flame chart, call tree) — PNG only
                    menu.appendChild(createMenuItem('Copy as PNG', function() {
                        htmlToCanvas(container, function(canvas) {
                            canvas.toBlob(function(blob) {
                                navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                            }, 'image/png');
                        });
                    }));
                    menu.appendChild(createMenuItem('Save as PNG', function() {
                        htmlToCanvas(container, function(canvas) {
                            canvas.toBlob(function(blob) {
                                var a = document.createElement('a');
                                a.href = URL.createObjectURL(blob);
                                a.download = getDiagramFilename(container, 'png');
                                a.click();
                                URL.revokeObjectURL(a.href);
                            }, 'image/png');
                        });
                    }));
                }

                if (source && diagramType === 'plantuml') {
                    var callerSource = container._noteOriginalSource || source;
                    var payloads = extractCallerPayloads(callerSource);
                    if (payloads) {
                        menu.appendChild(createMenuItem('Copy all caller request payloads', function() {
                            navigator.clipboard.writeText(payloads);
                            showToast('Copied ' + payloads.split('\n\n').length + ' request payload(s)');
                        }));
                    }
                }

                menu.appendChild(createSeparator());
                menu.appendChild(createMenuItem('Show default browser menu', function() {
                    showToast('To use the browser menu, right-click outside the diagram area.');
                }));

                document.body.appendChild(menu);

                if (!window.matchMedia('(max-width: 768px)').matches) {
                    var rect = menu.getBoundingClientRect();
                    var x = e.clientX;
                    var y = e.clientY;
                    if (x + rect.width > window.innerWidth) x = window.innerWidth - rect.width - 4;
                    if (y + rect.height > window.innerHeight) y = window.innerHeight - rect.height - 4;
                    if (x < 0) x = 0;
                    if (y < 0) y = 0;
                    menu.style.left = x + 'px';
                    menu.style.top = y + 'px';
                }
            });

            document.addEventListener('click', function(e) {
                if (menu && !menu.contains(e.target)) closeMenu();
            });
            document.addEventListener('keydown', function(e) {
                if (e.key === 'Escape') closeMenu();
            });
            document.addEventListener('scroll', closeMenu, true);

            // ── Diagram Selection ──
            var selectedDiagram = null;

            function selectDiagram(container) {
                if (selectedDiagram && selectedDiagram !== container) {
                    selectedDiagram.classList.remove('diagram-selected');
                }
                container.classList.add('diagram-selected');
                selectedDiagram = container;
            }

            function deselectDiagram() {
                if (selectedDiagram) {
                    selectedDiagram.classList.remove('diagram-selected');
                    selectedDiagram = null;
                }
            }

            document.addEventListener('click', function(e) {
                var container = findDiagramContainer(e.target);
                if (container) {
                    if (container === selectedDiagram) {
                        deselectDiagram();
                    } else {
                        selectDiagram(container);
                    }
                } else if (!e.target.closest('.diagram-zoom-controls')) {
                    deselectDiagram();
                }
            });

            document.addEventListener('keydown', function(e) {
                if (e.key === 'Escape') deselectDiagram();
            });

            // ── Zoom Helpers ──

            // Get the natural (unscaled) SVG width
            function getNaturalWidth(container) {
                var svg = getSvg(container);
                if (!svg) return 0;
                var saved = svg.style.maxWidth;
                var savedW = svg.style.width;
                svg.style.maxWidth = 'none';
                svg.style.width = '';
                var naturalW = svg.getBoundingClientRect().width;
                svg.style.maxWidth = saved;
                svg.style.width = savedW;
                return naturalW;
            }

            // Check whether an SVG diagram is wider than its container (needs zoom)
            function isDiagramZoomable(container) {
                var svg = getSvg(container);
                if (!svg) return false;
                var saved = svg.style.maxWidth;
                svg.style.maxWidth = 'none';
                var naturalW = svg.getBoundingClientRect().width;
                svg.style.maxWidth = saved;
                return naturalW > container.clientWidth + 1;
            }

            // Calculate the fit-to-width zoom percentage
            function getFitPercent(container) {
                var svg = getSvg(container);
                if (!svg) return 100;
                var savedMax = svg.style.maxWidth;
                var savedW = svg.style.width;
                svg.style.maxWidth = 'none';
                svg.style.width = '';
                var naturalW = svg.getBoundingClientRect().width;
                svg.style.maxWidth = savedMax;
                svg.style.width = savedW;
                if (naturalW <= 0) return 100;
                var pct = Math.round(container.clientWidth / naturalW * 100);
                return Math.max(1, Math.min(pct, 100));
            }

            // Track last known cursor position per container
            document.addEventListener('mousemove', function(e) {
                var c = findDiagramContainer(e.target);
                if (c) { c._lastCursorX = e.clientX; c._lastCursorY = e.clientY; }
            });

            // Apply zoom level (0-100) to a container, optionally preserving a point under cursor
            function applyZoomLevel(container, percent, cursorClientX, cursorClientY) {
                var svg = getSvg(container);
                if (!svg) return;
                var fitPct = getFitPercent(container);
                percent = Math.max(fitPct, Math.min(100, percent));

                // Get container rect
                var cRect = container.getBoundingClientRect();

                // Calculate natural width
                var savedMax = svg.style.maxWidth;
                var savedW = svg.style.width;
                svg.style.maxWidth = 'none';
                svg.style.width = '';
                var naturalW = svg.getBoundingClientRect().width;
                svg.style.maxWidth = savedMax;
                svg.style.width = savedW;

                var newWidth = naturalW * percent / 100;
                var oldWidth = svg.getBoundingClientRect().width;

                // Calculate zoom-to-point scroll adjustment
                var viewportX = 0, scrollLeftBefore = container.scrollLeft;
                if (typeof cursorClientX === 'number' && oldWidth > 0) {
                    viewportX = cursorClientX - cRect.left;
                    var svgFraction = (viewportX + container.scrollLeft) / oldWidth;
                    var newScrollLeft = svgFraction * newWidth - viewportX;

                    // Apply the new width
                    if (percent >= 100) {
                        svg.style.maxWidth = 'none';
                        svg.style.width = '';
                    } else if (percent <= fitPct) {
                        svg.style.maxWidth = '100%';
                        svg.style.width = '';
                    } else {
                        svg.style.maxWidth = 'none';
                        svg.style.width = newWidth + 'px';
                    }

                    // Set scroll after width change
                    container.scrollLeft = Math.max(0, newScrollLeft);
                } else {
                    if (percent >= 100) {
                        svg.style.maxWidth = 'none';
                        svg.style.width = '';
                    } else if (percent <= fitPct) {
                        svg.style.maxWidth = '100%';
                        svg.style.width = '';
                    } else {
                        svg.style.maxWidth = 'none';
                        svg.style.width = newWidth + 'px';
                    }
                }

                // Update container overflow
                var isZoomed = percent > fitPct;
                if (isZoomed) {
                    container.style.overflowX = 'auto';
                    container.style.overflowY = '';
                    container.style.cursor = 'grab';
                    container.classList.add('diagram-natural-size');
                } else {
                    container.style.overflowX = '';
                    container.style.overflowY = '';
                    container.style.cursor = '';
                    container.classList.remove('diagram-natural-size');
                }

                // Update slider
                var slider = container.querySelector('.diagram-zoom-slider');
                if (slider) slider.value = String(percent);
            }



            // ── Zoom Controls (slider) ──

            function addZoomButton(container) {
                if (container.querySelector('.diagram-zoom-controls')) return;
                var svg = getSvg(container);
                if (!svg) return;
                if (!isDiagramZoomable(container)) return;
                container.style.position = 'relative';

                var controls = document.createElement('div');
                controls.className = 'diagram-zoom-controls';

                var fitPct = getFitPercent(container);
                var slider = document.createElement('input');
                slider.type = 'range';
                slider.className = 'diagram-zoom-slider';
                slider.min = String(fitPct);
                slider.max = '100';
                slider.value = container.classList.contains('diagram-natural-size') ? '100' : String(fitPct);
                slider.title = 'Zoom level';
                slider.addEventListener('input', function(e) {
                    e.stopPropagation();
                    var pct = parseInt(slider.value);
                    var cx = container._lastCursorX;
                    var cy = container._lastCursorY;
                    applyZoomLevel(container, pct, cx, cy);
                });
                slider.addEventListener('click', function(e) { e.stopPropagation(); });
                controls.appendChild(slider);

                container.prepend(controls);

                // Restore zoom state on the new SVG after re-render
                restoreZoomState(container);
            }

            // Re-apply zoom inline styles after SVG re-render (innerHTML replacement destroys them)
            function restoreZoomState(container) {
                var svg = getSvg(container);
                if (!svg) return;
                if (container.classList.contains('diagram-natural-size')) {
                    svg.style.maxWidth = 'none';
                    container.style.overflowX = 'auto';
                    container.style.overflowY = '';
                    container.style.cursor = 'grab';
                } else {
                    svg.style.maxWidth = '100%';
                }
            }

            // ── Keyboard Zoom (Ctrl+Plus / Ctrl+Minus) ──

            document.addEventListener('keydown', function(e) {
                if (!e.ctrlKey && !e.metaKey) return;
                var isPlus = (e.key === '=' || e.key === '+' || e.key === 'NumpadAdd');
                var isMinus = (e.key === '-' || e.key === '_' || e.key === 'NumpadSubtract');
                if (!isPlus && !isMinus) return;

                if (!selectedDiagram) return;
                var container = selectedDiagram;
                if (!container.querySelector('.diagram-zoom-slider')) return;

                e.preventDefault();
                var slider = container.querySelector('.diagram-zoom-slider');
                var current = parseInt(slider.value);
                var range = 100 - parseInt(slider.min);
                var step = Math.max(1, Math.round(range * 0.05));
                var newVal = isPlus ? Math.min(100, current + step) : Math.max(parseInt(slider.min), current - step);
                var cx = container._lastCursorX;
                var cy = container._lastCursorY;
                applyZoomLevel(container, newVal, cx, cy);
            });

            // ── Mouse Wheel Zoom (Ctrl+Wheel only) ──

            document.addEventListener('wheel', function(e) {
                if (!e.ctrlKey && !e.metaKey) return;
                var container = findDiagramContainer(e.target);
                if (!container) return;
                var slider = container.querySelector('.diagram-zoom-slider');
                if (!slider) return;

                e.preventDefault();
                var current = parseInt(slider.value);
                var range = 100 - parseInt(slider.min);
                var step = Math.max(1, Math.round(range * 0.05));
                var delta = e.deltaY < 0 ? step : -step;
                var newVal = Math.max(parseInt(slider.min), Math.min(100, current + delta));
                applyZoomLevel(container, newVal, e.clientX, e.clientY);
            }, { passive: false });

            // Drag-to-pan when zoomed
            (function() {
                var dragging = false, dragContainer, startX, startY, scrollL;
                document.addEventListener('mousedown', function(e) {
                    var c = findDiagramContainer(e.target);
                    if (!c || !c.classList.contains('diagram-natural-size')) return;
                    if (e.target.closest('.diagram-zoom-controls')) return;
                    dragging = true;
                    dragContainer = c;
                    startX = e.pageX;
                    startY = e.clientY;
                    scrollL = c.scrollLeft;
                    c.style.cursor = 'grabbing';
                    c.style.userSelect = 'none';
                    e.preventDefault();
                });
                document.addEventListener('mousemove', function(e) {
                    if (!dragging) return;
                    dragContainer.scrollLeft = scrollL - (e.pageX - startX);
                    window.scrollBy(0, startY - e.clientY);
                    startY = e.clientY;
                });
                document.addEventListener('mouseup', function() {
                    if (!dragging) return;
                    dragging = false;
                    dragContainer.style.cursor = 'grab';
                    dragContainer.style.userSelect = '';
                });
            })();

            window._addZoomButton = addZoomButton;

            // Lazily add zoom buttons when diagram containers scroll into view.
            // A per-container MutationObserver waits for the SVG to render before
            // checking whether the diagram is wide enough to need a zoom toggle.
            (function() {
                var zoomIO = new IntersectionObserver(function(entries) {
                    entries.forEach(function(entry) {
                        if (!entry.isIntersecting) return;
                        var container = entry.target;
                        zoomIO.unobserve(container);
                        // SVG may already be present (server-rendered / inline)
                        if (getSvg(container)) { requestAnimationFrame(function() { addZoomButton(container); }); return; }
                        // Otherwise wait for the PlantUML WASM render to insert the SVG
                        var mo = new MutationObserver(function() {
                            if (!getSvg(container)) return;
                            mo.disconnect();
                            requestAnimationFrame(function() { addZoomButton(container); });
                        });
                        mo.observe(container, { childList: true, subtree: true });
                    });
                }, { rootMargin: '200px' });
                function observeAll() {
                    document.querySelectorAll('[data-diagram-type]').forEach(function(c) {
                        zoomIO.observe(c);
                    });
                }
                if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', observeAll);
                } else {
                    observeAll();
                }
            })();
        })();
        </script>
        """;

    public static string GetInternalFlowPopupScript() => """
        <script>
        (function() {
            var iflowData = window.__iflowSegments || {};

            function showPopup(segmentId) {
                var existing = document.querySelector('.iflow-overlay');
                if (existing) existing.remove();

                var overlay = document.createElement('div');
                overlay.className = 'iflow-overlay';

                var popup = document.createElement('div');
                popup.className = 'iflow-popup';

                var closeBtn = document.createElement('button');
                closeBtn.className = 'iflow-popup-close';
                closeBtn.innerHTML = '&times;';
                closeBtn.onclick = function() { overlay.remove(); };
                popup.appendChild(closeBtn);

                var segment = iflowData[segmentId];
                if (segment && segment.content) {
                    var header = document.createElement('h3');
                    header.textContent = segment.title || 'Internal Flow';
                    popup.appendChild(header);

                    var diagramDiv = document.createElement('div');
                    diagramDiv.className = 'iflow-diagram';
                    diagramDiv.innerHTML = segment.content;
                    popup.appendChild(diagramDiv);

                    // Render flame chart from data if available
                    if (segment.flameData && window._renderPopupFlameCharts) {
                        window._renderPopupFlameCharts(diagramDiv, segment.flameData);
                    }

                    if (window.plantuml && diagramDiv.querySelector('.plantuml-browser')) {
                        diagramDiv.querySelectorAll('.plantuml-browser').forEach(function(el) {
                            el.dataset.queued = '1';
                            function renderEl(source) {
                                try {
                                    var lines = source.split('\n');
                                    if (lines.length > 3000) {
                                        el.dataset.rendered = '1';
                                        el.innerHTML = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px">' +
                                            '<strong>Activity diagram too large for browser rendering (' + lines.length + ' lines).</strong><br>' +
                                            'Use <code>CallTree</code> style for large relationship flows.</div>';
                                        return;
                                    }
                                    var mo = new MutationObserver(function() {
                                        mo.disconnect();
                                        el.dataset.rendered = '1';
                                    });
                                    mo.observe(el, { childList: true, subtree: true });
                                    window.plantuml.render(lines, el.id);
                                    setTimeout(function() {
                                        var text = el.textContent || '';
                                        if (text.indexOf('RuntimeException') >= 0 || text.indexOf('RangeError') >= 0) {
                                            mo.disconnect();
                                            el.dataset.rendered = '1';
                                            el.innerHTML = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px">' +
                                                '<strong>Activity diagram too large for browser rendering.</strong><br>' +
                                                'Use <code>CallTree</code> style for large relationship flows.</div>';
                                        }
                                    }, 100);
                                } catch(e) {
                                    el.dataset.rendered = '1';
                                    el.textContent = 'Activity diagram too large for browser rendering. Use CallTree style instead.';
                                    el.style.color = '#c00';
                                }
                            }
                            var source = el.getAttribute('data-plantuml');
                            if (source) {
                                renderEl(source);
                            } else {
                                var pumlZ = window._getPumlZ ? window._getPumlZ(el) : el.getAttribute('data-plantuml-z');
                                if (pumlZ) {
                                    decompressGzipBase64(pumlZ).then(function(decoded) {
                                        el.setAttribute('data-plantuml', decoded);
                                        renderEl(decoded);
                                    }).catch(function() { el.dataset.rendered = '1'; el.textContent = 'Decompression error'; });
                                }
                            }
                        });
                    }
                } else {
                    var noData = document.createElement('div');
                    noData.className = 'iflow-no-data';
                    noData.textContent = segment && segment.message
                        ? segment.message
                        : 'No internal flow data available for this segment.';
                    popup.appendChild(noData);
                }

                overlay.appendChild(popup);

                // Wire up toggle buttons if present
                var toggleBtns = popup.querySelectorAll('.iflow-toggle-btn');
                if (toggleBtns.length) {
                    toggleBtns.forEach(function(btn) {
                        btn.addEventListener('click', function() {
                            var view = btn.getAttribute('data-view');
                            var container = popup.querySelector('.iflow-diagram');
                            if (!container) return;
                            toggleBtns.forEach(function(b) { b.classList.remove('iflow-toggle-active'); });
                            btn.classList.add('iflow-toggle-active');
                            var main = container.querySelector('.iflow-view-main');
                            var flame = container.querySelector('.iflow-view-flame');
                            if (main) main.style.display = view === 'main' ? '' : 'none';
                            if (flame) flame.style.display = view === 'flame' ? '' : 'none';
                        });
                    });
                }

                overlay.addEventListener('click', function(e) {
                    if (e.target === overlay) overlay.remove();
                });
                document.body.appendChild(overlay);
            }

            // Expose for direct binding from the render script
            window._iflowShowPopup = showPopup;

            // Fallback: document-level click handler (capture phase for IKVM/server SVG compatibility)
            document.addEventListener('click', function(e) {
                var el = e.target;
                while (el && el !== document) {
                    if (el.localName === 'a') {
                        var href = el.getAttribute('xlink:href') || el.getAttribute('href') || '';
                        if (href.indexOf('#iflow-') === 0) {
                            e.preventDefault();
                            e.stopPropagation();
                            showPopup(href.substring(1));
                            return;
                        }
                    }
                    el = el.parentNode;
                }
            }, true);

            document.addEventListener('keydown', function(e) {
                if (e.key === 'Escape') {
                    var overlay = document.querySelector('.iflow-overlay');
                    if (overlay) overlay.remove();
                }
            });
        })();
        </script>
        """;

    public static string GetToggleScript() => """
        <script>
        document.addEventListener('click', function(e) {
            var btn = e.target.closest ? e.target.closest('.iflow-toggle-btn') : null;
            if (!btn) return;
            var toggle = btn.closest('.iflow-toggle');
            if (!toggle) return;
            var container = toggle.parentElement;
            if (!container) return;
            var view = btn.getAttribute('data-view');
            toggle.querySelectorAll('.iflow-toggle-btn').forEach(function(b) {
                b.classList.toggle('iflow-toggle-active', b === btn);
            });
            container.querySelectorAll('.iflow-view').forEach(function(v) {
                v.style.display = 'none';
            });
            var target = container.querySelector('.iflow-view-' + view);
            if (target) {
                target.style.display = '';
                window._renderFlameCharts(target);
            }
        });
        document.addEventListener('click', function(e) {
            var btn = e.target.closest ? e.target.closest('.diagram-toggle-btn') : null;
            if (!btn) return;
            var toggle = btn.closest('.diagram-toggle');
            if (!toggle) return;
            var container = toggle.parentElement;
            if (!container) return;
            var dtype = btn.getAttribute('data-dtype');
            toggle.querySelectorAll('.diagram-toggle-btn').forEach(function(b) {
                b.classList.toggle('diagram-toggle-active', b === btn);
            });
            container.querySelectorAll('.diagram-view').forEach(function(v) {
                v.style.display = 'none';
            });
            var target = container.querySelector('.diagram-view-' + dtype);
            if (target) {
                target.style.display = '';
                if (window._renderFlameCharts) window._renderFlameCharts(target);
            }
        });
        </script>
        """;

    /// <summary>
    /// Client-side JavaScript that renders flame charts from compact JSON data.
    /// Flame chart elements with a <c>data-flame</c> attribute or inside popups
    /// with <c>flameData</c> are rendered on demand instead of being pre-rendered
    /// as HTML on the server, dramatically reducing report file size.
    /// </summary>
    public static string GetFlameChartRenderScript() => """
        <script>
        (function() {
            var MIN_BAR_PCT = 0; // minimum display width enforced via CSS min-width

            function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {
                var hue = Math.abs(hashCode(source)) % 360;
                var lightness = 70 + Math.min(depth * 5, 20);
                var durText = durMs >= 1 ? ' (' + durMs + 'ms)' : '';
                var pctText = totalDurMs > 0 ? ' — ' + (durMs / totalDurMs * 100).toFixed(1) + '% of total' : '';
                return '<div class="iflow-flame-bar" style="margin-left:' + leftPct.toFixed(2)
                    + '%;width:' + widthPct.toFixed(2) + '%;background:hsl(' + hue + ', 60%, ' + lightness
                    + '%)" data-left="' + leftPct.toFixed(4) + '" data-width="' + widthPct.toFixed(4)
                    + '" title="[' + escHtml(source) + '] ' + escHtml(name) + durText + pctText
                    + '"><span class="iflow-flame-label">' + escHtml(name) + durText + '</span></div>';
            }

            function renderFlameData(container, data, viewLeft, viewRight) {
                if (!data || !data.s || !data.f || data.f.length === 0) return;
                if (!viewLeft && !viewRight && container.dataset.flameRendered) return;
                container.dataset.flameRendered = '1';

                var sources = data.s;
                var hasMarkers = data.m && data.m.length > 0;
                if (hasMarkers) container.style.position = 'relative';
                var isZoomed = viewLeft != null && viewRight != null;
                var vl = viewLeft || 0, vr = viewRight || 100;
                var vw = vr - vl;

                // Compute total duration from max span end minus min span start
                var totalDurMs = 0;
                if (data.f.length > 0) {
                    var minLeft = 100, maxRight = 0;
                    for (var i = 0; i < data.f.length; i++) {
                        var sp = data.f[i];
                        if (sp[2] < minLeft) minLeft = sp[2];
                        var right = sp[2] + sp[3];
                        if (right > maxRight) maxRight = right;
                    }
                    // Sum leaf durations for total (approximate)
                    for (var i = 0; i < data.f.length; i++) totalDurMs += data.f[i][5] || 0;
                }

                var html = [];
                if (isZoomed) {
                    html.push('<div class="iflow-flame-zoom-hint" title="Double-click to reset zoom">🔍 Zoomed — double-click to reset</div>');
                }
                if (hasMarkers) {
                    for (var mi = 0; mi < data.m.length; mi++) {
                        var m = data.m[mi];
                        var mPos = isZoomed ? (m[0] - vl) / vw * 100 : m[0];
                        if (isZoomed && (m[0] < vl || m[0] > vr)) continue;
                        html.push('<div class="iflow-boundary-marker" style="left:' + mPos.toFixed(2) + '%" title="' + escHtml(m[1]) + '"></div>');
                    }
                }
                for (var i = 0; i < data.f.length; i++) {
                    var sp = data.f[i];
                    var srcIdx = sp[0], name = sp[1], leftPct = sp[2], widthPct = sp[3], depth = sp[4], durMs = sp[5];
                    if (isZoomed) {
                        var right = leftPct + widthPct;
                        if (right < vl || leftPct > vr) continue; // outside viewport
                        var clampedLeft = Math.max(leftPct, vl);
                        var clampedRight = Math.min(right, vr);
                        leftPct = (clampedLeft - vl) / vw * 100;
                        widthPct = (clampedRight - clampedLeft) / vw * 100;
                    }
                    var source = sources[srcIdx];
                    html.push(buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs));
                }
                container.innerHTML = html.join('');
            }

            function renderSequentialFlameData(container, data) {
                if (!data || !data.s || !data.b || data.b.length === 0) return;
                if (container.dataset.flameRendered) return;
                container.dataset.flameRendered = '1';
                var sources = data.s;
                var html = [];
                for (var bi = 0; bi < data.b.length; bi++) {
                    var band = data.b[bi];
                    var totalDurMs = 0;
                    for (var i = 0; i < band.f.length; i++) totalDurMs += band.f[i][5] || 0;
                    html.push('<div class="iflow-test-band"><div class="iflow-test-band-label">' + escHtml(band.id) + '</div>');
                    for (var i = 0; i < band.f.length; i++) {
                        var sp = band.f[i];
                        var srcIdx = sp[0], name = sp[1], leftPct = sp[2], widthPct = sp[3], depth = sp[4], durMs = sp[5];
                        var source = sources[srcIdx];
                        html.push(buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs));
                    }
                    html.push('</div>');
                }
                container.innerHTML = html.join('');
            }

            function escHtml(s) {
                return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
            }

            function hashCode(s) {
                var h = 0;
                for (var i = 0; i < s.length; i++) {
                    h = ((h << 5) - h + s.charCodeAt(i)) | 0;
                }
                return h;
            }

            // Decompress gzip+base64 data (used for whole-test-flow compressed attributes)
            function decompressBase64(base64) {
                var raw = atob(base64);
                var bytes = new Uint8Array(raw.length);
                for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
                var stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
                return new Response(stream).text();
            }

            // Click-to-zoom: click a bar to zoom into its time range, double-click to reset
            function attachZoomHandlers(container, data) {
                container.addEventListener('click', function(e) {
                    var bar = e.target.closest('.iflow-flame-bar');
                    if (!bar) return;
                    var left = parseFloat(bar.getAttribute('data-left'));
                    var width = parseFloat(bar.getAttribute('data-width'));
                    if (isNaN(left) || isNaN(width) || width <= 0) return;
                    // Zoom to this bar with 5% padding on each side
                    var pad = width * 0.05;
                    var vl = Math.max(0, left - pad);
                    var vr = Math.min(100, left + width + pad);
                    container.dataset.flameRendered = '';
                    renderFlameData(container, data, vl, vr);
                    container._flameData = data;
                });
                container.addEventListener('dblclick', function(e) {
                    e.preventDefault();
                    var d = container._flameData || data;
                    container.dataset.flameRendered = '';
                    renderFlameData(container, d);
                    container._flameData = d;
                });
            }

            // Render all data-flame elements within a container (or document)
            function renderFlameCharts(root) {
                var els = (root || document).querySelectorAll('.iflow-flame[data-flame]');
                for (var i = 0; i < els.length; i++) {
                    if (els[i].dataset.flameRendered) continue;
                    try {
                        var data = JSON.parse(els[i].getAttribute('data-flame'));
                        renderFlameData(els[i], data);
                        attachZoomHandlers(els[i], data);
                    } catch(e) {}
                }
                // Handle compressed flame data (whole-test-flow)
                var zEls = (root || document).querySelectorAll('.iflow-flame[data-flame-z]');
                for (var i = 0; i < zEls.length; i++) {
                    if (zEls[i].dataset.flameRendered) continue;
                    (function(el) {
                        el.dataset.flameRendered = '1';
                        decompressBase64(el.getAttribute('data-flame-z')).then(function(json) {
                            el.dataset.flameRendered = '';
                            var data = JSON.parse(json);
                            renderFlameData(el, data);
                            attachZoomHandlers(el, data);
                        }).catch(function() {});
                    })(zEls[i]);
                }
                var seqEls = (root || document).querySelectorAll('.iflow-sequential-tests[data-flame]');
                for (var i = 0; i < seqEls.length; i++) {
                    if (seqEls[i].dataset.flameRendered) continue;
                    try {
                        var data = JSON.parse(seqEls[i].getAttribute('data-flame'));
                        renderSequentialFlameData(seqEls[i], data);
                    } catch(e) {}
                }
            }

            // Render flame charts from flameData property in popup segments
            function renderPopupFlameCharts(container, flameData) {
                if (!flameData) return;
                var el = container.querySelector('.iflow-flame[data-diagram-type="flamechart"]');
                if (el) {
                    renderFlameData(el, flameData);
                    attachZoomHandlers(el, flameData);
                }
            }

            // Expose globally
            window._renderFlameCharts = renderFlameCharts;
            window._renderPopupFlameCharts = renderPopupFlameCharts;

            // Auto-render visible data-flame elements on page load
            document.addEventListener('DOMContentLoaded', function() {
                renderFlameCharts(document);

                // Render on details expand
                document.addEventListener('toggle', function(e) {
                    if (e.target.tagName === 'DETAILS' && e.target.open) {
                        renderFlameCharts(e.target);
                    }
                }, true);
            });
        })();
        </script>
        """;

    public static string GetCollapsibleNotesScript() => CollapsibleNotesScriptContent;

    private const string CollapsibleNotesScriptContent = """
        <script>
        (function() {
            var SVGNS = 'http://www.w3.org/2000/svg';

            function parseNoteBlocks(source) {
                if (!source) return [];
                var lines = source.split('\n');
                var notes = [];
                for (var i = 0; i < lines.length; i++) {
                    var trimmed = lines[i].trim();
                    if (/^note(?:<<\w+>>)?\s+(left|right)/.test(trimmed)) {
                        var start = i;
                        i++;
                        var contentLines = [];
                        while (i < lines.length && lines[i].trim() !== 'end note') {
                            contentLines.push(lines[i]);
                            i++;
                        }
                        notes.push({ start: start, end: i, contentLines: contentLines });
                    }
                }
                return notes;
            }

            function getNotePreview(contentLines) {
                var nonGray = contentLines.map(function(l) { return l.trim(); })
                    .filter(function(l) { return !l.match(/^<color:gray>/); });
                var raw = nonGray.join(' ').trim();
                if (!raw) return '';
                if (raw.length <= 60) return raw;
                return raw.substring(0, 60) + '...';
            }

            function hasNoteFill(pathEl) {
                var fill = (pathEl.getAttribute('fill') || '').toLowerCase().trim();
                if (!fill || fill === 'none' || fill === 'transparent') return false;
                if (fill === '#000000' || fill === '#000' || fill === 'black' || fill === 'rgb(0,0,0)') return false;
                if (/^#[0-9a-f]{6}00$/.test(fill)) return false;
                return true;
            }

            // Detect the small triangular fold path that is characteristic of
            // PlantUML note shapes. This distinguishes notes from participant
            // shapes (entity boxes, database cylinders, queue shapes), which
            // also produce path+text groups but never have a fold triangle.
            // Works regardless of theme or note fill color.
            function hasNoteFoldTriangle(paths) {
                if (paths.length < 2) return false;
                try {
                    // Find the largest path as the body reference (not always paths[0]
                    // when PlantUML adds extra decorative paths for large/anchored notes)
                    var bodyBB = null;
                    var bodyIdx = -1;
                    for (var bi = 0; bi < paths.length; bi++) {
                        var bb = paths[bi].getBBox();
                        if (bb.width <= 0 || bb.height <= 0) continue;
                        if (!bodyBB || (bb.width * bb.height) > (bodyBB.width * bodyBB.height)) {
                            bodyBB = bb;
                            bodyIdx = bi;
                        }
                    }
                    if (!bodyBB) return false;
                    for (var pi = 0; pi < paths.length; pi++) {
                        if (pi === bodyIdx) continue;
                        var fBB = paths[pi].getBBox();
                        if (fBB.width <= 0 || fBB.height <= 0) continue;
                        var smallEnough = (fBB.width < bodyBB.width * 0.5 && fBB.height < bodyBB.height * 0.5)
                            || (fBB.width < 25 && fBB.height < 25);
                        if (!smallEnough) continue;
                        if (fBB.width > bodyBB.width * 0.9 && fBB.height > bodyBB.height * 0.9) continue;
                        var tol = 3;
                        var atRight = Math.abs((fBB.x + fBB.width) - (bodyBB.x + bodyBB.width)) < tol;
                        var atLeft = Math.abs(fBB.x - bodyBB.x) < tol;
                        var atTop = Math.abs(fBB.y - bodyBB.y) < tol;
                        var atBot = Math.abs((fBB.y + fBB.height) - (bodyBB.y + bodyBB.height)) < tol;
                        if ((atRight || atLeft) && (atTop || atBot)) return true;
                    }
                } catch(e) {}
                return false;
            }

            function findNoteGroups(svg) {
                var mainG = null;
                for (var i = 0; i < svg.children.length; i++) {
                    if (svg.children[i].tagName === 'g') { mainG = svg.children[i]; break; }
                }
                if (!mainG) return [];
                var children = Array.from(mainG.children);
                var candidates = [];
                var ci = 0;
                while (ci < children.length) {
                    if (children[ci].tagName === 'g') { ci++; continue; }
                    if (children[ci].tagName === 'path' && hasNoteFill(children[ci])) {
                        var grp = { paths: [], texts: [] };
                        var startFill = (children[ci].getAttribute('fill') || '').toLowerCase();
                        while (ci < children.length && children[ci].tagName === 'path') {
                            var pFill = (children[ci].getAttribute('fill') || '').toLowerCase();
                            var sameFill = pFill === startFill;
                            var transparent = !pFill || pFill === 'none' || pFill === 'transparent'
                                || pFill === '#00000000' || /^#[0-9a-f]{6}00$/.test(pFill);
                            if (!sameFill && !transparent && hasNoteFill(children[ci])) break;
                            grp.paths.push(children[ci]);
                            ci++;
                        }
                        // Compute note bounding box as the union of ALL collected paths
                        var noteBox = null;
                        for (var nbi = 0; nbi < grp.paths.length; nbi++) {
                            try {
                                var bb = grp.paths[nbi].getBBox();
                                if (bb.width <= 0 && bb.height <= 0) continue;
                                if (!noteBox) {
                                    noteBox = { x: bb.x, y: bb.y, right: bb.x + bb.width, bottom: bb.y + bb.height };
                                } else {
                                    if (bb.x < noteBox.x) noteBox.x = bb.x;
                                    if (bb.y < noteBox.y) noteBox.y = bb.y;
                                    if (bb.x + bb.width > noteBox.right) noteBox.right = bb.x + bb.width;
                                    if (bb.y + bb.height > noteBox.bottom) noteBox.bottom = bb.y + bb.height;
                                }
                            } catch(e) {}
                        }
                        // Collect text elements. PlantUML Creole markup (e.g. ..text..
                        // separators) inserts <line>, <path>, <rect>, <circle> elements
                        // inside notes between paths and texts. Skip any non-text
                        // element that is visually inside the note bounding box.
                        while (ci < children.length) {
                            var tag = children[ci].tagName;
                            if (tag === 'text') { grp.texts.push(children[ci]); ci++; }
                            else if (noteBox && tag !== 'g') {
                                try {
                                    var ebb = children[ci].getBBox();
                                    if (ebb.x >= noteBox.x - 2 && ebb.x + ebb.width <= noteBox.right + 2
                                        && ebb.y >= noteBox.y - 2 && ebb.y + ebb.height <= noteBox.bottom + 2) {
                                        ci++;
                                    } else { break; }
                                } catch(e) { break; }
                            }
                            else { break; }
                        }
                        if (grp.paths.length > 0 && grp.texts.length > 0) {
                            candidates.push(grp);
                        }
                    } else {
                        ci++;
                    }
                }
                // Sweep for orphaned text elements that fall within a candidate's
                // bounding box but weren't collected in the forward scan. This
                // handles PlantUML Creole separators (..text..) which render text
                // and line elements BEFORE the note's path elements in DOM order.
                candidates.forEach(function(grp) {
                    var nb = null;
                    for (var nbi = 0; nbi < grp.paths.length; nbi++) {
                        try {
                            var pbb = grp.paths[nbi].getBBox();
                            if (pbb.width <= 0 && pbb.height <= 0) continue;
                            if (!nb) { nb = { x: pbb.x, y: pbb.y, r: pbb.x + pbb.width, b: pbb.y + pbb.height }; }
                            else {
                                if (pbb.x < nb.x) nb.x = pbb.x;
                                if (pbb.y < nb.y) nb.y = pbb.y;
                                if (pbb.x + pbb.width > nb.r) nb.r = pbb.x + pbb.width;
                                if (pbb.y + pbb.height > nb.b) nb.b = pbb.y + pbb.height;
                            }
                        } catch(e) {}
                    }
                    if (!nb) return;
                    var collected = new Set(grp.texts);
                    for (var oi = 0; oi < children.length; oi++) {
                        if (children[oi].tagName !== 'text') continue;
                        if (collected.has(children[oi])) continue;
                        try {
                            var tb = children[oi].getBBox();
                            if (tb.x >= nb.x - 2 && tb.x + tb.width <= nb.r + 2
                                && tb.y >= nb.y - 2 && tb.y + tb.height <= nb.b + 2) {
                                grp.texts.push(children[oi]);
                            }
                        } catch(e) {}
                    }
                });

                // Filter to groups with the note fold triangle — this excludes
                // participant shapes (entity/database/queue boxes) that also
                // produce path+text groups but lack the fold. Falls back to all
                // candidates if fold detection finds nothing (unusual rendering).
                var foldGroups = candidates.filter(function(g) { return hasNoteFoldTriangle(g.paths); });
                return foldGroups.length > 0 ? foldGroups : candidates;
            }

            function getNoteBBox(grp) {
                var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
                grp.paths.concat(grp.texts).forEach(function(el) {
                    try {
                        var bb = el.getBBox();
                        if (bb.x < minX) minX = bb.x;
                        if (bb.y < minY) minY = bb.y;
                        if (bb.x + bb.width > maxX) maxX = bb.x + bb.width;
                        if (bb.y + bb.height > maxY) maxY = bb.y + bb.height;
                    } catch(e) {}
                });
                return { x: minX, y: minY, width: maxX - minX, height: maxY - minY };
            }

            function isLongNote(contentLines, truncateLines, headersHidden) {
                var limit = truncateLines || window._truncateLines;
                if (!contentLines) return false;
                if (!headersHidden) return contentLines.length > limit;
                var count = 0;
                var afterGray = false;
                for (var i = 0; i < contentLines.length; i++) {
                    var trimmed = contentLines[i].trim();
                    if (/^<color:gray>/.test(trimmed)) { afterGray = true; continue; }
                    if (afterGray && trimmed === '') continue;
                    afterGray = false;
                    count++;
                }
                return count > limit;
            }

            // noteStep: 0=collapsed, 1=truncated, 2=expanded (3=truncated on way back down)
            function noteStepState(step) {
                if (step === 0) return 'collapsed';
                if (step === 2) return 'expanded';
                return 'truncated';
            }

            function createNoteButtons(svg, bbox, noteStep, onExpand, onContract, onTruncate, onCycle, contentLines, grp, container) {
                var size = 12;
                var topSize = 14;
                var pad = 3;
                var state = noteStepState(noteStep);
                var hdrHidden = container._headersHidden !== undefined ? container._headersHidden : (container.parentElement && container.parentElement._headersHidden) || window._headersHidden;
                var longNote = isLongNote(contentLines, container._truncateLines, hdrHidden);
                var buttons = [];

                // Top-right area: contract buttons — shown when expanded or truncated
                if (state === 'expanded' || state === 'truncated') {
                    // For expanded long notes: ▴ arrow to the left of −
                    if (state === 'expanded' && longNote) {
                        var ax = bbox.x + bbox.width - topSize * 2 - pad * 2;
                        var ay = bbox.y + pad;
                        var ga = document.createElementNS(SVGNS, 'g');
                        ga.setAttribute('class', 'note-toggle-icon');
                        ga.style.cursor = 'pointer';
                        ga.style.opacity = '0';
                        var bgA = document.createElementNS(SVGNS, 'rect');
                        bgA.setAttribute('x', ax); bgA.setAttribute('y', ay);
                        bgA.setAttribute('width', topSize); bgA.setAttribute('height', topSize);
                        bgA.setAttribute('rx', '2'); bgA.setAttribute('fill', '#ffffff');
                        bgA.setAttribute('stroke', '#999'); bgA.setAttribute('stroke-width', '0.5');
                        ga.appendChild(bgA);
                        var symA = document.createElementNS(SVGNS, 'text');
                        symA.setAttribute('x', ax + topSize / 2); symA.setAttribute('y', ay + topSize - 3);
                        symA.setAttribute('text-anchor', 'middle'); symA.setAttribute('font-size', '12');
                        symA.setAttribute('font-family', 'sans-serif'); symA.setAttribute('fill', '#666');
                        symA.style.pointerEvents = 'none';
                        symA.textContent = '\u25B2'; // ▲
                        ga.appendChild(symA);
                        bgA.addEventListener('click', function(ev) { ev.stopPropagation(); onTruncate(); });
                        bgA.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                        buttons.push(ga);
                    }
                    // − (minus) button — top-right
                    var ix = bbox.x + bbox.width - topSize - pad;
                    var iy = bbox.y + pad;
                    var gc = document.createElementNS(SVGNS, 'g');
                    gc.setAttribute('class', 'note-toggle-icon');
                    gc.setAttribute('data-note-btn', 'minus');
                    gc.style.cursor = 'pointer';
                    gc.style.opacity = '0';
                    var bgC = document.createElementNS(SVGNS, 'rect');
                    bgC.setAttribute('x', ix); bgC.setAttribute('y', iy);
                    bgC.setAttribute('width', topSize); bgC.setAttribute('height', topSize);
                    bgC.setAttribute('rx', '2'); bgC.setAttribute('fill', '#ffffff');
                    bgC.setAttribute('stroke', '#999'); bgC.setAttribute('stroke-width', '0.5');
                    gc.appendChild(bgC);
                    var symC = document.createElementNS(SVGNS, 'line');
                    symC.setAttribute('x1', ix + 3); symC.setAttribute('y1', iy + topSize / 2);
                    symC.setAttribute('x2', ix + topSize - 3); symC.setAttribute('y2', iy + topSize / 2);
                    symC.setAttribute('stroke', '#666'); symC.setAttribute('stroke-width', '2');
                    symC.setAttribute('stroke-linecap', 'round');
                    symC.style.pointerEvents = 'none';
                    gc.appendChild(symC);
                    bgC.addEventListener('click', function(ev) { ev.stopPropagation(); onContract(); });
                    bgC.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                    buttons.push(gc);
                }

                // + (plus) button — top-right, shown when collapsed
                if (state === 'collapsed') {
                    var px = bbox.x + bbox.width - topSize - pad;
                    var py = bbox.y + pad;
                    var gp = document.createElementNS(SVGNS, 'g');
                    gp.setAttribute('class', 'note-toggle-icon');
                    gp.setAttribute('data-note-btn', 'plus');
                    gp.style.cursor = 'pointer';
                    gp.style.opacity = '0';
                    var bgP = document.createElementNS(SVGNS, 'rect');
                    bgP.setAttribute('x', px); bgP.setAttribute('y', py);
                    bgP.setAttribute('width', topSize); bgP.setAttribute('height', topSize);
                    bgP.setAttribute('rx', '2'); bgP.setAttribute('fill', '#ffffff');
                    bgP.setAttribute('stroke', '#999'); bgP.setAttribute('stroke-width', '0.5');
                    gp.appendChild(bgP);
                    var symPH = document.createElementNS(SVGNS, 'line');
                    symPH.setAttribute('x1', px + 3); symPH.setAttribute('y1', py + topSize / 2);
                    symPH.setAttribute('x2', px + topSize - 3); symPH.setAttribute('y2', py + topSize / 2);
                    symPH.setAttribute('stroke', '#666'); symPH.setAttribute('stroke-width', '2');
                    symPH.setAttribute('stroke-linecap', 'round');
                    symPH.style.pointerEvents = 'none';
                    gp.appendChild(symPH);
                    var symPV = document.createElementNS(SVGNS, 'line');
                    symPV.setAttribute('x1', px + topSize / 2); symPV.setAttribute('y1', py + 3);
                    symPV.setAttribute('x2', px + topSize / 2); symPV.setAttribute('y2', py + topSize - 3);
                    symPV.setAttribute('stroke', '#666'); symPV.setAttribute('stroke-width', '2');
                    symPV.setAttribute('stroke-linecap', 'round');
                    symPV.style.pointerEvents = 'none';
                    gp.appendChild(symPV);
                    bgP.addEventListener('click', function(ev) { ev.stopPropagation(); onExpand(); });
                    bgP.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                    buttons.push(gp);
                }

                // Bottom-center expand button (▾) — shown when collapsed or truncated
                if (state === 'collapsed' || state === 'truncated') {
                    var expandW = size * 3;
                    var expandH = size;
                    var ex = bbox.x + (bbox.width - expandW) / 2;
                    var ey = bbox.y + bbox.height - expandH - pad;
                    var ge = document.createElementNS(SVGNS, 'g');
                    ge.setAttribute('class', 'note-toggle-icon');
                    ge.style.cursor = 'pointer';
                    ge.style.opacity = '0';
                    var bgE = document.createElementNS(SVGNS, 'rect');
                    bgE.setAttribute('x', ex); bgE.setAttribute('y', ey);
                    bgE.setAttribute('width', expandW); bgE.setAttribute('height', expandH);
                    bgE.setAttribute('rx', '2'); bgE.setAttribute('fill', '#ffffff');
                    bgE.setAttribute('stroke', '#999'); bgE.setAttribute('stroke-width', '0.5');
                    ge.appendChild(bgE);
                    var symE = document.createElementNS(SVGNS, 'text');
                    symE.setAttribute('x', ex + expandW / 2); symE.setAttribute('y', ey + expandH - 2.5);
                    symE.setAttribute('text-anchor', 'middle'); symE.setAttribute('font-size', '10');
                    symE.setAttribute('font-family', 'sans-serif'); symE.setAttribute('fill', '#666');
                    symE.style.pointerEvents = 'none';
                    symE.textContent = '\u25BC'; // ▼
                    ge.appendChild(symE);
                    bgE.addEventListener('click', function(ev) { ev.stopPropagation(); onExpand(); });
                    bgE.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                    buttons.push(ge);
                }

                // Bottom-center contract button (▴) — shown when expanded and long note
                if (state === 'expanded' && longNote) {
                    var contractW = size * 3;
                    var contractH = size;
                    var cx = bbox.x + (bbox.width - contractW) / 2;
                    var cy = bbox.y + bbox.height - contractH - pad;
                    var gbc = document.createElementNS(SVGNS, 'g');
                    gbc.setAttribute('class', 'note-toggle-icon');
                    gbc.style.cursor = 'pointer';
                    gbc.style.opacity = '0';
                    var bgBC = document.createElementNS(SVGNS, 'rect');
                    bgBC.setAttribute('x', cx); bgBC.setAttribute('y', cy);
                    bgBC.setAttribute('width', contractW); bgBC.setAttribute('height', contractH);
                    bgBC.setAttribute('rx', '2'); bgBC.setAttribute('fill', '#ffffff');
                    bgBC.setAttribute('stroke', '#999'); bgBC.setAttribute('stroke-width', '0.5');
                    gbc.appendChild(bgBC);
                    var symBC = document.createElementNS(SVGNS, 'text');
                    symBC.setAttribute('x', cx + contractW / 2); symBC.setAttribute('y', cy + contractH - 2.5);
                    symBC.setAttribute('text-anchor', 'middle'); symBC.setAttribute('font-size', '10');
                    symBC.setAttribute('font-family', 'sans-serif'); symBC.setAttribute('fill', '#666');
                    symBC.style.pointerEvents = 'none';
                    symBC.textContent = '\u25B2'; // ▲
                    gbc.appendChild(symBC);
                    bgBC.addEventListener('click', function(ev) { ev.stopPropagation(); onTruncate(); });
                    bgBC.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                    buttons.push(gbc);
                }

                // Hover detection via the note's own SVG elements (paths = background, texts = content).
                // This avoids an overlay rect that would block native text selection.
                var _noteHideTimeout;
                function _noteShowButtons() {
                    clearTimeout(_noteHideTimeout);
                    // For tall notes, reposition top-right buttons to the visible
                    // portion of the note so they're not scrolled off-screen
                    if (bbox.height > 500 && svg.getScreenCTM) {
                        try {
                            var ctm = svg.getScreenCTM();
                            if (ctm) {
                                var noteScreenTop = bbox.y * ctm.d + ctm.f;
                                var noteScreenBot = (bbox.y + bbox.height) * ctm.d + ctm.f;
                                var visTop = Math.max(0, noteScreenTop);
                                var visSvgY = (visTop - ctm.f) / ctm.d;
                                var btnY = Math.max(bbox.y + pad, Math.min(visSvgY + pad, bbox.y + bbox.height - topSize * 2 - pad));
                                buttons.forEach(function(b) {
                                    var btn = b.getAttribute('data-note-btn');
                                    if (btn === 'minus' || btn === 'plus') {
                                        var rects = b.querySelectorAll('rect');
                                        var lines = b.querySelectorAll('line');
                                        var txts = b.querySelectorAll('text');
                                        if (rects.length > 0) {
                                            rects[0].setAttribute('y', btnY);
                                            lines.forEach(function(l) {
                                                l.setAttribute('y1', btnY + topSize / 2);
                                                l.setAttribute('y2', btnY + topSize / 2);
                                            });
                                            txts.forEach(function(t) {
                                                t.setAttribute('y', btnY + topSize - 3);
                                            });
                                        }
                                    }
                                });
                            }
                        } catch(e) {}
                    }
                    buttons.forEach(function(b) { b.style.opacity = '1'; });
                }
                function _noteScheduleHide() {
                    _noteHideTimeout = setTimeout(function() {
                        buttons.forEach(function(b) { b.style.opacity = '0'; });
                    }, 300);
                }

                // Note background paths: hover detection + dblclick to cycle state
                if (grp) {
                    grp.paths.forEach(function(p) {
                        p.addEventListener('mouseenter', _noteShowButtons);
                        p.addEventListener('mouseleave', _noteScheduleHide);
                        p.addEventListener('dblclick', function(ev) {
                            ev.stopPropagation(); ev.preventDefault(); onCycle();
                        });
                    });
                    // Note text elements: hover detection only (dblclick selects word naturally)
                    grp.texts.forEach(function(t) {
                        t.addEventListener('mouseenter', _noteShowButtons);
                        t.addEventListener('mouseleave', _noteScheduleHide);
                    });
                }

                buttons.forEach(function(b) {
                    b.addEventListener('mouseenter', _noteShowButtons);
                    b.addEventListener('mouseleave', _noteScheduleHide);
                });

                // Tooltip for collapsed notes
                if (state === 'collapsed' && contentLines && grp && grp.paths.length > 0) {
                    var tipLines = contentLines.map(function(l) {
                        return l.replace(/^\s*<color:gray>/, '');
                    });
                    var tipText = tipLines.join('\n').trim();
                    if (tipText) {
                        var displayLines = tipText.split('\n');
                        var tipLimit = (container && container._truncateLines) || window._truncateLines;
                        if (displayLines.length > tipLimit) {
                            tipText = displayLines.slice(0, tipLimit).join('\n') + '\n...';
                        }
                        var titleEl = document.createElementNS(SVGNS, 'title');
                        titleEl.textContent = tipText;
                        grp.paths[0].appendChild(titleEl);
                    }
                }

                // Transparent hover-detection rect covering the note bounding box.
                // This ensures mouseenter/mouseleave fire reliably even when other
                // SVG elements (arrows, lifeline labels) overlap the note area.
                // Inserted inside mainG BEFORE the note's path elements so that
                // text elements (which follow paths in SVG order) remain on top
                // and native text selection is preserved.
                var hoverRect = document.createElementNS(SVGNS, 'rect');
                hoverRect.setAttribute('x', bbox.x);
                hoverRect.setAttribute('y', bbox.y);
                hoverRect.setAttribute('width', bbox.width);
                hoverRect.setAttribute('height', bbox.height);
                hoverRect.setAttribute('fill', 'transparent');
                hoverRect.setAttribute('stroke', 'none');
                hoverRect.setAttribute('class', 'note-hover-rect');
                hoverRect.style.pointerEvents = 'all';
                hoverRect.addEventListener('mouseenter', _noteShowButtons);
                hoverRect.addEventListener('mouseleave', _noteScheduleHide);
                hoverRect.addEventListener('dblclick', function(ev) {
                    ev.stopPropagation(); ev.preventDefault(); onCycle();
                });
                grp.paths[0].parentNode.insertBefore(hoverRect, grp.paths[0]);

                // Insert buttons on top (after hoverRect so they receive clicks)
                buttons.forEach(function(b) { svg.appendChild(b); });
            }

            function makeNotesCollapsible(container) {
                var svg = container.querySelector('svg');
                if (!svg) return;

                // Remove existing toggle icons and hover rects from previous renders
                svg.querySelectorAll('.note-toggle-icon').forEach(function(el) { el.remove(); });
                svg.querySelectorAll('.note-hover-rect').forEach(function(el) { el.remove(); });

                // Resolve the owner container (the .plantuml-browser parent for fragments)
                var owner = container.classList.contains('puml-fragment') ? container.parentElement : container;
                var fragSource = container.getAttribute('data-plantuml');
                var ownerSource = owner._noteOriginalSource || owner.getAttribute('data-plantuml');
                // For fragments, use the fragment's own source for SVG note detection
                var source = fragSource || ownerSource;
                if (!source) return;
                if (!owner._noteOriginalSource) owner._noteOriginalSource = ownerSource || source;

                // Parse notes from the fragment's source (for SVG matching)
                var fragNoteBlocks = parseNoteBlocks(source);
                // Parse notes from the owner's full source (for global indexing)
                var ownerNoteBlocks = parseNoteBlocks(owner._noteOriginalSource);
                if (fragNoteBlocks.length === 0) return;
                if (!owner._noteSteps) owner._noteSteps = {};

                // For fragments, compute the global note index offset and mapping.
                // When chunkLargeNotes splits a note across fragments, the continuation
                // block in later fragments maps to the SAME original note (index 0),
                // not to the next sequential index.
                var noteIndexOffset = 0;
                var fragContinuationMap = null;
                if (container.classList.contains('puml-fragment')) {
                    var fragIdx = parseInt(container.dataset.fragment || '0', 10);
                    var siblingFrags = owner.querySelectorAll('.puml-fragment');
                    for (var fi = 0; fi < fragIdx && fi < siblingFrags.length; fi++) {
                        var sibSource = siblingFrags[fi].getAttribute('data-plantuml');
                        if (sibSource) noteIndexOffset += parseNoteBlocks(sibSource).length;
                    }
                    if (fragNoteBlocks.length > 0 && fragNoteBlocks[0].contentLines[0] &&
                        fragNoteBlocks[0].contentLines[0].indexOf('Continued From Previous Diagram') >= 0) {
                        fragContinuationMap = [0];
                        for (var fci = 1; fci < fragNoteBlocks.length; fci++) {
                            fragContinuationMap.push(noteIndexOffset + fci - 1);
                        }
                    }
                }

                // Use fragment's note blocks for SVG matching, owner for state
                var noteBlocks = fragNoteBlocks;
                // Propagate truncateLines from owner
                if (!container._truncateLines) container._truncateLines = owner._truncateLines || window._truncateLines;

                // Map filtered note indices → original-source note indices.
                // When filters (database/step/assertion) remove notes, the
                // rendered source has fewer notes than the original.  Button
                // callbacks must reference original indices so setNoteState
                // and buildSourceWithNoteStates stay aligned.
                var filteredToOrigMap = null;
                if (owner._noteOriginalSource && ownerNoteBlocks.length > 0) {
                    var cleanFilt = owner._noteOriginalSource;
                    if (!owner._assertionsVisible) cleanFilt = stripAssertionNotes(cleanFilt);
                    if (!owner._stepsVisible) cleanFilt = stripStepDelimiters(cleanFilt);
                    if (!owner._databasesVisible) cleanFilt = stripDatabaseCalls(cleanFilt);
                    var cleanFiltNotes = parseNoteBlocks(cleanFilt);
                    if (cleanFiltNotes.length < ownerNoteBlocks.length && cleanFiltNotes.length > 0) {
                        var _map = [];
                        var _oi = 0;
                        for (var _fi = 0; _fi < cleanFiltNotes.length && _oi < ownerNoteBlocks.length; _fi++) {
                            var _fc = cleanFiltNotes[_fi].contentLines.join('\n');
                            while (_oi < ownerNoteBlocks.length) {
                                if (ownerNoteBlocks[_oi].contentLines.join('\n') === _fc) {
                                    _map.push(_oi);
                                    _oi++;
                                    break;
                                }
                                _oi++;
                            }
                        }
                        if (_map.length === cleanFiltNotes.length) filteredToOrigMap = _map;
                    }
                }

                var noteGroups = findNoteGroups(svg);

                // Safety net: if more SVG groups were detected than note blocks exist,
                // filter to only groups whose fill matches the expected note count.
                // When multiple fills have the matching count, prefer the one whose
                // groups appear latest in the DOM (notes always come after participants
                // and partitions in PlantUML sequence diagram SVGs).
                if (noteGroups.length > noteBlocks.length && noteBlocks.length > 0) {
                    var fillMap = {};
                    noteGroups.forEach(function(g, idx) {
                        var f = (g.paths[0].getAttribute('fill') || '').toLowerCase();
                        if (!fillMap[f]) fillMap[f] = { groups: [], lastIdx: idx };
                        fillMap[f].groups.push(g);
                        fillMap[f].lastIdx = idx;
                    });
                    var bestFill = null;
                    var bestLastIdx = -1;
                    for (var f in fillMap) {
                        if (fillMap[f].groups.length === noteBlocks.length) {
                            if (fillMap[f].lastIdx > bestLastIdx) {
                                bestLastIdx = fillMap[f].lastIdx;
                                bestFill = f;
                            }
                        }
                    }
                    if (bestFill) {
                        noteGroups = fillMap[bestFill].groups;
                    } else {
                        // Fallback: no single fill color has the matching count.
                        // Use positional heuristic — notes appear after participants/partitions
                        // in PlantUML SVGs, so take the last N groups. Validate by checking
                        // that each candidate group's text matches its source note content.
                        var candidate = noteGroups.slice(-noteBlocks.length);
                        var validated = true;
                        for (var ci = 0; ci < candidate.length && validated; ci++) {
                            var grpText = candidate[ci].texts.map(function(t) {
                                return t.textContent.trim();
                            }).join(' ').trim();
                            var srcText = noteBlocks[ci].contentLines.map(function(l) {
                                return l.replace(/<[^>]*>/g, '').trim();
                            }).filter(function(l) { return l; }).join(' ').trim();
                            // Check if at least part of the source text appears in the SVG text
                            if (srcText && grpText) {
                                var srcStart = srcText.substring(0, Math.min(20, srcText.length));
                                if (grpText.indexOf(srcStart) < 0 && srcStart.indexOf(grpText.substring(0, Math.min(20, grpText.length))) < 0) {
                                    validated = false;
                                }
                            }
                        }
                        if (validated) {
                            noteGroups = candidate;
                        }
                        // If validation fails, leave noteGroups as-is; the loop count
                        // will be clamped to min(noteGroups, noteBlocks) preventing overflow.
                    }
                }

                // Build index mapping: when some notes are empty in the rendered SVG
                // (e.g. header-only notes with headers hidden, or collapsed all-gray notes),
                // the SVG has fewer groups than source blocks. Map each SVG group to the
                // correct source block index by computing which blocks are visible.
                var sourceIndexMap = null;
                if (noteGroups.length < noteBlocks.length) {
                    sourceIndexMap = [];
                    for (var si = 0; si < noteBlocks.length; si++) {
                        var sGlobalIdx = fragContinuationMap ? (si < fragContinuationMap.length ? fragContinuationMap[si] : si + noteIndexOffset) : si + noteIndexOffset;
                        var sStep = owner._noteSteps[sGlobalIdx] || 0;
                        var sState = noteStepState(sStep);
                        var noteEmpty = false;
                        if (sState === 'collapsed') {
                            var prev = getNotePreview(noteBlocks[si].contentLines);
                            if (!prev) noteEmpty = true;
                        } else if (owner._headersHidden) {
                            var hasVisible = false;
                            var afterGrayLine = false;
                            for (var li = 0; li < noteBlocks[si].contentLines.length; li++) {
                                var cl = noteBlocks[si].contentLines[li].trim();
                                if (/^<color:gray>/.test(cl)) { afterGrayLine = true; continue; }
                                if (afterGrayLine && cl === '') continue;
                                afterGrayLine = false;
                                hasVisible = true;
                                break;
                            }
                            if (!hasVisible) noteEmpty = true;
                        }
                        if (!noteEmpty) sourceIndexMap.push(si);
                    }
                }

                var loopCount = sourceIndexMap
                    ? Math.min(noteGroups.length, sourceIndexMap.length)
                    : Math.min(noteGroups.length, noteBlocks.length);

                for (var ni = 0; ni < loopCount; ni++) {
                    (function(svgIdx, srcIdx) {
                        var globalFilteredIdx = fragContinuationMap ? srcIdx : srcIdx + noteIndexOffset;
                        var globalIdx = filteredToOrigMap ? (globalFilteredIdx < filteredToOrigMap.length ? filteredToOrigMap[globalFilteredIdx] : globalFilteredIdx) : globalFilteredIdx;
                        var grp = noteGroups[svgIdx];
                        var bbox = getNoteBBox(grp);
                        var step = owner._noteSteps[globalIdx] || 0;
                        // For continuation notes (fragContinuationMap[0]), use the fragment's
                        // block content since ownerNoteBlocks[0] may be truncated from its
                        // initial render state and not reflect the actual chunk content.
                        var origContentLines;
                        if (fragContinuationMap && svgIdx === 0) {
                            origContentLines = noteBlocks[0].contentLines;
                        } else {
                            origContentLines = ownerNoteBlocks[globalIdx] ? ownerNoteBlocks[globalIdx].contentLines : noteBlocks[srcIdx].contentLines;
                        }
                        // Short notes only have steps 0 (collapsed) and 2 (expanded)
                        if (!isLongNote(origContentLines, container._truncateLines, owner._headersHidden) && step === 1) step = 2;
                        createNoteButtons(svg, bbox, step,
                            function() {
                                var long = isLongNote(origContentLines, container._truncateLines, owner._headersHidden);
                                var curStep = owner._noteSteps[globalIdx] || 0;
                                setNoteState(owner, globalIdx, (long && curStep === 0) ? 1 : 2);
                            },
                            function() { setNoteState(owner, globalIdx, 0); },
                            function() { setNoteState(owner, globalIdx, 1); },
                            function() {
                                var curStep = owner._noteSteps[globalIdx] || 0;
                                var long = isLongNote(origContentLines, container._truncateLines, owner._headersHidden);
                                var nextStep;
                                if (curStep === 2) nextStep = long ? 1 : 0;
                                else if (curStep === 1) nextStep = 0;
                                else nextStep = long ? 1 : 2;
                                setNoteState(owner, globalIdx, nextStep);
                            },
                            origContentLines, grp, container);
                    })(ni, sourceIndexMap ? sourceIndexMap[ni] : (fragContinuationMap ? fragContinuationMap[ni] : ni));
                }
            }

            function buildSourceWithNoteStates(origSource, noteSteps, noteBlocks, hideHeaders, truncateLines) {
                var limit = truncateLines || window._truncateLines;
                var lines = origSource.split('\n');
                var newLines = [];
                var nIdx = 0;
                var inNote = false;
                var noteMode = 'normal'; // 'normal', 'collapsed', 'truncated'
                var truncateLineCount = 0;
                var justSkippedGray = false;
                var noteContentEmitted = false;

                for (var i = 0; i < lines.length; i++) {
                    var trimmed = lines[i].trim();
                    if (!inNote && /^note(?:<<\w+>>)?\s+(left|right)/.test(trimmed)) {
                        inNote = true;
                        justSkippedGray = false;
                        noteContentEmitted = false;
                        newLines.push(lines[i]);
                        var step = noteSteps[nIdx] || 0;
                        var state = noteStepState(step);
                        if (state === 'collapsed') {
                            noteMode = 'collapsed';
                            var preview = getNotePreview(noteBlocks[nIdx].contentLines);
                            if (preview) { newLines.push(preview); noteContentEmitted = true; }
                        } else if (state === 'truncated') {
                            noteMode = 'truncated';
                            truncateLineCount = 0;
                        } else {
                            noteMode = 'normal';
                        }
                        nIdx++;
                        continue;
                    }
                    if (inNote && trimmed === 'end note') {
                        if (noteMode === 'truncated' && truncateLineCount > limit) {
                            newLines.push('...');
                            noteContentEmitted = true;
                        }
                        // Ensure note is never empty — PlantUML must render text so that
                        // findNoteGroups detects it and index alignment is preserved
                        if (!noteContentEmitted) {
                            newLines.push('\u00A0');
                        }
                        inNote = false;
                        noteMode = 'normal';
                        justSkippedGray = false;
                        newLines.push(lines[i]);
                        continue;
                    }
                    if (noteMode === 'collapsed') continue;
                    if (noteMode === 'truncated') {
                        if (hideHeaders && /^<color:gray>/.test(trimmed)) { justSkippedGray = true; continue; }
                        if (justSkippedGray && trimmed === '') continue;
                        justSkippedGray = false;
                        truncateLineCount++;
                        if (truncateLineCount <= limit) {
                            newLines.push(lines[i]);
                            noteContentEmitted = true;
                        }
                        continue;
                    }
                    if (inNote && hideHeaders && /^<color:gray>/.test(trimmed)) { justSkippedGray = true; continue; }
                    if (justSkippedGray && trimmed === '') continue;
                    justSkippedGray = false;
                    newLines.push(lines[i]);
                    if (inNote) noteContentEmitted = true;
                }
                return newLines.join('\n');
            }

            var _svgCache = {};

            function setNoteState(container, noteIdx, targetStep) {
                if (container._noteRendering || window._plantumlRendering) return;
                if (!container._noteSteps) container._noteSteps = {};
                if (container._noteSteps[noteIdx] === targetStep) return;
                var oldStep = container._noteSteps[noteIdx];
                container._noteSteps[noteIdx] = targetStep;

                var origSource = container._noteOriginalSource;
                if (!origSource) { container._noteSteps[noteIdx] = oldStep; return; }
                var noteBlocks = parseNoteBlocks(origSource);
                var newSource = applyDatabasesFilter(applyStepsFilter(applyAssertionFilter(buildSourceWithNoteStates(origSource, container._noteSteps, noteBlocks, !!container._headersHidden, container._truncateLines), !!container._assertionsVisible), !!container._stepsVisible), !!container._databasesVisible);

                container.setAttribute('data-plantuml', newSource);

                // Re-split and render as fragments if needed
                if (window._splitWithChunkedNotes) {
                    var fragments = window._splitWithChunkedNotes(newSource);
                    if (fragments.length > 1 || container.querySelector('.puml-fragment')) {
                        // Multiple fragments or was previously fragmented — re-render all fragments
                        container._fragments = fragments;
                        // Preserve container height to prevent layout shift during re-render
                        container.style.minHeight = container.offsetHeight + 'px';
                        // Keep old children visible until new fragments are rendered
                        var oldChildren = Array.from(container.children);
                        container.dataset.rendered = '1';
                        container._noteRendering = true;
                        window._plantumlRendering = true;
                        var fragQueue = [];
                        for (var fi = 0; fi < fragments.length; fi++) {
                            var fragDiv = document.createElement('div');
                            fragDiv.className = 'puml-fragment puml-fragment-new';
                            fragDiv.id = container.id + '-frag-n-' + fi;
                            fragDiv.dataset.fragment = fi;
                            fragDiv.setAttribute('data-plantuml', fragments[fi]);
                            fragDiv.style.display = 'none';
                            container.appendChild(fragDiv);
                            fragQueue.push({ el: fragDiv, source: fragments[fi], isFragment: true, parentEl: container });
                        }
                        // Process fragment render queue sequentially
                        var fragIdx = 0;
                        function renderNextFrag() {
                            if (fragIdx >= fragQueue.length) {
                                // All new fragments rendered — swap: remove old, show new
                                for (var oi = 0; oi < oldChildren.length; oi++) {
                                    if (oldChildren[oi].parentNode === container) container.removeChild(oldChildren[oi]);
                                }
                                var newFrags = container.querySelectorAll('.puml-fragment-new');
                                for (var ni = 0; ni < newFrags.length; ni++) {
                                    newFrags[ni].style.display = '';
                                    newFrags[ni].classList.remove('puml-fragment-new');
                                    newFrags[ni].id = container.id + '-frag-' + ni;
                                }
                                // Apply post-render hooks after fragments are visible (getBBox needs visible elements)
                                for (var pi = 0; pi < fragQueue.length; pi++) {
                                    if (window._iflowBindLinks) window._iflowBindLinks(fragQueue[pi].el, origSource);
                                    makeNotesCollapsible(fragQueue[pi].el);
                                    addAssertionTooltips(fragQueue[pi].el);
                                    (function(el) { requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(el); }); })(fragQueue[pi].el);
                                }
                                container._noteRendering = false;
                                window._plantumlRendering = false;
                                container.style.minHeight = '';
                                return;
                            }
                            var item = fragQueue[fragIdx++];
                            // Check SVG cache first
                            if (_svgCache[item.source]) {
                                item.el.innerHTML = _svgCache[item.source];
                                item.el.dataset.rendered = '1';
                                renderNextFrag();
                                return;
                            }
                            var fDone = false;
                            function afterFragRender() {
                                if (fDone) return;
                                fDone = true;
                                _svgCache[item.source] = item.el.innerHTML;
                                item.el.dataset.rendered = '1';
                                window._plantumlRendering = false;
                                renderNextFrag();
                            }
                            window._plantumlRendering = true;
                            var fmo = new MutationObserver(function() {
                                if (!item.el.querySelector('svg')) return;
                                fmo.disconnect();
                                afterFragRender();
                            });
                            fmo.observe(item.el, { childList: true, subtree: true });
                            try {
                                window.plantuml.render(item.source.split('\n'), item.el.id);
                            } catch(e) {
                                fmo.disconnect();
                                window._plantumlRendering = false;
                                renderNextFrag();
                            }
                        }
                        renderNextFrag();
                        return;
                    }
                }

                // Single diagram (no splitting needed) — existing behavior
                // Check SVG cache — skip plantuml.js re-render if we have a cached result
                if (_svgCache[newSource]) {
                    container.innerHTML = _svgCache[newSource];
                    if (window._iflowBindLinks) window._iflowBindLinks(container, origSource);
                    makeNotesCollapsible(container);
                    addAssertionTooltips(container);
                    requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(container); });
                    return;
                }

                container._noteRendering = true;
                window._plantumlRendering = true;
                // Preserve container height to prevent layout shift during re-render
                container.style.minHeight = container.offsetHeight + 'px';

                // Render into a temporary child div to keep old SVG visible until new one is ready
                // Use unique ID per render to prevent TeaVM global state leaking between sequential renders
                window._renderTmpCounter = (window._renderTmpCounter || 0) + 1;
                var renderTarget = document.createElement('div');
                renderTarget.id = container.id + '-render-tmp-' + window._renderTmpCounter;
                renderTarget.style.display = 'none';
                container.appendChild(renderTarget);

                var done = false;
                function afterRender() {
                    if (done) return;
                    done = true;
                    // Swap: remove old content, show new SVG
                    var newSvg = renderTarget.innerHTML;
                    container.innerHTML = newSvg;
                    _svgCache[newSource] = newSvg;
                    container._noteRendering = false;
                    window._plantumlRendering = false;
                    container.style.minHeight = '';
                    if (window._iflowBindLinks) window._iflowBindLinks(container, origSource);
                    makeNotesCollapsible(container);
                    addAssertionTooltips(container);
                    requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(container); });
                }

                var mo = new MutationObserver(function(mutations) {
                    if (!renderTarget.querySelector('svg')) return;
                    mo.disconnect();
                    afterRender();
                });
                mo.observe(renderTarget, { childList: true, subtree: true });

                try {
                    window.plantuml.render(newSource.split('\n'), renderTarget.id);
                } catch(e) {
                    mo.disconnect();
                    if (renderTarget.parentNode) renderTarget.parentNode.removeChild(renderTarget);
                    container._noteRendering = false;
                    window._plantumlRendering = false;
                    container.style.minHeight = '';
                    // Render failed — restore previous step and sync buttons
                    container._noteSteps[noteIdx] = oldStep;
                    makeNotesCollapsible(container);
                    addAssertionTooltips(container);
                }

                var pollCount = 0;
                var poll = setInterval(function() {
                    pollCount++;
                    if (done) { clearInterval(poll); return; }
                    var svg = renderTarget.querySelector('svg');
                    if (svg && !svg.querySelector('.note-toggle-icon')) {
                        clearInterval(poll);
                        mo.disconnect();
                        afterRender();
                    }
                    if (pollCount > 120) {
                        clearInterval(poll);
                        mo.disconnect();
                        if (renderTarget.parentNode) renderTarget.parentNode.removeChild(renderTarget);
                        container._noteRendering = false;
                        window._plantumlRendering = false;
                        container.style.minHeight = '';
                        // Render timed out — restore previous step and sync buttons
                        container._noteSteps[noteIdx] = oldStep;
                        makeNotesCollapsible(container);
                        addAssertionTooltips(container);
                    }
                }, 250);
            }

            window._makeNotesCollapsible = makeNotesCollapsible;
            window._findNoteGroups = findNoteGroups;
            window._getNoteBBox = getNoteBBox;
            window._parseNoteBlocks = parseNoteBlocks;

            // Global defaults
            window._headersHidden = false;
            window._truncateLines = 40;
            window._detailsDefault = 'truncated';
            window._assertionsVisible = false;
            window._stepsVisible = true;
            window._databasesVisible = true;

            function stripAssertionNotes(source) {
                return source.replace(/\n?hnote across <<assertionNote>>[^\n]*\n[\s\S]*?end note\n?/g, '');
            }

            function stripStepDelimiters(source) {
                return source.replace(/\n?hnote across <<stepDelimiter>>[^\n]*\n?/g, '');
            }

            function applyAssertionFilter(source, showing) {
                return showing ? source : stripAssertionNotes(source);
            }

            function applyStepsFilter(source, showing) {
                return showing ? source : stripStepDelimiters(source);
            }

            function stripDatabaseCalls(source) {
                // Find all database participant aliases
                var dbAliases = [];
                var dbDeclRe = /^(?:database|collections)\s+"[^"]*"\s+as\s+(\S+)/gm;
                var m;
                while ((m = dbDeclRe.exec(source)) !== null) {
                    dbAliases.push(m[1].replace(/\s.*$/, ''));
                }
                if (dbAliases.length === 0) return source;

                // Build set of escaped aliases for regex matching
                var escaped = dbAliases.map(function(a) { return a.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); });

                function isDbArrow(line) {
                    for (var i = 0; i < escaped.length; i++) {
                        // Arrow where alias is the source
                        if (new RegExp('^' + escaped[i] + '\\s+-').test(line)) return true;
                        // Arrow where alias is the target
                        if (new RegExp('^\\S+\\s+-[^\\n]*>\\s*' + escaped[i] + '(\\s|:|$)').test(line)) return true;
                    }
                    return false;
                }

                function isDbDecl(line) {
                    return /^(?:database|collections)\s+"[^"]*"\s+as\s+\S+/.test(line);
                }

                function isDbExplicitNote(line) {
                    for (var i = 0; i < escaped.length; i++) {
                        if (new RegExp('^note\\s+(?:left|right|over)\\s+(?:of\\s+)?' + escaped[i] + '(\\s|:|$)').test(line)) return true;
                    }
                    return false;
                }

                // Note start: "note left", "note right", "note<<stereotype>> left", etc.
                // but NOT "note left of alias" (handled separately above)
                function isPositionalNoteStart(line) {
                    return /^note\s*(?:<<[^>]*>>)?\s+(?:left|right)\s*$/.test(line);
                }

                var lines = source.split('\n');
                var result = [];
                var i = 0;
                var lastRemovedArrow = false;
                while (i < lines.length) {
                    var line = lines[i];
                    var trimmed = line.trim();

                    if (isDbDecl(trimmed)) {
                        // Skip database declaration
                        i++;
                        continue;
                    }

                    if (isDbArrow(trimmed)) {
                        // Skip database arrow and mark for trailing note removal
                        lastRemovedArrow = true;
                        i++;
                        continue;
                    }

                    if (isDbExplicitNote(trimmed)) {
                        // Skip note block explicitly referencing database alias
                        if (trimmed.indexOf(':') !== -1) {
                            // Single-line note: "note left of alias: text"
                            i++;
                        } else {
                            // Multi-line note: skip until "end note"
                            i++;
                            while (i < lines.length && lines[i].trim() !== 'end note') i++;
                            if (i < lines.length) i++; // skip "end note"
                        }
                        continue;
                    }

                    // Remove positional notes that follow a removed database arrow
                    if (lastRemovedArrow && isPositionalNoteStart(trimmed)) {
                        // Multi-line positional note following a removed arrow
                        i++;
                        while (i < lines.length && lines[i].trim() !== 'end note') i++;
                        if (i < lines.length) i++; // skip "end note"
                        continue;
                    }

                    // Activate/deactivate for database alias
                    var isActivate = false;
                    for (var ai = 0; ai < escaped.length; ai++) {
                        if (new RegExp('^(?:activate|deactivate)\\s+' + escaped[ai] + '\\s*$').test(trimmed)) {
                            isActivate = true;
                            break;
                        }
                    }
                    if (isActivate) { i++; continue; }

                    // Non-blank, non-removed line resets the trailing-note flag
                    if (trimmed !== '') lastRemovedArrow = false;
                    result.push(line);
                    i++;
                }
                return result.join('\n');
            }

            function applyDatabasesFilter(source, showing) {
                return showing ? source : stripDatabaseCalls(source);
            }

            // Parse assertion source locations from PlantUML comments
            function parseAssertionLocations(source) {
                if (!source) return [];
                var locs = [];
                var re = /^'__\^\*__:(.+)$/gm;
                var m;
                while ((m = re.exec(source)) !== null) {
                    locs.push(m[1]);
                }
                return locs;
            }

            // Find assertion note groups in SVG by scanning for path+text groups
            // whose first text starts with ✓ or ✗. Unlike findNoteGroups(), this
            // does NOT filter by fold triangle — hnote across renders as a hexagonal
            // path without the fold corner that regular note left/right shapes have.
            // When a diagram has both regular notes and hnotes, findNoteGroups()
            // excludes hnotes because foldGroups is non-empty. Issue #53.
            // hnote across renders as polygon+text (not path+text like regular notes).
            function findAssertionNoteGroups(svg) {
                var mainG = null;
                for (var i = 0; i < svg.children.length; i++) {
                    if (svg.children[i].tagName === 'g') { mainG = svg.children[i]; break; }
                }
                if (!mainG) return [];
                var children = Array.from(mainG.children);
                var groups = [];
                var ci = 0;
                while (ci < children.length) {
                    if (children[ci].tagName === 'g') { ci++; continue; }
                    var isShape = (children[ci].tagName === 'path' || children[ci].tagName === 'polygon')
                        && hasNoteFill(children[ci]);
                    if (isShape) {
                        var grp = { paths: [], texts: [] };
                        while (ci < children.length && (children[ci].tagName === 'path' || children[ci].tagName === 'polygon')) {
                            grp.paths.push(children[ci]);
                            ci++;
                        }
                        var noteBox = null;
                        try {
                            var bb = grp.paths[0].getBBox();
                            noteBox = { x: bb.x, y: bb.y, right: bb.x + bb.width, bottom: bb.y + bb.height };
                        } catch(e) {}
                        while (ci < children.length) {
                            var tag = children[ci].tagName;
                            if (tag === 'text') { grp.texts.push(children[ci]); ci++; }
                            else if (noteBox && (tag === 'line' || tag === 'rect' || tag === 'circle')) {
                                try {
                                    var ebb = children[ci].getBBox();
                                    if (ebb.x >= noteBox.x - 2 && ebb.x + ebb.width <= noteBox.right + 2
                                        && ebb.y >= noteBox.y - 2 && ebb.y + ebb.height <= noteBox.bottom + 2) {
                                        ci++;
                                    } else { break; }
                                } catch(e) { break; }
                            }
                            else { break; }
                        }
                        if (grp.paths.length > 0 && grp.texts.length > 0) {
                            var firstChar = (grp.texts[0].textContent || '').trim().charAt(0);
                            if (firstChar === '\u2713' || firstChar === '\u2717') {
                                groups.push(grp);
                            }
                        }
                    } else {
                        ci++;
                    }
                }
                return groups;
            }

            // Add source-location tooltips to assertion note SVG groups
            function addAssertionTooltips(container) {
                var svg = container.querySelector('svg');
                if (!svg) return;
                var source = container._noteOriginalSource || container.getAttribute('data-plantuml');
                if (!source) return;
                var locs = parseAssertionLocations(source);
                if (locs.length === 0) return;
                var groups = findAssertionNoteGroups(svg);
                var count = Math.min(locs.length, groups.length);
                for (var i = 0; i < count; i++) {
                    var titleEl = document.createElementNS(SVGNS, 'title');
                    titleEl.textContent = locs[i].replace(/:L/, ' L:');
                    // Add tooltip to main path of the note
                    var existing = groups[i].paths[0].querySelector('title');
                    if (existing) existing.remove();
                    groups[i].paths[0].appendChild(titleEl);
                }
            }
            window._addAssertionTooltips = addAssertionTooltips;

            // Pre-process source before initial render — applies current report-level defaults
            window._preProcessSource = function(el, source) {
                // Strip assertion notes before note-block parsing when hidden
                el._assertionsVisible = window._assertionsVisible;
                el._stepsVisible = window._stepsVisible;
                el._databasesVisible = window._databasesVisible;
                var renderSource = !el._assertionsVisible ? stripAssertionNotes(source) : source;
                renderSource = !el._stepsVisible ? stripStepDelimiters(renderSource) : renderSource;
                renderSource = !el._databasesVisible ? stripDatabaseCalls(renderSource) : renderSource;
                // Always use the ORIGINAL source for note indexing so that
                // _noteSteps indices align with buildSourceWithNoteStates and
                // setNoteState, which both operate on the original source.
                var origNoteBlocks = parseNoteBlocks(source);
                if (origNoteBlocks.length === 0 && renderSource === source) return source;
                el._noteOriginalSource = source;
                if (origNoteBlocks.length === 0) return renderSource;
                var state = window._detailsDefault;
                // Always initialize _noteSteps so that subsequent headers toggle
                // or details changes see the correct state (step 2 = expanded)
                if (!el._noteSteps) el._noteSteps = {};
                // Preserve per-element truncateLines if already set by a scenario-level change
                if (el._truncateLines === undefined) el._truncateLines = window._truncateLines;
                for (var i = 0; i < origNoteBlocks.length; i++) {
                    var targetStep;
                    if (state === 'expanded') { targetStep = 2; }
                    else if (state === 'truncated') { targetStep = isLongNote(origNoteBlocks[i].contentLines, el._truncateLines, window._headersHidden) ? 1 : 2; }
                    else { targetStep = 0; }
                    el._noteSteps[i] = targetStep;
                }
                el._headersHidden = window._headersHidden;
                if (state !== 'expanded' || window._headersHidden) {
                    var built = buildSourceWithNoteStates(source, el._noteSteps, origNoteBlocks, window._headersHidden, el._truncateLines);
                    return applyDatabasesFilter(applyStepsFilter(applyAssertionFilter(built, el._assertionsVisible), el._stepsVisible), el._databasesVisible);
                }
                return renderSource;
            };

            // Shared serialized render queue — TeaVM engine uses shared global state
            function processRenderQueue(queue, onAllDone) {
                var renderWaitCount = 0;
                function processNext() {
                    if (queue.length === 0) { window._renderCompleteCount = (window._renderCompleteCount || 0) + 1; if (onAllDone) onAllDone(); return; }
                    // Wait for any in-flight initial render to complete before proceeding
                    if (window._plantumlRendering) {
                        renderWaitCount++;
                        if (renderWaitCount > 300) {
                            // Stuck render — force-reset after 15s and proceed
                            window._plantumlRendering = false;
                        } else {
                            setTimeout(processNext, 50);
                            return;
                        }
                    }
                    renderWaitCount = 0;
                    var item = queue.shift();
                    var container = item.container;
                    var origNoteBlocks = parseNoteBlocks(container._noteOriginalSource);
                    var newSource = applyDatabasesFilter(applyStepsFilter(applyAssertionFilter(buildSourceWithNoteStates(container._noteOriginalSource, container._noteSteps, origNoteBlocks, !!container._headersHidden, container._truncateLines), !!container._assertionsVisible), !!container._stepsVisible), !!container._databasesVisible);
                    container.setAttribute('data-plantuml', newSource);

                    // Re-split into fragments if needed
                    if (window._splitWithChunkedNotes) {
                        var fragments = window._splitWithChunkedNotes(newSource);
                        if (fragments.length > 1 || container.querySelector('.puml-fragment')) {
                            container._fragments = fragments;
                            // Preserve container height to prevent layout shift during re-render
                            container.style.minHeight = container.offsetHeight + 'px';
                            // Keep old children visible until new fragments are rendered
                            var oldChildren = Array.from(container.children);
                            container.dataset.rendered = '1';
                            var fragList = [];
                            for (var fi = 0; fi < fragments.length; fi++) {
                                var fragDiv = document.createElement('div');
                                fragDiv.className = 'puml-fragment puml-fragment-new';
                                fragDiv.id = container.id + '-frag-n-' + fi;
                                fragDiv.dataset.fragment = fi;
                                fragDiv.setAttribute('data-plantuml', fragments[fi]);
                                fragDiv.style.display = 'none';
                                container.appendChild(fragDiv);
                                fragList.push({ el: fragDiv, source: fragments[fi] });
                            }
                            var fragI = 0;
                            function renderNextFragment() {
                                if (fragI >= fragList.length) {
                                    // All new fragments rendered — swap: remove old, show new
                                    for (var oi = 0; oi < oldChildren.length; oi++) {
                                        if (oldChildren[oi].parentNode === container) container.removeChild(oldChildren[oi]);
                                    }
                                    var newFrags = container.querySelectorAll('.puml-fragment-new');
                                    for (var ni = 0; ni < newFrags.length; ni++) {
                                        newFrags[ni].style.display = '';
                                        newFrags[ni].classList.remove('puml-fragment-new');
                                        newFrags[ni].id = container.id + '-frag-' + ni;
                                    }
                                    // Apply post-render hooks after fragments are visible (getBBox needs visible elements)
                                    for (var pi = 0; pi < fragList.length; pi++) {
                                        if (window._iflowBindLinks) window._iflowBindLinks(fragList[pi].el, container._noteOriginalSource);
                                        makeNotesCollapsible(fragList[pi].el);
                                        addAssertionTooltips(fragList[pi].el);
                                        (function(el) { requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(el); }); })(fragList[pi].el);
                                    }
                                    container.style.minHeight = '';
                                    processNext();
                                    return;
                                }
                                var fItem = fragList[fragI++];
                                if (_svgCache[fItem.source]) {
                                    fItem.el.innerHTML = _svgCache[fItem.source];
                                    fItem.el.dataset.rendered = '1';
                                    renderNextFragment();
                                    return;
                                }
                                window._plantumlRendering = true;
                                var fDone = false;
                                function afterFrag() {
                                    if (fDone) return;
                                    fDone = true;
                                    _svgCache[fItem.source] = fItem.el.innerHTML;
                                    fItem.el.dataset.rendered = '1';
                                    window._plantumlRendering = false;
                                    renderNextFragment();
                                }
                                var fmo = new MutationObserver(function() {
                                    if (!fItem.el.querySelector('svg')) return;
                                    fmo.disconnect(); afterFrag();
                                });
                                fmo.observe(fItem.el, { childList: true, subtree: true });
                                try { window.plantuml.render(fItem.source.split('\n'), fItem.el.id); } catch(e) { fmo.disconnect(); window._plantumlRendering = false; renderNextFragment(); }
                            }
                            renderNextFragment();
                            return;
                        }
                    }

                    // Single diagram — existing behavior
                    // Check SVG cache first
                    if (_svgCache[newSource]) {
                        container.innerHTML = _svgCache[newSource];
                        if (window._iflowBindLinks) window._iflowBindLinks(container, container._noteOriginalSource);
                        makeNotesCollapsible(container);
                        addAssertionTooltips(container);
                        requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(container); });
                        processNext();
                        return;
                    }
                    container._noteRendering = true;
                    window._plantumlRendering = true;
                    // Preserve container height to prevent layout shift during re-render
                    container.style.minHeight = container.offsetHeight + 'px';
                    // Render into temporary child to keep old SVG visible until new one is ready
                    // Use unique ID per render to prevent TeaVM global state leaking between sequential renders
                    window._renderTmpCounter = (window._renderTmpCounter || 0) + 1;
                    var renderTarget = document.createElement('div');
                    renderTarget.id = container.id + '-render-tmp-' + window._renderTmpCounter;
                    renderTarget.style.display = 'none';
                    container.appendChild(renderTarget);
                    var done = false;
                    function afterRender() {
                        if (done) return;
                        done = true;
                        var newSvg = renderTarget.innerHTML;
                        container.innerHTML = newSvg;
                        _svgCache[newSource] = newSvg;
                        container._noteRendering = false;
                        window._plantumlRendering = false;
                        container.style.minHeight = '';
                        if (window._iflowBindLinks) window._iflowBindLinks(container, container._noteOriginalSource);
                        makeNotesCollapsible(container);
                        addAssertionTooltips(container);
                        requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(container); });
                        processNext();
                    }
                    var mo = new MutationObserver(function() {
                        if (!renderTarget.querySelector('svg')) return;
                        mo.disconnect();
                        afterRender();
                    });
                    mo.observe(renderTarget, { childList: true, subtree: true });
                    try { window.plantuml.render(newSource.split('\n'), renderTarget.id); } catch(e) { mo.disconnect(); if (renderTarget.parentNode) renderTarget.parentNode.removeChild(renderTarget); container._noteRendering = false; window._plantumlRendering = false; container.style.minHeight = ''; processNext(); }
                    var pollCount = 0;
                    var poll = setInterval(function() {
                        pollCount++;
                        if (done) { clearInterval(poll); return; }
                        var svg = renderTarget.querySelector('svg');
                        if (svg && !svg.querySelector('.note-toggle-icon')) { clearInterval(poll); mo.disconnect(); afterRender(); }
                        if (pollCount > 120) { clearInterval(poll); mo.disconnect(); if (renderTarget.parentNode) renderTarget.parentNode.removeChild(renderTarget); container._noteRendering = false; window._plantumlRendering = false; container.style.minHeight = ''; processNext(); }
                    }, 250);
                }
                processNext();
            }

            function buildDetailsQueue(containers, targetState, force) {
                var queue = [];
                containers.forEach(function(container) {
                    if (container.classList.contains('puml-fragment')) return;
                    if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
                    var noteBlocks = parseNoteBlocks(container._noteOriginalSource);
                    if (noteBlocks.length === 0) return;
                    if (!container._noteSteps) container._noteSteps = {};
                    if (container._headersHidden === undefined) container._headersHidden = window._headersHidden;
                    if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
                    var containerLines = container._truncateLines;
                    var hh = container._headersHidden !== undefined ? container._headersHidden : window._headersHidden;
                    var needsUpdate = false;
                    for (var i = 0; i < noteBlocks.length; i++) {
                        var targetStep;
                        if (targetState === 'expanded') {
                            targetStep = 2;
                        } else if (targetState === 'truncated') {
                            targetStep = isLongNote(noteBlocks[i].contentLines, containerLines, hh) ? 1 : 2;
                        } else {
                            targetStep = 0;
                        }
                        if ((container._noteSteps[i] || 0) !== targetStep) {
                            container._noteSteps[i] = targetStep;
                            needsUpdate = true;
                        }
                    }
                    if (needsUpdate || force) queue.push({ container: container, noteBlocks: noteBlocks });
                });
                return queue;
            }

            function buildHeadersQueue(containers, hiding) {
                var queue = [];
                containers.forEach(function(container) {
                    if (container.classList.contains('puml-fragment')) return;
                    if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
                    var noteBlocks = parseNoteBlocks(container._noteOriginalSource);
                    if (noteBlocks.length === 0) return;
                    if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
                    if (!container._noteSteps) {
                        container._noteSteps = {};
                        var state = window._detailsDefault;
                        var containerLines = container._truncateLines;
                        for (var i = 0; i < noteBlocks.length; i++) {
                            if (state === 'expanded') { container._noteSteps[i] = 2; }
                            else if (state === 'truncated') { container._noteSteps[i] = isLongNote(noteBlocks[i].contentLines, containerLines, hiding) ? 1 : 2; }
                            else { container._noteSteps[i] = 0; }
                        }
                    }
                    var wasHidden = !!container._headersHidden;
                    if (wasHidden === hiding) return;
                    container._headersHidden = hiding;
                    // Adjust note steps: notes in truncated state (step 1) that are no longer
                    // "long" under the new header visibility should auto-expand to step 2
                    var containerLines = container._truncateLines;
                    for (var i = 0; i < noteBlocks.length; i++) {
                        var curStep = container._noteSteps[i] || 0;
                        if (curStep === 1 && !isLongNote(noteBlocks[i].contentLines, containerLines, hiding)) {
                            container._noteSteps[i] = 2;
                        }
                    }
                    queue.push({ container: container, noteBlocks: noteBlocks });
                });
                return queue;
            }

            function syncRadioButtons(parent, targetState) {
                parent.querySelectorAll('.details-radio-btn[data-state]').forEach(function(b) {
                    if (b.getAttribute('data-state') === targetState) b.classList.add('details-active');
                    else b.classList.remove('details-active');
                });
                // Enable/disable dropdown
                parent.querySelectorAll('.truncate-lines-select').forEach(function(sel) {
                    sel.disabled = targetState !== 'truncated';
                });
            }

            // Scenario-level: expand/truncate/collapse details
            window._setAllNotes = function(btn, targetState) {
                var scenario = btn.closest('details.scenario');
                if (!scenario) return;
                // When switching to truncated, read the scenario's dropdown value
                if (targetState === 'truncated') {
                    var sel = scenario.querySelector('.truncate-lines-select');
                    if (sel) {
                        var scenarioLines = parseInt(sel.value, 10) || window._truncateLines;
                        var containers = scenario.querySelectorAll('[data-plantuml]');
                        containers.forEach(function(c) { c._truncateLines = scenarioLines; });
                    }
                }
                syncRadioButtons(scenario, targetState);
                var containers = scenario.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildDetailsQueue(containers, targetState));
            };

            // Report-level: expand/truncate/collapse details for all scenarios
            window._setReportDetails = function(targetState) {
                window._detailsDefault = targetState;
                syncRadioButtons(document.querySelector('.toolbar-right'), targetState);
                // Reset all scenario-level radio buttons too
                document.querySelectorAll('details.scenario').forEach(function(sc) {
                    syncRadioButtons(sc, targetState);
                });
                // When switching to truncated at report level, sync container truncate lines from global
                if (targetState === 'truncated') {
                    document.querySelectorAll('[data-diagram-type="plantuml"]').forEach(function(c) {
                        c._truncateLines = window._truncateLines;
                    });
                    // Sync scenario dropdowns to match global value
                    document.querySelectorAll('.truncate-lines-select').forEach(function(s) {
                        s.value = String(window._truncateLines);
                    });
                }
                var containers = document.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildDetailsQueue(containers, targetState));
            };

            // Change truncation line count (report-level)
            window._setTruncateLines = function(sel) {
                window._truncateLines = parseInt(sel.value, 10) || 20;
                // Sync all dropdowns (report + scenario level)
                document.querySelectorAll('.truncate-lines-select').forEach(function(s) {
                    s.value = String(window._truncateLines);
                });
                // Update per-container truncate lines — include not-yet-decompressed elements
                var allDiagrams = document.querySelectorAll('[data-diagram-type="plantuml"]');
                allDiagrams.forEach(function(c) { c._truncateLines = window._truncateLines; });
                // Only re-render containers that have been decompressed (have data-plantuml)
                var containers = document.querySelectorAll('[data-plantuml]');
                // Force re-render all containers as truncated (even if already truncated — line count changed)
                processRenderQueue(buildDetailsQueue(containers, 'truncated', true));
                // Update report + scenario radio buttons to truncated state
                syncRadioButtons(document.querySelector('.toolbar-right'), 'truncated');
                document.querySelectorAll('details.scenario').forEach(function(sc) {
                    syncRadioButtons(sc, 'truncated');
                });
            };

            // Change truncation line count (scenario-level)
            window._setScenarioTruncateLines = function(sel) {
                var scenario = sel.closest('details.scenario');
                if (!scenario) return;
                var scenarioLines = parseInt(sel.value, 10) || 20;
                // Store per-container — include not-yet-decompressed elements
                var allDiagrams = scenario.querySelectorAll('[data-diagram-type="plantuml"]');
                allDiagrams.forEach(function(c) { c._truncateLines = scenarioLines; });
                // Only re-render containers that have been decompressed (have data-plantuml)
                var containers = scenario.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildDetailsQueue(containers, 'truncated', true));
                syncRadioButtons(scenario, 'truncated');
            };

            function syncToggleBtn(parent, toggleName, shown) {
                parent.querySelectorAll('.toggle-btn[data-toggle="' + toggleName + '"]').forEach(function(b) {
                    b.setAttribute('data-shown', shown ? 'true' : 'false');
                    if (shown) b.classList.add('details-active');
                    else b.classList.remove('details-active');
                    var label = toggleName.charAt(0).toUpperCase() + toggleName.slice(1);
                    b.textContent = label + (shown ? ' Shown' : ' Hidden');
                });
            }

            // Report-level: toggle headers for all scenarios
            window._toggleHeaders = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                window._headersHidden = !shown;
                syncToggleBtn(document.querySelector('.toolbar-right'), 'headers', shown);
                document.querySelectorAll('details.scenario').forEach(function(sc) {
                    syncToggleBtn(sc, 'headers', shown);
                });
                var containers = document.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildHeadersQueue(containers, !shown));
            };

            // Scenario-level: toggle headers for one scenario
            window._toggleScenarioHeaders = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                var scenario = btn.closest('details.scenario');
                if (!scenario) return;
                syncToggleBtn(scenario, 'headers', shown);
                var containers = scenario.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildHeadersQueue(containers, !shown));
            };

            function buildAssertionsQueue(containers, showing) {
                var queue = [];
                containers.forEach(function(container) {
                    if (container.classList.contains('puml-fragment')) return;
                    if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
                    var wasVisible = !!container._assertionsVisible;
                    if (wasVisible === showing) return;
                    container._assertionsVisible = showing;
                    var origSource = container._noteOriginalSource;
                    var renderSource = applyAssertionFilter(origSource, showing);
                    renderSource = applyStepsFilter(renderSource, !!container._stepsVisible);
                    renderSource = applyDatabasesFilter(renderSource, !!container._databasesVisible);
                    var noteBlocks = parseNoteBlocks(renderSource);
                    if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
                    if (!container._noteSteps) container._noteSteps = {};
                    queue.push({ container: container, noteBlocks: noteBlocks });
                });
                return queue;
            }

            // Report-level: toggle assertions for all scenarios
            window._toggleAssertions = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                window._assertionsVisible = shown;
                syncToggleBtn(document.querySelector('.toolbar-right'), 'assertions', shown);
                document.querySelectorAll('details.scenario').forEach(function(sc) {
                    syncToggleBtn(sc, 'assertions', shown);
                });
                var containers = document.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildAssertionsQueue(containers, shown));
            };

            // Scenario-level: toggle assertions for one scenario
            window._toggleScenarioAssertions = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                var scenario = btn.closest('details.scenario');
                if (!scenario) return;
                syncToggleBtn(scenario, 'assertions', shown);
                var containers = scenario.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildAssertionsQueue(containers, shown));
            };

            function buildStepsQueue(containers, showing) {
                var queue = [];
                containers.forEach(function(container) {
                    if (container.classList.contains('puml-fragment')) return;
                    if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
                    var wasVisible = !!container._stepsVisible;
                    if (wasVisible === showing) return;
                    container._stepsVisible = showing;
                    var origSource = container._noteOriginalSource;
                    var renderSource = applyAssertionFilter(origSource, !!container._assertionsVisible);
                    renderSource = applyStepsFilter(renderSource, showing);
                    renderSource = applyDatabasesFilter(renderSource, !!container._databasesVisible);
                    var noteBlocks = parseNoteBlocks(renderSource);
                    if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
                    if (!container._noteSteps) container._noteSteps = {};
                    queue.push({ container: container, noteBlocks: noteBlocks });
                });
                return queue;
            }

            // Report-level: toggle step delimiters for all scenarios
            window._toggleSteps = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                window._stepsVisible = shown;
                syncToggleBtn(document.querySelector('.toolbar-right'), 'steps', shown);
                document.querySelectorAll('details.scenario').forEach(function(sc) {
                    syncToggleBtn(sc, 'steps', shown);
                });
                var containers = document.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildStepsQueue(containers, shown));
            };

            // Scenario-level: toggle step delimiters for one scenario
            window._toggleScenarioSteps = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                var scenario = btn.closest('details.scenario');
                if (!scenario) return;
                syncToggleBtn(scenario, 'steps', shown);
                var containers = scenario.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildStepsQueue(containers, shown));
            };

            function buildDatabasesQueue(containers, showing) {
                var queue = [];
                containers.forEach(function(container) {
                    if (container.classList.contains('puml-fragment')) return;
                    if (!container._noteOriginalSource) container._noteOriginalSource = container.getAttribute('data-plantuml');
                    var wasVisible = container._databasesVisible !== false;
                    if (wasVisible === showing) return;
                    container._databasesVisible = showing;
                    var origSource = container._noteOriginalSource;
                    var renderSource = applyAssertionFilter(origSource, !!container._assertionsVisible);
                    renderSource = applyStepsFilter(renderSource, !!container._stepsVisible);
                    renderSource = applyDatabasesFilter(renderSource, showing);
                    var noteBlocks = parseNoteBlocks(renderSource);
                    if (container._truncateLines === undefined) container._truncateLines = window._truncateLines;
                    if (!container._noteSteps) container._noteSteps = {};
                    queue.push({ container: container, noteBlocks: noteBlocks });
                });
                return queue;
            }

            // Report-level: toggle database participants for all scenarios
            window._toggleDatabases = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                window._databasesVisible = shown;
                syncToggleBtn(document.querySelector('.toolbar-right'), 'databases', shown);
                document.querySelectorAll('details.scenario').forEach(function(sc) {
                    syncToggleBtn(sc, 'databases', shown);
                });
                var containers = document.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildDatabasesQueue(containers, shown));
            };

            // Scenario-level: toggle database participants for one scenario
            window._toggleScenarioDatabases = function(btn) {
                var shown = btn.getAttribute('data-shown') !== 'true';
                var scenario = btn.closest('details.scenario');
                if (!scenario) return;
                syncToggleBtn(scenario, 'databases', shown);
                var containers = scenario.querySelectorAll('[data-plantuml]');
                processRenderQueue(buildDatabasesQueue(containers, shown));
            };
        })();
        </script>
        """;
}