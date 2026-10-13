#:package Kronikol.Extensions.Otlp@4.18.1
// Does OtlpTap in forward mode send a request whose target names another host to that host? ForwardAsync builds
// new HttpRequestMessage(method, request.Target) from the raw request-target (OtlpTap.cs L393 at 34145c44), and its
// HttpClient's BaseAddress is ForwardBaseUri: a "//host/x" target is a network-path reference there, and an
// absolute-form target ("http://host/x") is an absolute URI that BaseAddress never touches.
// Run: dotnet run --file probe-otlp-network-path.cs
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

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
var tapPort = FreePort();
await using var tap = new OtlpTap(new OtlpTapOptions
{
    ListenPort = tapPort, ForwardBaseUri = new Uri($"http://127.0.0.1:{configuredPort}"), Sink = new NoSink(),
});
await tap.StartAsync();
Console.WriteLine($"Kronikol.Extensions.Otlp {typeof(OtlpTap).Assembly.GetName().Version}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

foreach (var (method, target) in new[]
         {
             ("POST", "/v1/traces"),
             ("POST", $"//127.0.0.1:{otherPort}/v1/traces"),
             ("POST", $"http://127.0.0.1:{otherPort}/v1/traces"),
             ("GET", $"http://127.0.0.1:{otherPort}/anything"),
         })
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(IPAddress.Loopback, tapPort);
    var stream = tcp.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes($"{method} {target} HTTP/1.1\r\nHost: localhost:{tapPort}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
    using var reader = new StreamReader(stream, Encoding.ASCII);
    var answer = await reader.ReadToEndAsync();
    Console.WriteLine($"{method} {target,-44} -> client got: {answer.Split("\r\n")[0]} / body '{answer.Split("\r\n\r\n").LastOrDefault()}'");
}
await Task.Delay(300);
lock (seen) foreach (var s in seen) Console.WriteLine($"   {s}");

sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
