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
sp build
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj
sp portable-tests
sp worker-tests
sp package-tests
sp corpus -Configuration Release
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

## Multiple independent workspaces

Each source directory or remote ref must use a different Compose project name.
This gives it a private persistent source volume, cache volumes, and output
tree. Put the project name and ref in each checkout's untracked `.env` file,
then use the same commands on every operating system:

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
Use those records before changing concurrency or time budgets.
