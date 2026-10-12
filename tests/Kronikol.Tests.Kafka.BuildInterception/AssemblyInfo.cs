// Every fact here patches Confluent's builders for the whole process through Harmony, and a patch is never removed
// (removing one cannot restore the code other type pairs share), so the facts run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
