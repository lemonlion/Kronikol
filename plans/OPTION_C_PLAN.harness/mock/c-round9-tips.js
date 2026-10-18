
/* Round 9 (owner's note, 2026-10-18): a tooltip on every control, saying what it acts on. The note controls say
   whether they change every sequence diagram in the report (the bar) or one scenario's (its own row). Titles are
   set once the page is built, again when a tab opens, and for anything drawn later (a scenario's controls, a label
   that flips from Expand to Collapse) the moment the pointer or the keyboard reaches it. A title already on a
   control is kept unless the rule rewords it (force). */
(function () {
  var doc = document;
  function where(el) { return el.closest('.report-nav') ? 'every sequence diagram in the report' : "this scenario's sequence diagrams"; }
  function by(map, key) { return function (el) { return map[el.getAttribute(key)] || null; }; }
  function text(el) { return (el.textContent || '').replace(/\s+/g, ' ').trim(); }
  var STATUS = { Passed: 'passed', Failed: 'failed', Skipped: 'were skipped', Bypassed: 'were bypassed' };
  var SEG = {
    view: function (el) {
      var tl = !!el.closest('.scenario-timeline, #panel-timeline');
      return el.getAttribute('data-view') === 'full'
        ? (tl ? 'Every scenario in the run, whatever the filters' : 'Every component and call in the run, whatever the filters')
        : (tl ? 'Only the scenarios the filters show' : 'Only the components and calls of the scenarios the filters show');
    },
    warm: by({ shown: 'Bars and times include the first-call warm-up each scenario paid, marked in its bar', hidden: "Bars and times leave the warm-up out: each scenario's own time" }, 'data-warm'),
    order: by({ with: 'Slowest first, counting the warm-up', without: "Slowest first by each scenario's own time, warm-up left out" }, 'data-order'),
    dir: by({ lr: 'Draw the diagram from left to right', tb: 'Draw the diagram from top to bottom' }, 'data-dir'),
    calls: by({ shown: 'Show the call counts and methods on each connection', hidden: 'Hide the call counts and methods on the connections' }, 'data-calls')
  };
  // [selector, title (a string or a function of the element), force: replace a title already there, live: work it
  // out again each time, as the label or the state behind it changes]
  var RULES = [
    // the bar
    ['.report-tab[data-tab="features"]', 'The features and their scenarios, with the filters applied'],
    ['.report-tab[data-tab="timeline"]', 'Every scenario as a bar, slowest first, with the first-call warm-up it paid'],
    ['.report-tab[data-tab="components"]', 'The services, databases and queues the scenarios called, drawn as one diagram'],
    ['.report-tab[data-tab="history"]', 'This run against the earlier runs on the same branch: what is still failing, new, slower or gone'],
    ['.report-tab[data-tab="diagnostics"]', 'What Kronikol noticed while building this report, such as calls it could not put under a scenario'],
    ['.nav-readout', 'The scenarios and features the filters leave showing'],
    ['.nav-clear', 'Clear every filter: the search, statuses, duration, happy paths, tags and dependencies', true],
    ['.view-btn', function (el) { return /summary/i.test(text(el)) ? 'One table row per feature, with its counts and times' : 'Each feature as a card with its scenarios'; }],
    ['.sort-select', 'The order the features are listed in'],
    ['.collapse-expand-all', function (el) {
      var t = text(el);
      if (/^expand all features/i.test(t)) return 'Open every feature; the scenarios inside stay as they are';
      if (/^collapse all features/i.test(t)) return 'Close every feature';
      if (/^expand all scenarios/i.test(t)) return 'Open every scenario, with its steps and diagrams (a closed feature still hides its own)';
      if (/^collapse all scenarios/i.test(t)) return 'Close every scenario';
      return null;
    }, true, true],
    // how the payloads (PlantUML's notes) are drawn: in the bar for every diagram, in a scenario's own row for its diagrams
    ['.details-radio-btn[data-state="expanded"]', function (el) { return 'The payloads in ' + where(el) + ' show in full'; }, true],
    ['.details-radio-btn[data-state="collapsed"]', function (el) { return 'The payloads in ' + where(el) + " fold to one line each; a box's + button opens it"; }, true],
    ['.details-radio-btn[data-state="truncated"]', function (el) { return 'Payloads in ' + where(el) + ' longer than the line limit beside this are cut at that many lines (the default)'; }, true],
    ['.details-radio-label', function (el) { return 'How the payloads in ' + where(el) + ' are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit'; }],
    ['.truncate-lines-select', function (el) { return 'How many lines a cut payload box keeps in ' + where(el) + ' (with Truncated picked)'; }, true],
    ['.toggle-btn[data-toggle="headers"]', function (el) { return 'Show or hide the HTTP headers in the payloads of ' + where(el); }, true],
    ['.toggle-btn[data-toggle="assertions"]', function (el) { return "Show or hide the boxes with the test's assertions in " + where(el); }, true],
    ['.toggle-btn[data-toggle="steps"]', function (el) { return 'Show or hide the bars marking where each step starts in ' + where(el); }, true],
    ['.toggle-btn[data-toggle="databases"]', function (el) { return 'Show or hide the databases and their calls in ' + where(el); }, true],
    // round 16: shown only where a diagram has them
    ['.toggle-btn[data-toggle="events"]', function (el) { return 'Show or hide the messages to and from message brokers (Kafka, Event Hubs, Pub/Sub, Service Bus) and their payloads in ' + where(el); }, true],
    ['.toggle-btn[data-toggle="setup"]', function (el) { return 'Show or hide the setup in ' + where(el) + ': everything before the first When step, or the Setup section where the report keeps setup apart'; }, true],
    ['.note-format-select', function (el) { return 'How the payloads in ' + where(el) + ' are written: JSON as captured, or the same data as YAML'; }, true],
    ['.note-width-select', function (el) { return 'How wide the payloads are in ' + where(el) + '. Wrap: they wrap at a set width. Wide: they widen to fill the diagram'; }, true],
    // the filter box
    ['.filtering-box-export .export-btn[onclick*="clear_all_filters"]', 'Clear every filter: the search, statuses, duration, happy paths, tags and dependencies'],
    ['.filtering-box-export .export-btn[onclick*="export_html"]', 'Download a copy of this report holding only the features the filters show'],
    ['.filtering-box-export .export-btn[onclick*="export_csv"]', 'Download the scenarios the filters show as a CSV file: feature, scenario, status and duration'],
    ['.filter-readout', 'The scenarios and features the filters leave showing'],
    ['#searchbar', 'Search names, steps, tags and diagram text. Combine words with && (and), || (or) and !! (not); @tag and $status pick by tag or status, dep:, duration: and is:happy by dependency, time and happy path; ? shows the syntax'],
    ['.search-help-toggle', 'How to write a search: words, "phrases", @tags, $statuses, dep:, duration:, is:happy, &&, ||, !! and parentheses', true],
    ['.status-toggle[data-status]', function (el) { var s = STATUS[el.getAttribute('data-status')]; return s ? 'Show only the scenarios that ' + s + '; pick more statuses to show them together' : null; }],
    ['#duration-threshold', 'Seconds: scenarios taking at least this long, not counting first-call warm-up'],
    ['.happy-seg[data-hp]', by({ off: 'Happy-path scenarios and the rest', in: 'Only the scenarios tagged as happy paths', out: 'Leave out the scenarios tagged as happy paths' }, 'data-hp')],
    ['.tag-mode-toggle', 'OR: scenarios with any of the picked tags. AND: scenarios with all of them. Click to switch'],
    ['.tag-toggle[data-none]', 'Click: only scenarios with no tags. Click again: leave them out. Once more: off'],
    ['.tag-toggle', function (el) { return 'Click: only scenarios tagged ' + text(el) + '. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once'; }],
    ['.dependency-toggle', function (el) { return 'Click: only scenarios that call ' + text(el) + '. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once'; }],
    ['.group-clear', function (el) { return el.closest('.tag-filters') ? 'Clear the tag filters' : el.closest('.dependency-filters') ? 'Clear the dependency filters' : 'Clear these filters'; }],
    ['.filters-empty-clear', 'Clear every filter'],
    ['.mobile-filter-toggle', 'Show or hide the filters'],
    // the timeline and the component diagram
    ['.tl-seg-btn[data-view]', SEG.view],
    ['.tl-seg-btn[data-warm]', SEG.warm],
    ['.tl-seg-btn[data-order]', SEG.order],
    ['.tl-seg-btn[data-dir]', SEG.dir],
    ['.tl-seg-btn[data-calls]', SEG.calls],
    // a scenario
    ['.diagram-toggle-btn', function (el) {
      var t = text(el);
      if (/sequence/i.test(t)) return 'The calls between the test and the services, in order';
      if (/activity/i.test(t)) return 'The steps and the calls each made, as an activity diagram';
      if (/flame/i.test(t)) return 'Where the time went, call by call, as nested bars';
      return null;
    }],
    ['summary.h4', function (el) {
      var t = text(el);
      if (/^steps/i.test(t)) return "This scenario's steps, with how long each took";
      if (/^diagrams/i.test(t)) return "This scenario's diagrams";
      if (/^sequence diagrams/i.test(t)) return 'The sequence diagrams of this part of the scenario';
      if (/^failure result/i.test(t)) return 'Why the scenario failed: the error, with the expected and actual values';
      return null;
    }],
    // a step line that opens onto the steps inside it
    ['details.step-collapsible > summary', function (el) {
      var n = el.parentNode.querySelectorAll(':scope > .sub-steps > .step').length;
      return n ? (n === 1 ? 'This step has one step inside it' : 'This step has ' + n + ' steps inside it') + ': click to show or hide them' : null;
    }],
    ['a.step-attachment', function (el) { return 'Open ' + text(el) + ', a file this step attached'; }],
    ['summary', function (el) {
      var t = text(el);
      if (/^raw plant ?uml/i.test(t)) return 'The PlantUML source this diagram was drawn from';
      if (/^history\b/i.test(t)) return 'This run against the earlier runs on the same branch: click to show or hide the details';
      if (/^report diagnostics/i.test(t)) return 'What Kronikol noticed while building this report: click to show or hide the list';
      return null;
    }],
    ['a.history-link', 'Go to this scenario on the Features tab'],
    // the page
    ['.jump-to-failure', 'Go to the next failed scenario; the count says which one of how many'],
    ['.back-to-top', 'Back to the top of the report'],
    ['.run-link', 'Open the CI run this report came from']
  ];
  var SELECTOR = RULES.map(function (r) { return r[0]; }).join(',');

  function give(el, rule) {
    if (el.title && !rule[2] && !rule[3]) return;
    var t = typeof rule[1] === 'function' ? rule[1](el) : rule[1];
    if (t && el.title !== t) el.title = t;
  }
  function apply(root) {
    RULES.forEach(function (rule) { Array.prototype.forEach.call(root.querySelectorAll(rule[0]), function (el) { give(el, rule); }); });
  }
  // a box's buttons (PlantUML calls the box a note), drawn with its diagram: the report titles some (format, width), not the fold (one line), open
  // (two lines, a plus), more (a down triangle) and shorten (an up triangle) ones
  var SVGNS = 'http://www.w3.org/2000/svg';
  function noteButton(g) {
    if (g.querySelector('title')) return;
    var glyph = g.querySelector('text'), lines = g.querySelectorAll('line').length, t = null;
    if (glyph && /▲/.test(glyph.textContent)) t = 'Cut this box back to the line limit';
    else if (glyph && /▼/.test(glyph.textContent)) t = 'Show more of this box';
    else if (!glyph && lines === 2) t = 'Open this box in full';
    else if (!glyph && lines === 1) t = 'Fold this box to one line';
    if (!t) return;
    var title = doc.createElementNS(SVGNS, 'title');
    title.textContent = t;
    g.insertBefore(title, g.firstChild);
  }
  // drawn later, or a label that has flipped: worked out when the pointer or the keyboard gets there
  function reach(e) {
    var icon = e.target && e.target.closest ? e.target.closest('g.note-toggle-icon') : null;
    if (icon) { noteButton(icon); return; }
    var el = e.target && e.target.closest ? e.target.closest(SELECTOR) : null;
    if (!el) return;
    // a rule that rewords keeps its wording even where a script has put a title back since
    RULES.forEach(function (rule) { if (el.matches(rule[0]) && (rule[2] || rule[3] || !el.title)) give(el, rule); });
  }
  function run() {
    apply(doc);
    doc.addEventListener('pointerover', reach, true);
    doc.addEventListener('focusin', reach, true);
    doc.addEventListener('click', function (e) { if (e.target.closest && e.target.closest('.report-tab')) setTimeout(function () { apply(doc); }, 0); }, true);
    window.__kronikolTips = { apply: apply, rules: RULES };
  }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();
