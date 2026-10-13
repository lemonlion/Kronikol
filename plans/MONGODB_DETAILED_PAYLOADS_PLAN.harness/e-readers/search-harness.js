// Runs the report's own search functions (read from the source, unmodified) over a note corpus.
'use strict';
const fs = require('fs');
const vm = require('vm');
const R = require('path').resolve(__dirname, '../../..', 'src/Kronikol/Reports') + '/';
const code = ['advanced-search.js', 'report-search-function.js', 'report-search-index.js']
  .map(f => fs.readFileSync(R + f, 'utf8')).join('\n;\n');
const ctx = vm.createContext({ console });   // no window/document: report-search-index.js's main-thread IIFE returns early
vm.runInContext(code, ctx);
const S = ctx;

// The note bodies as PlantUmlCreator writes them (TryFormatAsJson output is identical for both stored forms,
// verified in fmt.cs), and, hypothetically, as one verbatim line (what a body that does NOT parse would show).
const reindented = 'note left\n{\n  "status": "Ready",\n  "n": 1,\n  "u": {\n    "$uuid": "0f8fad5b-d9cb-469f-a165-70867728950e"\n  }\n}\nend note';
const verbatimCompact = 'note left\n{"status":"Ready","n":1,"u":{"$uuid":"0f8fad5b-d9cb-469f-a165-70867728950e"}}\nend note';
const verbatimSpaced = 'note left\n{ "status" : "Ready", "n" : 1, "u" : { "$uuid" : "0f8fad5b-d9cb-469f-a165-70867728950e" } }\nend note';

function deepMatch(rawQuery, diagram) {
  const input = rawQuery.toLowerCase().trim();               // run_search_scenarios does this before _kronDeepQuery
  const searchText = 'my feature my scenario mongodb';       // data-search: names, steps, participants, URIs - no note bodies
  const corpus = [S.kronNormalizeForSearch(searchText), S.kronNormalizeForSearch(diagram)].join('\n');
  const deepInput = S.kronNormalizeQueryText(input);
  return {
    advanced: S.isAdvancedSearch(input),
    eligible: S.kronIsDeepEligible(input),
    tokens: S.isAdvancedSearch(input) ? JSON.stringify(S.advancedSearchTokenise(input)) : JSON.stringify(S.parseSearchTokensIncludingQuotes(S.splitLegacyTagExpression(input).textInput)),
    shallow: S.isAdvancedSearch(input) ? S.advancedSearchMatch(input, searchText, new Set(), 'Passed', new Set()) : null,
    deep: S.kronDeepMatchesItem(input, deepInput, corpus, new Set(), 'Passed', new Set()),
  };
}

const queries = ['"status":"Ready"', 'status Ready', '0f8fad5b-d9cb-469f-a165-70867728950e', '"status": "Ready"', '$uuid', '"$uuid"', 'status: ready'];
for (const [name, diagram] of [['re-indented note (what the generator writes for either stored form)', reindented], ['verbatim compact line', verbatimCompact], ['verbatim spaced line', verbatimSpaced]]) {
  console.log('\n## ' + name);
  console.log('normalized corpus piece:', JSON.stringify(S.kronNormalizeForSearch(diagram)));
  for (const q of queries) console.log(q.padEnd(40), JSON.stringify(deepMatch(q, diagram)));
}
