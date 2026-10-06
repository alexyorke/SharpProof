# 3. Prove four effect contracts

A tiny method can demonstrate four independent properties: observable purity, zero managed allocations, no escaping exceptions, and no ambient capabilities. This lesson asks the native worker to establish all four.

## Enable full verification

Add Verifier privately to the lesson 1 package references. Inside the canonical container, use:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof.Verifier" Version="1.0.0-preview.1" PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofVerify>true</SharpProofVerify>
  <SharpProofVerifyPolicy>require-proven</SharpProofVerifyPolicy>
  <SharpProofAssumptionPolicy>warn</SharpProofAssumptionPolicy>
</PropertyGroup>
```

The tutorial uses advisory analyzer configuration with an explicit require-proven worker policy. `strict` is the alternative profile that requires worker verification and defaults to require-proven plus assumption policy error. These controls are separate.

Build with `dotnet build -c Release` in that container. Verification runs through MSBuild after compilation; you do not invoke Z3 yourself. To retain machine-readable reports at explicit paths, add:

```xml
<PropertyGroup>
  <SharpProofVerifyResultFile>$(MSBuildProjectDirectory)/artifacts/sharpproof/result.json</SharpProofVerifyResultFile>
  <SharpProofVerifySarifFile>$(MSBuildProjectDirectory)/artifacts/sharpproof/result.sarif</SharpProofVerifySarifFile>
</PropertyGroup>
```

Archive those files with your CI build artifacts. Avoid having multiple builds write the same report path concurrently. The tutorial runner gives each consumer separate paths and copies its reports into the run's evidence directory.

## State four claims

```csharp
using SharpProof.Attributes;
public static class Effects
{
    [EnforcePure]
    [ZeroAllocations]
    [DoesNotThrow]
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Identity(int value) => value;
}
```

| Attribute | Claim |
| --- | --- |
| `EnforcePure` | No modeled observable impurity |
| `ZeroAllocations` | No modeled managed allocation |
| `DoesNotThrow` | No modeled escaping exception |
| `AllowedCapabilities(None)` | No ambient capability use outside the empty set |

Returning the scalar input needs no managed allocation, nonlocal write, external call, or throwing operation. The worker records separate claim results rather than one generic "safe method" label.

The result is about this body and each selected property. It does not establish performance, confidentiality, thread safety, or a useful business postcondition. You can add a postcondition when you need to relate return values to inputs.

## Explore a change

Adding `new object()` changes allocation behavior. Writing a static field changes purity. Adding checked arithmetic can introduce overflow. The analyzer can flag may-effects, while a native Refuted result needs replayable violating evidence.

An allowed effect does not imply another allowed effect. For a complete `EffectContract`, allowing Throws does not also allow Allocates. Similarly, permitting one capability does not permit all I/O or nondeterminism.

## Inspect the records

The runner saves native JSON and SARIF. Effect certainty `CompleteMayEffectSummary` distinguishes checked body evidence from `TrustedCompleteBoundary`, which is a reviewed external declaration. Lesson 8 explores the latter.

An Unknown effect result is not partial success. Require-proven rejects incomplete selected coverage. Under advisory or warn-on-unknown, incomplete coverage can be reported without failing the build; Refuted remains an error.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[03-effects] build exit: 0
  run: Complete; failure: None
  M:Effects.Identity(System.Int32)~System.Int32 | Effect/AllowedCapabilities | Proven | None | CompleteMayEffectSummary | None
  M:Effects.Identity(System.Int32)~System.Int32 | Effect/DoesNotThrow | Proven | None | CompleteMayEffectSummary | None
  M:Effects.Identity(System.Int32)~System.Int32 | Effect/EnforcePure | Proven | None | CompleteMayEffectSummary | None
  M:Effects.Identity(System.Int32)~System.Int32 | Effect/ZeroAllocations | Proven | None | CompleteMayEffectSummary | None
```

## Continue

[Previous: diagnostics](02-diagnostics.md) | [Next: decision logic](04-branching.md). Reference: [effect semantics](../../SEMANTICS.md#effects).
