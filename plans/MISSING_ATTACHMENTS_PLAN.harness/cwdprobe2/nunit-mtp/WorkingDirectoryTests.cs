using NUnit.Framework;

namespace CwdProbe.NUnitMtp;

[TestFixture]
public sealed class WorkingDirectoryProbeTests
{
    [Test]
    public void Records_the_working_directory()
    {
        CwdProbeShared.ProbeLog.RecordTest("NUnit", typeof(TestAttribute));
        CwdProbeShared.ProbeLog.Append(CwdProbeShared.ProbeLog.DetailsPath,
            $"NUnit context|{CwdProbeShared.ProbeLog.Mode}|WorkDirectory={TestContext.CurrentContext.WorkDirectory}|TestDirectory={TestContext.CurrentContext.TestDirectory}|relative response.json resolves to={System.IO.Path.GetFullPath("response.json")}");
    }
}
