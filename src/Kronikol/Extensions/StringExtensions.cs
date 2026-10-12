namespace Kronikol.Extensions;

/// <summary>
/// Provides common string manipulation extension methods used throughout the library.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// <paramref name="value"/> in consecutive chunks of at most <paramref name="chunkLength"/> characters. A chunk never
    /// ends between the two halves of a character written as two UTF-16 code units (an emoji, say), since either half
    /// alone draws as U+FFFD: such a chunk ends one code unit early.
    /// </summary>
    public static IEnumerable<string> ChunksUpTo(this string value, int chunkLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkLength);
        return Chunks(value, chunkLength);

        static IEnumerable<string> Chunks(string value, int chunkLength)
        {
            for (var at = 0; at < value.Length;)
            {
                var length = Math.Min(chunkLength, value.Length - at);
                if (length > 1 && at + length < value.Length && char.IsHighSurrogate(value[at + length - 1]))
                    length--;
                yield return value.Substring(at, length);
                at += length;
            }
        }
    }

    public static string StringJoin(this IEnumerable<string> value, string separator) => string.Join(separator, value);
    
    public static string TrimEnd(this string stringToTrimFrom, string trimValue)
    {
        return stringToTrimFrom.EndsWith(trimValue) ? stringToTrimFrom.Remove(stringToTrimFrom.Length - trimValue.Length) : stringToTrimFrom;
    }
}