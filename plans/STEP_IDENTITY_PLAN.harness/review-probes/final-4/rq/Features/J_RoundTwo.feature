Feature: J round two

  @tieinner
  Scenario: J01 a hook ahead of Kronikol's throws at an inner step, a step method after it
    Given a step that runs an inner step whose hook throws, then a step method
    Then the order is confirmed

  @blockstep
  Scenario: J02 an undefined step, then a block hook's step method
    Given nobody defined this step in J02
    When the customer places the order
    Then the order is confirmed

  @afterstepinnerthrows
  Scenario: J03 an AfterStep hook throws on an inner step
    Given a step that runs the inner step an AfterStep hook throws on
    Then the order is confirmed

  @hookthrows @afterstepfails
  Scenario: J04 a plain hook throws and an AfterScenario step method fails
    Given a registered customer
    When the customer places the order

  @afterstepfails
  Scenario: J05 a step fails and an AfterScenario step method fails
    Given a registered customer
    When the payment fails
    Then the order is confirmed

  @lastrunsstep
  Scenario: J06 a hook that ties with Kronikol's last hook runs a Gherkin step after a failure
    When the payment fails
    Then the order is confirmed

  @needsgpu @caughtseed
  Scenario: J07 a caught failed step method, then the scenario ignores itself at run time
    Given a registered customer
    When the customer places the order

  @needsgpu
  Scenario: J08 the scenario ignores itself at run time
    Given a registered customer
    When the customer places the order

  @afterstepfails @needsgpu
  Scenario: J09 ignored at run time and an AfterScenario step method fails
    Given a registered customer
    When the customer places the order

  @strays
  Scenario: J10 leaves a Gherkin step running for the next scenario
    Given a step that leaves a Gherkin step to run later

  @waitsinafter
  Scenario: J11 fails at once and waits in its AfterScenario hook
    When the payment fails
    Then the order is confirmed

  Scenario: J12 a table with a pipe and a doc string with a brace, skipped
    When the payment fails
    Then the basket is listed
      | sku     | count |
      | PE\|AR  | 2     |
    Then the note says
      """
      {braces} and <angles>
      """
