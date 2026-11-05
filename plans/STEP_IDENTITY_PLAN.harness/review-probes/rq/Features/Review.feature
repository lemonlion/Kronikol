Feature: Review probes

Scenario: R00 a forwarding step method awaited normally is the control
    Given a forwarding step method is awaited
    When the order takes a while and is then read back
    Then the order is confirmed

Scenario: R01 a forwarding step method left running
    Given a forwarding step method is left running
    When the order takes a while and is then read back
    Then the order is confirmed

Scenario: R02 a sync step method that leaves one running
    Given a sync step method leaves one running
    When the order takes a while and is then read back
    Then the order is confirmed

Scenario: R17 the control for R01, the plan's T17 shape on the same later steps
    Given a step method is left running
    When the order takes a while and is then read back
    Then the order is confirmed

@r03
Scenario: R03 a hook leaves a step method running and the first step is undefined
    Given nobody wrote this step either
    When the customer places the order
    Then the order is confirmed

@r04
Scenario: R04 a hook at Kronikol's order throws
    Given a registered customer
    When the customer places the order

@r05
Scenario: R05 a hook's step method runs a Gherkin step and then makes a call
    Given a registered customer
    When the customer places the order

@r06
Scenario: R06 two step methods in parallel in a hook, the first ending first
    Given a registered customer
    When the customer places the order

@r13
Scenario: R13 a step method fails in AfterScenario
    Given a registered customer
    When the customer places the order

Scenario: R18 a failing forwarding step method left running, in a scenario that passes
    Given a failing forwarding step method is left running
    When the order takes a while and is then read back
    Then the order is confirmed

Scenario: R99 keeps the run alive past the left-running steps
    Given the run waits a while
