# Mutation runs for ASSERTION_VALUE_SUBSTITUTION_PLAN R1 (plan section 4.3), in a snapshot worktree.
# Usage: python mutate.py <worktree> [M1 M2 ...]. Each mutation edits one or more files (exact-string replace, asserted
# to match once), runs the facts it should turn red, records the summary and the failed names, and restores the files
# from copies taken before the edit (never git checkout).
import os
import re
import shutil
import subprocess
import sys

WT = sys.argv[1]
ONLY = set(sys.argv[2:])
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
WEAVE = 'tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj'
AC = 'src/Kronikol/Tracking/AccessChain.cs'
FMT = 'src/Kronikol/Tracking/AssertionExpressionFormatter.cs'
CVR = 'src/Kronikol/Tracking/ClosureValueResolver.cs'
WV = 'src/Kronikol.AssertionTracking/AssertionWeaver.cs'
TRACKING = ('FullyQualifiedName~TrackValueResolutionTests|FullyQualifiedName~ClosureValueResolverTests|'
            'FullyQualifiedName~AssertionExpressionFormatterTests|FullyQualifiedName~ValuePathParityTests|'
            'FullyQualifiedName~AccessChainTests')
WEAVE_FILTER = 'FullyQualifiedName~AssertionValueWeaveTests'

MUTATIONS = {
    'M1': ('a member that a call follows read as a member again', [(AC,
        '''        while (readCount < segments.Count && segments[readCount].Kind == SegmentKind.Member &&
               !(readCount + 1 < segments.Count && segments[readCount + 1].Kind == SegmentKind.Call))''',
        '''        while (readCount < segments.Count && segments[readCount].Kind == SegmentKind.Member)''')],
        [(CORE, TRACKING), (WEAVE, WEAVE_FILTER)]),
    'M2': ('unreadable answered as null again', [(AC,
        '''            if (!TryReadMember(current, segment.Name!, out current, out var reason))
                return ChainRead.Unreadable(reason);''',
        '''            if (!TryReadMember(current, segment.Name!, out current, out var reason))
                return ChainRead.WholeValue(null);''')], [(CORE, TRACKING)]),
    'M3': ('a null before a plain dot read as null', [(AC,
        '''                    : ChainRead.Unreadable($"null before '.{segment.Name}'");''',
        '''                    : ChainRead.WholeValue(null);''')], [(CORE, TRACKING)]),
    'M4': ('a null before ?. read as unreadable', [(AC,
        '''                return segment.NullConditional
                    ? ChainRead.WholeValue(null)''',
        '''                return segment.NullConditional
                    ? ChainRead.Unreadable("x")''')], [(CORE, TRACKING)]),
    'M5': ('the substitution back to a search with the identifier-character boundary', [(FMT,
        '''        var sb = new StringBuilder(args.Length);
        var pos = 0;
        foreach (var chain in AccessChain.Find(args))''',
        '''        foreach (var name in values.Keys.OrderByDescending(k => k.Length))
        {
            var idx = 0;
            while ((idx = args.IndexOf(name, idx, StringComparison.Ordinal)) >= 0)
            {
                var before = idx > 0 ? args[idx - 1] : ' ';
                var after = idx + name.Length < args.Length ? args[idx + name.Length] : ' ';
                if (IsIdentifierChar(before) || IsIdentifierChar(after) || LiteralMask(args)[idx]) { idx += name.Length; continue; }
                var replacement = "'" + values[name] + "'";
                args = args[..idx] + replacement + args[(idx + name.Length)..];
                idx += replacement.Length;
            }
        }
        var sb = new StringBuilder(args.Length);
        var pos = 0;
        foreach (var chain in new List<AccessChain>())''')], [(CORE, TRACKING)]),
    'M6': ('the three-segment limit restored', [(AC,
        '''            if (text[opener] == '.' && !(opener + 1 < text.Length && text[opener + 1] == '.'))
            {''',
        '''            if (text[opener] == '.' && !(opener + 1 < text.Length && text[opener + 1] == '.') && segments.Count < 3)
            {''')], [(CORE, TRACKING)]),
    'M7': ('the resolver keying the raw text instead of the canonical one', [(AC,
        '''            CanonicalKey(text[start..end]), CanonicalKey(text[start..readEnd]));''',
        '''            text[start..end], text[start..readEnd]);''')], [(CORE, TRACKING)]),
    'M8a': ('Q1 scalar test removed (always the read part)', [(AC,
        '''        if (read.Value is null || !AccessChain.IsScalarType(read.Value.GetType()))
            return false;''',
        '''        if (read.Value is null)
            return false;''')], [(CORE, TRACKING)]),
    'M8b': ('Q1 scalar test inverted (never the read part)', [(AC,
        '''        if (read.Value is null || !AccessChain.IsScalarType(read.Value.GetType()))
            return false;''',
        '''        if (read.Value is null || read.Value is not null)
            return false;''')], [(CORE, TRACKING)]),
    'M9': ('the quoted last segment restored', [(FMT,
        '''        // Wrap lambda expressions in square brackets for readability
        if (args.Contains("=>"))
            return $"[ {args} ]";
''',
        '''        // Wrap lambda expressions in square brackets for readability
        if (args.Contains("=>"))
            return $"[ {args} ]";
        if (!args.Contains('\\'') && !args.Contains('(') && !args.Contains('"') && !args.Contains(' ') && !args.Contains(',') && args.Contains('.'))
        {
            var segs = args.Split('.');
            if (segs.Length > 2 || args.StartsWith("_"))
                args = "'" + segs[^1].Trim('_') + "'";
        }
''')], [(CORE, TRACKING)]),
    'M11': ("ShouldlyArguments' start from the previous producer again", [(WV,
        '''            var begin = end;
            while (slot >= 0 && begin - 1 >= start && analysis.StackBefore(statement[begin - 1]) is { } before &&
                   before.Length > slot && ReferenceEquals(before[slot].Producer, below))
                begin--;''',
        '''            var begin = k > 0 ? Math.Max(IndexOf(k - 1) + 1, start) : start;''')], [(WEAVE, WEAVE_FILTER)]),
    'M12a': ("suppression by the type's own attribute alone", [(WV,
        '''            for (var enclosing = type; enclosing != null; enclosing = enclosing.DeclaringType)
            {''',
        '''            for (var enclosing = type; enclosing != null; enclosing = null)
            {'''), (WV,
        '''                if (TryResolve(reference) is { } target && target.Module == module)
                    Cover(target);''',
        '''                if (TryResolve(reference) is { } target && target.Module == module && false)
                    Cover(target);''')], [(WEAVE, WEAVE_FILTER)]),
    'M12b': ('every lambda suppressed', [(WV,
        '''        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (types.Contains(type) || HasSuppressAttribute(method))
                    Cover(method);''',
        '''        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (types.Contains(type) || HasSuppressAttribute(method) || method.Name.StartsWith("<"))
                    Cover(method);''')], [(WEAVE, WEAVE_FILTER)]),
    'M13': ('FormatSubject splitting literals again', [(FMT,
        '''        var segments = SplitOutsideLiterals(subject, '.');''',
        '''        var segments = subject.Split('.').ToList();'''), (FMT,
        '''            sb.Append(literal[i] ? part : SplitPascalCase(part).ToLowerInvariant());''',
        '''            sb.Append(SplitPascalCase(part).ToLowerInvariant());''')], [(CORE, TRACKING)]),
    'M14': ('the to-string rule over the whole sentence, FluentAssertions only', [(FMT,
        '''        if (segments.Count > 1)
            segments.RemoveAll(segment => segment is "ToString()" or "ToString");''',
        ''''''), (FMT,
        '''        return string.IsNullOrEmpty(formattedArgs)
            ? $"{formattedSubject} should {formattedMethod}"
            : $"{formattedSubject} should {formattedMethod} {formattedArgs}";''',
        '''        return (string.IsNullOrEmpty(formattedArgs)
            ? $"{formattedSubject} should {formattedMethod}"
            : $"{formattedSubject} should {formattedMethod} {formattedArgs}").Replace(" to string() ", " ");''')],
        [(CORE, TRACKING)]),
    'M15': ('a colon anywhere in the arguments read as a named argument again', [(WV,
        '''        if (close < 0 || argument > commas.Count || HasNamedArgument(text, open, commas))''',
        '''        if (close < 0 || argument > commas.Count || text.Substring(open, close - open).Contains(": "))''')],
        [(WEAVE, WEAVE_FILTER)]),
}


def run(project, flt):
    framework = ['-f', 'net10.0'] if 'AssertionTracking' in project else []
    p = subprocess.run(['dotnet', 'test', project, *framework, '--filter', flt], cwd=WT, capture_output=True,
                       text=True, encoding='utf-8', errors='replace')
    out = p.stdout + p.stderr
    summary = [l.strip() for l in out.splitlines() if re.search(r'(Passed|Failed)!\s+-', l)]
    errors = sorted({l.strip()[:160] for l in out.splitlines() if 'error CS' in l})
    failed = sorted({re.sub(r'\s*\[[^\]]*\]$', '', l.strip()[len('Failed '):])[:150]
                     for l in out.splitlines() if l.strip().startswith('Failed Kronikol')})
    return summary, errors, failed


for name, (what, edits, runs) in MUTATIONS.items():
    if ONLY and name not in ONLY:
        continue
    backups = {}
    for path, _, _ in edits:
        full = os.path.join(WT, path)
        if full not in backups:
            backups[full] = open(full, encoding='utf-8', newline='').read()
    try:
        for path, a, b in edits:
            full = os.path.join(WT, path)
            s = open(full, encoding='utf-8', newline='').read()
            assert s.count(a) == 1, (name, a[:80])
            open(full, 'w', encoding='utf-8', newline='').write(s.replace(a, b))
        print(f'== {name}: {what}', flush=True)
        for project, flt in runs:
            summary, errors, failed = run(project, flt)
            print(f'   {os.path.basename(project)}: {summary or "NO SUMMARY"}', flush=True)
            for e in errors[:5]:
                print(f'     COMPILE {e}', flush=True)
            for f in failed[:40]:
                print(f'     red: {f}', flush=True)
    finally:
        for full, text in backups.items():
            open(full, 'w', encoding='utf-8', newline='').write(text)
print('done', flush=True)
