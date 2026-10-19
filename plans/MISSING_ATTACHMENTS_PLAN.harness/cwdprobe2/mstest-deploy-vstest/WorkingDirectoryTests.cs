using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CwdProbe.MsTestDeploy;

[TestClass]
public sealed class DeploymentProbeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DeploymentItem("deployme.txt")]
    public void Records_the_working_directory_with_a_deployment_item()
    {
        CwdProbeShared.ProbeLog.RecordTest("MSTest", typeof(TestClassAttribute));
        CwdProbeShared.ProbeLog.Append(CwdProbeShared.ProbeLog.DetailsPath,
            $"MSTest deployment|{CwdProbeShared.ProbeLog.Mode}|DeploymentDirectory={TestContext.DeploymentDirectory}|TestRunDirectory={TestContext.TestRunDirectory}|relative response.json resolves to={System.IO.Path.GetFullPath("response.json")}|assemblyLocation={typeof(DeploymentProbeTests).Assembly.Location}");
    }
}
