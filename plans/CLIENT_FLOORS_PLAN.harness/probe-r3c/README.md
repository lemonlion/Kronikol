# probe-r3c: F39 and F40

R3c's probes of `Kronikol.PlantUml.Ikvm` (`plans/CLIENT_FLOORS_PLAN.md` section 8), 2026-10-29, Windows x64.

- `bind/`: the file-based program that found both, `probe.cs`. It references the package by its project
  (`#:project`, the release worktree's path) and IKVM 8.16.3 by package, and prints the text of a sequence and a
  component diagram. Built on 8.9.0 the component diagram drew PlantUML's error page after "Currency data format is
  incorrect" (`run-8.9.0-on-8.16.3.txt`); built on 8.15.0 it drew (`run-8.15.0-on-8.16.3.txt`). The difference was the
  image the package's build copied into the program's output, not the package (F39).
- `mixing.txt`: which release's files the suite's output held, before and after the fix, and why 8.15.0's image
  passed where 8.9.0's failed.
- `package_sizes.py`, `package-sizes.txt`: the size of every published `Kronikol.PlantUml.Ikvm` package, 26 to 36 KB,
  too small to carry the PlantUML assembly (F40).
- `consumer/`: a project that takes the package from a feed, as a consumer does. `run-5.27.12-nuget.org.txt` is
  5.27.12 from nuget.org, which fails at the first render (F40). `consumer.sh` packs the release commit's `Kronikol`
  and `Kronikol.PlantUml.Ikvm` as `5.27.13-probe.1` and runs the project three ways, each from an empty bin/ and obj/:
  the package alone, on 8.9.0 (`run-5.27.13-probe.1-alone.txt`); IKVM alone raised to 8.16.3, which brings IKVM.Image
  and IKVM.MSBuild with it while IKVM.Image.JDK and IKVM.Image.JRE stay on 8.9.0 (`...-ikvm-raised.txt`); and all
  five raised (`...-all-raised.txt`). Each draws both diagrams. The project ran from a folder outside the repository,
  with the packed packages in `feed/` beside it (`nuget.config`), so the repository's build props did not apply; the
  probe packages were removed from the NuGet packages folder afterwards.
