// Reflection as verb-labels.cs, over the packages it left out, every enum of an info's constructor at every value, and gRPC with the method name set to a verb.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.StorageQueues@5.27.13
#:package Kronikol.Extensions.SQS@5.27.13
#:package Kronikol.Extensions.SNS@5.27.13
#:package Kronikol.Extensions.ServiceBus@5.27.13
#:package Kronikol.Extensions.PubSub@5.27.13
#:package Kronikol.Extensions.EventHubs@5.27.13
#:package Kronikol.Extensions.EventBridge@5.27.13
#:package Kronikol.Extensions.Kafka@5.27.13
#:package Kronikol.Extensions.MassTransit@5.27.13
#:package Kronikol.Extensions.Grpc@5.27.13
#:package Kronikol.Extensions.Redis@5.27.13
#:package Kronikol.Extensions.S3@5.27.13
#:package Kronikol.Extensions.DynamoDB@5.27.13
#:package Kronikol.Extensions.AtlasDataApi@5.27.13
#:package Kronikol.Extensions.Elasticsearch@5.27.13
#:package Kronikol.Extensions.MongoDB@5.27.13
#:package Kronikol.Extensions.CloudStorage@5.27.13
#:package Kronikol.Extensions.CosmosDB@5.27.13
#:package Kronikol.Extensions.Bigtable@5.27.13
#:property PublishAot=false
#:property NoWarn=CS0618
using System.Reflection;

string[] verbs = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE", "CONNECT"];
string[] packages = ["StorageQueues", "SQS", "SNS", "ServiceBus", "PubSub", "EventHubs", "EventBridge", "Kafka", "MassTransit", "Grpc", "Redis", "S3", "DynamoDB", "AtlasDataApi", "Elasticsearch", "MongoDB", "CloudStorage", "CosmosDB", "Bigtable"];
foreach (var package in packages)
{
    var assembly = Assembly.Load($"Kronikol.Extensions.{package}");
    var types = assembly.GetTypes();
    var seen = 0;
    var printed = 0;
    foreach (var classifier in types.Where(t => t.Name.EndsWith("OperationClassifier") && t.IsPublic))
    foreach (var method in classifier.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "GetDiagramLabel" && m.GetParameters().Length == 2))
    {
        var infoType = method.GetParameters()[0].ParameterType;
        var verbosityType = method.GetParameters()[1].ParameterType;
        var ctor = infoType.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
        var parameters = ctor.GetParameters();
        var enumIdx = Enumerable.Range(0, parameters.Length).Where(i => parameters[i].ParameterType.IsEnum).ToArray();
        var defaultVerbosity = types.Where(t => t.Name.EndsWith("Options") && t.GetProperty("Verbosity")?.PropertyType == verbosityType)
            .Select(t => t.GetProperty("Verbosity")!.GetValue(Activator.CreateInstance(t))?.ToString()).FirstOrDefault() ?? "?";

        IEnumerable<object[]> Combos(int k)
        {
            if (k == enumIdx.Length)
            {
                yield return [];
                yield break;
            }
            foreach (var v in Enum.GetValues(parameters[enumIdx[k]].ParameterType))
            foreach (var rest in Combos(k + 1))
                yield return [v, .. rest];
        }

        string?[] fillers = package == "Grpc"
            ? verbs.Select(v => (string?)(v[0] + v[1..].ToLowerInvariant())).Append("x").ToArray()
            : ["x", null];
        var reported = new HashSet<string>();
        foreach (var combo in Combos(0))
        foreach (var verbosity in Enum.GetValues(verbosityType))
        foreach (var fill in fillers)
        {
            var values = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var at = Array.IndexOf(enumIdx, i);
                values[i] = at >= 0 ? combo[at]
                    : p.ParameterType == typeof(string) ? fill
                    : p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            }
            string? label;
            try { label = method.Invoke(null, [ctor.Invoke(values), verbosity]) as string; }
            catch (TargetInvocationException) { continue; }
            seen++;
            if (label is null || !verbs.Contains(label.ToUpperInvariant()) || label == label.ToUpperInvariant())
                continue;
            var how = fill is null ? "only with its names empty" : fill == "x" ? "with names filled" : "method named " + fill;
            var marker = verbosity!.ToString() == defaultVerbosity ? "default verbosity" : "";
            var line = $"{package,-14} {string.Join("/", combo),-26} {verbosity,-11} {label,-8} {how,-28} {marker}";
            if (reported.Add(line))
            {
                Console.WriteLine(line);
                printed++;
            }
        }
    }
    Console.WriteLine($"{package,-14} -- {seen} labels read, {printed} verb-cased");
}
