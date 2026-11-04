// The public surface of a set of assemblies, one member a line, sorted: every public or protected member of every type
// reachable from outside. Run on two builds and diff the output.
//   dotnet run --file PublicApi.cs -- <assembly> [<assembly>...]
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

foreach (var path in args)
{
    using var pe = new PEReader(File.OpenRead(path));
    var md = pe.GetMetadataReader();
    var lines = new List<string>();
    var provider = new Names(md);

    foreach (var handle in md.TypeDefinitions)
    {
        var type = md.GetTypeDefinition(handle);
        if (!Reachable(md, type)) continue;
        var name = FullName(md, type);
        lines.Add($"type {name} {type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Interface)}");

        foreach (var h in type.GetMethods())
        {
            var m = md.GetMethodDefinition(h);
            var access = m.Attributes & MethodAttributes.MemberAccessMask;
            if (access is not (MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem)) continue;
            var sig = m.DecodeSignature(provider, null);
            lines.Add($"{name}::{md.GetString(m.Name)}({string.Join(", ", sig.ParameterTypes)}) -> {sig.ReturnType} [{access}]");
        }

        foreach (var h in type.GetFields())
        {
            var f = md.GetFieldDefinition(h);
            var access = f.Attributes & FieldAttributes.FieldAccessMask;
            if (access is not (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem)) continue;
            var value = f.GetDefaultValue().IsNil ? "" : " const";
            lines.Add($"{name}::{md.GetString(f.Name)} : {f.DecodeSignature(provider, null)}{value} [{access}]");
        }
    }

    lines.Sort(StringComparer.Ordinal);
    Console.WriteLine($"# {Path.GetFileName(path)}");
    foreach (var line in lines) Console.WriteLine(line);
}

static bool Reachable(MetadataReader md, TypeDefinition type)
{
    var visibility = type.Attributes & TypeAttributes.VisibilityMask;
    if (visibility == TypeAttributes.Public) return true;
    if (visibility is not (TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem)) return false;
    return Reachable(md, md.GetTypeDefinition(type.GetDeclaringType()));
}

static string FullName(MetadataReader md, TypeDefinition type)
{
    var declaring = type.GetDeclaringType();
    if (!declaring.IsNil) return $"{FullName(md, md.GetTypeDefinition(declaring))}+{md.GetString(type.Name)}";
    var ns = md.GetString(type.Namespace);
    return ns.Length == 0 ? md.GetString(type.Name) : $"{ns}.{md.GetString(type.Name)}";
}

sealed class Names(MetadataReader md) : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(", ", typeArguments)}>";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullNameOf(reader.GetTypeDefinition(handle));
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? reader.GetString(type.Name) : $"{ns}.{reader.GetString(type.Name)}";
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private string FullNameOf(TypeDefinition type)
    {
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return $"{FullNameOf(md.GetTypeDefinition(declaring))}+{md.GetString(type.Name)}";
        var ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? md.GetString(type.Name) : $"{ns}.{md.GetString(type.Name)}";
    }
}
