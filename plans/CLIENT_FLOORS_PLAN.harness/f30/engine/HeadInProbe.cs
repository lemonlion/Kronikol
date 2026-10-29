using Kronikol.TabularAttributes;

namespace F30;

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

public class HeadInProbe
{
    [Test]
    [HeadIn("Name", "Age")]
    [Inputs("Alice", 30)]
    [Inputs("Bob", 25)]
    public async Task Rows_reach_the_test(TabularInputs<Person> inputs)
    {
        await Assert.That(inputs.Count).IsEqualTo(2);
        await Assert.That(inputs[1].Name).IsEqualTo("Bob");
        await Assert.That(inputs[1].Age).IsEqualTo(25);
    }
}
