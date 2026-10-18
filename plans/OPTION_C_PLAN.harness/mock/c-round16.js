
/* Round 16 (owner, 2026-10-18): the filters' ANY / ALL switches, ANY by default for tags and dependencies. The
   report's switches read AND and OR, and the dependencies started on AND. The modes underneath keep the report's own
   values (the URL hash and Clear All read them); only what the switch says changes, wherever the report writes it. */
(function () {
  var doc = document;
  var WORD = { AND: 'ALL', OR: 'ANY' };
  function relabel(btn, title) {
    var fix = function () {
      var t = (btn.textContent || '').trim();
      if (WORD[t]) btn.textContent = WORD[t];
    };
    btn.title = title;
    fix();
    new MutationObserver(fix).observe(btn, { childList: true, characterData: true, subtree: true });
  }
  function run() {
    // the dependencies start on ANY, as the tags do; a link that names a mode keeps it
    if (typeof window._depModeDefault !== 'undefined' && window._depModeDefault !== 'OR') {
      window._depModeDefault = 'OR';
      if (!/(^|[#&])depmode=/.test(location.hash.slice(1))) {
        window._depMode = 'OR';
        if (doc.querySelector('.dependency-toggle.dependency-active') && typeof window.filter_dependencies === 'function') window.filter_dependencies();
      }
    }
    var dep = doc.querySelector('.dep-mode-toggle');
    if (dep) {
      // the switch says the mode the filter is in (the report wrote its own default into it)
      dep.textContent = window._depMode || dep.textContent;
      relabel(dep, 'ANY: scenarios that call any of the chosen dependencies. ALL: scenarios that call every one of them. Click to switch.');
    }
    var tag = doc.querySelector('.tag-mode-toggle');
    if (tag) relabel(tag, 'ANY: scenarios with any of the chosen tags. ALL: scenarios with every one of them. Click to switch.');
    var cat = doc.querySelector('.cat-mode-toggle');
    if (cat) relabel(cat, 'ANY: scenarios in any of the chosen categories. ALL: scenarios in every one of them. Click to switch.');
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 16 (owner): every filter can be written in the search bar. Tags (@tag) and statuses ($status) already could;
   dependencies, duration and happy paths are written as qualifiers, a key and a colon, as GitHub, Gmail and Slack
   searches write them:
     dep:cow             scenarios that call a dependency whose name holds "cow" (case, spaces and punctuation
                         ignored; dep:"cow service" for a name with spaces); dep:cow,goat calls any of them
     duration:>2s        scenarios by their own time (the first-call warm-up left out, as the duration filter and the
                         percentiles do): >, >=, <, <= or a range 1s..5s, in ms, s or m; p50, p90, p95, p99 are the
                         report's percentiles (duration:>p90); a bare duration:2s means 2s or longer
     is:happy            happy paths
   Each can be negated with !!, as any search term can. A qualifier only ever narrows: it combines with the rest of the
   search, and with the other qualifiers, by AND (dep:cow dep:goat calls both). A key nobody would type before a colon
   in a search for text, and only these three keys, so a search for "status: 400" or "milk:" still searches text; a
   phrase in quotes is always text.
   The qualifiers are taken out of the search before the report runs it, so the report's own search, its deep search
   of the diagrams and its URL hash work as they did, and they hide scenarios through a flag of their own beside the
   other filters'. */
(function () {
  var doc = document;
  var QRX = /(^|[\s(])(!!\s*)?(dep|dependency|duration|is):("[^"]*"|[^\s()"]+)/gi;
  var UNIT = { ms: 1, s: 1000, m: 60000, min: 60000 };
  function norm(s) { return String(s).toLowerCase().replace(/[^a-z0-9]/g, ''); }
  function percentiles() {
    var d = doc.querySelector('.duration-filters'), out = {};
    if (d) ['p50', 'p90', 'p95', 'p99'].forEach(function (k) { var v = parseFloat(d.getAttribute('data-' + k)); if (!isNaN(v)) out[k] = v; });
    return out;
  }
  function ms(v, pct) {
    v = v.toLowerCase();
    if (pct[v] !== undefined) return pct[v];
    var m = /^(\d+(?:\.\d+)?)(ms|s|m|min)?$/.exec(v);
    return m ? parseFloat(m[1]) * UNIT[m[2] || 's'] : null;
  }
  // one qualifier, made into a test of a scenario; null when it cannot be read
  function qualifier(key, value, neg, pct) {
    key = key.toLowerCase();
    if (value.charAt(0) === '"') value = value.slice(1, -1);
    var test = null;
    if (key === 'dep' || key === 'dependency') {
      var wants = value.split(',').map(norm).filter(Boolean);
      if (!wants.length) return null;
      test = function (d) {
        var names = Array.from(d.deps || []).map(norm);
        return wants.some(function (w) { return names.some(function (n) { return n.indexOf(w) >= 0; }); });
      };
    } else if (key === 'duration') {
      var range = /^(.+?)\.\.(.+)$/.exec(value), lo, hi, op, at;
      if (range) {
        lo = ms(range[1], pct); hi = ms(range[2], pct);
        if (lo === null || hi === null) return null;
        test = function (d) { var t = own(d); return t !== null && t >= lo && t <= hi; };
      } else {
        var m = /^(>=|<=|>|<|=)?(.+)$/.exec(value);
        op = m[1] || '>='; at = ms(m[2], pct);
        if (at === null) return null;
        test = function (d) {
          var t = own(d);
          if (t === null) return false;
          return op === '>' ? t > at : op === '>=' ? t >= at : op === '<' ? t < at : op === '<=' ? t <= at : Math.abs(t - at) < 0.5;
        };
      }
    } else if (key === 'is') {
      if (!/^happy(-?paths?)?$/.test(value.toLowerCase())) return null;
      test = function (d) { return !!d.isHappy; };
    }
    if (!test) return null;
    return neg ? function (d) { return !test(d); } : test;
  }
  // a scenario's own time, as the duration filter reads it
  function own(d) {
    if (!d.el.hasAttribute('data-duration-ms')) return null;
    return parseFloat(d.el.getAttribute('data-duration-ms')) - parseFloat(d.el.getAttribute('data-warmup-ms') || '0');
  }
  // the search, split into its qualifiers and the rest; the rest keeps its own operators, and one left dangling where
  // a qualifier was taken out goes with it
  function split(input) {
    var quals = [], bad = [], pct = percentiles();
    var rest = input.replace(QRX, function (all, lead, neg, key, value) {
      var q = qualifier(key, value, !!neg, pct);
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
  function apply(quals) {
    if (typeof window.fc !== 'function') return;
    var c = window.fc();
    c.items.forEach(function (d) { d.q = quals.length ? !quals.every(function (q) { return q(d); }) : false; });
  }
  function run() {
    var bar = doc.getElementById('searchbar');
    if (!bar || typeof window.run_search_scenarios !== 'function' || typeof window.applyVisibility !== 'function') return;
    // the qualifiers' flag hides a scenario as every other filter's does
    var show = window.applyVisibility;
    window.applyVisibility = function (c) {
      var r = show.apply(this, arguments);
      var any = false;
      (c && c.items || []).forEach(function (d) { if (d.q) { any = true; d.el.style.display = 'none'; } });
      // a rule or a feature whose scenarios a qualifier hid is hidden as the report hides it for its own filters
      if (any) {
        Array.prototype.forEach.call(doc.getElementsByClassName('rule'), function (r) {
          var seen = Array.prototype.some.call(r.getElementsByClassName('scenario'), function (s) { return s.style.display !== 'none'; });
          r.style.display = seen ? '' : 'none';
        });
        Array.prototype.forEach.call(c.features || [], function (f) {
          var seen = Array.prototype.some.call(f.getElementsByClassName('scenario'), function (s) { return s.style.display !== 'none'; });
          f.style.display = seen ? '' : 'none';
        });
      }
      return r;
    };
    var search = window.run_search_scenarios;
    window.run_search_scenarios = function () {
      var full = bar.value, parts = split(full);
      apply(parts.quals);
      bar.classList.toggle('kron-q-bad', parts.bad.length > 0);
      bar.setAttribute('data-q-bad', parts.bad.join(' '));
      if (!parts.quals.length) return search.apply(this, arguments);
      // the report's search reads the bar: for the length of the call it holds the search without the qualifiers
      var focused = doc.activeElement === bar, s = bar.selectionStart, e = bar.selectionEnd;
      bar.value = parts.rest;
      try { return search.apply(this, arguments); }
      finally {
        bar.value = full;
        if (focused) { try { bar.setSelectionRange(s, e); } catch (x) {} }
        if (typeof window.update_url_hash === 'function') window.update_url_hash();
      }
    };
    // the syntax, where the report explains its own
    var table = doc.querySelector('.search-help-table');
    if (table && !table.querySelector('.kron-q-row')) {
      [['dep:name', 'Scenarios that call a dependency: part of its name is enough; commas for any of several', 'dep:cow, dep:"cow service", dep:cow,goat'],
       ['duration:>2s', "Scenarios by their own time: >, >=, <, <= or a range; ms, s or m; p50, p90, p95, p99 are this report's percentiles", 'duration:>2s, duration:<500ms, duration:>p90, duration:1s..5s'],
       ['is:happy', 'Happy paths', 'is:happy, !!is:happy']].forEach(function (r) {
        var tr = doc.createElement('tr');
        tr.className = 'kron-q-row';
        var td = function (html) { var c = doc.createElement('td'); c.innerHTML = html; tr.appendChild(c); };
        td('<code>' + r[0] + '</code>');
        td(r[1]);
        td(r[2].split(', ').map(function (x) { return '<code>' + x.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;') + '</code>'; }).join(', '));
        table.appendChild(tr);
      });
      var note = doc.querySelector('.search-help-note');
      if (note && !note.querySelector('.kron-q-note')) {
        var p = doc.createElement('span');
        p.className = 'kron-q-note';
        p.textContent = ' dep:, duration: and is: always narrow the search: they combine with the rest of it by AND, and can be negated with !!.';
        note.appendChild(p);
      }
    }
    bar.setAttribute('placeholder', 'Search... (@tag, $status, dep:, duration:, &&, ||, !!)');
    // a search the page opened with (a link's q=) is run again with its qualifiers read
    if (bar.value && QRX.test(bar.value)) { QRX.lastIndex = 0; window.run_search_scenarios(); }
    QRX.lastIndex = 0;
  }
  window.__kronikolQuery = { split: split };
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 16 (owner): an expected/actual diff marks whole words and numbers, not letters. The report diffs the two
   strings character by character, so "HttpStatusCode.Gone {value: 410}" against "...NotFound {value: 404}" kept
   the o and n that Gone shares with NotFound and struck G, e and the 1 of 410. Each diff is drawn again from its
   own two strings, compared token by token: a run of letters or digits, a run of spaces, or one other character.
   (The report's ErrorDiffParser.GenerateDiffHtml does the character diff; the same change belongs there.) */
(function () {
  var doc = document;
  function tokens(s) { return s.match(/[A-Za-z0-9_]+|\s+|[^A-Za-z0-9_\s]/g) || []; }
  // which tokens of a and of b are common, by a longest common subsequence over tokens
  function common(a, b) {
    var m = a.length, n = b.length, dp = [], i, j;
    for (i = 0; i <= m; i++) { dp.push(new Int32Array(n + 1)); }
    for (i = m - 1; i >= 0; i--) for (j = n - 1; j >= 0; j--)
      dp[i][j] = a[i] === b[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    var inA = new Uint8Array(m), inB = new Uint8Array(n);
    for (i = 0, j = 0; i < m && j < n;) {
      if (a[i] === b[j]) { inA[i] = inB[j] = 1; i++; j++; }
      else if (dp[i + 1][j] >= dp[i][j + 1]) i++;
      else j++;
    }
    return [inA, inB];
  }
  function draw(code, toks, keep, cls) {
    code.textContent = '';
    var run = null;
    for (var i = 0; i < toks.length; i++) {
      if (keep[i]) { run = null; code.appendChild(doc.createTextNode(toks[i])); continue; }
      if (!run) { run = doc.createElement('span'); run.className = cls; code.appendChild(run); }
      run.appendChild(doc.createTextNode(toks[i]));
    }
  }
  function redraw(diff) {
    if (diff.__kTokens) return;
    diff.__kTokens = true;
    var e = diff.querySelector('.diff-expected code'), a = diff.querySelector('.diff-actual code');
    if (!e || !a) return;
    var te = tokens(e.textContent), ta = tokens(a.textContent);
    if (te.length * ta.length > 4e6) return; // a very long message keeps the report's own marks
    var c = common(te, ta);
    // spaces between two changed tokens belong to the change
    [[te, c[0]], [ta, c[1]]].forEach(function (p) {
      var t = p[0], k = p[1];
      for (var i = 1; i < t.length - 1; i++) if (k[i] && /^\s+$/.test(t[i]) && !k[i - 1] && !k[i + 1]) k[i] = 0;
    });
    draw(e, te, c[0], 'diff-del');
    draw(a, ta, c[1], 'diff-ins');
  }
  function run() {
    Array.prototype.forEach.call(doc.querySelectorAll('.error-diff'), redraw);
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
  window.__kronikolTokenDiff = { tokens: tokens, common: common };
})();
