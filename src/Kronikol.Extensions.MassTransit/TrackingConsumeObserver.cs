using MassTransit;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.MassTransit;

/// <summary>
/// MassTransit observer that logs operations to the tracking system.
/// </summary>
public class TrackingConsumeObserver(MassTransitTracker tracker, MassTransitTrackingOptions options) : IConsumeObserver
{
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class
    {
        if (options.PropagateTestIdentity)
        {
            var testName = context.Headers.Get<string>(TestTrackingMessageHeaders.TestName);
            var testId = context.Headers.Get<string>(TestTrackingMessageHeaders.TestId);
            if (testName is not null && testId is not null)
                TestIdentityScope.SetFromMessage(testName, testId);
        }
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        // The message's identity ends with the message; the next one names its own or none.
        TestIdentityScope.ClearMessageIdentity();
        var op = MassTransitOperationClassifier.ClassifyConsume(context);
        tracker.LogConsume(op, context.Message);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        TestIdentityScope.ClearMessageIdentity();
        var op = new MassTransitOperationInfo(
            MassTransitOperation.ConsumeFault, typeof(T).Name,
            context.ReceiveContext?.InputAddress, context.SourceAddress,
            context.MessageId, context.ConversationId);
        tracker.LogConsumeFault(op, exception);
        return Task.CompletedTask;
    }
}
