using System.Reflection;
using System.Runtime.Loader;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// Shouldly 5 (Q6: 4.x is what the weave supports, with facts on 5.0's preview). Its preview marks the subject's
/// parameter <c>[CallerArgumentExpression]</c> on 200 of 204 methods, which the compiler fills with code text: never a
/// value. Built by the 10 SDK in a folder of its own, and run in a load context that takes Shouldly 5 from that
/// folder, since this test process carries 4.3.0; Kronikol is the process's own, so the notes reach its log.
/// </summary>
public class Shouldly5PreviewTests
{
    private const string Version = "5.0.0-preview.2";

    private const string Source = """
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Tests
        {
            public void Passes()
            {
                var result = 3;
                var expected = 3;
                result.ShouldBe(expected);
            }

            public void Fails()
            {
                var result = 3;
                result.ShouldBe(5, "custom message");
            }

            public async Task Throws_async()
            {
                await Should.ThrowAsync<System.InvalidOperationException>(() => Task.FromException(new System.InvalidOperationException()));
            }
        }
        """;

    private sealed class FixtureContext(string directory) : AssemblyLoadContext("Shouldly5", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) =>
            name.Name == "Shouldly" ? LoadFromAssemblyPath(Path.Combine(directory, "Shouldly.dll")) : null;
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void Shouldly_5_preview_is_woven(string configuration)
    {
        if (!TestAssemblyBuilder.IsSdkAvailable("10.0"))
            Assert.Skip(".NET 10 SDK not installed");

        var name = $"Shouldly5{configuration}";
        // Built for the framework this test process runs on (Shouldly 5's preview ships net8.0 and later), so each
        // of the suite's three hosts can load it.
        var path = TestAssemblyBuilder.BuildWithSdk(name, Source, "10.0.0", $"net{Environment.Version.Major}.0", configuration,
            packageVersions: new Dictionary<string, string> { ["Shouldly"] = Version });
        // A library's build copies no package assemblies; Shouldly 5 is read from the package the build restored, and
        // the weave is handed its folder as the build task hands it the project's references.
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var shouldly = Path.Combine(packages, "shouldly", Version, "lib", "net8.0");
        var result = WovenIl.Weave(new AssertionWeaver(null, [shouldly]), path);
        Assert.Equal(3, result.WeavedCount);

        var context = new FixtureContext(shouldly);
        try
        {
            var type = context.LoadFromAssemblyPath(path).GetType("Tests")!;
            Assert.Equal(5, type.Assembly.GetReferencedAssemblies().Single(a => a.Name == "Shouldly").Version!.Major);
            var instance = Activator.CreateInstance(type);

            (Exception? Thrown, WovenRun.Note[] Notes) Run(string method)
            {
                var testId = $"{name}_{method}_{Guid.NewGuid():N}";
                Exception? thrown;
                using (TestIdentityScope.Begin(testId, testId))
                {
                    thrown = Record.Exception(() =>
                    {
                        if (type.GetMethod(method)!.Invoke(instance, null) is Task task)
                            task.GetAwaiter().GetResult();
                    });
                }
                return (thrown is TargetInvocationException { InnerException: { } inner } ? inner : thrown, WovenRun.NotesFor(testId));
            }

            var (passesThrew, passesNotes) = Run("Passes");
            Assert.True(passesThrew is null, passesThrew?.ToString());
            var passed = Assert.Single(passesNotes);
            Assert.True(passed.Passed);
            Assert.Equal("Result should be '3'", passed.Label);

            var failed = Assert.Single(Run("Fails").Notes);
            Assert.False(failed.Passed);
            Assert.Equal("Result should be 5", failed.Label);

            var thrown = Assert.Single(Run("Throws_async").Notes);
            Assert.True(thrown.Passed);
            Assert.Equal("Should throw System.InvalidOperationException", thrown.Label);
        }
        finally
        {
            context.Unload();
        }
    }
}
