using System.Reflection;
using Xunit.Abstractions;
using Xunit.Extensions.AssemblyFixture;

namespace Probe.Prototype;

/// <summary>
/// The suite's own framework (here Xunit.Extensions.AssemblyFixture's) with its executor wrapped. This is
/// the whole of what a suite on another framework would write: a subclass and one line.
/// </summary>
public sealed class PrototypeFramework(IMessageSink messageSink) : AssemblyFixtureFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        Prototype.Wrap(base.CreateExecutor(assemblyName));
}
