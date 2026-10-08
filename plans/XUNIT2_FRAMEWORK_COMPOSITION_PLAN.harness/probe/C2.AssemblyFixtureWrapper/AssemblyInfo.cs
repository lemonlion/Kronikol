using System.Reflection;
using Kronikol.xUnit2;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions.AssemblyFixture;

[assembly: TestFramework("Probe.C2.ReportingAssemblyFixtureFramework", "C2.AssemblyFixtureWrapper")]
[assembly: TestTracking]

namespace Probe.C2;

/// <summary>What the wiki tells a suite on Xunit.Extensions.AssemblyFixture to write.</summary>
public sealed class ReportingAssemblyFixtureFramework(IMessageSink messageSink) : AssemblyFixtureFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        base.CreateExecutor(assemblyName).WithKronikolReporting();
}
