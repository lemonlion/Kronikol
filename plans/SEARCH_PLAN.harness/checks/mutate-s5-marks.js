// SEARCH_PLAN 6.4, S5 marks: each mutation is applied to a shipped script, the E2E facts it should break are run (bundled
// Chromium), and the script is put back byte for byte (its SHA-256 checked). A mutation counts only when its facts pass
// unmutated first.
//   node mutate-s5-marks.js <worktree> [id,id,...]
// Writes the results as JSON on stdout; mutations-s5-marks.md records them.
const fs = require('fs'), path = require('path'), crypto = require('crypto'), { execSync } = require('child_process');

const root = path.resolve(process.argv[2] || '.');
const MARKS = path.join(root, 'src/Kronikol/Reports/report-search-marks-script.js');
const MENU = path.join(root, 'src/Kronikol/Reports/context-menu-script.js');
const EXPORT = path.join(root, 'src/Kronikol/Reports/report-export-function.js');
const PROJECT = 'tests/Kronikol.Tests.EndToEnd';
const fact = (name) => 'FullyQualifiedName~SearchHitTests.' + name;
const H16 = fact('H16_The_marks_come_back_after_a_redraw_and_go_on_Escape_a_click_outside_or_another_search');
const BOX = fact('A_match_in_a_long_note_s_scroll_box_is_marked_in_the_box_and_the_box_scrolled_to_it');
const WHOLE = fact('A_diagram_given_whole_is_outlined_and_the_words_in_its_picture_marked');
const ROW_JOIN = "row.join = row.apart ? LINE : end.slice(-2) === ZW + ZW ? ' ' : end.slice(-1) === ZW ? '' : rowJoin;";

const MUTATIONS = [
    { id: 'pair', what: 'a pair of zero-width spaces ending a row is read as one: nothing between the rows', file: MARKS,
      from: ROW_JOIN,
      to: "row.join = row.apart ? LINE : end.slice(-1) === ZW ? '' : rowJoin;",
      facts: fact('A_pair_of_zero_width_spaces_ending_a_row_is_the_space_the_wrap_took') },
    { id: 'single', what: "one zero-width space ending a row is a gap, a term's space or nothing", file: MARKS,
      from: ROW_JOIN,
      to: "row.join = row.apart ? LINE : end.slice(-2) === ZW + ZW ? ' ' : end.slice(-1) === ZW ? GAP : rowJoin;",
      facts: fact('A_match_runs_on_to_the_next_row_of_a_wrapped_note') },
    { id: 'note-join', what: "a note's rows join as a label's do: the engine's wrap a space or nothing", file: MARKS,
      from: "kind: 'svg', groups: [{ svg: svg, texts: grp.texts.filter(shownText), rowJoin: ' ' }], outline: grp.paths,",
      to: "kind: 'svg', groups: [{ svg: svg, texts: grp.texts.filter(shownText), rowJoin: GAP }], outline: grp.paths,",
      facts: fact('A_match_runs_on_to_the_next_row_of_a_wrapped_note') },
    { id: 'gap', what: 'texts on one y with a gap between them are one row: two participants side by side read as one word', file: MARKS,
      from: "if (row && x - end > (num(t, 'font-size') || 12) / 2) { row.apart = true; row = null; }",
      to: '',
      facts: WHOLE },
    { id: 'number', what: "an arrow's number is read as part of its label", file: MARKS,
      from: "number = tagOf(e) === 'g' ? numberOf(e, inner) : null;",
      to: 'number = null;',
      facts: fact('A_match_runs_on_across_the_rows_of_an_arrow_s_label_as_the_search_reads_them') + '|'
           + fact('A_marked_label_comes_back_after_a_redraw_that_numbers_its_arrow_anew') },
    { id: 'refind', what: "an arrow's label is found again by its number as well as its words", file: MARKS,
      from: 'var keyOf = function (c) { return textKey(wordsOf(c)); }',
      to: 'var keyOf = function (c) { return textKey(textsOf(c)); }',
      facts: fact('A_marked_label_comes_back_after_a_redraw_that_numbers_its_arrow_anew') },
    { id: 'whole', what: "the pictures an HTML place holds are not marked as pictures (a diagram given whole)", file: MARKS,
      from: "(tagOf(html) === 'svg' ? [html] : svgsOf(html)).forEach(function (svg) { groups = groups.concat(pictureGroups(svg)); });",
      to: '',
      facts: WHOLE },
    { id: 'svg-text', what: "an HTML place's text takes in the SVG text of the pictures it holds (::highlight on SVG text)", file: MARKS,
      from: 'return el.namespaceURI !== SVGNS && !el.closest(',
      to: 'return !el.closest(',
      facts: WHOLE },
    { id: 'reading', what: "a whole picture's occurrences in the order of its groups, not in reading order", file: MARKS,
      from: 'if (p.groups.length > 1) svgOcc.sort(readingOrder);',
      to: '',
      facts: WHOLE },
    { id: 'frame', what: "a scroll box's frame is taken for the picture's element it is, not for the box", file: MARKS,
      from: 'if (inFrame) place = inFrame;',
      to: '',
      facts: fact('A_scroll_box_given_by_its_frame_is_outlined_inside_it_and_found_again') },
    { id: 'box-refind', what: 'a scroll box is not found again in a picture drawn anew', file: MARKS,
      from: "refind: note === null || !host ? null : function () { return host.querySelector('.note-scroll-frame[data-note=\"' + note + '\"]'); }",
      to: 'refind: null',
      facts: fact('A_scroll_box_given_by_its_frame_is_outlined_inside_it_and_found_again') },
    { id: 'picture-added', what: 'a picture drawn in an HTML place marked before it was is not marked', file: MARKS,
      from: 'return boxNode(n) || (!!d.html && pictureNode(n));',
      to: 'return boxNode(n);',
      facts: fact('A_diagram_marked_before_its_picture_is_drawn_is_marked_once_it_is') },
    { id: 'order', what: 'a mark is inserted after the text it marks, over its words', file: MARKS,
      from: 'm.anchor.parentNode.insertBefore(ground, m.anchor);',
      to: 'm.anchor.parentNode.insertBefore(ground, m.anchor.nextSibling);',
      facts: fact('A_mark_leaves_every_text_of_the_diagram_whole') },
    { id: 'current', what: 'the occurrence named is underlined as the others are', file: MARKS,
      from: 'var named = m.n === current, transform',
      to: 'var named = false, transform',
      facts: fact('The_named_occurrence_is_outlined_and_the_others_underlined') },
    { id: 'redraw', what: 'a redraw does not draw the marks again', file: MARKS,
      from: 'if (rec === active && !rec.retry && stale(rec, records)) redraw(rec);',
      to: 'void records;',
      facts: H16 + '|' + BOX },
    { id: 'arrive', what: 'a redraw pulses the place again, as an arrival', file: MARKS,
      from: '        draw(rec, false);\n    }\n    function watch(rec) {',
      to: '        draw(rec, true);\n    }\n    function watch(rec) {',
      facts: H16 },
    { id: 'escape', what: 'Escape takes the marks away while a layer is open', file: MARKS,
      from: "if (active && e.key === 'Escape' && !e.isComposing && !layerOpen()) clear();",
      to: "if (active && e.key === 'Escape' && !e.isComposing) clear();",
      facts: H16 },
    { id: 'inside', what: 'a click inside the scenario takes the marks away', file: MARKS,
      from: 'if (!rec || !t || t === doc.documentElement || layerOpen() || (rec.scope && rec.scope.contains(t))) return;',
      to: 'if (!rec || !t || t === doc.documentElement || layerOpen()) return;',
      facts: H16 },
    { id: 'search', what: 'a search run with another query leaves the marks (the search function not wrapped)', file: MARKS,
      from: "if (typeof run !== 'function' || run.kronMarks) return;",
      to: 'return;',
      facts: H16 },
    { id: 'close', what: 'closing the scenario leaves the marks', file: MARKS,
      from: 'if (d.contains(at || rec.scope)) clear();',
      to: 'void at;',
      facts: fact('The_marks_go_when_the_scenario_is_closed') },
    { id: 'back', what: 'Back that leaves the scenario leaves the marks', file: MARKS,
      from: 'if (active === rec && !layerOpen() && !rendered(rec.scope)) clear();',
      to: 'void rec;',
      facts: fact('The_marks_go_when_Back_leaves_the_scenario') },
    { id: 'box-scroll', what: 'the scroll box is not brought to the words', file: MARKS,
      from: 'if (inBox) scrollBoxTo(inBox, target);',
      to: 'void inBox;',
      facts: BOX },
    { id: 'under-box', what: 'the rows a scroll box lies over, hidden by the sheet, are read', file: MARKS,
      from: "return cs.visibility !== 'hidden' && cs.display !== 'none';",
      to: "return cs.display !== 'none';",
      facts: BOX },
    { id: 'sized', what: 'a picture not laid out is marked at once, on nothing', file: MARKS,
      from: 'var sized = svgs.filter(function (svg) { return svg.getBoundingClientRect().width > 0; });',
      to: 'var sized = svgs;',
      facts: fact('A_place_not_laid_out_is_marked_once_it_is') },
    { id: 'wait', what: 'a picture not laid out is not waited for', file: MARKS,
      from: 'svgs.forEach(function (svg) { if (sized.indexOf(svg) < 0) waitForSize(rec, svg); });',
      to: '',
      facts: fact('A_place_not_laid_out_is_marked_once_it_is') },
    { id: 'copy', what: "a copy keeps the marks (lookClone's strip leaves them)", file: MENU,
      from: "clone.querySelectorAll('.note-scroll-frame, .kron-mark-svg')",
      to: "clone.querySelectorAll('.note-scroll-frame')",
      facts: fact('H32_Copies_of_a_marked_diagram_carry_no_marks') },
    { id: 'export', what: 'an export keeps the marks', file: EXPORT,
      from: "    copy.querySelectorAll('.kron-mark-svg').forEach(function (el) { el.parentNode.removeChild(el); });\n    copy.querySelectorAll('.kron-mark-place').forEach(function (el) { el.classList.remove('kron-mark-place', 'kron-mark-arrive'); });\n",
      to: '',
      facts: fact('An_export_taken_while_marks_are_on_carries_none') },
];

const sha = b => crypto.createHash('sha256').update(b).digest('hex');
function run(filter) {
    execSync(`dotnet build ${PROJECT} -nologo -v q`, { cwd: root, stdio: 'pipe' });
    let out;
    try { out = execSync(`dotnet test ${PROJECT} --no-build -nologo -v q --filter "${filter}"`, { cwd: root, stdio: 'pipe' }).toString(); }
    catch (e) { out = (e.stdout || '').toString() + (e.stderr || '').toString(); }
    const m = /Failed:\s+(\d+),\s+Passed:\s+(\d+)/.exec(out);
    return m ? { failed: +m[1], passed: +m[2] } : { failed: -1, passed: -1, output: out.slice(-800) };
}

const only = process.argv[3] ? process.argv[3].split(',') : null;
const results = [];
for (const mu of MUTATIONS) {
    if (only && !only.includes(mu.id)) continue;
    const bytes = fs.readFileSync(mu.file);
    const text = bytes.toString('utf8');
    const at = text.indexOf(mu.from);
    if (at < 0 || text.indexOf(mu.from, at + 1) >= 0) { results.push({ id: mu.id, error: at < 0 ? 'from not found' : 'from not unique' }); continue; }
    const before = run(mu.facts);
    let after;
    try {
        fs.writeFileSync(mu.file, text.slice(0, at) + mu.to + text.slice(at + mu.from.length));
        after = run(mu.facts);
    } finally {
        fs.writeFileSync(mu.file, bytes);
    }
    const restored = sha(fs.readFileSync(mu.file)) === sha(bytes);
    results.push({ id: mu.id, what: mu.what, file: path.basename(mu.file), facts: mu.facts, unmutated: before, mutated: after, restored });
    process.stderr.write(`${mu.id}: unmutated ${JSON.stringify(before)} mutated ${JSON.stringify(after)} restored ${restored}\n`);
}
execSync(`dotnet build ${PROJECT} -nologo -v q`, { cwd: root, stdio: 'pipe' });
console.log(JSON.stringify(results, null, 2));
