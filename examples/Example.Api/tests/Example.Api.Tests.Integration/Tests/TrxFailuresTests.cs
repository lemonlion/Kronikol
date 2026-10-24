using Example.Api.Tests.Integration.Helpers;

namespace Example.Api.Tests.Integration.Tests;

/// <summary>
/// A nested run's results file, read when the run fails. A test assembly's cleanup failure reaches the console as one
/// line naming the exception's type ("[Test Assembly Cleanup Failure (...)] Xunit.Sdk.TestPipelineException", the
/// ReqNRoll xUnit3 example on CI, 2026-10-13, 2026-10-20 and 2026-10-24); the results file has that line as a run
/// message and the message and stack trace in the run's output. A failure the adapter attaches to several tests is read
/// once.
/// </summary>
public class TrxFailuresTests
{
    private const string Cleanup = "[Test Assembly Cleanup Failure (/x/Example.dll)]: Xunit.Sdk.TestPipelineException : Assembly fixture threw in DisposeAsync\n---- System.IO.IOException : The process cannot access the file 'TestRunReport.html'";

    private static string Trx(string results, string runInfos = "", string stdOut = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>{results}</Results>
          <ResultSummary outcome="Failed"><Output><StdOut>{stdOut}</StdOut></Output><RunInfos>{runInfos}</RunInfos></ResultSummary>
        </TestRun>
        """;

    [Fact]
    public void A_test_assembly_cleanup_failure_is_read_with_its_inner_exception_and_stack_trace_from_the_run_s_output()
    {
        // as xunit.runner.visualstudio 3.1.5 writes it (an [AfterTestRun] hook made to throw, 2026-10-24): every test
        // passed, the run's message names the exception's type alone, and its message and stack trace are in the run's
        // own output, on no test
        var path = Write(Trx(
            """<UnitTestResult testName="A" outcome="Passed" />""",
            """<RunInfo computerName="m" outcome="Error"><Text>[xUnit.net 00:00:02.65]     [Test Assembly Cleanup Failure (/x/Example.dll)] Xunit.Sdk.TestPipelineException</Text></RunInfo>""",
            """
            [xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.11)&#xD;
            [xUnit.net 00:00:00.36]   Discovering: Example&#xD;
            [xUnit.net 00:00:00.82]   Discovered:  Example&#xD;
            [xUnit.net 00:00:01.18]   Starting:    Example&#xD;
            [xUnit.net 00:00:02.65]       Assembly fixture type 'Example_XUnit3AssemblyFixture' threw in DisposeAsync&#xD;
            [xUnit.net 00:00:02.66]       ---- System.InvalidOperationException : thrown in AfterTestRun&#xD;
            [xUnit.net 00:00:02.66]       Stack Trace:&#xD;
            [xUnit.net 00:00:02.66]         ----- Inner Stack Trace -----&#xD;
            [xUnit.net 00:00:02.66]         /x/Hooks/TestSetupHooks.cs(78,0): at Example.Hooks.TestSetupHooks.AfterTestRun()&#xD;
            [xUnit.net 00:00:02.70]   Finished:    Example&#xD;
            """));
        try
        {
            var read = TrxFailures.Read(path);

            Assert.Contains("[Test Assembly Cleanup Failure (/x/Example.dll)] Xunit.Sdk.TestPipelineException", read);
            Assert.Contains("Assembly fixture type 'Example_XUnit3AssemblyFixture' threw in DisposeAsync", read);
            Assert.Contains("---- System.InvalidOperationException : thrown in AfterTestRun", read);
            Assert.Contains("/x/Hooks/TestSetupHooks.cs(78,0): at Example.Hooks.TestSetupHooks.AfterTestRun()", read);
            Assert.True(read.IndexOf("] Xunit.Sdk.TestPipelineException", StringComparison.Ordinal) < read.IndexOf("threw in DisposeAsync", StringComparison.Ordinal),
                "the run's message comes before its output");
            // the adapter's own progress lines say nothing about the failure
            Assert.DoesNotContain("VSTest Adapter", read);
            Assert.DoesNotContain("Discovering:", read);
            Assert.DoesNotContain("Starting:", read);
            Assert.DoesNotContain("Finished:", read);
            Assert.DoesNotContain("\r", read);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_run_whose_output_holds_only_the_adapter_s_progress_adds_nothing_for_it()
    {
        var path = Write(Trx(Failed("A", "a test failed", "at A()"), stdOut: """
            [xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.11)&#xD;
            [xUnit.net 00:00:00.36]   Discovering: Example&#xD;
            [xUnit.net 00:00:00.82]   Discovered:  Example&#xD;
            [xUnit.net 00:00:01.18]   Starting:    Example&#xD;
            [xUnit.net 00:00:02.70]   Finished:    Example&#xD;
            """));
        try { Assert.Equal("a test failed\nat A()", TrxFailures.Read(path)); }
        finally { File.Delete(path); }
    }

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
