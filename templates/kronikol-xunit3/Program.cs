// TODO: Remove this file once you add a <ProjectReference> to your real API project.
// This placeholder allows the template to compile and run immediately.
namespace KronikolComponentTests;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/", () => "Hello from SERVICE_NAME");
        await app.RunAsync();
    }
}
