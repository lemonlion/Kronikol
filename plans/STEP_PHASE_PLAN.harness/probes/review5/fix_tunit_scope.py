import sys
p = sys.argv[1]
t = open(p, encoding='utf-8').read()
old = 'protected override void OnTearDown()\n    {\n        Skeleton.Write("rev5-lightbdd-tunit");\n    }'
new = 'protected override ValueTask OnTearDown()\n    {\n        Skeleton.Write("rev5-lightbdd-tunit");\n        return ValueTask.CompletedTask;\n    }'
assert old in t
open(p, 'w', encoding='utf-8', newline='\n').write(t.replace(old, new))
print('fixed')
