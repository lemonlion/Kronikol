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

    function createNoteButtons(svg, bbox, noteStep, onExpand, onContract, onTruncate, onCycle, contentLines, grp, container, forceIsLong) {
        var size = 12;
        var topSize = 14;
        var pad = 3;
        var state = noteStepState(noteStep);
        var hdrHidden = container._headersHidden !== undefined ? container._headersHidden : (container.parentElement && container.parentElement._headersHidden) || window._headersHidden;
        var longNote = forceIsLong || isLongNote(contentLines, container._truncateLines, hdrHidden);
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
                if (sibSource) {
                    var sibNotes = parseNoteBlocks(sibSource);
                    var sibCount = sibNotes.length;
                    if (sibCount > 0 && sibNotes[0].contentLines[0] &&
                        sibNotes[0].contentLines[0].indexOf('Continued From Previous Diagram') >= 0) {
                        sibCount--;
                    }
                    noteIndexOffset += sibCount;
                }
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
            (function(svgIdx, localIdx) {
                var globalFilteredIdx = fragContinuationMap
                    ? (localIdx < fragContinuationMap.length ? fragContinuationMap[localIdx] : localIdx + noteIndexOffset)
                    : localIdx + noteIndexOffset;
                var globalIdx = filteredToOrigMap ? (globalFilteredIdx < filteredToOrigMap.length ? filteredToOrigMap[globalFilteredIdx] : globalFilteredIdx) : globalFilteredIdx;
                var grp = noteGroups[svgIdx];
                var bbox = getNoteBBox(grp);
                var step = owner._noteSteps[globalIdx] || 0;
                var origContentLines = ownerNoteBlocks[globalIdx]
                    ? ownerNoteBlocks[globalIdx].contentLines
                    : (noteBlocks[localIdx] ? noteBlocks[localIdx].contentLines : []);
                // Continuation notes are always "long" — they're chunks of a
                // larger note, and expand should reveal the full original content.
                var forceIsLong = !!(fragContinuationMap && svgIdx === 0);
                // Short notes only have steps 0 (collapsed) and 2 (expanded)
                if (!forceIsLong && !isLongNote(origContentLines, container._truncateLines, owner._headersHidden) && step === 1) step = 2;
                createNoteButtons(svg, bbox, step,
                    function() {
                        var long = forceIsLong || isLongNote(origContentLines, container._truncateLines, owner._headersHidden);
                        var curStep = owner._noteSteps[globalIdx] || 0;
                        setNoteState(owner, globalIdx, (long && curStep === 0) ? 1 : 2);
                    },
                    function() { setNoteState(owner, globalIdx, 0); },
                    function() { setNoteState(owner, globalIdx, 1); },
                    function() {
                        var curStep = owner._noteSteps[globalIdx] || 0;
                        var long = forceIsLong || isLongNote(origContentLines, container._truncateLines, owner._headersHidden);
                        var nextStep;
                        if (curStep === 2) nextStep = long ? 1 : 0;
                        else if (curStep === 1) nextStep = 0;
                        else nextStep = long ? 1 : 2;
                        setNoteState(owner, globalIdx, nextStep);
                    },
                    origContentLines, grp, container, forceIsLong);
            })(ni, sourceIndexMap ? sourceIndexMap[ni] : ni);
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