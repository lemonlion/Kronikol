global using Xunit;

// The classes here listen to every Kronikol.Kafka span in the process (ActivitySource listeners are process-wide)
// and give their messages the same W3C example trace id, so two classes run at once saw each other's spans: CI
// failed KafkaConsumeScopeTests.Under_the_background_a_scope_logs_nothing_and_starts_no_span on a process span of
// KafkaMessageProcessingTests. One class at a time, as Kronikol.Tests.Kafka.BuildInterception and Kronikol.Tests.Dapper.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
