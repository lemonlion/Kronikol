// line-refs-probe.cs, for plans/TAP_CALLER_TIME_LIMITS_PLAN.md (#173): three facts about the code that the sweep's
// citation check (notes-line-refs.md) settled by running. Run: `dotnet run line-refs-probe.cs`.
using System.Globalization;
using System.Net;
using System.Net.Sockets;

// 1. Content-Length values, parsed exactly as HttpRequestReader.cs L92-94 parses them.
foreach (var v in new[] { "abc", "-5", "10, 10", "3000000000", "+5", "0", "2147483648", "2147483647" })
{
    var ok = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length);
    Console.WriteLine($"Content-Length '{v}': TryParse={ok}, value={length}, takes body path={ok && length > 0}");
}

// 2. The read size Stream.CopyToAsync(Stream) asks a non-seekable source for.
var src = new RecordingStream(300_000);
await src.CopyToAsync(new MemoryStream());
Console.WriteLine($"CopyToAsync read sizes requested: {string.Join(", ", src.Requested.Distinct())}");

// 3. Setting NoDelay on an accepted socket whose peer has reset (OtlpTap.cs L281).
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
for (var round = 0; round < 3; round++)
{
    var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port);
    var accepted = await listener.AcceptTcpClientAsync();
    client.Client.LingerState = new LingerOption(true, 0);
    client.Client.Close(); // RST
    await Task.Delay(200);
    try
    {
        accepted.NoDelay = true;
        Console.WriteLine($"NoDelay after peer reset (round {round}): no exception");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"NoDelay after peer reset (round {round}): {ex.GetType().Name}: {ex.Message}");
    }
    accepted.Dispose();
}
listener.Stop();
Console.WriteLine($"OS: {Environment.OSVersion}, runtime {Environment.Version}");

sealed class RecordingStream(int total) : Stream
{
    private int _left = total;
    public List<int> Requested { get; } = [];
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Take(count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Take(buffer.Length));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Take(count));
    private int Take(int count) { Requested.Add(count); var n = Math.Min(count, _left); _left -= n; return n; }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
