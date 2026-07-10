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

When the source belongs to a restored project, add `--project` or `--solution`
so references, compiler settings, analyzer configuration, baselines, and effect
summaries come from the build. In project-aware `explain`, SharpProof also runs
the analyzer and prints the diagnostics that survive configured severity and
baseline suppression. See [project-aware proof queries](project-aware-queries.md).

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

For a compilation loaded by a Roslyn workspace, use
`SymbolicProjectQueryContext`. It creates a source input over the existing
project compilation and decodes the same global SMT and bounded-analysis
options used by the analyzer.

Public result objects expose source-like facts, proof outcomes, SMT diagnostics,
and unknown reasons. Raw SMT terms are not the primary public abstraction.

Bounded fact and state merges expose `AnalysisTruncation` instead of silently
discarding proof quality. The analyzer, API, CLI, defaults, event codes, and
override names are documented in [bounded analysis limits](analysis-limits.md).

Long-running hosts can inspect solver health, retry transient Z3 failures, and
recycle thread-local contexts without clearing proof-result caches. See
[SMT lifecycle and health](smt-lifecycle.md).

Reachability, implication, and runtime-hazard results also expose concrete
solver assignments and conservative input-domain summaries. See
[solver witnesses and input domains](input-witnesses.md) for the status model,
query properties, aggregation rules, and full-JSON behavior.

Unknown and unsupported results carry stable cross-family codes in addition to
their existing enums and raw reason strings. See the
[unknown-reason taxonomy](unknown-reasons.md) before branching on solver,
capability, complexity, hazard, purity, or contract failures.

Nullness proofs use the same Roslyn flow state and CodeAnalysis attribute
contracts across condition, reachability, runtime-hazard, purity, and
`[Ensures]` queries. See [shared nullable-flow facts](nullable-flow-facts.md)
for precedence, mutation invalidation, and trust boundaries.

Every current query family now exposes its compact projection from the public
library, not from CLI-only model classes:

- invariant point, line, span, and file results use their existing
  `ToCompactResult(...)` and `ToInvariantQueryResult(...)` methods
- `SymbolicCapabilityResult.ToCompactResult()` returns
  `SymbolicCompactCapabilityResult`
- `SymbolicComplexityResult.ToCompactResult()` returns
  `SymbolicCompactComplexityResult`
- `SymbolicRuntimeHazardQueryResult.ToCompactResult(...)` returns
  `SymbolicCompactRuntimeHazardQueryResult`; use
  `SymbolicCompactRuntimeHazardQueryOptions` to bound hazards and path
  conditions while retaining untruncated totals

All of these implement `ISymbolicCompactResult`, which pins `Kind`, the
format-specific `SchemaVersion`, and the shared evidence schema fields. New
compact query families, including a future machine-readable `explain` result,
should implement that same interface. Serialize with lower-camel-case property
names and string enums to match CLI `--compact-json` output.

The package ships `SharpProof.Symbolic.dll` as a `lib/netstandard2.0` asset with
XML documentation, nullable annotations, and portable PDBs containing Source
Link metadata. The packaged `samples/SharpProof.Symbolic` console project shows
the minimal source-text query workflow. `SearchLib.dll` is bundled only as a
runtime implementation dependency; consumers should build against the
`SharpProof.Symbolic` namespace instead of referencing `SearchLib` directly.

## Standalone Compilation Profiles

File and source-text queries can intentionally match the compiler settings of
the source they inspect. Create a `SymbolicSourceCompilationProfile` and attach
it to `SymbolicSourceInput`; the same profile flows through invariant, proof,
runtime-hazard, capability, and complexity queries:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Symbolic;

var profile = new SymbolicSourceCompilationProfile(
    languageVersion: LanguageVersion.CSharp12,
    preprocessorSymbols: new[] { "FEATURE_X" },
    nullableContext: NullableContextOptions.Enable,
    allowUnsafe: true,
    documentationMode: DocumentationMode.Diagnose,
    platform: Platform.X64,
    optimizationLevel: OptimizationLevel.Release,
    assemblyName: "Example.Analysis");

var input = SymbolicSourceInput.FromTextWithProfile(source, profile, "Example.cs");
var result = new SymbolicQueryService().Query(
    new SymbolicQueryRequest(input, SymbolicQueryTarget.AllLines()));
```

The CLI exposes the same settings:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --all-lines --language-version 12 --define FEATURE_X --nullable enable --allow-unsafe --documentation-mode diagnose --platform x64 --optimization release --assembly-name Example.Analysis --compact-json
```

| Profile setting | CLI option | Default |
| --- | --- | --- |
| C# language version | `--language-version` | `preview` |
| Preprocessor symbols | repeated `--define` | none |
| Nullable context | `--nullable` | `disable` |
| Unsafe allowance | `--allow-unsafe` | off |
| Documentation mode | `--documentation-mode` | `parse` |
| Platform | `--platform` | `AnyCpu` |
| Optimization | `--optimization` | `debug` |
| Assembly identity name | `--assembly-name` | query-mode default |

Profiles apply only when SharpProof creates a standalone compilation for file
or text input. `FromSyntaxTree(...)` and `FromNode(...)` retain the caller's
existing Roslyn parse and compilation options; SharpProof does not overwrite
them. Metadata references remain controlled separately through
`SymbolicQueryOptions` or repeated CLI `--reference` values.

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
Fact-collection and state-merge limit hits are separately visible through
stable `analysis_limit.*` events.
