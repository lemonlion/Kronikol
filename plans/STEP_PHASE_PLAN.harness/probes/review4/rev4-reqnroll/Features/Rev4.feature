Feature: Rev4

Scenario: Q01 two When bindings that each mark the boundary
    Given a call named q01-seed
    When a marking act named q01-act1
    And a marking act named q01-act2
    Then a call named q01-then

Scenario: Q02 two When bindings, second under its own When
    Given a call named q02-seed
    When a marking act named q02-act1
    Then a call named q02-check
    When a marking act named q02-act2
    Then a call named q02-then

Scenario: Q03 a consumer hook makes a call before and after each step
    Given a hooked call named q03-given
    When a hooked call named q03-when
    Then a hooked call named q03-then

Scenario: Q04 step argument transformation makes a call
    Given a transformed call <q04-arg>
    When a call named q04-when

Scenario: Q05 an undefined step
    Given a call named q05-seed
    When nothing binds this step at all
    Then a call named q05-then

Scenario: Q06 a pending step
    Given a call named q06-seed
    When a pending step
    Then a call named q06-then

Scenario: Q07 a failing step then the after-scenario hook
    Given a call named q07-seed
    When a failing step named q07-act
    Then a call named q07-then

Scenario: Q08 a worker of a host started by a hook, first
    Given a call named q08-seed
    And the worker handles q08-worker-in-setup
    When a call named q08-act
    And the worker handles q08-worker-in-action

Scenario: Q09 a worker of a host started by a hook, second
    Given a call named q09-seed
    And the worker handles q09-worker-in-setup
    When a call named q09-act
    And the worker handles q09-worker-in-action

Scenario: Q10 a worker of a host started by a hook, third
    Given a call named q10-seed
    And the worker handles q10-worker-in-setup
    When a call named q10-act
    And the worker handles q10-worker-in-action
