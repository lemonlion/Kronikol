import sys

for path in sys.argv[1:]:
    with open(path, encoding="utf-8") as f:
        s = f.read()
    bad = '[Given(@"the shopper uploads C:\\baskets\\monday\\.csv")]'
    good = '[Given(@"the shopper uploads C:\\\\baskets\\\\monday\\.csv")]'
    assert s.count(bad) == 1, path
    with open(path, "w", encoding="utf-8") as f:
        f.write(s.replace(bad, good))
    print(path, "fixed")
