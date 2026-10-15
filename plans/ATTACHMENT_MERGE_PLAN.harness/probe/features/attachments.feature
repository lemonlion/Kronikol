Feature: Attachment probe

  Scenario: A failing UI scenario keeps Playwright's artefacts
    Given the page is open
    When a step attaches a named image
    Then the check fails

  Scenario: A passing scenario attaches the same name twice
    Given the page is open
    When a step attaches two notes with one name
    Then the figure is right

  Scenario: A flaky scenario passes on its retry
    Given the page is open
    Then the check fails on the first attempt only

  Scenario: A step attaches kronikol-bypass
    Given the page is open
    Then the local-only check attaches a bypass and returns

  @big
  Scenario: A two-minute failure with a 58 MB trace
    Given the page is open
    Then the big check fails with a big trace
