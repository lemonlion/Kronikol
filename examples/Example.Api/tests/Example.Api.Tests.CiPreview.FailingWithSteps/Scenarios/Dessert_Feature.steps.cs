using System.Net;
using System.Net.Http.Json;
using Example.Api.Requests;
using Example.Api.Responses;
using Example.Api.Tests.CiPreview.FailingWithSteps.Infrastructure;
using FluentAssertions;
using LightBDD.Framework;
using LightBDD.Framework.Scenarios;

namespace Example.Api.Tests.CiPreview.FailingWithSteps.Scenarios;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
public partial class Dessert_Feature : BaseFixture
{
    private readonly CakeRequest _cakeRequest = new();
    private HttpResponseMessage? _cakeResponseMessage;
    private CakeResponse? _cakeResponse;

    #region Given

    private async Task A_valid_cake_request()
    {
        _cakeRequest.Milk = (await Client.GetFromJsonAsync<MilkResponse>("milk"))!.Milk;
        _cakeRequest.Eggs = (await Client.GetFromJsonAsync<EggsResponse>("eggs"))!.Eggs;
        _cakeRequest.Flour = (await Client.GetFromJsonAsync<FlourResponse>("flour"))!.Flour;
    }

    #endregion

    #region When

    /// <summary>
    /// Shape A. The call and the assertion that fails are in the SAME top-level step, which is the only
    /// arrangement that produces an attributed interaction on a failed step. Splitting them - call in
    /// <c>when</c>, assert in <c>then</c> - yields an empty <c>calls</c> table, which is what every
    /// ordinary suite does and is why this shape had never been captured.
    /// </summary>
    private async Task The_cake_is_ordered_and_the_response_is_a_conflict()
    {
        _cakeResponseMessage = await Client.PostAsJsonAsync("cake", _cakeRequest);
        _cakeResponseMessage.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Shape A with a second, distinct message, so the digest has more than one cluster to find.</summary>
    private async Task The_cake_is_ordered_and_the_ingredients_are_wrong()
    {
        _cakeResponseMessage = await Client.PostAsJsonAsync("cake", _cakeRequest);
        _cakeResponse = await _cakeResponseMessage.Content.ReadFromJsonAsync<CakeResponse>();
        _cakeResponse!.Ingredients.Should().Contain("Marzipan");
    }

    /// <summary>Shape B' - a top-level step that fails having made no call, so the empty table is honest.</summary>
    private async Task The_recipe_is_checked_locally_and_is_rejected()
    {
        _cakeRequest.Milk.Should().Be("Buttermilk");
    }

    /// <summary>
    /// Shape D' - the failure is in a nested sub-step. Step attribution is recorded for top-level steps
    /// only, so the failing path (<c>1.2</c>) can never appear in the attribution map and the digest's
    /// wanted-path set cannot match it. Structural, not a threshold.
    /// </summary>
    private async Task<CompositeStep> The_cake_is_ordered_through_a_composite_step()
    {
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => The_order_is_placed(),
                _ => The_placed_order_is_confirmed_as_a_conflict())
            .Build();
    }

    private async Task The_order_is_placed() =>
        _cakeResponseMessage = await Client.PostAsJsonAsync("cake", _cakeRequest);

    private async Task The_placed_order_is_confirmed_as_a_conflict() =>
        _cakeResponseMessage!.StatusCode.Should().Be(HttpStatusCode.Conflict);

    private async Task The_cake_is_ordered() =>
        _cakeResponseMessage = await Client.PostAsJsonAsync("cake", _cakeRequest);

    #endregion

    #region Then

    private async Task The_order_is_recorded() =>
        _cakeResponseMessage!.StatusCode.Should().Be(HttpStatusCode.OK);

    private async Task The_response_is_successful()
    {
        _cakeResponseMessage!.StatusCode.Should().Be(HttpStatusCode.OK);
        _cakeResponse = await _cakeResponseMessage.Content.ReadFromJsonAsync<CakeResponse>();
        _cakeResponse!.Ingredients.Should().Contain(_cakeRequest.Milk);
    }

    #endregion
}
