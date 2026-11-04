Feature: R8 more

  Scenario: S01 definition throws, then a next step
    Given r8 one~x
    When r8 next

  Scenario: S02 inner definition throws, outer lets it through
    Given r8 outer(inner~x)
    When r8 next

  Scenario: S03 inner definition throws, outer catches
    Given r8 outer~k(inner~x)
    When r8 next

  Scenario: S04 inner definition throws under a woven wrapper
    Given r8 outer~o(inner~x)

  Scenario: S05 inner definition throws and the hook throws on it
    Given r8 outer(inner~x!)

  Scenario: S06 outer left open, inner fine
    Given r8 outer~!(inner)

  Scenario: S07 outer left open, two inner, the second fails
    Given r8 outer~!(in1,in2~x)

  Scenario: S08 outer catches and goes on, hook throws on the second inner, third inner is skipped
    Given r8 outer~k(in1,in2~!,in3)
    When r8 next

  Scenario: S09 definition throws after a left open inner it caught
    Given r8 outer~kx(inner~!)

  @min-read @z-read
  Scenario: S10 nested left open, reads at each order
    Given r8 outer~!(inner~!)

  @z-attach
  Scenario: A01 attachment in AfterScenario at 0, step left open
    Given r8 one~!

  @min-attach
  Scenario: A02 attachment in AfterScenario at int.MinValue, step left open
    Given r8 one~!

  Scenario: A03 attachment in the throwing hook
    Given r8 one~a!

  Scenario: I01 the hook ignores the scenario at run time
    Given r8 one~i
    When r8 next

  @bs-step
  Scenario: P01 a step BeforeScenario runs is left open
    Given r8 one
