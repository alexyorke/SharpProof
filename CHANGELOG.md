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
- Windows x64 worker containment, cache validation, and resource budgets.
- Three exact-version packages for the contract API, portable analyzer and
  generator, and Windows x64 verifier.
- Portable-PDB symbol packages with SourceLink bound to the packaged commit.
- Deterministic SHA-256 release manifests, SPDX 2.3 package/component SBOM
  generation, restored-dependency version checks, and separately permissioned
  GitHub build-provenance and SBOM attestations.
- Central package versions, dependency auditing, coverage baselines,
  changed-TCB coverage enforcement, retained/rotating fuzz campaigns, and
  scheduled security and acceptance workflows.
- Immutable tag/package/version/hash, master-ancestry, and predecessor-order
  validation plus an owner-gated NuGet
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
- Source-located structured compiler effect-violation candidates for the
  narrow unconditional direct-operation subset. The worker reports these as
  `Unknown(CounterexampleReplayFailed)` until it can replay an executable
  lowered-body effect trace.

### Changed

- The verifier consumes the final compiler compilation artifact instead of
  reconstructing a compilation from source files.
- Protocol version 9 and cache schema 11 distinguish undefined
  postconditions, non-replayable modeled calls, genuine replay failures,
  effect-evidence certainty, and explicit vacuity evidence.
- The semantic disk cache accepts only complete, postcondition-only responses
  whose claims are all replay-validated `Refuted` outcomes. It reconstructs
  scalar models, validates source intervals and entry assumptions, and replays
  every claim on both read and write eligibility; `Proven` and effect claims
  are never cached.
- Compiler artifact schema 8 records both raw and effective per-tree
  preprocessor symbols and binds them through compilation fingerprint domain
  5; worker-side validation rejects runtime-enabled ghost contracts.
- `require-proven` runs bypass the local semantic cache.
- Effect contracts consume independent read/write, allocation, capability,
  and escaping-exception evidence facets; an unrelated unknown facet no
  longer blocks a result.
- Effect analysis builds and caches only the requested reachable source-call
  graph while retaining deterministic exhaustive analysis.
- Every repeatable effect-attribute occurrence has its own stable manifest
  claim and dense ordinal while sharing the effective combined constraint.
- Cache identity now binds the canonical packaged worker runtime closure,
  including proof/runtime assemblies, JSON runtime assets, and managed/native
  Z3 payloads.
- Constructor postconditions report `UnsupportedBody` until constructor
  initialization semantics are represented.
- Package-consumer CI restores the exact same packed bytes across Windows x64,
  Linux x64, macOS x64, and macOS ARM64.
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
- Compiler artifact schema 8 binds exception constraints and exact witness
  hierarchies to canonical full assembly identities, including version,
  culture, and public-key token, plus constructed-type reference IDs, so
  aliased same-name assemblies cannot collide during independent worker replay.

### Fixed

- Explicit `ref` and `in` precondition arguments now read aliased local or
  parameter storage at call-entry state after later argument side effects;
  unsupported aliases remain `Unknown`.
- Synthesized `ref` and `in` extension receivers use the same call-entry alias
  semantics as explicit arguments, while nonlocal aliases remain `Unknown`.
- Contract-selected methods whose precondition call-site analysis is unknown
  now emit one SP0047; unselected advisory callers remain quiet.
- Concrete precondition replay preserves potentially failing object-to-string
  casts and proves them only from definite string runtime-type evidence.
- Contract discovery and compiler-bound ghost specification resolution now
  admit the contract API only from the matching `SharpProof.Attributes`
  assembly identity and exact compiler-elision shape. Source, project,
  version, key, and malformed API lookalikes produce visible incomplete
  analysis and contribute no proof facts.
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
