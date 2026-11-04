Feature: R8 hooks

  Scenario: H01 AfterStep throws on a plain step
    Given r8 one
    When r8 two~!
    Then r8 three

  @z-read @max-read @min-read @minp-read
  Scenario: H02 who sees the failed step in the list
    Given r8 one
    When r8 two~!

  @z-woven
  Scenario: H03 woven step in AfterScenario at 0, step left open
    Given r8 one~!

  @min-woven
  Scenario: H04 woven step in AfterScenario at int.MinValue, step left open
    Given r8 one~!

  @minp-woven
  Scenario: H05 woven step in AfterScenario at int.MinValue plus one, step left open
    Given r8 one~!

  @max-woven
  Scenario: H06 woven step in AfterScenario at int.MaxValue, step left open
    Given r8 one~!

  @min-throw
  Scenario: H07 AfterScenario at int.MinValue throws, step left open
    Given r8 one~!

  @z-throw
  Scenario: H08 AfterScenario at 0 throws, step left open
    Given r8 one~!

  @z-throw
  Scenario: H09 AfterScenario at 0 throws, nothing left open
    Given r8 one

  Scenario: H10 nested step left open, the outer lets it through, hook throws once
    Given r8 outer(inner~!)

  Scenario: H11 nested step left open, the outer catches, hook throws once
    Given r8 outer~k(inner~!)
    When r8 next

  Scenario: H12 nested and outer both throw in the hook
    Given r8 outer~!(inner~!)

  Scenario: H13 deep nesting all left open
    Given r8 a~!(b~!(c~!(d~!)))

  Scenario: H14 a sibling after a caught left open step
    Given r8 outer~k(inner~!,sib)
    When r8 next

  Scenario: H15 woven wrapper between outer and left open step
    Given r8 outer~o(inner~!)

  Scenario: H16 woven wrapper catches, outer goes on
    Given r8 outer~ok(inner~!)
    When r8 next

  Scenario: H17 woven step in the throwing hook
    Given r8 one~W!
    When r8 next

  Scenario: H18 woven step in the definition and in the throwing hook
    Given r8 one~wW!

  Scenario: H19 definition throws and the hook throws too
    Given r8 one~x!
    When r8 next

  Scenario: H20 hook throws pending
    Given r8 one~p
    When r8 next

  @z-step
  Scenario: H21 a step run by AfterScenario, nothing left open
    Given r8 one

  @z-step
  Scenario: H22 a step run by AfterScenario, a step left open
    Given r8 one~!

  @z-step-throw
  Scenario: H23 a step run by AfterScenario leaves itself open, hook catches
    Given r8 one

  @bs-woven
  Scenario: H24 woven in BeforeScenario, step left open
    Given r8 one~w
    When r8 two~!

  @bs-woven
  Scenario: H25 woven in BeforeScenario, nothing left open
    Given r8 one~w
    When r8 two~w

  @z-woven-fail
  Scenario: H26 failing woven in AfterScenario at 0, step left open
    Given r8 one~!

  Scenario: H27 BeforeStep ahead throws under a nested step
    Given r8 outer(inner~^)

  Scenario: H28 BeforeStep ahead throws at the top, then AfterStep sees no frame
    Given r8 one~^
    When r8 next

  @z-woven
  Scenario: H29 BeforeStep ahead throws, no frame, woven in AfterScenario takes its place
    Given r8 one
    When r8 two~^

  Scenario Outline: H30 outline rows, hook throws on one row
    Given r8 <a>
    When r8 two

    Examples:
      | a     |
      | fine  |
      | fails~! |
      | later |

  @z-tamper
  Scenario: H31 suite clears the list in AfterScenario, step left open
    Given r8 one~!

  Scenario: H32 scenario passes, control
    Given r8 one~w
    When r8 outer(inner)
