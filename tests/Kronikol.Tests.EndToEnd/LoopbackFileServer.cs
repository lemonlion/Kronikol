using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Serves one directory over plain <c>http://127.0.0.1:&lt;port&gt;</c>, so a test can open a report the way a team hosts
/// one instead of from <c>file://</c>. Only the report's own files come from here; the engine still comes from the real
/// CDN, so nothing the page requests is mocked. GET only, one request per connection.
/// </summary>
internal sealed class LoopbackFileServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _root;
    private readonly Task _loop;
    private int _served;

    private LoopbackFileServer(string root)
    {
        _root = Path.GetFullPath(root);
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    public static LoopbackFileServer Start(string root) => new(root);

    /// <summary>How many files it has answered with a 200.</summary>
    public int Served => Volatile.Read(ref _served);

    /// <summary>
    /// The file's address. <paramref name="host"/> is any name the browser resolves to the loopback address (a test browser
    /// can map one with <c>--host-resolver-rules</c>): a name other than <c>localhost</c> is not a secure context.
    /// </summary>
    public string UrlOf(string fileName, string host = "127.0.0.1") =>
        $"http://{host}:{((IPEndPoint)_listener.LocalEndpoint).Port}/{Uri.EscapeDataString(fileName)}";

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) when (_stop.IsCancellationRequested) { return; }
            _ = Task.Run(() => Answer(client));
        }
    }

    private async Task Answer(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { /* headers */ }

                var parts = requestLine?.Split(' ') ?? [];
                var path = parts.Length >= 2 && parts[0] == "GET"
                    ? Path.GetFullPath(Path.Combine(_root, Uri.UnescapeDataString(parts[1].Split('?')[0].TrimStart('/'))))
                    : null;
                if (path is null || !path.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                {
                    await stream.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
                    return;
                }

                var body = await File.ReadAllBytesAsync(path);
                var type = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".js" => "application/javascript; charset=utf-8",
                    ".css" => "text/css; charset=utf-8",
                    ".json" => "application/json",
                    ".svg" => "image/svg+xml",
                    ".png" => "image/png",
                    _ => "application/octet-stream",
                };
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                await stream.WriteAsync(body);
                Interlocked.Increment(ref _served);
            }
            catch (IOException) { /* the browser closed the connection */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _loop; } catch { /* stopping */ }
        _stop.Dispose();
    }
}
