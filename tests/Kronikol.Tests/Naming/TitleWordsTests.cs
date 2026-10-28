using Kronikol.Naming;

namespace Kronikol.Tests.Naming;

/// <summary>
/// A suite's own spellings, put back into the titles Kronikol made (plans/TITLES_AS_WRITTEN_PLAN.md 4.5): an entry
/// matches a run of whole words whose letters and digits, read without case, are the entry's own, and the run reads as
/// the entry is written. The rows are the harness's <c>words.txt</c>, over the issue's five words and a suite's.
/// </summary>
public class TitleWordsTests
{
    private static readonly string[] IssueWords = ["AcmePay", "API", "MCA", "LOC", "SBR"];
    private static readonly string[] SuiteWords = ["gRPC", "OpenAPI", "AsyncAPI", "ClickHouse", "SQL", "DuckDB"];

    [Theory]
    [InlineData("Acme Pay Webhook Tests", "AcmePay Webhook Tests")]
    [InlineData("Webhook lead created via api journey for an eligible customer", "Webhook lead created via API journey for an eligible customer")]
    [InlineData("When mca funding activates the loc is drawn", "When MCA funding activates the LOC is drawn")]
    [InlineData("Api key is rotated", "API key is rotated")]
    [InlineData("The sbr is filed for the loc", "The SBR is filed for the LOC")]
    // a plural is its own entry
    [InlineData("The apis are listed", "The apis are listed")]
    // BDDfy's own humaniser writes each capital of a run as a word of its own
    [InlineData("When m c a funding activates", "When MCA funding activates")]
    public void The_issue_s_words_read_as_the_suite_spells_them(string title, string expected) =>
        Assert.Equal(expected, new TitleWords(IssueWords).Apply(title));

    [Theory]
    [InlineData("Grpc Order Status Tests", "gRPC Order Status Tests")]
    [InlineData("Grpc streams the order updates", "gRPC streams the order updates")]
    [InlineData("Two g RPC hops carry the identity to the third host", "Two gRPC hops carry the identity to the third host")]
    [InlineData("Specifications Open Api Scalar UI Tests", "Specifications OpenAPI Scalar UI Tests")]
    [InlineData("Specifications Async Api Feature", "Specifications AsyncAPI Feature")]
    [InlineData("Click House Sql Translator Tests", "ClickHouse SQL Translator Tests")]
    [InlineData("Duck Db Click House Query Engine Tests", "DuckDB ClickHouse Query Engine Tests")]
    [InlineData("The scalar UI endpoint should return a valid page", "The scalar UI endpoint should return a valid page")]
    public void An_entry_matches_its_letters_however_the_title_split_them(string title, string expected) =>
        Assert.Equal(expected, new TitleWords(SuiteWords).Apply(title));

    [Theory]
    // the punctuation around the run stays where it was
    [InlineData("Shows the ui, then the api.", "Shows the UI, then the API.")]
    [InlineData("Pays through (acme pay) twice", "Pays through (AcmePay) twice")]
    // a mark inside the run is no part of a word, and is never dropped
    [InlineData("Pays through acme, pay", "Pays through acme, pay")]
    [InlineData("Pays through acme-pay", "Pays through acme-pay")]
    [InlineData("Pays through acme (pay) twice", "Pays through acme (pay) twice")]
    // whole words only
    [InlineData("Rapid apis build", "Rapid apis build")]
    [InlineData("Ui and api", "UI and API")]
    public void Whole_words_are_matched_and_punctuation_kept(string title, string expected) =>
        Assert.Equal(expected, new TitleWords(["UI", "API", "AcmePay"]).Apply(title));

    [Fact]
    public void The_longest_entry_is_tried_first()
    {
        Assert.Equal("Reads the OpenAPI spec", new TitleWords(["API", "OpenAPI"]).Apply("Reads the open api spec"));
        Assert.Equal("MongoDB reads", new TitleWords(["Mongo", "MongoDB"]).Apply("Mongo db reads"));
        Assert.Equal("Mongo reads", new TitleWords(["Mongo", "MongoDB"]).Apply("mongo reads"));
    }

    [Theory]
    // the last word of a run keeps the digits written after it
    [InlineData("X unit3 assembly contains binding annotated types", "xUnit3 assembly contains binding annotated types")]
    [InlineData("O auth2 token is refreshed", "OAuth2 token is refreshed")]
    // a word that is digits alone is a word of its own
    [InlineData("Runs on x unit 3", "Runs on xUnit 3")]
    // an entry that ends in digits matches them
    [InlineData("Writes to s3 then reads", "Writes to S3 then reads")]
    public void Digits_after_the_last_word_are_kept(string title, string expected) =>
        Assert.Equal(expected, new TitleWords(["xUnit", "OAuth", "S3"]).Apply(title));

    [Fact]
    public void An_entry_of_ordinary_words_rewrites_them_too() =>
        Assert.Equal("CheckOut the basket", new TitleWords(["CheckOut"]).Apply("Check out the basket"));

    [Fact]
    public void An_entry_with_no_letter_or_digit_is_ignored()
    {
        var words = new TitleWords(["", " ", "--", "API"]);

        Assert.Equal("Calls the API -- twice", words.Apply("Calls the api -- twice"));
        Assert.True(new TitleWords(["", "--"]).IsEmpty);
        Assert.True(new TitleWords(null).IsEmpty);
        Assert.False(words.IsEmpty);
    }

    [Theory]
    // a row's arguments are its values, as the row's example values have them
    [InlineData("Adds vat for the uk [uk]", "Adds VAT for the UK [uk]")]
    [InlineData("Adds vat for the uk(country: \"uk\")", "Adds VAT for the UK(country: \"uk\")")]
    // and so is a value LightBDD writes into the name, in quotes
    [InlineData("Charges \"vat\" for the uk", "Charges \"vat\" for the UK")]
    public void A_row_s_values_are_left_as_they_are(string title, string expected) =>
        Assert.Equal(expected, new TitleWords(["VAT", "UK"]).Apply(title));

    [Fact]
    public void An_empty_list_changes_nothing()
    {
        Assert.Equal("Acme pay api", new TitleWords([]).Apply("Acme pay api"));
        Assert.Equal("", new TitleWords(IssueWords).Apply(""));
    }
}
