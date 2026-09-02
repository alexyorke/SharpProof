# SharpProof

SharpProof 1.0.0-preview.1 is a soundness-first Roslyn analyzer and bounded
out-of-process verifier for C#. It deliberately supports a narrow,
compiler-bound subset and fails closed when code, evidence, or resources fall
outside that subset.

The preview is split into three packages:

- SharpProof.Attributes is the netstandard2.0 compile-time contract API.
- SharpProof contains the portable analyzer, generator, and build
  configuration. Keep it private to the development build.
- SharpProof.Verifier contains the build tasks, SharpProof.Worker, launcher,
  and pinned Linux payload for container-only verification.

The verifier has not been promoted to the public NuGet feed yet. Until the
first preview publication, use the repository's package-backed sample matrix,
which packs the exact three-package graph into an isolated local feed.

## Start here

1. Add the contract package to a library that publishes SharpProof annotations.
2. Add the portable analyzer package as a private development dependency.
3. Choose an advisory or strict profile.
4. Build in the canonical Linux amd64 container when verifier execution is
   enabled.

The smallest package setup is:

~~~xml
<ItemGroup>
  <PackageReference Include="SharpProof.Attributes"
                    Version="1.0.0-preview.1" />
  <PackageReference Include="SharpProof"
                    Version="1.0.0-preview.1"
                    PrivateAssets="all" />
</ItemGroup>

<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofFeatures>all</SharpProofFeatures>
</PropertyGroup>
~~~

The default advisory profile analyzes selected code while keeping unannotated
code quiet. SharpProofFeatures accepts effects, contracts, or all.
SharpProofProfile accepts advisory, strict, or off; off omits the analyzer and
generator from the build.

The same choices can be made through analyzer configuration:

~~~ini
[*.cs]
sharpproof_profile = advisory
sharpproof_features = all
~~~

The recognized configuration keys are sharpproof_profile and
sharpproof_features. Their accepted values are `advisory`, `strict`,
`off`, `effects`, `contracts`, and `all`, as documented in the
[diagnostic reference](docs/diagnostic-examples.md).

## A minimal contract

~~~csharp
using SharpProof.Attributes;

public static class Calculator
{
    public static int Increment(int value)
    {
        Contract.Requires(value >= 0);
        Contract.Ensures(Contract.Result<int>() > value);
        return value + 1;
    }
}
~~~

Contract.Requires, Contract.Ensures, and Contract.Assume are direct,
contiguous prologue clauses. They are compiler-elided unless
SHARPPROOF_CONTRACTS is defined; the analyzer rejects that runtime-contract
symbol when it would make a proof unsound. The supported attributes and clause
shape are listed in the [public API reference](docs/public-api.md).

## Strict container verification

Strict builds need the verifier package and an explicit policy. The following
configuration requires every selected claim to be proven and treats unresolved
assumptions as errors:

~~~xml
<ItemGroup>
  <PackageReference Include="SharpProof.Verifier"
                    Version="1.0.0-preview.1"
                    PrivateAssets="all" />
</ItemGroup>

<PropertyGroup>
  <SharpProofProfile>strict</SharpProofProfile>
  <SharpProofFeatures>all</SharpProofFeatures>
  <SharpProofVerify>true</SharpProofVerify>
  <SharpProofVerifyPolicy>require-proven</SharpProofVerifyPolicy>
  <SharpProofAssumptionPolicy>error</SharpProofAssumptionPolicy>
</PropertyGroup>
~~~

SharpProofVerify=true runs the compiler collector and the SharpProof.Worker
through the packaged launcher. The worker consumes a closed compiler artifact,
validates its manifest and evidence, and then runs the bounded proof and replay
checks. Full verifier execution is supported only in the canonical Linux amd64
container; a native host or unsupported container fails explicitly. The
portable analyzer remains cross-platform.

The verify policy values are advisory, warn-on-unknown, and require-proven.
The assumption policy values are allow, warn, and error. Strict profile
defaults are require-proven and error; set them explicitly when the build
contract should be obvious to readers.

Current preview wire contracts are:

- protocol version 11
- cache schema version 13
- manifest schema version 4
- compiler artifact schema version 18
- relational-summary schema version 2
- specification-pack schema version 1

## Results and diagnostics

SharpProof reports one of three semantic outcomes for each worker claim:

| Outcome | Meaning |
|---|---|
| Proven | The goal follows from exact lowering, accountable evidence, and the proof kernel. |
| Refuted | An executable postcondition counterexample or admitted allocation, capability, or exception effect violation was independently replayed. |
| Unknown | The language, model, evidence, or resource budget was insufficient. It is not a proof. |

The schema-owned typed result table includes `VacuousEntry` and the full
`Unavailable` domain; see [unknown reasons](docs/unknown-reasons.md#worker-verification-records).

The analyzer is conservative. An unsupported explicitly selected method emits
SP0047; a concrete precondition violation emits SP0027; verifier assumptions
and trusted evidence are reported through SP0048; and a compiler-artifact
collection failure is SP0049. See the complete diagnostic table and examples
in [docs/diagnostic-examples.md](docs/diagnostic-examples.md).

The portable analyzer does not load Z3. The worker handles bounded Boolean and
integer obligations, exact compiler-produced whole-body CFG/IR, selected API
specifications, and bounded relational summaries. Loops, recursion, virtual
dispatch, mutable-heap reasoning, general sequence reasoning, and unsupported
language forms remain Unknown or visible abstentions. The authoritative
capability matrix is [docs/coverage-and-limits.md](docs/coverage-and-limits.md).

Effect contracts include EnforcePure, ZeroAllocations, DoesNotThrow,
AllowedCapabilities, AllowedExceptions, and EffectContract. Contract analysis
covers direct clauses, closed parameter and return attributes, and
compiler-bound ContractFor companions. These surfaces are described in
[docs/public-api.md](docs/public-api.md) and the
[coverage reference](docs/coverage-and-limits.md).

## Run the repository

Docker Engine or Docker Desktop with Compose v2 is the only host prerequisite
for repository development and verifier qualification. The pinned image
contains the required SDK, Roslyn, PowerShell, and native solver payload.

Use the same named profiles locally and in CI. With PowerShell 7 available,
the optional wrapper runs the cached Compose build, then executes the command
in an isolated Linux amd64 workspace:

~~~text
./build.ps1 quick                # changed tests for the edit loop
./build.ps1 check                # complete local development check
./build.ps1 pr                   # exact pull-request gate
./build.ps1 test -Target SharpProof.Effects.Test/SharpProof.Effects.Test.csproj
~~~

CI invokes the matching `tooling pr`, `tooling nightly`, `tooling security`,
and `tooling coverage` container commands. Without host PowerShell, run the
same two commands directly: `docker compose build tooling`, followed by, for
example, `docker compose run --rm tooling pr`.

The package-backed sample matrix exercises passing, diagnostic, mixed-outcome,
strict-library, and host-rejection consumers:

~~~text
./build.ps1 samples -Configuration Release
~~~

For an incremental edit loop, use the persistent development container:

~~~text
docker compose up -d dev
docker compose exec dev sharpproof-dev-init
docker compose exec dev bash
sp test-changed -Fast
sp check
~~~

`-Fast` keeps source generation and test execution but skips diagnostic
analyzers during the iterative build. It is not qualification evidence; run
the command without `-Fast`, or run `sp check`, before delivery.

The [container development guide](docs/container-development.md) explains
workspace isolation, CI-parity profiles, test targets, resource overrides, and
when a disposable `build.ps1` qualification profile is preferable.

Containers use all CPUs available to Docker and up to 40960 MiB by default.
Semantic-test scheduling uses every container-visible CPU.
Set `SHARPPROOF_SEMANTIC_TEST_PARALLELISM` to cap it between 1 and the visible CPU count.
Package integration tests use 75% of container-visible CPU lanes by default.
Other test-project concurrency auto-detects the available CPUs and uses one lane per 2 CPUs.
Parallel prerequisite builds use 75% of container-visible CPU lanes by default.
Trusted mutations use 4 deterministic weighted lanes.
The default Debug check concurrently performs one Debug solution build and one Release package-product build, then runs 3 Release pack commands with `--no-build`.
The Release check performs one Release solution build and 3 Release pack commands with `--no-build`.

## Documentation and support boundary

Use [docs/README.md](docs/README.md) as the documentation map. It separates
current user guidance, normative semantics, maintained architecture, release
evidence, generated projections, and historical notes.

The most useful references are:

- [Getting started](docs/getting-started.md) for package setup and common
  build commands.
- [Coverage and limits](docs/coverage-and-limits.md) for implemented behavior
  and unsupported language forms.
- [Supported public API](docs/public-api.md) for the compile-time contract
  surface.
- [Diagnostics](docs/diagnostic-examples.md) for IDs, defaults, and examples.
- [Preview support boundary](docs/preview-support.md) for host, filesystem,
  concurrency, and trust assumptions.
- [SEMANTICS.md](SEMANTICS.md) for the normative soundness rules.

The package payload is unsigned. Release trust is based on exact package and
assembly identity, pinned container inputs, and tested byte-promotion evidence.
Semantic payload hashes remain inside the proof protocol where they bind
compiler and worker evidence. The preview is not production-ready; protected
release environments, package publication, pilot evidence, and the exact
candidate release run remain owner-controlled work.

Before publication, every main package must be absent from the destination.
Main and symbol packages are pushed without duplicate skipping; any collision
or partial publication fails closed and requires a new version.

## Policies

- [Contributing](CONTRIBUTING.md)
- [Security](SECURITY.md)
- [Changelog](CHANGELOG.md)
- [Release and acceptance evidence](eng/acceptance/README.md)

When source behavior changes, update the source-owned catalog or schema first,
then update the relevant reference page. Run
scripts/Test-SharpProofReadme.ps1 to check versions, configuration,
diagnostics, API IDs, worker properties, protocol enums, links, anchors,
parseable XML and PowerShell fences, line endings, and BOM policy. SARIF and
other generated projections remain machine-owned and should be regenerated by
their owning scripts rather than hand-edited.
