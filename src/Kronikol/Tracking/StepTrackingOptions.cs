namespace Kronikol.Tracking;

/// <summary>
/// Configuration options for step tracking behavior.
/// Set via <see cref="StepCollector.Options"/> static property.
/// </summary>
public record StepTrackingOptions
{
    /// <summary>Whether to prepend keyword ("Given", "When", "Then") to step text in reports. Default: true.</summary>
    public bool PrependKeyword { get; set; } = true;

    /// <summary>Whether to include parameter values inline in step text. Default: true.</summary>
    public bool InlineParameters { get; set; } = true;

    /// <summary>Whether a step attribute's keyword moves the test's phase ([GivenStep] and [ButStep] to Setup, [WhenStep], [ThenStep] and [ButWhenStep] to Action), for a step with no keyword above it that does so. A framework's own steps are not step attributes: Reqnroll's, LightBDD's and BDDfy's move the phase whatever this says. Default: true.</summary>
    public bool WhenTriggersAction { get; set; } = true;

    /// <summary>Whether to insert step delimiter hnotes into sequence diagrams when top-level steps start. Default: true.</summary>
    public bool ShowStepDelimiters { get; set; } = true;

    /// <summary>Whether tracked assertions (via Track.That) appear as sub-steps in the report step list. Default: true.</summary>
    public bool IncludeTrackedAssertionsInStepList { get; set; } = true;
}
