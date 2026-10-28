using System.Text;
using Kronikol.Reports;

namespace Kronikol.Naming;

/// <summary>
/// A suite's own spellings of the words in the titles Kronikol makes (<see cref="ReportConfigurationOptions.TitleWords"/>,
/// plans/TITLES_AS_WRITTEN_PLAN.md 4.5): an entry matches a run of whole words whose letters and digits, read without
/// case, are the entry's own, and the run then reads as the entry is written. So <c>AcmePay</c> mends "Acme pay",
/// <c>gRPC</c> both "Grpc" and "g RPC", and <c>MCA</c> BDDfy's "m c a".
/// </summary>
/// <remarks>
/// Longer entries are tried first. The punctuation before the run's first word and after its last stays, and so do the
/// digits written after its last word (<c>xUnit</c> mends "X unit3" into "xUnit3"); a mark inside the run is no part of
/// a word, so a run with one never matches and nothing is dropped. A title's arguments (what follows its words in
/// brackets) and a value written into it in quotes (LightBDD's) are the row's values, and are left as they are. An
/// entry with no letter or digit is ignored.
/// </remarks>
internal sealed class TitleWords
{
    private readonly (string Spelling, string Letters)[] _entries;

    public TitleWords(IEnumerable<string>? entries) =>
        _entries = (entries ?? [])
            .Where(entry => entry is not null)
            .Select(entry => (Spelling: entry.Trim(), Letters: LettersOf(entry)))
            .Where(entry => entry.Letters.Length > 0)
            // stable, so of two entries with the same letters the first listed wins
            .OrderByDescending(entry => entry.Letters.Length)
            .ToArray();

    /// <summary>Whether no entry has a letter or digit, so that nothing would change.</summary>
    public bool IsEmpty => _entries.Length == 0;

    /// <summary>
    /// Rewrites the titles Kronikol made in a finished model (a feature, a scenario and an outline's title each marked as
    /// made, never a name a person wrote), recording first what each id is computed from, so that adding, editing or
    /// removing an entry never moves an id. Runs after <see cref="StepText.ApplyToTitles"/>, which re-cases the names it
    /// would otherwise record.
    /// </summary>
    public void ApplyTo(IEnumerable<Feature>? features)
    {
        if (IsEmpty || features is null)
            return;
        foreach (var feature in features)
        {
            if (feature.NameGenerated && Apply(feature.DisplayName) is var name && name != feature.DisplayName)
            {
                feature.StableIdName ??= feature.DisplayName;
                feature.DisplayName = name;
            }

            foreach (var scenario in feature.Scenarios ?? [])
            {
                if (scenario.NameGenerated && Apply(scenario.DisplayName) is var shown && shown != scenario.DisplayName)
                {
                    scenario.StableIdKey ??= new ScenarioIdKey(ScenarioStableId.KeyName(scenario), scenario.OutlineId, scenario.ExampleValues);
                    scenario.DisplayName = shown;
                }

                // the heading's words only: OutlineId is the key the rows are grouped and hashed by, and stays
                if (scenario.OutlineTitleGenerated && scenario.OutlineTitle is { } title)
                    scenario.OutlineTitle = Apply(title);
            }
        }
    }

    /// <summary>The title with each run of words an entry matches written as the entry is.</summary>
    public string Apply(string title)
    {
        if (IsEmpty || string.IsNullOrEmpty(title))
            return title;

        // a prefix of the title: what follows is its arguments
        var words = ParameterParser.ExtractBaseName(title) ?? title;
        var text = new StringBuilder(title.Length);
        var start = 0;
        while (start < words.Length)
        {
            var open = words.IndexOf('"', start);
            var close = open < 0 ? -1 : words.IndexOf('"', open + 1);
            if (close < 0)
            {
                text.Append(Rewrite(words[start..]));
                break;
            }

            text.Append(Rewrite(words[start..open])).Append(words, open, close - open + 1);
            start = close + 1;
        }

        return text.Append(title, words.Length, title.Length - words.Length).ToString();
    }

    private string Rewrite(string segment)
    {
        var words = Words(segment);
        if (words.Count == 0)
            return segment;

        var text = new StringBuilder(segment.Length);
        var written = 0;
        for (var i = 0; i < words.Count; i++)
        {
            if (Match(segment, words, i) is not { } match)
                continue;

            var first = words[i];
            var last = words[match.LastWord];
            text.Append(segment, written, first.Start - written)
                .Append(segment, first.Start, first.Lead)
                .Append(match.Spelling)
                .Append(segment, last.CoreEnd - match.Digits, match.Digits)
                .Append(segment, last.CoreEnd, last.End - last.CoreEnd);
            written = last.End;
            i = match.LastWord;
        }

        return written == 0 ? segment : text.Append(segment, written, segment.Length - written).ToString();
    }

    private (string Spelling, int LastWord, int Digits)? Match(string segment, List<Word> words, int at)
    {
        foreach (var (spelling, letters) in _entries)
        {
            var matched = 0;
            for (var j = at; j < words.Count; j++)
            {
                var word = words[j];
                // a mark before an inner word or inside any word parts it from the run
                if ((j > at && word.Lead > 0) || !word.Whole)
                    break;

                var core = segment.AsSpan(word.CoreStart, word.CoreEnd - word.CoreStart);
                var rest = letters.AsSpan(matched);
                var shared = 0;
                while (shared < core.Length && shared < rest.Length && char.ToLowerInvariant(core[shared]) == rest[shared])
                    shared++;

                if (shared == rest.Length && IsDigits(core[shared..]))
                    return (spelling, j, core.Length - shared);
                // a word that is only partly the entry, or the run went on past a mark after its word
                if (shared < core.Length || word.CoreEnd < word.End)
                    break;
                matched += shared;
            }
        }

        return null;
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
            if (!char.IsDigit(c))
                return false;
        return true;
    }

    private static string LettersOf(string text)
    {
        var letters = new StringBuilder(text.Length);
        foreach (var c in text)
            if (char.IsLetterOrDigit(c))
                letters.Append(char.ToLowerInvariant(c));
        return letters.ToString();
    }

    // The words of a segment, each with the marks before and after its letters and digits.
    private static List<Word> Words(string segment)
    {
        var words = new List<Word>();
        var i = 0;
        while (i < segment.Length)
        {
            if (char.IsWhiteSpace(segment[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < segment.Length && !char.IsWhiteSpace(segment[i]))
                i++;
            var coreStart = start;
            while (coreStart < i && !char.IsLetterOrDigit(segment[coreStart]))
                coreStart++;
            var coreEnd = i;
            while (coreEnd > coreStart && !char.IsLetterOrDigit(segment[coreEnd - 1]))
                coreEnd--;
            var whole = coreEnd > coreStart;
            for (var c = coreStart; whole && c < coreEnd; c++)
                whole = char.IsLetterOrDigit(segment[c]);
            words.Add(new Word(start, coreStart, coreEnd, i, whole));
        }

        return words;
    }

    private readonly record struct Word(int Start, int CoreStart, int CoreEnd, int End, bool Whole)
    {
        public int Lead => CoreStart - Start;
    }
}
