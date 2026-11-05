Feature: G steps at depth

  Scenario: G01 A runs B which fails then C
    Given A runs B which fails then C
    Then the order is confirmed

  Scenario: G02 three levels
    Given depth one runs depth two then a sibling
    Then the order is confirmed

  Scenario: G03 a step method between the Gherkin steps
    Given a step whose step method runs two steps
    Then the order is confirmed

  Scenario: G04 an undefined inner step
    Given a step that runs an undefined step
    Then the order is confirmed

  Scenario: G05 a pending inner step
    Given a step that runs a pending step
    Then the order is confirmed

  Scenario: G06 an inner step skips itself
    Given a step that runs a step that skips itself
    Then the order is confirmed

  Scenario: G07 one inner step three times
    Given a step that retries an inner step three times
    Then the order is confirmed

  Scenario: G08 inner steps with arguments after a failure
    Given a step that runs steps with arguments, the first failing
    Then the order is confirmed

  Scenario: G09 And and But inner steps skipped
    Given a step that runs a step with the wrong keyword text
    Then the order is confirmed

  Scenario: G10 an outer step throws after an inner step passed
    Given a step that fails after running a step
    Then the order is confirmed

  Scenario: G11 an outer step throws after an inner failure
    Given a step that fails inner and throws itself
    Then the order is confirmed

  @aftercall
  Scenario: G12 the inner failure with a plain call in AfterScenario
    Given A runs B which fails then C
    Then the order is confirmed

  @afterstep
  Scenario: G13 the inner failure with a step method in AfterScenario
    Given A runs B which fails then C
    Then the order is confirmed
