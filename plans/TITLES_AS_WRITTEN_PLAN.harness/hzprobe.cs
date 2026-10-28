#:property PublishAot=false
#:property Nullable=enable

// Runs a Humanizer.dll's own Humanize(string) and Titleize(string), loaded by path, over names given as arguments.
//   dotnet run hzprobe.cs -- <path to Humanizer.dll> name...

using System.Reflection;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
var assembly = Assembly.LoadFrom(args[0]);
var humanizeType = assembly.GetType("Humanizer.StringHumanizeExtensions")!;
var humanize = humanizeType.GetMethods(BindingFlags.Public | BindingFlags.Static)
    .First(m => m.Name == "Humanize" && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(string));
var inflector = assembly.GetType("Humanizer.InflectorExtensions")!;
var titleize = inflector.GetMethods(BindingFlags.Public | BindingFlags.Static)
    .First(m => m.Name == "Titleize" && m.GetParameters() is [{ ParameterType: var q }] && q == typeof(string));
Console.WriteLine($"{assembly.GetName().Name} {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
foreach (var name in args.Skip(1))
    Console.WriteLine($"{name} | {humanize.Invoke(null, [name])} | {titleize.Invoke(null, [name])}");
