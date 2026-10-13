#:package Kronikol.Extensions.ProxyTap@4.18.1
// What a ProxyTap whose ForwardBaseUri is absolute but not http(s) does: Validate (ProxyTapOptions.cs L162-163)
// accepts it. Run: dotnet run --file probe-scheme.cs
using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.ProxyTap;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

foreach (var forward in new[] { "ftp://127.0.0.1:21/", "file:///tmp/" })
{
    var port = FreePort();
    await using var tap = new ProxyTap(new ProxyTapOptions { ListenPort = port, ForwardBaseUri = new Uri(forward), CallerName = "app", ServiceName = "api" });
    try { await tap.StartAsync(); }
    catch (Exception ex) { Console.WriteLine($"{forward}: StartAsync threw {ex.GetType().Name}: {ex.Message}"); continue; }
    using var http = new HttpClient();
    var answer = await http.GetAsync($"http://localhost:{port}/x");
    Console.WriteLine($"{forward}: StartAsync accepted it; GET /x -> {(int)answer.StatusCode} {await answer.Content.ReadAsStringAsync()}; ForwardFailures={tap.ForwardFailures}");
}
