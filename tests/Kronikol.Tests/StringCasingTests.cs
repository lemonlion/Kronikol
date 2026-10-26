namespace Kronikol.Tests;

/// <summary>
/// <see cref="StringCasing.Titleize"/> keeps a word with an apostrophe in it as written (5.9.1, plans/REPORT_UI_FIXES_PLAN.md
/// N2: until then <c>Month's birthday</c> became "Month S Birthday"), and an all-capitals word before a comma (E16); and
/// <see cref="StringCasing.TitleizeAsOf590"/>, the frozen copy of 5.9.0's function the scenarios' stable ids are keyed
/// by (N3), keeps giving 5.9.0's names. The values are the plan's measurements (section 3, M-I4, harness gen/titleize.cs).
/// </summary>
public class StringCasingTests
{
    [Theory]
    [InlineData("Month's birthday", "Month's Birthday")]
    [InlineData("Month\u2019s birthday", "Month\u2019s Birthday")]
    [InlineData("O'Brien's order", "O'Brien's Order")]
    [InlineData("Don't bake twice", "Don't Bake Twice")]
    [InlineData("Customer's Orders", "Customer's Orders")]
    [InlineData("the 'quoted' word", "The Quoted Word")]
    [InlineData("rock 'n' roll", "Rock N Roll")]
    public void Titleize_keeps_a_word_with_an_apostrophe_as_written(string written, string shown) =>
        Assert.Equal(shown, written.Titleize());

    [Theory]
    [InlineData("Order Status via gRPC", "Order Status Via G RPC")]
    [InlineData("OrderProcessing", "Order Processing")]
    [InlineData("my_feature_name", "My Feature Name")]
    [InlineData("v1.2 release", "V 1 2 Release")]
    [InlineData("3d printing", "3D Printing")]
    [InlineData("Pancakes \U0001F95E flip", "Pancakes Flip")]
    public void Titleize_cases_every_other_word_as_before(string written, string shown) =>
        Assert.Equal(shown, written.Titleize());

    [Fact]
    public void Titleize_keeps_an_all_capitals_word_before_a_comma() =>
        Assert.Equal("HTTP, Then The Order", "HTTP, then the order".Titleize());

    // 5.9.0's names, which N3 keys the ids of renamed features by: these must never change
    [Theory]
    [InlineData("Month's birthday", "Month S Birthday")]
    [InlineData("Month\u2019s birthday", "Month S Birthday")]
    [InlineData("O'Brien's order", "O Brien S Order")]
    [InlineData("Don't bake twice", "Don T Bake Twice")]
    [InlineData("Customer's Orders", "Customer S Orders")]
    [InlineData("HTTP, then the order", "Http, Then The Order")]
    [InlineData("the 'quoted' word", "The Quoted Word")]
    [InlineData("Order Status via gRPC", "Order Status Via G RPC")]
    [InlineData("Order Cross-Field Validation", "Order Cross Field Validation")]
    [InlineData("Orders: retry on 503", "Orders Retry On 503")]
    [InlineData("Returns 404 (not found)", "Returns 404 Not Found")]
    [InlineData("OrderProcessing", "Order Processing")]
    [InlineData("my_feature_name", "My Feature Name")]
    [InlineData("v1.2 release", "V 1 2 Release")]
    [InlineData("3d printing", "3D Printing")]
    [InlineData("Pancakes \U0001F95E flip", "Pancakes Flip")]
    public void TitleizeAsOf590_gives_5_9_0s_names(string written, string shown) =>
        Assert.Equal(shown, StringCasing.TitleizeAsOf590(written));
}
