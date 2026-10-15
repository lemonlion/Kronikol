// Prints a portable PDB's documents (name, whether its source is embedded) and its Source Link JSON.
// Usage: dotnet run --file dumpdocs.cs -- <file.pdb | file.dll> [...]
// For a .dll it also prints the debug directory (CodeView path, PDB checksum, Reproducible) and the MVID, then the
// documents of the embedded PDB when there is one.
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

var embeddedSourceKind = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");

foreach (var path in args)
{
    Console.WriteLine($"== {path}");
    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        MetadataReaderProvider? embedded = null;
        foreach (var e in pe.ReadDebugDirectory())
        {
            var line = $"debugdir: {e.Type} stamp=0x{e.Stamp:X8} ver={e.MajorVersion}.{e.MinorVersion}";
            if (e.Type == DebugDirectoryEntryType.CodeView)
            {
                var cv = pe.ReadCodeViewDebugDirectoryData(e);
                line += $" guid={cv.Guid} age={cv.Age} path={cv.Path}";
            }
            else if (e.Type == DebugDirectoryEntryType.PdbChecksum)
            {
                var pc = pe.ReadPdbChecksumDebugDirectoryData(e);
                line += $" {pc.AlgorithmName}:{Convert.ToHexString(pc.Checksum.ToArray())[..16]}...";
            }
            else if (e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                embedded = pe.ReadEmbeddedPortablePdbDebugDirectoryData(e);
            }
            Console.WriteLine(line);
        }
        var md = pe.GetMetadataReader();
        Console.WriteLine($"mvid={md.GetGuid(md.GetModuleDefinition().Mvid)}");
        if (embedded is null)
        {
            Console.WriteLine("(no embedded portable PDB)");
            continue;
        }
        Console.WriteLine("-- embedded portable PDB:");
        using (embedded)
            Dump(embedded.GetMetadataReader());
    }
    else
    {
        using var fs = File.OpenRead(path);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(fs, MetadataStreamOptions.PrefetchMetadata);
        Dump(provider.GetMetadataReader());
    }
}

void Dump(MetadataReader r)
{
    if (r.DebugMetadataHeader is { } header)
        Console.WriteLine($"pdb id={Convert.ToHexString(header.Id.ToArray())}");
    var withEmbeddedSource = new HashSet<DocumentHandle>();
    var sourceLinks = new List<string>();
    foreach (var h in r.CustomDebugInformation)
    {
        var cdi = r.GetCustomDebugInformation(h);
        var kind = r.GetGuid(cdi.Kind);
        if (kind == embeddedSourceKind && cdi.Parent.Kind == HandleKind.Document)
            withEmbeddedSource.Add((DocumentHandle)cdi.Parent);
        else if (kind == sourceLinkKind)
            sourceLinks.Add(Encoding.UTF8.GetString(r.GetBlobBytes(cdi.Value)));
    }
    Console.WriteLine($"documents: {r.Documents.Count}, with embedded source: {withEmbeddedSource.Count}");
    foreach (var dh in r.Documents)
    {
        var d = r.GetDocument(dh);
        Console.WriteLine($"  doc: {r.GetString(d.Name)}  embeddedSource={(withEmbeddedSource.Contains(dh) ? "yes" : "no")}");
    }
    if (sourceLinks.Count == 0)
        Console.WriteLine("sourcelink: (none)");
    foreach (var s in sourceLinks)
        Console.WriteLine($"sourcelink: {s}");
}
