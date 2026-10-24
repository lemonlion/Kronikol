using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// Tests running in parallel share one <c>WebApplicationFactory</c> and make their clients from it at the same moment,
/// as the parallel features of a Reqnroll suite do in their [BeforeScenario] hooks. The factory keeps its clients in a
/// list with no lock and starts its application on first use with no lock, so clients made at once from many threads
/// could leave a null in that list, which made the factory's Dispose throw NullReferenceException (the ReqNRoll xUnit3
/// example's test assembly cleanup failure on CI, about 1 nested run in 1,500), lose a client the factory then never
/// disposed, or start the application more than once.
/// </summary>
public class WebApplicationFactoryClientsTests
{
    private const int Threads = 16;

    [Fact]
    public async Task Tracking_clients_made_at_once_from_one_factory_start_its_application_once_and_all_go_with_it()
    {
        for (var round = 0; round < 25; round++)
        {
            var factory = new OneAppFactory();
            using var together = new Barrier(Threads);
            var clients = await Task.WhenAll(Enumerable.Range(0, Threads).Select(_ => OnItsOwnThread(() =>
            {
                together.SignalAndWait();
                return factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions
                {
                    CallerName = "Test",
                    FixedNameForReceivingService = "App",
                    CurrentTestInfoFetcher = () => ("Make clients at once", "clients-at-once"),
                });
            })));

            Assert.Equal(1, factory.Starts);
            Assert.Equal("ok", await clients[0].GetStringAsync("/"));
            Assert.Null(Record.Exception(factory.Dispose));
            foreach (var client in clients)
                await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("/"));
        }
    }

    [Fact]
    public async Task Tracking_clients_made_at_once_with_handlers_of_their_own_start_the_application_once_and_all_go_with_it()
    {
        var factory = new OneAppFactory();
        using var together = new Barrier(Threads);
        var clients = await Task.WhenAll(Enumerable.Range(0, Threads).Select(_ => OnItsOwnThread(() =>
        {
            together.SignalAndWait();
            return factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions { CallerName = "Test", FixedNameForReceivingService = "App" },
                new PassOn());
        })));

        Assert.Equal(1, factory.Starts);
        Assert.Null(Record.Exception(factory.Dispose));
        foreach (var client in clients)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("/"));
    }

    [Fact]
    public async Task Tracking_clients_made_at_once_from_a_factory_already_started_all_go_with_it()
    {
        for (var round = 0; round < 200; round++)
        {
            var factory = new OneAppFactory();
            factory.StartServer();
            using var together = new Barrier(Threads);
            var clients = await Task.WhenAll(Enumerable.Range(0, Threads).Select(_ => OnItsOwnThread(() =>
            {
                together.SignalAndWait();
                return factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions { CallerName = "Test", FixedNameForReceivingService = "App" });
            })));

            Assert.Null(Record.Exception(factory.Dispose));
            foreach (var client in clients)
                await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("/"));
        }
    }

    [Fact]
    public void Every_client_or_server_Kronikol_takes_from_a_factory_is_taken_under_the_factory_s_lock()
    {
        // each framework package has a CreateTestTrackingClient of its own, and Kronikol.Extensions.Grpc reaches the
        // server: one of them making its client outside the lock would bring the race back for that framework's users
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
            directory = directory.Parent;
        var src = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Kronikol.sln not found"), "src");
        var findings = new List<string>();
        var sites = 0;

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            foreach (var call in new[] { ".CreateDefaultClient(", "factory.Server.", "factory.Services." })
            {
                for (var at = text.IndexOf(call, StringComparison.Ordinal); at >= 0; at = text.IndexOf(call, at + call.Length, StringComparison.Ordinal))
                {
                    // a usage example in a doc comment is not a call
                    var lineStart = text.LastIndexOf('\n', at) + 1;
                    if (text.AsSpan(lineStart, at - lineStart).TrimStart().StartsWith("//", StringComparison.Ordinal))
                        continue;
                    // inside the lock: OneAtATime( opens after the method's start and before the call
                    var method = text.LastIndexOf("public static ", at, StringComparison.Ordinal);
                    var locked = text.LastIndexOf("OneAtATime(", at, StringComparison.Ordinal);
                    sites++;
                    if (locked < 0 || locked < method)
                        findings.Add($"{relative}:{text[..at].Count(c => c == '\n') + 1}: {call}");
                }
            }
        }

        Assert.True(sites >= 20, $"only {sites} sites found: the scan no longer sees the extensions");
        Assert.True(findings.Count == 0, "taken outside the factory's lock:\n" + string.Join("\n", findings));
    }

    // a thread of its own for each caller: callers held at a barrier on the thread pool would wait for the pool to grow
    private static Task<T> OnItsOwnThread<T>(Func<T> run) => Task.Factory.StartNew(run, CancellationToken.None,
        TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>A factory for an app made here: no entry point's project, and a count of the times it starts.</summary>
    private sealed class OneAppFactory : WebApplicationFactory<OneAppFactory>
    {
        // the content root the factory looks for first, named after the entry point's assembly: else it looks for a
        // project folder of that name beside the solution
        static OneAppFactory() => Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_KRONIKOL_TESTS", AppContext.BaseDirectory);

        private int _starts;

        public int Starts => Volatile.Read(ref _starts);

        protected override IHostBuilder CreateHostBuilder()
        {
            Interlocked.Increment(ref _starts);
            return Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web => web
                .Configure(app => app.Run(context => context.Response.WriteAsync("ok"))));
        }
    }

    private sealed class PassOn : DelegatingHandler;
}
