#:project C:/Code/Kronikol-audit136/src/Kronikol.Extensions.AtlasDataApi/Kronikol.Extensions.AtlasDataApi.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false
using Kronikol.Extensions.AtlasDataApi;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;

void Try(string name, Action a)
{
    try { a(); Console.WriteLine($"{name}: OK"); }
    catch (Exception e) { Console.WriteLine($"{name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
}

var opts = new AtlasDataApiTrackingMessageHandlerOptions { ServiceName = "Atlas", CurrentTestInfoFetcher = () => ("t", "id") };

Try("1 wiki Atlas AddHttpMessageHandler(() => new Handler(opts)) + CreateClient", () =>
{
    var s = new ServiceCollection();
    s.AddHttpClient("A").AddHttpMessageHandler(() => new AtlasDataApiTrackingMessageHandler(opts));
    s.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("A");
});
Try("2 DelegatingHandler.InnerHandler = null", () =>
{
    var h = new AtlasDataApiTrackingMessageHandler(opts);
    h.InnerHandler = null!;
});
Try("3 core TestTrackingMessageHandler recipe (InnerHandler = null!) + CreateClient", () =>
{
    var s = new ServiceCollection();
    s.AddHttpClient("B").AddHttpMessageHandler(sp =>
    {
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { CallerName = "x" });
        handler.InnerHandler = null!;
        return handler;
    });
    s.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("B");
});
Try("4 core TestTrackingMessageHandler without the null line + CreateClient", () =>
{
    var s = new ServiceCollection();
    s.AddHttpClient("C").AddHttpMessageHandler(sp => new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { CallerName = "x" }));
    s.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("C");
});
Try("5 Atlas ConfigurePrimaryHttpMessageHandler(() => new Handler(opts)) + CreateClient", () =>
{
    var s = new ServiceCollection();
    s.AddHttpClient("D").ConfigurePrimaryHttpMessageHandler(() => new AtlasDataApiTrackingMessageHandler(opts));
    s.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("D");
});
