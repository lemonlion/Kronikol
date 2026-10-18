function createNoteButtons(svg, bbox, noteStep, onExpand, onContract, onTruncate, onCycle, contentLines, grp, container, forceIsLong, formatInfo, appearanceInfo) {
        var size = 12;
        var topSize = 14;
        var pad = 3;
        var state = noteStepState(noteStep);
        var hdrHidden = container._headersHidden !== undefined ? container._headersHidden : (container.parentElement && container.parentElement._headersHidden) || window._headersHidden;
        var longNote = forceIsLong || isLongNote(contentLines, container._truncateLines, hdrHidden); var kronWhole = state === 'collapsed' && kronCollapsedIsWhole(contentLines, hdrHidden);
        var buttons = [];
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
        }
        var formatBtn = null;
        var widthBtn = null;
        // Round 17 (owner): the top-right buttons sit side by side from the note's right edge, with no gap where a
        // hidden button (an ineligible format or width button) would be (item 14); each is created at the first
        // place and moved by a transform, which also carries a tall note's scroll offset. The triangles are drawn,
        // not set as text, exactly 2px clear of their button's top and bottom edges in every browser (item 13).
        var topRight = [];
        var upTop = null;
        function kTriangle(x, y, w, h, up, inset) {
            // inset: clearance from the button's top and bottom edges (2 px; the top-right toggle's 3 px, item 24)
            var i = inset || 2;
            var th = h - 2 * i, tw = th * 1.25, cx = x + w / 2, top = y + i, bot = y + h - i;
            var pts = up ? [cx - tw / 2, bot, cx + tw / 2, bot, cx, top] : [cx - tw / 2, top, cx + tw / 2, top, cx, bot];
            var p = document.createElementNS(SVGNS, 'polygon');
            p.setAttribute('points', pts.map(function(v) { return +v.toFixed(2); }).join(' '));
            p.setAttribute('fill', '#666');
            p.style.pointerEvents = 'none';
            return p;
        }
        function packTopRight(dy) {
            var k = 0;
            topRight.forEach(function(b) {
                if (dy !== undefined) b._kdy = dy;
                if (b.style.display === 'none') return;
                b.setAttribute('transform', 'translate(' + (-k * (topSize + pad)) + ',' + (b._kdy || 0) + ')');
                k++;
            });
        }

        // One top-right glyph button, at `slot` places in from the right edge of the note.
        // Created hidden when `lazy` — eligibility is decided on the note's first hover, never in a
        // per-render sweep.
        function glyphButton(slot, glyph, title, onClick, btnName, lazy) {
            var gx = bbox.x + bbox.width - topSize - pad;
            var gy = bbox.y + pad;
            var g = document.createElementNS(SVGNS, 'g');
            g.setAttribute('class', 'note-toggle-icon note-' + btnName + '-btn');
            g.setAttribute('data-note-btn', btnName);
            g.style.cursor = 'pointer';
            g.style.opacity = '0';
            if (lazy) g.style.display = 'none';
            var bg = document.createElementNS(SVGNS, 'rect');
            bg.setAttribute('x', gx); bg.setAttribute('y', gy);
            bg.setAttribute('width', topSize); bg.setAttribute('height', topSize);
            bg.setAttribute('rx', '2'); bg.setAttribute('fill', '#ffffff');
            bg.setAttribute('stroke', '#999'); bg.setAttribute('stroke-width', '0.5');
            var titleEl = document.createElementNS(SVGNS, 'title');
            titleEl.textContent = title;
            bg.appendChild(titleEl);
            g.appendChild(bg);
            var sym = document.createElementNS(SVGNS, 'text');
            sym.setAttribute('x', gx + topSize / 2); sym.setAttribute('y', gy + topSize - 3);
            sym.setAttribute('text-anchor', 'middle'); sym.setAttribute('font-size', '10');
            sym.setAttribute('font-family', 'sans-serif'); sym.setAttribute('fill', '#666');
            sym.setAttribute('font-weight', 'bold');
            sym.style.pointerEvents = 'none';
            sym.textContent = glyph;
            g.appendChild(sym);
            bg.addEventListener('click', function(ev) { ev.stopPropagation(); onClick(); });
            bg.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
            return g;
        }

        // Top-right area: contract buttons — shown when expanded or truncated
        if (state === 'expanded' || state === 'truncated') {
            // For expanded long notes: ▴ arrow to the left of −
            if (state === 'expanded' && longNote) {
                var ax = bbox.x + bbox.width - topSize - pad;
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
                ga.setAttribute('data-note-btn', 'truncate');
                ga.appendChild(kTriangle(ax, ay, topSize, topSize, true, 3));
                bgA.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onTruncate, ev); });
                bgA.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                buttons.push(ga);
                upTop = ga;
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
            bgC.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onContract, ev); });
            bgC.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
            buttons.push(gc);
            topRight.push(gc);
            if (upTop) topRight.push(upTop);

            // JSON ⇄ YAML format toggle — joins the top-right cluster to the
            // left of − (and ▲). Created hidden: eligibility is computed
            // lazily on the note's first hover (_noteShowButtons), so it only
            // ever shows for notes whose text reconstructs into valid JSON.
            if (formatInfo) {
                var slot = (state === 'expanded' && longNote) ? 3 : 2;
                var fx = bbox.x + bbox.width - topSize - pad;
                var fy = bbox.y + pad;
                var gf = document.createElementNS(SVGNS, 'g');
                gf.setAttribute('class', 'note-toggle-icon note-format-btn');
                gf.setAttribute('data-note-btn', 'format');
                gf.style.cursor = 'pointer';
                gf.style.opacity = '0';
                gf.style.display = 'none';
                var bgF = document.createElementNS(SVGNS, 'rect');
                bgF.setAttribute('x', fx); bgF.setAttribute('y', fy);
                bgF.setAttribute('width', topSize); bgF.setAttribute('height', topSize);
                bgF.setAttribute('rx', '2'); bgF.setAttribute('fill', '#ffffff');
                bgF.setAttribute('stroke', '#999'); bgF.setAttribute('stroke-width', '0.5');
                var titleF = document.createElementNS(SVGNS, 'title');
                titleF.textContent = formatInfo.format === 'yaml' ? 'Show payload as JSON' : 'Show payload as YAML';
                bgF.appendChild(titleF);
                gf.appendChild(bgF);
                var symF = document.createElementNS(SVGNS, 'text');
                symF.setAttribute('x', fx + topSize / 2); symF.setAttribute('y', fy + topSize - 3);
                symF.setAttribute('text-anchor', 'middle'); symF.setAttribute('font-size', '10');
                symF.setAttribute('font-family', 'sans-serif'); symF.setAttribute('fill', '#666');
                symF.setAttribute('font-weight', 'bold');
                symF.style.pointerEvents = 'none';
                // The glyph names the format a click switches TO
                symF.textContent = formatInfo.format === 'yaml' ? 'J' : 'Y';
                gf.appendChild(symF);
                bgF.addEventListener('click', function(ev) { ev.stopPropagation(); formatInfo.onToggle(); });
                bgF.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                formatBtn = gf;
                buttons.push(gf);
                topRight.push(gf);
            }

            // The full-width toggle. (4.0.0 removed the opt-in monospace glyph that could sit before it.)
            if (appearanceInfo) {
                var nextSlot = (state === 'expanded' && longNote) ? 4 : 3;
                widthBtn = glyphButton(nextSlot,
                    appearanceInfo.width === 'full' ? '\u2192\u2190' : '\u2194',
                    appearanceInfo.width === 'full' ? 'Back to the default width' : 'Widen this box to fill the diagram',
                    function() { appearanceInfo.onToggleWidth(); }, 'width', appearanceInfo.width !== 'full');
                buttons.push(widthBtn);
                topRight.push(widthBtn);
            }
        }

        // + (plus) button — top-right, shown when collapsed
        if (state === 'collapsed' && !kronWhole) {
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
            topRight.push(gp);
        }

        // Bottom-center expand button (▾) — shown when collapsed or truncated
        if ((state === 'collapsed' && !kronWhole) || state === 'truncated') {
            // Round 17 (owner, item 15): a truncated note can go either way, so its bottom holds ▲ (collapse) and
            // ▼ (expand) side by side, each two thirds of the single button's width.
            var pair = state === 'truncated';
            var expandW = pair ? size * 2 : size * 3;
            var expandH = size;
            var ex = bbox.x + (bbox.width - (pair ? expandW * 2 + pad : expandW)) / 2 + (pair ? expandW + pad : 0);
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
            ge.setAttribute('data-note-btn', 'expand-bottom');
            ge.appendChild(kTriangle(ex, ey, expandW, expandH, false));
            bgE.addEventListener('click', function(ev) { ev.stopPropagation(); onExpand(); });
            bgE.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
            buttons.push(ge);
            if (pair) {
                var cxp = ex - expandW - pad;
                var gcp = document.createElementNS(SVGNS, 'g');
                gcp.setAttribute('class', 'note-toggle-icon');
                gcp.setAttribute('data-note-btn', 'collapse-bottom');
                gcp.style.cursor = 'pointer';
                gcp.style.opacity = '0';
                var bgCP = document.createElementNS(SVGNS, 'rect');
                bgCP.setAttribute('x', cxp); bgCP.setAttribute('y', ey);
                bgCP.setAttribute('width', expandW); bgCP.setAttribute('height', expandH);
                bgCP.setAttribute('rx', '2'); bgCP.setAttribute('fill', '#ffffff');
                bgCP.setAttribute('stroke', '#999'); bgCP.setAttribute('stroke-width', '0.5');
                gcp.appendChild(bgCP);
                gcp.appendChild(kTriangle(cxp, ey, expandW, expandH, true));
                bgCP.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onContract, ev); });
                bgCP.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                buttons.push(gcp);
            }
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
            gbc.setAttribute('data-note-btn', 'truncate-bottom');
            gbc.appendChild(kTriangle(cx, cy, contractW, contractH, true));
            bgBC.addEventListener('click', function(ev) { ev.stopPropagation(); keepPlace(onTruncate, ev); });
            bgBC.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
            buttons.push(gbc);
        }

        packTopRight();

        // Hover detection via the note's own SVG elements (paths = background, texts = content).
        // This avoids an overlay rect that would block native text selection.
        var _noteHideTimeout;
        function _noteShowButtons() {
            clearTimeout(_noteHideTimeout);
            // Lazy JSON-eligibility check on the note's first hover — never a
            // per-render sweep. The verdict is cached per original note index
            // on the owner container (inside formatInfo.checkEligible).
            if (formatBtn) {
                if (formatBtn._eligible === undefined) formatBtn._eligible = formatInfo.checkEligible();
                formatBtn.style.display = formatBtn._eligible ? '' : 'none';
            }
            // Widening only does something for a note whose text actually wraps. Painted rows vs
            // source lines is an exact wrapping signal and survives both shapes that could break it:
            // a blank line paints its own whitespace-only <text> row, and each grey header line
            // paints one row.
            if (widthBtn) {
                if (widthBtn._eligible === undefined) widthBtn._eligible = appearanceInfo.canWiden();
                widthBtn.style.display = widthBtn._eligible ? '' : 'none';
            }
            // For tall notes, the top-right cluster follows the visible part of the note, so it is not scrolled off.
            var dy;
            if (bbox.height > 500 && svg.getScreenCTM) {
                try {
                    var ctm = svg.getScreenCTM();
                    if (ctm) {
                        var noteScreenTop = bbox.y * ctm.d + ctm.f;
                        var visTop = Math.max(0, noteScreenTop);
                        var visSvgY = (visTop - ctm.f) / ctm.d;
                        var btnY = Math.max(bbox.y + pad, Math.min(visSvgY + pad, bbox.y + bbox.height - topSize * 2 - pad));
                        dy = btnY - (bbox.y + pad);
                    }
                } catch(e) {}
            }
            packTopRight(dy);
            buttons.forEach(function(b) { if (b.style.display !== 'none') b.style.opacity = '1'; });
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
                    ev.stopPropagation(); ev.preventDefault(); keepPlace(onCycle, ev);
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
                return l.replace(NOTE_HEADER_TAG_INDENTED, '').replace(NOTE_KEY_LINE_INDENTED, '$1Key ');
            });
            // Read like Copy box text: the width bound's breaks joined first, then the escapes decoded (§12.5).
            var tipText = decodeNoteEscapes(rejoinWrappedNoteLines(tipLines.join('\n'))).trim();
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
            ev.stopPropagation(); ev.preventDefault(); keepPlace(onCycle, ev);
        });
        grp.paths[0].parentNode.insertBefore(hoverRect, grp.paths[0]);

        // Insert buttons on top (after hoverRect so they receive clicks)
        buttons.forEach(function(b) { svg.appendChild(b); });
    }

    // ── Fragment note indexing ──────────────────────────────────────────
    // Note state lives on the owner container keyed by ORIGINAL-source note
    // index, so every fragment must translate its local block indices. When
    // chunkLargeNotes splits one oversized note across fragments, the
    // continuation block that OPENS a later fragment is the TAIL of the last
    // note counted in the preceding fragments (original index offset - 1),
    // not a new note. The hover buttons and the context menu must agree on
    // this arithmetic, so it lives here and is exposed as
    // window._computeGlobalNoteIndex.