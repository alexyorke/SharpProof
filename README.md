# SharpProof

SharpProof is a Roslyn analyzer and bounded symbolic-analysis library for C#.

The analyzer now computes one composable `MethodEffects` result per method. Purity, allocation freedom, capability compliance, and exception contracts are projections over those facts rather than separate analysis engines.

## Contracts

```csharp
using SharpProof.Attributes;

sealed class Example {
    [EnforcePure]
    public int Add(int left, int right) => left + right;

    [ZeroAllocations]
    public int Twice(int value) => value * 2;

    [AllowedCapabilities(SharpProofCapability.Synchronization)]
    public void Guarded() {
        lock (this) { }
    }

    [EffectContract(
        SharpProofEffect.ReadsAmbientState,
        Complete = true,
        IsDeterministic = true)]
    public static extern int ReadExternalState();
}
```

`[EnforcePure]` is the only purity-facing attribute. Fresh allocation and deterministic exceptions do not by themselves make a method observably impure. Writes to pre-existing reachable state, ambient reads, I/O, synchronization, native interaction, nondeterminism, capabilities, and unresolved transitive effects prevent a proven-pure verdict.

`[EffectContract]` is the generic trusted-boundary contract. It can declare primitive effects, capabilities, exception types, determinism, and completeness. Assembly-level contracts additionally identify an external method using its exact canonical structural key.

## .NET API

```csharp
using SharpProof.Symbolic;

using var session = SharpProofAnalysisSession.FromText(source);
var result = session.Analyze(new SharpProofAnalysisRequest(
    new SharpProofTarget(SharpProofTargetKind.Line, Line: 12),
    SharpProofAnalysisFacet.All,
    Condition: "value >= 0"));

Console.WriteLine(result.MethodEffects?.Purity);
Console.WriteLine(result.MethodEffects?.Effects);
```

The canonical entry point is:

```text
SharpProofAnalysisSession.Analyze(SharpProofAnalysisRequest)
    -> SharpProofAnalysisResult
```

The result contains method effects and their derived three-state verdicts, compact proof facts and counterexamples, runtime hazards, complexity, unified unknown reasons, truncations, and structured errors. Full Z3 models and synthesized input domains remain internal analysis evidence rather than default JSON payloads.

Every session creates a bounded Z3 service. Path feasibility, conditions, contracts, nullable reasoning, exception escape, and runtime hazards use Z3 whenever their formulas are translatable. Unsupported translations, solver failures, timeouts, cancellation, incomplete CFG data, and exhausted budgets produce `Unknown`; there is no public switch that bypasses SMT.

## CLI

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- analyze `
  --file Example.cs --target line:12 --facets effects,proofs,hazards,complexity `
  --format json
```

The only exit gates are `--fail-on-unknown` and `--fail-on-disproven`. Exit codes are 0 for accepted analysis, 2 for usage/input errors, 3 for analysis failures, 4 for the unknown gate, and 5 for the disproven gate. Old mode-specific query flags and aliases are unsupported.

## Metadata analysis

Referenced methods are inspected lazily from the exact compilation reference. Metadata results are cached in memory by module MVID, method token, and generic context. Analysis is bounded by call depth, visited-method count, and IL-instruction count. Missing paths, missing bodies, malformed IL, unresolved dispatch, recursion, and exhausted budgets produce explicit unknown evidence.

SharpProof does not generate or consume effect-summary JSON, scan whole assemblies eagerly, write disk caches, or fall back to namespace/type/member-name purity catalogs.

## Build and test

Use the repository wrapper so .NET processes remain inside the configured Windows Job Object:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 build SharpProof.sln --configuration Release
.\scripts\Invoke-SharpProofDotnet.ps1 test SharpProof.sln --configuration Release --no-build
```

The repository contains the analyzer, attributes, symbolic API, CLI, NuGet packaging, fuzzing, and net472 smoke project.

See [the supported modern C# surface](docs/modern-csharp-surface.md) for language-version coverage.
