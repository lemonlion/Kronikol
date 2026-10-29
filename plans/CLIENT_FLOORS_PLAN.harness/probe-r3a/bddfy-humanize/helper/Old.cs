using TestStack.BDDfy.Configuration;

// Built on 8.0.1.3, as the published adapter is: an instance field read through Configurator.Scanners.
public static class Old
{
    public static string Humanize(string name) => Configurator.Scanners.Humanize(name);
}
