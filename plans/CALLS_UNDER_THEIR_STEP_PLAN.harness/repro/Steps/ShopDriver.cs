using System.Net;
using System.Net.Http.Json;
using Kronikol.Tracking;
using Shouldly;

namespace Spike.Substeps.Steps;

/// <summary>The sub-steps: plain methods woven by Kronikol.StepTracking, called from Reqnroll step definitions.</summary>
public class ShopDriver(HttpClient client)
{
    private static int _customers;

    public static string NextCustomerName() => $"customer-{Interlocked.Increment(ref _customers)}";

    public string? CustomerId { get; private set; }
    public string? OrderStatus { get; private set; }

    [Step]
    public async Task A_customer_is_registered(string name)
    {
        var response = await client.PostAsJsonAsync("customers", new { name });
        response.EnsureSuccessStatusCode();
        CustomerId = name;
        await Their_address_is_verified();
    }

    [Step]
    public async Task Their_address_is_verified()
    {
        var response = await client.GetAsync($"customers/{CustomerId}/address-check");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Step(Description = "Items are added to the basket")]
    public async Task AddItems(int count)
    {
        for (var i = 0; i < count; i++)
            (await client.PutAsJsonAsync($"basket/{CustomerId}/items/{i}", new { sku = $"SKU-{i}" })).EnsureSuccessStatusCode();
    }

    [Step]
    public async Task An_item_is_added(string sku, int qty)
    {
        (await client.PutAsJsonAsync($"basket/{CustomerId}/items/{sku}", new { sku, qty })).EnsureSuccessStatusCode();
    }

    [Step]
    public async Task The_payment_is_authorised(decimal amount, string card)
    {
        var response = await client.PostAsJsonAsync("payments", new { amount, card });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Step]
    public async Task The_order_is_submitted()
    {
        var response = await client.PostAsJsonAsync("orders", new { customer = CustomerId });
        OrderStatus = (await response.Content.ReadFromJsonAsync<OrderResult>())!.Status;
    }

    [Step]
    public void The_confirmation_email_is_queued()
    {
        OrderStatus.ShouldBe("Confirmed");
    }

    [GivenStep]
    public async Task A_discount_code_is_applied(string code)
    {
        (await client.GetAsync($"discounts/{code}")).EnsureSuccessStatusCode();
    }

    [Step]
    public async Task Reference_data_is_seeded()
    {
        (await client.PostAsync("seed", null)).EnsureSuccessStatusCode();
    }

    private sealed record OrderResult(string Status);
}
