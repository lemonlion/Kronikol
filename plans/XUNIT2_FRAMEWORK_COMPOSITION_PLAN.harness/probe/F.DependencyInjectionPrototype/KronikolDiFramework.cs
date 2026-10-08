using System.Reflection;
using Probe.Prototype;
using Xunit;
using Xunit.Abstractions;
using Xunit.DependencyInjection;

namespace Probe.Di;

/// <summary>
/// Composes instead of subclassing: DependencyInjectionTestFramework is sealed. Holds one, forwards the
/// source information provider, discovery and disposal to it, and hands back its executor wrapped by the
/// prototype. LongLivedMarshalByRefObject for the same reason as the prototype's sink (xUnit3000).
/// </summary>
public sealed class KronikolDiFramework : LongLivedMarshalByRefObject, ITestFramework
{
    private readonly DependencyInjectionTestFramework _inner;

    public KronikolDiFramework(IMessageSink messageSink) => _inner = new DependencyInjectionTestFramework(messageSink);

    public ISourceInformationProvider SourceInformationProvider
    {
        set => _inner.SourceInformationProvider = value;
    }

    public ITestFrameworkDiscoverer GetDiscoverer(IAssemblyInfo assembly) => _inner.GetDiscoverer(assembly);

    public ITestFrameworkExecutor GetExecutor(AssemblyName assemblyName) => Prototype.Prototype.Wrap(_inner.GetExecutor(assemblyName));

    public void Dispose() => _inner.Dispose();
}
