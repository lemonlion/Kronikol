using System.Net;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// What a mid-formatting processor is given, and what becomes of what it returns (3.30.2). It runs after the body
/// is pretty-printed as JSON, or a form body is split into its fields, and before Kronikol escapes the body for
/// PlantUML, so it sees the payload as captured and a redaction regex matches the whole value. What it returns is
/// escaped like the payload. In 3.30.1 it ran after the escaper, which writes a literal <c>~</c> as
/// <c>&lt;U+007E&gt;</c>: the wiki's Bearer recipe stopped at a token's first tilde and the rest of the token was
/// drawn in the report. A form body reached it already cut into 80-character chunks, so a long field was only
/// ever redacted up to its first cut.
/// </summary>
public class NoteProcessorTests
{
    /// <summary>The recipe the wiki gives (Filtering-and-Redacting-Diagram-Content, Content-Formatting).</summary>
    private const string BearerRecipe = @"Bearer [A-Za-z0-9\-._~+/]+=*";

    [Fact]
    public void The_wikis_bearer_recipe_redacts_a_token_that_holds_a_tilde()
    {
        var source = Diagram("""{"echo":"Bearer abc~def~ghi"}""", "application/json",
            mid: c => Regex.Replace(c, BearerRecipe, "Bearer ***"));

        Assert.Contains("\"echo\": \"Bearer ***\"", source);
        Assert.DoesNotContain("def", source);
        Assert.DoesNotContain("ghi", source);
    }

    [Fact]
    public void A_mid_processor_sees_the_body_as_captured()
    {
        string? seen = null;

        Diagram("""{"path":"~/.bashrc","sql":"'a'","call":"%date()","pair":"a--b--c"}""", "application/json", mid: c => seen = c);

        Assert.NotNull(seen);
        Assert.Contains("\"path\": \"~/.bashrc\"", seen);
        Assert.Contains("\"call\": \"%date()\"", seen);
        Assert.Contains("\"pair\": \"a--b--c\"", seen);
        Assert.DoesNotContain("<U+", seen);
    }

    [Fact]
    public void What_a_mid_processor_returns_is_escaped_like_the_payload()
    {
        const string returned = "**x** and **y** in ~/home";

        var source = Diagram("""{"a":1}""", "application/json", mid: _ => returned);

        Assert.Contains(PlantUmlCreator.EscapeCreoleMarkup(returned), source);
        Assert.DoesNotContain("**x**", source);
    }

    [Fact]
    public void A_mid_processor_sees_each_field_of_a_form_body_whole_on_a_line_of_its_own()
    {
        var token = string.Concat(Enumerable.Repeat("abcDEF-_~.", 12)); // 120 characters: longer than a note chunk
        string? seen = null;

        var source = Diagram($"grant_type=refresh_token&refresh_token={token}&client_id=app", "application/x-www-form-urlencoded",
            mid: c => Regex.Replace(seen = c, @"(?<=refresh_token=)[^&\s]+", "***"));

        Assert.Equal(["grant_type=refresh_token", $"refresh_token={token}", "client_id=app"], seen!.Split('\n'));
        Assert.Contains("refresh_token=***", source);
        Assert.DoesNotContain("abcDEF", source);
    }

    [Fact]
    public void A_form_body_a_mid_processor_leaves_alone_is_drawn_as_before()
    {
        const string body = "grant_type=client_credentials&scope=orders.read&note=a__b__c";

        Assert.Equal(Diagram(body, "application/x-www-form-urlencoded", mid: null), Diagram(body, "application/x-www-form-urlencoded", mid: c => c));
    }

    [Fact]
    public void A_mid_processor_is_not_given_the_header_lines()
    {
        // The wiki said a mid-processor redacts a Bearer token in the Authorization header. It never saw headers.
        string? seen = null;

        Diagram("""{"a":1}""", "application/json", mid: c => seen = c, headers: [("Authorization", "Bearer abc")]);

        Assert.NotNull(seen);
        Assert.DoesNotContain("Authorization", seen);
    }

    private static string Diagram(string body, string contentType, Func<string, string>? mid, (string, string?)[]? headers = null)
    {
        var pair = Guid.NewGuid();
        (string, string?)[] requestHeaders = [("Content-Type", contentType), .. headers ?? []];
        RequestResponseLog[] logs =
        [
            new("t", "t", HttpMethod.Post, body, new Uri("http://example.com/api/echo"), requestHeaders, "Svc", "Caller",
                RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new("t", "t", HttpMethod.Post, "{}", new Uri("http://example.com/api/echo"), [("Content-Type", "application/json")], "Svc", "Caller",
                RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: HttpStatusCode.OK),
        ];
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, requestMidFormattingProcessor: mid).Single().PlantUmls.Single().PlainText;
    }
}
