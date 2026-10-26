using Jint;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// <c>searchQuerySpans</c> (SEARCH_PLAN 4.4, L9): the query cut into spans, which the query builder colours (a key
/// plain, a value in the accent, an operator purple, a value the search cannot read in the warning colour) and reads to
/// find the token at the caret. The spans run in order and touch, from 0 to the end of the query, so the builder can
/// draw them one after another. Rendered here as <c>kind.key[text]</c>, the spaces between words left out. A span's
/// reading is the search's: an unreadable qualifier is the one the search function marks the field for, and a word is
/// an operator only where the search reads it as one (<c>not</c> before a qualifier, <c>and</c>, <c>or</c> and
/// <c>not</c> in a legacy tag expression).
/// </summary>
public class SearchQuerySpansTests : IDisposable
{
    private readonly Engine _engine = new();

    public SearchQuerySpansTests()
    {
        foreach (var resource in new[] { "advanced-search.js", "report-search-function.js" })
            _engine.Execute(SearchPage.Raw(resource));
        // the search function's split reads the percentiles from the page; there are none here
        _engine.Execute("var document = { querySelector: function () { return null; } };");
        _engine.Execute("""
            function __opts(json) { return json ? JSON.parse(json) : undefined; }
            function __render(text, json) {
                return searchQuerySpans(text, __opts(json))
                    .filter(function (s) { return !(s.kind === 'text' && /^\s+$/.test(text.slice(s.start, s.end))); })
                    .map(function (s) { return s.kind + (s.key ? '.' + s.key : '') + '[' + text.slice(s.start, s.end) + ']'; }).join(' ');
            }
            function __check(text, json) {
                var spans = searchQuerySpans(text, __opts(json)), at = 0, kinds = { text: 1, phrase: 1, key: 1, value: 1, operator: 1, unreadable: 1 };
                for (var i = 0; i < spans.length; i++) {
                    var s = spans[i];
                    if (s.start !== at) return 'span ' + i + ' starts at ' + s.start + ', not ' + at;
                    if (!(s.end > s.start)) return 'span ' + i + ' is empty';
                    if (!kinds[s.kind]) return 'span ' + i + ' is a ' + s.kind;
                    at = s.end;
                }
                return at === text.length ? '' : 'the spans end at ' + at + ' of ' + text.length;
            }
            // what the search function marks the field for, and the unreadable spans' qualifiers, each as written
            function __marked(text) { return split_search_qualifiers(text).bad.join(' | '); }
            // a qualifier as written: its unreadable value back to the first span of its key, the spaces between passed over
            function __unreadable(text) {
                var spans = searchQuerySpans(text, { statuses: [] }), out = [];
                for (var i = 0; i < spans.length; i++) {
                    if (spans[i].kind !== 'unreadable') continue;
                    var from = spans[i].start;
                    for (var j = i - 1; j >= 0; j--) {
                        if (spans[j].key === spans[i].key && spans[j].kind !== 'value' && spans[j].kind !== 'unreadable') from = spans[j].start;
                        else if (!(spans[j].kind === 'text' && /^\s+$/.test(text.slice(spans[j].start, spans[j].end)))) break;
                    }
                    out.push(text.slice(from, spans[i].end));
                }
                return out.join(' | ');
            }
            """);
    }

    private string Render(string text, string opts = "") => _engine.Invoke("__render", text, opts).AsString();

    [Theory]
    // words, phrases, operators and brackets
    [InlineData("order", "", "text[order]")]
    [InlineData("\"delete order\"", "", "phrase[\"delete order\"]")]
    [InlineData("order && !!delete", "", "text[order] operator[&&] operator[!!] text[delete]")]
    [InlineData("(payment || order) && create", "", "operator[(] text[payment] operator[||] text[order] operator[)] operator[&&] text[create]")]
    [InlineData("does not exist", "", "text[does] text[not] text[exist]")]
    [InlineData("order && not", "", "text[order] operator[&&] text[not]")]
    [InlineData("   order   ", "", "text[order]")]
    [InlineData("", "", "")]
    // @tag and $status
    [InlineData("@smoke", "", "key.tag[@] value.tag[smoke]")]
    [InlineData("@\"Happy Path\"", "", "key.tag[@] value.tag[\"Happy Path\"]")]
    [InlineData("$failed", "", "key.status[$] value.status[failed]")]
    [InlineData("@\"\"", "", "text[@\"\"]")]
    // tag: and status:
    [InlineData("tag:smoke", "", "key.tag[tag:] value.tag[smoke]")]
    [InlineData("TAG:\"two words\"", "", "key.tag[TAG:] value.tag[\"two words\"]")]
    [InlineData("-tag:slow && status:failed", "", "operator.tag[-] key.tag[tag:] value.tag[slow] operator[&&] key.status[status:] value.status[failed]")]
    [InlineData("!!tag:slow", "", "operator[!!] key.tag[tag:] value.tag[slow]")]
    [InlineData("(-tag:api) && $passed", "", "operator[(] operator.tag[-] key.tag[tag:] value.tag[api] operator[)] operator[&&] key.status[$] value.status[passed]")]
    [InlineData("pancake -status:failed", "", "text[pancake] operator.status[-] key.status[status:] value.status[failed]")]
    [InlineData("tag:x&&y", "", "key.tag[tag:] value.tag[x] operator[&&] text[y]")]
    [InlineData("\"a\"tag:b", "", "phrase[\"a\"] key.tag[tag:] value.tag[b]")]
    [InlineData("not tag:x", "", "text[not] key.tag[tag:] value.tag[x]")]
    [InlineData("status:\"failed\"", "", "key.status[status:] value.status[\"failed\"]")]
    // a status name: any is read until the report's statuses are given; the execution results always are
    [InlineData("status:nonsense", "", "key.status[status:] value.status[nonsense]")]
    [InlineData("status:nonsense", "{\"statuses\":[]}", "key.status[status:] unreadable.status[nonsense]")]
    [InlineData("-status:nonsense", "{\"statuses\":[]}", "text.status[-] key.status[status:] unreadable.status[nonsense]")]
    [InlineData("status:flaky", "{\"statuses\":[\"flaky\"]}", "key.status[status:] value.status[flaky]")]
    [InlineData("status:Failed", "{\"statuses\":[]}", "key.status[status:] value.status[Failed]")]
    [InlineData("status:\"x y\"", "{\"statuses\":[]}", "key.status[status:] unreadable.status[\"x y\"]")]
    [InlineData("status:400 @kitchen or @ui", "{\"statuses\":[]}", "key.status[status:] unreadable.status[400] key.tag[@] value.tag[kitchen] operator[or] key.tag[@] value.tag[ui]")]
    [InlineData("status:400 @kitchen or @ui", "", "key.status[status:] value.status[400] key.tag[@] value.tag[kitchen] text[or] key.tag[@] value.tag[ui]")]
    // a key with no value: a key, for the builder to complete; a tag needs a name
    [InlineData("tag:", "", "key.tag[tag:]")]
    [InlineData("-tag:", "", "text.tag[-] key.tag[tag:]")]
    [InlineData("dep:", "", "key.dep[dep:]")]
    [InlineData("status: 400", "", "key.status[status:] text[400]")]
    [InlineData("tag:\"\"", "", "key.tag[tag:] unreadable.tag[\"\"]")]
    // the search function's qualifiers
    [InlineData("dep:payment", "", "key.dep[dep:] value.dep[payment]")]
    [InlineData("dependency:\"order service\"", "", "key.dep[dependency:] value.dep[\"order service\"]")]
    [InlineData("dep:payment,order", "", "key.dep[dep:] value.dep[payment,order]")]
    [InlineData("dep:\"\"", "", "key.dep[dep:] unreadable.dep[\"\"]")]
    [InlineData("duration:>2s", "", "key.duration[duration:] value.duration[>2s]")]
    [InlineData("duration:1s..5s", "", "key.duration[duration:] value.duration[1s..5s]")]
    [InlineData("duration:abc", "", "key.duration[duration:] unreadable.duration[abc]")]
    [InlineData("duration:>=p90", "", "key.duration[duration:] value.duration[>=p90]")]
    [InlineData("duration:>=p90", "{\"percentiles\":{\"p90\":3000}}", "key.duration[duration:] value.duration[>=p90]")]
    [InlineData("duration:>=p90", "{\"percentiles\":{\"p50\":1000}}", "key.duration[duration:] unreadable.duration[>=p90]")]
    [InlineData("is:happy", "", "key.is[is:] value.is[happy]")]
    [InlineData("is:xyz", "", "key.is[is:] unreadable.is[xyz]")]
    [InlineData("feature:\"Pancakes Creation\"", "", "key.feature[feature:] value.feature[\"Pancakes Creation\"]")]
    [InlineData("-dep:payment", "", "operator.dep[-] key.dep[dep:] value.dep[payment]")]
    [InlineData("!!dep:payment", "", "operator.dep[!!] key.dep[dep:] value.dep[payment]")]
    [InlineData("not dep:payment", "", "operator.dep[not] key.dep[dep:] value.dep[payment]")]
    [InlineData("!!(dep:payment)", "", "operator[!!] operator[(] key.dep[dep:] unreadable.dep[payment] operator[)]")]
    [InlineData("!!duration:fast", "", "operator.duration[!!] key.duration[duration:] unreadable.duration[fast]")]
    [InlineData("-duration:fast", "", "text.duration[-] key.duration[duration:] unreadable.duration[fast]")]
    [InlineData("\"order dep:payment\"", "", "phrase[\"order dep:payment\"]")]
    [InlineData("order || dep:payment", "", "text[order] operator[||] key.dep[dep:] value.dep[payment]")]
    [InlineData("$failed dep:payment duration:>1s", "", "key.status[$] value.status[failed] key.dep[dep:] value.dep[payment] key.duration[duration:] value.duration[>1s]")]
    // a - before a word that is no qualifier is part of it
    [InlineData("-pancake", "", "text[-pancake]")]
    [InlineData("a-tag:x", "", "text[a-tag:x]")]
    // legacy tag expressions: and, or, not and the brackets against a tag are operators; a tag keeps the brackets it closes
    [InlineData("not @slow", "", "operator[not] key.tag[@] value.tag[slow]")]
    [InlineData("(@ui or @api) and not @slow", "", "operator[(] key.tag[@] value.tag[ui] operator[or] key.tag[@] value.tag[api] operator[)] operator[and] operator[not] key.tag[@] value.tag[slow]")]
    [InlineData("@issue(12) and @api", "", "key.tag[@] value.tag[issue(12)] operator[and] key.tag[@] value.tag[api]")]
    [InlineData("@smoke and dep:payment", "", "key.tag[@] value.tag[smoke] operator[and] key.dep[dep:] value.dep[payment]")]
    [InlineData("getOrder(id) @smoke", "", "text[getOrder(id)] key.tag[@] value.tag[smoke]")]
    public void searchQuerySpans_cuts_keys_values_operators_and_unreadable_qualifiers(string text, string opts, string spans)
    {
        Assert.Equal(spans, Render(text, opts));
    }

    // Every query the facts above and the wiki write, and some broken ones: the spans touch from 0 to the end
    public static TheoryData<string> Corpus()
    {
        var data = new TheoryData<string>();
        foreach (var q in new[]
                 {
                     "", " ", "\t", "\"", "\"\"", "\"unclosed", "@", "$", "@\"", "@\"unclosed", "(((", ")))", "&&&", "|||", "!!!", "&", "|", "!",
                     "-", "--", "not", "and", "or", "a\nb", "tag:\"unclosed", "dep:\"", "dep:\"x", "duration:1s..", "is:", "status:-", "status:\"",
                     ")tag:x", "&&tag:x", "!!-tag:x", "-!!tag:x", "tag:tag:x", "status:status:x", "@tag:x", "$status:failed", "支付 dep:支付 tag:支付",
                     "order (dep:payment || tag:x)", "not  dep:x", "!! dep:x", "x dep:a dep:b -dep:c", "\"a\" \"b\" c", "(order && create",
                     "tag:a)tag:b", "@a@b", "$a$b", "tag:\"a\"b", "status:failed&&tag:x||@y", " -tag:x ", "-status:\"x y\"", "feature:\"\"",
                 })
            data.Add(q);
        foreach (var q in SearchCompatibilityTests.Queries)
            data.Add(q);
        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_spans_touch_from_the_start_of_the_query_to_its_end(string text)
    {
        Assert.Equal("", _engine.Invoke("__check", text, "").AsString());
        Assert.Equal("", _engine.Invoke("__check", text, "{\"statuses\":[],\"percentiles\":{}}").AsString());
    }

    // The builder's warning colour and the field's amber edge never disagree: a qualifier is unreadable in the spans
    // exactly where the search function marks the field for it
    [Theory]
    [InlineData("duration:fast")]
    [InlineData("duration:\"\" order")]
    [InlineData("is:sad && dep:payment")]
    [InlineData("!!(dep:payment)")]
    [InlineData("@smoke && !!(dep:a || @b)")]
    [InlineData("feature:\"\"")]
    [InlineData("status:400")]
    [InlineData("-status:nonsense pancake")]
    [InlineData("status:\"x y\"")]
    [InlineData("tag:\"\"")]
    [InlineData("tag:smoke status:failed dep:payment -feature:x is:happy duration:>2s")]
    [InlineData("\"is:sad\" order")]
    [InlineData("duration:fast duration:slow")]
    [InlineData("not is:sad")]
    [InlineData("!! duration:fast")]
    [InlineData("-duration:fast")]
    public void A_qualifier_is_unreadable_where_the_search_marks_the_field_for_it(string text)
    {
        Assert.Equal(_engine.Invoke("__marked", text).AsString(), _engine.Invoke("__unreadable", text).AsString());
    }

    public void Dispose() => _engine.Dispose();
}
