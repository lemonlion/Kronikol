@endpoint:/diagnostic
Feature: Month's specials via gRPC
    /diagnostic - A feature whose title has an apostrophe, an acronym and a lower-case word, shown as written (5.9.1)

    Scenario: This month's special muffins are baked
        Given a valid apple cinnamon muffin recipe with all ingredients
        When the muffins are prepared
        Then the muffin response should contain a valid batch with all ingredients
