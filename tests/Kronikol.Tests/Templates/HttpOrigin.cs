using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kronikol.Tests.Templates;

/// <summary>
/// A <see cref="BareOrigin"/> served over smart HTTP: <c>git http-backend</c> behind basic authentication, taking a
/// token the way github.com takes <c>GITHUB_TOKEN</c> (user <c>x-access-token</c>, the token as the password). It is
/// plans/HISTORY_ACTION_PLAN.harness/githttp.py made into C#, on a raw socket so that no URL reservation is needed on
/// Windows. Every fact about the token, a machine's credential helpers or what a read downloads needs it: a
/// <c>file://</c> origin has no authentication and no status codes.
/// <list type="bullet">
/// <item><see cref="Token"/> reads and writes.</item>
/// <item><see cref="ReadOnlyToken"/> reads, and its push is answered 403, as a fork's pull request token is.</item>
/// <item><see cref="OtherUser"/>:<see cref="OtherPassword"/> is another identity that reads and writes: the one a
/// machine's credential store holds.</item>
/// <item>A request with no credential it knows is answered 401 with a Basic challenge, which is what makes git ask
/// the machine's credential helpers for one.</item>
/// </list>
/// Every request is logged with its status, the bytes served, who it was served as and how many Authorization
/// headers it carried; no secret is logged.
/// </summary>
internal sealed class HttpOrigin : IDisposable
{
    public const string Token = "origin-token-7f3a9c2e";
    public const string ReadOnlyToken = "origin-read-only-51d2b8";
    public const string OtherUser = "someone";
    public const string OtherPassword = "machine-password";

    private readonly BareOrigin _origin;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Request> _requests = [];
    private readonly string _home;

    public HttpOrigin(BareOrigin origin)
    {
        _origin = origin;
        _home = Path.Combine(origin.Root, "http-home");
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "empty.gitconfig"), "");
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    /// <summary>What <c>github.server_url</c> would be: <c>http://127.0.0.1:&lt;port&gt;</c>.</summary>
    public string ServerUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public sealed record Request(string Method, string Path, int Status, long Bytes, string? Who, int AuthorizationHeaders);

    public IReadOnlyList<Request> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToList();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var head = await ReadHeadAsync(stream);
            if (head is null)
                return;

            var lines = head.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            var headers = lines.Skip(1).Where(l => l.Contains(':'))
                .Select(l => (Name: l[..l.IndexOf(':')].Trim(), Value: l[(l.IndexOf(':') + 1)..].Trim())).ToList();
            string? Header(string name) => headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

            if (string.Equals(Header("Expect"), "100-continue", StringComparison.OrdinalIgnoreCase))
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"));
            var body = await ReadBodyAsync(stream, Header("Transfer-Encoding"), Header("Content-Length"));

            var method = requestLine[0];
            var target = requestLine[1];
            var authorizations = headers.Where(h => string.Equals(h.Name, "Authorization", StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();
            var known = authorizations.Select(Identity).Where(i => i is not null).ToList();
            if (known.Count == 0)
            {
                await ReplyAsync(stream, method, target, 401, [("WWW-Authenticate", "Basic realm=\"origin\"")], "Unauthorized\n"u8.ToArray(), null, authorizations.Count);
                return;
            }

            var (who, canWrite) = known[0]!.Value;
            var path = target.Split('?')[0];
            var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
            if (!canWrite && (path.Contains("git-receive-pack", StringComparison.Ordinal) || query.Contains("service=git-receive-pack", StringComparison.Ordinal)))
            {
                await ReplyAsync(stream, method, target, 403, [("Content-Type", "text/plain")], "Write access to repository not granted.\n"u8.ToArray(), who, authorizations.Count);
                return;
            }

            var (status, cgiHeaders, payload) = RunBackend(method, path, query, Header("Content-Type") ?? "", Header("Content-Encoding"), Header("Git-Protocol"), who, body);
            await ReplyAsync(stream, method, target, status, cgiHeaders, payload, who, authorizations.Count);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            // The client went away.
        }
    }

    private static (string, bool)? Identity(string header)
    {
        if (!header.StartsWith("basic ", StringComparison.OrdinalIgnoreCase))
            return null;
        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
        }
        catch (FormatException)
        {
            return null;
        }

        var user = decoded.Split(':')[0];
        var password = decoded.Contains(':') ? decoded[(decoded.IndexOf(':') + 1)..] : "";
        if (user == "x-access-token" && password == Token)
            return ("token", true);
        if (user == "x-access-token" && password == ReadOnlyToken)
            return ("read-only-token", false);
        if (user == OtherUser && password == OtherPassword)
            return (OtherUser, true);
        return null;
    }

    private (int, List<(string, string)>, byte[]) RunBackend(string method, string path, string query, string contentType, string? contentEncoding,
        string? gitProtocol, string who, byte[] body)
    {
        var start = new ProcessStartInfo(GitProbe.Executable!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _origin.Root
        };
        start.ArgumentList.Add("http-backend");
        start.Environment.Clear();
        foreach (var (name, value) in ChildProcess.BaseEnvironment(_home, Path.GetTempPath(), Path.Combine(_home, "empty.gitconfig")))
            start.Environment[name] = value;
        start.Environment["PATH"] = string.Join(Path.PathSeparator, ChildProcess.SystemPath());
        start.Environment["GIT_PROJECT_ROOT"] = _origin.Root;
        start.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        start.Environment["REQUEST_METHOD"] = method;
        start.Environment["PATH_INFO"] = path;
        start.Environment["QUERY_STRING"] = query;
        start.Environment["CONTENT_TYPE"] = contentType;
        start.Environment["CONTENT_LENGTH"] = body.Length.ToString();
        start.Environment["REMOTE_USER"] = who;
        start.Environment["REMOTE_ADDR"] = "127.0.0.1";
        if (contentEncoding is not null)
            start.Environment["HTTP_CONTENT_ENCODING"] = contentEncoding;
        if (gitProtocol is not null)
            start.Environment["HTTP_GIT_PROTOCOL"] = gitProtocol;

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        process.StandardInput.BaseStream.Write(body);
        process.StandardInput.Close();
        copy.Wait(TimeSpan.FromMinutes(2));
        process.WaitForExit(120_000);
        _ = stderr.Result;

        var bytes = output.ToArray();
        var split = IndexOf(bytes, "\r\n\r\n"u8.ToArray());
        var separator = 4;
        if (split < 0)
        {
            split = IndexOf(bytes, "\n\n"u8.ToArray());
            separator = 2;
        }
        if (split < 0)
            return (500, [], bytes);

        var status = 200;
        var headers = new List<(string, string)>();
        foreach (var line in Encoding.Latin1.GetString(bytes, 0, split).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains(':')))
        {
            var name = line[..line.IndexOf(':')].Trim();
            var value = line[(line.IndexOf(':') + 1)..].Trim();
            if (string.Equals(name, "Status", StringComparison.OrdinalIgnoreCase))
                status = int.Parse(value.Split(' ')[0]);
            else
                headers.Add((name, value));
        }
        return (status, headers, bytes[(split + separator)..]);
    }

    private async Task ReplyAsync(NetworkStream stream, string method, string target, int status, List<(string Name, string Value)> headers, byte[] payload, string? who, int authorizations)
    {
        var head = new StringBuilder($"HTTP/1.1 {status} {Reason(status)}\r\n");
        foreach (var (name, value) in headers)
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        head.Append("Content-Length: ").Append(payload.Length).Append("\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()));
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
        lock (_requests)
            _requests.Add(new Request(method, target, status, payload.Length, who, authorizations));
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", _ => "Status"
    };

    private static async Task<string?> ReadHeadAsync(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) == 0)
                return head.Count == 0 ? null : throw new IOException("the connection closed inside a request's head");
            head.Add(one[0]);
            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
                return Encoding.Latin1.GetString(head.ToArray(), 0, head.Count - 4);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(NetworkStream stream, string? transferEncoding, string? contentLength)
    {
        if (string.Equals(transferEncoding, "chunked", StringComparison.OrdinalIgnoreCase))
        {
            var body = new MemoryStream();
            while (true)
            {
                var size = Convert.ToInt32((await ReadLineAsync(stream)).Split(';')[0].Trim(), 16);
                if (size == 0)
                {
                    while ((await ReadLineAsync(stream)).Length > 0) { }
                    return body.ToArray();
                }
                body.Write(await ReadExactlyAsync(stream, size));
                await ReadLineAsync(stream);
            }
        }

        return int.TryParse(contentLength, out var length) && length > 0 ? await ReadExactlyAsync(stream, length) : [];
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var line = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one) > 0)
        {
            if (one[0] == '\n')
                break;
            if (one[0] != '\r')
                line.Add(one[0]);
        }
        return Encoding.Latin1.GetString(line.ToArray());
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer);
        return buffer;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }
        return -1;
    }
}
