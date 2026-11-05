Feature: Parallel A

@shared-handler
Scenario: PA1
    Given a call named pa1-g0
    And a call named pa1-g1
    When a call named pa1-w0
    And a call named pa1-w1
    Then a call named pa1-t

@shared-handler
Scenario: PA2
    Given a call named pa2-g0
    And a call named pa2-g1
    When a call named pa2-w0
    And a call named pa2-w1
    Then a call named pa2-t
