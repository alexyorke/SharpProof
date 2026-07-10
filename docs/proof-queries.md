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

Install the supported public library package:

```powershell
dotnet add package SharpProof.Symbolic --version 0.1.0-preview.1
```

Use `SymbolicQueryService` as the public entrypoint:

- `Query(...)` for invariants, reachability, and implication checks
- `QueryRuntimeHazards(...)` for bounded runtime-hazard candidates
- `QueryCapabilities(...)` for method capability summaries
- `QueryComplexity(...)` for conservative method complexity

Public result objects expose source-like facts, proof outcomes, SMT diagnostics,
and unknown reasons. Raw SMT terms are not the primary public abstraction.

The package ships `SharpProof.Symbolic.dll` as a `lib/netstandard2.0` asset with
XML documentation, nullable annotations, and portable PDBs containing Source
Link metadata. The packaged `samples/SharpProof.Symbolic` console project shows
the minimal source-text query workflow. `SearchLib.dll` is bundled only as a
runtime implementation dependency; consumers should build against the
`SharpProof.Symbolic` namespace instead of referencing `SearchLib` directly.

## Compatibility Baselines

`SharpProof.Symbolic/PublicAPI.Shipped.txt` is the supported API baseline.
Builds fail when a shipped API is removed or changed, or when a new public API
is added without being recorded. During development, intentional additions go
in `PublicAPI.Unshipped.txt`; release preparation promotes them to the shipped
baseline.

`SharpProof.Symbolic/PackageBaseline.json` records the package identity,
version, dependencies, target framework, and required assets. Packaging tests
compare both the project and built `.nupkg` against it, then restore the package
into a disposable console application and run the packaged sample. Intentional
package-contract changes therefore require an explicit baseline and version
review.

## Evidence Policy

Query results are bounded. Unsupported syntax, unknown external calls, SMT-off
mode, solver timeout, cancellation, native-load failure, and budget exhaustion
must stay visible as unknown, unsupported, unproven, or conservative results.
