using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The access-chain reader that both value paths and the label formatter share (ASSERTION_VALUE_SUBSTITUTION_PLAN
/// §3.2, #163): what a chain is, which part of it is read, and the three answers reading gives.
/// </summary>
public class AccessChainTests
{
    [Theory]
    [InlineData("x.ShouldBe(expected.Colour.ToString())", "expected.Colour.ToString()", "expected.Colour")]
    [InlineData("x.ShouldBe(expected.Lines[0])", "expected.Lines[0]", "expected.Lines")]
    [InlineData("x.ShouldBe(expected.Lines.First().Length)", "expected.Lines.First().Length", "expected.Lines")]
    [InlineData("x.ShouldBe(expected.Get<int>())", "expected.Get<int>()", "expected")]
    [InlineData("x.ShouldBe(expected.Get<Dictionary<string, int[]>>().Count)", "expected.Get<Dictionary<string,int[]>>().Count", "expected")]
    [InlineData("x.ShouldBe(expected.Inner?.Value)", "expected.Inner.Value", "expected.Inner.Value")]
    [InlineData("x.ShouldBe(expected.Inner!.Value!)", "expected.Inner.Value", "expected.Inner.Value")]
    [InlineData("x.ShouldBe(expected.Lines?[0])", "expected.Lines[0]", "expected.Lines")]
    [InlineData("x.ShouldBe(expected\n    .Inner\n    ?.Value)", "expected.Inner.Value", "expected.Inner.Value")]
    [InlineData("x.ShouldBe(this.expected.Id)", "this.expected.Id", "this.expected.Id")]
    [InlineData("x.ShouldBe(expected.Count < limit)", "expected.Count", "expected.Count")]
    [InlineData("x.ShouldBe(expected.Id != other)", "expected.Id", "expected.Id")]
    [InlineData("x.ShouldBe(expected.Lines.Count(l => l == \"a.b\"))", "expected.Lines.Count(l=>l==\"a.b\")", "expected.Lines")]
    public void A_chain_runs_to_its_last_segment_and_is_read_to_its_first_call_or_index(string text, string key, string readKey)
    {
        var chain = AccessChain.Find(text).Single(c => c.Root == "expected");

        Assert.Equal(key, chain.Key);
        Assert.Equal(readKey, chain.ReadKey);
    }

    [Theory]
    [InlineData("x.ShouldBe(nameof(expected.Colour))")]
    [InlineData("x.ShouldBe(\"expected.Colour\")")]
    [InlineData("x.ShouldBe($\"{expected.Colour}\")")]
    [InlineData("x.ShouldBe(other.expected)")]
    [InlineData("x.ShouldBe(other?.expected)")]
    [InlineData("x.ShouldBe(typeof(expected))")]
    public void A_name_that_is_not_evaluated_or_is_a_member_starts_no_chain(string text)
    {
        Assert.DoesNotContain(AccessChain.Find(text), c => c.Root == "expected");
    }

    [Fact]
    public void A_chain_inside_another_chains_brackets_is_found()
    {
        var chains = AccessChain.Find("x.ShouldBe(map[key.Id].Get(id))");

        Assert.Equal(["x", "map", "key", "id"], chains.Select(c => c.Root));
        Assert.Equal("key.Id", chains[2].Key);
    }

    [Fact]
    public void A_range_is_not_member_access()
    {
        Assert.Contains(AccessChain.Find("x.ShouldBe(items[..count])"), c => c.Root == "count");
    }

    [Theory]
    [InlineData("a?.b . c!", "a.b.c")]
    [InlineData("a?[0] ?? \"x ?. y\"", "a[0]??\"x ?. y\"")]
    [InlineData("a != b", "a!=b")]
    public void The_canonical_key_drops_what_does_not_change_a_value(string code, string key)
    {
        Assert.Equal(key, AccessChain.CanonicalKey(code));
    }

    [Fact]
    public void Members_are_read_by_property_then_field_including_a_base_types_private_ones()
    {
        var read = Chain("x.ShouldBe(d.Secret)", "d").Read(new Derived());

        Assert.True(read.Readable);
        Assert.True(read.Whole);
        Assert.Equal("base", read.Value);
    }

    [Fact]
    public void A_member_that_is_not_there_is_unreadable()
    {
        var read = Chain("x.ShouldBe(d.Nothing)", "d").Read(new Derived());

        Assert.False(read.Readable);
        Assert.Contains("Nothing", read.Reason);
    }

    [Fact]
    public void A_getter_that_throws_is_unreadable()
    {
        var read = Chain("x.ShouldBe(d.Throws)", "d").Read(new Derived());

        Assert.False(read.Readable);
        Assert.Contains("InvalidOperationException", read.Reason);
    }

    [Fact]
    public void A_null_before_a_plain_dot_is_unreadable()
    {
        Assert.False(Chain("x.ShouldBe(d.Next.Secret)", "d").Read(new Derived()).Readable);
    }

    [Theory]
    [InlineData("x.ShouldBe(d.Next?.Secret)")]
    [InlineData("x.ShouldBe(d.Next?.Secret.Length)")]
    [InlineData("x.ShouldBe(d.Next?.ToString())")]
    [InlineData("x.ShouldBe(d.NoItems?[0])")]
    public void A_null_before_a_null_conditional_gives_the_whole_chain_null(string text)
    {
        var read = Chain(text, "d").Read(new Derived());

        Assert.True(read.Readable);
        Assert.True(read.Whole);
        Assert.Null(read.Value);
    }

    [Fact]
    public void A_chain_that_goes_on_past_a_call_gives_its_read_parts_value()
    {
        var read = Chain("x.ShouldBe(d.Name.ToUpper())", "d").Read(new Derived());

        Assert.True(read.Readable);
        Assert.False(read.Whole);
        Assert.Equal("derived", read.Value);
    }

    [Fact]
    public void A_method_is_never_read_as_a_member_even_when_a_property_shares_its_name()
    {
        var chain = Chain("x.ShouldBe(d.Items.Count(i => i > 0))", "d");

        Assert.Equal("d.Items", chain.ReadKey);
        Assert.IsType<List<int>>(chain.Read(new Derived()).Value);
    }

    [Fact]
    public void Only_a_single_value_read_before_a_call_is_kept_as_a_prefix()
    {
        var values = new ChainValues();
        var scalar = Chain("x.ShouldBe(d.Name.ToUpper())", "d");
        var list = Chain("x.ShouldBe(d.Items.First())", "d");

        values.Keep(scalar, scalar.Read(new Derived()), (v, _) => v?.ToString());
        values.Keep(list, list.Read(new Derived()), (v, _) => v?.ToString());

        Assert.Equal(["d.Name"], values.Prefixes);
        Assert.Equal(["d.Name"], values.Keys);
    }

    private static AccessChain Chain(string text, string root) => AccessChain.Find(text).Single(c => c.Root == root);

    private class Base
    {
        private string Secret { get; } = "base";
    }

    private sealed class Derived : Base
    {
        public string Name => "derived";
        public Derived? Next => null;
        public List<int> Items { get; } = [1, 2];
        public List<int>? NoItems => null;
        public string Throws => throw new InvalidOperationException("boom");
    }
}
