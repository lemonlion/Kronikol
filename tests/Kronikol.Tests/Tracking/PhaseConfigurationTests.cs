using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

public class PhaseConfigurationTests
{
    public PhaseConfigurationTests()
    {
        TestPhaseContext.Reset();
    }

    // ─── ShouldTrack ────────────────────────────────────────────

    [Fact]
    public void ShouldTrack_returns_true_when_phase_is_Unknown()
    {
        Assert.True(PhaseConfiguration.ShouldTrack(trackDuringSetup: false, trackDuringAction: false));
    }

    [Fact]
    public void ShouldTrack_returns_true_during_Setup_when_enabled()
    {
        TestPhaseContext.Current = TestPhase.Setup;

        Assert.True(PhaseConfiguration.ShouldTrack(trackDuringSetup: true, trackDuringAction: false));
    }

    [Fact]
    public void ShouldTrack_returns_false_during_Setup_when_disabled()
    {
        TestPhaseContext.Current = TestPhase.Setup;

        Assert.False(PhaseConfiguration.ShouldTrack(trackDuringSetup: false, trackDuringAction: true));
    }

    [Fact]
    public void ShouldTrack_returns_true_during_Action_when_enabled()
    {
        TestPhaseContext.Current = TestPhase.Action;

        Assert.True(PhaseConfiguration.ShouldTrack(trackDuringSetup: false, trackDuringAction: true));
    }

    [Fact]
    public void ShouldTrack_returns_false_during_Action_when_disabled()
    {
        TestPhaseContext.Current = TestPhase.Action;

        Assert.False(PhaseConfiguration.ShouldTrack(trackDuringSetup: true, trackDuringAction: false));
    }

    // ─── GetEffectiveVerbosity ──────────────────────────────────

    [Fact]
    public void GetEffectiveVerbosity_returns_default_when_phase_is_Unknown()
    {
        var result = PhaseConfiguration.GetEffectiveVerbosity(
            defaultVerbosity: TestVerbosity.Detailed,
            setupVerbosity: TestVerbosity.Raw,
            actionVerbosity: TestVerbosity.Summarised);

        Assert.Equal(TestVerbosity.Detailed, result);
    }

    [Fact]
    public void GetEffectiveVerbosity_returns_setup_override_during_Setup()
    {
        TestPhaseContext.Current = TestPhase.Setup;

        var result = PhaseConfiguration.GetEffectiveVerbosity(
            defaultVerbosity: TestVerbosity.Detailed,
            setupVerbosity: TestVerbosity.Raw,
            actionVerbosity: TestVerbosity.Summarised);

        Assert.Equal(TestVerbosity.Raw, result);
    }

    [Fact]
    public void GetEffectiveVerbosity_returns_action_override_during_Action()
    {
        TestPhaseContext.Current = TestPhase.Action;

        var result = PhaseConfiguration.GetEffectiveVerbosity(
            defaultVerbosity: TestVerbosity.Detailed,
            setupVerbosity: TestVerbosity.Raw,
            actionVerbosity: TestVerbosity.Summarised);

        Assert.Equal(TestVerbosity.Summarised, result);
    }

    [Fact]
    public void GetEffectiveVerbosity_falls_back_to_default_when_setup_override_is_null()
    {
        TestPhaseContext.Current = TestPhase.Setup;

        var result = PhaseConfiguration.GetEffectiveVerbosity<TestVerbosity>(
            defaultVerbosity: TestVerbosity.Detailed,
            setupVerbosity: null,
            actionVerbosity: TestVerbosity.Summarised);

        Assert.Equal(TestVerbosity.Detailed, result);
    }

    [Fact]
    public void GetEffectiveVerbosity_falls_back_to_default_when_action_override_is_null()
    {
        TestPhaseContext.Current = TestPhase.Action;

        var result = PhaseConfiguration.GetEffectiveVerbosity<TestVerbosity>(
            defaultVerbosity: TestVerbosity.Detailed,
            setupVerbosity: TestVerbosity.Raw,
            actionVerbosity: null);

        Assert.Equal(TestVerbosity.Detailed, result);
    }

    // ─── DrawnAt ────────────────────────────────────────────────

    [Fact]
    public void DrawnAt_is_the_default_alone_when_no_phase_has_a_verbosity_of_its_own()
    {
        var result = PhaseConfiguration.DrawnAt<TestVerbosity>(TestVerbosity.Summarised, setupVerbosity: null, actionVerbosity: null);

        Assert.Equal([TestVerbosity.Summarised], result);
    }

    [Fact]
    public void DrawnAt_adds_each_phase_s_verbosity_while_the_phase_is_Unknown()
    {
        var result = PhaseConfiguration.DrawnAt(TestVerbosity.Summarised, setupVerbosity: TestVerbosity.Raw, actionVerbosity: TestVerbosity.Detailed);

        Assert.Equal([TestVerbosity.Summarised, TestVerbosity.Raw, TestVerbosity.Detailed], result);
    }

    [Fact]
    public void DrawnAt_gives_a_phase_without_a_verbosity_of_its_own_the_default()
    {
        var result = PhaseConfiguration.DrawnAt<TestVerbosity>(TestVerbosity.Summarised, setupVerbosity: null, actionVerbosity: TestVerbosity.Detailed);

        Assert.Equal([TestVerbosity.Summarised, TestVerbosity.Summarised, TestVerbosity.Detailed], result);
    }

    [Theory]
    [InlineData(TestPhase.Setup, TestVerbosity.Raw)]
    [InlineData(TestPhase.Action, TestVerbosity.Detailed)]
    public void DrawnAt_is_the_phase_s_verbosity_alone_once_the_phase_is_known(TestPhase phase, TestVerbosity expected)
    {
        TestPhaseContext.Current = phase;

        var result = PhaseConfiguration.DrawnAt(TestVerbosity.Summarised, setupVerbosity: TestVerbosity.Raw, actionVerbosity: TestVerbosity.Detailed);

        Assert.Equal([expected], result);
    }

    // A tracker reads a body that any verbosity in DrawnAt shows, so every variant AttachVariants builds is at one of them.
    [Theory]
    [InlineData(TestPhase.Unknown, null, null)]
    [InlineData(TestPhase.Unknown, TestVerbosity.Raw, null)]
    [InlineData(TestPhase.Unknown, null, TestVerbosity.Detailed)]
    [InlineData(TestPhase.Unknown, TestVerbosity.Detailed, TestVerbosity.Raw)]
    [InlineData(TestPhase.Setup, TestVerbosity.Raw, TestVerbosity.Detailed)]
    [InlineData(TestPhase.Action, TestVerbosity.Raw, TestVerbosity.Detailed)]
    public void DrawnAt_holds_every_verbosity_a_variant_is_built_at(TestPhase phase, TestVerbosity? setup, TestVerbosity? action)
    {
        TestPhaseContext.Current = phase;
        var built = new List<TestVerbosity>();
        var log = new RequestResponseLog("Test", "id", HttpMethod.Get, null, new Uri("http://localhost/"), [], "Service", "Caller",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

        log.AttachVariants(TestVerbosity.Summarised, setup, action, v =>
        {
            built.Add(v);
            return new PhaseVariant("label", new Uri("http://localhost/"), null, [], false);
        });

        var drawnAt = PhaseConfiguration.DrawnAt(TestVerbosity.Summarised, setup, action);
        Assert.All(built, v => Assert.Contains(v, drawnAt));
        Assert.Contains(PhaseConfiguration.GetEffectiveVerbosity(TestVerbosity.Summarised, setup, action), drawnAt);
    }

    // ─── ResolvePhaseFromStepType ───────────────────────────────

    [Theory]
    [InlineData("Given")]
    [InlineData("GIVEN")]
    [InlineData("given")]
    [InlineData("And")]
    [InlineData("AND")]
    [InlineData("But")]
    [InlineData("BUT")]
    public void ResolvePhaseFromStepType_returns_Setup_for_setup_keywords(string stepType)
    {
        Assert.Equal(TestPhase.Setup, PhaseConfiguration.ResolvePhaseFromStepType(stepType));
    }

    [Theory]
    [InlineData("When")]
    [InlineData("WHEN")]
    [InlineData("when")]
    [InlineData("Then")]
    [InlineData("THEN")]
    [InlineData("then")]
    public void ResolvePhaseFromStepType_returns_Action_for_action_keywords(string stepType)
    {
        Assert.Equal(TestPhase.Action, PhaseConfiguration.ResolvePhaseFromStepType(stepType));
    }

    [Fact]
    public void ResolvePhaseFromStepType_returns_Unknown_for_null()
    {
        Assert.Equal(TestPhase.Unknown, PhaseConfiguration.ResolvePhaseFromStepType(null));
    }

    [Fact]
    public void ResolvePhaseFromStepType_returns_Unknown_for_unrecognised_value()
    {
        Assert.Equal(TestPhase.Unknown, PhaseConfiguration.ResolvePhaseFromStepType("Arrange"));
    }

    // ─── Test helper enum ───────────────────────────────────────

    public enum TestVerbosity
    {
        Raw,
        Detailed,
        Summarised
    }

    // ─── PhaseOfStep and PhaseOfAttribute (plans/STEP_PHASE_PLAN.md 5.1) ─────────

    [Theory]
    [InlineData("Given", TestPhase.Setup)]
    [InlineData("GIVEN", TestPhase.Setup)]
    [InlineData("given", TestPhase.Setup)]
    [InlineData("SETUP", TestPhase.Setup)]
    [InlineData("But", TestPhase.Setup)]
    [InlineData("When", TestPhase.Action)]
    [InlineData("WHEN", TestPhase.Action)]
    [InlineData("Then", TestPhase.Action)]
    [InlineData("THEN", TestPhase.Action)]
    [InlineData("ButWhen", TestPhase.Action)]
    public void PhaseOfStep_reads_a_word_in_any_case(string word, TestPhase expected)
    {
        Assert.Equal(expected, PhaseConfiguration.PhaseOfStep(word, TestPhase.Unknown));
    }

    [Theory]
    [InlineData("And", TestPhase.Action)]
    [InlineData("AND", TestPhase.Setup)]
    [InlineData("and", TestPhase.Unknown)]
    public void PhaseOfStep_reads_And_as_the_phase_before_it(string word, TestPhase previous)
    {
        Assert.Equal(previous, PhaseConfiguration.PhaseOfStep(word, previous));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("Because")]
    public void PhaseOfStep_says_nothing_for_a_word_it_does_not_know(string? word)
    {
        Assert.Null(PhaseConfiguration.PhaseOfStep(word, TestPhase.Action));
    }

    [Theory]
    [InlineData("Given", TestPhase.Setup)]
    [InlineData("But", TestPhase.Setup)]
    [InlineData("When", TestPhase.Action)]
    [InlineData("Then", TestPhase.Action)]
    [InlineData("ButWhen", TestPhase.Action)]
    public void PhaseOfAttribute_reads_todays_five_words(string word, TestPhase expected)
    {
        Assert.Equal(expected, PhaseConfiguration.PhaseOfAttribute(word));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("And")]
    [InlineData("GIVEN")]
    [InlineData("given")]
    [InlineData("Setup")]
    [InlineData("Because")]
    public void PhaseOfAttribute_says_nothing_for_any_other_word(string? word)
    {
        Assert.Null(PhaseConfiguration.PhaseOfAttribute(word));
    }

    [Fact]
    public void ResolvePhaseFromStepType_reads_ButWhen_as_Action()
    {
        Assert.Equal(TestPhase.Action, PhaseConfiguration.ResolvePhaseFromStepType("ButWhen"));
        Assert.Equal(TestPhase.Action, PhaseConfiguration.ResolvePhaseFromStepType("BUTWHEN"));
        Assert.Equal(TestPhase.Setup, PhaseConfiguration.ResolvePhaseFromStepType("But"));
    }

    // The in-process table and ingest's agree on these, and their differences are on purpose (plans/STEP_PHASE_PLAN.md 5.8).
    [Theory]
    [InlineData("Given", TestPhase.Action)]
    [InlineData("When", TestPhase.Setup)]
    [InlineData("Then", TestPhase.Setup)]
    [InlineData("And", TestPhase.Setup)]
    [InlineData("And", TestPhase.Action)]
    [InlineData("ButWhen", TestPhase.Setup)]
    public void PhaseOfStep_and_ingest_agree(string word, TestPhase previous)
    {
        Assert.Equal(Kronikol.Ingestion.IngestAttribution.PhaseForStep(word, null, previous), PhaseConfiguration.PhaseOfStep(word, previous));
    }

    [Fact]
    public void PhaseOfStep_and_ingest_differ_on_But_setup_and_a_word_neither_knows_on_purpose()
    {
        // But is [ButStep]'s here, a setup step, as a framework's But reaches this table as the keyword it continues. Ingest has only the word.
        Assert.Equal(TestPhase.Setup, PhaseConfiguration.PhaseOfStep("But", TestPhase.Action));
        Assert.Equal(TestPhase.Action, Kronikol.Ingestion.IngestAttribution.PhaseForStep("But", null, TestPhase.Action));

        // LightBDD's own step type, which no Gherkin step is.
        Assert.Equal(TestPhase.Setup, PhaseConfiguration.PhaseOfStep("setup", TestPhase.Action));
        Assert.Equal(TestPhase.Action, Kronikol.Ingestion.IngestAttribution.PhaseForStep("setup", null, TestPhase.Action));

        // A word neither knows says nothing here, and keeps the step before in ingest.
        Assert.Null(PhaseConfiguration.PhaseOfStep("Because", TestPhase.Action));
        Assert.Equal(TestPhase.Action, Kronikol.Ingestion.IngestAttribution.PhaseForStep("Because", null, TestPhase.Action));
    }
}
