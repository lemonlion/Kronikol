using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil.Cil;

namespace Kronikol.AssertionTracking;

/// <summary>
/// The source of each PDB document, read as the compiler read it (#165). A portable PDB's document name is whatever
/// the compiler was told to write: in a build with deterministic source paths (<c>ContinuousIntegrationBuild</c>
/// turns them on) or a project's own <c>PathMap</c> it is <c>/_/…</c> or another prefix that no machine has, so the
/// name alone is no path. A document's bytes come from, in this order: a file the build's own map leads back to (each
/// <c>PathMap</c> entry and source root whose mapped path begins the name, the longest first, then the name as written),
/// taken only when its bytes hash to the checksum the PDB records; the source the PDB embeds; and the name as written
/// when the file is there but fails the checksum, which is what a weave read before. The bytes are decoded and split
/// into lines by the compiler's rules, so a sequence point's line and columns fall on the text they were counted in.
/// </summary>
internal sealed class SourceDocuments
{
    /// <summary>The kind of Roslyn's <c>TypeDefinitionDocuments</c> custom debug information.</summary>
    internal static readonly Guid TypeDefinitionDocumentsKind = new("932e74bc-dba9-4478-8d46-0f32a7bab3d3");

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>UTF-8 with U+FFFD for each byte it cannot read: the compiler's fallback on Linux and macOS.</summary>
    private static readonly Encoding ReplacingUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>Roslyn's <c>LargeObjectHeapLimitInChars</c>: a text it reckons at this many characters or more is read
    /// through a <c>StreamReader</c>.</summary>
    private const int LargeTextChars = 40 * 1024;

    private readonly IReadOnlyList<KeyValuePair<string, string>> _map;
    private readonly Encoding? _codePage;
    private readonly Dictionary<string, string[]?> _lines = new(StringComparer.Ordinal);
    private readonly List<string> _found = new();
    private readonly List<UnreadSource> _unread = new();

    /// <param name="map">Each local directory and the name the compiler wrote for it (a <c>PathMap</c> entry's key
    /// and value, or a source root and its <c>MappedPath</c>), the <c>PathMap</c> entries first.</param>
    /// <param name="codePage">The encoding of the build's <c>CodePage</c>, or null when it sets none.</param>
    public SourceDocuments(IEnumerable<KeyValuePair<string, string>> map, Encoding? codePage)
    {
        // The longest mapped path first, as written where two are as long: a name two entries reach is read from the
        // more specific one when the PDB has no checksum to tell them apart. OrderByDescending is stable.
        _map = map
            .Where(e => e.Key.Length > 0 && e.Value.Length > 0)
            .Select(e => new KeyValuePair<string, string>(EnsureTrailingSeparator(e.Key), EnsureTrailingSeparator(e.Value)))
            .OrderByDescending(e => e.Value.Length)
            .ToList();
        _codePage = codePage;
    }

    /// <summary>Where each document read through a map, or from the PDB, was found: one line each, for the build's
    /// low-importance log.</summary>
    public IReadOnlyList<string> Found => _found;

    /// <summary>The documents no candidate answered for, with every path tried.</summary>
    public IReadOnlyList<UnreadSource> Unread => _unread;

    /// <summary>The document's lines as the compiler split them, or null when its source cannot be read.</summary>
    public string[]? Lines(Document document)
    {
        if (_lines.TryGetValue(document.Url, out var cached))
            return cached;

        var lines = Read(document);
        _lines[document.Url] = lines;
        return lines;
    }

    /// <summary>Counts an assertion statement the weave wrapped toward its document's <see cref="UnreadSource"/>, when
    /// its source could not be read.</summary>
    public void CountStatement(Document document)
    {
        foreach (var unread in _unread)
        {
            if (string.Equals(unread.Document, document.Url, StringComparison.Ordinal))
            {
                unread.Statements++;
                return;
            }
        }
    }

    private string[]? Read(Document document)
    {
        var name = document.Url;
        var candidates = Candidates(name);
        var checksum = Checksum.For(document);

        // 1. A file the compiler's map leads back to, or the name itself, with the bytes the compiler hashed.
        byte[]? unverified = null;
        var tried = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (ReadFile(candidate) is not { } bytes)
            {
                tried.Add(candidate);
                continue;
            }
            if (checksum is null || checksum.Matches(bytes))
            {
                if (!string.Equals(candidate, name, StringComparison.Ordinal))
                    _found.Add($"Read the source of {name} from {candidate}");
                return SplitLines(Decode(bytes, _codePage));
            }

            // A file under a mapped name that fails the checksum is another file; the name as written is what the
            // weave read before the PDB's checksum was looked at, so it stays the last resort.
            if (string.Equals(candidate, name, StringComparison.Ordinal))
                unverified = bytes;
            tried.Add(candidate + " (changed since the compile)");
        }

        // 2. The source the PDB embeds: the compiler's own bytes. Read only when no file answered, since Cecil
        // deflates an embedded source it has read again when it writes the PDB, which would change the woven PDB.
        if (EmbeddedSource(document) is { } embedded)
        {
            _found.Add($"Read the source of {name} from the source the PDB embeds");
            return SplitLines(Decode(embedded, _codePage));
        }

        // 3. The name as written, though it does not hash to the PDB's checksum.
        if (unverified is not null)
        {
            _found.Add($"Read the source of {name} as written, though it does not match the checksum the PDB records");
            return SplitLines(Decode(unverified, _codePage));
        }

        _unread.Add(new UnreadSource(name, tried));
        return null;
    }

    /// <summary>
    /// The files a document name can have come from: for each mapped path that begins the name, the local directory
    /// and the rest of the name, the longest mapped path first; then the name as written.
    /// </summary>
    internal IReadOnlyList<string> Candidates(string name)
    {
        var candidates = new List<string>();
        foreach (var entry in _map)
        {
            if (!name.StartsWith(entry.Value, StringComparison.Ordinal))
                continue;
            var candidate = entry.Key + WithSeparatorsOf(entry.Key, name.Substring(entry.Value.Length));
            if (!candidates.Contains(candidate, StringComparer.Ordinal))
                candidates.Add(candidate);
        }

        if (!candidates.Contains(name, StringComparer.Ordinal))
            candidates.Add(name);
        return candidates;
    }

    /// <summary>
    /// The rest of a mapped name with its separators turned back to the local directory's kind. The compiler turns
    /// every separator of the mapped name to the mapped path's kind when that path uses only one
    /// (<c>PathUtilities.NormalizePathPrefix</c>), so on Linux a map to a value written with <c>\</c> leaves no
    /// <c>/</c> to find.
    /// </summary>
    private static string WithSeparatorsOf(string directory, string rest)
    {
        var slash = directory.IndexOf('/') >= 0;
        var backslash = directory.IndexOf('\\') >= 0;
        if (slash && !backslash)
            return rest.Replace('\\', '/');
        if (backslash && !slash)
            return rest.Replace('/', '\\');
        return rest;
    }

    private static byte[]? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            // Shared for reading and writing, as the compiler opens a source: a file another process holds open for
            // writing (an editor, a watcher) is read, not refused.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static byte[]? EmbeddedSource(Document document)
    {
        if (!document.HasCustomDebugInformations)
            return null;
        foreach (var information in document.CustomDebugInformations)
        {
            if (information is EmbeddedSourceDebugInformation embedded && embedded.Content is { Length: > 0 } content)
                return content;
        }

        return null;
    }

    /// <summary>
    /// The text of a source file's bytes, as the compiler decodes them (Roslyn's <c>EncodedStringText.Create</c> and
    /// <c>SourceText.Decode</c>): a byte order mark's encoding (UTF-8, UTF-16 little or big endian) wins; else the
    /// build's <c>CodePage</c>, when it sets one, for every file without a mark, valid UTF-8 or not; else strict UTF-8,
    /// and on bytes that are not UTF-8, the fallback Roslyn asks for as code page 0: on Linux and macOS, which have no
    /// ANSI code page, UTF-8 with U+FFFD for each byte it cannot read; on Windows the machine's ANSI code page, read
    /// here as Windows-1252, the one machine setting not followed: a build that depends on it reads each file
    /// differently on each machine. Roslyn reads a text of 40 K characters or more through a <c>StreamReader</c>,
    /// which knows UTF-32's byte order marks too, and a smaller one from its bytes, where FF FE is UTF-16's.
    /// </summary>
    internal static string Decode(byte[] bytes, Encoding? codePage) =>
        Decode(bytes, codePage, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    /// <summary><see cref="Decode(byte[], Encoding?)"/> with the platform given, so both fallbacks can be tested on
    /// either.</summary>
    internal static string Decode(byte[] bytes, Encoding? codePage, bool onWindows)
    {
        if ((codePage ?? StrictUtf8).GetMaxCharCount(bytes.Length) >= LargeTextChars && bytes.Length >= 4)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
                return new UTF32Encoding(bigEndian: false, byteOrderMark: true).GetString(bytes, 4, bytes.Length - 4);
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes, 4, bytes.Length - 4);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (codePage is not null)
            return codePage.GetString(bytes);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return onWindows ? Windows1252(bytes) : ReplacingUtf8.GetString(bytes);
        }
    }

    /// <summary>
    /// The lines of a text as the compiler counts them: a line ends at CR, LF, CRLF, U+0085, U+2028 or U+2029. As
    /// <c>File.ReadAllLines</c> does, a text that ends with a line break has no empty line after it.
    /// </summary>
    internal static string[] SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n' && c != '\u0085' && c != '\u2028' && c != '\u2029')
                continue;
            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text.Substring(start));
        return lines.ToArray();
    }

    // Windows-1252's 0x80 to 0x9F; the five bytes it leaves undefined decode to the control character of their own
    // value, as .NET's and Windows' decoders do.
    private static readonly char[] Windows1252High =
    {
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    };

    /// <summary>Windows-1252, decoded here: the task runs in MSBuild on .NET, which has no code pages but its own
    /// unless one is registered.</summary>
    internal static string Windows1252(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b >= 0x80 && b <= 0x9F ? Windows1252High[b - 0x80] : (char)b;
        }

        return new string(chars);
    }

    /// <summary>
    /// The encoding of a build's <c>CodePage</c> (<c>$(CodePage)</c>, the compiler's <c>/codepage</c>), or null when it
    /// sets none (empty or 0). The compiler takes a positive number only. MSBuild on .NET knows only the Unicode code
    /// pages and Latin-1 by itself, so the others come from the runtime's code pages provider, as the compiler gets
    /// them; one that cannot be had is noted in <paramref name="note"/>, and files are then read as with no
    /// <c>CodePage</c>.
    /// </summary>
    internal static Encoding? CodePageEncoding(string? value, out string? note)
    {
        note = null;
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // MSBuild hands Csc the property as a number, a sign and spaces allowed: +1252 reaches it as /codepage:1252.
        if (!int.TryParse(value!.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var codePage))
        {
            note = $"CodePage '{value}' is not a code page; the sources are read as in a build without one";
            return null;
        }

        if (codePage <= 0)
            return null;

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException)
        {
            // Not registered in this process; the runtime's provider has it.
        }

        try
        {
            var provider = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages", throwOnError: false)
                ?.GetProperty("Instance")?.GetValue(null) as EncodingProvider;
            if (provider?.GetEncoding(codePage) is { } encoding)
                return encoding;
        }
        catch (Exception)
        {
            // Treated as not available below.
        }

        if (codePage == 1252)
            return Windows1252Encoding.Instance;

        note = $"CodePage {codePage} is not available to the weave; the sources are read as in a build without one";
        return null;
    }

    /// <summary>
    /// The compiler's <c>/pathmap</c> parsing (Roslyn's <c>CommandLineParser.ParsePathMap</c>): entries split on a
    /// single <c>,</c>, key and value on a single <c>=</c>, a doubled one read as the character itself, an empty entry
    /// skipped, and a separator added to each side that lacks one. An entry the compiler refuses (not one <c>=</c>, or
    /// an empty side), which cannot reach a successful compile, is skipped and noted in <paramref name="refused"/>.
    /// </summary>
    internal static List<KeyValuePair<string, string>> ParsePathMap(string? pathMap, ICollection<string> refused)
    {
        var map = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(pathMap))
            return map;

        foreach (var entry in SplitWithDoubledSeparatorEscaping(pathMap!, ','))
        {
            if (entry.Length == 0)
                continue;
            var keyAndValue = SplitWithDoubledSeparatorEscaping(entry, '=');
            if (keyAndValue.Count != 2 || keyAndValue[0].Length == 0 || keyAndValue[1].Length == 0)
            {
                refused.Add(entry);
                continue;
            }

            map.Add(new KeyValuePair<string, string>(EnsureTrailingSeparator(keyAndValue[0]), EnsureTrailingSeparator(keyAndValue[1])));
        }

        return map;
    }

    /// <summary>Splits on <paramref name="separator"/>, reading two in a row as one character of the part
    /// (<c>"a,,b,c"</c> on <c>,</c> is <c>"a,b"</c> and <c>"c"</c>), as the compiler does.</summary>
    private static List<string> SplitWithDoubledSeparatorEscaping(string text, char separator)
    {
        var parts = new List<string>();
        if (text.Length == 0)
            return parts;

        var part = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == separator)
            {
                if (i < text.Length && text[i] == separator)
                {
                    i++;
                }
                else
                {
                    parts.Add(part.ToString());
                    part.Clear();
                    continue;
                }
            }

            part.Append(c);
        }

        parts.Add(part.ToString());
        return parts;
    }

    /// <summary>The compiler's <c>PathUtilities.EnsureTrailingSeparator</c>: a path that ends without a separator
    /// gains the kind it uses throughout, or the platform's when it uses none or both.</summary>
    internal static string EnsureTrailingSeparator(string path)
    {
        if (path.Length == 0 || path[path.Length - 1] is '/' or '\\')
            return path;

        var slash = path.IndexOf('/') >= 0;
        var backslash = path.IndexOf('\\') >= 0;
        if (slash && !backslash)
            return path + '/';
        if (backslash && !slash)
            return path + '\\';
        return path + Path.DirectorySeparatorChar;
    }

    /// <summary>The checksum the PDB records for a document, and the algorithm it was taken with.</summary>
    private sealed class Checksum
    {
        private readonly byte[] _hash;
        private readonly DocumentHashAlgorithm _algorithm;

        private Checksum(byte[] hash, DocumentHashAlgorithm algorithm)
        {
            _hash = hash;
            _algorithm = algorithm;
        }

        public static Checksum? For(Document document) =>
            document.Hash is { Length: > 0 } hash && document.HashAlgorithm is DocumentHashAlgorithm.SHA1 or DocumentHashAlgorithm.SHA256 or DocumentHashAlgorithm.MD5
                ? new Checksum(hash, document.HashAlgorithm)
                : null;

        public bool Matches(byte[] bytes)
        {
            using HashAlgorithm algorithm = _algorithm switch
            {
                DocumentHashAlgorithm.SHA256 => SHA256.Create(),
                DocumentHashAlgorithm.SHA1 => SHA1.Create(),
                _ => MD5.Create(),
            };
            return algorithm.ComputeHash(bytes).SequenceEqual(_hash);
        }
    }

    /// <summary>Windows-1252 as an <see cref="Encoding"/>, for a build whose <c>CodePage</c> is 1252 where the runtime
    /// has no provider for it. Decoding only: the weave never writes a source file.</summary>
    private sealed class Windows1252Encoding : Encoding
    {
        public static readonly Windows1252Encoding Instance = new();

        public override int GetByteCount(char[] chars, int index, int count) => throw new NotSupportedException();

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
            throw new NotSupportedException();

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            var text = Windows1252(bytes.Skip(byteIndex).Take(byteCount).ToArray());
            text.CopyTo(0, chars, charIndex, text.Length);
            return text.Length;
        }

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}

/// <summary>A document whose source no candidate answered for: its statements are labelled by their line, show no
/// values, and their pragma comments are not read.</summary>
public sealed class UnreadSource
{
    public UnreadSource(string document, IReadOnlyList<string> tried)
    {
        Document = document;
        Tried = tried;
    }

    /// <summary>The document's name in the PDB.</summary>
    public string Document { get; }

    /// <summary>Every path tried, in the order tried.</summary>
    public IReadOnlyList<string> Tried { get; }

    /// <summary>The assertion statements of the document the weave wrapped, each labelled by its line.</summary>
    public int Statements { get; internal set; }
}
