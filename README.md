# PurelySharp - C# Roslyn Purity Analyzer with Z3 SMT

PurelySharp is a conservative .NET static analysis tool and C# Roslyn analyzer
for method purity, side-effect detection, symbolic invariants, and
exception-flow checks. It ships an analyzer, code fixes, attributes, bounded
Z3/SMT reasoning, build-time generated effect-summary data, and a standalone
symbolic invariant query library/CLI.

The analyzer does not execute user code and does not attempt an unbounded
whole-program proof. When it cannot prove a fact within the implemented rules
and budgets, it stays conservative: purity falls back to `PS0002` for methods
marked pure, and SMT/exceptions fall back to unknown or no proof.

## Current State

- Current package metadata: `PurelySharp` `0.0.4` and
  `PurelySharp.Attributes` `0.0.4`.
- Analyzer and symbolic library target `netstandard2.0`; the symbolic CLI
  targets `net8.0`.
- Public diagnostics in active use are `PS0002` through `PS0012`.
- Z3 is packaged with the analyzer via `PurelySharp.Symbolic.dll`,
  `SearchLib.dll`, `Microsoft.Z3.dll`, and native `libz3.dll`.
- Built-in effect summaries are regenerated into `obj` during build/test and
  embedded as analyzer resources. The repo should not depend on checked-in or
  preexisting `*.PurelySharp.EffectSummary.json` outputs.
- Explicit user-supplied `PurelySharp.EffectSummary.json` additional files are
  still supported for opt-in external summaries.

## Support Status Legend

`[x]` means the capability is implemented and regression-tested in this repo.
`[~]` means the capability exists but is bounded, partial, or intentionally
conservative. `[ ]` means it is not implemented or remains roadmap work.

Evidence references point to representative tests or source files. They are not
the entire test surface.

## Common Use Cases

- Enforce C# method purity with `[EnforcePure]` and `[Pure]` attributes.
- Find side effects, unknown external calls, mutable state access, and unsafe
  purity gaps during build or in the IDE.
- Use Z3 SMT solving to prove bounded path facts such as null guards, numeric
  ranges, branch reachability, string predicates, and regex constraints.
- Query symbolic invariants at a source line or syntax position from a
  standalone .NET library or CLI.
- Audit exception-flow risks such as uncaught throws, divide-by-zero,
  null dereferences, and index hazards.
- Calibrate .NET SDK and BCL purity behavior with regenerated effect-summary
  data instead of checked-in generated artifacts.

## Quick Start

Install the analyzer package in projects that should be checked:

```powershell
dotnet add package PurelySharp --version 0.0.4
```

Use the attributes in source:

```csharp
using PurelySharp.Attributes;

public sealed class Calculator
{
    [EnforcePure]
    public int Add(int left, int right) => left + right;

    [EnforcePure]
    public int ReadClock() => DateTime.Now.Second; // PS0002
}
```

Add `PurelySharp.Attributes` only when a project needs the attributes without
installing the analyzer package:

```powershell
dotnet add package PurelySharp.Attributes --version 0.0.4
```

## Capability Matrix

| Area | Status | What works today | Evidence |
| --- | --- | --- | --- |
| Attribute contracts | [x] | `[EnforcePure]` and `[Pure]` mark source methods for purity enforcement. `[PureExternal]` and `[Impure]` model trusted or rejected boundaries. `[AllowSynchronization]` can permit synchronization inside otherwise pure methods. | [BoundaryAttributeTests.cs](PurelySharp.Test/BoundaryAttributeTests.cs), [PurelySharpCodeFixTests.cs](PurelySharp.Test/PurelySharpCodeFixTests.cs) |
| Purity diagnostics | [x] | `PS0002` reports marked methods whose bodies cannot be proven pure. `PS0003` reports misplaced purity attributes. `PS0004` suggests missing purity attributes on methods that appear pure. `PS0005` through `PS0008` cover conflicting purity attributes and synchronization attribute misuse. | [PurelySharpDiagnostics.cs](PurelySharp.Analyzer/PurelySharpDiagnostics.cs), [DiagnosticEvidenceTests.cs](PurelySharp.Test/DiagnosticEvidenceTests.cs) |
| Optional explanation diagnostics | [x] | `PS0009` emits structured explanation data when `purelysharp_emit_explanations = true`. `PS0012` emits a non-authoritative BCL fallback guess when an otherwise unknown metadata BCL member has no stronger evidence. | [AnalyzerReleases.Unshipped.md](PurelySharp.Analyzer/AnalyzerReleases.Unshipped.md), [DiagnosticEvidenceTests.cs](PurelySharp.Test/DiagnosticEvidenceTests.cs) |
| Code fixes | [x] | Code fixes add `[EnforcePure]`, remove conflicting attributes, remove invalid purity attributes, and clean up synchronization attributes. | [PurelySharpCodeFixTests.cs](PurelySharp.Test/PurelySharpCodeFixTests.cs) |
| Analyzer configuration | [x] | `.editorconfig` and global analyzerconfig settings control known pure/impure methods, impure namespaces/types, purity profile, missing-attribute suggestions, explanations, exception reporting, effect-summary JSON, SMT mode, and SMT budgets. | [ConfigKeys.cs](PurelySharp.Analyzer/Configuration/ConfigKeys.cs), [AnalyzerConfiguration.cs](PurelySharp.Analyzer/Configuration/AnalyzerConfiguration.cs) |
| Baseline suppression | [x] | `PurelySharp.Baseline.json` additional files can suppress known diagnostics by ID, symbol documentation ID, and path for incremental adoption. | [BaselineSuppressionTests.cs](PurelySharp.Test/BaselineSuppressionTests.cs) |
| NuGet/package layout | [x] | The analyzer package contains the analyzer, code fixes, attributes, symbolic library, SearchLib, and Z3 assets. It does not ship loose effect-summary JSON artifacts. | [AnalyzerPackagingTests.cs](PurelySharp.Test/AnalyzerPackagingTests.cs), [PurelySharp.Package.csproj](PurelySharp.Package/PurelySharp.Package.csproj) |
| Build-time built-in summaries | [x] | Built-in summaries are regenerated into analyzer intermediates, embedded for the current build, and loaded only from embedded resources. Loose analyzer-directory JSON files are ignored for built-ins. | [AnalyzerPackagingTests.cs](PurelySharp.Test/AnalyzerPackagingTests.cs), [PurelySharp.Analyzer.csproj](PurelySharp.Analyzer/PurelySharp.Analyzer.csproj) |
| External summary additional files | [x] | Explicit `*.PurelySharp.EffectSummary.json` additional files can be consumed when effect-summary JSON is enabled and identity validation passes. | [ExceptionSummaryCatalogValidationTests.cs](PurelySharp.Test/ExceptionSummaryCatalogValidationTests.cs), [docs/effect-summary.md](docs/effect-summary.md) |
| CFG purity analysis | [~] | Method bodies are analyzed through Roslyn operations and CFG-style flow. The analyzer handles many common expressions, calls, assignments, returns, object/array creation, delegates, LINQ, `using`, async, records, switch, try/catch, and common BCL APIs. Unknown or high-risk shapes remain conservative. | [RoslynConstructCoverageTests.cs](PurelySharp.Test/RoslynConstructCoverageTests.cs), [BasicPurityTests.cs](PurelySharp.Test/BasicPurityTests.cs), [UsingStatementTests.cs](PurelySharp.Test/UsingStatementTests.cs) |
| Z3/SMT service | [~] | One bounded `SmtAnalysisService` classifies reachability and implication, caches repeated queries, handles budgets/timeouts, and falls back conservatively when SMT is off, over budget, or unavailable. | [SmtAnalysisServiceTests.cs](PurelySharp.Test/SmtAnalysisServiceTests.cs), [SearchLibZ3SmokeTests.cs](PurelySharp.Test/SearchLibZ3SmokeTests.cs) |
| Path-sensitive facts | [~] | Path facts include local/parameter versions, constants, null/non-null, numeric comparisons, affine arithmetic, multiplication by constant, boolean short-circuiting, conditionals, coalesce, switch arms, relational patterns, property/list patterns, assignments, tuple/array/list facts, and guarded exception hazards. | [SemanticOracleSmtTests.cs](PurelySharp.Test/SemanticOracleSmtTests.cs), [ExceptionFlowPathFactStressTests.cs](PurelySharp.Test/ExceptionFlowPathFactStressTests.cs) |
| String and regex SMT facts | [~] | Z3 string theory is used for string equality, concatenation, length, contains, starts-with, ends-with, and a translated subset of .NET regex patterns. Concrete regex/string facts are self-validated with .NET regex where applicable. Unsupported regex options or patterns stay unknown. Regex APIs are not automatically pure just because their predicates can feed SMT. | [SmtAnalysisServiceTests.cs](PurelySharp.Test/SmtAnalysisServiceTests.cs), [SemanticOracleSmtTests.cs](PurelySharp.Test/SemanticOracleSmtTests.cs), [RegexTests.cs](PurelySharp.Test/RegexTests.cs) |
| Symbolic invariant API | [~] | `PurelySharp.Symbolic` can query merged invariants at a line, column, syntax position, or all source lines, and can use SMT to check reachability or implication. Line and file queries expose per-program-point facts plus merged aggregate summaries. It is useful as a library independent of the analyzer package, but the facts are still bounded and syntax/semantic-model derived. | [SymbolicSourceQueryLineTests.cs](PurelySharp.Test/SymbolicSourceQueryLineTests.cs), [docs/symbolic-invariants.md](docs/symbolic-invariants.md), [SymbolicSourceQueryService.cs](PurelySharp.Symbolic/SymbolicSourceQueryService.cs) |
| Symbolic CLI | [x] | `Tools/PurelySharp.SymbolicCli` exposes line, position, and all-lines invariant queries, references, JSON output, reachability checks, implication checks, and SMT budget switches. | [AnalyzerPackagingTests.cs](PurelySharp.Test/AnalyzerPackagingTests.cs), [Program.cs](Tools/PurelySharp.SymbolicCli/Program.cs) |
| Exception-flow diagnostics | [~] | `PS0010` reports method-level escaping exceptions when `purelysharp_report_exceptions = true`. `PS0011` reports uncaught operation sites when `purelysharp_checked_exceptions = true`. The analyzer tracks direct throws, rethrows, source call chains, trusted metadata summaries, divide-by-zero, null dereference, index hazards, catch filters, and some resource disposal flows. | [SemanticOracleSmtTests.cs](PurelySharp.Test/SemanticOracleSmtTests.cs), [ExceptionSummaryCatalogValidationTests.cs](PurelySharp.Test/ExceptionSummaryCatalogValidationTests.cs), [RecursiveExceptionFlowTests.cs](PurelySharp.Test/RecursiveExceptionFlowTests.cs) |
| Dispatch, delegates, and LINQ | [~] | The analyzer narrows many exact concrete receiver flows, delegate targets, default equality/comparison dispatch, immutable collection operations, LINQ materialization, and enumerable hazards. Deeper heterogeneous merges, unknown dynamic dispatch, and unresolved external targets remain conservative. | [ExactConcreteDispatchFlowTests.cs](PurelySharp.Test/ExactConcreteDispatchFlowTests.cs), [DelegateTests.cs](PurelySharp.Test/DelegateTests.cs), [LinqOperationsTests.cs](PurelySharp.Test/LinqOperationsTests.cs), [LinqSoundnessStressTests.cs](PurelySharp.Test/LinqSoundnessStressTests.cs) |
| Fresh ownership and mutation | [~] | Some fresh arrays, collection expressions, inline arrays, local mutation, fresh returns, and disposal cases are modeled. Full borrow-checker-grade ownership, escape, alias, lifetime, and resource-release analysis is not implemented. | [CollectionExpressionTests.cs](PurelySharp.Test/CollectionExpressionTests.cs), [ArrayMutationTests.cs](PurelySharp.Test/ArrayMutationTests.cs), [UsingStatementTests.cs](PurelySharp.Test/UsingStatementTests.cs), [REMAINING_ANALYZER_BACKLOG.md](REMAINING_ANALYZER_BACKLOG.md) |
| BCL/.NET SDK coverage | [~] | Coverage is evidence-backed and member-level, using reviewed catalogs, generated build-time summaries, hand-coded conservative roots, and tests for many runtime families. There is no meaningful "percent of the .NET SDK" claim yet because SDK APIs are not a uniform denominator and many APIs depend on runtime, OS, culture, time, randomness, reflection, native state, or hidden implementation behavior. | [EffectSummaryToolTests.cs](PurelySharp.Test/EffectSummaryToolTests.cs), [ConstantsTests.cs](PurelySharp.Test/ConstantsTests.cs), [CryptographyTests.cs](PurelySharp.Test/CryptographyTests.cs), [REMAINING_ANALYZER_BACKLOG.md](REMAINING_ANALYZER_BACKLOG.md) |
| BCL fallback guesses | [~] | When attributes, config, generated summaries, semantic catalogs, and source analysis all miss a metadata BCL member, `PS0002` carries low-confidence fallback properties such as `probably_pure`, `probably_impure`, or `unknown`. With `purelysharp_emit_explanations = true`, `PS0012` reports the same guess as an info diagnostic. The effect-summary tool can also emit a local `BclFallbackInventory` for SDK/runtime auditing. These guesses do not make a method pure. | [DiagnosticEvidenceTests.cs](PurelySharp.Test/DiagnosticEvidenceTests.cs), [EffectSummaryToolTests.cs](PurelySharp.Test/EffectSummaryToolTests.cs), [BclPurityFallbackClassifier.cs](PurelySharp.Analyzer/Engine/BclPurityFallbackClassifier.cs) |
| Full C# operation coverage | [~] | Every Roslyn operation kind should have an explicit coverage decision. Some shapes are intentionally conservative, including unsafe address capture, function pointer invocation, and custom interpolated-string-handler execution. | [RoslynConstructCoverageTests.cs](PurelySharp.Test/RoslynConstructCoverageTests.cs) |
| Whole-program execution prediction | [ ] | PurelySharp does not run or fully simulate arbitrary C# programs. It derives bounded facts from syntax, semantics, CFG/path facts, catalogs, summaries, and SMT. | [REMAINING_ANALYZER_BACKLOG.md](REMAINING_ANALYZER_BACKLOG.md) |
| Rust-style borrow checker | [ ] | A full borrow/resource ownership system is roadmap work. Current ownership handling is local, bounded, and purity-focused. | [REMAINING_ANALYZER_BACKLOG.md](REMAINING_ANALYZER_BACKLOG.md) |

## Diagnostics

| ID | Default severity | Meaning |
| --- | --- | --- |
| `PS0002` | Error | A method marked `[EnforcePure]` or `[Pure]` contains operations the analyzer cannot prove pure. |
| `PS0003` | Error | `[EnforcePure]` or `[Pure]` is applied to a non-method declaration. |
| `PS0004` | Warning | A method appears pure but is not marked `[EnforcePure]`. |
| `PS0005` | Warning | Conflicting purity attributes are applied together. |
| `PS0006` | Warning | `[AllowSynchronization]` is used without a purity attribute. |
| `PS0007` | Error | `[AllowSynchronization]` is applied to a non-method declaration. |
| `PS0008` | Info | `[AllowSynchronization]` is redundant because no synchronization was detected. |
| `PS0009` | Info | Optional purity explanation emitted when `purelysharp_emit_explanations = true`. |
| `PS0010` | Info | Optional escaping-exception summary emitted when `purelysharp_report_exceptions = true`. |
| `PS0011` | Warning | Optional uncaught operation-site exception warning emitted when `purelysharp_checked_exceptions = true`. |
| `PS0012` | Info | Optional non-authoritative BCL purity fallback guess emitted when `purelysharp_emit_explanations = true`. |

## Configuration

Example `.editorconfig`:

```ini
is_global = true

dotnet_diagnostic.PS0002.severity = error
dotnet_diagnostic.PS0004.severity = suggestion

purelysharp_purity_profile = balanced
purelysharp_suggest_missing_enforce_pure = true
purelysharp_suggest_missing_enforce_pure_scope = all
purelysharp_emit_explanations = false

purelysharp_report_exceptions = false
purelysharp_checked_exceptions = false
purelysharp_enable_effect_summary_json = false

purelysharp_smt_mode = bounded
purelysharp_smt_timeout_ms = 750
purelysharp_smt_method_budget_ms = 5000
purelysharp_smt_max_path_conditions = 192
purelysharp_smt_max_expression_nodes = 2048
```

Supported analyzer keys:

- `purelysharp_known_impure_methods`
- `purelysharp_known_pure_methods`
- `purelysharp_known_impure_namespaces`
- `purelysharp_known_impure_types`
- `purelysharp_purity_profile`
- `purelysharp_enable_debug_logging`
- `purelysharp_suggest_missing_enforce_pure`
- `purelysharp_suggest_missing_enforce_pure_scope`
- `purelysharp_suggest_missing_enforce_pure_exclude_generated`
- `purelysharp_suggest_missing_enforce_pure_exclude_tests`
- `purelysharp_suggest_missing_enforce_pure_min_complexity`
- `purelysharp_suggest_missing_enforce_pure_namespace_filters`
- `purelysharp_emit_explanations`
- `purelysharp_report_exceptions`
- `purelysharp_checked_exceptions`
- `purelysharp_enable_effect_summary_json`
- `purelysharp_smt_mode`
- `purelysharp_smt_timeout_ms`
- `purelysharp_smt_method_budget_ms`
- `purelysharp_smt_max_path_conditions`
- `purelysharp_smt_max_expression_nodes`

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

`PurelySharp.Symbolic` exposes:

- `QuerySyntaxTree`
- `QuerySyntaxTreeAtPosition`
- `QuerySyntaxTreeLine`
- `QuerySyntaxTreeAllLines`
- `QueryFileLine`
- `QueryFileAllLines`
- `QuerySourceLine`
- `QuerySourceAllLines`
- line-level merged fact summaries via `SymbolicLineQueryResult`
- file-level aggregate summaries via `SymbolicFileQueryResult`
- post-query result filters via `SymbolicSourceQueryFilter`

The CLI mirrors the library:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
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

## Effect Summaries

PurelySharp still uses effect-summary JSON as data, but not as checked-in
generated outputs.

- The source manifest `PurelySharp.Analyzer/BuiltInEffectSummaryArtifactSpec.json`
  is checked in.
- During analyzer build/test, built-in summaries are regenerated into
  analyzer intermediates under `obj`.
- The generated intermediate directory is cleared before regeneration.
- The analyzer embeds only the current run's generated summaries.
- Built-in loaders do not probe `Assembly.Location`, analyzer directories, repo
  artifacts, or `AppContext.BaseDirectory` for built-in summary files.
- Explicit user additional files named `*.PurelySharp.EffectSummary.json` remain
  supported for external opt-in summaries.

`Tools/PurelySharp.EffectSummary` remains ad hoc tooling for calibration,
summary generation, report-only SDK/runtime analysis, and disposable BCL
fallback inventories. See
[docs/effect-summary.md](docs/effect-summary.md).

## Known Limitations

- Purity for arbitrary C# is undecidable; PurelySharp is a bounded practical
  analyzer, not a proof assistant.
- Unknown external calls remain conservative unless trusted by explicit
  attributes, configuration, catalogs, or validated summaries. BCL fallback
  guesses add diagnostic context but do not prove purity.
- Runtime-native, OS, environment, time, randomness, culture, reflection,
  threading, synchronization, unsafe, dynamic, and hidden implementation
  surfaces are intentionally conservative unless explicitly modeled.
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
  backlog instead.

## Roadmap

The detailed canonical backlog is
[REMAINING_ANALYZER_BACKLOG.md](REMAINING_ANALYZER_BACKLOG.md). This README
keeps only the product-facing summary.

- [x] Analyzer, attributes, code fixes, and NuGet/VSIX package layout.
- [x] Core CFG purity rules and explainable `PS0002` evidence.
- [x] Build-time regenerated built-in summaries with embedded-resource loading.
- [x] External additional-file summary support with identity validation.
- [x] Bounded Z3/SMT service with cache, timeout, budget, and fallback behavior.
- [x] Symbolic invariant library and CLI.
- [~] Broader SMT path facts for branch pruning, exception hazards, string, and regex conditions.
- [~] Deeper exception-flow summaries and metadata/library effect-summary consumption.
- [~] Better dispatch, delegate, LINQ, and enumerator precision.
- [~] Better fresh-object, fresh-array, alias, escape, and local ownership modeling.
- [~] More member-level SDK/BCL effect-summary coverage and catalog retirement.
- [ ] Rust-style borrow/resource ownership model.
- [ ] Mutual recursion proofs beyond the current bounded direct/self-recursive handling.
- [ ] More conservative Roslyn operation-shape reductions where sound regressions can be locked.
- [ ] Corpus-driven SDK coverage metrics that can report meaningful numerator/denominator data.
- [ ] Performance and profiling work that preserves analysis quality while reducing test/build cost.

## Verification

Use the repo wrapper for .NET commands so long-running tests run under the
expected Windows Job Object:

```powershell
.\scripts\Invoke-PurelySharpDotnet.ps1 build PurelySharp.sln --configuration Release
.\scripts\Invoke-PurelySharpDotnet.ps1 test PurelySharp.Test\PurelySharp.Test.csproj --configuration Release --no-build
```

Representative evidence suites:

- Packaging and generated-summary policy:
  [AnalyzerPackagingTests.cs](PurelySharp.Test/AnalyzerPackagingTests.cs)
- Z3 solver, budgets, caching, string, and regex:
  [SmtAnalysisServiceTests.cs](PurelySharp.Test/SmtAnalysisServiceTests.cs),
  [SearchLibZ3SmokeTests.cs](PurelySharp.Test/SearchLibZ3SmokeTests.cs)
- CFG/path-sensitive symbolic facts:
  [SemanticOracleSmtTests.cs](PurelySharp.Test/SemanticOracleSmtTests.cs)
- Invariant query API:
  [SymbolicSourceQueryLineTests.cs](PurelySharp.Test/SymbolicSourceQueryLineTests.cs)
- Exception summaries and additional-file summaries:
  [ExceptionSummaryCatalogValidationTests.cs](PurelySharp.Test/ExceptionSummaryCatalogValidationTests.cs),
  [EffectSummaryToolTests.cs](PurelySharp.Test/EffectSummaryToolTests.cs)
- Roslyn operation coverage decisions:
  [RoslynConstructCoverageTests.cs](PurelySharp.Test/RoslynConstructCoverageTests.cs)
- Boundary attributes, baselines, and code fixes:
  [BoundaryAttributeTests.cs](PurelySharp.Test/BoundaryAttributeTests.cs),
  [BaselineSuppressionTests.cs](PurelySharp.Test/BaselineSuppressionTests.cs),
  [PurelySharpCodeFixTests.cs](PurelySharp.Test/PurelySharpCodeFixTests.cs)
