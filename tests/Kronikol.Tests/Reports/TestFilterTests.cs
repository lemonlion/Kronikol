using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The fully qualified test name, read out of a stack frame's method in the shape <c>dotnet test
/// --filter "FullyQualifiedName~…"</c> matches. The compiler puts scaffolding between the test and its
/// frame - async state machines, lambdas, display classes, generic arity - and every shape here was seen
/// in a real trace.
/// </summary>
public class TestFilterTests
{
    [Theory]
    [InlineData("Acme.Orders.Tests.CheckoutTests.Pays_by_card()", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.Pays_by_card(String currency, Int32 amount)", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext()", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.<>c__DisplayClass3_0.<Pays_by_card>b__0()", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.<>c.<Pays_by_card>b__3_0(Order o)", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests`1.Pays_by_card[T](T value)", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("  Acme.Orders.Tests.CheckoutTests.Pays_by_card  ", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.<>c__DisplayClass3_0.<<Pays_by_card>b__0>d.MoveNext()", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.<Pays_by_card>g__Charge|3_0(Int32 amount)", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    [InlineData("Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext() in", "Acme.Orders.Tests.CheckoutTests.Pays_by_card")]
    public void The_test_name_is_the_frame_without_the_compilers_scaffolding(string frame, string expected)
    {
        Assert.Equal(expected, FailureText.TestFilter(frame));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Main()")]
    public void A_frame_that_names_no_test_yields_nothing(string? frame)
    {
        Assert.Null(FailureText.TestFilter(frame));
        Assert.Null(FailureText.RerunCommand(frame));
    }

    [Fact]
    public void The_digest_prints_the_rerun_line_and_the_jsonl_carries_the_name()
    {
        var digest = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure",
                        ErrorStackTrace = "   at Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext() in C:/src/CheckoutTests.cs:line 42"
                    }
                ]
            }],
            null, "TestRunReport", "3.7.0");

        Assert.Contains("Re-run: `dotnet test --filter \"FullyQualifiedName~Acme.Orders.Tests.CheckoutTests.Pays_by_card\"`", digest.Markdown);

        var failure = FailuresJsonl.Failures(digest.Jsonl).Single();
        Assert.Equal("Acme.Orders.Tests.CheckoutTests.Pays_by_card", failure.GetProperty("testName").GetString());
        Assert.Equal("dotnet test --filter \"FullyQualifiedName~Acme.Orders.Tests.CheckoutTests.Pays_by_card\"", failure.GetProperty("rerun").GetString());
    }

    [Fact]
    public void A_failure_without_a_trace_has_null_name_and_command_in_the_jsonl()
    {
        var digest = FailuresDigestGenerator.Generate(
            [new Feature { DisplayName = "Checkout", Scenarios = [new Scenario { Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "boom" }] }],
            null, "TestRunReport", "3.7.0");

        var failure = FailuresJsonl.Failures(digest.Jsonl).Single();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, failure.GetProperty("testName").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, failure.GetProperty("rerun").ValueKind);
        Assert.DoesNotContain("Re-run:", digest.Markdown);
    }

    [Fact]
    public void The_rerun_command_is_a_contains_match_so_a_parameterised_test_reruns_with_every_row()
    {
        Assert.Equal(
            "dotnet test --filter \"FullyQualifiedName~Acme.Orders.Tests.CheckoutTests.Pays_by_card\"",
            FailureText.RerunCommand("Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext()"));
    }
}
