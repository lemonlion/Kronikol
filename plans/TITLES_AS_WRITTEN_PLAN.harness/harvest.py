"""Harvest test names from C# sources, for the title probe (probe.cs).

Reads every .cs file under each root (bin/ and obj/ skipped) and writes one TSV line per test method:
corpus, attribute, class, method, explicit display name (empty when none). Regex-only: nothing is built or run.

usage: python -I harvest.py OUT.tsv NAME=ROOT [NAME=ROOT ...]
"""
import os
import re
import sys

TEST_ATTRIBUTES = (
    "Fact", "Theory", "SkippableFact", "SkippableTheory", "Test", "TestCase", "TestCaseSource",
    "TestMethod", "DataTestMethod", "Scenario",
)
ATTRIBUTE = re.compile(r"\[\s*(?:Xunit\.)?(" + "|".join(TEST_ATTRIBUTES) + r")\b(\([^\]]*\))?\s*\]")
METHOD = re.compile(
    r"\b(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?(?:override\s+)?"
    r"(?:Task|ValueTask|void|Task<[^>]+>|ValueTask<[^>]+>)\s+(\w+)\s*(?:<[^>]*>)?\s*\(")
CLASS = re.compile(r"\b(?:class|record)\s+(\w+)")
# xUnit [Fact(DisplayName = "..")], MSTest [TestMethod("..")] or DisplayName = "..", NUnit TestName = "..",
# TUnit [DisplayName("..")]
DISPLAY_NAME = re.compile(r'(?:DisplayName\s*=\s*|TestName\s*=\s*)"((?:[^"\\]|\\.)*)"')
MSTEST_POSITIONAL = re.compile(r'^\(\s*"((?:[^"\\]|\\.)*)"')
TUNIT_DISPLAY = re.compile(r'\[\s*DisplayName\s*\(\s*"((?:[^"\\]|\\.)*)"')


def harvest(corpus, root, out):
    count = 0
    for directory, subdirectories, files in os.walk(root):
        subdirectories[:] = [d for d in subdirectories if d not in ("bin", "obj", ".git", "node_modules")]
        for name in files:
            if not name.endswith(".cs") or name.endswith(".feature.cs"):
                continue
            path = os.path.join(directory, name)
            try:
                text = open(path, encoding="utf-8-sig", errors="replace").read()
            except OSError:
                continue
            classes = [(m.start(), m.group(1)) for m in CLASS.finditer(text)]
            for attribute in ATTRIBUTE.finditer(text):
                method = METHOD.search(text, attribute.end())
                # the method must follow its attribute block, not a later member
                if method is None or method.start() - attribute.end() > 600:
                    continue
                between = text[attribute.start():method.start()]
                display = ""
                arguments = attribute.group(2) or ""
                found = DISPLAY_NAME.search(arguments)
                if found is None and attribute.group(1) in ("TestMethod", "DataTestMethod"):
                    found = MSTEST_POSITIONAL.search(arguments)
                if found:
                    display = found.group(1)
                tunit = TUNIT_DISPLAY.search(between)
                if tunit:
                    display = tunit.group(1)
                owner = ""
                for position, class_name in classes:
                    if position < attribute.start():
                        owner = class_name
                display = display.replace("\t", " ").replace("\n", " ")
                out.write(f"{corpus}\t{attribute.group(1)}\t{owner}\t{method.group(1)}\t{display}\n")
                count += 1
    return count


def main():
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as out:
        for pair in sys.argv[2:]:
            corpus, root = pair.split("=", 1)
            print(f"{corpus}: {harvest(corpus, root, out)} tests", file=sys.stderr)


if __name__ == "__main__":
    main()
