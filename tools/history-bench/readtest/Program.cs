// Reader-vs-writer on the ledger (CROSS_RUN_HISTORY_PLAN §3.4 / §6.5).
// §6.5 measured writer-vs-writer. This measures what a READER sees while a writer appends —
// the realistic case, because several test projects in one repo share one ledger and run in parallel.
using System.Diagnostics;
using System.Text;

if (args.Length > 0 && args[0] == "writer")
{
    var p = args[1]; var n = int.Parse(args[2]); var share = args[3];
    var fsShare = share switch { "none" => FileShare.None, "read" => FileShare.Read, _ => FileShare.ReadWrite };
    for (var i = 0; i < n; i++)
    {
        var line = "{\"t\":\"run\",\"i\":" + i + ",\"pad\":\"" + new string('x', 4000) + "\"}\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        for (var a = 0; a < 500; a++)
        {
            try { using var fs = new FileStream(p, FileMode.Append, FileAccess.Write, fsShare); fs.Write(bytes); fs.Flush(true); break; }
            catch (IOException) { Thread.Sleep(1); }
        }
        Thread.Sleep(1);
    }
    return 0;
}

var path = args[0]; var lines = int.Parse(args[1]); var writerShare = args[2]; var readerShare = args[3];
File.Delete(path);
File.WriteAllText(path, "{\"t\":\"header\"}\n");
var exe = Environment.ProcessPath!;
var dll = System.Reflection.Assembly.GetEntryAssembly()!.Location;
var psi = new ProcessStartInfo(exe);
if (exe.EndsWith("dotnet.exe") || exe.EndsWith("dotnet")) psi.ArgumentList.Add(dll);
psi.ArgumentList.Add("writer"); psi.ArgumentList.Add(path);
psi.ArgumentList.Add(lines.ToString()); psi.ArgumentList.Add(writerShare);
var w = Process.Start(psi)!;

int reads = 0, denied = 0, torn = 0, ok = 0; var maxSeen = -1;
var rShare = readerShare switch { "read" => FileShare.Read, "rw" => FileShare.ReadWrite, _ => FileShare.None };
var sw = Stopwatch.StartNew();
while (!w.HasExited || sw.ElapsedMilliseconds < 50)
{
    reads++;
    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, rShare);
        using var sr = new StreamReader(fs);
        string? last = null; int count = 0; string? line;
        while ((line = sr.ReadLine()) != null) { last = line; count++; }
        // a torn tail is a final line that is not valid JSON-ish (no closing brace)
        if (last is not null && !last.TrimEnd().EndsWith("}")) torn++;
        else { ok++; if (last is not null && last.Contains("\"i\":")) {
            var s = last.IndexOf("\"i\":") + 4; var e = last.IndexOf(',', s);
            if (e > s && int.TryParse(last[s..e], out var v)) maxSeen = Math.Max(maxSeen, v); } }
    }
    catch (IOException) { denied++; }
}
w.WaitForExit();
Console.WriteLine($"writer FileShare.{writerShare,-4}  reader FileShare.{readerShare,-4}  " +
                  $"reads={reads,6}  denied={denied,6}  tornTail={torn,5}  clean={ok,6}  highestRunSeen={maxSeen}");
return 0;
