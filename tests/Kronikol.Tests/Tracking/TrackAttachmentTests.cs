using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TrackAttachmentTests : IDisposable
{
    private readonly string _testId = $"TrackAttachmentTests.{Guid.NewGuid():N}";

    public void Dispose()
    {
        Track.TestIdResolver = null;
        StepCollector.ClearSteps(_testId);
    }

    [Fact]
    public void Attachment_on_active_step_is_stored_on_that_step()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        StepCollector.StartStep(_testId, "When", "I upload a file", null, null);
        Track.Attachment("/tmp/screenshot.png", "Screenshot");
        StepCollector.CompleteStep(_testId, passed: true);

        var steps = StepCollector.GetSteps(_testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].Attachments);
        Assert.Single(steps[0].Attachments);
        Assert.Equal("Screenshot", steps[0].Attachments[0].Name);
        Assert.Equal("/tmp/screenshot.png", steps[0].Attachments[0].RelativePath);
    }

    [Fact]
    public void Attachment_name_defaults_to_filename_when_not_provided()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        StepCollector.StartStep(_testId, "When", "I upload a file", null, null);
        Track.Attachment("/tmp/reports/result.pdf");
        StepCollector.CompleteStep(_testId, passed: true);

        var steps = StepCollector.GetSteps(_testId);
        Assert.Equal("result.pdf", steps[0].Attachments![0].Name);
    }

    [Fact]
    public void Multiple_attachments_on_same_step()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        StepCollector.StartStep(_testId, "When", "I upload files", null, null);
        Track.Attachment("/tmp/a.png", "First");
        Track.Attachment("/tmp/b.png", "Second");
        StepCollector.CompleteStep(_testId, passed: true);

        var steps = StepCollector.GetSteps(_testId);
        Assert.Equal(2, steps[0].Attachments!.Length);
        Assert.Equal("First", steps[0].Attachments[0].Name);
        Assert.Equal("Second", steps[0].Attachments[1].Name);
    }

    [Fact]
    public void Attachment_when_no_active_step_creates_scenario_level_attachment()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        // No step started — scenario-level attachment
        Track.Attachment("/tmp/log.txt", "Test Log");

        var attachments = StepCollector.GetScenarioAttachments(_testId);
        Assert.NotNull(attachments);
        Assert.Single(attachments);
        Assert.Equal("Test Log", attachments[0].Name);
        Assert.Equal("/tmp/log.txt", attachments[0].RelativePath);
    }

    [Fact]
    public void Attachment_without_test_id_is_noop()
    {
        // No TestIdentityScope — should not throw
        Track.TestIdResolver = null;
        Track.Attachment("/tmp/file.txt", "Orphan");
        // No exception = success
    }

    [Fact]
    public void Attachment_on_nested_step_stored_on_inner_step()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        StepCollector.StartStep(_testId, "Given", "Outer step", null, null);
        StepCollector.StartStep(_testId, null, "Inner step", null, null);
        Track.Attachment("/tmp/inner.png", "Inner File");
        StepCollector.CompleteStep(_testId, passed: true); // complete inner
        StepCollector.CompleteStep(_testId, passed: true); // complete outer

        var steps = StepCollector.GetSteps(_testId);
        Assert.Single(steps);
        Assert.Null(steps[0].Attachments); // outer has no attachments
        Assert.NotNull(steps[0].SubSteps);
        Assert.Single(steps[0].SubSteps);
        Assert.NotNull(steps[0].SubSteps[0].Attachments);
        Assert.Equal("Inner File", steps[0].SubSteps[0].Attachments[0].Name);
    }

    [Fact]
    public void Attachment_with_backslash_path_extracts_correct_filename()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        StepCollector.StartStep(_testId, "When", "I do something", null, null);
        Track.Attachment(@"C:\Users\test\screenshots\page.png");
        StepCollector.CompleteStep(_testId, passed: true);

        var steps = StepCollector.GetSteps(_testId);
        Assert.Equal("page.png", steps[0].Attachments![0].Name);
    }
}
