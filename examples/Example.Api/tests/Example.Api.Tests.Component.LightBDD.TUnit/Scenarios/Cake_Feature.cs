using Example.Api.Tests.Component.LightBDD.TUnit.Models;
using Kronikol.LightBDD;
using Kronikol.LightBDD.TUnit;
using LightBDD.Framework;
using LightBDD.Framework.Scenarios;
using LightBDD.TabularAttributes;
using LightBDD.TabularAttributes.Attributes;

namespace Example.Api.Tests.Component.LightBDD.TUnit.Scenarios;

[FeatureDescription("/cake")]
public partial class Cake_Feature
{
    [HappyPath]
    [Scenario]
    public async Task Calling_Create_Cake_Endpoint_Successfully()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_post_request_for_the_Cake_endpoint(),
            when => The_request_is_sent_to_the_cake_post_endpoint(),
            then => The_response_should_be_successful());
    }

    [Scenario]
    public async Task Calling_Create_Cake_Endpoint_Without_Eggs()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_post_request_for_the_Cake_endpoint(),
            but => The_request_body_is_missing_eggs(),
            when => The_request_is_sent_to_the_cake_post_endpoint(),
            then => The_response_http_status_should_be_bad_request());
    }

    [Scenario]
    [HeadIn("Ingredient")][HeadOut("Response Status", "Error Message")]
    [Inputs("Eggs"      )][Outputs("BadRequest",      "The Eggs field is required.")]
    [Inputs("Milk"      )][Outputs("BadRequest",      "The Milk field is required.")]
    [Inputs("Flour"     )][Outputs("BadRequest",      "The Flour field is required.")]
    public async Task Calling_Create_Cake_Endpoint_Without_Ingredient()
    {
        await Runner.RunScenarioAsync(
            given => A_valid_post_request_for_the_Cake_endpoint(),
            and => The_request_body_is_missing_a_specified_ingredient(TableFrom.Inputs<MissingIngredientFromCakeRequest>()),
            when => The_requests_are_sent_to_the_cake_post_endpoint(),
            then => The_response_http_status_and_error_message_should_be_matching(VerifiableTableFrom.Outputs<CakeErrorResult>()));
    }
}
