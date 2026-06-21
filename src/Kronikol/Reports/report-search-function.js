var searchTimeoutId;

function toggle_search_help() {
    var panel = document.querySelector('.search-help-panel');
    if (panel) panel.style.display = panel.style.display === 'none' ? '' : 'none';
}

function search_scenarios() {
    if (searchTimeoutId)
        clearTimeout(searchTimeoutId);

    searchTimeoutId = setTimeout(function () {
        run_search_scenarios();
    }, 300);
}

function run_search_scenarios() {
    var c = fc();
    let input = document.getElementById('searchbar').value;
    input = input.toLowerCase().trim();

    // Advanced search path: when input contains &&, ||, or !!
    if (isAdvancedSearch(input)) {
        if (input.length === 0) {
            for (let i = 0; i < c.items.length; i++) {
                c.items[i].sr = false;
                c.items[i].el.removeAttribute('open');
            }
            applyVisibility(c);
            update_url_hash();
            return;
        }

        let advancedFailed = false;
        let matchCount = 0;
        let singleMatch = null;
        let advSearchTokens = [];
        for (let i = 0; i < c.items.length; i++) {
            let item = c.items[i];
            let tags = new Set();
            let cats = (item.el.getAttribute('data-categories') || '').toLowerCase();
            let labels = (item.el.getAttribute('data-labels') || '').toLowerCase();
            if (cats) cats.split(',').forEach(function(t) { tags.add(t.trim()); });
            if (labels) labels.split(',').forEach(function(t) { tags.add(t.trim()); });

            let result = advancedSearchMatch(input, item.searchText, tags, item.status);
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
                singleMatch.setAttribute('open', '');
                let diagrams = singleMatch.querySelector('details.example-diagrams');
                if (diagrams) diagrams.setAttribute('open', '');
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
            return;
        }
        // If advancedFailed, fall through to legacy path
    }

    // Legacy search path
    // Extract @tag expressions
    let tagExpr = null;
    let textInput = input;
    if (input.indexOf('@') !== -1) {
        let tagParts = [];
        let textParts = [];
        let tokens = input.split(/\s+/);
        let inTag = false;
        for (let t = 0; t < tokens.length; t++) {
            let tok = tokens[t];
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

    let searchTokens = parseSearchTokensIncludingQuotes(textInput);

    if (searchTokens.length === 0 && !tagExpr) {
        for (let i = 0; i < c.items.length; i++) {
            c.items[i].sr = false;
            c.items[i].el.removeAttribute('open');
        }
        applyVisibility(c);
        update_url_hash();
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
            let cats = (c.items[i].el.getAttribute('data-categories') || '').toLowerCase();
            let labels = (c.items[i].el.getAttribute('data-labels') || '').toLowerCase();
            let allTags = new Set();
            if (cats) cats.split(',').forEach(function(t) { allTags.add(t.trim()); });
            if (labels) labels.split(',').forEach(function(t) { allTags.add(t.trim()); });
            tagMatch = evaluateTagExpression(tagExpr, allTags);
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
        singleMatch.setAttribute('open', '');
        let diagrams = singleMatch.querySelector('details.example-diagrams');
        if (diagrams) diagrams.setAttribute('open', '');
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
    let parts = expr.split(/\s+/);
    for (let i = 0; i < parts.length; i++) {
        let p = parts[i];
        if (p === 'and' || p === 'or' || p === 'not') tokens.push({type: p});
        else if (p === '(') tokens.push({type: 'lparen'});
        else if (p === ')') tokens.push({type: 'rparen'});
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