#:package Kronikol.Extensions.ProxyTap@4.18.1
// Does a request path that starts with "//" leave the tap's ForwardBaseUri? HttpRequestMessage takes the path as a
// relative URI, and HttpClient resolves it against BaseAddress (RFC 3986), where "//host/x" is a network-path
// reference that replaces the authority.
// Run: dotnet run --file probe-network-path.cs
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

// The configured upstream, and a second server the tap was never pointed at.
HttpListener Serve(int port, string name, List<string> seen)
{
    var l = new HttpListener(); l.Prefixes.Add($"http://127.0.0.1:{port}/"); l.Start();
    _ = Task.Run(async () =>
    {
        while (l.IsListening)
        {
            HttpListenerContext c; try { c = await l.GetContextAsync(); } catch { return; }
            lock (seen) seen.Add($"{name} got {c.Request.HttpMethod} {c.Request.RawUrl}");
            var body = Encoding.UTF8.GetBytes(name); c.Response.ContentLength64 = body.Length;
            await c.Response.OutputStream.WriteAsync(body); c.Response.Close();
        }
    });
    return l;
}

var seen = new List<string>();
var configuredPort = FreePort(); var otherPort = FreePort();
using var configured = Serve(configuredPort, "configured", seen);
using var other = Serve(otherPort, "OTHER", seen);
var records = new List<RequestResponseLog>();
await using var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = FreePort(), ForwardBaseUri = new Uri($"http://127.0.0.1:{configuredPort}"),
    CallerName = "app", ServiceName = "api", IdentityResolver = (_, _) => ("A scenario", "t1"), Sink = new ListSink(records),
});
await tap.StartAsync();
Console.WriteLine($"Kronikol.Extensions.ProxyTap {typeof(ProxyTap).Assembly.GetName().Version}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

foreach (var target in new[] { "/plain", $"//127.0.0.1:{otherPort}/x", "//api/v1/items" })
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(IPAddress.Loopback, tap.Options.ListenPort);
    var stream = tcp.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: localhost:{tap.Options.ListenPort}\r\nConnection: close\r\n\r\n"));
    using var reader = new StreamReader(stream, Encoding.ASCII);
    var answer = await reader.ReadToEndAsync();
    Console.WriteLine($"request-target {target,-28} -> client got: {answer.Split("\r\n")[0]} / body '{answer.Split("\r\n\r\n").LastOrDefault()}'");
}
await Task.Delay(300);
lock (seen) foreach (var s in seen) Console.WriteLine($"   {s}");
lock (records) foreach (var r in records.Where(r => r.Type == RequestResponseType.Request)) Console.WriteLine($"   recorded request uri: {r.Uri}");
Console.WriteLine($"   ForwardFailures={tap.ForwardFailures}");

sealed class ListSink(List<RequestResponseLog> list) : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { lock (list) list.Add(log); }
}
