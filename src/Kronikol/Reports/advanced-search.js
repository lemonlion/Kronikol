// Advanced Search Engine for Kronikol Reports
// Pure functions — no DOM access. Tested via Jint.

/**
 * Returns true if the input contains advanced search operators (&&, ||, !!)
 * or a $status filter (which has no legacy equivalent), or a tag: or status: qualifier, which is a token of the
 * advanced search as @tag and $status are (SEARCH_PLAN 4.4): where the tokeniser reads one, outside quotes and where a
 * word starts, so "tag:x" and hashtag:x leave the search as it was.
 * When false, the caller should use the legacy search path.
 */
function isAdvancedSearch(input) {
    if (/&&|\|\||!!|\$\w/.test(input)) return true;
    if (!/(tag|status):/i.test(input)) return false;
    var tokens = advancedSearchTokenise(input);
    for (var i = 0; i < tokens.length; i++) {
        if (tokens[i].form) return true;
    }
    return false;
}

/**
 * Tokenises an advanced search input string.
 * Returns an array of token objects: { type, value }
 * Types: 'and', 'or', 'not', 'lparen', 'rparen', 'text', 'phrase', 'tag', 'status'
 * Each token the input holds also carries its offsets in the input, start and end (SEARCH_PLAN 4.4, for
 * searchQuerySpans); a tag or status its valueStart, past the @, the $ or the colon; one written tag: or status: its
 * form, 'tag:' or 'status:'; and a not written - before it, minus. The ands put between operands have no offsets.
 */
function advancedSearchTokenise(input) {
    var tokens = [];
    var i = 0;
    var str = input.trim();
    var off = input.length - input.replace(/^\s+/, '').length;

    while (i < str.length) {
        // Skip whitespace
        if (/\s/.test(str[i])) {
            i++;
            continue;
        }

        // &&
        if (str[i] === '&' && i + 1 < str.length && str[i + 1] === '&') {
            tokens.push({ type: 'and', start: off + i, end: off + i + 2 });
            i += 2;
            continue;
        }

        // ||
        if (str[i] === '|' && i + 1 < str.length && str[i + 1] === '|') {
            tokens.push({ type: 'or', start: off + i, end: off + i + 2 });
            i += 2;
            continue;
        }

        // !!
        if (str[i] === '!' && i + 1 < str.length && str[i + 1] === '!') {
            tokens.push({ type: 'not', start: off + i, end: off + i + 2 });
            i += 2;
            continue;
        }

        // (
        if (str[i] === '(') {
            tokens.push({ type: 'lparen', start: off + i, end: off + i + 1 });
            i++;
            continue;
        }

        // )
        if (str[i] === ')') {
            tokens.push({ type: 'rparen', start: off + i, end: off + i + 1 });
            i++;
            continue;
        }

        // Quoted phrase
        if (str[i] === '"') {
            var end = str.indexOf('"', i + 1);
            if (end === -1) {
                // Unclosed quote — treat rest as phrase
                var phrase = str.substring(i + 1).trim();
                if (phrase.length > 0) {
                    tokens.push({ type: 'phrase', value: phrase.toLowerCase(), start: off + i, end: off + str.length });
                }
                i = str.length;
            } else {
                var phrase = str.substring(i + 1, end).trim();
                if (phrase.length > 0) {
                    tokens.push({ type: 'phrase', value: phrase.toLowerCase(), start: off + i, end: off + end + 1 });
                }
                i = end + 1;
            }
            continue;
        }

        // @tag, or @"a tag" for a name with spaces (plans/TAGS_PLAN.md §4.8)
        if (str[i] === '@' && str[i + 1] === '"') {
            var at = i;
            var close = str.indexOf('"', i + 2);
            var quoted = (close < 0 ? str.substring(i + 2) : str.substring(i + 2, close)).trim().toLowerCase();
            i = close < 0 ? str.length : close + 1;
            if (quoted.length > 0) {
                tokens.push({ type: 'tag', value: quoted, start: off + at, valueStart: off + at + 1, end: off + i });
            }
            continue;
        }
        if (str[i] === '@') {
            var start = i + 1;
            i = start;
            while (i < str.length && !/[\s()"]/.test(str[i])) {
                i++;
            }
            var tagName = str.substring(start, i).toLowerCase();
            if (tagName.length > 0) {
                tokens.push({ type: 'tag', value: tagName, start: off + start - 1, valueStart: off + start, end: off + i });
            }
            continue;
        }

        // $status
        if (str[i] === '$') {
            var start = i + 1;
            i = start;
            while (i < str.length && !/[\s()"]/.test(str[i])) {
                i++;
            }
            var statusName = str.substring(start, i).toLowerCase();
            if (statusName.length > 0) {
                tokens.push({ type: 'status', value: statusName, start: off + start - 1, valueStart: off + start, end: off + i });
            }
            continue;
        }

        // tag:name and status:name, either quoted, and a - before either to exclude it (SEARCH_PLAN 4.4): the tests @name
        // and $name make, read where a word starts, in any case. The name ends where a word does; with no name after it
        // the key is the word it was, and tag:"" the text tag: as before.
        var form = /^(-?)(tag|status):/i.exec(str.substring(i, i + 8));
        if (form) {
            var at = i + form[0].length, key = form[2].toLowerCase(), stop = at, name;
            if (str[at] === '"') {
                stop = str.indexOf('"', at + 1);
                name = (stop < 0 ? str.substring(at + 1) : str.substring(at + 1, stop)).trim();
                stop = stop < 0 ? str.length : stop + 1;
            } else {
                while (stop < str.length && !/[\s()"]/.test(str[stop]) &&
                       !((str[stop] === '&' || str[stop] === '|' || str[stop] === '!') && str[stop + 1] === str[stop])) {
                    stop++;
                }
                name = str.substring(at, stop);
            }
            if (name.length > 0) {
                if (form[1]) tokens.push({ type: 'not', minus: true, start: off + i, end: off + i + 1 });
                tokens.push({ type: key, value: name.toLowerCase(), form: key + ':', start: off + i + form[1].length, valueStart: off + at, end: off + stop });
                i = stop;
                continue;
            }
        }

        // Bare word (text token)
        var start = i;
        while (i < str.length && !/[\s()"]/.test(str[i])) {
            // Stop if we hit && || !!
            if ((str[i] === '&' && i + 1 < str.length && str[i + 1] === '&') ||
                (str[i] === '|' && i + 1 < str.length && str[i + 1] === '|') ||
                (str[i] === '!' && i + 1 < str.length && str[i + 1] === '!')) {
                break;
            }
            i++;
        }
        var word = str.substring(start, i).toLowerCase();
        if (word.length > 0) {
            tokens.push({ type: 'text', value: word, start: off + start, end: off + i });
        }
    }

    // Insert implicit AND between adjacent operands (text, phrase, tag, status, rparen→operand, operand→lparen)
    var operandTypes = { 'text': true, 'phrase': true, 'tag': true, 'status': true };
    var result = [];
    for (var t = 0; t < tokens.length; t++) {
        if (t > 0) {
            var prev = tokens[t - 1];
            var curr = tokens[t];
            var prevIsOperand = operandTypes[prev.type] || prev.type === 'rparen';
            var currIsOperand = operandTypes[curr.type] || curr.type === 'lparen' || curr.type === 'not';
            if (prevIsOperand && currIsOperand) {
                result.push({ type: 'and' });
            }
        }
        result.push(tokens[t]);
    }

    return result;
}

/**
 * Parses a token array into an AST.
 * Returns an AST node or null on parse error.
 *
 * Grammar:
 *   Expression → OrExpr
 *   OrExpr     → AndExpr ( 'or' AndExpr )*
 *   AndExpr    → NotExpr ( 'and' NotExpr )*
 *   NotExpr    → 'not' Primary | Primary
 *   Primary    → '(' Expression ')' | phrase | tag | status | text
 */
function advancedSearchParse(tokens) {
    var pos = { i: 0 };

    function parseOr() {
        var left = parseAnd();
        if (left === null) return null;
        while (pos.i < tokens.length && tokens[pos.i].type === 'or') {
            pos.i++;
            var right = parseAnd();
            if (right === null) return null;
            left = { type: 'or', left: left, right: right };
        }
        return left;
    }

    function parseAnd() {
        var left = parseNot();
        if (left === null) return null;
        while (pos.i < tokens.length && tokens[pos.i].type === 'and') {
            pos.i++;
            var right = parseNot();
            if (right === null) return null;
            left = { type: 'and', left: left, right: right };
        }
        return left;
    }

    function parseNot() {
        if (pos.i < tokens.length && tokens[pos.i].type === 'not') {
            pos.i++;
            var operand = parsePrimary();
            if (operand === null) return null;
            return { type: 'not', operand: operand };
        }
        return parsePrimary();
    }

    function parsePrimary() {
        if (pos.i >= tokens.length) return null;

        var tok = tokens[pos.i];

        if (tok.type === 'lparen') {
            pos.i++;
            var expr = parseOr();
            if (expr === null) return null;
            if (pos.i >= tokens.length || tokens[pos.i].type !== 'rparen') return null;
            pos.i++;
            return expr;
        }

        if (tok.type === 'text') {
            pos.i++;
            return { type: 'text', value: tok.value };
        }

        if (tok.type === 'phrase') {
            pos.i++;
            return { type: 'phrase', value: tok.value };
        }

        if (tok.type === 'tag') {
            pos.i++;
            return { type: 'tag', value: tok.value };
        }

        if (tok.type === 'status') {
            pos.i++;
            return { type: 'status', value: tok.value };
        }

        // Unexpected token
        return null;
    }

    var ast = parseOr();

    // If there are leftover tokens, it's a parse error
    if (ast !== null && pos.i < tokens.length) return null;

    return ast;
}

/**
 * Evaluates an AST node against scenario data.
 * @param {object} ast - The AST node from advancedSearchParse
 * @param {string} searchText - Lowercased pre-computed search text for the scenario
 * @param {object} tags - A Set (or object with .has()) of lowercased tag strings
 * @param {string} status - The scenario status string (e.g. "Passed", "Failed")
// kron:verdicts
 * @param {object} [verdicts] - A Set of the scenario's lowercased cross-run history verdicts
 *   ("flaky", "broke", "new"...), or null/undefined when the report carries no history
// kron:/verdicts
 * @returns {boolean}
 */
// kron:verdicts
function advancedSearchEvaluate(ast, searchText, tags, status, verdicts) {
// kron:else
//|function advancedSearchEvaluate(ast, searchText, tags, status) {
// kron:/verdicts
    if (!ast) return false;

    switch (ast.type) {
        case 'text':
            return searchText.indexOf(ast.value) !== -1;
        case 'phrase':
            return searchText.indexOf(ast.value) !== -1;
        case 'tag':
            return tags.has(ast.value);
        case 'status':
// kron:verdicts
            // `$failed` is the execution result; `$flaky`, `$broke`, `$new`... are the cross-run
            // history verdicts, carried as their own set because a scenario is routinely both (a
            // flaky scenario's status is Passed). An execution-result name is always the status and
            // is never looked up as a verdict, so nothing existing can be shadowed.
            // a scenario skipped after a failure is skipped, as the summary counts it (E3, 5.9.1)
            if (status.toLowerCase() === ast.value || (ast.value === 'skipped' && status === 'SkippedAfterFailure')) return true;
            if (ast.value === 'passed' || ast.value === 'failed' || ast.value === 'skipped' ||
                ast.value === 'bypassed' || ast.value === 'skippedafterfailure') return false;
            return !!(verdicts && verdicts.has(ast.value));
// kron:else
//|            return status.toLowerCase() === ast.value || (ast.value === 'skipped' && status === 'SkippedAfterFailure');
// kron:/verdicts
        case 'and':
// kron:verdicts
            return advancedSearchEvaluate(ast.left, searchText, tags, status, verdicts) &&
                   advancedSearchEvaluate(ast.right, searchText, tags, status, verdicts);
// kron:else
//|            return advancedSearchEvaluate(ast.left, searchText, tags, status) &&
//|                   advancedSearchEvaluate(ast.right, searchText, tags, status);
// kron:/verdicts
        case 'or':
// kron:verdicts
            return advancedSearchEvaluate(ast.left, searchText, tags, status, verdicts) ||
                   advancedSearchEvaluate(ast.right, searchText, tags, status, verdicts);
// kron:else
//|            return advancedSearchEvaluate(ast.left, searchText, tags, status) ||
//|                   advancedSearchEvaluate(ast.right, searchText, tags, status);
// kron:/verdicts
        case 'not':
// kron:verdicts
            return !advancedSearchEvaluate(ast.operand, searchText, tags, status, verdicts);
// kron:else
//|            return !advancedSearchEvaluate(ast.operand, searchText, tags, status);
// kron:/verdicts
        default:
            return false;
    }
}

/**
 * Convenience entry point: tokenise → parse → evaluate.
 * Returns true/false on success, or null on parse error (caller falls back to legacy).
 */
// kron:verdicts
function advancedSearchMatch(input, searchText, tags, status, verdicts) {
// kron:else
//|function advancedSearchMatch(input, searchText, tags, status) {
// kron:/verdicts
    var tokens = advancedSearchTokenise(input);
    if (tokens.length === 0) return null;
    var ast = advancedSearchParse(tokens);
    if (ast === null) return null;
// kron:verdicts
    return advancedSearchEvaluate(ast, searchText, tags, status, verdicts);
// kron:else
//|    return advancedSearchEvaluate(ast, searchText, tags, status);
// kron:/verdicts
}

/**
 * The tag: and status: qualifiers of a query, where the tokeniser reads them, and what the search makes of each
 * (SEARCH_PLAN 4.4). statuses lists the names status: reads besides the execution results, which are this report's
 * history verdict names, or is null to read every name. The search function and searchQuerySpans both read a query
 * through it. Returns { forms, bad, text }:
 *   forms  each qualifier, { key: 'tag' or 'status', from, keyStart, valueStart, end, readable }, offsets in input,
 *          from at the - before it; a key whose quotes hold no name (tag:"") is one that cannot be read
 *   bad    those that cannot be read, as written, for the search field to name
 *   text   input as the search reads it: a status name it cannot read is the text it was before status: was a key,
 *          put in quotes, or with a space after its colon where the name was in quotes; tag:"" reads so as it stands
 */
function searchTokenQualifiers(input, statuses) {
    var results = ['passed', 'failed', 'skipped', 'bypassed', 'skippedafterfailure'];
    var names = statuses ? statuses.map(function (s) { return String(s).toLowerCase(); }) : null;
    var tokens = advancedSearchTokenise(input), forms = [], bad = [], text = input, m;
    for (var i = 0; i < tokens.length; i++) {
        var t = tokens[i];
        if (t.form) {
            forms.push({ key: t.type, from: i > 0 && tokens[i - 1].minus ? tokens[i - 1].start : t.start, keyStart: t.start,
                valueStart: t.valueStart, end: t.end,
                readable: t.type === 'tag' || !names || results.indexOf(t.value) >= 0 || names.indexOf(t.value) >= 0 });
        } else if (t.type === 'text' && (m = /^(-?)(tag|status):$/.exec(t.value)) && input.charAt(t.end) === '"') {
            var shut = input.indexOf('"', t.end + 1);
            forms.push({ key: m[2], from: t.start, keyStart: t.start + m[1].length, valueStart: t.end,
                end: shut < 0 ? input.length : shut + 1, readable: false });
        }
    }
    for (var j = forms.length - 1; j >= 0; j--) {
        var f = forms[j];
        if (f.readable) continue;
        bad.unshift(input.slice(f.from, f.end));
        if (f.key !== 'status') continue;
        text = input.charAt(f.valueStart) === '"'
            ? text.slice(0, f.valueStart) + ' ' + text.slice(f.valueStart)
            : text.slice(0, f.from) + '"' + text.slice(f.from, f.end) + '"' + text.slice(f.end);
    }
    return { forms: forms, bad: bad, text: text };
}

/**
 * Cuts a query into spans (SEARCH_PLAN 4.4): the query builder colours each (a key plain, a value in the accent, an
 * operator purple, a value the search cannot read in the warning colour) and finds the token at the caret among them.
 * Pure, with what it cannot know itself in opts:
 *   opts.statuses     the names status: reads besides the execution results: this report's history verdict names.
 *                     Given, any other name cannot be read; left out, every name is read.
 *   opts.percentiles  this report's percentiles of own time as the Duration row carries them, { p90: 3000 }. Given,
 *                     a percentile not among them cannot be read; left out, p50, p90, p95 and p99 are read.
 * Returns the spans in order, each starting where the one before ends, from 0 to text.length (none for an empty
 * text). Each is { kind, start, end }, offsets in text with end exclusive; the spans of a qualifier also carry key,
 * its name: 'tag', 'status', 'dep', 'feature', 'duration' or 'is' (dependency: is 'dep', @ a tag and $ a status).
 * A kind is one of
 *   'text'        a word the search reads as text, one span a word, or the spaces between words
 *   'phrase'      a "quoted phrase"
 *   'key'         a qualifier's key and colon (tag:, dep:), or the @ or $ before a tag or a status
 *   'value'       a qualifier's value the search reads (failed, "two words", >2s, payment,order)
 *   'operator'    &&, ||, !!, a bracket, a - or not that negates a qualifier, and and, or and not in a legacy tag
 *                 expression
 *   'unreadable'  the value of a qualifier the search cannot read, which it reads as text and marks the field for
 *                 (duration:abc, is:xyz, status:nonsense); its key stays a key, and a - or not before it is text
 * A word is cut as the search reads it: a query with no &&, ||, !!, $status, tag: or status: by the legacy path, where
 * and, or, not and the brackets against a tag join tags and a tag keeps the brackets it closes (@issue(12)); any other
 * by the advanced grammar, as is one it cannot parse (a bracket not yet closed), which the search reads as words.
 */
function searchQuerySpans(text, opts) {
    text = text === undefined || text === null ? '' : String(text);
    opts = opts || {};
    var statuses = opts.statuses || null, percentiles = opts.percentiles || null;
    var spans = [], quals = [], m;
    function add(kind, start, end, key) {
        if (end > start) spans.push(key ? { kind: kind, start: start, end: end, key: key } : { kind: kind, start: start, end: end });
    }

    // The search function's qualifiers, found and read as its SEARCH_QUALIFIER, qualifier_place and search_qualifier
    // read them (report-search-function.js): on their own, a !!, not or - before them; never in quotes; in a group a
    // !! or not negates, unread. SearchQuerySpansTests holds the two to one reading.
    var re = /(^|[\s(])(!!\s*|not\s+|-)?(dep|dependency|duration|is|feature):("[^"]*"|[^\s()"]+)/gi;
    function placed(at) {
        var negated = [], quoted = false;
        for (var i = 0; i < at; i++) {
            var ch = text.charAt(i);
            if (ch === '"') quoted = !quoted;
            else if (quoted) continue;
            else if (ch === '(') negated.push(/(!!\s*|\bnot\s+)$/i.test(text.slice(0, i)));
            else if (ch === ')') negated.pop();
        }
        return quoted ? 'quoted' : negated.indexOf(true) >= 0 ? 'negated' : 'top';
    }
    function ms(v) {
        v = v.toLowerCase();
        if (/^p(50|90|95|99)$/.test(v)) return !percentiles || percentiles[v] !== undefined;
        return /^\d+(\.\d+)?(ms|s|m|min)?$/.test(v);
    }
    function readable(key, v) {
        if (v.charAt(0) === '"') v = v.slice(1, -1);
        if (key === 'dep') return v.split(',').some(function (w) { return w.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '') !== ''; });
        if (key === 'feature') return v.split(',').some(function (w) { return w.trim() !== ''; });
        if (key === 'is') return /^happy(-?paths?)?$/.test(v.toLowerCase());
        var range = /^(.+?)\.\.(.+)$/.exec(v), at = /^(>=|<=|>|<|=)?(.+)$/.exec(v);
        return range ? ms(range[1]) && ms(range[2]) : !!at && ms(at[2]);
    }
    while ((m = re.exec(text))) {
        var from = m.index + m[1].length, place = placed(from);
        if (place === 'quoted') continue;
        var key = m[3].toLowerCase() === 'dependency' ? 'dep' : m[3].toLowerCase(), keyStart = from + (m[2] || '').length;
        quals.push({ from: from, neg: m[2] || '', key: key, keyStart: keyStart, valueStart: keyStart + m[3].length + 1,
            end: m.index + m[0].length, readable: place === 'top' && readable(key, m[4]) });
    }

    // The path the search takes, from the query as the search function hands it on: the qualifiers it read taken out,
    // with the operators they leave dangling as it takes them, and the tag: and status: it cannot read as text
    var read = text, taken = false;
    for (var q = quals.length - 1; q >= 0; q--) {
        if (!quals[q].readable) continue;
        read = read.slice(0, quals[q].from) + ' ' + read.slice(quals[q].end);
        taken = true;
    }
    for (var n = 0, prev = null; taken && n < 6 && prev !== read; n++) {
        prev = read;
        read = read.replace(/(&&|\|\||\band\b|\bor\b)\s*(?:&&|\|\||\band\b|\bor\b)/gi, '$1')
            .replace(/\(\s*(&&|\|\||\band\b|\bor\b)/gi, '(').replace(/(&&|\|\||\band\b|\bor\b)\s*\)/gi, ')')
            .replace(/\(\s*\)/g, ' ').replace(/^\s*(?:&&|\|\||\band\b|\bor\b)/i, '')
            .replace(/(?:&&|\|\||\band\b|\bor\b)\s*$/i, '').replace(/!!\s*(?=$|&&|\|\||\))/g, '');
    }
    read = searchTokenQualifiers(read, statuses).text;
    var advanced = isAdvancedSearch(read), tagExpression = !advanced && read.indexOf('@') >= 0;

    quals.forEach(function (q) {
        var word = q.neg.replace(/\s+$/, '');
        if (word) add(q.readable || word === '!!' ? 'operator' : 'text', q.from, q.from + word.length, q.key);
        add('key', q.keyStart, q.valueStart, q.key);
        add(q.readable ? 'value' : 'unreadable', q.valueStart, q.end, q.key);
    });

    // The rest, read by the tokeniser at the same offsets with those qualifiers blanked out
    var rest = text, forms = {};
    quals.forEach(function (q) { rest = rest.slice(0, q.from) + new Array(q.end - q.from + 1).join(' ') + rest.slice(q.end); });
    searchTokenQualifiers(rest, statuses).forms.forEach(function (f) { forms[f.keyStart] = f; });
    var tokens = advancedSearchTokenise(rest), until = 0, tails = [];
    function bracket(t) {
        if (advanced) return true;
        if (t.type === 'lparen') return /^\(+@/.test(rest.slice(t.start));
        return tails.some(function (r) { return t.start >= r[0] && t.start < r[1]; });
    }
    for (var i = 0; i < tokens.length; i++) {
        var t = tokens[i];
        if (t.start === undefined || t.start < until) continue;
        if (t.type === 'and' || t.type === 'or') add('operator', t.start, t.end);
        else if (t.type === 'not') {
            var minus = t.minus ? forms[t.end] : null;
            add(minus && !minus.readable ? 'text' : 'operator', t.start, t.end, minus ? minus.key : null);
        } else if (t.type === 'lparen' || t.type === 'rparen') add(bracket(t) ? 'operator' : 'text', t.start, t.end);
        else if (t.type === 'phrase') add('phrase', t.start, t.end);
        else if (t.type === 'tag' || t.type === 'status') {
            var form = t.form ? forms[t.start] : null, end = t.end;
            if (!advanced && t.type === 'tag' && rest.charAt(t.valueStart) !== '"') {
                // a legacy tag is its whole word but the brackets after it that close a group: @issue(12), @api)
                var legacyWord = /^\S+/.exec(rest.slice(t.start))[0], tail = 0;
                var opens = (legacyWord.match(/\(/g) || []).length, closes = (legacyWord.match(/\)/g) || []).length;
                while (closes - tail > opens && legacyWord.charAt(legacyWord.length - 1 - tail) === ')') tail++;
                end = until = t.start + legacyWord.length - tail;
                tails.push([end, end + tail]);
            }
            add('key', t.start, t.valueStart, t.type);
            add(form && !form.readable ? 'unreadable' : 'value', t.valueStart, end, t.type);
        } else {
            var bare = /^(-?)(tag|status|dep|dependency|duration|is|feature):$/.exec(t.value);
            if (bare) {
                var bareKey = bare[2] === 'dependency' ? 'dep' : bare[2], empty = forms[t.start + bare[1].length];
                add('text', t.start, t.start + bare[1].length, bareKey);
                add('key', t.start + bare[1].length, t.end, bareKey);
                if (empty) add('unreadable', empty.valueStart, empty.end, bareKey);
            } else add(tagExpression && /^(and|or|not)$/.test(t.value) ? 'operator' : 'text', t.start, t.end);
        }
    }

    // In order, the gaps between them text (spaces apart), and a word cut in pieces one span
    spans.sort(function (a, b) { return a.start - b.start; });
    var out = [], at = 0;
    function space(s) { return /^\s+$/.test(text.slice(s.start, s.end)); }
    function gap(to) {
        var run = /\s+|\S+/g, g;
        var piece = text.slice(at, to);
        while ((g = run.exec(piece))) out.push({ kind: 'text', start: at + g.index, end: at + g.index + g[0].length });
        at = to;
    }
    spans.forEach(function (s) {
        if (s.start < at) return;
        if (s.start > at) gap(s.start);
        out.push(s);
        at = s.end;
    });
    if (at < text.length) gap(text.length);
    var merged = [];
    out.forEach(function (s) {
        var last = merged[merged.length - 1];
        if (last && last.kind === 'text' && s.kind === 'text' && last.key === s.key && last.end === s.start && !space(last) && !space(s)) last.end = s.end;
        else merged.push(s);
    });
    return merged;
}
