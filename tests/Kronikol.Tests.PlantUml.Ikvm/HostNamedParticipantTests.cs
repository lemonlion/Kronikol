using System.Net;
using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.Ingestion;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// Participant names drawn by real Java PlantUML. #190's fallback names a remote host by itself where every unmapped
/// host was <c>localhost:{port}</c>, so a punycode host's two <c>--</c> became a participant's name. A name was wrapped
/// but not creole-escaped, so creole read the two as a strike; an IPv6 host gives an alias that starts with <c>_</c>,
/// which no test had drawn; and a name holding a quote ended its own declaration.
/// </summary>
public class HostNamedParticipantTests
{
    private static RequestResponseLog Log(RequestResponseType type, string serviceName, Guid id) =>
        new(
            TestName: "Host names",
            TestId: "host-names-1",
            Method: InteractionRecord.ParseMethod("GET"),
            Content: null,
            Uri: new Uri("http://example.com/api"),
            Headers: [],
            ServiceName: serviceName,
            CallerName: "Caller",
            Type: type,
            TraceId: Guid.NewGuid(),
            RequestResponseId: id,
            TrackingIgnore: false);

    private static string Diagram(string serviceName)
    {
        var id = Guid.NewGuid();
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId([Log(RequestResponseType.Request, serviceName, id), Log(RequestResponseType.Response, serviceName, id)])
            .Single().PlantUmls.Single().PlainText;
    }

    [Theory]
    [InlineData("xn--e1afmkfd.xn--p1ai:443")]
    [InlineData("a--b--c.example:8443")]
    public void A_host_holding_two_double_hyphens_is_drawn_as_written(string host)
    {
        var svg = RenderedDiagram.Svg(Diagram(host));

        Assert.Contains(host, RenderedDiagram.DrawnText(svg));
        Assert.DoesNotContain("line-through", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ipv6_host_whose_alias_starts_with_an_underscore_is_drawn()
    {
        var source = Diagram("[fe80::1]:8080");
        Assert.Contains(" as _", source, StringComparison.Ordinal);

        var svg = RenderedDiagram.Svg(source);

        Assert.Contains("[fe80::1]:8080", RenderedDiagram.DrawnText(svg));
    }

    // Any name, not only a host: creole reads a doubled marker with a partner on its line as a style, so a participant
    // named with two of them lost both and had the text between restyled; and a quote ended the declaration, which made
    // the whole diagram PlantUML's Syntax Error picture.
    public static TheoryData<string> MarkedNames() =>
    [
        "xn--e1afmkfd.xn--p1ai:443",
        "Orders**Read**Model",
        "Billing//Legacy//Gateway",
        "queue__orders__dlq",
        "Payments ~~beta~~ API",
        "a\"\"quoted\"\"name",
        "the \"legacy\" ledger",
        "one\"quote",
    ];

    [Theory]
    [MemberData(nameof(MarkedNames))]
    public void A_sequence_participant_is_drawn_as_written(string name)
    {
        var svg = RenderedDiagram.Svg(Diagram(name));

        Assert.Contains(name, RenderedDiagram.DrawnText(svg).Select(WebUtility.HtmlDecode));
    }

    [Theory]
    [MemberData(nameof(MarkedNames))]
    public void A_component_is_drawn_as_written_and_stays_bold(string name)
    {
        var relationships = new ComponentRelationship[]
        {
            new("Caller", name, "HTTP", ["GET"], 5, 3, null),
        };

        var svg = RenderedDiagram.Svg(ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false));

        AssertDrawnBold(svg, name);
    }

    [Theory]
    [MemberData(nameof(MarkedNames))]
    public void A_component_diff_draws_a_new_service_as_written(string name)
    {
        var baseline = new ComponentRelationship[] { new("Caller", "Orders", "HTTP", ["GET"], 5, 3, null) };
        var current = new ComponentRelationship[] { new("Caller", "Orders", "HTTP", ["GET"], 5, 3, null), new("Caller", name, "HTTP", ["GET"], 1, 1, null) };

        var svg = RenderedDiagram.Svg(ComponentDiagramDiffer.GenerateDiffPlantUml(ComponentDiagramDiffer.Compare(baseline, current), useC4: false));

        AssertDrawnBold(svg, name);
    }

    private static void AssertDrawnBold(string svg, string name)
    {
        // A rectangle draws its name a word at a time.
        Assert.Contains(name, RenderedDiagram.DrawnLine(svg), StringComparison.Ordinal);
        var runs = Regex.Matches(svg, @"<text\b[^>]*>([^<]*)</text>").Select(m => (Run: m.Value, Text: WebUtility.HtmlDecode(m.Groups[1].Value))).ToArray();
        foreach (var word in name.Split(' '))
        {
            var drawn = runs.Where(r => r.Text == word).ToArray();
            Assert.NotEmpty(drawn);
            Assert.All(drawn, r => Assert.Contains("font-weight=\"bold\"", r.Run, StringComparison.Ordinal));
        }
    }
}
