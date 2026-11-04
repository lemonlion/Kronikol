using TestStack.BDDfy;
using Xunit;

namespace ProbeBddfyFluent;

// BDDfy's reflective names: AndGiven and AndWhen keep the And in the title, and a long name wraps on its bar.
public class Story_reflective_and
{
    public void GivenAUserWithAVeryLongNameThatGoesOnPastThePointWhereABarWrapsItsLabelAtOneHundredAndTenCharactersOrSoAndABit() => Http.Client.GetAsync("given").GetAwaiter().GetResult();
    public void AndGivenASecondUserWithAVeryLongNameThatGoesOnPastThePointWhereABarWrapsItsLabelAtOneHundredAndTenCharactersOrSoAndABit() => Http.Client.GetAsync("and-given").GetAwaiter().GetResult();
    public void ButGivenNoThirdUserWithAVeryLongNameThatGoesOnPastThePointWhereABarWrapsItsLabelAtOneHundredAndTenCharactersOrSoAndABit() => Http.Client.GetAsync("but-given").GetAwaiter().GetResult();
    public void WhenTheyOrder() => Http.Client.GetAsync("when").GetAwaiter().GetResult();
    public void AndWhenTheyPayWithAVeryLongNameThatGoesOnPastThePointWhereABarWrapsItsLabelAtOneHundredAndTenCharactersOrSoAndABit() => Http.Client.GetAsync("and-when").GetAwaiter().GetResult();
    public void ThenItShips() => Http.Client.GetAsync("then").GetAwaiter().GetResult();
    public void AndThenItArrivesWithAVeryLongNameThatGoesOnPastThePointWhereABarWrapsItsLabelAtOneHundredAndTenCharactersOrSoAndABit() => Http.Client.GetAsync("and-then").GetAwaiter().GetResult();

    [Fact]
    public void Reflective_story() => this.BDDfy();
}
