using System.Security.Cryptography;
using System.Text;
using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The Node renderer's engine cache checks every file it downloads and every file it finds against a known hash
/// (plans/ENGINE_PIN_PLAN.md S3). Before, it wrote whatever the CDN answered straight to the final name and never
/// looked at a file again: a proxy's rewrite, a captive portal's login page or a download cut short by a killed
/// process was trusted by every later run. A temp directory and a fake downloader, no network.
/// </summary>
public sealed class EngineCacheTests : IDisposable
{
    private const string Base = "https://cdn.example/npm/engine@1.0.0";
    private static readonly byte[] Engine = Encoding.UTF8.GetBytes("engine " + new string('e', 4000));
    private static readonly byte[] Viz = Encoding.UTF8.GetBytes("viz " + new string('v', 3000));
    private static readonly byte[] Wrong = Encoding.UTF8.GetBytes("<html>a captive portal's login page</html>");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-engine-cache-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static string Sri(byte[] bytes) => "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes));

    private EngineCache Cache(Func<string, byte[]> download) => new(_dir, download, Base,
        new Dictionary<string, string> { ["viz-global.js"] = Sri(Viz), ["plantuml.js"] = Sri(Engine) });

    /// <summary>Answers each file's right bytes, counting the calls per URL.</summary>
    private static Func<string, byte[]> Serving(Dictionary<string, int> calls, Func<string, int, byte[]?>? answer = null) => url =>
    {
        lock (calls)
            calls[url] = calls.GetValueOrDefault(url) + 1;
        var right = url.EndsWith("/plantuml.js", StringComparison.Ordinal) ? Engine : Viz;
        return answer?.Invoke(url, calls[url]) ?? right;
    };

    private string PathOf(string file) => Path.Combine(_dir, file);

    [Fact]
    public void Both_files_are_downloaded_verified_and_asked_for_once_each()
    {
        var calls = new Dictionary<string, int>();

        Cache(Serving(calls)).EnsureFiles();

        Assert.Equal(Engine, File.ReadAllBytes(PathOf("plantuml.js")));
        Assert.Equal(Viz, File.ReadAllBytes(PathOf("viz-global.js")));
        Assert.Equal(1, calls[Base + "/plantuml.js"]);
        Assert.Equal(1, calls[Base + "/viz-global.js"]);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void A_file_that_fails_its_hash_is_replaced_and_its_code_cache_deleted()
    {
        // V8 checks a code cache against the source's length, not its bytes (plan §1.12): a replaced engine of the
        // same length would run the stale cached code, so the cache goes whenever the engine does.
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(PathOf("plantuml.js"), Engine.Take(Engine.Length / 2).ToArray());
        File.WriteAllBytes(PathOf("viz-global.js"), Viz);
        File.WriteAllBytes(PathOf("plantuml.js.v8cache"), [1, 2, 3]);
        var calls = new Dictionary<string, int>();

        Cache(Serving(calls)).EnsureFiles();

        Assert.Equal(Engine, File.ReadAllBytes(PathOf("plantuml.js")));
        Assert.False(File.Exists(PathOf("plantuml.js.v8cache")));
        Assert.False(calls.ContainsKey(Base + "/viz-global.js"));
    }

    [Fact]
    public void A_wrong_download_is_retried_once()
    {
        var calls = new Dictionary<string, int>();

        Cache(Serving(calls, (url, n) => url.EndsWith("/plantuml.js", StringComparison.Ordinal) && n == 1 ? Wrong : null)).EnsureFiles();

        Assert.Equal(Engine, File.ReadAllBytes(PathOf("plantuml.js")));
        Assert.Equal(2, calls[Base + "/plantuml.js"]);
    }

    [Fact]
    public void A_download_that_never_matches_is_refused_with_both_hashes_and_nothing_left_under_the_final_name()
    {
        var calls = new Dictionary<string, int>();

        var e = Assert.Throws<InvalidOperationException>(() =>
            Cache(Serving(calls, (url, _) => url.EndsWith("/plantuml.js", StringComparison.Ordinal) ? Wrong : null)).EnsureFiles());

        Assert.Contains(Base + "/plantuml.js", e.Message);
        Assert.Contains(Sri(Engine), e.Message);
        Assert.Contains(Sri(Wrong), e.Message);
        Assert.Contains(_dir, e.Message);
        Assert.Contains($"{Wrong.Length:N0} bytes", e.Message);
        Assert.Equal(2, calls[Base + "/plantuml.js"]);
        Assert.False(File.Exists(PathOf("plantuml.js")));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void A_verified_file_is_not_downloaded_again()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(PathOf("plantuml.js"), Engine);
        File.WriteAllBytes(PathOf("viz-global.js"), Viz);
        File.WriteAllBytes(PathOf("plantuml.js.v8cache"), [1, 2, 3]);
        var calls = new Dictionary<string, int>();

        Cache(Serving(calls)).EnsureFiles();

        Assert.Empty(calls);
        Assert.True(File.Exists(PathOf("plantuml.js.v8cache")));
    }

    [Fact]
    public void Two_caches_on_one_directory_both_finish_with_the_verified_file()
    {
        // Two processes share the directory (parallel test assemblies, the tool beside a test run). Each writes to
        // a temporary name of its own and renames it into place; the slower one finds a verified file there.
        using var slowStarted = new ManualResetEventSlim();
        var fastCalls = new Dictionary<string, int>();
        var slowCalls = new Dictionary<string, int>();
        var slow = Task.Run(() => Cache(Serving(slowCalls, (url, _) =>
        {
            slowStarted.Set();
            Thread.Sleep(300);
            return null;
        })).EnsureFiles());
        slowStarted.Wait(TimeSpan.FromSeconds(10));

        Cache(Serving(fastCalls)).EnsureFiles();
        slow.GetAwaiter().GetResult();

        Assert.Equal(Engine, File.ReadAllBytes(PathOf("plantuml.js")));
        Assert.Equal(Viz, File.ReadAllBytes(PathOf("viz-global.js")));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
