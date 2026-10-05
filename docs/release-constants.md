# Release constants and ownership

SharpProof keeps exact values only when they have a named owner and a reason.
This classification prevents both accidental duplication and the opposite
mistake of parameterizing values whose purpose is to detect drift.

## Intentional release pins

These remain exact. Changing one is a reviewed compatibility, soundness, or
release action rather than routine configuration.

| Pin | Owner | Enforcement |
|---|---|---|
| Package and assembly version | `SharpProof.Release.props` | package and README checks |
| Worker protocol, manifest, and cache schemas | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs` | worker and package tests |
| Compiler-artifact schema | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs` | worker and package tests |
| Supported target frameworks and host boundary | `eng/acceptance/contract.json` and `docs/preview-support.md` | acceptance and packaged-host tests |
| Trusted kernel paths | `eng/acceptance/contract.json` | project ownership and compiler access checks |
| Corpus outcomes and wall-time ceiling | `SharpProof.Gates/Corpus/` | canonical snapshot comparison and a five-minute gate limit |

## Behavioral defaults

The hand-maintained C# `WorkerBudgets`, `WorkerCacheOptions`, and
`WorkerLauncherDefaults` own worker budget, cache, and launcher defaults. The
verifier MSBuild defaults live in the package props companion; the acceptance
contract and documentation mirror those values for validation and explanation.
Package and worker checks require exact parity. This covers
query/method limits, wall times, parallelism, expression depth, termination
grace, and cache defaults.

Portable profile, feature, verification-policy, and assumption-policy defaults
are owned by the package props/targets and mirrored in the acceptance contract.
The acceptance script reads the MSBuild XML and rejects drift.

## Derived measurements

Package layouts and test counts are computed from the current build. They are
never copied into production behavior.

The C# files with `.generated.cs` names are hand-maintained tables and models.
Update them directly and run the relevant semantic and package tests.
