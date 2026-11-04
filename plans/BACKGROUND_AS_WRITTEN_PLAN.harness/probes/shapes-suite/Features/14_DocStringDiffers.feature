Feature: 14 No background, a doc string differs

Scenario: 14 one
    Given a note says
        """
        apple
        """
    Then the outcome is "one"

Scenario: 14 two
    Given a note says
        """
        banana
        """
    Then the outcome is "two"
