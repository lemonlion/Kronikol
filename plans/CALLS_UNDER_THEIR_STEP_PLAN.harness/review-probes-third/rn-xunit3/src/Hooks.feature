Feature: Hooks

@hookstep-before
Scenario: H1 BeforeScenario hook runs a step method
  Given a plain given
  When I act "h1"
  Then I check

@hookstep-before-async
Scenario: H2 Async BeforeScenario hook runs a step method
  Given a plain given
  When I act "h2"
  Then I check

@hookstep-after
Scenario: H3 AfterScenario hook runs a step method
  Given a plain given
  When I act "h3"

@hookstep-beforestep
Scenario: H4 BeforeStep hook runs a step method
  Given a plain given
  When I act "h4"

@hookstep-afterstep
Scenario: H5 AfterStep hook runs a step method
  Given a plain given
  When I act "h5"

@hookcall-all
Scenario: H6 Every hook makes a call
  Given a plain given
  When I act "h6"

@hookstep-before-fails-caught
Scenario: H7 BeforeScenario step method fails and the hook catches it
  Given a plain given
  When I act "h7"
  Then I check

@hook-leaves-running
Scenario: H8 BeforeScenario hook leaves a step method running
  Given a plain given
  When I act "h8"
  Then I check

@hookthrows-beforescenario
Scenario: H9 BeforeScenario hook throws
  Given a plain given
  When I act "h9"

@hookthrows-beforestep
Scenario: H10 BeforeStep hook throws
  Given a plain given
  When I act "h10"
  Then I check

@hookthrows-afterstep
Scenario: H11 AfterStep hook throws
  Given a plain given
  When I act "h11"
  Then I check

@hookthrows-afterscenario
Scenario: H12 AfterScenario hook throws
  Given a plain given
  When I act "h12"

@hookstep-before-fails
Scenario: H13 BeforeScenario step method fails and fails the hook
  Given a plain given
  When I act "h13"
  Then I check
