// Advanced Search Engine for Kronikol Reports
// Pure functions — no DOM access. Tested via Jint.

/**
 * Returns true if the input contains advanced search operators (&&, ||, !!)
 * or a $status filter (which has no legacy equivalent).
 * When false, the caller should use the legacy search path.
 */
function isAdvancedSearch(input) {
    return /&&|\|\||!!|\$\w/.test(input);
}

/**
 * Tokenises an advanced search input string.
 * Returns an array of token objects: { type, value }
 * Types: 'and', 'or', 'not', 'lparen', 'rparen', 'text', 'phrase', 'tag', 'status'
 */
function advancedSearchTokenise(input) {
    var tokens = [];
    var i = 0;
    var str = input.trim();

    while (i < str.length) {
        // Skip whitespace
        if (/\s/.test(str[i])) {
            i++;
            continue;
        }

        // &&
        if (str[i] === '&' && i + 1 < str.length && str[i + 1] === '&') {
            tokens.push({ type: 'and' });
            i += 2;
            continue;
        }

        // ||
        if (str[i] === '|' && i + 1 < str.length && str[i + 1] === '|') {
            tokens.push({ type: 'or' });
            i += 2;
            continue;
        }

        // !!
        if (str[i] === '!' && i + 1 < str.length && str[i + 1] === '!') {
            tokens.push({ type: 'not' });
            i += 2;
            continue;
        }

        // (
        if (str[i] === '(') {
            tokens.push({ type: 'lparen' });
            i++;
            continue;
        }

        // )
        if (str[i] === ')') {
            tokens.push({ type: 'rparen' });
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
                    tokens.push({ type: 'phrase', value: phrase.toLowerCase() });
                }
                i = str.length;
            } else {
                var phrase = str.substring(i + 1, end).trim();
                if (phrase.length > 0) {
                    tokens.push({ type: 'phrase', value: phrase.toLowerCase() });
                }
                i = end + 1;
            }
            continue;
        }

        // @tag
        if (str[i] === '@') {
            var start = i + 1;
            i = start;
            while (i < str.length && !/[\s()"]/.test(str[i])) {
                i++;
            }
            var tagName = str.substring(start, i).toLowerCase();
            if (tagName.length > 0) {
                tokens.push({ type: 'tag', value: tagName });
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
                tokens.push({ type: 'status', value: statusName });
            }
            continue;
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
            tokens.push({ type: 'text', value: word });
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
 * @param {object} [verdicts] - A Set of the scenario's lowercased cross-run history verdicts
 *   ("flaky", "broke", "new"...), or null/undefined when the report carries no history
 * @returns {boolean}
 */
function advancedSearchEvaluate(ast, searchText, tags, status, verdicts) {
    if (!ast) return false;

    switch (ast.type) {
        case 'text':
            return searchText.indexOf(ast.value) !== -1;
        case 'phrase':
            return searchText.indexOf(ast.value) !== -1;
        case 'tag':
            return tags.has(ast.value);
        case 'status':
            // `$failed` is the execution result; `$flaky`, `$broke`, `$new`... are the cross-run
            // history verdicts, carried as their own set because a scenario is routinely both (a
            // flaky scenario's status is Passed). An execution-result name is always the status and
            // is never looked up as a verdict, so nothing existing can be shadowed.
            if (status.toLowerCase() === ast.value) return true;
            if (ast.value === 'passed' || ast.value === 'failed' || ast.value === 'skipped' ||
                ast.value === 'bypassed' || ast.value === 'skippedafterfailure') return false;
            return !!(verdicts && verdicts.has(ast.value));
        case 'and':
            return advancedSearchEvaluate(ast.left, searchText, tags, status, verdicts) &&
                   advancedSearchEvaluate(ast.right, searchText, tags, status, verdicts);
        case 'or':
            return advancedSearchEvaluate(ast.left, searchText, tags, status, verdicts) ||
                   advancedSearchEvaluate(ast.right, searchText, tags, status, verdicts);
        case 'not':
            return !advancedSearchEvaluate(ast.operand, searchText, tags, status, verdicts);
        default:
            return false;
    }
}

/**
 * Convenience entry point: tokenise → parse → evaluate.
 * Returns true/false on success, or null on parse error (caller falls back to legacy).
 */
function advancedSearchMatch(input, searchText, tags, status, verdicts) {
    var tokens = advancedSearchTokenise(input);
    if (tokens.length === 0) return null;
    var ast = advancedSearchParse(tokens);
    if (ast === null) return null;
    return advancedSearchEvaluate(ast, searchText, tags, status, verdicts);
}
