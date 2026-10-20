#:project C:/Code/Kronikol-tags/src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// Writes a mergeable TestRunReport.json as a .NET adapter's run would (GenerateMergeableData = true), for the recipe
// that tags the data file and renders it again with kronikol merge. Usage: dotnet run --file run.cs -- <reports dir>
using Kronikol;
using Kronikol.Reports;

Feature[] features =
[
    new Feature
    {
        DisplayName = "Orders", SourceFile = "Features/Orders.feature", Tags = ["orders"],
        Scenarios =
        [
            new Scenario { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, SourceFile = "Features/Orders.feature", SourceLine = 12, Tags = ["smoke"] },
            new Scenario { Id = "s2", DisplayName = "Cancel an order", Result = ExecutionResult.Passed, SourceFile = "Features/Orders.feature", SourceLine = 20 }
        ]
    },
    new Feature
    {
        DisplayName = "Catalogue",
        Scenarios = [new Scenario { Id = "s3", DisplayName = "Browse", Result = ExecutionResult.Passed }]
    }
];

ReportGenerator.CreateStandardReportsWithDiagrams(features,
    new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 20, 10, 0, 30, DateTimeKind.Utc),
    new ReportConfigurationOptions
    {
        ReportsFolderPath = args[0],
        GenerateMergeableData = true,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false
    });
Console.WriteLine("written");
