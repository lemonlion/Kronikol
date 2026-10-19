using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The one rule a report's readers use to tell two tags apart, and the lists made with it (plans/TAGS_PLAN.md §4.2,
/// §4.3): ASCII letters fold and nothing else does, a list keeps the first spelling it meets, and a scenario's effective
/// tags are its feature's and then its own.
/// </summary>
public class ScenarioTagsTests
{
    [Theory]
    [InlineData("smoke", "Smoke")]
    [InlineData("SMOKE", "smoke")]
    [InlineData("Happy Path", "happy path")]
    [InlineData("a,b", "A,B")]
    public void Names_equal_once_their_ASCII_letters_fold_are_one_tag(string x, string y)
    {
        Assert.True(ScenarioTags.Comparer.Equals(x, y));
        Assert.Equal(ScenarioTags.Comparer.GetHashCode(x), ScenarioTags.Comparer.GetHashCode(y));
    }

    [Theory]
    // .NET's OrdinalIgnoreCase folds these two; JavaScript does not fold the second, Java folds both differently
    // (plans/TAGS_PLAN.harness/case-fold/results.txt)
    [InlineData("Über", "über")]
    [InlineData("Été", "été")]
    // the Kelvin sign is k to JavaScript and Java, not to .NET; the dotless i is i to Java alone
    [InlineData("K", "k")]
    [InlineData("ı", "i")]
    [InlineData("smoke", "smoke ")]
    public void No_other_letter_folds(string x, string y)
    {
        Assert.False(ScenarioTags.Comparer.Equals(x, y));
    }

    [Fact]
    public void Merge_keeps_the_first_spelling_and_the_order_the_names_are_met_in()
    {
        Assert.Equal(["Smoke", "api", "Slow"], ScenarioTags.Merge(["Smoke", "api"], null, ["smoke", "Slow", "API", " ", ""]));
    }

    [Fact]
    public void Merge_of_nothing_is_empty()
    {
        Assert.Empty(ScenarioTags.Merge());
        Assert.Empty(ScenarioTags.Merge(null, []));
    }

    [Fact]
    public void Shared_is_what_every_list_holds_in_the_first_list_s_order_and_spelling()
    {
        Assert.Equal(["Checkout", "api"], ScenarioTags.Shared([["Checkout", "slow", "api"], ["API", "checkout"], ["checkout", "api", "fast"]]));
    }

    [Fact]
    public void Shared_is_empty_when_any_list_is_empty_or_there_is_none()
    {
        Assert.Empty(ScenarioTags.Shared([["a"], []]));
        Assert.Empty(ScenarioTags.Shared([["a"], null]));
        Assert.Empty(ScenarioTags.Shared([]));
        Assert.Empty(ScenarioTags.Shared([["a"], ["b"]]));
    }

    [Fact]
    public void Shared_of_one_list_is_that_list_once_each()
    {
        Assert.Equal(["a", "B"], ScenarioTags.Shared([["a", "B", "b", "A"]]));
    }

    [Fact]
    public void The_row_puts_Happy_Path_first_then_folds_ASCII_letters_then_breaks_ties_by_code_unit()
    {
        // "smoke" before "Smoke": only the code-unit tie-break puts them the other way round
        string[] names = ["zebra", "Äpfel", "smoke", "apple", "Smoke", "Happy Path", "Zed", "_x", "10", "9"];
        var sorted = names.ToList();
        sorted.Sort(ScenarioTags.CompareForRow);
        // '1' (0x31) < '9' (0x39) < '_' (0x5F) < 'a'..'z' < 'Ä' (0xC4); "Smoke" before "smoke" ('S' < 's')
        Assert.Equal(["Happy Path", "10", "9", "_x", "apple", "Smoke", "smoke", "zebra", "Zed", "Äpfel"], sorted);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    [InlineData("cs-CZ")]
    public void The_row_order_is_the_same_under_every_culture(string culture)
    {
        string[] names = ["Äpfel", "ice", "Ich", "chip", "hat", "Zebra", "apple"];
        CultureRun.Under(culture, () =>
        {
            var sorted = names.ToList();
            sorted.Sort(ScenarioTags.CompareForRow);
            Assert.Equal(["apple", "chip", "hat", "ice", "Ich", "Zebra", "Äpfel"], sorted);
        });
    }

    [Fact]
    public void A_scenario_s_effective_tags_are_its_feature_s_then_its_own_each_once()
    {
        var feature = new Feature { DisplayName = "F", Labels = ["Checkout", "api"] };
        var scenario = new Scenario { Id = "s", DisplayName = "S", Labels = ["smoke", "checkout"], Categories = ["API", "Slow"] };
        Assert.Equal(["Checkout", "api", "smoke", "Slow"], ScenarioTags.Effective(feature, scenario));
    }

    [Fact]
    public void A_scenario_with_no_tags_in_a_feature_with_none_has_none()
    {
        Assert.Empty(ScenarioTags.Effective(new Feature { DisplayName = "F" }, new Scenario { Id = "s", DisplayName = "S" }));
    }

    [Fact]
    public void The_retry_labels_an_attempt_accounts_for_are_not_tags_and_an_author_s_own_are()
    {
        var feature = new Feature { DisplayName = "F" };
        var retried = new Scenario { Id = "s", DisplayName = "S", Attempt = 3, Labels = ["retry 1", "flaky", "retry 2", "retry 3"] };
        Assert.Equal(["flaky", "retry 3"], ScenarioTags.Effective(feature, retried));

        var never = new Scenario { Id = "t", DisplayName = "T", Labels = ["retry 2"] };
        Assert.Equal(["retry 2"], ScenarioTags.Effective(feature, never));
    }

    [Theory]
    [InlineData("retry 1", 2, true)]
    [InlineData("retry 2", 3, true)]
    [InlineData("retry 2", 2, false)]
    [InlineData("retry 0", 2, false)]
    [InlineData("retry 1", 1, false)]
    [InlineData("retry 1", null, false)]
    [InlineData("Retry 1", 2, false)]
    [InlineData("retry  1", 2, false)]
    [InlineData("retry +1", 2, false)]
    [InlineData("retry 1x", 2, false)]
    public void A_generated_retry_label_is_retry_k_for_an_attempt_before_the_one_shown(string label, int? attempt, bool generated)
    {
        Assert.Equal(generated, ScenarioTags.IsGeneratedRetryLabel(label, attempt));
    }
}
