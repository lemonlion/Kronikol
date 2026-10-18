#:package ZstdSharp.Port@0.8.8
using ZstdSharp;
var data = File.ReadAllBytes(args[0]);
var blob = File.ReadAllBytes(args[1]);
// One-shot unwrap over marker + frame.
try { using var d = new Decompressor(); var o = d.Unwrap(blob).ToArray(); Console.WriteLine($"Unwrap(marker+frame) equals: {o.AsSpan().SequenceEqual(data)}"); }
catch (Exception e) { Console.WriteLine($"Unwrap(marker+frame) threw {e.GetType().Name}: {e.Message}"); }
// Streaming over marker + frame.
try { using var s = new DecompressionStream(new MemoryStream(blob)); using var m = new MemoryStream(); s.CopyTo(m); Console.WriteLine($"DecompressionStream(marker+frame) equals: {m.ToArray().AsSpan().SequenceEqual(data)}"); }
catch (Exception e) { Console.WriteLine($"DecompressionStream(marker+frame) threw {e.GetType().Name}: {e.Message}"); }
// Streaming over the frame alone, after the reader has consumed the 24-byte marker itself.
using (var s = new DecompressionStream(new MemoryStream(blob, 24, blob.Length - 24))) { using var m = new MemoryStream(); s.CopyTo(m); Console.WriteLine($"DecompressionStream(frame after marker) equals: {m.ToArray().AsSpan().SequenceEqual(data)}"); }
// Corruption and truncation.
var bad = (byte[])blob.Clone(); bad[bad.Length / 2] ^= 0xFF;
try { using var s = new DecompressionStream(new MemoryStream(bad, 24, bad.Length - 24)); s.CopyTo(Stream.Null); Console.WriteLine("corrupted: NOT detected"); }
catch (Exception e) { Console.WriteLine($"corrupted: {e.GetType().FullName}"); }
try { using var s = new DecompressionStream(new MemoryStream(blob, 24, blob.Length - 1024)); s.CopyTo(Stream.Null); Console.WriteLine("truncated: NOT detected (stream ended quietly)"); }
catch (Exception e) { Console.WriteLine($"truncated: {e.GetType().FullName}"); }
// Streaming compression with checksum, no pledged size: does the frame carry a content size?
using (var m = new MemoryStream()) {
  using (var c = new CompressionStream(m, 9, leaveOpen: true)) { c.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1); c.Write(data); }
  var f = m.ToArray(); Console.WriteLine($"streamed frame {f.Length:N0} bytes, header byte FHD=0x{f[4]:X2} (content-size flag bits {f[4] >> 6})");
}
