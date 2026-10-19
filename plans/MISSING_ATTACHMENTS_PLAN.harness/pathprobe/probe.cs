using System;
using System.IO;

var bs = (char)92;
var nul = (char)0;
var dir = Path.Combine(Path.GetTempPath(), "kronikol-pathprobe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var file = Path.Combine(dir, "shot.png");
File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5 });

void Try(string label, Func<object?> act)
{
    try { Console.WriteLine($"{label}: OK -> {act()}"); }
    catch (Exception ex) { Console.WriteLine($"{label}: {ex.GetType().Name}: {ex.Message}"); }
}

Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}");
Try("File.Copy onto itself (overwrite)", () => { File.Copy(file, file, overwrite: true); return "copied"; });
Console.WriteLine($"  length after: {new FileInfo(file).Length}");
Try("GetFullPath with NUL, cwd base", () => Path.GetFullPath("a" + nul + "b.png", Directory.GetCurrentDirectory()));
Try("GetFullPath relative base", () => Path.GetFullPath("shot.png", "relative/base"));
Try("GetFullPath pipe in name", () => Path.GetFullPath("a|b.png", dir));
Try("File.Exists with NUL", () => File.Exists("a" + nul + "b.png"));
Try("IsPathRooted of a Windows path", () => Path.IsPathRooted("C:" + bs + "x" + bs + "shot.png"));
Try("Uri.EscapeDataString of a name", () => Uri.EscapeDataString("shot#1 50%.png"));
Directory.Delete(dir, true);
