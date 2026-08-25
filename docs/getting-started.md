# Getting started

This is the task-oriented setup guide for SharpProof 1.0.0-preview.1. Use
[docs/README.md](README.md) to choose deeper references, and use
[coverage and limits](coverage-and-limits.md) when you need the exact
implemented boundary.

## Choose the package shape

SharpProof is delivered as three packages:

| Package | Add it to | Purpose |
|---|---|---|
| SharpProof.Attributes | Libraries that publish annotations | The netstandard2.0 compile-time contract API |
| SharpProof | Development builds | Portable analyzer, generator, and build-transitive configuration |
| SharpProof.Verifier | Strict CI builds | Container-only worker, launcher, build tasks, and native payload |

The packages must use the same preview version. The first public NuGet
publication is still pending, so repository samples create a local feed from
the exact package graph.

For a library or application using annotations:

~~~xml
<ItemGroup>
  <PackageReference Include="SharpProof.Attributes"
                    Version="1.0.0-preview.1" />
  <PackageReference Include="SharpProof"
                    Version="1.0.0-preview.1"
                    PrivateAssets="all" />
</ItemGroup>
~~~

The analyzer package adds no compile-time assembly reference. Keep it private
so consumers receive only the contract API and its IntelliSense XML.

## Select analysis

The default profile is advisory and the default feature selection is all:

~~~xml
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofFeatures>all</SharpProofFeatures>
</PropertyGroup>
~~~

SharpProofProfile accepts advisory, strict, and off. SharpProofFeatures accepts
effects, contracts, and all. The analyzer configuration equivalents are
sharpproof_profile and sharpproof_features:

~~~ini
[*.cs]
sharpproof_profile = advisory
sharpproof_features = all
~~~

Advisory analysis keeps unannotated code quiet. Explicitly selected unsupported
code remains visible as an incomplete-analysis diagnostic. Set the profile to
off when an older host must consume only the contract API.

## Add a contract

The supported clause methods are direct, contiguous prologue statements:

~~~csharp
using SharpProof.Attributes;

public static class Calculator
{
    public static int ClampNonNegative(int value)
    {
        Contract.Requires(value >= 0);
        Contract.Ensures(Contract.Result<int>() >= 0);
        return value;
    }
}
~~~

The public API also includes closed NotNull, Positive, and InRange attributes,
effect contracts, and compiler-bound ContractFor companions. See
[Supported public API](public-api.md) for exact signatures and trust rules.

Do not define SHARPPROOF_CONTRACTS in a build analyzed by SharpProof. The
conditional contract methods are intended to disappear from the emitted
program; enabling the runtime-contract symbol is rejected when it would make
the compiler artifact unsound.

## Enable strict verification

Strict verification is a separate package-consumer concern. Add the verifier
package privately and set the worker policies explicitly:

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

SharpProofVerify=true requests compiler artifact collection and launches the
SharpProof.Worker. Strict profile defaults are require-proven and error, but
explicit properties make the CI contract easy to audit. The verify policy
values are advisory, warn-on-unknown, and require-proven. The assumption policy
values are allow, warn, and error.

The verifier runs only in the canonical Linux amd64 container. The portable
analyzer can run on other operating systems, but native verifier execution,
Visual Studio verifier execution, Rider integration, and ARM64 qualification
are outside this preview.

## Try the packaged samples

From a checkout containing compose.yaml:

~~~text
docker compose build tooling
docker compose run --rm tooling samples -Configuration Release
~~~

The sample matrix restores from an isolated local feed and checks effects,
preconditions, ContractFor, trusted boundaries, strict-library behavior,
Proven, Refuted, Unknown, expected diagnostics, and unsupported-host policy.
See [samples/README.md](../samples/README.md) for the project-by-project
matrix.

## Develop the repository

Use the persistent development container for repeated edits:

~~~text
docker compose up -d dev
docker compose exec dev sharpproof-dev-init
docker compose exec dev bash
sp test-changed
sp check
~~~

Use disposable tooling commands for clean qualification:

~~~text
docker compose run --rm tooling build
docker compose run --rm tooling test
docker compose run --rm tooling acceptance -Configuration Release
~~~

The [container development guide](container-development.md) explains Compose
project isolation, resource limits, test targets, and the difference between
the persistent workspace and disposable task containers.

## Read the result

The worker emits Proven only after the proof kernel accepts the closed model
and evidence. Refuted requires independent replay of the admitted
counterexample or allocation-effect witness. Unknown means that the bounded
language, model, evidence, or resource budget was insufficient. Diagnostic
silence is not a proof.

The most common diagnostics are SP0027 for a concrete precondition violation,
SP0047 for an explicitly selected unsupported callable, SP0048 for assumptions
or trusted evidence, and SP0049 for compiler-artifact collection failure. The
[diagnostic reference](diagnostic-examples.md) contains the full catalog and
configuration examples.

## Next references

- [Coverage and limits](coverage-and-limits.md)
- [Supported public API](public-api.md)
- [Diagnostics](diagnostic-examples.md)
- [Typed abstention reasons](unknown-reasons.md)
- [Preview support boundary](preview-support.md)
- [Normative semantics](../SEMANTICS.md)
