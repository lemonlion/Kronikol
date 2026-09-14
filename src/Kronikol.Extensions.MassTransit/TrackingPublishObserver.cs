using MassTransit;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.MassTransit;

/// <summary>
/// MassTransit observer that logs operations to the tracking system.
/// </summary>
public class TrackingPublishObserver(MassTransitTracker tracker, MassTransitTrackingOptions options) : IPublishObserver
{
    public Task PrePublish<T>(PublishContext<T> context) where T : class
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

    public Task PostPublish<T>(PublishContext<T> context) where T : class
    {
        var op = MassTransitOperationClassifier.ClassifyPublish(context);
        tracker.LogPublish(op, context.Message);
        return Task.CompletedTask;
    }

    public Task PublishFault<T>(PublishContext<T> context, Exception exception) where T : class
    {
        var op = new MassTransitOperationInfo(
            MassTransitOperation.PublishFault, typeof(T).Name,
            context.DestinationAddress, context.SourceAddress,
            context.MessageId, context.ConversationId);
        tracker.LogPublishFault(op, exception);
        return Task.CompletedTask;
    }
}
