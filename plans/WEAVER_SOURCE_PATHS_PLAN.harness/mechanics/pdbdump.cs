#:package Mono.Cecil@0.11.6
#:property Nullable=enable

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilAsm = Mono.Cecil.AssemblyDefinition;
using CecilDoc = Mono.Cecil.Cil.Document;

var dllPath = Path.GetFullPath(args[0]);
var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
var pathMap = args.Length > 1 ? args[1] : "";

var embeddedSourceKind = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
var typeDefDocsKind = new Guid("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");
var sha256Guid = new Guid("8829d00f-11b8-4213-878b-770e8597ac16");
var kindNames = new Dictionary<Guid, string>
{
    [embeddedSourceKind] = "EmbeddedSource",
    [typeDefDocsKind] = "TypeDefinitionDocuments",
    [new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A")] = "SourceLink",
    [new Guid("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8")] = "CompilationOptions",
    [new Guid("7E4D4708-096E-4C5C-AEDA-CB10BA6A740D")] = "CompilationMetadataReferences",
};
string KindName(Guid g) => kindNames.TryGetValue(g, out var n) ? n : g.ToString();

var map = ParsePathMap(pathMap);
Console.WriteLine($"== Parsed PathMap (Roslyn rules, sorted longest key first): {map.Count} entries");
foreach (var (k, v) in map) Console.WriteLine($"   '{k}' => '{v}'");

Console.WriteLine("\n== ORIGINAL (Roslyn) ==");
var original = Analyze(File.ReadAllBytes(dllPath), File.ReadAllBytes(pdbPath), resolve: true);

// Cecil round-trip exactly as AssertionWeaver does when a .pdb sits beside the assembly.
(byte[] dll, byte[] pdb) CecilRoundTrip(bool touchEmbedded, bool deterministicMvid = false)
{
    var rp = new ReaderParameters
    {
        ReadSymbols = true,
        SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
        ReadingMode = ReadingMode.Immediate,
        SymbolStream = new MemoryStream(File.ReadAllBytes(pdbPath)),
    };
    using var asm = CecilAsm.ReadAssembly(new MemoryStream(File.ReadAllBytes(dllPath)), rp);
    var seen = new HashSet<CecilDoc>();
    foreach (var t in asm.MainModule.GetTypes())
    foreach (var m in t.Methods)
    foreach (var sp in m.DebugInformation.SequencePoints)
    {
        if (!seen.Add(sp.Document)) continue;
        var es = sp.Document.CustomDebugInformations.OfType<EmbeddedSourceDebugInformation>().FirstOrDefault();
        if (touchEmbedded)
        {
            var text = es == null ? "<none>" : $"{es.Content.Length} bytes, Compress={es.Compress}, first line: {FirstLine(es.Content)}";
            Console.WriteLine($"   cecil doc: Url='{sp.Document.Url}' HashAlg={sp.Document.HashAlgorithm} Hash={Hex(sp.Document.Hash, 8)} Embedded={text}");
        }
    }
    var outDll = new MemoryStream();
    var outPdb = new MemoryStream();
    asm.Write(outDll, new WriterParameters
    {
        WriteSymbols = true,
        SymbolWriterProvider = new PortablePdbWriterProvider(),
        SymbolStream = outPdb,
        DeterministicMvid = deterministicMvid,
    });
    return (outDll.ToArray(), outPdb.ToArray());
}

Console.WriteLine("\n== CECIL view of documents reached through sequence points (Content read) ==");
var touched = CecilRoundTrip(touchEmbedded: true);
var untouchedA = CecilRoundTrip(touchEmbedded: false);
var untouchedB = CecilRoundTrip(touchEmbedded: false);
var detA = CecilRoundTrip(touchEmbedded: false, deterministicMvid: true);
var detB = CecilRoundTrip(touchEmbedded: false, deterministicMvid: true);

Console.WriteLine("\n== CECIL ROUND-TRIP (Content not read) ==");
var after = Analyze(untouchedA.dll, untouchedA.pdb, resolve: false);
Console.WriteLine("\n== CECIL ROUND-TRIP (Content read) : embedded blobs vs original ==");
var afterTouched = Analyze(touched.dll, touched.pdb, resolve: false, quiet: true);
foreach (var (name, blob) in original.EmbeddedBlobs)
{
    var u = after.EmbeddedBlobs.TryGetValue(name, out var ub) ? (ub.SequenceEqual(blob) ? "identical" : "DIFFERENT") : "MISSING";
    var t = afterTouched.EmbeddedBlobs.TryGetValue(name, out var tb) ? (tb.SequenceEqual(blob) ? "identical" : $"DIFFERENT raw blob ({blob.Length} -> {tb.Length} bytes) but same text={Decompress(tb).SequenceEqual(Decompress(blob))}") : "MISSING";
    Console.WriteLine($"   {name}: untouched={u}; touched={t}");
}
Console.WriteLine("\n== DETERMINISM ==");
Console.WriteLine($"   two Cecil writes, default params: dll equal={untouchedA.dll.SequenceEqual(untouchedB.dll)} pdb equal={untouchedA.pdb.SequenceEqual(untouchedB.pdb)}");
Console.WriteLine($"   two Cecil writes, DeterministicMvid=true: dll equal={detA.dll.SequenceEqual(detB.dll)} pdb equal={detA.pdb.SequenceEqual(detB.pdb)}");
Console.WriteLine($"   MVID original={original.Mvid} cecil(default)={after.Mvid} cecil(DeterministicMvid)={Analyze(detA.dll, detA.pdb, false, true).Mvid}");
Console.WriteLine($"   PE timestamp original=0x{original.Stamp:X8} cecil=0x{after.Stamp:X8}");

Result Analyze(byte[] dll, byte[] pdb, bool resolve, bool quiet = false)
{
    var r = new Result();
    using var pe = new PEReader(new MemoryStream(dll));
    var md = pe.GetMetadataReader();
    r.Mvid = md.GetGuid(md.GetModuleDefinition().Mvid);
    r.Stamp = (uint)pe.PEHeaders.CoffHeader.TimeDateStamp;
    if (!quiet)
    {
        foreach (var e in pe.ReadDebugDirectory())
        {
            var extra = e.Type switch
            {
                DebugDirectoryEntryType.CodeView => $"path='{pe.ReadCodeViewDebugDirectoryData(e).Path}' guid={pe.ReadCodeViewDebugDirectoryData(e).Guid}",
                DebugDirectoryEntryType.PdbChecksum => $"alg={pe.ReadPdbChecksumDebugDirectoryData(e).AlgorithmName}",
                _ => ""
            };
            Console.WriteLine($"   debugdir {e.Type} stamp=0x{e.Stamp:X8} {extra}");
        }
        for (var h = MetadataTokens.UserStringHandle(1); !h.IsNil && md.GetHeapSize(HeapIndex.UserString) > MetadataTokens.GetHeapOffset(h); h = md.GetNextHandle(h))
        {
            var s = md.GetUserString(h);
            if (s.StartsWith("caf")) Console.WriteLine($"   #US literal compiled from Ansi.cs: \"{s}\" last char U+{(int)s[^1]:X4}");
        }
    }
    using var prov = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
    var pr = prov.GetMetadataReader();
    var docNames = new Dictionary<DocumentHandle, string>();
    foreach (var dh in pr.Documents)
    {
        var d = pr.GetDocument(dh);
        var name = pr.GetString(d.Name);
        docNames[dh] = name;
        byte[]? blob = null;
        foreach (var ch in pr.GetCustomDebugInformation(dh))
        {
            var c = pr.GetCustomDebugInformation(ch);
            if (pr.GetGuid(c.Kind) == embeddedSourceKind) blob = pr.GetBlobBytes(c.Value);
        }
        if (blob != null) r.EmbeddedBlobs[name] = blob;
        var hash = pr.GetBlobBytes(d.Hash);
        if (quiet) continue;
        var alg = pr.GetGuid(d.HashAlgorithm);
        var line = $"   doc#{MetadataTokens.GetRowNumber(dh)} '{name}' alg={(alg == sha256Guid ? "SHA256" : alg == Guid.Empty ? "none" : alg.ToString())} hash={Hex(hash, 6)} embedded={(blob == null ? "no" : $"yes ({BitConverter.ToInt32(blob, 0)} uncompressed-len field)")}";
        if (resolve)
        {
            var local = Invert(name, map);
            var exists = local != null && File.Exists(local);
            var match = exists && hash.Length > 0 && SHA256.HashData(File.ReadAllBytes(local!)).SequenceEqual(hash);
            line += $"\n        -> local '{local ?? "(no PathMap value is a prefix)"}' exists={exists} sha256 matches={match}";
        }
        Console.WriteLine(line);
    }
    var mod = EntityHandle.ModuleDefinition;
    foreach (var ch in pr.GetCustomDebugInformation(mod))
    {
        var c = pr.GetCustomDebugInformation(ch);
        var b = pr.GetBlobBytes(c.Value);
        r.ModuleCdis[KindName(pr.GetGuid(c.Kind))] = b;
        if (!quiet) Console.WriteLine($"   module CDI {KindName(pr.GetGuid(c.Kind))} ({b.Length} bytes){(KindName(pr.GetGuid(c.Kind)) == "SourceLink" ? ": " + Encoding.UTF8.GetString(b) : "")}");
    }
    foreach (var ch in pr.CustomDebugInformation)
    {
        var c = pr.GetCustomDebugInformation(ch);
        if (pr.GetGuid(c.Kind) != typeDefDocsKind) continue;
        var td = md.GetTypeDefinition((TypeDefinitionHandle)c.Parent);
        var br = pr.GetBlobReader(c.Value);
        var rids = new List<string>();
        while (br.RemainingBytes > 0)
        {
            var rid = br.ReadCompressedInteger();
            var dh = MetadataTokens.DocumentHandle(rid);
            rids.Add($"#{rid}={(rid <= pr.Documents.Count ? docNames[dh] : "OUT OF RANGE")}");
        }
        if (!quiet) Console.WriteLine($"   type CDI TypeDefinitionDocuments on {md.GetString(td.Name)}: {string.Join(", ", rids)}");
    }
    return r;
}

static List<(string Key, string Value)> ParsePathMap(string s)
{
    var list = new List<(string, string)>();
    foreach (var kv in Split(s, ','))
    {
        if (kv.Length == 0) continue;
        var p = Split(kv, '=');
        if (p.Count != 2 || p[0].Length == 0 || p[1].Length == 0) throw new InvalidDataException("ERR_InvalidPathMap: " + kv);
        list.Add((EnsureTrailingSeparator(p[0]), EnsureTrailingSeparator(p[1])));
    }
    // Roslyn sorts with ImmutableArray.Sort (not stable); OrderByDescending is stable, which only matters for duplicate keys.
    return list.OrderByDescending(e => e.Item1.Length).ToList();
}

static List<string> Split(string str, char sep)
{
    var result = new List<string>();
    if (str.Length == 0) return result;
    var part = new StringBuilder();
    for (int i = 0; i < str.Length;)
    {
        char c = str[i++];
        if (c == sep)
        {
            if (i < str.Length && str[i] == sep) i++;
            else { result.Add(part.ToString()); part.Clear(); continue; }
        }
        part.Append(c);
    }
    result.Add(part.ToString());
    return result;
}

static string EnsureTrailingSeparator(string s)
{
    if (s.Length == 0 || s[^1] == '/' || s[^1] == '\\') return s;
    bool slash = s.Contains('/'), back = s.Contains('\\');
    return slash && !back ? s + "/" : !slash && back ? s + "\\" : s + Path.DirectorySeparatorChar;
}

// Inverse of PathUtilities.NormalizePathPrefix: find entries whose (separator-normalized) value prefixes the document name.
static string? Invert(string documentName, List<(string Key, string Value)> map)
{
    foreach (var (key, value) in map.OrderByDescending(e => e.Value.Length))
    {
        if (!documentName.StartsWith(value, StringComparison.Ordinal)) continue;
        var rest = documentName.Substring(value.Length);
        bool keySlash = key.Contains('/'), keyBack = key.Contains('\\');
        rest = keyBack && !keySlash ? rest.Replace('/', '\\') : keySlash && !keyBack ? rest.Replace('\\', '/') : rest;
        return key + rest;
    }
    return Path.IsPathFullyQualified(documentName) ? documentName : null;
}

static string Hex(byte[] b, int n) => b.Length == 0 ? "(empty)" : Convert.ToHexString(b, 0, Math.Min(n, b.Length)) + "..";
static string FirstLine(byte[] b) { var s = Encoding.UTF8.GetString(b); var i = s.IndexOfAny(new[] { '\r', '\n' }); return (i < 0 ? s : s[..i]).Trim('﻿'); }
static byte[] Decompress(byte[] blob)
{
    int format = BitConverter.ToInt32(blob, 0);
    if (format == 0) return blob[4..];
    using var ds = new DeflateStream(new MemoryStream(blob, 4, blob.Length - 4), CompressionMode.Decompress);
    var ms = new MemoryStream(); ds.CopyTo(ms); return ms.ToArray();
}

class Result
{
    public Guid Mvid;
    public uint Stamp;
    public Dictionary<string, byte[]> EmbeddedBlobs = new();
    public Dictionary<string, byte[]> ModuleCdis = new();
}
