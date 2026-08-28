using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Example.Api.Requests;
using Example.Api.Responses;
using Example.Api.Tests.Component.LightBDD.TUnit.Extensions;
using Example.Api.Tests.Component.LightBDD.TUnit.Infrastructure;
using Example.Api.Tests.Component.LightBDD.TUnit.Models;
using Example.Api.Tests.Component.Shared;
using FluentAssertions;
using LightBDD.Framework;
using LightBDD.Framework.Parameters;
using LightBDD.Framework.Scenarios;
using Kronikol.LightBDD;
using Kronikol.LightBDD.TUnit;

namespace Example.Api.Tests.Component.LightBDD.TUnit.Scenarios;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
public partial class Cake_Feature : BaseFixture
{
    private readonly CakeRequest _cakeRequest = new();
    private HttpResponseMessage? _cakeResponseMessage;
    private string? _cakeResponseString;
    private CakeResponse? _cakeResponse;
    private InputTable<MissingIngredientFromCakeRequest>? _missingIngredientsFromRequest;
    private readonly List<CakeRequest> _cakeRequests = new();
    private readonly List<HttpResponseMessage> _cakeResponseMessages = new();

    private MilkResponse _milkResponse = new();
    private FlourResponse _flourResponse = new();
    private EggsResponse _eggsResponse = new();

    #region Given

    private async Task<CompositeStep> A_valid_post_request_for_the_Cake_endpoint()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_valid_request_body())
            .Build();
    }

    private async Task<CompositeStep> A_valid_request_body()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => The_body_specifies_milk(),
                _ => The_body_specifies_eggs(),
                _ => The_body_specifies_flour())
            .Build();
    }

    private async Task<CompositeStep> The_body_specifies_milk()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => Milk_is_retrieved_from_the_get_milk_endpoint(),
                _ => Retrieved_milk_is_set_on_the_body())
            .Build();
    }

    private async Task Milk_is_retrieved_from_the_get_milk_endpoint()
    {
        _milkResponse = (await Client.GetFromJsonAsync<MilkResponse>("milk"))!;
    }

    private async Task Retrieved_milk_is_set_on_the_body() => _cakeRequest.Milk = _milkResponse.Milk;

    private async Task<CompositeStep> The_body_specifies_eggs()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => Eggs_is_retrieved_from_the_get_eggs_endpoint(),
                _ => Retrieved_eggs_is_set_on_the_body())
            .Build();
    }

    private async Task Eggs_is_retrieved_from_the_get_eggs_endpoint()
    {
        _eggsResponse = (await Client.GetFromJsonAsync<EggsResponse>("eggs"))!;
    }

    private async Task Retrieved_eggs_is_set_on_the_body() => _cakeRequest.Eggs = _eggsResponse.Eggs;

    private async Task<CompositeStep> The_body_specifies_flour()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => Flour_is_retrieved_from_the_get_flour_endpoint(),
                _ => Retrieved_flour_is_set_on_the_body())
            .Build();
    }

    private async Task Flour_is_retrieved_from_the_get_flour_endpoint()
    {
        _flourResponse = (await Client.GetFromJsonAsync<FlourResponse>("flour"))!;
    }

    private async Task Retrieved_flour_is_set_on_the_body() => _cakeRequest.Flour = _flourResponse.Flour;

    private async Task The_request_body_is_missing_eggs() => _cakeRequest.Eggs = null;

    private async Task The_request_body_is_missing_a_specified_ingredient(InputTable<MissingIngredientFromCakeRequest> missingIngredientsFromRequest)
    {
        _missingIngredientsFromRequest = missingIngredientsFromRequest;

        foreach (var missingIngredientFromRequest in _missingIngredientsFromRequest!.Select(x => x.Ingredient))
            _cakeRequests.Add(_cakeRequest.GetWithPropertyRemoved(missingIngredientFromRequest!));
    }

    #endregion

    #region When
    private async Task The_request_is_sent_to_the_cake_post_endpoint()
    {
        // When run by the integration test project, field focus and setup separation are configured via environment variables.
        // Otherwise, no focus is applied — keeping this as a clean example for users.
        if (IntegrationTestConfiguration.IsIntegrationTestMode)
        {
            TrackingDiagramOverride.StartAction();
            IntegrationTestConfiguration.ApplyDiagramFocus();
        }

        _cakeResponseMessage = await Client.PostAsJsonAsync("cake", _cakeRequest);
    }

    private async Task The_requests_are_sent_to_the_cake_post_endpoint()
    {
        for (var i = 0; i < _cakeRequests.Count; i++)
        {
            TrackingDiagramOverride.InsertTestDelimiter($"{i + 1}");
            _cakeResponseMessages.Add(await Client.PostAsJsonAsync("cake", _cakeRequests[i]));
        }
    }

    #endregion

    #region Then
    private async Task<CompositeStep> The_response_should_be_successful()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => The_response_http_status_should_be_ok(),
                _ => The_response_should_be_valid_json(),
                _ => The_response_ingredients_should_include_milk(),
                _ => The_response_ingredients_should_include_eggs(),
                _ => The_response_ingredients_should_include_flour())
            .Build();
    }

    private async Task The_response_http_status_should_be_ok() => _cakeResponseMessage!.StatusCode.Should().Be(HttpStatusCode.OK);

    private async Task The_response_should_be_valid_json()
    {
        _cakeResponseString = await _cakeResponseMessage!.Content.ReadAsStringAsync();
        Json.IsValid(_cakeResponseString).Should().BeTrue();

        _cakeResponse = Json.Deserialize<CakeResponse>(_cakeResponseString)!;
    }

    private async Task The_response_ingredients_should_include_milk() => _cakeResponse!.Ingredients.Should().Contain(_milkResponse.Milk);
    private async Task The_response_ingredients_should_include_eggs() => _cakeResponse!.Ingredients.Should().Contain(_flourResponse.Flour);
    private async Task The_response_ingredients_should_include_flour() => _cakeResponse!.Ingredients.Should().Contain(_eggsResponse.Eggs);

    private async Task The_response_http_status_should_be_bad_request() => _cakeResponseMessage!.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    private async Task The_response_http_status_and_error_message_should_be_matching(VerifiableDataTable<CakeErrorResult> expectedOutputs)
    {
        var results = await _cakeResponseMessages.SelectAsync(async x => await x.Content.ReadFromJsonAsync<ErrorResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

        var cakeErrorResults = results.Select((x, i) => new CakeErrorResult(
            _cakeResponseMessages[i].StatusCode.ToString(),
            x?.Errors.FirstOrDefault().Value?.FirstOrDefault() ?? default
        ));
        expectedOutputs.SetActual(cakeErrorResults);
    }

    #endregion

    public record CakeErrorResult(string? ResponseStatus, string? ErrorMessage);
    public record MissingIngredientFromCakeRequest(string? Ingredient);
}
