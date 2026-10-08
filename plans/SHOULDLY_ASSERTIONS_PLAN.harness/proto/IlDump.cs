// Dump <dll> <Type.Method>: each instruction, with the source line where a sequence point starts.
using Mono.Cecil;
var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadSymbols = true });
var parts = args[1].Split('.');
var m = asm.MainModule.GetType(parts[0]).Methods.First(x => x.Name == parts[1]);
var sps = m.DebugInformation.SequencePoints.ToDictionary(s => s.Offset);
foreach (var i in m.Body.Instructions)
    Console.WriteLine($"{(sps.TryGetValue(i.Offset, out var sp) ? (sp.IsHidden ? "hidden" : "L" + sp.StartLine) : ""),7}  {i}");
foreach (var h in m.Body.ExceptionHandlers) Console.WriteLine($"handler {h.HandlerType} try {h.TryStart.Offset:x}-{h.TryEnd.Offset:x}");
