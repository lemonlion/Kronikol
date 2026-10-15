using KronikolComponentTests.Infrastructure;
using Kronikol.xUnit2;

namespace KronikolComponentTests;

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class DiagrammedTestCollection : ICollectionFixture<TestRun> { }
