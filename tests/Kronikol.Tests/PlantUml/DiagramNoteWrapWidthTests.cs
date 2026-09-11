using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// <c>DiagramNoteWrapWidth</c> — how wide a note body is drawn before PlantUML breaks it at a space.
/// <para>
/// Until now this was the hard-coded literal 800 in <c>PlantUmlCreator</c>. It is the only note-width
/// control that reaches <c>PlantUmlRendering.Local</c>, <c>NodeJs</c> and a copied-out source, because
/// the per-note width and monospace toggles are client-side and exist only in a <c>BrowserJs</c>
/// report. A wide analytics query is unreadable at 800 px whatever the renderer.
/// </para>
/// </summary>
public class DiagramNoteWrapWidthTests
{
    private static RequestResponseLog Request(string? content = null) =>
        new("Width test", "w-1", HttpMethod.Post, content, new Uri("http://example.com/api/orders"),
            [], "OrderService", "WebApp", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

    private static string Generate(int? wrapWidth = null) =>
        (wrapWidth is null
            ? PlantUmlCreator.GetPlantUmlImageTagsPerTestId([Request("SELECT 1")])
            : PlantUmlCreator.GetPlantUmlImageTagsPerTestId([Request("SELECT 1")], diagramNoteWrapWidth: wrapWidth.Value))
        .Single().PlantUmls.First().PlainText;

    [Fact]
    public void The_built_in_width_is_the_literal_every_report_shipped_with()
    {
        Assert.Contains("skinparam wrapWidth 800", Generate());
    }

    [Fact]
    public void A_configured_width_reaches_the_diagram()
    {
        Assert.Contains("skinparam wrapWidth 1600", Generate(1600));
    }

    [Fact]
    public void Passing_the_built_in_width_explicitly_changes_not_one_byte()
    {
        Assert.Equal(Generate(), Generate(800));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(719)]   // below the floor the form-body chunk arithmetic needs
    [InlineData(4097)]  // past PLANTUML_LIMIT_SIZE, where a raster render is cropped
    public void A_width_outside_the_supported_range_fails_generation_with_the_range_in_the_message(int width)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Generate(width));

        Assert.Contains("720", error.Message, StringComparison.Ordinal);
        Assert.Contains("4096", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(720)]
    [InlineData(4096)]
    public void Both_ends_of_the_supported_range_are_accepted(int width)
    {
        Assert.Contains($"skinparam wrapWidth {width}", Generate(width));
    }

    [Fact]
    public void The_floor_is_where_a_form_body_chunk_still_fits_on_one_drawn_line()
    {
        // MaxNoteChunkChars is 80 because 80 characters is about 720 pixels at the note font size, and
        // a form-url-encoded chunk that overflows wrapWidth is broken by the ENGINE — mid-line, which
        // can split one of Kronikol's own inline colour tags in half. Rather than derive the chunk size
        // from the option (and change the bytes of every form body at every non-default width), the
        // option is floored at the width that arithmetic already assumes.
        Assert.Equal(720, DiagramWidth.MinNoteWrapWidthPx);
        Assert.True(PlantUmlCreator.MaxNoteChunkCharsForTests * 9 <= DiagramWidth.MinNoteWrapWidthPx);
    }
}
