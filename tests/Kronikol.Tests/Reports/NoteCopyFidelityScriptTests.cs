using Kronikol.PlantUml;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Structural guards for the client half of NOTE_COPY_FIDELITY_PLAN. The behaviour is covered in the
/// Playwright suite (<c>NoteCopyFidelityTests</c>), which reads the real clipboard; these pin the
/// wiring, because the failure mode here is silent — a copy path that skips the rejoin still copies
/// something, just with the token in pieces.
/// </summary>
public class NoteCopyFidelityScriptTests
{
    private readonly string _notes = DiagramContextMenu.GetCollapsibleNotesScript();
    private readonly string _menu = DiagramContextMenu.GetContextMenuScript();

    [Fact]
    public void The_rejoin_is_exported_for_the_context_menu_to_use()
    {
        // The context menu is a separate script: it can only reach the rejoin through window.
        Assert.Contains("window._rejoinWrappedNoteLines", _notes);
        Assert.Contains("window._stripZeroWidth", _notes);
        Assert.Contains("window._rejoinWrappedNoteLines", _menu);
    }

    [Fact]
    public void Every_copy_path_goes_through_the_one_rejoin()
    {
        // noteText, currentText and extractCallerPayloads all build their text with noteLinesToText,
        // which is the only place the strip/rejoin/unescape order is written down. Copy box text and
        // Open box text in new tab share those two strings, so wiring them is wiring four menu items.
        Assert.Contains("function noteLinesToText(", _menu);
        // One definition plus a call for each branch of the three text builders: noteText (YAML
        // header / plain), currentText (YAML header / YAML body / plain) and extractCallerPayloads.
        Assert.Equal(7, CountOccurrences(_menu, "noteLinesToText("));
    }

    [Fact]
    public void Text_read_out_of_the_painted_svg_is_stripped_of_the_resolved_marker()
    {
        // The engine resolves the marker to a real zero-width space, so it IS in the DOM. Anything
        // reading note text from there rather than from the source has to drop it.
        Assert.Contains("function paintedNoteText(", _menu);
        Assert.Contains("_stripZeroWidth", _menu);
        // Exactly once, inside paintedNoteText: any OTHER place reading the painted text straight
        // into a copy target would ship the resolved marker to the clipboard.
        Assert.Equal(1, CountOccurrences(_menu, "texts.map(function(t) { return t.textContent; }).join('\\n')"));
    }

    [Fact]
    public void The_yaml_reconstructor_rejoins_before_it_unescapes()
    {
        // Order is load-bearing: unescaping first turns a payload's own ~<U+200B> into a bare marker,
        // and the rejoin then eats a real newline.
        var rejoin = _notes.IndexOf("text = rejoinWrappedNoteLines(text);", StringComparison.Ordinal);
        var unescape = _notes.IndexOf("text = decodeNoteEscapes(text);", StringComparison.Ordinal);

        Assert.True(rejoin > 0, "reconstructNoteJson must rejoin");
        Assert.True(unescape > 0, "reconstructNoteJson must unescape");
        Assert.True(rejoin < unescape, "the rejoin must run BEFORE the creole unescape");
    }

    [Fact]
    public void The_browser_and_the_generator_agree_on_the_markers()
    {
        // Four implementations of the same three rules exist because nothing can be shared across the
        // C#/browser/bench boundary. The shared vectors pin the search pair; this pins the other two.
        Assert.Contains("'<U+' + '200B>'", _notes);
        Assert.Equal("<U+200B>", DiagramWidth.JoinMarker);
        Assert.Contains("NOTE_JOIN_SPACE_MARKER = NOTE_JOIN_MARKER + NOTE_JOIN_MARKER", _notes);
        Assert.Equal(DiagramWidth.JoinMarker + DiagramWidth.JoinMarker, DiagramWidth.JoinSpaceMarker);
    }

    [Fact]
    public void The_search_worker_roster_carries_the_rejoin_helpers()
    {
        // buildWorker serialises a FIXED LIST of functions with toString() and ships that as the
        // worker source. A helper left off the list does not exist inside the worker, the first
        // normalization throws, and deep search simply never returns — no error, just a timeout.
        // That is exactly what happened when rule 1b landed.
        var search = LoadEmbedded("report-search-index.js");
        var roster = search[search.IndexOf("var fns = [", StringComparison.Ordinal)..];
        roster = roster[..roster.IndexOf("];", StringComparison.Ordinal)];

        Assert.Contains("kronRejoinMarkedBreaks", roster);
        Assert.Contains("kronEndsWithOwnMarker", roster);
        // And the markers are literals inside those functions: a module-scope var would not travel.
        Assert.DoesNotContain("var KRON_JOIN_MARKER", search);
    }

    private static string LoadEmbedded(string name)
    {
        var assembly = typeof(ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
