using System.Reflection;
using Kronikol.xUnit2;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using Xunit.DependencyInjection;

[assembly: TestFramework("Kronikol.Tests.xUnit2.Fixtures.KronikolDependencyInjectionFramework", "DependencyInjection")]

namespace Kronikol.Tests.xUnit2.Fixtures;

/// <summary>
/// The wiki's composition recipe for a sealed framework: an ITestFramework of the suite's own holds the package's,
/// passes everything through, and wraps the executor it hands out.
/// </summary>
public sealed class KronikolDependencyInjectionFramework(IMessageSink messageSink) : LongLivedMarshalByRefObject, ITestFramework
{
    private readonly DependencyInjectionTestFramework _inner = new(messageSink);

    public ISourceInformationProvider SourceInformationProvider
    {
        set => _inner.SourceInformationProvider = value;
    }

    public ITestFrameworkDiscoverer GetDiscoverer(IAssemblyInfo assembly) => _inner.GetDiscoverer(assembly);

    public ITestFrameworkExecutor GetExecutor(AssemblyName assemblyName) => _inner.GetExecutor(assemblyName).WithKronikolReporting();

    public void Dispose() => _inner.Dispose();
}

public interface IGreeter
{
    string Greet(string name);
}

public sealed class Greeter : IGreeter
{
    public string Greet(string name) => $"hello {name}";
}

/// <summary>Xunit.DependencyInjection's startup, named by XunitStartupFullName in the project file.</summary>
public class Startup
{
    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IGreeter, Greeter>();
}

/// <summary>Takes the registered service by constructor injection.</summary>
[TestTracking]
public class Injected(IGreeter greeter, ITestOutputHelper output)
{
    [Fact]
    public async Task Receives_the_injected_service()
    {
        output.WriteLine($"injected: {greeter.Greet("fixture")}");
        await FixtureHttp.GetAsync("/injected", output);
        Assert.Equal("hello fixture", greeter.Greet("fixture"));
    }
}
