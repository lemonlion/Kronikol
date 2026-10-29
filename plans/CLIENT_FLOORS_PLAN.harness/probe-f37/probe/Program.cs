// F37: start the Playwright driver and stop it, no browser. Exit 0 when it starts, 1 with the error when it does not.
try
{
    var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    Console.WriteLine("driver started");
    (playwright as IDisposable)?.Dispose();
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"driver failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}");
    return 1;
}
