using AngleSharp.Dom;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The note a tab that follows the filters writes while they leave scenarios out (the Timeline's and the Components
/// tab's): its sentence, empty until the page writes it, the part a screen reader announces; and straight after it the
/// way back, Clear All, as where no scenario is left (5.1.0).
/// </summary>
internal static class FilteredNoteMarkup
{
    public static void AssertEmptyWithClearAll(IElement note)
    {
        Assert.Null(note.GetAttribute("role"));
        var children = note.Children.ToArray();
        Assert.Equal(["span", "button"], children.Select(c => c.LocalName));

        var text = children[0];
        Assert.Contains("filtered-note-text", text.ClassList);
        Assert.Equal("status", text.GetAttribute("role"));
        Assert.Equal("", text.TextContent);

        var clear = children[1];
        Assert.Contains("filtered-note-clear", clear.ClassList);
        Assert.Equal("button", clear.GetAttribute("type"));
        Assert.Equal("Clear every filter", clear.GetAttribute("title"));
        Assert.Equal("Clear All", clear.TextContent);
    }
}
