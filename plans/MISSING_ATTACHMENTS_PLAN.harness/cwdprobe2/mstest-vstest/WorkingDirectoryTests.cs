using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CwdProbe.MsTestVsTest;

[TestClass]
public sealed class WorkingDirectoryProbeTests
{
    [TestMethod]
    public void Records_the_working_directory()
    {
        CwdProbeShared.ProbeLog.RecordTest("MSTest", typeof(TestClassAttribute));
    }
}
