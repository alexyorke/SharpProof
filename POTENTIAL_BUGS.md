# Potential Bugs - Triaged Backlog

Last triaged: 2026-07-10

This file contains only code-confirmed behaviors that still need a regression
test or a deliberate behavior change. Original audit numbers are preserved so
old review notes remain searchable.

An entry belongs here only when it names a concrete code path and an observable
correctness, evidence, reliability, or bounded-resource risk. File size, style,
future-proofing, and unmeasured performance concerns belong in `PLAN.md` or a
profile-backed issue instead.

## P1 - Proof Correctness And Host Reliability

These can change a purity or reachability conclusion, omit a real hazard, or
grow process state across a long-running host. Add a failing regression before
changing proof behavior.

| Audit ID | Code-confirmed behavior | Required closure |
| --- | --- | --- |
| 54 | `ImpurityCatalog.IsKnownPureBCLMember` defaults a property signature without an accessor suffix to `.get`, including setter-only properties. | Add a setter-only catalog regression and select the existing accessor explicitly. |
| 60 | IR conversion lowering accepts only `Int32`-backed enum conversions although enum types are generally classified as SMT integers. | Cover byte, long, and unsigned enum conversions and preserve their integral values. |
| 65 | `DelegateCreationPurityRule` can return `Pure` without classifying a target that is not an `IMethodReferenceOperation`. | Add conversion/local-delegate target regressions and classify the target operation before returning pure. |
| 67 | `UsingStatementPurityRule` treats a missing dispose member as impure for a declaration but pure for an expression resource. | Add equivalent declaration/expression regressions and use one conservative policy. |
| 70 | `LoopPurityRule` skips runtime member checks for interface and metadata-only enumerator types. | Add an external custom-enumerator regression and route `MoveNext`, `Current`, and `Dispose` through external purity evidence. |
| 78 | `SmtSolver.PrepareConcreteFacts` can abort a whole query for divide/remainder terms whose divisor is not already proven non-zero. | Add a query Z3 can decide symbolically and narrow the conservative precheck to concrete evaluation only. |
| 89 | `PurityClassificationEngine` ignores an unresolved, non-interop external call after both resolution paths fail. | Add an effect-summary fixture and emit `unknown_callee` rather than allowing a pure classification. |
| 90, 98, 107 | `SymbolicProofService` result/encoded-state caches and the structural path-condition cache have no entry bound; the fallback cache is process-wide. | Add cache-size telemetry and bounded eviction tests that preserve successful cache reuse. |
| 94 | `TryCollectIrSimplePatternBranchAssumptions` returns no IR facts for false pattern branches. | Add `is not` and negated type-pattern reachability regressions, then encode the complementary facts. |
| 103 | Runtime-hazard expression unwrapping can strip an integral cast from a divisor before lowering it. | Add `(int)doubleValue` divide/modulo regressions and preserve the conversion term through trigger construction. |

## P2 - Precision And Evidence Quality

These are conservative or output-quality gaps. They should not outrank a P1
item unless a regression shows a wrong proof rather than an `Unknown` result.

| Audit ID | Code-confirmed behavior | Required closure |
| --- | --- | --- |
| 61 | Jagged indexed-variable round-tripping reconstructs the receiver as a synthetic variable name. | Add structural round-trip tests for `a[i][j]` and retain nested element terms. |
| 63 | Range/index shape write detection descends into unexecuted lambda bodies. | Add a captured-but-not-invoked lambda regression and stop traversal at callable boundaries. |
| 72 | Multiple guarded breaks use fewer fallback strategies than a single guarded break. | Add nested multi-break loop-exit regressions and share the single-break fallback pipeline. |
| 73 | Switch exit exclusions build pattern conditions without pattern bindings before negation. | Add bound-pattern exit regressions and prove that exclusions remain conservative. |
| 77 | Internal-only purity results report impurity feasibility as `Unsatisfiable` even though no impurity feasibility query ran. | Add evidence-contract coverage and report `Unknown` unless unsatisfiability was established. |
| 84 | `SmtAnalysisService` applies formula-node budgets before path-condition normalization. | Add redundant-`true` budget regressions and budget the normalized query while preserving truncation evidence. |
| 96 | Source condition proof fallback can surface the formula proof reason after an IR-state proof was the limiting step. | Add a dual-backend failure regression and attribute the final reason to the decisive backend. |
| 100 | Source-property inlining can replace an already-lowered IR condition formula. | Add a property case where the formulas differ and establish an explicit precedence rule. |
| 101 | Unknown `for`-loop complexity can aggregate pre-loop drivers twice. | Add exact driver/reason cardinality assertions and remove the duplicate aggregation. |
| 106 | Mixed IR/formula aggregate length triggers discard the IR subset and subject term. | Add mixed-dimension evidence tests and preserve partial typed evidence in the conservative result. |
| 115 | List-pattern element variables use `SmtFormula.ToString()` as part of solver identity. | Add identity stability tests and use a canonical structural key. |
| 119 | Affine contradiction recognition abandons the shortcut when negating `long.MinValue`. | Add boundary regressions and represent or isolate the non-negatable coefficient without aborting unrelated facts. |
| 125 | Exception-summary source-path fallback can emit a symbol key as though it were a file path. | Add incomplete-edge JSON coverage and keep source path absent when no path exists. |
| 126 | Alternate-containing-type key generation rewrites only keys with the display-name prefix, omitting other metadata key formats. | Add runtime implementation lookup coverage for every key family and rewrite each structured key form. |

## Disposition Of The 2026-07-07 Audit

All 123 entries present at the start of this triage are accounted for below.

### Fixed Or Covered By Regression Tests

IDs: 1, 5, 6, 7, 11, 15, 20, 31, 35, 40, 45, 48, 51, 52, 59, 71, 85,
86, 93, 102, 124, 135, 136, 137.

Notable closures include stable public unknown reasons, bounded-analysis
truncation evidence, SMT retry/recycle health controls, immutable catalogs,
validated embedded summaries, symbolic index identity, member-specific state
invalidation, job-bounded VSIX builds, and staged NuGet publication.

This triage added or confirmed regressions in commits `223c846b`, `08bb0e25`,
`f18d0ad3`, and `1f731cbd`.

### Maintenance Or Existing Roadmap Work, Not Standalone Bugs

IDs: 8, 9, 16, 17, 18, 19, 22, 25, 27, 38, 43, 56, 57, 74, 75,
79, 80, 81, 83, 87, 92, 104, 138, 139, 140, 141, 142, 144, 146,
149.

These cover file decomposition, fallback inventory, prospective assertions,
unmeasured performance, test-host cleanup, and packaging architecture. The
actionable packaging and concurrency portions already have dedicated high
priority items in `PLAN.md`; the rest require a profile or failing test before
they should become implementation work.

### Disproved, Duplicate, Or Intentional Conservative Behavior

IDs: 12, 14, 21, 24, 28, 29, 30, 32, 33, 36, 37, 39, 41, 42, 44,
46, 49, 50, 53, 55, 58, 66, 76, 82, 88, 91, 99, 105, 108, 109,
114, 116, 118, 121, 122, 131, 132, 134, 143, 145, 147, 148, 150.

Representative findings:

- resource-budget conversion saturates before its unsigned cast;
- `global.json` explicitly rolls forward to the latest feature band;
- a missing delegate-map entry during `+=` means the prior invocation list is
  unknown, so `Unresolved` is conservative;
- merging `Unresolved` delegate targets is absorbing and order-independent;
- formula fallback and unsupported nonlinear updates intentionally return
  conservative evidence instead of inventing facts;
- 32-bit subsequence limits model C# string/array index APIs, not arbitrary SMT
  integers;
- a negated conjunction cannot be flattened into independent public facts
  without changing its meaning;
- CLI console redirection is protected by an inner `finally`, analyzer options
  are immutable, and deterministic fuzz seeds are intentional reproducibility.

When a removed concern gains a minimal reproduction, re-add it under P1 or P2
with the expected observable result and the exact test needed to close it.
