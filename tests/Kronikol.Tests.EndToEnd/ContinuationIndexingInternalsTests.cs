namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Unit-style coverage of <c>window._computeGlobalNoteIndex</c> — the shared
/// fragment-note indexing helper used by both the note hover buttons
/// (makeNotesCollapsible) and the context menu. A continuation chunk that
/// opens a fragment is the TAIL of the last note counted in the preceding
/// fragments (original index offset - 1), not a new note. Driven against
/// synthetic .puml-fragment DOM via <c>Page.EvaluateAsync</c>, following the
/// NoteYamlInternalsTests pattern.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class ContinuationIndexingInternalsTests : DiagramNotePlaywrightBase
{
    public ContinuationIndexingInternalsTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateToReport([System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        await Page.GotoAsync(GenerateReport($"ContIndexing_{testName}.html"));
        await Page.WaitForFunctionAsync("() => !!window._computeGlobalNoteIndex",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    /// <summary>
    /// Builds a detached owner with one .puml-fragment per source and returns
    /// _computeGlobalNoteIndex for every (fragment, localIdx) pair as
    /// "frag:local=global" tokens. Each entry in <paramref name="fragmentNotes"/>
    /// is one fragment: an array of first-content-lines, one per note block.
    /// </summary>
    private async Task<string> ComputeIndices(string[][] fragmentNotes)
    {
        return await Page.EvaluateAsync<string>("""
            (fragmentNotes) => {
                var owner = document.createElement('div');
                owner.className = 'plantuml-browser';
                var frags = fragmentNotes.map(function(noteLines, fi) {
                    var src = noteLines.map(function(l) {
                        return 'note left\n' + l + '\nend note';
                    }).join('\n');
                    var frag = document.createElement('div');
                    frag.className = 'puml-fragment';
                    frag.dataset.fragment = String(fi);
                    frag.setAttribute('data-plantuml', src);
                    owner.appendChild(frag);
                    return frag;
                });
                var out = [];
                fragmentNotes.forEach(function(noteLines, fi) {
                    noteLines.forEach(function(_, li) {
                        out.push(fi + ':' + li + '=' + window._computeGlobalNoteIndex(owner, frags[fi], li));
                    });
                });
                return out.join(' ');
            }
            """, (object)fragmentNotes);
    }

    private const string Cont = "..Continued From Previous Diagram..";

    [Fact]
    public async Task Continuation_chunk_maps_to_the_note_it_continues()
    {
        await NavigateToReport();
        // Notes [0, 1(split over 3), 2, 3]: fragment 1 is the interior chunk
        // of note 1, fragment 2 opens with its final chunk.
        var result = await ComputeIndices(new[]
        {
            new[] { "payload0", "huge part 1" },
            new[] { Cont },
            new[] { Cont, "payload2", "payload3" }
        });
        Assert.Equal("0:0=0 0:1=1 1:0=1 2:0=1 2:1=2 2:2=3", result);
    }

    [Fact]
    public async Task Two_split_notes_in_one_diagram_map_independently()
    {
        await NavigateToReport();
        // Notes A(split), B, C(split), D: fragment 1 opens with A's tail and
        // ends with C's head; fragment 2 opens with C's tail.
        var result = await ComputeIndices(new[]
        {
            new[] { "huge A part 1" },
            new[] { Cont, "payload B", "huge C part 1" },
            new[] { Cont, "payload D" }
        });
        Assert.Equal("0:0=0 1:0=0 1:1=1 1:2=2 2:0=2 2:1=3", result);
    }

    [Fact]
    public async Task Fragment_without_continuation_uses_plain_offset()
    {
        await NavigateToReport();
        var result = await ComputeIndices(new[]
        {
            new[] { "a", "b" },
            new[] { "c" }
        });
        Assert.Equal("0:0=0 0:1=1 1:0=2", result);
    }

    [Fact]
    public async Task Non_fragment_element_returns_local_index_unchanged()
    {
        await NavigateToReport();
        var result = await Page.EvaluateAsync<int>("""
            () => {
                var owner = document.createElement('div');
                owner.className = 'plantuml-browser';
                return window._computeGlobalNoteIndex(owner, owner, 2);
            }
            """);
        Assert.Equal(2, result);
    }
}
