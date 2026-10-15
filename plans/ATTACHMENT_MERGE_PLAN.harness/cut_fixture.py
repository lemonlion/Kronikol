"""Cuts the test fixture tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-attachments/ from a probe run.

usage: python cut_fixture.py <probe dir> <run dir, PROBE_OUT> <fixture dir>

The run is `PROBE_SMALL=1 PROBE_OUT=out-fx npx playwright test --grep-invert @big` in probe/: all four scenarios of
attachments.feature but the @big one, and background.feature, with a 48x27 page and a trace without screenshots,
snapshots or sources, so every artefact is small. Cut it at once: Playwright empties test-results/ on its next run.

What the cut changes, and nothing else:
- every file the tests file names is copied under the fixture: test-results/<long dir>/x becomes
  test-results/<scenario>[-retry1]/x, and <run>/inline/x becomes inline/x; the tests file names them relative to
  the fixture folder, for IngestRequest.AttachmentsBase;
- this machine's paths become C:\\fixture, in the messages (stack traces), the tests file and inside each trace.zip.
  A trace is zipped again with its entries rewritten, and the messages' base64 copy of it is replaced by the same new
  bytes, so the two copies of every artefact still have one SHA-256.
"""
import base64
import hashlib
import io
import json
import os
import shutil
import sys
import zipfile

probe, run, fixture = (os.path.abspath(a) for a in sys.argv[1:4])
local_app_data = os.path.join(os.environ.get('USERPROFILE', 'C:\\Users\\nobody'), 'AppData', 'Local')

# Longest first: the probe root, then the profile's AppData (the browser's executablePath).
roots = [(probe, 'C:\\fixture'), (local_app_data, 'C:\\fixture\\AppData\\Local')]


def forms(path):
    """The spellings a path takes in the files: backslashes, JSON-escaped backslashes, forward slashes."""
    yield path
    yield path.replace('\\', '\\\\')
    yield path.replace('\\', '/')


def scrub_text(text):
    for old, new in roots:
        for o, n in zip(forms(old), forms(new)):
            text = text.replace(o, n)
            # Windows paths are case-insensitive, and node writes the drive letter either way.
            text = text.replace(o[0].lower() + o[1:], n)
    return text


def scrub_zip(data):
    source = zipfile.ZipFile(io.BytesIO(data))
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as target:
        for info in source.infolist():
            body = source.read(info)
            try:
                body = scrub_text(body.decode('utf-8')).encode('utf-8')
            except UnicodeDecodeError:
                pass  # a binary resource (the screenshot, the video): kept as it is
            entry = zipfile.ZipInfo(info.filename, date_time=info.date_time)
            entry.compress_type = zipfile.ZIP_DEFLATED
            target.writestr(entry, body)
    return out.getvalue()


short_dirs = {
    "A failing UI scenario keeps Playwright's artefacts": 'failing',
    'A flaky scenario passes on its retry': 'flaky',
    'A failing scenario under a background': 'background',
}

records = [json.loads(line) for line in open(os.path.join(run, 'tests.jsonl'), encoding='utf-8') if line.strip()]
names = {r['testId']: r['testName'] for r in records if r.get('event') == 'start'}

if os.path.exists(fixture):
    shutil.rmtree(fixture)
os.makedirs(fixture)

replaced = {}  # sha256 of a trace as Playwright wrote it -> the bytes the fixture keeps
inline = os.path.join(run, 'inline')
results = os.path.join(probe, 'test-results')
for record in records:
    if record.get('event') != 'attachment' or not record.get('path'):
        continue
    path = os.path.abspath(record['path'])
    if os.path.commonpath([path, inline]) == inline:
        relative = 'inline/' + os.path.relpath(path, inline).replace('\\', '/')
    elif os.path.commonpath([path, results]) == results:
        parts = os.path.relpath(path, results).split(os.sep)
        folder = short_dirs[names[record['testId']]] + ('-retry1' if parts[0].endswith('-retry1') else '')
        relative = 'test-results/' + folder + '/' + '/'.join(parts[1:])
    else:
        raise SystemExit(f'not under the run: {path}')
    data = open(path, 'rb').read()
    if path.endswith('.zip'):
        scrubbed = scrub_zip(data)
        replaced[hashlib.sha256(data).hexdigest()] = scrubbed
        data = scrubbed
    target = os.path.join(fixture, *relative.split('/'))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    open(target, 'wb').write(data)
    record['path'] = relative

with open(os.path.join(fixture, 'tests.ndjson'), 'w', encoding='utf-8', newline='\n') as tests:
    for record in records:
        tests.write(scrub_text(json.dumps(record, ensure_ascii=False, separators=(',', ':'))) + '\n')

kept = 0
with open(os.path.join(fixture, 'messages.ndjson'), 'w', encoding='utf-8', newline='\n') as messages:
    for line in open(os.path.join(run, 'messages.ndjson'), encoding='utf-8'):
        if not line.strip():
            continue
        envelope = json.loads(line)
        attachment = envelope.get('attachment')
        if attachment and attachment.get('contentEncoding') == 'BASE64':
            digest = hashlib.sha256(base64.b64decode(attachment['body'])).hexdigest()
            if digest in replaced:
                attachment['body'] = base64.b64encode(replaced[digest]).decode('ascii')
                line = json.dumps(envelope, ensure_ascii=False, separators=(',', ':'))
                kept += 1
        messages.write(scrub_text(line.rstrip('\n')) + '\n')

left = []
for folder, _, files in os.walk(fixture):
    for name in files:
        with open(os.path.join(folder, name), 'rb') as f:
            blob = f.read()
        if b'Users\\' in blob or b'Users/' in blob or b'Users\\\\' in blob:
            left.append(name)
        if name.endswith('.zip'):
            with zipfile.ZipFile(io.BytesIO(blob)) as z:
                for info in z.infolist():
                    if b'Users' in z.read(info):
                        left.append(name + ':' + info.filename)
print(f'traces rewritten: {len(replaced)}; messages copies replaced: {kept}; files with a local path left: {left}')
