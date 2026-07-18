# Capability Analysis

SharpProof exposes a conservative capability-analysis surface for both
attribute-driven diagnostics and ad hoc symbolic queries.

## Analyzer Contracts

Use `[AllowedCapabilities(...)]` on methods, local functions, accessors,
constructors, and operators to state which proven capability categories are
allowed:

```csharp
using SharpProof.Attributes;

public static class Example
{
    [AllowedCapabilities(SharpProofCapability.Console)]
    public static void Log(string message)
    {
        Console.WriteLine(message);
    }
}
```

Current categories are:

- `None`
- `IO`
- `FileRead`
- `FileWrite`
- `Network`
- `Console`
- `Process`
- `Environment`
- `Registry`
- `Clock`
- `Randomness`
- `Reflection`
- `Synchronization`
- `NativeInterop`

`IO` is a derived umbrella capability. File and network classifications imply
it automatically.

Diagnostics:

- `SP0015`: a concrete operation or proven transitive source callee exceeds the
  declared capability set.
- `SP0016`: the analyzer could not verify capability requirements
  conservatively because the operation was dynamic, external, unsupported, or
  otherwise unresolved.
- `SP0017`: `[AllowedCapabilities]` was applied to a non-method declaration.

The current implementation is intentionally conservative. Unsupported
reflection-heavy flows, dynamic dispatch, missing metadata classification, or
unknown external behavior do not count as success.

## Library API

`SharpProof.Symbolic` exposes capability queries through
`SharpProofAnalysisSession.Analyze(SharpProofQuery.Capabilities(...))`.

Supported source shapes:

- `SharpProofAnalysisSession.FromFile(...)`
- `SharpProofAnalysisSession.FromText(...)`
- project-loaded sessions created by `SymbolicProjectQueryContext`

Supported target shapes:

- `SymbolicQueryTarget.Point(...)`
- `SymbolicQueryTarget.Position(...)`
- `SymbolicQueryTarget.Line(...)`
- `SymbolicQueryTarget.Node(...)`

Capability queries resolve the containing method-like body and return:

- merged `Capabilities`
- display-friendly `CapabilityText`
- per-site evidence in `Sites`
- conservative `UnknownReasons`
- method and span metadata

Invalid source/target combinations are API misuse and currently throw
`NotSupportedException`.

## CLI

The symbolic CLI exposes the same classification through `--capabilities`:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --capabilities
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --capabilities --json
```

Current CLI target support is intentionally narrow:

- `--line`
- `--line` with `--column`
- `--position`

Invalid combinations, such as `--capabilities --all-lines`, currently fail with
an argument error instead of attempting a best-effort aggregate result.

## Limitations

- This is not OS-syscall tracing. It is analyzer-level capability classification
  over proven source operations, transitive source callees, effect summaries,
  and existing analyzer evidence.
- Unknown metadata behavior stays conservative.
- Capability queries do not currently aggregate whole-file results.
- The analyzer does not attempt to prove that a method is permanently free of a
  capability under all future callee implementations unless the current
  evidence supports that claim.
