# Repository layout

SharpProof adopts the [Arcade SDK repository requirements](https://github.com/dotnet/arcade/blob/1141c9df487f40450bc0ab7444f8941b863528dd/Documentation/ArcadeSdk.md).
`global.json` pins .NET SDK `10.0.403` and Arcade `9.0.0-beta.26523.9`, the
Arcade version used by Microsoft's .NET 9 release branch when this integration
was prepared.

Several test projects stay on `net8.0` after .NET 8 leaves support. They
certify facts about the runtime they run on: they assert CoreCLR 8.0.32 and its
`System.Private.CoreLib` identity directly, and the resource-bound model they
check is conditional on that runtime. Moving them to a newer runtime means
re-auditing those facts, not changing a target framework. The worker's
resource-lock witness is pinned the same way, to the CoreLib module of the
pinned .NET 10 runtime. Each runtime patch bump updates that pin after
checking that no CoreLib source changed.

```text
build.cmd / build.ps1 / build.sh Thin build entrypoint wrappers
restore.cmd / restore.sh        Restore entrypoints
test.cmd / test.sh              Build and run unit/integration tests
SharpProof.slnx                  Non-sample source projects
SharpProof.*.slnf                Focused test solution filters
Directory.Build.props/targets   Arcade imports and repository-wide settings
Directory.Packages.props        Central package references
global.json                     Exact .NET and Arcade SDK pins
NuGet.config                    Public package sources and source mapping
src/                            Shipping C# projects, their tests, and developer tools
src/samples/                    Package-backed examples and their solution
eng/Build.props                 Arcade build graph
eng/Versions.props              Component, dependency and tool versions
eng/common/                     Unmodified upstream Arcade bootstrap scripts files
eng/arcade-source.json           Upstream commit, file inventory and checksums
eng/scripts/                    Repository command implementations and checks
eng/SharpProofNUnit/             NUnit integration for Arcade's Test target
eng/container/                  Canonical Linux amd64 toolchain and entrypoints
eng/                            Other packaging, coverage and test infrastructure
tests/                          Corpus gates, fuzzers, test helpers, golden and qualification data
docs/                           Guides and implementation documentation
artifacts/                      Ignored build output, packages and evidence
```

Project directories match project filenames. Test project files use Arcade's
`.Tests` or `.IntegrationTests` suffix. Explicit assembly names retain existing
`SharpProof.*.Test` identities, namespaces and friend-assembly contracts. The
repository-only defensive analyzer is a non-shipping source project. Samples
have a separate solution because their restore requires locally built packages.

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
compiler intermediates and restored build tools remain private. Explicit consumer
fixtures and coverage runs may choose an isolated temporary artifact root. The
three product package IDs and preview version format remain unchanged.

Arcade's custom test-runner extension retains the centrally pinned NUnit
dependencies. Microsoft signing, Visual Studio insertion, localization, Helix
and publishing features are not enabled. Shared bootstrap scripts files remain
unmodified; local builds require no Azure DevOps service or secret.

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
host. Existing `build.ps1 check` and other named profiles remain available.
Additional Arcade switches pass to the unmodified common build driver.

The layout checker inventories every tracked and untracked source file, verifies
all pinned common files, validates project and solution/filter references, and
evaluates every project's output paths. It writes
`artifacts/layout-audit/report.json`. Git administration, ignored local caches
and separate Git worktrees are outside the source inventory; the physical audit
records these exclusions separately instead of treating them as checked source.

Keep solutions, filters, package manifests, acceptance contracts, source links
and documentation current when adding or moving a project. See
[container development](container-development.md) and
[contributing](../CONTRIBUTING.md) for the other validation commands.
