Feature: HookAsync

@asyncstephooks
Scenario: HA1 Async BeforeStep and AfterStep hooks run step methods and attach
  Given a plain given
  When I act "ha1"

@afterstep-throws-after-step-method
Scenario: HA2 An AfterStep hook runs a step method then throws
  Given a plain given
  When I act "ha2"

@beforestep-step-method-fails
Scenario: HA3 A BeforeStep hook's step method fails
  Given a plain given
  When I act "ha3"

@afterscenario-attach
Scenario: HA4 An AfterScenario hook attaches and asserts
  Given a plain given

@beforescenario-attach
Scenario: HA5 A BeforeScenario hook attaches and asserts
  Given a plain given

@step-attach-in-hooks
Scenario: HA6 BeforeStep and AfterStep hooks attach and assert
  Given a plain given
