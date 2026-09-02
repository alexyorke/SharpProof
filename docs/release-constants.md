# Release constants and ownership

SharpProof keeps exact values only when they have a named owner and a reason.
This classification prevents both accidental duplication and the opposite
mistake of parameterizing values whose purpose is to detect drift.

## Intentional release pins

These remain exact. Changing one is a reviewed compatibility, soundness, or
release action rather than routine configuration.

| Pin | Owner | Enforcement |
|---|---|---|
| Package and assembly version | `SharpProof.Release.props` | package, README, release-manifest, tag, and promotion checks |
| Worker protocol, manifest, and cache schemas | `SharpProof.Worker.Protocol/ProtocolModel.schema.json` | generated model plus acceptance/package parity checks |
| Compiler-artifact schema | `SharpProof.CompilerArtifact/CompilerArtifactModel.schema.json` | generated model plus acceptance/package parity checks |
| Supported target frameworks and host boundary | `eng/acceptance/contract.json` and `docs/preview-support.md` | acceptance and packaged-host tests |
| TCB paths and mutation catalog | `eng/acceptance/contract.json` | exact path/count ownership and deterministic mutation gates |
| Corpus, fuzz, performance, and complexity ceilings | `eng/acceptance/contract.json` | acceptance scripts; complexity changes require `ceilingRationale` |

## Behavioral defaults

The protocol schema owns worker budget, cache, and launcher defaults. Generated
`WorkerBudgets`, `WorkerCacheOptions`, and `WorkerLauncherDefaults` are the
compiled projection. Verifier MSBuild properties, the acceptance contract, and
documentation repeat user-visible values only where the format cannot consume
the C# constants; package, worker, acceptance, and README checks require exact
parity. This covers query/method limits, wall times, parallelism, expression
depth, parallelism, termination grace, and cache defaults.

Portable profile, feature, verification-policy, and assumption-policy defaults
are owned by the package props/targets and mirrored in the acceptance contract.
The acceptance script reads the MSBuild XML and rejects drift.

## Derived measurements

Source complexity, TCB inventory structure, generated-output shape, package
layout, SBOM structure, and release evidence are computed. They are never
copied into production behavior. Checked-in ratchets are reviewed upper bounds
or expected structural values, and their scripts recompute the current value
before a gate can pass.

Generated files must be changed through their owning schema/catalog generator.
Do not hand-edit generated protocol, compiler-artifact, launcher-argument,
diagnostic, IR, or projection code.
