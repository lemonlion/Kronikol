using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILVerify;
using Kronikol.AssertionTracking;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// Runs ILVerify over a fixture before and after its weave and fails on any error the weave added. A run proves only
/// the methods it reaches; the verifier reads every method in the assembly. An error the compiler's own IL already
/// has (a <c>stackalloc</c>, say) is not counted, so the net judges the weave and nothing else.
/// </summary>
internal static class WovenIl
{
    private static readonly IReadOnlyDictionary<string, string> Platform =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, PEReader> References = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every weave in this project goes through here (<see cref="WovenIlVerifiesTests"/> holds that), so every fixture
    /// the suite weaves, in every build it is compiled in, is verified.
    /// </summary>
    public static WeaveResult Weave(AssertionWeaver weaver, string assemblyPath)
    {
        var before = File.ReadAllBytes(assemblyPath);
        var result = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));
        AssertNoNewErrors(before, assemblyPath);
        return result;
    }

    /// <summary>Fails when the assembly at <paramref name="assemblyPath"/> has a verifier error the image
    /// <paramref name="unwoven"/> does not.</summary>
    public static void AssertNoNewErrors(byte[] unwoven, string assemblyPath)
    {
        var added = NewErrors(unwoven, File.ReadAllBytes(assemblyPath), assemblyPath);
        Assert.True(added.Count == 0,
            $"The weave wrote IL that does not verify ({Path.GetFileName(assemblyPath)}):\n  " + string.Join("\n  ", added));
    }

    /// <summary>The woven image's errors, as text, that the unwoven image does not have: compared by method and
    /// error code, since a weave moves every offset after its first insertion.</summary>
    public static IReadOnlyList<string> NewErrors(byte[] unwoven, byte[] woven, string assemblyPath)
    {
        var known = Verify(unwoven, assemblyPath)
            .GroupBy(e => e.Key)
            .ToDictionary(g => g.Key, g => g.Count());
        var added = new List<string>();
        foreach (var group in Verify(woven, assemblyPath).GroupBy(e => e.Key))
        {
            var allowed = known.GetValueOrDefault(group.Key);
            added.AddRange(group.Skip(allowed).Select(e => e.Text));
        }
        return added;
    }

    /// <summary>Every verifier error in the image, keyed by method and error code.</summary>
    public static IReadOnlyList<(string Key, string Text)> Verify(byte[] image, string assemblyPath)
    {
        using var reader = new PEReader(ImmutableArray.Create(image));
        var verifier = new Verifier(new Resolver(assemblyPath), new VerifierOptions { SanityChecks = true });
        verifier.SetSystemModuleName(new AssemblyNameInfo("System.Private.CoreLib"));

        var metadata = reader.GetMetadataReader();
        return verifier.Verify(reader)
            .Select(error =>
            {
                var method = error.Method.IsNil ? "(no method)" : MethodName(metadata, error.Method);
                var arguments = error.ErrorArguments is { Length: > 0 } args
                    ? " (" + string.Join(", ", args.Select(a => $"{a.Name} {a.Value}")) + ")"
                    : "";
                return ($"{method}: {error.Code}", $"{method}: {error.Code}: {error.Message}{arguments}");
            })
            .ToList();
    }

    private static string MethodName(MetadataReader metadata, MethodDefinitionHandle handle)
    {
        var method = metadata.GetMethodDefinition(handle);
        return $"{TypeName(metadata, method.GetDeclaringType())}.{metadata.GetString(method.Name)}";
    }

    private static string TypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        if (type.GetDeclaringType() is { IsNil: false } outer)
            return $"{TypeName(metadata, outer)}/{name}";
        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    /// <summary>Resolves a fixture's references: its own directory first (a fixture built by another SDK keeps its
    /// packages in <c>{name}_build</c> beside it), then the assemblies this test process runs on.</summary>
    private sealed class Resolver(string assemblyPath) : IResolver
    {
        private readonly string[] _directories =
        [
            Path.GetDirectoryName(assemblyPath)!,
            Path.Combine(Path.GetDirectoryName(assemblyPath)!, Path.GetFileNameWithoutExtension(assemblyPath) + "_build"),
        ];

        public PEReader ResolveAssembly(AssemblyNameInfo assemblyName) => Open(assemblyName.Name)!;

        public PEReader ResolveModule(AssemblyNameInfo referencingAssembly, string fileName) => null!;

        private PEReader? Open(string name)
        {
            var path = _directories.Select(d => Path.Combine(d, name + ".dll")).FirstOrDefault(File.Exists)
                ?? Platform.GetValueOrDefault(name);
            return path is null
                ? null
                : References.GetOrAdd(path, p => new PEReader(ImmutableArray.Create(File.ReadAllBytes(p))));
        }
    }
}
