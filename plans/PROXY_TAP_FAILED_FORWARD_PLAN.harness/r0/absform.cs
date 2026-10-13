#:package Kronikol.Extensions.ProxyTap@4.19.0
// Who answers an absolute-form target naming another local HttpListener, sent to the tap's port?
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.ProxyTap;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
HttpListener Serve(string prefix, string name)
{
    var l = new HttpListener(); l.Prefixes.Add(prefix); l.Start();
    _ = Task.Run(async () => { while (l.IsListening) { HttpListenerContext c; try { c = await l.GetContextAsync(); } catch { return; }
        Console.WriteLine($"     {name} got {c.Request.HttpMethod} {c.Request.RawUrl} (Url {c.Request.Url}, local port {c.Request.LocalEndPoint.Port})");
        var b = Encoding.UTF8.GetBytes(name); c.Response.ContentLength64 = b.Length; try { await c.Response.OutputStream.WriteAsync(b); c.Response.Close(); } catch { } } });
    return l;
}
static async Task<string> Raw(int port, string target)
{
    using var tcp = new TcpClient(); await tcp.ConnectAsync("localhost", port);
    var s = tcp.GetStream();
    await s.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
    var all = await new StreamReader(s).ReadToEndAsync();
    return all.Split("\r\n")[0] + " / " + (all.Split("\r\n\r\n", 2) is { Length: 2 } p ? p[1] : "");
}
Console.WriteLine($".NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
foreach (var host in new[] { "localhost", "127.0.0.1" })
{
    int cfg = FreePort(), oth = FreePort(), tapPort = FreePort();
    using var c = Serve($"http://{host}:{cfg}/", "configured");
    using var o = Serve($"http://{host}:{oth}/", "OTHER");
    await using var tap = new ProxyTap(new ProxyTapOptions { ListenPort = tapPort, ListenHost = host, ForwardBaseUri = new Uri($"http://{host}:{cfg}"), CallerName = "a", ServiceName = "b", EmitActivities = false });
    await tap.StartAsync();
    foreach (var t in new[] { $"http://{host}:{oth}/abs-other", $"http://localhost:{oth}/abs-other-localhost", $"http://127.0.0.1:{oth}/abs-other-ip", $"http://{host}:{tapPort}/abs-self" })
    {
        Console.WriteLine($"  [{host} prefixes] GET {t} -> {await Raw(tapPort, t)}");
        await Task.Delay(200);
    }
}
