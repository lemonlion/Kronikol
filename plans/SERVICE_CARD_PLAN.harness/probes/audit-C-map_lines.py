# Maps a line cited at one commit to the same line at another, by content: prints the cited line's text at the old
# commit and every line number at the new commit whose stripped text is identical.
# Usage: python -I -X utf8 map_lines.py <repo> <old> <new>   (citations are listed below)
import subprocess
import sys

CITES = [
    ("src/Kronikol/History/InteractionShape.cs", [52, 102, 209, 246, 271, 272, 278]),
    ("src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs", [54, 531]),
    ("src/Kronikol/Reports/ReportGenerator.cs", [579, 594, 781, 1063, 5780]),
    ("src/Kronikol/Query/QueryCommand.cs", [172]),
    ("src/Kronikol/Reports/RunManifest.cs", [27, 79, 135]),
    ("src/Kronikol.Tool/CtrfCommand.cs", [80]),
    ("src/Kronikol/History/HistoryRunBuilder.cs", [150]),
    ("src/Kronikol/Query/QueryCommand.Overview.cs", [335, 353]),
    ("src/Kronikol/Reports/CtrfReportGenerator.cs", [212]),
    ("src/Kronikol/Constants/TrackingDefaults.cs", [12]),
    ("Directory.Build.props", [5, 6]),
]


def show(repo, commit, path):
    out = subprocess.run(["git", "-C", repo, "show", f"{commit}:{path}"], capture_output=True, check=True)
    return out.stdout.decode("utf-8", "replace").splitlines()


def main(repo, old, new):
    for path, lines in CITES:
        a = show(repo, old, path)
        b = show(repo, new, path)
        for n in lines:
            text = a[n - 1].strip() if n - 1 < len(a) else "<past end>"
            hits = [i + 1 for i, line in enumerate(b) if line.strip() == text and text]
            print(f"{path}:{n} -> {hits if hits else 'GONE'}   | {text[:110]}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
