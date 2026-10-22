@endpoint:/diagnostic
Feature: Party Muffins
    /diagnostic - An outline whose title names its row, as Gherkin fills it

    Scenario Outline: A batch of apple cinnamon muffins is baked for <occasion>
        Given a valid apple cinnamon muffin recipe with all ingredients
        When the muffins are prepared
        Then the muffin response should contain a valid batch with all ingredients

        Examples:
            | occasion  |
            | breakfast |
            | a picnic  |
