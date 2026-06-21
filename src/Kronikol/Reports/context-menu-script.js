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