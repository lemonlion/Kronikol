using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The line a capture writes after a document it capped is read by three readers: the page's note formatter (C#), the
/// page's YAML view (JavaScript) and the query tool. One definition serves the C# readers; these hold the page's script
/// and the writers to it (the #168 audit).
/// </summary>
public class CaptureFootnoteTests
{
    [Fact]
    public void The_pages_script_reads_the_footnote_by_the_same_pattern()
    {
        // The YAML view takes the footnote off before it rebuilds the JSON; a pattern that drifted from the C# one would
        // offer the toggle on one note and refuse it on the next.
        var script = DiagramContextMenu.GetCollapsibleNotesScript();
        var match = Regex.Match(script, @"var capFootnoteRegex = /\^(?<body>.+?)\$/;");

        Assert.True(match.Success, "collapsible-notes-script.js no longer declares capFootnoteRegex");
        Assert.Equal(CaptureFootnote.Pattern[@"\r?\n".Length..], match.Groups["body"].Value);
    }

    [Fact]
    public void A_sql_rows_body_the_formatter_capped_is_split_at_its_footnote()
    {
        var rows = new List<object> { new Dictionary<string, object?> { ["total"] = 5 }, new Dictionary<string, object?> { ["total"] = 7 } };
        var body = SqlRowFormatter.Format(SqlResponseDetail.FullRows, maxRows: 2, totalRows: 5, rows, columnNames: null);

        var split = CaptureFootnote.Split(body);

        Assert.NotNull(split);
        Assert.Equal("... (3 more rows not shown)", split.Value.Footnote);
        using var document = JsonDocument.Parse(split.Value.Document);
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("3 rows\n... (1 more rows not shown)")]
    [InlineData("first line\nsecond line\n... (3 more)")]
    public void A_text_body_is_not_split_whatever_its_last_line(string body)
    {
        // The page reads a footnote only after a JSON document (an object or an array).
        Assert.Null(CaptureFootnote.Split(body));
    }

    [Theory]
    [InlineData("[{ \"a\" : 1 }]\n... (3 more documents not shown)", "... (3 more documents not shown)")]
    [InlineData("{ \"updates\" : [] }\n... (2 more)", "... (2 more)")]
    [InlineData("  [1]\r\n... (12 more rows not shown)\n", "... (12 more rows not shown)")]
    public void A_document_is_split_at_each_footnote_a_writer_uses(string body, string footnote)
    {
        Assert.Equal(footnote, CaptureFootnote.Split(body)?.Footnote);
    }
}
