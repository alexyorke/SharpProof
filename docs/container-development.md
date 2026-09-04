# Container development

The canonical Linux amd64 Dev Container is the primary SharpProof development
environment. The host needs Docker Engine or Docker Desktop, Compose v2, and
an editor that supports Dev Containers. It does not need Git, .NET,
PowerShell, MSBuild, Visual Studio, Z3, Make, or Just for repository execution.
The source directory containing `compose.yaml` may be obtained as an archive,
through an editor's repository-clone command, or with any source-control
client; SharpProof never invokes that host client.

## Open the permanent environment

1. Open the source directory that contains `compose.yaml`.
2. Give each independent checkout a unique `COMPOSE_PROJECT_NAME`. The easiest
   OS-neutral configuration is an untracked `.env` file beside `compose.yaml`:

   ```text
   COMPOSE_PROJECT_NAME=sharpproof-feature-a
   SHARPPROOF_ORIGIN_URL=https://github.com/alexyorke/SharpProof.git
   SHARPPROOF_DEV_REF=feature-a
   ```

3. Ensure `SHARPPROOF_DEV_REF`, when set, names a branch or tag available from
   that origin, then choose **Dev Containers: Reopen in Container** in VS Code.

No initialization command runs on the host. The first start builds the
pinned toolchain image and runs container Git to clone the configured origin
and optional ref into the Compose-owned `sharpproof-workspace` volume. It then
validates the installed container contract and performs a locked restore. All
edits, Git operations, `bin`/`obj` trees, and local artifacts live in that
volume. Later starts reuse the workspace, NuGet, and .NET-home volumes, so they
work offline when the required source and packages are already present. The
terminal and editor server run as the non-root `sharpproof` user.

## Run the same profiles as CI

From a host checkout with PowerShell 7, `build.ps1` is the convenient
finite-task entrypoint. It runs the cached Compose build and invokes the same
container commands used by GitHub Actions. The coverage profile also uses host
Git to resolve its comparison ref to an exact commit:

```powershell
./build.ps1 quick
./build.ps1 pr
./build.ps1 nightly
./build.ps1 security
./build.ps1 coverage -ComparisonRef origin/master
```

The workflows add only GitHub-hosted concerns such as event-to-comparison-ref
selection, cache transport, artifact upload, and protected publication
environments. Gate selection and ordering live in the
container pipeline profiles, so reproducing CI does not require translating
workflow YAML.

Docker remains the only required host tool. Without PowerShell, run the same
composite profiles directly through Compose:

```text
docker compose run --rm tooling pr
docker compose run --rm tooling nightly
docker compose run --rm tooling security
```

An existing workspace is never fetched, reset, or switched automatically.
Commit and push from inside the container. Set a new project name when a clean
checkout of another ref is required.

Use the short `sp` command inside the container:

```text
sp test-changed
sp check
sp build
sp self-apply
sp self-apply -Configuration Release -PackageSource artifacts/container-packages
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -Fast
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -NoBuild
sp test-changed -Fast
sp test-changed -NoBuild
sp semantic-tests
sp semantic-tests -NoBuild
sp portable-tests
sp portable-tests -NoBuild
sp worker-tests
sp worker-tests -NoBuild
sp package-tests
sp package-tests -NoBuild
sp corpus -Configuration Release
sp coverage
sp mutation -Configuration Release
sp acceptance -Configuration Release
```

Use `sp test-changed` during an edit loop. It derives the affected test-project
closure from Git and project references.
Use `-Fast` while iterating on code. It asks Roslyn to skip diagnostic-analyzer
execution for that build while still compiling and running the selected tests.
It is non-qualifying: run the same command without `-Fast`, or run `sp check`,
before delivery. Roslyn records the skipped-analyzer build so a later normal
build reruns analyzers even when outputs are otherwise up to date. `-Fast` and
`-NoBuild` are mutually exclusive.
After a matching build, `sp test-changed -NoBuild` reuses the existing output
trees and skips both restore and compilation; use the normal command whenever
source, project, or configuration changes require a rebuild.
The default Debug check concurrently performs one Debug solution build and one Release package-product build, then runs 3 Release pack commands with `--no-build`.
The Release check performs one Release solution build and 3 Release pack commands with `--no-build`.
Both run
duration-aware semantic, Worker, and package shards plus a short performance
smoke. The exact release performance protocol remains part of `sp acceptance`;
trusted mutations remain a separate exact-commit gate.

The permanent `dev` service retains `bin` and `obj`, MSBuild nodes, and
Roslyn's compiler server. It also enables the opt-in MSBuild server, whose
evaluation cache remains warm between closely spaced commands. A no-change
rebuild is therefore incremental and avoids repeatedly starting MSBuild.
Finite `build.ps1` profiles and their equivalent
`docker compose run --rm tooling ...` commands materialize the current source
snapshot in a private temporary workspace and pay a cold build; use them for
qualification, not for every edit. `contract`, `build`, and ordinary test
commands work when the source directory came from an archive without `.git`.
For direct Compose runs, add `--no-TTY --quiet-pull` for the same concise
terminal behavior that `build.ps1` uses by default.

For a host-edited Git checkout, the separate `loop` service provides the same
warm-build behavior without writing Linux outputs into the host tree:

```text
docker compose up -d loop
docker compose exec loop sharpproof-loop test-changed -Fast
docker compose exec loop sharpproof-loop test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -Fast
```

When PowerShell and Git are available on the host, use the optional snapshot
wrapper for the shortest host-edited cycle:

```powershell
pwsh -File scripts/Invoke-SharpProofLoop.ps1 test-changed -Fast
pwsh -File scripts/Invoke-SharpProofLoop.ps1 test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -Fast
```

The wrapper captures the Git patch and untracked files on the host, where Git
can scan the checkout quickly, and passes that bounded snapshot to the private
Linux workspace. The regular `sharpproof-loop` command remains the portable
fallback when host Git is unavailable.

The private clone uses `SHARPPROOF_ORIGIN_URL` (the public SharpProof origin by
default), so local Release builds retain the same Source Link identity as
disposable qualification builds. Keep that override credential-free.

Each `sharpproof-loop` command mirrors the current tracked and non-ignored
untracked source into a private Compose volume, then runs `sp` there. The mirror
keeps `bin`, `obj`, MSBuild and Roslyn compiler servers, and package caches
between commands. A
non-blocking workspace lock rejects overlapping commands, so multiple agents
cannot corrupt the shared incremental outputs. Use a distinct
`COMPOSE_PROJECT_NAME` for an independent checkout or independent build lane.
The loop source must be a Git checkout that is directly visible inside Docker;
use the ordinary persistent Dev Container for a linked Git worktree whose
administrative Git directory is outside the mounted checkout. The loop volume
is a disposable mirror: edit and commit only in the host checkout. Continue to
use finite `tooling` containers for final qualification.

After a completed build in the permanent workspace, `sp worker-tests -NoBuild`
reuses those outputs and skips the restore/build phase for fast filtered runs.
Use the normal `sp worker-tests` command after source, project, or configuration
changes so the Worker dependency closure is rebuilt.
The same `-NoBuild` fast path is available on `sp test`, `sp semantic-tests`,
`sp portable-tests`, and `sp package-tests`; use it only when the matching
configuration and package outputs already exist in this workspace.
When `sp test -Target SharpProof.sln` is run without a filter, the ordinary
solution lane runs every non-package test and then hands `SharpProof.Package.Test`
to the dedicated package scheduler. That scheduler builds and packs the product
feed once, reuses the already-built test harness, and shards the package tests
instead of running the package project as one serial solution test process.
For a single test project, `sp test` performs the required incremental build
and then runs the built assembly directly through VSTest, avoiding a second
MSBuild project-graph evaluation. `-NoBuild` skips that build as well.
`sp worker-tests` uses the same build-then-VSTest path.
`sp test-changed` also uses it when the dependency analysis selects exactly
one test project. When that project is `SharpProof.ArchitectureTest`, it reuses
the semantic runner's duration-aware architecture-fixture sharding instead of
running the repository-wide checks serially in one test process.
Non-coverage semantic shards and ordinary package shards use the same
direct-assembly path. Package process-containment postflight shards retain the
project-aware runner and run exclusively after the parallel wave. The generic
single-project command also retains the project-aware runner for
`SharpProof.Package.Test` because callers can select those containment tests.
Coverage keeps the project-aware runner so each shard can receive isolated
instrumented outputs. Solution commands retain their project-aware runner
because they coordinate multiple outputs.
Commands that compare revisions or certify exact-commit evidence (`test-changed`,
acceptance, mutation, packaging, pilots, fuzz, coverage, and release commands)
require a Git-backed source workspace. Start the persistent Dev Container to
obtain that workspace using container Git; Git remains unnecessary on the host.

`portable-tests`, broad coverage, and acceptance run independent test projects
through MSBuild's project scheduler.

Containers use all CPUs available to Docker and up to 40960 MiB by default.
Semantic-test scheduling uses every container-visible CPU.
Set `SHARPPROOF_SEMANTIC_TEST_PARALLELISM` to cap it between 1 and the visible CPU count.
The persistent workspace serializes commands.
Package integration tests use 75% of container-visible CPU lanes by default.
Other test-project concurrency auto-detects the available CPUs and uses one lane per 2 CPUs.
Parallel prerequisite builds use 75% of container-visible CPU lanes by default.
Finite task workspaces use an 8 GiB `/tmp` tmpfs by default, keeping source
snapshots, compiler scratch, and test outputs off the host filesystem. Set
`SHARPPROOF_TMPFS_SIZE` higher for unusually large package or coverage runs.
Trusted mutations use 4 deterministic weighted lanes. Worker fixtures and
package integration methods run in isolated duration-weighted processes.
Override the
Docker budget with
`SHARPPROOF_CONTAINER_CPU_LIMIT` and `SHARPPROOF_CONTAINER_MEMORY_LIMIT`; the
lane count follows the CPUs visible to .NET. Use
`SHARPPROOF_TEST_PROJECT_PARALLELISM` only for profiling or diagnosis. To cap
semantic-test scheduling without changing other test/build lanes, use
`SHARPPROOF_SEMANTIC_TEST_PARALLELISM` (an integer from 1 through the
container-visible CPU count).
When set, that override caps semantic and other test-project concurrency as
well as parallel prerequisite-build lanes.
The lane count is per container: when several agents share one Docker VM, cap
each heavy container with that override (typically 1-2 lanes) and keep the
aggregate build-capable containers within the VM memory budget.

NUnit tests within a project are not globally forced parallel. Several package
tests intentionally mutate process-local environment variables; those fixtures
run in separate test processes instead. Z3 retains its query and method
instruction limits. Whole-process wall deadlines remain necessary for compiler,
MSBuild, filesystem, or child-process hangs that solver limits cannot observe.

## Multiple independent workspaces

Each source directory or remote ref must use a different Compose project name.
This gives it a private tooling image tag, persistent source volume, cache
volumes, and output tree. By default the tooling image is
`<compose-project-name>-tooling:local`; build and run therefore resolve the
same checkout-owned tag. Put the project name and ref in each checkout's
untracked `.env` file, especially when two checkout paths have the same base
directory name, then use the same commands on every operating system. A
reviewed immutable image can instead be selected explicitly with
`SHARPPROOF_TOOLING_IMAGE`.

```text
docker compose up -d dev
docker compose exec dev sharpproof-dev-init
docker compose exec dev bash
```

Dev Containers runs the initializer automatically. When starting Compose
directly, invoke it once as shown above. To deliberately discard a container
workspace, stop the Compose project and remove only that project's named
workspace volume.

Do not run two builds against the same Compose project. One project-owned
workspace volume is the isolation boundary; there is no Docker socket,
privileged mode, host network, host build output, or cross-machine shared state.

Every finite acceptance run writes phase durations to
`artifacts/timings/acceptance-<configuration>.json`. Mutation campaigns write
their lane durations to `artifacts/timings/mutation-<configuration>.json`.
Semantic, package, and developer checks write corresponding timing records in
the same directory and reuse those timings for scheduling.
Use those records before changing concurrency or time budgets.
