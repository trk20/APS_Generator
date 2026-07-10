# AGENTS.md

## Cursor Cloud specific instructions

APS Generator is a single **.NET 10 Avalonia desktop GUI app** (no servers, databases, or network services). Standard build/test/run commands live in `README.md` and `.github/workflows/release.yml`; the notes below only cover non-obvious caveats for this environment.

### Environment (already provisioned in the VM snapshot)
- The **.NET 10 SDK** is installed at `$HOME/.dotnet` and added to `PATH` via `~/.bashrc`. If `dotnet` is not found in a non-login shell, use `$HOME/.dotnet/dotnet` or `source ~/.bashrc`.
- The system package `libgmpxx4ldbl` (providing `libgmpxx.so.4`) is installed. This is a **runtime dependency of the checked-in native solver** `src/ApsGenerator.Solver/runtimes/linux-x64/native/libcryptominisat5.so`. Without it, every `ApsGenerator.Solver` test fails with `DllNotFoundException: Unable to load shared library 'cryptominisat5'`.
- The `native/cryptominisat` git submodule is **not** needed for dev/testing — a prebuilt `libcryptominisat5.so` is already checked in and auto-copied to the build output. Building the submodule is only for producing a fresh native lib for release.

### Build / test / run
- Build: `dotnet build ApsGenerator.slnx -c Release`
- Test (matches CI, excludes slow/benchmark categories): `dotnet test --filter 'Category!=DataCollection&Category!=Benchmark&Category!=RegressionBenchmark&Category!=Slow'`. The full solver suite still takes ~45s.
- Run the GUI: `dotnet run --project src/ApsGenerator.UI/ApsGenerator.UI.csproj -c Release`. It's a normal cross-platform Avalonia desktop app and runs on either X11 or Wayland (e.g. niri) with no extra setup; in this VM a display is available at `DISPLAY=:1`. The app writes little/nothing to stdout; confirm it is up via the process (`ApsGenerator.UI`) or a screenshot, not logs.
- Solve time is driven mainly by clip count and symmetry, not grid size (a 15×15 solve can finish in ~71ms). 3-Clip is the fastest; 4-Clip and 5-Clip take considerably longer. Enabling symmetry decreases solve time dramatically, and hard symmetry is generally preferable. 4-Clip usually prefers rotational symmetry, while reflexive symmetry is strictly better for 5-Clip. For a quick manual test, 3-Clip solves near-instantly at typical grid sizes.

### Lint / formatting
- There is no configured linter or CI lint step and no `.editorconfig`. Compilation with 0 warnings is the effective check. `dotnet format --verify-no-changes` reports pre-existing whitespace deviations in the test projects that are **not** enforced — do not "fix" them as part of unrelated work.
