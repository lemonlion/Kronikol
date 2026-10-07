Feature: Bypass probe

  Scenario: A step attaches kronikol-bypass and returns early
    Given the overview has loaded
    Then the local-only check attaches a bypass and returns
    And the figure on screen is the figure the API returned

  Scenario: A step calls test.skip
    Given the overview has loaded
    Then the local-only check calls test.skip
    And the figure on screen is the figure the API returned

  Scenario: A step fails
    Given the overview has loaded
    Then the check fails
    And the figure on screen is the figure the API returned
