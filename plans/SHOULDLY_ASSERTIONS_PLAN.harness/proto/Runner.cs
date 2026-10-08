// Runs the prototype weaver over a built assembly: Runner <dll>. The dll's folder is the resolver's search path.
using Kronikol.AssertionTracking;
var dll = Path.GetFullPath(args[0]);
var r = new AssertionWeaver(null, [Path.GetDirectoryName(dll)!]).Weave(dll, Path.ChangeExtension(dll, ".pdb"));
Console.WriteLine($"weaved {r.WeavedCount} assertion(s) in {r.MethodCount} method(s); skip: {r.SkipReason ?? "-"}");
