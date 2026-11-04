"""Runs mutants of cut 10 one after another: apply, pack as 5.28.3-<id>, run the suites as a consumer would, compare with the unmutated rounds, restore, clean.

Usage:
  mutrun.py check                       every edit matches the patched source; nothing is built
  mutrun.py control-pack                cut 10 unmutated as 5.28.3-n00
  mutrun.py control-fast <first> <n> <family or family/suite> ...   unmutated rounds into results/n00/<round>, each suite built once
  mutrun.py clean-control               the m00 packages out of the feed and the NuGet cache
  mutrun.py run <id> [<id> ...]         those mutants, with escalation for a survivor
  mutrun.py rerun <id> <family/suite> <n>   a mutant's one suite n times, for a racing shape (results/<id>/repeat/<n>/...)

A file STOP in the mut folder ends the run between mutants.
"""
import glob
import hashlib
import os
import shutil
import subprocess
import sys
import time

import compare
from dumps import MUT
CUT = os.environ.get("MUT_CUT", "10")
if CUT == "11":
    from mutations11 import MUTANTS, ORDER, PATCHED_FILES
else:
    from mutations10 import MUTANTS, ORDER, PATCHED_FILES

WT = os.path.join(MUT, "wt")
PATCHED = os.path.join(MUT, "patched11" if CUT == "11" else "patched")
FEED = os.path.join(MUT, "feed")
RESULTS = os.path.join(MUT, "results")
LOGS = os.path.join(MUT, "logs")
SUMMARY = os.path.join(RESULTS, "summary.txt")
PACKAGES = os.path.join(os.environ["USERPROFILE"], ".nuget", "packages")

PACK_BASE = ["Kronikol", "Kronikol.StepTracking", "Kronikol.ReqNRoll.Core", "Kronikol.ReqNRoll.xUnit2", "Kronikol.LightBDD.Core", "Kronikol.LightBDD.xUnit2",
             "Kronikol.xUnit2", "Kronikol.xUnit3", "Kronikol.BDDfy.xUnit3", "Kronikol.AssertionTracking"]
PACK_MATRIX = ["Kronikol.NUnit4", "Kronikol.MSTest", "Kronikol.TUnit", "Kronikol.ReqNRoll.xUnit3", "Kronikol.ReqNRoll.TUnit"]

FAMILIES = {
    "main": ["reqnroll-xunit2", "xunit3-plain", "lightbdd-xunit2", "bddfy-xunit3"],
    "review": ["rev3-plain", "rev3-reqnroll", "rev3-lightbdd", "rev3-more", "rev3-bddfy", "rev3-collapse", "rev3-parallel", "rev3-readers", "rev3-lightbdd-legacy", "rev3-reqnroll2"],
    "rev4": ["rev4-a", "rev4-b", "rev4-c", "rev4-d", "rev4-bddfy", "rev4-reqnroll", "rev4-reentrant", "rev4-weave", "rev4-weave@Release", "rev4-fsharp", "rev4-diagram"],
    "rev5": ["rev5-a", "rev5-bddfy", "rev5-reqnroll", "rev5-reqnroll-x3", "rev5-reqnroll-tunit", "rev5-reqnroll-par", "rev5-reqnroll-retry", "rev5-lightbdd-x2", "rev5-lightbdd-legacy", "adapter-xunit2", "adapter-nunit4", "adapter-mstest", "adapter-tunit", "rev5-weave", "rev5-weave@Release", "rev5-diagram"],
    "rev6": ["rev6-a", "rev6-bddfy", "rev6-fetch", "rev6-lightbdd-legacy", "rev6-lightbdd-x2", "rev6-mix", "rev6-nunit", "rev6-reqnroll", "rev6-reqnroll-x3"],
    "old": ["lightbdd"],
    "matrix": ["nunit4", "mstest", "tunit", "xunit2", "xunit3", "reqnroll-xunit2", "reqnroll-xunit3", "reqnroll-nunit", "reqnroll-tunit", "reqnroll-server", "bddfy-xunit3", "xunit3-plain", "lightbdd-xunit2"],
}
FOLDER = {
    "main": os.path.join(MUT, "probes", "main"),
    "review": os.path.join(MUT, "probes", "review"),
    "rev4": os.path.join(MUT, "probes", "rev4"),
    "rev5": os.path.join(MUT, "probes", "rev5"),
    "rev6": os.path.join(MUT, "probes", "rev6"),
    "matrix": os.path.join(MUT, "probes", "matrix"),
    "old": os.path.join(MUT, "probes", "main", "review"),
}
# Cut 11 has baselines over main, rev5, rev6 and rev3-plain only.
ESCALATION = ["rev5", "rev6"] if CUT == "11" else ["review", "rev4", "rev5", "old"]
SUITE_ENV = {
    ("review", "rev3-readers"): {"PROBE_SEPARATE_SETUP": "true"},
    ("rev5", "rev5-weave"): {"PROBE_CONFIG": "-Debug"},
    ("rev5", "rev5-weave@Release"): {"PROBE_CONFIG": "-Release"},
    ("rev5", "adapter-tunit"): {"PHASE_PROBE_NAME": "adapter-tunit"},
}
NEEDS_MATRIX_PACK = {("rev5", s) for s in ("rev5-reqnroll-x3", "rev5-reqnroll-tunit", "adapter-nunit4", "adapter-mstest", "adapter-tunit")}
NEEDS_MATRIX_PACK |= {("rev6", "rev6-nunit"), ("rev6", "rev6-reqnroll-x3")}
SUITE_TIMEOUT = 420
PACK_TIMEOUT = 900
MIN_FREE_BYTES = 3 * 1024 ** 3
BASE_ROOT = "p00" if CUT == "11" else "n00"
CONTROL_VERSION = "5.28.3-" + BASE_ROOT


def base_env():
    env = dict(os.environ)
    env.update(MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", NUGET_XMLDOC_MODE="skip")
    return env


def say(line):
    os.makedirs(RESULTS, exist_ok=True)
    with open(SUMMARY, "a", encoding="utf-8") as handle:
        handle.write(line + "\n")
    print(line, flush=True)


def kill_tree(pid):
    subprocess.run(["taskkill", "/T", "/F", "/PID", str(pid)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def run(cmd, cwd, env, log_path, timeout):
    """(exit code or 'TIMEOUT', seconds)"""
    started = time.time()
    with open(log_path, "ab") as log:
        log.write(("\n$ " + " ".join(cmd) + "\n").encode())
        log.flush()
        proc = subprocess.Popen(cmd, cwd=cwd, env=env, stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
        try:
            code = proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            kill_tree(proc.pid)
            proc.wait()
            code = "TIMEOUT"
    return code, time.time() - started


def fresh_write(path, data):
    with open(path, "wb") as handle:
        handle.write(data)
    now = time.time()
    os.utime(path, (now, now))


def restore_all():
    for rel in PATCHED_FILES:
        with open(os.path.join(PATCHED, rel), "rb") as handle:
            fresh_write(os.path.join(WT, rel), handle.read())


def snapshot_patched():
    for rel in PATCHED_FILES:
        target = os.path.join(PATCHED, rel)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        if not os.path.exists(target):
            shutil.copyfile(os.path.join(WT, rel), target)


def mutated_text(mutant_id):
    """rel -> the mutant's text, checking every edit's count against the patched source, edit after edit."""
    texts = {}
    for rel, old, new, count in MUTANTS[mutant_id]["edits"]:
        if rel not in texts:
            with open(os.path.join(PATCHED, rel), encoding="utf-8", newline="") as handle:
                texts[rel] = handle.read()
        found = texts[rel].count(old)
        if found != count:
            raise SystemExit(f"{mutant_id}: {rel}: expected {count} of [{old[:70]}], found {found}")
        texts[rel] = texts[rel].replace(old, new)
    return texts


def apply_mutant(mutant_id, out):
    import difflib
    restore_all()
    texts = mutated_text(mutant_id)
    diff = []
    for rel, text in texts.items():
        with open(os.path.join(PATCHED, rel), encoding="utf-8", newline="") as handle:
            before = handle.read()
        diff += list(difflib.unified_diff(before.splitlines(), text.splitlines(), "patched/" + rel, "mutant/" + rel, lineterm="", n=1))
        with open(os.path.join(WT, rel), "w", encoding="utf-8", newline="") as handle:
            handle.write(text)
        now = time.time()
        os.utime(os.path.join(WT, rel), (now, now))
    with open(os.path.join(out, "mutation.diff"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(diff) + "\n")
    return texts


def pack(version, projects, log_path):
    """One build of a project list of all of them, so each is evaluated and built once. The nupkgs are checked one by one after."""
    os.makedirs(FEED, exist_ok=True)
    traversal = os.path.join(MUT, "tools", "pack-base.proj" if projects is PACK_BASE else "pack-matrix.proj")
    code, secs = run(["dotnet", "build", traversal, "-c", "Release", f"-p:Version={version}", f"-p:PackageVersion={version}", "-p:GeneratePackageOnBuild=true",
                      f"-p:PackageOutputPath={FEED}", "--nologo", "-v", "q"], os.path.join(MUT, "tools"), base_env(), log_path, PACK_TIMEOUT)
    if code != 0:
        return f"pack failed: {code}", secs
    for project in projects:
        if not glob.glob(os.path.join(FEED, f"{project}.{version}.nupkg")):
            return f"no package for {project}", secs
    return None, secs


def pdb_has(rel, text):
    """The mutated source's hash in the project's PDB: the build read this very text."""
    project = rel.split("/")[1]
    found = glob.glob(os.path.join(WT, "src", project, "bin", "Release", "*", project + ".pdb"))
    if not found:
        return None
    digest = hashlib.sha256(text.encode("utf-8-sig" if text.startswith("\ufeff") else "utf-8")).digest()
    with open(found[0], "rb") as handle:
        return digest in handle.read()


def clean_version(version):
    if not version.startswith(("5.28.3-m", "5.28.3-n", "5.28.3-p")):
        raise SystemExit("refusing to clean " + version)
    failures = []
    for folder in glob.glob(os.path.join(PACKAGES, "kronikol*", version)):
        shutil.rmtree(folder, ignore_errors=True)
        if os.path.exists(folder):
            failures.append(folder)
    for nupkg in glob.glob(os.path.join(FEED, f"*.{version}.nupkg")):
        os.remove(nupkg)
    return failures


def drop_cached(version):
    for folder in glob.glob(os.path.join(PACKAGES, "kronikol*", version)):
        shutil.rmtree(folder, ignore_errors=True)


def suite_parts(family, suite):
    """(folder, configuration arguments)"""
    folder, _, config = suite.partition("@")
    if folder == "rev4-fsharp":
        config = "Release"
    return os.path.join(FOLDER[family], folder), (["-c", config] if config else [])


def remove_build(cwd):
    # C: is short of room, and a probe's bin holds its reports: nothing in them is read here.
    for name in ("bin", "obj"):
        shutil.rmtree(os.path.join(cwd, name), ignore_errors=True)


def tunit_exe(cwd):
    proj = glob.glob(os.path.join(cwd, "*.csproj"))[0]
    return os.path.join(cwd, "bin", "Debug", "net10.0", os.path.basename(proj)[:-7] + ".exe")


def run_command(cwd, config, version, no_build):
    extra = (["--no-build"] if no_build else []) + config + [f"-p:KronikolVersion={version}", "--nologo"]
    if os.path.basename(cwd).endswith("-diagram"):
        return ["dotnet", "run"] + extra
    return ["dotnet", "test"] + extra + ["-v", "q"]


def run_suite(family, suite, version, out_root):
    cwd, config = suite_parts(family, suite)
    out = os.path.join(out_root, f"{family}-{suite}")
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    remove_build(cwd)
    env = base_env()
    env["PHASE_PROBE_OUT"] = out
    env.update(SUITE_ENV.get((family, suite), {}))
    log = os.path.join(out, "run.log")
    if "tunit" in suite:
        code, secs = run(["dotnet", "build", f"-p:KronikolVersion={version}", "--nologo", "-v", "q"], cwd, env, log, SUITE_TIMEOUT)
        if code == 0:
            code, more = run([tunit_exe(cwd), "--maximum-parallel-tests", "1", "--no-progress", "--disable-logo"], cwd, env, log, SUITE_TIMEOUT)
            secs += more
    else:
        code, secs = run(run_command(cwd, config, version, no_build=False), cwd, env, log, SUITE_TIMEOUT)
    remove_build(cwd)
    return write_outcome(out, code, secs)


def write_outcome(out, code, secs):
    with open(os.path.join(out, "run.log"), encoding="utf-8", errors="replace") as handle:
        lines = handle.read().splitlines()
    summary = [l.strip()[:170] for l in lines if l.startswith(("Passed!", "Failed!")) or l.startswith(("Test run summary", "  total:", "  failed:", "  succeeded:", "  skipped:"))
               or " error " in l or "Test Run Aborted" in l or "aborted" in l.lower() or "crashed" in l.lower()]
    with open(os.path.join(out, "outcome.txt"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(f"exit={code}\n" + "\n".join(summary[:12]) + "\n")
    return code, secs, summary[:3]


def disk_ok():
    free = shutil.disk_usage("C:\\").free
    if free < MIN_FREE_BYTES:
        say(f"STOPPING: C: has {free / 1024 ** 3:.1f} GB free")
        return False
    return True


def run_suites(pairs, version, out, have_matrix_pack, pack_log):
    for family, suite in pairs:
        if not disk_ok():
            return False
        if (family == "matrix" or (family, suite) in NEEDS_MATRIX_PACK) and not have_matrix_pack[0]:
            error, _ = pack(version, PACK_MATRIX, pack_log)
            if error:
                say(f"  {error}")
                return False
            have_matrix_pack[0] = True
            drop_cached(version)
        code, secs, summary = run_suite(family, suite, version, out)
        line = (summary[0] if summary else "(no summary)")[:96]
        print(f"    {family}/{suite}: exit={code} {secs:.0f}s {line}", flush=True)
    return True


def remove_all_builds():
    """bin and obj of the worktree's projects and of every probe, after every mutation: C: is short of room."""
    roots = [os.path.join(WT, "src"), os.path.join(MUT, "probes")]
    for root in roots:
        for folder, subfolders, _ in os.walk(root):
            for name in ("bin", "obj"):
                if name in subfolders:
                    shutil.rmtree(os.path.join(folder, name), ignore_errors=True)
                    subfolders.remove(name)
            # Nothing a project builds lies deeper than its own folder's bin and obj, but a probe can sit two levels down.


def run_mutant(mutant_id):
    info = MUTANTS[mutant_id]
    out = os.path.join(RESULTS, mutant_id)
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    version = "5.28.3-" + mutant_id
    log = os.path.join(LOGS, f"{mutant_id}.pack.log")
    if os.path.exists(log):
        os.remove(log)
    started = time.time()
    drop_cached(version)
    for nupkg in glob.glob(os.path.join(FEED, f"*.{version}.nupkg")):
        os.remove(nupkg)
    texts = apply_mutant(mutant_id, out)
    try:
        error, secs = pack(version, PACK_BASE, log)
        if error:
            say(f"{mutant_id} BUILD-FAILED {error} (see logs/{mutant_id}.pack.log)")
            return
        checks = {rel: pdb_has(rel, text) for rel, text in texts.items()}
        if any(value is False for value in checks.values()):
            say(f"{mutant_id} BUILD-STALE the PDB does not hold the mutated source's hash: {checks}")
            return
        print(f"  {mutant_id}: packed in {secs:.0f}s, source hash in PDB: {checks}", flush=True)
        drop_cached(version)
        have_matrix = [False]
        pairs = [("main", suite) for suite in FAMILIES["main"]] + [tuple(p) for p in info["extra"]]
        if not run_suites(pairs, version, out, have_matrix, log):
            return
        ran = set(pairs)
        verdict = compare.compare_mutant(mutant_id, out)
        escalated = []
        for family in info.get("families", ESCALATION):
            if verdict.solid and not info.get("full"):
                break
            more = [(family, s) for s in FAMILIES[family] if (family, s) not in ran]
            if not more:
                continue
            print(f"  {mutant_id}: nothing solid yet, the {family} family next", flush=True)
            run_suites(more, version, out, have_matrix, log)
            ran |= set(more)
            escalated.append(family)
            verdict = compare.compare_mutant(mutant_id, out)
        tail = f" escalated:{'+'.join(escalated)}" if escalated else ""
        say(f"{mutant_id} {verdict.word} solid={verdict.solid} order={verdict.order} racing-new={verdict.racing_new} racing-seen={verdict.racing_seen} {time.time() - started:.0f}s first: {verdict.first}{tail}")
    finally:
        restore_all()
        failures = clean_version(version)
        if failures:
            say(f"{mutant_id} could not remove: {failures}")
        remove_all_builds()


def control_pack():
    version = CONTROL_VERSION
    log = os.path.join(LOGS, BASE_ROOT + ".pack.log")
    if os.path.exists(log):
        os.remove(log)
    restore_all()
    error, secs = pack(version, PACK_BASE, log)
    if error:
        say(f"control: {error}")
        return
    error, more = pack(version, PACK_MATRIX, log)
    if error:
        say(f"control: {error}")
        return
    print(f"control packed in {secs:.0f}s + {more:.0f}s", flush=True)
    for rel in PATCHED_FILES:
        with open(os.path.join(PATCHED, rel), encoding="utf-8", newline="") as handle:
            print(f"  {rel}: source hash in PDB: {pdb_has(rel, handle.read())}", flush=True)
    drop_cached(version)
    say("CONTROL_PACKED")


def control_fast(first_round, rounds, tokens):
    """Unmutated rounds: each suite built once, then run rounds times, so what races by itself shows. A token is a family or family/suite. The m00 packages are in the feed."""
    version = CONTROL_VERSION
    pairs = []
    for token in tokens:
        family, _, one = token.partition("/")
        pairs += [(family, one)] if one else [(family, s) for s in FAMILIES[family]]
    if True:
        for family, suite in pairs:
            if not disk_ok():
                break
            cwd, config = suite_parts(family, suite)
            remove_build(cwd)
            env = base_env()
            build_log = os.path.join(LOGS, f"control-fast-{family}-{suite}.log")
            if os.path.exists(build_log):
                os.remove(build_log)
            code, _ = run(["dotnet", "build"] + config + [f"-p:KronikolVersion={version}", "--nologo", "-v", "q"], cwd, env, build_log, SUITE_TIMEOUT)
            if code != 0:
                say(f"control-fast: {family}/{suite} did not build")
                continue
            for round_no in range(first_round, first_round + rounds):
                out = os.path.join(RESULTS, BASE_ROOT, str(round_no), f"{family}-{suite}")
                shutil.rmtree(out, ignore_errors=True)
                os.makedirs(out)
                env = base_env()
                env["PHASE_PROBE_OUT"] = out
                env.update(SUITE_ENV.get((family, suite), {}))
                log = os.path.join(out, "run.log")
                if "tunit" in suite:
                    code, secs = run([tunit_exe(cwd), "--maximum-parallel-tests", "1", "--no-progress", "--disable-logo"], cwd, env, log, SUITE_TIMEOUT)
                else:
                    code, secs = run(run_command(cwd, config, version, no_build=True), cwd, env, log, SUITE_TIMEOUT)
                write_outcome(out, code, secs)
            remove_build(cwd)
            print(f"  {family}/{suite}: {rounds} rounds, exit of the last {code}", flush=True)
    say("CONTROL_FAST_DONE")


def more(mutant_id, tokens):
    """More suites for a mutant already run: keeps what it has, adds these, compares again. A token is a family or family/suite; a suite it has run is skipped."""
    out = os.path.join(RESULTS, mutant_id)
    os.makedirs(out, exist_ok=True)
    version = "5.28.3-" + mutant_id
    log = os.path.join(LOGS, f"{mutant_id}.pack.log")
    pairs = []
    for token in tokens:
        family, _, one = token.partition("/")
        for suite in ([one] if one else FAMILIES[family]):
            if not os.path.isdir(os.path.join(out, f"{family}-{suite}")):
                pairs.append((family, suite))
    if not pairs:
        say(f"{mutant_id} more: nothing new to run")
        return
    drop_cached(version)
    for nupkg in glob.glob(os.path.join(FEED, f"*.{version}.nupkg")):
        os.remove(nupkg)
    apply_mutant(mutant_id, out)
    try:
        error, _ = pack(version, PACK_BASE, log)
        if error:
            say(f"{mutant_id} more BUILD-FAILED {error}")
            return
        drop_cached(version)
        run_suites(pairs, version, out, [False], log)
        verdict = compare.compare_mutant(mutant_id, out)
        say(f"{mutant_id} more {len(pairs)} suites: {verdict.word} solid={verdict.solid} order={verdict.order} racing-new={verdict.racing_new} racing-seen={verdict.racing_seen} first: {verdict.first}")
    finally:
        restore_all()
        clean_version(version)
        remove_all_builds()


def rerun(mutant_id, pair, times):
    family, suite = pair.split("/")
    out_root = os.path.join(RESULTS, mutant_id, "repeat")
    os.makedirs(out_root, exist_ok=True)
    version = "5.28.3-" + mutant_id
    log = os.path.join(LOGS, f"{mutant_id}.pack.log")
    drop_cached(version)
    apply_mutant(mutant_id, out_root)
    try:
        error, _ = pack(version, PACK_BASE, log)
        if not error and (family == "matrix" or (family, suite) in NEEDS_MATRIX_PACK):
            error, _ = pack(version, PACK_MATRIX, log)
        if error:
            say(f"{mutant_id} rerun BUILD-FAILED {error}")
            return
        drop_cached(version)
        cwd, config = suite_parts(family, suite)
        remove_build(cwd)
        code, _ = run(["dotnet", "build"] + config + [f"-p:KronikolVersion={version}", "--nologo", "-v", "q"], cwd, base_env(), log, SUITE_TIMEOUT)
        if code != 0:
            say(f"{mutant_id} rerun: {pair} did not build")
            return
        for n in range(1, times + 1):
            out = os.path.join(out_root, str(n), f"{family}-{suite}")
            shutil.rmtree(out, ignore_errors=True)
            os.makedirs(out)
            env = base_env()
            env["PHASE_PROBE_OUT"] = out
            env.update(SUITE_ENV.get((family, suite), {}))
            log_run = os.path.join(out, "run.log")
            if "tunit" in suite:
                code, secs = run([tunit_exe(cwd), "--maximum-parallel-tests", "1", "--no-progress", "--disable-logo"], cwd, env, log_run, SUITE_TIMEOUT)
            else:
                code, secs = run(run_command(cwd, config, version, no_build=True), cwd, env, log_run, SUITE_TIMEOUT)
            write_outcome(out, code, secs)
        remove_build(cwd)
        say(f"{mutant_id} rerun of {pair} x{times} done")
    finally:
        restore_all()
        clean_version(version)
        remove_all_builds()


def main():
    os.makedirs(LOGS, exist_ok=True)
    os.makedirs(RESULTS, exist_ok=True)
    snapshot_patched()
    verb = sys.argv[1]
    if verb == "check":
        for mutant_id in ORDER:
            mutated_text(mutant_id)
        print(f"{len(ORDER)} mutants: every edit matches")
    elif verb == "control-pack":
        control_pack()
    elif verb == "control-fast":
        control_fast(int(sys.argv[2]), int(sys.argv[3]), sys.argv[4:])
    elif verb == "clean-control":
        print(clean_version(CONTROL_VERSION))
    elif verb == "run":
        for mutant_id in sys.argv[2:]:
            if os.path.exists(os.path.join(MUT, "STOP")):
                say("STOP file: ending the run")
                break
            if not disk_ok():
                break
            if os.path.exists(os.path.join(MUT, 'SKIP_' + mutant_id)):
                say(f'{mutant_id} skipped: a SKIP file')
                continue
            print(f"== {mutant_id}: {MUTANTS[mutant_id]['desc']} (expect {MUTANTS[mutant_id]['expect']})", flush=True)
            run_mutant(mutant_id)
        say("RUN_DONE")
    elif verb == "rerun":
        rerun(sys.argv[2], sys.argv[3], int(sys.argv[4]))
        say("RUN_DONE")
    elif verb == "more":
        more(sys.argv[2], sys.argv[3:])
        say("RUN_DONE")


if __name__ == "__main__":
    main()
