# Working on the skuirrels/DuckDB.NET fork

## Branches

The fork has exactly two release lines:

| Branch | DuckDB engine | Releases |
| --- | --- | --- |
| `main` | 2.x | 2.0.0 and later |
| `release/1.5.x` | 1.5.6 | 1.5.7 and later 1.5 maintenance releases |

- `main` is the default branch and the primary line. Base new work on the fork's `main` and open pull requests against `main`.
- `release/1.5.x` started at the `v1.5.6` tag and takes only fixes for 1.5 users. A fix needed on both lines goes to `main` first, then is cherry-picked onto a branch from `release/1.5.x` with its own pull request. Never merge `main` into `release/1.5.x` or the other way round.
- Older branches such as `release/fork-1.5.4` are history. Do not base work on them.
- There is no `develop` branch on this fork. It was deleted on 2026-11-15 after falling behind at DuckDB 1.5.4; its last state is the tag `archive/develop-2026-11-15`. Never recreate it or base work on it.
- In local clones the `origin` remote may be upstream Giorgi/DuckDB.NET, whose default branch is `develop`. Tools that read `origin/HEAD` will then report `develop` as the main branch. Ignore that: the fork's remote and `main` are what count. Do not open pull requests upstream.
- The `develop` references in `.github/workflows/Sonar.yml`, `scorecard.yml`, the `publish` job in `ci.yml` and `GitVersion.yml` are inherited from upstream and do nothing on the fork. Do not point the `ci.yml` publish job at `main`: it pushes to the upstream GitHub package feed.

## Before starting work

1. Decide which release line the work is for. If the task does not say, it is `main`.
2. Fetch the fork and branch from that line, for example `git fetch <fork-remote> && git switch -c <branch> <fork-remote>/main`.
3. Check the DuckDB engine the branch builds against: `DuckDbNativeVersion` in `Directory.Build.props`. It must match the table above. If it does not, stop and ask.
4. If anything suggests a different base branch, such as a task saying "the default branch" or a tool reporting `develop`, confirm it against the table above before writing code.

## Native libraries

- The build downloads the DuckDB native libraries into `DuckDB.NET.Bindings/obj/runtimes`. If the download fails, run `dotnet msbuild DuckDB.NET.Bindings/Bindings.csproj -t:DownloadNativeLibs -p:BuildType=Full -p:TargetFramework=net8.0`.
- The benchmarks silently fall back to a system DuckDB (for example Homebrew's) when the downloaded library is missing, so check which library loaded before trusting benchmark numbers.

## Commits and releases

- Author commits as `Skuirrels`. Do not add `Co-Authored-By` trailers, "Generated with" footers or any other AI attribution to commits or pull request descriptions.
- Do not publish packages or GitHub releases unless asked. Publishing a release triggers `preview-release.yml`, which packs and pushes the fork packages.
- Publish a 1.5 release from `release/1.5.x` and a 2.x release from `main`. The release workflow on each line only accepts tags for its own version: `v1.5.*` on `release/1.5.x` and `v2.0.*` on `main`. A new minor version, such as 2.1, needs both the job-level tag guard and the version check in `preview-release.yml` updated.
