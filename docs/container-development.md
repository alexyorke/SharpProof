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
checksum-pinned image and runs container Git to clone the configured origin
and optional ref into the Compose-owned `sharpproof-workspace` volume. It then
validates the installed container contract and performs a locked restore. All
edits, Git operations, `bin`/`obj` trees, and local artifacts live in that
volume. Later starts reuse the workspace, NuGet, and .NET-home volumes, so they
work offline when the required source and packages are already present. The
terminal and editor server run as the non-root `sharpproof` user.

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
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -NoBuild
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
After a matching build, `sp test-changed -NoBuild` reuses the existing output
trees and skips both restore and compilation; use the normal command whenever
source, project, or configuration changes require a rebuild.
The default Debug check performs one Debug solution build, one additional Debug package-test build, and 3 build-capable Release pack commands.
The Release check performs one Release solution build and 3 Release pack commands with `--no-build`.
Both run
duration-aware semantic, Worker, and package shards plus a short performance
smoke. The exact release performance protocol remains part of `sp acceptance`;
trusted mutations remain a separate exact-commit gate.

The permanent `dev` service retains `bin` and `obj`, MSBuild nodes, and
Roslyn's compiler server. A no-change rebuild is therefore incremental.
Finite `docker compose run --rm tooling ...` commands materialize the current
source snapshot in a private temporary workspace and pay a cold build; use them
for qualification, not for every edit. `contract`, `build`, and ordinary test
commands work when the source directory came from an archive without `.git`.
After a completed build in the permanent workspace, `sp worker-tests -NoBuild`
reuses those outputs and skips the restore/build phase for fast filtered runs.
Use the normal `sp worker-tests` command after source, project, or configuration
changes so the Worker dependency closure is rebuilt.
The same `-NoBuild` fast path is available on `sp test`, `sp semantic-tests`,
`sp portable-tests`, and `sp package-tests`; use it only when the matching
configuration and package outputs already exist in this workspace.
For a single test project, `sp test -NoBuild` runs the built test assembly
directly through VSTest, avoiding another MSBuild project-graph evaluation.
The non-coverage semantic worker shards use the same direct-assembly path;
coverage keeps the project-aware runner so each shard can receive isolated
instrumented outputs.
Solution and sharded semantic/package commands retain their project-aware
runner because they coordinate multiple outputs and fixtures.
Commands that compare revisions or certify exact-commit evidence (`test-changed`,
acceptance, mutation, packaging, pilots, fuzz, coverage, and release commands)
require a Git-backed source workspace. Start the persistent Dev Container to
obtain that workspace using container Git; Git remains unnecessary on the host.

`portable-tests`, broad coverage, and acceptance run independent test projects
through MSBuild's project scheduler.

Containers use all CPUs available to Docker and up to 40960 MiB by default.
Test-project concurrency auto-detects the available CPUs and uses one lane per 2 CPUs.
Finite task workspaces use an 8 GiB `/tmp` tmpfs by default, keeping source
snapshots, compiler scratch, and test outputs off the host filesystem. Set
`SHARPPROOF_TMPFS_SIZE` higher for unusually large package or coverage runs.
Trusted mutations use 4 deterministic weighted lanes. Worker fixtures and
package integration methods run in isolated duration-weighted processes.
Override the
Docker budget with
`SHARPPROOF_CONTAINER_CPU_LIMIT` and `SHARPPROOF_CONTAINER_MEMORY_LIMIT`; the
lane count follows the CPUs visible to .NET. Use
`SHARPPROOF_TEST_PROJECT_PARALLELISM` only for profiling or diagnosis.
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
