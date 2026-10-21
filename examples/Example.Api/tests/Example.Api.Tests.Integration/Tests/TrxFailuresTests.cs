using Example.Api.Tests.Integration.Helpers;

namespace Example.Api.Tests.Integration.Tests;

/// <summary>
/// A nested run's results file, read when the run fails. A test assembly's cleanup failure reaches the console as one
/// line naming the exception's type ("[Test Assembly Cleanup Failure (...)] Xunit.Sdk.TestPipelineException", the
/// ReqNRoll xUnit3 example on CI, 2026-10-13 and 2026-10-20), while the VSTest adapter attaches its message and stack
/// trace to each of the assembly's tests in the results file.
/// </summary>
public class TrxFailuresTests
{
    private const string Cleanup = "[Test Assembly Cleanup Failure (/x/Example.dll)]: Xunit.Sdk.TestPipelineException : Assembly fixture threw in DisposeAsync\n---- System.IO.IOException : The process cannot access the file 'TestRunReport.html'";

    private static string Trx(string results, string runInfos = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>{results}</Results>
          <ResultSummary outcome="Failed"><RunInfos>{runInfos}</RunInfos></ResultSummary>
        </TestRun>
        """;

    private static string Failed(string name, string message, string stack) =>
        $"""<UnitTestResult testName="{name}" outcome="Failed"><Output><ErrorInfo><Message>{System.Security.SecurityElement.Escape(message)}</Message><StackTrace>{stack}</StackTrace></ErrorInfo></Output></UnitTestResult>""";

    private static string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"trx-{Guid.NewGuid():N}.trx");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_failure_attached_to_every_test_is_read_once_with_its_stack_trace_and_the_run_messages_after_it()
    {
        var path = Write(Trx(
            Failed("A", Cleanup, "at Fixture.DisposeAsync()") + Failed("B", Cleanup, "at Fixture.DisposeAsync()")
                + """<UnitTestResult testName="C" outcome="Passed" />""",
            """<RunInfo computerName="m" outcome="Error"><Text>[Test Assembly Cleanup Failure (/x/Example.dll)] Xunit.Sdk.TestPipelineException</Text></RunInfo>"""));
        try
        {
            var read = TrxFailures.Read(path);

            Assert.Equal(1, CountOf(read, "The process cannot access the file 'TestRunReport.html'"));
            Assert.Contains("at Fixture.DisposeAsync()", read);
            Assert.Contains("[Test Assembly Cleanup Failure (/x/Example.dll)] Xunit.Sdk.TestPipelineException", read);
            Assert.True(read.IndexOf("IOException", StringComparison.Ordinal) < read.IndexOf("] Xunit.Sdk.TestPipelineException", StringComparison.Ordinal),
                "the failures come before the run's messages");
            Assert.DoesNotContain("testName", read);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Two_different_failures_are_both_read_and_a_passed_test_adds_nothing()
    {
        var path = Write(Trx(Failed("A", "first failure", "at A()") + Failed("B", "second failure", "at B()")
            + """<UnitTestResult testName="C" outcome="Passed"><Output><ErrorInfo><Message>not a failure</Message></ErrorInfo></Output></UnitTestResult>"""));
        try
        {
            var read = TrxFailures.Read(path);

            Assert.Contains("first failure", read);
            Assert.Contains("second failure", read);
            Assert.DoesNotContain("not a failure", read);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_or_unreadable_results_file_adds_nothing()
    {
        Assert.Equal("", TrxFailures.Read(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.trx")));

        var path = Write("<TestRun><Results>");
        try { Assert.Equal("", TrxFailures.Read(path)); }
        finally { File.Delete(path); }
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
