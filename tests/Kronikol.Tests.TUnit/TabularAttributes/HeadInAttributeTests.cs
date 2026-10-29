using System.Reflection;
using Kronikol.TabularAttributes;
using TUnit.Core;
using TUnit.Core.Enums;

namespace Kronikol.Tests.TUnit.TabularAttributes;

public class HeadInAttributeTests
{
    public class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    public class Greeting
    {
        public string Message { get; set; } = "";
    }

    // Dummy methods decorated with tabular attributes for reflection-based testing

#pragma warning disable TUnit0019 // These are metadata targets, not actual TUnit tests
    [HeadIn("Name", "Age")]
    [Inputs("Alice", 30)]
    [Inputs("Bob", 25)]
    public void InputsOnly(TabularInputs<Person> inputs) { }

    [HeadIn("Name", "Age"), HeadOut("Message")]
    [Inputs("Alice", 30), Outputs("Hello Alice")]
    [Inputs("Bob",   25), Outputs("Hello Bob")]
    public void InputsAndOutputs(
        TabularInputs<Person> inputs, TabularOutputs<Greeting> outputs) { }

    [HeadIn]
    [Inputs("Charlie", 35)]
    public void InferredColumns(TabularInputs<Person> inputs) { }

    [HeadIn("Name", "Age")]
    [Inputs("Alice", 30)]
    public void SingleRow(TabularInputs<Person> inputs) { }
#pragma warning restore TUnit0019

    [Fact]
    public async Task Inputs_are_resolved_correctly()
    {
        var attr = new HeadInAttribute("Name", "Age");
        var metadata = BuildMetadata(nameof(InputsOnly));

        var rows = await CollectRows(attr, metadata);

        Assert.Single(rows);
        var args = rows[0];
        var inputs = Assert.IsType<TabularInputs<Person>>(args![0]);
        Assert.Equal(2, inputs.Count);
        Assert.Equal("Alice", inputs[0].Name);
        Assert.Equal(30, inputs[0].Age);
        Assert.Equal("Bob", inputs[1].Name);
        Assert.Equal(25, inputs[1].Age);
    }

    [Fact]
    public async Task Inputs_and_outputs_resolved_together()
    {
        var attr = new HeadInAttribute("Name", "Age");
        var metadata = BuildMetadata(nameof(InputsAndOutputs));

        var rows = await CollectRows(attr, metadata);

        Assert.Single(rows);
        var args = rows[0];
        var inputs = Assert.IsType<TabularInputs<Person>>(args![0]);
        var outputs = Assert.IsType<TabularOutputs<Greeting>>(args[1]);
        Assert.Equal(2, inputs.Count);
        Assert.Equal(2, outputs.Count);
        Assert.Equal("Hello Alice", outputs[0].Message);
        Assert.Equal("Hello Bob", outputs[1].Message);
    }

    [Fact]
    public async Task Inferred_column_names()
    {
        var attr = new HeadInAttribute();
        var metadata = BuildMetadata(nameof(InferredColumns));

        var rows = await CollectRows(attr, metadata);

        Assert.Single(rows);
        var inputs = Assert.IsType<TabularInputs<Person>>(rows[0]![0]);
        Assert.Equal("Charlie", inputs[0].Name);
        Assert.Equal(35, inputs[0].Age);
    }

    [Fact]
    public async Task Single_row()
    {
        var attr = new HeadInAttribute("Name", "Age");
        var metadata = BuildMetadata(nameof(SingleRow));

        var rows = await CollectRows(attr, metadata);

        Assert.Single(rows);
        var inputs = Assert.IsType<TabularInputs<Person>>(rows[0]![0]);
        Assert.Single(inputs);
        Assert.Equal("Alice", inputs[0].Name);
    }

    [Fact]
    public async Task Foreach_iterates_all_rows()
    {
        var attr = new HeadInAttribute("Name", "Age");
        var metadata = BuildMetadata(nameof(InputsOnly));

        var rows = await CollectRows(attr, metadata);
        var inputs = Assert.IsType<TabularInputs<Person>>(rows[0]![0]);

        var names = new List<string>();
        foreach (var person in inputs)
            names.Add(person.Name);

        Assert.Equal(["Alice", "Bob"], names);
    }

    private DataGeneratorMetadata BuildMetadata(string methodName)
    {
        var method = GetType().GetMethod(methodName,
            BindingFlags.Public | BindingFlags.Instance)!;
        var parameters = method.GetParameters();

        var methodMetadata = new MethodMetadata
        {
            Name = method.Name,
            Type = GetType(),
            GenericTypeCount = 0,
            Parameters = parameters.Select(p => new ParameterMetadata(p.ParameterType)
            {
                Name = p.Name!,
                TypeInfo = new ConcreteType(p.ParameterType),
                // Required before TUnit.Core 1.19.0 and settable on every 1.x, so the floor search builds this on any
                // release (plans/CLIENT_FLOORS_PLAN.md R3a).
                ReflectionInfo = p,
                Position = p.Position,
            }).ToArray(),
            Class = new ClassMetadata
            {
                Type = GetType(),
                Name = GetType().Name,
                Namespace = GetType().Namespace!,
                TypeInfo = new ConcreteType(GetType()),
                Assembly = new AssemblyMetadata { Name = GetType().Assembly.GetName().Name! },
                Parameters = [],
                Properties = [],
                Parent = null!,
            },
            ReturnTypeInfo = new ConcreteType(typeof(void)),
            TypeInfo = new ConcreteType(GetType()),
        };

        var builderContext = new TestBuilderContext { TestMetadata = methodMetadata };

        return new DataGeneratorMetadata
        {
            TestBuilderContext = new TestBuilderContextAccessor(builderContext),
            MembersToGenerate = [],
            TestInformation = methodMetadata,
            Type = DataGeneratorType.TestParameters,
            TestSessionId = Guid.NewGuid().ToString(),
            TestClassInstance = this,
            ClassInstanceArguments = null,
        };
    }

    private static async Task<List<object?[]?>> CollectRows(
        HeadInAttribute attr, DataGeneratorMetadata metadata)
    {
        var results = new List<object?[]?>();
        await foreach (var factory in attr.GetDataRowsAsync(metadata))
        {
            results.Add(await factory());
        }
        return results;
    }
}
