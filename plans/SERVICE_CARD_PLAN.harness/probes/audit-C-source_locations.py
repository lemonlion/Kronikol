# Counts only: per decoded data file, how many scenarios carry a scenario-level sourceFile, how many features carry
# one, and how many scenarios have at least one step (or sub-step) with a sourceFile. Prints no names or paths.
# Usage: python -I -X utf8 source_locations.py <decoded.json> [...]
import json
import sys


def step_files(steps):
    found = []
    for step in steps or []:
        if step.get("sourceFile"):
            found.append(step["sourceFile"])
        found += step_files(step.get("subSteps"))
    return found


def main(paths):
    for path in paths:
        with open(path, encoding="utf-8") as f:
            root = json.load(f)
        scenarios = own = features_with = with_steps = 0
        distinct = set()
        for feature in root.get("features") or []:
            if feature.get("sourceFile"):
                features_with += 1
            for scenario in feature.get("scenarios") or []:
                scenarios += 1
                if scenario.get("sourceFile"):
                    own += 1
                files = step_files(scenario.get("steps")) + step_files(scenario.get("backgroundSteps"))
                if files:
                    with_steps += 1
                    distinct.update(files)
        name = path.replace("\\", "/").split("/")[-1]
        print(f"{name}: scenarios {scenarios}, scenario sourceFile {own}, features with sourceFile {features_with}, "
              f"scenarios with a step sourceFile {with_steps}, distinct step files {len(distinct)}")


if __name__ == "__main__":
    main(sys.argv[1:])
