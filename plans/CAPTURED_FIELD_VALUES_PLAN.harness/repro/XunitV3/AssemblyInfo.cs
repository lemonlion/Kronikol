using Kronikol.Tracking;

[assembly: TrackAssertions]
// xUnit v3 has no [assembly: TestTracking]: each test class derives from DiagrammedComponentTest, whose collection's
// fixture (TestRun.cs) writes the report, as the wiki's Integration-xUnit3 page sets it up.
