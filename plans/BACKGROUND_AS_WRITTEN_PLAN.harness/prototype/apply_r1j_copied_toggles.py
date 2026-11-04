"""R1, after a last read of the plan: `ResolvedToggleDefaults.BuiltIn with { BackgroundStepsOpen = false }`, a record a caller of the
public `GenerateHtmlReport` can pass, cloned `BackgroundStepsOpenOnFailure` with it and so opened for a failure though the caller said
false. Setting the property now clears the flag. The Overlay sets the flag after the property, so it still says what it means there."""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def sub(path, old, new):
    target = root / path
    text = target.read_bytes().decode("utf-8")
    assert text.count(old) == 1, old[:80]
    target.write_bytes(text.replace(old, new).encode("utf-8"))


sub(
    "src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
    """    public bool BackgroundStepsOpen { get; init; }
    /// <summary>Whether a section holding a failed step opens though <see cref="BackgroundStepsOpen"/> is false: so when
    /// <see cref="ReportToggleDefaults.BackgroundStepsOpen"/> was left unset (#222). A value set there, or a record built by
    /// hand, says what it says.</summary>
""",
    """    public bool BackgroundStepsOpen
    {
        get;
        init
        {
            field = value;
            BackgroundStepsOpenOnFailure = false;
        }
    }
    /// <summary>Whether a section holding a failed step opens though <see cref="BackgroundStepsOpen"/> is false: so when
    /// <see cref="ReportToggleDefaults.BackgroundStepsOpen"/> was left unset (#222). A value set there, on a record built by
    /// hand or on a copy of <see cref="BuiltIn"/>, says what it says, which is why setting that property clears this.</summary>
""",
)

sub(
    "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    """    [Fact]
    public void A_toggle_record_built_by_hand_says_what_it_says()
    {
        var features = new[]
""",
    """    [Fact]
    public void A_toggle_record_built_by_hand_says_what_it_says() =>
        Assert.Contains("<details class=\\"scenario-background\\">", WithAFailedBackgroundStep(new ResolvedToggleDefaults { BackgroundStepsOpen = false }));

    [Fact]
    public void A_copy_of_the_built_in_toggles_with_the_section_set_says_what_it_says() =>
        Assert.Contains("<details class=\\"scenario-background\\">", WithAFailedBackgroundStep(ResolvedToggleDefaults.BuiltIn with { BackgroundStepsOpen = false }));

    [Fact]
    public void A_copy_of_the_built_in_toggles_for_another_setting_still_opens_for_a_failure() =>
        Assert.Contains("<details class=\\"scenario-background\\" data-holds-failure open>", WithAFailedBackgroundStep(ResolvedToggleDefaults.BuiltIn with { NotePayloadFormat = NotePayloadFormat.Json }));

    private static string WithAFailedBackgroundStep(ResolvedToggleDefaults toggles)
    {
        var features = new[]
""",
)
sub(
    "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    """            separateBackgroundSteps: true, toggleDefaults: new ResolvedToggleDefaults { BackgroundStepsOpen = false });

        Assert.Contains("<details class=\\"scenario-background\\">", ReportHtml.Read(path));
    }
""",
    """            separateBackgroundSteps: true, toggleDefaults: toggles);

        return ReportHtml.Read(path);
    }
""",
)
print("applied")
