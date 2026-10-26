using System.Reflection;
using System.Text.Json;
using Jint;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// A scenario of <see cref="SearchPage"/>'s report: what the generator writes on its element (its tags as the Tags row
/// carries them, its dependencies, status, own time and history verdicts) and the text the instant search reads.
/// </summary>
public sealed record PageScenario(
    string Name,
    string Status,
    string[] Tags,
    string[] Deps,
    string[] Steps,
    bool Happy = false,
    int? DurationMs = null,
    int WarmUpMs = 0,
    string? Error = null,
    string[]? Verdicts = null);

/// <summary>A feature of <see cref="SearchPage"/>'s report: its name, the endpoint and tags drawn after it on its card.</summary>
public sealed record PageFeature(string Name, string? Endpoint, string[] Labels, PageScenario[] Scenarios);

/// <summary>
/// The report's search as the page runs it (SEARCH_PLAN 4.4, S3): the shipped <c>advanced-search.js</c>,
/// <c>report-scenario-feature-map-helper.js</c> and <c>report-search-function.js</c> under Jint, over a page of stub
/// elements carrying the attributes the generator writes, so a query goes through <c>run_search_scenarios</c>, its
/// qualifiers, the advanced grammar or the legacy path, and the filter cache's flags, exactly as in a browser. What it
/// keeps is what <c>applyVisibility</c> leaves showing. The Jint <c>document</c> of the other facts is a bare stub
/// (<c>SearchQualifierPlacementTests</c>), which reaches the qualifiers only. <c>verdicts</c> loads the variant a
/// report drawn with <c>ShowScenarioHistory</c> ships (the raw scripts, whose fences are comments), and without it the
/// default variant every other report ships.
/// </summary>
public sealed class SearchPage : IDisposable
{
    private readonly Engine _engine = new();

    public SearchPage(IReadOnlyList<PageFeature> features, bool verdicts = true, IReadOnlyDictionary<string, double>? percentiles = null)
    {
        _engine.Execute(DomStub);
        _engine.SetValue("__pageJson", JsonSerializer.Serialize(new
        {
            features = features.Select(f => new
            {
                name = f.Name,
                endpoint = f.Endpoint,
                labels = f.Labels,
                scenarios = f.Scenarios.Select(s => new
                {
                    attrs = Attributes(f, s, verdicts),
                    happy = s.Happy
                })
            }),
            percentiles = percentiles ?? new Dictionary<string, double>()
        }));
        _engine.Execute("__buildPage(JSON.parse(__pageJson));");
        foreach (var script in new[] { "advanced-search.js", "report-scenario-feature-map-helper.js", "report-search-function.js" })
            _engine.Execute(verdicts ? Raw(script) : DefaultVariant(script));
    }

    /// <summary>The searchable text the generator writes for a scenario: its feature, its name, its tags, its error, its
    /// steps and the names of the dependencies its diagrams draw, lowered.</summary>
    public static string SearchText(PageFeature feature, PageScenario s) =>
        string.Join(" ", new[] { feature.Name, s.Name }
            .Concat(s.Tags.Where(t => t != "Happy Path"))
            .Concat(s.Error is null ? [] : [s.Error])
            .Concat(s.Steps)
            .Concat(s.Deps)).ToLowerInvariant();

    private static Dictionary<string, string> Attributes(PageFeature f, PageScenario s, bool verdicts)
    {
        var attrs = new Dictionary<string, string>
        {
            ["data-status"] = s.Status,
            ["data-search"] = SearchText(f, s),
            ["data-tags"] = string.Join(",", s.Tags.Select(Uri.EscapeDataString)),
            ["data-stable-id"] = s.Name
        };
        if (s.Deps.Length > 0)
            attrs["data-dependencies"] = string.Join(",", s.Deps.OrderBy(d => d, StringComparer.Ordinal));
        if (s.DurationMs is { } ms)
            attrs["data-duration-ms"] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (s.WarmUpMs > 0)
            attrs["data-warmup-ms"] = s.WarmUpMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (verdicts && s.Verdicts is { Length: > 0 })
            attrs["data-history-verdicts"] = string.Join(",", s.Verdicts);
        return attrs;
    }

    /// <summary>What the search leaves showing, and how the search field is marked.</summary>
    public sealed record Outcome(string[] Kept, bool Bad, string? BadText, string? DeepInput);

    /// <summary>Types <paramref name="query"/> as the search field holds it and runs the search the page runs.</summary>
    public Outcome Search(string query)
    {
        _engine.SetValue("__query", query);
        var json = _engine.Evaluate("__search(__query)").AsString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new Outcome(
            root.GetProperty("kept").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            root.GetProperty("bad").GetBoolean(),
            root.GetProperty("badText").ValueKind == JsonValueKind.Null ? null : root.GetProperty("badText").GetString(),
            root.GetProperty("deep").ValueKind == JsonValueKind.Null ? null : root.GetProperty("deep").GetString());
    }

    /// <summary>The names the search keeps, in page order.</summary>
    public string[] Kept(string query) => Search(query).Kept;

    public Engine Engine => _engine;

    internal static string Raw(string name)
    {
        var assembly = typeof(Reports.ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)!);
        return reader.ReadToEnd();
    }

    internal static string DefaultVariant(string name)
    {
        // ReportGenerator.LoadScript is internal; this project is not a friend assembly of Kronikol.
        var loadScript = typeof(Reports.ReportGenerator).GetMethod("LoadScript", BindingFlags.NonPublic | BindingFlags.Static)
                         ?? throw new InvalidOperationException("ReportGenerator.LoadScript not found");
        return (string)loadScript.Invoke(null, [name, false])!;
    }

    // Elements answer what the three scripts ask of them: attributes, classes, the scenarios of a feature, a feature's
    // summary (its own text, then the endpoint and tag elements the generator writes after it), and style.display.
    private const string DomStub = """
        function __el(attrs, classes) {
            var el = {
                _a: attrs || {}, _c: classes || [], _kids: [], _summary: null, style: {}, value: '',
                getAttribute: function (n) { return Object.prototype.hasOwnProperty.call(this._a, n) ? this._a[n] : null; },
                hasAttribute: function (n) { return Object.prototype.hasOwnProperty.call(this._a, n); },
                setAttribute: function (n, v) { this._a[n] = String(v); },
                removeAttribute: function (n) { delete this._a[n]; },
                getElementsByClassName: function (c) { return this._kids.filter(function (k) { return k._c.indexOf(c) >= 0; }); },
                querySelector: function (s) { return s === 'summary' || s === ':scope > summary' ? this._summary : null; },
                querySelectorAll: function () { return []; },
                closest: function () { return null; },
                appendChild: function () { }
            };
            el.classList = {
                contains: function (c) { return el._c.indexOf(c) >= 0; },
                add: function (c) { if (el._c.indexOf(c) < 0) el._c.push(c); },
                remove: function (c) { el._c = el._c.filter(function (x) { return x !== c; }); },
                toggle: function (c, on) { if (on === undefined) on = el._c.indexOf(c) < 0; if (on) el.classList.add(c); else el.classList.remove(c); return on; }
            };
            return el;
        }
        // a run of sibling nodes: text nodes (3) and elements (1)
        function __nodes(list) {
            var first = null, prev = null;
            list.forEach(function (n) { if (prev) prev.nextSibling = n; else first = n; prev = n; });
            return first;
        }
        var __features = [], __scenarios = [], __durations = null, __deep = null;
        function __buildPage(page) {
            page.features.forEach(function (f) {
                var feature = __el({}, ['feature']);
                var parts = [{ nodeType: 3, nodeValue: f.name, nextSibling: null }];
                if (f.endpoint) parts.push({ nodeType: 3, nodeValue: ' ', nextSibling: null }, { nodeType: 1, textContent: f.endpoint, nextSibling: null });
                f.labels.forEach(function (l) { parts.push({ nodeType: 3, nodeValue: ' ', nextSibling: null }, { nodeType: 1, textContent: l, nextSibling: null }); });
                feature._summary = { firstChild: __nodes(parts), textContent: parts.map(function (p) { return p.nodeType === 3 ? p.nodeValue : p.textContent; }).join('') };
                f.scenarios.forEach(function (s) {
                    var scenario = __el(s.attrs, s.happy ? ['scenario', 'happy-path'] : ['scenario']);
                    feature._kids.push(scenario);
                    __scenarios.push(scenario);
                });
                __features.push(feature);
            });
            var p = page.percentiles, attrs = {}, any = false;
            for (var k in p) { attrs['data-' + k] = String(p[k]); any = true; }
            if (any) __durations = __el(attrs, ['duration-filters']);
        }
        var __bar = __el({ id: 'searchbar' }, []);
        var document = {
            getElementsByClassName: function (c) { return c === 'feature' ? __features : c === 'scenario' ? __scenarios : []; },
            getElementById: function (id) { return id === 'searchbar' ? __bar : null; },
            querySelector: function (s) { return s === '.duration-filters' ? __durations : null; },
            querySelectorAll: function () { return []; },
            addEventListener: function () { },
            dispatchEvent: function () { },
            createElement: function () { return __el(); },
            createTextNode: function () { return {}; }
        };
        var window = { _kronDeepQuery: function (input) { __deep = input; }, _kronDeepReset: function () { __deep = null; } };
        function CustomEvent(type, init) { this.type = type; this.detail = init && init.detail; }
        function update_url_hash() { }
        function __search(query) {
            __bar.value = query;
            __deep = null;
            run_search_scenarios();
            return JSON.stringify({
                kept: __scenarios.filter(function (s) { return s.style.display !== 'none'; }).map(function (s) { return s.getAttribute('data-stable-id'); }),
                bad: __bar.classList.contains('kron-q-bad'),
                badText: __bar.getAttribute('data-q-bad'),
                deep: __deep
            });
        }
        """;

    public void Dispose() => _engine.Dispose();
}
