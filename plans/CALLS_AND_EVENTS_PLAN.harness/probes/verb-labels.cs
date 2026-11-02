// Every label a package's own classifier can write that a merge would rewrite: a verb in another casing. Reflection over the
// published 5.27.13 packages: for each *OperationClassifier.GetDiagramLabel(info, verbosity), every member of the operation
// enum at every verbosity, with the default verbosity read from the package's options type.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Redis@5.27.13
#:package Kronikol.Extensions.MongoDB@5.27.13
#:package Kronikol.Extensions.CosmosDB@5.27.13
#:package Kronikol.Extensions.Dapper@5.27.13
#:package Kronikol.Extensions.EfCore.Relational@5.27.13
#:package Kronikol.Extensions.CloudStorage@5.27.13
#:package Kronikol.Extensions.BigQuery@5.27.13
#:package Kronikol.Extensions.Spanner@5.27.13
#:package Kronikol.Extensions.Bigtable@5.27.13
#:package Kronikol.Extensions.BlobStorage@5.27.13
#:package Kronikol.Extensions.S3@5.27.13
#:package Kronikol.Extensions.DynamoDB@5.27.13
#:package Kronikol.Extensions.Elasticsearch@5.27.13
#:package Kronikol.Extensions.AtlasDataApi@5.27.13
#:property PublishAot=false
#:property NoWarn=CS0618

using System.Reflection;

string[] verbs = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE", "CONNECT"];
string[] packages = ["Redis", "MongoDB", "CosmosDB", "Dapper", "EfCore.Relational", "CloudStorage", "BigQuery", "Spanner", "Bigtable", "BlobStorage", "S3", "DynamoDB", "Elasticsearch", "AtlasDataApi"];

foreach (var package in packages)
{
    var assembly = Assembly.Load($"Kronikol.Extensions.{package}");
    var types = assembly.GetTypes();
    var seen = 0;
    foreach (var classifier in types.Where(t => t.Name.EndsWith("OperationClassifier") && t.IsPublic))
    foreach (var method in classifier.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "GetDiagramLabel" && m.GetParameters().Length == 2))
    {
        var infoType = method.GetParameters()[0].ParameterType;
        var verbosityType = method.GetParameters()[1].ParameterType;
        var ctor = infoType.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
        var operation = ctor.GetParameters().First(p => p.ParameterType.IsEnum).ParameterType;
        var defaultVerbosity = types.Where(t => t.Name.EndsWith("Options") && t.GetProperty("Verbosity")?.PropertyType == verbosityType)
            .Select(t => t.GetProperty("Verbosity")!.GetValue(Activator.CreateInstance(t))?.ToString()).FirstOrDefault() ?? "?";
        foreach (var op in Enum.GetValues(operation))
        foreach (var verbosity in Enum.GetValues(verbosityType))
        {
            // Once with every other field empty, and once with each string filled: a label that needs a table or an object
            // name to be missing is not one a real call usually writes, so the output says which it is.
            string? Label(bool filled)
            {
                var arguments = ctor.GetParameters().Select(p => p.ParameterType.IsEnum && p.ParameterType == operation ? op
                    : filled && p.ParameterType == typeof(string) ? "x"
                    : p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                try { return method.Invoke(null, [ctor.Invoke(arguments), verbosity]) as string; }
                catch (TargetInvocationException) { return null; }
            }
            var empty = Label(false);
            var full = Label(true);
            seen++;
            bool Hit(string? l) => l is not null && verbs.Contains(l.ToUpperInvariant()) && l != l.ToUpperInvariant();
            if (package == "Redis" && op!.ToString() is "Get" or "Delete")
                Console.WriteLine($"{package,-18} {op,-14} {verbosity,-11} labels: '{empty}' / '{full}'");
            if (!Hit(empty) && !Hit(full))
                continue;
            var when = Hit(full) ? "always" : "only with no table or object named";
            var marker = verbosity!.ToString() == defaultVerbosity ? "default verbosity" : "";
            Console.WriteLine($"{package,-18} {op,-14} {verbosity,-11} {(Hit(full) ? full : empty),-8} {when,-36} {marker}");
        }
    }
    if (seen == 0) Console.WriteLine($"{package,-18} (no classifier with GetDiagramLabel(info, verbosity) was found)");
}
