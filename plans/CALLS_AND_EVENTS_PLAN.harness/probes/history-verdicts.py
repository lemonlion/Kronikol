# The verdict the labs page gives each scenario of history-across-merge.cs. args: <labs page>
import html, re, sys
text = open(sys.argv[1], encoding='utf8', errors='replace').read()
text = re.sub(r'<(script|style)\b.*?</\1>', ' ', text, flags=re.S)
lines = [l.strip() for l in html.unescape(re.sub(r'<[^>]+>', '\n', text)).split('\n') if l.strip()]
names = ['Control, an HTTP GET', 'Control, Redis Get (Hit)', 'Control, SQL DELETE in capitals', 'Spanner Delete as an Event',
         'Spanner Delete as Default', 'BigQuery Delete, the handler', 'CosmosDB Delete, today', 'CosmosDB Patch, today',
         'EF Core Delete, today', 'Blob Storage Delete, today']
seen = set()
for n, line in enumerate(lines):
    for name in names:
        if line.startswith(name) and name not in seen:
            seen.add(name)
            print(f'{name:34} {[x for x in lines[n + 1:n + 4] if not x.startswith("100")][0][:24]}')
for name in names:
    if name not in seen:
        print(f'{name:34} NOT LISTED')
