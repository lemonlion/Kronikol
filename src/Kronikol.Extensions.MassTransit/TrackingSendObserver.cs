using MassTransit;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.MassTransit;

/// <summary>
/// MassTransit observer that logs operations to the tracking system.
/// </summary>
public class TrackingSendObserver(MassTransitTracker tracker, MassTransitTrackingOptions options) : ISendObserver
{
    public Task PreSend<T>(SendContext<T> context) where T : class
    {
        if (options.PropagateTestIdentity)
        {
            var testInfo = TestInfoResolver.ResolveWithSource(options.HttpContextAccessor, options.CurrentTestInfoFetcher);
            if (testInfo is not null)
            {
                context.Headers.Set(TestTrackingMessageHeaders.TestName, testInfo.Value.Name);
                context.Headers.Set(TestTrackingMessageHeaders.TestId, testInfo.Value.Id);
            }
        }
        return Task.CompletedTask;
    }

    public Task PostSend<T>(SendContext<T> context) where T : class
    {
        var op = MassTransitOperationClassifier.ClassifySend(context);
        tracker.LogSend(op, context.Message);
        return Task.CompletedTask;
    }

    public Task SendFault<T>(SendContext<T> context, Exception exception) where T : class
    {
        var op = new MassTransitOperationInfo(
            MassTransitOperation.SendFault, typeof(T).Name,
            context.DestinationAddress, context.SourceAddress,
            context.MessageId, context.ConversationId);
        tracker.LogSendFault(op, exception);
        return Task.CompletedTask;
    }
}
