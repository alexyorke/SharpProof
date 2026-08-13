# SharpProof current convergence register

This is the finite active code and technical-debt register for the
container-only `1.0.0-preview.1` candidate. Historical audit queues and
completed Windows-hosted qualification records are archived under
`eng/agent-notes/archive/`; they are not active backlogs.

Exact-commit mutation, package, pilot, SBOM, and publication-plan artifacts
are generated after the final source commit. They are external qualification
evidence, not checked-in debt rows: recording their result in this file would
change the commit they qualify.

## Open demonstrated bugs

### SP-AUDIT-001 - Coalesce assignment omits observable writes (high)

- [ ] `OperationSupport.catalog.json` declares `CoalesceAssignment` exact, but
  `OperationEffectScanner.ScanCore` handles only simple and compound
  assignments. An `ICoalesceAssignmentOperation` therefore falls through to
  child traversal, which reads a field/property target without recording the
  conditional write or property setter effects.
- Supported impact: `field ??= value` produced no
  `WritesReceiverState` projection in the canonical container. A selected
  no-write/purity claim can therefore be established for a method that mutates
  caller-visible state.
- Reproduction: a temporary regression analyzed
  `value ??= new object()` on an instance field. The focused Effects test failed
  with expected `WritesReceiverState`, actual `None`.
- Required closure: add field, static field, argument-owned field, property,
  and local-only controls; model the conditional target write/setter or
  conservatively abstain; add a trusted mutation that removes the dedicated
  handling.

### SP-AUDIT-002 - Using disposal effects are omitted (high)

- [ ] `Using` and `UsingDeclaration` are cataloged as exact, but the effect
  scanner has no `IUsingOperation`/using-declaration handling. Default child
  traversal does not include the compiler's implicit `Dispose` invocation.
- Supported impact: a method using an `IDisposable` whose `Dispose` writes
  static state produced no `WritesStaticState`. Dispose capabilities, writes,
  and escaping exceptions can be omitted, allowing an unsound selected effect
  claim.
- Reproduction: a temporary regression analyzed `using (resource) { }` where
  source `Dispose` increments a static field. The focused Effects test failed
  with expected `WritesStaticState`, actual `None`.
- Required closure: resolve and join the exact synchronous dispose method for
  statement and declaration forms, including null/conditional disposal and
  source/API-spec summaries, or mark both operation shapes unsupported; add
  mutation-discriminating write, capability, and exception cases.

### SP-AUDIT-003 - Pilot evidence is not bound to package bytes (high)

- [ ] `Test-SharpProofPilots.ps1` checks only the three expected package
  filenames/version, uses the ambient global NuGet cache, and writes only the
  checkout commit into `report.json`. It neither validates each nuspec
  repository commit nor records package hashes.
- Certifier impact: all five pilots passed against packages embedding commit
  `a4c5100164d5923a68ace77bf66f552df96c502c`, while the generated report claimed
  current commit `a1c28160205b5376ec75cd4e11ef11de1ef122a4`. The reproduction used a
  fresh NuGet cache, so the stale package source itself is sufficient; a shared
  cache creates an additional same-version substitution path.
- Evidence: `artifacts/pilots/audit-stale-package-report.json` records five
  qualified pilots for the wrong checkout/package pairing.
- Required closure: parse and require the exact checkout commit from all three
  main packages, require one coherent package version/graph, hash the consumed
  package files into the report, and use a candidate-private NuGet cache. Add a
  stale-commit and stale-cache certifier fixture.

### SP-AUDIT-004 - Release qualification does not run or retain pilots (high)

- [ ] The tag-only `release-qualification` job in
  `.github/workflows/package-consumers.yml` runs acceptance, mutation,
  coverage, package consumers, publication planning, and qualification
  binding, but never runs `tooling pilots`. Qualification evidence also does
  not require or hash a pilot report.
- Certifier impact: the private-preview and public publication jobs depend on
  `release-qualification`, so a tag can reach a publication environment with
  no five-pilot result for those exact package bytes, contrary to the checked-in
  completion contract.
- Required closure: run the corrected pilot certifier against the downloaded
  exact-SHA package artifact in the tag job, retain/hash its report in
  qualification evidence, and make publication reject missing, stale, or
  package-mismatched pilot evidence.

### SP-AUDIT-005 - Initializer call sites escape SP0027 analysis (medium)

- [ ] The operation-block pipeline returns immediately when Roslyn assigns an
  operation block to a field or property rather than an `IMethodSymbol`.
  Constructor call-site discovery adds base/`this` initializers, but it never
  adds instance/static field or auto-property initializer expressions.
- Supported impact: definitely executed ordinary calls in initializers can
  violate compiler-bound `Contract.Requires` clauses without the documented
  SP0027 warning. This is not a conditional or unsupported-expression
  abstention; the same constant call in a method or expression-bodied property
  is reported.
- Reproduction: a temporary analyzer regression put `Positive(-1)` in an
  instance field initializer and `Positive(-2)` in an auto-property
  initializer. The canonical container run expected two SP0027 diagnostics and
  received an empty diagnostic set.
- Required closure: analyze executable initializer operation roots exactly once
  and associate their outcome with the relevant synthesized/declared
  constructor without duplicating diagnostics across constructor overloads.
  Add instance/static field, auto-property, valid-call, generated-tree, and
  multiple-constructor controls plus a mutation that removes initializer
  discovery.

### SP-AUDIT-006 - Publication lock symlinks mutate an unowned target (high)

- [ ] `LinuxPathIdentity.PublicationLock` calls `open(O_CREAT)` on the derived
  lock path before checking it with `lstat`, and does not use a no-follow open.
  A pre-existing lock-path symlink is therefore followed before SharpProof
  rejects it as non-regular metadata.
- Supported impact: an ordinary rejected publication configuration can create
  or open a file outside the publication set. In the demonstrated case the
  lock symlink pointed to a nonexistent victim; acquisition threw as intended,
  but the victim file had already been created. This violates fail-closed
  ownership even without concurrent path swapping.
- Reproduction: a temporary Linux package test created
  `result.json.sharpproof-publication-lock -> victim.txt`, acquired the result
  publication set, and asserted rejection plus byte-preserving non-creation.
  Rejection occurred, but `victim.txt` existed afterward.
- Adjacent reproduction: a temporary Linux worker test created an
  exact-content ownership-marker file outside the publication set and made
  `result.json.sharpproof-publication-set` a symlink to it. Publication-set
  acquisition was accepted without an exception. The derived marker path is
  therefore both followed before lock rejection and trusted outright during
  ownership validation.
- Required closure: open lock metadata with no-follow semantics, validate the
  opened descriptor with `fstat`, dispose it on every validation failure, and
  read ownership markers only from no-follow, regular-file descriptors whose
  identity remains stable. Add missing-target, existing-target, directory,
  FIFO/device, marker-symlink, and normal-lock controls plus a containment
  mutation.

### SP-AUDIT-007 - Release validators accept an SBOM bound to another commit (high)

- [ ] `New-SharpProofReleaseEvidence.ps1 -SbomPath` and
  `Test-SharpProofReleaseArtifacts.ps1` validate the SPDX graph and package
  hashes but never validate the commit-bearing `documentNamespace` or its
  commit-derived creation metadata.
- Certifier impact: release evidence can contain exact packages and an exact
  release manifest while the SBOM asserts a different source revision. The
  resulting SBOM hash is then blessed by `SharpProof.release.json` and
  `SHA256SUMS`, so the final validator cannot distinguish the stale provenance.
- Reproduction: in a clean detached checkout matching the existing package
  commit, the audit changed the SBOM namespace suffix to forty zeroes, reran
  release-evidence generation with that SBOM, and then ran the final artifact
  validator. Both commands exited zero and reported immutable artifacts valid.
- Required closure: require the exact deterministic namespace
  `.../sbom/<version>/<package repository commit>`, validate the commit-derived
  creation record, and preferably compare a supplied SBOM with a regenerated
  canonical document. Add stale-commit, stale-timestamp, malformed-namespace,
  and canonical round-trip certifier fixtures.

### SP-AUDIT-008 - Primary-constructor initializers escape SP0027 analysis (medium)

- [ ] The call-site pass handles explicit constructor declarations by adding
  their `base(...)` or `this(...)` initializer to the entry CFG, but a C# 12
  primary constructor is represented by a synthesized constructor whose
  declaration is the containing type. The operation-block pipeline requires a
  method declaration syntax reference, while the callable subset gate rejects
  type declarations, so the primary-constructor initializer is never analyzed.
- Supported impact: `sealed class Derived(int marker) : Base(-1)` produced no
  SP0027 even though `Base(int value)` has the compiler-bound precondition
  `Contract.Requires(value > 0)`. The equivalent initializer on an explicit
  constructor is a documented replayable call-site shape and is reported.
- Reproduction: a temporary canonical-container analyzer regression enabled
  contract diagnostics for the primary-constructor source, expected one
  SP0027, and received an empty diagnostic set.
- Required closure: map a primary-constructor type declaration and its base
  initializer to the synthesized constructor exactly once, without admitting
  the type declaration as an effect/postcondition-verifier body. Add class,
  record, valid-argument, generated-tree, and explicit-constructor controls
  plus a mutation that removes primary-initializer discovery.

### SP-AUDIT-009 - Container evidence path guards are case-insensitive (medium)

- [ ] Several Linux-only evidence commands validate repository-contained
  output paths with `StringComparison.OrdinalIgnoreCase`. On the canonical
  case-sensitive filesystem, a sibling directory whose spelling differs only
  by case therefore passes as if it were below the repository.
- Supported impact: coverage, corpus/performance evidence, fuzz output, pilot
  reports, mutation evidence/baselines, and release-configuration evidence can
  be written outside the checked-out repository despite their explicit
  containment contract. The mutation cleanup guard uses the same comparison,
  so it also has a wider deletion boundary than its message claims.
- Reproduction: a disposable canonical-container probe placed
  `Invoke-SharpProofGateEvidence.ps1` under `/tmp/.../Repo/scripts` and supplied
  `/tmp/.../repo/out/report.json`. The command passed the guard and created the
  case-distinct sibling output directory before later failing because the
  disposable fixture intentionally contained no gate project
  (`OUTSIDE_CREATED=1`).
- Required closure: centralize repository-relative path resolution with
  platform-correct ordinal containment, reject the repository root itself when
  a child is required, and use it in every evidence/output/baseline/workspace
  command. Add case-distinct sibling, exact child, `..`, absolute path, and
  cleanup-boundary fixtures plus a release-authority mutation.

### SP-AUDIT-010 - Filtered worker tests depend on Z3 test order (medium)

- [ ] The canonical `tooling test -Target SharpProof.Worker.Test` path does
  not install the contract-pinned native Z3 resolver for the worker test
  assembly. `SharpProof.Smt.Test` has an assembly-level
  `ContainerNativeLibrarySetup`, but the worker tests instantiate
  `IrSmtBackend` directly and rely on some unrelated earlier test to install a
  process-wide resolver.
- Developer and certifier impact: selecting the proof-kernel worker tests in
  the documented disposable container command failed eight cases with
  `DllNotFoundException: libz3`, while 73 non-Z3 cases in the same filter
  passed. A full run can hide the defect through test order, and a focused
  mutation/regression run can fail before exercising its mutant.
- Reproduction: `docker compose run --rm tooling test -Target
  SharpProof.Worker.Test -TestFilter
  "FullyQualifiedName~CompilerManifestArtifactTests|FullyQualifiedName~WorkerTcbEdgeCaseTests|FullyQualifiedName~AcyclicBlockPredicateExecutorTests|FullyQualifiedName~VerificationCacheTests"`
  produced 73 passes and eight native-load failures. The equivalent SMT test
  assembly passed 20/20 because it owns explicit resolver setup.
- Required closure: install and verify the exact container Z3 resolver in a
  worker-test assembly setup that runs for every filter and ordering, then add
  an isolated single-test invocation to the container command fixtures. Keep
  ambient `LD_LIBRARY_PATH` and system Z3 outside the trust boundary.

### SP-AUDIT-011 - Dirty source can be certified as the clean HEAD (high)

- [ ] The canonical `tooling pack` path derives `RepositoryCommit` from
  `git rev-parse HEAD` but does not require a clean tracked index/worktree or
  reject untracked production sources. The disposable container checkout then
  overlays the caller's entire working tree, so uncommitted code is compiled
  while every package and release document claims the unchanged commit.
- Certifier impact: a locally prepared or manually published candidate can
  pass package-graph, deterministic release-evidence, and immutable-artifact
  validation while its binaries do not correspond to the asserted Git tree.
  The same unchecked source overlay is available to other exact-commit local
  evidence commands.
- Reproduction: the audit added an untracked internal constant containing
  `SHARPPROOF_AUDIT_DIRTY_PACKAGE_20260811` to `SharpProof.Attributes`, then ran
  `docker compose run --rm tooling pack -Configuration Release`. The complete
  build, package-source validator, release-evidence generator, and final
  artifact validator exited zero. The accepted Attributes DLL contained the
  UTF-16 marker, while its nuspec and `SharpProof.release.json` both asserted
  clean HEAD `a1c28160205b5376ec75cd4e11ef11de1ef122a4`.
- Required closure: make every exact-commit package/evidence/publication entry
  require a clean tracked index and worktree and reject relevant untracked
  source/build inputs before the disposable clone is created. Record the
  verified Git tree identity in the evidence and add tracked-dirty,
  staged-dirty, untracked-source, clean-tree, and CI-checkout fixtures. The
  temporary marker source has been removed.

### SP-AUDIT-012 - Foreach omits implicit enumerator effects (high)

- [ ] `OperationSupport.catalog.json` classifies every Roslyn `Loop` as exact,
  and `OperationEffectScanner` handles `ILoopOperation` by scanning its child
  operations and adding `MayDiverge`. For `IForEachLoopOperation`, the
  compiler-selected `GetEnumerator`, `MoveNext`, `Current`, and `Dispose`
  members are semantic operations rather than ordinary child invocations, so
  none of their summaries is joined.
- Supported impact: a source enumerator whose `MoveNext` and `Dispose` both
  increment a static field produced `WritesStaticState = None` and a complete
  effect projection for the containing `foreach` method. Allocations,
  capabilities, and escaping exceptions from the same implicit members can be
  omitted as well, allowing selected purity/effect claims to be proven from an
  incomplete model.
- Reproduction: a temporary canonical-container Effects regression analyzed
  `foreach (var value in values) { }` over a pattern enumerator with effectful
  `MoveNext` and `Dispose`; the expected static write was absent.
- Required closure: handle synchronous foreach as its own operation shape and
  resolve/join all compiler-selected enumeration members with their actual
  receiver/element regions, including disposal and array/string intrinsic
  controls, or classify foreach as unsupported while retaining exact ordinary
  `for`/`while` loops. Add mutation-discriminating write, allocation,
  exception, pattern, interface, and no-dispose cases.

### SP-AUDIT-013 - Mutation qualification trusts a forged summary (high)

- [ ] `Invoke-SharpProofTrustedMutationsParallel.ps1` returns before running
  the campaign when an existing JSON file repeats eight scalar header fields.
  The reuse path does not validate the `mutations` array, registered names,
  individual outcomes, selected tests, logs, TRX files, or a digest/signature
  over the evidence.
- Certifier impact: the canonical `tooling mutation` command can report a
  complete 136-case trusted-boundary campaign even though no mutation was run
  and the supplied evidence contains zero result rows. A stale or fabricated
  ignored artifact can therefore satisfy an exact-commit qualification step.
- Reproduction: the audit temporarily supplied
  `artifacts/mutation/trusted-mutations.json` with the current commit,
  configuration, selection, catalog count/hash, `mutationCount = 136`,
  `killedCount = 136`, and `mutations = []`. `docker compose run --rm tooling
  mutation -Configuration Release` exited zero in 12 seconds and printed
  `Mutation evidence is already complete`. The pre-existing ignored evidence
  file was restored afterward.
- Required closure: route every reuse candidate through the same exact
  `Test-SharpProofMutationCatalog.ps1` validation as newly generated evidence;
  require all 136 canonical rows and names, killed outcomes, selected-test
  identities, and existing digest-bound log/TRX receipts. Bind the evidence to
  a clean Git tree and hash the validated record into qualification evidence.
  Add empty-array, duplicated-row, wrong-name, missing-log, forged-count,
  dirty-tree, and valid-reuse certifier fixtures.

### SP-AUDIT-014 - Empty symbol packages pass release validation (medium)

- [ ] Package-source, release-evidence, final-artifact, and publication-plan
  validation treat a `.snupkg` as valid when it contains one nuspec with the
  expected ID/version/commit. They do not require the matching portable PDBs,
  Source Link records, or correspondence between symbol and main-package
  assemblies.
- Certifier impact: the immutable release bundle can certify and publish an
  unusable or unrelated symbol package while all release manifests and hashes
  remain internally consistent. This defeats the checked-in promise that the
  exact debug/source artifacts belong to the promoted package bytes.
- Reproduction: the audit copied the current six package files, removed every
  `.pdb` entry from `SharpProof.Attributes.1.0.0-preview.1.snupkg`, and reran
  `New-SharpProofReleaseEvidence.ps1`. Evidence generation exited zero and
  blessed the modified symbol-package hash. `Test-SharpProofReleaseArtifacts`
  then exited zero and reported the bundle immutable for current HEAD.
- Required closure: validate the exact expected PDB entry set in every symbol
  package, require portable PDB format and canonical Source Link commit, and
  match each PDB's debug identifier to the corresponding DLL in the main
  package. Add missing-PDB, foreign-PDB, wrong-commit Source Link, duplicate,
  malformed-symbol-package, and exact-valid fixtures.

### SP-AUDIT-015 - Release evidence does not authenticate package payloads (medium)

- [ ] `New-SharpProofReleaseEvidence.ps1` classifies package DLLs as
  third-party only when their leaf name does not start with `SharpProof.`.
  It does not compare exempt assemblies with the catalog-owned SharpProof
  output closure or validate their assembly identity/hash provenance.
- Certifier impact: an arbitrary additional binary can be shipped in a
  release package without a third-party component, license record, notice, or
  SPDX package. Release hashes then make the incomplete SBOM internally
  consistent rather than exposing the missing component.
- Reproduction: the audit added a foreign byte payload as
  `tools/net9/SharpProof.Untracked.dll` to the current verifier nupkg, then ran
  release-evidence generation. The payload was ignored by third-party
  inventory and SBOM generation; `Test-SharpProofReleaseArtifacts.ps1` also
  exited zero and certified the modified bundle for current HEAD.
- Adjacent reproduction: the audit flipped one byte in the verifier package's
  catalog-pinned `libz3.so` without changing its size. Evidence generation and
  final artifact validation again exited zero. The separate
  `PackageGraphAndLayoutsAreExact` test correctly rejected the hash, so the
  tag workflow currently has a mitigating gate; the release-evidence and
  direct publication boundary is not independently authoritative.
- Adjacent reproduction: the audit added a second
  `lib/netstandard2.0/SharpProof.Attributes.dll` ZIP entry with different,
  invalid bytes to the current main package. Both release-evidence generation
  and final artifact validation exited zero and certified the ambiguous
  archive for current HEAD. The disposable package copy and probe script were
  removed.
- Required closure: derive the exact first-party assembly entry set from the
  generated package/runtime catalogs and exempt only entries whose names,
  assembly identities, and hashes match those owned outputs. Treat every
  other managed/native payload as third-party and require complete license and
  SBOM coverage. Validate every declared third-party entry against its
  catalog-owned size/hash as part of evidence generation and final publication
  validation. Add renamed-foreign-DLL, unexpected SharpProof DLL,
  altered-first-party DLL, altered-Z3, duplicate-entry, and exact-closure
  fixtures.

### SP-AUDIT-016 - Property and event accessor calls escape SP0027 analysis (medium)

- [ ] `RequiresCallSiteDiscovery.CreateCandidate` recognizes only
  `IInvocationOperation` and `IObjectCreationOperation`. Reads/writes
  represented by `IPropertyReferenceOperation` and subscriptions represented
  by `IEventAssignmentOperation` never become call-site candidates for their
  getter, setter, add, or remove method, even though those accessors and
  expressions are explicitly admitted by the documented analyzer subset.
- Supported impact: compiler-bound `Contract.Requires` clauses on a property
  getter or setter are not checked at ordinary property uses. Definitely false
  preconditions therefore produce no SP0027 while the equivalent ordinary
  method call does. The same omission applies to custom event add/remove
  accessors.
- Reproduction: a temporary canonical-container analyzer test defined a
  static getter containing `Contract.Requires(false)` and a setter requiring
  `value > 0`; a caller read the getter and assigned `-1` to the setter. The
  focused contracts-profile run expected two SP0027 diagnostics and received
  an empty diagnostic set.
- Adjacent reproduction: custom event add/remove accessors each required a
  non-null `value`; subscribing and removing `null!` expected two SP0027
  diagnostics and likewise produced none.
- Required closure: create getter/setter call-site candidates from property and
  indexer operations with compiler evaluation order, receiver, index
  arguments, and assigned value aligned to accessor parameters; likewise map
  event assignment value and add/remove direction. Deduplicate
  compound/increment operations and conditional access. Add static/instance
  getter, setter, indexer, add/remove, compound assignment, null-conditional,
  generated tree, valid-precondition, and mutation-discriminating cases.

### SP-AUDIT-017 - Publication-set path hashing is not injective (medium)

- [ ] `LinuxPathIdentity.PublicationSetId` hashes
  `string.Join("\n", canonicalPaths)` without length framing or escaping.
  Linux path components may contain newline characters, so distinct sorted
  path arrays can produce the same byte sequence and ownership marker.
- Supported impact: two sequential publication configurations can partially
  overlap one output while carrying different companion outputs, yet the
  persistent marker accepts both as the same set. Locking serializes the
  writers but cannot keep both request/result/manifest/SARIF generations
  coherent after the false identity match.
- Reproduction: a temporary canonical-container worker test formed two
  three-path sets whose newline-containing names serialize identically and
  that share a fourth ordinary output path. After acquiring the first set,
  acquisition of the second was expected to throw `partially overlap`; it was
  accepted without an exception.
- Required closure: hash a domain/version plus a count and length-prefixed UTF-8
  path sequence using the repository's canonical hash framing, or reject all
  control characters before marker/lock creation. Add newline, carriage-return,
  separator-like Unicode, ordinary paths, reordered sets, and true
  partial-overlap cases plus a mutation that restores delimiter joining.

### SP-AUDIT-018 - Definitely safe casts report impossible exceptions (medium)

- [ ] `OperationEffectScanner.ClassifyConversion` assigns both
  `InvalidCastException` and `NullReferenceException` to every unboxing
  conversion before accounting for a nullable target and the operand's known
  null value. A conversion from a null boxed value to `Nullable<T>` returns an
  empty nullable value and cannot throw either exception. The explicit
  reference-conversion branch likewise assigns `InvalidCastException` without
  checking that a null reference is always a valid result. Its nullable
  conversion branch also assigns `InvalidOperationException` whenever a
  nullable is unwrapped, even when managed flow proves it has a value.
- Supported impact: `(int?)(object?)null` produced both impossible exception
  types, and `(string)(object?)null` produced an impossible
  `InvalidCastException`. A local initialized as `int? value = 1` and returned
  as `(int)value` produced an impossible `InvalidOperationException`; all three
  projections were complete. This can reject a valid selected no-throw claim
  and makes effect evidence disagree with CLR conversion semantics for
  admitted conversion shapes.
- Reproduction: a temporary canonical-container Effects regression analyzed
  `public static int? Convert() => (int?)(object?)null`; it expected an empty
  throw set and received `InvalidCastException` plus `NullReferenceException`.
  An adjacent regression for `public static string? Convert() =>
  (string)(object?)null` received `InvalidCastException` instead of an empty
  throw set. A third focused regression initialized `int? value = 1`, converted
  it to `int`, and received a nonempty throw set.
- Required closure: classify nullable unboxing, nullable unwrapping, and
  explicit reference casts with abstract null/type state, eliminating
  impossible throws while retaining conservative failure for unknown or
  incompatible values. Add definitely-null, definitely-present, compatible
  boxed/reference, incompatible boxed/reference, non-nullable unbox, unknown
  operand, and mutation-discriminating cases.

### SP-AUDIT-019 - Unreachable catch handlers contribute effects (medium)

- [ ] `OperationEffectScanner.IsReachable` returns true for every operation
  lexically inside a catch, filter, or finally clause even when managed flow
  proves it unreachable. This avoids losing exceptional paths, but it also
  scans catch handlers for `try` regions that contain no throwing operation.
- Supported impact: an empty `try` followed by a catch that increments a static
  field produced a complete summary containing a static write. Impossible
  filters and their handler bodies can similarly add calls, capabilities,
  allocations, writes, and exceptions, rejecting valid selected effect claims.
- Reproduction: a temporary canonical-container Effects regression analyzed
  `try { } catch { state++; }`; it expected an empty write set and received a
  nonempty set from the unreachable handler.
- Required closure: compute handler reachability from the set of exceptions
  that can escape the protected region and the catch type/filter selection,
  while continuing to scan `finally` clauses that execute on reachable exits.
  Add empty/no-throw try, known thrown type, unknown call, constant true/false
  filter, rethrow, finally, and mutation-discriminating cases.

### SP-AUDIT-020 - Exact safe array stores report type-mismatch throws (medium)

- [ ] `ArrayStoreIsDefinitelyCompatible` suppresses
  `ArrayTypeMismatchException` only when the declared element type is sealed or
  the assigned value is null. It does not retain the exact runtime element type
  of a fresh array through a local, nor use assignability when covariance cannot
  have changed that runtime type.
- Supported impact: storing a string into a freshly allocated `object[]`
  produced a complete summary containing `ArrayTypeMismatchException`, although
  the store is necessarily valid. This can reject a valid no-throw claim for an
  ordinary supported array operation.
- Reproduction: a temporary canonical-container Effects regression analyzed
  `var values = new object[1]; values[0] = "value";`; it expected no
  `ArrayTypeMismatchException` and received one.
- Required closure: track exact array-allocation element types through local
  aliases and prove assignment compatibility when the runtime type is fixed;
  retain the exception for covariant aliases such as `object[] values = new
  string[1]`. Add fresh/local/parameter/field aliases, null, compatible and
  incompatible values, covariance, and a mutation-discriminating case.

### SP-AUDIT-021 - Documented ordinary interpolation is rejected (medium)

- [ ] The public language matrix admits ordinary interpolated strings and
  rejects only custom interpolated-string handlers, but
  `OperationSupport.catalog.json` includes `InterpolatedStringText` and
  `Interpolation` without including their parent `InterpolatedString`
  operation. `LanguageSubsetGate` therefore rejects every selected method
  containing ordinary interpolation before `OperationEffectScanner` can apply
  its exact constant-string handling.
- Supported impact: a `[ZeroAllocations]` method returning the compile-time
  constant `$"sharp"` receives SP0047 `UnsupportedOperationKind
  (InterpolatedString)` instead of establishing the exact allocation-free
  result promised by the documented subset.
- Reproduction: a temporary canonical-container analyzer regression enabled
  SP0047 for that selected method and expected no diagnostic; it received one
  SP0047 at the interpolation expression.
- Required closure: add the parent operation to the generated support catalog
  and implement a dedicated scanner branch that keeps constant interpolation
  effect-free while conservatively accounting for allocation, formatting
  calls, and possible exceptions in nonconstant ordinary interpolation. Keep
  custom handlers rejected. Add constant, string, primitive-format, throwing
  `ToString`, custom-handler, and catalog-removal mutation cases.

### SP-AUDIT-022 - ContractFor generator resolves profiles differently (medium)

- [ ] `AnalyzerConfiguration` and the compiler collector choose the first
  configured profile alias in their authoritative key order and disable
  analysis on an invalid value. `ContractForValidatorGenerator.IsProfileOff`
  instead scans all three aliases and returns off if any one equals `off`; it
  treats every invalid value as enabled.
- Supported impact: a custom host that supplies raw `advisory` plus build-
  property `off` runs analyzer/collector semantics but silently suppresses all
  ContractFor validation. Supplying an invalid profile disables the analyzer
  and collector with a configuration diagnostic while the generator continues
  and emits unrelated SPCF errors. One compilation therefore has no coherent
  SharpProof profile.
- Reproduction: a temporary generator regression compared default advisory
  diagnostics with conflicting `sharpproof_profile=advisory` and
  `build_property.SharpProofProfile=off`; the default emitted one SPCF error and
  the conflicting run emitted none. A second regression supplied
  `sharpproof_profile=invalid`; it expected the generator to remain disabled
  with the analyzer but received SPCF0004.
- Required closure: expose one shared profile-resolution result to analyzer,
  generator, and collector, including alias precedence, trimming, accepted
  values, provider failures, and invalid/conflicting aliases. Prefer rejecting
  conflicting nonblank aliases rather than silently selecting one. Add all
  alias permutations, invalid values, redundant equal values, provider failure,
  MSBuild-only, and mutation-discriminating parity cases.

### SP-AUDIT-023 - Empty nullable boxing reports an allocation (medium)

- [ ] `OperationEffectScanner.ClassifyConversion` classifies every boxing
  conversion as a managed allocation. CLR nullable boxing is conditional: an
  empty `Nullable<T>` becomes a null reference and allocates no box.
- Supported impact: `public static object? Box() => (int?)null;` produced a
  complete effect projection whose allocation kind was `Managed`. A valid
  selected `ZeroAllocations` claim is therefore rejected for a definitely
  allocation-free supported conversion.
- Reproduction: a temporary canonical-container Effects regression expected
  `EffectAllocationKind.None` and a complete projection for that method. It
  failed with actual allocation `Managed`.
- Required closure: use constant/abstract nullable state when classifying
  boxing. Empty nullable values must be allocation-free, known nonempty values
  must allocate, and unknown values must preserve the conditional possibility
  without inventing a definite witness. Add empty, nonempty, parameter,
  lifted-conversion, ordinary value-type boxing, and mutation-discriminating
  cases.

### SP-AUDIT-024 - Effects survive definite earlier execution failure (medium)

- [ ] `OperationEffectScanner.ScanCall` joins the resolved callee summary even
  when managed flow proves that an instance receiver is null. Runtime argument
  evaluation is followed by `NullReferenceException`; the target method never
  begins executing. More generally, call children are joined without
  left-to-right completion: a definitely throwing earlier argument does not
  suppress later argument or callee effects.
- Supported impact: a caller assigned `null` to a local and invoked a source
  instance method whose body increments static state. The caller received the
  callee's static write in a complete projection, although execution always
  throws before that write. This can reject valid no-write, pure, allocation,
  capability, or exact-throw claims.
- Reproduction: a temporary canonical-container Effects regression expected
  an empty write set plus `NullReferenceException`; the write set was nonempty.
  A second regression invoked `Target(Fail(), Mutate())`, where `Fail` always
  throws and both the later argument and target mutate static state; those
  impossible writes were likewise retained.
- Adjacent reproduction: a definitely-null `lock` retained the static write
  from its body even though `Monitor.Enter` throws first. An object creation
  whose constructor definitely throws likewise retained the static write from
  its object initializer, which runs only after successful construction. A
  combined focused regression reported both methods as writing static state.
- Constructor member initializers have the same defect on a separate path:
  `ScanConstructorMemberInitializers` sorts members by metadata name and joins
  every initializer independently. A first initializer that definitely threw
  `InvalidOperationException` did not suppress the later initializer's static
  write, even though construction can never reach it.
- Required closure: model receiver, argument, implicit runtime boundary,
  constructor, initializer, and protected-body evaluation in language order,
  requiring normal completion before advancing. Suppress callee effects for a
  definitely-null true instance receiver, retain both paths for unknown
  nullness, and do not apply that rule to reduced extension receivers where
  null is an ordinary argument. Add source/external methods, property
  accessors, first/middle/last argument failures, lock entry, object/collection
  initialization, array initialization, argument side effects, unknown
  receivers, conditional access, reduced extensions, and a mutation probe.

### SP-AUDIT-025 - Cancellation gate accepts a decoy canceled status (high)

- [ ] `CancellationBoundaryAnalyzer.ReifiesWorkerProgramCancellation` accepts
  a Worker `Main` catch when the return expression contains a local `Respond`
  call, any `WorkerResultAssembler.Create` call, and any descendant reference
  to `WorkerRunStatus.Canceled`. It does not prove that the canceled value is
  the `runStatus` argument of the response passed to `Respond`.
- Certifier impact: a trusted-boundary mutation can translate cancellation to
  `Failed` (or another non-canceled result), place an unrelated/dead
  `WorkerRunStatus.Canceled` comparison elsewhere in the return expression,
  and still pass SPMETA003. The cancellation policy can therefore certify the
  exact swallowing behavior it is intended to prohibit.
- Reproduction: a temporary Meta.Analyzers regression returned a conditional
  whose true branch was `Respond(Create(WorkerRunStatus.Failed))` while its
  constant condition mentioned `Canceled` twice. It expected one SPMETA003 and
  received zero.
- Required closure: validate one exact operation tree: the sole returned value
  must be the local `Respond` invocation, its response argument must be the
  exact `WorkerResultAssembler.Create` invocation, and that invocation's
  `runStatus` parameter must receive the exact `Canceled` enum field. Reject
  conditionals, decoys, alternate overloads, and detached calls; add mutations
  replacing, relocating, or conditionally wrapping the status.

### SP-AUDIT-026 - Pilot qualification accepts stale verifier outputs (high)

- [ ] `Test-SharpProofPilots.ps1` does not remove each pilot's prior
  `obj/.../SharpProof/result.json`, SARIF, cache, or logs before invoking
  restore/build. It accepts a zero build exit and then reads whatever result
  and SARIF files already occupy those paths, without binding them to the
  current invocation.
- Certifier impact: all five pilot libraries can be reported qualified without
  running restore, compilation, analyzers, the launcher, worker, or Z3. This is
  independent of SP-AUDIT-003's package-commit gap: even correctly identified
  package files do not prove that the retained pilot results came from them.
- Reproduction: a disposable canonical-container clone copied the five stale
  result/SARIF directories, put a fake `dotnet` first on `PATH`, and ran the
  real pilot certifier. The fake returned zero without doing work (and emitted
  the expected SP0027 text/nonzero status only for negative controls). The
  script exited zero in 7.5 seconds and printed `Qualified 5 pilot libraries`.
- Required closure: run every pilot in a fresh candidate-private output/cache
  directory, reject pre-existing outputs, and require newly created result,
  manifest, SARIF, and build-log identities bound to the exact package hashes
  and invocation. Validate monotonic creation/commit data rather than file
  presence alone. Add stale-result, stale-SARIF, no-op-build, partial-output,
  package-substitution, and clean-run certifier fixtures.

### SP-AUDIT-027 - Release-tag validation accepts branch refs (medium)

- [ ] `Invoke-SharpProofReleaseContainer.ps1 -Mode ValidateTag` performs its
  tag name, annotation, commit, and `origin/master` ancestry checks only when
  `GITHUB_REF` starts with `refs/tags/v`. Every other nonempty ref skips the
  entire validation block and is reported as a valid release identity.
- Certifier impact: the authoritative `tooling release-tag` command can certify
  a branch, a non-version tag, or another arbitrary ref. The current GitHub
  workflow's tag-only job condition mitigates its normal path, but local
  qualification and future workflow reuse can accept an invalid release
  identity instead of failing closed.
- Reproduction: in the canonical container, `GITHUB_REF=refs/heads/master`,
  `GITHUB_REF_NAME=master`, and `GITHUB_SHA` equal to the checkout HEAD made
  `tooling release-tag` exit zero and print `Release identity is valid` for
  `1.0.0-preview.1`.
- Required closure: unconditionally require exact
  `refs/tags/v<SharpProofPackageVersion>`, then require the matching annotated
  tag object, resolved tag commit equal to `GITHUB_SHA` and checkout HEAD, and
  ancestry from `origin/master`. Add branch, non-version tag, lightweight tag,
  wrong-version, wrong-commit, and exact-valid-annotated-tag fixtures.

### SP-AUDIT-028 - Qualification evidence blesses arbitrary package files (high)

- [ ] `Invoke-SharpProofReleaseContainer.ps1 -Mode
  WriteQualificationEvidence` trusts `GITHUB_SHA` and `GITHUB_REF_NAME`, then
  accepts every regular file whose name ends in `.nupkg` or `.snupkg`. It does
  not require the six expected artifacts, open NuGet packages, validate the
  three-package graph/version/nuspec commit, compare identity with checkout
  HEAD, or require the gate evidence whose completion `status: passed` claims.
- Certifier impact: a standalone or reused canonical qualification command can
  produce exact-looking passed evidence for arbitrary bytes and a nonexistent
  commit/tag. The current GitHub job's preceding steps reduce exposure in its
  normal sequence, but the retained qualification document does not prove
  those steps or the identity of the files it certifies.
- Reproduction: a disposable canonical-container clone supplied one text file
  named `not-a-package.nupkg`, forty zeroes as `GITHUB_SHA`, and
  `v999.999.999` as `GITHUB_REF_NAME`. `tooling release-qualification` exited
  zero and wrote `qualification.json` with `status: passed`, the bogus identity,
  and the text file's hash.
- Required closure: make qualification self-validating: invoke strict release
  identity validation, require checkout HEAD and a clean tree, validate the
  exact six-file artifact set and package graph/metadata, and bind the required
  acceptance, coverage, mutation, package-consumer, and corrected pilot
  receipts into the record. Add fake-file, missing-package, extra-package,
  wrong-commit/tag, missing-receipt, stale-receipt, and exact-valid fixtures.

### SP-AUDIT-029 - Failed publication-lock construction leaks descriptors (medium)

- [ ] `LinuxPathIdentity.AcquirePublicationSet` constructs its entire
  `PublicationLock[]` through a LINQ pipeline before entering the cleanup
  `try/finally`. If construction of a later lock throws, every earlier
  `SafeFileHandle` is abandoned without deterministic disposal.
- Supported impact: repeated ordinary invalid publication configurations can
  exhaust file descriptors in a long-lived Core MSBuild process, eventually
  breaking unrelated builds and containment checks. Garbage collection may
  eventually run the safe-handle finalizers, but the failure path has no
  bounded cleanup and can accumulate descriptors faster than collection.
- Reproduction: a temporary canonical-container worker regression used two
  ordered output paths and made only the second derived lock path a directory,
  causing its `open(O_RDWR)` to fail. Thirty-two rejected acquisition attempts
  increased `/proc/self/fd` by exactly 32.
- Required closure: construct locks incrementally inside an ownership-aware
  `try/finally`, disposing every successfully created lock when any later
  constructor or acquisition fails. Add first/middle/last constructor failure,
  acquisition timeout, cancellation, successful lease, repeated-failure file
  descriptor, and cleanup mutation cases.

### SP-AUDIT-030 - Definitely null conditional access retains skipped effects (medium)

- [ ] `ConditionalAccess` is cataloged as exact, but
  `OperationEffectScanner` handles it through default child traversal. The
  scanner therefore joins the conditional-access body even when managed flow
  proves the receiver is null and runtime execution skips that body.
- Supported impact: `Target? target = null; target?.Mutate();` produced a
  complete summary containing the source callee's static write. The equivalent
  known conditional and null-coalescing controls correctly excluded their
  skipped calls, isolating the defect to conditional access. This can reject a
  valid selected purity, no-write, allocation, capability, or exception
  contract.
- Reproduction: a temporary canonical-container Effects regression analyzed
  conditional-access, constant-conditional, and definitely-nonnull coalesce
  methods together. Only `ConditionalAccess` retained the skipped static write;
  the focused test failed with actual method set
  `["ConditionalAccess"]`, expected empty.
- Required closure: model the receiver's null/non-null split and scan the
  `WhenNotNull` subtree only on the non-null path; preserve both paths for an
  unknown receiver and avoid inventing an ordinary null-receiver exception.
  Add definitely-null, definitely-nonnull, unknown, nested access, property,
  indexer, invocation, argument-side-effect, and mutation-discriminating cases.

### SP-AUDIT-031 - Field-like event accessor claims are not discovered (medium)

- [ ] `ClaimManifestBuilder.DiscoverMethods` adds methods, explicit accessor
  declarations, and accessors from `BasePropertyDeclarationSyntax`, but it
  never visits `EventFieldDeclarationSyntax`. C# can apply a method-targeted
  SharpProof effect attribute on a field-like event to both synthesized add and
  remove methods, so those selected accessor symbols exist without any syntax
  node handled by the discovery switch.
- Supported impact: `[method: DoesNotThrow] public event Action? Changed;`
  gave both `Changed.add` and `Changed.remove` one real SharpProof attribute,
  while the compiler collector emitted zero manifest callables and zero claims.
  The documented callable subset explicitly includes event add/remove
  accessors, so strict verification silently omits selected obligations.
- Reproduction: a temporary canonical-container `ClaimManifestBuilder` test
  first asserted one attribute on each Roslyn accessor, then expected two
  manifest callables and claims. Attribute assertions passed; manifest length
  was zero.
- Required closure: discover each field-like event symbol from its variable
  declarator and add its synthesized add/remove accessors exactly once, while
  preserving deterministic IDs and avoiding the raise/backing-field surface.
  Add instance/static events, nullable/nonnullable delegates, multiple event
  declarators, explicit events, generated trees, profile filtering, analyzer/
  collector parity, and a mutation that removes event-field discovery.

### SP-AUDIT-032 - Primary-constructor contracts are absent from the manifest (medium)

- [ ] `ClaimManifestBuilder.DiscoverMethods` discovers declared method/accessor
  syntax and top-level synthesized entry points, but never maps a type
  declaration with a primary-parameter list to its synthesized constructor.
  Roslyn exposes that constructor and its parameter contract attributes even
  though it has no `BaseMethodDeclarationSyntax` node.
- Supported impact: `sealed class Subject([Positive] int value)` produced a
  real one-parameter constructor whose parameter carried the compiler-bound
  attribute, while the compiler collector emitted zero callables and zero
  assumptions. Instance constructors are in the documented callable subset,
  so the worker silently loses the selected precondition evidence/obligation.
- Reproduction: a temporary canonical-container `ClaimManifestBuilder` test
  resolved the synthesized constructor, first asserted its parameter had one
  attribute, then expected one manifest callable with one assumption. The
  symbol assertion passed and manifest length was zero.
- Required closure: map class/struct/record primary-constructor syntax to the
  exact synthesized constructor once, while keeping unsupported lowering typed
  and deterministic. Add class, struct, record class/struct, closed parameter
  attributes, trusted scopes, explicit base initializers, generated trees,
  ordinary-constructor controls, and a discovery mutation. Coordinate the
  analyzer call-site initializer fix in SP-AUDIT-008 without conflating the two
  independent ownership paths.

### SP-AUDIT-033 - Definitely null lifted operators report impossible exceptions (medium)

- [ ] Integral division and checked-overflow classification inspect the
  underlying scalar operation without first applying lifted nullable
  normal-completion semantics. If a nullable operand is definitely null, the
  underlying operator is skipped and the result is null, so it cannot throw
  an arithmetic hazard.
- Supported impact: null-left and null-right division, checked nullable
  addition, checked nullable increment, and nullable compound division all
  produced complete summaries containing impossible `DivideByZeroException`
  and/or `OverflowException` facts. Valid selected
  `DoesNotThrow`/allowed-exception contracts can therefore be rejected for
  supported scalar expressions.
- Reproduction: a temporary canonical-container Effects regression analyzed
  null-left and null-right methods together and expected neither exception.
  The focused division failure reported both method names as containing the
  impossible exceptions. A follow-up four-shape matrix reported
  `Add:OverflowException`, `Increment:OverflowException`, and both division
  exceptions for `DivideAssign`; its checked nullable conversion control was
  clean.
- Required closure: evaluate nullable presence before arithmetic hazards:
  definitely absent on any lifted input means no underlying exception,
  definitely present reduces to the scalar operation, and unknown presence
  retains the possible hazard. Add binary/unary/increment/compound shapes,
  divide/remainder, signed/unsigned, checked/unchecked, left/right/both-null,
  known-present, unknown, conversion controls, and mutation-discriminating
  runtime controls.

### SP-AUDIT-034 - Compiler identity and parse-option strings are not canonical (medium)

- [ ] `CompilationFingerprint.ValidSnapshot` accepts compiler version and
  assembly-identity fields with only `HasText`; `ValidReference` does the same
  for reference identity; and the snapshot/module validators accept MVIDs with
  only `Guid.TryParseExact(value, "D")`. The collector instead emits
  `AssemblyIdentity.ToString()`, PE assembly identity display names,
  `Version.ToString()`, and lowercase `Guid.ToString("D")` values. Syntax-tree
  `LanguageVersion` is likewise accepted as arbitrary nonblank text although
  capture emits only `CSharpParseOptions.LanguageVersion.ToString()`.
- Supported impact: uppercase compiler, C# compiler, and reference-module MVID
  strings and arbitrary nonempty version text are accepted after recomputing
  `CompilationSha256`. The same compiler identity can have multiple valid
  serialized forms, while compiler and reference identity text the collector
  can never produce passes canonical deserialization, weakening deterministic
  cache/provenance identity.
- Reproduction: one temporary canonical-container worker test replaced all
  three MVID categories with uppercase D-format GUIDs; a later test set
  `CompilerVersion` to `not-a-version`. Both recomputed the compilation
  fingerprint, serialized directly, expected `JsonException`, and received no
  exception. A third probe replaced a tree language version with
  `not-a-csharp-language-version`; canonical deserialization accepted it too.
  Two adjacent probes replaced the compilation assembly identity and an
  assembly-reference identity with `not-an-assembly-identity`; both also passed
  fingerprint recomputation and canonical deserialization. A final probe
  changed `AssemblyName` while retaining the original, individually valid
  `AssemblyIdentity`; that compiler-impossible cross-field mismatch was also
  accepted.
- Required closure: validate the exact producer-canonical `System.Version`
  round trip for both version fields and lowercase D-format round trip for every
  MVID field (or generate shared canonical identity-string rules from the
  schema), and require each assembly identity to parse and round-trip through
  Roslyn's canonical display-name representation (with module identity kept as
  the exact module name), and require the compilation assembly name to equal
  the parsed assembly-identity name. Add malformed/overlong/noncanonical
  identities and versions, valid-but-mismatched name/identity pairs,
  uppercase/mixed-case, braces, compact GUID format, nil/non-nil policy,
  lowercase valid, every producer-emittable C# language-version spelling,
  arbitrary parse versions, recomputed-hash, and canonical serialization
  mutation cases.

### SP-AUDIT-035 - Compile-time-false loops are classified as diverging (medium)

- [ ] Termination classification calls `ManagedAbstractFlow.IsAcyclic` over
  reachable Roslyn CFG blocks, but follows every regular successor from each
  starting block without excluding a constant-infeasible edge. A syntactic
  loop therefore supplies a back edge even when its condition is the constant
  `false` and the body can never execute.
- Supported impact: a complete effect summary for `while (false) { }` reported
  `EffectTermination.MayDiverge` instead of `Terminates`. A valid selected
  termination claim can consequently be rejected for an exact, bounded
  control-flow shape.
- Reproduction: a temporary canonical-container Effects regression analyzed a
  method containing only `while (false) { }`; the focused assertion expected
  `Terminates` and received `MayDiverge`.
- Required closure: perform cycle detection over feasible CFG edges, at least
  folding compiler constants before following conditional successors, while
  remaining conservative for unknown conditions. Add while/for/do forms,
  false/true/unknown conditions, unreachable-body effects, nested loops, and a
  mutation that restores raw syntactic-cycle classification.

### SP-AUDIT-036 - Release certifiers ignore SBOM license declarations (high)

- [ ] Release evidence generation and final artifact validation compare SPDX
  package/component identities, versions, hashes, containment, and dependency
  relationships, but never compare each package's `licenseDeclared` and
  `licenseConcluded` fields with the authoritative release and third-party
  component manifests.
- Certifier impact: a release can be blessed with an SBOM that claims no known
  license for SharpProof or any bundled dependency, while the separate release
  manifest still says the components are MIT. The retained, hashed SBOM is
  therefore internally inconsistent with the evidence that qualified it.
- Reproduction: a disposable copy of the current exact-commit package set
  rewrote both SPDX license fields to `NOASSERTION` for every SharpProof and
  third-party package. `New-SharpProofReleaseEvidence.ps1` regenerated the
  release manifest and `Test-SharpProofReleaseArtifacts.ps1` then reported the
  immutable artifacts valid for commit
  `a1c28160205b5376ec75cd4e11ef11de1ef122a4`; both commands exited zero.
- Required closure: derive the expected license per SPDX package from the
  package/third-party authorities and require exact declared/concluded values
  during generation and final validation. Also validate the canonical package
  download/file-analysis fields and add missing, `NOASSERTION`, wrong-license,
  duplicate-component, package-license, and canonical-SBOM mutations.

### SP-AUDIT-037 - Ordinary leading comments can suppress analyzer diagnostics (medium)

- [ ] `AnalyzerGeneratedCodePolicy.HasGeneratedHeader` scans every leading
  single-line and multiline comment and treats a case-insensitive occurrence
  of `<auto-generated` or `<autogenerated` anywhere in the comment as a
  generated-file marker. It does not require the conventional header token or
  its placement/shape.
- Supported impact: a handwritten source file whose leading documentation or
  license comment merely discusses the auto-generated marker is classified as
  generated. Unselected supported call sites in that file are then skipped, so
  real SP0027 contract violations disappear.
- Reproduction: a temporary canonical-container analyzer regression began an
  ordinary `Subject.cs` with `// This handwritten source discusses the
  <auto-generated marker.` and called a positive-requiring method with `-1`.
  The expected SP0027 set contained one diagnostic; the actual set was empty.
- Required closure: recognize only a canonical generated-header comment in the
  permitted leading position, ideally using one shared policy compatible with
  Roslyn's generated-code convention. Add explanatory/license comments,
  malformed markers, later comments, exact single/multiline headers,
  case/path/provider controls, and a mutation that restores substring matching.

### SP-AUDIT-038 - Final release validation trusts fabricated component inventory (high)

- [ ] `Test-SharpProofReleaseArtifacts.ps1` proves that the release manifest and
  SPDX graph agree with each other, but it never binds their third-party
  component IDs, versions, or packaged entry paths to
  `eng/release/third-party-components.json` or to the actual nupkg contents.
- Certifier impact: a self-consistent release evidence pair can replace a real
  bundled dependency with an invented component name/version while retaining
  the unchanged package bytes. The final publication validator then certifies
  a materially false software-bill-of-materials inventory for the exact commit.
- Reproduction: a disposable copy of the current package bundle replaced its
  first real third-party component with `Fabricated.Component` version
  `99.0.0`, changed the corresponding SPDX package ID and containment edge, and
  recomputed the SBOM artifact hash, byte count, release manifest, and
  `SHA256SUMS`. `Test-SharpProofReleaseArtifacts.ps1` exited zero and reported
  the immutable artifacts valid for commit
  `a1c28160205b5376ec75cd4e11ef11de1ef122a4`.
- Required closure: load the checked-in third-party catalog during final
  validation, require an exact canonical package/component/version/entry set,
  and independently reopen each nupkg to prove every inventoried entry exists
  and every bundled third-party payload is owned. Add invented, omitted,
  stale-version, wrong-package, wrong-entry, duplicate, reordered, and
  self-consistently rehashed evidence mutations.

### SP-AUDIT-039 - Request-bound results accept fabricated runtime provenance (high)

- [ ] `WorkerProtocolJson.ValidateForRequest` binds the request hash, compiler
  input hash, manifest, and budgets, but `WorkerVerificationSummary.Versions`
  is checked only for nonblank version strings and syntactically valid hashes.
  It is never compared with the worker closure and API-spec identities already
  used to construct the expected input hash.
- Supported impact: a worker result can claim an arbitrary worker version,
  API-spec version, worker binary digest, and API-spec content digest while
  passing the canonical request-bound validator. Published JSON/SARIF and
  downstream evidence can therefore report false runtime provenance even
  though the semantic result is bound to a different actual closure.
- Reproduction: a temporary canonical-container protocol test created an
  otherwise valid request-bound response, replaced both version strings with
  `fabricated-*` values and both provenance hashes with different valid
  lowercase SHA-256 strings, then expected `ValidateForRequest` to reject it.
  Validation returned `IsValid == true`.
- Required closure: pass an expected immutable runtime/spec identity into the
  request-bound validator (or derive and return the exact identity alongside
  the expected input hash) and compare all version-summary fields exactly.
  Add independent field mutations, swapped-valid hashes, stale versions,
  ordinary failure/cancellation responses, cache hits, canonical serialization,
  launcher publication, and a mutation that restores shape-only validation.

### SP-AUDIT-040 - Same-typed compiler variable ownership can be swapped (high)

- [ ] `CompilerLoweredArtifact.DecodeBody` checks that every program-parameter
  binding targets a distinct canonical parameter with the same IR type, but it
  does not bind that target to the source parameter's compiler ordinal.
  `ValidateVariables` similarly permits two same-typed `PreState` rows to swap
  their `CurrentStateVariable` sources because it checks type and injectivity,
  not the compiler-owned `pre:N` association.
- Supported impact: a self-consistent lowered artifact can make symbolic
  execution interpret the body parameter `left` as canonical parameter
  `right`, and can independently reinterpret `Old(left)` as the snapshot of
  `right`. Either substitution can turn an invalid postcondition into a proof
  candidate despite the compiler's real variable mapping.
- Reproduction: temporary canonical-container worker tests lowered methods
  with two used `int` parameters, then separately swapped the two
  `ParameterBindings.Target` indices and the two pre-state
  `CurrentStateVariable` indices. Both expected
  `CompilerManifestArtifactJson.DecodeCallables` to fail closed; both returned
  normally. The existing binding corruption control uses an `int` and a `bool`,
  so its swap is rejected only by type and does not discriminate ordinal
  ownership.
- Required closure: add compiler-owned parameter ordinal (or an equally
  authoritative symbol identity) to every binding and pre-state association,
  validate one-to-one matches against the canonical inventory, and reject rows
  the collector could not emit. Add same-type binding/pre-state swaps,
  arbitrary-local sources, unused parameters, generic names, valid mixed-type,
  canonical round-trip, counterexample replay, and proof-outcome mutations. If
  the wire shape changes, perform the required compiler-artifact schema bump
  rather than silently defaulting the field.

### SP-AUDIT-041 - Request-bound results accept fabricated evidence (medium)

- [ ] Protocol validation checks proof cores, counterexample models, effect
  witnesses, and assumption-use flags only for local shape or self-consistency.
  It never ties proof labels or model variables to the compiler artifact, never
  requires an effect witness to violate the sealed claim's specific contract
  kind, and deliberately omits `Used` from its manifest-assumption comparison.
- Supported impact: a response can remain request-, input-, manifest-, budget-,
  and result-set-bound while falsely explaining why a claim was proved or
  refuted. This violates the documented canonical-proof-core contract, permits
  invented counterexample assignments, can report a state write as the reason
  a `DoesNotThrow` contract failed, and can inflate the reported use of a
  trusted boundary without corresponding solver evidence.
- Reproduction: temporary canonical-container protocol tests passed four
  fabricated payloads through `ValidateForRequest`: a proven result with sole
  core label `fabricated-proof-core`; a refuted postcondition with invented
  model variable `fabricated = 999`; and a refuted `DoesNotThrow` claim whose
  witness contained only `WritesStaticState`; plus a proven result that changed
  a declared `TrustedBoundary` assumption from unused to used and recomputed
  the summary. An existing vacuity test likewise accepts `requires:0` although
  its fixture manifest declares no precondition, so current coverage codifies
  shape-only acceptance.
- Required closure: validate claim evidence with the decoded compiler artifact,
  or replace free-form rows with typed identities. Require every proof label to
  name admitted compiler/spec/domain/summary evidence, every model variable and
  value kind to match the claim's canonical replay inventory, and every effect
  witness to contradict its exact effect contract; bind every `Used` flag to
  admitted proof/vacuity evidence. Add fabricated/wrong-ordinal core, model
  name/type/value, mismatched effect kind/capability/throw hierarchy, assumption
  use, vacuity, empty hygienic, valid controls, canonical ordering, and
  mutations restoring each shape-only path.

### SP-AUDIT-042 - Partial-method effect violations are reported twice (medium)

- [ ] The analyzer registers both symbol and operation-block paths for partial
  methods. A definition symbol normalizes to its implementation for effect
  resolution, while the implementation operation block analyzes the same body
  again; there is no effect-analysis/reporting ownership gate equivalent to
  the call-site and attribute deduplication gates.
- Supported impact: one declared effect contract and one violating executable
  body produce duplicate diagnostics. This doubles build/IDE errors and can
  distort diagnostic counts or warning-as-error reporting for a standard C#
  partial-method form.
- Reproduction: a temporary canonical-container analyzer test declared
  `[EnforcePure] public static partial void Write();` and implemented it with a
  static-field write. The expected single SP0002 was reported twice. The
  attribute was correctly inherited, so this is duplicate analysis rather than
  a missing-contract false negative. The temporary test was removed.
- Required closure: normalize to one compiler-owned partial-method identity and
  atomically assign effect diagnostic/outcome ownership before either symbol or
  operation-block analysis reports. Preserve definition-only attributes and
  implementation-body syntax. Add definition/implementation attribute
  placement, identical/conflicting duplicates, valid/violating bodies,
  concurrent repeated analyzer runs, exact diagnostic location/count, outcome
  observer, and mutation tests that remove the ownership gate.

### SP-AUDIT-043 - Run status need not match result evidence (medium)

- [ ] `WorkerProtocolJson.ValidateRun` enforces only one direction of the
  status/evidence relation: timeout or cancellation evidence requires a
  compatible status. It does not require a `TimedOut` or `Canceled` status to
  have any corresponding callable or claim evidence.
- Supported impact: a request-, input-, manifest-, budget-, result-set-, and
  summary-bound response can report that verification timed out or was canceled
  even when every callable is complete and every claim is proven. Consumers
  therefore cannot trust the top-level completion classification they use for
  build policy and reporting.
- Reproduction: a temporary canonical-container protocol test started from the
  valid all-proven response fixture, changed only `RunStatus` to `TimedOut` and
  then `Canceled`, and passed each through `ValidateForRequest`. Both responses
  remained valid. The temporary test was removed.
- Required closure: derive the exact admissible run status and failure reason
  from validated protocol errors, callable coverage/reasons, and claim
  outcomes/reasons, then require equality rather than one-way implications.
  Add all-proven timeout/cancel/failure, genuine timeout/cancel, mixed fatal,
  empty-manifest, protocol-error, cache-hit, valid complete, and reverting
  mutation cases.

### SP-AUDIT-044 - Release artifact roles are not bound to file types (medium)

- [ ] `Test-SharpProofReleaseArtifacts.ps1` requires three `package` and three
  `symbols` rows with the expected IDs, but it never requires a package row to
  name the ID/version `.nupkg` or a symbols row to name the matching `.snupkg`.
  Its SBOM package checksum follows whichever row merely says `package`.
- Certifier impact: the standalone immutable-artifact gate can bless an
  evidence bundle that treats symbol packages as main products and main
  packages as symbols, so its success does not establish the stated artifact
  roles. `Publish-SharpProofRelease.ps1` independently checks file extensions
  and currently rejects the inverted bundle before planning or publication;
  that later check mitigates, but does not make the advertised final validator
  authoritative on its own.
- Reproduction: a disposable canonical-container copy of the current package
  bundle swapped `kind` between every main/symbol pair, repointed each SPDX
  package checksum to the newly labeled main artifact, and recomputed the SBOM
  hash/size, release manifest, and `SHA256SUMS`. The canonical final validator
  exited zero and certified the inverted bundle for current HEAD. The temporary
  copy and probe script were removed.
- Required closure: derive the exact filename and extension from package ID,
  version, and role; open each archive and enforce main-versus-symbol layout and
  matching nuspec identity/commit before using its hash in the SBOM or publish
  plan. Add role swaps, renamed extensions, cross-ID pairs, duplicate roles,
  valid controls, plan/publish consumers, and a mutation restoring label-only
  validation.

### SP-AUDIT-045 - Portable IR accepts compiler-impossible unused metadata (medium)

- [ ] `PortableIrGraphCodec.Decoder` validates and materializes every type,
  identity, variable, member, operation, and term row, but it never requires
  the resulting metadata closure to be exactly reachable from the graph roots
  and program. The compiler encoder builds these tables on demand and cannot
  emit an unused row.
- Integrity impact: multiple canonical schema-12 artifacts and hashes can
  represent the same compiler-lowered callable, and an untrusted artifact can
  carry arbitrary inert metadata that the worker accepts as compiler-owned.
  This weakens canonical provenance, cache identity, and resource accounting;
  later code cannot infer that successful hydration proves the artifact was in
  the encoder's image.
- Reproduction: a temporary canonical-container worker test appended one
  unused `PortableIrOperation("unused")` to an otherwise valid contract graph,
  serialized and canonically deserialized the artifact, then expected callable
  hydration to reject it. `DecodeCallables` accepted the artifact without an
  exception. The temporary test was removed.
- Required closure: require the exact encoder-reachable metadata closure (or
  deterministically re-encode and compare every table/slot) before returning a
  decoded graph. Add unused type, identity, variable, member, operation, term,
  and block fixtures; duplicated/equality-collapsing and valid shared-row
  controls; and a mutation that removes the closure check.

### SP-AUDIT-046 - Container toolchain validation accepts decoy pins (high)

- [ ] `Test-SharpProofContainerContract.ps1` validates Dockerfile authorities
  with unanchored whole-file regex presence checks. It requires the catalog's
  exact `ARG ...=<image>@<digest>` text to occur somewhere, but does not parse
  the effective Dockerfile instruction or require that each authority is
  declared exactly once. Similar substring checks protect Compose platform and
  container-entry contracts.
- Certifier impact: a Dockerfile can retain the reviewed SDK pin as a decoy and
  redeclare the same build argument with another image before `FROM`. The
  canonical contract gate reports success even though a build resolves a
  different base. A crafted compatible image containing the pinned SDK could
  therefore preserve subsequent build behavior while bypassing the intended
  base-image digest boundary.
- Reproduction: the audit temporarily added a second
  `ARG DOTNET_SDK_IMAGE=...9.0.300...@sha256:...` immediately after the
  catalog-owned 9.0.316 declaration, then ran canonical-container `tooling
  contract`. The command exited zero with `SharpProof container contract
  validation passed.` The temporary Dockerfile mutation was removed.
- Required closure: parse a deliberately constrained Dockerfile authority
  grammar (or generate the authoritative prefix), require exactly one global
  declaration and one matching `FROM` consumer for every pinned image, and
  validate the effective Compose service contract structurally rather than by
  substring. Add duplicate/redeclared/unused/comment-decoy pins, alternate
  `FROM`, duplicate platform keys, canonical files, and a certifier mutation
  that restores presence-only matching.

### SP-AUDIT-047 - SBOM validators accept fabricated topology (medium)

- [ ] `New-SharpProofReleaseEvidence.ps1` and
  `Test-SharpProofReleaseArtifacts.ps1` require each catalog-owned `CONTAINS`
  edge to occur once and require exactly two `DEPENDS_ON` edges, but they never
  reject additional `CONTAINS` relationships or other fabricated relationship
  rows. Package rows are selected by name/version, but their SPDX IDs are not
  required to be unique or equal their catalog-derived IDs, and
  `documentDescribes` is not checked for exact correspondence. Their "exact
  package/component graph" check is therefore only a partial required-subset
  check.
- Certifier impact: an otherwise authentic bundle can falsely claim that a
  package contains a component owned by another package. The manifest, SBOM,
  package bytes, and checksums can all remain internally hash-consistent while
  the final validator certifies materially false supply-chain topology or an
  ambiguous package identity graph.
- Reproduction: the audit copied the exact-HEAD release bundle, added
  `SharpProof.Attributes CONTAINS Microsoft.Z3 4.12.2`, recomputed the SBOM
  hash/size in `SharpProof.release.json` and `SHA256SUMS`, and ran the canonical
  final artifact validator. It exited zero with `Validated immutable
  SharpProof 1.0.0-preview.1 artifacts`. A second rehashed-bundle probe assigned
  `SharpProof.Attributes` the same SPDX ID as `SharpProof`; it was also
  accepted. Both disposable bundles were removed.
- Required closure: derive the complete canonical relationship set from the
  exact package graph and third-party inventory, require set equality with no
  duplicate, dangling, unknown-type, or extra edges; require globally unique,
  canonical SPDX IDs and exact `documentDescribes` membership; and share that
  validator between evidence generation and final validation. Add extra/
  dangling/cross-package/duplicate relationship fixtures, duplicated or
  swapped IDs, orphan descriptions, canonical generated evidence, and a
  certifier mutation that restores required-edge-only validation.

### SP-AUDIT-048 - Pre-launch failures leak invocation directories (medium)

- [ ] Every build allocates a GUID-scoped
  `obj/.../SharpProof/runs/<invocation>` directory for its compiler manifest.
  `_SharpProofVerifyCore` removes that directory only after `RunVerifier`.
  Configuration, manifest, launcher, worker, and other MSBuild `<Error>` paths
  before the task bypass cleanup, leaving the compiler-emitted manifest and
  ownership metadata indefinitely.
- Supported impact: repeated ordinary configuration failures accumulate one
  unique directory per build under `obj`. This is unbounded local disk growth
  on the supported container/MSBuild path and persists until the consumer
  manually cleans intermediates.
- Reproduction: a disposable isolated-feed strict consumer compiled valid
  source twice with `SharpProofVerifyPolicy=invalid`. Both builds failed at the
  documented policy validation, and the probe found two distinct directories
  under `obj/Release/net8.0/SharpProof/runs`. Syntax-error and semantic-error
  controls left zero directories and were rejected as non-reproductions. The
  disposable consumer was removed.
- Required closure: place invocation cleanup in an MSBuild finally-equivalent
  target that runs for every post-compilation success/failure/cancellation path,
  while preserving files long enough for result classification and diagnostics.
  Add every pre-launch `<Error>` condition, task launch failure, cancellation,
  successful verification, repeated-build, and cleanup-failure controls plus a
  mutation that restores success-only `RemoveDir` placement.

### SP-AUDIT-049 - Release validation accepts ambiguous JSON evidence (medium)

- [ ] Release evidence and SPDX documents are parsed with PowerShell
  `ConvertFrom-Json`, then validated only through the resulting object model.
  The certifier does not reject duplicate JSON member names, require the
  generator's canonical serialization, or compare a supplied document with a
  canonical reserialization. PowerShell silently keeps the later duplicate.
- Certifier impact: one supposedly immutable validated evidence file can assert
  two conflicting package versions, repository identities, artifact graphs, or
  SBOM fields. Consumers that retain the first occurrence can make a different
  release decision from the SharpProof validator, while package bytes and all
  existing checksums remain unchanged.
- Reproduction: the audit copied the exact-HEAD release bundle and inserted a
  forged `"packageVersion":"999.0.0-forged"` immediately before the original
  `"packageVersion":"1.0.0-preview.1"` in `SharpProof.release.json`.
  `Test-SharpProofReleaseArtifacts.ps1` exited zero and certified the bundle for
  current HEAD. The disposable copy was removed.
- Required closure: parse release and SBOM JSON with a duplicate-property-
  rejecting reader, reject unmapped fields and noncanonical ordering/format,
  and validate exact byte equality with the deterministic canonical form where
  these files are authoritative. Add duplicate top-level/nested/array-row
  properties with forged-first and forged-last order, unknown properties,
  reordered/whitespace variants, canonical generated documents, and a
  certifier mutation restoring permissive `ConvertFrom-Json` parsing.

### SP-AUDIT-050 - Coverage validation trusts a report-defined universe (high)

- [ ] `Test-SharpProofCoverage.ps1` builds each project's coverable-line
  denominator exclusively from sequence points present in caller-supplied
  Cobertura files. It requires at least one path per configured project, but
  never proves report assembly identity, expected source-file membership, or
  the complete sequence-point universe produced by the current binaries.
- Certifier impact: a fabricated or truncated coverage report can omit nearly
  all production code and certify 100% project/aggregate coverage. When
  `ComparisonRef` has no changed TCB lines, the changed-TCB lane also reports
  100%, allowing a release coverage receipt with no meaningful test execution.
- Reproduction: the audit generated one Cobertura report containing exactly
  one `hits=1` line for one `.cs` file in each of the 22 baseline projects and
  ran the canonical coverage validator with `ComparisonRef=HEAD`. It reported
  `coveredLines=22`, `coverableLines=22`, every project at `100.0`, changed TCB
  at `100.0`, and overall `passed=true`. The forged report was removed.
- Required closure: derive the expected production source/sequence-point
  universe from exact current PDBs (or a separately authenticated canonical
  inventory), require exact assembly/module and source-document identity, and
  reject missing/duplicate/foreign reports and omitted sequence points before
  calculating percentages. Bind the retained summary to report hashes and the
  exact commit. Add one-line/truncated/missing-project/wrong-assembly/foreign-
  source/duplicate-report fixtures, canonical merged reports, and a certifier
  mutation restoring report-defined denominators.

### SP-AUDIT-051 - Nested catch rethrows fabricate an outer exception (medium)

- [ ] `EffectExceptionFlow.ContainsRethrow` treats every descendant bare
  `throw;` as rethrowing the exception owned by the catch block being
  inspected. It excludes lambdas and local functions, but does not exclude a
  rethrow lexically owned by a nested catch.
- Supported impact: when an outer catch handles exception A and a nested catch
  rethrows exception B, the effect summary reports both A and B as escaping.
  `[DoesNotThrow]`, `[AllowedExceptions]`, and exact effect-contract outcomes
  can therefore be rejected by an exception that cannot escape at runtime.
- Reproduction: a focused container regression caught
  `InvalidOperationException`, then inside that handler caught and rethrew an
  `ApplicationException`. The expected escaping set contained only the inner
  application exception; the analyzer returned both exception types. The
  temporary test was removed.
- Required closure: associate a bare throw with its nearest enclosing catch
  and count it only when that catch is the block currently being analyzed.
  Add direct, nested, sibling, nested-try, lambda/local-function, filtered,
  and no-rethrow controls plus a mutation restoring descendant-wide matching.

### SP-AUDIT-052 - An empty release secret set cannot be certified (medium)

- [ ] `Test-SharpProofReleaseConfiguration.ps1` declares the `Expected`
  parameter of `Require-SetMembers` as a mandatory `object[]` without
  `AllowEmptyCollection`. The authoritative environment contract intentionally
  gives public `nuget.org` an empty `secrets` array, so binding that exact
  valid value throws before evidence can be written.
- Certifier impact: the checked-in owner-configuration contract cannot pass
  its own canonical validator. A final release either remains blocked or must
  bypass/change the asserted configuration outside the reviewed evidence path.
- Reproduction: the canonical container ran the validator against mocked
  GitHub responses through the public-environment secret check. PowerShell
  terminated with `Cannot bind argument to parameter 'Expected' because it is
  an empty array.` The mock and output were removed.
- Required closure: admit an empty expected set explicitly and compare sets
  exactly. Add zero/one/multiple expected member controls for tags, variables,
  and secrets, including the checked-in public-environment contract.

### SP-AUDIT-053 - Release environments may authorize extra tags (high)

- [ ] `Test-SharpProofReleaseConfiguration.ps1` uses
  `Require-SetMembers` for environment deployment tag policies. It proves only
  that required tags are present and never rejects additional tag patterns.
- Certifier impact: an environment intended to authorize three exact release
  tags can also authorize `*` (or another unreviewed tag pattern) while the
  release-configuration certifier reports success. This defeats the
  environment policy as an independent publication boundary.
- Reproduction: after temporarily working around SP-AUDIT-052 only, a mocked
  GitHub API returned every contract-required policy plus an additional `*`
  tag policy for both NuGet environments. The canonical validator exited zero,
  wrote passed evidence, and printed that both publishing environments were
  validated. The contract change, API mock, and evidence were removed.
- Required closure: require exact set equality for deployment tag policies and
  reject duplicate, broader, differently cased, branch, or otherwise invented
  policies. Keep variables/secrets least-privilege checks explicit rather than
  silently conflating required presence with exact authorization. Add wildcard,
  extra exact tag, branch policy, missing tag, duplicate, and exact-contract
  fixtures plus a certifier mutation restoring subset-only validation.

### SP-AUDIT-054 - Protected release tags may have unreviewed bypass actors (high)

- [ ] `Test-SharpProofReleaseConfiguration.ps1` selects an active tag ruleset
  and requires the `deletion` and `update` rule types, but never inspects the
  ruleset's `bypass_actors` collection or bypass modes.
- Certifier impact: the evidence can claim immutable protected release tags
  while an administrator, repository role, team, or app has an always-on
  bypass that permits the protected update/deletion operations. This defeats
  the independent tag-identity boundary the release procedure relies on.
- Reproduction: a mocked GitHub API returned the exact required tag includes
  and rule types plus a repository-role bypass actor with
  `bypass_mode: always`. After temporarily bypassing only SP-AUDIT-052's
  empty-array binder failure, the canonical container validator exited zero
  and reported both publishing environments and the tag ruleset valid. The
  contract edit, API mock, and generated evidence were removed.
- Required closure: make ruleset bypass authority catalog-owned and exact;
  preferably require no bypass actors for release tags, or explicitly allowlist
  actor identity, type, and non-always mode with a documented operational need.
  Add always/pull-request-only/unknown actor, extra actor, missing field,
  duplicated actor, no-bypass, and exact-allowlist fixtures plus a certifier
  mutation that again ignores bypass authority.

### SP-AUDIT-055 - Protected release tags may be explicitly excluded (high)

- [ ] `Test-SharpProofReleaseConfiguration.ps1` selects a ruleset when its
  `conditions.ref_name.include` array contains every catalog pattern, but it
  never inspects `conditions.ref_name.exclude`.
- Certifier impact: the same nominally active ruleset can explicitly exclude a
  real release tag from deletion/update protection while retained evidence
  claims that the required release-tag boundary is valid.
- Reproduction: a mocked GitHub API returned the exact required includes and
  rule types, no bypass actors, and an exclusion for
  `refs/tags/v1.0.0-preview.1`. After temporarily bypassing only
  SP-AUDIT-052's empty-array binder failure, the canonical validator exited
  zero and reported the tag ruleset and both publishing environments valid.
  The contract edit, mock, and evidence were removed.
- Required closure: validate the complete effective ref condition rather than
  inclusion membership alone: require the exact catalog-owned include set and
  no overlapping or invented exclusions (preferably an exact empty exclusion
  set). Add exact-tag, wildcard, negating-exclusion, unrelated-exclusion,
  duplicate/case variant, missing field, and canonical fixtures plus a
  certifier mutation restoring include-only validation.

### SP-AUDIT-056 - Generated companions depend on filename conventions (medium)

- [ ] Final-compilation ContractFor validation filters syntax trees through
  `AnalyzerGeneratedCodePolicy`, which recognizes provider classification,
  conventional generated suffixes, or an auto-generated header. A peer source
  generator is permitted to add an ordinary `.cs` hint without that header;
  the incremental ContractFor generator cannot see the peer's output, and the
  final analyzer silently excludes it.
- Supported impact: malformed or overlapping generated ContractFor companions
  can participate in the final compilation and runtime binding without any
  SPCF diagnostic solely because their generator chose an ordinary filename.
- Reproduction: the existing generated-companion regression was changed only
  from `AddSource("GeneratedContracts.g.cs", ...)` to the legal hint
  `GeneratedContracts.cs`. The malformed empty companion previously produced
  SPCF0004; the focused canonical-container run returned no diagnostics. The
  temporary filename change was removed.
- Required closure: identify post-generator trees from authoritative final-
  compilation provenance rather than filename/header heuristics, while keeping
  handwritten generated-code suppression separate. Add ordinary, `.g.cs`,
  header, provider-classified, peer-generator ordering, profile-off,
  overlap/malformed, and handwritten controls plus a mutation restoring the
  heuristic-only filter.

### SP-AUDIT-057 - A resultless launcher makes verification succeed (high)

- [ ] The frozen public `SharpProofLauncherPath`/`SharpProofToolsDirectory`
  properties allow a project to select an arbitrary managed launcher. The
  verification target checks only that this file exists and treats process
  exit code zero as success; neither `RunVerifier` nor MSBuild independently
  requires or validates a published result before printing its result path.
- Supported impact: an enabled strict verifier build can report success with
  no worker request/result evidence and no proof execution. The stable result
  is invalidated first, so the build's claimed result path need not even exist.
- Reproduction: a focused canonical-container package test supplied an
  ordinary local console DLL that reads the startup line and exits zero as
  `SharpProofLauncherPath`. The consumer build exited zero, printed
  `SharpProof verifier result: .../result.json`, and reported `Build
  succeeded` although no result was created. The temporary regression was
  removed.
- Required closure: remove project-controlled runtime-closure overrides from
  the supported interface or authenticate every selected launcher/tool file
  against the package-owned closure. Independently require, parse, and bind the
  exact invocation result before the build task can return success. Add
  resultless, malformed, stale, wrong-request, nonzero, valid package launcher,
  and override-rejection fixtures plus containment/evidence mutations.

### SP-AUDIT-058 - Relational-summary provenance is not authenticated (high)

- [ ] `CompilerLoweredArtifact.ValidSummaryEvidence` validates source and
  specification-pack provenance as only a syntactically valid SHA-256 plus an
  origin-shaped identity. It never binds a specification-pack digest to the
  catalog content identified by `pack@version`, nor a source-summary digest to
  the summarized callee/body. Implementation-IL evidence need only match some
  module in the compilation reference closure, not the callee's owning module.
- Supported impact: a lowered relation can retain arbitrary or relabeled
  provenance while entering worker/Z3 reasoning unchanged. The proof core can
  therefore attribute a result to an audited pack/source/module that did not
  authorize those relation bytes.
- Reproduction: a focused worker regression produced a valid proven
  `dotnet.scalar@1` summary, changed only its evidence digest to 64 lowercase
  `b` characters, and required `DecodeCallables` to reject it. The decoder
  returned successfully. The temporary mutation was removed.
- Required closure: recompute or look up the exact specification-pack content
  digest from the catalog; bind source summaries to canonical callee/body
  identity; bind IL summaries to the actual resolved member and owning module,
  including dependency evidence. Add wrong-pack digest/identity, wrong source
  body/callee, unused-reference module substitution, transitive dependency,
  and exact-valid controls plus provenance mutations.

### SP-AUDIT-059 - Cancellation rethrow ignores replacing finally blocks (high)

- [ ] The SPMETA003 cancellation policy accepts a catch when its first
  statement is a bare `throw;`, without checking enclosing `finally` control
  flow. A later finally can replace the cancellation with another exception or
  diverge forever.
- Certifier impact: trusted production code can convert cancellation into an
  infrastructure failure or nontermination while the soundness meta-analyzer
  certifies the catch as an immediate cancellation rethrow.
- Reproduction: a focused meta-analyzer test used two cancellation catches
  whose first statement was `throw;`; one enclosing finally threw
  `InvalidOperationException`, and the other looped forever. An empty-finally
  method served as a valid control. The expected two SPMETA003 diagnostics were
  both absent. The temporary regression was removed.
- Required closure: prove the rethrow reaches the method boundary through all
  enclosing finally clauses; reject replacement throws, nonreturning finally
  bodies, and other paths that prevent propagation. Add empty/normally
  completing finally, replacing throw, return/branch legality, divergence,
  nested try/finally, and outer-catch controls plus a mutation restoring the
  catch-body-only check.

### SP-AUDIT-060 - Managed flow loses post-catch execution state (high)

- [ ] `ManagedAbstractFlow` makes throws bottom and propagates only regular CFG
  successors. It does not model structured exception edges into handlers or
  merge handler exit states into the post-try continuation. The scanner forces
  handler operations themselves reachable, but not later operations whose only
  path runs through a catch.
- Supported impact: definite effects and exceptions after a handled exception
  can disappear from a complete summary. Purity, no-throw, allowed-exception,
  and effect-contract claims may therefore be accepted using an impossible
  pre-handler state.
- Reproduction: a focused effect test initialized a divisor to one, threw and
  caught `InvalidOperationException`, assigned zero in the catch, then divided
  by that local. The expected certain `DivideByZeroException` was absent and
  the throw set was empty. The temporary regression was removed.
- Required closure: model exceptional CFG transfer into catches/finally and
  merge normally completing handler states into the continuation, preserving
  definite facts only when valid on every escaping path. Add exact/base/
  filtered catches, catch assignments, multiple handlers, finally completion,
  rethrow/nonreturning handlers, post-handler effects, and mutation cases.

### SP-AUDIT-061 - Ref/out calls do not update local ownership regions (high)

- [ ] Local effect regions are built from declarators and assignment
  operations before scanning. A call with a `ref` or `out` local havocs scalar
  facts but never updates that local's pointee/ownership region from the
  callee's by-reference assignment.
- Supported impact: after a callee aliases a local to caller-owned state,
  later mutations through that local can be classified as writes to an empty
  region. A complete effect summary can therefore omit argument-state writes
  and incorrectly satisfy purity/effect contracts.
- Reproduction: a focused effect test called `Alias(input, out alias)`, then
  assigned `alias.Value = 1`. The complete summary did not contain
  `EffectRegionId.Parameter(0)`. The temporary regression was removed.
- Required closure: propagate by-reference postconditions/alias regions across
  direct calls, joining every possible assigned pointee and conservatively
  handling opaque calls. Add `out`, `ref` reassignment, unchanged ref,
  conditional callee assignment, fresh/null/static/receiver aliases, multiple
  by-ref parameters, opaque calls, and mutation-discriminating purity cases.

### SP-AUDIT-062 - Compound assignment omits conversion effects (high)

- [ ] `OperationSupport.catalog.json` classifies compound assignment as exact,
  but `OperationEffectScanner.ScanCompoundAssignment` scans only the target,
  value, selected operator, arithmetic hazards, and final write. It never
  scans the operation's user-defined input/output conversions.
- Supported impact: a conversion required by `x op= y` can allocate, mutate
  state, throw, diverge, or invoke capabilities while the complete effect
  summary omits it. Selected effect contracts can consequently be proven from
  incomplete execution semantics.
- Reproduction: a focused effect test defined `Number + int -> Product` and an
  implicit `Product -> Number` conversion that increments static state. For
  `value += 1`, the complete summary did not contain the static write. The
  temporary regression was removed.
- Required closure: scan the compiler-selected input and output conversions in
  exact evaluation order, applying the same conversion effect/exception rules
  as explicit conversions and avoiding double-counting child operations. Add
  input/output/both conversion effects, throwing/allocation/capability cases,
  built-in conversions, checked/lifted forms, properties/indexers, and a
  mutation restoring operator-only scanning.

### SP-AUDIT-063 - Late publication failure leaves a mixed generation (medium)

- [ ] `PublishOutputs` replaces the compiler manifest and request before it
  writes optional SARIF and the result commit marker. Its failure cleanup
  deletes only result and SARIF, so an ordinary late write failure preserves
  the new request/manifest beside missing or older remaining outputs.
- Supported impact: cooperative publication no longer behaves as one coherent
  four-file transaction. Verification still fails closed because the result
  commit marker is absent, but later tooling and retries observe partially
  advanced provenance instead of either the previous complete set or a fully
  invalidated set.
- Reproduction: a focused container package test created an owned four-file
  publication set, filled every member with stable bytes, then used a
  227-character SARIF basename. Lock and ownership metadata fit the filesystem
  component limit, while `AtomicFile`'s random temporary suffix made the SARIF
  write fail after manifest/request replacement. The launcher returned 3, the
  request had changed from 19 to 526 bytes, and result/SARIF were absent. The
  temporary regression was removed.
- Required closure: stage every output before committing any destination, then
  publish a generation atomically under the complete lock or restore/invalidate
  all members on failure. Add failures at each stage, pre-existing coherent
  generation, no-SARIF, retry, and cleanup controls plus a mutation that
  restores manifest/request-first publication.

### SP-AUDIT-064 - Effects profile skips generated ContractFor validation (medium)

- [ ] The incremental `ContractFor` validator is documented and packaged for
  every non-`off` profile, but the final-compilation analyzer registers peer-
  generated companion validation only when the Contracts feature is enabled.
  An `effects` profile therefore has no component that can validate companions
  emitted by another source generator.
- Supported impact: malformed generated companion surfaces compile without the
  promised SPCF diagnostics under a supported non-off profile. This is
  independent of SP-AUDIT-056: the missing diagnostic occurs even for a
  conventional `.g.cs` generated tree that the generated-code policy recognizes.
- Reproduction: a focused analyzer test ran a peer generator that emitted an
  empty `[ContractFor(typeof(IService))]` companion as
  `GeneratedContracts.g.cs`, then analyzed the final compilation with profile
  and feature set to `effects`. The expected SPCF0004 set was empty. The
  temporary regression was removed.
- Required closure: register final-compilation companion validation for every
  non-off profile while keeping contract proof consumption feature-gated. Add
  `off`, `advisory`, `effects`, `contracts`, and `all` cases for handwritten and
  peer-generated valid/malformed/overlapping companions, plus a mutation that
  restores the Contracts-only registration condition.

### SP-AUDIT-065 - Release workflow authority is checked as raw substrings (high)

- [ ] `Test-SharpProofReleaseConfiguration.ps1` certifies workflow use of
  release environments, tag tokens, variables, secrets, and OIDC by searching
  the entire YAML text with `Contains`. It does not parse jobs, expressions,
  permissions, environments, or data flow, so comments or unrelated jobs can
  satisfy every required token while the actual publishing jobs use different
  authority.
- Certifier impact: owner configuration can be correct while the checked-in
  workflow publishes outside the reviewed environments or omits job-scoped
  release controls, yet the canonical release-configuration validator emits
  passing exact-commit evidence.
- Reproduction: a disposable workflow mutation changed the private and public
  publishing jobs to `unrelated-private` and `unrelated-public`, retaining
  `nuget.private-preview` and `nuget.org` only in inline comments. With local
  deterministic GitHub-API responses for otherwise valid configuration (and a
  temporary nonempty-secret workaround for SP-AUDIT-052), the validator exited
  0 and reported both environments validated. No network or external write was
  performed; all workflow, contract, and mock changes were removed.
- Required closure: parse the workflow with a pinned YAML reader or compare a
  generated semantic release-job model; bind each release tag predicate,
  `needs`, environment, permissions, secret/variable reference, OIDC login,
  artifact input, and publication command to the actual authorized job. Add
  comment/dead-job decoys, wrong environment, missing job permission, wrong
  secret source, changed guard, and valid workflow controls plus certifier
  mutations for every semantic field.

### SP-AUDIT-066 - Final worker deadline applies termination grace twice (medium)

- [ ] The launcher computes its worker wait limit as project wall time plus
  almost the full termination grace, then `LinuxWorkerProcess` starts a second
  termination phase with another full grace budget. A TERM-ignoring child
  therefore receives an extra half-grace before forced kill, beyond the
  documented project-plus-one-grace final boundary.
- Supported impact: cancellation/timeout cleanup can retain the worker and hold
  the build longer than the public limit. It still terminates fail-closed, but
  the configured outer deadline is not the deadline users or package tests are
  promised.
- Reproduction: a focused Linux container test launched a direct shell child
  that ignored SIGTERM, passed `ComputeHardLimit(100, 1000)` and a 1,000 ms
  termination grace to `WaitForExit`, and required completion within the
  documented 1,100 ms plus a 200 ms scheduling allowance. Completion was
  correctly classified `TimedOut` but took 1,523 ms. The temporary regression
  was removed.
- Required closure: derive one monotonic project-plus-grace deadline and pass
  only its remaining duration through graceful and forced termination. Add
  normal exit, TERM-cooperative, TERM-ignoring, natural-exit race, cancellation,
  minimum/maximum grace, and loaded-container controls plus a mutation that
  restores independent wait and termination budgets.

### SP-AUDIT-067 - SBOM package dependencies are fabricated, not derived (medium)

- [ ] Release evidence reads only each nuspec's package ID, version, and
  repository metadata. It then hardcodes the two expected `DEPENDS_ON`
  relationships into the SBOM; final artifact validation and plan-only
  publication compare against the same hardcoded graph without reading the
  dependency groups in the package bytes.
- Certifier impact: the immutable bundle can contain a package whose actual
  dependency topology differs from the SBOM and reviewed three-package graph,
  yet every local release certifier approves and plans those bytes. Fresh
  package-layout tests mitigate normal builds but do not make these independent
  evidence/publication boundaries authoritative.
- Reproduction: a disposable package test replaced the `SharpProof` nuspec's
  `SharpProof.Attributes` dependency with `Fabricated.Dependency`, leaving
  package identity/version/repository metadata intact. Release-evidence
  generation exited 0, immutable artifact validation exited 0, and plan-only
  publication exited 0 while the SBOM retained the expected hardcoded edge.
  The package workspace and temporary regression/helper were removed.
- Required closure: parse and canonicalize every dependency group from each
  main package, validate supported target-framework groups and exact version
  ranges, generate the SBOM relationships from those bytes, and independently
  compare them again during final validation/publication. Add removed, added,
  replaced, wrong-version, framework-specific, duplicate, symbol/main mismatch,
  and exact-valid cases plus mutations severing each package-to-SBOM binding.

### SP-AUDIT-068 - Mixed partial generated companions escape all validation (medium)

- [ ] When any declaration of a `ContractFor` companion is generated, the
  incremental validator excludes the entire named-type symbol. Final-
  compilation validation starts only from generated declarations that carry
  the attribute themselves, so it cannot rediscover a symbol whose handwritten
  partial declaration owns `[ContractFor]` and whose generated partial owns no
  attribute.
- Supported impact: a supported source generator can contribute a partial
  companion declaration and cause a malformed handwritten attributed companion
  to receive no SPCF diagnostics. This is distinct from SP-AUDIT-056's filename
  recognition and SP-AUDIT-064's effects-profile registration gap: it occurs
  with a conventional `.g.cs` tree in the contracts validator pipeline.
- Reproduction: a focused two-generator test combined an empty handwritten
  `[ContractFor(typeof(IService))] partial class ServiceContracts` with an empty
  generated `ServiceContracts.g.cs` partial declaration. The target required
  `Map`, but both the incremental generator diagnostic IDs and direct final-
  compilation generated-tree diagnostic IDs were empty instead of containing
  SPCF0004. The temporary regression/generator were removed.
- Required closure: discover candidates at symbol scope whenever any partial
  declaration carries the attribute, then assign diagnostic ownership to an
  available source declaration without excluding mixed symbols. Add attribute-
  on-handwritten/generated, members split both directions, valid/missing/extra/
  overlapping companions, profile matrix, and order controls plus mutations
  restoring either side's declaration-local filter.

### SP-AUDIT-069 - Unused nested control attributes are never validated (medium)

- [ ] Nested `SharpProofSuppress`/`SharpProofTrusted` validation is performed
  only while recursively analyzing a reachable nested callable. Unreferenced
  local functions and never-invoked lambdas are deliberately excluded from
  call-site analysis, but no independent declaration/symbol action validates
  their control-attribute reasons.
- Supported impact: malformed empty or whitespace-only reasons silently escape
  the documented SP0024 policy depending on whether a nested callable happens
  to be invoked. Attribute validity is a source declaration rule and should not
  vary with execution reachability.
- Reproduction: a focused contracts-profile analyzer test declared an unused
  local function with `[SharpProofSuppress("")]` and an uninvoked lambda with
  `[SharpProofTrusted(" ")]`, enabling only SP0024. The expected two diagnostics
  were both absent. Existing invoked-callable controls diagnose the same reason
  shapes. The temporary regression was removed.
- Required closure: validate nested callable control attributes independently
  of semantic call-site traversal, retaining syntax/span deduplication when an
  invoked callable is also analyzed. Add used/unused local functions, lambdas,
  anonymous methods, method-group escape, expression trees, valid reasons, and
  duplicate-action controls plus a mutation restoring reachability-only checks.

### SP-AUDIT-070 - Never-invoked lambda bodies are analyzed as executed (medium)

- [ ] Nested call-site traversal treats every lambda creation in a reachable
  parent CFG block as an executable child callable. Unlike local functions, it
  does not require an invocation, method-reference escape, or other executable
  reachability before analyzing the lambda body.
- Supported impact: advisory/contracts analysis emits SP0027 for a call that
  cannot execute, creating a false positive and inconsistent semantics between
  equivalent unused local functions and lambdas. This can make supported builds
  fail when warnings are promoted to errors.
- Reproduction: a focused contracts-profile test assigned
  `Func<int> dead = () => Positive(-1);` and returned zero without invoking or
  exposing `dead`. The analyzer emitted one SP0027 at the call inside the lambda
  instead of an empty diagnostic set. The temporary regression was removed.
- Required closure: distinguish lambda construction from body execution; seed
  child analysis only from reachable invocation/delegate-flow roots, with a
  bounded conservative escape policy where exact tracking is unavailable. Add
  never-used, directly invoked, invoked through local copy, returned/passed/
  stored escape, conditional invocation, expression-tree, nested sibling, and
  local-function parity cases plus a mutation restoring creation-implies-run.

### SP-AUDIT-071 - Unicode-escaped Contract identifiers evade advisory activation (medium)

- [ ] Advisory activation uses raw source-text substring searches for contract
  method spellings before it creates an analyzer session. C# Unicode escapes
  are decoded by the compiler inside identifiers, so an invocation can bind to
  the exact `Contract.Requires` symbol while its source text contains none of
  the activation substrings.
- Supported impact: compiler-equivalent spelling alone can suppress all
  contracts analysis for an otherwise ordinary source tree. A definitely false
  call-site precondition then receives no SP0027 in the documented advisory
  profile.
- Reproduction: a focused advisory/contracts test declared a precondition as
  `Contract.\u0052equires(value > 0)` and called that method with `-1`. The
  compilation was valid, but the expected SP0027 set was empty. Replacing the
  escaped identifier with literal `Requires` is the existing positive control.
  The temporary regression was removed.
- Required closure: make the cheap activation scan syntax-aware and compare
  identifier `ValueText`, or use a bounded semantic candidate scan, while
  retaining false-positive-resistant fast paths. Add literal, verbatim,
  `\u`/`\U` escape, trivia/string decoy, generated-tree, and no-contract controls
  plus a mutation restoring raw-text-only activation.

### SP-AUDIT-072 - String concatenation omits implicit ToString effects (high)

- [ ] Built-in string concatenation is treated as operand traversal plus an
  allocation. When an operand is not already a string, the compiler/runtime
  conversion invokes formatting or `ToString`, but the effect scanner does not
  resolve or join that call when the binary operation has no user-defined
  operator method.
- Supported impact: a selected purity or no-throw claim can be proven for a
  method whose string concatenation invokes source code that writes observable
  state or throws. The produced effect projection remained complete while the
  real operation was not effect-free.
- Reproduction: a focused Effects test concatenated a literal with an instance
  of a sealed source type whose `ToString()` increments a static field and
  throws `ApplicationException`. The summary contained neither the static write
  nor the declared exception. The temporary regression was removed.
- Required closure: model the exact string-concatenation conversion/formatting
  path and join source/API summaries where it is statically resolvable;
  conservatively abstain for open or otherwise unresolved virtual formatting.
  Add string, primitive, nullable, null, sealed source override, open virtual,
  throwing, interpolated-string parity, and allocation controls plus a mutation
  restoring allocation-only handling.

### SP-AUDIT-073 - Object-initializer writes lose fresh-object ownership (medium)

- [ ] An object creation is assigned a fresh effect region for its constructor,
  but the member initializer is scanned independently. Its implicit instance
  reference is consequently classified as an unknown/method receiver rather
  than the newly allocated object.
- Supported impact: an otherwise complete method that only initializes a field
  of a new object is reported incomplete and observably impure. Selected purity
  claims are falsely rejected, and the result is inconsistent with equivalent
  fresh array-element initialization already modeled as fresh-owned.
- Reproduction: a focused Effects test analyzed
  `new Value { Number = 1 }`. The write set was `Unknown`, projection
  completeness was false, and `IsObservablePure` was false instead of a sole
  fresh-region write and a complete pure result. The temporary test was removed.
- Required closure: analyze object/member initializers under the enclosing
  creation's fresh receiver region while still joining initializer-expression
  effects and property-setter summaries. Add field, property, nested object,
  static side effect, throwing setter, collection-initializer, struct, and fresh
  array parity controls plus a mutation restoring detached initializer scanning.

### SP-AUDIT-074 - Response validation accepts an impossible cache hit (medium)

- [ ] Request-bound response validation checks the request hash, input,
  manifest, and budgets, but it is not given the expected cache or verification
  policy. It therefore validates only the response's internal
  `(CacheStatus, CacheHit)` pair, not whether that state is possible for the
  bound request.
- Supported impact: malformed worker or replay evidence can claim a cache hit
  for a cache-disabled (or otherwise non-cacheable) verification request and be
  accepted as valid. This makes cache provenance in the certified response
  unreliable even though the request hash itself is exact.
- Reproduction: a focused protocol test created a valid complete response for
  a request with `Cache.Enabled = false`, changed the summary to
  `CacheStatus=Hit` and `CacheHit=true`, and retained the exact computed request
  hash. `ValidateForRequest` returned valid. The temporary test was removed.
- Required closure: validate against the complete canonical request or pass all
  policy fields needed for cross-document invariants. Reject hits when caching
  is disabled, when verification policy forbids cache use, and for outcomes the
  cache cannot store. Add disabled/enabled, hit/miss/written/rejected,
  require-proven, failed/unknown/refuted, stale-hash, and valid-hit controls plus
  a protocol mutation that removes request/cache binding.

### SP-AUDIT-075 - Invalid nested publication paths leave output residue (medium)

- [ ] Publication lock objects create each lock's parent directory before the
  complete destination set is proven structurally valid. If one destination is
  nested beneath another destination filename, preparing the child's lock
  creates the parent destination itself as a directory; ownership binding then
  rejects the set but does not undo that filesystem mutation.
- Supported impact: an ordinary invalid configuration fails as intended yet
  leaves a configured output path behind as an unowned directory. Subsequent
  corrected builds can remain blocked, and fail-closed validation has mutated
  the publication namespace it was supposed to preserve.
- Reproduction: a focused Linux host test acquired a set containing
  `result.json` and `result.json/child.json`. Acquisition threw `IOException`,
  but `result.json` existed afterward as a directory. The temporary test was
  removed.
- Required closure: validate ancestor/descendant conflicts for the complete
  canonical set before constructing locks or directories, and make all
  pre-acquisition metadata setup failure-atomic. Add both nesting orders,
  three-level paths, pre-existing parent directories, siblings, lock/marker
  residue, retry, and valid disjoint controls plus a containment mutation that
  removes the structural preflight.

### SP-AUDIT-076 - Warning-like text in a Linux path changes the diagnostic (medium)

- [ ] The build task parses launcher warnings by searching the entire stderr
  line for the first SP0047 marker before it searches for SP0048. Linux
  filenames may contain colons and the complete marker text, so a marker inside
  the source path is mistaken for the diagnostic boundary.
- Supported impact: a real SP0048 can be reclassified as SP0047, and its source
  path, line, and column are truncated. Code-specific `NoWarn` or
  `WarningsAsErrors` policy can therefore suppress or promote the wrong
  diagnostic, while MSBuild/binlog/IDE locations become incorrect.
- Reproduction: a focused BuildTasks test logged an SP0048 at line 4, column 5
  for `/tmp/source: warning SP0047: detail.cs`. The task emitted SP0047 with
  file `/tmp/source` and zero location instead. The temporary test was removed.
- Required closure: parse the canonical diagnostic suffix/location grammar from
  the right-hand boundary rather than searching arbitrary path text; preferably
  carry structured launcher diagnostics across the process boundary. Add both
  codes, marker-like filenames, parentheses/commas/Unicode, locationless
  diagnostics, malformed lines, and code-specific warning-policy integration
  controls plus a parser mutation restoring first-marker matching.

### SP-AUDIT-077 - The canonical Dockerfile frontend is not digest-pinned (high)

- [ ] The Dockerfile opts into `docker/dockerfile:1.7` by a mutable tag, while
  the toolchain catalog and container-contract validator pin and compare only
  the stage base images. The frontend controls how the canonical Dockerfile is
  parsed and executed but has no catalog-owned digest or evidence binding.
- Certifier impact: the source can select `docker/dockerfile:latest` (or the
  registry can move the current tag) and still pass the canonical contract
  gate. Builds for the same Git commit may therefore execute different frontend
  code/semantics while release evidence claims one pinned container toolchain.
- Reproduction: the audit temporarily changed only the syntax directive from
  `docker/dockerfile:1.7` to `docker/dockerfile:latest` and ran
  `docker compose run --rm tooling contract`. It exited zero with
  `SharpProof container contract validation passed.` The Dockerfile was restored
  immediately; no image build or external write was performed.
- Required closure: catalog and use an immutable frontend image digest, require
  the exact syntax directive in contract validation, and bind that digest into
  container/release evidence. Add missing, mutable-tag, wrong-digest, decoy,
  duplicate-directive, canonical, and evidence-round-trip fixtures plus a
  release-authority mutation that removes frontend verification.

### SP-AUDIT-078 - Behavioral declaration changes bypass changed-TCB coverage (high)

- [ ] The changed-TCB gate counts only changed lines that appear as Coverlet
  sequence points. It treats every changed source line absent from the report's
  line map as non-executable syntax and silently skips it, even when the line is
  a behavioral constant, attribute, initializer, signature modifier, or other
  declaration that materially changes trusted execution.
- Certifier impact: a TCB change can alter resource/soundness behavior without
  requiring any discriminating test while the exact changed-TCB gate reports
  100 percent. This is independent of SP-AUDIT-050's forged-report acceptance;
  the reproduction used authentic project coverage.
- Reproduction: the audit temporarily changed the compiler IL lowerer's trusted
  `MaximumStack` ceiling from 128 to 129, then ran the canonical coverage
  validator over 44 authentic Cobertura reports with `-ComparisonRef HEAD
  -IncludeWorkingTree`. It reported one changed TCB file, zero coverable lines,
  100 percent changed coverage, and `passed=true`. The constant was restored.
- Required closure: classify changed semantic declarations rather than assuming
  no sequence point means no behavior. Require explicit mutation/structural
  evidence for uncovered declaration changes, or conservatively fail changed
  TCB files whose semantic diff is not mapped to coverage. Add constant value,
  field/property initializer, attribute, modifier, expression body, signature,
  trivia-only, brace-only, and generated-declaration fixtures plus a certifier
  mutation that restores sequence-point-only accounting.

### SP-AUDIT-079 - Backend renewal failure is erased as a method timeout (medium)

- [ ] After a timed-out solver lane is retired, `TryRenew` catches every
  replacement-factory failure and returns only `false`. The caller retains the
  original timeout reason and assigns it to all unclaimed work, losing the
  distinct fact that the verifier backend could no longer be created.
- Supported impact: a missing/corrupt native solver or other backend creation
  failure after the first timeout is reported as an ordinary `TimedOut` run
  with no failure reason. Operators and retry policy receive the wrong result,
  and exact backend-availability evidence is absent, although claims remain
  fail-closed as unknown.
- Reproduction: a focused worker test used one lane and two callables. The first
  backend exceeded the method deadline; the second factory call threw
  `DllNotFoundException`. The response was `TimedOut`, failure reason `None`,
  with both claims labeled `MethodTimeout` instead of a failed
  `BackendUnavailable` result. The temporary test was removed.
- Required closure: preserve a typed renewal failure and stop/classify remaining
  work as backend unavailable while retaining the completed timeout result.
  Add missing-library, null/reused replacement, disposal failure, successful
  renewal, factoryless timeout, concurrent lanes, cancellation, and project
  deadline controls plus a mutation that reduces renewal to a Boolean again.

### SP-AUDIT-080 - Compiler feature labels do not bind discovered proof scope (high)

- [ ] The compiler artifact records a global `WorkerFeatureSet`, but envelope
  validation checks only that the enum value is defined. It never proves that
  the callable selections, claim kinds, clauses, effect evidence, and lowered
  bodies are the complete output of discovery for that declared feature set.
- Supported impact: a canonical artifact can claim the `All` profile while
  containing only effects-selected callables and claims. Contract clauses that
  should have been discovered and verified can be absent, yet the worker has no
  independent profile/scope authority with which to reject the omission.
- Reproduction: a focused compiler-artifact test captured an effects-only
  source containing both `[DoesNotThrow]` and a false `Contract.Ensures`, changed
  only `artifact.Features` from `Effects` to `All`, and expected canonical
  serialization to reject it. Serialization succeeded. The temporary test was
  removed.
- Required closure: cryptographically/canonically bind requested feature scope
  to discovery output and independently cross-check global features against
  per-callable selections, selection reasons, claim kinds, and lowered
  evidence. Add effects/contracts/all, mixed annotations, omitted and extra
  claims, duplicate selections, generated companions, serialization/cache
  round trips, and real strict-worker controls plus a trusted mutation that
  removes scope parity.

### SP-AUDIT-081 - Reference modules do not authenticate the manifest-first slot (medium)

- [ ] Assembly-reference validation requires a nonempty module array and sorts
  only rows after index zero, but the row schema carries no manifest/linked role
  and validation never proves that index zero is the assembly manifest module.
  Any valid linked row can occupy that privileged slot.
- Compiler-evidence impact: multiple canonical fingerprints can describe the
  same reference closure with contradictory module roles, and an artifact can
  attribute the assembly's primary identity to linked-module bytes. This
  weakens the schema-12 manifest-first provenance contract even though each
  individual row's path/MVID/hash shape remains valid.
- Reproduction: a focused compiler-artifact test created a valid assembly
  snapshot with manifest and linked rows, swapped the two, recomputed
  `CompilationSha256`, and expected serialization to reject it. Canonical
  serialization succeeded. The temporary test was removed.
- Required closure: encode and validate an explicit module role or bind the
  first row to independently captured assembly-manifest identity/metadata; keep
  linked rows strictly sorted thereafter. Add real emitted multi-module
  manifest/linked order, swapped role, duplicate role, module-kind reference,
  MVID/hash/path replacement, serialization/cache, and package-projection
  controls plus a provenance mutation that removes first-row authentication.

### SP-AUDIT-082 - Release tags never run the required Debug solution gate (high)

- [ ] The authoritative preview-evidence catalog requires
  `debug-solution-gate`, but the tag-triggered package, consumer,
  release-qualification, and publication dependency graph runs only Release
  configurations. The ordinary CI workflow does not run on tag refs and cannot
  supply exact-tag Debug evidence.
- Certifier impact: a source change that fails only in Debug can still satisfy
  every scheduled tag qualification dependency and reach either publication
  environment, contrary to the checked-in release contract.
- Reproduction: a focused architecture regression first confirmed that
  `preview-evidence.v1.json` contains `debug-solution-gate`, then required the
  exact release workflow to schedule a Debug configuration. The canonical
  container test failed because `package-consumers.yml` contains no
  `-Configuration Debug` invocation. The temporary test was removed.
- Required closure: run the full Debug solution gate at the exact tagged commit
  in release qualification, retain and hash its evidence, and make publication
  require that receipt. Add Debug-only build/test failure, missing/stale/wrong-
  SHA receipt, Release-only control, dependency-graph, and publication-plan
  fixtures plus a workflow-authority mutation that removes the Debug job.

### SP-AUDIT-083 - Compose projects share one mutable tooling image tag (medium)

- [ ] Compose project names isolate containers, networks, and named volumes, but
  the common service explicitly sets every project image to the same default
  `sharpproof-tooling:local` tag. Building one worktree moves the tag globally;
  another worktree can then start that image despite having different source
  and container inputs.
- Developer/certifier impact: the documented independent-worktree workflow is
  not image-isolated. Concurrent builds can race, and a later task can execute
  the wrong worktree's SDK/native/tooling image while its bind-mounted source
  and evidence paths belong to the current worktree.
- Reproduction: the audit ran `docker compose config --images` with
  `COMPOSE_PROJECT_NAME=audit-one` and then `audit-two`. Both resolved every
  relevant service to exactly `sharpproof-tooling:local`; no image was built or
  changed by the probe.
- Required closure: make the default tooling image identity project/worktree-
  scoped (or immutable content-addressed) while retaining an explicit override,
  and bind the selected image identity into task/evidence execution. Add two-
  project build/run, concurrent build, stale-tag, explicit shared override,
  cleanup, and exact-image evidence controls plus a Compose mutation restoring
  the global tag.

### SP-AUDIT-084 - Strict verifier diagnostics bypass the MSBuild error channel (medium)

- [ ] The launcher emits policy-promoted SP0047 and SP0048 diagnostics with
  severity `error`, but `RunVerifier.LogStandardError` recognizes only their
  warning spellings. Error lines fall through to high-importance messages, and
  the target later emits only a generic unlocated exit-code error.
- Supported impact: strict builds do fail, but binlogs, IDE navigation, custom
  loggers, and code-specific error accounting never receive the actual SP0047/
  SP0048 `BuildErrorEventArgs` or its source location. The documented
  policy-matched severity is therefore not represented at the MSBuild boundary.
- Reproduction: a focused BuildTasks test supplied ordinary located
  `error SP0047` and locationless `error SP0048` lines to `LogStandardError`.
  The recording engine received zero coded errors and two messages. The
  temporary test was removed.
- Required closure: parse both warning and error severity through one exact
  diagnostic grammar and call `LogWarning`/`LogError` accordingly; retain the
  generic process error only for unclassified launcher failure. Add both codes,
  both severities, located/locationless, malformed, path-marker, strict packaged
  build, binlog, and custom-logger controls plus a parser mutation that drops
  error severity.

### SP-AUDIT-085 - Cache paths nested under outputs pass topology validation (medium)

- [ ] Output/cache separation is checked in only one direction: an output under
  the cache is rejected, but a cache under a publication filename is accepted.
  The launcher likewise checks equality and worker-tree containment without
  rejecting this ancestor relationship.
- Supported impact: with result `result.json` and cache
  `result.json/cache`, invalidation succeeds; cache initialization can then
  create `result.json` as a directory, after which publication fails and leaves
  cache/output residue that blocks retries.
- Reproduction: a focused Linux BuildTasks test configured the cache beneath
  the result path and expected `InvalidatePublishedResult.Execute()` to reject
  the topology before mutation. It returned true. The temporary test was
  removed; source inspection confirms `VerificationCache` creates the accepted
  directory before reading.
- Required closure: apply symmetric ancestor/equality rules across every
  publication, cache, input, and runtime path at both invalidation and launcher
  preflight, before any directory/lock/marker creation. Add both nesting
  directions for each publication member, cache-root equality, siblings,
  default cache, marker/lock residue, retry, and integration controls plus a
  containment mutation that removes the reverse-direction check.

### SP-AUDIT-086 - Unboxed struct copies retain boxed-argument ownership (medium)

- [ ] Region classification strips every built-in conversion without
  distinguishing representation-preserving reference conversions from
  unboxing. A local initialized by `(Value)boxed` therefore inherits the
  reference parameter's region even though unboxing creates a by-value struct
  copy.
- Supported impact: mutating a field of the local copy is reported as a write
  to caller-owned parameter state, so a genuinely observable-pure method is
  falsely rejected. This contradicts the existing by-value struct-copy
  semantics elsewhere in the scanner.
- Reproduction: a focused Effects test unboxed an `object` argument into a
  struct local and assigned `copy.Number = 1`. The write set was nonempty and
  `IsObservablePure` was false instead of an empty write set and true. The
  temporary test was removed.
- Required closure: classify conversion ownership by exact conversion kind;
  unboxing and other value-producing copies must introduce local/value
  ownership while reference-preserving conversions retain aliases. Add boxed
  argument, boxed field, interface boxing, nullable unboxing, failed-cast
  exceptions, reference casts, `ref` unbox controls, and by-value/ref struct
  parity plus a mutation restoring conversion-blind alias propagation.

### SP-AUDIT-087 - Explicitly selected nested callables are silently skipped (high)

- [ ] The main analyzer registers method symbols and operation blocks, while
  its nested-CFG traversal performs only control-attribute and Requires
  call-site analysis. Effect selection and unsupported-callable reporting are
  never applied to local functions or lambdas.
- Supported impact: a reachable local function annotated `[DoesNotThrow]`
  threw `InvalidOperationException`, yet effects-profile analysis emitted no
  SP0047. The documented rule that explicitly selected unsupported callables
  abstain visibly is therefore bypassed, and a nested effect claim can vanish
  without a proof or an unknown diagnostic.
- Reproduction: a temporary Analyzer test invoked the selected local from an
  ordinary outer method and expected one SP0047; the diagnostic set was empty.
  The temporary test was removed.
- Required closure: route supported nested symbols through the same selection,
  control-attribute, closed-attribute, effect, outcome-recording, and SP0047
  policy as top-level methods, or reject nested selection explicitly. Add local,
  lambda, reached/unreached, valid/invalid control attribute, all effect claims,
  profile, generated-tree, and duplicate-analysis controls plus a mutation
  that removes nested selection.

### SP-AUDIT-088 - Compiler resource-limit effect evidence is rejected (medium)

- [ ] Compiler capture can emit an unknown effect claim with reason
  `ResourceLimit` and certainty `IncompleteMayEffectSummary`, but the compiler
  artifact effect codec's closed reason list omits `ResourceLimit` and the
  protocol certainty predicate accepts that certainty only for
  `EffectSummaryIncomplete`.
- Supported impact: an ordinary supported method whose acyclic effect analysis
  exceeds its operation budget is serialized by the collector, then rejected
  by worker hydration as `compiler_manifest.lowered_ir` instead of producing
  the documented typed Unknown/ResourceLimit result.
- Reproduction: a focused Worker test changed otherwise valid compiler effect
  evidence to the exact producer tuple Unknown/ResourceLimit/
  IncompleteMayEffectSummary, resealed it, and expected hydration. The decoder
  threw `InvalidDataException: Compiler effect-claim evidence is invalid.` The
  temporary test was removed; existing analyzer budget tests establish the
  producer side of the tuple.
- Required closure: give producer projection, artifact codec, protocol model,
  hydration, and result assembly one generated reason/certainty catalog. Add
  exact and boundary resource-budget capture through worker response, adjacent
  unsupported-body controls, malformed tuple rejection, and mutations removing
  each permitted producer tuple.

### SP-AUDIT-089 - Runtime failure reasons masquerade as compiler lowering (medium)

- [ ] Failed callable hydration accepts any defined worker claim reason except
  `Unspecified`, although compiler lowering can emit only its small closed set
  of unsupported compiler reasons. Runtime-only reasons such as
  `MethodTimeout` therefore pass as canonical compiler evidence.
- Certifier impact: a compiler artifact for an unsupported loop was changed
  from `UnsupportedBody` to `MethodTimeout`; hydration accepted it and can
  manufacture an internally consistent TimedOut result even though no solver
  deadline elapsed.
- Reproduction: a temporary Worker test created the real unsupported-loop
  artifact, performed that one-field mutation, and expected
  `InvalidDataException`. No exception was thrown. The temporary test was
  removed.
- Required closure: validate failed callables against a generated compiler-
  producer reason catalog, separate compiler abstention from runtime outcomes,
  and table-test every allowed compiler reason and every rejected runtime
  reason. Add a mutation restoring broad enum-defined acceptance.

### SP-AUDIT-090 - Trailing separators create alternate compilation identities (medium)

- [ ] Compilation path validation defines canonicality only as equality with
  `Path.GetFullPath`. On Linux, `GetFullPath("/tmp/project/")` retains the
  trailing slash, so both `/tmp/project` and `/tmp/project/` are accepted as
  canonical project-directory evidence and hash differently.
- Provenance impact: one physical compilation directory has multiple accepted
  schema-12 fingerprints, weakening canonical cache and evidence identity.
- Reproduction: the malformed nested-evidence matrix appended `/` to a
  non-root captured `ProjectDirectory`, recomputed the compilation hash, and
  expected deserialization rejection. Deserialization succeeded. The temporary
  case was removed.
- Required closure: define directory canonicalization separately from file
  paths, normalize or reject trailing separators except for the filesystem
  root, and make capture and validation share that owner. Add root, ordinary,
  repeated-separator, dot-segment, Unicode, and file-row controls plus a
  mutation restoring `GetFullPath` equality alone.

### SP-AUDIT-091 - Peer-generated rejected ContractFor attributes evade validation (high)

- [ ] Final-compilation candidate discovery first resolves the one exact
  trusted `ContractForAttribute` symbol and returns no candidates when that
  resolution is rejected or ambiguous. The incremental validator cannot see
  a peer generator's output, so a peer-generated source-defined lookalike is
  owned by neither validation path.
- Supported impact: a generator emitted a conventional `.g.cs` companion with
  a source `SharpProof.Attributes.ContractForAttribute` lookalike. Final
  contracts-profile analysis emitted no SPCF0001, although the same rejected
  handwritten attribute is required to fail closed.
- Reproduction: a temporary generated-companion analyzer test supplied the
  trusted Attributes reference, emitted the lookalike and attributed companion,
  and expected SPCF0001. The diagnostic set was empty. The test was removed.
- Required closure: discover syntactic/metadata-name candidates in the final
  compilation independently of successful trusted-symbol resolution, then
  classify each against the exact symbol and diagnose rejection. Add peer-
  generated shadow, ambiguous, missing, exact, mixed handwritten/generated,
  every profile, and non-generated filename controls plus a mutation that
  restores the early empty return.

### SP-AUDIT-092 - Contract companion bodies are analyzed as implementations (medium)

- [ ] Analyzer method/operation-block registration does not exclude methods in
  validated ContractFor companion types, although compiler discovery treats
  companions only as contract sources and verifies their matched target
  implementations.
- Supported impact: harmless dummy implementation details inside a companion
  produce SP0047 against the companion itself. A valid companion containing a
  local delegate received `UnsupportedOperationShape (VariableDeclarator)`,
  even though its body is not the target program being specified.
- Reproduction: a temporary contracts-profile Analyzer test created a valid
  `Service.Map` target and exact companion whose body carried the Ensures clause
  plus a dummy lambda. It expected no SP0047 and received one at the companion
  method. The temporary test was removed.
- Required closure: exclude validated companion methods from implementation
  analysis while retaining clause extraction, control-attribute validation,
  and target-method diagnostics. Add valid/invalid companion, target, dummy
  body, generated/handwritten, nested, explicit-selection, and duplicate-
  diagnostic controls plus a mutation removing the exclusion.

### SP-AUDIT-093 - Pilot qualification accepts duplicate and vacuous libraries (high)

- [ ] `Test-SharpProofPilots.ps1` trusts the catalog's project, library, and
  category labels, checks only row/category counts, and permits effect- or
  contract-designated pilots with zero relevant claims. It does not require
  five distinct projects or package identities.
- Certifier impact: the exact qualification predicates accepted five rows
  backed by only three projects while all four effect/contract rows contained
  zero claims. A release can therefore claim five qualified libraries without
  exercising five libraries or the required semantic categories.
- Reproduction: a harmless in-memory catalog/report fixture duplicated project
  paths and package identities and supplied zero-claim effect/contract rows;
  every current qualification predicate passed.
- Required closure: schema-validate the catalog, require exactly five unique
  IDs, projects, and external library identities, authenticate library/version
  from each project reference, and require non-vacuous category-specific claim
  evidence. Add duplicate, mislabeled, zero-claim, and valid five-library
  certifier fixtures.

### SP-AUDIT-094 - Package licenses are not bound to release evidence (high)

- [ ] Release evidence reads package ID, version, and repository metadata from
  nuspecs but independently hardcodes first-party SPDX licenses as MIT. Final
  artifact and publication-plan validators never compare the nuspec license to
  the SBOM license.
- Certifier impact: a package can declare a different valid NuGet license while
  the SBOM and release evidence still certify MIT. This permits internally
  inconsistent legal/supply-chain evidence for the promoted bytes.
- Reproduction: a temporary real nupkg mutation changed the SharpProof nuspec
  license from MIT to Apache-2.0 while preserving all currently validated
  identity fields. `New-SharpProofReleaseEvidence.ps1` exited zero and retained
  MIT SBOM policy. The disposable package was removed.
- Required closure: make one catalog the license authority; compare every
  package's nuspec expression and every corresponding SPDX package row during
  evidence creation, final validation, and plan-only publication. Add a
  self-consistently rehashed wrong-license fixture.

### SP-AUDIT-095 - SBOM package URLs are not validated (medium)

- [ ] Evidence generation emits canonical NuGet purls in SPDX `externalRefs`,
  but evidence, artifact, and publication validators never inspect those rows.
- Certifier impact: a canonical component name, version, checksum, SPDX ID, and
  relationship graph can carry a purl naming an unrelated package. Package-
  manager consumers of the accepted SBOM then receive false component identity.
- Reproduction: a harmless in-memory mutation replaced SharpProof's purl with
  `pkg:nuget/Fabricated.Package@99.0.0`; every field currently consumed by the
  validators remained unchanged, and bounded source inspection found no purl
  validation path.
- Required closure: derive one exact purl from each already authenticated
  package name/version, require exactly one matching purl per first- and third-
  party component in all three release stages, and add substituted, duplicate,
  omitted, encoded, and canonical controls.

### SP-AUDIT-096 - VerifyTarget cancellation checks certify arbitrary catch tails (high)

- [ ] `CancellationBoundaryAnalyzer` recognizes either an initial
  `ThrowIfCancellationRequested` or caller-canceled `if`, then accepts the catch
  without validating how the remaining timeout case is translated.
- Supported impact: replacing the required timeout result with an arbitrary
  `CallableVerificationResult` produced no SPMETA003. A trusted mutation can
  turn method/project cancellation into a semantic answer while the meta-
  analyzer certifies the boundary.
- Reproduction: two isolated in-memory analyzer fixtures used the throw-prefix
  and caller-canceled-if forms followed by an arbitrary return; both emitted
  zero SPMETA003 diagnostics.
- Required closure: validate the complete catch control flow and exact caller-
  cancellation/timeout translations, including every return and fallthrough.
  Add both bypasses, reordered branches, extra statements, nested control flow,
  and the current valid production shape as mutation-discriminating controls.

### SP-AUDIT-097 - Semantic strings evade the meta-analyzer through patterns (medium)

- [ ] SPMETA004 registers invocation and binary operations and recognizes only
  equality or `string.Equals`. Constant patterns and switch arms can make proof
  behavior depend on semantic reason strings without entering that policy.
- Supported impact: both `reason is "ir_condition_both_branches_feasible"`
  and an equivalent switch expression emitted zero SPMETA004 diagnostics in a
  soundness-critical namespace.
- Reproduction: harmless in-memory meta-analyzer fixtures exercised both forms;
  the diagnostic sets were empty.
- Required closure: analyze constant/relational patterns and switch statement/
  expression labels that control soundness behavior, deduplicate overlapping
  syntax, and retain nonsemantic-string controls. Add a mutation that removes
  each newly owned operation shape.

### SP-AUDIT-098 - Mutable static properties evade analyzer-state policy (medium)

- [ ] SPMETA002 registers only `SymbolKind.Field`. Static auto-properties and
  field-like events introduce mutable process-wide backing storage without an
  explicit field symbol action.
- Supported impact: a mutable static auto-property in `SharpProof.Analyzer`
  emitted no SPMETA002, so moving shared state from a field to a property can
  bypass the compilation/worker-scoped state invariant.
- Reproduction: an in-memory meta-analyzer fixture declared
  `private static int State { get; set; }`; the diagnostic set was empty.
- Required closure: cover mutable static properties and events in critical
  assemblies while allowing proven immutable/get-only forms. Add auto/manual
  property, init/get-only, event, containing-type, and namespace controls plus
  a move-field-to-property mutation.

### SP-AUDIT-099 - Compiler integer domains can be narrowed to fabricate proofs (high)

- [ ] The producer derives scalar intervals from Roslyn types, but schema 12
  stores only minimum/maximum values. Hydration accepts any recognized primitive
  interval for any integer canonical variable instead of binding it to the
  variable's compiler type.
- Supported impact: changing an `int` parameter's interval from Int32 bounds to
  byte `[0,255]` survived canonical serialization and hydration. The worker then
  installs that forged domain as a proof assumption, which can prove contracts
  that are false for negative Int32 inputs.
- Reproduction: a temporary worker artifact test narrowed the parameter domain
  and expected `InvalidDataException`; decode returned normally. The test was
  removed.
- Required closure: persist and authenticate the source scalar type/signedness/
  width, derive the one exact interval during hydration, and reject substitutes.
  Add every scalar width, parameter/result/receiver, boundary, narrowed/widened,
  and false-proof controls plus a mutation restoring range-only acceptance.

### SP-AUDIT-100 - Summary result and existential roles are interchangeable (high)

- [ ] Relational-summary artifacts store `Result` separately from ordered
  `ExistentialVariables`, but hydration authenticates only type, freshness, and
  set disjointness. It does not bind which same-typed free variable denotes the
  callee result.
- Supported impact: swapping a source summary's result with its same-typed
  nested-call existential, then canonical-serializing and deserializing the
  artifact, was accepted by `DecodeCallables`. Runtime evaluation uses the
  substituted `Result`, so the changed role assignment can alter a caller's
  value while leaving the relation and provenance untouched.
- Reproduction: a temporary compiler-artifact test built `Caller -> Wrapper ->
  Inner`, confirmed one existential, swapped it with the summary result, and
  expected `InvalidDataException`; no exception was thrown. The test was removed.
- Required closure: authenticate/reconstruct input, result, and ordered
  existential roles from the authoritative summary construction, not only the
  free-variable set. Add same/different-type swaps, permutations, nested depth,
  valid round trips, and a false-caller-contract mutation probe.

### SP-AUDIT-101 - Branch refinement re-evaluates mutations and hides effects (high)

- [ ] `ManagedAbstractFlow` transfers a branch expression, including nested
  assignments, then evaluates the expression again from the mutated state when
  applying edge assumptions. The second evaluation can mark the actually taken
  edge unreachable.
- Supported impact: for `x = 1; if (x + (x = 2) == 4) { } else { state++; }`,
  runtime takes the else branch (`1 + 2 != 4`), but analysis re-evaluates it as
  `2 + 2 == 4`, omits the reachable static write, and can accept `[EnforcePure]`.
- Reproduction: a temporary Effects regression expected the summary to contain
  `EffectRegionId.Static()`; actual was false. The test was removed.
- Required closure: snapshot branch facts before mutation or conservatively
  suppress refinement for mutation-bearing predicates. Add assignment,
  increment, ref/local-call mutation, evaluation-order, both-edge, nested
  boolean/conditional, projection, analyzer, and mutation-reversion controls.

### SP-AUDIT-102 - Selected specification packs are absent when unused (medium)

- [ ] Selected pack IDs are passed only into callable lowering. Schema 12 stores
  a pack/catalog identity only in summaries that actually use a pack; the
  artifact envelope contains no configured selection set.
- Supported impact: the same call-free contract project produced byte-identical
  canonical artifacts with specification packs unset and with `dotnet.scalar`
  selected, contrary to the documented claim that selection and exact catalog
  content are sealed.
- Reproduction: a temporary worker artifact test produced both variants and
  required unequal serialization; the two JSON documents were exactly equal.
  The test was removed.
- Required closure: seal canonical selected pack IDs plus the authoritative
  catalog/version digest at the envelope, validate them before lowering, and
  bind them into the compilation/input fingerprint even when unused. Add unset,
  one/multiple, reordered/duplicate, unknown, unused/used, and digest mutation
  controls; a wire-version decision may be required.

### SP-AUDIT-103 - Disabled cache paths are still treated as active topology (medium)

- [ ] MSBuild always passes the configured cache path to invalidation and the
  launcher always validates it against inputs/outputs, even when cache is
  disabled or `require-proven` makes cache access impossible.
- Supported impact: a valid packaged proof with
  `SharpProofVerifyCacheEnabled=false` and the unused cache directory equal to
  the result path failed before verification with the distinct-path error. An
  inactive configuration value therefore rejects an otherwise valid cache-free
  build.
- Reproduction: a temporary packaged integration test first completed a normal
  proof, reran the verification target with cache disabled and the alias, and
  expected exit zero; exit was one with `output, input, cache, and worker paths
  must be distinct`. The test was removed.
- Required closure: compute effective cache use once and omit cache path,
  locking, ownership, and topology validation when disabled or policy-bypassed.
  Add disabled, require-proven, enabled alias/nesting, cache creation, and
  launcher/invalidation parity controls.

### SP-AUDIT-104 - Worker cancellation reification ignores argument evaluation (high)

- [ ] The cancellation meta-analyzer verifies the local `Interrupted` helper's
  body and that a catch returns a call to it, but never validates the call's
  arguments or their evaluation before the helper executes.
- Supported impact: `return Interrupted(ReplaceCancellation())`, where the
  argument throws `InvalidOperationException`, emitted no SPMETA003. The new
  exception replaces cancellation before the certified reification helper can
  run.
- Reproduction: a harmless in-memory meta-analyzer fixture used the exact
  trusted Worker catch/helper shape with the executable argument; diagnostics
  were empty.
- Required closure: require the exact zero-argument form or an already-
  established inert snapshot local, and reject calls, properties, conversions,
  assignments, or other executable argument expressions. Add both production
  call shapes and argument-evaluation mutations.

### SP-AUDIT-105 - Semantic cache policy misses aliases and indexer writes (medium)

- [ ] SPMETA010 checks named cache-write invocation argument descendants only.
  It performs no local value-flow and registers no assignment/property write
  operation.
- Supported impact: both `var answer = Answer.Unknown; cache.Add("key",
  answer)` and `cache["key"] = Answer.Unknown` emitted no SPMETA010, allowing
  the exact transient semantic answer prohibited by policy into a cache while
  the boundary remains certified.
- Reproduction: two harmless in-memory meta-analyzer fixtures exercised the
  local alias and indexer setter forms; both diagnostic sets were empty.
- Required closure: track simple local aliases into recognized writes and own
  indexer/property-setter cache writes, conservatively rejecting unresolved
  semantic answer values. Add Unknown/TimedOut/Failed aliases, Proven controls,
  overwrite/add/indexer APIs, branches, and mutation probes.

### SP-AUDIT-106 - Interpolated C# synthesis bypasses SPMETA009 (medium)

- [ ] The C#-expression-text policy recognizes only string binary addition;
  interpolated strings are not registered or inspected.
- Supported impact: `$"({name}) is not null"` emitted no SPMETA009 although it
  constructs the same semantic C# expression text as the rejected concatenated
  form.
- Reproduction: a harmless in-memory soundness-analyzer fixture used the
  interpolated form and received an empty diagnostic set.
- Required closure: inspect interpolated-string operations for semantic C#
  expression construction while allowing ordinary non-expression formatting.
  Add constant, formatted, aligned, escaped, concatenated, and decoy controls
  plus a mutation removing interpolation ownership.

### SP-AUDIT-107 - Release certifier leaves are absent from the canonical TCB (high)

- [ ] The `releaseContainment` inventory includes wrapper scripts but omits the
  leaf release-evidence generator, artifact validator, publisher, release
  workflow, and verifier nuspec they execute. TCB hashing and changed-TCB
  coverage derive solely from that incomplete inventory.
- Certifier impact: changing package-hash validation, evidence generation,
  publication validation, workflow authority, or package layout leaves the
  claimed trusted-computing-base digest and file count unchanged and bypasses
  changed-TCB selection.
- Reproduction: a read-only executable inventory probe flattened all canonical
  components and found all five authorities absent from its 264 paths:
  `New-SharpProofReleaseEvidence.ps1`, `Test-SharpProofReleaseArtifacts.ps1`,
  `Publish-SharpProofRelease.ps1`, `package-consumers.yml`, and the verifier
  nuspec.
- Required closure: inventory every statically invoked release-authority leaf,
  workflow, and package-layout authority exactly once. Add an architecture
  closure test and disposable-commit digest mutation proving that any leaf byte
  change alters both production and TCB digests.

### SP-AUDIT-108 - Checked arithmetic re-evaluates a mutated left operand (high)

- [ ] Checked-overflow proof rejects mutation only in the binary right operand.
  It transfers a left-side assignment, then re-evaluates that assignment from
  its post-state when deciding whether the enclosing checked operation is safe.
- Supported impact: `value = int.MinValue; checked((value = value + 1) - 2)`
  throws `OverflowException` at runtime, but the summary omitted the exception,
  allowing a false `DoesNotThrow` result.
- Reproduction: a temporary canonical Effects test expected
  `System.OverflowException`; the actual throw set was empty. The test was
  removed.
- Required closure: reject/refine using one pre-evaluation snapshot whenever
  either operand contains mutation. Add left/right assignment, increment,
  conversion, nested checked/unchecked, safe boundary, projection/analyzer, and
  mutation-reversion controls.

### SP-AUDIT-109 - Value-type instance calls omit type initialization (high)

- [ ] The effect call graph attaches a type-initialization boundary only to
  constructors and static methods. A non-`beforefieldinit` value type can also
  run its explicit static constructor before an instance method call.
- Supported impact: `default(Value).Touch()` was classified complete and
  effect-free even though `Value`'s static constructor increments a static
  field on the first call. A selected purity claim can therefore omit a real
  static write.
- Reproduction: a temporary Effects test defined the explicit struct static
  constructor and expected an incomplete type-initialization boundary; the
  projection was complete. The test was removed.
- Required closure: include value-type instance method/property entry in the
  exact type-initialization trigger model. Add struct/class, explicit/no cctor,
  default/constructed receiver, instance/static/constructor/property, repeated-
  call, projection, and mutation controls.

### SP-AUDIT-110 - Base property access is treated as open virtual dispatch (medium)

- [ ] Property effect scanning classifies dispatch from the accessor symbol
  alone, so every open virtual accessor is uncertain. Ordinary invocation uses
  operation-level `IsVirtual` and correctly recognizes an explicit base call as
  statically bound.
- Supported impact: `base.Value` produced an incomplete projection while the
  equivalent `base.GetValue()` was complete, rejecting an exact supported base
  property access for no runtime-dispatch reason.
- Reproduction: a temporary Effects test compared both members on the same
  derived class; the property assertion expected complete and received false,
  while the method control was complete. The test was removed.
- Required closure: pass operation-level property dispatch information into
  classification. Add base/this/interface/sealed/nonvirtual getter/setter/indexer
  controls and a mutation restoring symbol-only classification.

### SP-AUDIT-111 - Publication can overwrite compiler outputs created later (high)

- [ ] Publication invalidation runs before compilation, receives no compiler-
  output paths, and may claim an absent output path. If CoreCompile later creates
  the assembly at that path, the pre-created ownership marker causes launcher
  publication to accept and replace it.
- Supported impact: a clean packaged build configured the verifier result as
  `obj/Release/net8.0/Consumer.dll`; the build exited zero and reported Proven,
  then replaced the intermediate managed assembly with result JSON.
- Reproduction: a temporary package integration test required early rejection
  and a valid `MZ` intermediate assembly. Current build succeeded and named the
  DLL as the verifier result. The test was removed.
- Required closure: reserve every compiler output (intermediate/final assembly,
  PDB, reference assembly, generated files and manifests) before publication
  ownership is established, and revalidate claimed absent paths before commit.
  Add every publication member/output combination and clean/incremental controls.

### SP-AUDIT-112 - Valid long filenames overflow publication metadata names (medium)

- [ ] Publication lock and marker paths append 27- or 28-character suffixes
  directly to the user output basename. A valid path component near Linux
  `NAME_MAX` therefore becomes an invalid SharpProof metadata component.
- Supported impact: a valid absent 228-byte ASCII output basename failed before
  verification with `could not open a publication lock (errno 36)` solely
  because the derived lock filename exceeded 255 bytes.
- Reproduction: a temporary Linux host test first created and deleted the
  228-byte destination successfully, then attempted to acquire its publication
  set; acquisition threw `IOException`. The test was removed.
- Required closure: use fixed-size hash-derived metadata basenames in an owned
  directory rather than suffixing arbitrary user names. Add exact NAME_MAX
  boundaries, multibyte Unicode byte counts, all four members, collision, stable
  identity, cleanup, and compatibility controls.

### SP-AUDIT-113 - Nested body evidence accepts non-producer ordering (medium)

- [ ] The compiler producer orders parameter bindings by source variable and
  spec calls by instruction, but canonical serialization preserves arbitrary
  nested array order and hydration inserts rows into dictionaries without
  enforcing the producer order.
- Supported impact: reversing two valid parameter-binding rows, canonical-
  serializing, deserializing, and hydrating produced no error. Schema 12 thus
  accepts multiple canonical artifacts/fingerprints for the same lowered body.
- Reproduction: a temporary artifact test reused the two-parameter binding
  fixture, reversed its rows, and expected `InvalidDataException`; decode
  returned normally. The test was removed.
- Required closure: validate strict producer order (and uniqueness) for every
  nested evidence array or canonicalize it before hashing. Add reversed and
  duplicate parameter/spec/summary call arrays, valid one/many rows, and a
  mutation restoring dictionary-only acceptance.

### SP-AUDIT-114 - ContractFor reports foreign-compilation source locations (high)

- [ ] ContractFor validation selects any target location marked `IsInSource`,
  even when that syntax tree belongs to a `CompilationReference`, and passes
  the foreign location to the active compilation's diagnostic reporter.
- Supported impact: an empty companion for a referenced source interface
  produced only Roslyn warning `CS8785` because intended error `SPCF0004` used
  a location outside the compilation. The malformed companion therefore
  escaped the error-level ContractFor gate.
- Reproduction: the read-only analyzer auditor used the existing compilation-
  reference harness and received `CS8785` instead of `SPCF0004`.
- Required closure: select a source location only when its syntax tree belongs
  to the active compilation; otherwise report at the current companion
  attribute. Add reference/source/metadata targets and mutation controls.

### SP-AUDIT-115 - Methodless scopes hide rejected control attributes (medium)

- [ ] Rejected SharpProof control-attribute identity is checked only while a
  method is analyzed. Named-type and assembly scope validation recognizes only
  the exact trusted symbols and silently ignores same-metadata-name lookalikes.
- Supported impact: a source-shadowed `SharpProofSuppress` on an empty class
  emitted no `SP0047`; adding methods made the same rejected attribute visible
  through method-owned analysis. Methodless type and assembly declarations can
  therefore evade the documented rejected-API diagnostic.
- Reproduction: the read-only analyzer auditor confirmed zero diagnostics for
  the empty-type case and two method-owned `SP0047` diagnostics for its control.
- Required closure: validate rejected control identity directly at assembly
  and named-type declaration scopes, once per attribute, while retaining exact
  method selection behavior. Add suppress/trusted and source/project lookalikes.

### SP-AUDIT-116 - Mutation-bearing initializers fabricate scalar facts (high)

- [ ] Managed scalar flow transfers a local initializer or simple-assignment
  RHS and then evaluates the same expression again from the mutated state.
- Supported impact: runtime assigns `y = 3` for
  `var x = 1; var y = x + (x = 2); return 1 / (y - 3);`, but analysis
  re-evaluates the initializer as `2 + 2`, records `y = 4`, and removes the
  real `DivideByZeroException`. A selected no-throw claim can be accepted.
- Reproduction: a temporary canonical-container Effects test expected
  `System.DivideByZeroException`; the summary's throw set was empty. The test
  was removed.
- Required closure: evaluate each mutation-bearing value once against its
  pre-mutation snapshot and store that result. Cover declarators, assignments,
  left/right nesting, safe controls, exception projection, and a mutation that
  restores post-transfer re-evaluation.

### SP-AUDIT-117 - Selected auto-accessors disappear without an outcome (medium)

- [ ] Symbol validation returns early for every concrete non-extern method and
  relies on operation-block callbacks. A semicolon auto-accessor has no source
  body that enters the normal effect pipeline, and no end-of-compilation check
  reconciles selected methods with recorded outcomes.
- Supported impact: an `[EnforcePure]` auto-property setter, whose generated
  implementation writes receiver state, emitted neither a purity diagnostic
  nor typed `MissingOperationRoot` abstention.
- Reproduction: a temporary canonical-container analyzer test required any
  selected-accessor outcome and received an empty diagnostic set. The test was
  removed.
- Required closure: inventory all selected accessors and require an exact
  effect result or typed abstention. Add auto get/set/init, explicit, abstract,
  extern, suppression, outcome-recording, and mutation controls.

### SP-AUDIT-118 - Implicit empty constructors become unmodeled calls (medium)

- [ ] Source-call ownership requires a syntax reference. An implicit
  parameterless constructor has none, so object creation falls through to the
  external unknown boundary instead of the exact empty-source-constructor path.
- Supported impact: `new Empty()` for a sealed class with no members produced
  an incomplete effect projection, while fresh allocation itself is allowed
  and the equivalent explicit empty constructor is analyzable. This creates a
  systematic false unknown on an ordinary supported construction form.
- Reproduction: a temporary canonical-container Effects test required a
  complete projection for the implicit empty constructor and received false.
  The test was removed.
- Required closure: synthesize the exact implicit constructor summary, including
  the pinned base call and initializers, or typed-abstain consistently. Cover
  class/struct/record, implicit/explicit, base/member/cctor, and mutation cases.

### SP-AUDIT-119 - SBOM accepts contradictory SHA-256 identities (high)

- [ ] Release evidence generation and final validation filter an SPDX package's
  checksum rows to the expected SHA-256 and require one matching row, but never
  authenticate the complete checksum array.
- Certifier impact: adding a second SHA-256 row containing 64 zeroes left one
  expected-value match, so both current predicates accepted an SBOM that gives
  two contradictory immutable identities for the same package bytes.
- Reproduction: the read-only release auditor mutated an in-memory first-party
  SPDX package and confirmed two distinct SHA-256 values while both canonical
  acceptance predicates returned true.
- Required closure: require exactly one checksum row with the exact algorithm
  and value in evidence creation, final validation, and publication validation.
  Add wrong, duplicate, extra-algorithm, missing, stale, and canonical controls.

### SP-AUDIT-120 - Coverage policy can authorize its own removal (high)

- [ ] The coverage certifier accepts any nonempty caller-supplied project map
  and floors from zero through 100, then derives both the measured project and
  aggregate universes solely from that map. The baseline, producer, runsettings,
  and workflow are absent from the canonical TCB.
- Certifier impact: a fixture reduced the 22-project policy to one project and
  set every floor to zero; the schema predicate accepted it, and the canonical
  TCB digest did not change. A release commit can therefore delete most
  coverage and lower every threshold while its certifier blesses the weakened
  policy.
- Reproduction: the read-only release auditor executed the reduced-policy
  predicate and independently flattened the current TCB inventory.
- Required closure: derive the exact production-project universe independently,
  prohibit floor regression against a contract-owned predecessor, and inventory
  every coverage authority. Add project-removal, zero-floor, workflow/settings,
  digest, canonical policy, and mutation controls.

### SP-AUDIT-121 - Failed cache publication leaves a reusable entry (medium)

- [ ] Cache writing atomically publishes the exact-key file before post-write
  path validation and eviction. Failure or cancellation after that rename has
  no rollback path.
- Supported impact: a run canceled at the deterministic post-publication
  validation point returned `Canceled` but retained its cache JSON; a later
  identical run can replay it as a hit. Ordinary post-publication failure can
  similarly report cache unavailable while exceeding the configured byte cap.
- Reproduction: a temporary canonical-container worker test canceled through
  the existing path-validation hook after the exact cache file appeared. The
  response was canceled and the file remained. The test was removed.
- Required closure: make publish, validation, and eviction transactional; remove
  only the newly committed owned entry on every later failure/cancellation.
  Add cancellation, invalid sibling, eviction, pre-existing valid entry, byte-
  bound, later-hit, and rollback-removal mutation controls.

### SP-AUDIT-122 - Clean cannot recover publication-set metadata (medium)

- [ ] Publication-set markers persist beside every member, but the verifier
  registers no Clean hook or `FileWrites` entries for them. The failure message
  instructs users to clean before changing paths even though `dotnet clean`
  does not remove the metadata.
- Supported impact: a successful set A build followed by `dotnet clean` and a
  set B build with an alternate result path still failed with “publication
  paths partially overlap.” Recovery requires manual discovery and deletion of
  hidden implementation files.
- Reproduction: a temporary packaged-consumer test performed that exact
  build/clean/rebuild sequence in the canonical container; the second build
  exited one. The test was removed.
- Required closure: register owned marker/lock metadata for supported Clean or
  provide an explicit safe reset target that removes a complete matching set
  under its locks. Add clean/no-clean, changed member, partial failure,
  unrelated-neighbor, incremental, and mutation controls.

### SP-AUDIT-123 - Cache hits ignore a lowered byte cap (medium)

- [ ] Cache capacity is intentionally absent from the semantic cache key, but
  the read path never checks an existing exact-key entry or the complete owned
  cache set against the request's current `MaximumBytes` value.
- Supported impact: after a refutation was cached under the default cap, setting
  only `MaximumBytes = 1` returned the oversized entry as `Hit`, made no second
  backend call, and left the file resident beyond the documented maximum size.
- Reproduction: a temporary canonical-container worker test observed
  `Written` then `Hit`, with backend call count one instead of two. The test was
  removed.
- Required closure: enforce the current cap under the cache lock before LRU
  touch or replay; remove an oversized owned hit or evict other owned entries
  before returning it. Add single/multiple-entry, same/lowered cap, unowned and
  symlink siblings, concurrency, byte-bound, and read-check mutation controls.

### SP-AUDIT-124 - One SARIF path breaks multitarget verification (medium)

- [ ] Request, result, and compiler-manifest defaults are framework-scoped, but
  the configured SARIF path is passed unchanged into every inner framework
  build. Publication ownership rejects the resulting partially overlapping
  sets.
- Supported impact: a packaged `net8.0;net9.0` strict build with one ordinary
  SARIF path proved and published net8, then net9 failed with “publication paths
  partially overlap another publication set.”
- Reproduction: a temporary canonical-container package test exercised the
  two-framework build and received exit one at the net9 invalidation task. The
  test was removed.
- Required closure: make SARIF framework-scoped for multitarget builds or define
  one intentional aggregate owner. Add relative/absolute/default paths,
  two/many frameworks, build order, parallel/serial, incremental/Clean, and
  exact publication binding controls.

### SP-AUDIT-125 - Archive checkouts cannot run container tooling (medium)

- [ ] Documentation supports source obtained as an archive and says Git is not
  a host prerequisite, but every finite non-dev container command unconditionally
  performs `git clone --shared` and `git rev-parse HEAD` before dispatch.
- Supported impact: a canonical source snapshot copied without `.git` failed
  before `tooling contract` with `fatal: repository ... does not exist`; build,
  test, and acceptance are equally unreachable from the advertised archive form.
- Reproduction: a disposable in-container tar copy excluded `.git`, then
  invoked the canonical entrypoint with that copy as `SHARPPROOF_REPO_ROOT`.
  No repository files or external state were changed.
- Required closure: materialize finite tasks directly from a source snapshot,
  using Git only when available for exact-commit evidence, or explicitly restore
  Git as a documented prerequisite. Add archive/Git, dirty/deleted/untracked,
  executable-bit, contract/build, and evidence-command controls.

### SP-AUDIT-126 - Ill-formed UTF-16 is changed inside compiler artifacts (high)

- [ ] Compiler string terms and generated source text can contain lone UTF-16
  surrogates, but default JSON/text encoding replaces them with U+FFFD. The
  producer neither preserves nor typed-rejects the value before hashing and
  serialization.
- Supported impact: a valid ASCII-escaped contract comparing U+D800 with
  U+FFFD lowered successfully, then round-trip serialization collapsed both
  values to U+FFFD. Hydration failed with `CompilerManifestMismatch` instead of
  preserving semantics or returning a typed unsupported expression.
- Reproduction: the read-only compiler auditor observed code units
  `55296,65533` before serialization and `65533,65533` afterward; decode threw
  `InvalidDataException`. Generated-source hashing also collided for the two
  values.
- Required closure: for frozen schema 12, reject ill-formed UTF-16 before any
  hash or wire encoding and map affected literals to typed abstention. Add
  source/generated text, high/low/lone/paired surrogates, U+FFFD, diagnostics,
  fingerprint, round-trip, and validation-removal mutation controls.

### SP-AUDIT-127 - Peer-generated target members evade ContractFor validation (medium)

- [ ] The incremental validator sees a handwritten companion before peer
  generators run, while final-compilation reconciliation searches only generated
  companion declarations. It never revalidates a wholly handwritten companion
  when a peer generator expands the target type.
- Supported impact: an empty handwritten companion was valid for an initially
  empty partial target; a peer-generated `Added()` target member then left the
  final one-to-one surface stale, yet both validation owners emitted zero
  diagnostics instead of `SPCF0004`.
- Reproduction: the read-only analyzer auditor ran the two-generator final-
  compilation fixture with zero compiler and SharpProof diagnostics.
- Required closure: reconcile every companion whose target or companion surface
  changed in the final compilation, with one diagnostic owner. Add generated
  target/companion/both, unchanged surface, duplicate, profile, and generator-
  order mutation controls.

### SP-AUDIT-128 - Partial-property definition contracts use the wrong body (medium)

- [ ] Requires analysis normalizes a partial getter symbol to its implementation
  but retains the semicolon definition declaration selected earlier. The
  normalized method is therefore paired with a declaration that cannot supply
  its implementation CFG.
- Supported impact: a legal definition-side `[return: Positive]` partial
  property with `get => 1` in its implementation emitted spurious `SP0047`;
  moving only the attribute to the implementation emitted no diagnostic.
- Reproduction: the read-only analyzer auditor confirmed the definition and
  implementation symbols expose `PartialImplementationPart`, yet only the
  definition-side placement failed.
- Required closure: normalize symbol and owning declaration together before
  all requires/body analysis. Add definition/implementation/duplicate return
  and accessor attributes, getter/setter, generated partials, and mutation
  controls.

### SP-AUDIT-129 - Publication plans omit immutable artifact identities (high)

- [ ] Plan-only publication validates package hashes transiently, then emits
  only package IDs, versions, paths, and filenames. It records no package hash
  or size and no release-manifest, SBOM, or checksum-file identity; nothing
  subsequently validates the plan.
- Certifier impact: different validated package bundles with the same IDs,
  versions, filenames, and embedded commit collapse to the same publication
  plan representation, so the dry-run evidence cannot identify the bytes it
  claims are ready to publish.
- Reproduction: the read-only release auditor inspected the current plan and
  producer projection: every package row has zero hash/byte properties and the
  sole repository consumer is the producer itself.
- Required closure: embed main/symbol hashes and sizes plus manifest/SBOM/
  checksum identities, validate the completed plan, and bind it into the exact-
  commit qualification receipt. Add two-bundle, stale-plan, changed-symbol,
  canonical bundle, and projection-removal mutation controls.

### SP-AUDIT-130 - SBOM attestation includes undescribed symbol packages (medium)

- [ ] The workflow attests `nupkgs/*.*nupkg` with the SBOM predicate, selecting
  both main and symbol packages, while SBOM generation models and hashes only
  main `.nupkg` files.
- Certifier impact: the exact current bundle resolved six SBOM-attestation
  subjects, but only the three main package hashes appeared in the SBOM. Each
  symbol package is thus associated with a predicate describing different
  bytes, despite a separate provenance attestation already covering it.
- Reproduction: the read-only release auditor resolved the workflow glob and
  compared all six subjects to first-party SPDX checksum rows: 6 subjects, 3
  represented, 3 absent.
- Required closure: restrict the SBOM subject set to main packages or explicitly
  model symbol artifacts in an applicable SBOM. Add exact glob resolution,
  main/symbol pairs, absent/extra subjects, and workflow-pattern mutations.

### SP-AUDIT-131 - Public package metadata is not contract-validated (medium)

- [ ] Package and release validators authenticate ID, version, repository,
  dependencies, and layout but ignore `authors`, `projectUrl`, `description`,
  and `tags`. Those fields are duplicated across nuspecs despite checked-in
  release authorities.
- Certifier impact: replacing them with a fabricated publisher, unrelated URL,
  Windows-only product description, and unrelated tags left every field consumed
  by the current evidence, planning, and exact-package predicates unchanged.
- Reproduction: the read-only release auditor applied that in-memory nuspec
  mutation and compared the complete consumed projections.
- Required closure: establish one package-metadata catalog and require exact
  values during pack, artifact qualification, final validation, and planning.
  Add every field, three packages, conflicting support claims, canonical output,
  and individual-field mutation controls.

### SP-AUDIT-132 - Compiler diagnostic coordinates violate the source-location model (medium)

- [ ] The compiler-artifact producer stores Roslyn's zero-based mapped line and
  column directly, while all other `WorkerSourceLocation` producers and the
  protocol contract require positive, one-based coordinates. Artifact validation
  contains a weaker diagnostic-only exception that admits zero.
- Supported impact: a physical line 5, column 16 error was recorded as 4,15; a
  first-character error was accepted as 0,0 even though
  `WorkerProtocolMetadata.IsSourceLocationValid` rejects that location.
- Reproduction: the read-only compiler auditor produced both artifacts and
  confirmed the source span remained correct while only display coordinates
  violated the shared model.
- Required closure: add one to mapped source coordinates, allow the all-zero
  sentinel only for genuinely non-source diagnostics, and cover first position,
  multiline, `#line`, sentinel, and conversion-removal mutations.

### SP-AUDIT-133 - Release qualification omits the release-configuration certifier (high)

- [ ] `Test-SharpProofReleaseConfiguration.ps1` owns the tag-ruleset,
  environment, and release-workflow contract and can emit exact-commit evidence,
  but acceptance and every release workflow omit it from their command graphs.
- Certifier impact: qualification can report success without any retained
  evidence that the owner-managed release configuration matches its checked-in
  contract. This is independent of the known defects inside that checker.
- Evidence: repository command-graph inspection found zero executable callers;
  only explanatory release documentation names the script.
- Required closure: repair the checker rows already registered, invoke it exactly
  once before release qualification, and bind its exact-commit receipt into the
  qualification document. Add disconnected, stale-receipt, wrong-commit, and
  canonical graph fixtures.

### SP-AUDIT-134 - Plan-only accepts a package version outside release authority (medium)

- [ ] Evidence generation requires only that all six package versions agree,
  and plan-only publication accepts any matching SemVer. Neither path requires
  equality with `SharpProof.Release.props`, the frozen version owner.
- Certifier impact: a same-commit bundle consistently labeled
  `9.9.9-preview.9` satisfies the evidence and plan predicates even though the
  checked-in candidate is `1.0.0-preview.1`. The later tag-aware wrapper mitigates
  actual promotion but does not repair the dry-run certificate.
- Evidence: the release auditor evaluated the exact predicates with the foreign
  version; every internal bundle predicate passed and the publisher contains no
  release-property comparison.
- Required closure: derive one ordinal version from the release props, require it
  in evidence and planning, and record the authority identity. Add override,
  case-only, mixed-package, canonical, and comparison-removal fixtures.

### SP-AUDIT-135 - The documentation support-contract validator is disconnected (medium)

- [ ] `Generate-Readme.ps1 -Verify` checks package/version/support text and stale
  Windows-verifier claims, but acceptance, container dispatch, and all workflows
  omit it even though every main package embeds the README.
- Certifier impact: a forbidden `SharpProof.Verifier.Win-x64` support claim was
  rejected by the dormant validator but remained outside every release-blocking
  command graph.
- Evidence: an in-memory documentation mutation triggered the validator; exact
  caller searches across acceptance, workflows, and the dispatcher returned
  none.
- Required closure: run verification during static acceptance and release
  qualification, then bind its exact-commit outcome. Add stale-platform,
  package/version drift, disconnected-call, and clean-document controls.

### SP-AUDIT-136 - Project-body package path overrides are silently ignored (medium)

- [ ] NuGet imports `SharpProof.props` before the consumer project body and
  eagerly freezes analyzer, collector, tools, launcher, worker, protocol, and
  BuildTasks paths. Later public-property assignments change the visible values
  but not the private paths consumed by targets.
- Supported impact: a real MSBuild evaluation showed
  `SharpProofAnalyzerDirectory=/tmp/audit-analyzers` while
  `_SharpProofAnalyzerPath` still named the package default; collector paths had
  the same split. Existing tests mask it by recomputing private properties.
- Reproduction: the canonical container evaluated an ordinary project-body
  override and printed the divergent public/private property set. Global `-p:`
  assignment remains an early-working control.
- Required closure: either remove unsafe runtime overrides before preview or
  derive every retained override after project evaluation. Add project-body,
  global, analyzer, collector, worker, launcher, and packaged-feed controls.

### SP-AUDIT-137 - Whitespace-only SARIF configuration fails verification (medium)

- [ ] Invalidation trims `SharpProofVerifySarifFile` and treats whitespace as
  absent, while targets test only raw inequality with the empty string and pass
  `--publish-sarif` plus the whitespace value. The launcher then rejects it.
- Supported impact: an ordinary strict packaged build whose property evaluated
  to two spaces failed with launcher usage exit 2 instead of behaving as the
  documented nonblank opt-in being unset.
- Reproduction: the focused canonical-container package test failed exactly at
  launcher argument validation; an XML whitespace-literal control was normalized
  to empty by MSBuild and passed. The temporary test was removed.
- Required closure: compute one whitespace-aware presence value and use it for
  invalidation, argument construction, and messages. Cover empty, spaces, tabs,
  property-function whitespace, and valid paths containing spaces.

### SP-AUDIT-138 - Compiler-elided invocations still receive SP0027 (medium)

- [ ] Requires call-site discovery analyzes every `IInvocationOperation` without
  checking whether the compiler emits it. Undefined `[Conditional]` calls and
  calls to unimplemented partial methods are therefore treated as executing.
- Supported impact: `Positive(-1)` emitted SP0027 both when its conditional
  symbol was undefined and when defined; an erased unimplemented-partial call
  emitted SP0027 plus SP0047. Defined/implemented controls correctly emitted
  SP0027.
- Reproduction: the analyzer auditor executed both source matrices against the
  current engine.
- Required closure: share the exact invocation-emission policy already used by
  Effects. Cover conditional metadata/source methods, multiple symbols, erased
  arguments, missing/implemented partial methods, and admission-removal mutation.

### SP-AUDIT-139 - Duplicate additional files make the collector reject its own snapshot (medium)

- [ ] Final compilation capture sorts but retains duplicate `AdditionalFiles`;
  canonical fingerprint validation requires unique paths, so the producer
  immediately rejects an otherwise legal repeated identical analyzer input.
- Supported impact: supplying the same canonical additional file twice produces
  SP0049 even when both path and content are identical and no evidence is
  ambiguous.
- Evidence: the collector passes the analyzer-host list unchanged, capture emits
  both rows, and producer validation applies the strict-unique reader predicate.
- Required closure: preserve observable multiplicity explicitly or collapse only
  same-path/same-hash rows and reject conflicting content. Add exact duplicates,
  lexical aliases, input permutations, conflicting bytes, and dedup-removal
  mutation controls.

### SP-AUDIT-140 - Relational-summary term work bypasses the symbolic-operation limit (medium)

- [ ] The summary builder charges fixed CFG actions but traverses, substitutes,
  rebuilds, merges, and composes arbitrarily wide term DAGs without charging
  their nodes. The separate depth check does not bound shallow width.
- Supported impact: a one-block caller can instantiate a dependency containing
  thousands of shallow unique leaves while a tiny symbolic-operation budget
  remains unexhausted, contradicting the documented 65,536-operation bound.
- Evidence: all dependency substitution and final relation construction paths
  are outside `Spend`; the limit gates only a small fixed set of block actions.
- Required closure: charge every visited/created term node across direct and
  dependency paths, or impose an equivalent total DAG-size bound. Add exact/+1,
  broad/shallow, shared-node, composition, and charge-removal controls.

### SP-AUDIT-141 - Captured primary-constructor reads lose receiver ownership (high)

- [ ] The effect scanner treats every by-value parameter reference as
  effect-free, including a primary-constructor parameter captured into hidden
  instance state and read later by an ordinary instance member.
- Supported impact: `sealed class Sample(int value) { int Read() => value; }`
  produced a complete summary without `ReadsReceiverState`; an exact
  `EffectContract(None)` can therefore be proven for a real receiver-state read.
- Reproduction: a focused canonical-container Effects regression expected the
  receiver read and failed with `False`. The temporary test was removed.
- Required closure: identify instance-captured primary-constructor parameters as
  receiver-backed while retaining ordinary method parameters as snapshots. Cover
  class/record/struct, methods/accessors, constructor-only use, forwarding, exact
  claims, and blanket-parameter mutation.

### SP-AUDIT-142 - The standalone Docker build target cannot run its default command (medium)

- [ ] The Dockerfile `build` stage copies the repository to `/src` and declares
  `CMD ["build"]`, but unlike later `test` and `package` stages it does not set
  `SHARPPROOF_REPO_ROOT=/src`. The shared entrypoint defaults to the nonexistent
  `/workspace/SharpProof`.
- Supported impact: running the named immutable build image fails during its
  pre-dispatch Git snapshot instead of executing the build it declares.
- Evidence: the Dockerfile stage/root/command and entrypoint default form an
  unconditional path mismatch; Compose avoids it only by using the `dev` stage.
- Required closure: give every named stage the same explicit repository root and
  non-root execution contract. Add direct build/test/package image-command,
  working-directory, UID, and Compose parity fixtures.

### SP-AUDIT-143 - Publication-plan output can overwrite certified bundle inputs (high)

- [ ] Plan-only validates the bundle, then writes `PlanOutputPath` without
  checking whether it aliases the release manifest, checksums, SBOM, or any main
  or symbol package.
- Certifier impact: planning can exit successfully after replacing the very
  manifest or package it just certified, leaving a success plan beside a newly
  invalid source bundle.
- Evidence: validation precedes an unconditional `WriteAllText`; no input/output
  topology guard exists on the plan path.
- Required closure: canonicalize and reject every bundle-input overlap before
  validation, reserve evidence filenames, and publish the plan atomically. Add
  every input role, lexical alias, valid disjoint path, and post-plan revalidation
  controls.

### SP-AUDIT-144 - Publication plans do not bind or validate destinations (medium)

- [ ] Plan-only records the raw main source without validating its URI and
  returns before computing the effective symbol source, so symbol destination is
  absent entirely.
- Certifier impact: invalid HTTP/relative main targets can receive plan evidence,
  and two different symbol destinations collapse to identical plans.
- Evidence: main HTTPS validation and effective symbol-source computation both
  occur only after the plan-only return.
- Required closure: validate target syntax in both modes, compute destinations
  before projection, and record main and symbol destinations separately. Cover
  invalid schemes, relative values, inherited/distinct symbol targets, targetless
  plans, and projection-removal mutations.

### SP-AUDIT-145 - Release qualification omits portable OS consumers (medium)

- [ ] The only package-consumer and tag-qualification jobs run on Ubuntu; the
  workflow has no Windows/macOS matrix, and publication depends only on that
  Linux qualification chain.
- Certifier impact: exact package bytes can reach publication without exercising
  the portable analyzer package on Windows or macOS, despite those hosts
  remaining in its supported surface.
- Evidence: workflow graph inspection found no `strategy`/`matrix`; no required
  job downloads the exact package artifact on all three supported OS families.
- Required closure: add an exact-artifact Linux/Windows/macOS portable-consumer
  matrix and require it from qualification. Keep full verifier execution inside
  the canonical Linux container. Add missing-leg, source-build, wrong-artifact,
  disconnected-matrix, and canonical controls.

### SP-AUDIT-146 - Diagnostic documentation contradicts rejected-API behavior (medium)

- [ ] `docs/diagnostic-examples.md` says a readable wrong-payload Contract API
  disables analysis without a diagnostic, while implementation and the other
  public documents emit and promise SP0047 `ContractApiIdentityRejected`.
- Supported impact: users are told to expect silence for behavior that produces
  an explicit incomplete-coverage diagnostic, obscuring the distinction from
  unreadable payloads and SP0050.
- Evidence: resolver rejection and analyzer diagnostic routing match README and
  public API text; only the diagnostic example states the opposite.
- Required closure: add a wrong-hash readable-assembly behavioral test and bind
  the documentation validator to the result. Forbid the stale silent-analysis
  claim while retaining the unreadable-payload distinction.

### SP-AUDIT-147 - The canonical TCB inventory can authorize its own shrink (high)

- [ ] TCB paths and `inventorySha256` live in the same editable acceptance
  contract, and both gates merely recompute the union hash against that sibling
  field. No independent owner pins required runtime membership.
- Certifier impact: removing `IrProgramInterpreter.cs` from `replay` and updating
  the colocated digest leaves components nonempty, all paths valid, coverage
  nonblocking, and release digesting satisfied while proof-runtime code has left
  the declared TCB.
- Evidence: the audit computed the accepted reduced-union digest and verified the
  removed file is neither a mutation target nor an independent tripwire.
- Required closure: derive the canonical TCB from evaluated production authority
  plus explicit semantic roles, or pin changes through an independent reviewed
  inventory. Add required-member deletion/move, component transfer, union/hash
  rewrite, and canonical-update controls.

### SP-AUDIT-148 - Evaluated production Compile items can escape all certifier universes (high)

- [ ] TCB, complexity, and coverage infer production sources from separate
  checked-in path lists/directories rather than evaluated project `Compile`
  items. `Directory.Build.props`, which can add linked production source, is not
  itself TCB-owned.
- Certifier impact: a tracked `eng/Fixture.cs` linked into production through the
  common props compiles into shipping assemblies while escaping changed-TCB,
  production complexity, and project/aggregate coverage ownership.
- Evidence: the three gates respectively consume contract paths, regex-derived
  directory roots, and project-name path prefixes; none evaluates the added
  Compile item.
- Required closure: establish one evaluated production source/assembly manifest
  consumed by TCB, complexity, and coverage, and inventory the build files that
  can alter it. Add linked-source, outside-root, generated, duplicate, removed,
  and assembly-identity fixtures.

### SP-AUDIT-149 - Complexity gates ignore production preprocessor symbols (medium)

- [ ] Both complexity parsers use C# parse options without the symbols defined
  by actual production projects. `SharpProof.Dataflow`, for example, defines
  `SHARPPROOF_DATAFLOW_ARGUMENT_GUARD`.
- Certifier impact: arbitrary production code placed under that active symbol is
  compiled but treated as disabled text by aggregate and algorithm-size metrics,
  so ceilings do not measure the shipped source.
- Evidence: source-metric and architecture parsers omit symbols while the project
  file supplies one to the compiler.
- Required closure: measure evaluated per-project parse options/Compile items and
  union shared-file configurations conservatively. Add active/inactive symbol,
  linked-file, multi-project, exact metric, and symbol-removal controls.

### SP-AUDIT-150 - Generated-output exclusions lack generator provenance (medium)

- [ ] Complexity exclusion trusts an editable filename/header manifest but does
  not bind each excluded production file to a declared generator and authoritative
  input set.
- Certifier impact: an ordinary hand-authored `*.generated.cs` can be added to
  the exclusion manifest and omitted from complexity even though it is shipping
  executable code and no deterministic generator owns it.
- Evidence: boundary enforcement equates generated naming/header with the
  manifest; the complexity gate excludes every manifest row without provenance.
- Required closure: record generator/input/output ownership and verify output
  bytes through the generator before exclusion. Add hand-authored, renamed,
  stale-input, byte-drift, duplicate-owner, and canonical generated controls.

### SP-AUDIT-151 - UnsupportedContract callable coverage is unreachable (medium)

- [ ] Failed preparation with claim reason `UnsupportedContract` is always
  collapsed to callable reason `SemanticUnknown`, although the frozen protocol
  and public unknown-reason documentation explicitly admit callable
  `Incomplete/UnsupportedContract`.
- Supported impact: the existing mismatched-result-type fixture retains the
  exact claim reason but exposes the wrong callable and SARIF reason, making the
  documented category unreachable.
- Evidence: the only production callable mapper preserves `UnsupportedCallable`
  and collapses every other unsuccessful preparation; no production assignment
  of callable `UnsupportedContract` exists.
- Required closure: add one authoritative preparation/claim-to-callable mapper
  that preserves exact contract failure while retaining semantic unknown for
  lower-level abstention. Cover all failure reasons, mixed claims, SARIF, and
  collapse-restoration mutation.

### SP-AUDIT-152 - Effect vacuity and certainty can contradict each other (medium)

- [ ] Response validation independently accepts `EffectCertainty=VacuousEntry`
  and `Vacuity=None`; it never enforces the producer invariant that vacuous
  effect proof carries contradictory-precondition evidence and a core.
- Certifier impact: changing only a valid non-vacuous proven effect result's
  certainty to `VacuousEntry` leaves strict request-bound validation successful.
- Evidence: the closed certainty and vacuity predicates each accept the tuple;
  the real assembler emits only the coupled form.
- Required closure: add a cross-field effect-vacuity invariant and cover both
  contradictory directions, proof-core presence, non-vacuous proof, refuted/
  unknown controls, and invariant-removal mutation.

### SP-AUDIT-153 - Missing nested JSON fields default into valid evidence (high)

- [ ] Exact-property presence is checked only at request/response roots. Nested
  models initialize semantic fields to valid values, so omitted wire properties
  such as `manifest.schemaVersion` silently become the current version.
- Certifier impact: removing only the nested schema-version property from an
  otherwise valid request-bound response survives deserialization, hash/equality
  checks, and strict validation even though the wire document never declared its
  schema.
- Evidence: nested deserialization has no presence map and the generated manifest
  initializer supplies the accepted current value. The same class reaches budget,
  vacuity, usage, and summary fields whose defaults are valid.
- Required closure: enforce recursive exact required-property presence with
  bounded converters/generated metadata. Add every nested model, absent versus
  explicit default, unknown/duplicate property, canonical round-trip, and
  presence-check mutation controls.

### SP-AUDIT-154 - Strict protocol validation accepts noncanonical order and spelling (medium)

- [ ] Serialization canonically orders manifest/results/buckets/evidence, but
  validation sorts or set-compares received arrays and the enum converter accepts
  case-insensitive spellings.
- Certifier impact: reversing all major arrays without resealing still validates;
  lowercase enum text likewise hydrates into canonical values. Multiple wire
  byte representations therefore certify as the same supposedly canonical
  evidence.
- Evidence: manifest payload and result-ID comparisons normalize received order
  instead of requiring it to match canonical projections.
- Required closure: compare every received array and enum token to its canonical
  representation before certification. Add reversed/subset arrays, nested cores/
  models, enum case, canonical control, and normalization-removal mutations.

### SP-AUDIT-155 - Manifests accept globally contradictory assumption kinds (medium)

- [ ] Manifest validation enforces assumption identity/kind only per callable,
  while response summarization groups globally and rejects one ID carrying two
  kinds.
- Certifier impact: a sealed manifest with `shared` as `Precondition` in one
  callable and `UserAssume` in another validates, yet every exact response must
  fail `summary.assumption_conflict`.
- Evidence: the manifest and response validators apply different identity
  scopes to the same assumption ID domain.
- Required closure: define global versus callable-scoped assumption identity and
  enforce it consistently. Add same/different kind, same/different callable,
  generated IDs, summary reconstruction, and scope-check mutation controls.

### SP-AUDIT-156 - SHA256SUMS byte canonicality is not validated (medium)

- [ ] Evidence generation emits strict UTF-8 without BOM, LF separators, and a
  terminal LF, but final validation reads logical lines and rejoins them,
  erasing encoding and newline differences.
- Certifier impact: UTF-16LE/BOM or CRLF/no-terminal-newline checksum files with
  the same displayed seven rows are accepted as canonical release evidence.
- Evidence: the validator's `Get-Content` comparison cannot distinguish those
  byte forms; another repository evidence helper already performs strict raw
  comparison.
- Required closure: reconstruct and compare exact UTF-8 bytes, rejecting BOM,
  invalid UTF-8, CR/LF drift, missing/extra terminal newline, and noncanonical
  digest spelling. Add byte-level mutation fixtures.

### SP-AUDIT-157 - Release version identity is case-folded (medium)

- [ ] Evidence generation deduplicates nuspec versions case-insensitively, and
  artifact validation/publication compare version text case-insensitively even
  though tag-to-source authority uses ordinal equality.
- Certifier impact: a coherent `1.0.0-PREVIEW.1` bundle can validate and receive
  a plan for tag `v1.0.0-preview.1`, despite byte-distinct version authority.
- Evidence: generator, validator, and publisher all contain case-folding at the
  package/SBOM/manifest boundary; the tag owner does not.
- Required closure: derive one source version and use ordinal equality in nuspec,
  filenames, manifest, SBOM, namespace, tag, and plan. Add each case-only drift
  and comparison-removal control.

### SP-AUDIT-158 - Acceptance skip switches still certify Passed (high)

- [ ] `Verify.ps1 -SkipTests` records semantic, package, fuzz, corpus, and
  performance phases as skipped, then writes top-level `status=passed` and prints
  that acceptance passed. `-SkipBuild` runs downstream `--no-build` gates against
  potentially stale binaries with the same success semantics.
- Certifier impact: partial developer modes can emit evidence indistinguishable
  at the top level from full release acceptance for current source.
- Evidence: both switch branches and final evidence projection are unconditional;
  no caller or test restricts their output to a non-qualifying schema/status.
- Required closure: make partial runs explicitly non-qualifying and impossible to
  consume as release evidence; bind source/build identity for no-build reuse. Add
  each skip combination, stale binary, full run, and status-forging controls.

### SP-AUDIT-159 - The nightly fuzz campaign is disconnected (high)

- [ ] Nightly runs only ordinary `tooling acceptance`, whose fuzz phase always
  uses the PR case count and fixed seed. The script that owns `nightlyCases`,
  rotating seeds, and retained-seed replay has no workflow or container-command
  caller.
- Certifier impact: the nightly job can remain green while never executing the
  larger/rotating campaign represented by the checked-in fuzz contract.
- Evidence: workflow and dispatcher command graphs contain no nightly campaign
  invocation; acceptance hardcodes the PR count and seed.
- Required closure: expose one container-native nightly fuzz command, invoke it
  from the nightly workflow, retain seed/case/commit evidence, and add command-
  graph, fixed-seed, retained-seed, case-count, and disconnected-script controls.

### SP-AUDIT-160 - Standalone gate evidence is not source or result bound (medium)

- [ ] `Invoke-SharpProofGateEvidence.ps1` runs corpus/performance with
  `--no-build` and declares success from exit zero plus any parseable non-null
  JSON. It checks neither result schema/Passed/gate identity nor source/build
  identity.
- Certifier impact: a stale gate binary emitting `{}` can receive `passed=true`
  evidence attributed to current source.
- Evidence: all semantic fields are projected without validation after the
  process exit; no assembly or commit digest is compared.
- Required closure: require the exact result schema and successful gate identity,
  bind executable/source commit and inputs, and reject stale/no-build reuse unless
  independently certified. Add `{}`, wrong gate, false Passed, stale assembly,
  canonical, and omitted-check mutations.

### SP-AUDIT-161 - Acceptance omits repeated forced-termination stability (medium)

- [ ] PR gates run the five-launch forced-termination deadline regression, but
  acceptance excludes Performance-category tests and its performance executable
  measures forced termination only once.
- Certifier impact: release/nightly acceptance is weaker than PR certification
  for cancellation cleanup stability and can miss intermittent deadline drift.
- Evidence: the dispatcher includes the exact test only in `pr-gates`; acceptance
  filters it out and has no equivalent repetition.
- Required closure: make one catalog-owned repeated termination gate part of
  acceptance and PR paths, with identical limits/evidence. Add missing/one-shot,
  five-run, threshold, and command-graph controls.

### SP-AUDIT-162 - Hydration accepts cyclic and oversized lowered CFGs (high)

- [ ] The compiler producer rejects cycles and more than 64 reachable blocks,
  but `CompilerLoweredArtifact.DecodeBody` checks only entry shape and the
  4,096-instruction limit.
- Certifier impact: replacing a terminal return with `Goto 0` survived canonical
  round-trip and hydration; an equivalent 65-block graph is also producer-
  impossible but reader-admissible.
- Reproduction: the compiler-lowering auditor's canonical probe reported
  `self-cycle=accepted`.
- Required closure: apply the exact producer acyclicity/reachable-block predicate
  before creating a prepared body. Add self/back/cross cycles, unreachable rows,
  64/65 blocks, instruction interplay, and validator-removal mutation.

### SP-AUDIT-163 - Hydration accepts bodyless successful postcondition callables (high)

- [ ] Honest lowering requires an admitted body and emits a `Trivial` body even
  for contract-only void methods, but the decoder accepts `Body=null` alongside
  postcondition claims.
- Certifier impact: deleting the body from `void Check() { Ensures(false); }`
  survived canonical round-trip and hydration, leaving a producer-impossible
  proof target without executable semantics.
- Reproduction: the compiler-lowering probe reported `bodyless=accepted` after
  confirming the honest artifact contained a trivial body.
- Required closure: reject missing bodies for successful postcondition targets
  and define exact failed/effect-only exceptions. Add void/value, trivial/program,
  failed target, effects-only, and body-check mutation controls.

### SP-AUDIT-164 - Portable IR encode/decode type-depth domains disagree (medium)

- [ ] The encoder recursively emits nested sequence types without a bound, while
  the decoder rejects depth 257.
- Supported impact: honest in-memory IR at depth 257 can be serialized by the
  producer but cannot be consumed, turning producer output into a manifest
  failure instead of typed abstention.
- Reproduction: a canonical codec probe accepted depth 256 and encoded depth 257,
  whose decode threw `Portable IR type depth exceeds the supported limit`.
- Required closure: enforce the same exact depth during type construction/encode
  with typed abstention or make both paths iterative over one bounded domain. Add
  255/256/257, mixed nesting, shared nodes, and bound-removal controls.

### SP-AUDIT-165 - Z3 AST temporaries rely on finalization for native release (medium)

- [ ] SMT encoding creates many temporary `MkInt`, comparison, Boolean,
  conditional, and division wrapper objects without disposing them. The encoder
  owns cached roots but cannot reach those intermediate wrappers.
- Supported impact: a long-lived solver lane accumulates native references until
  managed GC/finalization, making query memory behavior depend on finalizer timing
  rather than the documented per-query lifecycle.
- Evidence: the file explicitly disposes other Z3-return wrappers and recognizes
  their native ownership, while these construction paths have no owner or
  `Dispose` scope.
- Required closure: introduce explicit expression ownership for every temporary
  and dispose after solver consumption. Add many-query native-allocation,
  no-GC-region, post-finalizer, cancellation, exception, and disposal-removal
  controls against the pinned Z3 build.

### SP-AUDIT-166 - Corpus snapshot schema labels are not validated (medium)

- [ ] The corpus writer emits schema 3, but the loader discards comment/header
  rows and never requires one exact schema declaration.
- Certifier impact: missing, duplicated, contradictory, schema 2, and schema 999
  snapshots are accepted whenever their data rows still parse.
- Required closure: make the schema/header part of the parsed canonical format.
  Add exact-schema, missing, duplicate, conflicting, older, newer, and malformed
  header controls plus a validator-removal mutation.

### SP-AUDIT-167 - Fuzz campaign validation accepts non-schema JSON (medium)

- [ ] `Invoke-SharpProofFuzzCampaign.ps1` coercively casts numeric fields,
  treats `Failures: null` as zero, and does not require the schema-4
  `FrontendCoverage` shape.
- Certifier impact: an exit-zero runner receipt with numeric strings, omitted
  coverage details, and null failure evidence can certify a passing campaign.
- Required closure: validate the exact property set, JSON token kinds, non-null
  arrays, counts, and full schema-4 coverage object before accepting a run. Add
  one mutation fixture per weakened field.

### SP-AUDIT-168 - Failed fuzz runs preserve stale passing evidence (medium)

- [ ] The fuzz output directory is reused without invalidating `campaign.json`
  or obsolete per-seed logs before manifest validation and process launch.
- Certifier impact: an early validation or launch failure can leave a prior
  `passed: true` receipt and removed-seed evidence looking current.
- Required closure: make each campaign output transactional or start by
  removing/stale-marking prior owned evidence. Add pre-launch failure, launch
  failure, changed seed set, successful replacement, and unrelated-file controls.

### SP-AUDIT-169 - Standalone gate consumers ignore the acceptance schema (medium)

- [ ] The standalone fuzz script and performance-contract loader consume nested
  acceptance values without requiring the root `schemaVersion`.
- Certifier impact: missing, malformed, or unsupported acceptance-contract
  versions can still control standalone certification.
- Required closure: share one exact acceptance-contract decoder and accept only
  integer schema 4. Add missing, string, older, newer, and canonical controls.

### SP-AUDIT-170 - Disabling verification preserves a stale success commit (high)

- [ ] Result invalidation runs only when verification is active. After one
  successful verified build, changing source and building with
  `SharpProofVerify=false` or profile `off` leaves the old request, result,
  manifest, and SARIF set in the ordinary output location.
- Supported impact: downstream tooling can mistake the surviving result commit
  for evidence about newly compiled binaries.
- Required closure: invalidate the result/SARIF commit on every real building
  transition even when verification is disabled, while preserving the current
  design-time and `BuildingProject=false` behavior. Add single/multitarget and
  verifier/profile disable controls plus a condition-removal mutation.

### SP-AUDIT-171 - Minimum-SDK package compatibility is not qualified (medium)

- [ ] Exact package bytes are consumed only by the current pinned container SDK;
  no release prerequisite pins and exercises the minimum supported SDK and
  target framework.
- Certifier impact: packages can qualify while failing restore, analyzer load,
  or compilation on the oldest advertised consumer toolchain.
- Required closure: derive the minimum SDK/TFM from the support authority and
  require an exact-artifact, no-roll-forward consumer job in release
  qualification. Reject source references, newer-SDK substitution, and a
  disconnected minimum-SDK job.

### SP-AUDIT-172 - Value-returning lowered bodies may omit the return value (high)

- [ ] Hydration validates a return operand only when one is present and never
  binds return presence/type to the callable's canonical result variable.
- Soundness impact: changing `int Id(int x) => x` from a valued return to
  `A=-1` survived canonical serialization, deserialization, and hydration,
  creating a producer-impossible successful proof body.
- Required closure: require every reachable return of a value-returning target
  to carry exactly the canonical result IR type. Add void/value, missing/wrong
  value, reachable/unreachable, multi-return, and validator-removal controls.

### SP-AUDIT-173 - Summary relations are not bound to ordered call arguments (high)

- [ ] The compiler instantiates a relational summary from the original receiver
  and ordered arguments, but hydration authenticates only identity, types,
  freshness, and relation shape.
- Soundness impact: swapping two same-typed Boolean argument term indices while
  retaining the original relation survived canonical round-trip and hydration;
  worker reasoning can therefore apply a relation to different actuals.
- Required closure: independently reconstruct or authenticate the exact
  receiver/ordered-argument instantiation before admitting a prepared summary
  call. Add permutations, duplicates, receiver, generic, same/different-type,
  and binding-removal mutation controls.

### SP-AUDIT-174 - Non-generic wrappers misalign generic ContractFor owners (medium)

- [ ] Companion compatibility intentionally ignores non-generic lexical layers,
  but type-parameter ownership recursively aligns every containing type layer.
- Supported impact: a valid companion for
  `Outer<T>.Middle.ITarget<U>` under `CompanionOuter<T>.TargetContracts<U>`
  emits `SPCF0005`, while aligned wrappers and the supported omitted-wrapper
  control are accepted.
- Required closure: align generic owner layers independently of intervening
  non-generic wrappers. Add target-only, companion-only, aligned, multiple
  wrapper, constructed binding, reordered-owner, and generated-tree controls.

### SP-AUDIT-175 - Coverage merges case-distinct TCB files on Linux (high)

- [ ] Canonical TCB paths are case-sensitive, but changed-TCB coverage identity
  uses case-insensitive comparison and max-hit merging.
- Certifier impact: two authentic Linux files whose paths differ only by case
  can collapse into one coverage record, allowing hits from one trusted file to
  certify uncovered changed lines in the other.
- Required closure: use canonical-container ordinal path identity throughout
  coverage ingestion, unions, and reporting. Add case-twin source/report,
  exact-hit, missing-file, Windows-produced-report, and comparer-mutation tests.

### SP-AUDIT-176 - Publication durability omits data and directory sync (medium)

- [ ] Atomic publication closes a temporary stream and renames it without
  synchronizing file data or the containing directory; marker creation also
  omits directory synchronization.
- Supported impact: host interruption can durably retain the final result rename
  while losing an earlier request/manifest rename, or retain an output while
  losing its ownership marker, contradicting the durable local-workspace
  contract.
- Required closure: implement `write -> data sync -> rename -> directory sync`
  for durable publication members and markers. Add an operation-recorder fault
  model that interrupts after each step and proves no durable incoherent commit.

### SP-AUDIT-177 - Pre-canceled publication locking still mutates disk (medium)

- [ ] `AcquirePublicationSet` constructs lock objects, directories, and lock
  files before its first cancellation check.
- Supported impact: an already-canceled request can leave publication metadata
  behind, and an incidental path error can replace the requested cancellation.
- Required closure: observe cancellation before any path or lock I/O while
  retaining checks during ordered acquisition. Add fresh-path, invalid-path,
  cancellation-precedence, partial-acquisition, and handle-release controls.

### SP-AUDIT-178 - Batched baselines can falsely certify mutation kills (high)

- [ ] Mutation baselines OR all project filters into one invocation, while each
  mutant is run under its focused filter. The ledger proves selected test names,
  not invocation-equivalent independent baseline success.
- Certifier impact: ordered test A can initialize state needed by B in the
  batched baseline; focused mutated B then fails before reaching the mutant and
  is accepted as an assertion-backed kill.
- Required closure: baseline each distinct project/filter with the exact focused
  invocation used by its mutants and share results only when command bytes and
  environment are identical. Add ordered-state, fixture-state, parallel, shared-
  filter, and invocation-shape mutation fixtures.

### SP-AUDIT-179 - Source-location validation permits overflowing spans (medium)

- [ ] Location validation checks nonnegative `Start` and `Length` separately but
  never requires `Start + Length` to remain representable.
- Certifier impact: a resealed manifest with `Start=int.MaxValue, Length=1`
  authenticates a source interval the Roslyn producer cannot emit.
- Required closure: use checked interval validation for callable, claim, and
  effect-witness locations. Add max/zero, max/one, ordinary boundary, overflow,
  and predicate-removal controls.

### SP-AUDIT-180 - Response validation accepts impossible elapsed times (medium)

- [ ] `ElapsedMilliseconds` is constrained only to be nonnegative and is not
  bounded by the producer's representable `TimeSpan` or supported execution
  envelope.
- Certifier impact: changing an otherwise valid request-bound response to
  `long.MaxValue` remains valid canonical evidence.
- Required closure: impose the exact producer-representable upper bound and,
  where launcher context is authoritative, the checked project-wall/grace
  envelope. Add zero, boundary, boundary+1, long-max, and overflow controls.

### SP-AUDIT-181 - Mutation-lane documentation contradicts its authority (medium)

- [ ] Container development documentation promises eight deterministic
  mutation lanes, while the acceptance contract, implementation, and
  architecture gate all own the value four.
- Supported impact: operators size hosts and interpret campaign duration using
  a nonexistent default concurrency level.
- Required closure: generate the documented lane count from the acceptance
  contract and reject documentation/catalog drift in either direction.

### SP-AUDIT-182 - `sp check` is not the documented single build (medium)

- [ ] README and container-development docs describe one incremental build, but
  the default Debug path performs another package-test build and three
  build-capable Release pack operations.
- Supported impact: the everyday check command is materially slower and does
  more compilation than its performance/iteration contract states.
- Required closure: either reuse the initial build outputs with explicit
  no-build package phases or document and snapshot the intentional command
  graph. Add an instrumented command-plan regression.

### SP-AUDIT-183 - Publication plans falsely claim symbol preflight (medium)

- [ ] Targetless plans label symbol actions `PreflightThenPush`, while the real
  publisher preflights only main-package URLs and the documented NuGet V3
  limitation makes symbol nonexistence unqueryable.
- Certifier impact: dry-run evidence promises a collision check the publisher
  cannot perform.
- Required closure: model main and symbol destination states separately and use
  an explicit unchecked/collision-on-push symbol action. Add targetless, mocked
  main preflight, zero-symbol-preflight, and legal state/action controls.

### SP-AUDIT-184 - Release bundles may contain unmanifested files (medium)

- [ ] Evidence generation and final validation enumerate packages and the one
  expected SBOM but ignore every other top-level or nested file, while CI uploads
  the whole package artifact directory.
- Certifier impact: foreign files can ship in the release artifact without a
  manifest row or `SHA256SUMS` entry.
- Required closure: require the exact regular-file set
  `{release manifest, checksums} + manifest artifacts` before upload and
  validation. Add ordinary extra, alternate SBOM, nested, directory, and exact-
  set controls.

### SP-AUDIT-185 - SBOM document name is not version bound (medium)

- [ ] Generation owns `name = SharpProof-<version>`, but supplied-SBOM and final
  bundle validators neither require nor inspect that field.
- Certifier impact: a missing or fabricated product/version name is accepted
  after updating only the SBOM hash and size evidence.
- Required closure: require an ordinal exact document name derived from the
  authoritative release version. Add absent, wrong type, empty, product drift,
  version drift, case drift, and canonical controls.

### SP-AUDIT-186 - Z3 model extraction escapes resource accounting (medium)

- [ ] SMT resource usage is sampled immediately after `solver.Check()`, before
  model creation and every `model.Evaluate` call.
- Supported impact: the pinned Z3 counter rose from 46 to 10,047 across 10,000
  model evaluations, but that cost is omitted from the producing method and can
  be charged to a later query instead.
- Required closure: account resources after all status-specific model/core
  extraction, including exceptional exits. Add zero/many-variable SAT queries,
  a following-query delayed-charge control, cancellation, malformed model, and
  accounting-removal mutation.

### SP-AUDIT-187 - Relational dependency expansion is unbounded (medium)

- [ ] `CompilerRelationalSummaryProvider` detects cycles but recursively expands
  arbitrarily deep acyclic call chains before any per-summary resource bound is
  applied.
- Supported impact: a long supported scalar-call chain can exhaust the collector
  stack; broad dependency DAGs can accumulate an unbounded provenance closure.
- Required closure: add catalog-owned dependency-depth and total unique-
  dependency/evidence limits with typed resource abstention. Test exact/+1,
  long chains, diamonds, cycles, deterministic ordering, and charge-removal
  mutation without inducing a real stack overflow.

### SP-AUDIT-188 - Outer gate failures preserve stale passing receipts (high)

- [ ] Container acceptance, PR-gate, and performance dispatchers perform restore
  or build work before the scripts that own their stable evidence files start.
- Certifier impact: a prerequisite failure can leave an earlier `passed` receipt
  intact, and CI uploads `artifacts` under `if: always()` using the current run
  and commit artifact identity.
- Required closure: invalidate or create a current incomplete receipt before any
  prerequisite runs, then atomically replace it on completion. Add preseeded
  pass plus restore/build failure, successful replacement, and unrelated-file
  controls for every stable evidence path.

### SP-AUDIT-189 - Acceptance restore predates its declared timeline (medium)

- [ ] The dispatcher completes and times restore before `Verify.ps1` captures
  `startedUtc`, but the receipt inserts restore as its first phase and includes
  it in total elapsed time.
- Certifier impact: the recorded phase interval is not contained by the
  receipt's declared start/completion interval.
- Required closure: give the dispatcher and receipt one timeline owner so start
  precedes restore. Add controlled zero/nonzero restore-duration and clock-
  boundary tests.

### SP-AUDIT-190 - Generated partial attributes suppress handwritten analysis (medium)

- [ ] Attribute-based generated-code classification scans merged method,
  associated, and containing-type symbols without requiring the attribute's
  syntax reference to belong to the tree currently being analyzed.
- Supported impact: `[GeneratedCode]` on a `.g.cs` partial type can classify its
  handwritten partial caller as generated and suppress a concrete `SP0027`
  precondition violation.
- Required closure: make attribute classification declaration/tree-owned. Add
  mixed partial type/method/property/accessor, nested partial, wholly generated,
  handwritten-call, generated-call, and provider-override controls.

### SP-AUDIT-191 - Git-quoted TCB paths bypass changed-line coverage (high)

- [ ] Changed-TCB coverage parses ordinary `git diff` text without disabling or
  decoding Git C-style path quoting. Quoted Unicode patch headers do not match
  `+++ b/`, and name-only output does not equal the canonical TCB path.
- Certifier impact: an uncovered executable change in a Unicode-named trusted
  file can be classified as metadata and skipped while project floors are met
  elsewhere.
- Required closure: use NUL-delimited exact paths and decoded ordinal hunk
  identity, or reject unsupported control-character paths end-to-end. Add
  Unicode, quoting, tab/newline, ambient `core.quotePath`, ASCII, and parser-
  removal fixtures.

### SP-AUDIT-192 - Exception text can masquerade as a mutation assertion (high)

- [ ] Mutation evidence classifies assertion-backed failures from message text;
  its exception preamble rejection recognizes only type names ending in
  `Exception` and ignores structured error/stack fields.
- Certifier impact: a custom `ProbeFailure : Exception` whose message contains
  NUnit-shaped `Assert.That`/Expected/But-was text can fail before the mutant is
  exercised and still certify the mutant as killed.
- Required closure: validate structured adapter assertion provenance or reject
  all generic exception headers across every error field. Add custom-type,
  assertion-shaped exception, real scalar/collection assertion, user-context,
  mixed-failure, and heuristic-removal fixtures.

### SP-AUDIT-193 - Release manifests coerce invalid JSON scalar types (medium)

- [ ] PowerShell comparisons/casts accept Boolean `schemaVersion: true` as
  schema 2 and accept string or fractional artifact byte counts as integers.
- Certifier impact: producer-impossible release evidence can pass final bundle,
  publisher, and retained-artifact predicates without changing package hashes.
- Required closure: parse the raw JSON token kinds and require exact integer
  schema 2 plus positive Int64 byte counts equal to physical files. Add Boolean,
  string, fraction, exponent, null, negative, overflow, and canonical controls.

### SP-AUDIT-194 - README assigns memory limits to the worker (medium)

- [ ] README says the worker has deterministic memory limits, while worker
  budgets cover query/method resources, wall time, parallelism, and expression
  depth; Docker owns the only hard memory boundary.
- Supported impact: users can expect an in-worker or native-host memory ceiling
  that does not exist.
- Required closure: derive the documented worker budget nouns from the protocol
  schema and separately identify Docker-owned CPU/memory limits. Reject drift in
  the maintained-document gate.

### SP-AUDIT-195 - Specification-pack identity limits disagree (medium)

- [ ] Catalog parsing accepts unlimited-length pack IDs and versions, then joins
  them as `id@version`; artifact hydration rejects combined identities over 128
  characters.
- Supported impact: an honestly selected valid pack can produce an artifact the
  canonical reader rejects.
- Required closure: share one identity predicate at catalog load, provenance
  construction, serialization, and hydration. Add combined length 127/128/129,
  ID/version allocation, selected/unselected, and validator-removal controls.

### SP-AUDIT-196 - Exact ref-readonly parameters are rejected (medium)

- [ ] Roslyn adds a compiler-owned required `InAttribute` modifier to an
  interface `ref readonly` parameter but not the equivalent static companion;
  matcher normalization handles only `in` parameters.
- Supported impact: source-identical target and companion signatures emit
  `SPCF0005` while `in`, `scoped ref`, and ordinary ref controls pass.
- Required closure: normalize the compiler-created modifier for
  `RefReadOnlyParameter` while preserving genuine custom-modifier differences.
  Add interface/abstract/virtual, metadata, and generated-final controls.

### SP-AUDIT-197 - Ref-readonly expression bodies appear bodyless (medium)

- [ ] Contract inventory asks Roslyn for an operation on the isolated
  `RefExpressionSyntax`; Roslyn returns none, so a valid expression-bodied
  companion is reported as bodyless with `SPCF0007`.
- Supported impact: `public static ref readonly int Read(...) => ref field;` is
  rejected while the block-bodied `return ref field` equivalent is accepted.
- Required closure: resolve the method/arrow body operation rather than the
  isolated ref expression. Add expression/block ref and ref-readonly returns,
  ordinary expression bodies, partial declarations, and generated ownership.

### SP-AUDIT-198 - Offline feed state is attributed to a real destination (medium)

- [ ] Plan-only derives remote state entirely from
  `RemotePackageDirectory` but independently records the caller-supplied real
  feed URL without identifying the fixture authority.
- Certifier impact: an empty local directory produces `Absent/Push` evidence for
  a real feed that was never queried.
- Required closure: forbid mixing a destination source with fixture simulation,
  or record a distinct fixture authority and digest that cannot be projected as
  destination state. Add empty/present fixtures and zero-network controls.

### SP-AUDIT-199 - Offline collision checks use case-sensitive filenames (medium)

- [ ] Fixture simulation probes only the exact local artifact filename, whereas
  NuGet identity and real V3 lookup normalize package ID/version casing.
- Certifier impact: the same package stored under a lowercase or otherwise
  renamed flat-container filename is missed on Linux and planned as `Push`.
- Required closure: enumerate fixture archives and compare canonical nuspec
  identity/version/role independent of filename. Add lowercase, renamed, mixed-
  case, main/symbol, duplicate, and exact-name controls.

### SP-AUDIT-200 - Effect replay is not bound to full tree identity (medium)

- [ ] Compiler effect replay binds events only to syntax-tree ordinal, text hash,
  and span, although compilation snapshots also bind path and parse settings.
- Certifier impact: an event can be resealed against a byte-identical tree with
  different preprocessor symbols or generated/path identity, including a tree
  where the operation is inactive, and still hydrate/replay.
- Required closure: bind replay origins to a canonical digest of the complete
  selected syntax-tree snapshot. Add identical-text/different-path, symbols,
  language/features, generated identity, ordinary exact, and binding-removal
  controls.

### SP-AUDIT-201 - Module references accept impossible property combinations (medium)

- [ ] Compilation fingerprint validation permits aliases and embedded-interop
  flags on `MetadataImageKind.Module`, although Roslyn forbids both for module
  references.
- Certifier impact: resealed producer-impossible module provenance remains a
  valid canonical compilation snapshot.
- Required closure: require module aliases empty and embed-interop false. Add
  each independent mutation, combined mutation, ordinary assembly, real module,
  and predicate-removal controls.

### SP-AUDIT-202 - Manifest assumptions admit producer-impossible kinds (medium)

- [ ] Shared validation accepts every defined assumption kind, while the sole
  manifest producer emits only preconditions, user assumptions, and trusted
  boundaries; hydration ignores several non-clause kinds and does not bind an
  invented trusted boundary to source inventory.
- Certifier impact: reserved `ApiSpecification`, `SourceDomain`, or
  `NormalCompletion` rows and fabricated trusted rows survive into validated
  result/summary evidence.
- Required closure: enforce the closed producer kind set and bind trusted IDs to
  the compiler/source trusted-attribute inventory. Add each reserved kind,
  fabricated/honest trusted, and kind-check mutation controls.

### SP-AUDIT-203 - Trusted effect certainty lacks trusted provenance (medium)

- [ ] Strict response validation permits `TrustedCompleteBoundary` on any proven
  effect claim without requiring `EffectContract` kind or an owner
  `TrustedBoundary` assumption.
- Certifier impact: ordinary complete effect evidence can be relabeled as a
  trusted boundary and remain request-bound valid.
- Required closure: validate certainty jointly with sealed effect-contract kind
  and exact trusted-boundary provenance. Add ordinary/trusted effects, missing or
  foreign assumption, mixed claims, and predicate-removal controls.

### SP-AUDIT-204 - Verifier libz3 pollutes application runtime assets (medium)

- [ ] The verifier package places its 31 MiB `libz3.so` under NuGet's
  conventional `runtimes/linux-x64/native` path, so consumers receive it as an
  application runtime target even with `PrivateAssets=all`.
- Supported impact: build/publish graphs can copy a dead verifier-native asset;
  the worker actually loads the canonical container's verified native root.
- Required closure: store the package payload under build-tool assets or bind
  the worker explicitly without runtime classification. Add isolated RID
  restore, `RuntimeCopyLocalItems`, build/publish output, and worker-load controls.

### SP-AUDIT-205 - Duplicate publication inputs poison corrected builds (medium)

- [ ] Invalidation does not reject `RequestPath == ManifestPath`; publication
  locking silently deduplicates it and persists a reduced-set marker before the
  launcher rejects the configuration.
- Supported impact: correcting only the manifest path then fails permanent
  partial-overlap validation, and current clean behavior cannot recover it.
- Required closure: validate distinctness of the complete publication/invocation
  set before acquiring locks or writing markers. Add every input/output duplicate,
  valid set, zero-mutation rejection, and corrected-retry controls.

### SP-AUDIT-206 - Canceled launchers leak staged worker closures (medium)

- [ ] Each launch stages up to 64 MiB under a unique
  `/tmp/SharpProof.Worker.Runtime.*`; cleanup exists only in launcher-managed
  failure/disposal paths, while MSBuild cancellation kills that launcher.
- Supported impact: ordinary canceled builds in a persistent dev container
  accumulate runtime closures until the container is recreated.
- Required closure: make staging parent-owned/invocation-scoped so BuildTasks can
  clean after termination, or implement a bounded ownership-validated recovery
  sweep. Add cancellation-after-stage, repetition, success, input failure,
  timeout, and finite-container controls.

### SP-AUDIT-207 - Module initializer effects are omitted (high)

- [ ] Pre-body effect analysis considers only the analyzed callable's containing
  type initialization and never inventories module initializers.
- Soundness impact: a selected empty method can prove `DoesNotThrow` and a
  complete empty effect contract even though a module initializer throws before
  the first callable body executes.
- Required closure: attach the current module-initialization boundary to every
  callable it may precede, excluding recursive self-entry, and disable direct
  witnesses when it can prevent the event. Add empty/effectful/throwing/diverging,
  multiple initializer, method/constructor, first/subsequent entry, and
  containing-type-only mutation controls.

### SP-AUDIT-208 - Named arguments are lowered in source order (high)

- [ ] Roslyn exposes invocation arguments in source order, but summary and API-
  specification instantiation bind the emitted array positionally in parameter
  declaration order.
- Soundness impact: `First(y: 2, x: 1)` can bind summary parameter `x` to `2`
  even though runtime passes `1`, allowing a false result postcondition to be
  proven by honestly produced compiler evidence.
- Required closure: emit arguments by `IArgumentOperation.Parameter.Ordinal` and
  reject missing, duplicate, or unsupported ordinal shapes. Add reversed same-
  typed names, mixed named/positional forms, optional parameters, declaration-
  ordered controls, summaries, API specs, and ordering-removal mutation.

### SP-AUDIT-209 - Typed-Unknown docs omit VacuousEntry certainty (medium)

- [ ] The exact effect-certainty reference omits `VacuousEntry`, although the
  schema admits it and the worker emits it for effects proven from contradictory
  entry preconditions; the documentation gate does not validate this enum.
- Supported impact: an integrator can receive a valid protocol value absent from
  the purported closed reference.
- Required closure: derive the exact certainty member table and allowed tuples
  from the protocol schema, document `VacuousEntry`, and reject missing/extra or
  stale table entries.

### SP-AUDIT-210 - Mutation ledgers compare identities case-insensitively (high)

- [ ] Baseline/mutant ledger sorting, uniqueness, and `Compare-Object` equality
  use PowerShell's default case-insensitive semantics.
- Certifier impact: a mutation can change a parameterized test case from
  `Case("A")` to `Case("a")`; a different case then fails before exercising the
  intended mutant yet is accepted as the same selected test and a valid kill.
- Required closure: use ordinal case-sensitive identity and uniqueness
  throughout ledgers. Add case-only argument/display/class identities, duplicate
  rows, identical controls, discovery drift, and comparer-removal fixtures.

### SP-AUDIT-211 - Compiler errors bypass callable-shape validation (medium)

- [ ] The producer's compiler-error branch emits only failed callables without
  lowered bodies, but envelope validation treats diagnostics and callables
  independently, and the worker returns CompilationFailure before deep decoding.
- Certifier impact: a canonical artifact can combine an honest error snapshot
  with successful or malformed lowered callables/effect evidence and remain
  accepted, despite being producer-impossible.
- Required closure: with realized errors, require the exact failed-callable
  branch and fully validate any attached evidence; with no errors, deep-decode
  normally. Add baseline/error branch swaps, malformed effect/body, zero-error,
  and branch-check mutation controls.

### SP-AUDIT-212 - Empty syntax-tree provenance accepts impossible fields (medium)

- [ ] A zero-length tree must have the known empty SHA-256 and cannot contain
  source directives, yet fingerprint validation accepts any shaped hash and an
  effective preprocessor-symbol set unrelated to raw parse symbols.
- Certifier impact: producer-impossible compilation provenance survives canonical
  serialization after resealing.
- Required closure: enforce empty hash and effective-symbol derivation for empty
  trees. Add empty/no-symbol, empty/raw-symbol, nonempty directive, generated-
  empty, wrong hash/symbol, and predicate-removal controls.

### SP-AUDIT-213 - Plan output creates the collision it certified absent (medium)

- [ ] Fixture state is inspected before unrestricted `PlanOutputPath` is written;
  that output may equal an expected main or symbol package path inside the remote
  fixture directory.
- Certifier impact: plan-only exits successfully claiming `Absent/Push`, while at
  return its own presence predicate is true and immediate replay reports a
  collision.
- Required closure: resolve output topology before state inspection, keep plan
  output outside source/fixture authority, write atomically, and revalidate
  fixture identity. Add every package role, aliases, disjoint output, and replay.

### SP-AUDIT-214 - SBOM checksum arrays accept scalar objects (medium)

- [ ] SBOM validators pipeline `checksums` without requiring an array token, so
  one scalar checksum object satisfies the exactly-one predicate.
- Certifier impact: structurally invalid SPDX JSON is accepted by evidence
  generation and final bundle validation.
- Required closure: require an exact JSON array containing exactly one canonical
  object. Add null, object, string, nested array, mixed types, empty/multiple,
  and canonical controls.

### SP-AUDIT-215 - Fixed SBOM vocabulary is case-folded (medium)

- [ ] PowerShell comparisons accept case drift in producer-owned exact values
  such as `SPDX-2.3` and `CC0-1.0`.
- Certifier impact: noncanonical document vocabulary survives supplied-evidence
  and final-artifact validation after rebinding its hash.
- Required closure: require ordinal exact strings and JSON types for fixed SPDX
  vocabulary. Add case, whitespace, alias, type, unsupported, and canonical
  controls.

### SP-AUDIT-216 - Parentheses disable direct precondition replay (medium)

- [ ] Replayable-prefix detection compares raw expression and invocation spans;
  transparent `ParenthesizedExpressionSyntax` widens the owned expression span.
- Supported impact: `(Positive(-1))` in an expression body, return, local
  initializer, or assignment becomes Unknown without `SP0027`, while the
  unparenthesized equivalent reports the violation.
- Required closure: strip only transparent parentheses before top-level call
  comparison. Add all four documented forms, nested parentheses, plain controls,
  nontransparent conversion/wrapper rejects, and per-shape mutation controls.

### SP-AUDIT-217 - Proven preconditions remain marked unused (medium)

- [ ] The proof-core map and `Used` update cover only `UserAssume`, not Requires
  preconditions, even though proof labels include both.
- Evidence impact: a canonical non-vacuous proof can list `requires:0` in its core
  while the exact precondition row says `Used=false` and summary Used remains 0.
- Required closure: map every manifest-backed Requires/Assume label to its exact
  assumption ID and mark use independent of kind. Add used/unused multiple
  preconditions, user-assume parity, vacuous/refuted/unknown, cache, strict
  validation, and kind-gate mutation controls.

### SP-AUDIT-218 - Callable coverage need not match owned claims (medium)

- [ ] Validation enforces Unknown claim implies incomplete callable, but not the
  reverse; an all-decided callable may be relabeled
  `Incomplete/SemanticUnknown` and remain request-bound valid.
- Certifier impact: false incomplete coverage can make require-proven consumers
  fail even though every owned claim is decided.
- Required closure: derive coverage/reason from the exact owned result set. Add
  all-proven, mixed proven/refuted, unknown, zero-claim, failed-preparation, and
  projection-removal controls.

### SP-AUDIT-219 - Claim reasons are not bound to claim kind (medium)

- [ ] Outcome validation accepts the union of postcondition-only and effect-only
  Unknown reasons for every claim and checks claim kind only for effect certainty
  and witness shape.
- Certifier impact: a postcondition can be relabeled
  `EffectContractNotEstablished`, or an effect as `DeepPostcondition`, and pass
  strict validation after count recomputation.
- Required closure: generate and enforce exact
  `(ClaimKind, Outcome, Reason, Certainty)` tuples. Add every cross-kind swap,
  honest per-kind cases, mixed callables, and tuple-table mutation controls.

### SP-AUDIT-220 - Manifest spans are not bound to sealed source length (medium)

- [ ] Callable/claim locations are checked for nonnegative and nonoverflowing
  shape but never related to any sealed syntax tree's `TextLength`.
- Certifier impact: a resealed location beyond every captured source can hydrate
  and enter validated responses despite being producer-impossible.
- Required closure: persist exact physical tree identity separately from mapped
  display paths and require checked end within that tree. Add callable/claim,
  exact-end, beyond-end, mapped `#line`, generated, and binding-removal controls.

### SP-AUDIT-221 - Function-pointer convention order is overconstrained (medium)

- [ ] ContractFor compares unmanaged calling-convention types positionally, but
  the compiler permits order-interchangeable convention sets such as
  `[Cdecl, SuppressGCTransition]` and the reverse.
- Supported impact: compiler-equivalent target and companion signatures emit
  `SPCF0005`.
- Required closure: compare convention types as an exact unordered set while
  retaining identity/cardinality checks. Add reordered return/parameter,
  genuinely different, duplicate-invalid, metadata, and generator-final controls.

### SP-AUDIT-222 - Reference-null spec terms lose concrete type (medium)

- [ ] API-spec validation/instantiation admits coarse Reference variables, but
  materializes every reference null as `object` rather than the concrete
  substituted reference type.
- Supported impact: a valid `Widget` result specification `result != null`
  becomes a `Widget` versus `object` IR comparison, fails instantiation, and
  causes the worker to abandon an otherwise supported spec application.
- Required closure: derive null's concrete type from the paired operand/
  substitution or preserve concrete declarative type. Add object, string, user-
  defined reference, result/parameter, nullable comparison, and unsupported
  sequence-null controls.

### SP-AUDIT-223 - Changed-line coverage fallback checks only the tip commit (high)

- [ ] When CI or a local run lacks a usable comparison reference, changed-TCB
  coverage falls back to `HEAD^`, so it inventories only the final commit.
- Certifier impact: a multi-commit branch can change trusted production code in
  an earlier commit, leave the tip unrelated, and pass the changed-line policy
  without covering the trusted change.
- Required closure: require a durable merge-base/baseline authority for every
  qualification mode and fail closed when it is unavailable. Add two- and
  three-commit branches, workflow dispatch, shallow checkout, explicit base,
  merge commit, and fallback-removal controls.

### SP-AUDIT-224 - Release evidence output is not self-contained (medium)

- [ ] The evidence generator permits a separate output directory and external
  SBOM, but writes only the manifest/checksums there and records package/SBOM
  leaf names without staging those bytes.
- Supported impact: generation exits successfully while its own artifact
  validator and publisher immediately fail because the six packages or supplied
  SBOM are absent; tests mask this by copying packages separately.
- Required closure: atomically stage and self-validate every recorded byte, or
  require exact co-location and the canonical SBOM leaf. Add separate-output,
  external/renamed SBOM, collision, co-located, and generator-to-validator tests.

### SP-AUDIT-225 - Typed-Unknown docs misstate Unavailable certainty (medium)

- [ ] The public certainty reference says `Unavailable` is limited to
  infrastructure or invalid-contract failures, but the schema and worker use it
  as the fallback certainty for many Unknown causes, including resource limits,
  timeout, cancellation, unsupported callables, and unknown entry feasibility.
- Supported impact: integrators can reject or misclassify valid protocol-11
  results by following the purported closed semantic reference.
- Required closure: derive the documented outcome/reason/certainty patterns from
  the protocol schema and describe `Unavailable` as the fallback Unknown
  certainty whose reason carries the cause. Add exact table-parity gating.

### SP-AUDIT-226 - Compiler diagnostic namespaces are not validated (medium)

- [ ] The sole producer emits `compiler.<diagnostic-id>` and the public contract
  reserves that namespace, but artifact validation accepts any nonblank code.
- Certifier impact: a resealed compiler diagnostic can masquerade as
  `worker.infrastructure`, an analyzer diagnostic, or a bare `compiler.` code
  and is copied verbatim into the worker response.
- Required closure: require the exact ordinal `compiler.` prefix plus a canonical
  nonblank diagnostic ID. Add foreign namespace, bare prefix, case/whitespace,
  honest compiler ID, and prefix-check mutation controls.

### SP-AUDIT-227 - Ill-formed UTF-16 constants collapse in Z3 (high)

- [ ] The IR admits .NET strings containing unpaired surrogates and sends them
  through `MkString`; the pinned Z3 binding maps a lone high surrogate to the
  same solver string as U+FFFD although the interpreter distinguishes them.
- Soundness impact: an end-to-end conditional equality returned Proven for all
  branches even though the runtime/interpreter false branch compares unequal
  strings.
- Required closure: reject ill-formed UTF-16 before proof, or use an injective
  UTF-16 code-unit encoding shared by the interpreter and SMT backend. Add lone
  high/low, replacement, valid pair, mixed text, conditional, model, and
  encoding-removal mutation controls.

### SP-AUDIT-228 - Specification equality loses concrete operand types (medium)

- [ ] API-spec equality validation accepts operands by coarse `Reference` or
  `Sequence` kind, while instantiated IR equality requires exact `IrTypeId`.
- Supported impact: a catalog-valid equality over distinct concrete reference
  types or sequence element types survives selection, then fails during worker
  instantiation and abandons an otherwise supported specification.
- Required closure: preserve and unify exact substituted operand types before
  accepting equality. Add base/derived, unrelated references, exact/different
  sequence elements, generic substitutions, null, and honest equality controls.

### SP-AUDIT-229 - SBOM row collections accept nested singleton arrays (medium)

- [ ] PowerShell member projection flattens or member-enumerates nested singleton
  arrays for SPDX `packages` and `relationships`, so structurally invalid nested
  collections satisfy the row validators.
- Certifier impact: producer-impossible SPDX shapes can pass supplied-evidence
  and immutable-bundle validation after their hashes are rebound.
- Required closure: validate raw JSON token kinds and require one flat array of
  objects for each collection. Add object, nested singleton/multiple, mixed,
  empty, null, and canonical controls.

### SP-AUDIT-230 - Publication planning does not validate SBOM semantics (high)

- [ ] The canonical publication-plan path binds recorded file hashes but does
  not run the strict SBOM or checksum semantics validator.
- Certifier impact: replacing the SBOM with non-JSON bytes and rebinding the
  release manifest/checksum lets plan-only succeed even though the same bundle
  is rejected by the immutable-artifact validator.
- Required closure: make publication planning consume the exact strict release-
  artifact validation result before projecting actions. Add malformed/rebound
  SBOM, inconsistent checksum, valid bundle, and validation-removal fixtures.

### SP-AUDIT-231 - ContractFor conflates positive and negative zero defaults (medium)

- [ ] Optional floating-point defaults are compared through boxed equality, so
  `+0.0` and `-0.0` are treated as identical despite distinct compiler metadata
  bits and observable reciprocal behavior.
- Supported impact: a target and companion with different optional defaults can
  be accepted as an exact contract surface without `SPCF0005`.
- Required closure: compare canonical IEEE bit patterns for floating defaults.
  Add float/double positive/negative zero, NaN payload policy, infinities,
  ordinary constants, metadata/source, and comparator-removal controls.

### SP-AUDIT-232 - Implicit base initializers skip precondition replay (medium)

- [ ] Constructor call-site replay adds base/this calls only when an explicit
  `ConstructorInitializerSyntax` exists, although C# inserts `base()` for an
  ordinary constructor with no initializer.
- Supported impact: a derived constructor can silently violate a parameterless
  base constructor's compiler-bound precondition while the explicit `: base()`
  equivalent reports `SP0027`.
- Required closure: analyze the compiler-selected implicit base call exactly
  once. Add implicit/explicit base, this chaining, object base, generated,
  inaccessible/unsupported, and discovery-removal controls.

### SP-AUDIT-233 - Rejected metadata preconditions evade SP0047 (medium)

- [ ] A readable but wrong-payload contract assembly can attach a closed
  precondition attribute to an external target; advisory activation starts, but
  call-site binding returns NotApplicable instead of accountable rejection.
- Supported impact: a call using rejected contract metadata emits neither the
  expected `SP0047` nor a semantic precondition diagnostic.
- Required closure: propagate rejected contract-API identity through metadata
  precondition binding and diagnose every attempted use. Add wrong payload,
  unreadable payload, trusted metadata, source target, mixed attributes, and
  no-contract controls.

### SP-AUDIT-234 - Trusted effect proofs mark their boundary unused (medium)

- [ ] A `TrustedCompleteBoundary` effect proof copies the exact
  `TrustedBoundary` assumption with `Used=false`, and the response summary also
  reports zero used assumptions.
- Evidence impact: canonical evidence says no trust was used even though the
  effect claim is proven solely from that boundary.
- Required closure: bind trusted effect evidence to exact trusted assumption IDs
  and mark only those IDs used on the corresponding proven result. Add ordinary
  complete, multiple trust scopes, unknown/refuted, missing/foreign provenance,
  summary, and use-update mutation controls.

### SP-AUDIT-235 - Semantic claim IDs can be renamed self-consistently (medium)

- [ ] Compiler-owned `spc1:` claim identities are accepted by cross-reference
  equality only; the manifest, callable list, and lowered clause can all be
  renamed together to an arbitrary nonblank value and resealed.
- Certifier impact: identical sealed compiler semantics can carry multiple
  supposedly stable semantic identities, breaking canonical provenance and
  downstream evidence correlation.
- Required closure: enforce exact `spc1:<64 lowercase hex>` shape and persist the
  producer fingerprint inputs needed to recompute the exact identity. Add linked
  all-side rename, prefix/length/case/digest, direct/companion/effect, and honest
  controls.

### SP-AUDIT-236 - Manifest display coordinates are not source-bound (medium)

- [ ] Manifest locations bind a physical span only by loose shape; their display
  line and column can be changed independently of the sealed source and mapped
  line directives.
- Certifier impact: canonical evidence and SARIF navigation can assert a false
  source coordinate while retaining otherwise valid callable/claim identity.
- Required closure: bind locations to exact physical tree identity and a
  compiler-produced line-map digest, then validate physical span and mapped
  display start together. Add line-only/column-only, ordinary, `#line`, generated,
  callable/claim/effect witness, and binding-removal controls.

## Current comprehensive audit evidence

- [x] Pass A reviewed analyzer/generator/collector ownership, ContractFor
  exactness, effect-operation support, schema-12 reference provenance, and
  worker proof/result classification. Two executable soundness failures were
  admitted as SP-AUDIT-001 and SP-AUDIT-002. The temporary failing probes were
  removed after their results were recorded.
- [x] Pass B reviewed Linux path/publication containment, worker cancellation,
  exact Z3 resolution, package discovery, package consumers, pilot evidence,
  SBOM/release evidence, and tag qualification. Two certifier defects were
  admitted as SP-AUDIT-003 and SP-AUDIT-004; no supported Linux host/runtime
  failure was reproduced.
- [x] Follow-up Pass C reviewed adjacent analyzer/dataflow/compiler authority
  without reopening the first two effect defects. It admitted the executable
  initializer false negative as SP-AUDIT-005; compiler-reference ordering,
  size, stale-metadata, and canonical-shape paths had no new discriminating
  failure.
- [x] Follow-up Pass D reviewed Linux metadata paths, process containment,
  exact Z3 loading, protocol validation, container commands, and independent
  package/SBOM/release certifiers. It admitted SP-AUDIT-006 and SP-AUDIT-007.
- [x] Follow-up Pass E reviewed worker proof/result validation, compiler
  schema-12 authority, analyzer call-site ownership, and adjacent supported
  callable forms. It admitted the primary-constructor false negative as
  SP-AUDIT-008. A synthesized top-level-entry call-site probe was rejected
  because that callable is not included in the documented portable call-site
  traversal surface.
- [x] Follow-up Pass F reviewed Linux publication metadata, exact Z3/process
  containment, package consumers, container execution, and qualification
  scripts without reopening the existing release findings. It admitted the
  case-sensitive evidence-path containment defect as SP-AUDIT-009, and a
  second executable probe proved SP-AUDIT-006 also accepts an exact-content
  ownership-marker symlink.
- [x] Follow-up Pass G reviewed semantic authority and the documented targeted
  container test surface. It admitted SP-AUDIT-010 after a filtered worker
  proof-kernel matrix exposed order-dependent native Z3 setup; the Effects,
  analyzer/final-compilation, summary, proof-kernel, and SMT matrices otherwise
  passed 178 cases.
- [x] Clean Pass H reviewed the remaining semantic/compiler authority after
  SP-AUDIT-010 was admitted: ContractFor final-compilation validation,
  constructed and exact-symbol companion binding, contract inventory,
  compiler-artifact canonical hydration, acyclic execution, protocol result
  assembly, and cache replay. The dedicated ContractFor and Contracts matrices
  passed 142 cases; the compiler/worker cases were included in the 100-case
  clean host/protocol matrix below. No additional finding was admitted.
- [x] Clean Pass I reviewed the Linux host and certifier surface after the last
  finding: path and publication identity, child startup/cancellation, container
  contract and exact Z3 checks, protocol/cache failure handling, BuildTasks,
  package layout and entrypoint discovery, a real packaged proof, and offline
  publication planning. The canonical container contract passed, as did 100
  worker/host/protocol cases, 64 launcher/build-task cases, three exact-package
  cases, and five publication-plan cases. No additional finding was admitted.
- [x] Follow-up Pass J reviewed newly adjacent exact-effect operation shapes
  and admitted SP-AUDIT-012 after a canonical-container regression proved that
  `foreach` silently omits compiler-selected enumerator writes. The temporary
  source test was removed after its result was recorded.
- [x] Follow-up Pass K reviewed container source materialization and cached
  release evidence. It admitted SP-AUDIT-011 after a real package contained an
  uncommitted marker while asserting clean HEAD, and SP-AUDIT-013 after an
  empty forged mutation summary satisfied the canonical mutation command.
  Temporary source and evidence probes were removed or restored.
- [x] Follow-up Pass M reviewed the remaining release bundle structure and
  admitted SP-AUDIT-014 after both evidence generation and final validation
  accepted a symbol package containing no PDB. The disposable package
  directory was deleted after the result was recorded.
- [x] Clean Pass L reviewed remaining compiler-inserted and computed-value
  effect paths. An implicit base-constructor control passed. A compound
  property alias probe exposed a raw-summary imprecision, but its only
  discriminating form required a user-defined operator that the documented
  analyzer subset rejects before proof, so it was rejected rather than
  promoted. All temporary semantic tests were removed.
- [x] Follow-up Pass O reviewed package-to-SBOM provenance relationships and
  admitted SP-AUDIT-015 after a foreign SharpProof-prefixed DLL escaped the
  third-party inventory and both release validators. The disposable package
  directory was deleted after the result was recorded.
- [x] Follow-up Pass N reviewed admitted argument and callable-boundary
  propagation and admitted SP-AUDIT-016 after definite getter and setter
  precondition violations produced no SP0027. The temporary analyzer test was
  removed after its result was recorded.
- [x] Follow-up Pass P checked the adjacent custom-event accessor surface and
  broadened SP-AUDIT-016 after definite add/remove precondition violations also
  produced no SP0027. The temporary event test was removed.
- [x] Follow-up Pass Q corrupted the catalog-owned packaged Z3 payload and
  broadened SP-AUDIT-015 after both release validators accepted it. The exact
  package-layout test rejected the corruption, documenting the current
  workflow mitigation and the non-self-contained release boundary. The
  disposable package directory was deleted.
- [x] Follow-up Pass Q also exercised publication identity framing and admitted
  SP-AUDIT-017 after two distinct newline-containing path sets with a shared
  output were accepted as one ownership set. The temporary worker test was
  removed.
- [x] Follow-up Pass R reviewed remaining conversion exception
  classification and admitted SP-AUDIT-018 after definitely null nullable
  unboxing produced two impossible exceptions in a complete effect projection.
  The temporary Effects test was removed.
- [x] Clean Pass S reviewed cache ownership, eviction, replay validation,
  protocol canonicalization, malformed-result handling, and publication-marker
  canonicality after the framing defect was separated into SP-AUDIT-017.
  Existing tests cover symlinked cache entries, locks and directories, unowned
  suffix matches, byte-bound rollback, stale manifests, scalar-model replay,
  and oversized JSON. No additional executable supported-surface failure was
  admitted.
- [x] Follow-up Pass T reviewed exception-handler reachability, cast exception
  classification, and array-store compatibility. It admitted SP-AUDIT-019 and
  SP-AUDIT-020 from focused canonical-container failures and broadened
  SP-AUDIT-018 with a second definite reference-cast reproduction. All three
  temporary Effects probes were removed.
- [x] Clean Pass U rechecked schema-12 compiler-reference capture and package
  authority adjacent to the newly admitted semantic defects. Reference module
  count/byte budgets, exact backing-metadata comparison, deterministic linked
  module ordering, stale-module rejection, package layout, native Z3 hash, and
  repository metadata have existing direct checks. No new independent
  executable certifier failure was admitted; the remaining source/package and
  release-boundary weaknesses are already separated as SP-AUDIT-003,
  SP-AUDIT-004, SP-AUDIT-007, SP-AUDIT-011, and SP-AUDIT-013 through 015.
- [x] Follow-up Pass V compared the documented expression matrix with the
  generated operation authority and admitted SP-AUDIT-021 after a constant
  ordinary interpolation was rejected before exact effect analysis. The
  temporary analyzer probe was removed.
- [x] Clean Pass W reviewed launcher exit/result reconciliation, worker
  timeout/cancellation cleanup, protocol run/failure consistency, package
  consumer provenance, and tag-artifact acquisition. No independent
  executable failure was admitted: exact workflow artifacts are commit-named,
  while the remaining local package/pilot/release binding weaknesses are
  already captured by SP-AUDIT-003, SP-AUDIT-004, SP-AUDIT-011, and
  SP-AUDIT-013 through 015.
- [x] Clean Pass X inventoried every default API specification and the enabled
  relational pack, then cross-checked their proof-bearing effects, exception,
  nullness, cardinality, and scalar-result facts against the pinned runtime
  semantics. The catalog is deliberately small and conservative; no new
  executable model mismatch was admitted.
- [x] Follow-up Pass Y reviewed compilation-global activation across analyzer,
  ContractFor generator, and compiler collector. It admitted SP-AUDIT-022 from
  two focused custom-host failures covering conflicting aliases and an invalid
  profile value. Both temporary generator probes were removed.
- [x] Follow-up Pass Z reviewed conversion allocation semantics, exceptional
  call sequencing, source type initialization, summary/SMT normal-completion
  handling, and cancellation-boundary certification. It admitted
  SP-AUDIT-023 through SP-AUDIT-025 from three focused failures. A source static
  initializer candidate was rejected because the effect analysis already
  surfaced the boundary as incomplete instead of proving through it. All
  temporary tests were removed.
- [x] Follow-up Pass AA reviewed container publication/process containment,
  pilot isolation, package/evidence binding, and tag qualification. It admitted
  SP-AUDIT-026 after the real five-pilot certifier accepted copied stale outputs
  with a no-op `dotnet`. The disposable clone and fake executable were removed;
  no host artifacts were changed.
- [x] Follow-up Pass AB tested left-to-right call completion and broadened
  SP-AUDIT-024 after a definitely throwing first argument failed to suppress a
  later argument's and the callee's static writes. The temporary Effects probe
  was removed.
- [x] Clean Pass AC reviewed request hashing, response/manifest canonical
  validation, cache-key policy inputs, cache reconstruction, counterexample
  replay, malformed result classification, and cancellation/timeout binding.
  The cache only retains replay-validated refuted postconditions and rebuilds
  request-specific response metadata; no independent executable protocol or
  cache failure was admitted.
- [x] Follow-up Pass AD reviewed release/container command preconditions and
  admitted SP-AUDIT-027 after the canonical `release-tag` certifier accepted a
  branch ref as a valid release identity. The probe used only environment
  overrides and created no repository evidence or tracked changes.
- [x] Follow-up Pass AE reviewed analyzer diagnostic ownership at executable
  root boundaries. A focused console-compilation probe confirmed that top-level
  statements receive no SP0027, but the documented portable call-site surface
  names only methods, local functions, lambdas, and anonymous methods; the
  probe was rejected as unsupported and removed. SP-AUDIT-005 remains limited
  to documented initializer execution.
- [x] Follow-up Pass AF reviewed release-evidence self-authentication and
  admitted SP-AUDIT-028 after the canonical qualification command emitted
  passed evidence for a text file, nonexistent commit, and unrelated version.
  The disposable clone and its private Compose volumes were removed.
- [x] Follow-up Pass AG reviewed nullable conversion semantics and broadened
  SP-AUDIT-018 after a focused Effects regression proved that definitely
  present nullable unwrapping reports an impossible exception in a complete
  projection. The temporary Effects test was removed.
- [x] Clean Pass AH reviewed proof-kernel result shapes, unsat-core bounds,
  counterexample replay, model-variable ownership, undefined goals, typed
  backend failures, SMT integer bounds/division, cancellation, and native
  result disposal. The canonical Verify and SMT suites passed 33 cases; no new
  proof-acceptance or fail-closed result defect was admitted.
- [x] Follow-up Pass AI reviewed Linux publication lock acquisition and failure
  cleanup and admitted SP-AUDIT-029 after a focused regression measured one
  leaked descriptor per failed multi-lock construction. The temporary worker
  test was removed.
- [x] Clean Pass AJ reviewed ContractFor type/member exactness and direct
  attribute-scope ownership after the profile-resolution defect was separated
  into SP-AUDIT-022. Every consumed effect/control attribute explicitly opts
  out of CLR attribute inheritance; the validator and runtime binder agree on
  receiver placement, ordinary-member identity, nested/open generic
  specialization, constraints, nullability, defaults, ref/scoped kinds,
  custom modifiers, function pointers, and generated/final-compilation
  companions. The canonical ContractFor generator and Contracts suites passed
  151 cases; no additional executable supported-surface failure was admitted.
- [x] Clean Pass AK reviewed cache ownership, byte-bound rollback, lock and
  reparse rejection, replay validation, stale-schema handling, and adjacent
  package-closure authority without reopening SP-AUDIT-003, SP-AUDIT-007,
  SP-AUDIT-014, SP-AUDIT-015, or SP-AUDIT-028. The canonical cache-focused
  matrix passed 29 cases. A combined package layout/symbol/evidence probe
  exceeded its 120-second audit bound and was stopped cleanly, so it was not
  counted as new closure evidence; static review found no independent defect
  outside the existing package and release rows.
- [x] Follow-up Pass AL compared every effect-discovery operation catalog row
  with CFG reachability and scanner dispatch. It admitted SP-AUDIT-030 after a
  three-shape control proved only definitely-null conditional access retained
  the skipped callee write; constant conditional and definitely-nonnull
  coalesce controls were clean. The temporary Effects test was removed.
- [x] Follow-up Pass AM reviewed compiler-to-worker callable ownership for
  declared, synthesized, nested, generated, property/event, top-level, partial,
  and primary-constructor forms. It admitted SP-AUDIT-031 and SP-AUDIT-032
  after Roslyn symbol assertions proved the selected accessor/constructor
  contracts existed while manifest discovery returned zero. Both temporary
  worker tests were removed.
- [x] Follow-up Pass AN reviewed implicit normal-completion sequencing and
  lifted integral hazards. It broadened SP-AUDIT-024 after null lock entry and
  a definitely throwing constructor both failed to suppress later writes, and
  admitted SP-AUDIT-033 after null-left and null-right lifted division both
  retained impossible exceptions. The temporary Effects probes were removed.
- [x] Follow-up Pass AO reviewed hash-consistent compiler-manifest canonical
  shapes and admitted SP-AUDIT-034 after uppercase compiler and reference MVIDs
  survived direct serialization, fingerprint recomputation, and canonical
  deserialization. The temporary worker test was removed.
- [x] Follow-up Pass AP tested definitely-null lifted arithmetic across checked
  addition, checked conversion, increment, and compound division. It broadened
  SP-AUDIT-033 after addition, increment, and compound division retained
  impossible hazards; the nullable conversion control was clean. The temporary
  Effects test was removed.
- [x] Clean Pass AQ reviewed relational-summary signature/environment
  validation, substitution, fresh-variable ownership, normal-completion
  definedness, compiler lowering, and worker assumption encoding. A disposable
  divide-by-zero instantiation faulted rather than admitting a relation, a
  valid instantiation evaluated exactly, and independent instances had
  disjoint fresh variables. The temporary Summaries test was removed; no new
  proof-authority defect was admitted.
- [x] Follow-up Pass AR inventoried exact effect-region ownership for value
  copies, ref-like and unsupported operation shapes, then tested termination
  feasibility. It admitted SP-AUDIT-035 after a compile-time-false while loop
  was classified as diverging. The temporary Effects test was removed.
- [x] Follow-up Pass AS audited package/SBOM hash, identity, relationship, and
  license binding. It admitted SP-AUDIT-036 after license-corrupted current-HEAD
  evidence passed both canonical release certifiers. The disposable package
  copy and probe script were removed.
- [x] Follow-up Pass AT reviewed generated-source ownership after the prior
  string-literal correction. It admitted SP-AUDIT-037 after an explanatory
  leading comment suppressed a real SP0027 violation. The temporary analyzer
  test was removed.
- [x] Follow-up Pass AU revisited compiler-identity producer canonicality. It
  broadened SP-AUDIT-034 after arbitrary nonempty compiler-version text
  survived fingerprint recomputation and canonical deserialization. The
  temporary worker test was removed.
- [x] Follow-up Pass AV independently audited final third-party inventory
  authority. It admitted SP-AUDIT-038 after fabricated component identity and
  version data, made self-consistent only inside the evidence pair, passed the
  canonical final release validator. The disposable bundle and probe script
  were removed.
- [x] Follow-up Pass AW audited worker-response/result binding and admitted
  SP-AUDIT-039 after a fully fabricated worker/spec version summary passed
  request-bound validation. The temporary protocol test was removed.
- [x] Clean Pass AX reviewed Linux path canonicalization, file identity,
  publication lock ordering and partial acquisition, persistent marker
  ownership, atomic output replacement, parent-death and signal cancellation,
  and exact Z3 contract loading. No new executable trusted-container failure
  was found beyond SP-AUDIT-006, SP-AUDIT-017, and SP-AUDIT-029.
- [x] Clean Pass AY systematically compared every declared exact
  effect-discovery operation with scanner dispatch, CFG reachability, and
  implicit runtime semantics. No new independent omission was found beyond
  SP-AUDIT-001, SP-AUDIT-002, and SP-AUDIT-012; the remaining catalog entries
  are structural/compile-time nodes or reach dedicated field, property, array,
  call, allocation, operator, lock, loop, conversion, and throw handlers.
- [x] Follow-up Pass AZ audited compiler-lowered graph ownership and admitted
  SP-AUDIT-040 after both a same-typed parameter-target swap and a same-typed
  pre-state/current-state swap survived callable decoding. The existing
  mixed-type corruption control rejects only because the IR types differ. The
  temporary worker tests were removed.
- [x] Clean Pass BA reviewed callable discovery and subset ownership across
  ordinary/explicit-interface methods, constructors, property/indexer/event
  accessors, operators/conversions, destructors, local functions, lambdas,
  anonymous methods, top-level code, generated trees, and companion targets.
  The compiler inventory discovers the additional callable forms and the
  documented subset deliberately types unsupported kinds; no independent
  omission beyond SP-AUDIT-005, SP-AUDIT-008, SP-AUDIT-016, SP-AUDIT-031, and
  SP-AUDIT-032 was admitted.
- [x] Follow-up Pass BB audited result claim/assumption ownership, vacuity,
  proof-core shape, summary counts, and request binding. It admitted
  SP-AUDIT-041 after fabricated proof, counterexample-model, and mismatched
  effect-witness payloads survived the strict request-bound validator. The
  temporary protocol tests were removed.
- [x] Clean Pass BC reviewed cache path prevalidation, owned-entry naming,
  lock lifetime, byte-bound rollback, eviction continuation, payload hashing,
  manifest binding, counterexample model decoding, assumption replay, and
  cache-hit response reconstruction. No independent defect was found; unlike
  SP-AUDIT-006, the cache validates its derived lock path before opening it,
  and refuted claims are replayed against decoded compiler evidence.
- [x] Clean Pass BD reviewed call-site receiver/argument mapping for direct and
  reduced extension calls, named and omitted optional arguments, ref/in alias
  treatment, constructors, property/accessor calls, and unsupported param-array
  expansion. The mapping uses Roslyn parameter ordinals and shifts only the
  reduced receiver; no new executable failure was admitted beyond the existing
  call-site ownership rows.
- [x] Clean Pass BE reviewed closed-control attribute ownership and allowed
  exception inheritance across methods, accessors, local functions, lambdas,
  containing types, and assemblies. Scope validation, deduplication, malformed
  reason handling, and derived-exception matching remain fail-closed; no new
  supported-surface defect was admitted.
- [x] Follow-up Pass BF audited partial-method selection, effect-contract
  inheritance, normalized symbol identity, and executable-body ownership. It
  admitted SP-AUDIT-042 after one definition-level purity contract and one
  implementation body produced duplicate SP0002 diagnostics. The temporary
  analyzer test was removed.
- [x] Follow-up Pass BG audited worker run-status, failure-reason, callable
  coverage, claim outcome, summary, and strict request-binding consistency. It
  admitted SP-AUDIT-043 after all-proven responses remained valid when labeled
  `TimedOut` or `Canceled`. The temporary protocol test was removed.
- [x] Clean Pass BH reviewed Linux canonicalization and `lstat` ancestry,
  regular-file deletion and hardlink protection, mount-type rejection,
  publication metadata namespaces, ordered `flock` acquisition, marker
  rollback, worker parent-death setup, termination deadlines, and exact Z3
  hash/load ownership. No independent trusted-container defect was admitted
  beyond SP-AUDIT-006, SP-AUDIT-017, and SP-AUDIT-029; hostile concurrent path
  replacement remains an explicit preview boundary.
- [x] Follow-up Pass BI audited release artifact count, identity, filename,
  role, checksum, SBOM, and final-validation binding. It admitted SP-AUDIT-044
  after all three main/symbol roles could be inverted while retaining accepted
  self-consistent evidence. The disposable bundle and probe script were
  removed.
- [x] Rejected Pass BJ tested whether two same-identity summarized calls could
  exchange their relation/result closures while remaining inside the supported
  compiler-lowered surface. Both an order-sensitive `int` fixture and a checked
  `long` fixture abstained before producing two summary descriptors, so the
  hypothesized corruption path was not executable and no row was added. The
  temporary worker test was removed.
- [x] Follow-up Pass BK audited NuGet archive entry identity, first-party
  payload ownership, evidence regeneration, and final release validation. It
  broadened SP-AUDIT-015 after an exact duplicate first-party assembly path
  carrying different bytes was accepted and re-certified. The disposable
  package copy and probe script were removed.
- [x] Clean Pass BL cross-checked canonical container commands, CI workflow
  sequencing, exact-SHA package handoff, release planning, qualification,
  attestation, and publication ownership. It admitted no independent defect
  beyond SP-AUDIT-003, SP-AUDIT-004, SP-AUDIT-013, SP-AUDIT-027,
  SP-AUDIT-028, and SP-AUDIT-044. The review also corrected SP-AUDIT-044 to
  record that publication planning independently rejects role-swapped file
  extensions, leaving the standalone final validator defective but mitigated.
- [x] Follow-up Pass BM audited Portable IR slot canonicality, equality
  partitions, table construction, hydration, and round-trip validation. It
  admitted SP-AUDIT-045 after an unused operation row survived canonical
  serialization/deserialization and callable decoding. The temporary worker
  test was removed.
- [x] Clean Pass BN audited generated-code authority across compiler provider
  results, attributes on methods and associated members, leading trivia,
  filename fallbacks, and empty/reference-free trees. An explicit
  `GeneratedKind.NotGenerated` result intentionally overrides fallbacks and is
  already regression-tested; no independent defect was admitted beyond
  SP-AUDIT-037.
- [x] Clean Pass BO tested CFG feasibility for a compile-time constant
  conditional whose dead branch throws, and POSIX normalization for equivalent
  single- and double-leading-slash local paths. The dead effect was excluded
  and both path spellings resolved to the same canonical path, so neither
  candidate produced a supported-surface failure. Both temporary tests were
  removed.
- [x] Clean Pass BP tested a compile-time-selected switch with a throwing dead
  arm. Managed flow excluded the impossible arm while retaining a complete
  summary, so the constant-switch candidate did not reproduce. The temporary
  Effects test was removed.
- [x] Clean Pass BQ reviewed the generic forward solver's round-based joins,
  cyclic-block widening, convergence limit, graph validation, and worklist
  permutation checks together with proof-kernel backend-status, unsat-core,
  exact-model, and counterexample-replay validation. The remaining permissive
  shapes either canonicalize harmless backend redundancy or fail closed; no
  independent proof-soundness defect was admitted.
- [x] Clean Pass BR reviewed worker runtime-closure staging, cache-key inputs,
  cache entry ownership and refutation replay, direct-child startup,
  parent-death installation, timeout cleanup, and native Z3 ownership. No new
  trusted-container failure was found beyond SP-AUDIT-006, SP-AUDIT-010,
  SP-AUDIT-029, and SP-AUDIT-039.
- [x] Follow-up Pass BS audited compiler-implicit execution ordering around
  constructor member initialization. It broadened SP-AUDIT-024 after a
  definitely throwing first initializer failed to suppress a later
  initializer's static write. The temporary Effects regression was removed.
- [x] Rejected Pass BT tested whether a warning level above the command-line
  convention represented compiler-impossible snapshot evidence. Roslyn's
  current `CSharpCompilationOptions.WithWarningLevel` accepts the value, so the
  premise was false and no canonicality row was added. The temporary worker
  test was removed.
- [x] Follow-up Pass BU audited syntax-tree parse-option provenance and
  broadened SP-AUDIT-034 after an arbitrary non-language-version string passed
  fingerprint recomputation and canonical deserialization. The temporary
  worker test was removed.
- [x] Follow-up Pass BV audited compiler and reference assembly-identity
  provenance and broadened SP-AUDIT-034 after arbitrary non-identity text in
  either field passed fingerprint recomputation and canonical deserialization.
  Both temporary worker regressions were removed.
- [x] Follow-up Pass BW tested cross-field compiler identity consistency and
  broadened SP-AUDIT-034 after a changed assembly name paired with the original
  valid assembly identity passed fingerprint recomputation and canonical
  deserialization. The temporary worker regression was removed.
- [x] Clean Pass BX reviewed Linux publication path canonicalization, local
  filesystem identification, lock/marker ownership, cache locking and eviction,
  container-contract validation, exact native-Z3 resolution, bounded protocol
  JSON reads, manifest/result set validation, and cache replay binding. No new
  supported-container defect was found beyond SP-AUDIT-006, SP-AUDIT-010,
  SP-AUDIT-017, SP-AUDIT-029, SP-AUDIT-039, SP-AUDIT-041, and SP-AUDIT-043.
- [x] Rejected Pass BY exercised calls in a synthesized top-level entry point
  and compiler-generated collection-initializer `Add` invocations. Both probes
  produced no SP0027, but neither callable shape belongs to the documented
  portable call-site traversal/replay surface. The worker separately records
  the top-level callable as `UnsupportedCallable`; collection `Add` replay is
  not among the finite supported prefix forms. Neither probe was promoted, and
  both temporary analyzer tests were removed.
- [x] Clean Pass BZ reviewed API-spec identity, instantiation, term validation,
  and table canonicalization; relational-summary construction, validation, and
  substitution; SMT encoding, cancellation, model/core disposal, and proof
  kernel result checks; and worker lane retirement and result assembly. No new
  supported-surface defect was admitted beyond the existing semantic and
  worker rows.
- [x] Follow-up Pass CA audited the container toolchain certifier and admitted
  SP-AUDIT-046 after a second effective Dockerfile SDK-image argument passed
  the canonical `tooling contract` gate while the reviewed pin remained only
  as a decoy. The temporary Dockerfile mutation was removed.
- [x] Follow-up Pass CB audited release SBOM graph authority and admitted
  SP-AUDIT-047 after the final certifier accepted a fabricated cross-package
  containment relationship and duplicate package SPDX identifiers in fully
  rehashed exact-HEAD bundles. Both disposable bundles were removed.
- [x] Follow-up Pass CC audited compiler-manifest and per-invocation lifecycle.
  Syntax and semantic compiler-error probes left no run directories and were
  rejected. A valid compilation followed by an invalid verifier policy admitted
  SP-AUDIT-048 after two builds leaked two unique invocation directories. The
  disposable consumer was removed.
- [x] Follow-up Pass CD audited release JSON canonicality and admitted
  SP-AUDIT-049 after the final validator accepted contradictory duplicate
  `packageVersion` properties in the exact-HEAD manifest. The disposable bundle
  was removed.
- [x] Rejected Pass CE tested request/cache-policy consistency by changing a
  response to `CacheStatus=Hit` for a cache-disabled request. Existing generated
  protocol rules rejected the impossible response, so no row was added. The
  temporary protocol test was removed.
- [x] Clean Pass CF reviewed final-compilation option capture, syntax-tree and
  additional-file hashing, reference metadata/image parity, linked-module
  ordering and budgets, diagnostic fingerprinting, callable lowering admission,
  manifest/callable parity, and cache input identity. No independent supported-
  surface defect was admitted beyond SP-AUDIT-034, SP-AUDIT-040, SP-AUDIT-045,
  and the existing reference/resource coverage.
- [x] Clean Pass CG reviewed isolated package-source mapping, actual SDK
  selection, analyzer-item discovery, framework consumer builds, and focused
  real verifier execution. The consumer path clears ambient sources, uses a
  private package cache, checks actual requested SDK selection, and executes
  the intended package tests; no independent bypass was admitted beyond the
  existing package-payload/evidence rows.
- [x] Follow-up Pass CH audited coverage-report authority and admitted
  SP-AUDIT-050 after a forged 22-line Cobertura report passed every project,
  aggregate, and changed-TCB floor at 100%. The forged report and probe were
  removed.
- [x] Clean Pass CI reviewed managed exceptional control flow across exact and
  base catches, constant and nonconstant filters, rethrows, sibling handlers,
  throwing and nonreturning finally blocks, unknown external throws, and
  nested callable boundaries. Existing focused tests exercise the escaping-
  exception decisions; no independent defect was admitted beyond
  SP-AUDIT-019 and the existing effect-flow rows.
- [x] Clean Pass CJ reviewed dependency-audit and trusted-mutation evidence
  accounting. The canonical dependency command invokes NuGet directly,
  requires the exact solution-project and approved-source sets, and rejects
  reported problems or vulnerabilities. Its supplied-report mode is not used
  by release qualification. Mutation result/TRX validation remains covered by
  SP-AUDIT-013's existing exact-evidence weakness; no nonduplicative finding
  was admitted.
- [x] Follow-up Pass CK audited analyzer call discovery and managed exception
  ownership. The documented call-shape inventory yielded no new omission
  beyond SP-AUDIT-005, SP-AUDIT-008, and SP-AUDIT-016. A focused nested-catch
  probe admitted SP-AUDIT-051 after a rethrow owned by the inner catch
  fabricated escape of the outer caught exception. The probe was removed.
- [x] Follow-up Pass CL audited release receipt and environment-configuration
  authority. Receipt/package gaps remained covered by SP-AUDIT-003,
  SP-AUDIT-004, SP-AUDIT-028, and SP-AUDIT-049. The canonical configuration
  validator admitted SP-AUDIT-052 for its empty-set binder failure and
  SP-AUDIT-053 after wildcard environment authorization passed a mocked exact
  API check. All mocks, generated evidence, and temporary contract edits were
  removed.
- [x] Follow-up Pass CM used six independent read-only auditors across
  analyzer/ContractFor, compiler provenance, Effects/dataflow, Linux host/build,
  worker/SMT/protocol, and release/package/CI authority. Focused canonical-
  container probes admitted SP-AUDIT-054 through SP-AUDIT-062: release-tag
  bypass actors and exclusions, generated-tree validation, resultless launcher
  success, unauthenticated summary provenance, cancellation/finally handling,
  catch-state flow, by-reference ownership, and compound-conversion effects.
  Every temporary test, mock, and evidence directory was removed; the six
  auditors made no repository edits.
- [x] Follow-up Pass CN tested the strongest remaining independent candidates
  from that wave. It admitted SP-AUDIT-063 after a deterministic late SARIF
  failure left a mixed publication generation, SP-AUDIT-064 after the effects
  profile skipped a recognized peer-generated companion, SP-AUDIT-065 after
  unrelated release environments hidden behind comment tokens passed the
  canonical configuration validator, SP-AUDIT-066 after the composed worker
  deadline exceeded the documented project-plus-one-grace boundary, and
  SP-AUDIT-067 after all three release stages certified a package dependency
  graph that disagreed with the package bytes. A peer-generator stale-
  diagnostic probe passed its exact final-compilation control and was rejected;
  Roslyn CFG exposure also disproved the proposed user-defined truth-operator
  omission. All temporary source, workflow, contract, mock, timing-test, and
  package mutation changes were removed.
- [x] Follow-up Pass CO used six independent read-only auditors across
  analyzer/ContractFor, compiler provenance, Effects/dataflow, Linux host/build,
  worker/SMT/protocol, and release/package/container authority. Focused
  canonical-container probes admitted SP-AUDIT-068 through SP-AUDIT-074:
  mixed generated partials, unused nested control attributes, never-executed
  lambdas, Unicode-escaped activation, implicit string-conversion effects,
  fresh object-initializer ownership, and request/cache-state binding. A
  coalescing-conversion hypothesis was rejected because the current scanner
  retained the demonstrated static write and conservatively returned unknown
  throws/incomplete evidence. Every temporary test was removed; auditors made
  no repository edits.
- [x] Follow-up Pass CP exercised the strongest remaining nonduplicative host,
  worker, compiler, coverage, and container-certifier candidates. It admitted
  SP-AUDIT-075 through SP-AUDIT-081 after nested publication setup left an
  output directory, path text corrupted warning identity, a floating Dockerfile
  frontend passed the contract gate, an authentic changed-TCB report skipped a
  behavioral constant, backend renewal failure became a timeout, feature scope
  failed to bind discovery, and a linked module occupied the manifest slot.
  The authentic coverage probe used 44 existing reports and was read-only;
  every temporary constant, Dockerfile mutation, and regression was restored.
  Duplicate launcher/publication/workflow candidates remained covered by
  SP-AUDIT-057, SP-AUDIT-063, and SP-AUDIT-065. Catalog-only pilot uniqueness
  and active-SDK claims were not admitted without an executable reproduction.
- [x] Follow-up Pass CQ used six independent read-only auditors for analyzer/
  ContractFor, compiler provenance, Effects/dataflow, Linux host/build,
  worker/SMT/protocol, and release/package/CI authority. Focused canonical-
  container probes admitted SP-AUDIT-082 through SP-AUDIT-092: the tag workflow
  omitted Debug qualification, Compose projects shared a mutable image tag,
  strict diagnostics bypassed the MSBuild error channel, cache/output topology
  was asymmetric, unboxing retained caller ownership, selected nested callables
  disappeared, compiler resource-limit evidence was rejected, runtime reasons
  masqueraded as compiler lowering, trailing separators changed fingerprints,
  peer-generated rejected ContractFor attributes escaped, and companion bodies
  were analyzed as implementations. Every temporary regression was removed and
  the auditors made no repository edits. A lock-nested collection-initializer
  hypothesis was rejected because the scanner already returned conservative
  unknown write/throw evidence; worker/protocol pass 2 found only duplicates of
  SP-AUDIT-041, SP-AUDIT-074, and SP-AUDIT-079. Pilot-row uniqueness, exact SDK
  selection, and package-license binding were not admitted without executable
  certifier reproductions.
- [x] Follow-up Pass CR used the same six disjoint read-only areas and admitted
  SP-AUDIT-093 through SP-AUDIT-103 after focused local or exact in-memory
  probes demonstrated duplicate/vacuous pilot qualification, unbound package
  licenses and SBOM purls, incomplete cancellation-catch certification,
  pattern-based semantic-string and static-property meta-policy bypasses,
  mutable compiler integer domains, swappable relational-summary roles,
  mutation-bearing branch re-evaluation, omitted unused specification-pack
  selection, and inactive cache topology validation. Every temporary test and
  disposable package mutation was removed; auditors made no repository edits.
  The pass rejected source-only runtime-snapshot cleanup and bind-mount
  ownership hypotheses without canonical-host executions, a conservative
  lock-nested collection-initializer path, unsupported compound-operator cases,
  and duplicates of the existing proof/core/cache/provenance rows.
- [x] Follow-up Pass CS repeated the six bounded read-only areas and admitted
  SP-AUDIT-104 through SP-AUDIT-113 after exact in-memory or canonical-container
  probes demonstrated executable cancellation arguments, aliased/indexer cache
  writes, interpolated source synthesis, omitted release-certifier TCB leaves,
  left-mutation checked-overflow loss, value-type instance initialization loss,
  base-property dispatch imprecision, compiler-output publication replacement,
  `NAME_MAX` metadata overflow, and noncanonical nested body ordering. Every
  temporary test was removed and all auditors remained read-only. The pass did
  not admit unexecuted runtime-snapshot cleanup, API-spec provenance, or
  65-block hydration hypotheses; those require a discriminating supported-
  surface fixture rather than source inference alone. Adjacent proof, cache,
  workflow, and generated-compilation ideas deduplicated to existing rows.
- [x] Follow-up Pass CT repeated the six disjoint local-correctness areas and
  admitted SP-AUDIT-114 through SP-AUDIT-122 after exact in-memory or canonical-
  container probes demonstrated foreign ContractFor diagnostic locations,
  methodless rejected controls, mutation-bearing initializer re-evaluation,
  missing auto-accessor outcomes, implicit-constructor imprecision,
  contradictory SBOM checksums, self-lowering coverage policy, nontransactional
  cache publication, and unrecoverable publication-set metadata. Every
  temporary test was removed and auditors made no repository edits. A real
  apostrophe-path packaged build passed and was rejected as a hypothesis;
  Linux host ownership changes were not admitted without an executable UID/GID
  fixture. Other candidates deduplicated to existing provenance, proof, cache,
  package, and publication rows.
- [x] Follow-up Pass CU repeated the six read-only partitions and admitted
  SP-AUDIT-123 through SP-AUDIT-131 after canonical-container or exact local-
  certifier evidence demonstrated ignored lowered cache caps, multitarget SARIF
  overlap, archive-tooling Git dependence, lossy UTF-16 artifacts, peer-
  generated target drift, partial-property body mismatch, unbound publication
  plans, mismatched SBOM subjects, and unchecked public package metadata. All
  temporary probes were removed and the auditors edited no files. A net9
  `System.Threading.Lock` no-throw probe already abstained and was rejected;
  an apostrophe-path packaged build also remained green. Remaining aggregation,
  resource, atomic-file, diagnostic, and schema ideas deduplicated to existing
  rows or lacked an executable supported-surface discriminator.
- [x] Follow-up Pass CV repeated six bounded read-only partitions and admitted
  SP-AUDIT-132 through SP-AUDIT-137. Compiler-location probes demonstrated the
  zero/one-based model split; exact release command graphs exposed three
  disconnected or unbound authorities; canonical MSBuild evaluation reproduced
  stale private analyzer/collector paths; and a focused packaged build reproduced
  whitespace-only SARIF usage failure. A disposable Git directory-to-file
  snapshot probe remained green and was rejected. Analyzer, Effects, worker/SMT,
  and remaining host hypotheses were clean, fail-closed, or duplicates.
- [x] Follow-up Pass CW expanded to 20 simultaneous, disjoint, read-only
  partitions while retaining one coordinator as the only register writer. It
  admitted SP-AUDIT-138 through SP-AUDIT-165 from executable analyzer/compiler/
  effect probes and exact local protocol, container, acceptance, TCB, SBOM, CI,
  and release-certifier predicates. Focused canonical-container tests reproduced
  the captured primary-constructor receiver-read omission; source auditors had
  already executed the conditional-call and lowered-graph discriminators. The
  coordinator rejected the disproved snapshot-transition report, duplicated
  mutation/cache/host issues, fail-closed precision losses, unsupported-host
  requests, and an unexecuted ContractFor generic-wrapper suspicion. All
  temporary probes were removed and no auditor edited repository files.
- [x] Follow-up Pass CX repeated the same 20 disjoint read-only partitions and
  admitted SP-AUDIT-166 through SP-AUDIT-187. Exact compiler round trips
  demonstrated missing return values and reordered summary arguments; the
  ContractFor owner probe reproduced generic-layer misalignment; the pinned Z3
  probe measured uncharged model extraction; and local acceptance, build, TCB,
  mutation, protocol, publication, SBOM, CI, and documentation predicates
  exposed the remaining certifier and lifecycle defects. Effects, analyzer,
  package, compiler-provenance, and worker challenge passes were clean or
  deduplicated. A persistent-volume UID-change hypothesis and a dormant receipt
  helper were rejected without supported executable callers. Auditors made no
  repository edits and no temporary probe remains.
- [x] Follow-up Pass CY again used 20 disjoint, read-only partitions and admitted
  SP-AUDIT-188 through SP-AUDIT-207. Offline ContractFor probes reproduced both
  `ref readonly` failures; exact local predicates demonstrated Git-quoted TCB
  loss, mutation exception-text misclassification, release scalar coercion,
  offline publication-state drift, and package runtime-asset leakage. Source
  reviews identified stale outer-gate receipts, generated-partial suppression,
  syntax-tree replay rebinding, module-reference/assumption/certainty gaps,
  publication marker poisoning, canceled staging residue, and omitted module
  initialization. CI, BuildTasks, container, worker, and SMT challenge passes
  were otherwise clean or duplicates. One benign compiler lane was blocked by
  an automated classifier and reassigned; no security work was attempted, no
  auditor edited repository files, and no temporary probe remains.
- [x] Follow-up Pass CZ repeated 20 disjoint read-only partitions and admitted
  SP-AUDIT-208 through SP-AUDIT-222. A local Roslyn probe demonstrated honest
  named-argument misordering; offline compiler checks established function-
  pointer convention equivalence; exact local predicates reproduced case-folded
  mutation ledgers, SBOM shape/vocabulary drift, and self-invalidating plan
  output. Analyzer, provenance, protocol, worker, docs, and summary/spec reviews
  exposed the remaining parenthesis, branch-shape, empty-tree, claim/coverage,
  span, assumption-use, and concrete-null mismatches. Acceptance, BuildTasks, CI,
  container, coverage, package, Effects, Linux host, and SMT passes were clean or
  duplicates. No auditor edited repository files and no temporary probe remains.
- [x] Follow-up Pass DA repeated 20 disjoint read-only partitions and admitted
  SP-AUDIT-223 through SP-AUDIT-236. Exact local predicates exposed tip-only
  coverage fallback, non-self-contained release evidence, permissive SBOM and
  publication-plan validation, and unbound compiler/protocol identities. The
  pinned Z3 probe reproduced an ill-formed UTF-16 false proof; ContractFor,
  analyzer, specification, worker, and documentation reviews exposed the signed-
  zero, implicit-base, rejected-metadata, concrete-type, trust-use, and public-
  semantics gaps. Acceptance, BuildTasks, CI, container, compiler lowering,
  Effects, Linux host, mutation, and package passes were otherwise clean or
  duplicates. No auditor edited repository files and no temporary probe remains.

## Previously closed bounded audit

- [x] Pass A: audit analyzer/generator/collector parity, final-compilation
  ContractFor behavior, compiler-artifact schema 12 provenance, and mutation
  discrimination for the consolidated semantic authority. The bounded review
  accepted no defect; 99 analyzer/core/collector, 51 ContractFor, and 32
  schema-12 worker cases passed in the canonical container.
- [x] Pass B: audit Linux host containment/publication, cancellation and exact
  Z3 loading, the three-package graph, packaged consumers, and release-evidence
  commit binding. The bounded review accepted no defect; 21 host/worker,
  16 package/release-authority, and 2 exact native-boundary cases passed in the
  canonical container.
- [x] Close every accepted supported-surface reproduction with a focused
  regression and, for trusted-boundary behavior, a discriminating mutation.
  Neither bounded pass admitted a supported-surface or certifier defect, so no
  production fix, new regression, wire change, or mutation entry was required.

Only an executable failure in the documented container-supported surface, or
a demonstrated certifier defect that can admit invalid release evidence, may
add a row. The audit stops after these two passes; unsupported roadmap features
and speculative hostile-host races do not reopen the preview.

## Closed architecture and supported behavior

- [x] The canonical Linux amd64 container is the only full-verifier host.
  Native Windows/Visual Studio execution and Windows runtime primitives are
  removed from the supported verifier.
- [x] `SharpProof.Analyzer` and `SharpProof.ContractForGenerator` are thin
  entry assemblies over one `SharpProof.Analyzer.Core` implementation; the
  package exposes exactly one analyzer, one generator, and one collector.
- [x] Generated companions are validated on the final compilation without
  duplicating handwritten generator diagnostics.
- [x] Compiler artifact schema 12 binds canonical module order, image sizes,
  MVIDs, hashes, metadata identity, warning policy, and realized diagnostics;
  per-module, closure-byte, and module-count limits are enforced.
- [x] Publication locks and ownership markers cover request, result, compiler
  manifest, and optional SARIF paths as one canonical set; partial overlap,
  unowned destination files, and recognized network filesystems fail closed.
  SP-AUDIT-006 records the remaining derived-metadata symlink hole.
- [x] The launcher owns one direct Linux worker child with a bounded startup
  barrier, parent-death signal, cancellation deadline, and exact packaged Z3
  resolver.
- [x] The release graph is exactly `SharpProof.Attributes`, `SharpProof`, and
  `SharpProof.Verifier`; interface, schema, generated-output, TCB, coverage,
  mutation-catalog, documentation, and package inventories are drift-gated.
- [x] CI and local repository commands execute in the pinned container; Docker
  owns CPU and memory isolation.

## External qualification and publication

After the final source commit, the release procedure must generate evidence at
that exact commit: Debug and Release gates, coverage, all 136 trusted
mutations, local packages and isolated consumers, five pilots, SBOM/package
validation, and publication plan-only. Any subsequent tracked change
invalidates that evidence and requires regeneration.

NuGet credentials, protected release tags, private/public environments, and
external publication are owner-controlled release operations. They are not
locally controlled code debt and require separate authorization. This audit
does not authenticate, publish, promote, or tag.

## Explicit preview boundaries

Native host execution, ARM64 verifier containers, Rider integration,
shared/network publication, hostile concurrent host filesystem mutation,
loops, mutable-heap reasoning, virtual dispatch, and general source-callee
verification remain explicit roadmap items rather than release blockers.
