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

    /// <summary>
    /// A trace is text from the machine that ran the tests. Its paths keep that machine's separators, so
    /// the declaring-file match must split on either separator or a Windows report read on a Linux runner
    /// prefers the wrong frame. On Windows this passes with <c>Path.GetFileName</c> too; on Linux it does
    /// not, which is what makes it a guard rather than a restatement.
    /// </summary>
    [Fact]
    public void A_windows_path_in_the_trace_matches_the_declaring_file_on_every_os()
    {
        const string trace = """
               at Acme.Helpers.Retry.Run(Action body) in C:\src\helpers\Retry.cs:line 9
               at Checkout.Tests.PaymentTests.Pay_with_an_expired_card() in C:\src\tests\PaymentTests.cs:line 42
            """;

        var frame = FailureText.ThrownAt(trace, @"tests\PaymentTests.cs");

        Assert.NotNull(frame);
        Assert.Equal("Checkout.Tests.PaymentTests.Pay_with_an_expired_card()", frame.Value.Method);
        Assert.Equal(42, frame.Value.Line);
    }

    /// <summary>
    /// The real trace from a CiPreview.Mixed run, copied rather than invented. xUnit v3 ships source-linked
    /// PDBs, so THREE assertion frames carry a file and a line before the test does — which is why "the
    /// first frame with source information" was the wrong rule and why this had to be measured on output
    /// rather than reasoned about.
    /// </summary>
    private const string RealXunitV3Trace = """
           at Xunit.Assert.Equal(ReadOnlySpan`1 expected, ReadOnlySpan`1 actual, Boolean ignoreCase, Boolean ignoreLineEndingDifferences, Boolean ignoreWhiteSpaceDifferences, Boolean ignoreAllWhiteSpace) in /_/src/xunit.v3.assert/Asserts/StringAsserts.cs:line 875
           at Xunit.Assert.Equal(String expected, String actual, Boolean ignoreCase, Boolean ignoreLineEndingDifferences, Boolean ignoreWhiteSpaceDifferences, Boolean ignoreAllWhiteSpace) in /_/src/xunit.v3.assert/Asserts/StringAsserts.cs:line 1354
           at Xunit.Assert.Equal(String expected, String actual) in /_/src/xunit.v3.assert/Asserts/StringAsserts.cs:line 825
           at Example.Api.Tests.CiPreview.Mixed.Scenarios.Cake_Error_Diff_Feature.Cake_batch_id_should_be_a_specific_value() in C:\src\Scenarios\CakeErrorDiff.cs:line 31
        """;

    [Fact]
    public void An_assertion_librarys_own_frames_are_not_the_answer()
    {
        var frame = FailureText.ThrownAt(RealXunitV3Trace);

        Assert.NotNull(frame);
        Assert.Equal("Example.Api.Tests.CiPreview.Mixed.Scenarios.Cake_Error_Diff_Feature.Cake_batch_id_should_be_a_specific_value()",
            frame!.Value.Method);
        Assert.Equal(31, frame.Value.Line);
    }

    [Fact]
    public void The_file_the_scenario_was_declared_in_wins_over_the_namespace_guess()
    {
        // The signal that needs no list. A producer that reported a source file has already said which
        // file the test is in, so the frame in that file is the frame — whatever its namespace looks like.
        var frame = FailureText.ThrownAt(RealXunitV3Trace, preferFile: "Scenarios/CakeErrorDiff.cs");

        Assert.NotNull(frame);
        Assert.Equal(31, frame!.Value.Line);
    }

    [Fact]
    public void A_trace_that_is_all_framework_still_names_the_assertion_that_threw()
    {
        // Better than the blank: a reader learns WHICH assertion failed even when no frame is theirs.
        var frame = FailureText.ThrownAt(
            "   at Xunit.Assert.Equal(String expected, String actual) in /_/src/xunit.v3.assert/Asserts/StringAsserts.cs:line 825");

        Assert.NotNull(frame);
        Assert.Equal(825, frame!.Value.Line);
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
