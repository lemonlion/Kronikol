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

    // What a click, a zoom and a pan act on: the diagram's container, for a long diagram the page draws in parts
    // (.puml-fragment, each its own picture inside the container) as for one drawn whole. A diagram in parts is one
    // diagram (plans/ZOOM_EVERY_DIAGRAM_PLAN.md Z8, Z13, 5.7.0): from 5.2.2 each part zoomed and was selected on its own,
    // and until 5.2.2 every zoom on it went to its first part's picture.
    function zoomBoxOf(el) {
        return findDiagramContainer(el);
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
    // A copied picture is plain SVG (5.4.0): a long payload's scroll box is left out and the rows the engine drew under it
    // are shown, the note's first lines and its `...`, so the copy opens anywhere and reads like a truncated note.
    function withoutScrollBoxes(clone) {
        clone.querySelectorAll('.note-scroll-frame').forEach(function(f) { f.remove(); });
        clone.querySelectorAll('.note-scroll-under').forEach(function(t) { t.classList.remove('note-scroll-under'); });
        return clone;
    }

    function lookClone(svg) {
        var clone = svg.cloneNode(true);
        // the page's zoom and placing of the picture are not the picture's: a copy is at its natural size (Z9)
        ['width', 'max-width', 'margin-left', 'margin-right'].forEach(function(p) { clone.style.removeProperty(p); });
        var from = svg.querySelectorAll('*'), to = clone.querySelectorAll('*');
        if (from.length !== to.length || !window.getComputedStyle) return { svg: withoutScrollBoxes(clone), ground: null };
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
                if (cs.visibility === 'hidden' && !from[i].classList.contains('note-scroll-under')) to[i].style.setProperty('visibility', 'hidden');
            }
            var bg = getComputedStyle(svg).backgroundColor;
            if (bg && bg !== 'transparent' && !/^rgba\(.*,\s*0\)$/.test(bg)) ground = bg;
        } finally {
            if (theme === null) root.removeAttribute('data-theme'); else root.setAttribute('data-theme', theme);
            probe.remove();
        }
        return { svg: withoutScrollBoxes(clone), ground: ground };
    }

    // The engine writes the diagram's source into the SVG as an instruction, <?plantuml-src ...?>. Drawn into the page
    // it becomes a comment, and an encoded source can hold "--", which no XML comment may: the SVG was not XML, so its
    // PNG never loaded (Copy and Save as PNG did nothing) and its SVG file opened in no viewer, for whichever diagrams
    // the engine encoded so (5.4.1). The copy writes such a comment back as the instruction it was, and leaves out any
    // other comment XML cannot hold.
    function xmlComments(node) {
        var doc = node.ownerDocument, walker = doc.createTreeWalker(node, NodeFilter.SHOW_COMMENT), bad = [], comment;
        while ((comment = walker.nextNode())) if (comment.data.indexOf('--') >= 0 || /-$/.test(comment.data)) bad.push(comment);
        bad.forEach(function(c) {
            var pi = /^\?([A-Za-z_][\w.-]*)\s+([\s\S]*?)\??$/.exec(c.data), instruction = null;
            if (pi && pi[2].indexOf('?>') < 0) {
                try { instruction = doc.createProcessingInstruction(pi[1], pi[2]); } catch (e) { instruction = null; }
            }
            if (instruction) c.parentNode.replaceChild(instruction, c);
            else c.parentNode.removeChild(c);
        });
        return node;
    }

    // XMLSerializer writes a C0 control, U+FFFE or U+FFFF as it is, and an SVG holding one is not XML: its PNG never
    // loaded (Copy and Save did nothing) and its SVG file opened in no viewer. Kronikol's generator no longer writes
    // one, but a source from an earlier report can, so the copy writes each as the generator would (§12.6).
    function serializeSvg(svg) {
        return new XMLSerializer().serializeToString(xmlComments(svg.isConnected ? lookClone(svg).svg : svg)).replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\uFFFE\uFFFF]/g, function (c) {
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

    // A browser draws a canvas of at most 32,767 px a side and 268,435,456 px in all (Chromium; Firefox and Safari on a
    // computer take at least as much). A PNG is drawn at twice the picture's natural size, or at the largest scale under
    // that which fits (plans/ZOOM_EVERY_DIAGRAM_PLAN.md Q10, 5.7.0): until then a picture too large drew nothing.
    var CANVAS_SIDE = 32767, CANVAS_AREA = 268435456;
    function canvasScaleFor(w, h) {
        if (!(w > 0 && h > 0)) return 2;
        return Math.min(2, CANVAS_SIDE / w, CANVAS_SIDE / h, Math.sqrt(CANVAS_AREA / (w * h)));
    }

    // `svg` is a drawn picture, or the whole picture of a diagram in parts ({ svg, ground }, wholeSvg).
    function svgToCanvas(svg, callback) {
        var svgData = serializeSvg(svg.svg || svg);
        var url = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgData)));
        var img = new Image();
        img.onload = function() {
            var scale = canvasScaleFor(img.naturalWidth, img.naturalHeight);
            var canvas = document.createElement('canvas');
            canvas.width = Math.floor(img.naturalWidth * scale);
            canvas.height = Math.floor(img.naturalHeight * scale);
            var ctx = canvas.getContext('2d');
            ctx.scale(scale, scale);
            ctx.drawImage(img, 0, 0);
            callback(canvas);
        };
        img.src = url;
    }

    function svgToCanvasWithBg(svg, callback) {
        var look = svg.svg ? svg : svg.isConnected ? lookClone(svg) : null;
        if (svg.svg) svg = svg.svg;
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
        img.onload = function() {
            var scale = canvasScaleFor(img.naturalWidth, img.naturalHeight);
            var canvas = document.createElement('canvas');
            canvas.width = Math.floor(img.naturalWidth * scale);
            canvas.height = Math.floor(img.naturalHeight * scale);
            var ctx = canvas.getContext('2d');
            ctx.scale(scale, scale);
            ctx.drawImage(img, 0, 0);
            callback(canvas);
        };
        img.src = url;
    }

    // A diagram's source as PlantUML encodes it into a picture's <?plantuml-src ...?>: raw deflate, then PlantUML's own 64
    // characters. Null where the browser cannot compress (no CompressionStream): the picture is then copied with no source.
    var PLANTUML_64 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_';
    function encodeSource(text) {
        if (typeof CompressionStream !== 'function' || typeof ReadableStream !== 'function') return Promise.resolve(null);
        try {
            var bytes = new TextEncoder().encode(text);
            var stream = new ReadableStream({ start: function(c) { c.enqueue(bytes); c.close(); } })
                .pipeThrough(new CompressionStream('deflate-raw'));
            return new Response(stream).arrayBuffer().then(function(buf) {
                var b = new Uint8Array(buf), out = '';
                for (var i = 0; i < b.length; i += 3) {
                    var b1 = b[i], b2 = i + 1 < b.length ? b[i + 1] : 0, b3 = i + 2 < b.length ? b[i + 2] : 0;
                    out += PLANTUML_64.charAt(b1 >> 2) + PLANTUML_64.charAt(((b1 & 3) << 4) | (b2 >> 4))
                        + PLANTUML_64.charAt(((b2 & 15) << 2) | (b3 >> 6)) + PLANTUML_64.charAt(b3 & 63);
                }
                return out;
            }, function() { return null; });
        } catch (e) {
            return Promise.resolve(null);
        }
    }

    // A part's ids made its own: two parts' pictures can use the same id, and a reference within a part goes to its own.
    function prefixIds(root, prefix) {
        var ids = {};
        [root].concat(Array.prototype.slice.call(root.querySelectorAll('[id]'))).forEach(function(e) {
            if (!e.id) return;
            ids[e.id] = prefix + e.id;
            e.id = prefix + e.id;
        });
        Array.prototype.forEach.call(root.querySelectorAll('*'), function(e) {
            for (var a = 0; a < e.attributes.length; a++) {
                var at = e.attributes[a], v = at.value;
                if (v.indexOf('#') < 0) continue;
                var nv = v.replace(/url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)/g, function(m, id) { return ids[id] ? 'url(#' + ids[id] + ')' : m; });
                if (at.localName === 'href' && nv.charAt(0) === '#' && ids[nv.slice(1)]) nv = '#' + ids[nv.slice(1)];
                if (nv !== v) at.value = nv;
            }
        });
    }

    // The engine writes the source it was given into each picture, drawn into the page as a comment; a part's holds the
    // lines the page added to draw it (Z12)
    function withoutSourceComments(root) {
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_COMMENT), found = [], c;
        while ((c = walker.nextNode())) if (/^\?plantuml-src\b/.test(c.data)) found.push(c);
        found.forEach(function(n) { n.parentNode.removeChild(n); });
        return root;
    }

    // The size of a diagram in parts drawn whole at its natural size: its widest part as the parts stand, and every part's
    // height together.
    function wholeSizeOf(container) {
        var w = 0, h = 0;
        drawnPartsOf(container).forEach(function(part) {
            var s = getSvg(part), vb = s.viewBox && s.viewBox.baseVal;
            var pw = (s.width.baseVal && s.width.baseVal.value) || (vb && vb.width) || 0;
            var ph = (s.height.baseVal && s.height.baseVal.value) || (vb && vb.height) || 0;
            w = Math.max(w, pw + shiftOf(s));
            h += ph;
        });
        return { w: w, h: h };
    }

    // One picture of a diagram in parts (Z13): every part's picture in order, at its natural size, each placed across as
    // the page places it at full size (lined up by its shift, else in the middle), each part's ids its own, the parts'
    // own sources left out and one for the diagram's own (`encoded`), as a diagram drawn whole carries.
    function wholeSvg(container, encoded) {
        var NS = 'http://www.w3.org/2000/svg';
        var parts = drawnPartsOf(container), size = wholeSizeOf(container);
        var lined = parts.some(function(p) { return typeof p._kShift === 'number'; });
        var out = document.createElementNS(NS, 'svg'), y = 0, ground = null;
        out.setAttribute('xmlns', NS);
        out.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');
        parts.forEach(function(part, i) {
            var svg = getSvg(part), look = lookClone(svg), clone = withoutSourceComments(look.svg);
            if (!ground) ground = look.ground;
            var vb = svg.viewBox && svg.viewBox.baseVal;
            var w = (svg.width.baseVal && svg.width.baseVal.value) || (vb && vb.width) || 0;
            var h = (svg.height.baseVal && svg.height.baseVal.value) || (vb && vb.height) || 0;
            prefixIds(clone, 'p' + (i + 1) + '-');
            // the page's zoom and placing, which a copy at the natural size does not carry
            ['width', 'max-width', 'margin-left', 'margin-right', 'display', 'margin-inline'].forEach(function(p) { clone.style.removeProperty(p); });
            clone.setAttribute('x', String(lined ? shiftOf(svg) : (size.w - w) / 2));
            clone.setAttribute('y', String(y));
            clone.setAttribute('width', String(w));
            clone.setAttribute('height', String(h));
            out.appendChild(clone);
            y += h;
        });
        out.setAttribute('width', String(size.w));
        out.setAttribute('height', String(y));
        out.setAttribute('viewBox', '0 0 ' + size.w + ' ' + y);
        if (encoded) out.insertBefore(document.createProcessingInstruction('plantuml-src', encoded), out.firstChild);
        return { svg: out, ground: ground };
    }

    // What the picture items of the menu act on (Z13, Q10). A diagram drawn whole: its picture. A diagram in parts: the
    // whole diagram, its source encoded as the menu opens so that an item copies at once; until every part is drawn the
    // items are left out. One too long for a canvas even at its own size: its PNG items give the part under the click.
    // One whose part will never draw (a render error, too large, nothing to draw: the page has settled it with no
    // picture) gives the part under the click, as before 5.7.0, and its items say which: until 5.7.3 it had no picture
    // items at all.
    function pictureOf(container, svg) {
        var all = container.querySelectorAll(':scope > .puml-fragment:not(.puml-fragment-new)');
        if (!all.length) return { ready: true, svgOf: function() { return svg; }, pngOf: function() { return svg; }, asPng: 'as PNG', asSvg: 'as SVG' };
        var drawn = drawnPartsOf(container), encoded = null;
        if (drawn.length !== all.length) {
            var settled = Array.prototype.every.call(all, function(p) { return !!getSvg(p) || !!p.dataset.rendered; });
            var under = svg.closest ? svg.closest('.puml-fragment') : null;
            if (!settled || !under || !getSvg(under) || !container.contains(under)) return { ready: false };
            var which = 'part ' + (Array.prototype.indexOf.call(all, under) + 1) + ' of ' + all.length;
            return { ready: true, svgOf: function() { return svg; }, pngOf: function() { return svg; }, asPng: which + ' as PNG', asSvg: which + ' as SVG' };
        }
        encodeSource(getSource(container)).then(function(e) { encoded = e; });
        var size = wholeSizeOf(container), part = svg.closest ? svg.closest('.puml-fragment') : null;
        var tooLong = canvasScaleFor(size.w, size.h) < 1 && part && container.contains(part);
        return {
            ready: true,
            svgOf: function() { return wholeSvg(container, encoded).svg; },
            pngOf: function() { return tooLong ? svg : wholeSvg(container, encoded); },
            asPng: tooLong ? 'part ' + (Array.prototype.indexOf.call(all, part) + 1) + ' of ' + all.length + ' as PNG' : 'as PNG',
            asSvg: 'as SVG'
        };
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

    // A FocusFields payload's emphasis (its focused field in <b>, the rest in <color:lightgray>) is Kronikol's markup, not
    // the payload, so it goes with the header tags (5.4.0). It comes off each line once a long run's pieces are joined:
    // one pair of tags can span them (collapsible-notes-script.js, liftEmphasisOverJoins).
    function noteLinesToText(lines, unescapeLine) {
        var headless = lines.map(function(l) {
            return l.replace(NOTE_HEADER_TAG_INDENTED, '').replace(NOTE_KEY_LINE_INDENTED, '$1Key ');
        });
        if (window._liftEmphasisOverJoins) headless = window._liftEmphasisOverJoins(headless);
        return rejoinNoteSource(headless.join('\n')).split('\n').map(function(l) {
            return unescapeLine(window._stripNoteEmphasis ? window._stripNoteEmphasis(l) : l);
        }).join('\n');
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
                // A long payload's box (5.4.0) holds the note's body as text, a line to a row; the rows the engine drew
                // under it, and the clear row that keeps the note as wide as its widest line, are not what the reader sees.
                var box = node.parentNode && node.parentNode.closest ? node.parentNode.closest('.note-scroll') : null;
                if (box) {
                    if (!piece) continue;
                    piece.split('\n').forEach(function(seg, k) {
                        if (k > 0 || !(last && last.box === box)) { row = []; rows.push(row); }
                        row.push(seg);
                    });
                    last = { box: box };
                    continue;
                }
                var el = node.parentNode && node.parentNode.closest ? node.parentNode.closest('text') : null;
                if (!el || el.namespaceURI !== 'http://www.w3.org/2000/svg') {
                    if (/\S/.test(piece)) return null;
                    continue;
                }
                if (!piece || el.classList.contains('note-scroll-under')
                    || /^#[0-9a-f]{6}00$/i.test(el.getAttribute('fill') || '')) continue;
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
                        // A long payload's box holds its lines in place of the rows drawn under it, which end in a clear
                        // row (5.4.0): its current text is read from them, with the note's drawn header lines.
                        var boxLines = window._noteScrollLines ? window._noteScrollLines(container, globalNoteIdx) : null;
                        if (boxLines) curLines = splitGrayHeader(curLines).header.concat(boxLines);
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
            // A diagram drawn in parts is one picture (plans/ZOOM_EVERY_DIAGRAM_PLAN.md Z13, 5.7.0): its picture items copy,
            // save and open every part's picture as one, with one source, and are left out until every part is drawn, as
            // for a box with no picture. Too long for a canvas even at its own size, its PNG items give the part under the
            // click and say which (Q10). Until 5.7.0 they gave the part under the click.
            var picture = pictureOf(container, svg);
            var svgOf = picture.svgOf, pngOf = picture.pngOf, asPng = picture.asPng, asSvg = picture.asSvg;
            // A PNG reaches the clipboard only where the page can copy an image (window.canCopyImagesToClipboard): not on a
            // report served over plain http from another machine. Copy as SVG is text, copied everywhere.
            if (picture.ready) menu.appendChild(createSubMenu('Copy image', (window.canCopyImagesToClipboard ? [
                createMenuItem('Copy ' + asPng, function() {
                    svgToCanvas(pngOf(), function(canvas) {
                        canvas.toBlob(function(blob) {
                            navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                        }, 'image/png');
                    });
                }),
                createMenuItem('Copy ' + asPng + ' (no transparency)', function() {
                    svgToCanvasWithBg(pngOf(), function(canvas) {
                        canvas.toBlob(function(blob) {
                            navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                        }, 'image/png');
                    });
                })
            ] : []).concat([
                createMenuItem('Copy ' + asSvg, function() {
                    copyTextToClipboard(serializeSvg(svgOf()));
                })
            ])));
            // The source the picture was composed from before a long payload's cap (5.4.0, Q4): the cap is how the page
            // shows a payload, not part of the diagram, so a copied source never holds its clear rows.
            var currentSource = (source && window._noteUncappedSource && window._noteUncappedSource(container, source)) || source;
            if (source) {
                var origSource = container._noteOriginalSource || currentSource;
                if (origSource !== currentSource) {
                    menu.appendChild(createSubMenu('Copy ' + typeLabel + ' source', [
                        createMenuItem('Copy full ' + typeLabel + ' source', function() {
                            copyTextToClipboard(origSource);
                        }),
                        createMenuItem('Copy current ' + typeLabel + ' source', function() {
                            copyTextToClipboard(currentSource);
                        })
                    ]));
                } else {
                    menu.appendChild(createMenuItem('Copy ' + typeLabel + ' source', function() {
                        copyTextToClipboard(currentSource);
                    }));
                }
            }
            menu.appendChild(createSeparator());
            if (picture.ready) menu.appendChild(createSubMenu('Save image', [
                createMenuItem('Save ' + asPng, function() {
                    svgToCanvas(pngOf(), function(canvas) {
                        canvas.toBlob(function(blob) {
                            var a = document.createElement('a');
                            a.href = URL.createObjectURL(blob);
                            a.download = getDiagramFilename(container, 'png');
                            a.click();
                            URL.revokeObjectURL(a.href);
                        }, 'image/png');
                    });
                }),
                createMenuItem('Save ' + asPng + ' (no transparency)', function() {
                    svgToCanvasWithBg(pngOf(), function(canvas) {
                        canvas.toBlob(function(blob) {
                            var a = document.createElement('a');
                            a.href = URL.createObjectURL(blob);
                            a.download = getDiagramFilename(container, 'png');
                            a.click();
                            URL.revokeObjectURL(a.href);
                        }, 'image/png');
                    });
                }),
                createMenuItem('Save ' + asSvg, function() {
                    var blob = new Blob([serializeSvg(svgOf())], { type: 'image/svg+xml' });
                    var a = document.createElement('a');
                    a.href = URL.createObjectURL(blob);
                    a.download = getDiagramFilename(container, 'svg');
                    a.click();
                    URL.revokeObjectURL(a.href);
                })
            ]));
            if (picture.ready) menu.appendChild(createSubMenu('Open image in new tab', [
                createMenuItem('Open ' + asPng + ' image in new tab', function() {
                    var tab = openTab();
                    svgToCanvas(pngOf(), function(canvas) { openPngInTab(tab, canvas); });
                }),
                createMenuItem('Open ' + asPng + ' image (no transparency) in new tab', function() {
                    var tab = openTab();
                    svgToCanvasWithBg(pngOf(), function(canvas) { openPngInTab(tab, canvas); });
                }),
                createMenuItem('Open ' + asSvg + ' image in new tab', function() {
                    openSvgInTab(serializeSvg(svgOf()));
                })
            ]));
            if (source) {
                var origSource2 = container._noteOriginalSource || currentSource;
                if (origSource2 !== currentSource) {
                    menu.appendChild(createSubMenu('Open ' + typeLabel + ' source in new tab', [
                        createMenuItem('Open full ' + typeLabel + ' in new tab', function() {
                            openTextInTab(origSource2, typeLabel + ' source');
                        }),
                        createMenuItem('Open current ' + typeLabel + ' in new tab', function() {
                            openTextInTab(currentSource, typeLabel + ' source');
                        })
                    ]));
                } else {
                    menu.appendChild(createMenuItem('Open ' + typeLabel + ' source in new tab', function() {
                        openTextInTab(currentSource, typeLabel + ' source');
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
        followSoon();
    }

    function deselectDiagram() {
        if (selectedDiagram) {
            selectedDiagram.classList.remove('diagram-selected');
            selectedDiagram = null;
        }
    }

    document.addEventListener('click', function(e) {
        // a click inside a payload's scroll box places the caret or works its scrollbar: the selection stays (5.4.0)
        if (e.target.closest && e.target.closest('.note-scroll')) return;
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

    // Every drawn diagram zooms (plans/ZOOM_EVERY_DIAGRAM_PLAN.md, 5.7.0). A picture wider than its box's content is
    // "wide": its slider runs from the level that fits it to 100 and starts there. One no wider "fits": its slider runs
    // from ZOOM_OUT_FLOOR to 100 and starts at 100. Until 5.7.0 only a wide picture had a slider, and Ctrl with the wheel
    // over any other diagram was left to the browser, which zoomed the whole page.
    // The floor is one constant, the owner's choice (Q1): text paints at 5.5 to 6.5 px there.
    var ZOOM_OUT_FLOOR = 50;

    // The picture's width at its natural (unscaled) size. The engine writes it on the picture in pixels, and the page's
    // style sizes the picture by it, so it is read from there: drawing the picture at that size to measure it and back
    // laid out every word in it twice, about 40 ms on a long diagram, several times for each zoom slider (5.5.1). A
    // width written in other units is measured as before. A picture with no box (in a hidden panel, or a scenario closed
    // in Firefox) measures 0, as it did when it was measured: a slider is made for a box only once it is laid out, and
    // the note width control falls back (5.5.3: 5.5.1 gave such a box a slider at 1% that it kept once shown).
    function naturalWidthOf(svg) {
        var w = svg.width && svg.width.baseVal;
        if (w && (w.unitType === 1 || w.unitType === 5) && w.value > 0) return svg.getClientRects().length ? w.value : 0; // a number, or pixels
        var saved = svg.style.maxWidth;
        var savedW = svg.style.width;
        svg.style.maxWidth = 'none';
        svg.style.width = '';
        var naturalW = svg.getBoundingClientRect().width;
        svg.style.maxWidth = saved;
        svg.style.width = savedW;
        return naturalW;
    }

    // A diagram the page draws in parts holds its parts as children; parts still being drawn by a redraw are hidden
    // (.puml-fragment-new) and are not its parts until they replace the old ones: until then its pictures are the old
    // parts, or the picture it was drawn whole in (5.2.5: asked meanwhile, it keeps its zoom for its new parts).
    function hasParts(box) {
        return !!box.querySelector(':scope > .puml-fragment:not(.puml-fragment-new)');
    }

    function drawnPartsOf(box) {
        return Array.prototype.filter.call(box.querySelectorAll(':scope > .puml-fragment:not(.puml-fragment-new)'),
            function(part) { return !!getSvg(part); });
    }

    // The pictures a box zooms: each drawn part's, in order, or its own.
    function picturesOf(box) {
        if (hasParts(box)) return drawnPartsOf(box).map(getSvg);
        var svg = box.querySelector(':scope > svg');
        return svg ? [svg] : [];
    }

    // Get the natural (unscaled) width of a box's picture: its widest part's, as the parts stand, for a diagram in parts
    function getNaturalWidth(container) {
        var f = zoomFacts(container);
        if (f) return f.width;
        var svg = getSvg(container);
        return svg ? naturalWidthOf(svg) : 0;
    }

    // The width a box draws its picture in: its own less its padding. A diagram's box has 1em of padding on each side
    // (inline-svg-styles.css) and its picture is drawn no wider than what is left; until 5.7.0 the fit was measured
    // against the whole box, so a picture a little wider than the content was drawn scaled down with no slider, and a
    // wider one's slider started above the level it was drawn at (B1).
    function contentWidthOf(box) {
        var w = box.clientWidth;
        if (!w) return 0;
        var cs = window.getComputedStyle(box);
        return w - (parseFloat(cs.paddingLeft) || 0) - (parseFloat(cs.paddingRight) || 0);
    }

    // How far right a lined-up part stands at full size, so that its lifelines are under the other parts'
    // (plantuml-browser-render-script.js lines a diagram's parts up and gives each part its shift, 5.7.0).
    function shiftOf(svg) {
        var part = svg.parentElement;
        return part && typeof part._kShift === 'number' ? part._kShift : 0;
    }

    // What the zoom rules read of a box, once per zoom: null when it has no picture, is not laid out, or a picture
    // measures nothing. A diagram in parts is measured whole: its widest part, the parts placed as they stand (Z8).
    function zoomFacts(box) {
        var pics = picturesOf(box);
        if (!pics.length) return null;
        var naturals = [];
        for (var i = 0; i < pics.length; i++) {
            var w = naturalWidthOf(pics[i]);
            if (!(w > 0)) return null;
            naturals.push(w);
        }
        return factsOf(box, pics, naturals, hasParts(box));
    }

    // The zoom rules' facts for pictures of a box at their natural widths. A box as wide as what it holds (the
    // internal-flow popup stands in the middle of the window) narrows with its picture zoomed out: measured then, a picture
    // that fits took itself for a wide one at its fit level, and every other notch drew it whole again (5.7.0 to 5.7.2).
    // Zoomed out, a box is taken at the width it had before, which the window changing width forgets (the resize below).
    function factsOf(box, pics, naturals, parts) {
        var cw = contentWidthOf(box);
        if (box.classList.contains('diagram-zoomed-out')) cw = Math.max(cw, box._kContentWidth || 0);
        else box._kContentWidth = cw;
        if (cw <= 0) return null;
        var width = 0;
        for (var i = 0; i < pics.length; i++) width = Math.max(width, naturals[i] + shiftOf(pics[i]));
        var fitPct = Math.max(1, Math.min(100, Math.round(cw / width * 100)));
        var wide = fitPct < 100;
        return { pics: pics, naturals: naturals, parts: parts, cw: cw, width: width, wide: wide,
            fit: wide ? cw / width : 1, fitPct: fitPct, min: wide ? fitPct : ZOOM_OUT_FLOOR, start: wide ? fitPct : 100 };
    }

    // The new parts of a diagram drawn again, sized and placed at the level the diagram shows while they are still
    // hidden, before they take the old ones' place (collapsible-notes-script.js). Shown as the engine drew them, each was
    // sized a frame after the paint, a part a frame, and a lined-up part laid out again for its new place (about 25 ms
    // a part on a long diagram: a toggle of the consumer report's 15-part diagram ended 300 ms later than with no lining
    // up), and the first frame showed the parts unplaced (5.7.3). The slider, the marks and the level are the slider's,
    // made after the paint, which then finds every size in place. A width is read from the picture, which is not laid
    // out while hidden.
    window._sizeHiddenParts = function(container, parts) {
        var pics = [], naturals = [];
        for (var i = 0; i < parts.length; i++) {
            var svg = getSvg(parts[i]), w = svg && svg.width && svg.width.baseVal;
            if (!svg) continue;
            if (!(w && (w.unitType === 1 || w.unitType === 5) && w.value > 0)) return;
            pics.push(svg);
            naturals.push(w.value);
        }
        var f = pics.length ? factsOf(container, pics, naturals, true) : null;
        if (!f) return;
        var percent = Math.max(f.min, Math.min(100, Math.round(levelFor(container, f))));
        var zoomedIn = f.wide && percent > f.fitPct, zoomedOut = !f.wide && percent < 100;
        var size = partSizer(f, f.wide && !zoomedIn ? f.fit : percent / 100, zoomedIn, zoomedOut);
        for (var k = 0; k < pics.length; k++) size(k);
    };

    // The level a box shows when its slider is made or measured again: a level it was zoomed to is kept in its own
    // direction only (Z7), moved into the new range: zoomed in, while its picture is still wide; zoomed out, while it
    // still fits. Otherwise a wide picture starts at its fit level and one that fits at 100, so a diagram zoomed in and
    // drawn again to fit comes back at full size (5.2.5), and one zoomed out and drawn again wider at its fit level.
    function levelFor(box, f) {
        var kept = box._zoomPercent;
        if (kept && f.wide && box.classList.contains('diagram-natural-size')) return Math.max(f.min, Math.min(100, kept));
        if (kept && !f.wide && box.classList.contains('diagram-zoomed-out')) return Math.max(f.min, Math.min(100, kept));
        return f.start;
    }

    // Track last known cursor position per container
    document.addEventListener('mousemove', function(e) {
        var c = zoomBoxOf(e.target);
        if (c) { c._lastCursorX = e.clientX; c._lastCursorY = e.clientY; }
        if (c !== hoveredBox) { hoveredBox = c; followSoon(); }
    });

    // The browser keeps what is on screen in place when something above it changes size (scroll anchoring), except in
    // Safari. A zoom keeps the reader's place itself (Z6), so the browser's own adjustment is held off from the change to
    // the frame after, or the two would add up. A panel of its own (the internal-flow popup) needs no hold: its one
    // picture, zoomed under the pointer, is in view, and the browser anchors on it or on what is above it, neither of
    // which moves.
    var anchoringHeld = 0;
    function holdScrollAnchoring() {
        var root = document.documentElement;
        if (!anchoringHeld++) root.style.overflowAnchor = 'none';
        requestAnimationFrame(function() {
            setTimeout(function() { if (!--anchoringHeld) root.style.overflowAnchor = ''; }, 0);
        });
    }

    // What a box scrolls in down the page: the window, or a panel of its own (the internal-flow popup, a fixed overlay):
    // the panel when it scrolls, and when it does not, a fixed panel still, as the window's scroll moves only the page
    // behind it (null for the window). Until 5.7.3 a zoom in the popup scrolled the report behind it.
    function scrollerOf(box) {
        for (var el = box.parentElement; el && el !== document.body && el !== document.documentElement; el = el.parentElement) {
            var cs = window.getComputedStyle(el);
            if ((cs.overflowY === 'auto' || cs.overflowY === 'scroll') && el.scrollHeight > el.clientHeight) return el;
            if (cs.position === 'fixed') return el;
        }
        return null;
    }

    // The reader's place before a zoom: the point of a picture under the pointer, as a fraction of the picture; with no
    // pointer over the box, what is in the middle of the window (a picture's point, or the element there). A point
    // between two parts of a diagram, in the space that parts them, is taken in the nearest part (until 5.7.3 nothing
    // kept it, though the browser's own scroll anchoring was held off).
    function placeBefore(box, pics, x, y) {
        if (typeof x !== 'number' || typeof y !== 'number') {
            x = window.innerWidth / 2;
            y = window.innerHeight / 2;
            var r0 = box.getBoundingClientRect();
            if (r0.bottom < 0 || r0.top > window.innerHeight) {
                // a box above the window moves what is on screen by the height it gains
                return r0.bottom < 0 ? { el: box, bottom: r0.bottom } : null;
            }
        }
        var nearest = null, distance = Infinity;
        for (var i = 0; i < pics.length; i++) {
            var r = pics[i].getBoundingClientRect();
            if (!(r.width > 0 && r.height > 0)) continue;
            if (y >= r.top && y <= r.bottom) return { svg: pics[i], fx: (x - r.left) / r.width, fy: (y - r.top) / r.height, x: x, y: y };
            var d = y < r.top ? r.top - y : y - r.bottom;
            if (d < distance) { distance = d; nearest = { svg: pics[i], fx: (x - r.left) / r.width, fy: (y - r.top) / r.height, x: x, y: y }; }
        }
        var el = document.elementFromPoint(x, y);
        if (el && !box.contains(el)) return { el: el, top: el.getBoundingClientRect().top };
        return el ? nearest : null;
    }

    // Puts the reader's place back: down the page by the window's scroll (or the panel's the box scrolls in), across by
    // the box's while it scrolls. Returns what of the move down the page the window could not make: it scrolls by whole
    // pixels, so the parts resized one a frame after a notch carry it from one to the next (carried), or the reader's
    // place drifted by up to a pixel a part, 5 px over a long diagram.
    function keepPlace(box, place, across, carried) {
        if (!place) return carried || 0;
        var dx = 0, dy;
        if (place.svg) {
            var r = place.svg.getBoundingClientRect();
            dx = r.left + place.fx * r.width - place.x;
            dy = r.top + place.fy * r.height - place.y;
        } else if (typeof place.bottom === 'number') {
            dy = place.el.getBoundingClientRect().bottom - place.bottom;
        } else {
            dy = place.el.getBoundingClientRect().top - place.top;
        }
        if (across && Math.abs(dx) > 0.5) box.scrollLeft = Math.max(0, box.scrollLeft + dx);
        dy += carried || 0;
        if (Math.abs(dy) <= 0.5) return dy;
        var scroller = scrollerOf(box);
        if (scroller) {
            var top = scroller.scrollTop;
            scroller.scrollTop = top + dy;
            return dy - (scroller.scrollTop - top);
        }
        var was = window.scrollY;
        window.scrollBy(0, dy);
        return dy - (window.scrollY - was);
    }

    // The marks of a level: zoomed in past the fit level, the box scrolls across and pans (diagram-natural-size); zoomed
    // out below 100, a picture that fits stands in the middle of its box with no scroll and no pan (diagram-zoomed-out, a
    // mark with no style, Z5). The box is made to scroll before its scroll is set: a part's first step lost its place
    // across when it was the other way round (B3).
    function setZoomMarks(box, zoomedIn, zoomedOut) {
        box.classList.toggle('diagram-natural-size', zoomedIn);
        box.classList.toggle('diagram-zoomed-out', zoomedOut);
        box.style.overflowX = zoomedIn ? 'auto' : '';
        box.style.overflowY = '';
    }

    function sizeWhole(svg, natural, percent, scale, zoomedIn, zoomedOut) {
        if (zoomedIn) {
            svg.style.maxWidth = 'none';
            svg.style.width = percent >= 100 ? '' : (natural * scale) + 'px';
        } else if (zoomedOut) {
            svg.style.maxWidth = '100%';
            svg.style.width = (natural * scale) + 'px';
        } else {
            svg.style.maxWidth = '100%';
            svg.style.width = '';
        }
    }

    // A diagram in parts is drawn at one level (Z8). Lined up, each part stands at its shift, the parts together in the
    // middle of the box; otherwise each part stands in the middle on its own. Drawn to fit (a wide diagram at its fit
    // level, or one a hair wider than its box at 100, whose fit level rounds to 100), the widest part is drawn a quarter
    // of a pixel narrower than the box's content, so that rounding never gives the box a scrollbar, and a part drawn
    // with the box's own width (as the engine draws it) is not a width to change. Until 5.7.3 a diagram a hair wider
    // than its box was drawn at its natural width, past the box, and the box's slider made again (every redraw) held the
    // browser's scroll anchoring off for a change it never made. The window changing width makes the slider again.
    // Pixels, not percent: zoomed in, a part is as wide as its picture (inline-svg-styles.css), and a part not yet
    // resized whose picture was sized in percent was laid out again at once, every part in the notch's own frame.
    function partSizer(f, scale, zoomedIn, zoomedOut) {
        var lined = f.pics.some(function(svg) { return svg.parentElement && typeof svg.parentElement._kShift === 'number'; });
        if (!zoomedIn && !zoomedOut && f.width > f.cw) scale = Math.max(0, f.cw - 0.25) / f.width;
        var left = Math.max(0, (f.cw - f.width * scale) / 2);
        var size = function(i) {
            var svg = f.pics[i];
            if (!svg.isConnected) return;
            svg.style.maxWidth = zoomedIn ? 'none' : '100%';
            svg.style.width = (f.naturals[i] * scale) + 'px';
            svg.style.marginLeft = lined ? (left + shiftOf(svg) * scale) + 'px' : '';
            svg.style.marginRight = lined ? '0' : '';
        };
        size.width = function(i) { return f.naturals[i] * scale; };
        // Whether size(i) would write what the picture's style already holds, read from the style and never the layout:
        // a slider made again at the level its parts are drawn at (every redraw, the new parts sized while hidden) has
        // nothing to change, and until 5.7.3 read every far part's size to find that out, one a frame for the frames after
        // a toggle.
        function sameLength(value, want) { return value !== '' && Math.abs((parseFloat(value) || 0) - want) < 0.01; }
        size.unchanged = function(i) {
            var svg = f.pics[i], st = svg.style;
            if (!svg.isConnected) return true;
            if (st.maxWidth !== (zoomedIn ? 'none' : '100%') || !sameLength(st.width, f.naturals[i] * scale)) return false;
            if (!lined) return st.marginLeft === '' && st.marginRight === '';
            return sameLength(st.marginLeft, left + shiftOf(svg) * scale) && sameLength(st.marginRight, 0);
        };
        return size;
    }

    // The width sizeWhole gives a picture drawn whole: neither way zoomed, it is drawn at its natural width, the box's
    // content width at most.
    function wholeWidth(f, percent, scale, zoomedIn, zoomedOut) {
        if (zoomedIn) return percent >= 100 ? f.naturals[0] : f.naturals[0] * scale;
        if (zoomedOut) return f.naturals[0] * scale;
        return Math.min(f.naturals[0], f.cw);
    }

    // Whether a level changes the width (and so the height) a picture is drawn at. A slider made again at the level the
    // picture is already drawn at, as every redraw makes it, changes neither, and the page's scroll and the browser's own
    // scroll anchoring are then left alone: held off for every diagram a toggle drew again, they were off as a reader
    // jumped down the report, and the jump landed short by the height the browser laid out late (content-visibility).
    // A part whose style already holds the level (unchanged) is not laid out to find that out.
    function resizes(pics, widthOf, unchanged) {
        return pics.some(function(svg, i) {
            return svg.isConnected && !(unchanged && unchanged(i)) && Math.abs(svg.getBoundingClientRect().width - widthOf(i)) > 0.5;
        });
    }

    // The parts within a window's height of the screen are resized at once and the rest one a frame after: every part
    // at once cost 191 ms on a diagram of 15 parts (M6). A part resized later keeps the reader's place, and a new level
    // given meanwhile takes over the parts still to do.
    // Which parts are near is read before anything is written: read between the writes, every part laid out again.
    function nearParts(pics) {
        var h = window.innerHeight;
        return pics.map(function(svg) {
            var r = svg.parentElement.getBoundingClientRect();
            return r.bottom >= -h && r.top <= 2 * h;
        });
    }

    function sizeParts(box, f, size, near) {
        var rest = [];
        near = near || nearParts(f.pics);
        for (var i = 0; i < f.pics.length; i++) {
            if (near[i]) size(i); else if (!size.unchanged(i)) rest.push(i);
        }
        box._zoomPending = rest.length ? { size: size, rest: rest, pics: f.pics } : null;
        function step() {
            box._zoomFrame = 0;
            var pending = box._zoomPending;
            if (!pending || !box.isConnected) { box._zoomPending = null; return; }
            var k = pending.rest.shift();
            // a part sized meanwhile (gone from the page, or drawn again and sized while hidden) is passed over unread
            while (k !== undefined && pending.size.unchanged(k)) k = pending.rest.shift();
            if (k === undefined) { box._zoomPending = null; return; }
            var changes = resizes([pending.pics[k]], function() { return pending.size.width(k); });
            if (changes) holdScrollAnchoring();
            var place = changes ? placeBefore(box, pending.pics) : null;
            pending.size(k);
            // what the window could not scroll, a fraction of a pixel, at most a pixel (at the page's end it could not
            // scroll at all)
            pending.carried = Math.max(-1, Math.min(1, keepPlace(box, place, false, pending.carried)));
            if (pending.rest.length) box._zoomFrame = requestAnimationFrame(step);
            else box._zoomPending = null;
        }
        // the far parts start the frame after the near ones are painted: their first one, laid out in the frame the near
        // ones were painted in, held that paint back by its own layout
        if (rest.length && !box._zoomFrame) box._zoomFrame = requestAnimationFrame(function() { box._zoomFrame = requestAnimationFrame(step); });
    }

    // Apply a zoom level (the slider's percent) to a box, keeping the reader's place: the point under the pointer when
    // one is given (the wheel, and the keys and the slider, from the last pointer position over the box), else what is
    // in the middle of the window (Z6). The point was kept across only until 5.7.0, and moved down the page as the
    // picture's height changed with its width (B2).
    function applyZoomLevel(container, percent, cursorClientX, cursorClientY) {
        var f = zoomFacts(container);
        if (!f) return;
        percent = Math.max(f.min, Math.min(100, Math.round(percent)));
        var zoomedIn = f.wide && percent > f.fitPct;
        var zoomedOut = !f.wide && percent < 100;
        var scale = f.wide && !zoomedIn ? f.fit : percent / 100;
        var size = f.parts ? partSizer(f, scale, zoomedIn, zoomedOut) : null;
        var changes = resizes(f.pics, size ? size.width : function() { return wholeWidth(f, percent, scale, zoomedIn, zoomedOut); }, size ? size.unchanged : null);
        if (changes) holdScrollAnchoring();
        var place = changes ? placeBefore(container, f.pics, cursorClientX, cursorClientY) : null;
        var near = f.parts ? nearParts(f.pics) : null;
        setZoomMarks(container, zoomedIn, zoomedOut);
        if (f.parts) sizeParts(container, f, size, near);
        else sizeWhole(f.pics[0], f.naturals[0], percent, scale, zoomedIn, zoomedOut);
        // back from a zoom in, nothing scrolls across: the parts far from the screen are made narrow a frame at a time, and
        // until the last of them was, the box kept the scroll a pan had given it, the parts on screen drawn off its left (5.7.3)
        if (!zoomedIn && container.scrollLeft) container.scrollLeft = 0;
        keepPlace(container, place, zoomedIn);
        // The level is kept on the box, either way: a diagram drawn again gets a new picture and a new slider, and the
        // box gives the level back to them (5.2.3)
        container._zoomPercent = zoomedIn || zoomedOut ? percent : undefined;
        // the place the box is left at with this level: a zoom that moves no scroll fires no scroll event (5.2.5)
        container._lastScrollLeft = zoomedIn ? container.scrollLeft : undefined;
        var slider = ownSlider(container);
        if (slider) slider.value = String(percent);
    }

    // ── Zoom Controls (slider) ──

    // One slider for every drawn diagram, for a diagram in parts on its container (Z1, Z8). Asked again (a part drawn,
    // a redraw), the slider takes the new range: a later part wider than those above it lowers the diagram's level once,
    // keeping the reader's place.
    function addZoomButton(el) {
        var container = el.classList && el.classList.contains('puml-fragment') ? findDiagramContainer(el.parentElement) || el : el;
        Array.prototype.forEach.call(container.querySelectorAll(':scope > .puml-fragment > .diagram-zoom-controls'), function(c) { c.remove(); });
        var f = zoomFacts(container);
        var controls = container.querySelector(':scope > .diagram-zoom-controls');
        if (!f) {
            // No picture, no zoom: a box that has one but measures nothing (not laid out) keeps its zoom and is asked
            // again when it shows (5.5.3)
            if (!picturesOf(container).length && !container.querySelector(':scope > .puml-fragment') && container.clientWidth > 0) {
                if (controls) controls.remove();
                dropZoom(container);
            }
            return;
        }
        container.style.position = 'relative';
        var slider = controls && controls.querySelector('.diagram-zoom-slider');
        if (!slider) {
            controls = document.createElement('div');
            controls.className = 'diagram-zoom-controls';
            slider = document.createElement('input');
            slider.type = 'range';
            slider.className = 'diagram-zoom-slider';
            slider.max = '100';
            slider.title = 'Zoom level';
            slider.addEventListener('input', function(e) {
                e.stopPropagation();
                applyZoomLevel(container, parseInt(slider.value), container._lastCursorX, container._lastCursorY);
            });
            slider.addEventListener('click', function(e) { e.stopPropagation(); });
            controls.appendChild(slider);
            container.prepend(controls);
        }
        var level = levelFor(container, f);
        slider.min = String(f.min);
        slider.value = String(level);
        // a box across the middle of the window keeps what the reader sees there as its level changes
        var r = container.getBoundingClientRect(), y = window.innerHeight / 2;
        var reading = r.top < y && r.bottom > y;
        applyZoomLevel(container, level, reading ? r.left + r.width / 2 : undefined, reading ? y : undefined);
        // the place across a redraw kept, once the box is laid out (5.2.3)
        if (typeof container._zoomScrollLeft === 'number') {
            if (container.classList.contains('diagram-natural-size')) {
                container.scrollLeft = container._zoomScrollLeft;
                container._lastScrollLeft = container.scrollLeft;
            }
            container._zoomScrollLeft = undefined;
        }
        followSoon();
    }

    function dropZoom(box) {
        box.classList.remove('diagram-natural-size');
        box.classList.remove('diagram-zoomed-out');
        box.classList.remove('diagram-panning');
        box.style.overflowX = '';
        box._zoomPercent = undefined;
        box._zoomScrollLeft = undefined;
        box._lastScrollLeft = undefined;
        box._zoomPending = null;
    }

    // Re-apply zoom inline styles after SVG re-render (innerHTML replacement destroys them): the level the box was zoomed
    // to, in its own direction, and its place across the picture (5.2.3), once the box is laid out. A box that measures
    // nothing is drawn at its natural style until its zoom slider is made, which restores it again.
    function restoreZoomState(container) {
        if (!picturesOf(container).length) return;
        if (zoomFacts(container)) { addZoomButton(container); return; }
        var zoomedIn = container.classList.contains('diagram-natural-size');
        picturesOf(container).forEach(function(svg) { svg.style.maxWidth = zoomedIn ? 'none' : '100%'; });
        if (zoomedIn) {
            container.style.overflowX = 'auto';
            container.style.overflowY = '';
        }
    }

    // ── The slider follows the window down its box (B4, Z10) ──
    // The controls are sticky, but the box's horizontal scroll makes the box their scroll box, which never scrolls down:
    // in the middle of a tall diagram the slider stayed at the diagram's top, out of reach (until 5.7.0). The controls of
    // the box under the pointer and of the selected one are moved down the box with the window, below the report's bar.
    var hoveredBox = null, followFrame = 0;
    function placeControls(box) {
        var controls = box.querySelector(':scope > .diagram-zoom-controls');
        if (!controls) return;
        var r = box.getBoundingClientRect();
        var bar = parseFloat(window.getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h')) || 0;
        var top = parseFloat(window.getComputedStyle(controls).top) || 0;
        var down = Math.max(0, Math.min(bar + 8 - (r.top + top), r.height - 2 * top - 24));
        controls.style.transform = down > 0 ? 'translateY(' + Math.round(down) + 'px)' : '';
    }
    function followSoon() {
        if (followFrame) return;
        followFrame = requestAnimationFrame(function() {
            followFrame = 0;
            [hoveredBox, selectedDiagram].forEach(function(b) { if (b && b.isConnected) placeControls(b); });
        });
    }
    window.addEventListener('scroll', followSoon, { passive: true });

    // The window changes width (resized, a docked panel, the page zoomed): a box's width decides its range and its fit
    // level, so every slider is made again, near the screen at once and the rest as they come near (until 5.7.3 a slider
    // kept the range of the width it was made at).
    var resizeTimer = 0, lastWidth = window.innerWidth;
    var staleIO = typeof IntersectionObserver === 'function' ? new IntersectionObserver(function(entries) {
        entries.forEach(function(entry) {
            if (!entry.isIntersecting) return;
            staleIO.unobserve(entry.target);
            addZoomButton(entry.target);
        });
    }, { rootMargin: '200px' }) : null;
    window.addEventListener('resize', function() {
        if (window.innerWidth === lastWidth) return;
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function() {
            lastWidth = window.innerWidth;
            var h = window.innerHeight;
            document.querySelectorAll('[data-diagram-type]').forEach(function(box) {
                if (!ownSlider(box) || !box.clientWidth) return;
                delete box._kContentWidth;
                var r = box.getBoundingClientRect();
                if (r.bottom >= -h && r.top <= 2 * h) addZoomButton(box);
                else if (staleIO) staleIO.observe(box);
            });
        }, 150);
    });

    // ── Keyboard Zoom (Ctrl+Plus / Ctrl+Minus) ──

    document.addEventListener('keydown', function(e) {
        if (!e.ctrlKey && !e.metaKey) return;
        var isPlus = (e.key === '=' || e.key === '+' || e.key === 'NumpadAdd');
        var isMinus = (e.key === '-' || e.key === '_' || e.key === 'NumpadSubtract');
        if (!isPlus && !isMinus) return;
        // keys typed in a field are the field's: until 5.7.3, Ctrl and - typed in the search box zoomed the selected diagram
        var t = e.target;
        if (t && t.closest && t.closest('textarea, select, [contenteditable]:not([contenteditable="false"]), input:not([type="range"])')) return;

        if (!selectedDiagram) return;
        // a part drawn again away keeps no selection, and the keys go to the browser (5.2.5)
        if (!selectedDiagram.isConnected) { deselectDiagram(); return; }
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
        // a sideways wheel (a tilt wheel, a sideways swipe) has no up or down: until 5.7.3 it zoomed out
        if (!e.deltaY) return;
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
            // a drag inside a payload's scroll box selects its text (5.4.0)
            if (e.target.closest('.note-scroll')) return;
            dragging = true;
            dragContainer = c;
            startX = e.pageX;
            startY = e.clientY;
            downX = e.clientX;
            downY = e.clientY;
            moved = false;
            scrollL = c.scrollLeft;
            // the grabbing cursor and no selection, on the picture under the pointer (inline-svg-styles.css)
            c.classList.add('diagram-panning');
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
            dragContainer.classList.remove('diagram-panning');
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
    // A box's zoom across a redraw (5.2.2, the level and the place 5.2.3, the direction 5.7.0): `of` reads it while the
    // old picture is still on the page (a box taken off the page reads no scroll), `give` hands it to the box drawn in its
    // place, and `drop` takes it off a box that no longer zooms. A diagram in parts keeps its zoom on its container, as a
    // diagram drawn whole does (Z8), so a redraw from whole to parts or back keeps it as it is. A box that is not laid out
    // (not displayed, as in a hidden panel or a closed scenario in Firefox) reads its scroll as 0, so `of` reads such a
    // box's place from where it was last left: a scroll it has not been given back yet, else the last scroll seen while
    // it was zoomed.
    document.addEventListener('scroll', function(e) {
        var box = e.target;
        if (box.classList && box.classList.contains('diagram-natural-size') && box.clientWidth > 0) box._lastScrollLeft = box.scrollLeft;
    }, true);

    window._zoomState = {
        of: function(box) {
            // Zoomed neither way, a box has no level or place to keep, and nothing of its layout is read: every diagram a
            // control draws again is asked, and a box read far from the screen lays its scenario out (5.2.5).
            var out = box.classList.contains('diagram-zoomed-out');
            if (!box.classList.contains('diagram-natural-size'))
                return out ? { zoomed: false, out: true, percent: box._zoomPercent, selected: selectedDiagram === box }
                    : { zoomed: false, out: false, selected: selectedDiagram === box };
            var place = box.clientWidth > 0 ? box.scrollLeft
                : typeof box._zoomScrollLeft === 'number' ? box._zoomScrollLeft : box._lastScrollLeft || 0;
            return { zoomed: true, out: false, percent: box._zoomPercent, scrollLeft: place, selected: selectedDiagram === box };
        },
        // the mark is given as it was taken, and applied by the slider in its own direction only (Z7)
        give: function(box, state) {
            if (state.zoomed) {
                box.classList.add('diagram-natural-size');
                box._zoomPercent = state.percent;
                box._zoomScrollLeft = state.scrollLeft;
            } else if (state.out) {
                box.classList.add('diagram-zoomed-out');
                box._zoomPercent = state.percent;
            }
            if (state.selected) selectDiagram(box);
        },
        drop: dropZoom
    };

    // The diagram's NATURAL width in CSS pixels — what it would draw at with the container's
    // `max-width: 100%` lifted. The note full-width control needs exactly this number to work out
    // how much slack a diagram has, and measuring it in two places would be two things to keep in
    // step, so the zoom code's measurement is the one both use.
    window._getDiagramNaturalWidth = getNaturalWidth;

    // Lazily add zoom buttons when diagram containers scroll into view.
    // A per-container MutationObserver waits for the SVG to render before
    // making its zoom slider (every drawn diagram has one since 5.7.0).
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