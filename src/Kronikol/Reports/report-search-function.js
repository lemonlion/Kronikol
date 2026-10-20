var searchTimeoutId;

function toggle_search_help() {
    var panel = document.querySelector('.search-help-panel');
    if (panel) panel.style.display = panel.style.display === 'none' ? '' : 'none';
    // the help opens on its own while More filters folds the rows around it (5.0.0, plans/OPTION_C_PLAN.md S7, D15)
    var box = document.querySelector('.filtering-box');
    if (box && panel) box.classList.toggle('help-open', panel.style.display !== 'none');
}

// A single search match is revealed fully: the scenario opens along with every section
// that can hold matched content and might start closed under a configured toggle default
// (steps, background steps, diagrams), plus an enclosing rule section — a match inside a
// closed rule would otherwise stay invisible. Raw Plant UML disclosures deliberately stay
// closed (they always were; a deep hit on diagram source is one click away, documented).
// What the search opened: emptying the search closes those, and only those (5.2.0): until then it closed every scenario, the
// one a link had just opened too (plans/BACK_BUTTON_NAVIGATION_PLAN.md S8)
var _openedBySearch = [];
function revealSingleMatch(el) {
    // the match shows: its feature (closed from the start on a long report, so until 5.2.0 the match opened out of
    // sight), its rule and the scenario
    [el.closest('details.feature'), el.closest('details.rule'), el].forEach(function(d) {
        if (d && !d.open) { _openedBySearch.push(d); d.setAttribute('open', ''); }
    });
    ['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background'].forEach(function(sel) {
        var section = el.querySelector(sel);
        if (section) section.setAttribute('open', '');
    });
}

function close_opened_by_search() {
    _openedBySearch.forEach(function(d) { d.removeAttribute('open'); });
    _openedBySearch = [];
}

function search_scenarios() {
    if (searchTimeoutId)
        clearTimeout(searchTimeoutId);
    searchTimeoutId = null;
    // an emptied search answers at once (5.2.0): it is the end of the run, and Back right after it has to see it
    var bar = document.getElementById('searchbar');
    if (bar && !bar.value) { run_search_scenarios(); return; }

    searchTimeoutId = setTimeout(function () {
        searchTimeoutId = null;
        run_search_scenarios();
    }, 300);
}
// Enter, and leaving the field, answer at once (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md 4.8): a word typed is one
// entry of the session history, and Back right after Enter undoes it with no wait.
function flush_search() {
    if (!searchTimeoutId) return;
    clearTimeout(searchTimeoutId);
    searchTimeoutId = null;
    run_search_scenarios();
}
// only where there is a page: the search's own tests load this file with none (tests/Kronikol.Tests.SearchEngine)
if (typeof document !== 'undefined') {
    document.addEventListener('keydown', function (e) { if (e.key === 'Enter' && e.target && e.target.id === 'searchbar') flush_search(); }, true);
    document.addEventListener('change', function (e) { if (e.target && e.target.id === 'searchbar') flush_search(); }, true);
}

// Every filter can be written in the search (5.0.0, plans/OPTION_C_PLAN.md S6, D16): tags (@tag) and statuses ($status)
// could already, and dependencies, duration and happy paths are qualifiers, a key and a colon, as GitHub's search writes
// them. dep:cow is a scenario that calls a dependency whose name holds "cow" (case, spaces and punctuation ignored;
// quotes for a name with spaces, commas for any of several); duration: compares a scenario's own time (the first-call
// warm-up left out, as the duration filter does) with >, >=, <, <=, = or a range a..b, in ms, s or m, or the report's
// percentiles p50 to p99, a bare value meaning at least; is:happy is a happy path. !! negates one. A qualifier only
// narrows: it combines with the rest of the search, and with the others, by AND. Only these keys, and never inside
// quotes, so a search for "status: 400" or "milk:" is still text. The qualifiers are taken out before the search runs,
// so the search, its deep search of the diagrams and the link see the rest, and a flag of their own hides a scenario.
var SEARCH_QUALIFIER = /(^|[\s(])(!!\s*)?(dep|dependency|duration|is):("[^"]*"|[^\s()"]+)/gi;
var DURATION_UNIT = { ms: 1, s: 1000, m: 60000, min: 60000 };
function qualifier_name(s) { return String(s).toLowerCase().replace(/[^a-z0-9]/g, ''); }
function qualifier_ms(v, percentiles) {
    v = v.toLowerCase();
    if (percentiles[v] !== undefined) return percentiles[v];
    var m = /^(\d+(?:\.\d+)?)(ms|s|m|min)?$/.exec(v);
    return m ? parseFloat(m[1]) * DURATION_UNIT[m[2] || 's'] : null;
}
// a scenario's own time, as the duration filter reads it
function qualifier_own_ms(d) {
    if (!d.el.hasAttribute('data-duration-ms')) return null;
    return parseFloat(d.el.getAttribute('data-duration-ms')) - parseFloat(d.el.getAttribute('data-warmup-ms') || '0');
}
// one qualifier as a test of a scenario; null when it cannot be read, and it stays text
function search_qualifier(key, value, negated, percentiles) {
    key = key.toLowerCase();
    if (value.charAt(0) === '"') value = value.slice(1, -1);
    var test = null;
    if (key === 'dep' || key === 'dependency') {
        var wants = value.split(',').map(qualifier_name).filter(Boolean);
        if (!wants.length) return null;
        test = function (d) {
            var names = Array.from(d.deps || []).map(qualifier_name);
            return wants.some(function (w) { return names.some(function (n) { return n.indexOf(w) >= 0; }); });
        };
    } else if (key === 'duration') {
        var range = /^(.+?)\.\.(.+)$/.exec(value);
        if (range) {
            var lo = qualifier_ms(range[1], percentiles), hi = qualifier_ms(range[2], percentiles);
            if (lo === null || hi === null) return null;
            test = function (d) { var t = qualifier_own_ms(d); return t !== null && t >= lo && t <= hi; };
        } else {
            var m = /^(>=|<=|>|<|=)?(.+)$/.exec(value), op = m[1] || '>=', at = qualifier_ms(m[2], percentiles);
            if (at === null) return null;
            test = function (d) {
                var t = qualifier_own_ms(d);
                if (t === null) return false;
                return op === '>' ? t > at : op === '>=' ? t >= at : op === '<' ? t < at : op === '<=' ? t <= at : Math.abs(t - at) < 0.5;
            };
        }
    } else if (key === 'is') {
        if (!/^happy(-?paths?)?$/.test(value.toLowerCase())) return null;
        test = function (d) { return !!d.isHappy; };
    }
    if (!test) return null;
    return negated ? function (d) { return !test(d); } : test;
}
// the search, split into its qualifiers and the rest; an operator a qualifier leaves dangling goes with it
function split_search_qualifiers(input) {
    var quals = [], bad = [], percentiles = {};
    var box = document.querySelector('.duration-filters');
    if (box) ['p50', 'p90', 'p95', 'p99'].forEach(function (k) { var v = parseFloat(box.getAttribute('data-' + k)); if (!isNaN(v)) percentiles[k] = v; });
    SEARCH_QUALIFIER.lastIndex = 0;
    var rest = input.replace(SEARCH_QUALIFIER, function (all, lead, negated, key, value) {
        var q = search_qualifier(key, value, !!negated, percentiles);
        if (!q) { bad.push(all.trim()); return all; }
        quals.push(q);
        return lead + ' ';
    });
    if (quals.length) {
        for (var i = 0, prev = null; i < 6 && prev !== rest; i++) {
            prev = rest;
            rest = rest.replace(/(&&|\|\|)\s*(&&|\|\|)/g, '$1').replace(/\(\s*(&&|\|\|)/g, '(').replace(/(&&|\|\|)\s*\)/g, ')')
                .replace(/\(\s*\)/g, ' ').replace(/^\s*(&&|\|\|)/, '').replace(/(&&|\|\|)\s*$/, '').replace(/!!\s*(?=$|&&|\|\||\))/g, '');
        }
        rest = rest.replace(/\s+/g, ' ').trim();
    }
    return { quals: quals, rest: rest, bad: bad };
}

function run_search_scenarios() {
    var c = fc();
    var bar = document.getElementById('searchbar');
    var parts = split_search_qualifiers(bar.value);
    for (var qi = 0; qi < c.items.length; qi++) {
        var qItem = c.items[qi];
        qItem.q = parts.quals.length > 0 && !parts.quals.every(function (test) { return test(qItem); });
    }
    // a qualifier that cannot be read stays text, and the bar says so
    bar.classList.toggle('kron-q-bad', parts.bad.length > 0);
    if (parts.bad.length) bar.setAttribute('data-q-bad', parts.bad.join(' ')); else bar.removeAttribute('data-q-bad');
    let input = parts.rest;
    input = input.toLowerCase().trim();

    // Advanced search path: when input contains &&, ||, or !!
    if (isAdvancedSearch(input)) {
        if (input.length === 0) {
            for (let i = 0; i < c.items.length; i++) {
                c.items[i].sr = false;
            }
            close_opened_by_search();
            applyVisibility(c);
            update_url_hash();
            if (window._kronDeepReset) window._kronDeepReset();
            return;
        }

        let advancedFailed = false;
        let matchCount = 0;
        let singleMatch = null;
        let advSearchTokens = [];
        for (let i = 0; i < c.items.length; i++) {
            let item = c.items[i];
            // what the Tags row filters on, Happy Path with it (plans/TAGS_PLAN.md §4.8), compared without case
            let tags = search_tags(item.el);

// kron:verdicts
            let result = advancedSearchMatch(input, item.searchText, tags, item.status, item.verdicts);
// kron:else
//|            let result = advancedSearchMatch(input, item.searchText, tags, item.status);
// kron:/verdicts
            if (result === null) {
                advancedFailed = true;
                break;
            }
            item.sr = !result;
            if (result) {
                matchCount++;
                singleMatch = item.el;
            }
        }

        if (!advancedFailed) {
            applyVisibility(c);
            update_url_hash();

            if (matchCount === 1 && singleMatch) {
                revealSingleMatch(singleMatch);
            }

            // Parameterized group row highlighting for advanced search
            let tokens = advancedSearchTokenise(input);
            advSearchTokens = tokens.filter(function(t) { return t.type === 'text' || t.type === 'phrase'; }).map(function(t) { return t.value; });
            if (advSearchTokens.length > 0) {
                document.querySelectorAll('details.scenario-parameterized').forEach(function(group) {
                    if (group.style.display === 'none') return;
                    var rows = group.querySelectorAll('tr[data-row-search]');
                    var firstMatchRow = null;
                    for (var ri = 0; ri < rows.length; ri++) {
                        var tbl = rows[ri].closest('table');
                        if (tbl && tbl.style.display === 'none') { rows[ri].classList.remove('row-search-match'); continue; }
                        var rowText = rows[ri].getAttribute('data-row-search') || '';
                        var allMatch = true;
                        for (var j = 0; j < advSearchTokens.length; j++) {
                            if (!rowText.includes(advSearchTokens[j])) { allMatch = false; break; }
                        }
                        if (allMatch && !firstMatchRow) { firstMatchRow = rows[ri]; }
                        rows[ri].classList.toggle('row-search-match', allMatch);
                    }
                    if (firstMatchRow && !firstMatchRow.classList.contains('row-active')) {
                        firstMatchRow.click();
                    }
                });
            } else {
                document.querySelectorAll('tr.row-search-match').forEach(function(r) { r.classList.remove('row-search-match'); });
            }
            if (window._kronDeepQuery) window._kronDeepQuery(input);
            return;
        }
        // If advancedFailed, fall through to legacy path
    }

    // Legacy search path
    let split = splitLegacyTagExpression(input);
    let tagExpr = split.tagExpr;
    let textInput = split.textInput;

    let searchTokens = parseSearchTokensIncludingQuotes(textInput);

    if (searchTokens.length === 0 && !tagExpr) {
        for (let i = 0; i < c.items.length; i++) {
            c.items[i].sr = false;
        }
        close_opened_by_search();
        applyVisibility(c);
        update_url_hash();
        if (window._kronDeepReset) window._kronDeepReset();
        return;
    }

    // Match at the scenario level
    let matchCount = 0;
    let singleMatch = null;
    for (let i = 0; i < c.items.length; i++) {
        let textMatch = true;
        if (searchTokens.length > 0) {
            let text = c.items[i].searchText;
            for (let j = 0; j < searchTokens.length; j++) {
                if (!text.includes(searchTokens[j])) {
                    textMatch = false;
                    break;
                }
            }
        }
        let tagMatch = true;
        if (tagExpr) {
            tagMatch = evaluateTagExpression(tagExpr, search_tags(c.items[i].el));
        }
        c.items[i].sr = !(textMatch && tagMatch);
        if (textMatch && tagMatch) {
            matchCount++;
            singleMatch = c.items[i].el;
        }
    }

    applyVisibility(c);
    update_url_hash();

    // Single match: expand scenario with diagrams
    if (matchCount === 1 && singleMatch) {
        revealSingleMatch(singleMatch);
    }

    // For parameterized groups: highlight matching row(s) based on per-row search data
    if (searchTokens.length > 0) {
        document.querySelectorAll('details.scenario-parameterized').forEach(function(group) {
            if (group.style.display === 'none') return;
            var rows = group.querySelectorAll('tr[data-row-search]');
            var firstMatchRow = null;
            for (var ri = 0; ri < rows.length; ri++) {
                var tbl = rows[ri].closest('table');
                if (tbl && tbl.style.display === 'none') { rows[ri].classList.remove('row-search-match'); continue; }
                var rowText = rows[ri].getAttribute('data-row-search') || '';
                var allMatch = true;
                for (var j = 0; j < searchTokens.length; j++) {
                    if (!rowText.includes(searchTokens[j])) { allMatch = false; break; }
                }
                if (allMatch && !firstMatchRow) { firstMatchRow = rows[ri]; }
                rows[ri].classList.toggle('row-search-match', allMatch);
            }
            if (firstMatchRow && !firstMatchRow.classList.contains('row-active')) {
                firstMatchRow.click();
            }
        });
    } else {
        document.querySelectorAll('tr.row-search-match').forEach(function(r) { r.classList.remove('row-search-match'); });
    }

    if (window._kronDeepQuery) window._kronDeepQuery(input);
}

// A legacy query's words, where @"a tag" is one word for a tag with spaces (plans/TAGS_PLAN.md §4.8).
function legacySearchWords(input) {
    return input.match(/@"[^"]*"?|\S+/g) || [];
}
// Splits a legacy query into its @tag expression and its plain-text remainder. Shared with the
// deep-search worker (included in its Blob by name), so the two paths cannot drift.
function splitLegacyTagExpression(input) {
    var tagExpr = null;
    var textInput = input;
    if (input.indexOf('@') !== -1) {
        var tagParts = [];
        var textParts = [];
        var tokens = legacySearchWords(input);
        var inTag = false;
        for (var t = 0; t < tokens.length; t++) {
            var tok = tokens[t];
            if (tok.startsWith('@') || tok === 'and' || tok === 'or' || tok === 'not' || tok === '(' || tok === ')') {
                tagParts.push(tok);
                inTag = true;
            } else if (inTag && (tok === 'and' || tok === 'or' || tok === 'not')) {
                tagParts.push(tok);
            } else {
                textParts.push(tok);
                inTag = false;
            }
        }
        if (tagParts.length > 0) {
            tagExpr = tagParts.join(' ');
            textInput = textParts.join(' ');
        }
    }
    return { tagExpr: tagExpr, textInput: textInput };
}

function parseSearchTokensIncludingQuotes(str) {
    let quoteTokens = [];
    for (let match of str.matchAll(/"(.*?)"/g)) {
        let phrase = match[1].trim();
        if (phrase.length > 0) quoteTokens.push(phrase);
    }

    let remaining = str.replace(/"(.*?)"/g, '').trim();

    let simpleTokens = [];
    if (remaining.length > 0) {
        let rawWords = remaining.split(/\s+/);
        for (let i = 0; i < rawWords.length; i++) {
            let token = rawWords[i].trim();
            if (token.length > 0) simpleTokens.push(token);
        }
    }

    return quoteTokens.concat(simpleTokens);
}

function evaluateTagExpression(expr, tags) {
    // Tokenize
    let tokens = [];
    let parts = legacySearchWords(expr);
    for (let i = 0; i < parts.length; i++) {
        let p = parts[i];
        if (p === 'and' || p === 'or' || p === 'not') tokens.push({type: p});
        else if (p === '(') tokens.push({type: 'lparen'});
        else if (p === ')') tokens.push({type: 'rparen'});
        else if (p.startsWith('@"')) tokens.push({type: 'tag', value: p.substring(2).replace(/"$/, '').trim().toLowerCase()});
        else if (p.startsWith('@')) tokens.push({type: 'tag', value: p.substring(1).toLowerCase()});
        else tokens.push({type: 'tag', value: p.toLowerCase()});
    }
    let pos = {i: 0};
    function parseOr() {
        let left = parseAnd();
        while (pos.i < tokens.length && tokens[pos.i].type === 'or') {
            pos.i++;
            left = left || parseAnd();
        }
        return left;
    }
    function parseAnd() {
        let left = parseNot();
        while (pos.i < tokens.length && tokens[pos.i].type === 'and') {
            pos.i++;
            left = left && parseNot();
        }
        return left;
    }
    function parseNot() {
        if (pos.i < tokens.length && tokens[pos.i].type === 'not') {
            pos.i++;
            return !parsePrimary();
        }
        return parsePrimary();
    }
    function parsePrimary() {
        if (pos.i < tokens.length && tokens[pos.i].type === 'lparen') {
            pos.i++;
            let result = parseOr();
            if (pos.i < tokens.length && tokens[pos.i].type === 'rparen') pos.i++;
            return result;
        }
        if (pos.i < tokens.length && tokens[pos.i].type === 'tag') {
            let v = tokens[pos.i].value;
            pos.i++;
            return tags.has(v);
        }
        return false;
    }
    return parseOr();
}