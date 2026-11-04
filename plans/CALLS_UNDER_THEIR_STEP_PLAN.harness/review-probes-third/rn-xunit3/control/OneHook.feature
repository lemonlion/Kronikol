Feature: OneHook

Background:
  Given a background step
  When I act "oh"

@hookstep-before-bg
Scenario: OH1 Only this scenario's hook runs a step method
  Then I check

Scenario: OH2 This one has no such hook
  Then I check again
