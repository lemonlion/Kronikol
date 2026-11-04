@q-hook-stops
Feature: Q13 A BeforeStep hook ahead of ours stops a nested step, and the composite runs a woven step after it

Scenario: Q13 one
  Given q composite runs a stopped step and then a woven step

Scenario: Q13 two, a nested step before it
  Given q composite runs a step, a stopped step and then a woven step
