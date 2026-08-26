# Changelog

All notable user-visible changes are recorded here. SharpProof follows
semantic versioning once a stable `1.0.0` release exists; preview releases may
contain documented breaking changes.

## Unreleased

### Added

- Compiler-produced claim manifests and lowered verification artifacts.
- Exact manifest/result accountability and deterministic worker summaries.
- Independent SMT-term and whole-body counterexample replay.
- Advisory and strict profiles with explicit incomplete-analysis diagnostics.
- Linux amd64 container worker containment, cache validation, and resource budgets.
- Three exact-version packages for the contract API, portable analyzer and
  generator, and Linux amd64 verifier.
- Portable-PDB symbol packages with SourceLink bound to the packaged commit.
- Deterministic SHA-256 release manifests, SPDX 2.3 package/component SBOM
  generation, restored-dependency version checks, and separately permissioned
  GitHub build-provenance and SBOM attestations.
- Central package versions, dependency auditing, coverage baselines,
  changed-TCB coverage enforcement, retained/rotating fuzz campaigns, and
  scheduled security and acceptance workflows.
- Immutable tag/package/version/hash, master-ancestry, predecessor-order, and
  full-release-delta coverage validation plus an owner-gated NuGet
  promotion workflow that sends `1.0.0-preview.1` to a protected private feed,
  then uses trusted publishing for public `preview.2`, `rc.1`, and `1.0.0`
  while promoting the already-tested bytes. Existing V3 packages must match
  the tested ZIP-entry payload before dependency-ordered main/symbol retry.
- Package-backed passing, diagnostic, mixed-outcome, strict-library, and
  host-policy samples with an isolated local-feed assertion runner.
- Exact IntelliSense XML documentation for every supported
  `SharpProof.Attributes` public API member.
- Conservative `EffectContractAttribute` defaults: external summaries are
  incomplete and nondeterministic until explicitly declared otherwise.
- An explicit SDK 9.0.300/Roslyn 4.14 minimum for enabled analyzer and
  generator hosts, with a clear older-host rejection.
- Opt-in deterministic SARIF 2.1.0 projection of validated claim, incomplete
  callable, assumption, and run-failure results.
- Compiler artifact schema 15 adds source-located, sealed effect replay events
  for unconditional object and array allocations. The worker independently
  interprets those events before reporting a `Refuted` allocation contract;
  every other direct effect candidate remains a typed `Unknown`.

### Changed

- The verifier consumes the final compiler compilation artifact instead of
  reconstructing a compilation from source files.
- Protocol version 11 and cache schema 13 distinguish undefined
  postconditions, non-replayable modeled calls, genuine replay failures,
  effect-evidence certainty, and explicit vacuity evidence.
- The semantic disk cache accepts only complete, postcondition-only responses
  whose claims are all replay-validated `Refuted` outcomes. It reconstructs
  scalar models, validates source intervals and entry assumptions, and replays
  every claim on both read and write eligibility; `Proven` and effect claims
  are never cached.
- Compiler artifact schema 15 records both raw and effective per-tree
  preprocessor symbols and binds them through compilation fingerprint domain
  5; worker-side validation rejects runtime-enabled ghost contracts.
- `require-proven` runs bypass the local semantic cache.
- Effect contracts consume independent read/write, allocation, capability,
  and escaping-exception evidence facets; an unrelated unknown facet no
  longer blocks a result.
- Effect analysis builds and caches only the requested reachable source-call
  graph while retaining deterministic exhaustive analysis.
- The unannotated advisory performance gate now pins temporary builds to the
  repository SDK, retains raw paired timing evidence, and uses adjacent
  opposite-order geometric ratios with a conventional median. It applies no
  retries or outlier removal. Its package fixture contains ordinary source and
  BCL calls so the measured analyzer run exercises no-precondition screening.
  Advisory compilations that conservatively contain no analysis trigger avoid
  constructing the heavyweight semantic session; configuration checks,
  runtime-contract rejection, and compiler-artifact collection still run, and
  strict mode never takes this fast path.
- Active advisory sessions classify the supported subset and run effect
  analysis only for explicitly selected methods. Call-site precondition
  analysis builds a CFG only when the cached contract binder finds an entry
  clause or cannot establish that none exists; malformed bindings and static
  initialization remain fail-closed.
- Contract-free advisory compilations now skip duplicate method-attribute and
  selected-contract work while retaining source and metadata precondition
  screening. Contract inventories, companion resolution, binders, API
  specifications, and effect analysis are initialized only when demanded;
  external closed preconditions remain visible to unannotated callers.
- Coverage collection uses the managed Microsoft collector so the exact
  trusted contract-API payload remains unchanged while tests run. Coverage
  instrumentation can no longer turn payload-identity checks into unrelated
  test failures, duplicate child-process builds cannot distort the coverage
  universe, and the redundant Coverlet dependency has been removed.
- Call-site precondition analysis now follows Roslyn child CFGs for executable
  local functions, lambdas, and anonymous methods. Each nested callable is
  analyzed once under its own flow state and outcome; quoted expression-tree
  lambdas remain conservative and do not produce execution diagnostics.
- Source and metadata effect summaries are imported only after their callee
  `Requires` and closed parameter preconditions are established. Unproven or
  invalidly placed entry contracts now produce typed incomplete effect
  evidence instead of a false `Proven` result.
- Every repeatable effect-attribute occurrence has its own stable manifest
  claim and dense ordinal while sharing the effective combined constraint.
- Cache identity now binds the canonical packaged worker runtime closure,
  including proof/runtime assemblies, JSON runtime assets, and managed/native
  Z3 payloads.
- Constructor postconditions report `UnsupportedBody` until constructor
  initialization semantics are represented.
- Package-consumer CI restores the exact same packed bytes across Windows x64,
  Linux x64, and the hosted macOS runner.
- Package builds run SDK package validation, and GitHub Actions dependencies
  are pinned to immutable commits.
- `System.Collections.Immutable` is updated to 9.0.18, and the verifier package
  carries that exact runtime asset so isolated consumers do not depend on an
  ambient shared-framework copy.

### Security

- Enabled analysis rejects `SHARPPROOF_CONTRACTS` from project constants,
  source directives, and generated trees so compiler-elided ghost expressions
  cannot execute in a supposedly verified runtime body.
- SAT models must exactly match the requested scalar model closure and pass
  independent replay before SharpProof emits `Refuted`.
- Compiler-only effect violation candidates cannot become `Refuted` without
  executable lowered-body replay evidence.
- Disk-cache payloads are treated as untrusted input; malformed, stale,
  unsupported, or non-replaying models are discarded and recomputed.
- Built-in API specifications approve only exact assembly-name,
  public-key-token, and reference-family triples observed across supported
  framework surfaces; unobserved identities or origins are not trusted.
- Canonical cache and evidence hashes encode nullability and value types, so
  text, numeric, enum, byte, null, and empty values cannot alias one another.
- Each verification lane owns its Z3 session and resource accounting;
  timeouts begin after lane acquisition and result ordering remains
  deterministic.
- Trusted complete effect contracts on bodyless source boundaries are honored,
  while malformed, incomplete, conflicting, or untrusted boundaries remain
  visible instead of disappearing.
- Compiler artifact schema 15 binds exception constraints and exact witness
  hierarchies to canonical full assembly identities, including version,
  culture, and public-key token, plus constructed-type reference IDs, so
  aliased same-name assemblies cannot collide during independent worker replay.

### Fixed

- Portable abstract flow no longer treats ordinary source `[NotNull]`,
  `[Positive]`, or `[InRange]` return annotations as established facts before
  verification. Only an explicit nonblank `[SharpProofTrusted]` boundary or
  an approved exact API specification can refine a callee result.
- Symbolic body execution now carries successful receiver and argument
  evaluation through modeled API calls in C# evaluation order. Partial terms
  embedded directly in a call can no longer be omitted from subsequent
  normal-completion predicates or spec guards.
- Symbolic body execution now carries assignment right-hand-side definedness
  into the evolving normal-completion predicate. An unused division, overflow,
  or other throwing expression can no longer yield a spurious counterexample
  that disagrees with whole-body replay.
- Explicit `ref`, `in`, and `ref readonly` precondition arguments now read
  aliased local or parameter storage at call-entry state after later argument
  side effects, while implicit readonly-reference rvalues retain their
  evaluation-time snapshot; unsupported aliases remain `Unknown`.
- Synthesized `ref` and `in` extension receivers use the same call-entry alias
  semantics as explicit arguments, while nonlocal aliases remain `Unknown`.
- Compound assignments and increments no longer substitute an operand or the
  old property target for the setter's computed `value`; setter preconditions
  remain visibly incomplete until the computed value is modeled exactly.
- Contract-selected methods whose precondition call-site analysis is unknown
  now emit one SP0047; unselected advisory callers remain quiet.
- Concrete precondition replay preserves potentially failing object-to-string
  casts and proves them only from definite string runtime-type evidence.
- Contract discovery and compiler-bound ghost specification resolution now
  admit the contract API only from the matching `SharpProof.Attributes`
  assembly identity, exact built-DLL SHA-256 payload, and compiler-elision
  shape. Source, project, version, key, payload, and malformed API lookalikes
  produce visible incomplete analysis and contribute no proof facts.
- Mutation-bearing argument expressions and expanded `params` elements no
  longer lend a recomputed scalar or element value to callee preconditions.
  These calls remain visibly incomplete until evaluation-time composite
  values and synthesized parameter arrays are modeled exactly.
- Unsupported value-type defaults and unary-plus expressions no longer acquire
  exact reference-shaped IR; they abstain with `UnsupportedType`.
- Analyzer, binder, and manifest discovery now share one effective contract
  source rule: valid direct clauses take precedence, otherwise a valid
  `ContractFor` companion remains usable despite misplaced or nested target
  clauses, and malformed companion intent stays visible.
- Every selected body with entry contracts or assumptions is admitted through
  symbolic subset validation even when it has no postcondition claim.
- Vacuity evidence now includes only source-level receiver, parameter, and
  explicit-precondition entry domains; facts learned from a body, result, user
  assumption, or API specification cannot make a proof vacuously succeed.
- Framework exception constructors are no longer blanket-trusted. Exact
  declarative throw and termination facets are required for a definite
  direct-throw witness; all other constructors fail closed.
- Object creation whose target type may still run a type initializer no longer
  produces a definite allocation-violation witness; its may-effect remains
  visible as incomplete allocation evidence.
- Preconditions on reduced extension-method calls now bind the extension
  receiver and reduced arguments to their original parameter ordinals.
- Closed parameter contracts now use one validator in the analyzer and binder;
  `out` parameters and non-reference-capable `[NotNull]` types fail visibly.
- Constructed generic exception types remain distinct in catch, allowed-
  exception, effect-set, manifest, and replay evidence; unbound generic
  exception contracts are rejected.
- Catch handlers are evaluated in source order. An exception rethrown by an
  earlier handler can no longer be consumed by a later sibling catch, and
  filtered, runtime-subtype, and unknown exception paths remain fail-closed.
- String `Length` and array `Length`/`LongLength` now contribute receiver-state
  reads, including alias-aware argument-region remapping. A complete empty
  effect contract can no longer be proven for these state reads.
- `SharpProofEffect.Throws` no longer implicitly permits managed allocation;
  every declared effect flag is enforced independently.
- Throwing a possibly null exception expression now includes the possible
  `NullReferenceException`; a proven non-null precondition removes that risk.
- Reference-array stores no longer report a spurious
  `ArrayTypeMismatchException` when the element type is sealed or the stored
  value is proven null.
- Exception replay no longer downgrades a definite violation because distinct
  same-simple-name assembly types collapsed to the same evidence identity.
- Nullable and native-sized division/remainder now contribute their exact
  modeled exception behavior to effect contracts.
- Constructor calls participate in concrete precondition checking.
- Clauses in local functions and lambdas are validated against their actual
  callable rather than being misreported on the containing method.
- Unsupported effect syntax no longer suppresses an independent concrete
  SP0027 precondition refutation.
- Built-in equality over `ulong`, native integers, floating-point, `decimal`,
  and enums now abstains instead of reusing reference-shaped IR; admitted
  bounded integer equality, including `uint`, remains exact.
- A postcondition that may throw yields typed `Unknown` instead of a fatal
  replay failure, and a modeled call that the independent interpreter cannot
  execute yields `CounterexampleNotReplayable`.

[Unreleased]: https://github.com/alexyorke/SharpProof/compare/master...HEAD
