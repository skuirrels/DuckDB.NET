# Working on the skuirrels/DuckDB.NET fork

## Branches

- `main` is the only release line. Every fork release (1.5.5, 1.5.6, 2.0.0 and later) is tagged and packed from `main`.
- Base every branch on the fork's `main` and open every pull request against `main`.
- There is no `develop` branch on this fork. It was deleted on 2026-11-15 after falling behind at DuckDB 1.5.4; its last state is the tag `archive/develop-2026-11-15`. Never recreate it or base work on it.
- In local clones the `origin` remote may be upstream Giorgi/DuckDB.NET, whose default branch is `develop`. Tools that read `origin/HEAD` will then report `develop` as the main branch. Ignore that: the fork's remote and `main` are what count. Do not open pull requests upstream.
- The `develop` references in `.github/workflows/Sonar.yml`, `scorecard.yml`, the `publish` job in `ci.yml` and `GitVersion.yml` are inherited from upstream and do nothing on the fork. Do not point the `ci.yml` publish job at `main`: it pushes to the upstream GitHub package feed.

## Before starting work

1. Fetch the fork and branch from its `main`, for example `git fetch <fork-remote> && git switch -c <branch> <fork-remote>/main`.
2. Check the DuckDB engine the branch builds against: `DuckDbNativeVersion` in `DuckDB.NET.Bindings/Bindings.csproj`. It must match the current release line. If it does not, stop and ask.
3. If anything suggests a different base branch, such as a task saying "the default branch" or a tool reporting `develop`, confirm it against the fork's `main` before writing code.

## Native libraries

- The build downloads the DuckDB native libraries into `DuckDB.NET.Bindings/obj/runtimes`. If the download fails, run `dotnet msbuild DuckDB.NET.Bindings/Bindings.csproj -t:DownloadNativeLibs -p:BuildType=Full -p:TargetFramework=net8.0`.
- The benchmarks silently fall back to a system DuckDB (for example Homebrew's) when the downloaded library is missing, so check which library loaded before trusting benchmark numbers.

## Commits and releases

- Author commits as `Skuirrels`. Do not add `Co-Authored-By` trailers, "Generated with" footers or any other AI attribution to commits or pull request descriptions.
- Do not publish packages or GitHub releases unless asked. Publishing a release triggers `preview-release.yml`, which packs and pushes the fork packages.
