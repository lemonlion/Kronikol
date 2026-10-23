#:package ZstdSharp.Port@0.8.8
// Compress time of ZstdSharp (the code Kronikol carries as a source copy) at level 9, window 2^23, checksum on.
using System.Diagnostics;
using ZstdSharp;
using ZstdSharp.Unsafe;

foreach (var path in args)
{
    var data = File.ReadAllBytes(path);
    for (var run = 0; run < 3; run++)
    {
        using var c = new Compressor(9);
        c.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, 23);
        c.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
        var sw = Stopwatch.StartNew();
        var output = c.Wrap(data);
        sw.Stop();
        Console.WriteLine($"{Path.GetFileName(path),-24} {data.Length,14:N0} -> {output.Length,12:N0}  {sw.Elapsed.TotalMilliseconds,8:N0} ms (run {run + 1})");
    }
}
