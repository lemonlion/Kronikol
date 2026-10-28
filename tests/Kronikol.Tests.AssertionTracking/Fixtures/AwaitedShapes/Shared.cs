// The types every shape uses, compiled after the shape's own file (the shape's usings and its assembly attribute come
// first in the compiled text, so the shape's line numbers are its file's).

public sealed class OrderRequest
{
    public string Id { get; init; } = "";
}

public sealed class OrdersClient
{
    public string ExpectedMessage { get; set; } = "not found";

    public async System.Threading.Tasks.Task<string> GetNameAsync()
    {
        await System.Threading.Tasks.Task.Yield();
        return "order";
    }

    public async System.Threading.Tasks.Task<string> GetOrderAsync(OrderRequest request)
    {
        await System.Threading.Tasks.Task.Yield();
        return request.Id == "known" ? "order" : throw new System.InvalidOperationException("not found");
    }

    public string GetOrder(OrderRequest request) =>
        request.Id == "known" ? "order" : throw new System.InvalidOperationException("not found");

    public async System.Threading.Tasks.Task<string> GetExpectedMessageAsync()
    {
        await System.Threading.Tasks.Task.Yield();
        return ExpectedMessage;
    }
}
