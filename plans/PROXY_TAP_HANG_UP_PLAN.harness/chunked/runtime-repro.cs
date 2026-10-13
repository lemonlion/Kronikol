// runtime-repro.cs: the managed HttpListener (Linux, macOS) reads a chunked request body that its client cut short
// with a FIN as if it were whole. `dotnet run runtime-repro.cs`, no packages; the report to dotnet/runtime (plan Q6).
// The client sends one chunk and no last chunk, then shuts its socket down for sending. A Content-Length body cut
// short the same way is a failed read on every platform; a chunked one completes with the bytes that arrived.
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}, HttpListener {(OperatingSystem.IsWindows() ? "http.sys" : "managed")}");

var port = FreePort();
using var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();

foreach (var (label, head, body) in new[]
{
    ("Content-Length: 20, 10 bytes sent", "Content-Length: 20", "0123456789"),
    ("chunked, one 10-byte chunk sent, no last chunk", "Transfer-Encoding: chunked", "a\r\n0123456789\r\n"),
    ("chunked, 5 of a 10-byte chunk sent", "Transfer-Encoding: chunked", "a\r\n01234"),
})
{
    using var client = new TcpClient();
    await client.ConnectAsync("localhost", port);
    await client.Client.SendAsync(Encoding.ASCII.GetBytes($"POST /upload HTTP/1.1\r\nHost: localhost:{port}\r\n{head}\r\n\r\n{body}"));
    var context = await listener.GetContextAsync();
    client.Client.Shutdown(SocketShutdown.Send); // the client stops sending: a FIN

    string outcome;
    try
    {
        using var read = new MemoryStream();
        await context.Request.InputStream.CopyToAsync(read).WaitAsync(TimeSpan.FromSeconds(5));
        outcome = $"the read completed, {read.Length} bytes: \"{Encoding.ASCII.GetString(read.ToArray())}\"";
    }
    catch (Exception ex)
    {
        outcome = $"the read threw {ex.GetType().FullName}: {ex.Message}";
    }

    Console.WriteLine($"{label}: {outcome}");
    context.Response.Abort();
}

static int FreePort()
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}
