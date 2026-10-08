using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// One report groups its failures twice — once in <c>Failures.md</c> and once in the HTML report's
/// cluster panel — from two implementations of "the first line, near enough". They disagreed three ways,
/// and every one of them is reachable: a message using bare CR line endings, a message that is present
/// but empty, and a message separated by a Unicode space the two rules classify differently.
///
/// <para>Each assertion here compares the two surfaces against each other rather than against a literal,
/// because a literal can only pin what one of them does. A reader who sees three clusters in the HTML
/// and two in the digest has no way to tell which is the run.</para>
/// </summary>
public class FailureClusterKeyTests
{
    private static Scenario[] Pair(string message) =>
    [
        new Scenario { Id = "t0", DisplayName = "First", Result = ExecutionResult.Failed, ErrorMessage = message },
        new Scenario { Id = "t1", DisplayName = "Second", Result = ExecutionResult.Failed, ErrorMessage = message }
    ];

    private static string[] DigestClusterHeadings(Scenario[] scenarios)
    {
        var digest = FailuresDigestGenerator.Generate(
            [new Feature { DisplayName = "Checkout", Scenarios = scenarios }], null, "TestRunReport", "3.1.0");

        // Only the headings under "## Clusters" — every worked failure below it is a `### ` heading too.
        return digest.Markdown.ReplaceLineEndings("\n").Split('\n')
            .SkipWhile(line => line != "## Clusters")
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.StartsWith("### ", StringComparison.Ordinal))
            .Select(line => line[4..(line.LastIndexOf(" — ", StringComparison.Ordinal) is var i and >= 0 ? i : line.Length)])
            .ToArray();
    }

    [Theory]
    [InlineData("Connection refused\ndetail")]
    [InlineData("Connection refused\r\ndetail")]
    // Bare CR. The digest cut at the first CR OR LF; the panel split on LF alone, so the panel's key was
    // the whole message and the digest's was its first line.
    [InlineData("Connection refused\rdetail")]
    // A no-break space. char.IsWhiteSpace says yes, the regex \s the panel used says yes too — but the
    // pair only agree once one rule decides for both.
    [InlineData("Connection\u00a0refused\ndetail")]
    [InlineData("  Connection refused  \r\ndetail")]
    [InlineData("Connection\u2028refused")]
    public void The_digest_and_the_cluster_panel_group_by_the_same_key(string message)
    {
        var scenarios = Pair(message);

        var panel = FailureClusterer.Cluster(scenarios).Select(c => c.ClusterKey).ToArray();

        Assert.Equal(panel, DigestClusterHeadings(scenarios));
    }

    [Fact]
    public void A_message_that_is_present_but_empty_forms_a_cluster_on_neither_surface()
    {
        // The panel filtered on `ErrorMessage is not null` and the digest on `key.Length > 0`, so two
        // failures carrying "" were one cluster keyed by the empty string in the HTML and no cluster at
        // all in the digest — a cluster whose heading was a blank line.
        var scenarios = Pair("");

        Assert.Empty(FailureClusterer.Cluster(scenarios));
        Assert.Empty(DigestClusterHeadings(scenarios));
    }

    [Fact]
    public void A_cluster_key_is_the_first_line_with_its_spacing_collapsed()
    {
        Assert.Equal("Connection refused", FailureText.FirstLine("Connection   refused\nwhile dialling"));
        Assert.Equal("Connection refused", FailureText.FirstLine("  Connection\trefused  \r\nwhile dialling"));
        Assert.Equal("", FailureText.FirstLine(null));
        Assert.Equal("", FailureText.FirstLine("   "));
    }

    [Fact]
    public void Two_failures_that_differ_only_after_the_first_line_are_one_cluster()
    {
        // The behaviour the key is FOR, kept explicit so that hardening the rule above cannot quietly
        // turn clustering off altogether.
        var scenarios = new[]
        {
            new Scenario { Id = "t0", DisplayName = "First", Result = ExecutionResult.Failed, ErrorMessage = "Connection refused\nat Dial(10.0.0.1)" },
            new Scenario { Id = "t1", DisplayName = "Second", Result = ExecutionResult.Failed, ErrorMessage = "Connection refused\nat Dial(10.0.0.2)" }
        };

        var cluster = Assert.Single(FailureClusterer.Cluster(scenarios));
        Assert.Equal("Connection refused", cluster.ClusterKey);
        Assert.Equal(2, cluster.Scenarios.Length);
    }

    [Fact]
    public void Two_failures_that_differ_on_the_first_line_are_two_causes_not_one()
    {
        var scenarios = new[]
        {
            new Scenario { Id = "t0", DisplayName = "First", Result = ExecutionResult.Failed, ErrorMessage = "Expected 5 but found 3" },
            new Scenario { Id = "t1", DisplayName = "Second", Result = ExecutionResult.Failed, ErrorMessage = "Expected OK but found 500" }
        };

        Assert.Empty(FailureClusterer.Cluster(scenarios));
        Assert.Empty(DigestClusterHeadings(scenarios));
    }

    private static Scenario Failed(string id, string message) =>
        new() { Id = id, DisplayName = id, Result = ExecutionResult.Failed, ErrorMessage = message };

    // SHOULDLY_ASSERTIONS_PLAN F15: Shouldly's first line is its subject's code, so on the first line alone
    // every failure on one variable was one cause, an equality and a comparison together.
    [Fact]
    public void Two_Shouldly_failures_on_one_subject_cluster_apart()
    {
        Scenario[] scenarios =
        [
            Failed("t0", "result\n    should be\n5\n    but was\n3"),
            Failed("t1", "result\n    should be\n6\n    but was\n3"),
            Failed("t2", "result\n    should be greater than\n10\n    but was\n3"),
            Failed("t3", "result\n    should be greater than\n12\n    but was\n3")
        ];

        var panel = FailureClusterer.Cluster(scenarios).Select(c => c.ClusterKey).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["result should be", "result should be greater than"], panel);
        Assert.Equal(panel, DigestClusterHeadings(scenarios).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Two_identical_Shouldly_failures_cluster_together()
    {
        var scenarios = Pair("name\r\n    should not be null but was");

        var cluster = Assert.Single(FailureClusterer.Cluster(scenarios));
        Assert.Equal("name should not be null but was", cluster.ClusterKey);
        Assert.Equal([cluster.ClusterKey], DigestClusterHeadings(scenarios));
    }

    [Fact]
    public void A_second_line_that_only_looks_like_Shouldly_keeps_the_first_line_key()
    {
        // Shouldly indents its check by four spaces; a message whose second line merely starts with "should"
        // is someone else's and keeps today's key.
        var cluster = Assert.Single(FailureClusterer.Cluster(Pair("Connection refused\nshould retry")));
        Assert.Equal("Connection refused", cluster.ClusterKey);
    }
}
