import sys

for root in sys.argv[1:]:
    path = f"{root}/Steps/ProbeSteps.cs"
    with open(path, encoding="utf-8") as f:
        s = f.read()
    for old, new in (
        ("public class ProbeSteps(HttpClient client, ProbeDriver probe)", "public class ProbeSteps(HttpClient client, ProbeDriver probe, ScenarioContext scenario)"),
        ("GivenASharedStepWhoseSubStepsDependOnTheScenario(ScenarioContext scenario)", "GivenASharedStepWhoseSubStepsDependOnTheScenario()"),
    ):
        assert s.count(old) == 1, old
        s = s.replace(old, new)
    with open(path, "w", encoding="utf-8") as f:
        f.write(s)
    print("fixed", root[-12:])
