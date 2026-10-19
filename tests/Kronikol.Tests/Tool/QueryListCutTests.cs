using Kronikol.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The list cut every capped list of <c>kronikol query</c> goes through (#161, plans/LIST_CAPS_PLAN.md T10): which entries a
/// list view prints, and how the line standing in for the rest names them. The views' facts are in
/// <see cref="QueryAttachmentListTests"/>; these hold the selection and the name list at their edges.
/// </summary>
public class QueryListCutTests
{
    [Theory]
    [InlineData(0, new int[0], new int[0], new int[0])]
    [InlineData(1, new[] { 0 }, new int[0], new int[0])]
    [InlineData(6, new[] { 0, 1, 2, 3, 4, 5 }, new int[0], new int[0])]
    [InlineData(7, new[] { 0 }, new[] { 1 }, new[] { 2, 3, 4, 5, 6 })]
    public void A_list_at_or_under_the_cap_is_whole_and_one_over_it_cuts_one(int count, int[] head, int[] cut, int[] tail)
    {
        var list = QueryWriter.CutList(Enumerable.Range(0, count).ToList(), cap: 6, head: 1);

        Assert.Equal(head, list.Head);
        Assert.Equal(cut, list.Cut);
        Assert.Equal(tail, list.Tail);
        Assert.Equal(head.Concat(tail), list.Shown);
    }

    [Fact]
    public void A_long_list_keeps_its_first_and_its_last_five()
    {
        var list = QueryWriter.CutList(Enumerable.Range(0, 60).ToList(), cap: 6, head: 1);

        Assert.Equal([0], list.Head);
        Assert.Equal(Enumerable.Range(1, 54), list.Cut);
        Assert.Equal([55, 56, 57, 58, 59], list.Tail);
    }

    [Fact]
    public void Another_head_moves_the_cut()
    {
        var list = QueryWriter.CutList(Enumerable.Range(0, 10).ToList(), cap: 4, head: 2);

        Assert.Equal([0, 1], list.Head);
        Assert.Equal([2, 3, 4, 5, 6, 7], list.Cut);
        Assert.Equal([8, 9], list.Tail);
    }

    [Fact]
    public void Names_come_in_the_order_each_first_appears_and_a_repeat_is_counted()
    {
        Assert.Equal("after-step.png ×3, step-shot.png",
            QueryWriter.NameList(["after-step.png", "step-shot.png", "after-step.png", "after-step.png"]));
    }

    [Fact]
    public void Names_stop_at_the_last_whole_name_that_fits()
    {
        // 88 characters through "trace"; "screenshot-end.png" would take the list to 108.
        Assert.Equal("after-step.png ×2, step-shot.png, screenshot, screenshot-start.png, error-context, trace, …",
            QueryWriter.NameList(["after-step.png", "step-shot.png", "after-step.png", "screenshot", "screenshot-start.png", "error-context", "trace", "screenshot-end.png", "video"]));
    }

    [Fact]
    public void A_list_of_exactly_the_limit_fits()
    {
        // Six names of fifteen characters and five separators: a hundred.
        string[] names = [.. Enumerable.Range(1, 7).Select(i => $"declined-{i:D2}.png")];

        Assert.Equal(string.Join(", ", names[..6]) + ", …", QueryWriter.NameList(names));
        Assert.Equal(string.Join(", ", names[..6]), QueryWriter.NameList(names[..6]));
    }

    [Fact]
    public void A_first_name_longer_than_the_limit_gives_no_list()
    {
        Assert.Null(QueryWriter.NameList([new string('a', 101), "b.png"]));
        Assert.Null(QueryWriter.NameList([]));
    }

    [Fact]
    public void A_name_is_put_on_one_line()
    {
        Assert.Equal("a b.png", QueryWriter.NameList(["a\nb.png"]));
    }

    [Fact]
    public void The_cut_line_names_the_count_the_names_and_the_view()
    {
        Assert.Equal("… 4 more attachment(s) (after-step.png ×3, step-shot.png): attachments s0",
            QueryWriter.CutLine(4, "attachment(s)", "after-step.png ×3, step-shot.png", "attachments s0"));
        Assert.Equal("… 41 more call(s): flow s0/1", QueryWriter.CutLine(41, "call(s)", null, "flow s0/1"));
    }
}
