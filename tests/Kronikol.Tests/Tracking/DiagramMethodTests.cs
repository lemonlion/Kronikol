using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class DiagramMethodTests
{
    [Fact]
    public void Implicit_conversion_from_HttpMethod()
    {
        DiagramMethod method = HttpMethod.Get;
        Assert.Equal(HttpMethod.Get, method.Value);
    }

    [Fact]
    public void Implicit_conversion_from_string()
    {
        DiagramMethod method = "Blob Upload";
        Assert.Equal("Blob Upload", method.Value);
    }

    [Fact]
    public void Is_assignable_to_OneOf()
    {
        DiagramMethod method = "Custom Op";
        OneOf<HttpMethod, string> oneOf = method;
        Assert.Equal("Custom Op", oneOf.Value);
    }

    [Fact]
    public void Can_be_used_in_LogPair()
    {
        var testId = Guid.NewGuid().ToString();
        DiagramMethod method = "Cache Get";

        RequestResponseLogger.LogPair(
            testName: "Test",
            testId: testId,
            method: method,
            uri: new Uri("redis://cache/key"),
            serviceName: "Redis",
            callerName: "API");

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Cache Get", logs[0].Method.Value);
    }
}

public class DiagramStatusCodeTests
{
    [Fact]
    public void Implicit_conversion_from_HttpStatusCode()
    {
        DiagramStatusCode status = HttpStatusCode.OK;
        Assert.Equal(HttpStatusCode.OK, status.Value);
    }

    [Fact]
    public void Implicit_conversion_from_string()
    {
        DiagramStatusCode status = "Hit";
        Assert.Equal("Hit", status.Value);
    }

    [Fact]
    public void Is_assignable_to_OneOf()
    {
        DiagramStatusCode status = "Miss";
        OneOf<HttpStatusCode, string> oneOf = status;
        Assert.Equal("Miss", oneOf.Value);
    }
}
