// SEARCH_INDEX_PLAN §4.1 normalization — REFERENCE IMPLEMENTATION.
// Mirrored in C# (generation) and in the shipped report JS (verify), pinned by shared vectors;
// validate-normalize.js asserts it against real PlantUmlCreator output.
'use strict';

function normalizeForSearch(s) {
  s = s.replace(/\r\n/g, '\n');                                  // 1. canonicalize CRLF
  s = s.replace(/[A-Z]/g, c => c.toLowerCase());                 // 2. ASCII-only fold
  s = s.replace(/~(?=[/*_\-"\[<#=])/g, '');                      // 3. creole escapes (same set the context-menu copy-text inverse strips)
  s = s.replace(/<\/?(?:color|font|i|b|size|back)[^>]*>/g, '');  // 4. markup tags
  s = s.replace(/\\n[ \t]*/g, '');                               // 5a. arrow-label literal \n escape + indent
  // 5b. note-body rejoin: newline followed by non-whitespace, scoped to note bodies.
  // Linear: parts are joined once at the end ('' join = rejoin, '\n' join = kept newline).
  // Openers cover every multi-line note form the formatter emits: `note left`, `note<<class>>
  // right` (event notes), `hnote across <<class>>` (assertion/render-error notes). A `:` on the
  // directive line marks PlantUML's single-line form (step delimiters, row markers) — no body
  // follows, so it must NOT enter note mode. The trailing \b keeps payload lines like
  // "note leftovers…" from opening a note.
  const noteOpener = /^[hr]?note(?:<<[^>]*>>)? (left|right|over|across)\b/;
  const lines = s.split('\n');
  const parts = [];
  let inNote = false;
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    const trimmed = l.trim();
    if (noteOpener.test(trimmed) && trimmed.indexOf(':') === -1) { inNote = true; if (parts.length) parts.push('\n'); parts.push(l); continue; }
    if (trimmed === 'end note') { inNote = false; if (parts.length) parts.push('\n'); parts.push(l); continue; }
    if (inNote && parts.length && l.length > 0 && !/\s/.test(l[0])) {
      parts.push(l);                                             // rejoin flush-left continuation
    } else {
      if (parts.length) parts.push('\n');
      parts.push(l);
    }
  }
  return (parts.join('') + '\n').replace(/[ \t]+/g, ' ');        // 6. collapse spaces
}

module.exports = { normalizeForSearch };
