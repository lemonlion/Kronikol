#:package Mono.Cecil@0.11.6
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilAsm = Mono.Cecil.AssemblyDefinition;

var dllPath = Path.GetFullPath(args[0]);
var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
var rp = new ReaderParameters
{
    ReadSymbols = true,
    SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
    ReadingMode = ReadingMode.Immediate,
    SymbolStream = new MemoryStream(File.ReadAllBytes(pdbPath)),
};
using var asm = CecilAsm.ReadAssembly(new MemoryStream(File.ReadAllBytes(dllPath)), rp);
Console.WriteLine("Cecil in-memory types with custom debug infos after read:");
foreach (var t in asm.MainModule.GetTypes())
    if (t.HasCustomDebugInformations)
        foreach (var c in t.CustomDebugInformations)
            Console.WriteLine($"   {t.Name} (token 0x{t.MetadataToken.ToUInt32():X8}) kind={c.Identifier} {(c is BinaryCustomDebugInformation b ? "blob=" + Convert.ToHexString(b.Data) : "")}");

Console.WriteLine("Original PDB CustomDebugInformation rows (parent token):");
Dump(File.ReadAllBytes(pdbPath));
// "touch": read every type's records while the tokens are still the compiler's; "drop": then remove the
// TypeDefinitionDocuments records (the fix R1d proposes).
if (args.Length > 1 && (args[1] == "touch" || args[1] == "drop")) foreach (var t in asm.MainModule.GetTypes()) _ = t.CustomDebugInformations;
if (args.Length > 1 && args[1] == "drop")
    foreach (var t in asm.MainModule.GetTypes())
        for (var i = t.CustomDebugInformations.Count - 1; i >= 0; i--)
            if (t.CustomDebugInformations[i].Identifier == new Guid("932e74bc-dba9-4478-8d46-0f32a7bab3d3"))
                t.CustomDebugInformations.RemoveAt(i);
foreach (var t in asm.MainModule.GetTypes())
foreach (var m in t.Methods)
foreach (var sp in m.DebugInformation.SequencePoints)
{
    if (sp.IsHidden || !(sp.Document.Url.EndsWith("Lsep.cs") || sp.Document.Url.EndsWith("Ansi.cs"))) continue;
    var local = Path.Combine(Path.GetDirectoryName(dllPath)!, "..", "..", "..", sp.Document.Url.Substring("/_/App/".Length));
    var lines = File.ReadAllLines(local);
    Console.WriteLine($"SP {t.Name}.{m.Name} doc={sp.Document.Url} StartLine={sp.StartLine} col={sp.StartColumn}-{sp.EndColumn}; File.ReadAllLines has {lines.Length} lines; lines[StartLine-1]='{(sp.StartLine - 1 < lines.Length ? lines[sp.StartLine - 1] : "<past end>")}'");
    if (local.EndsWith("Ansi.cs")) Console.WriteLine($"   ReadAllLines(Ansi.cs) line 5 last-quote char before: U+{(int)lines[4][lines[4].LastIndexOf('"') - 1]:X4}");
}
var outDll = new MemoryStream(); var outPdb = new MemoryStream();
asm.Write(outDll, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = outPdb });
Console.WriteLine("Cecil-written PDB CustomDebugInformation rows (parent token):");
Dump(outPdb.ToArray());
using var pe = new PEReader(new MemoryStream(outDll.ToArray()));
var md = pe.GetMetadataReader();
Console.WriteLine("Cecil-written TypeDef table:");
foreach (var th in md.TypeDefinitions) Console.WriteLine($"   0x{MetadataTokens.GetToken(th):X8} {md.GetString(md.GetTypeDefinition(th).Name)}");

static void Dump(byte[] pdb)
{
    using var prov = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
    var pr = prov.GetMetadataReader();
    foreach (var ch in pr.CustomDebugInformation)
    {
        var c = pr.GetCustomDebugInformation(ch);
        Console.WriteLine($"   row {MetadataTokens.GetRowNumber(ch)} parent=0x{MetadataTokens.GetToken(c.Parent):X8} ({c.Parent.Kind}) kind={pr.GetGuid(c.Kind)} blobLen={pr.GetBlobBytes(c.Value).Length}");
    }
}
