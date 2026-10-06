# Getting started

SharpProof offers portable analysis and optional full verification in the canonical Linux amd64 container. Read the [preview support boundary](preview-support.md) before enabling the verifier.

## Obtain this checkout's packages

Active analyzer builds require .NET SDK 9.0.300 or newer (Roslyn 4.14 or newer). The compiler SDK is separate from the application's target framework. Full verification additionally requires the canonical container, which supplies the pinned SDK and native dependencies.

From a clean, committed repository checkout:

```text
docker compose build tooling
docker compose run --rm tooling pack -Configuration Release
```

Packed artifacts are exported under `artifacts/container-packages`. Configure the consumer's NuGet sources to include that local feed; package references alone do not configure a feed. Paths must identify the feed inside the consumer's build environment. The pinned package version comes from [SharpProof.Release.props](../SharpProof.Release.props); the current value is `1.0.0-preview.1`.

`pack` rejects tracked modifications and untracked source files. For development validation of an uncommitted change, use the sample harness below, which builds its isolated feed without the exact-commit release entrypoint.

## Add package references

Add these items and properties to an SDK-style consumer project:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof.Attributes" Version="1.0.0-preview.1" />
  <PackageReference Include="SharpProof" Version="1.0.0-preview.1" PrivateAssets="all" />
  <PackageReference Include="SharpProof.Verifier" Version="1.0.0-preview.1" PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <SharpProofProfile>strict</SharpProofProfile>
  <SharpProofVerifyPolicy>require-proven</SharpProofVerifyPolicy>
</PropertyGroup>
```

The verifier reference is needed only for full verification. Build that consumer inside the canonical container. For a reproducible package-backed example, run:

```text
docker compose run --rm tooling samples -Configuration Release
```

The sample harness packs an isolated feed and checks builds, diagnostics, and claim records.

## Write a contract

```csharp
using SharpProof.Attributes;

public static class Counter
{
    public static int Next(int value)
    {
        Contract.Requires(value >= 0 && value < int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }
}
```

`Requires` defines the caller's obligation and the callee's entry domain. `Ensures` describes normal return. `Old` refers to entry state, and `Result` refers to the normal return value. Place direct clauses contiguously before executable body statements.

Do not define `SHARPPROOF_CONTRACTS`. Clause methods are compiler-elided static declarations, not runtime checks. Their emitted bodies do not enforce conditions, and the expression intrinsics throw when called at runtime.

## Select analysis behavior

| Setting | Values | Meaning |
| --- | --- | --- |
| `SharpProofProfile` | `advisory`, `strict`, `off` | Package/analyzer activation and build defaults |
| `SharpProofFeatures` | `effects`, `contracts`, `all` | Analyzer features and compiler-artifact claim selection |
| `SharpProofVerifyPolicy` | `advisory`, `warn-on-unknown`, `require-proven` | Worker result policy |
| `SharpProofAssumptionPolicy` | `allow`, `warn`, `error` | Treatment of declared user assumptions and trusted boundaries |

The portable default is advisory with all features. Profiles and verifier policies are separate controls: turning diagnostics off does not constitute a proof, and suppressing a diagnostic does not establish a claim. See package props/targets and [public API](public-api.md) for trust controls.

`strict` requires `SharpProofVerify=true` and defaults to `require-proven` with assumption policy `error`. To run the worker under the advisory profile, explicitly set `SharpProofVerify=true` and reference Verifier. `off` disables package analysis and verification; design-time builds do not run the worker.

Verifier policy controls incomplete selected coverage: advisory reports information, warn-on-unknown reports warnings, and require-proven rejects it. This includes timeouts attributed to selected coverage. A Refuted claim is always an error, and other failed runs remain failures. Assumption policy checks declared user assumptions/trusted boundaries, including declarations not marked as used by a particular proof.

## Inspect the result

A worker run reports selected callables and claims, including outcome, reason, assumptions, and vacuity. `Proven` requires complete modeled evidence, `Refuted` requires a replayable counterexample or effect violation, and `Unknown` states why the worker could not decide.

Inside the consumer build workspace, default published files are `request.json`, `result.json`, and `compiler-manifest.json` beneath the intermediate `SharpProof` directory, normally `obj/<Configuration>/<TargetFramework>/SharpProof`. Use a persistent container workspace to inspect those files after a build; ordinary finite task workspaces are disposable. `SharpProofVerifySarifFile` enables optional SARIF output.

The normal-return part of a postcondition says nothing about termination. Contradictory entry conditions and absence of modeled normal returns are recorded separately. Always inspect those records when interpreting a proof.

Use [diagnostics](diagnostic-examples.md), [Unknown reasons](unknown-reasons.md), and [coverage and limits](coverage-and-limits.md) to investigate results. Add only conditions justified by your application's actual inputs; `Assume` and trusted boundaries are proof assumptions.

## Next steps

Follow the [progressive tutorial series](tutorials/README.md) for complete examples with captured output, from portable checks through native proof and trusted boundaries. Explore the [samples](../samples/README.md), then run the relevant targeted tests and [container gates](container-development.md) for changes to SharpProof itself.
