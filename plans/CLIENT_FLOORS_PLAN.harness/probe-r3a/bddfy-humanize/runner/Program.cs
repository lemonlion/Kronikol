using System.Reflection;
using TestStack.BDDfy.Configuration;

// What the adapter built on 8.0.1.3 reads on the BDDfy this runs on, with the default humaniser and with one set.
const string name = "WhenMCAFundingActivates";
Console.WriteLine($"BDDfy {typeof(Configurator).Assembly.GetName().Version}");
var field = typeof(Scanners).GetField("Humanize", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)!;
Console.WriteLine($"Scanners.Humanize static: {field.IsStatic}");
var configured = typeof(Configurator).GetProperty("Humanizer", BindingFlags.Public | BindingFlags.Static);
string Configured() => configured is null ? "(no Configurator.Humanizer)"
    : (string)configured.PropertyType.GetMethod("Humanize", [typeof(string)])!.Invoke(configured.GetValue(null), [name])!;
// The first read of Scanners.Humanize, built on 8.0.1.3, comes before a humaniser is set (default-first) or after it
// (custom-first); then the humaniser changes and the read comes again.
var customFirst = args.Length > 0 && args[0] == "custom-first";
Console.WriteLine(customFirst ? "custom-first" : "default-first");
if (configured is null)
    Console.WriteLine($"  read = '{Old.Humanize(name)}' (8.0.1.3 has no Configurator.Humanizer)");
else
{
    var original = configured.GetValue(null);
    var shouted = Proxy.Create(configured.PropertyType, n => "Shouted " + n.ToUpperInvariant());
    configured.SetValue(null, customFirst ? shouted : original);
    Console.WriteLine($"  first:  read = '{Old.Humanize(name)}', Configurator.Humanizer = '{Configured()}'");
    configured.SetValue(null, customFirst ? original : shouted);
    Console.WriteLine($"  second: read = '{Old.Humanize(name)}', Configurator.Humanizer = '{Configured()}'");
}

public class Proxy : DispatchProxy
{
    private Func<string, string> _f = n => n;
    public static object Create(Type contract, Func<string, string> f)
    {
        var p = DispatchProxy.Create(contract, typeof(Proxy));
        ((Proxy)p)._f = f;
        return p;
    }
    protected override object? Invoke(MethodInfo? m, object?[]? args) => m?.Name == "Humanize" ? _f((string)args![0]!) : throw new NotSupportedException(m?.Name);
}
