using Xunit;

namespace CwdProbe.XunitV2;

public sealed class WorkingDirectoryProbeTests
{
    [Fact]
    public void Records_the_working_directory()
    {
        CwdProbeShared.ProbeLog.RecordTest("xUnit v2", typeof(FactAttribute));
    }
}
