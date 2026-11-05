Feature: E hooks

  @hookthrows
  Scenario: E01 a plain BeforeScenario hook throws
    Given a registered customer
    When the customer places the order

  @hookpending
  Scenario: E02 a BeforeScenario hook calls Pending
    Given a registered customer
    When the customer places the order

  @hookskip
  Scenario: E03 a BeforeScenario hook skips
    Given a registered customer
    When the customer places the order

  @caughtseed
  Scenario: E04 a hook catches a failed step method and every step passes
    Given a registered customer
    When the customer places the order

  @caughtseed
  Scenario: E05 a hook catches a failed step method and a step skips itself
    Given a registered customer
    When a step skips itself
    Then the order is confirmed

  @caughtseed
  Scenario: E05b a hook catches a failed step method and a step is pending
    Given a registered customer
    When a step is pending
    Then the order is confirmed

  @caughtseed
  Scenario: E05c a hook catches a failed step method and a step is undefined
    Given a registered customer
    When nobody defined this step in E05c
    Then the order is confirmed

  @afterstepfails
  Scenario: E06 a step skips itself and an AfterScenario step method fails
    Given a registered customer
    When a step skips itself
    Then the order is confirmed

  @afterstepfails
  Scenario: E07 a step is pending and an AfterScenario step method fails
    Given a registered customer
    When a step is pending
    Then the order is confirmed

  @afterstepfails
  Scenario: E08 every step passes and an AfterScenario step method fails
    Given a registered customer
    When the customer places the order

  @aftercaughtfail
  Scenario: E09 a step skips itself and an AfterScenario hook catches a failed step method
    Given a registered customer
    When a step skips itself
    Then the order is confirmed

  @aftercall
  Scenario: E10 a failed scenario with a plain call in AfterScenario
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @afterstep
  Scenario: E11 a failed scenario with a step method in AfterScenario
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @afterrunsstep
  Scenario: E12 a failed scenario whose AfterScenario hook runs a Gherkin step
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @hookrunstwo
  Scenario: E13 a hook runs two Gherkin steps, the first failing
    Given a registered customer
    When the customer places the order

  @hookstepmethodrunstwo
  Scenario: E14 a hook's step method runs two Gherkin steps, the first failing
    Given a registered customer
    When the customer places the order

  @hookrunsundefined
  Scenario: E15 a hook runs an undefined Gherkin step
    Given a registered customer
    When the customer places the order

  @blockcaught
  Scenario: E16 a block hook catches a failed step method and every step passes
    Given a registered customer
    When the customer places the order
    Then the order is confirmed

  @afterthrows
  Scenario: E17 a failed scenario whose AfterScenario hook throws
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @hookseedfails @aftercall
  Scenario: E18 a hook step fails and a plain call in AfterScenario
    Given a registered customer
    When the customer places the order

  @hookthrows @afterstep
  Scenario: E19 a plain hook throws and an AfterScenario hook has a step method
    Given a registered customer
    When the customer places the order
