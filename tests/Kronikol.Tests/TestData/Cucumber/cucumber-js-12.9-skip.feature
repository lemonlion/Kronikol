Feature: Skip probe

  Scenario: A step returns skipped
    Given the overview has loaded
    Then the local-only check returns skipped
    And the figure on screen is the figure the API returned
    And a step nobody defined

  Scenario: A step fails
    Given the overview has loaded
    Then the check fails
    And the figure on screen is the figure the API returned
