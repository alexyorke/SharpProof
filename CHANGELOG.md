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
- Source-located structured effect-violation witnesses for the narrow
  unconditional direct-operation subset, with independent worker validation
  and SARIF/cache preservation.

### Changed

- The verifier consumes the final compiler compilation artifact instead of
  reconstructing a compilation from source files.
- Protocol version 8 and cache schema 9 distinguish undefined
  postconditions, non-replayable modeled calls, genuine replay failures,
  effect-evidence certainty, and explicit vacuity evidence.
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

### Security

- SAT models must exactly match the requested scalar model closure and pass
  independent replay before SharpProof emits `Refuted`.
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

### Fixed

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
