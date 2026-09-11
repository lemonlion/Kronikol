using Kronikol.PlantUml;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// NOTE_COPY_FIDELITY_PLAN M3. A break in a note body is the width budget's decision, not the
/// payload's, so every path that hands note text back to a reader has to take it out again. Before
/// this, "Copy box text", "Open box text in new tab" and "Copy all caller request payloads" all
/// joined the note's SOURCE lines with <c>\n</c>, so a token cut mid-way came back in two pieces and
/// did not parse. These facts read the real clipboard, from a report whose note was wrapped by the
/// real generator, and every one of them fails against a build without the marker.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteCopyFidelityTests : DiagramNotePlaywrightBase
{
    public NoteCopyFidelityTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateTokenFixture(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithWrappedTokenNote(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
    }

    private async Task OpenNoteContextMenu()
    {
        await DispatchContextMenu(Page.Locator(".note-hover-rect").First);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
    }

    // ── The rejoin, in the runtime it actually ships in ─────────────────────

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("ab<U+200B>\ncd", "abcd")]
    [InlineData("ab<U+200B><U+200B>\ncd", "ab cd")]
    [InlineData("ab<U+200B>\ncd<U+200B>\nef", "abcdef")]
    [InlineData("a~<U+200B>\nb", "a~<U+200B>\nb")]
    // No marker, nothing to undo: the text keeps its bytes, CRLF included. Only a rewrite
    // normalises, so that a line split on \n is never left with a \r between it and its marker.
    [InlineData("ab\r\ncd", "ab\r\ncd")]
    [InlineData("ab<U+200B>\r\ncd", "abcd")]
    [InlineData("ab<U+200B>\r\ncd\r\nef", "abcd\nef")]
    public async Task The_shipped_rejoin_undoes_exactly_the_marked_breaks(string marked, string expected)
    {
        await NavigateTokenFixture("CopyFidelity_RejoinUnit.html");

        var actual = await Page.EvaluateAsync<string>(
            "t => window._rejoinWrappedNoteLines(t)", marked);

        Assert.Equal(expected, actual);
    }

    // ── The symptom the user reported ──────────────────────────────────────

    [Fact]
    public async Task Copy_box_text_yields_the_token_in_one_piece()
    {
        await NavigateTokenFixture("CopyFidelity_CopyBoxText.html");

        await OpenNoteContextMenu();
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text");
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();

        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The clipboard joins lines with \r\n on Windows, so the contract is asserted per line: the
        // token must sit WHOLE on one of them, which is exactly what the old build could not do.
        var lines = clipboard.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, l => l.Contains(ReportTestHelper.CopyFidelityJwt, StringComparison.Ordinal));
        Assert.DoesNotContain("U+200B", clipboard, StringComparison.Ordinal);
        Assert.DoesNotContain("​", clipboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_box_text_in_new_tab_yields_the_same_text_as_the_copy()
    {
        await NavigateTokenFixture("CopyFidelity_OpenBoxText.html");

        await OpenNoteContextMenu();
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Open box text in new tab");
        await item.WaitForAsync(new() { Timeout = 5000 });

        var popupTask = Page.Context.WaitForPageAsync();
        await item.ClickAsync();
        var popup = await popupTask;
        await popup.WaitForLoadStateAsync();

        var shown = await popup.EvaluateAsync<string>("() => document.body.innerText");

        Assert.Contains(ReportTestHelper.CopyFidelityJwt, shown.Replace("\r\n", "\n").Replace("\n", ""),
            StringComparison.Ordinal);
        var lines = shown.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, l => l.Contains(ReportTestHelper.CopyFidelityJwt, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Copy_all_caller_request_payloads_yields_the_token_in_one_piece()
    {
        await NavigateTokenFixture("CopyFidelity_CallerPayloads.html");

        await DispatchContextMenu(Page.Locator(".plantuml-browser svg").First);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy all caller request payloads");
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();

        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        var lines = clipboard.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, l => l.Contains(ReportTestHelper.CopyFidelityJwt, StringComparison.Ordinal));
    }

    // ── The marker must stay invisible ─────────────────────────────────────

    [Fact]
    public async Task The_marker_never_shows_in_the_painted_diagram()
    {
        await NavigateTokenFixture("CopyFidelity_Painted.html");

        // The browser engine has to resolve <U+200B> the way real Java PlantUML does, or every
        // wrapped note in every report grows eight visible characters per break.
        var painted = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('.plantuml-browser svg text'))
                .map(t => t.textContent).join('')
        """);

        Assert.DoesNotContain("U+200B", painted, StringComparison.Ordinal);
        Assert.Contains("​", painted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_generator_really_marked_the_fixture()
    {
        // Guards the fixture itself: if the wrapper stopped marking, every fact above would pass for
        // the wrong reason (nothing to rejoin).
        await NavigateTokenFixture("CopyFidelity_FixtureGuard.html");

        var source = await Page.EvaluateAsync<string>(
            "() => document.querySelector('.plantuml-browser').getAttribute('data-plantuml')");

        Assert.Contains(DiagramWidth.JoinMarker, source, StringComparison.Ordinal);
    }
}
