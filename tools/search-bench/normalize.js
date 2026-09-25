// SEARCH_INDEX_PLAN §4.1 normalization — REFERENCE IMPLEMENTATION.
// Mirrored in C# (generation) and in the shipped report JS (verify), pinned by shared vectors;
// validate-normalize.js asserts it against real PlantUmlCreator output.
'use strict';

// 1b. The generator marks every break it writes into a note body so a reader copying the note back
// out gets the payload whole (NOTE_COPY_FIDELITY_PLAN). Search wants the same thing for the same
// reason: a token cut in half is two terms and matches neither. This REPLACED a flush-left
// heuristic, which rejoined any note line that did not start with whitespace — right for indented
// JSON, wrong for a plain-text or SQL payload whose real lines start at column 0.
//
// It runs before the fold and before the creole-escape strip on purpose: pass 3 removes the `~` in
// front of a payload's own "<U+200B>", and after that Kronikol's marker and the payload's text are
// the same bytes.
const JOIN_MARKER = '<U+200B>';
const JOIN_SPACE_MARKER = JOIN_MARKER + JOIN_MARKER;

function endsWithOwnMarker(line, marker) {
  if (line.length < marker.length || line.slice(-marker.length) !== marker) return false;
  return !(line.length > marker.length && line[line.length - marker.length - 1] === '~');
}

function rejoinMarkedBreaks(s) {
  if (s.indexOf(JOIN_MARKER) === -1) return s;
  const lines = s.split('\n');
  let out = '';
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    const last = i === lines.length - 1;
    if (!last && endsWithOwnMarker(l, JOIN_SPACE_MARKER)) out += l.slice(0, l.length - JOIN_SPACE_MARKER.length) + ' ';
    else if (!last && endsWithOwnMarker(l, JOIN_MARKER)) out += l.slice(0, l.length - JOIN_MARKER.length);
    else out += l + (last ? '' : '\n');
  }
  return out;
}

// 3. Creole escapes and code points (3.30.1). The generator writes captured text that PlantUML's
// preprocessor or creole would act on as a code point, <U+hhhh>, which the engine paints as the one
// character, so a search reads the character. One left-to-right pass: `~<U+0027>` is an escaped `<`
// followed by the payload's own text, and a decoded `~` escapes nothing. It runs before the fold, so a
// decoded `E` folds like any other. A zero-width space (<U+200B>, the generator's pair and reference
// breaks) paints nothing and is dropped; a code point that is no character is left as written. The
// lowercase `<u+…>` is not decoded, because the engine paints it as written.
function decodeCreoleEscapes(s) {
  return s.replace(/~([/*_\-"\[<#=])|<U\+([0-9A-Fa-f]{4,6})>/g, (m, escaped, hex) => {
    if (escaped !== undefined) return escaped;
    const cp = parseInt(hex, 16);
    if (cp === 0x200b) return '';
    if ((cp >= 0xd800 && cp <= 0xdfff) || cp > 0x10ffff) return m;
    return String.fromCodePoint(cp);
  });
}

function normalizeForSearch(s) {
  s = s.replace(/\r\n/g, '\n');                                  // 1. canonicalize CRLF
  s = rejoinMarkedBreaks(s);                                     // 1b. undo the generator's own note-body breaks
  s = decodeCreoleEscapes(s);                                    // 3. creole escapes and code points (before the fold since 3.30.1)
  s = s.replace(/[A-Z]/g, c => c.toLowerCase());                 // 2. ASCII-only fold
  s = s.replace(/<\/?(?:color|font|i|b|size|back)[^>]*>/g, '');  // 4. markup tags
  s = s.replace(/\\n[ \t]*/g, '');                               // 5a. arrow-label literal \n escape + indent
  return (s + '\n').replace(/[ \t]+/g, ' ');                     // 6. collapse spaces
}

module.exports = { normalizeForSearch };
