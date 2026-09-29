"""R2 mutation checks: break one guard at a time, run the facts that should catch it, restore.

python mutate_r2.py <repo>   (prints one line per mutation: CAUGHT or SURVIVED)"""
import os
import subprocess
import sys

repo = sys.argv[1]
FILTER = ('FullyQualifiedName~PayloadCompressionTests|FullyQualifiedName~CompressedReportQueryTests|'
          'FullyQualifiedName~CompressedMergeTests|FullyQualifiedName~SchemaValidationTests|'
          'FullyQualifiedName~FallbackScriptTests|FullyQualifiedName~IngestCommandTests')

MUTATIONS = [
    ('threshold off by one', 'src/Kronikol/Reports/ReportPayloads.cs',
     'text.Length < Threshold', 'text.Length <= Threshold'),
    ('no "smaller" test', 'src/Kronikol/Reports/ReportPayloads.cs',
     'if (z.Length + WrapperOverhead >= JsonEncodedText.Encode(text, Encoder).EncodedUtf8Bytes.Length + 2)\n            return text;',
     'if (false)\n            return text;'),
    ('version not raised', 'src/Kronikol/Reports/ReportPayloads.cs',
     'public int FormatVersion => Compressed > 0 ? CompressedFormatVersion : ReportGenerator.ReportFormatVersion;',
     'public int FormatVersion => ReportGenerator.ReportFormatVersion;'),
    ('scanner drops the address', 'src/Kronikol/Query/ReportScanner.cs',
     '_interaction.BodyHash = wrapper.Hash;', '_interaction.BodyHash = null;'),
    ('scanner drops the length', 'src/Kronikol/Query/ReportScanner.cs',
     '_interaction.BodyLength = wrapper.Length ?? 0;', '_interaction.BodyLength = 0;'),
    ('reader does not inflate', 'src/Kronikol/Query/PayloadReader.cs',
     'return Kronikol.Reports.ReportPayloads.Inflate(reader.GetString()!);', 'return reader.GetString()!;'),
    ('gate refuses 2', 'src/Kronikol/Query/ReportGate.cs',
     '&& format != Kronikol.Reports.ReportPayloads.CompressedFormatVersion)', ')'),
    ('merge reader drops wrappers', 'src/Kronikol/Reports/Merge/MergeableReportReader.cs',
     'notes.CompressedPayloads = true;\n        try', 'notes.CompressedPayloads = true;\n        return null;\n        try'),
    ('merge writes plain', 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
     'PayloadsCompressed = reports.Any(r => r.PayloadsCompressed),', 'PayloadsCompressed = false,'),
    ('query.py does not inflate', 'templates/skills/kronikol-test-debugging/scripts/query.py',
     '        return path, inflate(json.load(handle))', '        return path, json.load(handle)'),
    ('ingest ignores the flag', 'src/Kronikol.Tool/IngestCommand.cs',
     'options.CompressTestRunReportPayloads = compress;', '_ = compress;'),
    ('schema accepts any wrapper', 'src/Kronikol/Reports/ReportGenerator.cs',
     '["required"] = new[] { "$h", "$n", "$z" }', '["required"] = new[] { "$h" }'),
]

for name, rel, old, new in MUTATIONS:
    path = os.path.join(repo, rel)
    original = open(path, encoding='utf-8', newline='').read()
    if original.count(old) != 1:
        print(f'{name}: MUTATION NOT APPLIED ({original.count(old)} matches)')
        continue
    open(path, 'w', encoding='utf-8', newline='').write(original.replace(old, new))
    try:
        run = subprocess.run(['dotnet', 'test', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '-f', 'net10.0',
                              '--filter', FILTER], cwd=repo, capture_output=True, text=True, encoding='utf-8',
                             errors='replace', timeout=1500)
        out = run.stdout + run.stderr
        failed = [l.strip() for l in out.splitlines() if l.strip().startswith('Failed Kronikol')]
        verdict = 'CAUGHT' if run.returncode != 0 and failed else ('BUILD FAILED' if 'error CS' in out else 'SURVIVED')
        print(f'{name}: {verdict} ({len(failed)} failed)' + (f'  e.g. {failed[0][:120]}' if failed else ''), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(original)
