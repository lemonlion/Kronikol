using System.Text;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ParameterValueRendererTests
{
    #region Test types

    private record SimpleScalarRecord(string Region, int Amount, string Currency);

    private record SmallComplexRecord(string Name, string SortCode, string AccountNo);

    private record LargeComplexRecord(string A, string B, string C, string D, string E, string F);

    private record NestedRecord(string Name, SmallComplexRecord Address);

    private record RecordWithArray(string Name, string[] Items);

    private record RecordWithCollection(string Name, List<int> Scores);

    private enum TestEnum { ValueA, ValueB }

    private record RecordWithEnum(string Name, TestEnum Status);

    private record RecordWithNullable(string Name, int? Value);

    private record EmptyRecord;

    #endregion

    #region IsScalarType

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(double))]
    [InlineData(typeof(long))]
    public void IsScalarType_returns_true_for_scalar_types(Type type)
    {
        Assert.True(ParameterValueRenderer.IsScalarType(type));
    }

    [Fact]
    public void IsScalarType_returns_true_for_enum()
    {
        Assert.True(ParameterValueRenderer.IsScalarType(typeof(TestEnum)));
    }

    [Fact]
    public void IsScalarType_returns_true_for_nullable_int()
    {
        Assert.True(ParameterValueRenderer.IsScalarType(typeof(int?)));
    }

    [Fact]
    public void IsScalarType_returns_false_for_complex_type()
    {
        Assert.False(ParameterValueRenderer.IsScalarType(typeof(SimpleScalarRecord)));
    }

    [Fact]
    public void IsScalarType_returns_false_for_array()
    {
        Assert.False(ParameterValueRenderer.IsScalarType(typeof(string[])));
    }

    #endregion

    #region IsSmallComplexObject

    [Fact]
    public void IsSmallComplexObject_returns_true_for_record_with_3_scalar_props()
    {
        var obj = new SmallComplexRecord("Acme", "12-34-56", "12345678");
        Assert.True(ParameterValueRenderer.IsSmallComplexObject(obj));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_null()
    {
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(null));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_scalar()
    {
        Assert.False(ParameterValueRenderer.IsSmallComplexObject("hello"));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_too_many_properties()
    {
        var obj = new LargeComplexRecord("a", "b", "c", "d", "e", "f");
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(obj));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_nested_object()
    {
        var obj = new NestedRecord("Test", new SmallComplexRecord("Acme", "12-34-56", "12345678"));
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(obj));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_object_with_array()
    {
        var obj = new RecordWithArray("Test", ["a", "b"]);
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(obj));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_collection()
    {
        var obj = new RecordWithCollection("Test", [1, 2, 3]);
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(obj));
    }

    [Fact]
    public void IsSmallComplexObject_returns_false_for_empty_record()
    {
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(new EmptyRecord()));
    }

    #endregion

    #region TryGetFlattenableProperties

    [Fact]
    public void TryGetFlattenableProperties_returns_names_for_simple_record()
    {
        var obj = new SimpleScalarRecord("UK", 100, "GBP");
        var result = ParameterValueRenderer.TryGetFlattenableProperties(obj, 10);
        Assert.NotNull(result);
        Assert.Contains("Region", result!);
        Assert.Contains("Amount", result);
        Assert.Contains("Currency", result);
    }

    [Fact]
    public void TryGetFlattenableProperties_returns_null_for_scalar()
    {
        Assert.Null(ParameterValueRenderer.TryGetFlattenableProperties("hello", 10));
    }

    [Fact]
    public void TryGetFlattenableProperties_returns_null_for_null()
    {
        Assert.Null(ParameterValueRenderer.TryGetFlattenableProperties(null, 10));
    }

    [Fact]
    public void TryGetFlattenableProperties_returns_null_for_nested_properties()
    {
        var obj = new NestedRecord("Test", new SmallComplexRecord("Acme", "12-34-56", "12345678"));
        Assert.Null(ParameterValueRenderer.TryGetFlattenableProperties(obj, 10));
    }

    [Fact]
    public void TryGetFlattenableProperties_returns_null_when_exceeds_maxColumns()
    {
        var obj = new SimpleScalarRecord("UK", 100, "GBP");
        Assert.Null(ParameterValueRenderer.TryGetFlattenableProperties(obj, 2));
    }

    [Fact]
    public void TryGetFlattenableProperties_returns_null_for_array()
    {
        Assert.Null(ParameterValueRenderer.TryGetFlattenableProperties(new[] { 1, 2, 3 }, 10));
    }

    [Fact]
    public void TryGetFlattenableProperties_works_with_enum_properties()
    {
        var obj = new RecordWithEnum("Test", TestEnum.ValueA);
        var result = ParameterValueRenderer.TryGetFlattenableProperties(obj, 10);
        Assert.NotNull(result);
        Assert.Contains("Name", result!);
        Assert.Contains("Status", result);
    }

    [Fact]
    public void TryGetFlattenableProperties_works_with_nullable_properties()
    {
        var obj = new RecordWithNullable("Test", 42);
        var result = ParameterValueRenderer.TryGetFlattenableProperties(obj, 10);
        Assert.NotNull(result);
    }

    #endregion

    #region FlattenToStringValues

    [Fact]
    public void FlattenToStringValues_extracts_property_values()
    {
        var obj = new SimpleScalarRecord("UK", 100, "GBP");
        var result = ParameterValueRenderer.FlattenToStringValues(obj, ["Region", "Amount", "Currency"]);
        Assert.Equal("UK", result["Region"]);
        Assert.Equal("100", result["Amount"]);
        Assert.Equal("GBP", result["Currency"]);
    }

    [Fact]
    public void FlattenToStringValues_uses_null_string_for_null_properties()
    {
        var obj = new RecordWithNullable("Test", null);
        var result = ParameterValueRenderer.FlattenToStringValues(obj, ["Name", "Value"]);
        Assert.Equal("Test", result["Name"]);
        Assert.Equal("null", result["Value"]);
    }

    #endregion

    #region RenderSubTable

    [Fact]
    public void RenderSubTable_renders_correct_html()
    {
        var obj = new SmallComplexRecord("Acme Ltd", "12-34-56", "12345678");
        var sb = new StringBuilder();
        ParameterValueRenderer.RenderSubTable(sb, obj);
        var html = sb.ToString();

        Assert.Contains("<table class=\"cell-subtable\">", html);
        Assert.Contains("<th>Name</th><td>Acme Ltd</td>", html);
        Assert.Contains("<th>SortCode</th><td>12-34-56</td>", html);
        Assert.Contains("<th>AccountNo</th><td>12345678</td>", html);
        Assert.Contains("</table>", html);
    }

    [Fact]
    public void RenderSubTable_html_encodes_values()
    {
        var obj = new SmallComplexRecord("<script>", "a&b", "\"test\"");
        var sb = new StringBuilder();
        ParameterValueRenderer.RenderSubTable(sb, obj);
        var html = sb.ToString();

        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("a&amp;b", html);
        Assert.Contains("&quot;test&quot;", html);
    }

    [Fact]
    public void RenderSubTable_renders_null_property_values_as_null()
    {
        var obj = new RecordWithNullable("Test", null);
        var sb = new StringBuilder();
        ParameterValueRenderer.RenderSubTable(sb, obj);
        var html = sb.ToString();

        Assert.Contains("<th>Name</th><td>Test</td>", html);
        Assert.Contains("<th>Value</th><td>null</td>", html);
    }

    #endregion

    #region RenderExpandable

    [Fact]
    public void RenderExpandable_renders_details_summary()
    {
        var obj = new NestedRecord("Test Corp", new SmallComplexRecord("Acme", "12-34-56", "12345678"));
        var sb = new StringBuilder();
        ParameterValueRenderer.RenderExpandable(sb, obj);
        var html = sb.ToString();

        Assert.Contains("<details class=\"param-expand\">", html);
        Assert.Contains("<summary title=\"Show or hide the whole value\">", html);
        Assert.Contains("Name:", html);
        Assert.Contains("<div class=\"expand-body\">", html);
        Assert.Contains("</details>", html);
    }

    [Fact]
    public void RenderExpandable_contains_highlighted_json()
    {
        var obj = new SmallComplexRecord("Acme", "12-34-56", "12345678");
        var sb = new StringBuilder();
        ParameterValueRenderer.RenderExpandable(sb, obj);
        var html = sb.ToString();

        Assert.Contains("<span class=\"prop-key\">", html);
        Assert.Contains("<span class=\"prop-val\">", html);
    }

    #endregion

    #region GeneratePreview

    [Fact]
    public void GeneratePreview_shows_properties_without_type_name()
    {
        var obj = new SimpleScalarRecord("UK", 100, "GBP");
        var preview = ParameterValueRenderer.GeneratePreview(obj);
        Assert.DoesNotContain("SimpleScalarRecord", preview);
        Assert.Contains("Region: \"UK\"", preview);
        Assert.Contains("Amount: 100", preview);
        Assert.Contains("Currency: \"GBP\"", preview);
        Assert.StartsWith("{", preview);
    }

    [Fact]
    public void GeneratePreview_truncates_after_3_properties()
    {
        var obj = new LargeComplexRecord("a", "b", "c", "d", "e", "f");
        var preview = ParameterValueRenderer.GeneratePreview(obj);
        Assert.Contains("...", preview);
    }

    [Fact]
    public void GeneratePreview_nested_object_does_not_include_type_name()
    {
        var obj = new NestedRecord("Test Corp", new SmallComplexRecord("Acme", "12-34-56", "12345678"));
        var preview = ParameterValueRenderer.GeneratePreview(obj);
        Assert.DoesNotContain("SmallComplexRecord", preview);
        Assert.Contains("Name = Acme", preview);
        Assert.StartsWith("{", preview);
    }

    [Fact]
    public void GeneratePreview_string_array_shows_items_not_properties()
    {
        var arr = new[] { "A", "B", "C" };
        var preview = ParameterValueRenderer.GeneratePreview(arr);
        Assert.DoesNotContain("Length", preview);
        Assert.DoesNotContain("Rank", preview);
        Assert.Contains("\"A\"", preview);
        Assert.Contains("\"B\"", preview);
        Assert.Contains("\"C\"", preview);
        Assert.StartsWith("[", preview);
        Assert.EndsWith("]", preview);
    }

    [Fact]
    public void GeneratePreview_int_array_shows_items()
    {
        var arr = new[] { 1, 2, 3 };
        var preview = ParameterValueRenderer.GeneratePreview(arr);
        Assert.Contains("1", preview);
        Assert.Contains("2", preview);
        Assert.Contains("3", preview);
        Assert.StartsWith("[", preview);
    }

    [Fact]
    public void GeneratePreview_large_array_truncates()
    {
        var arr = Enumerable.Range(1, 15).Select(i => $"item{i}").ToArray();
        var preview = ParameterValueRenderer.GeneratePreview(arr);
        Assert.Contains("...", preview);
        Assert.Contains("\"item1\"", preview);
        Assert.StartsWith("[", preview);
    }

    [Fact]
    public void GeneratePreview_list_of_scalars_shows_items()
    {
        var list = new List<string> { "x", "y" };
        var preview = ParameterValueRenderer.GeneratePreview(list);
        Assert.Contains("\"x\"", preview);
        Assert.Contains("\"y\"", preview);
        Assert.StartsWith("[", preview);
    }

    [Fact]
    public void GeneratePreview_list_of_complex_objects_shows_count()
    {
        var list = new List<SmallComplexRecord>
        {
            new("Acme", "12-34-56", "12345678"),
            new("Beta", "65-43-21", "87654321"),
            new("Gamma", "11-22-33", "44556677")
        };
        var preview = ParameterValueRenderer.GeneratePreview(list);
        Assert.Contains("3", preview);
        Assert.Contains("items", preview);
    }

    [Fact]
    public void GeneratePreview_empty_array_shows_empty_brackets()
    {
        var arr = Array.Empty<string>();
        var preview = ParameterValueRenderer.GeneratePreview(arr);
        Assert.Equal("[]", preview);
    }

    #endregion

    #region GenerateHighlightedJson

    [Fact]
    public void GenerateHighlightedJson_null_renders_null()
    {
        var result = ParameterValueRenderer.GenerateHighlightedJson(null, 0);
        Assert.Equal("<span class=\"prop-val\">null</span>", result);
    }

    [Fact]
    public void GenerateHighlightedJson_string_renders_quoted()
    {
        var result = ParameterValueRenderer.GenerateHighlightedJson("hello", 0);
        Assert.Contains("\"hello\"", result);
        Assert.Contains("prop-val", result);
    }

    [Fact]
    public void GenerateHighlightedJson_object_renders_properties()
    {
        var obj = new SmallComplexRecord("Acme", "12-34-56", "12345678");
        var result = ParameterValueRenderer.GenerateHighlightedJson(obj, 0);
        Assert.Contains("prop-key", result);
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Acme\"", result);
    }

    [Fact]
    public void GenerateHighlightedJson_array_renders_items()
    {
        var arr = new[] { "a", "b", "c" };
        var result = ParameterValueRenderer.GenerateHighlightedJson(arr, 0);
        Assert.Contains("[", result);
        Assert.Contains("]", result);
        Assert.Contains("\"a\"", result);
    }

    [Fact]
    public void GenerateHighlightedJson_nested_object_renders_recursively()
    {
        var obj = new NestedRecord("Test", new SmallComplexRecord("Acme", "12-34-56", "12345678"));
        var result = ParameterValueRenderer.GenerateHighlightedJson(obj, 0);
        Assert.Contains("\"Name\"", result);
        Assert.Contains("\"Address\"", result);
        // Should contain nested object's properties
        Assert.Contains("\"SortCode\"", result);
    }

    #endregion

    #region String-based R3/R4

    [Fact]
    public void TryRenderFromParsedString_renders_subtable_for_small_record_string()
    {
        var body = new StringBuilder();
        var result = ParameterValueRenderer.TryRenderFromParsedString(body, "Risk { Score = 320, Band = E }");
        Assert.True(result);
        Assert.Contains("cell-subtable", body.ToString());
        Assert.Contains("Score", body.ToString());
        Assert.Contains("320", body.ToString());
        Assert.Contains("Band", body.ToString());
        Assert.Contains("E", body.ToString());
    }

    [Fact]
    public void TryRenderFromParsedString_renders_expandable_for_large_record_string()
    {
        var body = new StringBuilder();
        var value = "Scenario { A = 1, B = 2, C = 3, D = 4, E = 5, F = 6 }";
        var result = ParameterValueRenderer.TryRenderFromParsedString(body, value);
        Assert.True(result);
        Assert.Contains("param-expand", body.ToString());
        Assert.Contains("<summary title=\"Show or hide the whole value\">", body.ToString());
        Assert.Contains("prop-key", body.ToString());
        Assert.Contains("prop-val", body.ToString());
    }

    [Fact]
    public void TryRenderFromParsedString_returns_false_for_plain_string()
    {
        var body = new StringBuilder();
        var result = ParameterValueRenderer.TryRenderFromParsedString(body, "hello");
        Assert.False(result);
        Assert.Empty(body.ToString());
    }

    [Fact]
    public void TryRenderFromParsedString_returns_false_for_null()
    {
        var body = new StringBuilder();
        var result = ParameterValueRenderer.TryRenderFromParsedString(body, null);
        Assert.False(result);
    }

    [Fact]
    public void RenderSubTableFromParsed_html_encodes_values()
    {
        var body = new StringBuilder();
        var parsed = new Dictionary<string, string> { ["Name"] = "<script>alert(1)</script>" };
        ParameterValueRenderer.RenderSubTableFromParsed(body, parsed);
        Assert.DoesNotContain("<script>", body.ToString());
        Assert.Contains("&lt;script&gt;", body.ToString());
    }

    [Fact]
    public void RenderExpandableFromParsed_shows_preview_with_type_name()
    {
        var body = new StringBuilder();
        var original = "MyType { A = 1, B = 2, C = 3, D = 4, E = 5, F = 6 }";
        var parsed = new Dictionary<string, string>
        {
            ["A"] = "1", ["B"] = "2", ["C"] = "3", ["D"] = "4", ["E"] = "5", ["F"] = "6"
        };
        ParameterValueRenderer.RenderExpandableFromParsed(body, original, parsed);
        var html = body.ToString();
        Assert.Contains("<summary title=\"Show or hide the whole value\">", html);
        Assert.Contains("MyType", html);
        Assert.Contains("param-expand", html);
    }

    [Fact]
    public void GeneratePreviewFromParsed_truncates_after_3_properties()
    {
        var parsed = new Dictionary<string, string>
        {
            ["A"] = "1", ["B"] = "2", ["C"] = "3", ["D"] = "4"
        };
        var preview = ParameterValueRenderer.GeneratePreviewFromParsed("Obj { A = 1, B = 2, C = 3, D = 4 }", parsed);
        Assert.Contains("A: 1", preview);
        Assert.Contains("B: 2", preview);
        Assert.Contains("C: 3", preview);
        Assert.Contains("...", preview);
        Assert.DoesNotContain("D: 4", preview);
    }

    [Fact]
    public void GenerateHighlightedJsonFromParsed_renders_all_properties()
    {
        var parsed = new Dictionary<string, string> { ["Score"] = "320", ["Band"] = "E" };
        var json = ParameterValueRenderer.GenerateHighlightedJsonFromParsed(parsed);
        Assert.Contains("prop-key", json);
        Assert.Contains("\"Score\"", json);
        Assert.Contains("prop-val", json);
        Assert.Contains("320", json);
        Assert.Contains("\"Band\"", json);
        Assert.Contains("E", json);
    }

    #endregion

    #region Dictionary Support

    [Fact]
    public void IsSmallComplexObject_DictionaryWithScalarValues_ReturnsTrue()
    {
        var dict = new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 30 };
        Assert.True(ParameterValueRenderer.IsSmallComplexObject(dict));
    }

    [Fact]
    public void IsSmallComplexObject_DictionaryOverMaxProperties_ReturnsFalse()
    {
        var dict = new Dictionary<string, object?>
        {
            ["A"] = "1", ["B"] = "2", ["C"] = "3",
            ["D"] = "4", ["E"] = "5", ["F"] = "6"
        };
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(dict));
    }

    [Fact]
    public void IsSmallComplexObject_EmptyDictionary_ReturnsFalse()
    {
        var dict = new Dictionary<string, object?>();
        Assert.False(ParameterValueRenderer.IsSmallComplexObject(dict));
    }

    [Fact]
    public void IsComplexValue_DictionaryReturnsTrue()
    {
        var dict = new Dictionary<string, object?> { ["Key"] = "Value" };
        Assert.True(ParameterValueRenderer.IsComplexValue(dict));
    }

    [Fact]
    public void RenderSubTable_Dictionary_RendersKeyValuePairs()
    {
        var dict = new Dictionary<string, object?> { ["Flour"] = "Plain", ["Apples"] = "Granny Smith" };
        var sb = new System.Text.StringBuilder();

        ParameterValueRenderer.RenderSubTable(sb, dict);

        var html = sb.ToString();
        Assert.Contains("cell-subtable", html);
        Assert.Contains("<th>Flour</th>", html);
        Assert.Contains("<td>Plain</td>", html);
        Assert.Contains("<th>Apples</th>", html);
        Assert.Contains("<td>Granny Smith</td>", html);
    }

    [Fact]
    public void GeneratePreview_Dictionary_ShowsKeyValueFormat()
    {
        var dict = new Dictionary<string, object?> { ["Name"] = "Classic", ["Size"] = "Large" };
        var preview = ParameterValueRenderer.GeneratePreview(dict);

        Assert.Contains("Name:", preview);
        Assert.Contains("Classic", preview);
    }

    [Fact]
    public void GenerateHighlightedJson_Dictionary_RendersAsObject()
    {
        var dict = new Dictionary<string, object?> { ["Flour"] = "Plain" };
        var json = ParameterValueRenderer.GenerateHighlightedJson(dict, 0);

        Assert.Contains("\"Flour\"", json);
        Assert.Contains("\"Plain\"", json);
        Assert.Contains("prop-key", json);
        Assert.Contains("prop-val", json);
    }

    [Fact]
    public void GenerateHighlightedJson_ListOfDictionaries_RendersAsArrayOfObjects()
    {
        var list = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Streusel" },
            new() { ["Name"] = "Icing" }
        };
        var json = ParameterValueRenderer.GenerateHighlightedJson(list, 0);

        Assert.Contains("[", json);
        Assert.Contains("\"Streusel\"", json);
        Assert.Contains("\"Icing\"", json);
        Assert.Contains("prop-key", json);
    }

    [Fact]
    public void GeneratePreview_ListOfDictionaries_ShowsItemCount()
    {
        var list = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "A" },
            new() { ["Name"] = "B" }
        };
        var preview = ParameterValueRenderer.GeneratePreview(list);

        Assert.Equal("2 items", preview);
    }

    [Fact]
    public void TryGetFlattenableProperties_DictionaryWithScalars_ReturnKeys()
    {
        var dict = new Dictionary<string, object?> { ["Flour"] = "Plain", ["Apples"] = "Granny Smith" };
        var props = ParameterValueRenderer.TryGetFlattenableProperties(dict, 10);

        Assert.NotNull(props);
        Assert.Contains("Flour", props!);
        Assert.Contains("Apples", props);
    }

    [Fact]
    public void FlattenToStringValues_Dictionary_ReturnsStringDict()
    {
        var dict = new Dictionary<string, object?> { ["Flour"] = "Plain", ["Apples"] = "Granny Smith" };
        var result = ParameterValueRenderer.FlattenToStringValues(dict, ["Flour", "Apples"]);

        Assert.Equal("Plain", result["Flour"]);
        Assert.Equal("Granny Smith", result["Apples"]);
    }

    [Fact]
    public void FlattenToRawValues_Dictionary_ReturnsRawDict()
    {
        var dict = new Dictionary<string, object?> { ["Flour"] = "Plain", ["Apples"] = "Granny Smith" };
        var result = ParameterValueRenderer.FlattenToRawValues(dict, ["Flour", "Apples"]);

        Assert.Equal("Plain", result["Flour"]);
        Assert.Equal("Granny Smith", result["Apples"]);
    }

    #endregion
}
