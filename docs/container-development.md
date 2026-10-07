# Container development

Run repository .NET, PowerShell, packaging, tests, and acceptance commands in the canonical Linux amd64 container. The host needs Docker Engine or Docker Desktop with Compose v2 and Linux-container support.

## Finite tasks from the host checkout

From the repository root:

```text
docker compose build tooling
docker compose run --rm tooling contract
docker compose run --rm tooling test -Target SharpProof.Worker.Test/SharpProof.Worker.Test.csproj -TestFilter FullyQualifiedName~NameOfTest
docker compose run --rm tooling pr
```

`tooling` starts a finite task. Its entrypoint clones/copies the source snapshot into a private workspace under `/tmp`, then invokes [Invoke-SharpProofContainer.ps1](../scripts/Invoke-SharpProofContainer.ps1). Dirty tracked changes are included for ordinary development commands. Builds do not reuse host `bin` or `obj`. Artifacts are exported through the repository's `artifacts` mount.

`pack`, `nightly`, and `fuzz-nightly` require clean exact-commit source. Commands that need source comparison also require usable Git metadata. Ignored `nupkgs` release inputs are copied explicitly; do not assume arbitrary ignored host files enter a finite task.

## Persistent development

Open [.devcontainer/devcontainer.json](../.devcontainer/devcontainer.json) with a compatible Dev Container client. The `dev` service uses a container-owned Git workspace in a named volume, not the host source bind mount. Initialization uses `SHARPPROOF_ORIGIN_URL` and optional `SHARPPROOF_DEV_REF`.

Inside that workspace, `sp` dispatches the same command profiles:

```text
sp contract
sp test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -TestFilter FullyQualifiedName~DocumentationSnippetTests
sp pr
```

Intentional source-writing maintenance such as `sp corpus-update -Configuration Release` belongs in a persistent container workspace. Review and commit those changes there. Finite task copies are disposable.

The `loop` service uses a separate named workspace with read-only host source input and exported artifacts. Its source input cannot accept golden expectation writes.

## Command reference

| Command | Purpose |
| --- | --- |
| `contract` | Validate the canonical container contract |
| `restore` | Locked restore of `-Target` |
| `build` | Locked restore and build of `-Target` |
| `test` | Targeted project/solution testing |
| `test-changed` | Change-based test selection |
| `quick` | Debug changed tests with `-Fast`; development feedback |
| `semantic-tests` | Scheduled semantic test suite |
| `portable-tests` | Portable test solution |
| `worker-tests` | Worker test project |
| `package-tests` | Package test scheduler and package-backed harness |
| `check` | Developer-check plan: restore, build, semantic and package validation |
| `pr` | Release `pr-gates` profile |
| `pr-gates` | Container contract, solution build, semantic/package tests excluding Performance, Coverage, and Corpus categories; requires Release |
| `self-apply` | Apply packaged analysis to the source project set and samples |
| `samples` | Package-backed sample matrix; packs a feed if none is supplied |
| `pack` | Clean exact-commit package graph build and validation |
| `package-consumers` | Real analyzer/verifier consumers and minimum-SDK framework consumers; requires `-PackageSource` |
| `corpus` | Canonical corpus and support-ratchet checks |
| `corpus-update` | Intentional corpus observation update |
| `coverage` | Release coverage and acceptance checks; requires a comparison ref |
| `dependency-audit` | Locked restore and dependency vulnerability audit |
| `security` | Dependency audit and full Release build with banned-API enforcement |
| `fuzz-nightly` | Clean exact-commit fuzz campaign |
| `nightly` | Dependency audit, corpus, then fuzz campaign |

`dev` opens a shell directly through the container entrypoint:

```text
docker compose run --rm tooling dev -lc 'pwd'
```

With `tooling dev`, the shell works in the host bind-mounted checkout and can change host source files. This differs from the named-volume `dev` service and from finite private task copies. Use the persistent Dev Container for sustained development, or this direct shell for a specific container-only operation in the host checkout.

## Parameters and package inputs

`-Configuration` accepts Debug or Release and normally defaults to Debug. Profiles such as `pr`, `security`, and coverage choose Release internally. `-Target` defaults to `SharpProof.slnx`. `-TestFilter` accepts the normal .NET test filter.

`-NoBuild` applies only to reusable test commands in an already built workspace; separate finite runs create separate workspaces. `-Fast` applies only to non-qualifying test commands and disables build analyzers while retaining generation and tests. `-ReuseTestHarness` is supported only for `package-tests`. These switches are not substitutes for qualification.

After packing clean source, test the exported package bytes:

```text
docker compose run --rm tooling package-consumers -Configuration Release -PackageSource artifacts/container-packages
docker compose run --rm tooling samples -Configuration Release -PackageSource artifacts/container-packages
```

## Coverage

The gate needs a reachable comparison commit/ref for changed trusted-code coverage. Choose the actual base of the change, not an arbitrary ref that hides changed lines.

```text
docker compose run --rm -e SHARPPROOF_COVERAGE_COMPARISON_REF=origin/master tooling coverage -Configuration Release
```

CI passes its resolved comparison SHA. The coverage scheduler reserves the complete semantic project phase's worker slots so solver work does not overlap other worker-heavy shards. This scheduling rule does not alter proof budgets, timeouts, or coverage floors.

Coverage validation caches coverage credits for each source line from matching PDB ranges within one run. It preserves every overlapping sequence-point start and keeps lines that are permitted without granting coverage credit. The cache is rebuilt from independently authenticated current binaries and PDBs on each invocation.

CI uploads the semantic and package coverage timing profiles from `artifacts/timings/*-coverage.json` alongside the coverage reports. These profiles show test phase and shard durations when investigating slow runs.

## Efficient test iteration

For local test iteration, combine related filters in one finite task to pay for one restore and build:

```text
docker compose run --rm tooling test -Configuration Release -Target SharpProof.Worker.Test/SharpProof.Worker.Test.csproj -TestFilter "FullyQualifiedName~ClaimManifestBuilderTests|FullyQualifiedName~CompilerManifestArtifactTests"
```

Use the persistent development workspace for repeated edits so normal builds reuse unchanged outputs. Separate finite tasks cannot share build outputs with `-NoBuild`.

## Multiple independent workspaces

Compose normally namespaces volumes by directory name. When worktree basenames collide, set a distinct `COMPOSE_PROJECT_NAME` before running commands. Do not run concurrent builds against a single bind-mounted set of `bin`, `obj`, or `artifacts`.

[compose.yaml](../compose.yaml) sets Linux amd64, a default 40 GiB memory limit, an 8 GiB `/tmp` tmpfs, and no CPU quota by default. Configure `SHARPPROOF_CONTAINER_MEMORY_LIMIT`, `SHARPPROOF_TMPFS_SIZE`, and `SHARPPROOF_CONTAINER_CPU_LIMIT` for your machine. `SHARPPROOF_TEST_PROJECT_PARALLELISM` caps test project lanes. Docker owns resource isolation; do not add host process-memory budgets.

Pinned SDK, runtime, PowerShell, and Z3 inputs are in [toolchain.json](../eng/container/toolchain.json). Rebuild the image after changing its Dockerfile, entrypoint, development initialization scripts, or pinned inputs.
