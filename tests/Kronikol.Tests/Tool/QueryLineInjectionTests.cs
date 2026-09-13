using Kronikol.Tool.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The plan's C1 is about a newline in a feature or scenario name reaching a job log at column zero,
/// where <c>actions/runner</c>'s own <c>ActionCommand.TryParseV2</c> consumes it as a workflow command —
/// it trims whitespace only. The worst reachable payload is <c>::stop-commands::&lt;token&gt;</c>, which
/// switches off workflow-command processing for the rest of the job.
///
/// <para>The library pointer is one channel and was closed with the run-end work. <b>This is the other
/// one, and the plan measured it as the channel where C1 is actually real:</b> `kronikol query` run as a
/// CI step owns its own stdout, so nothing swallows it. It prints feature names, scenario names, service
/// names and step text, all of which come from the run.</para>
///
/// <para>The fix is which way the default points. <see cref="QueryWriter.Line"/> now flattens, so every
/// line the tool composes is one line; the three places that deliberately write a multi-line payload say
/// so with <see cref="QueryWriter.Payload"/>. Safe by construction, and an opt-out that has to be typed.</para>
/// </summary>
public class QueryLineInjectionTests
{
    private static string Written(Action<QueryWriter> write)
    {
        var output = new StringWriter();
        var writer = new QueryWriter(output, maxBytes: 0);
        write(writer);
        writer.Flush(TextWriter.Null);
        return output.ToString();
    }

    [Theory]
    [InlineData("Checkout\n::stop-commands::deadbeef")]
    [InlineData("Checkout\r\n::error::forged")]
    [InlineData("Checkout\r::error::forged")]
    [InlineData("Checkout\u2028::error::forged")]
    public void A_name_carrying_a_line_ending_cannot_add_a_line(string hostile)
    {
        var text = Written(w => w.Line($"s0  {hostile} › Pay"));

        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Single(lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("::", StringComparison.Ordinal));

        // Reported, not dropped — a name the tool refuses to show is a scenario the reader cannot find.
        // Whatever followed the break is still in the line, just no longer at the start of one.
        var tail = hostile.Split('\n', '\r', '\u2028')[^1];
        Assert.Contains(tail, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_payload_is_still_written_with_its_own_line_breaks()
    {
        // The non-vacuity half, and the reason this is not one flatten inside Line: `query http --body`
        // exists to hand back a payload, and a JSON document flattened onto one line is the opposite of
        // what it was asked for.
        var text = Written(w => w.Payload("{\n  \"a\": 1,\n  \"b\": 2\n}"));

        Assert.Equal(4, text.TrimEnd('\n').Split('\n').Length);
        Assert.Contains("  \"a\": 1,", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_budget_counts_a_payload_the_same_way_it_counts_a_line()
    {
        // Opting out of flattening must not opt out of --max-bytes, or the one method that writes the
        // biggest strings in the tool would be the one the budget cannot see.
        var output = new StringWriter();
        var writer = new QueryWriter(output, maxBytes: 20);
        writer.Payload(new string('x', 500));
        writer.Flush(TextWriter.Null);

        Assert.DoesNotContain("xxxxx", output.ToString(), StringComparison.Ordinal);
    }
}
