# SharpProof Proof Queries

The symbolic CLI and .NET API are the inspection layer for analyzer results.
They answer point-in-code questions without executing user code.

## CLI Workflow

Start with `explain` when you want a compact overview for a line or source
position:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42
```

`explain` summarizes:

- the nearest invariant query result
- reachability status
- implication proof summaries when supplied with `--implies`
- runtime hazards on the selected line
- containing-method capability summary
- containing-method complexity summary

Use focused modes when you need a specific machine-readable answer:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --line 42 --runtime-hazards
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --line 42 --capabilities --json
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --line 42 --complexity --compact-json
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --line 42 --check-reachability --implies "value > 0"
```

## .NET API Workflow

Use `SymbolicQueryService` as the public entrypoint:

- `Query(...)` for invariants, reachability, and implication checks
- `QueryRuntimeHazards(...)` for bounded runtime-hazard candidates
- `QueryCapabilities(...)` for method capability summaries
- `QueryComplexity(...)` for conservative method complexity

Public result objects expose source-like facts, proof outcomes, SMT diagnostics,
and unknown reasons. Raw SMT terms are not the primary public abstraction.

## Evidence Policy

Query results are bounded. Unsupported syntax, unknown external calls, SMT-off
mode, solver timeout, cancellation, native-load failure, and budget exhaustion
must stay visible as unknown, unsupported, unproven, or conservative results.
