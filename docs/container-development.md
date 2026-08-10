# Container development

The canonical Linux amd64 Dev Container is the primary SharpProof development
environment. The host needs Docker Engine or Docker Desktop, Compose v2, Git,
and an editor that supports Dev Containers. It does not need .NET, PowerShell,
MSBuild, Visual Studio, or Z3.

## Open the permanent environment

1. Clone or create a Git worktree.
2. If another worktree has the same directory basename, give this one a unique
   `COMPOSE_PROJECT_NAME`; otherwise Compose derives a suitable default.
3. In VS Code, choose **Dev Containers: Reopen in Container**.

Before Compose starts, Dev Containers asks host Git to create a local bundle
under `.devcontainer`; this works for ordinary clones and linked Git worktrees
without exposing the host Git directory to the container. The first start
builds the checksum-pinned image, clones that read-only bundle into a
Compose-owned `sharpproof-workspace` volume, and validates the
installed container contract, and performs a locked restore. All edits, Git
operations, `bin`/`obj` trees, and local artifacts then live in that volume.
The host checkout is only the first-start seed; it is never a shared build
output directory. Later starts reuse the workspace, NuGet, and .NET-home
volumes. The terminal and editor server run as the non-root `sharpproof` user.
Only committed `HEAD` and its reachable refs seed a new volume; commit
host-side work before the first open. After that first open, edit and commit
inside the persistent Linux volume so Windows line endings and Git-worktree
metadata never enter the build tree.

Use the short `sp` command inside the container:

```text
sp build
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj
sp portable-tests
sp worker-tests
sp package-tests
sp coverage
sp mutation -Configuration Release
sp acceptance -Configuration Release
```

`portable-tests`, broad coverage, and acceptance run independent test projects
through MSBuild's project scheduler. The default 16-CPU container uses eight
project lanes. Trusted mutations use eight deterministic weighted lanes so
package and worker mutations do not collect in one slow tail. MSBuild node
reuse and Roslyn's shared compiler remain enabled inside each disposable
container; Docker removes those processes when the task exits. Override the
Docker budget with
`SHARPPROOF_CONTAINER_CPU_LIMIT` and `SHARPPROOF_CONTAINER_MEMORY_LIMIT`; the
lane count follows the CPUs visible to .NET. Use
`SHARPPROOF_TEST_PROJECT_PARALLELISM` only for profiling or diagnosis.

NUnit tests within a project are not globally forced parallel. Several package
tests intentionally mutate process-local environment variables; those fixtures
run in separate test processes instead. Z3 retains its query and method
instruction limits. Whole-process wall deadlines remain necessary for compiler,
MSBuild, filesystem, or child-process hangs that solver limits cannot observe.

## Multiple worktrees

Each host seed checkout must use a different Compose project name. This gives
it a private persistent source volume, cache volumes, and output tree:

```text
COMPOSE_PROJECT_NAME=sharpproof-feature-a docker compose up -d dev
COMPOSE_PROJECT_NAME=sharpproof-feature-b docker compose up -d dev
```

When starting Compose directly, initialize each volume once with
`git bundle create .devcontainer/repository.bundle HEAD --branches --tags`
followed by `docker compose exec dev sharpproof-dev-init`. Open a shell with
`docker compose exec dev bash`, or attach with Dev Containers (which runs the
initializer automatically).
Commit and push from that shell. To deliberately discard a container workspace,
stop the Compose project and remove only that project's named workspace volume.

Do not run two builds against the same Compose project. One project-owned
workspace volume is the isolation boundary; there is no Docker socket,
privileged mode, host network, host build output, or cross-machine shared state.

Every finite acceptance run writes phase durations to
`artifacts/timings/acceptance-<configuration>.json`. Mutation campaigns write
their lane durations to `artifacts/timings/mutation-<configuration>.json`.
Use those records before changing concurrency or time budgets.
