# 1. Your first contract

A useful first contract documents an input requirement and lets the analyzer check calls whose arguments it can establish. Here the method accepts positive integers, and a separate closed attribute constrains a range.

## Set up a consumer

Active analysis requires .NET SDK 9.0.300 or newer. The canonical container supplies the pinned SDK; the sample application can still target net8.0. SDK version and target framework are separate choices.

For a new class library, use this complete project file. For an existing SDK-style project, merge its references and SharpProof properties with your current settings:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <SharpProofProfile>advisory</SharpProofProfile>
    <SharpProofFeatures>all</SharpProofFeatures>
    <SharpProofVerify>false</SharpProofVerify>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpProof.Attributes" Version="1.0.0-preview.1" />
    <PackageReference Include="SharpProof" Version="1.0.0-preview.1" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

For this checkout, configure a local feed containing the packed packages. The [series runner](README.md#run-the-entire-series) handles that setup and builds this example as a real package consumer. Applications use the supported Attributes API; analyzer implementations are tool payloads.

Save the C# below as `FirstContract.cs` beside the project. Restore with your local feed configured, then build with `dotnet build -c Release`. The runner performs those same restore/build steps automatically with an isolated package cache. See [obtaining packages](../getting-started.md#obtain-this-checkouts-packages) for the clean-checkout packing command and exported feed location.

## Declare and call the methods

```csharp
using SharpProof.Attributes;
public static class FirstContract
{
    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
    public static int GoodCall() => Positive(1);
    public static int ClosedRange([InRange(0, 10)] int value) => value;
    public static int GoodRange() => ClosedRange(5);
}
```

`Requires(value > 0)` belongs at the beginning of the body, before executable statements. It is an obligation for a caller and an entry condition when the callee is verified. `GoodCall` supplies a literal 1, so the portable analyzer can establish that this call does not violate the requirement.

`[InRange(0, 10)]` is inclusive. A literal 5 satisfies it. You can also use `[Positive]` for greater than zero and `[NotNull]` for a reference-capable value. Parameter attributes describe incoming values; return attributes describe normal return. `out` parameters have no incoming value and cannot carry these entry contracts.

## What this run establishes

This lesson uses the portable analyzer without the native worker. A clean build means no emitted diagnostic failed that build. It does not constitute a worker Proven result or establish every possible call to `Positive`.

SharpProof does not insert runtime guard code. If an unverified caller can pass invalid data at runtime, keep a normal runtime check or validation layer where required. Do not define the reserved `SHARPPROOF_CONTRACTS` symbol.

## Try a failure

Change `Positive(1)` to `Positive(0)`. The predicate is concretely false, which is the situation for SP0027. Changing an argument to an unknown value does not automatically justify a violation diagnostic.

The next lesson makes this change and captures the actual diagnostic. When editing checked-in tutorial inputs, the runner intentionally fails if the expected outcomes no longer match; restore the example or review its assertions before treating a changed result as expected.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[01-first-contract] build exit: 0
```

## Continue

[Next: catch mistakes while compiling](02-diagnostics.md). Reference: [closed contract rules](../public-api.md#closed-parameter-and-return-contracts).
