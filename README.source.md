# SharpProof - Bounded Symbolic C# Analysis with Z3 SMT

SharpProof is a conservative .NET static analysis tool and C# Roslyn analyzer
for bounded symbolic C# analysis. It includes method purity checking,
side-effect detection, symbolic invariants, runtime-hazard and exception-flow
checks, build-time generated effect-summary data, Z3/SMT reasoning, code
fixes, attributes, and a standalone symbolic query library/CLI.

## Preview Status

SharpProof is still preview software. Treat the current branch and packages as
alpha/beta quality rather than production-hardened tooling.

The project has also been developed through rapid AI-assisted iteration, or
"vibe-coded" development in the informal sense: broad feature growth, fast
refactoring, and heavy test coverage, but not the kind of long-lived
stabilization, compatibility discipline, or external production mileage that a
mature analyzer platform would normally have.

That means you should expect rough edges:

- analyzer false positives and false negatives
- unsupported C# or library shapes that stay conservative or unknown
- public API, CLI, configuration, and diagnostic-surface changes between preview releases
- incomplete packaging/publication polish while the release process is still being finalized

The analyzer does not execute user code and does not attempt an unbounded
whole-program proof. When it cannot prove a fact within the implemented rules
and budgets, it stays conservative: purity falls back to `SP0002` for methods
marked pure, and SMT/exceptions fall back to unknown or no proof.

## Platform Direction

SharpProof is moving toward one bounded symbolic C# analysis platform, not a
set of unrelated analyzer rules and not only a purity analyzer. Purity is one
consumer of the shared platform. The intended spine is:

```text
Roslyn/C# -> Symbolic IR -> normalized symbolic state -> proof service -> Z3-backed conclusions -> analyzer/API/CLI outputs
```

Purity diagnostics, runtime-hazard detection, invariant queries, and SDK/BCL
effect-summary consumption should all flow through that shared pipeline.
`SharpProof.Symbolic.Ir` owns C#/Roslyn semantic facts, terms, atoms,
exceptional preconditions, ownership/freshness/mutation facts, type tests,
string/regex facts, and bounds. `SearchLib` remains the backend SMT/Z3 layer.
The analyzer should consume symbolic services instead of growing new local
path, reachability, hazard, or raw `SmtFormula` proof logic.

The long-term goal is maximum eligible Z3 usage through shared facts and proof
obligations, not direct translation of arbitrary C# into solver calls. Cheap
syntactic proofs can still short-circuit, but unresolved eligible obligations
should go through the bounded proof service with caching, cancellation, timeout
and method budgets, native-load fallback, and conservative unknown results.

## Naming Direction

The codebase now uses SharpProof across packages, namespaces, diagnostics,
configuration keys, scripts, effect-summary file conventions, and the GitHub
repository. The remaining old-name surface is historical only.

Suggested GitHub repository metadata:

- About: `Bounded symbolic C# analysis platform for purity, invariants, runtime hazards, ownership/resource facts, and Z3-backed proofs.`
- Topics: `csharp`, `roslyn-analyzer`, `static-analysis`, `symbolic-execution`, `smt`, `z3`, `purity`, `runtime-hazards`, `invariants`, `dotnet`, `sharpproof`.

## Current State

- Current package metadata: `SharpProof` `0.1.0-preview.1` and
  `SharpProof.Attributes` `0.1.0-preview.1`.
- Product positioning is broader than the analyzer package name: the analyzer
  is one delivery surface for the symbolic analysis platform.
- Analyzer and symbolic library target `netstandard2.0`; the symbolic CLI
  targets `net8.0`.
- Public diagnostics in active use are `SP0002` through `SP0017`.
- Z3 is packaged with the analyzer via `SharpProof.Symbolic.dll`,
  `SearchLib.dll`, `Microsoft.Z3.dll`, and native `libz3.dll`.
- Built-in effect summaries are regenerated into `obj` during build/test and
  embedded as analyzer resources. The repo should not depend on checked-in or
  preexisting `*.SharpProof.EffectSummary.json` outputs.
- Explicit user-supplied `SharpProof.EffectSummary.json` additional files are
  still supported for opt-in external summaries.

## Support Status Legend

`[x]` means the capability is implemented and regression-tested in this repo.
`[~]` means the capability exists but is bounded, partial, or intentionally
conservative. `[ ]` means it is not implemented or remains roadmap work.

Evidence references point to representative tests or source files. They are not
the entire test surface.

## Common Use Cases

- Enforce C# method purity with `[EnforcePure]` and `[Pure]` attributes.
- Enforce direct zero-allocation contracts with `[ZeroAllocations]`.
- Enforce proven capability contracts with `[AllowedCapabilities(...)]`.
- Find side effects, unknown external calls, mutable state access, and unsafe
  purity gaps during build or in the IDE.
- Use Z3 SMT solving to prove bounded path facts such as null guards, numeric
  ranges, branch reachability, string predicates, and regex constraints.
- Inspect symbolic facts, proof outcomes, unknown reasons, and runtime-hazard
  preconditions as first-class query results.
- Query symbolic invariants at a source line or syntax position from a
  standalone .NET library or CLI.
- Query conservative method capabilities and asymptotic complexity from the
  same symbolic library or CLI.
- Audit runtime-failure risks such as uncaught throws, divide-by-zero,
  null dereferences, and index hazards.
- Calibrate .NET SDK and BCL purity behavior with regenerated effect-summary
  data instead of checked-in generated artifacts.

## Quick Start

The package IDs and versions below are the intended public install surface, but
as of this preview branch neither `SharpProof` nor `SharpProof.Attributes` is
published to NuGet.org yet.

For local preview use, build a local feed from this repo and install from that
feed:

```powershell
.\build-nuget.ps1 -Configuration Release
dotnet add package SharpProof --version 0.1.0-preview.1 --source .\artifacts\nuget
```

The main analyzer package already includes the attributes assembly for normal
package consumers.

Once the packages are published, install the analyzer package in projects that
should be checked with:

```powershell
dotnet add package SharpProof --version 0.1.0-preview.1
```

Use the attributes in source:

```csharp
using SharpProof.Attributes;

public sealed class Calculator
{
    [EnforcePure]
    public int Add(int left, int right) => left + right;

    [EnforcePure]
    public int ReadClock() => DateTime.Now.Second; // SP0002
}
```

Add `SharpProof.Attributes` only when a project needs the attributes without
installing the analyzer package. For local preview builds, point `dotnet add`
at the same `.\artifacts\nuget` feed:

```powershell
dotnet add package SharpProof.Attributes --version 0.1.0-preview.1
```

## Selected Examples

These curated blocks are generated from committed example inputs and committed
output snapshots. Each example is backed by a regression test so the README can
fail fast when the public behavior or documentation drifts.

<!-- README_EXAMPLES -->

## Capability Matrix

| Area | Status | What works today | Evidence |
| --- | --- | --- | --- |
| Attribute contracts | [x] | `[EnforcePure]` and `[Pure]` mark source methods for purity enforcement. `[PureExternal]` and `[Impure]` model trusted or rejected boundaries. `[AllowSynchronization]` can permit synchronization inside otherwise pure methods. | [BoundaryAttributeTests.cs](SharpProof.Test/BoundaryAttributeTests.cs), [SharpProofCodeFixTests.cs](SharpProof.Test/SharpProofCodeFixTests.cs) |
| Zero-allocation contracts | [x] | `[ZeroAllocations]` reports one warning per direct source-visible allocation site in an annotated method-like body. The first tranche covers object creation, array creation, anonymous objects, collection expressions that materialize heap-backed values, delegate creation, boxing, and supported reference-type `with` expressions. | [ZeroAllocationContractTests.cs](SharpProof.Test/ZeroAllocationContractTests.cs), [MethodAllocationAnalyzer.cs](SharpProof.Analyzer/MethodAllocationAnalyzer.cs) |
| Capability contracts | [x] | `[AllowedCapabilities]` enforces proven capability categories such as `FileRead`, `FileWrite`, `Network`, `Console`, `Process`, `Environment`, `Registry`, `Clock`, `Randomness`, `Reflection`, `Synchronization`, `NativeInterop`, and derived `IO`. Violations and conservative unknowns are reported per site. | [CapabilityContractTests.cs](SharpProof.Test/CapabilityContractTests.cs), [MethodCapabilityAnalyzer.cs](SharpProof.Analyzer/MethodCapabilityAnalyzer.cs) |
| Purity diagnostics | [x] | `SP0002` reports marked methods whose bodies cannot be proven pure. `SP0003` reports misplaced purity attributes. `SP0004` suggests missing purity attributes on methods that appear pure. `SP0005` through `SP0008` cover conflicting purity attributes and synchronization attribute misuse. | [SharpProofDiagnostics.cs](SharpProof.Analyzer/SharpProofDiagnostics.cs), [DiagnosticEvidenceTests.cs](SharpProof.Test/DiagnosticEvidenceTests.cs) |
| Optional explanation diagnostics | [x] | `SP0009` emits structured explanation data when `sharpproof_emit_explanations = true`. `SP0012` emits a non-authoritative BCL fallback guess when an otherwise unknown metadata BCL member has no stronger evidence and either explanations or `sharpproof_report_bcl_fallback_guesses` are enabled. | [AnalyzerReleases.Shipped.md](SharpProof.Analyzer/AnalyzerReleases.Shipped.md), [DiagnosticEvidenceTests.cs](SharpProof.Test/DiagnosticEvidenceTests.cs) |
| Code fixes | [x] | Code fixes add `[EnforcePure]`, remove conflicting attributes, remove invalid purity attributes, and clean up synchronization attributes. | [SharpProofCodeFixTests.cs](SharpProof.Test/SharpProofCodeFixTests.cs) |
| Analyzer configuration | [x] | `.editorconfig` and global analyzerconfig settings control known pure/impure methods, impure namespaces/types, purity profile, missing-attribute suggestions, explanations, runtime-hazard reporting, exception summaries, effect-summary JSON, SMT mode, and SMT budgets. | [ConfigKeys.cs](SharpProof.Analyzer/Configuration/ConfigKeys.cs), [AnalyzerConfiguration.cs](SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs) |
| Baseline suppression | [x] | `SharpProof.Baseline.json` additional files can suppress known diagnostics by ID, symbol documentation ID, and path for incremental adoption. | [BaselineSuppressionTests.cs](SharpProof.Test/BaselineSuppressionTests.cs) |
| NuGet/package layout | [x] | The analyzer package contains the analyzer, code fixes, attributes, symbolic library, SearchLib, and Z3 assets. It does not ship loose effect-summary JSON artifacts. | [AnalyzerPackagingTests.cs](SharpProof.Test/AnalyzerPackagingTests.cs), [SharpProof.Package.csproj](SharpProof.Package/SharpProof.Package.csproj) |
| Build-time built-in summaries | [x] | Built-in summaries are regenerated into analyzer intermediates, embedded for the current build, and loaded only from embedded resources. Loose analyzer-directory JSON files are ignored for built-ins. | [AnalyzerPackagingTests.cs](SharpProof.Test/AnalyzerPackagingTests.cs), [SharpProof.Analyzer.csproj](SharpProof.Analyzer/SharpProof.Analyzer.csproj) |
| External summary additional files | [x] | Explicit `*.SharpProof.EffectSummary.json` additional files can be consumed when effect-summary JSON is enabled and identity validation passes. | [ExceptionSummaryCatalogValidationTests.cs](SharpProof.Test/ExceptionSummaryCatalogValidationTests.cs), [docs/effect-summary.md](docs/effect-summary.md) |
| CFG purity analysis | [~] | Method bodies are analyzed through Roslyn operations and CFG-style flow. The analyzer handles many common expressions, calls, assignments, returns, object/array creation, delegates, LINQ, `using`, async, records, switch, try/catch, and common BCL APIs. Unknown or high-risk shapes remain conservative. | [RoslynConstructCoverageTests.cs](SharpProof.Test/RoslynConstructCoverageTests.cs), [BasicPurityTests.cs](SharpProof.Test/BasicPurityTests.cs), [UsingStatementTests.cs](SharpProof.Test/UsingStatementTests.cs) |
| Z3/SMT service | [~] | One bounded `SmtAnalysisService` classifies reachability and implication, caches repeated queries, handles budgets/timeouts, and falls back conservatively when SMT is off, over budget, or unavailable. | [SmtAnalysisServiceTests.cs](SharpProof.Test/SmtAnalysisServiceTests.cs), [SearchLibZ3SmokeTests.cs](SharpProof.Test/SearchLibZ3SmokeTests.cs) |
| Path-sensitive facts | [~] | Path facts include local/parameter versions, constants, null/non-null, numeric comparisons, affine arithmetic, multiplication by constant, boolean short-circuiting, conditionals, coalesce, switch arms, relational patterns, property/list patterns, assignments, tuple/array/list facts, and guarded exception hazards. | [SemanticOracleSmtTests.cs](SharpProof.Test/SemanticOracleSmtTests.cs), [ExceptionFlowPathFactStressTests.cs](SharpProof.Test/ExceptionFlowPathFactStressTests.cs) |
| String and regex SMT facts | [~] | Z3 string theory is used for string equality, concatenation, length, contains, starts-with, ends-with, and a translated subset of .NET regex patterns. Concrete regex/string facts are self-validated with .NET regex where applicable. Unsupported regex options or patterns stay unknown. Regex APIs are not automatically pure just because their predicates can feed SMT. | [SmtAnalysisServiceTests.cs](SharpProof.Test/SmtAnalysisServiceTests.cs), [SemanticOracleSmtTests.cs](SharpProof.Test/SemanticOracleSmtTests.cs), [RegexTests.cs](SharpProof.Test/RegexTests.cs) |
| Symbolic invariant API | [~] | `SharpProof.Symbolic` can query merged invariants at a line, column, syntax position, span, node, or all source lines through `SymbolicQueryService`, and can use SMT to check reachability or implication. Query results expose per-program-point facts plus merged aggregate summaries. It is useful as a library independent of the analyzer package, but the facts are still bounded and syntax/semantic-model derived. | [SymbolicSourceQueryLineTests.cs](SharpProof.Test/SymbolicSourceQueryLineTests.cs), [docs/symbolic-invariants.md](docs/symbolic-invariants.md), [SymbolicQueryApi.cs](SharpProof.Symbolic/SymbolicQueryApi.cs) |
| Symbolic runtime-hazard API | [~] | `SharpProof.Symbolic` can query proven runtime hazards at a line, span, syntax tree, or file, including direct throws, rethrows, divide-by-zero, null dereference, nullable value access, index/range hazards, casts, array covariance stores, checked overflow, negative array lengths, and dynamic null-binding hazards. Unknown candidates stay hidden by default unless explicitly requested. | [SymbolicRuntimeHazardQueryTests.cs](SharpProof.Test/SymbolicRuntimeHazardQueryTests.cs), [SymbolicRuntimeHazardQueryService.cs](SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs) |
| Symbolic capability API | [x] | `SharpProof.Symbolic` can query the containing method-like body's proven capability categories plus per-site evidence and conservative unknown reasons. The same classification spine backs `[AllowedCapabilities]` diagnostics and CLI capability queries. | [SymbolicCapabilityQueryTests.cs](SharpProof.ToolingTest/SymbolicCapabilityQueryTests.cs), [SymbolicCapabilityService.cs](SharpProof.Symbolic/SymbolicCapabilityService.cs), [docs/capability-analysis.md](docs/capability-analysis.md) |
| Symbolic complexity API | [~] | `SharpProof.Symbolic` can query a conservative asymptotic complexity summary such as `O(1)`, `O(n)`, `O(n * m)`, `O(n^2)`, `Unknown`, or `RecursiveUnknown` for the containing method-like body. It handles straight-line code, bounded `for` loops, supported `foreach`, some monotone `while` loops, and bounded callee composition; unsupported loop shapes or unknown callees stay conservative. | [SymbolicComplexityTests.cs](SharpProof.Test/SymbolicComplexityTests.cs), [SymbolicComplexityService.cs](SharpProof.Symbolic/SymbolicComplexityService.cs), [docs/complexity-queries.md](docs/complexity-queries.md) |
| Symbolic CLI | [x] | `Tools/SharpProof.SymbolicCli` exposes invariant queries, runtime-hazard queries, capability queries, complexity queries, references, JSON output, reachability checks, implication checks, and SMT budget switches. | [AnalyzerPackagingTests.cs](SharpProof.Test/AnalyzerPackagingTests.cs), [SymbolicRuntimeHazardQueryTests.cs](SharpProof.Test/SymbolicRuntimeHazardQueryTests.cs), [Program.cs](Tools/SharpProof.SymbolicCli/Program.cs) |
| Runtime hazards and exception flow | [~] | `sharpproof_runtime_hazard_mode = sites` reports `SP0011` operation-site hazards without requiring purity attributes. `all` also emits `SP0010` method summaries. Legacy `sharpproof_report_exceptions` and `sharpproof_checked_exceptions` remain supported. The analyzer tracks direct throws, rethrows, source call chains, trusted metadata summaries, divide-by-zero, null dereference, dynamic null binding, negative array lengths, index hazards, catch filters, and some resource disposal flows. | [DiagnosticEvidenceTests.cs](SharpProof.Test/DiagnosticEvidenceTests.cs), [SemanticOracleSmtTests.cs](SharpProof.Test/SemanticOracleSmtTests.cs), [ExceptionSummaryCatalogValidationTests.cs](SharpProof.Test/ExceptionSummaryCatalogValidationTests.cs), [RecursiveExceptionFlowTests.cs](SharpProof.Test/RecursiveExceptionFlowTests.cs) |
| Dispatch, delegates, and LINQ | [~] | The analyzer narrows many exact concrete receiver flows, delegate targets, default equality/comparison dispatch, immutable collection operations, LINQ materialization, and enumerable hazards. Deeper heterogeneous merges, unknown dynamic dispatch, and unresolved external targets remain conservative. | [ExactConcreteDispatchFlowTests.cs](SharpProof.Test/ExactConcreteDispatchFlowTests.cs), [DelegateTests.cs](SharpProof.Test/DelegateTests.cs), [LinqOperationsTests.cs](SharpProof.Test/LinqOperationsTests.cs), [LinqSoundnessStressTests.cs](SharpProof.Test/LinqSoundnessStressTests.cs) |
| Fresh ownership and mutation | [~] | Some fresh arrays, collection expressions, inline arrays, local mutation, fresh returns, and disposal cases are modeled. Full borrow-checker-grade ownership, escape, alias, lifetime, and resource-release analysis is not implemented. | [CollectionExpressionTests.cs](SharpProof.Test/CollectionExpressionTests.cs), [ArrayMutationTests.cs](SharpProof.Test/ArrayMutationTests.cs), [UsingStatementTests.cs](SharpProof.Test/UsingStatementTests.cs) |
| BCL/.NET SDK coverage | [~] | Coverage is evidence-backed and member-level, using reviewed catalogs, generated build-time summaries, hand-coded conservative roots, and tests for many runtime families. There is no meaningful "percent of the .NET SDK" claim yet because SDK APIs are not a uniform denominator and many APIs depend on runtime, OS, culture, time, randomness, reflection, native state, or hidden implementation behavior. | [EffectSummaryToolTests.cs](SharpProof.Test/EffectSummaryToolTests.cs), [ConstantsTests.cs](SharpProof.Test/ConstantsTests.cs), [CryptographyTests.cs](SharpProof.Test/CryptographyTests.cs) |
| BCL fallback guesses | [~] | When attributes, config, generated summaries, semantic catalogs, and source analysis all miss a metadata BCL method, property, constructor, or field, `SP0002` carries low-confidence fallback properties such as `probably_pure`, `probably_impure`, or `unknown`. With `sharpproof_emit_explanations = true` or `sharpproof_report_bcl_fallback_guesses = true`, `SP0012` reports the same guess as an info diagnostic. The effect-summary tool can also emit a local `BclFallbackInventory` for SDK/runtime auditing. These guesses do not make a method pure. | [DiagnosticEvidenceTests.cs](SharpProof.Test/DiagnosticEvidenceTests.cs), [EffectSummaryToolTests.cs](SharpProof.Test/EffectSummaryToolTests.cs), [BclPurityFallbackClassifier.cs](SharpProof.Analyzer/Engine/BclPurityFallbackClassifier.cs) |
| Full C# operation coverage | [~] | Every Roslyn operation kind should have an explicit coverage decision. Some shapes are intentionally conservative, including unsafe address capture, function pointer invocation, and custom interpolated-string-handler execution. | [RoslynConstructCoverageTests.cs](SharpProof.Test/RoslynConstructCoverageTests.cs) |
| Whole-program execution prediction | [ ] | SharpProof does not run or fully simulate arbitrary C# programs. It derives bounded facts from syntax, semantics, CFG/path facts, catalogs, summaries, and SMT. | [docs/symbolic-invariants.md](docs/symbolic-invariants.md), [SymbolicQueryApi.cs](SharpProof.Symbolic/SymbolicQueryApi.cs) |
| Rust-style borrow checker | [ ] | A full borrow/resource ownership system is roadmap work. Current ownership handling is local, bounded, and purity-focused. | [UsingStatementTests.cs](SharpProof.Test/UsingStatementTests.cs), [ArrayMutationTests.cs](SharpProof.Test/ArrayMutationTests.cs) |

## Diagnostics

| ID | Default severity | Meaning |
| --- | --- | --- |
| `SP0002` | Error | A method marked `[EnforcePure]` or `[Pure]` contains operations the analyzer cannot prove pure. |
| `SP0003` | Error | `[EnforcePure]` or `[Pure]` is applied to a non-method declaration. |
| `SP0004` | Warning | A method appears pure but is not marked `[EnforcePure]`. |
| `SP0005` | Warning | Conflicting purity attributes are applied together. |
| `SP0006` | Warning | `[AllowSynchronization]` is used without a purity attribute. |
| `SP0007` | Error | `[AllowSynchronization]` is applied to a non-method declaration. |
| `SP0008` | Info | `[AllowSynchronization]` is redundant because no synchronization was detected. |
| `SP0009` | Info | Optional purity explanation emitted when `sharpproof_emit_explanations = true`. |
| `SP0010` | Info | Optional escaping-exception summary emitted when `sharpproof_report_exceptions = true` or `sharpproof_runtime_hazard_mode = summaries/all`. |
| `SP0011` | Warning | Optional uncaught operation-site exception/runtime-hazard warning emitted when `sharpproof_checked_exceptions = true` or `sharpproof_runtime_hazard_mode = sites/all`. |
| `SP0012` | Info | Optional non-authoritative BCL purity fallback guess emitted when `sharpproof_emit_explanations = true` or `sharpproof_report_bcl_fallback_guesses = true`. |
| `SP0013` | Warning | A method marked `[ZeroAllocations]` contains a direct source-visible allocation site. |
| `SP0014` | Error | `[ZeroAllocations]` is applied to a non-method declaration. |
| `SP0015` | Warning | A method marked `[AllowedCapabilities]` contains an operation or proven transitive callee that exceeds the declared capability set. |
| `SP0016` | Warning | A method marked `[AllowedCapabilities]` contains an operation whose capability requirements could not be fully verified conservatively. |
| `SP0017` | Error | `[AllowedCapabilities]` is applied to a non-method declaration. |

## Configuration

Example `.editorconfig`:

```ini
is_global = true

dotnet_diagnostic.SP0002.severity = error
dotnet_diagnostic.SP0004.severity = suggestion

sharpproof_purity_profile = balanced
sharpproof_suggest_missing_enforce_pure = true
sharpproof_suggest_missing_enforce_pure_scope = all
sharpproof_emit_explanations = false
sharpproof_report_bcl_fallback_guesses = false

sharpproof_runtime_hazard_mode = off
sharpproof_report_exceptions = false
sharpproof_checked_exceptions = false
sharpproof_enable_effect_summary_json = false

sharpproof_smt_mode = bounded
sharpproof_smt_timeout_ms = 750
sharpproof_smt_method_budget_ms = 5000
sharpproof_smt_max_path_conditions = 192
sharpproof_smt_max_expression_nodes = 2048
```

Supported analyzer keys:

- `sharpproof_known_impure_methods`
- `sharpproof_known_pure_methods`
- `sharpproof_known_impure_namespaces`
- `sharpproof_known_impure_types`
- `sharpproof_purity_profile`
- `sharpproof_enable_debug_logging`
- `sharpproof_suggest_missing_enforce_pure`
- `sharpproof_suggest_missing_enforce_pure_scope`
- `sharpproof_suggest_missing_enforce_pure_exclude_generated`
- `sharpproof_suggest_missing_enforce_pure_exclude_tests`
- `sharpproof_suggest_missing_enforce_pure_min_complexity`
- `sharpproof_suggest_missing_enforce_pure_namespace_filters`
- `sharpproof_emit_explanations`
- `sharpproof_report_bcl_fallback_guesses`
- `sharpproof_runtime_hazard_mode`
- `sharpproof_report_exceptions`
- `sharpproof_checked_exceptions`
- `sharpproof_enable_effect_summary_json`
- `sharpproof_smt_mode`
- `sharpproof_smt_timeout_ms`
- `sharpproof_smt_method_budget_ms`
- `sharpproof_smt_max_path_conditions`
- `sharpproof_smt_max_expression_nodes`

Runtime hazard modes:

| Mode | Diagnostics | Use |
| --- | --- | --- |
| `off` | None | Default. Runtime-failure checks stay disabled unless legacy exception switches are enabled. |
| `sites` | `SP0011` | Report analyzer-proven uncaught operation-site hazards such as throws, divide-by-zero, null dereference, and index hazards. |
| `summaries` | `SP0010` | Report method-level escaping exception summaries without operation-site warnings. |
| `all` | `SP0010`, `SP0011` | Emit both method summaries and operation-site runtime-hazard warnings. |

SMT modes:

| Mode | Query timeout | Method budget | Max path conditions | Max expression nodes | Use |
| --- | ---: | ---: | ---: | ---: | --- |
| `off` | 750 ms | 5000 ms | 192 | 2048 | Disable solver proofs while preserving conservative behavior. |
| `bounded` | 750 ms | 5000 ms | 192 | 2048 | Default IDE/build-safe solver usage. |
| `deep` | 2000 ms | 15000 ms | 512 | 8192 | More expensive local analysis for harder facts. |

Budget keys override the mode defaults when set to positive integer values.

## Symbolic Invariants And Z3

The symbolic layer starts from small facts such as `if` conditions, assignments,
patterns, null checks, numeric comparisons, string predicates, and regex
predicates. Larger conclusions are derived by formula lowering and Z3 rather
than by hard-coding one method's control flow.

SharpProof exposes symbolic queries through the
`SharpProof.Symbolic` assembly:

- `SymbolicQueryService.Query(SymbolicQueryRequest)` for point, position, line, span, line-span, all-lines, and node queries
- `SymbolicSourceInput.FromFile`, `FromText`, `FromSyntaxTree`, and `FromNode`
- `SymbolicQueryTarget.Point`, `Position`, `Line`, `Span`, `LineSpan`, `AllLines`, and `Node`
- `SymbolicQueryService.Prove(SymbolicConditionProofRequest)` for condition implication checks
- `SymbolicQueryService.QueryRuntimeHazards(SymbolicRuntimeHazardRequest)` for symbolic runtime-hazard queries
- aggregate summaries via `SymbolicQueryResult`
- post-query result filters via `SymbolicSourceQueryFilter`

The CLI mirrors the library:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
```

Use `--all-lines` to enumerate every source line with statement/expression
program points from one parse/compilation pass. Text output includes file-level
line/program-point counts, observed distinct fact counts, and aggregate
reachability/implication counts when SMT checks are requested. File-level
observed facts are an overview; line and point results remain the source of
actual merged invariants.

For aggregate queries, pass `--node-kind`, `--with-facts`, or `--reachability`
to narrow output after analysis. The CLI recomputes line/file summaries from
the retained program points.

Use `--json` for machine-readable output. Pass `--smt-mode`, `--smt-timeout-ms`,
`--smt-method-budget-ms`, `--smt-max-path-conditions`, and
`--smt-max-expression-nodes` to tune solver cost.

Regex support lowers a practical subset of .NET regex syntax to Z3 string/regex
constraints and then self-validates concrete examples with .NET regex when a
concrete string is available. Unsupported options, invalid patterns, and regex
features outside the translator remain unknown rather than proven.

Runtime type tests are represented as Z3-backed reference predicates. That lets
`is`, declaration/type patterns, switch pattern exclusions, and guarded casts
share the same path facts without hard-coded method or branch special cases.

The same CLI can query runtime hazards instead of invariant program points:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --runtime-hazards --json
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --runtime-hazards --hazard-kind NullDereference
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --runtime-hazards --include-unproven-hazards --hazard-status Unknown --compact-json --max-hazards 50
```

`SymbolicRuntimeHazardQueryService` is the library surface behind that CLI. It
returns only proven hazards by default, and can include unknown, unreachable, or
unsupported candidates for tooling that wants to display conservative
possibilities. See [docs/symbolic-invariants.md](docs/symbolic-invariants.md)
for hazard scopes, filters, and compact output notes.

This is source-analysis infrastructure rather than a compiler modification: it
can be pointed at user code or compiler source, but IDE/compiler inline surfacing
is separate integration work.

## Capability, Allocation, And Complexity Queries

SharpProof now exposes three additional user-facing bounded analysis surfaces
beyond purity and invariants:

- `[ZeroAllocations]` is a cheap analyzer contract for direct source-visible
  heap allocations inside a method-like body. It is intentionally syntactic and
  does not claim whole-call-graph allocation freedom.
- `[AllowedCapabilities(...)]` is a conservative analyzer contract over proven
  capability categories such as `Console`, `FileRead`, `FileWrite`, `Network`,
  `Process`, `Environment`, `Clock`, `Randomness`, `Reflection`,
  `Synchronization`, `NativeInterop`, and derived `IO`.
- `SymbolicQueryService.QueryCapabilities(...)` and the CLI `--capabilities`
  mode return the containing method-like body's merged capability set, per-site
  evidence, and conservative unknown reasons.
- `SymbolicQueryService.QueryComplexity(...)` and the CLI `--complexity` mode
  return a conservative method-level Big-O summary plus drivers, unknown
  reasons, and callee summaries.

Current request-shape rules are intentionally narrow:

- Library capability queries support point, position, line, and node targets.
- Library complexity queries support point, position, line, and node targets.
- CLI `--capabilities` and `--complexity` support `--line`, `--line` with
  `--column`, or `--position`.
- Invalid source/target combinations are API misuse and currently throw
  `NotSupportedException` in the library or exit with an argument error in the
  CLI.

See [docs/capability-analysis.md](docs/capability-analysis.md) and
[docs/complexity-queries.md](docs/complexity-queries.md) for examples, target
support, and conservative fallback details.

## Effect Summaries

SharpProof still uses effect-summary JSON as data, but not as checked-in
generated outputs.

- The source manifest `SharpProof.Analyzer/BuiltInEffectSummaryArtifactSpec.json`
  is checked in.
- During analyzer build/test, built-in summaries are regenerated into
  analyzer intermediates under `obj`.
- The generated intermediate directory is cleared before regeneration.
- The analyzer embeds only the current run's generated summaries.
- Built-in loaders do not probe `Assembly.Location`, analyzer directories, repo
  artifacts, or `AppContext.BaseDirectory` for built-in summary files.
- Explicit user additional files named `*.SharpProof.EffectSummary.json` remain
  supported for external opt-in summaries.

`Tools/SharpProof.EffectSummary` remains ad hoc tooling for calibration,
summary generation, report-only SDK/runtime analysis, and disposable BCL
fallback inventories. See
[docs/effect-summary.md](docs/effect-summary.md).

## Known Limitations

- Purity for arbitrary C# is undecidable; SharpProof is a bounded practical
  analyzer, not a proof assistant.
- Unknown external calls remain conservative unless trusted by explicit
  attributes, configuration, catalogs, or validated summaries. BCL fallback
  guesses add diagnostic context but do not prove purity.
- Runtime-native, OS, environment, time, randomness, culture, reflection,
  threading, synchronization, unsafe, dynamic, and hidden implementation
  surfaces are intentionally conservative unless explicitly modeled.
- The compiler and IDE are not modified to display symbolic facts inline yet.
  Today those facts are available through the analyzer diagnostics and
  `SharpProof.Symbolic` library/CLI.
- Regex SMT support is a subset. It can prove useful string/regex facts, but it
  does not enumerate every possible matching string or fully implement .NET
  regex semantics.
- Fresh ownership/escape analysis is useful but local. It is not a full
  Rust-style borrow checker.
- Mutual recursion and deep whole-program dispatch remain limited.
- Some Roslyn operation shapes are deliberately classified as conservative
  even though they are recognized.
- There is no current, defensible "percent of the .NET SDK covered" metric.
  Member-level evidence is tracked through tests, generated summaries, and the
  roadmap below instead.

## Roadmap

This README now carries the public roadmap directly. Separate tracked planning
or backlog markdown files have been removed to avoid drift.

- [x] Analyzer, attributes, code fixes, and NuGet/VSIX package layout.
- [x] Core CFG purity rules and explainable `SP0002` evidence.
- [x] Build-time regenerated built-in summaries with embedded-resource loading.
- [x] External additional-file summary support with identity validation.
- [x] Bounded Z3/SMT service with cache, timeout, budget, and fallback behavior.
- [x] Symbolic invariant library and CLI.
- [~] Shared symbolic platform spine across analyzer diagnostics, invariant queries, runtime hazards, and summary-backed reasoning.
- [~] Broader SMT path facts for branch pruning, exception hazards, string, and regex conditions.
- [~] Standalone symbolic runtime-hazard query API and CLI.
- [x] Runtime type-test atoms for Z3-backed invariants and invalid-cast pruning.
- [~] Migration from analyzer-local raw SMT/path logic into symbolic IR facts and shared proof orchestration.
- [~] Deeper exception-flow summaries and metadata/library effect-summary consumption.
- [~] Better dispatch, delegate, LINQ, and enumerator precision.
- [~] Better fresh-object, fresh-array, alias, escape, and local ownership modeling.
- [~] More member-level SDK/BCL effect-summary coverage and catalog retirement.
- [ ] Rust-style borrow/resource ownership model.
- [ ] Mutual recursion proofs beyond the current bounded direct/self-recursive handling.
- [ ] More conservative Roslyn operation-shape reductions where sound regressions can be locked.
- [ ] Corpus-driven SDK coverage metrics that can report meaningful numerator/denominator data.
- [ ] IDE/compiler integration for inline invariant and runtime-hazard surfacing.
- [ ] Performance and profiling work that preserves analysis quality while reducing test/build cost.

Known remaining SMT runtime-hazard gaps are intentionally narrow: failed `as`
conversions do not yet become reusable negative type facts, dynamic binder
hazards beyond null receivers are not modeled, array covariance stores through
aliases can stay unknown, and richer throw-expression flow remains limited to
the currently proven `throw null` cases.

## Verification

Use the repo wrapper for .NET commands so long-running tests run under the
expected Windows Job Object:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 build SharpProof.sln --configuration Release
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane All
```

The default lane is `Main` so local loops skip the slower tooling and packaging
fixtures unless you opt into `-TestLane Tooling` or `-TestLane All`. For
red-suite loops, the test wrapper can stop after the first NUnit failure. It
can also emit a TRX slow-test profile, override worker counts explicitly, and
bound hung runs:

```powershell
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -FailFast
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -Profile -Top 30
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -Workers 8
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TimeoutSeconds 900
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane Tooling
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane All
```

For local iteration, the impacted-test wrapper can derive a VSTest filter from
changed files. It falls back to the full suite for shared infrastructure,
high-fanout analyzer core, broad generated dependency maps, or unmapped files
unless `-ForcePartial` is set. The wrapper combines curated path maps with the
checked-in generated inventory in `scripts/test-impact-inventory.json`:

```powershell
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -ListOnly
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -ListOnly -Json
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -ListOnly -Explain
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild
.\scripts\Invoke-SharpProofImpactedTests.ps1 -BaseRef origin/main -NoBuild
.\scripts\Invoke-SharpProofImpactedTests.ps1 -BaseRef origin/main -NoBuild -ForcePartial
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -TimeoutSeconds 900
```

Use `-ChangedFile <path>` with `-ListOnly` to preview the mapping for a
specific edit before staging it. Use `scripts/Get-SharpProofTestImpactInventory.ps1`
to regenerate the inventory after adding modules, fixtures, or production
types. The helper is deliberately conservative and is not a CI replacement; run
the full wrapper or GitHub CI before relying on broad behavioral changes.

Representative evidence suites:

- Packaging and generated-summary policy:
  [AnalyzerPackagingTests.cs](SharpProof.Test/AnalyzerPackagingTests.cs)
- Z3 solver, budgets, caching, string, and regex:
  [SmtAnalysisServiceTests.cs](SharpProof.Test/SmtAnalysisServiceTests.cs),
  [SearchLibZ3SmokeTests.cs](SharpProof.Test/SearchLibZ3SmokeTests.cs)
- CFG/path-sensitive symbolic facts:
  [SemanticOracleSmtTests.cs](SharpProof.Test/SemanticOracleSmtTests.cs)
- Invariant query API:
  [SymbolicSourceQueryLineTests.cs](SharpProof.Test/SymbolicSourceQueryLineTests.cs)
- Runtime-hazard query API:
  [SymbolicRuntimeHazardQueryTests.cs](SharpProof.Test/SymbolicRuntimeHazardQueryTests.cs)
- Exception summaries and additional-file summaries:
  [ExceptionSummaryCatalogValidationTests.cs](SharpProof.Test/ExceptionSummaryCatalogValidationTests.cs),
  [EffectSummaryToolTests.cs](SharpProof.Test/EffectSummaryToolTests.cs)
- Roslyn operation coverage decisions:
  [RoslynConstructCoverageTests.cs](SharpProof.Test/RoslynConstructCoverageTests.cs)
- Boundary attributes, baselines, and code fixes:
  [BoundaryAttributeTests.cs](SharpProof.Test/BoundaryAttributeTests.cs),
  [BaselineSuppressionTests.cs](SharpProof.Test/BaselineSuppressionTests.cs),
  [SharpProofCodeFixTests.cs](SharpProof.Test/SharpProofCodeFixTests.cs)
