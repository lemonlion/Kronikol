using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Probe.Di;

public interface IGreeter
{
    Guid Id { get; }
    string Greet(string name);
}

public sealed class Greeter : IGreeter
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Greet(string name) => $"hello {name}";
}

/// <summary>Xunit.DependencyInjection's Startup (named by XunitStartupFullName in the project file).</summary>
public class Startup
{
    public void ConfigureServices(IServiceCollection services) => services.AddSingleton<IGreeter, Greeter>();
}

/// <summary>Takes the registered service by constructor injection and makes one tracked call.</summary>
public class DiInjected(IGreeter greeter, ITestOutputHelper output)
{
    [Fact]
    public async Task Receives_the_injected_service()
    {
        output.WriteLine($"di-service: {greeter.Id} {greeter.Greet("probe")}");
        await ProbeHttp.GetAsync("/di/injected", output);
        Assert.Equal("hello probe", greeter.Greet("probe"));
    }
}
