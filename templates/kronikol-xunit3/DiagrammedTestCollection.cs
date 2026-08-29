using KronikolComponentTests.Infrastructure;
using Kronikol.xUnit3;

namespace KronikolComponentTests;

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class DiagrammedTestCollection : ICollectionFixture<TestRun> { }
