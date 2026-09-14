using System.Diagnostics;
using System.Text;

// worker <path> <label> <count> <mode> <lineBytes>
if (args.Length > 0 && args[0] == "worker")
{
    var path = args[1]; var label = args[2];
    var count = int.Parse(args[3]); var mode = args[4]; var lineBytes = int.Parse(args[5]);
    var rnd = new Random(label.GetHashCode());
    for (var i = 0; i < count; i++)
    {
        var head = $"{{\"p\":\"{label}\",\"i\":{i},\"d\":\"";
        var tail = "\"}\n";
        var pad = Math.Max(1, lineBytes - head.Length - tail.Length);
        var line = head + new string('x', pad) + tail;
        var bytes = Encoding.UTF8.GetBytes(line);

        if (mode == "lock")
        {
            var ok = false;
            for (var attempt = 0; attempt < 200 && !ok; attempt++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                    fs.Write(bytes); fs.Flush(true); ok = true;
                }
                catch (IOException) { Thread.Sleep(rnd.Next(1, 6)); }
            }
            if (!ok) Console.Error.WriteLine($"{label}: gave up on line {i}");
        }
        else // nolock: plain append, FileShare.ReadWrite - what a naive implementation does
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(bytes); fs.Flush(true);
        }
    }
    return 0;
}

// driver <dir> <procs> <count> <mode> <lineBytes>
var dir = args.Length > 0 ? args[0] : ".";
var procs = args.Length > 1 ? int.Parse(args[1]) : 8;
var perProc = args.Length > 2 ? int.Parse(args[2]) : 40;
var m = args.Length > 3 ? args[3] : "nolock";
var lb = args.Length > 4 ? int.Parse(args[4]) : 4300;
var file = Path.Combine(dir, $"ledger-{m}-{lb}.jsonl");
if (File.Exists(file)) File.Delete(file);

var exe = Environment.ProcessPath!;
var dll = System.Reflection.Assembly.GetEntryAssembly()!.Location;
var running = new List<Process>();
var sw = Stopwatch.StartNew();
for (var p = 0; p < procs; p++)
{
    var psi = new ProcessStartInfo();
    if (exe.EndsWith("dotnet") || exe.EndsWith("dotnet.exe"))
    { psi.FileName = exe; psi.ArgumentList.Add(dll); }
    else psi.FileName = exe;
    foreach (var a in new[] { "worker", file, "P" + p, perProc.ToString(), m, lb.ToString() })
        psi.ArgumentList.Add(a);
    psi.RedirectStandardError = true;
    running.Add(Process.Start(psi)!);
}
var errs = new List<string>();
foreach (var pr in running) { errs.Add(pr.StandardError.ReadToEnd()); pr.WaitForExit(); }
sw.Stop();

var expected = procs * perProc;
var raw = File.ReadAllText(file);
var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
var wellFormed = 0; var torn = 0; var perProcess = new Dictionary<string, int>();
foreach (var line in lines)
{
    var okShape = line.StartsWith("{\"p\":\"P") && line.EndsWith("\"}") && line.Length == lb - 1;
    if (okShape)
    {
        wellFormed++;
        var label = line.Substring(6, line.IndexOf('"', 6) - 6);
        perProcess[label] = perProcess.GetValueOrDefault(label) + 1;
    }
    else torn++;
}
var interleaved = raw.Contains("xx{\"p\"") || raw.Contains("\"}{\"p\"");
Console.WriteLine($"MODE={m} LINE={lb}B PROCS={procs} EACH={perProc} ELAPSED={sw.ElapsedMilliseconds}ms");
Console.WriteLine($"  expected lines   : {expected}");
Console.WriteLine($"  lines in file    : {lines.Length}");
Console.WriteLine($"  well-formed      : {wellFormed}");
Console.WriteLine($"  TORN/malformed   : {torn}");
Console.WriteLine($"  mid-line splice  : {interleaved}");
Console.WriteLine($"  bytes            : {new FileInfo(file).Length} (expected {(long)expected * lb})");
Console.WriteLine($"  complete procs   : {perProcess.Count(k => k.Value == perProc)}/{procs}");
foreach (var e in errs.Where(e => e.Length > 0)) Console.WriteLine("  stderr: " + e.Trim());
return torn == 0 && lines.Length == expected ? 0 : 1;
