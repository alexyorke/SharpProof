# SharpProof comprehensive audit register

This register bounds the audit started from merged `master` at
`9b5342c0be2123b339dc4ba77c196ba2646e1cd4`. A supported-surface defect is
closed only when an executable regression discriminates the baseline behavior
from the fix. Speculative findings remain candidates and do not expand this
tranche.

## Closed in this branch

- [x] Publication sets reject pre-existing unowned destinations before any
  marker or output mutation, reject reserved marker-suffix destinations, and
  validate all ownership bindings before creating markers.
- [x] Publication and invalidation share cancellation-aware, deadline-bounded
  lock acquisition; partial acquisitions and partial marker creation are
  released or rolled back safely.
- [x] Relational summaries reject result/existential variables that alias one
  another or canonical/program variables, at decode and application boundaries.
- [x] Generated-code detection recognizes a conventional leading comment
  header instead of raw text in string literals, and honors generated-code
  attributes on associated properties and events.
- [x] Nested local/lambda call-site analysis applies the same suppression
  validation and outcome recording as top-level callables.
- [x] ContractFor validation diagnoses unmatched extra ordinary companion
  members without cascading after an incomplete target surface; exact matching
  includes varargs, calling convention, custom/ref modifiers, and oblivious
  receiver normalization.
- [x] ContractFor generator execution honors compilation-global profile-off
  configuration in custom analyzer hosts.
- [x] Request-bound response validation rejects a null expected manifest and
  returns an invalid result, rather than throwing, for malformed actual claims.
- [x] Cache eviction continues past undeletable entries, rolls back an
  over-budget new entry, protects the just-written path, and deletes only owned
  SHA-256-named entries.
- [x] MSBuild verifier output projects SP0047/SP0048 launcher lines as real
  warnings, preserving warning-as-error, binlog, and Visual Studio behavior.
- [x] Missing backend proof-label mappings fail closed rather than fabricating
  a `hygienic` justification.
- [x] By-value struct copies no longer appear as caller-owned writes.
- [x] Summary building preserves an earlier resource/depth/unsupported reason
  over a later call failure, and malformed foreign IDs/null environment terms
  return typed invalid results instead of throwing.
- [x] Compiler evidence rejects null syntax-tree paths, Windows case-alias
  additional paths, noncanonical effect ordering, ADS/device linked-module
  names, and oversized serialized manifests.
- [x] API-spec generation rejects coercive non-boolean/non-integer JSON scalar
  values before model generation.
- [x] Worker cancellation is rechecked through synchronous evidence/execution
  boundaries and before final responses; factory-less timeout retirement keeps
  timeout classification for unclaimed work.
- [x] Z3 unsat-core expression wrappers are disposed on success and early
  validation exits.
- [x] Launcher cancellation is not converted to infrastructure failure, and
  launcher code is directly covered by the cancellation meta-analyzer.
- [x] Packaged analyzer discovery has exactly one SharpProofAnalyzer and one
  ContractFor generator. The collector uses non-entrypoint
  `SharpProof.Analyzer.Core`; collectible discovery loads package bytes from
  streams so files are not pinned.
- [x] SPMETA001/004/009 apply to the entire analyzer-attached production
  compilation instead of bypassable namespace strings.
- [x] Cancellation-boundary exemptions accept only the exact Worker canceled
  response shapes; arbitrary success returns no longer bypass policy.
- [x] Maintained docs no longer claim unsigned referenced-assembly public-key
  authenticity and now include preview-support/release-constant sources.
- [x] Frozen MSBuild input validation enforces the exact active/retired
  SharpProof property set and all six schema/version pins.
- [x] Mutation catalog authority hashes Project, Original, and Mutated fields
  in addition to name/file/filter; deterministic evidence and the acceptance
  pin were updated together.
- [x] BuildTasks runtime-companion inventory is generated from the launcher
  argument catalog and exact-parity checked by generation/package/TCB gates.
- [x] Pilot projects derive the package version from `SharpProof.Release.props`
  instead of duplicating the preview version.
- [x] Production-complexity ceiling increases require a rationale token bound
  to the exact expression/decision/member limits; this audit's tightly bounded
  increase carries a dated architectural justification and regression.

## Deferred supported/release work

- [ ] Visual Studio qualification still needs an isolated local-feed,
  PackageReference-only x64 build and verification matrix, including net472,
  long/percent paths, cache/SARIF, concurrency, and VS-hosted cancellation.
- [ ] Release qualification must prove consumption of the exact package-job
  artifact and seal required VS host identity/zero required skips in evidence.
- [ ] Launcher cancellation has a suspended-process orphan window between
  process creation and job assignment; a deterministic startup seam or atomic
  job-list startup attribute is required.
- [ ] Companions emitted by another source generator are not visible to the
  ContractFor generator input compilation; a final-compilation design is
  broader than this audit.
- [ ] Reference-module hashing needs a catalog-owned byte limit shared across
  collector and worker layers.
- [ ] TCB path inventory should reject dot-segment aliases.
- [ ] Release environment/tag configuration and five real pilot libraries
  require external release infrastructure and project selections.

## Rejected or unproven candidates

- [x] Coalesce-assignment alias propagation was withdrawn after confirming the
  operation participates in the existing assignment path; no failing baseline
  reproduction was demonstrated.
- [ ] Aliased same-identity reference assemblies may select the wrong IL bytes;
  no compact Roslyn-admitted ambiguity reproduction was demonstrated.

## Validation evidence

- Focused Debug suites: ContractFor 50/50; Analyzer 208/208; Contracts 100/100;
  Effects 148/148; Specs 46/46; Summaries 10/10; Worker 428 passed with 6
  expected skips; SMT 21/21; Architecture 52/52; Meta 65/65.
- Package fixtures: BuildTask 9/9; LauncherArgument 56 passed with 1 expected
  skip; PackageLayoutSmoke 16/16; WorkerMsBuildIntegration 54 passed with 1
  expected skip; DependencyAudit 14/14; FinalCompilationProbe 7/7; and
  ReleasePublication 5/5.
- Full Debug completed in 1118.6 seconds: 1,628 passed and 8 expected skips.
  Its two performance-only failures were the known noisy advisory ratio and a
  forced-termination sample under parallel load; the latter passed immediately
  in isolation. Package completed with 233 passed and 2 expected skips.
- Full Release acceptance passed in 1328.5 seconds: deterministic generators,
  234-path TCB inventory, structural ratchets, a zero-warning/error build,
  1,628 tests with 8 expected skips, package, 1,000-case fuzzing, corpus, and
  performance gates were all green.
- Release performance evidence: advisory raw p95 ratio 1.097929, order-balanced
  median ratio 1.016486, and forced termination 674.540 ms.
- Generator/document/mutation checks passed: launcher-argument generation
  verify, maintained-readme verify, mutation evidence, and TCB inventory.
- Baseline full Debug elapsed time was 1564.9 seconds. Its only failure was the
  noisy performance ratio gate (advisory p95 1.219 versus ceiling 1.2).
