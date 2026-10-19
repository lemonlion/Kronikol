public class WhereProbe
{
    [Test]
    public void Where()
    {
        var line = $"cwd={Directory.GetCurrentDirectory()} | base={AppContext.BaseDirectory} | via={Environment.GetEnvironmentVariable("PROBE_VIA")}";
        File.AppendAllText(Environment.GetEnvironmentVariable("PROBE_OUT")!, line + Environment.NewLine);
    }
}
