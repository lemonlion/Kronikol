using System.Diagnostics;

namespace Kronikol.Tests;

/// <summary>
/// The probe that decides whether the <c>query.py</c> facts run (<see cref="PythonProbe"/>). A busy machine can take
/// seconds to start an interpreter, and the probe waited five, then read the exit code of a process still running,
/// which threw and read as "no python": every script fact of the run skipped and none failed (a full core suite under
/// another session's load skipped 30).
/// </summary>
public class PythonProbeTests
{
    [Fact]
    public void An_interpreter_that_answers_after_six_seconds_still_answers()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");

        Assert.True(PythonProbe.Answers(PythonProbe.Name!, "-c \"import time; time.sleep(6)\"", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void An_interpreter_that_never_answers_is_stopped_and_does_not_count()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var watch = Stopwatch.StartNew();

        Assert.False(PythonProbe.Answers(PythonProbe.Name!, "-c \"import time; time.sleep(300)\"", TimeSpan.FromSeconds(2)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"the probe took {watch.Elapsed} to give up");
    }

    [Fact]
    public void An_interpreter_that_fails_does_not_count()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");

        Assert.False(PythonProbe.Answers(PythonProbe.Name!, "-c \"import sys; sys.exit(3)\"", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_name_not_on_the_path_does_not_count()
    {
        Assert.False(PythonProbe.Answers("kronikol-no-such-interpreter", "--version", TimeSpan.FromSeconds(5)));
    }
}
