@endpoint:/diagnostic
Feature: Parameterized Diagnostic Feature
    /diagnostic - Creating apple cinnamon muffins with baking profiles and toppings

    @happy-path
    Scenario: A valid apple cinnamon muffin request should return a fresh batch
        Every diagnostic batch is baked to order from the complete ingredient list.
        Stale muffins must never reach a caller.

        Given a valid apple cinnamon muffin recipe with all ingredients
        When the muffins are prepared
        Then the muffin response should contain a valid batch with all ingredients
        And the cow service should have received a milk request for the muffins

    Scenario Outline: Different muffin recipes should produce the expected batch
        Covers speciality flour, baking profile and topping permutations.

        Given a muffin recipe "<RecipeName>" with the following ingredients:
            | Flour   | Apples         | Cinnamon       |
            | <Flour> | <AppleVariety> | <CinnamonType> |
        And the following baking:
            | Temperature   | DurationMinutes | PanType   |
            | <Temperature> | <Duration>      | <PanType> |
        And the following muffin toppings:
            | Name       | Amount    |
            | <Topping1> | <Amount1> |
            | <Topping2> | <Amount2> |
        When the muffins are prepared
        Then the muffin batch should have <ExpectedIngredientCount> ingredients
        And the muffin response should include <ExpectedToppingCount> toppings
        And the muffin response should have baking info <ExpectedHasBakingInfo>

        Examples: a classic bake
            The everyday plain-flour recipe.
            | RecipeName       | Flour        | AppleVariety | CinnamonType | Temperature | Duration | PanType   | Topping1          | Amount1 | Topping2           | Amount2 | ExpectedIngredientCount | ExpectedToppingCount | ExpectedHasBakingInfo |
            | Classic          | Plain Flour  | Granny Smith | Ceylon       | 180         | 25       | Standard  | Streusel          | Light   | Icing Glaze        | Drizzle | 5                       | 2                    | True                  |

        Examples: speciality flours
            | RecipeName       | Flour        | AppleVariety | CinnamonType | Temperature | Duration | PanType   | Topping1          | Amount1 | Topping2           | Amount2 | ExpectedIngredientCount | ExpectedToppingCount | ExpectedHasBakingInfo |
            | Rustic Wholesome | Whole Wheat  | Honeycrisp   | Cassia       | 175         | 30       | Cast Iron | Brown Sugar Crumb | Heavy   | Maple Drizzle      | Light   | 5                       | 2                    | True                  |
            | Spiced Deluxe    | Almond Flour | Pink Lady    | Saigon       | 190         | 20       | Silicone  | Cinnamon Sugar    | Heavy   | Cream Cheese Swirl | Thick   | 5                       | 2                    | True                  |
