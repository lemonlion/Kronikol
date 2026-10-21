<script>
(function() {
    var menu = null;
    // A note's header line starts with Kronikol's header ink: `gray` before 3.30.0, a computed hex grey from
    // it (NotePalette.HeaderInk). A merged report holds sources written before and after, so both forms are
    // header lines. collapsible-notes-script.js carries the same literal (DiagramContextMenuTests checks).
    var NOTE_HEADER_TAG = /^<color:(?:gray|#[0-9A-Fa-f]{6})>/;
    var NOTE_HEADER_TAG_INDENTED = new RegExp('^\\s*' + NOTE_HEADER_TAG.source.slice(1));
    // A message's key line (collapsible-notes-script.js carries the same literal): metadata, copied as text with the
    // headers, never as payload.
    var NOTE_KEY_LINE = /^<b>Key<\/b> /;
    var NOTE_KEY_LINE_INDENTED = /^(\s*)<b>Key<\/b> /;

    function findDiagramContainer(el) {
        while (el) {
            if (el.dataset && el.dataset.diagramType) return el;
            el = el.parentElement;
        }
        return null;
    }

    // What a zoom acts on: the part under the pointer when the page draws a long diagram in parts (.puml-fragment, each
    // its own picture inside the diagram's container), else the diagram's container. Each part zooms on its own; until
    // 5.2.2 every zoom on a diagram in parts went to its container, and so to its first part's picture.
    function zoomBoxOf(el) {
        var container = findDiagramContainer(el);
        if (!container) return null;
        var part = el.closest ? el.closest('.puml-fragment') : null;
        return part && container.contains(part) ? part : container;
    }

    function ownSlider(box) {
        return box.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
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

    // A copy carries no page CSS, so the look the page gives a drawn diagram (5.0.0, plans/OPTION_C_PLAN.md S10, Q6: the
    // theme's colours, the role pass's cards, bands and step tables, the fold it hides) is written into the copy: each
    // drawn element's paint where the stylesheet changed it, and the ground, which a copy without transparency takes. A
    // step table's ground, with no fill of its own, was drawn black (F9). A copy takes the light theme's look whichever
    // theme the page shows, as every copy did before: it is pasted into documents, nearly always on white. The theme is
    // put back in the same task, so the page paints nothing in between, and its own picture is left as it is.
    var PAINTED = /^(path|rect|line|polygon|polyline|circle|ellipse|text|tspan)$/i;
    function lookClone(svg) {
        var clone = svg.cloneNode(true);
        var from = svg.querySelectorAll('*'), to = clone.querySelectorAll('*');
        if (from.length !== to.length || !window.getComputedStyle) return { svg: clone, ground: null };
        var root = document.documentElement, theme = root.getAttribute('data-theme');
        var probe = document.createElement('i');
        probe.style.display = 'none';
        document.body.appendChild(probe);
        root.setAttribute('data-theme', 'light');
        var ground = null, seen = {};
        // a colour as an attribute writes it, as the browser computes it ('none' as it is)
        function computed(v) {
            if (!v) return null;
            if (!(v in seen)) {
                probe.style.color = '';
                probe.style.color = v;
                seen[v] = probe.style.color ? getComputedStyle(probe).color : v;
            }
            return seen[v];
        }
        try {
            for (var i = 0; i < from.length; i++) {
                if (!PAINTED.test(from[i].tagName)) continue;
                var cs = getComputedStyle(from[i]);
                for (var k = 0; k < 2; k++) {
                    var p = k ? 'stroke' : 'fill', v = cs.getPropertyValue(p);
                    if (v && v !== computed(from[i].getAttribute(p))) to[i].style.setProperty(p, v);
                }
                if (cs.visibility === 'hidden') to[i].style.setProperty('visibility', 'hidden');
            }
            var bg = getComputedStyle(svg).backgroundColor;
            if (bg && bg !== 'transparent' && !/^rgba\(.*,\s*0\)$/.test(bg)) ground = bg;
        } finally {
            if (theme === null) root.removeAttribute('data-theme'); else root.setAttribute('data-theme', theme);
            probe.remove();
        }
        return { svg: clone, ground: ground };
    }

    // XMLSerializer writes a C0 control, U+FFFE or U+FFFF as it is, and an SVG holding one is not XML: its PNG never
    // loaded (Copy and Save did nothing) and its SVG file opened in no viewer. Kronikol's generator no longer writes
    // one, but a source from an earlier report can, so the copy writes each as the generator would (§12.6).
    function serializeSvg(svg) {
        return new XMLSerializer().serializeToString(svg.isConnected ? lookClone(svg).svg : svg).replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\uFFFE\uFFFF]/g, function (c) {
            var code = c.charCodeAt(0);
            return code < 0x20 ? String.fromCharCode(0x2400 + code) : '\uFFFD';
        });
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
        var look = svg.isConnected ? lookClone(svg) : null;
        var bg = look && look.ground ? look.ground : getBackgroundColor(svg);
        var clone = look ? look.svg : svg.cloneNode(true);
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

    // "In new tab" (5.0.0, plans/OPTION_C_PLAN.md S12, round 17 item 21): the tab is opened while the click is handled,
    // which a popup blocker allows, and the picture or the text is written into it. A PNG item used to open its tab
    // only once the picture was drawn, after the click, which Firefox refused, and a blob: URL made by a file:// page has
    // an opaque origin. Where no tab opens, the blob: URL is tried as before.
    function escapeTabText(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function openTab() {
        try { return window.open('', '_blank'); } catch (e) { return null; }
    }

    function writeTab(tab, title, body, ground) {
        tab.document.open();
        tab.document.write('<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">' +
            '<title>' + escapeTabText(title) + '</title><body style="margin:0;padding:16px;background:' + ground + '">' + body + '</body>');
        tab.document.close();
    }

    // The tab is opened by the click, before the picture is drawn; the picture is written once it is.
    function openPngInTab(tab, canvas) {
        if (!tab || tab.closed) {
            canvas.toBlob(function(blob) {
                window.open(URL.createObjectURL(blob));
            }, 'image/png');
            return;
        }
        writeTab(tab, 'Diagram (PNG)', '<img alt="Diagram" style="max-width:none" src="' + canvas.toDataURL('image/png') + '">', '#f5f7fb');
    }

    function openSvgInTab(markup) {
        var tab = openTab();
        if (!tab) {
            window.open(URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml' })));
            return;
        }
        writeTab(tab, 'Diagram (SVG)', '<img alt="Diagram" style="max-width:none" src="data:image/svg+xml;base64,' +
            btoa(unescape(encodeURIComponent(markup))) + '">', '#ffffff');
    }

    function openTextInTab(text, title) {
        var tab = openTab();
        if (!tab) {
            window.open(URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' })));
            return;
        }
        writeTab(tab, title, '<pre style="margin:0;white-space:pre-wrap;font:13px/1.45 ui-monospace,Consolas,monospace">' +
            escapeTabText(text) + '</pre>', '#ffffff');
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

    // ── Turning note SOURCE lines back into what the payload actually was ───
    // Source-derived lines carry the generation-side creole escapes (~/ for /, etc.) which PlantUML
    // consumes before display, and the breaks the width budget inserted, which it marks. Both have to
    // come off, and the ORDER is not free: rejoin first, while the escaping is intact. Unescaping
    // first would turn a payload's own escaped `<U+200B>` text into a bare marker and a real newline would be
    // eaten. Every copy, open-in-new-tab and payload-extract path goes through here.
    // A creole escape (`~` and what it protects) or a code point (<U+hhhh>, 3.30.1: the generator writes
    // captured text PlantUML would act on that way), read in one pass: `~<U+0027>` is an escaped `<` and then
    // the payload's own text. A zero-width space paints nothing and is dropped. collapsible-notes-script.js
    // carries the same literal (DiagramContextMenuTests checks the two match).
    var NOTE_ESCAPE = /~([\/*_\-"\[<#=])|<U\+([0-9A-Fa-f]{4,6})>/g;

    function decodeNoteEscapes(text) {
        return text.replace(NOTE_ESCAPE, function(m, escaped, hex) {
            if (escaped !== undefined) return escaped;
            var cp = parseInt(hex, 16);
            if (cp === 0x200b) return '';
            if ((cp >= 0xd800 && cp <= 0xdfff) || cp > 0x10ffff) return m;
            return String.fromCodePoint(cp);
        });
    }

    function unescapeSourceNoteLine(l) {
        return decodeNoteEscapes(l);
    }

    function rejoinNoteSource(text) {
        return window._rejoinWrappedNoteLines ? window._rejoinWrappedNoteLines(text) : text;
    }

    function noteLinesToText(lines, unescapeLine) {
        var stripped = lines.map(function(l) { return l.replace(NOTE_HEADER_TAG_INDENTED, '').replace(NOTE_KEY_LINE_INDENTED, '$1Key '); });
        return rejoinNoteSource(stripped.join('\n')).split('\n').map(unescapeLine).join('\n');
    }

    // A note's gray header lines are a contiguous prefix: the tagged lines, then any blank lines that
    // separate them from the payload. Both copy paths need the split, so it lives in one place.
    function splitGrayHeader(lines) {
        var header = [];
        var afterGray = false;
        for (var i = 0; i < lines.length; i++) {
            var t = lines[i].trim();
            if (NOTE_HEADER_TAG.test(t) || NOTE_KEY_LINE.test(t)) { afterGray = true; header.push(lines[i]); continue; }
            if (afterGray && t === '') { header.push(lines[i]); continue; }
            break;
        }
        return { header: header, body: lines.slice(header.length) };
    }

    function joinNoteParts(headerText, bodyText) {
        return headerText ? headerText + '\n' + bodyText : bodyText;
    }

    // Text read out of the PAINTED svg rather than the source: the engine resolves the join marker to
    // a real zero-width space, so it is in the DOM and must not reach the clipboard.
    function withoutZeroWidth(text) {
        return window._stripZeroWidth ? window._stripZeroWidth(text) : text;
    }

    function paintedNoteText(grp) {
        return withoutZeroWidth(grp.texts.map(function(t) { return t.textContent; }).join('\n'));
    }

    // The selected diagram text as it is painted. Under `skinparam wrapWidth`, which every generated sequence diagram
    // carries, the engine paints each word, and each space, as its own <text>, and Chromium's Selection.toString() ends
    // every <text> with a line break: a label "GET /api/items" was copied as "GET\n \n/api/items". So the selection is
    // read from its text nodes: the pieces of one row join as they are, and a row starts at a new baseline, in another
    // svg, or where the text steps back to the left. A row painting only spaces is a note's blank line. Null when the
    // selection holds no diagram text, or holds other text too, which the browser's own reading serves.
    function selectedSvgText(selection) {
        if (!selection || !selection.rangeCount) return null;
        var rows = [], row = null, last = null;
        for (var r = 0; r < selection.rangeCount; r++) {
            var range = selection.getRangeAt(r);
            var root = range.commonAncestorContainer;
            var walker = document.createTreeWalker(root.nodeType === 3 ? root.parentNode : root, NodeFilter.SHOW_TEXT);
            for (var node = walker.nextNode(); node; node = walker.nextNode()) {
                if (!range.intersectsNode(node)) continue;
                var piece = node.data.substring(
                    node === range.startContainer ? range.startOffset : 0,
                    node === range.endContainer ? range.endOffset : node.data.length);
                var el = node.parentNode && node.parentNode.closest ? node.parentNode.closest('text') : null;
                if (!el || el.namespaceURI !== 'http://www.w3.org/2000/svg') {
                    if (/\S/.test(piece)) return null;
                    continue;
                }
                if (!piece) continue;
                var at = { svg: el.ownerSVGElement, text: el,
                    x: parseFloat(el.getAttribute('x')), y: parseFloat(el.getAttribute('y')) };
                var sameRow = last && (at.text === last.text || (at.svg === last.svg
                    && Math.abs(at.y - last.y) < 0.5 && !(at.x < last.x)));
                if (!sameRow) { row = []; rows.push(row); }
                row.push(piece);
                last = at;
            }
        }
        if (!rows.length) return null;
        return rows.map(function(pieces) {
            var line = pieces.join('');
            return /\S/.test(line) ? line : '';
        }).join('\n');
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
                // Arrows are coloured by default (SequenceDiagramArrowColors): `caller -[#438DD5]> svc` and
                // `svc -[#438DD5]-> caller` as well as the plain `->` and `-->`.
                if (line.match(new RegExp('^\\s*' + callerAlias.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\s+-(?:\\[[^\\]]*\\])?>\\s+'))) {
                    afterCallerRequest = true;
                } else if (line.match(/^\s*\S+\s+-(?:\[[^\]]*\])?->\s+/)) {
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
                    // Same treatment as Copy box text: this menu item exists to hand back the
                    // payloads as captured, so the width budget's breaks and the creole escaping both
                    // come off. Before this it returned neither.
                    var body = noteLinesToText(
                        noteLines.filter(function(l) { return !NOTE_HEADER_TAG_INDENTED.test(l); }),
                        unescapeSourceNoteLine).trim();
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

    // A right-click only: no key opens this menu. Its note items copy and open a payload's text; a note's other actions
    // (expand, fold, format, width) are its own buttons. "Accessibility" above processRenderQueue
    // (collapsible-notes-script.js) says what a keyboard route needs (5.1.1).
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

                // Compute global note index for _noteSteps lookup via the
                // shared fragment indexing (continuation chunks map back to
                // the note they continue — the buttons and this menu must
                // agree on the index they read).
                var globalNoteIdx = resolvedBlockIdx >= 0 ? resolvedBlockIdx : clickedNoteIdx;
                if (fragEl && fragEl !== container && window._computeGlobalNoteIndex) {
                    globalNoteIdx = window._computeGlobalNoteIndex(container, fragEl, globalNoteIdx);
                }
                // A note the note script reached carries the indices its buttons use: its index in the original
                // source and its block in the source it was drawn from. A filter that hides notes (Databases,
                // Assertions, Steps), a note drawn empty and a fragment each move a drawn note away from its
                // place in the original source, and the menu read a neighbour's text and state (4.26.8).
                var drawnBlockIdx = resolvedBlockIdx >= 0 ? resolvedBlockIdx : clickedNoteIdx;
                var noteIndices = window._noteIndicesOf ? window._noteIndicesOf(noteGroups[clickedNoteIdx]) : null;
                if (noteIndices) {
                    globalNoteIdx = noteIndices.original;
                    drawnBlockIdx = noteIndices.drawn;
                    noteBlocks = window._parseNoteBlocks(origSrc);
                    resolvedBlockIdx = noteBlocks[globalNoteIdx] ? globalNoteIdx : -1;
                }

                // Copy targets are the DISPLAYED lines; unescapeSourceNoteLine and the rejoin that
                // has to run before it are module-scope now, shared with extractCallerPayloads.
                // When the note is displayed as YAML, the payload lines are the
                // cached YAML lines (keyed on the owner container by global
                // note index) with the client splice escapes removed — not the
                // original JSON payload the source holds.
                var noteFormat = (container._noteFormats && container._noteFormats[globalNoteIdx]) || 'json';
                var yamlLines = noteFormat === 'yaml' && window._noteUnescapeDisplayLine
                    ? container._noteYamlLines && container._noteYamlLines[globalNoteIdx]
                    : null;

                var noteText;
                if (resolvedBlockIdx >= 0 && noteBlocks[resolvedBlockIdx]) {
                    var blockLines = noteBlocks[resolvedBlockIdx].contentLines;
                    if (yamlLines) {
                        // Full text = the note's gray header lines + the full YAML view (un-truncated
                        // CURRENT view, by design). The header is source-derived and can carry both a
                        // chunked value and its markers; the YAML lines are the client's own and
                        // carry neither.
                        var parts = splitGrayHeader(blockLines);
                        noteText = joinNoteParts(
                            noteLinesToText(parts.header, unescapeSourceNoteLine),
                            yamlLines.map(window._noteUnescapeDisplayLine).join('\n'));
                    } else {
                        noteText = noteLinesToText(blockLines, unescapeSourceNoteLine);
                    }
                    noteText = noteText.trim();
                } else {
                    noteText = paintedNoteText(noteGroups[clickedNoteIdx]);
                }

                // Check if note is truncated or collapsed
                var noteStep = container._noteSteps && container._noteSteps[globalNoteIdx];
                var isNotExpanded = noteStep !== undefined && noteStep !== 2;
                _fullNoteText = noteText;
                _noteIsNotExpanded = isNotExpanded;

                if (isNotExpanded) {
                    var currentSrc = fragEl ? (fragEl.getAttribute('data-plantuml') || '') : getSource(container);
                    var currentNoteBlocks = window._parseNoteBlocks(currentSrc);
                    var curBlockIdx = drawnBlockIdx;
                    var currentText;
                    if (currentNoteBlocks[curBlockIdx]) {
                        // Recover the displayed text: YAML payload lines carry the client splice
                        // escapes, everything else carries the generation-side ones — creole ~
                        // escapes must never reach the clipboard either way, and nor must the width
                        // budget's breaks.
                        var curLines = currentNoteBlocks[curBlockIdx].contentLines;
                        if (yamlLines) {
                            var curParts = splitGrayHeader(curLines);
                            currentText = joinNoteParts(
                                noteLinesToText(curParts.header, unescapeSourceNoteLine),
                                noteLinesToText(curParts.body, window._noteUnescapeDisplayLine));
                        } else {
                            currentText = noteLinesToText(curLines, unescapeSourceNoteLine);
                        }
                        currentText = currentText.trim();
                    } else {
                        currentText = paintedNoteText(noteGroups[clickedNoteIdx]);
                    }
                    _currentNoteText = currentText;
                    menu.appendChild(createSubMenu('Copy box text', [
                        createMenuItem('Copy full box text', function() {
                            copyTextToClipboard(noteText);
                        }),
                        createMenuItem('Copy current box text', function() {
                            copyTextToClipboard(currentText);
                        })
                    ]));
                } else {
                    menu.appendChild(createMenuItem('Copy box text', function() {
                        copyTextToClipboard(noteText);
                    }));
                }
                menu.appendChild(createSeparator());
            }
        }

        var selection = window.getSelection();
        var selectedText = (selectedSvgText(selection) || (selection || '').toString()).trim();
        // A selection inside a note is mapped back onto the note's text, which undoes the width budget's breaks
        // as well. The painted marker is a zero-width space the note's text does not hold, so it is left out of
        // the comparison: a selection across a marked break never matched, and was copied as painted.
        if (selectedText && svg && clickedNoteIdx >= 0) {
            var noteSource = _noteIsNotExpanded
                ? (_currentNoteText || _fullNoteText)
                : _fullNoteText;
            if (noteSource) {
                var selChars = withoutZeroWidth(selectedText).replace(/\s+/g, '');
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
                copyTextToClipboard(withoutZeroWidth(selectedText));
            }));
            menu.appendChild(createSeparator());
        }

        if (svg) {
            // Full SVG menu — grouped into submenus
            // A PNG reaches the clipboard only where the page can copy an image (window.canCopyImagesToClipboard): not on a
            // report served over plain http from another machine. Copy as SVG is text, copied everywhere.
            menu.appendChild(createSubMenu('Copy image', (window.canCopyImagesToClipboard ? [
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
                })
            ] : []).concat([
                createMenuItem('Copy as SVG', function() {
                    copyTextToClipboard(serializeSvg(svg));
                })
            ])));
            if (source) {
                var origSource = container._noteOriginalSource || source;
                if (origSource !== source) {
                    menu.appendChild(createSubMenu('Copy ' + typeLabel + ' source', [
                        createMenuItem('Copy full ' + typeLabel + ' source', function() {
                            copyTextToClipboard(origSource);
                        }),
                        createMenuItem('Copy current ' + typeLabel + ' source', function() {
                            copyTextToClipboard(source);
                        })
                    ]));
                } else {
                    menu.appendChild(createMenuItem('Copy ' + typeLabel + ' source', function() {
                        copyTextToClipboard(source);
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
                    var tab = openTab();
                    svgToCanvas(svg, function(canvas) { openPngInTab(tab, canvas); });
                }),
                createMenuItem('Open as PNG image (no transparency) in new tab', function() {
                    var tab = openTab();
                    svgToCanvasWithBg(svg, function(canvas) { openPngInTab(tab, canvas); });
                }),
                createMenuItem('Open as SVG image in new tab', function() {
                    openSvgInTab(serializeSvg(svg));
                })
            ]));
            if (source) {
                var origSource2 = container._noteOriginalSource || source;
                if (origSource2 !== source) {
                    menu.appendChild(createSubMenu('Open ' + typeLabel + ' source in new tab', [
                        createMenuItem('Open full ' + typeLabel + ' in new tab', function() {
                            openTextInTab(origSource2, typeLabel + ' source');
                        }),
                        createMenuItem('Open current ' + typeLabel + ' in new tab', function() {
                            openTextInTab(source, typeLabel + ' source');
                        })
                    ]));
                } else {
                    menu.appendChild(createMenuItem('Open ' + typeLabel + ' source in new tab', function() {
                        openTextInTab(source, typeLabel + ' source');
                    }));
                }
            }
            if (clickedNoteIdx >= 0 && _fullNoteText) {
                if (_noteIsNotExpanded && _currentNoteText) {
                    menu.appendChild(createSubMenu('Open box text in new tab', [
                        createMenuItem('Open full box text in new tab', function() {
                            openTextInTab(_fullNoteText, 'Box text');
                        }),
                        createMenuItem('Open current box text in new tab', function() {
                            openTextInTab(_currentNoteText, 'Box text');
                        })
                    ]));
                } else {
                    menu.appendChild(createMenuItem('Open box text in new tab', function() {
                        openTextInTab(_fullNoteText, 'Box text');
                    }));
                }
            }
        } else {
            // HTML content (flame chart, call tree) — PNG only, copied where the page can copy an image
            if (window.canCopyImagesToClipboard) menu.appendChild(createMenuItem('Copy as PNG', function() {
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
                    copyTextToClipboard(payloads);
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
        var container = zoomBoxOf(e.target);
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
        var c = zoomBoxOf(e.target);
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

        // Update container overflow. The level is kept on the box: a diagram drawn again gets a new picture and a new
        // slider, and the box gives the level back to them (5.2.3).
        var isZoomed = percent > fitPct;
        container._zoomPercent = isZoomed ? percent : undefined;
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
        var slider = ownSlider(container);
        if (slider) slider.value = String(percent);
    }



    // ── Zoom Controls (slider) ──

    function addZoomButton(container) {
        // A diagram drawn in parts zooms part by part, and its container has no picture of its own: the scroll observer
        // or a redraw asking for the container's zoom gives each drawn part its own, and takes back any the container
        // was given before it was drawn in parts.
        var parts = container.querySelectorAll(':scope > .puml-fragment');
        if (parts.length) {
            var stale = container.querySelector(':scope > .diagram-zoom-controls');
            if (stale) stale.remove();
            dropZoom(container);
            parts.forEach(function(part) { if (getSvg(part)) addZoomButton(part); });
            return;
        }
        if (container.querySelector(':scope > .diagram-zoom-controls')) return;
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

    function dropZoom(box) {
        box.classList.remove('diagram-natural-size');
        box.style.overflowX = '';
        box.style.cursor = '';
        box._zoomPercent = undefined;
        box._zoomScrollLeft = undefined;
    }

    // Re-apply zoom inline styles after SVG re-render (innerHTML replacement destroys them): the level the box was zoomed
    // to and its place across the picture (5.2.3), once the box is laid out. A box that measures nothing is drawn at
    // full size until its zoom slider is made, which restores it again.
    function restoreZoomState(container) {
        var svg = getSvg(container);
        if (!svg) return;
        if (container.classList.contains('diagram-natural-size')) {
            if (container._zoomPercent && container.clientWidth > 0) {
                applyZoomLevel(container, container._zoomPercent);
                if (typeof container._zoomScrollLeft === 'number') container.scrollLeft = container._zoomScrollLeft;
                container._zoomScrollLeft = undefined;
                return;
            }
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
        var slider = ownSlider(container);
        if (!slider) return;

        e.preventDefault();
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
        var container = zoomBoxOf(e.target);
        if (!container) return;
        var slider = ownSlider(container);
        if (!slider) return;

        e.preventDefault();
        var current = parseInt(slider.value);
        var range = 100 - parseInt(slider.min);
        var step = Math.max(1, Math.round(range * 0.05));
        var delta = e.deltaY < 0 ? step : -step;
        var newVal = Math.max(parseInt(slider.min), Math.min(100, current + delta));
        applyZoomLevel(container, newVal, e.clientX, e.clientY);
    }, { passive: false });

    // Drag-to-pan when zoomed. A drag that moved is not a click: the click the browser sends after it is dropped, so
    // panning a diagram leaves it selected or not as it was (until 5.2.2 every pan selected or unselected it).
    (function() {
        var dragging = false, dragContainer, startX, startY, scrollL, downX, downY, moved;
        function dropClick(e) { e.stopPropagation(); e.preventDefault(); }
        document.addEventListener('mousedown', function(e) {
            var c = zoomBoxOf(e.target);
            if (!c || !c.classList.contains('diagram-natural-size')) return;
            if (e.target.closest('.diagram-zoom-controls')) return;
            dragging = true;
            dragContainer = c;
            startX = e.pageX;
            startY = e.clientY;
            downX = e.clientX;
            downY = e.clientY;
            moved = false;
            scrollL = c.scrollLeft;
            c.style.cursor = 'grabbing';
            c.style.userSelect = 'none';
            e.preventDefault();
        });
        document.addEventListener('mousemove', function(e) {
            if (!dragging) return;
            if (Math.abs(e.clientX - downX) > 3 || Math.abs(e.clientY - downY) > 3) moved = true;
            dragContainer.scrollLeft = scrollL - (e.pageX - startX);
            window.scrollBy(0, startY - e.clientY);
            startY = e.clientY;
        });
        document.addEventListener('mouseup', function() {
            if (!dragging) return;
            dragging = false;
            dragContainer.style.cursor = 'grab';
            dragContainer.style.userSelect = '';
            if (!moved) return;
            // the click comes in this same task, after the mouseup; when none comes (released over another element), the
            // listener goes with the next task
            window.addEventListener('click', dropClick, { capture: true, once: true });
            setTimeout(function() { window.removeEventListener('click', dropClick, true); }, 0);
        });
    })();

    window._addZoomButton = addZoomButton;
    // the zoom a diagram drawn again keeps, for one whose zoom slider waits until it is near the screen (5.1.1)
    window._restoreZoomState = restoreZoomState;
    // A box's zoom across a redraw (5.2.2, the level and the place 5.2.3): `of` reads it while the old picture is still
    // on the page (a box taken off the page reads no scroll), `give` hands it to the box drawn in its place (the same
    // box, a new part, or each part of a diagram now drawn in parts), and `drop` takes it off a box that no longer zooms.
    // A box that is not laid out (not displayed, as in a hidden panel or a closed scenario in Firefox) reads its scroll as
    // 0, so `of` reads such a box's place from where it was last left: a scroll it has not been given back yet, else the
    // last scroll seen while it was zoomed.
    document.addEventListener('scroll', function(e) {
        var box = e.target;
        if (box.classList && box.classList.contains('diagram-natural-size') && box.clientWidth > 0) box._lastScrollLeft = box.scrollLeft;
    }, true);

    window._zoomState = {
        of: function(box) {
            var place = box.clientWidth > 0 ? box.scrollLeft
                : typeof box._zoomScrollLeft === 'number' ? box._zoomScrollLeft : box._lastScrollLeft || 0;
            return { zoomed: box.classList.contains('diagram-natural-size'), percent: box._zoomPercent,
                scrollLeft: place, selected: selectedDiagram === box };
        },
        give: function(box, state) {
            if (state.zoomed) {
                box.classList.add('diagram-natural-size');
                box._zoomPercent = state.percent;
                box._zoomScrollLeft = state.scrollLeft;
            }
            if (state.selected) selectDiagram(box);
        },
        drop: dropZoom
    };

    // The diagram's NATURAL width in CSS pixels — what it would draw at with the container's
    // `max-width: 100%` lifted. The note full-width control needs exactly this number to work out
    // how much slack a diagram has, and measuring it in two places would be two things to keep in
    // step, so the zoom code's measurement is the one both use.
    window._getDiagramNaturalWidth = function(container) {
        var svg = getSvg(container);
        if (!svg) return 0;
        var savedMax = svg.style.maxWidth;
        var savedW = svg.style.width;
        svg.style.maxWidth = 'none';
        svg.style.width = '';
        var naturalW = svg.getBoundingClientRect().width;
        svg.style.maxWidth = savedMax;
        svg.style.width = savedW;
        return naturalW;
    };

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