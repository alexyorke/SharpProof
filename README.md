# SharpProof

SharpProof 0.2.0-preview.1 is a soundness-first Roslyn analyzer for C# method
effects and compiler-bound preconditions. It reports only within an explicitly
enumerated language subset. Unsupported code, incomplete models, exhausted
budgets, and failed counterexample replay produce `Unknown` or no diagnostic;
they never become a proof.

The supported flagship is:

- `[EnforcePure]`
- `[ZeroAllocations]`
- `[AllowedCapabilities(...)]`
- `[DoesNotThrow]` and `[AllowedExceptions(...)]`
- call-site checking for `Contract.Requires(...)`

All feature diagnostics are Info and disabled by default. Configuration and
contract-usage errors remain enabled.

## Install and enable

Reference the preview package:

```xml
<PackageReference Include="SharpProof" Version="0.2.0-preview.1"
                  PrivateAssets="all" />
```

The package defaults to `off` and does not load its analyzer, so disabled
projects pay no analyzer-driver cost. Enable one compilation-global mode in
the project file:

```xml
<PropertyGroup>
  <SharpProofMode>effects</SharpProofMode>
</PropertyGroup>
```

Then opt individual Info diagnostics into the desired editor severity in
`.editorconfig`:

```ini
dotnet_diagnostic.SP0002.severity = suggestion
dotnet_diagnostic.SP0045.severity = suggestion
dotnet_diagnostic.SP0016.severity = suggestion
dotnet_diagnostic.SP0046.severity = suggestion
```

Valid `SharpProofMode` values are `off`, `effects`, `contracts`, and
`all-experimental`. The equivalent compilation-global `sharpproof_mode`
analyzer-config key remains supported when the analyzer is explicitly loaded
by a custom host.

## Effect contracts

```csharp
using SharpProof.Attributes;

public static class Example {
    [EnforcePure]
    public static int Add(int left, int right) => left + right;

    [ZeroAllocations]
    public static int Twice(int value) => value * 2;

    [AllowedCapabilities(SharpProofCapability.Synchronization)]
    public static void Guarded(object gate) {
        lock (gate) {
        }
    }

    [SharpProofTrusted("Reviewed external implementation contract.")]
    [EffectContract(
        SharpProofEffect.ReadsAmbientState,
        Complete = true,
        IsDeterministic = true)]
    public static extern int ReadExternalState();
}
```

The public effect enum is intentionally small. Internally, SharpProof tracks
regions, allocation, capabilities, exceptions, termination, completeness, and
typed uncertainty. Unmodeled metadata is unknown; SharpProof does not infer
effects by interpreting IL.

## Compiler-bound contracts

```csharp
using SharpProof.Attributes;

public static class Accounts {
    public static int Withdraw(int balance, int amount) {
        Contract.Requires(balance >= 0);
        Contract.Requires(amount > 0);
        Contract.Requires(amount <= balance);
        Contract.Ensures(
            Contract.Result<int>() ==
            Contract.Old(balance) - Contract.Old(amount));
        return balance - amount;
    }
}
```

Contract expressions bind as normal C# operations, so precedence, overloads,
shadowing, escaped identifiers, and generic substitution use compiler symbols
instead of reparsed strings. The analyzer performs only bounded, concretely
replayed call-site `Requires` checks. Deep `Ensures` proof runs only in the
out-of-process worker.

`Contract.Assume(...)`, `[SharpProofSuppress("reason")]`, and
`[SharpProofTrusted("reason")]` are explicit controls. Suppression changes
reporting only. Trust sharpens nothing unless an explicit complete contract is
also present. Free-form string contracts are not part of the v2 package. For a
one-time upgrade, install `SharpProof.Legacy.Attributes` and
`SharpProof.Migration`, apply the migration code fix, then remove both packages.
The legacy package also carries compile-only stubs for the removed complexity
annotation so existing code can build while it is deleted; it has no analyzer.

## Out-of-process verification

Set the opt-in MSBuild property:

```powershell
dotnet build /p:SharpProofVerify=true
```

The build-transitive target sends actual compile items and resolved reference
paths to `SharpProof.Worker`. The worker uses a versioned JSON protocol, one Z3
context per process, deterministic resource limits, bounded parallelism, and a
content-addressed cache under `obj/SharpProof/cache/v2`. Only hygienic
`Proven` and replay-validated `Refuted` outcomes are cached. Packaged worker
execution is Windows-only; Linux and macOS retain analyzer support. The IDE
analyzer contains no Z3 or native solver payload.

## Architecture and validation

The active implementation is split into typed IR, specs, dataflow, frontend,
contracts, effects, verification, SMT, analyzer, worker, meta-analyzers, and
testing layers. Architecture tests enforce the dependency DAG and semantic
boundaries.

Use the repository wrapper for .NET commands on Windows:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 restore SharpProof.sln
.\eng\acceptance\v2\Verify.ps1 -Configuration Release
```

The acceptance contract runs architecture and banned-API checks, lattice laws,
runtime spec witnesses, IR/C# and SMT differential oracles, counterexample and
unsat-core checks, analyzer corpus/metamorphic tests, worker/package smoke
tests, performance and cancellation gates, and a fixed-seed 1,000-case fuzz
gate.

See [SEMANTICS.md](SEMANTICS.md) and
[docs/architecture-v2.md](docs/architecture-v2.md) for the normative boundary.
The immutable `eng/acceptance/v1` tree is historical evidence only.
