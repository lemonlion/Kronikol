'use strict';
// The plan's mutation run (section 6.4): each mutation is applied to a page script of the worktree, the E2E project is
// built, the facts named for it are run, and at least one of them must fail; the script is put back after each, whatever
// happens. usage: node mutate.js <worktree> [--check] [name ...]
//   --check   only checks that every mutation's text is found once in its file, and changes nothing
//   name ...  runs only the mutations named
// Results go to stdout, one line a mutation: KILLED (a fact failed), SURVIVED (every fact passed) or ERROR.
const fs = require('fs');
const path = require('path');
const { execFileSync, spawnSync } = require('child_process');

const [worktree, ...rest] = process.argv.slice(2);
const check = rest.includes('--check');
const only = rest.filter((a) => a !== '--check');
const R = (f) => path.join(worktree, 'src', 'Kronikol', 'Reports', f);
const CM = R('context-menu-script.js'), CN = R('collapsible-notes-script.js'), RS = R('plantuml-browser-render-script.js');
const POP = R('internal-flow-popup-script.js');
// applyZoomLevel's lines after the marks are set (5.7.3 put the scroll across back to 0 between them)
const APPLY_TAIL = "        if (f.parts) sizeParts(container, f, size, near);\n        else sizeWhole(f.pics[0], f.naturals[0], percent, scale, zoomedIn, zoomedOut);\n"
  + "        // back from a zoom in, nothing scrolls across: the parts far from the screen are made narrow a frame at a time, and\n"
  + "        // until the last of them was, the box kept the scroll a pan had given it, the parts on screen drawn off its left (5.7.3)\n"
  + "        if (!zoomedIn && container.scrollLeft) container.scrollLeft = 0;\n        keepPlace(container, place, zoomedIn);\n";

// [name, file, the text as written, the text mutated, the facts (a dotnet test filter)]
const F = (...names) => names.map((n) => 'FullyQualifiedName~' + n).join('|');
const MUTATIONS = [
  ['no slider for a fitting box', CM,
    "var wide = fitPct < 100;", "var wide = fitPct < 100; if (!wide) return null;",
    F('FittingDiagramZoomTests.A_diagram_that_fits_has_a_slider_at_100')],
  ['the floor ignored', CM,
    "min: wide ? fitPct : ZOOM_OUT_FLOOR", "min: wide ? fitPct : 100",
    F('FittingDiagramZoomTests.A_diagram_that_fits_has_a_slider_at_100')],
  ['a fitting box starting at the floor', CM,
    "start: wide ? fitPct : 100 };", "start: wide ? fitPct : ZOOM_OUT_FLOOR };",
    F('FittingDiagramZoomTests.A_diagram_that_fits_has_a_slider_at_100')],
  ['the wheel not prevented at 100', CM,
    "        e.preventDefault();\n        var current = parseInt(slider.value);\n        var range = 100 - parseInt(slider.min);\n        var step = Math.max(1, Math.round(range * 0.05));\n        var delta",
    "        if (parseInt(slider.value) < 100) e.preventDefault();\n        var current = parseInt(slider.value);\n        var range = 100 - parseInt(slider.min);\n        var step = Math.max(1, Math.round(range * 0.05));\n        var delta",
    F('FittingDiagramZoomTests.Ctrl_wheel_up_over_a_diagram_that_fits_at_100')],
  ['a zoomed-out level given to a wide picture', CM,
    "if (kept && !f.wide && box.classList.contains('diagram-zoomed-out'))", "if (kept && box.classList.contains('diagram-zoomed-out'))",
    F('FittingDiagramZoomTests.A_zoomed_out_diagram_drawn_again_wider')],
  ['a zoomed-in level given to a fitting picture', CM,
    "if (kept && f.wide && box.classList.contains('diagram-natural-size'))", "if (kept && box.classList.contains('diagram-natural-size'))",
    F('ZoomKeptOnRedrawTests')],
  ['a redraw dropping out', CM,
    "        if (kept && !f.wide && box.classList.contains('diagram-zoomed-out')) return Math.max(f.min, Math.min(100, kept));\n", "",
    F('FittingDiagramZoomTests.A_zoomed_out_level_is_kept')],
  ['a zoomed-out box given the grab cursor and pan', CM,
    "box.classList.toggle('diagram-natural-size', zoomedIn);", "box.classList.toggle('diagram-natural-size', zoomedIn || zoomedOut);",
    F('FittingDiagramZoomTests.A_diagram_zoomed_out_has_no_grab_cursor')],
  ['the window scroll of Z6 removed', CM,
    "        window.scrollBy(0, dy);\n", "",
    F('GradualZoomTests.A_ctrl_wheel_step_keeps_the_point_under_the_pointer_in_place_down_the_page', 'SplitDiagramZoomTests.Ctrl_wheel_over_the_second_part')],
  ['a part\'s scroll set before overflowX', CM,
    "        setZoomMarks(container, zoomedIn, zoomedOut);\n" + APPLY_TAIL, APPLY_TAIL + "        setZoomMarks(container, zoomedIn, zoomedOut);\n",
    F('SplitDiagramZoomTests.Ctrl_wheel_over_the_second_part', 'GradualZoomTests', 'PartsZoomDrawingTests.A_notch_far_down_a_diagram_in_parts_keeps_the_point')],
  ['the scroll held off for a slider made again at its level', CM,
    "        if (changes) holdScrollAnchoring();\n        var place = changes ? placeBefore(container, f.pics, cursorClientX, cursorClientY) : null;",
    "        holdScrollAnchoring();\n        var place = changes ? placeBefore(container, f.pics, cursorClientX, cursorClientY) : null;",
    F('FittingDiagramZoomTests.A_slider_made_again_at_the_level', 'SplitDiagramZoomTests.A_slider_made_again_at_the_level')],
  ['a far part laid out to find it keeps its size', CM,
    "        size.unchanged = function(i) {", "        size.unchanged = function(i) { return false;",
    F('PartsZoomDrawingTests.A_slider_made_again_on_a_long_diagram')],
  ['the content width back to clientWidth', CM,
    "        return w - (parseFloat(cs.paddingLeft) || 0) - (parseFloat(cs.paddingRight) || 0);", "        return w;",
    F('GradualZoomTests.A_picture_wider_than_its_box_s_content', 'GradualZoomTests.The_slider_s_lowest_level_is_the_level')],
  ['the controls left where sticky puts them', CM,
    "    function placeControls(box) {", "    function placeControls(box) { return;",
    F('GradualZoomTests.The_slider_of_a_tall_diagram_stays_in_the_window', 'SplitDiagramZoomTests.Hovering_the_second_part')],
  ['a diagram\'s level taken from its first part', CM,
    "width = Math.max(width, naturals[i] + shiftOf(pics[i]));", "if (i === 0) width = naturals[i] + shiftOf(pics[i]);",
    F('SplitDiagramZoomTests.A_diagram_in_parts_starts_at_its_fit_level', 'SplitDiagramZoomTests.A_diagram_in_parts_not_lined_up')],
  ['only the part under the pointer resized', CM,
    "        var near = f.parts ? nearParts(f.pics) : null;", "        var near = f.parts ? f.pics.map(function(svg) { return !!place && place.svg === svg; }) : null;",
    F('PartsZoomDrawingTests.A_notch_resizes_every_part_on_screen_in_its_own_task')],
  ['the far parts never resized', CM,
    "            if (near[i]) size(i); else if (!size.unchanged(i)) rest.push(i);", "            if (near[i]) size(i);",
    F('PartsZoomDrawingTests.A_notch_on_a_diagram_of_fifteen_parts')],
  ['a part above the window resized without the window scroll', CM,
    "            pending.carried = Math.max(-1, Math.min(1, keepPlace(box, place, false, pending.carried)));", "",
    F('PartsZoomDrawingTests.A_notch_far_down_a_diagram_in_parts_keeps_the_point')],
  ['the window\'s whole pixels not carried from part to part', CM,
    "keepPlace(box, place, false, pending.carried)", "keepPlace(box, place, false, 0)",
    F('PartsZoomDrawingTests.A_notch_far_down_a_diagram_in_parts_keeps_the_point')],
  ['a zoomed-in part as wide as the box', R('inline-svg-styles.css'),
    ".diagram-natural-size > .puml-fragment {\n    width: max-content;", ".diagram-natural-size > .puml-fragment {\n    width: auto;",
    F('PartsZoomDrawingTests.A_notch_far_down_a_diagram_in_parts_keeps_the_point')],
  ['the ring put on the part clicked', CM,
    "    function zoomBoxOf(el) {\n        return findDiagramContainer(el);\n    }", "    function zoomBoxOf(el) {\n        return (el && el.closest && el.closest('.puml-fragment')) || findDiagramContainer(el);\n    }",
    F('DiagramInPartsAsOneTests.A_click_on_any_part_selects_the_whole_diagram')],
  ['a picture copy of the part under the click', CM,
    "            svgOf: function() { return wholeSvg(container, encoded).svg; },", "            svgOf: function() { return svg; },",
    F('DiagramInPartsAsOneTests.Copy_as_SVG_over_a_part')],
  ['a part\'s ids left as they are', CM,
    "            prefixIds(clone, 'p' + (i + 1) + '-');", "",
    F('DiagramInPartsAsOneTests.Copy_as_SVG_over_a_part')],
  ['a part\'s instruction left in the copy', CM,
    "clone = withoutSourceComments(look.svg);", "clone = look.svg;",
    F('DiagramInPartsAsOneTests.Copy_as_SVG_over_a_part')],
  ['the footbox kept on a middle part', RS,
    "                if (i === fragments.length - 1) return src;\n                return /^@startuml", "                if (i > 0) return src;\n                return /^@startuml",
    F('DiagramInPartsAsOneTests.Every_part_but_the_last_is_drawn_without_a_footbox')],
  ['the footbox taken off the last part', RS,
    "                if (i === fragments.length - 1) return src;\n                return /^@startuml", "                return /^@startuml",
    F('DiagramInPartsAsOneTests.Every_part_but_the_last_is_drawn_without_a_footbox')],
  ['hide footbox or the row written into a part\'s data-plantuml', RS,
    "                el._lineUp = lineUp;\n                el._lineUpPending = lineUp ? divs.length : 0;", "                el._lineUp = lineUp;\n                divs.forEach(function(d, k) { d.setAttribute('data-plantuml', engine[k]); });\n                el._lineUpPending = lineUp ? divs.length : 0;",
    F('DiagramInPartsAsOneTests.Every_part_but_the_last_is_drawn_without_a_footbox', 'LinedUpPartsTests.No_part_holds_the_row')],
  ['the row in a copied or opened source', CM,
    "    function getSource(container) {", "    function getSource(container) { if (container._lineUp) return container._lineUp.engine.join('\\n');",
    F('LinedUpPartsTests.The_source_items_give_the_diagram_as_written')],
  ['the row sized at zero', RS,
    "                var n = Math.ceil(targets[i] - c - 0.005);", "                var n = 0;",
    F('LinedUpPartsTests.The_row_draws_nothing_and_makes_each_part_only_its_height_taller')],
  ['autonumber stop left out', RS,
    "            var row = auto ? ['autonumber stop'].concat(rowLines, [auto]) : rowLines;", "            var row = auto ? rowLines.concat([auto]) : rowLines;",
    F('LinedUpPartsTests.Each_message_keeps_the_number', 'LinedUpPartsTests.The_row_is_exact')],
  ['the check after drawing skipped', RS,
    "            if (!lineUp || !lineUp.row || lineUp.pass > 1) return null;", "            return null;",
    F('LinedUpPartsTests.Parts_that_disagree_once_drawn')],
  ['parts not placed by their first lifeline', RS,
    "            parts.forEach(function(p, k) { if (firsts[k] === null) delete p._kShift; else p._kShift = most - firsts[k]; });", "",
    F('LinedUpPartsTests.Every_parts_lifelines_stand_at_the_same_places', 'LinedUpPartsTests.A_gap_wider_than_any_label')],
  ['the decorate restore ignoring diagram-zoomed-out', CN,
    "zoomBox.classList.contains('diagram-zoomed-out')", "false",
    F('FittingDiagramZoomTests.A_zoomed_out_level_is_kept', 'FittingDiagramZoomTests.A_zoomed_out_diagram_drawn_again_is_at_its_level')],
  ['the cursor written on the box', CM,
    "        box.style.overflowX = zoomedIn ? 'auto' : '';\n        box.style.overflowY = '';\n    }", "        box.style.overflowX = zoomedIn ? 'auto' : '';\n        box.style.overflowY = '';\n        box.style.cursor = zoomedIn ? 'grab' : '';\n    }",
    F('PartsZoomDrawingTests.A_notch_on_a_diagram_of_fifteen_parts', 'SplitDiagramZoomTests.Dragging_the_zoomed_diagram')],
  ['a box in the splitter ends the header', RS,
    " || /^box\\b/.test(trimmed) || /^end\\s+box\\b/.test(trimmed)) {", ") {",
    F('LinedUpPartsTests.A_gap_wider_than_any_label')],
  // 5.7.3, the audit of 2026-10-24: one a fix, each against the fact written red first for it (ZoomRobustnessTests)
  ['the new parts of a redraw shown as the engine drew them', CM,
    "    window._sizeHiddenParts = function(container, parts) {", "    window._sizeHiddenParts = function(container, parts) { return;",
    F('ZoomRobustnessTests.A_lined_up_diagram_in_parts_drawn_again_shows_its_parts_lined_up_in_the_first_frame')],
  ['the sliders left as they were when the window is resized', CM,
    "        if (window.innerWidth === lastWidth) return;", "        return;",
    F('ZoomRobustnessTests.A_diagram_in_parts_at_its_fit_level_fits_its_box_again', 'ZoomRobustnessTests.A_diagram_in_parts_far_from_the_screen')],
  ['a far diagram\'s slider not made again as it comes near after a resize', CM,
    "                else if (staleIO) staleIO.observe(box);\n", "",
    F('ZoomRobustnessTests.A_diagram_in_parts_far_from_the_screen')],
  ['back to the fit level with the box still scrolled across', CM,
    "        if (!zoomedIn && container.scrollLeft) container.scrollLeft = 0;\n", "",
    F('ZoomRobustnessTests.Back_to_the_fit_level_from_a_pan_across')],
  ['no place kept with the pointer over a seam', CM,
    "        return el ? nearest : null;\n", "        return null;\n",
    F('ZoomRobustnessTests.A_notch_with_the_pointer_between_two_parts')],
  ['the window scrolled for a box in a panel', CM,
    "        var scroller = scrollerOf(box);", "        var scroller = null;",
    F('ZoomRobustnessTests.Zooming_the_popups_activity_diagram')],
  ['a sideways wheel zooming', CM,
    "        if (!e.deltaY) return;\n", "",
    F('ZoomRobustnessTests.Ctrl_with_a_sideways_wheel')],
  ['keys typed in a field zooming', CM,
    "        if (t && t.closest && t.closest('textarea, select, [contenteditable]:not([contenteditable=\"false\"]), input:not([type=\"range\"])')) return;\n", "",
    F('ZoomRobustnessTests.Ctrl_and_minus_typed_in_the_search_box')],
  ['no picture items while a part will never draw', CM,
    "            if (!settled || !under || !getSvg(under) || !container.contains(under)) return { ready: false };", "            return { ready: false };",
    F('ZoomRobustnessTests.A_part_that_never_draws')],
  ['a picture a hair wider than its box drawn at its natural width', CM,
    "        if (!zoomedIn && !zoomedOut && f.width > f.cw) scale = Math.max(0, f.cw - 0.25) / f.width;\n", "",
    F('ZoomRobustnessTests.A_diagram_in_parts_a_hair_wider')],
  ['a part too large shown with the engine\'s text', CN,
    "            if (!/too large/i.test(String(message == null ? '' : message)) || !window._tooLargeMarkup) return false;", "            return false;",
    F('ZoomRobustnessTests.A_part_drawn_again_that_is_too_large')],
  ['the popup\'s activity diagram not asked for its slider when its tab is shown', POP,
    "if (main && view === 'main' && window._addZoomButton)", "if (false)",
    F('ZoomRobustnessTests.The_activity_diagram_of_a_popup_opened_on_its_flame_chart')],
  ['a keyword alias taken for a declaration in the header', RS,
    "var _declLineRx = /^", "var _declLineRx = /^\\s*(?:participant|actor|boundary|control|entity|database|collections|queue) /; var _declLineRx0 = /^",
    F('ZoomRobustnessTests.Parts_whose_messages_are_sent_by_a_participant_named_like_a_keyword')],
  ['a keyword alias taken for a declaration by the row', RS,
    "var _spacerDeclRx = /^", "var _spacerDeclRx = /^\\s*(?:actor|participant|database|collections|queue|entity|boundary|control)\\s+(\"[^\"]*\"|[^\\s\"]+)(?:\\s+as\\s+(\\S+))?/; var _spacerDeclRx0 = /^",
    F('ZoomRobustnessTests.Parts_whose_messages_are_sent_by_a_participant_named_like_a_keyword')],
  ['an outline the engine could not draw kept for the session', RS,
    "                    if (!o && _lineUps.get(key) === drawn) _lineUps.delete(key);\n", "",
    F('ZoomRobustnessTests.An_outline_the_engine_could_not_draw')],
  ['a calibration the engine could not draw kept for the session', RS,
    "                    if (c === null && _calibrations[key] === measured) delete _calibrations[key];\n", "",
    F('ZoomRobustnessTests.An_outline_the_engine_could_not_draw')],
  ['the parts after a forced cut numbered from 1', RS,
    "var prevSteps = baseNumber ? parseInt(baseNumber[1], 10) : 1;", "var prevSteps = 1;",
    F('ZoomRobustnessTests.A_diagram_numbered_from_past_one')],
  ['a participant declared partway left out of the later parts', RS,
    "            if (!fragments || fragments.length < 2) return fragments;\n            var carried = [];", "            return fragments;\n            var carried = [];",
    F('LinedUpPartsTests.A_gap_wider_than_any_label')],
  ['a picture the Java engine drew keeping its own height', R('inline-svg-styles.css'),
    ".plantuml-inline-svg > svg {\n    height: auto !important;", ".plantuml-inline-svg > svg {\n    height: auto;",
    F('ZoomRobustnessTests.A_picture_the_Java_engine_drew')],
  ['a box as wide as its picture measured narrowed by the zoom out', CM,
    "        if (box.classList.contains('diagram-zoomed-out')) cw = Math.max(cw, box._kContentWidth || 0);\n", "",
    F('ZoomRobustnessTests.Zooming_the_popups_activity_diagram')],
  ['a part given up left unsettled', RS,
    "' s';\n                    item.el.dataset.rendered = '1';\n", "' s';\n",
    F('ZoomRobustnessTests.A_part_the_engine_never_answers')],
];

function countOf(text, part) { let n = 0, at = 0; while ((at = text.indexOf(part, at)) >= 0) { n++; at += part.length; } return n; }

const project = path.join(worktree, 'tests', 'Kronikol.Tests.EndToEnd', 'Kronikol.Tests.EndToEnd.csproj');
let bad = 0;
for (const [name, file, from, to, facts] of MUTATIONS) {
  if (only.length && !only.includes(name)) continue;
  const original = fs.readFileSync(file, 'utf8');
  const n = countOf(original, from);
  if (n !== 1) { console.log(`ERROR ${name}: its text is found ${n} times in ${path.basename(file)}`); bad++; continue; }
  if (check) { console.log(`ok ${name}`); continue; }
  try {
    fs.writeFileSync(file, original.replace(from, to));
    const build = spawnSync('dotnet', ['build', project, '-c', 'Debug', '-nologo', '-v:q'], { encoding: 'utf8', maxBuffer: 64 << 20 });
    if (build.status !== 0) { console.log(`ERROR ${name}: the build failed`); bad++; continue; }
    const run = spawnSync('dotnet', ['test', project, '--no-build', '-nologo', '--filter', facts], { encoding: 'utf8', maxBuffer: 64 << 20 });
    const summary = (run.stdout.match(/(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+)/) || []);
    const failed = +summary[2] || 0, passed = +summary[3] || 0;
    if (!summary.length) { console.log(`ERROR ${name}: no test ran (${facts})`); bad++; }
    else if (failed > 0) console.log(`KILLED ${name}: ${failed} of ${failed + passed} failed`);
    else { console.log(`SURVIVED ${name}: all ${passed} passed`); bad++; }
  } finally {
    fs.writeFileSync(file, original);
  }
}
if (!check) execFileSync('dotnet', ['build', project, '-c', 'Debug', '-nologo', '-v:q'], { stdio: 'ignore' });
process.exit(bad ? 1 : 0);
