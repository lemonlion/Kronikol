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
