# SharpProof Production Reduction Progress

This is the source of truth for the 20,000-line maintained-production reduction.
Tests are excluded from the metric and must not be deleted.

## Constraints

- Preserve diagnostics, attributes, CLI behavior, serialization, packages, and
  conservative `Unknown` semantics.
- Count tracked handwritten production C#, PowerShell scripts, generators, and
  maintained production specifications.
- Do not obtain reductions through minification, code in strings, hidden
  generated complexity, or moving maintained logic outside the metric.
- Delete superseded implementations in the same green tranche.

## Baseline

- Commit: `e6c324df`.
- Maintained production: 107,626 lines, including the reduction enforcement
  script introduced with this baseline.
- Required reduction: 20,000 lines.
- Completion ceiling: 87,626 lines.
- Baseline tests: 6,147 passing with two documented skips.

## Completed

- [x] Added a tracked maintained-production baseline covering handwritten C#,
  PowerShell build/generator scripts, and explicit production specifications.
- [x] Added a reporting command with milestone and final-target enforcement.
- [x] Added an architecture test that validates the 20,000-line target,
  baseline arithmetic, and non-test production categories.
- [x] Added the shared inferred-summary model with typed purity, effects,
  freshness, visibility, explicit unknown reasons, stable cache keys, and
  semantic differential comparison.
- [x] EffectSummary now projects its existing classification into that shared
  model without changing its serialized contract. Contracts owns the mapping
  so Analyzer can reuse it without depending on the executable tool.
- [x] Removed the manual `System.Type` and `System.RuntimeType` metadata-member
  wrapper catalogs. Generic call-graph inference now owns those classifications;
  six runtime metadata slices characterize the unchanged output.
- [x] Made `SharpProofTarget` the single query-target model across the public
  session API, Symbolic execution, Analyzer, and CLI. Deleted the duplicate
  internal target, enum, validation, and conversion adapter.
- [x] Routed divide, modulo, checked arithmetic, checked update, compound
  assignment, and checked conversion hazards through one operation-level
  candidate adapter. Deleted seven syntax-specific forwarding routes.
- [x] Removed the production `PurityPolicyAuditRegistry`, which duplicated
  analyzer behavior solely for test inspection. The retained audit tests now
  own their expected documentation contract and continue exercising the real
  configuration and precedence paths.
- [x] Made `PurityProofQuery` the only ProofCore hazard-classification request
  path and migrated wrapper-specific tests to it. Removed seven service-specific
  classification entry points, a conversion-only lowering adapter, and four
  unreachable helpers across ProofCore, Symbolic, CLI, and EffectSummary.
- [x] Deleted the 170-line `SymbolicQueryService` pass-through. Analyzer, CLI,
  session, and retained tests now use the same `SymbolicQueryExecutor`; the
  architecture test verifies that the legacy facade type is absent.
- [x] Deleted the 243-line `SymbolicSourceQueryService` facade. Query dispatchers
  now own the program-point, range, and proof components directly; retained
  source-query tests use executor-based helpers in `SharpProof.Testing`.
- [x] Removed the legacy invariant snapshot and implication entry points and
  their duplicate result DTOs. Tests now use `SymbolicProgramPointAnalysis` and
  the canonical condition-proof executor; display and fact merging live with
  their focused projection types.
- [x] Collapsed three runtime-hazard syntax-tree routes into one target-based
  dispatcher path. Removed unreachable source-proof and line-analysis helpers,
  the unused typed hazard wrapper, and a redundant project-session adapter.
- [x] Internalized the concrete public query records behind the existing
  `SharpProofQuery` factories. The session now dispatches simple requests by
  query kind, and the intentional breaking API snapshot no longer exposes eight
  constructor-heavy request implementation types.
- [x] Added one deterministic operation-first runtime-hazard lowering registry
  for arithmetic, index construction, Math.Abs/Clamp, argument guards, and
  switch no-match. Deleted their syntax dispatch branches and forwarding
  adapters; syntax remains only where Roslyn operations do not carry required
  source evidence.
- [x] Moved array/stackalloc lengths, element and Array.GetValue bounds,
  slicing, collection cardinality, and array-store mismatch discovery behind
  the same operation registry. Direct lowerer tests now exercise `IOperation`
  requests instead of compatibility-shaped metadata parameter lists.
- [x] Moved cast, nullable-value, null receiver, dynamic-binding, lock, regex,
  await, with-expression, foreach, and deconstruction hazards into operation
  lowerers. Removed the legacy syntax candidate factory; the renamed 31-line
  source factory owns only multi-descriptor throw/rethrow projection, with one
  explicit Roslyn member-operation gap retained at enumeration.
- [x] Removed the manual string-from-ReadOnlySpan and Path string-slice
  normalization wrapper rules. Generic fixed-point inference reproduces their
  runtime and analyzer classifications; the fresh-copy and span-search rules
  were independently shown to remain necessary and were retained.
- [x] Removed the stack-local char-builder and immutable string-rewrite wrapper
  rules after independent PathCore and analyzer string-suite characterization.
  Generic fixed-point inference now owns both classifications and their call
  families are deleted.
- [x] Removed char-scalar projection and guarded string-scan wrapper rules after
  runtime Char-helper and analyzer string-suite parity. Their shared manual
  identity/display parser was unreachable and is deleted with the rules.
- [x] Characterized the remaining string-hash and `System.Type` identity
  wrapper rules independently. Removing either changes its runtime catalog
  slice from `pure` to `conservative_unknown`, and string hashing also regresses
  analyzer string invariants, so both remain explicit semantic owners rather
  than legacy deletion debt.
- [x] Re-audited the CFG program-point collector, loop transfer, and
  EffectSummary assembly-document reader. Each remains reachable and owns
  distinct proof, loop-invalidation, or exception-summary behavior; none is a
  compatibility adapter that can be removed without deleting a feature.
- [x] Removed the remaining substring semantic-wrapper rule after its dedicated
  runtime slice proved generic fixed-point inference preserves the catalog.
- [x] Replaced 332 net lines of handwritten impacted-test path rules and live
  token scans with the generated inventory's exact dependencies, source-root
  ownership, and reverse project closure. Explicit analyzer modules retain
  validated partial selection; inferred production mappings conservatively
  require the full suite, so missing lexical references cannot under-test a
  semantic change.
- [x] Removed the handwritten BCL impurity member catalog for random-number,
  mutable-string, array-mutation, threading, XML, diagnostics, I/O, and assembly
  loading APIs. Generated purity summaries, configured and namespace policy,
  metadata analysis, and conservative BCL fallback now own these decisions.
  Diagnostics remain present and conservative; as an intentional unreleased
  API break, evidence sources and categories may now describe the inferred path
  instead of the deleted semantic-rule name.
- [x] Retired the three-entry handwritten analyzer test-impact manifest and its
  parallel validation, ownership, dependency-closure, and evidence engine. The
  generated repository inventory is now the sole production/project ownership
  source; inferred mappings conservatively require the full suite. Selector
  tests retain every case against the generated path, and no test was deleted.
- [x] Made the requested test lane the single project-routing owner for filtered
  runs. Deleted five handwritten fixture catalogs and the duplicate filter-name
  parser/repartitioner; impacted selection now infers Main versus Tooling from
  the generated fixture inventory. Fixed six-lane filters, worker caps,
  fail-fast settings, profiling, and concurrent execution remain intact.
- [x] Retired the completed raw-SMT migration hotspot inventory. The canonical
  operation-transfer plan records every migration phase and deletion gate as
  complete, while architecture tests enforce the supported session/query/result
  boundary and prevent raw SMT types from entering the public API. No build,
  CI, documentation, or product path consumed the duplicate source scanner.
- [x] Retired the standalone clone and risk inventories plus their stale manual
  clone adjudications. Neither scanner had a build, CI, documentation, or
  product consumer; both duplicated canonical source/architecture metrics, and
  their rules still named the removed `Shared` root and deleted query facades.
  Production metrics and generated test-impact ownership remain enforced.
- [x] Replaced the configuration-reference generator's handwritten C# source
  parser with an internal compiled-registry projection hosted by Symbolic CLI.
  The PowerShell layer now only invokes the bounded dotnet wrapper and applies
  golden-file verification. Registry defaults, scopes, value kinds, allowed
  values, descriptions, samples, and diagnostics produce byte-identical docs.
- [x] Made the full solution build the single release-validation owner. The
  retained release command delegates to `Invoke-SharpProofBuild.ps1 -Full`,
  which now explicitly enables VSIX packaging; focused validation produces the
  NuGet and VSIX artifacts with zero warnings. Removed the superseded
  historical LOC reporter and mutable baseline in favor of this enforced ledger.
- [x] Made `architecture-modules.json` the single module and dependency-graph
  owner for architecture validation, production metrics, and impacted-test
  inference. Removed the test-impact generator's duplicate 19-module table and
  unused generated project graph; selector closure now follows module identities
  directly, and all 40 selector characterizations preserve conservative routing.
- [x] Condensed 20 internal immutable query, witness, capability, complexity,
  budget, cache, and summary carriers around primary constructors. Preserved
  class identity and normalization semantics, and made the two witness property
  orders that mix stored and computed values explicit so CLI JSON remains
  byte-identical instead of depending on compiler metadata order.
- [x] Removed the condition-proof, source-query, and runtime-hazard dispatcher
  objects plus their duplicate validated-request carrier. The query executor now
  owns orchestration directly, and one immutable query context flows through
  source, range, program-point, hazard, capability, and complexity execution
  instead of being expanded into parallel option parameter lists. Architecture
  coverage prevents all four superseded adapter types from returning.
- [x] Removed ExceptionSummaryCatalog's unreachable second exception-fact graph.
  The live `SummaryExceptionInfo` and structured edge projection remains the sole
  owner consumed by diagnostics; focused source/edge tests preserve direct and
  transitive evidence, and architecture coverage prevents the duplicate fact
  model and parser from returning.
- [x] Replaced the separate array-backed and span-backed by-ref-like wrapper
  rules with one ownership-aware construction inference path. A differential
  snapshot preserved all 34,519 CoreLib purity classifications exactly, focused
  runtime slices preserve Span and MemoryMarshal behavior, and architecture
  coverage prevents the specialized manual predicates from returning.
- [x] Made `SymbolicQueryMetrics` the single aggregate count owner for source
  queries and CLI gates. Removed four independently calculated summary graphs
  and condensed condition-proof projection/copying to immutable leaves; exact
  compact, full-JSON, explain, text, package-consumer, and semantic aggregate
  tests preserve output and conservative proof behavior.
- [x] Made public `SharpProofAnalysisBudget` the sole analysis-limit model for
  sessions, Symbolic, Analyzer, project configuration, and CLI. One named
  registry now applies all eleven CLI overrides and infers analyzer-config keys;
  the duplicate engine limit class and manual truncation-code table are deleted.
  Unknown limit kinds remain explicit and architecture coverage prevents the
  parallel model from returning.
- [x] Removed the remaining built-in impurity namespace, type, and member tables.
  Generated purity summaries, configured overrides, semantic classifiers, and
  the conservative BCL shape fallback now own metadata classification. Deleted
  the `PurityCatalogSemantics` forwarding layer; SP0002 diagnostics remain
  present while evidence identifies the inferred fallback instead of a manual
  catalog hit, and architecture coverage prevents the tables from returning.
- [x] Made the exported code-fix provider the sole diagnostic dispatch owner.
  Deleted the handler interface, six one-method handler adapters, the family
  enum, and the parallel handler registry. The provider retains the exact
  fixable diagnostic set, titles, equivalence keys, and family behavior through
  direct dispatch; architecture coverage prevents both adapter files from
  returning.
- [x] Derived analyzer diagnostics from their descriptor fields and suppression
  configuration from the suppressor specifications. Deleted the manual
  descriptor list, test-only feature/configuration metadata, and two duplicate
  suppression-ID lists; focused descriptor, configuration, and suppression
  tests preserve discovery, ordering, documentation, and proof-backed behavior.
- [x] Made one unknown-reason taxonomy own proof, capability, complexity,
  runtime-hazard, ensures, and purity classification. Deleted the parallel
  domain dictionaries, consolidated raw-reason precedence, and condensed the
  related immutable proof/result metadata. Exhaustive enum, stable-code, JSON,
  retry/configuration, and architecture tests preserve conservative fallbacks.
- [x] Replaced 164 manually enumerated Span/MemoryMarshal, DateTime, and
  DateTimeOffset effect-summary roots with five family prefixes plus one
  specialized exclusion. Full callee analysis preserves all 325 preexisting
  generated catalog entries exactly and now infers 589 additional entries; the
  narrow `System.Type` list remains explicit because broadening it changed
  reflection diagnostics. A repository test prevents the three migrated
  families from returning to member-by-member maintenance.
- [x] Removed the dormant manual-catalog comparison normalization and
  aggregation engine after the inferred-purity migration made all three input
  catalogs immutable empty sets. The serialized comparison contract still
  emits its three empty arrays, generated-purity catalog construction remains
  intact, and architecture and packaging tests prevent the retired normalizers
  from returning or the boundary shape from drifting.
- [x] Removed ProofCore's general manual string-shape reconstruction before SMT.
  Exact Z3 string constraints now own length, contains, prefix, suffix, and
  overlap consistency. Retained only a narrow same-length concrete-value bridge
  needed to validate .NET regex semantics conservatively; focused regex and
  string tests preserve all outcomes, and architecture coverage prevents the
  duplicate shape engine from returning.
- [x] Removed the pre-release baseline migration and recursive compatibility
  readers from both the Baseline tool and analyzer. Current version-2 documents
  now use one canonical `diagnostics` array and exact entry field names;
  unversioned, additive-v1, nested-group, alias, and arbitrary-object traversal
  paths are rejected. The `migrate` command remains as a current-schema
  validator/normalizer, and focused analyzer, CLI, documentation, and
  architecture coverage preserves current SARIF and baseline behavior.
- [x] Made one immutable tree-configuration snapshot flow through the common
  method analysis context. Purity, suggestions, exception reporting,
  nullability, and diagnostic suppression no longer re-read AnalyzerConfig
  through feature-specific adapters. Registry value kinds now drive one
  validation path, and focused configuration tests preserve defaults, aliases,
  tree overrides, invalid-value diagnostics, and suppression behavior.

## Current evidence

- Maintained production: 100,492 lines (96,573 C#, 3,189 scripts, and 730
  specifications); net reduction: 7,134 lines; remaining reduction: 12,866.
  This tranche removed 203 maintained lines without deleting tests.
- Release solution build: zero warnings and errors.
- Six lanes: 6,181 passing tests and two documented skips.

## Milestones

- [x] 3,000 lines removed: inferred-summary foundation and first migrated rules.
- [x] 7,000 lines removed: manual semantic catalogs substantially migrated.
- [ ] 12,000 lines removed: canonical symbolic traversal owns all transfer paths.
- [ ] 16,000 lines removed: query, analyzer policy, and EffectSummary duplication removed.
- [ ] 20,000 lines removed: CLI/ProofCore cleanup and final dead-code deletion complete.

## Current tranche

Replace the next complete duplicated semantic owner whose superseded path
repays at least 200 maintained-production lines, preferring 350-line or larger
cuts in Symbolic, Analyzer, ProofCore, or EffectSummary. Prefer inferred or
canonical analysis over manual catalogs when characterization proves it is
complete; otherwise preserve the explicit owner. Require focused parity before
deleting the old path in the same tranche, and do not revisit the independently
required string-hash, type-identity, CFG/loop-transfer, EffectSummary
assembly-reader, generated-purity override catalog, remaining semantic-wrapper
groups, syntactic proof classifier, or impacted-test orchestration owners
without new call-site ownership or generic-instantiation evidence. Removing the
syntactic classifier caused 48 focused regressions, including proven results
becoming unknown for formulas outside the current Z3 encoding and zero-budget
proofs that intentionally avoid solver work.

The analyzer's remaining BCL invocation overrides are also characterized as
semantic owners, not adapters. Removing the Type, StringComparer,
FormattableString, Enum/Boolean/IPAddress parsing, and Unsafe routes caused 17
focused regressions; generated summaries alone do not yet preserve their
operand, dispatch, out-argument, and compiler-lowering semantics.
