# The hook that closes what a suite's AfterStep left open was a new public method on a public class, found by reading the
# surface after the seventh review's change. Reqnroll runs a non-public hook, inherited or not (probes/non-public-hooks), so it
# is internal, and a fact pins the class's public methods. Usage: apply_r1n_internal_hook.py <root> [fact|hook]
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
what = sys.argv[2] if len(sys.argv) > 2 else "both"


def edit(path, old, new):
    target = root / path
    text = target.read_text(encoding="utf-8")
    assert text.count(old) == 1, (path, text.count(old), old[:80])
    target.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


if what in ("both", "fact"):
    tests = "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs"
    edit(tests, "using System.Globalization;\n", "using System.Globalization;\nusing System.Reflection;\n")
    edit(
        tests,
        """        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }
}

[Collection("DiagramsFetcher")]
public class QueryOverBackgroundStepsTests
""",
        """        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }

    [Fact]
    public void The_hooks_public_methods_are_the_four_they_were()
    {
        var methods = typeof(ReqNRollTrackingHooks).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(["AfterScenario", "AfterStep", "BeforeScenario", "BeforeStep"], methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
    }
}

[Collection("DiagramsFetcher")]
public class QueryOverBackgroundStepsTests
""",
    )

if what in ("both", "hook"):
    edit(
        "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
        "    public void CloseStepsLeftOpen()\n",
        "    internal void CloseStepsLeftOpen()\n",
    )
