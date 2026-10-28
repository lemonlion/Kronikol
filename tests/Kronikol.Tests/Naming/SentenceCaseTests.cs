using Kronikol.Naming;

namespace Kronikol.Tests.Naming;

/// <summary>
/// The rule that turns an identifier into a sentence (plans/TITLES_AS_WRITTEN_PLAN.md 4.1). The expected column is the
/// harness's executable copy of the rule (<c>rules.cs</c>, its <c>REC</c> variant), run over these names.
/// </summary>
public class SentenceCaseTests
{
    [Theory]
    // a word written in capitals keeps them; every other word is lower-cased and the first gets a capital
    [InlineData("WhenMCAFundingActivates_TheLOCIsDrawn", "When MCA funding activates the LOC is drawn")]
    [InlineData("Given_the_UK_store_when_I_pay_then_VAT_is_added", "Given the UK store when I pay then VAT is added")]
    [InlineData("ViaAPIJourney", "Via API journey")]
    [InlineData("Response_Has_OK_StatusCode", "Response has OK status code")]
    [InlineData("The_Scalar_UI_endpoint_should_return_a_valid_page", "The scalar UI endpoint should return a valid page")]
    [InlineData("HTTP_service_uses_participant_shape", "HTTP service uses participant shape")]
    [InlineData("JSON_that_is_not_whole", "JSON that is not whole")]
    [InlineData("Implicit_AND_inserted", "Implicit AND inserted")]
    [InlineData("R1_renders_parameter_name_columns", "R1 renders parameter name columns")]
    [InlineData("GetHTTPResponse", "Get HTTP response")]
    [InlineData("Get_HTTP_response", "Get HTTP response")]
    [InlineData("Should_ReturnOK_WhenValid", "Should return OK when valid")]
    [InlineData("IOStreamReads", "IO stream reads")]
    [InlineData("ÄÖÜ_wird_gelesen", "ÄÖÜ wird gelesen")]
    // a word in Pascal case cannot say it is an acronym or a brand: that is the word list's work
    [InlineData("WhenMcaFundingActivates", "When mca funding activates")]
    [InlineData("AcmePayWebhookTests", "Acme pay webhook tests")]
    [InlineData("V2Api", "V2 api")]
    [InlineData("NUnitRuns", "N unit runs")]
    [InlineData("MSTestRuns", "MS test runs")]
    [InlineData("XUnit3_assembly_contains_binding_annotated_types", "X unit3 assembly contains binding annotated types")]
    [InlineData("iOSApp", "I OS app")]
    [InlineData("OAuth2Token", "O auth2 token")]
    [InlineData("Implements_ITrackingComponent", "Implements I tracking component")]
    [InlineData("Listens_on_IPv4", "Listens on I pv4")]
    [InlineData("APIsReturnJson", "AP is return json")]
    // nor where two words in capitals written together part
    [InlineData("GetsTheUKVATRate", "Gets the UKVAT rate")]
    // a plural of capitals stays whole; a closing Is or As is a word
    [InlineData("ParsesURLs", "Parses URLs")]
    [InlineData("GetsAllIDs", "Gets all IDs")]
    [InlineData("ListsVMs", "Lists VMs")]
    [InlineData("XIs", "X is")]
    [InlineData("WhenXIsNull", "When X is null")]
    [InlineData("WhenURLIsInvalid", "When URL is invalid")]
    [InlineData("TheIDAsString", "The ID as string")]
    // a name with no lower-case letter is a sentence written in capitals
    [InlineData("GIVEN_VALID_INPUT_WHEN_SAVED", "Given valid input when saved")]
    [InlineData("HTTP_GET", "Http get")]
    // a digit splits from a capital only where a word starts
    [InlineData("Returns404WhenMissing", "Returns404 when missing")]
    [InlineData("Retries3TimesThenFails", "Retries3 times then fails")]
    [InlineData("Returns200OK", "Returns200 OK")]
    [InlineData("Default_64MB_cache", "Default 64 MB cache")]
    [InlineData("UseC4False", "Use C4 false")]
    [InlineData("E2ETestsPass", "E2E tests pass")]
    [InlineData("P2PSync", "P2P sync")]
    [InlineData("color_F6F6F6", "Color F6F6F6")]
    [InlineData("Http2Works", "Http2 works")]
    [InlineData("Base64Encodes", "Base64 encodes")]
    [InlineData("X509Certificate", "X509 certificate")]
    [InlineData("p95_is_reported", "P95 is reported")]
    [InlineData("utf8_bytes", "Utf8 bytes")]
    // a one-letter capital keeps its case, but the article A
    [InlineData("IsXValid", "Is X valid")]
    [InlineData("First_N_items", "First N items")]
    [InlineData("WhenIOpenTheApp", "When I open the app")]
    [InlineData("IAmAdmin", "I am admin")]
    [InlineData("ReturnsAValue", "Returns a value")]
    [InlineData("ATest", "A test")]
    // one word, letters of other scripts, and what is not a letter
    [InlineData("Single", "Single")]
    [InlineData("x", "X")]
    [InlineData("IPv4", "I pv4")]
    [InlineData("ÜberCaseWorks", "Über case works")]
    [InlineData("İstanbulIsChecked", "İstanbul is checked")]
    [InlineData("ΑλφαΒήτα", "Αλφα βήτα")]
    [InlineData("测试Works", "测试works")]
    [InlineData("Month's report", "Month's report")]
    // whitespace of any kind and runs of underscores are one space
    [InlineData("Order\tAPI works", "Order API works")]
    [InlineData("__Leading__and_trailing__", "Leading and trailing")]
    public void An_identifier_reads_as_a_sentence(string identifier, string expected) =>
        Assert.Equal(expected, SentenceCase.Sentence(identifier));

    [Theory]
    [InlineData("")]
    [InlineData("___")]
    [InlineData(" \t ")]
    public void A_name_with_no_words_reads_as_nothing(string identifier) =>
        Assert.Equal("", SentenceCase.Sentence(identifier));

    [Fact]
    public void A_letter_outside_the_basic_plane_is_carried_whole()
    {
        // U+1D400 MATHEMATICAL BOLD CAPITAL A, a surrogate pair: neither half is split from the other or re-cased
        const string bold = "𝐀";
        Assert.Equal($"Uses {bold} glyph", SentenceCase.Sentence($"Uses_{bold}_glyph"));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    public void A_name_reads_the_same_on_a_machine_whose_culture_cases_i_otherwise(string culture)
    {
        var (sentence, words) = CultureRun.Under(culture, () =>
            (SentenceCase.Sentence("itemsAreShipped_IDsMatch"), SentenceCase.Words("isInvalidID")));

        Assert.Equal("Items are shipped IDs match", sentence);
        Assert.Equal("is invalid ID", words);
    }

    [Theory]
    // code in words, for an assertion label: split and cased as a sentence's words, but no capital given and nothing else touched
    [InlineData("HTTPStatus", "HTTP status")]
    [InlineData("StatusCode", "status code")]
    [InlineData("BeEquivalentTo", "be equivalent to")]
    [InlineData("ShouldHaveCount", "should have count")]
    [InlineData("UserIDs", "user IDs")]
    [InlineData("MAX_RETRIES", "MAX_RETRIES")]
    [InlineData("max_retries", "max_retries")]
    [InlineData("E2EOrder", "E2E order")]
    [InlineData("Returns404When", "returns404 when")]
    [InlineData("Items Count", "items count")]
    // code's punctuation parts words, so a word in capitals keeps them inside brackets; whitespace alone says shouting
    [InlineData("Encode(1U)", "encode(1U)")]
    [InlineData("Parse(URLs)", "parse(URLs)")]
    [InlineData("HTTP(GET)", "HTTP(GET)")]
    [InlineData("Get(Order.ID)", "get(order.ID)")]
    [InlineData("", "")]
    public void Code_reads_as_words(string code, string expected) =>
        Assert.Equal(expected, SentenceCase.Words(code));
}
