# Repository layout

SharpProof adopts the [Arcade SDK repository requirements](https://github.com/dotnet/arcade/blob/1141c9df487f40450bc0ab7444f8941b863528dd/Documentation/ArcadeSdk.md).
[`global.json`](../global.json) pins the exact .NET SDK and Arcade SDK versions;
Dependabot updates the Arcade pin, and the layout check keeps `eng/common` in step
with it.

Several test projects stay on `net8.0` after .NET 8 left support on 2026-11-10.
They certify facts about the runtime they run on: the resource-bound model they
check was reviewed against the CoreCLR 8.0.32 source, and they assert the exact
runtime version and read `System.Private.CoreLib` metadata directly. The tests
run on 8.0.33, the final .NET 8 release. Its only changes from 8.0.32
([compare](https://github.com/dotnet/runtime/compare/v8.0.32...v8.0.33)) are in
`System.Net.Sockets` and the bundled native Brotli decoder; no CoreLib or
`src/coreclr` source changed, so the 8.0.32 review applies unchanged and the
product's `8.0.32` model identifiers stay as they are. Moving them to a newer
runtime means re-auditing those facts, not changing a target framework. The worker's
resource-lock witness is pinned the same way, to the CoreLib module of the
pinned .NET 10 runtime. Each runtime patch bump updates that pin after
checking that no CoreLib source changed.

```text
build.cmd / build.ps1 / build.sh Thin build entrypoint wrappers
restore.cmd / restore.sh        Restore entrypoints
test.cmd / test.sh              Build and run unit/integration tests
ci.sh                           The CI lanes, runnable locally (`./ci.sh --list`)
SharpProof.slnx                  Non-sample source projects (/src/ and /tests/ folders)
SharpProof.*.slnf                Focused test solution filters
Directory.Build.props/targets   Arcade imports and repository-wide settings
Directory.Packages.props        Central package references
global.json                     Exact .NET and Arcade SDK pins
NuGet.config                    Public package sources and source mapping
src/                            Shipping C# projects, their tests, and the repository build analyzers
src/samples/                    Package-backed examples and their solution
eng/Build.props                 Arcade build graph
eng/Versions.props              Component, dependency and tool versions
eng/common/                     Unmodified upstream Arcade bootstrap scripts files
eng/arcade-source.json           Upstream commit, file inventory and checksums
eng/scripts/                    Repository command implementations and checks
eng/testing/                    Shared test helper sources, linked by each test project
eng/SharpProofNUnit/             NUnit integration for Arcade's Test target
eng/container/                  Canonical Linux amd64 toolchain and entrypoints
eng/                            Other packaging, coverage and test infrastructure
tests/                          Corpus gates, fuzzers, smoke projects, golden and qualification data
tests/tooling/                  Python regression tests for the container and CI scripts
docs/                           Guides and implementation documentation
artifacts/                      Ignored build output, packages and evidence
```

| Area | Rule |
| --- | --- |
| Projects | Shipping projects and their tests are under `src`; non-shipping gates, fuzzers, smoke projects and their tests are under `tests`. Project directories match project filenames. The main solution excludes the package-backed samples under `src/samples`, which need locally built packages to restore. The upstream internal tools project stays in the required common tree. |
| Test projects | Test project files use Arcade's `.Tests` or `.IntegrationTests` suffix and are named after the production project they test. Their assembly names keep the historical `SharpProof.*.Test` identities (derived in `Directory.Build.props`), so namespaces and friend-assembly contracts are unchanged. Each test project lists the `eng/testing` helpers it compiles. |
| Build analyzers | `ConsoleLoggingAnalyzer` and `DefensiveCodingAnalyzer` are repository-only, non-shipping analyzers injected into every project by `Directory.Build.targets`. They keep their own pre-SDK output isolation, redirected into Arcade's artifact tree. |
| Shared scripts | Every file under `eng/common` matches the pinned upstream tree. `eng/arcade-source.json` records Git blobs, SHA256 hashes and executable modes. These files are immutable. |
| Engineering | `eng/Build.props` selects the source solution. Per-project restore supports `.slnx` with Arcade 9. Repository extensions live outside `eng/common`. |
| Versions and feeds | Product and package dependency versions live in `eng/Versions.props`; the package version is derived from `PreReleaseVersionLabel` and `PreReleaseVersionIteration`. Public engineering packages have explicit NuGet source mappings. |
| Testing | A custom Arcade test-runner extension uses the centrally pinned NUnit adapter and test SDK. Semantic and package schedulers remain available. |
| Packaging | Package IDs, preview versions, portable symbols and Source Link are product identities. Arcade's explicit symbol-format validation opt-out retains NuGet.org `.snupkg` packages. |
| License | The original MIT copyright and grant remain, with the title and reservation line formatted for Arcade's license validator. |
| Optional features | Microsoft signing, Visual Studio insertion, localization, IBC training, Helix, publishing and Azure DevOps integration are not enabled. GitHub CI is authoritative; local builds need no Azure DevOps service or secret. |

Arcade owns the default build output layout:

```text
artifacts/bin/<project>/<configuration>/<framework>/
artifacts/obj/<project>/<configuration>/<framework>/
artifacts/packages/<configuration>/Shipping/
artifacts/packages/<configuration>/NonShipping/
artifacts/TestResults/<configuration>/
artifacts/log/<configuration>/
artifacts/tmp/<configuration>/
artifacts/toolset/
```

Finite Docker tasks keep these build directories inside their private workspace.
They export evidence and packages on completion, including failed runs, while
compiler intermediates and restored build tools remain private. Worktree-aware
launchers mount resolved Git metadata read-only. Explicit consumer fixtures and
coverage runs may choose an isolated temporary artifact root; explicit restore
caches also supply Arcade's package-import root, and sample builds use private
compiler processes so they do not retain analyzers from deleted temporary feeds.

Run commands from the repository root in the canonical Linux amd64 container:

```text
docker compose build tooling
docker compose run --rm tooling layout
docker compose run --rm tooling arcade --restore --build
docker compose run --rm tooling arcade --restore --build --test --integrationTest
docker compose run --rm tooling test -Target src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj
docker compose run --rm tooling samples -Configuration Release
```

Root build wrappers dispatch through the canonical container when invoked on a
host and pass their switches to the unmodified Arcade common build driver. Named
task commands (`check`, `pr`, `test`, ...) run through
`docker compose run --rm tooling <command>`.

`docker compose run --rm tooling layout` inventories every tracked and nonignored
source file, verifies all pinned common files, validates project and
solution/filter references and static MSBuild inputs, and evaluates every
project's output paths. The repository CI lane runs it. It writes
`artifacts/layout-audit/report.json`. Git administration, ignored local caches
and separate Git worktrees are outside the source inventory; the physical audit
records these exclusions separately instead of treating them as checked source.
Synthetic project files embedded in test source are fixtures, not repository
projects.

Keep solutions, filters, package manifests, acceptance contracts, source links
and documentation current when adding or moving a project. See
[container development](container-development.md) and
[contributing](../CONTRIBUTING.md) for the other validation commands.
