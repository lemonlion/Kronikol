"""R6: the segments a page's map carries that no diagram of the page links, counted with their decoded bytes. Prints
numbers only.

python unlinked.py <TestRunReport.html>..."""
import base64
import gzip
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dedup import segment_map  # noqa: E402

LINK = re.compile(r'\[\[#(iflow-[^\s\]]+)')
TAG = '<script id="puml-data" type="application/json">'


def linked_ids(html):
    """Every segment id a diagram the page embeds links: the page's puml-data map, each source gzipped."""
    at = html.find(TAG)
    while at >= 0:
        start = at + len(TAG)
        if html[start] == '{':
            sources = json.loads(html[start:html.index('</script>', start)])
            return {i for value in sources.values() for i in LINK.findall(gzip.decompress(base64.b64decode(value)).decode('utf-8'))}
        at = html.find(TAG, start)
    return set()


for path in sys.argv[1:]:
    html = open(path, encoding='utf-8').read()
    linked = linked_ids(html)
    data = segment_map(path)
    unlinked = [k for k in data if k not in linked]

    def size(keys):
        return sum(len(json.dumps({k: data[k]}, separators=(',', ':'), ensure_ascii=False).encode('utf-8')) for k in keys)

    print(f'{path.replace(chr(92), "/").split("/")[-1]}: {len(data)} segments, {len(linked)} linked ids, '
          f'{len(unlinked)} segments unlinked, {size(unlinked):,} of {size(data):,} decoded bytes')
