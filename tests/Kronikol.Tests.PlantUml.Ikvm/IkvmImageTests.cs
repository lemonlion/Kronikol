using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// The IKVM image an output carries, the Java runtime's own files under <c>ikvm/&lt;runtime&gt;/</c>, has to be the one
/// of the IKVM release whose class library the output runs. The class library reads data files whose format it knows:
/// <c>java.util.Currency</c> refuses an older release's <c>currency.data</c>, and every diagram that formats a number then
/// draws PlantUML's error page. The suite runs on its own IKVM release while the package it tests is built on the
/// package's floor, and the package's build used to copy the floor's image into this output over the suite's, so built
/// on 8.9.0 and run on 8.16.3 the suite failed 46 of its 83 facts (<c>plans/CLIENT_FLOORS_PLAN.md</c> F39).
/// </summary>
public class IkvmImageTests
{
    [Fact]
    public void The_output_carries_the_image_of_the_IKVM_release_it_runs_on()
    {
        var running = typeof(java.lang.Object).Assembly.Location;
        var image = Path.Combine(AppContext.BaseDirectory, "ikvm", RuntimeInformation.RuntimeIdentifier, "bin", "IKVM.Java.dll");

        Assert.Equal(FileVersionInfo.GetVersionInfo(running).FileVersion, FileVersionInfo.GetVersionInfo(image).FileVersion);
    }
}
