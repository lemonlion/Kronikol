using System.Reflection;

foreach (var name in new[] { "TUnit.Core.IDataSourceAttribute", "TUnit.Core.AsyncUntypedDataSourceGeneratorAttribute",
             "TUnit.Core.UntypedDataSourceGeneratorAttribute" })
{
    var type = typeof(TUnit.Core.TestAttribute).Assembly.GetType(name);
    if (type is null) { Console.WriteLine($"{name}: absent"); continue; }
    Console.WriteLine($"{name}: base {type.BaseType?.Name}, interfaces {string.Join(", ", type.GetInterfaces().Select(i => i.Name))}");
    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
        Console.WriteLine($"  {(m.IsAbstract ? "abstract " : m.IsVirtual ? "virtual " : "")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
}
