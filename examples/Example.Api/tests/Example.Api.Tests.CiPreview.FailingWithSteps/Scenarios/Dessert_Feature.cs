using LightBDD.Framework;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;

namespace Example.Api.Tests.CiPreview.FailingWithSteps.Scenarios;

/// <summary>
/// Every shape the failures digest has to tell apart, in one run.
///
/// <para>The digest emits a <c>calls</c> table only when the failing step itself carries an attributed
/// interaction. Four other shapes produce an empty table for four different reasons, and a reader cannot
/// currently tell them apart. These scenarios are the corpus that makes that distinction assertable from
/// captured output rather than from a hand-built object graph.</para>
/// </summary>
[FeatureDescription(@"Failures that happen inside steps, in every shape the digest has to distinguish.

This run is deliberately red. It is a fixture, not a test of the API.")]
public partial class Dessert_Feature
{
    /// <summary>Shape A - the failing step made the call. This is the only shape that can populate <c>calls</c>.</summary>
    [Scenario]
    public async Task Ordering_a_cake_reports_the_wrong_status()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_cake_request(),
            when => The_cake_is_ordered_and_the_response_is_a_conflict(),
            then => The_order_is_recorded());
    }

    /// <summary>Shape A again, with the same error message - so clustering has something real to cluster.</summary>
    [Scenario]
    public async Task Ordering_a_second_cake_reports_the_wrong_status()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_cake_request(),
            when => The_cake_is_ordered_and_the_response_is_a_conflict(),
            then => The_order_is_recorded());
    }

    /// <summary>Shape A with a different message - so the run has more than one cluster.</summary>
    [Scenario]
    public async Task Ordering_a_cake_returns_the_wrong_ingredients()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_cake_request(),
            when => The_cake_is_ordered_and_the_ingredients_are_wrong(),
            then => The_order_is_recorded());
    }

    /// <summary>Shape B' - the failing step is top-level but made no call at all.</summary>
    [Scenario]
    public async Task Checking_the_recipe_fails_without_calling_anything()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_cake_request(),
            when => The_recipe_is_checked_locally_and_is_rejected(),
            then => The_order_is_recorded());
    }

    /// <summary>Shape D' - the only failing step is nested inside a composite, where attribution is top-level only.</summary>
    [Scenario]
    public async Task Ordering_a_cake_fails_inside_a_nested_step()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_cake_request(),
            when => The_cake_is_ordered_through_a_composite_step(),
            then => The_order_is_recorded());
    }

    /// <summary>A scenario that passes, so the fixture is not uniformly red and the digest has a denominator.</summary>
    [Scenario]
    public async Task Ordering_a_cake_succeeds()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_cake_request(),
            when => The_cake_is_ordered(),
            then => The_response_is_successful());
    }
}
