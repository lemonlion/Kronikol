#:package ZstdSharp.Port@0.8.6
using System.Diagnostics;
using System.IO.Compression;
var data = File.ReadAllBytes(args[0]);
Console.WriteLine($"raw {data.Length:N0}  .NET {Environment.Version}");
void Run(string name, Func<byte[], byte[]> comp, Func<byte[], byte[]> dec)
{
    comp(data); // warm up
    var sw = Stopwatch.StartNew(); var c = comp(data); var tc = sw.Elapsed.TotalMilliseconds;
    dec(c); sw.Restart(); var d = dec(c); var td = sw.Elapsed.TotalMilliseconds;
    if (d.Length != data.Length) throw new Exception(name);
    Console.WriteLine($"{name,-12} {c.Length,10:N0}  write {tc,7:F0} ms  read {td,5:F1} ms");
}
byte[] Gz(byte[] b, CompressionLevel l) { using var o = new MemoryStream(); using (var g = new GZipStream(o, l)) g.Write(b); return o.ToArray(); }
byte[] UnGz(byte[] b) { using var i = new GZipStream(new MemoryStream(b), CompressionMode.Decompress); using var o = new MemoryStream(); i.CopyTo(o); return o.ToArray(); }
byte[] Br(byte[] b, int q) { var dst = new byte[BrotliEncoder.GetMaxCompressedLength(b.Length)]; BrotliEncoder.TryCompress(b, dst, out var n, q, 24); return dst[..n]; }
byte[] UnBr(byte[] b) { using var i = new BrotliStream(new MemoryStream(b), CompressionMode.Decompress); using var o = new MemoryStream(); i.CopyTo(o); return o.ToArray(); }
byte[] Zs(byte[] b, int l) { using var c = new ZstdSharp.Compressor(l); return c.Wrap(b).ToArray(); }
byte[] UnZs(byte[] b) { using var d = new ZstdSharp.Decompressor(); return d.Unwrap(b).ToArray(); }
Run("gzip opt", b => Gz(b, CompressionLevel.Optimal), UnGz);
Run("gzip smallest", b => Gz(b, CompressionLevel.SmallestSize), UnGz);
foreach (var l in new[] { 3, 9, 12, 19 }) Run($"zstd {l}", b => Zs(b, l), UnZs);
foreach (var q in new[] { 5, 9 }) Run($"brotli {q}", b => Br(b, q), UnBr);
