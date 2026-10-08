using System.Reflection;
using Kronikol.xUnit2;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Kronikol.Tests.xUnit2.Fixtures.AsyncBusFramework", "AsyncBus")]

namespace Kronikol.Tests.xUnit2.Fixtures;

public sealed class AsyncBusFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new AsyncBusExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink).WithKronikolReporting();
}

public sealed class AsyncBusExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
    : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
    protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    {
        using var assemblyRunner = new AsyncBusAssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
        await assemblyRunner.RunAsync();
    }
}

public sealed class AsyncBusAssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink,
    IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
{
    protected override IMessageBus CreateMessageBus() => new MessageBus(ExecutionMessageSink);
}
