namespace Kronikol.Ingestion;

/// <summary>
/// The attempts a tests file records for each test: every <c>start</c> of one test id opens an attempt, as a runner's
/// own retries within one run keep the test's id (Jest's <c>retryTimes</c>, Playwright's <c>retries</c>). The report
/// shows the last attempt, as the Cucumber Messages lane always has: its records build the scenario, each earlier
/// attempt leaves a <c>retry N</c> label, and the earlier attempts' calls are left out.
/// </summary>
/// <remarks>
/// A record goes to the attempt its own <see cref="TestRunRecord.Attempt"/> names, when a <c>start</c> carries that
/// number; else to the last attempt that started at or before its timestamp (the first, when it is earlier than every
/// start); else, with no timestamp, to the last attempt whose <c>start</c> came before it in the file.
/// </remarks>
internal sealed class TestAttempts
{
    private readonly Dictionary<string, Attempted> _byTest = new(StringComparer.Ordinal);
    private readonly HashSet<TestRunRecord> _earlier = new(ReferenceEqualityComparer.Instance);

    private TestAttempts()
    {
    }

    /// <summary>Tests with more than one <c>start</c> of which at least one after the first carried no attempt number.</summary>
    public int InferredRetries { get; private set; }

    /// <summary>Splits <paramref name="records"/> into attempts, test by test.</summary>
    /// <param name="records">The tests records.</param>
    /// <param name="countsAsStart">
    /// Which <c>start</c> records open an attempt; the others are placed like any other record. Null: every one does.
    /// </param>
    public static TestAttempts Of(IReadOnlyList<TestRunRecord> records, Func<TestRunRecord, bool>? countsAsStart = null)
    {
        var attempts = new TestAttempts();
        var byTest = new Dictionary<string, List<(TestRunRecord Record, int Index)>>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (string.IsNullOrWhiteSpace(record.TestId) || !TestRunRecord.IsKnownEvent(record.Event))
                continue;
            if (!byTest.TryGetValue(record.TestId, out var list))
                byTest[record.TestId] = list = [];
            list.Add((record, i));
        }

        foreach (var (testId, list) in byTest)
        {
            var starts = list
                .Where(x => x.Record.Is(TestRunRecord.Events.Start) && (countsAsStart is null || countsAsStart(x.Record)))
                .OrderBy(x => x.Record.Timestamp ?? DateTimeOffset.MinValue)
                .ThenBy(x => x.Index)
                .ToList();
            if (starts.Count == 0)
                continue;

            var numbers = starts.Select((s, i) => s.Record.Attempt ?? i + 1).ToArray();
            attempts._byTest[testId] = new Attempted(numbers, starts[0].Record.Attempt, starts.Select(s => s.Record.Timestamp).ToArray());
            if (starts.Count == 1)
                continue;

            if (starts.Skip(1).Any(s => s.Record.Attempt is null))
                attempts.InferredRetries++;

            var last = starts.Count - 1;
            foreach (var (record, index) in list)
            {
                if (AttemptOf(record, index, starts, numbers) != last)
                    attempts._earlier.Add(record);
            }
        }

        return attempts;
    }

    private static int AttemptOf(TestRunRecord record, int index, List<(TestRunRecord Record, int Index)> starts, int[] numbers)
    {
        // A start that opened an attempt is that attempt; one that did not is placed like any other record.
        var opened = starts.FindIndex(s => ReferenceEquals(s.Record, record));
        if (opened >= 0)
            return opened;

        if (record.Attempt is { } number && Array.IndexOf(numbers, number) is var named and >= 0)
            return named;

        var found = 0;
        if (record.Timestamp is { } at)
        {
            for (var i = 0; i < starts.Count; i++)
            {
                if (starts[i].Record.Timestamp is { } started && started <= at)
                    found = i;
            }

            return found;
        }

        for (var i = 0; i < starts.Count; i++)
        {
            if (starts[i].Index < index)
                found = i;
        }

        return found;
    }

    /// <summary>Whether <paramref name="record"/> belongs to an attempt that is not its test's last.</summary>
    public bool IsEarlier(TestRunRecord record) => _earlier.Contains(record);

    /// <summary>The records of every test's last attempt, and every record that belongs to no test's attempts.</summary>
    public List<TestRunRecord> Current(IEnumerable<TestRunRecord> records) => records.Where(r => !_earlier.Contains(r)).ToList();

    /// <summary>
    /// The number of <paramref name="testId"/>'s last attempt: its <c>start</c>'s <see cref="TestRunRecord.Attempt"/>, or
    /// its place among the test's starts when there were several. Null for a test that started once and said nothing.
    /// </summary>
    public int? Number(string testId) =>
        !_byTest.TryGetValue(testId, out var attempted) ? null
        : attempted.Numbers.Length > 1 ? attempted.Numbers[^1]
        : attempted.OnlyStartDeclared;

    /// <summary>The numbers of <paramref name="testId"/>'s attempts before its last, in order.</summary>
    public IReadOnlyList<int> EarlierNumbers(string testId) =>
        _byTest.TryGetValue(testId, out var attempted) ? attempted.Numbers[..^1] : [];

    /// <summary>
    /// When <paramref name="testId"/>'s last attempt started, for a test with several: a call of the test made before it
    /// belongs to an earlier attempt. Null for a test that started once, or whose last start has no timestamp.
    /// </summary>
    public DateTimeOffset? LastAttemptStart(string testId) =>
        _byTest.TryGetValue(testId, out var attempted) && attempted.Numbers.Length > 1 ? attempted.Starts[^1] : null;

    /// <summary>When <paramref name="testId"/>'s first attempt started, which is when the test began.</summary>
    public DateTimeOffset? FirstAttemptStart(string testId) =>
        _byTest.TryGetValue(testId, out var attempted) ? attempted.Starts[0] : null;

    /// <param name="Numbers">Each attempt's number, in start order: its start's own, else its place.</param>
    /// <param name="OnlyStartDeclared">
    /// The number the first start declared, which for a test that started once is all there is to say: a producer that
    /// reports only the attempt that counted still says which it was.
    /// </param>
    /// <param name="Starts">When each attempt started.</param>
    private sealed record Attempted(int[] Numbers, int? OnlyStartDeclared, DateTimeOffset?[] Starts);
}
