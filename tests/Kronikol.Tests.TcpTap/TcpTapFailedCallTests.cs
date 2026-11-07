using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using MongoDB.Bson;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// A decoded command whose connection closes before its reply (plans/FAILED_CALLS_PLAN.md F26, Q5): it is answered with the close,
/// as a call that threw is, where the tap dropped it and only counted it; and history's earlier rules read no call there.
/// </summary>
public class TcpTapFailedCallTests
{
    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_Redis_command_whose_connection_closes_before_the_reply_is_answered_with_the_close()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "cart:1"));

        harness.Decoder.OnConnectionClosed();

        var (request, _) = FailedCallContract.AssertAnswered(harness.Sink.Logs.ToArray(), new IOException("The connection closed before the server replied."));
        Assert.Equal("redis://db0/cart:1", request.Uri.ToString());
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_MongoDB_command_whose_connection_closes_before_the_reply_is_answered_with_the_close()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, new BsonDocument { { "find", "orders" }, { "$db", "shop" } }));

        harness.Decoder.OnConnectionClosed();

        FailedCallContract.AssertAnswered(harness.Sink.Logs.ToArray(), new IOException("The connection closed before the server replied."));
    }

    [Fact]
    public void History_rules_before_12_read_no_call_where_the_tap_dropped_it()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "cart:1"));

        harness.Decoder.OnConnectionClosed();

        Assert.Equal("!IOException", Assert.Single(InteractionShape.Calls(harness.Sink.Logs, null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Empty(InteractionShape.Calls(harness.Sink.Logs, null, rule));
    }

    [Fact]
    public void A_command_answered_before_the_close_is_answered_once()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "cart:1"));
        harness.ServerToClient(Resp.Bulk("2"));

        harness.Decoder.OnConnectionClosed();

        Assert.Equal("OK", Assert.Single(harness.Sink.Responses).StatusCode?.Value);
    }
}
