#:package ZstdSharp.Port@0.8.8
using System.Diagnostics;
using System.IO.Compression;
using ZstdSharp;
var data = File.ReadAllBytes(args[0]);
Console.WriteLine($"content {data.Length:N0}  .NET {Environment.Version}");
for (var round = 0; round < 2; round++) {
  var sw = Stopwatch.StartNew();
  byte[] z; using (var m = new MemoryStream()) { using (var c = new CompressionStream(m, 9, leaveOpen: true)) c.Write(data); z = m.ToArray(); }
  var tc = sw.Elapsed.TotalMilliseconds; sw.Restart();
  long n; using (var s = new DecompressionStream(new MemoryStream(z))) { using var o = new MemoryStream(data.Length); s.CopyTo(o); n = o.Length; }
  var td = sw.Elapsed.TotalMilliseconds; sw.Restart();
  byte[] g; using (var m = new MemoryStream()) { using (var c = new GZipStream(m, CompressionLevel.Optimal, true)) c.Write(data); g = m.ToArray(); }
  var tg = sw.Elapsed.TotalMilliseconds; sw.Restart();
  using (var s = new GZipStream(new MemoryStream(g), CompressionMode.Decompress)) { using var o = new MemoryStream(data.Length); s.CopyTo(o); }
  var tgd = sw.Elapsed.TotalMilliseconds;
  if (round == 1) Console.WriteLine($"ZstdSharp 9: {z.Length:N0} write {tc:F0} ms read {td:F0} ms | gzip: {g.Length:N0} write {tg:F0} ms read {tgd:F0} ms | decoded {n:N0}");
}
