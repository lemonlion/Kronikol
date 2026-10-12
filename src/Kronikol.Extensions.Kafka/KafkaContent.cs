using System.Globalization;
using System.Reflection;
using System.Text;
using Confluent.Kafka;
using Kronikol.Constants;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// What a Kafka entry shows of a message (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 4.4, #167): its value as the body,
/// so a JSON payload is indented, offered the YAML view and queryable as an HTTP body is, and its key as the entry's
/// <c>kafka-key</c> header, which a note draws above the body, followed by the message's own headers.
/// </summary>
internal static class KafkaContent
{
    // The headers Kronikol's wrappers add to a message: a consume's entry leaves them out, as a produce's never sees them.
    private static readonly string[] KronikolHeaders =
        [TestTrackingMessageHeaders.TestName, TestTrackingMessageHeaders.TestId, TestTrackingMessageHeaders.TraceId];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The body and headers an entry records for <paramref name="message"/>: none when every verbosity is
    /// <see cref="KafkaTrackingVerbosity.Summarised"/>, the key only with <see cref="KafkaTrackingOptions.LogMessageKey"/>
    /// and the value only with <see cref="KafkaTrackingOptions.LogMessageValue"/>.
    /// </summary>
    internal static KafkaPayload Of<TKey, TValue>(Message<TKey, TValue>? message, KafkaTrackingOptions options, bool leaveOutKronikols)
    {
        if (message is null || !KafkaTracker.ShowsContent(options))
            return KafkaPayload.Empty;

        var headers = new List<(string Key, string? Value)>();
        if (options.LogMessageKey && Text(message.Key) is { } key)
            headers.Add((PlantUmlCreator.MessageKeyHeader, key));
        if (message.Headers is not null)
        {
            foreach (var header in message.Headers)
            {
                if (!leaveOutKronikols || Array.IndexOf(KronikolHeaders, header.Key) < 0)
                    headers.Add((header.Key, HeaderText(header.GetValueBytes())));
            }
        }

        return new KafkaPayload(options.LogMessageValue ? Text(message.Value) : null, headers.ToArray());
    }

    /// <summary>
    /// A key or value as text: a string as it is; bytes as the HTTP handler reads a body (UTF-8 text unless they are
    /// binary, else the JSON documents they hold, else <c>[binary content]</c>); a number, date, id or flag in its
    /// invariant form; a protobuf message as its JSON; anything else as the other trackers serialise it; nothing for
    /// null, <see cref="Null"/> and <see cref="Ignore"/>.
    /// </summary>
    internal static string? Text(object? value) => value switch
    {
        null or Null or Ignore => null,
        string text => text,
        byte[] bytes => BytesText(bytes),
        bool flag => flag ? "true" : "false",
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        Guid id => id.ToString(),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        _ => ProtobufJson(value) ?? TrackingSafeSerializer.Serialize(value),
    };

    private static string BytesText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return "";
        if (!HttpContentReader.ContainsBinaryContent(bytes))
            return Encoding.UTF8.GetString(bytes);
        return HttpContentReader.ExtractJsonDocuments(bytes) ?? "[binary content]";
    }

    // A header's value is its bytes as UTF-8 text when they decode strictly, else binary.
    private static string HeaderText(byte[]? bytes)
    {
        if (bytes is null)
            return "";
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return "[binary content]";
        }
    }

    // Google.Protobuf's JsonFormatter.Default, found through the message's own assembly, so the package takes no
    // reference on Google.Protobuf (Q10).
    private static string? ProtobufJson(object value)
    {
        var message = value.GetType().GetInterface("Google.Protobuf.IMessage");
        if (message is null)
            return null;
        try
        {
            var formatter = message.Assembly.GetType("Google.Protobuf.JsonFormatter");
            var instance = formatter?.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return formatter?.GetMethod("Format", [message])?.Invoke(instance, [value]) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>A Kafka entry's body and headers.</summary>
internal readonly record struct KafkaPayload(string? Body, (string Key, string? Value)[] Headers)
{
    internal static readonly KafkaPayload Empty = new(null, []);

    /// <summary>A hand-written log's content, with no headers.</summary>
    public static implicit operator KafkaPayload(string? body) => new(body, []);
}
