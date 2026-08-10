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

The first start builds the checksum-pinned image, validates the installed
container contract, and performs a locked restore. Later starts reuse that
worktree's Compose-owned NuGet and .NET-home volumes. The source remains a bind
mount so edits persist normally. The terminal and editor server run as the
non-root `sharpproof` user.

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
through MSBuild's project scheduler. The default 8-CPU container uses four
project lanes. Override the Docker budget with
`SHARPPROOF_CONTAINER_CPU_LIMIT` and `SHARPPROOF_CONTAINER_MEMORY_LIMIT`; the
lane count follows the CPUs visible to .NET. Use
`SHARPPROOF_TEST_PROJECT_PARALLELISM` only for profiling or diagnosis.

NUnit tests within a project are not globally forced parallel. Several package
tests intentionally mutate process-local environment variables; those fixtures
run in separate test processes instead. Z3 retains its query and method
instruction limits. Whole-process wall deadlines remain necessary for compiler,
MSBuild, filesystem, or child-process hangs that solver limits cannot observe.

## Multiple worktrees

Each worktree must use a different Compose project name. This gives it private
cache volumes and an independent source/output tree:

```text
COMPOSE_PROJECT_NAME=sharpproof-feature-a docker compose up -d tooling
COMPOSE_PROJECT_NAME=sharpproof-feature-b docker compose up -d tooling
```

Do not run two builds against the same worktree. One worktree per container is
the isolation boundary; there is no Docker socket, privileged mode, host
network, or cross-machine shared state.
