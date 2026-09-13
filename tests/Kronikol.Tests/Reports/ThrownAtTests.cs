using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The one fact a reader of a failure wants first is where it was thrown, and <c>errorStackTrace</c> has
/// carried it all along — the HTML shows it, <c>CiSummary.md</c> shows it, the XML and CTRF exports
/// carry it. <c>Failures.md</c> showed none of it, while the <c>CLAUDE.md</c> written beside it tells the
/// reader not to open the HTML. So the run's own instructions pointed at the one surface that had dropped
/// the field.
///
/// <para>Not the whole trace: that is assertion-library frames, runtime frames and async plumbing, and
/// inlining it would be the digest doing the thing it exists to prevent. The first frame with source
/// information is the interesting one, because library and runtime assemblies ship without PDBs — so
/// "has a file and a line" is a good enough proxy for "is the code someone here wrote".</para>
/// </summary>
public class ThrownAtTests
{
    private const string XunitTrace = """
           at Xunit.Assert.Equal[T](T expected, T actual)
           at Checkout.Tests.PaymentTests.Pay_with_an_expired_card() in C:\src\tests\PaymentTests.cs:line 42
           at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
        """;

    [Fact]
    public void The_first_frame_with_source_information_is_the_one_that_matters()
    {
        var frame = FailureText.ThrownAt(XunitTrace);

        Assert.NotNull(frame);
        Assert.Equal("Checkout.Tests.PaymentTests.Pay_with_an_expired_card()", frame!.Value.Method);
        Assert.Equal(@"C:\src\tests\PaymentTests.cs", frame.Value.File);
        Assert.Equal(42, frame.Value.Line);
    }

    [Fact]
    public void A_unix_path_and_a_generic_method_survive_the_parse()
    {
        var frame = FailureText.ThrownAt(
            "   at Checkout.Tests.Repo`1.Find[TKey](TKey key) in /src/tests/Repo.cs:line 7");

        Assert.NotNull(frame);
        Assert.Equal("Checkout.Tests.Repo`1.Find[TKey](TKey key)", frame!.Value.Method);
        Assert.Equal("/src/tests/Repo.cs", frame.Value.File);
        Assert.Equal(7, frame.Value.Line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   at Xunit.Assert.Equal[T](T expected, T actual)\n   at System.RuntimeMethodHandle.InvokeMethod()")]
    [InlineData("some producer's free text that is not a stack trace at all")]
    public void A_trace_with_no_source_information_yields_nothing_rather_than_a_guess(string? trace)
    {
        // Emitting a frame without a file would put an assertion-library method where the reader expects
        // their own code, which is worse than the blank the digest had before.
        Assert.Null(FailureText.ThrownAt(trace));
    }

    [Fact]
    public void The_digest_prints_the_frame_and_the_jsonl_carries_its_parts()
    {
        var digest = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed,
                        ErrorMessage = "Assert.Equal() Failure", ErrorStackTrace = XunitTrace
                    }
                ]
            }],
            null, "TestRunReport", "3.1.0");

        Assert.Contains("Checkout.Tests.PaymentTests.Pay_with_an_expired_card()", digest.Markdown, StringComparison.Ordinal);
        Assert.Contains(@"C:\src\tests\PaymentTests.cs:42", digest.Markdown, StringComparison.Ordinal);

        var failure = FailuresJsonl.Failures(digest.Jsonl).Single();
        var thrownAt = failure.GetProperty("thrownAt");
        Assert.Equal("Checkout.Tests.PaymentTests.Pay_with_an_expired_card()", thrownAt.GetProperty("method").GetString());
        Assert.Equal(@"C:\src\tests\PaymentTests.cs", thrownAt.GetProperty("file").GetString());
        Assert.Equal(42, thrownAt.GetProperty("line").GetInt32());
    }

    [Fact]
    public void A_failure_with_no_stack_trace_gains_no_empty_section()
    {
        var digest = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [new Scenario { Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "boom" }]
            }],
            null, "TestRunReport", "3.1.0");

        Assert.DoesNotContain("thrown at", digest.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            FailuresJsonl.Failures(digest.Jsonl).Single().GetProperty("thrownAt").ValueKind);
    }
}
