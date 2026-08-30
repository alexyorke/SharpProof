# Read-Only Multi-Agent Bug Audit - 2026-08-29

This section records the coordinator's unverified compilation of findings from exactly 10 read-only auditors. The central writer did not inspect or reverify the code. Auditor coverage: Analyzer/Core (4), Frontend/IR (4), Dataflow/Effects (2), Contracts/Specs/Summaries (2), SMT/Verifier (2), Worker/Host/Verify (2), Compiler/Build/Generators (4), Gates/Package/Meta (3), Tests/Fuzz/Misc (4), and Scripts/CI (1).

## 15. HIGH - Z3 validation and native load are vulnerable to a file-replacement race

- Files and members: `SharpProof.Host/ContainerContract.cs`, `ResolveZ3LibraryRequired`, lines 120-155; `SharpProof.Host/ContainerNativeLibrary.cs`, `InstallZ3ResolverRequired`, lines 28-36.
- Mechanism: The resolver hashes and closes a stream, returns only the pathname, and `NativeLibrary.Load` later reopens it. The verified bytes are not tied to the loaded file handle or inode.
- Impact: Deployment/update races or mutation in a writable native root can load bytes different from those hashed, defeating native-payload integrity.
- Safe reproduction/evidence: The code has a TOCTOU gap. A controlled unit or integration harness can pause between validation and load and replace the test fixture with a different same-length fixture.

## 18. HIGH - Worker and cache identity exclude the native Z3 solver

- File: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`
- Members: `WorkerBinaryIdentity.CreateSnapshot`, `RuntimeComponents`
- Lines: 48-105 and 193-244, especially 207-219
- Mechanism: The closure seeds the worker DLL, deps, and runtimeconfig and extracts only DLL names. `libz3.so` is absent from components, staging, and `WorkerBinarySha256`.
- Impact: Solver replacement or upgrade leaves cache/input identity unchanged. Cache results can cross solver versions, and the staged runtime does not pin the actual solver.
- Safe reproduction/evidence: Compute identity for two isolated fixture closures differing only in `libz3.so`; the identities remain equal. The package ships `tools/native/linux-x64/libz3.so`.

# Read-Only Multi-Agent Bug Audit - Wave 2 - 2026-08-29

This section records the coordinator's unverified compilation of 26 new findings from exactly 10 fresh read-only auditors, after title/mechanism deduplication against the prior audit and within this wave. The central writer did not inspect or reverify the code. Auditor coverage: Dataflow (1), SMT core (8), Verify core (1), Summaries (1), CompilerCollector (2), ContractForGenerator and Attributes (0), Worker/Launcher/Protocol (2), Gates (5), Package and BuildTasks (3), and release scripts (3).

# Read-Only Multi-Agent Bug Audit - Wave 3 - 2026-08-29

This section records 37 findings from exactly 10 fresh read-only auditors after title/mechanism-only deduplication against Waves 1-2 and within Wave 3. The coordinator compiled the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 3.12. HIGH - Record-class `with` expressions omit guaranteed allocation and mis-map copy-constructor regions

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanWith`
- Lines: 105-132, especially 110-119
- Mechanism: The scanner maps `<Clone>$` to the copy constructor, passes `withOperation.Operand` as the constructor receiver, and supplies empty argument-region and actual-argument arrays. Runtime creates a fresh record and passes the old record as copy-constructor `original`; the code does not join `Allocate(Managed)`.
- Impact: A pure source copy constructor can make `R Copy(R r) => r with { X=2 };` appear nonallocating; receiver writes may map onto `r`, while `original` effects map `Unknown`.
- Safe evidence: Analyze a reference record `R` with an explicit protected `R(R original)` and a method returning `r with { ... }`.

## Wave 3.13. HIGH - Definitely failing source type initialization contributes no exception effect to static field access

- File: `SharpProof.Effects/EffectAnalysisSession.cs`
- Member: `ResolveStaticFieldTypeInitialization`
- Lines: 253-293, especially 282-289
- Mechanism: `StaticInitializationCannotComplete` returns `EffectSummary.Empty`; the completion evaluator separately makes the access non-completing.
- Impact: Later operations are suppressed, but the runtime `TypeInitializationException` and initialization boundary are absent.
- Safe evidence: `class C { internal static int X=Fail(); static int Fail()=>throw new Exception(); } int M()=>C.X;`.

## Wave 3.14. MEDIUM - Pattern subpatterns are scanned before and independently of implicit Length/indexer calls and null gates

- Files and members: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanListPattern`, lines 899-934, especially 901 and 906-932; `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, dispatch at 366-372 and `ScanPropertySubpattern`, lines 5-13.
- Mechanism: `ScanMany` eagerly scans nested getters before `GetReachableImplicitListPatternMembers`; summaries are independently joined without respecting the governing nullness or completion gate.
- Impact: Nested getter effects may be reported even when a proven-null governing object or non-completing `Length` prevents evaluation.
- Safe evidence: Use a custom list-like type whose `Length` always throws and whose element `P` getter mutates state in `x is [{ P: 1 }]`; alternatively, use known-null `x is { P: 1 }`.

## Wave 3.15. MEDIUM - Nested rejected contract-API calls mark the containing method as rejected usage

- Files and members: `SharpProof.Contracts/ContractClauseInventoryBuilder.cs`, `CreateCore`, lines 55-79, especially 66-73; downstream `SharpProof.Contracts/EffectiveContractSourceResolver.cs`, `HasSelectedContractIntent`, lines 7-14.
- Mechanism: Descendant traversal classifies accepted nested calls as `NestedCallable`, but rejected or lookalike calls update one callable-wide flag without checking enclosing-callable ownership. `HasSelectedContractIntent` consumes it directly.
- Impact: An outer callable with no contract usage can be attributed a nested callable's invalid usage.
- Safe evidence: An outer method contains local `void Local(bool x){ RejectedOrShadowContract.Requires(x); }`; the outer inventory can set `HasRejectedContractApiUsage` and `HasSelectedContractIntent` true.

## Wave 3.16. MEDIUM - Full binding skips closed return-contract validation on void methods

- Files and members: `SharpProof.Contracts/ContractBinder.cs`, `BindClosedAttributes`, lines 252-262 at the `Result.HasValue` guard; `SharpProof.Contracts/ContractCanonicalization.cs`, `CreateVariables`, lines 289-299; `SharpProof.Contracts/ContractSelectionInventory.cs`, `Select`, lines 141-146.
- Mechanism: A void method creates no result variable, and return attributes bind only when a result exists, although selection sees recognized return attributes.
- Impact: Malformed declared intent disappears instead of producing `InvalidClosedAttribute`; binding may succeed without a return clause.
- Safe evidence: `[return: NotNull] static void M(){}` followed by `Bind(M)`. `System.Void` is not reference-capable, but validation is skipped.

## Wave 3.17. MEDIUM - Closed attributes on partial methods depend on which partial symbol is supplied

- Files and members: `SharpProof.Contracts/ContractBinder.cs`, `BindCore`, lines 87-113, 119-126, and 148, plus `BindClosedAttributes`, lines 241-262; `SharpProof.Contracts/ContractSelectionInventory.cs`, `Select`, lines 135-160, and `GetRejectedCallableSelectionFeatures`, lines 179-200.
- Mechanism: Direct clause inventory normalizes a partial definition to its implementation, but closed-attribute paths inspect only the supplied symbol. Distinct Roslyn method and parameter symbols make behavior depend on definition versus implementation and attribute location.
- Impact: `Positive`, `NotNull`, `InRange`, or rejected identity may be omitted; `Bind(definition)` and `Bind(implementation)` can differ for one logical method.
- Safe evidence: Place `[Positive]` on only one partial declaration and the body on the other; compare bind/select for both parts, then repeat with a return attribute.

## Wave 3.18. LOW/MEDIUM - Public contract inventory and binder can throw for symbols or operations from another Compilation

- File: `SharpProof.Contracts/ContractClauseInventoryBuilder.cs`
- Members: `Create`, lines 27-37, and `CreateCore`, lines 55-59, especially `CompilationModelProvider.GetSemanticModel(_compilation, body.SyntaxTree)`; downstream `ContractBinder.Bind` via `EffectiveContractSourceResolver`
- Mechanism: Public APIs accept `IMethodSymbol`/`IOperation` and request a semantic model for `body.SyntaxTree` without checking compilation ownership or `ContainsSyntaxTree`. Roslyn rejects a tree not owned by that compilation.
- Impact: Cross-wired analyzer input yields an unhandled `ArgumentException` instead of a typed `ContractBindingFailure`, potentially aborting analysis.
- Safe evidence: Use a builder or binder for compilation A with a source method symbol or implementation body from compilation B.

## Wave 3.19. MEDIUM - Ill-formed UTF-16 constants enter trusted spec tables, fail instantiation, and collide in digest input

- Files and members: `SharpProof.Specs/ApiSpecTermValidator.cs`, `Validate`, lines 50-58; `SharpProof.Specs/ApiSpecInstantiation.cs`, `Instantiation.Term`, line 145; `SharpProof.Specs/ApiSpecContentDigest.cs`, `Add(SpecTermDeclaration,...)`, lines 79-90.
- Mechanism: `SpecStringDeclaration` accepts lone surrogates and marks them total and non-null; `IrFactory.String` later rejects them. UTF-8 replacement fallback maps distinct lone surrogates such as D800 and D801 identically.
- Impact: Trusted table creation succeeds while instantiation returns `InvalidExpression`; distinct accepted content can share `ContentSha256` input without a cryptographic collision.
- Safe evidence: Equality between identical `SpecStringDeclaration("\uD800")` operands passes `Create`, but `InstantiatePostconditions` fails. Compare otherwise identical D800 and D801 table digests.

## Wave 3.20. MEDIUM - Result facets are not validated against target result type before cardinality proves non-null

- Files and members: `SharpProof.Specs/ApiSpecTable.cs`, `CompileTemplate`/`NormalizeFacets`, lines 122-145 and 251-307; `SharpProof.Specs/ApiSpecTermValidator.cs`, variable case, lines 38-45.
- Mechanism: `NormalizeFacets` lacks `ResultType`; a String result can have `MaybeNull+Empty`. The validator treats cardinality as non-null for any result and certifies `Length(result)` as total.
- Impact: An inapplicable facet certifies a potentially null string operation as a trusted total postcondition.
- Safe evidence: Define a static String result with `MaybeNull+Empty` and postcondition `Length(Result)>=0`; table creation accepts it.

## Wave 3.21. LOW - Spec totality validation ignores statically unreachable branches

- File: `SharpProof.Specs/ApiSpecTermValidator.cs`
- Members: `ValidateBinary`, lines 127-169; `ValidateConditional`, lines 172-193
- Mechanism: `AndAlso` and `OrElse` require both operands to be total, and conditional requires both branches, even when constant control makes the partial subtree unreachable.
- Impact: Semantically total short-circuit specifications are rejected, forcing weaker or absent specifications.
- Safe evidence: `false && ((1/0)==0)` and `true ? true : ((1/0)==0)` are rejected.

## Wave 3.26. HIGH - Publication lock pathname can be replaced while the original inode remains locked

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Members: `PublicationLock` constructor/`Acquire`, lines 803-824; `AcquirePublicationSet`, lines 249-285
- Mechanism: `flock` applies to the opened inode, but the code never verifies that the pathname still resolves to the same device and inode. A same-uid process can unlink and recreate the lock pathname while lease A retains the old inode; lease B then locks the replacement. Later lexical canonical-path comparison misses the inode change.
- Impact: Concurrent exclusive ownership enables overlapping staging, commit, invalidation, and rollback and can produce inconsistent artifacts.
- Safe evidence: In a disposable directory, acquire A, unlink `PublicationLockName(path)`, create a replacement with the same name, and acquire B with a short timeout while A remains held. Both can succeed.

## Wave 3.27. HIGH - Publication ancestor-directory replacement is not detected

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Members: `Canonicalize`, lines 50-108; `AcquirePublicationSet`, lines 249-285; `BindPublicationSet`, lines 469-525; `SyncDirectory`, lines 146-171
- Mechanism: `Canonicalize` discards observed device/inode identities and returns a lexical path; confirmation uses string equality. A renamed or replaced writable ancestor retains the same lexical path while lock descriptors remain in the old tree and output resolves through the replacement.
- Impact: Split lock/publication namespaces permit concurrent publication or redirection into the replacement tree.
- Safe evidence: Pause after lock open, rename parent `p` to `p-old`, recreate `p`, and resume. The canonical string is unchanged while the lock belongs under `p-old` and markers under new `p`.

## Wave 3.28. MEDIUM - The 16 KiB container-contract bound has pathname TOCTOU and does not bound the parsed file

- File: `SharpProof.Host/ContainerContract.cs`
- Member: `ReadBoundedJson`
- Lines: 173-190, with `FileInfo.Length` at 175-180 and separate `FileStream` open/parse at 181-186
- Mechanism: The pathname size check and later open can resolve different inodes; the descriptor is not fstat'ed or byte-limited before `JsonDocument.Parse`.
- Impact: The intended resource bound can be bypassed, causing excess allocation and CPU and possible memory exhaustion.
- Safe evidence: In a disposable directory, atomically exchange a no-more-than-16-KiB JSON file and a very large JSON file at the configured contract pathname during `ValidateRequired`; an iteration can stat the small file and parse the large one.

## Wave 3.29. MEDIUM - Z3 resolver registration is published before the verified native handle

- File: `SharpProof.Host/ContainerNativeLibrary.cs`
- Members: `InstallZ3ResolverRequired`, lines 28-37; `ResolveZ3Import`, lines 50-65
- Mechanism: `SetDllImportResolver` makes the callback callable at lines 32-34, but `_z3Handle` is not published with `Volatile.Write` until line 36. A concurrent first P/Invoke can invoke the resolver and read zero at line 65; P/Invoke callers do not take the installation lock.
- Impact: Nondeterministic binding failure or default probing to ambient `libz3` can bypass the verified-library-only policy.
- Safe evidence: In an isolated test process, race repeated install calls with the assembly's first Z3 API call and schedule the interval between resolver registration and handle write. The resolver can observe zero by construction.

## Wave 3.30. MEDIUM - Protocol serializers can emit documents rejected by the bounded reader

- Files and members: `SharpProof.Worker.Protocol/ProtocolJson.cs`, `SerializeRequest`, lines 61-64, `SerializeResponse`, lines 90-94, `OpenJsonReader`, lines 71-87, especially 80-84, and `ValidateProtocolErrors`, lines 671-680; `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, `IsProtocolErrorValid`, lines 900-902.
- Mechanism: Serialization has no UTF-8 size check against the 16 MiB `MaximumJsonBytes`; validation has no string or collection limits, while file reads reject documents over the ceiling.
- Impact: Legitimate output becomes an oversized or malformed-result failure at launcher ingestion.
- Safe evidence: An otherwise valid failed response with one nonblank error `Message` over 16 MiB validates and serializes. After writing, `ReadUtf8File` throws `InvalidDataException`.

## Wave 3.31. MEDIUM - Protocol errors override contradictory claim evidence during run-state projection

- Files and members: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `TryProjectRunState`, lines 140-168, especially 147-167, and `MatchesCallableProjection`, lines 170-229, especially 178-228; `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateRun`, lines 585-620, and `ValidateUnknownCoverage`, lines 564-584.
- Mechanism: When `Errors` is nonempty, recognized error codes dictate the run-state tuple without reconciling owned claim outcomes or the result of `Classify`. This admits both successful proof evidence under a fatal failure and fatal backend evidence projected as an unrelated timeout.
- Impact: Accepted responses can contradict their proof evidence, changing retry, telemetry, cache, and policy behavior or misleading consumers about successful claims.
- Safe evidence: (1) Add `backend.unavailable` to a one-`Proven` response and project it as `Failed/BackendUnavailable`; validation accepts the retained Proven claim. (2) A claim `Unknown/BackendUnavailable` plus `worker.timeout` can validate as `TimedOut/None` although `Classify` requires `Failed/BackendUnavailable`.

## Wave 3.32. MEDIUM - CreateIncomplete is not tolerant of malformed manifests it is used to report

- File: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`
- Member: `CreateIncomplete`
- Lines: 44-73; callable projection 50-57; claim projection 58-71
- Mechanism: The method directly enumerates `manifest.Callables` and `Claims` and dereferences their entries without null guards despite serving a malformed-manifest failure path. Null collections or entries throw `NullReferenceException`; `FirstOrDefault` over callables can also dereference a null entry while matching a nonnull claim.
- Impact: A malformed compiler or in-memory manifest replaces the intended structured failure with an unhandled worker failure or no result.
- Safe evidence: Invoke it with `Callables=null`, `Callables=[null!]`, `Claims=null`, or `Claims=[null!]`; current code throws instead of returning an incomplete response.

## Wave 3.33. HIGH - Owned-backend cleanup exceptions replace a completed manifest-bound response

- Files and members: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync` finally, lines 341-347; `VerificationLane.DisposeOwnedBackend`, lines 535-539; `SharpProof.Worker/Program.cs`, `Main`, lines 97-106.
- Mechanism: The `finally` block calls backend-supplied `Dispose` without isolation. A thrown exception replaces the pending response, and `Main` then builds a generic empty-manifest failure.
- Impact: Completed proof or refutation, hashes, association, and evidence are lost; a library caller receives an exception.
- Safe evidence: Use a backend that returns `Unsatisfiable` and then throws from `Dispose`. Valid one-claim verification completes, but line 345 prevents delivery.

## Wave 3.34. HIGH - Cleanup during partial lane-construction failure bypasses the manifest-bound BackendUnavailable response

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Members: `TryCreateLanes`, lines 437-465, especially 458-461; `VerifyAsync`, lines 207-211
- Mechanism: When lane N creation fails, earlier lanes are disposed without exception isolation. A cleanup exception exits before error assignment and `false` return.
- Impact: A contained setup failure escapes; `Main` emits a generic empty-manifest failure, and later lanes may remain undisposed.
- Safe evidence: Use two targets with parallelism 2. The first factory backend throws on `Dispose`, and the second factory throws `DllNotFoundException`.

## Wave 3.35. MEDIUM - Concurrent VerifyAsync calls share one injected backend without serialization

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Locations: Fields and constructor, lines 8-20; `VerifyAsync`, lines 39-42; `TryCreateLanes`, lines 431-435; `CreateLane`, lines 468-473
- Mechanism: Each invocation wraps the same `_backend`; there is no active-run guard. Concurrent callers can enter `CheckAsync` on the same backend, which is not required to be reentrant, and resource counters are shared.
- Impact: Cross-request interference, spurious infrastructure/resource outcomes, and native races can occur.
- Safe evidence: Use a test backend that blocks and flags active calls above one; `Task.WhenAll` over two `VerifyAsync` calls reaches the same backend concurrently.

## Wave 3.36. MEDIUM - Parallel renewal failures with different causes classify remaining targets nondeterministically

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Locations: Retirement state and `RecordRetirement`, lines 217-235; renewal handling, lines 257-279; unclaimed-target fill, lines 290-301; `VerificationLane.Renew`, lines 499-531
- Mechanism: Scheduler-dependent lock order determines which failed-renewal reason is permanently retained; a later distinct cause is discarded, and all unclaimed targets inherit the first.
- Impact: Identical inputs and equivalent factory behavior can yield different claim/run reasons and retry decisions.
- Safe evidence: Use at least three targets and parallelism 2. Let the first two time out, synchronize replacements to throw `DllNotFoundException` versus `InvalidOperationException`, and vary release order.

## Wave 3.37. MEDIUM - One lane-renewal failure globally retires healthy lanes

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Locations: Shared retirement, lines 217-220; record at 264-278; lane exit, lines 238-246; synthesized unclaimed targets, lines 290-301
- Mechanism: A lane-local or transient renewal failure sets global retirement. Other usable lanes exit, and remaining work becomes `Unknown`.
- Impact: Avoidable incomplete or failed runs and lost proofs/refutations occur under partial backend failure.
- Safe evidence: Use at least four targets and parallelism 2. Lane A times out and its replacement throws; healthy lane B completes its current query, then exits because of the global flag while remaining targets are synthesized `Unknown`.

# Read-Only Multi-Agent Bug Audit - Wave 4 - 2026-08-29

This section records 7 findings from exactly 10 fresh read-only auditors. The relay compiled the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 4.4. MEDIUM - Contradictory entry conditions do not short-circuit callable postcondition verification

- Files and members: `SharpProof.Worker/CallableVerifier.cs`, `VerifyPostconditionsAsync`, lines 96-101, 103-131, and 187-218; intended contradictory-vacuity handling at lines 251-292. Related: `EffectClaimResultAssembler.Assemble`, lines 62-77.
- Mechanism: After `entryFeasibility` establishes `IsContradictory`, the method still executes the body at lines 103-114, returns `Unknown` on body failure at 115-118, builds evidence and returns `Unknown` on evidence failure at 121-131, and can return per-claim `Unknown` for missing, deep, or unsupported postconditions at 187-218. Only after a successful backend query does it attach `ContradictoryPreconditions` vacuity at 251-292.
- Impact: Valid vacuous proofs become deterministic false-`Unknown` results whenever the body, evidence, or goal is unsupported, and the advertised contradictory-precondition vacuity and proof-core evidence are lost.
- Safe evidence: Static control-flow trace. This is inconsistent with `EffectClaimResultAssembler.Assemble` lines 62-77, which emits `Proven`/`VacuousEntry` directly once entry contradiction is known, except for its explicit `UnsupportedContract` case.

## Wave 4.5. MEDIUM - Effect replay authenticates capability and exception constraints but validates only allocation violations

- Files and members: `SharpProof.Worker/EffectCounterexampleReplayer.cs`, `Replay`, lines 34-56; `Interpret`, lines 117-155; `IsViolation`, lines 158-169; `ComputeConstraintIdentity`, lines 223-241. Downstream: `SharpProof.Worker/EffectClaimResultAssembler.cs`, `Assemble`, lines 80-103.
- Mechanism: `Interpret` recognizes only `ManagedObjectAllocation` and `ManagedArrayAllocation` at lines 122-135, rejects events carrying `SpecWitnessIdentifier`, `ScalarOperands`, or `ExactExceptionTypeHierarchy` at 137-143, and always constructs `Effects=Allocates` with no capability or exception witness at 146-155. `IsViolation` checks only `ZeroAllocations` or `AllowedEffects` at 162-168, never `AllowedCapabilities` or `AllowedExceptionTypes`, even though those dimensions are authenticated in `ComputeConstraintIdentity` at 233-238. `Replay` also returns null as soon as any event cannot be interpreted at 39-43.
- Impact: A refuted `EffectContract` whose violation is a forbidden capability or thrown exception cannot produce a replay witness; `EffectClaimResultAssembler` converts it to `Unknown/CounterexampleReplayFailed` at lines 86-93. Mixed replay streams containing an otherwise valid non-allocation event also fail wholesale.
- Safe evidence: Static field/use and exhaustive switch trace; no malformed input is required.

## Wave 4.6. LOW - Signed-64 endpoint bounds are not canonicalized to unbounded sentinels

- Files and members: `SharpProof.Dataflow/IntervalValue.cs`, type contract at lines 3-6; `SharpProof.Dataflow/IntervalDomain.cs`, `Range`/`Create`, lines 21-88, especially 54-75 and return at 88, and `LessThanOrEqual`, lines 91-115, especially 103-110; propagation into `SharpProof.Dataflow/SequenceCardinalityDomain.cs`, constructor/`Create`, lines 12-17 and 39-74.
- Mechanism: The carrier is signed 64-bit integers, and `TryCongruentBoundary` treats `long.MinValue`/`long.MaxValue` as effective universe endpoints. Nevertheless, `Create` preserves an explicitly supplied `long.MinValue` lower bound and `long.MaxValue` upper bound rather than replacing them with null. Thus `Range(long.MinValue, long.MaxValue)` denotes the same long values as `Top`, but `LessThanOrEqual(Top, Range(long.MinValue, long.MaxValue))` returns false at lines 103-106 because `Top` has no stored lower bound. Equality and `AreEquivalent` also report distinct values. The redundancy occurs one-sided and after sequence reduction, such as `Create(Top, Range(0, long.MaxValue))` versus sequence `Top`.
- Impact: Public construction admits semantically duplicate abstract states. Joins, widening, havoc, and client equality can report an upward change or information loss when the represented set did not change. This primarily creates precision, stability, and API-consistency risk, can add needless fixed-point transitions, and makes semantic equivalence checks unreliable at carrier endpoints.
- Safe evidence: Both `IntervalValue.Top.Contains(x)` and `IntervalValue.Range(long.MinValue, long.MaxValue).Contains(x)` are true for every possible `long x`; `Create` leaves endpoint fields set, and line 103 rejects `Top <= explicit full range`. The canonical container test passed all 48 tests; generated samples contain the explicit full range but do not assert equivalence with `Top`.

## Wave 4.7. HIGH - Exceptional verifier exits lose or disable retained-supervisor cleanup authentication

- File and members: `SharpProof.BuildTasks/RunVerifier.cs`, `RunVerifier.Execute`, `TryTerminate`, and `ObserveCleanupAnchorAsync`, current lines 323-375, 694-718, and 939-946.
- Mechanism: `TryTerminate` may report success while a SIGTERM-sent supervisor remains alive, requiring a retained cleanup anchor. On cancellation, the retained anchor is created with a null authentication-failure callback, so a missing cleanup receipt is silently ignored. On other post-launch exceptions, the live-but-successful termination result suppresses anchor retention altogether and `finally` disposes the pidfd and process.
- Impact: Verifier descendants can outlive canceled or failed builds without a containment failure, defeating cleanup guarantees on exceptional paths.
- Safe evidence: Exercise an alive supervisor whose bounded wait expires after SIGTERM. Under `_canceled`, complete it without `SharpProof.Cleanup/1` and observe that authentication failure is skipped; under another injected post-start exception, observe that no anchor/receipt observer is retained.

# Read-Only Multi-Agent Bug Audit - Wave 5 - 2026-08-29

This section records 34 findings from exactly 30 fresh read-only auditors. The relay compiled the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 5.1. MEDIUM - KnownSymbols construction can crash the meta-analyzer on a legal ref-kind overload

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Member: `KnownSymbols` constructor
- Current lines: 515-522, especially `SingleOrDefault` at line 516
- Mechanism: The predicate identifies `WorkerVerifyAsync` by instance/static status, arity, return type, parameter count, names, and types, but never checks `IParameterSymbol.RefKind`. C# legally permits `VerifyAsync(WorkerVerifyRequest, CancellationToken)` alongside `VerifyAsync(WorkerVerifyRequest, ref CancellationToken)`; both have the same Roslyn parameter `Type` and satisfy the predicate. `SingleOrDefault` then throws `InvalidOperationException` during the compilation-start action, where line 64 creates `KnownSymbols`.
- Impact: Analyzer failure or AD0001 prevents SPMETA cancellation and soundness enforcement instead of merely declining the audited-boundary exemption.
- Safe evidence: `RefKind` is absent from lines 516-522, and `SingleOrDefault` throws when more than one element matches.

## Wave 5.2. LOW - Global validation hides a retired-mode error whenever any current option is invalid

- File: `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs`
- Member: `GetInvalidGlobalConfigurationValues`
- Current lines: 93-104, especially the early return at 93-96 before `TryGetRetiredMode` at 98-104
- Mechanism: After collecting invalid or conflicting `sharpproof_profile` or `sharpproof_features` entries, the method immediately returns when `builder.Count != 0`, so it never checks `sharpproof_mode` or `build_property.SharpProofMode` during that run. For example, `sharpproof_profile=everything` with `sharpproof_mode=effects` yields only the profile diagnostic; after fixing the profile, a second build reveals the retired-mode diagnostic.
- Impact: Configuration repair becomes needlessly iterative, and CI/editor output presents an incomplete set of invalid compilation-global settings. Analysis is already fail-closed.
- Safe evidence: Direct control flow at lines 93-104. `GetInvalidTreeConfigurationValues` at lines 147-180 does not early-return and appends the retired-mode diagnostic after current-option diagnostics. Existing tests at `AnalyzerModeAndEffectTests.cs` lines 185-209 and 239-259 cover each error only in isolation.

## Wave 5.3. HIGH - Compiler response evidence authority does not bind effect outcome, reason, or certainty

- File: `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`
- Member: `ValidateEffectClaim`
- Current lines: 194-243, especially 212-241
- Mechanism: The authority never binds `result.Outcome`, `Reason`, or `EffectCertainty` to the compiler effect evidence's `Outcome`, `Reason`, or `Certainty`. For nonvacuous results it branches only on the response's chosen outcome. A response can replace compiler `Unknown` or `Refuted` effect evidence with `Outcome=Proven`, `Reason=None`, `EffectCertainty=CompleteMayEffectSummary`, `Vacuity=None`, `EffectWitness=null`, `Model=[]`, and `ProofCore=["compiler-effect:" + evidence.EvidenceSha256]`. Lines 225-234 accept that proof core, and generic protocol validation accepts the effect tuple.
- Impact: A tampered or corrupt worker/cache response can turn unavailable or incomplete evidence, or even a definite compiler violation, into an accepted proof.
- Safe evidence: Direct control-flow inspection. A mutation test based on an `Unknown` or `Refuted` effect response can set these fields and exercise response-evidence and proof-core authority.

## Wave 5.4. HIGH - Constructor effect scanning uses an unsound pre-body order

- File: `SharpProof.Effects/EffectMethodNodeBuilder.cs`
- Members: `Build`, lines 49-82, especially 49-55; `ScanConstructorMemberInitializers`, lines 108-167
- Mechanism: `Build` scans instance member initializers first and, when they cannot complete normally, never scans the `IConstructorBodyOperation` root. Runtime evaluates the constructor initializer, including base/this arguments and the base or delegated-constructor call, before derived instance field/property initializers. If a base constructor writes static state and a derived instance initializer necessarily throws, the initializer `EffectStep` is noncompleting, so lines 54-82 skip `scanner.Scan(root)`. No `EffectCallSite` for the base constructor is recorded, and the complete summary omits the runtime static write and initializer-argument effects. Delegating `this(...)` constructors are also scanned as if they ran member initializers themselves.
- Impact: Complete effect summaries can omit effects that execute before a noncompleting derived initializer.
- Safe evidence: Direct control-flow and runtime-order reasoning.

## Wave 5.5. MEDIUM - AtomicFile ordinary writes reject valid long destination basenames

- File: `SharpProof.Ir/AtomicFile.cs`
- Members: `WriteUtf8`, lines 70-76; `WriteBytesAsync`, lines 92-105; `Prepare`, lines 115-121, especially line 121
- Mechanism: The staging component is `destination + "." + 32-character GUID + ".tmp"`, adding 37 ASCII characters to the destination basename. A valid 220-character basename plus `.sarif` produces a 263-character temporary component, exceeding the 255-character component limit on the canonical Linux filesystem and typical Windows filesystems. The write fails before publication even though the destination is valid.
- Impact: Compiler-manifest emission at `FinalCompilationCollector.cs` line 35, worker request/response writes, and verification-cache writes can fail solely because of a valid long output or cache filename.
- Safe evidence: `AtomicFileTests.cs` lines 78-96 uses 220 `s` characters plus `.sarif` to verify `PrepareStaged` uses a short stable temporary name, while `WriteUtf8`/async tests at lines 29-50 cover only short names. The private `Prepare` used by those APIs retains the suffixing strategy.

## Wave 5.6. MEDIUM - Reference-null conditional branches cannot instantiate against concrete reference substitutions

- File: `SharpProof.Specs/ApiSpecInstantiation.cs`
- Members: `Instantiation.Null(SpecNullDeclaration)`, lines 172-183; `Instantiation.Conditional`, lines 269-287
- Mechanism: A standalone `SpecNullDeclaration(Reference)` always materializes as `factory.Null(factory.ObjectType)` at line 177. `Conditional` independently instantiates each branch and directly calls `factory.Conditional`; it does not apply `Binary`'s peer-typed null adaptation at lines 248-266. `ApiSpecTermValidator.ValidateConditional` at lines 172-193 accepts branches based on coarse `IrTypeKind.Reference`, so a template `condition ? null : referenceVariable` is valid. If the variable is substituted with a factory-owned concrete reference type such as `Widget`, `IrFactory.Conditional` at lines 447-451 rejects exact types Object versus Widget; `Term` catches this and returns `InvalidExpression`.
- Impact: Otherwise valid custom or trusted reference postconditions fail instantiation and are silently not applied when the worker's `ApplySpec` returns null on failed status, losing proof facts and precision.
- Safe evidence: Direct source-path proof. Existing coverage exercises Boolean conditionals and reference null in binary equality, not this combination.

## Wave 5.7. HIGH - ExceptionConstructionThrow erases constructor may-effects while sequencing an explicit throw

- Files and members: `SharpProof.Effects/EffectSummaryOperations.cs`, `ExceptionConstructionThrow`, lines 56-69; used by `SharpProof.Effects/OperationEffectScanner.cs`, lines 756-781, for external exception construction without a proven nonthrowing specification.
- Mechanism: The helper discards `construction.Reads`, `Writes`, `Allocation`, and `Throws`, retaining only capabilities, completeness, and uncertainty, then replaces the throw set with the explicitly thrown exception. A trusted complete external constructor contract can state that it writes ambient or static state and may throw `ArgumentException`; analyzing `throw new ExternalException()` then returns a complete summary with empty writes/allocation and only `ExternalException`, although allocation and constructor writes occur and the constructor may throw `ArgumentException` before the explicit throw. Even for unmodeled constructors, it erases Unknown reads, writes, and allocation.
- Impact: Falsely complete summaries can omit allocation, state effects, and earlier constructor exceptions.
- Safe evidence: The helper's constructor arguments at lines 60-69 directly zero these fields and replace `construction.Throws`.

## Wave 5.8. HIGH - Partial-event accessor contracts disappear when callers resolve the definition accessor

- Files and members: `SharpProof.Contracts/ContractClauseInventoryBuilder.cs`, `GetPartialImplementation`, lines 263-279, `NormalizeCallable`, lines 327-337, and `HaveSameDefinition`/`GetPartialDefinition`, lines 339-359; `SharpProof.Contracts/EffectiveContractSourceResolver.cs`, `Resolve`, lines 49-58, and `ResolveCore`, line 71.
- Mechanism: Roslyn 4.14 exposes `IEventSymbol.PartialDefinitionPart` and `PartialImplementationPart`, but all three partial-member bridges special-case only `IPropertySymbol` after checking `IMethodSymbol` partial parts. Accessor `IMethodSymbol` partial links are not event links. A partial event's definition add/remove accessor is not normalized to the implementation accessor; `GetDeclaredBodies(definition accessor)` has no body, and `GetPartialImplementation` returns null. `HaveSameDefinition` cannot equate definition and implementation event accessors. References bind through the defining event symbol, so add/remove preconditions yield an empty inventory even when the implementing accessor begins with `Contract.Requires` or `Ensures`.
- Impact: Event subscription and unsubscription call-site verification can silently omit direct preconditions; resolution differs depending on definition versus implementation accessor.
- Safe evidence: Microsoft.CodeAnalysis 4.14 API documentation exposes event counterpart links; `ContractBinder` supports `EventAdd`/`EventRemove`, and repository parsing uses preview language.

## Wave 5.9. HIGH - Nonexhaustive switch expression is treated as having no normal path when an unmatched path exists

- File: `SharpProof.Effects/ManagedAbstractFlow.cs`
- Member: `DefiniteOperationFacts.MayCompleteSwitchExpression`
- Current lines: 2061-2082, especially 2069-2075
- Mechanism: After confirming that the governing value may complete, the method returns false whenever `SwitchExpressionFacts.HasReachableUnmatchedPath` is true, before checking reachable arms. For `static int Choose(int x) => x switch { 0 => 1 };`, the unmatched path throws while `x==0` returns normally.
- Impact: `MethodCanCompleteNormally` reports `Choose` false; `OperationCompletionEvaluator.CanCompleteInvocation` at lines 588-609 then treats every call as nonreturning, so downstream source-order effects in callers can be suppressed even on the `x==0` path.
- Safe evidence: Direct control-flow counterexample.

## Wave 5.10. HIGH - Null-to-nullable and user-defined null-to-struct conversions are falsely marked noncompleting

- File: `SharpProof.Effects/OperationCompletionEvaluator.cs`
- Member: `CanCompleteConversion`
- Current lines: 1011-1028, especially 1018-1023
- Mechanism: Any value-type result whose operand `ConstantValue` is null returns false. `(int?)null` and `default(int?)` complete normally; a user-defined `string? -> S` conversion may likewise accept null and return `S`.
- Impact: `ScanCallStep` gates the call phase on argument completion, so `MayThrow((int?)null)` loses the callee's effects and throws. A `finally` containing `int? x = null;` can be treated as noncompleting, blocking real successors.
- Safe evidence: Language semantics plus the exact predicate. Existing nullable-boxing tests exercise `(int?)null` effects but not completion and sequencing.

## Wave 5.11. MEDIUM - ApiSpecTable has no expression-depth bound before recursive processing

- Files and members: `SharpProof.Specs/ApiSpecTable.cs`, `CompileTemplate`, lines 128-140; `SharpProof.Specs/ApiSpecContentDigest.cs`, `Add(term, variables)`, lines 75-106; `SharpProof.Specs/ApiSpecInstantiation.cs`, `Term`, lines 136-151.
- Mechanism: `CompileTemplate` calls recursive `ApiSpecTermValidator.Validate` on caller-owned public term trees; digesting and instantiation also recursively walk every child. A sufficiently deep but otherwise valid nested conditional or unary chain exhausts the CLR stack; `StackOverflowException` is not recoverable by the surrounding `ArgumentException` catch.
- Impact: Generated or custom specification declarations can terminate the compiler or worker process during table creation or later instantiation instead of returning a validation or failure result.
- Safe evidence: Structural source trace; none of these paths has a depth counter or iterative traversal.

## Wave 5.12. HIGH - ApiSpecTermValidator repeatedly validates shared DAG children without a budget or memoization

- File: `SharpProof.Specs/ApiSpecTermValidator.cs`
- Members: Recursive dispatch, lines 68-75; `ValidateBinary`, lines 132-133; `ValidateConditional`, lines 177-179. Public entry: `SharpProof.Specs/ApiSpecTable.cs`, line 44, reaching the validator around line 131.
- Mechanism: Starting with `SpecBooleanDeclaration(true)` and repeatedly constructing `term = new SpecBinaryDeclaration(AndAlso, term, term, Boolean)` creates only N+1 objects but forces approximately 2^(N+1)-1 `Validate` calls because both shared child occurrences are recursively revalidated. A deep unary chain can also reach `StackOverflowException`.
- Impact: Untrusted or plugin-supplied custom API specifications can consume extreme CPU or terminate the host during table creation, before ordinary `ArgumentException` fail-closed behavior.
- Safe evidence: Exact recurrence at lines 132-133; no large reproduction was executed.

## Wave 5.13. HIGH - Instance member-initializer checking is seeded from only the first eligible constructor

- File: `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`
- Member: `AnalyzeMemberInitializer`
- Current lines: 440-481, which builds and sorts the constructor list, selects the first nongenerated/nonsuppressed candidate, and breaks; 500-512, which analyzes every initializer call with only that constructor and records only on it
- Mechanism: Instance initializers run for construction paths reaching type initialization, but the implementation does not join or analyze entry states from all eligible instance constructors. It chooses the lexically first eligible constructor. `RequiresCallSiteAnalyzer.AnalyzeInitializerCall` receives that constructor as caller, so its entry contract seeds reachability. A first constructor with contradictory requirements makes initializer entry bottom or vacuous even when a later constructor has a satisfiable entry and executes the same initializer.
- Impact: Reachable call-precondition violations can be missed, and semantic outcome is attached only to an arbitrary constructor.
- Safe evidence: A class has `int value = Guard.Positive(-1)`, a first parameterless constructor requiring false, and a reachable second constructor. The bad initializer executes through the second path while analysis uses only the impossible first entry. `RequiresAndControlTests.cs` lines 1540-1583 establishes that caller contracts seed flow; member-initializer tests do not cover differing constructor preconditions.

## Wave 5.14. LOW - Public abstention value objects accept undefined FrontendAbstention discriminants

- File: `SharpProof.Frontend/FrontendSubset.cs`
- Members: `FrontendSubsetClassification` constructor, lines 31-51, with decision validity switch at 35-40; `Abstain`, lines 70-72; `FrontendProgramAbstention` constructor, lines 103-122, with reason check at 114-118
- Mechanism: Both boundaries test only that abstention or reason is not `FrontendAbstention.None`; neither applies `Enum.IsDefined`. Undefined cast values construct successfully. The same classification constructor rejects undefined `FrontendSubsetDecision` values while admitting undefined reasons.
- Impact: Public value objects can claim closed abstention with a reason outside the documented finite enum. External serializers, displays, exhaustive switches, and aggregations receive corrupt state. Current production creation uses named values, so this is an API-invariant issue.
- Safe evidence: Direct constructor and factory control flow. `FrontendLoweringTests.cs` around lines 363-381 tests unknown decisions only.

## Wave 5.15. HIGH - Pure opaque identity aliases operandless operations with distinct semantic operands

- File: `SharpProof.Frontend/CompilerIdentityBridge.cs`
- Members: `InternOperation`, lines 38-42; `CreateSemanticOperationIdentity`, lines 124-137
- Mechanism: Identity contains operation kind, result type, and operator flags only. `ITypeOfOperation` and `ISizeOfOperation` carry distinguishing `TypeOperand` as an `ITypeSymbol`, not a child term; both are deemed pure by `RoslynOperationLowerer`, while normal lowering does not admit these compiler constants. Thus `typeof(int)` and `typeof(string)` intern the same zero-argument pure opaque member and term; likewise `sizeof(int)` and `sizeof(long)`, whose result type is int.
- Impact: Opaque-conservative reasoning can prove false equalities or transfer facts across distinct `typeof` or `sizeof` expressions.
- Safe evidence: Lowering two `ITypeOfOperation` nodes with different `TypeOperand` in one factory/lowerer yields identical opaque identity because `TypeOperand` is not projected; the same holds for `sizeof` under unsafe compilation.

## Wave 5.16. HIGH - Contract API trust hashes mutable FilePath contents rather than the metadata supplying accepted symbols

- File: `SharpProof.Frontend/ContractApiIdentityResolver.cs`
- Members: Constructor, lines 38-45; `HasTrustedAttributesPayload`, lines 188-204
- Mechanism: Candidate and assembly symbols come from `PortableExecutableReference` metadata, but trust is decided by reopening `reference.FilePath` and hashing its current contents. Roslyn references can retain cached metadata after path replacement; a custom `PortableExecutableReference` can expose the genuine Attributes DLL `FilePath` while returning different metadata. A forged same-name, version, and shape assembly supplies Contract or attribute symbols while the genuine path satisfies SHA-256.
- Impact: Attacker-controlled contract/effect attributes and clause methods can cross the trusted API boundary, enabling unsound proofs.
- Safe evidence: A custom PE reference whose `FilePath` points to genuine `SharpProof.Attributes` but whose metadata implementation returns forged same-identity, valid-shape metadata is accepted. A cached reference plus atomic file replacement is the filesystem variant.

## Wave 5.17. MEDIUM - Ambiguous shared SyntaxTree ownership silently chooses an arbitrary source compilation

- File: `SharpProof.Frontend/CompilationModelProvider.cs`
- Member: `FindOwningCompilation`
- Current lines: 33-56
- Mechanism: Depth-first search returns the first compilation whose `SyntaxTrees` contains the exact tree. A root can have two `CompilationReference`s built from the same `SyntaxTree` object but with different references or options; stack order selects the last-pushed branch without ambiguity detection.
- Impact: Callers can receive a `SemanticModel` with wrong bindings or options, leading to incorrect collection or analysis for source compilation references.
- Safe evidence: Two leaf compilations built from one shared tree with different references defining the same name differently, both referenced from a root, produce one arbitrary owner in `GetSemanticModel(root, sharedTree)`.

## Wave 5.18. HIGH - ContractFor companion and target cycles are accepted

- Files and members: `SharpProof.Analyzer.Core/ContractForValidation/ContractForValidationEngine.cs`, `ResolveCompanions`, lines 176-189; `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs`, `Validate`, lines 16-29. Downstream: `AnalyzerSession.IsContractCompanion`, lines 137-142; `AnalyzerFeaturePipeline`, lines 185-187.
- Mechanism: `ResolveCompanions` records every successfully parsed edge, including companion equals target and cycles. The validator checks companion shape and target/candidate surfaces but not distinctness or acyclicity. `[ContractFor(typeof(Self))] public static class Self { public static int M(int x) => x; }` is legal; target and candidate methods are the same symbols, so maps succeed. Two static classes can likewise target each other. `AnalyzerSession.IsContractCompanion` classifies a method solely because its containing type appears as a companion, and `AnalyzerFeaturePipeline` skips operation-block analysis.
- Impact: Actual executable static methods in self-cycles or mutual cycles evade implementation verification while their bodies are accepted as specifications; calls can resolve contracts through the cycle or self edge.
- Safe evidence: Deterministic trace: `ResolveCompanions` accepts the self descriptor; `FindOverlappingCompanions` excludes identical type at lines 135-140; `Validate` compares each method to itself; `IsContractCompanion` skips its operation block.

## Wave 5.19. MEDIUM - SupportsProperty over-requires ApiSpecs for an accessor the operation does not execute

- File: `SharpProof.Analyzer.Core/LanguageSubsetGate.cs`
- Member: `SupportsProperty`
- Current lines: 196-208, especially 205-208
- Mechanism: For any property on a generic containing type, the method collects both `GetMethod` and `SetMethod` and requires `hasResolvedGenericApiSpec` for every available accessor. A read executes only the getter; a simple write executes only the setter. A read with an exact getter specification but no setter specification, or a write with a setter specification but no getter specification, is therefore classified `UnsupportedOperationShape`.
- Impact: Selected otherwise-supported effects or contracts abstain, and compiler-artifact selection is marked unsupported, although downstream effect scanning is accessor-specific.
- Safe evidence: `SharpProof.Effects/OperationEffectScanner.cs` lines 328-330 explicitly selects the getter for `EffectAccess.Read` and setter for `Write`.

## Wave 5.20. HIGH - SMT encoding truncates string literals at embedded NUL

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Member: `QueryEncoder.Encode`, line 441; equality consumption at lines 503-505
- Mechanism: `IrStringTerm` literals are passed to `Context.MkString(string)`. Microsoft.Z3 4.12.2 managed `Context.MkString` calls native `Z3_mk_string`, whose C API accepts a NUL-terminated character pointer; native `api_seq.cpp` constructs `zstring` without a length. `IrFactory.String` rejects malformed UTF-16 but permits U+0000, and C# strings can contain `\0`. Distinct runtime strings such as `a\0b` and `a` encode to the same Z3 literal.
- Impact: A goal comparing those strings for equality becomes UNSAT after goal negation and is returned as proven, while `IrInterpreter` ordinal equality is false. SAT replay cannot catch an UNSAT false proof.
- Safe evidence: Z3 4.12.2 `Context.cs` lines 2254-2260 and `api_seq.cpp` lines 43-60 distinguish truncating `Z3_mk_string` from length-aware `Z3_mk_lstring`.

## Wave 5.21. HIGH - Response effect-evidence tuple validation rejects six accepted and produced effect states

- File: `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`
- Members: `WorkerProtocolMetadata.MatchesEffectCertainty`, lines 756-772; `MatchesEffectEvidenceTuple`, lines 773-781
- Mechanism: `MatchesEffectCertainty` accepts `Unknown+TrustedCompleteBoundary` for `EffectSummaryIncomplete` and `EffectContractNotEstablished`, and `Unknown+{IncompleteMayEffectSummary, TrustedCompleteBoundary}` for `ResourceLimit` and `UnsupportedBody`. `MatchesEffectEvidenceTuple` has rows only for `EffectSummaryIncomplete+Incomplete`, `EffectContractNotEstablished+Complete`, and `Unknown+Unavailable`, omitting all six accepted tuples. `ProtocolJson.ValidateClaimResult` invokes both, so the latter emits `response.effect_evidence` for otherwise supported results.
- Impact: Legitimate partial or trusted effect-analysis `Unknown` results become `worker.malformed_result` failed runs instead of semantic `Unknown`, breaking effect verification under resource limits, unsupported bodies, and trusted-summary uncertainty.
- Safe evidence: `EffectClaimResultAssembler.Assemble` passes valid effect-certainty tuples through; `CompilerEffectEvidenceCatalog.SupportedEffectTuples` explicitly includes the six states; `CompilerWireMappings` maps real `ResourceLimit` and `UnsupportedBody` analyzer outcomes. `ProtocolJsonTests.ResourceLimitIncompleteEffectTupleIsAProtocolState` tests only `HasValidEffectCertainty`, not whole-response validation.

## Wave 5.22. LOW - Public capability-set constructor admits invalid partial-Unknown values

- File: `SharpProof.Effects/EffectValues.cs`
- Member: `EffectCapabilitySet` constructor
- Current lines: 5-13; `IsUnknown` at 22-23
- Mechanism: Validation rejects only bits outside `EffectCapabilityKind.Unknown` value 16383, so the reserved unknown-marker bit alone and marker plus arbitrary known subsets are accepted. The enum defines `Unknown` as marker plus `AllKnown=8191`; analogous `EffectSummary` validation requires its unknown marker to equal full `Unknown`. The malformed set reports `IsUnknown=true` while `Kinds` is neither a defined capability combination nor full `Unknown`; `Union` and `IsSubsetOf` preserve and order these partial-unknown states as ordinary bitsets.
- Impact: The public value/domain API exposes invalid lattice elements and inconsistent unknown semantics. Downstream projection fails closed, making this a robustness and API-correctness issue rather than a false-negative analyzer issue.
- Safe evidence: Generated enum values and the constructor mask directly establish the accepted partial patterns.

## Wave 5.23. HIGH - Structural package-policy validation does not model evaluated MSBuild behavior

- File and member: `SharpProof.Gates/Performance/PerformanceGate.cs`, `ValidateAdvisoryPackagePolicy(XDocument portableProps, XDocument portableTargets, XDocument verifierProps, XDocument verifierTargets)`, current lines 1209-1451, and `HasAnalyzerItem`, lines 1460-1477.
- Mechanism: The validator inspects selected literal nodes in four unevaluated XML documents. It does not scan legal executable targets in either `.props`, does not reject or evaluate `Condition` on required Analyzer/Target/RunVerifier nodes, and does not inspect or evaluate `Import` elements. Expected literal nodes can therefore remain unchanged while direct props work, false child conditions, or imported projects add verifier/arbitrary work or disable the required analyzer/verifier wiring.
- Impact: The performance/release gate can certify advisory/default package policy and measure near-baseline work even though evaluated MSBuild behavior executes forbidden work or performs no SharpProof analysis/verification.
- Safe evidence: Independently append a `Target BeforeTargets=CoreCompile` with `Exec` to a props document, add `Condition="'1' == '0'"` to a required Analyzer or verifier node, or append an `Import` whose project mutates work/items. Each altered document set passes the structural validator while ordinary MSBuild evaluation changes behavior.

## Wave 5.24. HIGH - Implicit list-pattern indexer and slice arguments are discarded

- File: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`
- Members: `GetListPatternCalls`, lines 639-652; `CreateImplicitListPatternCall`, lines 662-671. Downstream: `RequiresCallSiteAnalyzer.GetActual`, lines 512-535, `GetArgument`, lines 584-611, `AnalyzeConcreteCall`, lines 415-419, and `AnalyzeAbstractCallSite`, lines 327-330.
- Mechanism: Discovery resolves each list-item indexer or slice member but always constructs `Arguments=[]` and empty `ExplicitArguments`, regardless of `method.Parameters`. Contract-parameter lookup finds no actual value, so concrete analysis returns null and abstract analysis returns `Unknown`. For a one-element custom list whose integer indexer requires index greater than zero, `value is [_]` invokes the getter with index 0 but supplies no 0 to analysis. Slice start and length arguments are likewise lost.
- Impact: Parameter-dependent contracts on explicitly discovered implicit list-pattern calls produce silent false negatives.
- Safe evidence: Existing list-pattern tests at `RequiresCallSiteDiscoveryTests.cs` lines 780-826 use `Requires(false)` only and do not exercise parameters.

## Wave 5.25. MEDIUM - nameof uses can make a dead local function look executable or escaped

- File: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`
- Members: `TryCollectLocalReferences`, lines 438-460; `IsAnonymousExecutableOrEscaped`, lines 500-522
- Mechanism: Every reachable `IMethodReferenceOperation` is treated as a reachability edge after `IsAnonymousExecutableOrEscaped`; there is no `INameOfOperation` exclusion. The latter returns true whenever the reference is not stored solely in a dead local. Thus `return nameof(Dead); int Dead() => Positive(-1);` marks `Dead` reachable and analyzes `Positive(-1)` although `nameof` is compile-time-only; writing `nameof(Dead)` similarly appears escaped.
- Impact: False SP0027 or `Refuted` outcomes arise from nonexecuting local-function bodies.
- Safe evidence: `RequiresCallSiteDiscovery.cs` lines 1558-1560 explicitly recognizes `INameOfOperation` for property calls, showing that the nonexecution case is handled there but absent for method references.

## Wave 5.26. HIGH - MIT provenance accepts arbitrary appended license restrictions

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Member: `ImportAsync`
- Current lines: 99-117 and 145-151
- Mechanism: The importer accepts upstream `LICENSE` when normalized text merely `StartsWith("The MIT License (MIT)")`. It never compares the complete reviewed MIT text or requires the standard grant, conditions, and disclaimer, then unconditionally records `LicenseSpdx="MIT"` and hashes and copies whatever full text follows the prefix.
- Impact: An upstream revision whose license begins with that header but replaces or appends incompatible terms is imported and represented as MIT, defeating the licensing and provenance gate and potentially checking in source under unreviewed restrictions.
- Safe evidence: A license beginning with the expected header followed by additional restrictions satisfies lines 111-113 and reaches source construction at 145-151.

## Wave 5.27. MEDIUM - Cache and concurrent replay entirely exclude the 200 OSS methods

- File: `SharpProof.Gates/Corpus/CorpusGate.cs`
- Members: `RunAsync`, lines 75-79; `VerifyCacheReplayAsync`, lines 397-435; `VerifyConcurrentReplayAsync`, lines 439-463
- Mechanism: OSS observations are made only once in `RunAsync`. Cache replay filters to `Origin==SyntheticMetamorphic` and `Variant==Baseline`; concurrent replay selects each variant only from `Origin==SyntheticMetamorphic`. No `OpenSourceCorpusRunner` observation participates in either comparison.
- Impact: Nondeterminism or state/cache contamination unique to large multi-tree OSS execution or custom recording can evade determinism checks or surface only as flaky one-shot snapshot behavior, while the overall corpus result reports cache and concurrent replay counts.
- Safe evidence: Every replay item is statically required to be `SyntheticMetamorphic`, while OSS cases have `OpenSource` origin. `CorpusGateTests` confirms 200 OSS methods but has no OSS replay assertion.

## Wave 5.28. MEDIUM - Malformed nonsource diagnostics can escape validation as NullReferenceException

- File: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`
- Member: `CompilerManifestArtifactJson.HasValidDiagnosticBinding`
- Current lines: 665-671
- Mechanism: For one otherwise valid diagnostic with `Location=None`, `IsSource=false`, `SourceTreeOrdinal=-1`, but JSON `sourceTreePath:null` or a null SHA field, `HasValidDiagnosticShapes` passes and canonical ordering of a one-element array performs no comparison. Lines 669-671 then dereference `Length`. `Deserialize` throws `NullReferenceException`, not `JsonException`.
- Impact: Launcher `RunMain` input-validation catch omits `NullReferenceException`, and `WorkerInputSnapshot.LoadAsync` translates only `JsonException`, `InvalidDataException`, and `DecoderFallbackException`, so a corrupt producer-supplied manifest can crash launcher or worker instead of yielding defined invalid-manifest behavior.
- Safe evidence: Static control-flow trace; no matching null regression test was found.

## Wave 5.29. MEDIUM - Runtime snapshot can bind a new deps file to components parsed from an old deps file

- File: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`
- Members: `WorkerBinaryIdentity.CreateSnapshot`, lines 59-60 and 68-70; `RuntimeComponents`, lines 193-244
- Mechanism: The method opens `.deps.json` once and derives components from that retained handle, then later reopens every component by pathname for staging and hashing. On Linux, atomic replacement between those steps leaves the open handle on old deps while `ReadAllBytes(component.Value)` stages and hashes replacement deps. `EnsureStagedComponentConsistency` compares the replacement path with its staged copy, never with the dependency stream used to derive components.
- Impact: An accepted and hash-sealed staged closure can omit dependencies declared by its staged deps or contain a stale component set, causing reproducible launch or load failure under concurrent deployment or path replacement.
- Safe evidence: Source ordering and Unix rename semantics. Existing mutation tests alter components only after a completed snapshot, not deps replacement during capture.

## Wave 5.30. MEDIUM - Canonical corpus snapshot ordering is not enforced

- File: `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs`
- Members: `Render`, lines 14-21; `Parse`, lines 69-74; `IsData`, lines 77-80
- Mechanism: `Render` and `Parse` accept every nonempty line not beginning with `#`; neither validates the declared sorted-diagnostics grammar or order. Downstream `LoadSnapshot` at lines 491-497 sorts diagnostics while loading, so reversing comma-separated diagnostics in a checked-in snapshot produces the same expectation and passes the corpus gate.
- Impact: Schema-3 snapshot bytes are not canonical despite the format and test contract; material checked-in evidence mutation is silently normalized rather than detected, weakening reproducibility and review.
- Safe evidence: `expected.canonical.snapshot` contains multi-diagnostic rows. Reversing one differs bytewise, and ordinal sorting restores the original. The existing format test covers exact schema bytes but not order.

## Wave 5.31. MEDIUM - Valid imported OSS filenames can make the generated snapshot impossible to reload

- Files and members: `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs`, `ValidateRelativePath`, lines 349-357; `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs`, `Render`, lines 14-21, and `IsData`, lines 77-80
- Mechanism: Path validation allows snapshot grammar characters such as comma, pipe, and LF. The runner uses the basename in canonical diagnostic locations; `CorpusObservation` emits diagnostics comma-separated and fields pipe-separated. `Render` performs no escaping or delimiter rejection. A selected method in `Foo,Bar.cs` writes a comma inside one diagnostic, but `LoadSnapshot` splits it into two; `|` makes the row structurally invalid, and LF injects a row.
- Impact: Importing an otherwise valid upstream repository can write a canonical-looking snapshot that the next corpus run rejects, blocking durable corpus updates.
- Safe evidence: The current manifest has no delimiter paths, but the predicates directly accept these characters. The fix boundary is an input constraint or encoding plus grammar enforcement.

## Wave 5.32. MEDIUM - Abnormal worker exit publishes and retains a Complete success generation before reconciliation

- File: `SharpProof.Worker.Launcher/Program.cs`
- Member: `Program.RunMain`
- Current lines: 138-184, especially publication at 155-160 and later nonzero-exit reconciliation at 172-184
- Mechanism: Any protocol-valid response is published before `exitCode` is reconciled. If the worker atomically writes valid `RunStatus=Complete` and then crashes or exits nonzero other than special code 124, `validResponse` is true and `resultExitCode` can be zero, so `PublishOutputs` commits result and SARIF. Only afterward does `RunMain` return nonzero. The invocation result remains `Complete`.
- Impact: The current build fails, but downstream or later consumers can read stable published `Complete` evidence that the launcher itself rejected because the worker terminated abnormally, violating result-as-commit-marker and fail-closed publication behavior.
- Safe evidence: The existing `RunMain` `runWorker` seam can write a correctly rebound valid baseline response and return 17. Current behavior is exit 17 with a published `Complete` result and SARIF.

## Wave 5.33. HIGH - Event-assignment receiver handling suppresses reachable handler and accessor effects

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanEventAssignment`
- Current lines: 31-87, especially receiver handling at 47-62 and accessor resolution at 64-87
- Mechanism: `receiverCheck.CompletesNormally` equals `_nullnessEvaluator.IsProvenNonNull`, so an unknown or maybe-null receiver is treated as terminal despite a reachable nonnull path. The scanner also returns for a definitely null receiver before scanning `HandlerValue`, although runtime evaluates receiver and handler before accessor invocation/dereference.
- Impact: Handler-expression calls, throws, allocations, and add/remove accessor effects can be omitted, allowing false complete, pure, no-throw, or no-write proofs.
- Safe evidence: (1) `t.Changed += MakeHandler()` with maybe-null `t` reaches both `MakeHandler` and `add_Changed` on its nonnull path. (2) `((Publisher)null!).E += BuildHandler()` still executes `BuildHandler` before throwing NRE. Both paths are suppressed by the current receiver check.

## Wave 5.34. HIGH - Compound or increment property/indexer setters lose the stored-value argument region

- Files and members: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanReadModifyWrite`, lines 129-134, and `ScanWriteTarget`, lines 19-23; context `SharpProof.Effects/OperationEffectScanner.cs`, `ScanProperty`, lines 345-357.
- Mechanism: Every read-modify-write calls `ScanWriteTarget(... valueIsStoredDirectly:false)`, which turns the assigned value into null for property or indexer setters. `ScanProperty` then never fills the setter's final value parameter region or actual argument; arrays retain Empty/null before `_callResolver`.
- Impact: Effects of a setter on its value parameter are instantiated against no caller region and disappear. For reference-valued overloaded `+=` or `++` results, including operators returning the original getter value, a setter that mutates the stored object can be summarized as not writing caller or receiver state, enabling an unsound effect contract.
- Safe evidence: Simple assignment takes the opposite path and explicitly fills both region and actual argument at `OperationEffectScanner.cs` lines 349-356. Existing `PropertyIncrementUsesBothAccessorsWithoutBecomingIncomplete` checks receiver-field getter/setter effects, not effects remapped through the setter value parameter.

# Read-Only Multi-Agent Bug Audit - Wave 6 - 2026-08-29

This section records 30 unique findings from exactly 30 fresh read-only auditors, including 11 zero-finding reports. The relay deduplicated one exact mechanism duplicate from 31 raw findings. The central writer did not inspect or reverify the code.

## Wave 6.1. MEDIUM - Explicit-interface implementations are unconditionally made unverifiable

- File: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`
- Member: `BuildTarget`
- Current lines: 85-92, especially the `MethodKind` allowlist at 89-91
- Mechanism: `supported` admits only `Ordinary` or `Constructor`; Roslyn classifies explicit interface members as `ExplicitInterfaceImplementation` even when their declarations and selected subset are otherwise supported. Every selected explicit implementation is forced to `IsVerifierSupported=false`.
- Impact: `CompilerCallableLowerer.Prepare` rejects it as `UnsupportedCallable` at lines 49-53, and effect evidence is downgraded to `Unknown/UnsupportedContract` at `ClaimManifestBuilder` lines 380-383. `LanguageSubsetGate` lines 136-145 and `ContractBinder` lines 74-84 explicitly support this method kind.
- Safe evidence: Compile an interface plus a class with explicit `IThing.M` containing valid `Contract.Ensures`; the manifest emits the target and claim, but the target is not verifier-supported and lowering returns `UnsupportedCallable`.

## Wave 6.2. LOW - Unrelated nested callable insertion renumbers downstream selected callable and claim IDs

- File: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`
- Member: `CreateCallableIds`
- Current lines: 545-559 and 579-585
- Mechanism: Local and anonymous functions share a containing-symbol group, syntax order, and zero-based ordinal; discovery includes unselected nested callables. Inserting an unselected earlier callable increments a selected sibling's ordinal, changing its `CallableId` and derivative `ClaimId`s without semantic changes.
- Impact: Baseline, cache, and cross-build correlation churn after unrelated edits.
- Safe evidence: Compare a contract-bearing local function before and after inserting an unused noncontract lambda earlier in the containing method; `CallableId` and `ClaimId` differ.

## Wave 6.3. MEDIUM - Int64 is excluded from source integer interval projection

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`
- Member: `IntegerInterval`
- Current lines: 267-270
- Mechanism: `semantics.BitWidth < 64` excludes `System_Int64` even though signed Int64 semantics and exact long bounds are available.
- Impact: Long receiver, parameter, and result variables lack CLR-domain assumptions; valid long-domain proofs can become `Unknown` or spurious over unconstrained mathematical integers.
- Safe evidence: The predicate excludes exactly 64-bit semantics; signed Int64 bounds fit `CompilerIntegerInterval`, while UInt64 is not supported.

## Wave 6.4. MEDIUM - Expression-bodied contract-only void methods are always UnsupportedBody

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`
- Members: `PrepareBody`, `ContainsOnlyContractStatements`
- Current lines: 121-125 and 576-590
- Mechanism: Every void method uses `ContainsOnlyContractStatements`, which requires `VerifierDeclaration.Body` and recognizes only block-body statements. A legal expression-bodied member such as `static void M(int x) => Contract.Requires(x > 0);` has a null block body despite inventory support for expression-bodied base method declarations.
- Impact: These callables abstain while equivalent block-bodied forms are admitted as trivial bodies.
- Safe evidence: Direct syntax and control-flow distinction.

## Wave 6.5. MEDIUM - Per-claim full-compilation source recapture causes superlinear collector work

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs`, `TryResolveSource`, lines 234-251, especially loop 246-251, and `TryCreate`, lines 21-29; caller `ClaimManifestBuilder.CreateEffectEvidence`, lines 391-402.
- Mechanism: For each replayable allocation claim, `TryResolveSource` loops over every compilation syntax tree and calls `CompilerCompilationCapture.CaptureTree`, including the witness tree again. `CaptureTree` enumerates every line, maps spans, hashes full text and line maps, and rebuilds arrays. The producer later captures all trees again.
- Impact: M replayable claims across total source size S repeat O(M*S) source processing and transient allocation, risking analyzer or build timeouts in allocation-heavy, multi-file projects.
- Safe evidence: Direct call graph; existing tests cover only a few claims in one tree.

## Wave 6.6. MEDIUM - Malformed long-branch displacement can escape invalid-image handling

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
- Members: `Translator.TryDecode`, current lines 888-899; `DecodedInstruction.BranchTarget`, lines 1728-1729; outer `TryBuild` exception filter, lines 242-249
- Mechanism: `BranchTarget` performs `checked(NextOffset + (int)Operand)` before bounds validation. A malformed Int32 displacement can overflow, and `TryBuild` does not catch `OverflowException`.
- Impact: An otherwise loadable malformed reference can throw out of the collector rather than returning `InvalidImage/UnsupportedIl`, causing build-time denial of service.
- Safe evidence: A long branch with `NextOffset` 5 and displacement 2,147,483,647 necessarily overflows checked Int32 addition.

## Wave 6.7. LOW - Relational summary cache conflates closed forms nested inside generic outer types

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`
- Members: `IsSourceCandidate`, current lines 283-300, especially 290-292; `Normalize`, lines 308-312; `TryGet` cache, lines 93-100
- Mechanism: Generic exclusion checks only `method.ContainingType.TypeParameters.IsEmpty`; a nongeneric nested type within a generic outer passes. `Normalize(...).OriginalDefinition` collapses constructed forms to one cache key, while lowering creates distinct `IrMemberId`s. The first cached form makes another closed outer instantiation mismatch and return false; a failed attempt can similarly poison `_failed`.
- Impact: Exactly lowerable calls using two closed outer instantiations can abstain or fail depending on call order.
- Safe evidence: Use generic `Outer<T>` with nongeneric `Inner` and static scalar `F`; invoke `Outer<int>.Inner.F` and `Outer<long>.Inner.F` and require both summaries to prepare.

## Wave 6.8. MEDIUM - Blank specification-pack entries are silently discarded instead of failing closed

- File: `SharpProof.CompilerCollector/FinalCompilationCollector.cs`
- Member: `ParseSpecificationPacks`
- Current lines: 95-103, especially `Split` with `RemoveEmptyEntries` at 95-98
- Mechanism: Values such as `dotnet.scalar;`, `;dotnet.scalar`, doubled separators, or whitespace-only segments are accepted and serialized as though the blank ID were absent, making later blank validation ineffective.
- Impact: Malformed compiler-visible authority configuration emits a valid manifest rather than SP0049 and no artifact, concealing truncation or interpolation mistakes and violating documented fail-closed semantics.
- Safe evidence: Leading, trailing, doubled, and whitespace-only internal segments should produce SP0049; an entirely empty or unset property remains the no-packs default. `docs/analysis-limits.md` line 31 states that blank IDs fail closed.

## Wave 6.9. MEDIUM - Result-domain assumptions do not rewrite returns through available spec projections

- Files and members: `SharpProof.Worker/PostconditionObligationBuilder.cs`, `TryAddSourceDomainAssumptions`, current lines 32-46, especially 39-46; `CallableEvidenceBuilder.Build`, lines 153-160 and domain check at 195-198; `PostconditionObligationBuilder.IsSupportedProofDomain`, lines 114-117.
- Mechanism: For primitive-integer Result, the interval predicate uses raw `path.ReturnTerm`; projection rewrite is applied only to the path guard, not the interval predicate or return term. An integer return derived from a projected spec result, such as `Array.Empty<int>().Length`, retains the sequence variable although a valid length proxy exists, then proof-domain validation rejects sequence or reference variables.
- Impact: Otherwise-supported postconditions become `Unknown/UnsupportedExpression`, defeating existing spec cardinality and nullness projections.
- Safe evidence: `static int EmptyLength() { Contract.Ensures(Contract.Result<int>() == 0); return System.Array.Empty<int>().Length; }`. The catalog marks `Array.Empty` NonNull and Empty; the synthesized range predicate retains the sequence variable.

## Wave 6.10. MEDIUM - Interrupted cache transactions escape recovery and the configured byte cap

- File: `SharpProof.Worker/VerificationCache.cs`
- Members: `TryWriteAsync`, current lines 149-166; `TryStageCapacity`, lines 245-285; `DiscardStaged`, lines 288-295; `IsOwnedCacheEntry`, lines 367-374
- Mechanism: Exact-key replacement renames the old entry to `<cache>.{guid}.rollback`; eviction renames victims to `.eviction`. Cleanup is later and best-effort. After a crash or kill, future scans enumerate `*.sharp-proof-cache.json` and accept only exact canonical filenames, so transaction artifacts are never counted, recovered, or cleaned.
- Impact: Rollback can create a permanent cache miss; repeated interrupted transactions can exceed `MaximumBytes` without bound and retain stale counterexample payloads indefinitely.
- Safe evidence: Seed rollback or eviction artifacts, or interrupt after rename, instantiate a new cache, and assert recovery/cleanup and total transaction-owned bytes within `MaximumBytes`.

## Wave 6.11. HIGH - Sequential modeled calls can reuse one IR target and conflate distinct results

- File: `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs`
- Members: `Run.ExecuteBlock`, current lines 139-172, especially `environment.SetItem` at 165-168; `Run.ApplySpec`, lines 370-410
- Mechanism: Each modeled call result is instantiated as `factory.Variable(call.Target.Value)`, and postconditions are permanently appended. A later modeled call writing the same `IrVarId` is neither rejected nor versioned, so prior postconditions constrain the later result. Digest-valid lowered-artifact validation does not require call-target uniqueness.
- Impact: Proof soundness can fail: an invalid contract may be reported `Proven`.
- Safe evidence: Two `String.Length` specification calls on different receivers target the same `v`, producing assumptions `v == Length(s1)` and `v == Length(s2)`. Returning `v` after the second call can falsely prove equality with `Length(s1)` although runtime returns `Length(s2)`. Compiler IR normally uses fresh temporaries, but crafted validated artifacts can reach this path.

## Wave 6.12. MEDIUM - Branch conditions omit normal-evaluation constraints

- File: `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs`
- Member: `Run.TransferBranch`
- Current lines: 225-258, especially 229-250; contrast `ConstrainNormalExecution` call sites at 128-136, 346-367, and 432-455
- Mechanism: The executor substitutes a compound Boolean condition and directly forms `predicate && condition` and `predicate && !condition`, never adding successful-evaluation witnesses. A digest-valid graph can place a partial term such as `(1 / divisor) > 0` directly in a branch; the decoder checks type and control flow but not totality or variable-only form.
- Impact: SMT-totalized evaluation treats throwing executions as normal, causing spurious obligations or counterexamples and failure to prove contracts valid for all normally returning executions.
- Safe evidence: For `divisor == 0`, totalized division chooses a branch and enters the normal return-path disjunction; a contract such as `ensures divisor != 0` can fail despite the execution throwing.

## Wave 6.13. HIGH - IrFactory.Cast accepts invalid scalar, nonreference, and null-to-nonnullable casts

- File: `SharpProof.Ir/IrFactory.cs`
- Member: `Cast`
- Current lines: 465-485
- Mechanism: After ownership and identity/nullable folding, every remaining source and target pair is interned. Construction does not require reference-like source and target types for nonidentity casts, and null's special path bypasses general source-kind validation for nonnullable scalar targets.
- Impact: Invalid IR crosses a central public factory invariant and reaches consumers, where it is only later classified `Unsupported` or `InvalidCast`.
- Safe evidence: `f.Cast(f.BooleanType, f.Integer(1))`, an int-to-object cast, and `f.Cast(f.IntegerType, f.Null(f.StringType))` all succeed.

## Wave 6.15. LOW - Existing type lookup interns a discarded display name

- File: `SharpProof.Ir/IrFactory.cs`
- Member: `GetOrCreateTypeCore`
- Current lines: 656-667
- Mechanism: `InternStringCore(name)` runs before `_typeIds.TryGetValue`. Requesting an existing semantic identity under a different name returns the original type while allocating the ignored new string. Identity-bearing sequence types behave likewise.
- Impact: Ignored `GetOrCreate` inputs observably change `IrStringId` allocation and consume memory.
- Safe evidence: Create identity type `Widget`, request the same identity as `Gadget`, and then call `InternString("Gadget")`; it reveals that `Gadget` was already allocated.

## Wave 6.16. MEDIUM - String semantic-identity ban is bypassable through generic widening

- File: `SharpProof.Ir/IrFactory.cs`
- Member: `InternExternalIdentity<T>`
- Current lines: 55-64
- Mechanism: Rejection tests `typeof(T) == typeof(string)` instead of the runtime value. `object key = "semantic-key"; f.InternExternalIdentity(key, EqualityComparer<object>.Default)` succeeds while the inferred-string call throws.
- Impact: Acceptance of prohibited string semantic identities depends on call-site static typing, violating the factory's explicit identity rule.
- Safe evidence: Direct public generic call; the runtime check would need to reject `identity is string`.

## Wave 6.17. MEDIUM - Caller code executes under the factory-wide lock

- File: `SharpProof.Ir/IrFactory.cs`
- Members: `CreateSequenceValue`, current lines 279-287; `InternExternalIdentity`, lines 66-75; `ExternalIdentityKey.GetHashCode`, lines 750-754
- Mechanism: `CreateSequenceValue` holds `_gate` while materializing a caller-provided `IEnumerable`, and external identity interning invokes caller comparer hashing or equality under the same lock.
- Impact: A legal callback waiting on another thread that calls the factory can deadlock; slow or infinite enumeration blocks all factory work; recursive comparers can exhaust the stack.
- Safe evidence: An enumerable `MoveNext` that coordinates with another factory-calling thread demonstrates the lock inversion. Inputs should be materialized and foreign comparer callbacks avoided outside the global lock.

## Wave 6.18. MEDIUM - Null unboxing casts are classified InvalidCast rather than NullReference

- File: `SharpProof.Ir/IrInterpreter.cs`
- Member: `EvaluateCast`
- Current lines: 391-425, especially null-target logic at 404-413
- Mechanism: When an `ObjectType` value of kind Null is cast to Integer or Boolean, the nonnullable-target branch unconditionally returns `Fault(InvalidCast)`. Valid C# unboxing such as `(long)(object)null!` or `(bool)(object)null!` throws `NullReferenceException`; `InvalidCastException` applies to nonnull boxed values of the wrong type.
- Impact: Interpreter semantics diverge from C#; `IrCSharpDifferentialOracle` reports `Mismatch`, and exception-sensitive consumers receive false evidence.
- Safe evidence: Bind an `ObjectType` variable to `CreateNullValue(ObjectType)`, cast to `IntegerType`, and evaluate. Current result is Exception/InvalidCast while compiled C# throws `NullReferenceException`.

## Wave 6.19. MEDIUM - Pure opaque identity conflates `as` conversions with throwing reference casts

- Files and members: `SharpProof.Frontend/RoslynOperationLowerer.cs`, `VisitConversion`, current lines 786-835, exact string-only path 826-832 and fallback 834-835, plus `Opaque`/`IsDemonstrablyPure`, lines 279-357; `SharpProof.Frontend/CompilerIdentityProjections.generated.cs`, lines 12-22; `CompilerIdentityBridge.CreateSemanticOperationIdentity`, lines 124-137.
- Mechanism: Nonstring reference conversions such as `obj as Widget` and `(Widget)obj` both fall to pure opaque lowering. Semantic identity records checked, lifted, and other metadata but omits `IConversionOperation.IsTryCast` or conversion flavor. With identical operand and result types, both intern the same term although one returns null and the other throws on incompatible input.
- Impact: Fallback terms erase definedness and value differences and spuriously correlate expressions for consumers retaining abstained terms. Exact-contract exposure is limited by `ConversionMayChangeValue` classification.
- Safe evidence: Lower both descendants of `static bool Target(object value) => (value as C) == (C)value;` through one factory and compare opaque term IDs.

## Wave 6.20. MEDIUM - Noncatalog const fields of the same type collapse to one pure opaque term

- Files and members: `SharpProof.Frontend/RoslynOperationLowerer.cs`, `VisitFieldReference`, lines 461-466, `DefaultVisit`, lines 433-446, `Opaque`, lines 284-307, and `IsDemonstrablyPure`, lines 319-323; `CompilerIdentityBridge.CreateSemanticOperationIdentity`, lines 124-137.
- Mechanism: Constant fields outside narrow catalog integer boundaries go through `DefaultVisit` with no arguments and without passing `operation.Field`. `ConstantValue` makes them demonstrably pure, while structural identity contains neither value nor field symbol or occurrence. Same-type constants such as `const long One=1, Two=2` therefore intern the same `PureOpaque` term.
- Impact: Returned fallback terms falsely correlate distinct values. Exact-only binding limits accepted-proof exposure, but frontend and program consumers retaining abstained terms can observe spurious equality.
- Safe evidence: Direct lowering and interning trace.

## Wave 6.21. HIGH - Calls fail to havoc locals captured and mutated by local functions or closures

- File: `SharpProof.Frontend/RoslynProgramLowerer.cs`
- Location: Current lines 291-303
- Mechanism: Call-mutated IR variables are derived only from explicit ref/out arguments. An impure zero-argument closure call emits memory-only havoc and remains `Exact`, but captured locals are ordinary IR variables rather than modeled memory locations.
- Impact: Lowered results are unsound. `long x=0; void Mutate(){x=1;} Mutate(); return x;` continues to read IR variable 0 although C# returns 1.
- Safe evidence: Direct CFG and lowering trace. Captured-local variables must be included in `VariablesAndMemory` havoc or such invocations conservatively abstained.

## Wave 6.22. LOW - Malformed nonempty contract JSON escapes the contract-invalid exception path

- File: `SharpProof.Host/ContainerContract.cs`
- Members: `ValidateRequired`, lines 74-75; `ReadBoundedJson`, lines 173-190, especially `JsonDocument.Parse` at line 186
- Mechanism: Malformed nonempty JSON throws `JsonException` directly; a valid nonobject root can make `TryGetProperty` throw `InvalidOperationException`. Neither is normalized to the type's contract-specific `InvalidDataException`.
- Impact: Callers cannot consistently classify corrupt markers. Worker `Program` startup handling at lines 31-37 does not catch `JsonException`, so corruption between launcher preflight and startup, or direct invocation, can terminate unhandled instead of exiting 125. Normal launcher preflight limits severity.
- Safe evidence: Payload `{` deterministically throws `JsonException`; existing tests cover empty and valid-but-invalid-property payloads, not malformed or nonobject roots.

## Wave 6.23. MEDIUM - ResetPublicationSet decides marker state before acquiring publication locks

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Members: `ResetPublicationSet`, current lines 183-205, especially marker checks at 189-200 before `AcquirePublicationSet` at 202-205; `BindPublicationSet`, lines 469-525
- Mechanism: A publisher creates markers sequentially while holding locks, but concurrent reset examines marker and output state before locking. It can see zero markers and no outputs, return success, and then publication completes; or it can see a partial set and throw immediately instead of waiting for the lock timeout.
- Impact: Reset or clean is nonatomic with publication and can falsely report success while publication survives or fail spuriously during legitimate publication.
- Safe evidence: Direct ordering and sequential marker creation.

## Wave 6.25. HIGH - RequireLocalPath accepts many remote or shared filesystem types

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Members: `UnsupportedRemoteFileSystems`, lines 44-48; `RequireLocalPath`, lines 111-121; `FindFileSystemType`, lines 730-765
- Mechanism: Locality is inferred solely by excluding `cifs`, `nfs`, `nfs4`, `smb3`, `sshfs`, and `fuse.sshfs`. Other remote or shared types such as `ceph`, `glusterfs`, `lustre`, `9p`, `afs`, or `fuse.rclone` pass unchanged.
- Impact: Publication proceeds while assuming local flock, rename, and durability semantics on unsupported shared storage.
- Safe evidence: `bestType` is returned verbatim, and acceptance depends only on absence from the short blacklist.

## Wave 6.26. MEDIUM - Malformed numeric diagnostic fields can throw FormatException out of TryDeserialize

- File: `SharpProof.Host/VerifierDiagnosticTransport.cs`
- Location: Current lines 65, 74-75, 81-83
- Mechanism: `JsonElement.GetInt32` for schema, line, or column throws `FormatException` for fractional or Int32-out-of-range numeric values; the catch filter includes `OverflowException` but not `FormatException`.
- Impact: A structured-looking malformed stderr line can abort diagnostic processing and the MSBuild task because `RunVerifier.LogStandardError` does not catch it.
- Safe evidence: In the canonical tooling container, `GetInt32` on 2147483648 throws `FormatException`. Schema, line, and column cases require regression coverage.

## Wave 6.27. LOW - Analyzer semantic outcome keys collapse distinct methods in different syntax trees

- File: `SharpProof.Gates/AnalyzerGateHost.cs`
- Location: Current lines 314-331
- Mechanism: `MethodOutcomeKey` contains only `MetadataName`, `DeclaredAccessibility`, and the first in-source `SourceSpan.Start`; it omits syntax-tree or file identity, containing symbol, and signature. Same-name and accessibility methods at the same offset in different trees collide and their outcomes are combined.
- Impact: A multi-tree `Compilation` can produce a false combined outcome, omit a method, or report an incorrect target count. Current direct `CorpusGate` replay is single-tree, limiting present exposure; the `Compilation` overload is unconstrained.
- Safe evidence: `OpenSourceCorpusRunner`'s multi-tree recorder keys by `SyntaxTree` plus `SourceStart`, demonstrating that tree identity is needed.

## Wave 6.28. HIGH - Interface or base-typed cache receivers bypass cache-soundness analysis

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `AnalyzeWrite`, current lines 16-28, especially 18-22; `AnalyzeAssignment`, lines 30-43, especially 33-37; `IsCacheType`, lines 51-54
- Mechanism: Analysis is gated by whether the static receiver or containing type's simple `Name` contains `Cache`. A real cache referenced through an interface or base type without that substring is skipped before the unsafe value is examined; property and indexer writes have the same gap.
- Impact: Routine abstraction or refactoring removes the error-level invariant and permits Unknown or failure answers into an actual cache.
- Safe evidence: `IAnswerStore store = proofCache; store.Write(Answer.Unknown);` has receiver and containing type `IAnswerStore` and returns before value analysis.

## Wave 6.29. HIGH - Helper return analysis loses unsafe answers behind aliases and compound expressions

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `ResolveProperty`/`ResolveInvocation`, current lines 281-295; `GetReturnedValueNames`, lines 297-327; `IsNonCacheableName`, lines 350-358
- Mechanism: Returned-value extraction recognizes only a top-level member access, identifier, or object creation, returning raw names without resolving identifier definitions or recursively analyzing conditional, switch, coalesce, or invocation expressions.
- Impact: Trivial helper extraction or aliasing bypasses SPMETA010 and allows transient or abstaining facts to be cached.
- Safe evidence: `Answer Resolve(){ var x=Answer.Unknown; return x; } cache.Write(Resolve());` yields name `x`, treated safe. `Answer Resolve(bool b) => b ? Answer.Unknown : Answer.Proven;` yields no names, and method name `Resolve` is treated safe.

## Wave 6.30. HIGH - WorkerVerifyResponse lies outside the semantic-answer cache predicate

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `IsSemanticAnswerType`
- Current lines: 329-338; context `VerificationCache.TryWriteAsync`, lines 123-139, and `SharpProofWorker` guard, lines 324-332
- Mechanism: The predicate recognizes SharpProof type names containing `Answer`, `Result`, or `Outcome`; `WorkerVerifyResponse` is excluded. Writes of `WorkerVerifyResponse` therefore cannot produce SPMETA010 even when they contain `TimedOut`, `Failed`, or `Unknown` results.
- Impact: The non-cacheable-answer invariant is not enforced at the principal persistent cache boundary. The current `SharpProofWorker` guard limits immediate exposure, but deleting, weakening, or bypassing it would evade the analyzer.
- Safe evidence: `VerificationCache.TryWriteAsync` accepts and serializes `WorkerVerifyResponse`, whose name fails the predicate.

# Read-Only Multi-Agent Bug Audit - Wave 7 - 2026-08-29

This section records 38 findings from exactly 30 fresh read-only auditors: 20 reported findings and 10 reported none. The relay reported no exact title/mechanism duplicates. The central writer did not inspect or reverify the code.

## Wave 7.1. HIGH - Instance-call composition omits the mandatory nonnull receiver guard

- File: `SharpProof.Summaries/IrRelationalSummaryBuilder.cs`
- Member: `Run.ApplyCall`
- Current lines: 408-420 and 449-476, especially receiver handling at 408-420
- Mechanism: The receiver passes only through `ConstrainNormalExecution`; `IrSemanticTerms.RequiresDefinednessWitness` treats `IrVariableTerm` and `IrNullTerm` as needing no witness, so receiver-expression evaluation is checked but instance dispatch never requires receiver not equal to null. If the callee summary does not mention its receiver, its instantiated relation admits null and Effects can remain None.
- Impact: A caller summary can claim normal completion, a result, and no throw for a C# instance call that must throw `NullReferenceException`, enabling unsound downstream proofs.
- Safe evidence: `InstanceSummaryInstantiationSubstitutesTheReceiver` at `IrRelationalSummaryTests.cs` lines 482-521 uses a constant-return instance summary; composing it with an explicit null or nullable receiver adds no receiver constraint.

## Wave 7.2. HIGH - Runtime throw-operand types can make reachable catches disappear

- Files and members: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions`, current lines 174-218, especially 193-217, consumed by `CanKnownReach`, lines 2743-2764; contrast `SharpProof.Effects/EffectExceptionFlow.cs`, `ResolveThrownException`, lines 17-32.
- Mechanism: Potential exceptions are derived only from the operand's static exception type. A maybe-null operand does not add `NullReferenceException`, and a base-typed operand does not preserve possible runtime subtypes; `CanKnownReach` therefore rejects catches that are reachable for those runtime values.
- Impact: Handler writes, capabilities, and throws are unsoundly omitted for direct throws and source callees containing them.
- Safe evidence: (1) `void M(InvalidOperationException? e) { try { throw e; } catch (NullReferenceException) { Mutate(); } }` reaches the catch when `e` is null. (2) An `Exception e` holding `InvalidOperationException`, thrown inside a matching subtype catch, reaches that catch at runtime although the analysis retains only `Exception`.

## Wave 7.3. MEDIUM - Unreachable bare rethrows cause false escaping exceptions

- File: `SharpProof.Effects/EffectExceptionFlow.cs`
- Members: `ApplyCatches`/`ContainsRethrow`
- Current lines: 137-160 and 277-286
- Mechanism: `ContainsRethrow` is purely syntactic, so it marks a catch as rethrowing even when `throw;` is unreachable after return or a proven diverging call. `ApplyCatches` then preserves the protected exception whenever the catch is selected; `ManagedAbstractFlow` reachability is not consulted.
- Impact: False Throws effects or incompleteness and downstream false diagnostics.
- Safe evidence: `catch (E) { return; throw; }` or an unreachable rethrow after a diverging call. Existing tests around lines 5092 and 5327 cover handler writes, not the escaping throw set.

## Wave 7.6. MEDIUM - Effect replay geometry accepts cross-tree provenance splicing

- File: `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs`
- Member: `HasValidReplayGeometry`
- Current lines: 65-91, especially 65-75 and 80-91
- Mechanism: The validator independently binds syntax-tree ordinal, hashes, and range to one tree and source-tree ordinal, location, hashes, and path to another, but never requires equal ordinals or maps Location against the syntax tree. Equal numeric offsets permit operation provenance from tree A with mapped source authority from tree B.
- Impact: Malformed or tampered replay evidence passes validation while operation and source provenance refer to different physical trees, undermining source attribution.
- Safe evidence: Use two valid trees with distinct mapped paths at the same start and length; combine syntax hashes and snapshot from tree 0 with source path, line-map hash, and location from tree 1. Geometry returns true.

## Wave 7.7. HIGH - Scalar ref-local writes and compound reads are silently erased

- Files and members: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanWriteTarget`, current lines 24-29, and `ScanReadModifyWrite`, lines 110-134; `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCoreOperation`, lines 234-246.
- Mechanism: Every `ILocalReferenceOperation` target returns `EffectSummary.Empty`, including locals with `RefKind.Ref`. Simple `r = 1` records no pointee write; `r += 1` records neither target read nor write. Parameter ref/out arms emit region effects, but ref locals have no analogous handling.
- Impact: Methods mutating caller storage through ref locals can be summarized as complete with no argument effects, enabling unsound contract acceptance and propagation.
- Safe evidence: `void M(ref int p) { ref int r = ref p; r = 1; }` and the compound variant.

## Wave 7.8. MEDIUM - Ref reassignment of a ref parameter is misreported as caller-storage mutation

- File: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`
- Members: `ScanSimpleAssignment`, current lines 36-50; `ScanWriteTarget`, lines 24-27
- Mechanism: `ISimpleAssignmentOperation` exposes `IsRef`, but `ScanSimpleAssignment` never checks it and sends every target to `ScanWriteTarget`. `x = ref y` selects the ref-parameter write arm even though it only rebinds the callee managed-reference variable.
- Impact: False `WritesArgumentState` and rejection of valid no-write contracts.
- Safe evidence: `void Rebind(ref int x, ref int y) { x = ref y; }`; no `IsRef` branch exists in the assignment path.

## Wave 7.9. MEDIUM - EffectSummary domain equivalence ignores AnalysisIncompleteReason

- File: `SharpProof.Effects/EffectSummary.cs`
- Member: `EffectSummaryDomain.LessThanOrEqual`/`AreEquivalent`
- Current lines: 160-188, predicate at 175-182; `Join` at 206-217
- Mechanism: The order compares every component except `AnalysisIncompleteReason`, although `Join` unions it and diagnostics consume it. Otherwise identical incomplete summaries with `BlockBudgetExceeded` versus `CallPreconditionNotProven` compare less-than-or-equal both ways.
- Impact: Fixpoint or worklist clients can suppress a reason-only state change and lose the correct incompleteness explanation.
- Safe evidence: Construct the two internal summaries; `AreEquivalent` returns true while record inequality and `Join` preserve distinct or combined reasons.

## Wave 7.10. MEDIUM - Multiple module initializers are aggregated without execution ordering

- File: `SharpProof.Effects/EffectAnalysisSession.cs`
- Members: `Analyze`, current lines 91-101; `AnalyzeAll`, lines 118-139
- Mechanism: Entry initialization is joined into every source method, including each module initializer. `SummarizeBeforeEntry` skips only the initializer being analyzed and includes all siblings regardless of execution order; the aggregate is then unconditionally joined with its body.
- Impact: Earlier initializer summaries gain later initializer effects; if an earlier initializer definitely throws, later initializer body effects remain despite being unreachable, producing false throws, writes, and capabilities.
- Safe evidence: With two `ModuleInitializer` methods, one empty and one doing a static write, analyzing the empty one necessarily receives the sibling write. Tests cover only one initializer.

## Wave 7.11. HIGH - Delegate arguments do not havoc captured-local facts

- File: `SharpProof.Effects/ManagedAbstractFlow.cs`
- Members: `TransferCore` invocation, current lines 277-281; `HavocCall`/`HavocArguments`, lines 834-854; anonymous-body skip at 229-230; related `ManagedMutationFacts.HasMutation`, lines 5-18
- Mechanism: Flow forgets all facts only when the call target itself is a local function or delegate; ordinary methods and constructors havoc only ref/out arguments. A by-value delegate argument may synchronously mutate captured caller locals, but those facts persist.
- Impact: Stale nullness, scalar, or cardinality facts can suppress real exceptions and effects.
- Safe evidence: `string? x="ok"; Invoke(() => x=null); _=x.Length; static void Invoke(Action a)=>a();`; `x` remains proven nonnull although `Invoke` mutates it.

## Wave 7.12. MEDIUM - Catch arms count as normal paths without checking filter or exception-type feasibility

- Files and members: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteTry`, current lines 155-166; `DefiniteOperationFacts.TryMayCompleteNormally` in `SharpProof.Effects/ManagedAbstractFlow.cs`, lines 2084-2095.
- Mechanism: Both predicates OR in a catch whenever its filter expression and handler can complete. They do not require the filter to be capable of evaluating true or an exception from the protected body to be compatible with the catch type. Consequently both a literal-false filter and an unrelated sealed catch type create fictitious normal-completion paths.
- Impact: A genuinely nonreturning method is classified as completing, so callers retain unreachable suffix writes, calls, allocations, and exceptions and can receive false effect diagnostics or witnesses.
- Safe evidence: `try { throw new Exception(); } catch (Exception) when (false) { } Mutate();` never reaches `Mutate`. Likewise, a try that always throws sealed `A` followed only by `catch (B)` for unrelated sealed `B` always propagates, yet both predicates return true.

## Wave 7.13. MEDIUM - Definitely out-of-range array access is considered normally completing

- Files and members: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteArrayElement`, current lines 733-737; existing bounds reasoning in `SharpProof.Effects/ManagedAbstractFlow.cs`, `ProvesArrayAccess`, lines 1300-1309.
- Mechanism: Completion checks receiver, indices, and nullness but never proves bounds. Even exact empty-array index 0 is considered completing although effect scanning records `IndexOutOfRangeException`.
- Impact: Effects after a provably terminal access are retained, causing false summaries and contract diagnostics.
- Safe evidence: `_ = (new int[0])[0]; Mutate();`; `Mutate` cannot execute but remains reachable.

## Wave 7.14. HIGH - DecodeBody accepts coordinated same-typed parameter permutations

- File: `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`
- Member: `DecodeBody`
- Current lines: 601-635, source and target resolution at 616-621, checks at 622-629
- Mechanism: Each row's `SourceOrdinal` is artifact-controlled and compared only with the chosen target parameter's `Ordinal`; `SourceName` is checked only for a `Parameter:` prefix. For same-typed parameters, swapping both Target and `SourceOrdinal` across rows preserves uniqueness and type checks. Recomputed `FeatureScopeSha256` and canonical serialization have no independent compiler authority to recover the true mapping.
- Impact: Executor and replay use the corrupted substitution dictionary, so semantics can be permuted and an invalid contract become `Proven`.
- Safe evidence: For `int Pick(int left,int right) { Ensures(Result<int>()==right); return left; }`, swap the two binding targets plus `SourceOrdinal` and reseal. Body source `left` substitutes to canonical `right`, making the false postcondition tautological.

## Wave 7.15. HIGH - Abstract Requires evaluation assumes unrelated casts of definitely-string actuals succeed

- File: `SharpProof.Analyzer.Core/ManagedContractFacts.cs`
- Member: `EvaluateCast`
- Current lines: 105-120, especially 111-116; reached from `RequiresCallSiteAnalyzer.AnalyzeAbstractCallSite`, lines 357-371; concrete replay disabled for casts at 383-389
- Mechanism: `definitelyStrings` derives from actual value and type, and `EvaluateCast` returns the operand unchanged for every `IrCastTerm` on such a variable without checking `cast.Type`.
- Impact: An incompatible throwing cast can make a precondition evaluate `Proven`, unsoundly certifying a call.
- Safe evidence: `Need(object value) { Contract.Requires((IDisposable)value != null); } Call()=>Need("text");`; string does not implement `IDisposable`, so runtime precondition throws `InvalidCastException`, while abstract evaluation treats the cast as nonnull.

## Wave 7.16. HIGH - Call scanning null-checks the receiver before evaluating arguments

- File: `SharpProof.Effects/OperationEffectScanner.cs`
- Member: `ScanCallStep`
- Current lines: 619-648, especially receiver/null path at 629-639 and arguments at 641-648
- Mechanism: The scanner evaluates the instance, short-circuits on `PotentialNullReceiver`, and only then scans arguments. C# evaluates the receiver and all arguments before callvirt dereference.
- Impact: For a proven-null receiver, argument writes, calls, and throws are omitted even though they execute before NRE.
- Safe evidence: `((C)null!).M(Mutate())` or a null indexer receiver. `ExceptionHandlerReachability` lines 221-239 correctly treats arguments as prerequisites.

## Wave 7.18. HIGH - User-defined conditional binary scanning omits op_True and op_False calls

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanBinary`
- Current lines: 175-205, especially 199-202; contrast `OperationCompletionEvaluator`, lines 1034-1065
- Mechanism: User-defined `&&` and `||` invoke a truth operator after the left operand to decide whether to evaluate the right, but the scanner resolves only `binary.OperatorMethod`, namely `op_BitwiseAnd` or `op_BitwiseOr`, never the distinct `op_False` or `op_True`.
- Impact: Writes, throws, divergence, and capabilities of truth operators are absent from effect summaries.
- Safe evidence: The completion evaluator explicitly locates these truth operators as separate calls.

## Wave 7.19. HIGH - Branching expressions are scanned as unconditional linear child sequences

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Members: `ScanBinary`, current lines 175-205; `ScanCoreOperationTail`/`ScanDefault`, lines 335-383; `OperationEffectScanner.ScanSequence`, lines 952-963
- Mechanism: Binary RHS effects are scanned before short-circuit classification, while conditional and coalesce operations have no specialized branch-aware case and fall back to linear child scanning. This adds effects from unreachable branches; for an unknown conditional, a noncompleting first arm can also stop the sequence before its reachable sibling is scanned.
- Impact: Reachable effects can be omitted and impossible writes, calls, or throws can be added, causing both unsound summaries and false effect diagnostics.
- Safe evidence: `false && Mutate()` and `true || Mutate()` wrongly include the RHS; `new object() ?? Mutate()` wrongly includes `WhenNull`; and `b ? AlwaysThrows() : Mutate()` can omit `Mutate`. The completion and exception-reachability components already perform the required branch selection/joining.

## Wave 7.22. HIGH - Terminal construction paths can omit object-initializer effects

- File: `SharpProof.Effects/OperationEffectScanner.cs`
- Members: `ScanObjectCreation`, current lines 706-742, especially 733-740; `ScanThrow`, lines 756-781; completion source `OperationCompletionEvaluator.CanCompleteConstruction`, lines 901-914
- Mechanism: The special external-exception construction path never scans `creation.Initializer`. On the normal creation path, the constructor step uses completion for the whole creation including the initializer; a noncompleting initializer therefore makes the constructor step terminal before the scanner visits that initializer.
- Impact: Initializer arguments, setters, writes, capabilities, throws, and nontermination can be omitted, enabling false purity, no-write, or no-throw summaries.
- Safe evidence: `throw new Exception { Source = Mutate() };` omits `Mutate` and the setter on the special throw path; `new C { P = Boom() }`, where `Boom` writes and throws, makes construction noncompleting and skips `Boom` and the setter on the normal path.

## Wave 7.23. HIGH - Interpolation resolves nonstring holes to the wrong formatting method

- Files and members: `SharpProof.Effects/StringConcatenationEffectResolver.cs`, `ResolveFormattedValueCall`/`ResolveToString`, current lines 107-131 and 158-203; used by `OperationEffectScanner.Expressions.cs`, lines 250-266.
- Mechanism: Every nonstring interpolation hole is modeled as parameterless virtual `ToString`. Runtime interpolation uses `IFormattable.ToString(null, provider)` when supported. A sealed type can have a pure parameterless `ToString` but an explicit `IFormattable` implementation with writes or throws.
- Impact: Omitted effects enable unsound purity or `DoesNotThrow` proofs and can fail to suppress later-hole effects.
- Safe evidence: A sealed `F : IFormattable` has a pure override `ToString` and an explicit `IFormattable.ToString` that writes or throws; `$"{f}{Later()}"` binds `F.ToString` in the resolver while runtime calls the explicit interface method.

## Wave 7.24. MEDIUM - User-defined string `+` is always assigned a managed-allocation effect

- File: `SharpProof.Effects/StringConcatenationEffectResolver.cs`
- Member: `Resolve`
- Current lines: 18-30
- Mechanism: The resolver creates and returns an allocation summary before checking `OperatorMethod`, so every user-defined `+` returning string gets an allocation even if the operator returns a cached or interned string; operator call effects are separately resolved.
- Impact: False `Allocates` and failed zero-allocation proofs.
- Safe evidence: A user-defined operator returns a static cached string; line 29 alone marks allocation.

## Wave 7.25. HIGH - Earlier using resources are not unwound on a later initializer's mixed exceptional path

- File: `SharpProof.Effects/UsingDisposalEffectResolver.cs`
- Member: `ResolveResources`
- Current lines: 263-284
- Mechanism: Earlier acquired resources are marked for unwind only when a later initializer cannot complete normally. An initializer that can both return and throw leaves `acquisitionFailed=false`. If the body has no reachable exit, `scopeExitReachable=false` and the resolver returns Empty, ignoring the exceptional acquisition path that disposes earlier resources.
- Impact: `Dispose` writes and throws, and catch reachability, are omitted.
- Safe evidence: `using (R first=r, second=MaybeThrow(flag)) { while(true){} }`; `MaybeThrow` returns when false and throws when true. Runtime disposes `first` on the true path, but the resolver reports no disposal.

## Wave 7.26. HIGH - Source-nullness fallback remains stale after indirect local mutation

- File: `SharpProof.Effects/OperationNullnessEvaluator.cs`
- Member: `IsSourceDefinitelyNull`
- Current lines: 42-100, especially textual invalidation at 76-98; consumed by `ScanCallStep`
- Mechanism: Textual invalidation recognizes only direct writes and ref arguments to the original local before the use. It misses writes through ref-local aliases and mutations inside an invoked local function whose body appears later. The initial-null fallback therefore remains true, and `IsProvenNull` ORs it with abstract flow so correct nonnull flow cannot override it.
- Impact: A now-nonnull receiver is deemed null, an invented NRE stops scanning, and real receiver effects are omitted.
- Safe evidence: (1) `object? x=null; ref object? alias=ref x; alias=new Effectful(); x.ToString();`. (2) `C? x=null; Set(); x.Touch(); void Set()=>x=new C();`. Both mutate `x` before use without matching the fallback's direct-write test.

## Wave 7.28. HIGH - Mixed fresh and caller-owned CFG captures resolve as unconditionally fresh

- File: `SharpProof.Effects/CreationFlowCaptures.cs`
- Members: `Record`, current lines 8-33, especially 16-19 and 23-32; `TryResolve`, lines 35-46. Consumer: `ConversionOwnershipClassifier.ClassifyRegion`, lines 40-50.
- Mechanism: One `CaptureId` can be defined on conditional branches by both creation and a caller-owned value. `Record` tracks creation definitions but ignores noncreation definitions, so the capture resolves Fresh regardless of visitation order.
- Impact: Writes through the merged receiver can omit caller-owned mutation and make an impure method appear pure.
- Safe evidence: `(b ? new Box() : p).Set()` merges both values into one capture; current ownership retains only Fresh, dropping `Parameter(1)`.

## Wave 7.29. MEDIUM - Present boxed nullable round-trips are falsely classified InvalidCast

- File: `SharpProof.Effects/ConversionEffectClassifier.cs`
- Members: `ClassifyUnboxing`, current lines 160-191; `HasExactPreservedRuntimeType`, lines 193-224, especially 220-223
- Mechanism: Boxing a present `T?` produces a box of underlying `T`, but exact-type recognition compares source `T?` directly with the unboxing target or underlying `T`. With a proven nonnull operand, the classifier adds `InvalidCastException`.
- Impact: Normally returning methods get a complete but incorrect exception summary, defeating `DoesNotThrow` and effect proofs.
- Safe evidence: `int? x=1; return (int)(object)x;` returns 1; source `int?` versus target `int` equality fails, and the classifier reports `InvalidCast`.

## Wave 7.30. MEDIUM - Checked context invents intrinsic overflow for ordinary user-defined conversions

- File: `SharpProof.Effects/ConversionEffectClassifier.cs`
- Members: `Classify`, current lines 38-41; `ClassifyNullableAndCheckedConversion`, lines 237-250; `CheckedOverflow`, lines 96-105
- Mechanism: Every user-defined conversion is routed through `CheckedOverflow(operation.IsChecked)`. If no checked operator exists, checked context invokes the ordinary user operator and adds no built-in numeric-overflow semantics; operator effects are already scanned separately.
- Impact: A nonthrowing resolved conversion gains a spurious `OverflowException` and fails valid `DoesNotThrow` or effect claims.
- Safe evidence: With ordinary explicit `operator int(S)=>0`, `checked((int)s)` returns 0 when no checked operator exists, but the classifier cannot prove an interval and adds overflow.

## Wave 7.31. MEDIUM - BindRequires is poisoned by placement errors in non-Requires clauses

- Files and members: `SharpProof.Contracts/ContractBinder.cs`, `BindCore`, current lines 100-102, requires-only filtering at 113 and 186-189; `SharpProof.Contracts/EffectiveContractSourceResolver.cs`, lines 72-81.
- Mechanism: The resolver collapses any invalid direct-clause placement into `InvalidClausePlacement` when a valid direct clause exists; `BindCore` returns that failure before `BindInvocations` applies requires-only kind filtering.
- Impact: An unrelated malformed `Ensures` or `Assume` prevents extraction and call-site enforcement of a valid precondition.
- Safe evidence: A valid `Requires`, then an ordinary statement, then a late `Ensures`; `BindRequires` returns `InvalidClausePlacement` although requires-only binding intentionally ignores unsupported Ensures or Assume content.

## Wave 7.32. HIGH - Source-defined ModuleInitializerAttribute disables module-initializer discovery

- File: `SharpProof.Effects/EffectModuleInitialization.cs`
- Members: Constructor, current lines 13-20; `Discover` early return at 26-29
- Mechanism: The well-known attribute is retained only when its `ContainingAssembly` differs from `compilation.Assembly`. On targets lacking the BCL attribute, a standard source polyfill has the recognized metadata name but is nulled here, so `Discover` returns empty.
- Impact: Actual module-initializer writes, allocations, capabilities, nontermination, and throws are omitted before source entry points.
- Safe evidence: Compile against references lacking BCL `ModuleInitializerAttribute`, define the standard attribute in source, and annotate a static throwing or writing method. Roslyn emits it, `GetTypeByMetadataName` finds the source symbol, and lines 15-20 null it.

## Wave 7.33. MEDIUM - Constructor suppression is bypassed for member initializers through an unsuppressed delegating constructor

- File: `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`
- Member: `AnalyzeMemberInitializer`
- Current lines: 439-482; analysis and recording at 500-512
- Mechanism: The pipeline treats every instance constructor as a possible initializer owner and selects the first nonsuppressed candidate without excluding `this(...)`-delegating constructors. Initializers execute only in the nondelegating root constructor.
- Impact: False Requires diagnostics and false outcomes are attributed to a delegating constructor when the actual root execution is suppressed.
- Safe evidence: An initializer calls a failing Guard; the suppressed parameterless root exists with unsuppressed `Subject(int):this()`. C# runs the initializer only in the suppressed root, but the loop analyzes it under the delegating constructor.

## Wave 7.34. MEDIUM - Only one malformed EffectContract attribute is diagnosed per callable

- Files and members: `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs`, `ValidateArguments`, current lines 20-27; `ExternalEffectResolver.ResolveContract`, lines 76-89; generated resolution model, lines 176-180.
- Mechanism: `ValidateArguments` resolves once and reports only scalar `InvalidAttribute`. The resolver returns immediately on the first decode failure or inconsistent duplicate. The attribute allows multiple instances, so later independently invalid attributes are never validated or reported.
- Impact: Incomplete diagnostics and an iterative repair loop; tooling cannot enumerate all invalid declarations in one run.
- Safe evidence: Two `EffectContract` attributes with distinct invalid effect bits; selection includes both, but validation emits at most one SP0024.

## Wave 7.35. LOW - Property-level rejected control attributes are reported once per accessor

- Files and members: `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs`, `ValidateDeclaredScope`, current lines 19-44; `AnalyzerFeaturePipeline.ValidateMethodAttributes`; `ContractSelectionInventory.GetCallableAttributes`.
- Mechanism: Type and assembly rejected controls deduplicate by attribute syntax, but property attributes are surfaced for each accessor and method validation deduplicates by `IMethodSymbol`. An auto-property get and set therefore report identical SP0047 at one property attribute.
- Impact: Duplicate diagnostics and a count varying by accessor count for one invalid usage.
- Safe evidence: A source-shadow rejected `SharpProofSuppressAttribute` allowed on Property is applied to a get/set auto-property; both accessor actions report. Expected behavior is one diagnostic per attribute syntax.

## Wave 7.36. MEDIUM - Invocation-writable paths are not checked against compiler outputs before invalidation or run

- Files and members: `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, `Execute`, current lines 135-150 and 185-194; launcher writes and deletes request/result in `SharpProof.Worker.Launcher/Program.cs`, lines 71-74 and 122-149.
- Mechanism: `inputPaths` includes `InvocationRequestPath`, `InvocationResultPath`, and `ManifestPath`, but `aliasesCompilerOutput` compares only publication paths plus publication-marker paths with `CompilerOutputPaths`. Invocation paths may equal or hard-link compiler output without error.
- Impact: The launcher can overwrite or delete a compiler-owned assembly, PDB, or other output.
- Safe evidence: Set a compiler-owned regular file as `InvocationResultPath` with all other paths distinct; no comparison catches it and the task succeeds. Existing tests cover only publication members, confirming invocation omission.

## Wave 7.37. LOW - Canonical corpus snapshots accept noncanonical numeric or whitespace enum spellings

- Files and members: `SharpProof.Gates/Corpus/CorpusGate.cs`, `LoadSnapshot`, current lines 476-485; `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs`, `Parse`, lines 69-79.
- Mechanism: `Enum.TryParse(ignoreCase:false)` accepts decimal aliases such as `0` and surrounding whitespace instead of only canonical names emitted by `ToCanonicalLine`.
- Impact: The exact render-canonical snapshot and reproducible-baseline invariant is not enforced; numeric baselines silently depend on enum ordinal ordering.
- Safe evidence: Replacing `Proven` with `0` or a padded spelling yields the same enum and the gate can pass although corpus printing emits different bytes. Exact `Enum.GetName` plus `IsDefined`, or byte-for-byte rerender validation, is needed.

## Wave 7.38. LOW - Nonfinite JSON limits can disable performance checks

- Files and members: `SharpProof.Gates/Performance/AcceptancePerformanceContract.cs`, `Load`, current lines 30-49; `PerformanceGate.ValidateContract`, lines 1153-1175; threshold comparisons at 182-232 and 306-318.
- Mechanism: JSON number `1e400` becomes `PositiveInfinity` through `GetDouble`; validation checks only `<= 0`, so infinity passes. Infinite maximum ratios make every finite-regression comparison false.
- Impact: Malformed or tampered acceptance data can remove effective performance bounds while validation says limits are positive.
- Safe evidence: `GetDouble` on `1e400` returns Infinity, `IsFinite` is false, and `>0` is true. Every double contract field must require `double.IsFinite`.

# Read-Only Multi-Agent Bug Audit - Wave 8 - 2026-08-29

This section records 19 unique findings from exactly 30 fresh read-only auditors: 14 auditors reported 20 raw findings and 16 reported none. The relay removed one exact duplicate of Wave 5.7 before forwarding. The central writer did not inspect or reverify the code.

## Wave 8.1. MEDIUM - Existing publication rollback snapshots are read into memory without a size bound

- File: `SharpProof.Worker.Launcher/Program.cs`
- Member: `CapturePreviousPublication`
- Current lines: 571-593, especially 576-580
- Mechanism: `File.ReadAllBytes` materializes every preexisting publication member. Paths are caller-selected and prior contents are not protocol-bounded; all payloads remain in a `Dictionary<string, byte[]>`.
- Impact: Large stale or hostile regular files at publication destinations can cause proportional allocations and OOM before staging or commit, denying verification and publication.
- Safe evidence: Source semantics; modest sparse files show heap and working-set scaling. Artifact caps or bounded streaming backups to temporary files are needed.

## Wave 8.2. MEDIUM - Referenced-type namespace traversal ignores cancellation and uses unbounded recursion

- File: `SharpProof.Frontend/ReferencedTypeSymbols.cs`
- Member: Private `GetAll`
- Current lines: 41-52, especially 46-48; cancellation occurs only inside the type loop at line 33
- Mechanism: Namespace descent recursively calls `GetAll` without checking the token. A precanceled token is ignored through empty or type-free trees, and deeply nested namespaces consume the call stack before any type or cancellation check.
- Impact: Discovery can be unresponsive or terminate with `StackOverflowException` on deeply nested generated or metadata namespaces; the cancellation contract is violated.
- Safe evidence: A canceled token plus only empty namespaces completes normally; a deep type-free chain recurses. Cancellation should be checked on descent or traversal made iterative.

## Wave 8.3. MEDIUM - SARIF URI projection corrupts valid relative compiler-mapped paths

- File: `SharpProof.Worker.Launcher/SarifProjection.cs`
- Member: `LocationUri`
- Current lines: 179-183; consumed by `Result`, lines 148-152
- Mechanism: Nonabsolute paths only replace backslashes, without URI escaping or a base declaration. Valid mapped names may contain reserved characters or be relative; `mapped#source.cs` is interpreted as file `mapped` plus a fragment, and ordinary relative paths resolve against the SARIF location rather than project or source base.
- Impact: SARIF consumers can attach proof or refutation to the wrong file or fail source lookup.
- Safe evidence: Resolve emitted `mapped#source.cs` against `file:///artifact/result.sarif`; `LocalPath` is `/artifact/mapped` and Fragment is `#source.cs`, not the intended filename.

## Wave 8.4. MEDIUM - Authentic in-memory SharpProof.Attributes references are rejected

- File: `SharpProof.Frontend/ContractApiIdentityResolver.cs`
- Member: `HasTrustedAttributesPayload`
- Current lines: 188-201
- Mechanism: The sole matching `PortableExecutableReference` must have a nonempty `FilePath`. `MetadataReference.CreateFromImage` for identical authentic DLL bytes has `FilePath=null`, so `Contract` resolves null and genuine contract and effect attributes are rejected.
- Impact: Legitimate in-memory Roslyn hosts cannot use SharpProof contracts or effects and instead abstain or diagnose rejected API.
- Safe evidence: The same `SharpProof.Attributes.dll` resolves `Contract=true` through `CreateFromFile` and false after reading the identical bytes and using `CreateFromImage`.

## Wave 8.5. MEDIUM - Lowercase build-property spelling of retired sharpproof_mode is silently accepted

- File: `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs`
- Member: `TryGetRetiredMode`
- Current lines: 183-192, especially 187-190; normal resolver spellings at 199-203
- Mechanism: Retired-option detection recognizes `sharpproof_mode` and `build_property.SharpProofMode` but omits `build_property.sharpproof_mode`. Case-sensitive `AnalyzerConfigOptions` providers therefore miss it.
- Impact: Global and tree validation emit no SP0025, and analysis proceeds with defaults instead of failing closed on the removed option.
- Safe evidence: An ordinal dictionary containing only `build_property.sharpproof_mode=everything` should produce Off plus one removed-option invalid value, but current code returns no retired-mode error.

## Wave 8.6. MEDIUM - Catch-filter exclusion prover rejects multiple forms that provably exclude cancellation

- File and members: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`, `AnalyzeCatchClause`, `FilterExcludesCancellation`, `PatternExcludesCancellation`, and `Unwrap`, current lines 14-42 and 169-265.
- Mechanism: The exclusion prover accepts constant false and a narrow set of whole-filter pattern shapes. It does not handle classic `IIsTypeOperation`, logical negation, null constant patterns, or Boolean composition around an otherwise recognized exclusion. Each omitted form can make a handler unreachable for every `OperationCanceledException`, but the analyzer treats it as capable of swallowing cancellation.
- Impact: Error-severity SPMETA003 blocks safe builds and forces source rewrites or suppressions even though cancellation necessarily propagates.
- Safe reproduction/evidence: Each of these safe filters is falsely diagnosed: `caught is ArgumentException`; `!(caught is OperationCanceledException)`; `caught is null`; and `caught is not OperationCanceledException && Include(caught)`.

## Wave 8.7. MEDIUM - Generic exception identity omits assembly identity of type arguments

- Files and members: `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs`, `Encode`, current lines 5-15; `CompilerIdentityBridge.CreateTypeDisplay`/`TypeReference`, lines 151-156 and 171-175; `ClaimManifestBuilder`, lines 373-376.
- Mechanism: Identity prefixes only the outer generic exception's assembly and then appends the Roslyn documentation reference ID. Embedded type-argument IDs omit their defining assembly, so constructed types with same metadata-named arguments from different assemblies collide.
- Impact: Distinct allowed-exception constraints collapse in manifest identity and hash, and evidence cannot identify which constructed exception was analyzed.
- Safe evidence: Use one `GenericBoomException<T>` plus extern-aliased `Collision.Payload` types from distinct assembly identities; the constructed symbols differ but `Encode` returns equal strings.

## Wave 8.8. HIGH - Indirect dispatch bypasses SPMETA001 forbidden-API checks

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `Initialize`, current lines 62-71; `AnalyzeInvocation`, lines 90-101
- Mechanism: The analyzer registers only `OperationKind.Invocation` and checks `IInvocationOperation.TargetMethod`. A captured `IMethodReferenceOperation` is unseen and its delegate call targets `Invoke`; dynamic receiver calls are `IDynamicInvocationOperation`/`OperationKind.DynamicInvocation`, for which no action is registered and no target is inspected.
- Impact: Soundness-critical forbidden APIs can execute without SPMETA001, bypassing semantic-model and other enforced boundaries.
- Safe evidence: Both `Func<SymbolDisplayFormat?,string> f = symbol.ToDisplayString; return f(null);` and `dynamic c=compilation; c.ReplaceSyntaxTree(oldTree,newTree);` execute forbidden APIs while direct calls diagnose.

## Wave 8.9. MEDIUM - SPMETA005 has a namespace-wide self-exemption unrelated to generated catalog identity

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Member: `AnalyzeObjectCreation`
- Current lines: 132-141, especially 136-138; generated-code exclusion at line 61
- Mechanism: Any `DiagnosticDescriptor` construction in namespace exactly `SharpProof.Meta.Analyzers` is exempt regardless of containing type, file, or generated status.
- Impact: Production code can declare that namespace and bypass stable IDs, help links, and catalog generation; handwritten descriptors within the meta-analyzer namespace are unchecked.
- Safe evidence: Handwritten `namespace SharpProof.Meta.Analyzers; static class Rogue { static readonly DiagnosticDescriptor Rule = new(...); }` yields no SPMETA005. Generated code is already separately excluded.

## Wave 8.11. MEDIUM - Repository-origin allowlist normalization removes `.git` anywhere in the URL

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Members: `ImportAsync`, current lines 84-97; `NormalizeRepositoryUrl`, lines 427-438
- Mechanism: `Replace(".git", "", OrdinalIgnoreCase)` deletes every occurrence, not only a terminal suffix.
- Impact: A clean checkout of an unapproved lookalike repository can normalize to an approved origin and supply corpus source and license.
- Safe evidence: `https://github.com/aalhour/C-Sharp-.gitAlgorithms` normalizes to approved `https://github.com/aalhour/C-Sharp-Algorithms`.

## Wave 8.12. MEDIUM - Manual support review is carried across commits by a context-free method hash

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Members: `LoadExistingSupport`, current lines 211-227; reuse at 152-161; declaration hash from catalog `GetDeclaration`, lines 85-92
- Mechanism: Existing support is indexed only by `DeclarationSha256`; the declaration excludes containing type, namespace, usings, path, and source commit. Identical text moved or copied into a different semantic context inherits `Supported` or `IntentionallyUnsupported` without review.
- Impact: Support classification and coverage can silently become false after upstream updates, undermining the review barrier.
- Safe evidence: Identical method-declaration text in differing enclosing contexts yields the same reuse key. The expected verdict is reobserved, but support status is not.

## Wave 8.13. MEDIUM - Forced-termination probe can attach to a reused worker PID

- File: `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs`
- Members: `MeasureForcedTerminationCoreAsync`, current lines 217-229; `WaitForProcessExit`, lines 388-411
- Mechanism: A fake worker publishes only a numeric PID. The probe first waits for the launcher to exit, by which time the worker is reaped and its PID freed, and then calls `Process.GetProcessById`. Under process churn, the PID may belong to an unrelated process; the catch handles only an absent PID, not reuse.
- Impact: False forced-termination gate failure and about 10 seconds of budget consumption although the probed worker exited correctly.
- Safe evidence: The ordering is explicit in the members. A process handle or verified start identity must be retained while the worker is live.

## Wave 8.14. MEDIUM - Nested Contract.Result is falsely diagnosed as a return-type signature mismatch

- Files and members: `SharpProof.Contracts/ContractIntrinsicValidator.cs`, `Classify`, current lines 60-69; `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `ReportInvalidIntrinsics`, lines 652-671; `AnalyzerDiagnosticCatalog.DescribeIntrinsicViolation`, lines 13-29.
- Mechanism: `Result` inside `Old` is rejected because `context.InsideOld` but collapsed to `InvalidIntrinsicSignature`. The reporter infers intrinsic identity only from Old-specific failure kinds, so it labels the intrinsic as Result and the catalog states that the result type mismatches even when it matches. The catalog's `InvalidIntrinsicSignature/isOld` arm becomes unreachable.
- Impact: SP0024 gives advice that cannot fix the source and loses the actual forbidden-nesting cause.
- Safe evidence: In an int method, `Ensures(Old(Result<int>()) == value)` has a matching type, yet the diagnostic claims otherwise.

## Wave 8.15. MEDIUM - Atomic replacement discards destination filesystem metadata and access mode

- File: `SharpProof.Ir/AtomicFile.cs`
- Members: `PublishStaged`, current lines 43-52, with `File.Replace` at 47; private `Publish`, lines 123-132, especially 127; staging creation at 32-40, 75, and 98-103
- Mechanism: The temporary file is a fresh inode with umask-derived mode; `File.Replace` installs it over the destination. No destination Unix mode, ACL, or extended metadata is copied.
- Impact: Rewriting a restricted 0600 output can widen it to 0644 under umask 022 or otherwise alter administrator-selected policy.
- Safe evidence: Precreate a 0600 destination, call `WriteUtf8` or staged publish, and compare `GetUnixFileMode`; no mode-preservation code exists.

## Wave 8.16. MEDIUM - Symbol validator applies portable-PDB ID rules to unauthenticated CodeView records

- File: `scripts/SharpProof.SymbolPackageValidator.cs`
- Member: `ValidatePair`
- Current lines: 195-205 and ID construction at 247-251
- Mechanism: The validator selects any CodeView entry and treats `Guid+Stamp` as a portable PDB ID without requiring `IsPortableCodeView` or `Age==1`. Only portable marker `MinorVersion 0x504d` uses this scheme.
- Impact: The validator can pass a PE/PDB pair that consumers interpret as Windows-PDB identity and cannot resolve as the packaged portable PDB.
- Safe evidence: Change a valid PE debug entry's `MinorVersion` away from `0x504d` and `Age` away from 1 while retaining Guid, Stamp, and PDB; validation still passes. The repository inventory script already requires `IsPortableCodeView`.

## Wave 8.17. MEDIUM - Symbol validator ignores the PE portable-PDB checksum

- File: `scripts/SharpProof.SymbolPackageValidator.cs`
- Members: `ValidatePair`, current lines 195-231; `ValidatePortablePdb`, lines 243-255
- Mechanism: `DebugDirectoryEntryType.PdbChecksum` is ignored; only the mutable `#Pdb` header ID is compared with CodeView. Portable PDB metadata does not enforce its ID as a hash of current bytes.
- Impact: The gate can certify altered sequence points or documents; checksum-aware consumers reject the PDB while others show misleading source mappings.
- Safe evidence: Alter valid PDB document or sequence metadata while retaining the original `#Pdb` ID and valid Source Link; the parser and validator accept. The checksum entry must be required and recomputed according to the portable-PDB specification.

## Wave 8.18. MEDIUM - Source Link custom debug information on the wrong metadata parent is accepted

- File: `scripts/SharpProof.SymbolPackageValidator.cs`
- Location: Current lines 258-267
- Mechanism: The validator enumerates every `CustomDebugInformation` row and filters only by Kind; it never checks Parent. Portable PDB requires Source Link's parent to be Module.
- Impact: A certified `.snupkg` can contain a Source Link blob that debuggers cannot discover at module level.
- Safe evidence: Attach one canonical Source Link blob to a Document or MethodDef; count and JSON checks pass. The parent must equal `ModuleDefinitionHandle(1)`.

## Wave 8.19. MEDIUM - Source Link mappings need not cover any PDB document

- File: `scripts/SharpProof.SymbolPackageValidator.cs`
- Location: Current lines 269-293
- Mechanism: Validation requires nonempty mappings, keys ending in `/*`, and a canonical URL value, but never enumerates `reader.Documents` or applies Source Link matching semantics.
- Impact: The release gate can pass a Source Link claim whose actual source documents are unreachable for stepping or download.
- Safe evidence: A PDB document `/_/src/A.cs` with JSON mapping `/does-not-match/*` to the canonical commit URL passes although the mapping covers no document. Every nonembedded document must resolve under validated wildcard semantics.

# Read-Only Multi-Agent Bug Audit - Wave 9 - 2026-08-29

This section records 19 unique findings from exactly 30 fresh read-only auditors: 16 auditors reported findings and 14 reported none. The relay reported no exact prior-mechanism or same-wave duplicates. The central writer did not inspect or reverify the code.

## Wave 9.1. MEDIUM - Generic file-load failures are falsely reported as native SMT backend outages

- File: `SharpProof.Worker/Program.cs`
- Members: `Main` catch and `IsBackendUnavailable`
- Current lines: 97-106 and 171-182, especially 175-177
- Mechanism: Every verification exception is classified backend-unavailable when any `InnerException` is `FileNotFoundException`, `FileLoadException`, or `BadImageFormatException`, without binding it to Z3 or native loading. These are general CLR and file failures.
- Impact: Managed dependency or input failures publish `BackendUnavailable` and a false native-SMT message, misdirecting remediation and telemetry.
- Safe evidence: `IsBackendUnavailable(new FileNotFoundException("missing ordinary managed artifact"))` returns true, and `Main` emits native backend unavailable.

## Wave 9.2. MEDIUM - Replaceable cache-lock pathname permits simultaneous cache transactions

- File: `SharpProof.Worker/VerificationCache.cs`
- Members: `AcquireLock`, current lines 212-229, open at 218-222 and path validation at 226; consumers `TryReadAsync`, lines 25-89, and `TryWriteAsync`, lines 135-167
- Mechanism: `FileShare.None` protects the opened inode, but post-open `ValidatePath` only rechecks the pathname and never matches handle device or inode. A same-uid process can rename or unlink the lock and create a replacement; another transaction locks the replacement while the first retains the old inode.
- Impact: Concurrent transactions can lose or replace entries, stage inconsistent capacity, and strand artifacts, breaking availability, resource, and durability guarantees.
- Safe evidence: Pause transaction A after open, rename the lock, create a replacement, and start B; both exclusive opens succeed, and pathname validation accepts the replacement.

## Wave 9.3. MEDIUM - Void callable replay accepts an arbitrary returned value

- Files and members: `SharpProof.Worker/CallableCounterexampleReplayer.cs`, `Replay`, current lines 65-76; boundary validation `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `ValidateExecutableBody`, lines 883-917; executor `Run`, lines 179-188.
- Mechanism: Return-shape validation checks for missing or wrong type only when exactly one canonical Result exists. With zero Result variables it does not require `execution.ReturnValue=null`. Artifact validation likewise skips return checks for void, while the executor can produce a nonnull return term.
- Impact: Malformed or tampered void IR can yield a replay-accepted fabricated `Refuted` postcondition or cache result, causing a false build failure.
- Safe evidence: A zero-Result preparation whose program returns `Integer(0)` with `Ensures(false)` makes `Replay` return None instead of `CounterexampleReplayFailed`.

## Wave 9.4. HIGH - Worker creation and request can use different per-query solver limits

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Members: `Create`, current lines 27-37, especially 35-36; `VerifyAsync`/`RunLane`, lines 253-255
- Mechanism: `Create` closes over creation-time budgets and constructs every `IrSmtBackend` with `budgets.QueryRlimit`, while `VerifyAsync` accepts, schedules, and accounts any request using `request.Budgets.QueryRlimit`; no equality check exists.
- Impact: The same hashed request behaves differently depending on worker construction state. A query can exhaust early or use a higher Z3 limit than the authenticated request declares.
- Safe evidence: Compare `Create(low).VerifyAsync(requestHigh)` with the inverse; `CheckCore` installs the construction option directly as Z3 rlimit.

## Wave 9.5. MEDIUM - Worker responses alias the caller's mutable budget object

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Member: `VerifyAsync`
- Current lines: 58, 66, 108-109, 154-156, and 164-175; downstream `WorkerResultAssembler` assignment
- Mechanism: All paths pass `request.Budgets` directly, and `response.Summary.Budgets` stores the same reference. `WorkerBudgets` has public setters; no snapshot or clone is made.
- Impact: Mutating the request after receiving a response changes already-returned authoritative budget evidence while request and input hashes remain old, making the response internally inconsistent and serialization unstable.
- Safe evidence: Await a response, mutate `request.Budgets.QueryRlimit`, and observe `response.Summary.Budgets` change.

## Wave 9.6. MEDIUM - Instance `this` is never bound to the canonical contract receiver

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`, `TryCreateParameterBindings`, current lines 549-573; `CompilerCallableProjections.GetVariableSource`, line 20; `SharpProof.Frontend/RoslynOperationLowerer.cs`, `GetInstance`, lines 237-252, and `CreateVariableBindings`, lines 61-66.
- Mechanism: Bindings include only `BoundContractVariableRole.Parameter` and frontend `_variables`; body `this` lives in `_instances`, while the contract uses a distinct Receiver variable.
- Impact: Valid instance receiver-dependent postconditions lower with unrelated variables and become `Unknown`; base and this references can diverge.
- Safe evidence: `Subject Identity(){ Ensures(Result<Subject>()==this); return this; }`; the body returns an unbound frontend instance while `Ensures` uses the canonical receiver.

## Wave 9.7. MEDIUM - Object-allocation replay revalidation ignores cancellation during a large symbol scan

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs`, `TryCreateEvent`, current lines 90-102, and `IsDefiniteObjectAllocation`, lines 173-187; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `HasPotentialConstructionInitialization`, lines 170-223.
- Mechanism: After the last source-resolution cancellation check, revalidation calls the helper without a `CancellationToken`. It can traverse up to 256 base types and enumerate all members and syntax references with no later check.
- Impact: A large type hierarchy can keep collector CPU work and artifact construction running after build cancellation.
- Safe evidence: Cancel just after source capture; a large member-rich hierarchy continues scanning, and `TryCreate` can still return an artifact.

## Wave 9.8. HIGH - Opaque fallback recursively relowers already-lowered children exponentially

- File: `SharpProof.Frontend/RoslynOperationLowerer.cs`
- Members: `Opaque`, current lines 279-288; `VisitBinaryOperator`, lines 685-702 and 723-729; `OpaqueBinary`, lines 948-953; analogous unary, conditional, conversion, and array paths
- Mechanism: The visitor first calls `LowerCore` on children; if inexact, fallback passes the original operands to `Opaque`, which calls `LowerCore` on them again. A left-associated unsupported chain follows `T(n)=2T(n-1)+O(1)`.
- Impact: Dozens of legal nested operators can hang or crash analyzer and collector work through exponential visitor and factory work; impure anchors also allocate distinct IDs.
- Safe evidence: Measure small depths for unchecked-long left-associated additions, such as 8, 12, 16, and 20, avoiding unsafe depth jumps.

## Wave 9.9. HIGH - Specification-pack assembly authentication is forgeable through public-sign metadata

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs`
- Members: `TryResolve`, current lines 214-255, especially 235; `MatchesAssembly`, lines 323-335
- Mechanism: A candidate is authenticated only by documentation ID, scalar shape, assembly simple name, and public-key token. Roslyn exposes a token from public-key metadata but does not verify a private-key signature; a public-signed or hand-authored assembly can embed the approved public key and name and provide an arbitrary matching method.
- Impact: An audited specification relation can be applied to attacker-controlled implementation, enabling unsound `Proven` results.
- Safe evidence: Synthesize a public-signed fake approved assembly with arbitrary `System.Math.Max` through an extern alias; `CanResolve` returns true. Authentication must bind to captured and verified module identity.

## Wave 9.11. MEDIUM - Actual IL evaluation-stack depth is never enforced

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
- Members: `TryBuild`, current lines 150-159; `Translator.Translate`, lines 495-520; push paths such as `Dup`, lines 648-655
- Mechanism: Admission checks only declared `body.MaxStack<=128`. Translation uses an unbounded `Stack<IlValue>` and never compares actual `stack.Count` to the header or `MaximumStack`.
- Impact: Malformed IL with an understated header is accepted and summarized instead of abstaining; the intended stack-resource and valid-image boundary is ineffective.
- Safe evidence: Patch a method header to `MaxStack=1` for supported `ldarg.0; dup; pop; ret`, which reaches depth 2, or use 129 pushes followed by balanced pops.

## Wave 9.12. MEDIUM - IL block translation rescans all instructions for every basic block

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
- Member: `Translator.Translate`
- Current lines: 484-510, outer block loop at 485-487 and `instructions.Where` at 509-510
- Mechanism: Each block's LINQ filter enumerates the entire instruction array. With B blocks and I instructions the cost is O(B*I), quadratic for branch-heavy acyclic IL, before the summary resource budget.
- Impact: A valid method below 64 KiB with thousands of supported branch blocks can consume excessive collector CPU and risk build timeout.
- Safe evidence: Generate a long acyclic `if (x==k) return k` chain; every leader or fallthrough block rescans all decoded instructions.

## Wave 9.14. MEDIUM - Flow-proven negative array creation is treated as normally completing

- Files and members: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanArrayCreation`, current lines 817-833, and `ArrayCreationExceptions`, lines 994-1001; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteArrayCreation`, lines 917-928.
- Mechanism: The scanner uses abstract flow to record `OverflowException` for a negative dimension, but the allocation step has `completes=true`. The completion predicate rejects only a syntactic boxed int below zero and ignores managed-flow facts and other integral types.
- Impact: Unreachable later effects remain, producing false summaries and contract diagnostics.
- Safe evidence: `long n=-1; _=new byte[n]; Mutate();`; overflow is recorded, yet sequencing allows `Mutate` although allocation must terminate.

## Wave 9.15. MEDIUM - DataflowGraph cycle classification is quadratic on sparse acyclic graphs

- File: `SharpProof.Dataflow/DataflowGraph.cs`
- Members: Constructor call at line 89; `FindCyclicBlocks`, current lines 164-192, especially per-start DFS at 167-189
- Mechanism: The method performs a fresh reachability DFS from every block to determine whether it reaches itself. An n-node chain produces n(n-1)/2 visits plus per-start allocations; the public constructor has no size or cancellation bound.
- Impact: A linear-size graph can stall construction before the solver iteration budget.
- Safe evidence: A 10,000-node chain yields 49,995,000 visits for 9,999 edges. An SCC algorithm would be O(V+E).

## Wave 9.16. LOW - Worker-only timeout reserve is enabled by any matching argument string

- File: `SharpProof.BuildTasks/RunVerifier.cs`
- Members: `Execute`, current lines 133-150; `HasWorkerLauncherBudgetArguments`, lines 839-846
- Mechanism: The classifier returns true if any argument `ItemSpec` equals `--project-wall-ms`, without authenticating launcher identity or option position. A direct arbitrary payload with the same token and value gets an extra four seconds.
- Impact: A hung or noncooperative direct verifier can exceed the documented direct-call deadline; a short invocation can succeed instead of timing out with 124.
- Safe evidence: A helper sleeps 2.5 seconds with arguments `[helper.dll, --project-wall-ms]`, project wall 1 ms, and grace 1 ms. The reserve gives approximately five seconds and returns zero instead of timing out.

## Wave 9.17. HIGH - Stable bind-mount aliases bypass publication-set exclusivity

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Members: `CanonicalPublicationPaths`, current lines 365-371; `ValidatePublicationTopology`, lines 374-396; `PublicationMetadataPath`, lines 419-435; `AcquirePublicationSet`, lines 249-285
- Mechanism: Identity, topology, and lock names use normalized pathname strings only. Two local bind mounts of the same directory yield distinct lock and marker hashes for the same physical destination, and both leases acquire.
- Impact: Overlapping publishers or invalidation can concurrently replace, delete, or roll back the same output, producing mixed or corrupt state.
- Safe evidence: Bind one temporary directory at `/mnt/a` and `/mnt/b`; destination aliases share `st_dev` and `st_ino`, but both one-member `AcquirePublicationSet` calls succeed and lock names differ.

## Wave 9.18. MEDIUM - Recursive metadata-reference aliases are erased from compiler evidence

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`
- Member: `CaptureReference` return initializer
- Current lines: 278-285
- Mechanism: `CompilerReferenceSnapshot` has `HasRecursiveAliases`, but the producer never assigns `reference.Properties.HasRecursiveAliases`, so the captured row defaults false.
- Impact: Snapshot and fingerprint cannot distinguish recursive from nonrecursive alias scope and misrepresent transitive symbol visibility and binding.
- Safe evidence: `PortableExecutableReference.WithAliases(["X"]).WithRecursiveAliases(true)` has a true input property, but captured `HasRecursiveAliases` is false. The schema defines the field, but no producer assignment exists.

## Wave 9.19. HIGH - Generic source static-constructor effects are replaced by a bare exception effect

- File: `SharpProof.Effects/EffectAnalysisSession.cs`
- Member: `ResolveStaticFieldTypeInitialization`
- Current lines: 270-281; ordinary path at 282-292
- Mechanism: A cross-type access to a static field on a source generic type with any explicit static constructor returns only `Throw(TypeInitializationException)`. It neither summarizes the constructor nor adds `UnknownBoundary`; `Throw` carries no reads, writes, allocation, or capabilities.
- Impact: Callers can be falsely certified nonallocating, capability-free, or nonwriting although first access executes a generic type initializer with those effects.
- Safe evidence: Generic `G<T>` has a static constructor assigning `State.Value = new object()` and static field `X`; `C.M` reads `G<int>.X`. Runtime can allocate and write during initialization, but the summary contains only the field read and possible TIE. This is distinct from the recorded definitely-failing initializer exception omission.

# Read-Only Multi-Agent Bug Audit - Wave 10 - 2026-08-29

This section records 26 unique findings from exactly 30 fresh read-only auditors: 17 auditors reported findings and 13 reported none. The relay reported no exact prior-mechanism or same-wave duplicates. The central writer did not inspect or reverify the code.

## Wave 10.1. HIGH - Postcondition vacuity authority accepts labels without establishing vacuity

- File: `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`
- Members: `ValidateClaim`, current lines 97-107; `ValidatePostconditionClaim`, lines 268-292; `ValidateProofCore`, lines 304-315; `HasAdmissibleEntryCore`, lines 317-331
- Mechanism: `ContradictoryPreconditions` requires only allowed entry labels and at least one requires or domain label; it never proves inconsistency. `NoModeledNormalReturn` requires only literal `body:normal-completion` and never checks the prepared body. Validation is set membership.
- Impact: A tampered or corrupt response can turn a genuine counterexample into an accepted `Proven` vacuous proof, suppressing the violation.
- Safe evidence: Mutate a refuted postcondition to `Proven/None/Unspecified` with `ContradictoryPreconditions` and `ProofCore=[requires:0]` for satisfiable requirements, or use `NoModeledNormalReturn` with `[body:normal-completion]`.

## Wave 10.2. HIGH - Portable member semantic IDs bypass canonical decode binding

- Files and members: `SharpProof.CompilerArtifact/PortableIrGraphCodec.cs`, `RequireCanonicalEncoderImage`, current lines 89-128, especially 110-120; `SharpProof.CompilerArtifact/PortableIrModel.generated.cs`, `PortableIrMember.DocumentationCommentId`, line 60.
- Mechanism: The decoder reconstructs a member without its documentation ID. Canonical re-encoding would emit null, but the validator copies attacker-supplied IDs onto the canonical graph before comparison. The ID is never bound to declaring type, name, or signature.
- Impact: A shape-compatible arbitrary call can be labeled with an approved API identity; downstream specification application can use that identity to constrain an unrelated call, yielding an unsound `Proven` result.
- Safe evidence: Assign an unrelated valid documentation ID to an encoded member. `Decode` succeeds although normal re-encoding loses the ID. Align companion call identity and specification witness to apply the false specification.

## Wave 10.3. LOW - Diagnostic canonicalization leaves authority-field ties unresolved

- Files and members: `SharpProof.CompilerArtifact/CompilationFingerprint.cs`, `ComputeSha256`, current lines 46-61; `CompilerDiagnosticArtifactOrdering.Canonicalize`, lines 399-409; `Compare`/`IsCanonical`, lines 412-458.
- Mechanism: The comparator omits `IsSource`, source-tree ordinal, path, hash, and line-map hash. Diagnostics with the same mapped location, code, and message can compare equal despite distinct physical authority. Stable order preserves input and both permutations are canonical, while the fingerprint serializes the omitted fields.
- Impact: The same diagnostic multiset yields order-dependent artifact, cache, and input identities.
- Safe evidence: Two physical trees mapped to the same `#line` geometry produce identical diagnostic text. Both orders are canonical, but hashes differ.

## Wave 10.4. LOW - Snapshot shape validation leaks NullReferenceException for malformed nested rows

- File: `SharpProof.CompilerArtifact/CompilationFingerprint.cs`
- Members: `ValidateShape`, current lines 64-69; `ValidSummaryEvidence`, lines 108-148; `ValidSummaryEvidenceRow`, lines 151-203; `ValidReference`, lines 297-325
- Mechanism: JSON can supply explicit null for nonnullable reference properties. Unguarded `.Length` and `value.Modules[0].Name` execute before nested-row validation, so the Boolean validator cannot return false.
- Impact: Crafted or recomputed input escapes the documented invalid-evidence `JsonException` path as an unclassified NRE, causing denial of service or error misclassification.
- Safe evidence: A valid snapshot with summary `EvidenceIdentity=null` or a module-kind reference with `Modules=[null]` makes `ValidateShape` throw NRE.

## Wave 10.5. MEDIUM - Predicate-only deduplication erases trusted domain-assumption provenance

- File: `SharpProof.Worker/PostconditionObligationBuilder.cs`
- Member: `TryAddSourceDomainAssumptions`
- Current lines: 14 and 51-68
- Mechanism: `seenPredicates` is seeded by all prior assumption predicate IDs. A user `Contract.Assume` identical to a compiler primitive interval hash-conses to the same predicate, so trusted `domain:parameter:0` is skipped.
- Impact: Proof is falsely attributed to a user assumption and marked Used. The normal-completion probe filters user assumptions and loses the trusted domain fact, weakening or missing `NoModeledNormalReturn` and vacuity.
- Safe evidence: For int `x`, use exact Assume `x>=int.MinValue && x<=int.MaxValue`; body `return x<=int.MaxValue ? 1/(x-x) : 0;` and `Ensures(false)`. Trusted domain proves no modeled return, but dedup leaves only user provenance and the probe admits unbounded `x>int.MaxValue`.

## Wave 10.6. MEDIUM - Legal summary call identities over 512 characters crash artifact production

- Files and members: `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `ValidSummaryCallIdentity`, current lines 1052-1055 and callers at 1058-1090; producer `CompilerCallableLowerer.TryGetCallIdentity`, lines 376-380; `CompilerRelationalSummaryProvider.CreateAuthority`, lines 327-396.
- Mechanism: The validator hard-rejects identity length over 512, while the producer accepts any nonempty Roslyn documentation ID and emits it. A legal long namespace, type, method, and signature can exceed the cap.
- Impact: A valid contract project fails artifact production and build instead of scoped callable abstention.
- Safe evidence: A legal long callee invoked by a verified method yields a descriptor emitted by the producer and rejected by its own decoder or fingerprint. Admission must enforce the bound with abstention or the bounds must align.

## Wave 10.7. MEDIUM - Direct long.MinValue literal syntax abstains before special negation admission

- Files and members: `SharpProof.Frontend/CompilerConstantAdmission.cs`, `IsLiteralIntegerNegation`, current lines 28-35; `RoslynOperationLowerer.VisitUnaryOperator`, lines 558-562 and 588-590; scalar catalog at 72-81.
- Mechanism: `VisitUnaryOperator` lowers the operand exactly before the helper. Legal `-9223372036854775808L` is Roslyn unary minus over an ulong literal with a long result; ulong is unsupported, so the operand rejects and the helper never runs.
- Impact: Valid exact long-domain code and contracts using direct long-min syntax abstain as `UnsupportedType`, while `long.MinValue` field and `-1L` work.
- Safe evidence: Lower `static long Target()=>-9223372036854775808L`; expected result is exact `long.MinValue`, while current flow rejects the UInt64 operand.

## Wave 10.8. HIGH - Nullable<T> is globally misclassified as definitely nonnull

- File: `SharpProof.Effects/OperationNullnessEvaluator.cs`
- Member: `IsProvenNonNull`
- Current lines: 103-109, especially 107; consumers `OperationEffectScanner`, lines 845-865 and 878-893; `SwitchExpressionFacts`, lines 224-226
- Mechanism: Every `IsValueType` is treated nonnull, including `Nullable<T>`, despite a `HasValue=false` null path.
- Impact: Reachable null switch arms and their effects or throws can be omitted; conditional access can be falsely noncompleting.
- Safe evidence: Unknown `S? x` has Roslyn `IsValueType=true` while `x is null` can be true; the scanner marks the null arm Never.

## Wave 10.9. HIGH - Metadata-defined custom ref-struct list-pattern members are suppressed as compiler intrinsics

- File: `SharpProof.Effects/SwitchExpressionFacts.cs`
- Member: `IsCompilerIntrinsicListPatternMember`
- Current lines: 47-55; consumer `OperationEffectScanner.ScanListPattern`, lines 906-912
- Mechanism: Zero syntax references plus `ContainingType.IsRefLikeType` is enough for suppression, without restricting to authentic framework Span types or member identity. Referenced custom ref-struct `Length` and indexer are silently skipped.
- Impact: Arbitrary calls, writes, capabilities, and exceptions in effectful metadata members are omitted.
- Safe evidence: A referenced custom ref struct with effectful or throwing `Length` and indexer has zero syntax references and both members are suppressed.

## Wave 10.10. MEDIUM - Constant nonnull reference switch invents a reachable unmatched exception

- File: `SharpProof.Effects/SwitchExpressionFacts.cs`
- Member: `GetPatternSelection`
- Current lines: 376-413, especially 410; callers `GetReachableArms`, lines 79-98, and `HasReachableUnmatchedPath`, lines 149-172; `IsTotalPattern`, lines 290-293
- Mechanism: The constant-value path knows the value is nonnull but calls `IsTotalPattern` without `inputDefinitelyNonNull=true`, so an exact reference type pattern is not considered total.
- Impact: A guaranteed match gets a false `SwitchExpressionException` effect and contract rejection.
- Safe evidence: `"x" switch { string s => 1 }` is exhaustive, but selection is Maybe and an unmatched path is reported.

## Wave 10.12. HIGH - Unchecked reference and unboxing casts contribute no catch-reachability exceptions

- File: `SharpProof.Effects/ExceptionHandlerReachability.cs`
- Members: Conversion branch, current lines 626-649; fallback at 978-980; `CanThrowUnknown`, lines 2676-2714, especially 2687-2688
- Mechanism: Built-in conversions are treated as throwing only when `IsChecked`. Explicit reference and unboxing casts have no operator and `IsChecked=false` but can throw `InvalidCastException` or NRE.
- Impact: Relevant handlers are treated unreachable and their effects disappear.
- Safe evidence: `try { _=(string)new object(); } catch (InvalidCastException) { Mutate(); }`; the conversion adds no potential exception although runtime enters the catch.

## Wave 10.13. HIGH - Metadata property getters on ref-like types are blanket-treated nonthrowing

- File: `SharpProof.Effects/ExceptionHandlerReachability.cs`
- Location: Property-reference branch at current lines 693-753, special case 736-744
- Mechanism: Any nonvirtual accessor with no declaring syntax returns `EmptyPotential` when its containing type is ref-like, without restriction to verified compiler intrinsics.
- Impact: Catches for real metadata getter or indexer exceptions are marked unreachable and handler effects are omitted.
- Safe evidence: Access `span[0]` on an empty `Span<int>` inside a catch for `IndexOutOfRangeException`; the metadata ref-like accessor returns `EmptyPotential` although runtime throws.

## Wave 10.14. HIGH - Throws of exception-constrained type parameters are erased

- File: `SharpProof.Effects/ExceptionHandlerReachability.cs`
- Member: `GetPotentialExceptions(IThrowOperation)`
- Current lines: 174-219, with `INamedTypeSymbol` gate at 207-217
- Mechanism: Legal `throw value` where `T : Exception` has an `ITypeParameterSymbol`, so the explicit throw contributes neither a known exception nor Unknown.
- Impact: A generic source method can appear nonthrowing for reachability, and matching handler effects are omitted.
- Safe evidence: `Throw<T>(T value) where T:Exception { throw value; }` called with `ApplicationException` inside a matching catch; the generic body adds `EmptyPotential`.

## Wave 10.16. MEDIUM - Optional and params base constructors make implicit construction unconditionally incomplete

- Files and members: `SharpProof.Effects/EffectCallSiteResolver.cs`, `ResolveConstruction`, current lines 101-130, especially 120-126; `EffectMethodNodeBuilder.GetUniqueParameterlessBaseConstructor`, lines 239-246.
- Mechanism: The resolver peels a source implicit zero-formal derived constructor and then seeks a base constructor with `Parameters.IsDefaultOrEmpty`. C# implicit `base()` can select an all-optional or params constructor, but the helper rejects both and returns Unsupported.
- Impact: `new Derived()` loses precise base-call effects and blocks valid purity or effect proofs, conservatively incomplete.
- Safe evidence: `Base(int value=0)` has a supported body and sealed `Derived:Base` has an implicit constructor. Analysis finds no parameterless candidate and returns Unsupported; a params variant behaves the same.

## Wave 10.17. HIGH - One-layer wrapper normalization lets nested parentheses or conversions bypass SPMETA010

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `IsNonCacheableSemanticAnswer`
- Current lines: 65-90, normalization at 68-73
- Mechanism: The method removes exactly one parenthesized or non-user conversion and then switches without looping. A nested wrapper reaches default and returns false despite the underlying unsafe enum constant.
- Impact: Cosmetic parentheses or casts defeat the error-level direct cache-write invariant.
- Safe evidence: `cache.Write(((Answer.Unknown)))`; the outer parenthesis is removed and the inner one defaults safe. Nested ordinary conversions behave similarly.

## Wave 10.18. HIGH - Non-assignment local writes are absent from reaching-local definitions

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `GetReachingLocalValues`/`TransferLocalValues`/`GetLocalWriteValue`, current lines 117-181 and 204-251, with write switch at 239-250
- Mechanism: Only declarators and simple assignments directly targeting `ILocalReferenceOperation` are recognized. Invocations writing through `ref`/`out` and `IDeconstructionAssignmentOperation` neither update nor invalidate the tracked value.
- Impact: A local can change from a stable answer to Unknown through a helper or deconstruction and then enter the cache without SPMETA010.
- Safe evidence: Both `var answer=Answer.Proven; SetUnknown(out answer); cache.Write(answer);` and `var answer=Answer.Proven; (answer,_)= (Answer.Unknown,0); cache.Write(answer);` retain only the Proven initializer.

## Wave 10.19. HIGH - Coalesce and compound property or indexer cache writes are never analyzed

- Files and members: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `AnalyzeAssignment`, current lines 30-43; `SharpProofSoundnessAnalyzer.Initialize` registration at line 66.
- Mechanism: The analyzer registers only `OperationKind.SimpleAssignment`. `ICoalesceAssignmentOperation` and `ICompoundAssignmentOperation` cache setters reach neither `AnalyzeAssignment` nor invocation analysis.
- Impact: Direct property or indexer writes can persist explicitly unsafe semantic answers without SPMETA010.
- Safe evidence: `cache.Latest ??= new ErrorAnswer()` or a compound setter; no registered operation action handles it.

## Wave 10.20. MEDIUM - ContractFor companion bodies bypass nonplacement contract API validation

- Files and members: `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs`, `ValidateBody`, current lines 158-180; `AnalyzerFeaturePipeline` companion skips at 26-36 and 185-188; ordinary validation at 598-623.
- Mechanism: The companion validator checks implementation body and clause placement only; it never consumes `HasRejectedContractApiUsage` or `ContractIntrinsicValidator`. Normal operation-block paths skip companions, so the gap persists.
- Impact: A mapped companion can contain a rejected shadow Contract API or malformed intrinsic and receive no SP0047, SP0024, or SPCF diagnostic; the clause is ignored or fails later without an actionable source diagnostic.
- Safe evidence: A valid mapped int companion contains `Ensures(Result<long>()==0); return 0;`; mapping and placement pass, companion analysis is skipped, and diagnostics are empty.

## Wave 10.21. MEDIUM - Container contract validation is not closed over the generated schema

- Files and members: `SharpProof.Host/ContainerContract.cs`, `ValidateRequired`, current lines 74-108; generator `eng/container/New-ContainerContract.ps1`, lines 14-30.
- Mechanism: Validation checks only a subset of 15 emitted fields; it omits minimum SDK and framework, test runtime, base image and digest, and PowerShell version and digest. It rejects neither duplicates nor unknowns.
- Impact: Preflights certify a truncated, stale, or conflicting marker that does not bind pinned runtime and image identities and disagrees with the repository probe.
- Safe evidence: Remove or change `dotnetTestRuntimeVersion` or `dotnetMinimumSdkVersion` and `ValidateRequired` still succeeds; a duplicate required property with a matching value last also passes.

## Wave 10.22. LOW - InvalidatePublishedResult.Cancel can call a disposed CancellationTokenSource

- File: `SharpProof.BuildTasks/InvalidatePublishedResult.cs`
- Members: `Execute`, current lines 49-74; `Cancel`, lines 247-255
- Mechanism: `Cancel` copies `_cancelExecution` under a lock and invokes it after unlocking. `Execute` can clear the field, leave `finally`, and dispose the CTS before the copied delegate runs; the delegate is `cancellation.Cancel`.
- Impact: Legitimate MSBuild cancellation racing normal completion throws `ObjectDisposedException` from `ICancelableTask.Cancel`.
- Safe evidence: Interleaving: Cancel captures and unlocks; Execute clears, unlocks, and disposes; Cancel invokes. `CancellationTokenSource.Cancel` after Dispose throws.

## Wave 10.23. LOW - AlphaRenameContractFormals variants duplicate baselines for all effect seeds

- File: `SharpProof.Gates/Corpus/CorpusCatalog.cs`
- Member: `CreateCase`
- Current lines: 268-350, switches 269-292, alpha branch 304-310, and `CreatePrelude` default 372-373
- Mechanism: Effect seeds use default class, method, helper, input, and prelude and have no matching contract-formal text, so `AlphaRenameContractFormals` changes nothing.
- Impact: Eighteen counted metamorphic cases add zero transformation coverage; concurrent replay selects E01 alpha as its representative, which is also a duplicate, hiding the gap.
- Safe evidence: Compare `CreateCases` baseline and alpha sources for E01-E18; all 18 pairs are byte-identical.

## Wave 10.24. MEDIUM - Forced-termination performance probe does not test the shipped default boundary

- File: `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs`
- Member: `MeasureForcedTerminationCoreAsync`
- Current lines: 188-199; `PerformanceGate` assertions at 227-231 and 313-317
- Mechanism: The acceptance ceiling and production default grace are both 1000 ms, but the probe subtracts 300 ms headroom, launches explicitly with 700 ms, and then compares elapsed time against 1000 ms.
- Impact: Regressions in the real 1000 ms default are invisible and about 300 ms of overhead is budgeted away; release can pass while the shipped boundary exceeds threshold.
- Safe evidence: Contract and default both equal 1000, while the probe always passes explicit 700. Changing the production default above 1000 leaves the probe unchanged.

## Wave 10.25. MEDIUM - Null array-valued effect attributes crash semantic fingerprinting

- File: `SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs`
- Member: `WriteTypedConstant`
- Current lines: 467-483, especially 472-481; callers `EffectContractDiagnostics`, lines 174-186 and 293-310, and `ClaimManifestBuilder`, lines 311-319
- Mechanism: A Roslyn null attribute array is `TypedConstantKind.Array` with default immutable `Values`. The code reads `Values.Length` and enumerates without checking `IsDefault` or `constant.IsNull`. Invalid evaluation retains the selected attribute, and fingerprinting crashes before fail-closed evidence.
- Impact: Malformed but compilable source attributes abort final artifact collection or build instead of producing the intended diagnostic and evidence.
- Safe evidence: `[AllowedExceptions(null)]` or `EffectContract` with `ThrownExceptions=null`; default `ImmutableArray.Length` throws. Null and default state must be hashed explicitly.

## Wave 10.26. MEDIUM - Relative cache option is prevalidated against process cwd instead of project directory

- File: `SharpProof.Worker.Launcher/Program.cs`
- Members: `LauncherArguments.CreateRequest`, current lines 859-873, especially 860-862 versus 867-873; `ValidateDistinctPaths`, lines 901-912; generated `CreateCache`, lines 111-118
- Mechanism: The first topology check canonicalizes a raw relative cache path against `Environment.CurrentDirectory`. After manifest load, actual resolution correctly uses `artifact.Compilation.ProjectDirectory`, but the earlier check can reject a false collision.
- Impact: A valid direct or integrated launcher invocation fails solely because its working directory differs from the project directory; the packaged path may mask it.
- Safe evidence: Working directory `/tmp/invoke`, request `/tmp/invoke/cache`, project `/tmp/project`, and cache option `cache`; precheck aliases the request, while the actual cache `/tmp/project/cache` is distinct.

# Read-Only Multi-Agent Bug Audit - Wave 11 - 2026-08-29

This section records 22 unique findings from exactly 30 fresh read-only auditors. Seventeen auditors reported 23 final findings and thirteen reported none; one exact prior duplicate was not forwarded. One provisional finding was explicitly retracted and is excluded. The central writer did not inspect or reverify the code.

## Wave 11.1. MEDIUM - Callable projection rejects schema-valid incomplete coverage states

- Files and members: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `MatchesCallableProjection`, current lines 170-219; `ProtocolJson.ValidateRun`, lines 606-618; generated tuple catalogs, lines 710-755.
- Mechanism: Projection recognizes all-owned `Unknown/UnsupportedCallable` but maps all-owned `Unknown/UnsupportedContract` to `SemanticUnknown`, despite the declared `Incomplete/UnsupportedContract` state. Separately, `owned.Length==0` forces `None/Complete` even though zero-claim callables may validly be incomplete for `UnsupportedCallable`, `UnsupportedContract`, `SemanticUnknown`, or `InfrastructureFailure`.
- Impact: Schema-conforming typed abstentions become `response.callable_projection` and then `Failed/MalformedResult`, losing the advertised reason.
- Safe evidence: Both an all-`Unknown/UnsupportedContract` callable and a zero-claim `Incomplete/SemanticUnknown` callable pass the schema's declared state model but fail `MatchesCallableProjection`.

## Wave 11.2. HIGH - Coordinated same-typed pre-state association swaps pass validation

- Files and members: Generated `SharpProof.CompilerArtifact/CompilerVariableArtifact` fields, current lines 313-323; `CompilerLoweredArtifact` decode, lines 350-365, and `ValidateVariables`, lines 478-546; `PostconditionObligationBuilder`, lines 146-149.
- Mechanism: The validator checks that `CurrentStateVariable` is an injective receiver or parameter and that `SourceOrdinal` matches the selected current variable. `ModelLabel` only parses `pre:N`; it never requires N to equal the ordinal. Swapping `CurrentStateVariable` and `SourceOrdinal` among same-typed pre rows passes while clause roots remain unchanged.
- Impact: `Old(left)` can substitute `right`, enabling a false `Proven` result.
- Safe evidence: A method returns `right` with claim `Result==Old(left)` and dummy `Old(right)`; swap associations and reseal. The existing paired-swap test rejects only a stale hash, not coordinated decode.

## Wave 11.3. MEDIUM - Linked-module count cap is enforced after unbounded sorting and allocation

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`
- Members: `CaptureReference`, current lines 208-241, sorting and materialization at 215-225; `ReferenceCaptureBudget.Consume`, lines 300-315
- Mechanism: All nonmanifest modules are name-read, sorted, materialized, and assigned a full-size builder before the per-module budget rejects a count above the limit; the sort has no cancellation checks.
- Impact: A hostile multimodule reference forces O(n log n) work and O(n) allocation and can stall or exhaust memory before the intended `InvalidDataException`.
- Safe evidence: With limit 1 and several linked modules, all names are read and sorted and the builder is full-sized before `Consume` throws.

## Wave 11.4. HIGH - Recursive source methods are falsely classified nonreturning

- Files and members: `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MethodCanCompleteNormally`, current lines 1918-1950, especially cycle guard 1927-1929, and `InvocationMayCompleteNormally`, lines 2248-2266; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation`, lines 588-609.
- Mechanism: Active-method reentry returns false and is consumed as semantic noncompletion rather than uncertainty. A recursive method with a reachable base-case return becomes noncompleting.
- Impact: Calls are marked terminal, and CFG traversal omits real successor effects even for a base-case argument.
- Safe evidence: `R(n){if(n<=0)return;R(n-1);} C(){R(0);State++;}`; the analysis cycle makes `R` false, while runtime increments `State`.

## Wave 11.5. MEDIUM - Checked increment and decrement overflow proofs are disabled by the mutation gate

- Files and members: `SharpProof.Effects/ManagedAbstractFlow.cs`, public `ManagedFlowResult.ProvesNoOverflow`, current lines 1312-1316; internal increment case at 738-785, especially 765-772; `ManagedMutationFacts.HasMutation`, lines 5-16.
- Mechanism: The public query requires `!HasMutation`, while every increment and decrement is classified as mutation, so the internal increment proof case is unreachable.
- Impact: Range-safe checked increments retain a spurious `OverflowException` and weaken no-throw and effect proofs.
- Safe evidence: Constrain int `x <= int.MaxValue-1` and execute `checked x++`; the interval proves safety, but the outer gate returns false.

## Wave 11.6. HIGH - User-defined compound-assignment conversions are never scanned

- Files and members: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanCompoundAssignment`, current lines 79-100; `OperationCompletionEvaluator.CanCompleteCompoundValue`, lines 867-887; `ExceptionHandlerReachability` compound branch, lines 449-489.
- Mechanism: The scanner sequences target, RHS, operator, intrinsic, and store but never the `ICompoundAssignmentOperation.InConversion` or `OutConversion` `MethodSymbol`. Completion and reachability omit them too; Roslyn stores these conversions as metadata, not child operations.
- Impact: Conversion writes, allocation, throws, and divergence disappear, while unreachable store or later code can be reported.
- Safe evidence: A `Box += int` operator returns `Temp`, and implicit `Temp -> Box` writes and throws. The scanner sees the operator and store but omits conversion and catch reachability.

## Wave 11.7. HIGH - Positional-pattern Deconstruct calls are omitted from complete effect summaries

- Files and members: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanCoreOperationTail`, current lines 368-372; `OperationEffectScanner.ScanDefaultPattern`, lines 937-939; completion evaluator, lines 208-229.
- Mechanism: `IRecursivePatternOperation` falls to generic pattern-child scanning; `DeconstructSymbol` is never resolved even though it is an executable implicit call.
- Impact: `Deconstruct` writes, allocation, capabilities, exceptions, and call sites vanish without an Unsupported boundary.
- Safe evidence: `p is P(0)` where `P.Deconstruct` increments a static field; the caller remains Complete without the write.

## Wave 11.8. MEDIUM - Assignable reference equality abstains after common-type conversions are discarded

- File: `SharpProof.Frontend/RoslynOperationLowerer.cs`
- Members: `VisitBinaryOperator`, current lines 662-680 and 732-739; `UnwrapImplicitReferenceConversions`, lines 217-229; `IrTermServices` equality validation, lines 181-187
- Mechanism: Compiler-inserted implicit reference conversions are stripped before lowering. Legal `object==string` or `Base==Derived` becomes mismatched IR types; equality requires identical type and returns `UnsupportedType`.
- Impact: Exactly representable reference-equality contracts and bodies lose proof coverage.
- Safe evidence: Lower `static bool Target(object left,string right)=>left==right`; the right upcast is stripped and the result abstains instead of remaining Exact.

## Wave 11.9. MEDIUM - Deep exact expressions can terminate the host during recursive lowering

- File: `SharpProof.Frontend/RoslynOperationLowerer.cs`
- Members: `Lower`/`LowerCore`, current lines 52-56 and 73-75; recursive paths `VisitUnary` at 558, Binary at 685 and 698, Conditional at 758 and 765-766, Array at 888-889, and Property at 866
- Mechanism: `OperationVisitor` descent is recursive with no depth budget or worklist. A fully supported left-associated checked Int64 chain remains exact and consumes one CLR frame per node; `StackOverflowException` is uncatchable.
- Impact: A valid deep expression terminates the analyzer or collector process before typed abstention.
- Safe evidence: In an isolated process, gradually increase `checked(x+1L+...)` depth; the trace shows unbounded recursion. This is distinct from exponential opaque fallback.

## Wave 11.10. MEDIUM - Contract.Result bypasses supported-value-domain admission

- Files and members: `SharpProof.Contracts/ContractExpressionBinder.cs`, `BindIntrinsic`, current lines 54-59, and `BindWithFrontend`, lines 100-103; `ContractBinder.BindInvocations`, lines 197-205; `RoslynOperationLowerer.GetTypeId`, lines 78-109, and null comparison, lines 641-660.
- Mechanism: A custom Result variable uses `GetTypeId` directly without `IsSupportedValueDomain`. Unsupported domains such as `Nullable<T>`, pointer or function pointer, or unconstrained type parameter map to a reference type; the null-comparison fast path binds Exact.
- Impact: The binder publishes exact contract IR outside admitted CLR domains, potentially enabling fabricated reference-sort nullness reasoning instead of fail-closed `UnsupportedExpression`.
- Safe evidence: An `int?` method with `Ensures(Result<int?>()==null)` and no unsupported parameter operand binds successfully.

## Wave 11.11. MEDIUM - AnalyzerSession discards cancellation before lazy whole-compilation initialization

- File: `SharpProof.Analyzer.Core/AnalyzerSession.cs`
- Members: Constructor, current lines 62-107; accessors, lines 126-135; `AnalyzerFeaturePipeline` triggers at 76, 87-104, 174, and 218-224
- Mechanism: The token is checked once at line 68 but not stored; Lazy factories accept no token. First access performs companion discovery and clause walks without cancellation, and concurrent callbacks wait.
- Impact: Canceled IDE or build runs continue expensive whole-compilation work and block callbacks.
- Safe evidence: Create a session with a live CTS, cancel it, and first call `ResolveContractSource` on a large companion-rich compilation; scanning completes instead of throwing OCE.

## Wave 11.13. HIGH - Constructor-bypass allocation fabricates kernel-only outcomes without SPMETA011

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `Initialize`, current lines 62-71; `AnalyzeObjectCreation`, lines 132-175, with protected-type check at 167-174
- Mechanism: Enforcement applies only to `IObjectCreationOperation`. `RuntimeHelpers.GetUninitializedObject` allocates a protected type through an ordinary invocation and is not forbidden or inspected.
- Impact: Nonkernel code can manufacture a `ProvenOutcome` runtime instance; consumers trust type identity for cache and proof paths despite uninitialized fields.
- Safe evidence: `(ProvenOutcome)RuntimeHelpers.GetUninitializedObject(typeof(ProvenOutcome))` contains no ObjectCreation, and `RuntimeHelpers` is absent from the forbidden catalog.

## Wave 11.14. MEDIUM - SPMETA009's fragment detector misses equivalent expression-text constructions

- File and members: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `CSharpExpressionFragments`, `Initialize`, `AnalyzeBinaryOperation`, `AnalyzeCSharpExpressionText`, and `GetCSharpExpressionFragment`, current lines 48-49, 65-71, 183-187, and 256-309.
- Mechanism: The detector searches immediate constants for whitespace-padded fragments and runs only for binary `+` and interpolated strings. It therefore misses zero/asymmetric whitespace, unregistered construction through `+=` or `string.Concat`, and cataloged fragments split across adjacent binary nodes because it never analyzes the aggregate concatenation result.
- Impact: Soundness-critical code can synthesize equivalent C# predicate text while the error-level anti-synthesis boundary remains silent.
- Safe evidence: Bypasses include `x + "==null"`, `text += " is not null"`, `string.Concat(name, " == ", "null")`, and `name + " is" + " null"`.

## Wave 11.15. MEDIUM - SPMETA004 semantic-string enforcement is bypassed by common API and control-flow shapes

- File and members: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `Initialize`, `AnalyzeInvocation`, `AnalyzeSemanticEquals`, `AnalyzeSemanticString`, and `IsInsideCondition`, current lines 65-100, 189-224, and 330-342.
- Mechanism: Enforcement is local and syntax-shape-specific: it recognizes only selected direct equality operations, direct literals, and a fixed catalog of condition ancestors. It misses static `object.Equals`, non-equality string predicates such as `StartsWith`/`EndsWith`/`Contains`, comparisons routed through Boolean temporaries, semantic literals copied through non-const string locals, and query `where` conditions omitted from `IsInsideCondition`.
- Impact: Soundness-critical code can branch or filter on open-ended reason/provenance strings without the error-level typed-reason boundary.
- Safe evidence: Undiagnosed forms include `object.Equals(reason, "ir_timeout")`, `reason.StartsWith("ir_")`, `bool matches = reason == "ir_timeout"; if (matches)`, a direct condition comparing against a non-const local containing the literal, and `from reason in reasons where reason == "ir_timeout" select reason`.

## Wave 11.16. MEDIUM - Public solver declares convergence for unrestricted nonmonotone transfers

- Files and members: `SharpProof.Dataflow/ForwardDataflowAnalysis.cs`, `AnalyzeCore`, current lines 141-150 and termination at 198-200; `DataflowBlock`, lines 3-7; `IAbstractDomain`, lines 7-22.
- Mechanism: Output is updated as `Join(old, Transfer(input))`, so earlier facts never retract. This is correct only for monotone Transfer, but the public API accepts arbitrary `Func` and neither documents nor enforces monotonicity. If input grows and transfer shrinks, `changedOutputs` can be empty and the solver terminates with `output != Transfer(input)`.
- Impact: The purported fixed point contains stale or impossible facts propagated to successors.
- Safe evidence: In a powerset domain with a self-loop, initial `{A}`, and transfer `{A}->{B}`, `{A,B}->{}`, the result input is `{A,B}` and output `{B}`, while `transfer(input)` is empty.

## Wave 11.17. MEDIUM - Enabled-analyzer retention warmup hides stable first-use compilation leaks

- File: `SharpProof.Gates/Performance/PerformanceGate.cs`
- Members: `WarmEnabledAnalyzerRetentionPaths`, current lines 790-803; `MeasureEnabledAnalyzerRetention`, lines 805-830
- Mechanism: Warmup analyzes compilations, discards the returned weak references, performs GC, and then takes the baseline. A permanently retained warmup graph is baked into the baseline and absent from the later reachability count.
- Impact: The leak gate can report zero retained measured compilations and a low delta while the analyzer permanently roots a warmup Compilation.
- Safe evidence: Instrument the analyzer with `firstCompilation ??= compilation`; the warmup graph remains rooted, all 40 measured weak references die, and after-minus-before is near zero. Warmup references must be retained and checked or the baseline taken before analysis.

## Wave 11.18. MEDIUM - Source relational summaries admit API-spec calls but cannot compose them

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`
- Members: `IsAdmissiblePureCall`, current lines 76-84; `TryBuildSource`, lines 188-194; selected-call loop, lines 242-263; `TryGet`, lines 107-126
- Mechanism: API-spec side-effect-free calls lower exactly, but the dependency loop resolves every call only through source, IL, or spec-pack `TryGet`, never `_apiSpecs`.
- Impact: Wrapping a supported API call in a source helper changes a provable caller to `Unknown/UnsupportedBody`.
- Safe evidence: A direct `Math.Abs` call proves nonnegative; with `Local(v)=>Math.Abs(v)`, a caller of `Local` cannot resolve `Math.Abs` in the source-summary loop.

## Wave 11.19. MEDIUM - MaximumExpressionDepth lies outside both compiler evidence seals

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `Create`, current lines 71-92; `CompilerFeatureScopeFingerprint.ComputeSha256`, lines 14-26; `HasValidEnvelope`, lines 397-416; worker parity, lines 120-124.
- Mechanism: `MaximumExpressionDepth` is assigned after `CompilationSha256` inputs and excluded from `FeatureScopeSha256`; it is only range-checked. A coordinated artifact and request change passes.
- Impact: A postcollector modifier can raise or lower the verifier cutoff and change outcomes while compiler seals remain valid.
- Safe evidence: Change a valid artifact depth from 64 to 65 without recomputing hashes; serialization succeeds and a request using 65 passes worker parity.

## Wave 11.20. MEDIUM - Deep acyclic cutoff uncertainty is cached into shallower callees

- File: `SharpProof.Effects/EffectAnalysisSession.cs`
- Members: `ComputeSummaries`, current lines 379-415, especially 389-395 and 398-415; `EnsureAnalyzed`, lines 344-347
- Mechanism: A method exactly at depth 512 or greater is not cached, but its caller at 511 joins `UnknownBoundary` and is cached, as are ancestors. A later shallow `Analyze(caller)` returns the poisoned cache rather than recomputing.
- Impact: Request-order-dependent false effects or incompleteness and persistent precision loss.
- Safe evidence: For acyclic `M0 -> ... -> M512 -> leaf`, analyzing `M0` poisons `M511`; later `Analyze(M511)` is Unknown, while a fresh session computes it exactly.

## Wave 11.21. MEDIUM - AnalyzeAll omits primary constructors

- File: `SharpProof.Effects/EffectAnalysisSession.cs`
- Member: `CollectSourceMethods`
- Current lines: 460-489
- Mechanism: Enumeration collects `IMethodSymbol` returned directly by syntax nodes plus property accessors. A primary-constructor method is attached to `TypeDeclarationSyntax`, whose `GetDeclaredSymbol` returns `INamedTypeSymbol`; no `ConstructorDeclarationSyntax` child reaches the method arm.
- Impact: Bulk effect inventory and checking silently omit user-authored primary-constructor, base-list, and member-initializer effects, although direct `Analyze(primaryCtorSymbol)` can work.
- Safe evidence: `sealed class C(int x){ int y=SideEffect(x); }`; obtain the primary constructor from `InstanceConstructors` and analyze directly, then compare `AnalyzeAll` identities. The constructor is absent.

## Wave 11.22. MEDIUM - Protocol manifest validation accepts contradictory selection metadata

- Files and members: `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, `ManifestCallableRules`, current lines 839-846; `ProtocolJson.ValidateManifestCore`, lines 444-457; stronger compiler validator, lines 470-500.
- Mechanism: `SelectedFeatures` and `SelectionReasons` are validated independently as unique enums and never related to owned claims or assumptions. A callable can select Effects only while owning a Postcondition, have claim `DiscoveredPostcondition` with none, or carry contract assumptions without Contracts.
- Impact: Public protocol validation and hashing certify a manifest that cannot represent the claimed compiler selection; protocol-only consumers can interpret the wrong feature or provenance. Artifact-specific validation limits exposure.
- Safe evidence: Existing `CreateBoundaryManifest` uses Effects-only plus Postcondition. The targeted `ManifestHashSeparatesFormerlyAmbiguousCollectionBoundaries` passes validation 1/1.

# Read-Only Multi-Agent Bug Audit - Wave 12 - 2026-08-29

This section records 27 findings from exactly 30 fresh read-only auditors. The relay supplied the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 12.1. MEDIUM - SMT lanes are provisioned for compiler-abstained targets

- Files and members: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`, current lines 207-211, and `TryCreateLanes`, lines 421-450; `CallableVerificationPolicy.VerifyTargetAsync`, lines 15-20.
- Mechanism: Lane count uses total `targets.Length` even though failed preparations return typed `Unknown` without SMT.
- Impact: An all-unsupported manifest can become `Failed/BackendUnavailable`; a mixed run can abort because an unnecessary extra lane fails even though the supported target needs only one.
- Safe evidence: Use an unsupported-callable test with a factory throwing `DllNotFoundException`; for a mixed one-supported and one-unsupported run with parallelism 2, let the factory succeed once and then throw.

## Wave 12.2. HIGH - Summary-call dependency closure is not bound to the selected summary

- File: `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`
- Members: `DecodeBody`, current lines 706-795; `ValidDependencyEvidence`, lines 1149-1185; `SummaryInstantiationSha256`, lines 841-872
- Mechanism: Dependency rows are validated independently and only for ordering; an empty set is valid, and no equality with the selected summary's complete transitive closure is required. The instantiation digest excludes dependency provenance. A resealed artifact can delete or replace closure while retaining relation and origin.
- Impact: A stale stronger relation can survive a changed transitive callee and feed worker assumptions, enabling a false proof and false provenance labels.
- Safe evidence: The producer copies `summary.DependencyProvenance`; an empty array skips all `ValidDependencyEvidence` checks and `Decode` accepts it.

## Wave 12.3. MEDIUM - Composed relational summaries discard callee normal-completion guards

- File: `SharpProof.Summaries/IrRelationalSummaryBuilder.cs`
- Member: `Run.ApplyCall`
- Current lines: 441-477, especially 444-463
- Mechanism: The method validates and instantiates `dependency.NormalCompletion` but conjoins only `instantiated.NormalRelation` into the caller predicate; the completion term is unused.
- Impact: Wrappers around partial helpers gain spurious normal executions, causing false counterexamples, `Unknown`, and proof loss.
- Safe evidence: A callee `Div(x)=1/x` has completion `x!=0`, and caller `Wrap` returns a summary call. At `x=0`, caller completion remains admitted; a postcondition `x!=0` for normally returning `Wrap` can be spuriously refuted.

## Wave 12.4. MEDIUM - Successful callable envelope has no decodability invariant

- Files and members: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`, `Validate`, current lines 377-394, and `HasFeatureScopeParity`, lines 504-574, with success at 532-573; contrast `CompilerLoweredArtifact.Decode`, lines 280-293.
- Mechanism: Success validates effect payload and clause and assumption IDs and evidence but never requires `Graph` or validates body, variables, roots, or portable IR. The feature hash authenticates the malformed payload as-is.
- Impact: Manifest serialization and deserialization admit an artifact that cannot hydrate, causing late worker failure or denial of service.
- Safe evidence: Set `Graph=null` on a valid success and recompute the feature hash. Manifest serialization passes, while `DecodeCallables` throws incomplete.

## Wave 12.5. HIGH - Summary-evidence identity is not bound to source declaration or IL token

- File: `SharpProof.CompilerArtifact/CompilationFingerprint.cs`
- Members: `ValidSummaryEvidence`, current lines 108-149; `ValidSummaryEvidenceRow`, lines 151-204, with Source at 157-171 and Implementation IL at 173-188
- Mechanism: `CallIdentity` is checked only syntactically. Source evidence validates span and tree geometry, not declaration identity. IL evidence validates module tuple and token greater than zero, not MethodDef, signature, or identity.
- Impact: Summary and provenance can attach to the wrong source or metadata method, undermining audit and allowing a false relation to be applied for an unsound proof.
- Safe evidence: Relabel a source-summary call and row to a shape-compatible ID while retaining the original span, or set IL token to `int.MaxValue` with coordinated identity. Validation accepts the independent fields.

## Wave 12.6. HIGH - Synchronous using treats unsealed-class nonvirtual IDisposable implementation as exact

- File: `SharpProof.Effects/UsingDisposalEffectResolver.cs`
- Members: `ResolveDispose`, current lines 471-483; `ResolveResource`, lines 432-439; `IsDispatchUncertain`, lines 501-509
- Mechanism: The resolver maps `named.FindImplementationForInterfaceMember` to `Base.Dispose`; uncertainty is false when the method is nonvirtual even if `Base` is unsealed. A derived class can reimplement `IDisposable` with a new `Dispose`, and interface dispatch selects the derived method.
- Impact: `using(Base x)` can omit derived Dispose writes and throws and falsely prove effects or catch reachability.
- Safe evidence: An unsealed `Base : IDisposable` has pure `Dispose`; sealed `Derived : Base, IDisposable` has a new effectful `Dispose`. A Base-typed value holding Derived executes `Derived.Dispose`.

## Wave 12.7. HIGH - Interface-constrained type-parameter boxing is falsely classified fresh

- File: `SharpProof.Effects/ConversionOwnershipClassifier.cs`
- Member: `ClassifyConversionRegion`
- Current lines: 539-556, boxing branch at 552-555
- Mechanism: A T-to-interface conversion for `T where T:IBox` is Roslyn boxing when T is not known reference, but a class instantiation preserves caller object identity. The classifier always returns Fresh, erasing the parameter region.
- Impact: A generic method can be certified without caller-visible writes although a legal reference instantiation mutates the supplied object.
- Safe evidence: `Set<T>(T value) where T:IBox { ((IBox)value).X=1; }`; passing a `BoxClass` instance mutates it, but the effect is attributed Fresh.

## Wave 12.8. HIGH - Ref-like alias scan does not follow helper calls that rebind ref fields

- File: `SharpProof.Effects/ConversionOwnershipClassifier.cs`
- Members: `BuildLocalRegions`, current lines 140-203; `MethodMayIntroduceUnknownRefAlias`, lines 466-493
- Mechanism: The scan recognizes only direct `IsRef` assignments in the invoked method body. A wrapper calling a helper that rebinds a ref field has no direct assignment, so no Unknown region or transitive check is added.
- Impact: A later write uses stale or empty ownership and real static or caller mutation disappears.
- Safe evidence: `Wrapper(ref A a){Rebind(ref a);}` where `Rebind` points `a.Cell` at static `s`; a later `a.Set` writes `s`, but Wrapper is not flagged.

## Wave 12.9. HIGH - Advisory activation misses closed preconditions on CompilationReference accessors

- Files and members: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `MayContainExternalClosedPreconditions`, current lines 313-351; `TypeContainsClosedPrecondition`, lines 502-526, especially 508-520.
- Mechanism: The scan uses `type.GetMembers().OfType<IMethodSymbol>()` plus nested types. Property and event accessors are exposed through property and event symbols rather than direct method members, so indexer and accessor parameter or return attributes are missed. The PE path scans parameter attributes and does not share this gap.
- Impact: An advisory compilation containing only such a reference activates None and registers no session or actions, so invalid property, indexer, or event access receives no SP0027.
- Safe evidence: Use a `CompilationReference` with a closed return or parameter attribute on an accessor and no local candidate syntax; activation misses it. Accessors must be enumerated explicitly.

## Wave 12.10. HIGH - Sibling try regions can map to the first sibling finally

- File: `SharpProof.Effects/EffectMethodNodeBuilder.cs`
- Member: `CreateFinallyEntries`
- Current lines: 515-533, especially `FirstOrDefault` at 518-522; consumers at 428-440 and 463-473
- Mechanism: For each try, the method selects the first Finally region sharing the same parent instead of the finally owned by that try. Sequential sibling try/finally regions share a parent, so the second try can map to the first finally and the real later finally may never be seeded.
- Impact: Effects in a later finally can be omitted, yielding an unsound effect summary.
- Safe evidence: Use two sequential try/finally statements where only the second finally mutates state; static region and control-flow trace shows both try mappings can select the first sibling finally.

## Wave 12.11. HIGH - Definitely diverging explicit static constructor is erased from its own type's static method entry

- File: `SharpProof.Effects/EffectMethodNodeBuilder.cs`
- Members: `Build`, current lines 87-104, with static arm at 95-99; `StaticConstructorCanAffectEntry`, lines 289-304
- Mechanism: The helper returns false for a source static constructor with no lexical throw when `MethodCanCompleteNormally` is false; pure divergence is treated as unable to affect entry and the boundary is omitted.
- Impact: A static method can be summarized as terminating and side-effect-free although runtime never enters it because type initialization diverges.
- Safe evidence: `static C(){while(true){}} static void M(){}` and direct code-path trace.

## Wave 12.12. MEDIUM - May-throw API specifications are admitted as normally total

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`, `TryAdmitSpecCallEffects`, current lines 383-400, and `TryPrepareSpecCall`, lines 273-296; model `CompilerArtifactModel.generated.cs`, lines 154-159.
- Mechanism: Admission checks Effects and cardinality but not `template.Facets.Throws`; `Effects==None` returns true. Default `Math.Abs(int)` is MayThrow, yet prepared specification calls carry no throws or normal-completion field, so worker `ApplySpec` cannot recover the condition.
- Impact: Verification includes impossible exceptional inputs and can reject valid normal-return contracts.
- Safe evidence: `[Ensures(x != int.MinValue)] static int F(int x) => Math.Abs(x);`; at `int.MinValue` runtime throws, but the modeled call fabricates normal completion. Admission should require `SpecThrowBehavior.DoesNotThrow` or carry a sound completion predicate.

## Wave 12.13. LOW - Mixed missing and extra ContractFor surfaces suppress the extra diagnostic

- File: `SharpProof.Analyzer.Core/ContractForCompanionValidator.cs`
- Member: `Validate`
- Current lines: 43-45 and candidate loop 110-129, especially 121-128
- Mechanism: Extra-candidate diagnostics are emitted only if every target has a unique match. With target methods `M` and `Missing` and companion methods `M` and `Ghost`, `Missing` receives SPCF0004 while `Ghost` is suppressed.
- Impact: Fixing the missing member reveals a second avoidable diagnostic on a later run instead of reporting the complete invalid surface at once.
- Safe evidence: Direct loop and gate trace on that interface and companion shape.

## Wave 12.14. LOW - Effect exception diagnostics collapse distinct types sharing a simple name

- File: `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs`
- Member: `FormatDiagnosticTypes`
- Current lines: 385-390; consumed at 165-180
- Mechanism: Exception symbols are projected to `type.Name` and then `Distinct`, so `A.Collision` and `B.Collision` become one displayed entry.
- Impact: Diagnostics omit material type identity and can mislead remediation when different namespaces define the same exception name.
- Safe evidence: Use two thrown exception types with identical simple names in different namespaces.

## Wave 12.15. HIGH - User-defined conversions are erased before catch-filter cancellation proof

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Members: `FilterExcludesCancellation`, `Unwrap`, and `PatternExcludesCancellation`
- Current lines: 187-208, 211-225, and 229-264, especially 217-219
- Mechanism: `Unwrap` strips every `IConversionOperation`, including user-defined conversions, and then reasons as though the pattern tested the caught exception itself. A conversion can map every Exception, including OCE, to a nonnull unrelated wrapper, making the catch execute while the analyzer concludes the wrapper pattern excludes cancellation.
- Impact: A broad cancellation-swallowing catch evades SPMETA003.
- Safe evidence: Define `Wrapper` with an explicit operator from Exception returning a new Wrapper, then use `catch (Exception e) when ((Wrapper)e is Wrapper) { }`; runtime catches OCE but the analyzer suppresses the diagnostic.

## Wave 12.16. HIGH - Audited cancellation boundaries trust parameter identity after token reassignment

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Members: `ReifiesWorkerVerificationCancellation`, `ThrowsIfCallerCancellationRequested`, `ReifiesCallerCancellation`, and `ReferencesParameter`
- Current lines: 522-575, 622-691, and 731-744
- Mechanism: Checks only that `IsCancellationRequested` or `ThrowIfCancellationRequested` resolves to the designated parameter symbol; parameters are mutable and there is no reaching-definition or write check. Reassigning `cancellationToken` or `callerCancellation` to default before the accepted catch shape makes incoming cancellation false while still satisfying the analyzer.
- Impact: A one-line reassignment defeats the caller-cancellation guarantee and can reify cancellation as timeout without SPMETA003.
- Safe evidence: Add the reassignment before a throwing try in an otherwise accepted analyzer fixture; runtime with a precanceled incoming token follows the timeout path.

## Wave 12.17. MEDIUM - Cooperative timeout termination can leave descendants running past the hard deadline

- File: `SharpProof.Host/LinuxWorkerProcess.cs`
- Member: `Terminate`
- Current lines: 170-215, especially direct-child SIGTERM at 179, root `WaitForExit` at 193, and tree kill at 193-200
- Mechanism: SIGTERM is sent only to the direct child. If it exits while a descendant remains, `WaitForExit` returns true and the entire-process-tree kill branch is skipped. Parent-death signaling covers only the direct worker.
- Impact: Descendant CPU, memory, handles, or side effects can continue after timeout is reported.
- Safe evidence: A shell starts background sleep and traps TERM with exit 0; the shell exits during cooperative grace, making tree kill unreachable while sleep survives.

## Wave 12.18. MEDIUM - Enabled-analyzer retention probe discards the analyzer instance for every compilation

- File: `SharpProof.Gates/Performance/PerformanceGate.cs`
- Members: `AnalyzeEnabledCompilation`, current lines 832-851, especially new `SharpProofAnalyzer` at 841-846; caller `MeasureEnabledAnalyzerRetention`, lines 805-830
- Mechanism: Each of 40 compilations uses a new analyzer, and only compilation weak references escape. Instance-state caches and leaks die with each analyzer, unlike production Roslyn hosts that reuse an analyzer across compilations.
- Impact: Per-analyzer accumulation can pass with zero retained compilations.
- Safe evidence: An analyzer instance `List<Compilation>` populated during analysis is invisible to the probe; reusing one analyzer exposes retained weak references. This is distinct from Wave 11.17's discarded warmup references and static first-use mechanism.

## Wave 12.19. MEDIUM - Package-build benchmark has no internal wall-time boundary

- File: `SharpProof.Gates/Performance/PerformanceGate.cs`
- Members: `RunDotnetAsync`, current lines 608-679, especially `WaitForExitAsync(cancellationToken)` at 644-647; callers `MeasureUnannotatedAdvisoryPackageBuildsAsync`, lines 434-474. Program entrypoints supply default noncancelable tokens.
- Mechanism: Restore and build wait indefinitely unless an external caller cancels; standalone gate commands establish no CTS.
- Impact: An analyzer, compiler, or MSBuild deadlock wedges the gate forever and produces no bounded failure evidence.
- Safe evidence: A controlled blocking build step never reaches the kill path because Program passes `CancellationToken.None`.

## Wave 12.20. HIGH - Cache GetOrAdd value factories are never inspected

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `AnalyzeWrite` and `IsNonCacheableSemanticAnswer`
- Current lines: 12-22 and 65-90
- Mechanism: `GetOrAdd` is a write method, but analysis checks only arguments' immediate values. A factory argument is a delegate conversion or lambda whose returned semantic answer is never traversed.
- Impact: Lazy cache insertion can persist Unknown, timeout, or failure while bypassing SPMETA010.
- Safe evidence: `cache.GetOrAdd("k", static _ => Answer.Unknown);` reaches GetOrAdd analysis, but neither the key nor lambda is recognized as a noncacheable Answer.

## Wave 12.21. HIGH - Explicit numeric enum conversions erase the semantic-answer type

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `IsNonCacheableSemanticAnswer`
- Current lines: 65-90, especially 68-73 and 88-89
- Mechanism: Every non-user-defined conversion is replaced by its operand. `(Answer)0` becomes an int literal; fallback sees a nonsemantic type and treats it as safe even when zero means Unknown.
- Impact: An unsafe semantic answer can be directly cached using its underlying enum value.
- Safe evidence: With `enum Answer { Unknown=0, Proven=1 }`, `cache.Write((Answer)0)` analyzes the integer literal and is accepted.

## Wave 12.22. HIGH - SPMETA010 classifies unstable enum answers by surface names instead of semantic values

- Files and members: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `IsNonCacheableSemanticAnswer` and `IsNonCacheableName`, current lines 65-90 and 350-358; product example `SharpProof.Analyzer.Core/AnalyzerSemanticOutcome.cs`.
- Mechanism: Enum members are classified by their field names rather than canonical underlying values, while const fields declared outside the enum fall through to blanket `ConstantValue` trust. The name catalog also omits canonical abstention names such as `Abstain`/`Abstained`. Safe-named enum aliases, external const aliases, and genuine canonical abstention members are therefore all treated as cacheable.
- Impact: Unknown, timeout, failure, or abstention states can be persisted as stable semantic facts without SPMETA010 and reused across later compilations.
- Safe evidence: All of these bypass the rule: `enum Answer { Unknown=0, Retry=Unknown }` followed by `cache.Write(Answer.Retry)`; `const Answer Abstain = Answer.Unknown; cache.Write(Abstain)`; and `cache.Write(AnalyzerSemanticOutcome.Abstained)`.

## Wave 12.23. HIGH - Nested-callable filtering drops unsafe writes inside the callable

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `Root`, `GetReachingLocalValues`, `TransferLocalValues`, and `IsInsideNestedCallable`
- Current lines: 56-63, 117-181, 204-227, and 268-279
- Mechanism: `Root` climbs to the outer method; target discovery finds a nested cache use, but transfer excludes definitions inside lambdas and local functions. An outer safe initialization remains the reaching value when the nested callable overwrites a captured local with Unknown immediately before caching.
- Impact: Captured-local writes inside nested callables can persist unstable answers without SPMETA010.
- Safe evidence: `var answer=Answer.Proven; Action write=()=>{answer=Answer.Unknown; cache.Write(answer);};`.

## Wave 12.24. LOW - Corpus gate undercounts OSS files when sources share a relative path

- File: `SharpProof.Gates/Corpus/CorpusGate.cs`
- Member: `RunAsync`
- Current lines: 223-232, especially 229-232; catalog identity in `OpenSourceCorpusCatalog.Validate`, lines 131-149 and 254-262
- Mechanism: `OpenSourceFileCount` projects only `method.Path` before `Distinct`, while the catalog keys files by `SourceId` plus Path.
- Impact: JSON and monitoring underreport corpus breadth and can disagree with the validated catalog count.
- Safe evidence: Sources A and B each contribute `Algorithms/Sort.cs`; the catalog validates two files but reports one. Distinctness should use `(SourceId, Path)`.

## Wave 12.25. HIGH - Implementation IL can come from the wrong aliased assembly with the same identity

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
- Members: `TryFindReference`, current lines 253-296; consumed by `TryBuild`, lines 92-122
- Mechanism: Selection matches only assembly identity and module name, returns the first `PortableExecutableReference`, and does not verify symbol identity or aliases. Two aliased references with identical assembly identity and module but different bodies can cause a method bound through the second alias to use the first reference. `MetadataEquals` compares the selected reference with its own file and cannot detect substitution.
- Impact: An exact summary for one implementation can attach to a call targeting another, enabling false proofs.
- Safe evidence: Use two PEs with the same identity, name, signatures, and tokens but opposite Boolean bodies and distinct aliases; call the latter through an extern alias while ordering the former first.

## Wave 12.26. HIGH - Target-incompatible API argument effects collapse into a complete effect-free summary

- Files and members: `SharpProof.Specs/ApiSpecTable.cs`, `ValidateDeclaration`/`NormalizeFacets`, current lines 173-238 and 251-307, especially 265-274; `SharpProof.Effects/ExternalEffectResolver.cs`, `ResolveSpec`/`SpecRegions`, lines 270-360, especially 273-276 and 340-352; `EffectContractMappings.ParameterRegions`, lines 104-107.
- Mechanism: Table validation accepts regional effect bits without checking target shape. A zero-parameter target with `ReadsArgumentState` or `WritesArgumentState` expands through `ParameterRegions(0)` to empty while the summary remains Complete; static targets can likewise carry receiver flags.
- Impact: Trusted rows can erase declared effects and permit unsound propagation or contract acceptance.
- Safe evidence: A static parameterless exact row with `WritesArgumentState`, allocation None, and `DoesNotThrow` is accepted and resolves Complete with no regions.

## Wave 12.27. MEDIUM - Ordinary-label continuation injects nested-callable invocations into executable exception flow

- File: `SharpProof.Effects/ExceptionHandlerReachability.cs`
- Member: `GetGotoTargetContinuation`
- Current lines: 1365-1403, especially descendant invocation harvesting at 1368-1373 and insertion at 1397-1402; consumed by `GetPotentialExceptions`, lines 135-146
- Mechanism: Goto label handling collects every invocation under the labeled statement without excluding lambdas or local functions and pushes those operations as roots, bypassing the main nested-callable skip; fallback also takes a later method-wide invocation.
- Impact: Uninvoked nested code can make catch handlers spuriously reachable and add effects or witnesses.
- Safe evidence: `try { goto L; L: Action a=()=>ThrowApplication(); } catch(ApplicationException){state++;}` never invokes the lambda, but the harvested throw marks the catch reachable.

Wave 12 transport correction: the initial 27-finding marker and section count were superseded by the coordinator's corrected final marker of 28 findings after a late auditor addendum. The following finding completes the same Wave 12 section.

## Wave 12.28. HIGH - Implicit string-formatting calls are absent from catch reachability

- Files and members: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions` traversal and default, current lines 978-982, `PushChildren` default at 1168-1170, and `CanThrowUnknown`, lines 2676-2715; contrast `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanInterpolatedString`, lines 208-260, and `ScanBinary`, lines 190-202; handler gating in `SharpProof.Effects/OperationEffectScanner.cs`, `IsReachable`, lines 1251-1272.
- Mechanism: Roslyn interpolation holes expose the formatted expression, not implicit `ToString` or formatting as an invocation child. The effect scanner explicitly resolves that call, but exception reachability only walks children and never asks the interpolation resolver for exceptions. The same gap applies to built-in string concatenation with a formatted nonstring operand: reachability sees `IBinaryOperation` with null `OperatorMethod`, and `CanThrowUnknown` recognizes only checked, divide, or remainder cases, while `ScanBinary` calls `StringConcatenationEffectResolver.Resolve`.
- Impact: A throwing formatting call can leave its catch marked unreachable, omit catch effects and witnesses, and then be treated as lexically caught, underapproximating the method summary.
- Safe evidence: `try { _ = $"{value}"; } catch (InvalidOperationException) { s_state++; }` or `try { _ = "" + value; } catch (InvalidOperationException) { s_state++; }`, where hole or operand evaluation is nonthrowing but `value.ToString()` throws. The catch write is omitted.

# Read-Only Multi-Agent Bug Audit - Wave 13 - 2026-08-29

This section records 25 findings from exactly 30 fresh read-only auditors. The relay supplied the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 13.1. MEDIUM - Cache byte cap is enforced only after a replayable hit or cacheable write

- Files and members: `SharpProof.Worker/VerificationCache.cs`, `TryReadAsync`, current lines 27-84, and `TryStageCapacity`; `SharpProof.Worker/SharpProofWorker.cs`, lines 324-336.
- Mechanism: Missing, stale, malformed, manifest-mismatched, or nonreplayable entries return before `TryStageCapacity`. After a miss, the worker writes only when the computed response passes `IsCacheable`. Intact canonical entries are therefore never reconciled when a miss produces Proven, Unknown, or another noncacheable result.
- Impact: Reducing `MaximumBytes`, or opening an already over-cap cache, can leave active `*.sharp-proof-cache.json` entries above the documented aggregate limit indefinitely.
- Safe evidence: Populate multiple valid canonical entries under a large cap, instantiate `VerificationCache` with a smaller cap, read a missing or stale key, and produce no cacheable write. The active-entry byte sum remains above `MaximumBytes`. This is distinct from Wave 6.10, which concerns interrupted `.rollback` and `.eviction` artifacts.

## Wave 13.2. MEDIUM - Initial lane factory failures are all falsely classified as native backend unavailability

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Members: `VerifyAsync`, current lines 207-211; `TryCreateLanes`, lines 421-465, especially catch at 455-464
- Mechanism: `TryCreateLanes` catches every ordinary exception from injected factory or lane construction, including `InvalidOperationException` and other infrastructure or programming failures, and returns only a message plus false. `VerifyAsync` maps every false result to `Failed/BackendUnavailable`, `backend.unavailable`, and `BackendUnavailable` claim reasons. Unlike renewal at lines 525-531, no `Program.IsBackendUnavailable` classification survives initial construction.
- Impact: Infrastructure defects are published as native SMT outages, corrupting manifest-bound status, retry and remediation decisions, and telemetry for every target.
- Safe evidence: Construct an internal test worker with `new SharpProofWorker(() => throw new InvalidOperationException("factory defect"))` and a valid one-target request. The response is `Failed/BackendUnavailable` with `backend.unavailable` and all claims BackendUnavailable rather than `InfrastructureFailure`. This is distinct from Wave 9.1's broad managed-file exception classification.

## Wave 13.3. MEDIUM - Renewal collision detection disposes another lane's active backend

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Member: `VerificationLane.Renew`
- Current lines: 490-531, especially collision test at 510-514
- Mechanism: If a buggy factory returns a backend currently owned by another lane, the `lanes.Any` branch detects the identity collision but unconditionally calls `Dispose` on `replacement`. That object is borrowed from and may be concurrently executing in the other lane; the current lane has no ownership. The branch also double-disposes `prior` when the factory returns the just-disposed prior instance.
- Impact: A contained renewal or factory error can asynchronously tear down a healthy lane's live SMT backend, causing spurious infrastructure outcomes or a native dispose/check race and expanding one bad replacement into in-flight corruption.
- Safe evidence: With parallelism 2, retain backend B from lane 2; make lane 1 time out and have lane 1 renewal return B while lane 2 blocks in `CheckAsync`. `Renew` lines 511-514 calls `B.Dispose` before reporting BackendUnavailable. A test backend can observe Dispose while CheckAsync is active.

## Wave 13.4. MEDIUM - Protocol identity fields admit malformed UTF-16 and collapse distinct authorities

- Files and members: `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, `RequestRules`, lines 810-822, `IsSourceLocationValid`, lines 833-838, and `ManifestCallableRules`, lines 839-847; `AreDistinctNonblank` in `SharpProof.Worker.Protocol/ProtocolJson.cs`, lines 713-716; `ProtocolManifest.ComputeManifestHash`, lines 44-50; `ProtocolJson.SerializeRequest`, lines 61-64, `ComputeRequestHash`, lines 66-69, and `SerializeResponse`, lines 90-94.
- Mechanism: Wire and identity strings are accepted without well-formed UTF-16 checks. `Encoding.UTF8.GetBytes` uses replacement fallback, and `System.Text.Json` serializes lone surrogate code units as U+FFFD. Distinct accepted strings containing lone `\uD800` versus `\uD801` serialize and encode identically. `CompilerManifest.Path` then produces the same request hash for distinct in-memory paths; manifest locations, IDs, and assumption IDs can similarly collapse, and JSON round-trip rewrites both to U+FFFD.
- Impact: Request and manifest hashes are not injective over the accepted identity space. Authenticated path and provenance values can collapse or change at the wire boundary, so distinct authorities share hashes and accepted objects fail faithful round-trip.
- Safe evidence: Runtime UTF-8 encodes lone D800 and D801 as `EF-BF-BD`; `System.Text.Json` serializes D800 as `"\uFFFD"`. Both have UTF-16 length 1, so the manifest length prefix does not distinguish them. Existing BUGS entries cover malformed UTF-16 in IR and spec tables, not protocol request and manifest identity.

## Wave 13.5. HIGH - Null nonvirtual method-group conversion is modeled as NullReferenceException instead of ArgumentException

- Files and members: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanMethodReference`, lines 404-420; supporting `OperationCompletionEvaluator.CanCompleteMethodReference`, lines 716-723; `ExceptionHandlerReachability.GetPotentialExceptions`, lines 965-975, and `GetPotentialNullReceiver`, lines 1562-1595.
- Mechanism: Every nonstatic `IMethodReferenceOperation` passes through `PotentialNullReceiver`, which contributes `NullReferenceException` for a nullable receiver. For a nonvirtual instance method group such as `Action callback = value.Run`, the compiler creates a closed delegate without `ldvirtftn`; a null target is rejected by the delegate constructor with `ArgumentException`, not NRE.
- Impact: Effect summaries omit the real exception and invent the wrong one; throws contracts can pass incorrect coverage, and `catch (ArgumentException)` can be deemed unreachable, suppressing its effects.
- Safe evidence: A Linux tooling probe with sealed `Box`, nonvirtual `Run`, null `Box`, and `Action callback = value.Run` threw `System.ArgumentException: Delegate to an instance method cannot have null 'this'.` The scanner's only modeled null-receiver type is NRE.

## Wave 13.6. MEDIUM - Publication deletions are acknowledged without durable directory synchronization

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Members: `ResetPublicationSet`, current lines 211-225; `DeleteIfUnprotected`, lines 333-362; `BindPublicationSet` rollback, lines 534-548
- Mechanism: Each path calls `File.Delete` and returns or completes rollback without fsyncing the containing directory, including output directories and `.sharpproof-publication` marker directories.
- Impact: After a reported successful reset or invalidation, or a failed marker-creation rollback, abrupt host or container failure can recover deleted outputs and markers independently. Invalidated results can reappear, or marker and output state can recover partially and wedge later acquisition or reset.
- Safe evidence: The same class defines `SyncDirectory` as open plus retrying fsync at lines 146-171 and calls it after marker creation at 526-530; deletion paths never call it. Linux unlink durability requires syncing the containing directory.

## Wave 13.7. MEDIUM - Wide formula encoding ignores cancellation and the per-query solver budget

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Members: `CheckCore`; `QueryEncoder.Encode`, `EncodeUnary`, `EncodeBinary`, and `EncodeConditional`
- Current lines: 131-146 and 418-550, especially 429-451, 456, 469-470, and 532-534
- Mechanism: Cancellation is checked immediately before encoding each assumption and only after the entire goal is encoded. Recursive encoding receives no token and never checks cancellation. The 256-level validation limits depth, not width or total unique nodes. Z3 rlimit applies only when `solver.Check()` begins at line 146, after managed traversal and native AST construction.
- Impact: A canceled check can keep consuming CPU and native and managed memory while constructing a large shallow formula, bypassing the advertised per-query boundary and delaying worker cancellation and timeout recovery.
- Safe evidence: A balanced `AndAlso` tree of many unique predicates such as `v < k` has logarithmic depth, one model variable, and arbitrarily many nodes. After line 131, `EncodeBoolean` constructs every node without token checks; `Context.Interrupt()` does not poll cancellation in managed traversal and the solver budget starts only at line 146. This is distinct from recorded model-variable and repeated depth-validation mechanisms.

## Wave 13.8. MEDIUM - Ignored source files are imported under the pinned Git commit

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Members: `ImportAsync`, current lines 73-82 and 119; `DiscoverSources`, lines 251-277
- Mechanism: `git status --porcelain` does not report ignored files. `DiscoverSources` enumerates every on-disk `*.cs` beneath `Algorithms` and `DataStructures`, filtering only `bin` and `obj`, without requiring Git tracking.
- Impact: Ignored local source can enter the checked-in corpus and affect compilation, selected methods, and analyzer verdicts while the manifest falsely attributes it to the recorded upstream commit; the corpus is not reproducible from that commit.
- Safe evidence: In a disposable upstream clone, add `Algorithms/Injected.cs` to `.git/info/exclude`, create it, and observe `git status --porcelain` remains empty while `Directory.EnumerateFiles` includes it and the importer adds its content.

## Wave 13.9. MEDIUM - Mutable or custom substitution dictionary bypasses ownership and type validation

- File: `SharpProof.Specs/ApiSpecInstantiation.cs`
- Members: `ApiSpecInstantiator.InstantiatePostconditions` and `Instantiation.Variable`
- Current lines: 44-70 and 163-168
- Mechanism: The method validates substitutions by enumerating the caller-owned `IReadOnlyDictionary`, then passes the same unsnapshotted object into `Instantiation`; variable expansion later calls `TryGetValue` without rechecking factory ownership, nullness, or type. A mutable or concurrently changed dictionary, or a custom dictionary with different enumeration and lookup views, bypasses `ForeignIrTerm` and `TypeMismatch` checks.
- Impact: A successful result can contain an unchecked foreign or null term, violating the destination-factory invariant and causing downstream ownership or null exceptions.
- Safe evidence: Use a template with a bare Boolean spec variable. Enumeration yields a destination-factory Boolean term while `TryGetValue` returns a foreign-factory Boolean term or null. Lines 167 and 82 return `Succeeded` containing the unchecked value because no factory operation occurs for a bare variable. Snapshotting to an immutable dictionary before validation and use closes the gap.

## Wave 13.10. MEDIUM - Compiler-owned output inventory omits SDK-generated apphost paths

- File: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`
- Members: `_SharpProofInitializeVerify`; downstream `SharpProofVerify`
- Current lines: 79-98, especially 81-97; task input at line 112; scheduling at 227-230
- Mechanism: `_SharpProofCompilerOwnedOutput` omits SDK-generated apphost paths, including intermediate `apphost` and final native executable. `$(TargetPath)` covers the managed DLL, not apphost, so publication collision validation receives an incomplete compiler-output inventory.
- Impact: Configured request, result, manifest, or SARIF can alias apphost without rejection. For an intermediate-apphost collision, verification publishes JSON after `CoreCompile`; later `_CreateAppHost` can be skipped because JSON is newer than inputs, and JSON can be copied as the application launcher. Build and verification can report success while producing a nonrunnable executable.
- Safe evidence: In a fresh Linux SDK executable project with `UseAppHost=true` and `SharpProofVerifyResultFile=$(IntermediateOutputPath)apphost`, lines 81-97 add no apphost. `SharpProofVerify` is `AfterTargets="CoreCompile"` at 227-230, while SDK `_CreateAppHost` depends on `CoreCompile`, placing verification before apphost creation. This is distinct from Wave 7.36: paths are checked here, but compiler-output inventory is incomplete.

## Wave 13.11. MEDIUM - Nested callable syntax falsely disqualifies a supported selected outer method

- File: `SharpProof.Analyzer.Core/LanguageSubsetGate.cs`
- Members: `ClassifyEffects`, current lines 67-91; `SupportsCallable`, lines 116-128; `ContainsUnsafeSyntax`, lines 275-278; diagnostic path `AnalyzerFeaturePipeline.AnalyzeOperationBlock`, lines 259-295
- Mechanism: Operation walk and unsafe-syntax check traverse all descendants without stopping at local-function or lambda boundaries. An unused nested callable's unsupported operation or unsafe block abstains the selected outer method.
- Impact: A supported outer method receives false SP0047 or `Abstained` and skips verification.
- Safe evidence: `[EnforcePure] static int Outer() { void Dead() { unsafe { int* p = null; } } return 1; }` never calls `Dead`, but line 277 finds its unsafe statement and reports incompleteness.

## Wave 13.12. MEDIUM - Non-consuming delegate uses are mistaken for execution or escape

- File and members: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `IsAnonymousExecutableOrEscaped`, `CanReachConsumption`, `TryGetLocalDestination`, `IsNonExecutingObservation`, and `IsAssignmentTarget`, current lines 500-753 and 1166-1276.
- Mechanism: The nonexecuting whitelist is incomplete. It treats delegate metadata reads, write-only `out` argument locations, and compound-assignment reads used only to construct an unused combined delegate as consumption of the prior target, although none invokes or exposes that target.
- Impact: Dead local-function and lambda bodies are analyzed, producing false SP0027 diagnostics or `Refuted` outcomes.
- Safe evidence: False consumption occurs for `_ = d.Method`, `Reset(out callback)` where the callee overwrites the local, and `callback += Safe` when the resulting local is never called, returned, or stored.

## Wave 13.13. MEDIUM - Containment failures are returned as generic exit code 3

- Files and members: `SharpProof.Worker.Launcher/Program.cs`, `Program.RunMain`, current lines 172-184, especially 177-180, and `ClassifyLauncherFailure`, lines 194-200; `LauncherProjections.generated.cs`, `NoResultFailure`, lines 24-32, and `ExitCode`, lines 12-21.
- Mechanism: A containment failure sets `exitCode=125` and produces a valid `Failed/ContainmentFailure` response. `ValidateAndReport` projects every Failed status to 3; because the response is valid and `resultExitCode` is nonzero, lines 177-180 return 3 before the preserved launcher exit code at 184.
- Impact: Callers cannot distinguish containment unavailability, breaking the modeled exit-125 contract.
- Safe evidence: An exception classified by lines 194-200, or worker exit 125 with no result, deterministically yields 125, then a valid fail-closed Failed response, then `resultExitCode=3`, then return 3. Generated `NoResultFailure(125)` confirms that 125 is intended to persist.

## Wave 13.14. HIGH - Neutral-named semantic result construction bypasses SPMETA010

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `IsNonCacheableSemanticAnswer`
- Current lines: 65-90, especially 79-80
- Mechanism: `IObjectCreationOperation` is classified solely by constructed type name. A semantic type such as `VerificationResult` is declared cacheable without inspecting constructor arguments even when built from `Answer.Unknown` or `Status.TimedOut`.
- Impact: A transient or unknown result enters a semantic cache without an error diagnostic.
- Safe evidence: `cache.Write(new VerificationResult(Answer.Unknown))` reaches the object-creation arm; the type is semantic but its name is not noncacheable, so the method returns false before examining Unknown.

## Wave 13.16. HIGH - Audited worker cancellation accepts an arbitrary no-op response helper

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Member: `ReifiesWorkerProgramCancellation`
- Current lines: 379-475, especially 415-434
- Mechanism: The exemption verifies only that a catch awaits a correctly shaped local function named `Respond` and passes a canceled response; it never inspects the body or proves publication.
- Impact: Worker cancellation can be swallowed and reported as successful process completion while SPMETA003 is suppressed.
- Safe evidence: A matching `Respond(WorkerVerifyResponse _)` that returns 0 satisfies all checks; `return await Respond(Create(...Canceled))` is accepted with no canceled response emitted.

## Wave 13.18. MEDIUM - Compact shared IR DAGs expand exponentially into generated C#

- File: `SharpProof.Testing/IrCSharpDifferentialOracle.cs`
- Members: `TryCreateProgram` and `TryAppendExpression`
- Current lines: 98-100, 117-119, and 208-224
- Mechanism: The recursive emitter expands both binary children without memoizing shared terms or introducing temporaries; `TryCreateProgram` performs the full expansion twice and discards the first text.
- Impact: A small valid DAG can exhaust memory or stall fuzz and test work before compilation.
- Safe evidence: Repeatedly set `t = factory.Binary(Add, t, t)` 30 times. The factory retains about 31 terms, but each traversal emits about 2^30 variable occurrences, twice.

## Wave 13.19. MEDIUM - Shared nested sequence values cause exponential result comparison

- File: `SharpProof.Testing/IrCSharpDifferentialOracle.cs`
- Members: `ValuesAgree` and `SequenceAgrees`
- Current lines: 435-471
- Mechanism: Recursive comparison has no visited `(IrValue, Array)` pair set. `ToRuntimeValue` preserves sharing through a conversion cache, but `SequenceAgrees` retraverses every repeated child independently.
- Impact: A compact valid sequence result can stall or terminate the test process after successful execution.
- Safe evidence: Nested sequence values `[child, child]` using the same child at every level construct and convert linearly but compare in approximately 2^depth recursion.

## Wave 13.20. MEDIUM - Frontend lowers unreachable CFG blocks and lets them add global abstentions

- File: `SharpProof.Frontend/RoslynProgramLowerer.cs`
- Members: `LoweringSession.Lower`, current lines 82-98; `LowerStatement`, lines 168-171; `SelectBlocks`, lines 552-580
- Mechanism: `SelectBlocks` follows both CFG successors without checking `BasicBlock.IsReachable`; Roslyn retains constant-condition unreachable blocks, which are selected and lowered and can abstain globally.
- Impact: Valid methods become `UnsupportedBody` solely because of dead unsupported syntax.
- Safe evidence: `static long M(){ if(false){ object dead = new(); } return 1L; }` has an unreachable initializer, but traversal follows its predecessor edge and object creation causes inexact lowering. Other CFG consumers filter `IsReachable`.

## Wave 13.22. MEDIUM - Bounded descendant cleanup performs unbounded quadratic process-table scans

- File: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`
- Members: `StopDescendants`, current lines 204-279, especially 214-216, 224-269, and 278; `DescendantProcessIds`/`ReadProcessParents`, lines 308-376
- Mechanism: The deadline is checked only at the outer loop. An iteration scans all `/proc`, then performs another full process-parent scan for every descendant before the next time check; the final completeness check is unrestricted. With D descendants and P processes, a nominally bounded operation permits O(D*P) synchronous reads.
- Impact: Many descendants can delay cleanup far beyond 100 or 750 ms budgets and push builds toward project timeout.
- Safe evidence: With N sleeping descendants and a 1 ms budget, the code must finish the initial scan and up to N full rescans before line 214 observes expiry.

## Wave 13.23. MEDIUM - Effect authority accepts a source-tree tuple unrelated to its claim location

- File: `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs`
- Member: `CompilerEffectAuthority.Matches`
- Current lines: 79-115, especially 101-115
- Mechanism: `authority.Source` must equal the manifest claim location, but final validation only checks that `SourceTreeOrdinal`, path, tree SHA, and line-map SHA describe some tree; it never calls `CompilerSourceLocationAuthority.IsBound` for `authority.Source`. A location bound to tree A can carry an internally valid tuple for tree B and hydrate.
- Impact: Effect authority has contradictory physical provenance and no longer proves which captured tree supplied the claim.
- Safe evidence: With two snapshots, keep `authority.Source` at the expected tree-0 claim, set ordinal and tree fields exactly from tree 1, and retain payload. Lines 79-96 pass, and 111-115 compare only tree 1 and return true. This is distinct from the recorded stale-hash mutation.

## Wave 13.24. MEDIUM - Frontend failure minimization can replace a semantic mismatch with an unrelated compile error

- Files and members: `Tools/SharpProof.Fuzz/FuzzRunner.cs`, `FuzzRunner.RunAsync`, current lines 268-280, especially 273-277; `Tools/SharpProof.Fuzz/FrontendFuzzing.cs`, `FrontendDifferentialOracle.CompareBatch`, lines 1004-1013; `CSharpStructuralShrinker.Minimize`, lines 1874-1903; `GetCandidates`/`TryReplaceChild`, lines 1931-1982 and 2000-2034.
- Mechanism: The shrink predicate preserves only `Status == Mismatch`; `CompareBatch` also labels generated-C# compile failure as Mismatch. Type-preserving subtree replacement can create an invalid constant expression, so shrinking accepts a different mechanism.
- Impact: A real differential failure is replaced by an invalid C# compile error, losing the product reproducer.
- Safe evidence: From `(condition ? long.MaxValue : left) + 1`, promote the `long.MaxValue` child to form `long.MaxValue + 1`, which is smaller but has checked compile overflow. Existing tests at 303-327 establish overflow as Mismatch, and the predicate accepts it. The shrinker must preserve failure class and successful compilation.

## Wave 13.25. MEDIUM - Sequence return comparison is structural and can hide identity or aliasing defects

- File: `Tools/SharpProof.Fuzz/FrontendFuzzing.cs`
- Members: `FrontendDifferentialOracle.SemanticValueEquals`, current lines 1711-1736, sequence branch at 1730-1735; caller `CompareOutcomes`, lines 1666-1708
- Mechanism: Sequences compare only length and zipped elements, so distinct same-content arrays compare equal, unlike IR sequence identity semantics using `ReferenceEquals`.
- Impact: Array differential cases can report Agreement when lowering selects the wrong same-shaped array, loses aliasing, or copies, despite observable C# identity.
- Safe evidence: Two distinct `long[] {1,2}` arrays pass lines 1731-1735. Same-content array parameters do not detect swapped binding. Comparison should retain a runtime-array-to-`IrValue` identity map.

# Read-Only Multi-Agent Bug Audit - Wave 14 - 2026-08-29

This section records 51 findings from exactly 30 fresh read-only auditors. The relay supplied the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 14.4. MEDIUM - Partial candidate filtering can admit an excluded ContractFor attribute

- File: `SharpProof.Analyzer.Core/ContractForValidation/ContractForValidationEngine.cs`
- Member: `FindCandidates`
- Current lines: 80-103, especially 83-85 and 89-100
- Mechanism: `includeTree` filters declaration trees, but `symbol.GetAttributes` merges all partial declarations. An unrelated attribute on an included partial makes the syntax prefilter pass while `ContractFor` exists only on an excluded partial.
- Impact: An out-of-scope companion is validated, SPCF diagnostics originate from the excluded tree, and incremental behavior is unstable.
- Safe evidence: The excluded part has `ContractFor`; the included part has `Obsolete`. Removing `Obsolete` removes the candidate.

## Wave 14.5. MEDIUM - Companion surface matching is uncancellable and quadratic

- File: `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs`
- Member: `Validate`
- Current lines: 28-45; first cancellation check at line 64
- Mechanism: The validator builds and materializes `candidates.Where` for every target and `targets.Where` for every candidate, performing 2*T*C `MemberSignaturesMatch` calls plus allocations before checking the token.
- Impact: Large valid or generated surfaces retain analyzer CPU and allocation after cancellation.
- Safe evidence: A token canceled on entry is ignored until both matrices and the completeness map finish.

## Wave 14.6. HIGH - Effect witness is not bound to the replay that should establish it

- Files and members: `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs`, `HasValidOutcome`, lines 100-115, and `HasValidReplay`, lines 138-155; `CompilerResponseEvidenceAuthority.ValidateEffectClaim`, lines 212-220; `CompilerEffectAuthority.Matches`, lines 79-96.
- Mechanism: Witness and replay are validated and compared independently and are never related.
- Impact: A resealed artifact can validate a fabricated Refuted result and source attribution that worker replay could not produce.
- Safe evidence: Retain a real `ZeroAllocations` object-allocation replay; replace the witness with a protocol-valid managed-array-allocation/Allocates witness and matching response and authority, then reseal hashes. All predicates pass although the replay event cannot produce the witness.

## Wave 14.7. MEDIUM - Duplicate ordinary #line geometry prevents exact source-tree authority emission

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`, `ToSourceLocation`, lines 624-642; `CompilerManifestArtifactProducer.Create`, lines 84-86, and `CreateLocationAuthorities`, lines 180-199.
- Mechanism: `ToSourceLocation` discards `Location.SourceTree`; the producer later rediscovers a tree only from mapped path and physical geometry and requires uniqueness.
- Impact: A legal compilation loses its manifest and reports collector failure.
- Safe evidence: `A.cs` and `B.cs` have equal layouts and `#line 1 "shared.cs"`, with claims at the same offset. Both snapshots match geometry, so lookup cannot choose despite Roslyn originally knowing the tree. This is distinct from the enhanced-`#line` offset bug.

## Wave 14.8. MEDIUM - Method-local signatures bypass the implementation-IL resource boundary

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
- Members: `Translator.TryDecodeLocals`, lines 909-924; `Translate`, lines 474-505
- Mechanism: The 64 KiB body and 128-stack limits do not bound the metadata local signature. The decoder materializes all locals without cancellation, then creates an IR variable and default assignment for each local before any instruction or summary cap.
- Impact: A tiny referenced PE method with a huge supported scalar local signature can exhaust or stall the collector.
- Safe evidence: Use repeated Boolean, Int32, and Int64 locals with IL `ldc.i4.0; ret` and `MaxStack=1`; the full array and IR are emitted before validation.

## Wave 14.9. MEDIUM - Indirect calls bypass Contract.Result and Old placement validation

- File: `SharpProof.Contracts/ContractIntrinsicValidator.cs`
- Members: `Validate`, lines 22-30; `Classify`, lines 49-80
- Mechanism: Only `IInvocation` directly targeting an intrinsic is checked; a method-group conversion yields `IMethodReference`, followed by `Func.Invoke`.
- Impact: A shipped ghost method executes outside `Ensures` without SP0024 and throws `InvalidOperationException` from `SharpProof.Attributes/Contract.cs` lines 24-32.
- Safe evidence: `Func<int> r=Contract.Result<int>; return r();`.

## Wave 14.10. MEDIUM - Compound type matching drops metadata signature identity

- File: `SharpProof.Contracts/ContractForSymbolMatcher.cs`
- Members: `TypesMatch`, lines 506-511 and 536-545; reached from `MemberSignaturesMatch`, lines 191-224
- Mechanism: Arrays omit Sizes and LowerBounds; named generics recurse through `TypeArguments` but omit `GetTypeArgumentCustomModifiers`.
- Impact: A metadata target and companion with different encoded array shape or nested `modreq`/`modopt` can be certified exact, misassociating or ambiguating contracts.
- Safe evidence: `G<int modreq(Marker)>` versus `G<int>` shares the generic definition and equal int argument and passes; this contrasts with explicit top-level modifier checks at 202-207 and 348-350.

## Wave 14.12. HIGH - Implicit source constructors erase base-constructor completion and catch reachability

- Files and members: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteConstruction`, lines 901-914; `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetCallableExceptions`, lines 2536-2549.
- Mechanism: An implicit constructor is treated as default-completing with `EmptyPotential`, and its mandatory implicit base call is not followed, although effect resolution peels to the base constructor.
- Impact: A definite base throw can coexist with unreachable catch classification, omitting handler writes and falsely continuing.
- Safe evidence: Use a throwing Base constructor, an empty implicit Derived constructor, and `try { new Derived(); } catch (E) { state++; }`.

## Wave 14.13. HIGH - Lifted nullable division by constant zero is falsely terminal when the operand is null

- File: `SharpProof.Effects/OperationCompletionEvaluator.cs`
- Members: `CanCompleteCompoundValue`, lines 867-888, especially 876-880; `CanCompleteBinary`, lines 1032-1088, especially 1076-1080
- Mechanism: Zero-divisor gates ignore `IsLifted` and nullable absence, while the scanner hazard classifier skips a lifted operator when the operand is definitely null.
- Impact: Reachable later operations are omitted.
- Safe evidence: `int? left=null; _=left/(int?)0; s_state++;` and the compound form return null at runtime, but completion is false.

## Wave 14.14. HIGH - By-value structs erase call-mapped writes to referenced heap state

- Files and members: `SharpProof.Effects/ConversionOwnershipClassifier.cs`, `ClassifyParameter`, lines 75-113, especially 90-99, and `ClassifyConversionRegion`, lines 515-574, especially 558-564; `EffectSummaryOperations.Remap`/`RemapRegions`, lines 112-169, especially 160-164.
- Mechanism: A nonref value-type parameter or conversion maps to Empty even though a copied struct can contain reference fields; callee `Parameter(n)` writes remap to Empty.
- Impact: A trusted or API `WritesArgumentState` boundary can mutate caller heap while the caller appears pure.
- Safe evidence: `Holder` contains a `Cell` reference; trusted `Mutate(Holder h)` increments `h.Cell.Value`, but `ClassifyParameter` returns Empty.

## Wave 14.15. MEDIUM - PreconditionFree=true is ignored for every external instance boundary

- File: `SharpProof.Effects/ExternalEffectResolver.cs`
- Member: `ResolveContract`
- Current lines: 69-119, especially 91 and 98-108
- Mechanism: The decoded flag is forced through `preconditionFree &= method.IsStatic`, making all metadata instance methods, constructors, and accessors incomplete despite explicit certification and no Requires.
- Impact: Precise trusted instance boundaries always degrade to false Unknown or diagnostics.
- Safe evidence: A sealed external instance method with `Complete=true`, `PreconditionFree=true`, no parameters, and no companion necessarily enters the incomplete branch.

## Wave 14.16. MEDIUM - Approved repository origin is unauthenticated local metadata

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Member: `ImportAsync`
- Current lines: 60-97
- Mechanism: The importer checks local HEAD, clean status, and origin URL but never proves that HEAD exists in or is reachable from the approved remote; the origin URL is editable configuration.
- Impact: An arbitrary clean repository can be imported and attributed to the approved upstream.
- Safe evidence: A local repository with required content and license plus the approved literal `remote.origin.url` passes even when its commit never existed upstream.

## Wave 14.17. MEDIUM - Imported bytes are not bound to the recorded HEAD

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Members: `ImportAsync`, lines 60-82 and 99-120; `DiscoverSources`, lines 251-277
- Mechanism: HEAD and status are sampled, then mutable worktree bytes are read; status can omit assume-unchanged and skip-worktree files and races concurrent edits.
- Impact: Modified bytes can be hashed with unchanged prior commit provenance.
- Safe evidence: Mark a tracked C# file assume-unchanged and edit it. Porcelain remains clean, but the importer reads the edited bytes.

## Wave 14.18. MEDIUM - Lexical containment follows an out-of-tree license symlink

- Files and members: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`, `ImportAsync`, lines 125-143, and `EnsureContained`, lines 413-425; `OpenSourceCorpusCatalog.ValidateSource`, lines 325-345.
- Mechanism: `GetFullPath` and `GetRelativePath` do not resolve symlink or reparse targets; writes and reads follow them.
- Impact: Corpus update can overwrite outside the repository, and catalog validation can authenticate an external mutable license.
- Safe evidence: Make `Corpus/third-party` a directory symlink elsewhere. Containment sees a relative contained path, but the write lands in the target.

## Wave 14.19. MEDIUM - Documented corpus import entrypoint cannot enter the required container

- Files and members: `SharpProof.Gates/Corpus/Import-OssCorpus.ps1`, lines 9-24; `README.md`, lines 58-65
- Mechanism: The README invokes the script directly on Windows; the script calls the repository dotnet wrapper instead of `docker compose`, and the wrapper requires an already-running canonical Linux container. An external `C:\work` checkout is not a stock container mount.
- Impact: The supported reproducible update command fails before reaching the importer.
- Safe evidence: A read-only smoke invocation exits 1 with the canonical-container requirement.

## Wave 14.20. MEDIUM - Metamorphic classification divergence can be blessed per variant

- File: `SharpProof.Gates/Corpus/CorpusGate.cs`
- Members: `RunAsync`, lines 114-157; `RenderActualSnapshotAsync`, lines 280-303
- Mechanism: Each variant is checked only against its own snapshot row; semantic expectation constrains `CorpusVerdict`, and no SeedId grouping requires the same `AnalyzerSemanticOutcome` or diagnostic class. Snapshot update records the divergence.
- Impact: A transformation-specific regression passes after update while the metamorphic gate remains green.
- Safe evidence: An expected-Unknown seed variant changes from Unknown to Abstained with an allowlisted diagnostic; verdict and counts remain the same, and no cross-variant check rejects it.

## Wave 14.21. HIGH - Package-build overhead gate can pass when the SharpProof analyzer never loads

- File: `SharpProof.Gates/Performance/PerformanceGate.cs`
- Members: `CreatePerformanceProbeProject`, lines 488-545, especially 506-540; `RunDotnetAsync`, lines 608-678, especially 667-678; aggregation at 102-137 and 234-250
- Mechanism: The temporary project points Analyzer items to bin paths but verifies neither the files and dependencies nor treats load warnings as errors. Exit code alone determines success, and `AnalyzerDriverRunCount` is a separate in-process probe.
- Impact: Release performance evidence can falsely certify a near-baseline build with a missing or unloadable analyzer.
- Safe evidence: A missing analyzer yields a warning and exit 0 under normal SDK behavior; no `File.Exists`, hash, or load assertion exists.

## Wave 14.22. HIGH - Worker performance evidence is not bound to the binaries measured

- Files and members: `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs`, `VerifyCooperativeLauncherCancellationAsync`, lines 123-174, `MeasureForcedTerminationCoreAsync`, lines 176-246, `StartLauncher`, lines 255-317, and `FindBuiltAssembly`, lines 414-445; `PerformanceGate` result model and construction, lines 15-44 and 234-263; `Program.CreateStandaloneEnvelope`, lines 110-165.
- Mechanism: Mutable repository bin Worker and Launcher assemblies are selected, but their paths, hashes, and MVIDs never enter the measurements, result, or envelope. The envelope hashes only the Gates DLL, PDB, and contract.
- Impact: Stale or substituted compatible binaries can certify the current source commit.
- Safe evidence: Replacing only untracked worker or launcher output leaves every envelope identity field unchanged.

## Wave 14.23. MEDIUM - LinuxWorkerProcess restarts its cleanup budget after the final deadline

- File: `SharpProof.Host/LinuxWorkerProcess.cs`
- Members: `WaitForExit`, lines 87-116, especially 100 and 106; `Dispose`, lines 141-154, especially 150-151; `Terminate`, lines 170-215
- Mechanism: `WaitForExit` exhausts `finalLimit` and `Terminate` throws; `using` or `finally` then calls `Dispose`, which starts a new stopwatch and grants a hard-coded extra second.
- Impact: The advertised hard limit is exceeded exactly for a stuck worker.
- Safe evidence: No field carries the original deadline. The first `Terminate` times out, and `Dispose` resets elapsed to zero and waits through TERM and tree kill again.

## Wave 14.24. MEDIUM - Shared-DAG memoization bypasses the interpreter hard nesting cap

- File: `SharpProof.Ir/IrInterpreter.cs`
- Member: `EvaluateCore`
- Current lines: 118-149; cache return at 120-123 precedes depth check at 132-138
- Mechanism: Ascending conjuncts can precache every child of a more-than-256-level nested chain, keeping recursive evaluation shallow.
- Impact: A structurally over-depth term yields a Value while an equivalent unprimed term is Unsupported, diverging from the verifier support boundary.
- Safe evidence: `w0=Variable; wi=Negate(wi-1);` and a root conjunction `Equal(w1,w1)...Equal(w300,w300)` evaluates true despite depth greater than 256.

## Wave 14.25. LOW - IrPrinter emits type display names as raw IR syntax

- Files and members: `SharpProof.Ir/IrPrinter.cs`, `TypeName`, lines 31-34; `IrPrinterProjections.generated.cs`, `Format`, lines 21 and 27
- Mechanism: Null and cast output interpolate an unescaped display name and omit identity; legal names can contain delimiters or newlines, and distinct identities can share names.
- Impact: Diagnostics and fuzz evidence can be ambiguous, line-injected, or identical for distinct terms.
- Safe evidence: A fresh reference identity named `object` prints the same null as `ObjectType`; name `X)\nspoof` injects a line.

## Wave 14.26. LOW - Approved assembly uniqueness is inconsistent with token canonicalization

- Files and members: `SharpProof.Specs/ApiSpecTable.cs`, `ValidateDeclaration`, lines 199-215; `ApiSpecContentDigest.Compute`, lines 21-28
- Mechanism: `Distinct` uses case-sensitive record equality, while the digest uppercases public-key tokens and resolution matches them case-insensitively.
- Impact: The same semantic authorization set has multiple representations and different digests, causing cache and identity churn.
- Safe evidence: Lowercase and uppercase hex token duplicates are accepted as unique, then both canonicalize identically and grant no new authority.

## Wave 14.27. MEDIUM - Conditional sort validation leaks native Z3 Sort wrappers

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Member: `QueryEncoder.EncodeConditional`
- Current lines: 530-550, especially line 535
- Mechanism: Each `Value.Sort` call invokes native `Z3_get_sort`, `Sort.Create`, and `IncRef` and returns a disposable wrapper; neither wrapper is owned or disposed.
- Impact: A wide shallow query accumulates native references and finalizable wrappers until GC, defeating deterministic per-query cleanup.
- Safe evidence: Z3 4.12.2 reflection and probes show repeated `Expr.Sort` calls return distinct nonzero wrappers; disposing zeroes each. IR types already guarantee branch type equality.

## Wave 14.28. MEDIUM - Summary assumptions bypass available spec-result projections

- Files and members: `SharpProof.Worker/CallableEvidenceBuilder.cs`, `Build`, lines 108-151 and 195-198, contrasting rewrite at 79-106; `PostconditionObligationBuilder.IsSupportedProofDomain`, lines 101-117.
- Mechanism: Specification assumptions rewrite guard and predicate through `SpecResultProjections`, while summary assumptions retain raw terms; the final gate rejects a retained sequence or reference variable.
- Impact: A supported relational-summary call using a projected API result makes every postcondition `Unknown/UnsupportedExpression`.
- Safe evidence: `Array.Empty<int>()` yields sequence `s` plus a length proxy; a summary relation containing `Length(s)` remains unreplaced, and the gate rejects `s`.

## Wave 14.29. MEDIUM - Unsupported prepared-call operand evaluation is misclassified as nonfatal

- File: `SharpProof.Worker/CallableCounterexampleReplayer.cs`
- Member: `Replay`
- Current lines: 46-53
- Mechanism: Any Unsupported at an `IrCallInstruction` whose ID belongs to `SpecCalls` or `SummaryCalls` becomes `CounterexampleNotReplayable`, ignoring `execution.Unsupported`. The interpreter can report the call instruction after receiver or argument evaluation fails with `MissingVariable`, `InvalidVariableValue`, or `OpaqueTerm`, before the call host.
- Impact: A model, backend, or replay inconsistency is hidden as semantic Unknown instead of `CounterexampleReplayFailed`.
- Safe evidence: A prepared call with `PureOpaque` or an absent argument receives nonfatal classification; the same term in an assignment correctly fails replay.

## Wave 14.30. MEDIUM - Mutable request state is reread after the authenticated snapshot

- Files and members: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`, current lines 39-58, 86-87, 120, 153-155, 178-187, 207, 253-255, and 324-335; `WorkerInputSnapshot.LoadAsync`, lines 12, 32, and 44-46.
- Mechanism: Hashes and validation do not snapshot the mutable request or nested budgets and cache options; later checks, lane construction, solver budgets, response construction, and cache operations reread them.
- Impact: State B can execute, publish, and cache under hashes for state A, bypassing authenticated resource and depth boundaries.
- Safe evidence: A backend factory mutates `QueryRlimit` or `MaximumExpressionDepth` after hashes are computed; later lane, verification, and cache code use B.

## Wave 14.32. MEDIUM - Manifest accepts claim orders the worker cannot assemble

- File: `SharpProof.Worker.Protocol/ProtocolJson.cs`
- Members: `ValidateManifestCore`, lines 427-462; `ValidateClaimMembership`, lines 464-474
- Mechanism: Validation checks dense ordinals and exact membership but not required postcondition-before-effect grouping; the verifier treats the first `ensures.Length` ClaimIds as postconditions.
- Impact: A protocol-valid, hash-sealed artifact deterministically produces duplicate or missing rows and `MalformedResult`.
- Safe evidence: With one Ensures P and one effect E, use `ClaimIds=[E,P]` and matching dense claim rows and seals. The validator accepts it.

## Wave 14.33. MEDIUM - Accepted short termination grace cannot provide the declared cleanup reserve

- File: `SharpProof.Worker.Protocol/WorkerExecutionEnvelope.cs`
- Member: `MaximumElapsedMilliseconds`
- Current lines: 8-25, especially 13-14 and 24-25
- Mechanism: Grace accepts 1 through 300000, but allowance is project plus `Max(1, grace-100)`. Grace 1 makes final and hard deadlines equal; values 2 through 100 leave less than a 100 ms reserve.
- Impact: A protocol-valid configuration makes the cleanup promise impossible, and routine timeout can force-kill or fail immediately.
- Safe evidence: Grace 1 yields a 1 ms producer allowance and zero cleanup reserve; a consistent minimum is 101.

## Wave 14.34. MEDIUM - Failure and cancellation assembly is quadratic

- File: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`
- Member: `CreateIncomplete`
- Current lines: 44-72, especially 58-70
- Mechanism: For every claim, the method linearly scans all manifest callables to find the owner.
- Impact: A valid one-claim-per-callable recovery is O(N^2) and can consume launcher grace or lose the structured timeout response.
- Safe evidence: A representable 8,000-row fixture of about 6.5 MiB triggers roughly 64 million owner comparisons.

## Wave 14.35. MEDIUM - Canonicalization repeatedly scans the full claim manifest

- Files and members: `SharpProof.Worker.Protocol/ProtocolManifest.cs`, lines 31-37; `ProtocolJson.cs`, lines 217-220 and helpers at 850-859
- Mechanism: Each sort key uses `FirstOrDefault` to scan all claims; `SealManifest` and `SerializeResponse` are therefore quadratic on valid under-cap data.
- Impact: Response or manifest serialization can take seconds and violate availability expectations.
- Safe evidence: N ClaimIds plus N results each invoke a linear claim lookup. A 12,000-result response remains below 5 MiB but requires O(N^2) work.

## Wave 14.36. HIGH - Staged worker closure can change after the last hash check before managed load

- File: `SharpProof.Worker.Launcher/Program.cs`
- Members: `RunMain`, lines 92-106; `RunWorker`, lines 214-228
- Mechanism: `ExecutionWorkerPath` is hashed and later reopened by dotnet. Retained old-inode handles do not make dotnet load through them; a same-uid rename replacement between check and use wins.
- Impact: The input hash and version identify the genuine closure while different code executes and can fabricate protocol evidence.
- Safe evidence: A `computeWorkerSha256` seam computes the genuine digest, atomically replaces the pathname, and returns the digest; comparison passes and `runWorker` observes replacement bytes. This is distinct from the native-Z3 TOCTOU.

## Wave 14.37. MEDIUM - Existing directory result path escapes fail-closed handling

- File: `SharpProof.Worker.Launcher/Program.cs`
- Members: `RunMain`, lines 48-74 and 126-137; `DeleteIfExists`, lines 765-770; `LauncherArguments.ValidateDistinctPaths`, lines 877-940
- Mechanism: Topology validation permits a directory. `File.Exists` returns false for it, so the worker runs, the result is deemed missing, and `WriteLauncherFailureAsync` publication over the directory throws an uncaught `IOException`.
- Impact: A malformed argument starts the worker and then crashes the launcher without a controlled result.
- Safe evidence: Supply valid inputs with `--result` naming an existing empty directory and have `runWorker` return without writing. The recovery write deterministically fails. This is distinct from the FIFO hang.

## Wave 14.38. HIGH - Supervisor death leaves verifier bridge and descendants outside containment

- Files and members: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`, `RunWorker`, lines 178-195; `RunVerifier.TryTerminate`, lines 926-965; `ObserveCleanupAnchorAsync`, lines 693-707
- Mechanism: The launcher lacks a parent-death signal. If the supervisor is SIGKILLed, the bridge and verifier are reparented in a `setsid` group. pidfd signaling and descendant scans against the dead PID cannot reach them, and group kill occurs only after successfully stopping the supervisor. A missing receipt invokes only callback or `FailFast`, not fallback cleanup.
- Impact: The verifier survives containment failure and continues consuming resources or producing side effects.
- Safe evidence: In a disposable fixture, kill the bridge parent and supervisor; authentication failure is observed while the verifier PID remains alive.

## Wave 14.39. MEDIUM - Publication reset ignores build cancellation

- File: `SharpProof.BuildTasks/ResetPublishedVerification.cs`
- Members: Class declaration at line 6; `Execute`, lines 19-25
- Mechanism: The task does not implement `ICancelableTask` and calls `LinuxPathIdentity.ResetPublicationSet` without a token even though the API accepts one; a 30-second lock wait and deletion are uninterruptible.
- Impact: A canceled Clean can block for the full timeout or delete artifacts after cancellation.
- Safe evidence: Hold a lease, start the task, cancel the build, and release the lease. No Cancel or token transition exists, so deletion continues.

## Wave 14.41. HIGH - Simple field writes bypass SPMETA010

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `AnalyzeAssignment`
- Current lines: 30-43, especially line 33
- Mechanism: The SimpleAssignment callback accepts only an `IPropertyReference` target; an `IFieldReference` returns before value inspection.
- Impact: A cache can persist Unknown, timeout, or failure through a field without an error diagnostic.
- Safe evidence: `ProofCache.Latest = Answer.Unknown` is a field assignment and line 33 returns.

## Wave 14.42. MEDIUM - SPMETA010 treats every TryUpdate argument as written

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `AnalyzeWrite`
- Current lines: 16-28, especially 18-22
- Mechanism: For allowlisted write methods, `Any` checks every argument without binding the stored parameter; the compare value in CAS `TryUpdate` is read-only.
- Impact: A false error rejects a safe cleanup transition.
- Safe evidence: `cache.TryUpdate("k", Answer.Proven, Answer.Unknown)` reports because of the comparison argument although only Proven can be stored.

## Wave 14.43. MEDIUM - Immediate explicit cancellation rethrow is falsely diagnosed

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Members: `RethrowsCancellationImmediately`, lines 311-315; `AnalyzeCatchClause`, lines 26-35
- Mechanism: The exemption recognizes only a first-statement bare `throw;`; `throw caught;` is an immediate `ThrowStatement` with an expression and receives SPMETA003.
- Impact: Valid propagation of the same `OperationCanceledException` is blocked by an error.
- Safe evidence: `catch(OperationCanceledException caught){ throw caught; }`.

## Wave 14.44. MEDIUM - Cache reaching-definition analysis ignores analyzer cancellation

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `GetReachingLocalValues`, lines 117-181; `TransferLocalValues`, lines 204-227; source-return scanning, lines 297-326
- Mechanism: Every local cache write builds a CFG and repeatedly scans reachable blocks and descendants to a fixed point. No `OperationAnalysisContext.CancellationToken` checks exist, and `GetSyntax` omits a token.
- Impact: A canceled IDE or build continues substantial CPU and allocation, multiplied by cache writes.
- Safe evidence: A large branch or loop method with repeated cache writes has no cancellation-observation path.

## Wave 14.46. MEDIUM - Unsupported hosts retain stale verification publications

- File: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`
- Members: `_SharpProofInitializeVerify`, lines 99-113; `SharpProofResetPublishedVerification`, lines 234-253
- Mechanism: Normal invalidation and Clean reset both require `_SharpProofVerifierHostSupported=true`.
- Impact: After Linux verification, a disabled or off-profile build or Clean on an unsupported host leaves request, result, manifest, SARIF, and markers that appear current.
- Safe evidence: When the condition is false, both tasks are skipped and a shared worktree retains old evidence. This is distinct from host detection and relative Clean path issues.

## Wave 14.47. MEDIUM - Compiler failures strand a fresh invocation directory per build

- File: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`
- Members: `_SharpProofInitializeVerify`, lines 41-78 and 114; `_SharpProofCleanupInvocation`, lines 117-135; `_SharpProofVerifyCore`, lines 223-224; `SharpProofVerify`, lines 227-231
- Mechanism: A fresh GUID manifest path is created before `CoreCompile`; cleanup occurs only on initialization errors or after success targets. When `CoreCompile` errors, the `AfterTargets` target does not run, and Clean omits runs.
- Impact: Compile-error and cancellation loops accumulate source-derived manifests and directories without bound.
- Safe evidence: An MSBuild probe confirms `AfterTargets` is skipped on a `CoreCompile` error; repeated failing builds leave `runs/<guid>` directories.

## Wave 14.48. MEDIUM - Source-tree analyzer dependencies ignore mapped ProjectReference configurations

- File: `SharpProof.AnalyzerConsumer.props`
- Locations: ProjectReference and Analyzer groups at lines 28-67
- Mechanism: Project references can map to Release through solution or `SetConfiguration`, but dependency Analyzer paths hard-code `bin\$(Configuration)\netstandard2.0` from the consumer.
- Impact: A Debug consumer with Release-mapped SharpProof references points Roslyn at nonexistent Debug dependencies, causing load warnings or omitted analysis.
- Safe evidence: Map the three references to `Configuration=Release` while the consumer is Debug. Outputs follow metadata, while lines 39-66 remain `bin\Debug`.

## Wave 14.49. MEDIUM - Unescaped semicolons split valid analyzer paths into multiple items

- Files and members: `SharpProof.Package/buildTransitive/SharpProof.props`, directory properties at lines 4-11; `SharpProof.targets`, Analyzer items at lines 20-62; `SharpProof.AnalyzerConsumer.props`, items at lines 29-66
- Mechanism: Absolute directories and paths expand directly into `Include` without MSBuild escaping; a legal semicolon becomes an item separator.
- Impact: Truncated phantom ProjectReference or Analyzer items prevent loading, and advisory builds can continue without SharpProof.
- Safe evidence: In-memory MSBuild evaluation with `ProbeDirectory=C:\valid;segment` yields items `C:\valid` and `segment\SharpProof.Analyzer.dll`.

## Wave 14.50. MEDIUM - Partial-term fuzz evidence repeats only eight semantic cases

- Files and members: `Tools/SharpProof.Fuzz/PartialTermSmtFuzzing.cs`, `PartialTermSmtCaseGenerator.Create`, lines 39-64; `FuzzRunner.RunAsync`, lines 223-236; `FuzzSummary.Passed`, lines 93-110
- Mechanism: Formula and scenarios depend only on seed bits 1, 2, and 4, producing exactly eight cases. The seed advances by 397, equivalent to 5 modulo 8, and repeats every eight, while every repetition increments agreements and `Passed` checks only the count.
- Impact: A million-case campaign overstates coverage and cannot find other operator or nesting interactions.
- Safe evidence: Direct bit dependence and fixed scenarios.

## Wave 14.51. MEDIUM - Finite-domain oracle enumerates exponentially before SMT without a budget

- File: `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs`
- Members: `IsDefinedForAllAssignments`, lines 52-105; `CompareAsync`/`IsSatisfiableByEnumeration`, lines 135-170 and 219-281
- Mechanism: The oracle recursively assigns every collected variable, with 2 values per Boolean and 5 per integer, without a variable-count or assignment cap. `CompareAsync` performs this before SMT, and cancellation defaults to none.
- Impact: A compact formula can stall fuzz and tests without consuming solver budget.
- Safe evidence: Put 15 distinct integer variables in an unreachable RHS `false && rhs`; collection includes all of them and enumeration performs 5^15 leaves despite short-circuit during evaluation.

# Read-Only Multi-Agent Bug Audit - Wave 15 - 2026-08-29

This section records 27 unique findings from 30 coordinated read-only auditors. Findings were transported without code modification or re-verification.

## Wave 15.2. MEDIUM - Source generic method normalization erases constructed exception identity

- File: `SharpProof.Effects/EffectAnalysisSession.cs`
- Members: `ResolveCall`, lines 146-208, especially normalization at 164 and source-call handling at 189-199; `ComputeSummaries`, lines 381-415, especially remapping and catch filtering at 400-410; `NormalizeMethod`, lines 518-523, especially `OriginalDefinition` at 522
- Mechanism: Every `G<int>.Throw` target becomes open `G<T>.Throw`; the source summary carries `GenericException<T>`. Remapping changes regions, not exception types, and `KeepEscaping` compares the open type with concrete catch `GenericException<int>`, treating it as nonmatching.
- Impact: Caught exceptions remain escaping, falsely rejecting `DoesNotThrow` and exception proofs and losing constructed-type provenance.
- Safe evidence: `class G<T>{ public static void Throw()=>throw new GenericException<T>(); } try{G<int>.Throw();}catch(GenericException<int>){}` normalizes the target to its original definition and leaves symbol-unequal exception types. This is distinct from Waves 8.7 and 10.14.

## Wave 15.3. MEDIUM - Legal duplicate compiler input paths produce a snapshot the product rejects

- File: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`
- Members: `Capture`, lines 40-116, especially syntax trees at 108 and additional files at 113-115; `CaptureTree`, lines 118-158, especially path at 141-145; `CaptureAdditionalFile`, lines 317-328
- Mechanism: Capture preserves each input but uses path as the sole row identity without duplicate detection or disambiguation. Downstream `CompilationFingerprint.HasValidSnapshot` requires distinct syntax-tree paths at 100-103 and distinct additional-file paths at 341-346, rejecting legal capture output.
- Impact: Valid Roslyn hosts with distinct trees or `AdditionalText` instances sharing `FilePath` cannot emit a compiler manifest.
- Safe evidence: Two `CSharpSyntaxTree.ParseText` trees with path `Generated.g.cs` are legal and retain ordinals; capture emits duplicate `Path` rows and validation fails. This is distinct from Wave 5.17 shared objects and Wave 14.7 mapped-geometry collision.

## Wave 15.4. MEDIUM - Call-graph sorting delays cancellation under a shared session lock

- File: `SharpProof.Effects/EffectCallGraph.cs`
- Member: `FindRecursiveMethods`, lines 38-45 and 71-77; cancellation check only at 25
- Mechanism: `OrderBy` fully buffers and sorts all node keys before the first `Visit`, and every high-fanout call set before another token check; the comparer formats canonical symbol identities per comparison.
- Impact: Pre-canceled or newly canceled analysis can continue O(V log V + E log E) work while `EffectAnalysisSession` holds `_gate`, delaying cancellation and blocking other requests.
- Safe evidence: LINQ `OrderBy` executes before the `foreach` body, so line 25 cannot observe cancellation until sorting finishes. This is distinct from Waves 8.2 and 14.44.

## Wave 15.5. MEDIUM - Exhaustive prior catch filters cause false swallowed-cancellation errors

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Members: `CancellationHandledEarlier`, lines 55-79; `PatternIncludesAllCancellation`, lines 133-155
- Mechanism: Exhaustiveness recognizes type and binary patterns only; negated and null patterns fall through as false.
- Impact: Error-level SPMETA003 rejects safe cancellation propagation in a later catch that cannot receive cancellation.
- Safe evidence: `catch (Exception e) when (e is not null) { throw; } catch (Exception) { }`; exception objects are nonnull, so the first catch always receives and rethrows `OperationCanceledException`, but the second is diagnosed. The existing fixture labels this `UnsupportedExhaustivePattern`. This is distinct from Waves 8.6, 12.15, and 14.43.

## Wave 15.6. MEDIUM - Closed generic companion targets collapse to every constructed target

- File: `SharpProof.Effects/EffectCallPreconditionPolicy.cs`
- Members: `HasPotentialPreconditionsCore`, lines 101-108; `FindTypesWithCompanions`, lines 190-228, especially 224
- Mechanism: Companion discovery stores `[ContractFor(typeof(Target<...>))]` as `target.OriginalDefinition`; lookup also uses `method.ContainingType.OriginalDefinition`. A companion specific to `Target<int>` therefore marks `Target<string>` potentially preconditioned.
- Impact: Unrelated constructed metadata methods receive `NotProven` and `CallPreconditionNotProven`, reducing proof and effect coverage.
- Safe evidence: A referenced `Target<T>.M` plus source companion for `Target<int>` causes analysis of `Target<string>.M` to match `Target<T>`. The existing `DistinctClosedGenericCompanionsDoNotOverlap` test confirms closed targets intentionally differ. This is distinct from Wave 6.7's relational-summary cache collision.

## Wave 15.7. HIGH - Direct parser APIs outside a three-name catalog bypass SPMETA001

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `KnownTypeNames` and `ForbiddenMethods`, lines 13-46; `IsForbidden`, lines 103-115
- Mechanism: Enforcement recognizes only `SyntaxFactory.ParseStatement`, `ParseExpression`, and `ParseTypeName`. Direct `ParseCompilationUnit` and `ParseMemberDeclaration` are absent; `CSharpSyntaxTree.ParseText` cannot match because its containing type is uncataloged.
- Impact: Soundness-critical projects can synthesize and bind whole trees or members from text without the error boundary.
- Safe evidence: `SyntaxFactory.ParseCompilationUnit` matches the type but not a method name, while `CSharpSyntaxTree.ParseText` misses every known type. This is distinct from Waves 8.8 and 11.12's indirect-dispatch bypasses.

## Wave 15.9. MEDIUM - Oversized numeric enum strings escape malformed-protocol handling

- File: `SharpProof.Worker.Protocol/ProtocolJsonSupport.cs`
- Member: `EnsureCanonicalEnum`, lines 151-173, especially 165-173
- Mechanism: `Enum.Parse` throws `OverflowException` for numeric-looking strings outside the enum's underlying range, but only `ArgumentException` is wrapped as `JsonException`.
- Impact: A small malformed request or response bypasses typed `request.malformed` or `worker.malformed_result`; `Worker/Program.cs` lines 56-58 and launcher `Program.cs` lines 350-352 omit `OverflowException`, yielding unhandled termination.
- Safe evidence: `Enum.Parse(typeof(DayOfWeek), "999999999999999999999999999999", false)` produces `OverflowException`; replacing serialized `runStatus` with it follows this path. This is distinct from Wave 6.26 diagnostic `FormatException` and Wave 14.37 path `IOException`.

## Wave 15.10. MEDIUM - Semantically unreachable handler cycle fabricates MayDiverge

- Files: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`, lines 376-380; `SharpProof.Effects/ManagedAbstractFlow.cs`, `IsAcyclic`, lines 915-939
- Mechanism: Effect scanning excludes impossible catches semantically, but final termination calls `IsAcyclic` over every Roslyn `BasicBlock.IsReachable`. Roslyn marks impossible catches structurally reachable, so a loop there makes the whole CFG cyclic.
- Impact: Definitely terminating methods gain false divergence. For module initializers, later body entry appears preventable, suppressing direct violation and replay witnesses module-wide.
- Safe evidence: The CFG for `static void M(){try{}catch{while(true){}}}` has a reachable catch block with a self-edge, while the semantic scanner omits the catch and then joins `MayDiverge`. This is distinct from Waves 9.15, 7.12, and 13.20.

## Wave 15.11. MEDIUM - Arbitrary operation subtrees are promoted to valid contract bodies

- File: `SharpProof.Contracts/ContractClauseInventoryBuilder.cs`
- Members: `CreateCore`, lines 53-56; `TryGetDirectPlacement`, lines 155-158; `GetBodies`, lines 244-250
- Mechanism: Public `Create(callable, implementationBody)` accepts any `IOperation` without proving it is the callable body root. `GetBodies` falls back to supplied syntax; a supplied contract invocation looks like a non-block body whose site equals the invocation and becomes `ValidPrologue`.
- Impact: A conditional, late, or misplaced contract can be inventoried as valid, suppressing placement failure and changing downstream effective contracts.
- Safe evidence: Obtain the `IInvocationOperation` for a late `Contract.Requires(flag)` and call `Create(method, invocation)`; it is `ValidPrologue`, while `Create(method)` classifies the same clause `Late`. This is distinct from Wave 3.18's foreign-compilation throw and companion-selection issues.

## Wave 15.12. LOW - Mutable claim assumptions alias manifest and sibling evidence

- Files: `SharpProof.Worker/CallableClaimResultAssembler.cs`, `Create`, lines 102-114, especially 113, via `Unknown` at 74-81 and effect assembly; `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, mutable `WorkerAssumptionEvidence` at 325-330
- Mechanism: `Assumptions = [.. target.Entry.Assumptions]` copies only the array; mutable elements are shared. `DecodeAll` passes the actual manifest entry into preparations, and multiple claim results reuse those elements.
- Impact: Mutating one returned claim's `Used`, `Kind`, or `Id` mutates sealed manifest authority and sibling projections, invalidating or co-mutating an otherwise valid public response.
- Safe evidence: `result.Assumptions[0]` and `target.Entry.Assumptions[0]` are the same reference. `CanonicalizeAssumptions` only reorders shallowly. This is distinct from Wave 9.5's response-budget alias.

## Wave 15.13. LOW - Duplicate OSS source IDs escape catalog validation as generic collection exceptions

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs`
- Member: `Validate`, lines 123-129
- Mechanism: `document.Sources` is converted with `ToImmutableDictionary(source => source.Id)` before per-source validation, with no explicit uniqueness or null check.
- Impact: Malformed or imported corpus metadata fails closed but appears as an internal gate crash and stack trace rather than deterministic source-specific `InvalidDataException`.
- Safe evidence: Two otherwise-valid JSON source records with `Id="dup"` reach line 123 and throw `ArgumentException` before the loop; null similarly causes `NullReferenceException`. This is distinct from recorded snapshot-shape, filename-grammar, and origin mechanisms.

## Wave 15.14. MEDIUM - SBOM release identity fixture process can hang indefinitely

- File: `SharpProof.ArchitectureTest/SbomReleaseIdentityTests.cs`
- Member: `RunFixtureAsync`, lines 81-106, especially 102-106
- Mechanism: The test launches PowerShell, begins drains, and awaits `WaitForExitAsync` without cancellation, deadline, or a kill-and-wait `finally`; `Process.Dispose` does not terminate the process.
- Impact: A stalled PowerShell fixture or descendant Git process blocks the architecture suite indefinitely, and runner interruption can orphan both.
- Safe evidence: A controlled blocking PowerShell shim first on `PATH` makes the await never complete and no cleanup runs. This is distinct from Wave 2.18's corpus-import cancellation of an already-launched Git child.

## Wave 15.15. MEDIUM - Recursive delegate-alias reachability can terminate the analyzer host

- File: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`
- Member: `TreeAnalysis.CanReachConsumption`, lines 567-783; recursive calls at 668-674, 698-704, and 730-736
- Mechanism: Each propagated local, tuple, deconstruction, or pattern alias consumes a CLR stack frame. `activeDefinitions` prevents cycles but provides no depth bound or iterative worklist.
- Impact: A valid long chain such as `Func<int> a0=Reachable; Func<int> a1=a0; ...; return aN();` can cause uncatchable `StackOverflowException` and terminate the compiler or analyzer process.
- Safe evidence: Direct-delegate propagation recursively follows every distinct alias while retaining definitions active until final consumption, so depth is linear. This is distinct from Wave 13.12's false metadata consumption, Wave 8.2's namespace recursion, and deep-IR recursion.

## Wave 15.16. HIGH - Metadata exception construction suppresses reachable catch handlers

- File: `SharpProof.Effects/ExceptionHandlerReachability.cs`
- Member: `GetPotentialExceptions`, lines 536-570, especially 561-566
- Mechanism: Any metadata-defined constructor whose created type derives from `Exception` is forced to `EmptyPotential`, even for ordinary `new ExternalException()` and when the constructor is not known nonthrowing.
- Impact: A matching catch is marked unreachable and its effects omitted. `OperationEffectScanner` can honor a trusted complete constructor spec declaring `ArgumentException`, then filter it as caught, producing a falsely complete summary without the handler's writes or allocations.
- Safe evidence: An external exception constructor with an exact-`ArgumentException` effect spec in `try { _=new ExternalException(); } catch(ArgumentException){ State.Value=new object(); }` reaches the hard-coded empty branch solely because the created type is an exception. This is distinct from Waves 5.7 and 7.22's explicit throw and Wave 14.12's implicit source constructor/base call.

## Wave 15.17. MEDIUM - Non-refuted effect-result assembly ignores method cancellation

- File: `SharpProof.Worker/EffectClaimResultAssembler.cs`
- Member: `Assemble`, lines 28-125; token at line 32, observed only on the Refuted path at 80-85, with other returns at 41-78 and 106-125
- Mechanism: `UnsupportedContract`, entry failures, `Proven`, and ordinary `Unknown` do not check cancellation; only `Refuted` forwards the token to replay.
- Impact: If the method wall boundary expires after verification during materialization, non-refuted results can publish as complete or semantic rather than timeout; the path also performs assumption copying and scanning.
- Safe evidence: Calling the four-argument `Assemble` with an already-canceled token and valid `(Proven, None, CompleteMayEffectSummary)` returns `Proven`, while `Refuted` throws through replay. This is distinct from Waves 2.10, 4.5, 14.6, 5.21, and 14.44.

## Wave 15.18. MEDIUM - Compiler source-location authority proves geometry but not claim or callable ownership

- File: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`
- Member: `CompilerManifestArtifactJson.HasValidLocationAuthorities`, lines 688-750, especially 709-720 and 726-746
- Mechanism: Expected owner/location pairs derive from the mutable manifest itself. Validation only checks that authority repeats the pair and the span maps into a captured tree; it never independently associates the owner ID or lowered predicate with that span.
- Impact: Coordinated resealing can swap postcondition locations or assign another valid span while verification remains tied to the original claim; SARIF then reports predicate A at predicate B or an arbitrary location.
- Safe evidence: Swap two claim locations and corresponding authority location/tree tuples, retain `OwnerId`s, and reseal `Manifest.Hash` and `FeatureScopeSha256`; equality and tree geometry pass. This is distinct from Wave 13.23's inconsistent physical tuple and Wave 14.7's producer ambiguity.

## Wave 15.19. LOW - Non-semantic SourceLength permits noncanonical source-authority digests

- File: `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs`
- Members: `HasValidLineMap`, lines 31-50; `TryMap`, lines 262-290
- Mechanism: Validation only bounds `SourceLength`; it does not relate it to the next `SourceStart` or `TextLength`. Mapping selection and coordinates ignore `SourceLength` entirely. Structurally impossible alternate lengths remain valid while changing `LineMapSha256` and enclosing identities.
- Impact: Identical syntax SHA and mapping behavior acquire multiple accepted evidence and cache identities, causing churn and weakening canonical comparison.
- Safe evidence: In a valid multiline snapshot whose first line length exceeds two, set the first `SourceLength` to zero and recompute hashes; validation passes although the gap cannot be a line break, and `TryMap` is unchanged because it never reads the field. This is distinct from Waves 2.12, 3.24, 14.7, and other identity-canonicalization findings.

## Wave 15.20. MEDIUM - Effect completion analysis drops analyzer cancellation

- Files: `SharpProof.Effects/OperationCompletionEvaluator.cs`, constructor lines 17-35, especially 27-32; `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts` checks at 1825, 1921, and 1963
- Mechanism: Both `DefiniteOperationFacts` helpers are permanently constructed with `CancellationToken.None` despite a live callback token reaching `EffectAnalysisSession` and node building.
- Impact: Cancellation during source-body or operation-completion walks cannot stop work; canceled IDE and build callbacks consume CPU until the scan returns.
- Safe evidence: Cancel after a large source-call chain enters `CanMethodCompleteNormally`; the embedded token never cancels. This is distinct from Wave 11.11's lazy whole-compilation initialization and Wave 14.44's meta-analyzer CFG cancellation.

## Wave 15.21. MEDIUM - Project timeout and caller cancellation do not bound manifest acquisition and hydration

- File: `SharpProof.Worker/SharpProofWorker.cs`
- Member: `VerifyAsync`, lines 43-55, 86-87, 112-120, and 133-146
- Mechanism: Validation and request hashing precede the project timer; pre-canceled tokens are replaced with a fresh CTS; manifest loading receives `CancellationToken.None`; synchronous `DecodeCallables` has no token. Checks occur only after each full phase.
- Impact: Canceled or timed-out CLI work continues reading, hashing, deserializing, and hydrating a near-limit manifest, consumes termination grace, and may be force-killed without structured output; the in-process API has no launcher hard bound.
- Safe evidence: `WorkerInputSnapshot.LoadAsync` processes the full artifact, and `WorkerTests.PreCanceledRunLoadsTheAuthoritativeManifestWithoutStartingProofWork` at lines 5591-5609 confirms a pre-canceled call still loads it. This conflicts with the documented project wall-time outer boundary and is distinct from Wave 14.30's mutable request and Wave 14.34's result-assembly complexity.

## Wave 15.22. HIGH - Blanket lock-ancestor exemption suppresses unrelated implicit calls

- File: `SharpProof.Effects/OperationEffectScanner.cs`
- Member: `ScanInvocation`, lines 565-569
- Mechanism: Every implicit invocation anywhere under `LockStatementSyntax` returns `ScanArgumentValues` instead of `ScanCall`, not just synthesized monitor calls. Collection-initializer `Add` is implicit and its element syntax has a lock ancestor.
- Impact: User `Add` writes, throws, and completion are omitted, enabling unsound no-write and no-throw effect certification.
- Safe evidence: `lock(gate){ _ = new Values { 1 }; }` with a writing or throwing `Values.Add` is suppressed by the ancestor-wide condition. This is distinct from cataloged lock-null, await/foreach protocol, and call-order mechanisms.

## Wave 15.23. MEDIUM - Fuzz evidence architecture tests can hang or deadlock indefinitely

- File: `SharpProof.ArchitectureTest/FuzzRunnerEvidenceTests.cs`
- Members: `FuzzRunnerEvidenceUsesStrictSchemaFourDecoder`, lines 12-35; `FuzzCampaignEvidenceLifecycleIsFailClosedAndAtomic`, lines 53-76
- Mechanism: Both start PowerShell, redirect stdout and stderr, synchronously call `ReadToEnd` on stdout before stderr, and then use unbounded `WaitForExit` with no cancellation, timeout, or kill.
- Impact: A child filling stderr while stdout remains open deadlocks through pipe backpressure; any script nontermination wedges the architecture and qualification suite.
- Safe evidence: This follows standard finite redirected-pipe backpressure and the exact sequential read order. It is distinct from existing benchmark and worker wall-time findings and Wave 15.14's separate SBOM helper.

## Wave 15.24. MEDIUM - Strict fuzz-decoder integration oracle is satisfied by dot-sourcing alone

- File: `SharpProof.ArchitectureTest/FuzzRunnerEvidenceTests.cs`
- Member: `FuzzRunnerEvidenceUsesStrictSchemaFourDecoder`, lines 37-47; campaign script import at line 23 and actual invocation at 145-149
- Mechanism: The test runs a standalone decoder self-test, then claims campaign integration from `campaign Does.Contain("Assert-SharpProofFuzzRunnerResult")`; the campaign already contains that text in the dot-sourced filename independently of the actual call.
- Impact: Deleting or bypassing the decoder call while retaining the import leaves the test green, allowing malformed runner JSON into campaign evidence despite the named gate.
- Safe evidence: The exact substring at the import line satisfies the assertion without an invocation.

## Wave 15.25. MEDIUM - Conditional-elision fixture is rejected by payload authentication before the condition it claims to test

- File: `SharpProof.Specs.Test/ApiSpecTests.cs`
- Members: `SharpProofPackageSpecsRejectContractWithoutConditionalElision`, lines 260-280; `CreateSharpProofPackageReference`, lines 992-1031
- Mechanism: The helper compiles a synthetic `SharpProof.Attributes.dll`; production `ContractApiIdentityResolver` checks the exact shipped payload SHA before `HasValidContractShape`, so the synthetic image is rejected regardless of `Conditional` attributes. The test asserts only generic `UnapprovedReferenceFamily`.
- Impact: Removing or breaking conditional-shape validation would not fail the test, creating false confidence at the contract trust boundary.
- Safe evidence: Payload-SHA rejection necessarily precedes the shape condition; the shape check needs an isolated or authenticated fixture.

## Wave 15.26. MEDIUM - Lattice-law oracle does not establish a least upper bound

- File: `SharpProof.Dataflow.Test/GeneratedDomainPropertyTests.cs`
- Member: `GeneratedDomainLawAssertions.AssertLatticeAndBottomLaws`, lines 36-101, especially 80-101
- Mechanism: The deterministic tested upper bound is constructed as `Join(join, third)`, making `join <= bound` self-referential. The only independent check samples eight random values and may perform zero checks if none bounds both. It never directly checks join commutativity, associativity, or idempotence.
- Impact: Rare over-widening or non-lattice `Join` regressions can survive while the generated lattice-law tests remain green across three domains.
- Safe evidence: The self-derived upper bound and optional random filter provide no independent leastness witness.

## Wave 15.27. HIGH - Ordinary reference-parameter reassignment keeps entry argument ownership

- File: `SharpProof.Effects/ConversionOwnershipClassifier.cs`
- Members: `ClassifyParameter`, lines 77-111, especially 108-111; `BuildLocalRegions`, lines 310-379, especially 317-319 and 364-378
- Mechanism: `BuildLocalRegions` records learned ownership for `first = second`, but `ClassifyParameter` consumes learned regions only for ref-like types; ordinary class, interface, and array parameters always return the original `Parameter(ordinal)`.
- Impact: Later writes through a reassigned parameter are attributed to the wrong actual. Interprocedural remapping can map the reported write to fresh-only state and certify observable mutation as pure.
- Safe evidence: `Reassign(Box first, Box second){ first=second; first.Value=1; }` is summarized as `Parameter(0)`, not `Parameter(1)`. `Outer(Box live){ Reassign(new Box(),live); }` maps the write to the fresh first argument, so `IsObservablePure` accepts even though `live` mutates. This is distinct from Wave 12.8's ref-like storage and Wave 14.14's by-value structs.

# Read-Only Multi-Agent Bug Audit - Wave 18 - 2026-08-29

## Wave 18.1. HIGH - Ref-local aliases are lowered as independent scalar values while classification remains `Exact`

- Files/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`: `LowerStatement`, lines 142-146; `LowerDeclarator`, lines 184-195, especially 187 and 193-194; `LowerAssignment`, lines 205-220, especially 208-219. `SharpProof.Frontend/RoslynOperationLowerer.cs`: `GetVariable`, lines 118-127; `GetReferencedVariable`, lines 144-164. Downstream admission: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`: `PrepareBody`, lines 107-125 and 158-169, especially the by-ref method-parameter rejection at 111-114; `TryCreateParameterBindings`, lines 549-573, which skips all `ILocalSymbol` bindings at 558-560.
- Mechanism: Roslyn represents `ref long alias = ref value;` in its CFG as an `ISimpleAssignmentOperation` with `IsRef == true`. `LowerStatement` sends every simple assignment to `LowerAssignment` without inspecting `IsRef`, while `ILocalSymbol.RefKind` is likewise never inspected. `LowerDeclarator`/`LowerAssignment` evaluate the referenced local and assign that value to a fresh scalar IR variable for `alias`; later `alias = 42` changes only that fresh variable, whereas C# writes through the managed reference and changes `value`. Ref reassignment is also treated as a value copy. The long literal/local/declaration/simple-assignment/return path remains `Exact`, so callable lowering does not fail closed.
- Impact: Alias erasure underapproximates writes. An ordinary method with only by-value parameters can produce an accepted exact compiler body whose returned value differs from C#, permitting false postcondition proofs rather than merely degraded, abstained, or incomplete analysis.
- Safe reproduction:

```csharp
public static long Target(long value)
{
    Contract.Requires(value == 0);
    Contract.Ensures(Contract.Result<long>() == 0);
    ref long alias = ref value;
    alias = 42;
    return value;
}
```

  C# returns `42`. The lowered IR assigns `alias = value`, then `alias = 42`, then returns unchanged `value`; under the precondition it returns IR value `0`.
- Additional safe evidence: Acyclic supported body `long original=0L; ref long alias=ref original; alias=1L; return original;` with `Ensures(Result<long>() == 0L)` returns 1 at runtime, while the lowered program assigns 0 to two independent variables, assigns 1 only to `alias`, and returns `original=0`. A ref-readonly variant (`ref readonly long alias=ref original; original=1; return alias`) produces the same stale snapshot.
- Direct evidence: An in-memory Roslyn CFG probe confirmed the ref initializer is `SimpleAssignmentOperation`, `IsRef=True`. Lowering with the current built frontend returned `IsExact=True` and emitted `Goto; Assign alias=value; Assign alias=42; Return value; Return`.
- Admission evidence: `CompilerCallableLowerer.PrepareBody` rejects ref/out method parameters but does not reject ref locals. `TryCreateParameterBindings` skips all local-symbol bindings. The body is scalar, acyclic, call-free, and `Exact`, so it passes lines 158-169.
- Duplicate distinction: Wave 7.7 concerns the Effects scanner treating ref-local targets as `EffectSummary.Empty` and erasing ref-local read/write effects. Wave 7.8 concerns ref-parameter reassignment being misreported by that same effects scanner. Wave 9.6 concerns receiver binding. None records frontend managed-reference aliasing being converted to value assignment, `Exact` executable program lowering, or accepted compiler-body semantics.

## Wave 18.3. MEDIUM - Unsupported literals of the same CLR type collapse to one pure opaque term because literal payload is absent from semantic identity

- Paths/members/current lines: `SharpProof.Frontend/RoslynOperationLowerer.cs` `VisitLiteral` 471-475, `LowerConstant` 377-414 (fallback 413), `Opaque` 285-332, `IsDemonstrablyPure` 335-374 (`ConstantValue` makes pure at 337-340); `SharpProof.Frontend/CompilerIdentityBridge.cs` `InternOperation` 24-47 and `CreateSemanticOperationIdentity` 124-137.
- Mechanism: Unsupported constants such as double/float/decimal literals fall from `LowerConstant` to `Opaque`. `ConstantValue` makes them pure, so `InternOperation` uses `OperationSemanticIdentity`. That key holds only `OperationKind`, result type, binary/unary/instance flags, checked and lifted; it omits `ConstantValue`/literal syntax. Literal opacity has no receiver or arguments. Thus 1.0 and 2.0 of type double request the same member and `PureOpaque` term in one `IrFactory`/lowerer. The same omission affects distinct ill-formed UTF-16 string literals rejected by `LowerConstant` and other unsupported constant shapes.
- Impact: Public frontend/program consumers retaining closed-abstention terms can observe fabricated equality or transfer facts between distinct constants. Exact-only contract binding rejects them, limiting direct accepted-proof exposure.
- Safe repro/evidence: Compile one method containing 1.0 and 2.0, obtain the `ILiteralOperation` descendants, lower both with one `RoslynOperationLowerer`/`IrFactory`, then compare Term/Id; identity fields and the zero-argument shape coincide despite different `ConstantValue`.
- Nearest BUGS distinction: Wave 5.15 is missing `TypeOperand` for `typeof`/`sizeof`; old Wave 6.20 concerns const fields, but current `VisitFieldReference` passes `operation.Field` at line 493. This is the still-live omission of literal `ConstantValue` itself. Live BUGS search found no unsupported/distinct-literal entry.

## Wave 18.4. MEDIUM - Refuted-effect replay rehashes full compilation geometry twice per event without cancellation

- Files/members/lines: `SharpProof.Worker/EffectCounterexampleReplayer.cs`, `Replay` lines 12-16 and 33-52 (token checked at 12, then `CompilerEffectClaimArtifactCodec.Validate` at 13; next check only inside event loop; `ValidateEvent` 59-115 repeats geometry); `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs`, `HasValidReplayGeometry` lines 42-98, especially 56-95; `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs`, `HasValidLineMap` 20-50, `FindUniqueTree` 81-108, `IsBound` 110-149; `SharpProof.CompilerArtifact/CompilationFingerprint.cs`, snapshot/line-map hashes 16-43.
- Mechanism: For each replay event the codec serializes/hashes the selected whole syntax-tree snapshot, scans every compilation tree via `FindUniqueTree`, and serializes/hashes each line map; `IsBound` rehashes the chosen map. `Replay` then calls `ValidateEvent`, which repeats snapshot hashing and both location scans. None accepts/checks the method token. A cancellation arriving just after line 12 cannot be observed until this O(events * total compilation line-map bytes) work completes; the 16 MiB manifest cap still permits expensive repeated serialization, and every refuted effect claim repeats it.
- Impact: Method/project timeout can overrun substantially during result materialization, delaying lane renewal/worker termination and monopolizing CPU even though refuted replay is nominally cancellation-aware.
- Safe evidence: Static call graph above; `docs/soundness-notes/2026-07-30-allocation-effect-replay.md` line 58 claims cancellation is checked throughout, while test `SharpProof.Worker.Test/EffectCounterexampleReplayTests.cs`, `CanceledReplayDoesNotPoisonTheNextReplay`, lines 318-349 only exercises an already-canceled token caught before validation.
- Distinction: Wave 15.17 covers non-refuted assembler branches that never check cancellation, Wave 15.21 covers initial manifest acquisition/hydration outside the timer, Wave 9.7 covers collector-side symbol scan, and Wave 6.5 covers collector source recapture.

## Wave 18.5. LOW - Array replay admits null and empty MemberIdentity as different canonical identities for identical semantics

- Files/members/lines: `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs`, `CompilerEffectReplayEventArtifact.MemberIdentity` around 277-295 (nonnullable string, default empty); `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs`, `HasValidReplayEvent` lines 186-193 and `AddReplayEvent` lines 285-315, especially 293; `SharpProof.Worker/EffectCounterexampleReplayer.cs`, `Interpret` lines 128-135 and operation hashing lines 244-288; `SharpProof.Ir/CanonicalHashWriter.cs`, `Add(string?)` lines 9-14; `SharpProof.Worker.Protocol/ProtocolJsonSupport.cs`, options 191-204; `SharpProof.CompilerArtifact/CompilerFeatureScopeFingerprint.cs`, effect serialization lines 88-106.
- Mechanism: Array validation deliberately accepts `string.IsNullOrEmpty(MemberIdentity)` while requiring `MemberDocumentationId` null. JSON options do not enforce nullable annotations, so both JSON `null` and `""` deserialize and round-trip canonically. Interpretation treats both identically and derives the same array-allocation witness from `TypeIdentity`, but canonical hashing frames null and empty as different kinds, so `OperationIdentitySha256`/`EvidenceSha256` differ; feature-scope hashing serializes the differing artifact.
- Impact: The same semantic replay has multiple accepted evidence, feature-scope, manifest, and request identities, weakening canonical reproducibility and causing identity churn.
- Safe evidence: Change a valid array event's `memberIdentity:""` to null, reseal operation/evidence/feature hashes; all shape/geometry and replay checks accept and produce the same witness.
- Distinction: Wave 15.19 covers ignored line-map `SourceLength` ambiguity and Wave 14.26 covers token-case canonicalization; this is a replay-event null/empty split.

## Wave 18.6. HIGH - Virtual/interface contract association is keyed only to the compile-time target, so hierarchy preconditions disappear under ordinary static-type changes

- Files/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, `GetCalls`, lines 562-595 (invocations retain only `invocation.TargetMethod` at 570-575), and `GetPotentialCallOwners`, lines 14-87 (screening tests only that target at 59-63); `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, `Analysis.AnalyzeCallSite`, lines 264-327, especially `contractTarget = candidate.TargetMethod` at 276-278 and `session.BindRequires(contractTarget)` at 297; `SharpProof.Contracts/EffectiveContractSourceResolver.cs`, `ResolveCore`, lines 67-133 (only direct clauses/exact-containing-type `ContractFor` companion; no `OverriddenMethod` or interface-implementation mapping). `SharpProof.Contracts/ContractBinder.cs`, `BindCore`, lines 74-82 explicitly admits `MethodKind.ExplicitInterfaceImplementation`; `SharpProof.Contracts.Test/ContractBinderTests.cs`, `ExplicitInterfaceImplementationDirectContractsBind`, lines 311-336 proves that surface binds successfully.
- Mechanism: No hierarchy association occurs. A call through a derived static type binds only the override, dropping `Requires` on its base definition. Conversely a call through an interface/base symbol never sees a `Requires` declared on the concrete implementation; for an explicit interface implementation every legal source call has this latter shape, making its successfully bound precondition unenforceable by call-site analysis. Potential-call screening can return false before binding, so the owner is recorded `NotApplicable` rather than fail-closed `Unknown`.
- Impact: Deterministic violating virtual/interface calls receive no SP0027; the same runtime dispatch reports or disappears solely with an up/down cast or other static-type change. Base contracts are not inherited, and implementation-specific entry assumptions have no legal enforcement path.
- Safe reproduction/evidence: Release `AnalyzerTestHost`: `Base` defines virtual `M(int v){ Contract.Requires(v>0); }`; `Derived` overrides without clauses. `new Derived().M(-1)` returns 0 diagnostics, while `Base x=new Derived(); x.M(-1)` returns SP0027. Reverse the clause (only `Derived` `Requires`): direct Derived call returns SP0027, Base-typed call returns 0. Also `ITarget t=new Target(); t.Read(-1)` returns 0 when Target's explicit `ITarget.Read` contains `Requires`, while the binder unit test above establishes the explicit method binds one `Requires`.
- Closest-entry distinction: Wave 6.1 concerns compiler-callable verifier admission for explicit implementations, not analyzer call-site target association. The Wave 4 partial-event issue is definition/implementation normalization only for event accessors. Wave 3.1 covers non-invocation operation shapes; these are normal `IInvocationOperation` nodes carrying the wrong contract owner.

## Wave 18.7. MEDIUM - Omitted `base()` discovery ignores optional and params constructors, dropping their Requires entirely

- Files/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, `TryGetImplicitParameterlessBaseConstructor`, lines 382-413, especially candidate filtering with `constructor.Parameters.IsEmpty` at 401-405; duplicate primary-constructor helper `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, `TryGetImplicitParameterlessBaseConstructor`, lines 166-184, especially 179-183. Explicit initializers are resolved semantically in `AnalyzePrimaryConstructorInitializer`, lines 53-103, explaining the inconsistency.
- Mechanism: C# overload resolution permits an implicit omitted `base()` to select `Base(int value=-1)` or `Base(params int[] values)`. Both helpers equate callable-with-zero-arguments to symbol-with-zero-parameters and return no target. Neither potential-owner screening nor candidate construction sees the mandatory base call or its compiler-supplied default/empty-array actuals.
- Impact: A definitely executed base constructor with a deterministically false `Requires` is silently omitted; the caller outcome can remain `NotApplicable`, and no SP0027 is emitted.
- Safe reproduction/evidence: `class Base { public Base(int value=-1){ Contract.Requires(value>0); } } sealed class Derived:Base { public Derived(){} }` is legal and Release `AnalyzerTestHost` returns 0 SP0027. Changing only Derived to `public Derived():base(){}` returns one SP0027 (`false`). `Base(params int[] values){ Requires(values.Length>0); }` plus omitted base likewise returns 0.
- Closest-entry distinction: Wave 10.16 records the same C# language-resolution edge only in `CompilerImplementationIlSummaryLowerer` and reports relational-summary incompleteness. This finding is the separate `Requires` analyzer discovery path and directly suppresses SP0027; no existing entry names these two helpers.

## Wave 18.8. HIGH - Normal CFG transfers bypass required finally regions in both exact frontend lowering and nested-callable reachability

- Files/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LoweringSession.SelectBlocks` 552-580 and `LowerTerminator` 374-430 (successor use 383-425); `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `TreeAnalysis.RegularSuccessors` 985-1008 and `CanReachConsumption` 567-783 (regular enqueue 768-770).
- Mechanism: Roslyn represents a normal branch out of `try` as a regular branch directly to the post-try destination and records the mandatory finally bodies in `ControlFlowBranch.FinallyRegions`. Both walkers follow only `Destination`; neither expands `FinallyRegions`. The frontend therefore never selects/lowers the reachable finally blocks and can still remain `Exact`. Nested-callable reachability likewise jumps over calls/escapes/kills performed only in a normally-entered finally. Return/break/goto branches through finally have the same metadata issue.
- Impact: Compiler verification can prove a false postcondition from an IR that erased a mandatory finally mutation. `Requires` analysis can skip a lambda/local function that definitely executes in finally, missing SP0027.
- Safe repro/evidence: Roslyn 4.14 CFG for `Func<int> x=()=>1; try{} finally{_=x();} return 0;` is B2 `fall=Regular -> B4, FinallyRegions=[B3]`, while B3 contains the invocation. Both methods enqueue B4 and never B3. For proof impact: `int x=0; try{} finally{x=1;} return x;` lowers as return 0, so `Ensures(Result<int>()==0)` can be falsely proven although runtime returns 1. For analyzer impact, make the finally-only callback body call `Positive(-1)`. Microsoft.CodeAnalysis XML describes `FinallyRegions` as regions control goes through in execution order.
- Distinct: Wave 13.20 is unreachable-block inclusion; this is omission of reachable, mandatory finally blocks and exact semantic corruption.

## Wave 18.9. HIGH - A leading goto can make compiler body lowering start in an unreachable return block

- File/member/current lines: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`, `FindExecutableBodyStart` 428-450, `TryFindProgramStart` 466-495, call into `LowerSelected` 152-165.
- Mechanism: Body start is the first non-contract statement span, but `TryFindProgramStart` searches only block operations and `BranchValue`, not the control-flow branch corresponding to a statement. A `goto` has neither. The scan then selects the first later operation by block ordinal without checking `BasicBlock.IsReachable`; Roslyn orders the source-adjacent dead block before the actual label target.
- Impact: The verifier can prove/refute postconditions from a dead return rather than the executed body, an unsound result claim.
- Safe repro/evidence: Roslyn 4.14 CFG for `static int F(){ goto End; return 0; End: return 1; }` is B0 reachable `Regular->B2`, B1 unreachable `BranchValue=0/Return`, B2 reachable `BranchValue=1/Return`. With `bodyStart` at `goto`, B0 has no eligible op/value, so lines 472-490 select B1. A postcondition `Result<int>()==0` can be proven even though runtime returns 1.
- Distinct: Wave 13.20 starts at the correct entry then includes dead successor blocks, normally causing abstention; this defect chooses a dead block as entry and can change the verified return value while remaining `Exact`.

## Wave 18.10. HIGH - User-defined conversion/operator throws before delegate overwrite are modeled as nonthrowing, hiding a reachable old callback

- File/member/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `CanReachConsumption` 567-783 (assignment kill 606-629, exceptional enqueue 755-779), `BlockMayThrowBeforeAssignmentCommit` 1034-1064, `OperationMayThrow` 1066-1107.
- Mechanism: `OperationMayThrow` ignores `IConversionOperation.OperatorMethod`, and likewise ignores ordinary user-defined binary/unary operator methods unless the syntactic operator happens to be checked/divide/remainder. When a tracked delegate local is overwritten by such an operator expression, the code marks the old value killed and sets `exceptionalStateSurvivesKill=false`, even though the operator can throw before assignment commit and a catch can invoke the old delegate.
- Impact: A genuinely executed local function/lambda is classified dead and its `Requires` violations are never analyzed/reported.
- Safe repro: `Func<int> cb=Dead; try { cb = source; } catch(InvalidOperationException) { return cb(); } return 0; int Dead()=>Positive(-1);`, where `Source` defines `public static implicit operator Func<int>(Source _) => throw new InvalidOperationException();`. The conversion is implicit/user-defined, so all line 1068-1074 predicates are false; the assignment kills `cb` and no exceptional edge is traversed.
- Distinct: Wave 3.1 omits user-defined conversion calls as `Requires` call sites; this bug loses exception-before-commit reachability of a different, old delegate value. Wave 12.15 concerns cancellation filters.

## Wave 18.11. MEDIUM - Exceptional delegate reachability enters every sibling catch/filter regardless of feasibility

- File/member/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `OperationMayThrow` 1066-1107, `ExceptionalSuccessors` 1109-1135, consumed by `CanReachConsumption` 772-779 (also 757-764).
- Mechanism: Any possibly throwing operation enqueues the first block of every `Catch`, `Filter`, `FilterAndHandler`, and `Finally` sibling beneath the enclosing try owner. It does not use the known thrown type, catch ordering, or constant filter result. Thus a callback use in an impossible mismatched catch is treated executable.
- Impact: Dead local functions/lambdas are analyzed and can emit false SP0027 diagnostics.
- Safe repro: `int Outer(A ex){ Func<int> cb=Dead; try{throw ex;} catch(B){return cb();} catch(A){return 0;} int Dead()=>Positive(-1); }`, with unrelated sealed `A : Exception` and `B : Exception`. `throw ex` can only throw A (or NRE if null), never B, but the B catch is yielded and `cb()` makes `Dead` reachable.
- Distinct: Wave 7.2 is the Effects handler engine dropping reachable runtime-subtype catches; Wave 7.12 is effect completion through a false filter. This is the nested-callable reachability graph over-entering impossible handlers.

## Wave 18.12. HIGH - Lambda effect selections never enter the effect feature pipeline

- Files/members/lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `InitializeCompilation`, lines 114-128 (nested syntax callback and Method symbol callback registration); `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `ValidateNestedCallableDeclaration`, lines 5-23, and `ValidateMethodAttributes`, lines 72-168 (especially sole `EffectContractDiagnostics.ValidateArguments` call at 87 and selection at 103-160); `SharpProof.Contracts/ContractSelectionInventory.cs`, `GetCallableAttributes`, lines 239-254.
- Mechanism: C# permits method-targeted attributes on lambdas (the repository itself uses attributed lambdas in `SharpProof.Analyzer.Test/NestedRequiresCallSiteTests.cs` lines 1044-1046 and 1128-1138). Advisory activation therefore becomes Full, but the lambda syntax callback delegates only to `SharpProofControlAttributePolicy` and never validates or dispatches effect-selection attributes. The Method symbol callback owns all effect attribute validation/selection, while anonymous-function symbols do not get that ordinary declared-method dispatch; outer-method selection inspects only the outer method/property attributes. `RequiresCallSiteTreeAnalyzer` traverses nested CFGs only for call-site contracts and never invokes the effect pipeline.
- Impact: An explicitly selected lambda can silently violate `[ZeroAllocations]`, `[EnforcePure]`, `[DoesNotThrow]`, or `[EffectContract]` without its expected effect diagnostic or SP0047/incomplete outcome. The selected callable can disappear from analyzer outcome accounting.
- Safe reproduction/evidence: `Func<object> f = [ZeroAllocations] () => new object(); return f();` under advisory/all. The attribute triggers Full activation, but only the outer operation block is dispatched and `GetSelection(outer)` is empty; the lambda allocation never reaches `EffectContractDiagnostics.Analyze`. An invalid lambda `[EffectContract((SharpProofEffect)(1L << 40), Complete=true)] () => 0` likewise never reaches `ValidateArguments`/SP0024.
- Closest BUGS.md distinction: Wave 13.11 says nested syntax falsely disqualifies a selected outer method; Wave 3.15 says a nested rejected clause contaminates the outer inventory. This is the opposite ownership gap: a nested callable's own effect feature selection is never dispatched. Wave 14.9 concerns indirect `Contract.Result`/`Old` invocation, not feature registration.

## Wave 18.13. MEDIUM - Advisory activation can ignore cancellation for an entire giant syntax tree

- File/member/lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `GetAdvisoryActivation`, lines 203-248; `MayContainAdvisoryActivationSyntax`, 250-275; `ContainsOrdinal`, 277-301.
- Mechanism: The only loop token check is once per tree at line 209. `MayContainAdvisoryActivationSyntax` then scans `SourceText` (and may run repeated full ordinal searches) without a token, and a positive coarse screen enters `tree.GetRoot(token).DescendantNodes()` at 216-233 with no cancellation check inside node enumeration. Cancellation after the per-tree check is not observed until that whole tree finishes.
- Impact: Cancelled IDE/build analyzer runs can retain CPU on one very large/generated tree, delaying replacement compilations and analyzer-host shutdown before a session even exists.
- Safe reproduction/evidence: Use one huge tree containing an early `[` decoy but no attribute or contract invocation, begin advisory activation, then cancel during `DescendantNodes()` enumeration. There is no token observation until enumeration ends. A huge text without an early trigger similarly makes the manual text scan uncancellable until it returns.
- Closest BUGS.md distinction: Wave 11.11 covers cancellation discarded by lazy `AnalyzerSession` whole-compilation initialization, and Wave 14.5 covers companion surface matching. This occurs earlier, in pre-session feature activation/text-and-syntax dispatch.

## Wave 18.14. LOW - Any invalid global analyzer option hides every tree-local configuration diagnostic

- Files/members/lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `GetConfigurationDiagnostics`, lines 585-631, especially adding global errors at 591-596 and unconditional early return at 597-600 before tree scan 602-621; source global collection in `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs`, `FromOptions`, lines 40-67.
- Mechanism: Once `configuration.InvalidConfigurationValues` is nonempty, the reporter returns only that array and never calls `GetInvalidTreeConfigurationValues` for any syntax tree. Example: invalid global `sharpproof_profile=everything` plus tree-local `sharpproof_features=effects` reports only the global SP0025; fixing it reveals the second SP0025 on the next run.
- Impact: Diagnostics present an incomplete configuration error set and force iterative repair across builds, although analysis does fail closed.
- Safe evidence: Direct early-return control flow; the tree loop is unreachable whenever any global invalid value exists.
- Closest BUGS.md distinction: Wave 5.2 is a different earlier return inside `GetInvalidGlobalConfigurationValues` that hides a retired global alias behind another global error. This finding is in `SharpProofAnalyzerEngine.GetConfigurationDiagnostics` and suppresses all per-tree violations, including validly collected current-option aliases.

## Wave 18.15. HIGH - Source beforefieldinit static-method summaries omit possible type-initializer effects/exceptions

- Files/members/current lines: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `Build`, lines 84-104, especially the same-assembly non-constructor arm 95-99; `HasPotentialStaticInitialization`, lines 170-197. Corroborating intended behavior: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `AddStaticInitializationPotential`, lines 1599-1629; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `RequiresStaticInitializationCompletion`, lines 619-637.
- Mechanism: For a same-assembly non-constructor method, `Build` adds the own-type `UnknownBoundary` only when `StaticConstructors.Any(c => !c.IsImplicitlyDeclared && StaticConstructorCanAffectEntry(c))`. A type whose static field initializer produces only the compiler-generated implicit `.cctor` is `beforefieldinit`: `HasPotentialStaticInitialization` is true, but that `Any(explicit)` predicate is false, so the method summary contains only its body. In contrast, `AddStaticInitializationPotential` deliberately adds possible `TypeInitializationException` for the same static call even when `RequiresStaticInitializationCompletion` is false, establishing that initialization may run although it need not complete before the call.
- Impact: A public/compiler effect summary can remain `Complete` while omitting initializer reads, writes, allocation, capabilities, and `TypeInitializationException`. An allowed-exception or complete effect contract can therefore be falsely proven.
- Safe example/evidence: `static class B { static object S=FailInit(); static object FailInit()=>throw new ApplicationException(); static void Run()=>throw new InvalidOperationException(); }`. A legal beforefieldinit schedule can initialize first and surface `TypeInitializationException`, but `Run`'s summary reports only `InvalidOperationException`. Repository `EffectAnalysisTests.cs` already has `BeforeFieldInitBomb`/`BeforeFieldInitMethodMayRun` at lines 4989-4995 and 5275, with assertion 5515 that the type-initialization catch is reachable, while `Build` omits it from `Run`'s summary.
- Duplicate distinction: Wave 12.11 is a definitely diverging explicit `.cctor` omitted by the completion test; Wave 3.13 is definitely failing static-field access; Wave 9.19 is generic explicit-cctor field access. This is the separate implicit/beforefieldinit static-method entry path. Live `BUGS.md` had no beforefieldinit/implicit-static-constructor entry.

## Wave 18.16. HIGH - Constructor member-initializer scanning can suppress `ArrayTypeMismatchException` through a cross-tree `SpanStart` collision in scanner core state

- Files/members/current lines: `SharpProof.Effects/OperationEffectScanner.cs`, constructor lines 91-97 (`_freshArrayTypes` populated only from constructor root, keyed solely by integer `creation.Syntax.SpanStart`); `ArrayStoreIsDefinitelyCompatible`, lines 485-513, especially lookup 499-505 and conversion check 511-513. `SharpProof.Effects/ConversionOwnershipClassifier.cs`, `ClassifyRegion`, lines 60-65 and reference-conversion preservation 539-546. `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `ScanConstructorMemberInitializers`, lines 122-158.
- Mechanism: `SpanStart` is syntax-tree-relative, but both Fresh region IDs and `_freshArrayTypes` omit `SyntaxTree`. One partial declaration's member initializer can therefore reuse the same integer start as an array creation in another partial declaration's constructor body. Scanner construction records the body array's runtime type. While the same scanner later processes every member initializer across all declaring trees, a different creation at the colliding start resolves to that unrelated runtime type. `ArrayStoreIsDefinitelyCompatible` can then claim compatibility and omit the real covariant-store exception.
- Impact: A complete constructor summary can omit a reachable `ArrayTypeMismatchException`, corrupting downstream sequencing, catch reachability, and effect-contract decisions.
- Safe example/evidence: Two syntax trees define a partial class. In file A, use member initializer `((object[])new string[1])[0] = new object()`; in file B, pad a constructor-body `new object[1]` so the two array creations have the same numeric `SpanStart`. The map records `object[]`; the initializer's actual `string[]` is classified as the same `Fresh(start)` through the reference conversion; object-to-object appears implicit, so the scanner omits `ArrayTypeMismatchException` although runtime stores an object into `string[]` and throws.
- Duplicate distinction: Wave 7.13 is definitely out-of-range array completion and Wave 9.14 is negative array creation completion; neither covers covariant store compatibility or cross-tree Fresh/cache identity. Live `BUGS.md` had no `ArrayTypeMismatchException`, array-store, covariant-array, or fresh-span entry.

## Wave 18.17. HIGH - Captured primary-constructor parameter assignments omit receiver writes

- Files/members/lines: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanWriteTarget`, lines 5-33, especially parameter arms 24-29; contrast `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCoreOperation` lines 239-246; `SharpProof.Effects/PrimaryConstructorParameterOwnership.cs`, `IsReceiverBacked` lines 7-29.
- Mechanism: Reads of a primary-constructor parameter captured by an instance member explicitly use `IsReceiverBacked` and map to `Receiver`. The write path has no matching check: it emits a write only for `RefKind.Ref`/`Out`, then groups every ordinary `IParameterReferenceOperation` with locals/discards as `Empty`. Therefore direct `seed = value`, `seed++`, or compound writes in an instance member mutate the compiler-captured object field at runtime but contribute no receiver write (increment contributes only the receiver read).
- Impact: A complete method can be certified non-writing/observable-pure although it mutates its receiver, and that false summary propagates through calls.
- Safe reproduction/evidence: `sealed class C(int seed) { public void Set(int value) { seed=value; } public int Read()=>seed; }`. Existing `EffectAnalysisTests.CapturedPrimaryConstructorParametersReadReceiverState` (lines 1128-1198) confirms the intended read mapping but has no write case; current `ScanWriteTarget` takes the `Empty` arm.
- Closest-entry distinction: Wave 7.7 concerns ref-local pointee writes; Wave 7.8 concerns `= ref` rebinding; Wave 15.27 concerns ownership after ordinary reference-parameter reassignment. This is direct assignment to the compiler-captured storage of a primary-constructor parameter.

## Wave 18.18. HIGH - Terminal deconstruction assignment drops target-prefix writes and throws while remaining complete

- Files/members/lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanDeconstruction`, lines 17-31; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteDeconstruction`/`CanCompleteDeconstructionTarget`, lines 780-809; `SharpProof.Effects/EffectSummaryOperations.cs`, `MayDiverge` lines 117-120.
- Mechanism: `ScanDeconstruction` scans only the RHS. If the whole operation may complete, it adds `Unsupported`; but when any deconstruction phase or target cannot complete, it returns only the complete `MayDiverge()` summary and never scans any target. Earlier targets execute before a later throwing/diverging setter, so their writes/calls are lost; the terminal setter's exception/capability is also lost and replaced by generic divergence.
- Impact: A method can have a `Complete` no-write/no-throw summary despite performing a write and then throwing during deconstruction; catch and downstream effect decisions can be unsound.
- Safe reproduction/evidence: `sealed class T { public int First { set { State.Value++; } } public int Second { set { throw new InvalidOperationException(); } } }` then `(t.First,t.Second)=(1,2);`. Completion checks both targets with `All`, sees Second cannot complete, and `ScanDeconstruction` selects `MayDiverge` without visiting First or Second. Existing `AfterDivergingDeconstructionSetter` test uses only one diverging setter and checks later-code suppression, so it does not cover an executed effectful prefix or thrown effect.
- Closest-entry distinction: Wave 11.7 is positional-pattern `DeconstructSymbol` omission; Wave 11.6 is compound-assignment conversion omission. This is deconstruction-assignment target sequencing plus the terminal-path branch dropping `Unsupported`/effects.

## Wave 18.19. MEDIUM - `out` arguments are unconditionally modeled as reading their destination

- Files/members/lines: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep` argument loop lines 641-648 and `ScanCoreOperation` parameter arm lines 239-246; `ClassifyArguments` lines 1290-1305.
- Mechanism: Every call argument is scanned with `ScanStep(argument.Value)`, whose default access is `Read`. An enclosing ref/out parameter reference therefore emits `Read(Parameter)` before resolution regardless of `argument.Parameter.RefKind`. Passing an `out` destination evaluates its location but does not read its old value.
- Impact: Exact complete forwarding methods gain false `ReadsArgumentState` (and field/array out destinations gain false state reads), rejecting valid no-read contracts and contaminating callers.
- Safe reproduction/evidence: `static void Fill(out int x){x=0;} static void Forward(out int x)=>Fill(out x);`. Forward's `x` is an `IParameterReferenceOperation` with `RefKind.Out`, so lines 239-246 add `Parameter(0)` to Reads before Fill's write is resolved.
- Closest-entry distinction: Wave 7.16 is receiver-null checking before argument evaluation and omitted argument effects; Wave 7.7/7.8 concern ref-local mutation/rebinding. None covers the unconditional value-read classification of an out lvalue.

## Wave 18.20. MEDIUM - Coalesce evaluation retains the impossible null result when fallback is nonnull

- File/member/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `EvaluateCoalesce`, lines 722-735, especially 724-735; downstream `ManagedFlowResult.ProvesNonNull` lines 1268-1270; `SharpProof.Effects/OperationNullnessEvaluator.cs`, `IsProvenNonNull` lines 98-104. Existing regression expectation: `SharpProof.Effects.Test/ManagedAbstractFlowTests.cs`, `UnknownConditionalAndMaybeNullCoalesceRemainExplicit`, lines 192-238.
- Mechanism: For a `MaybeNull` left operand, `EvaluateCoalesce` returns `Join(value, Evaluate(WhenNull))`. The left contribution is unrefined, so it still contains null even though the left arm of `??` produces a result only on the nonnull substate. `MaybeNull` joined with a definitely-nonnull fallback remains `MaybeNull`.
- Impact: `object Result(object? x) => x ?? new object()` is not proven nonnull. Nullness/effect and exception-reachability consumers can retain spurious NRE paths or fail a nonnull precondition despite the language guaranteeing a nonnull result.
- Safe evidence: Set `x` to `Reference(MaybeNull)` and evaluate `x ?? new object()`. Line 735 joins `MaybeNull` with `NonNull` and yields `MaybeNull`. The cited existing test explicitly asserts both `IsDefinitelyNull` and `IsDefinitelyNonNull` are false, codifying the loss.
- Duplicate distinction: Distinct from Wave 7.21 (path-insensitive effect scanning of coalesce branches) and Wave 10.19 (meta-analyzer misses coalesce-assignment cache writes); this is the scalar abstract evaluator's result nullness for ordinary `??`.

## Wave 18.21. LOW - Boolean negation maps BooleanTop to untyped Unknown

- File/member/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `ManagedAbstractValue.NegateBoolean` lines 1606-1609; `BooleanUnknown` lines 1454-1458; `EvaluateUnary` lines 694-705. Also `SharpProof.Analyzer.Core/ManagedContractFacts.cs`, `Evaluate` unary-not at lines 73-75.
- Mechanism: `NegateBoolean` returns a Boolean only for singleton inputs; non-singleton `BooleanUnknown` returns global `ManagedAbstractValue.Unknown` instead of `BooleanUnknown`, although logical negation maps `{false,true}` exactly to `{false,true}`.
- Impact: `bool y = !x` destroys the Boolean-domain tag. Later boolean equality/refinement requires `current.IsBoolean` (`Refine` lines 429-435), so supported control facts cannot be refined and downstream proofs/reachability become unnecessarily `Unknown`.
- Safe evidence: `NegateBoolean(ManagedAbstractValue.BooleanUnknown).IsUnknown` is true; the exact abstract result is `BooleanUnknown`. Existing unary test lines 93-116 covers only singleton true.
- Duplicate distinction: No current `BUGS.md` Boolean/control entry addresses non-singleton unary-not abstract transfer.

## Wave 18.22. LOW - Sequence Append/Concat discard guaranteed nonemptiness whenever the upper bound is unbounded

- Files/members/current lines: `SharpProof.Dataflow/SequenceCardinalityDomain.cs`, `Append` lines 142-156 and `Concat` lines 158-171; `SharpProof.Dataflow/IntervalDomain.cs`, `Add`/`TryAddBounds` lines 175-203 and 269-280.
- Mechanism: `Add` substitutes `long.MaxValue` for an absent upper bound. Adding a positive value makes the synthetic maximum exceed `long.MaxValue`, `TryAddBounds` returns false, and `Add` returns full signed `Top`. `Sequence Create` restricts that to `[0,+inf]` and canonicalizes kind `Top`, discarding the lower bound even though positive append or concatenation with `NonEmpty` guarantees length >=1.
- Impact: Public sequence transformers lose the basic nonempty fact on common open-ended inputs, making downstream empty/nonempty checks and proofs abstain.
- Safe evidence: `Append(SequenceCardinalityValue.Top,1)` returns `SequenceCardinalityValue.Top` (includes zero), not `NonEmpty`; `Concat(NonEmpty,Top)` likewise returns `Top`. The loss comes only from synthetic upper-bound overflow; every successful result is nonempty.
- Duplicate distinction: Distinct from Wave 4.6 endpoint canonicalization and Wave 15.26 weak lattice tests; this is a concrete sequence transfer-function precision defect.

## Wave 18.23. HIGH - Source exception construction skips mandatory type-initialization exception reachability

- File/member: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions` object-creation arm, lines 536-573, especially the `!IsExceptionType(creation.Type)` gate at 543-550; helper `AddStaticInitializationPotential`, 1599-1630; `IsExceptionType`, 2663-2668.
- Mechanism: Object creation calls `AddStaticInitializationPotential` for every constructor except when the created type derives from `System.Exception`. For a source exception class with an explicit failing static constructor, `GetCallableExceptions` then inspects only the instance constructor body, so no `TypeInitializationException` enters `PotentialExceptions`.
- Impact: Matching catches are declared unreachable; handler writes/allocation/throws are omitted, enabling unsound complete effect claims. The construction can also be treated terminal without retaining the exception that caused termination.
- Safe repro/evidence: `sealed class BombException: Exception { static BombException()=>throw new ApplicationException(); public BombException(){} }` then `try { _=new BombException(); } catch(TypeInitializationException){ State=new object(); }`. Runtime initializes the type before construction and enters the catch; lines 543-550 skip that boundary solely because `BombException` is an exception.
- Nearest distinction: Wave 15.16 is metadata exception constructor-body/spec suppression at 561-565; this is source exception static initialization suppressed by the earlier 543-550 gate. Wave 3.13 is the static-field resolver, not construction/catch reachability.

## Wave 18.24. HIGH - Bare rethrow widens the actual exception to the catch declaration and hides reachable subtype catches

- Files/members: `SharpProof.Effects/EffectExceptionFlow.cs`, `ResolveRethrow`, lines 54-66, especially 58-62; consumed by `SharpProof.Effects/ExceptionHandlerReachability.cs`, explicit/rethrow arm 174-181 and `CanKnownReach` 2743-2764 / `CatchesKnownType` 2787-2799; also final throw summaries through `OperationEffectScanner`.
- Mechanism: `throw;` preserves the original runtime object/type, but `ResolveRethrow` replaces it with the enclosing catch's declared type. `CanKnownReach` then performs one-way `IsDerivedFrom(thrown,caught)`, so an outer catch for the real subtype is classified unreachable.
- Impact: Reachable outer-handler writes, allocations, and throws are omitted; the widened base exception can also remain falsely escaping.
- Safe repro/evidence: `class BaseE:Exception{} sealed class DerivedE:BaseE{}; try { try { throw new DerivedE(); } catch(BaseE){ throw; } } catch(DerivedE){ State++; }`. Runtime enters the outer catch. Analysis emits `BaseE` at 60-62, then `IsDerivedFrom(BaseE, DerivedE)` is false at 2798.
- Nearest distinction: Wave 7.2 covers operand-based `throw e` using the operand's static type/nullness. Bare `throw;` has no operand and loses precision specifically in `ResolveRethrow`.

## Wave 18.25. MEDIUM - Explicit throws in semantically unreachable finally blocks are still added by the lexical throw pass

- File/members: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanLexicalControlEffects`, lines 129-175, and `IsReachable`, 1251-1276, especially unconditional `FinallyClauseSyntax => true` at 1263-1265; joined unconditionally by `SharpProof.Effects/EffectMethodNodeBuilder.cs`, lines 77-81.
- Mechanism: The lexical pass scans every source throw. After only compile-time/Roslyn reachability, `IsReachable` declares every operation under a `finally` reachable, without asking whether the protected body can ever unwind. Interprocedurally proven divergence therefore cannot suppress a never-entered finally throw.
- Impact: A diverging/no-throw method acquires a false exact `Throws` effect and can fail `DoesNotThrow`/allowed-exception checks.
- Safe repro/evidence: `static void Spin(){while(true){}} static void M(){ try { Spin(); } finally { throw new ApplicationException(); } }`. Runtime never reaches the finally, but the lexical pass accepts the throw at 1263-1265 and unions it at builder line 80.
- Nearest distinction: Wave 15.10 concerns Roslyn-reachable impossible catch cycles fabricating `MayDiverge`; this is a never-entered finally fabricating a concrete exception.

## Wave 18.26. MEDIUM - Semantically noncompleting finally calls fail to suppress a protected exception

- File/member: `SharpProof.Effects/EffectExceptionFlow.cs`, private `KeepEscaping`, lines 87-135, especially finally suppression at 123-131.
- Mechanism: Original exceptions are discarded only when Roslyn `AnalyzeControlFlow(finally.Block).EndPointIsReachable == false`. That syntactic API does not know a source callee is proven nonreturning. Other effect components have `CanMethodCompleteNormally`, but it is unavailable here, so an exception that can never finish unwinding stays in the summary.
- Impact: False `Throws` effects and false exception-contract diagnostics for methods that actually diverge in `finally`.
- Safe repro/evidence: `static void Spin(){while(true){}} static void M(){ try { throw new InvalidOperationException(); } finally { Spin(); } }`. Runtime never lets the `InvalidOperationException` escape. Roslyn sees a regular successor after the call, so lines 123-131 do not empty it; the lexical throw pass retains it.
- Nearest distinction: Wave 7.3 is a syntactically present but unreachable bare rethrow inside a catch; this is replacement/suppression of a protected exception by an interprocedurally noncompleting finally.

## Wave 18.27. HIGH - Virtual source dispatch inherits the declared base body's noncompletion and suppresses returning overrides

- Files/members/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` 588-609 (especially 607-609), `CanCompleteProperty` 698-713; recursive counterpart `SharpProof.Effects/ManagedAbstractFlow.cs`, `InvocationMayCompleteNormally` 2248-2267 and property branch 2034-2040. Result use: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep` 655-667 passes `dispatchUncertain` to effect resolution but calls completion without it; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` 351-373 stops successors when false. Contrast `OperationCompletionEvaluator.CanDirectListPatternMemberCompleteNormally` 418-423, which correctly treats abstract/unsealed virtual dispatch as potentially completing.
- Mechanism: A virtual base method/getter with source body `throw` makes `MethodCanCompleteNormally(baseMember)` false. The call/property completion predicate ignores virtual dispatch even though effect resolution records it as uncertain. A returning override therefore cannot rescue the normal path.
- Impact: Real caller suffix effects are omitted; a complete purity/no-write summary can be accepted despite reachable writes.
- Safe reproduction/evidence: `class B { public virtual void M()=>throw new Exception(); public virtual int P=>throw new Exception(); } sealed class D:B { public override void M(){} public override int P=>1; } static void C(){ B x=new D(); x.M(); _=x.P; s_state++; }`. An in-memory runtime probe returned after both calls and incremented state.
- Duplicate distinction: Wave 11.4 is the same consumer poisoned by recursive-cycle handling, not dispatch; Wave 10.13 concerns exception reachability for metadata ref-like getters. No existing virtual-completion entry was found.

## Wave 18.28. HIGH - Unreachable terminal statements after a reachable return veto method normal completion

- Files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `MethodCanCompleteNormally` 1918-1942; `MayCompleteNormally` treats `IReturnOperation` by its children at 1967-1969 and method/block bodies as `SequenceMayCompleteNormally` at 1970-1975; `SequenceMayCompleteNormally` 2288-2299 linearly requires every lexical child to complete and never consults CFG reachability. Consumption: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` 607-609; `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep` 665-667; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, CFG successor suppression 351-373.
- Mechanism: `return;` is considered normally completing, then an unreachable following `throw` or diverging call is still visited and flips the whole method to false. Goto-skipped terminal statements have the same shape.
- Impact: Callers treat a definitely returning source method as terminal and omit reachable suffix writes, allocations, and calls, enabling false effect proofs.
- Safe reproduction/evidence: `static void Returns(){ return; throw new Exception(); } static void Caller(){ Returns(); s_state++; }`. This is valid C# with an unreachable-code warning; an in-memory runtime probe produced `state=1`. The helper returns false by direct branch trace.
- Duplicate distinction: Wave 5.9 is a switch-expression unmatched-path error, Wave 11.4 is recursion, and Wave 7.3 uses unreachable rethrows only to corrupt the escaping exception set. This finding is lexical sequence/fallthrough classification.

## Wave 18.29. HIGH - Async and iterator invocation completion is inferred from the eventual/deferred body instead of call-expression completion

- Files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `MethodCanCompleteNormally` 1918-1942 and body/throw handling 1961-1975, with no `method.IsAsync` or iterator/yield boundary; consumed by `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` 588-609; `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep` 655-667; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` 351-373. `SharpProof.Effects/EffectAnalysisSession.cs`, `BuildNodes` 427-456 transitively builds all source callees without applying `LanguageSubsetGate`, so an otherwise supported synchronous selected caller reaches this path even though async roots are gated elsewhere.
- Mechanism: An async method whose body definitely throws is classified noncompleting, although invocation returns a faulted `Task`; an iterator whose eventual execution cannot complete is likewise called successfully because its body is deferred.
- Impact: Reachable effects immediately after an unawaited async call or iterator creation disappear from a caller's complete summary.
- Safe reproduction/evidence: `static async Task Faulted(){throw new E();} static void C(){_=Faulted();s_state++;}`; and `static IEnumerable<int> Seq(){try{yield return 1;}finally{throw new E();}} static void C(){_=Seq();s_state++;}`. In-memory runtime probes incremented state in both cases.
- Duplicate distinction: Existing completion bugs Wave 5.9 and Wave 11.4 concern ordinary body-path/cycle logic; `BUGS.md` had no async/iterator invocation-completion entry.

## Wave 18.30. MEDIUM - Default-true completion for supported composite operations discards a terminal inner step

- Files/members/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteNormally` 38-135 has no `IArrayInitializerOperation` or `IInterpolatedStringOperation` case and defaults true at 134; `CanCompleteArrayCreation` 917-928 delegates initializer completion to that default. `SharpProof.Effects/OperationEffectScanner.cs`, `ScanStep` 971-974 attaches the outer evaluator result after specialized scanning; array scanning 817-833. `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, interpolation scanning 371-437 can internally encounter and stop on a noncompleting formatter but returns only a summary.
- Mechanism: The inner scan observes a terminal `Fail()` element or formatting call, yet the enclosing initializer/interpolation is relabeled completing by the default-true outer predicate.
- Impact: Impossible suffix effects remain in otherwise supported effect discovery, causing false writes, allocations, and contract failures.
- Safe reproduction/evidence: `_ = new[] { FailInt() }; s_state++;` where `FailInt` always throws; or sealed non-`IFormattable` `ToString()=>throw` in `_=$"{value}"; s_state++;`.
- Duplicate distinction: Wave 7.22 prematurely blocks object-initializer scanning and omits inner effects; here inner effects are scanned but terminality is lost. Waves 7.23 and 12.28 cover formatting target selection and exception reachability, not completion propagation.

## Wave 18.31. HIGH - Default API-spec reference-family authentication is forgeable through public-sign metadata plus a spoofed path

- Files/members/current lines: `SharpProof.Effects/ApiSpecResolution.cs`: `ResolveTemplate` 129-191 (authority use 147-168), `MatchAssembly` 222-255 (name/token only at 226-233), `ClassifyReferenceFamily` 257-280 (substring path markers at 264-270), `HasExpectedReferenceMetadata` 282-301 and `IsAttribute` 303-328 (`ReferenceAssemblyAttribute` accepted by metadata namespace/name only). Catalog authority: `SharpProof.Specs/DefaultApiSpecCatalog.generated.cs` 202-221 includes approved `System.Linq` runtime identity.
- Mechanism: Assembly approval checks only simple name and public-key token from metadata; Roslyn does not verify a private-key signature. Family approval then trusts a user-controlled `FilePath` substring and either the presence/absence of an attribute recognized only by namespace/name. A public-signed/hand-authored PE can copy the approved public key, choose an approved assembly name, and place itself under a matching `/shared/Microsoft.NETCore.App/` or `/packs/...ref/` spelling; all gates pass without payload authentication.
- Impact: An arbitrary implementation can receive a trusted default API row. Managed flow and compiler/worker consumers may assume false effects, nullness/cardinality, or postconditions, producing unsound `Proven` outcomes/reachability.
- Safe repro/evidence: Author fake `System.Linq.dll` with copied BCL public key metadata and an arbitrary `System.Linq.Enumerable.Empty<T>` (for example returns a nonempty sequence), put it under a path containing `/shared/Microsoft.NETCore.App/`, and omit `ReferenceAssemblyAttribute`. Reference only corelib plus this PE. Name/token match the default approved row, runtime-family classification passes, documentation ID/shape resolve, and `ManagedAbstractFlow.ReturnValue` (`SharpProof.Effects/ManagedAbstractFlow.cs` 610-638) assumes `NonNull+Empty` for the arbitrary call.
- Closest-entry distinction: Wave 9.9 covers `CompilerSpecificationPackProvider.MatchesAssembly` for opt-in relational spec packs. This is the separate `ApiSpecResolver`/default catalog path, adds independently forgeable family gates, and remains exploitable if Wave 9.9's provider alone is fixed. Wave 5.16 is the opposite Contract API payload-hash/metadata mismatch and does not protect BCL rows.

## Wave 18.32. MEDIUM - Equivalent file-backed SharpProof.Attributes alias references disable the entire trusted Contract API

- File/members/current lines: `SharpProof.Frontend/ContractApiIdentityResolver.cs`: constructor 38-47; `HasTrustedAttributesPayload` 180-210, especially matching-reference collection 188-194 and exact-cardinality check 195-201.
- Mechanism: After resolving the genuine candidate assembly symbol, the resolver gathers every compilation reference mapping to that symbol and requires `matches.Length == 1` before hashing. Roslyn compilations may retain/unify multiple reference views of the same genuine PE (for example global plus extern-alias references). Two authentic, file-backed, byte-identical references therefore fail before either payload is hashed.
- Impact: `Contract` becomes null and all genuine clause methods, closed contracts, effect attributes, controls, and `ContractFor` metadata are treated as unavailable/rejected, causing global abstention or diagnostics in otherwise valid alias-heavy hosts.
- Safe repro/evidence: Create two `PortableExecutableReference`s from the shipped `SharpProof.Attributes.dll`, one global and one with alias `SP`, add both to one compilation, and use the global Contract API. Both map to the resolved assembly; lines 188-194 collect two and lines 195-201 reject despite valid paths and bytes.
- Closest-entry distinction: Wave 8.4 is a single authentic in-memory reference rejected because `FilePath` is null. Here every reference is authentic and file-backed; the independent failure is exact reference cardinality (`matches.Length != 1`).

## Wave 18.33. MEDIUM - Validate-then-copy races admit unvalidated caller-array elements into core IR

- Files/members/current lines: `SharpProof.Ir/IrFactory.cs` `GetOrCreateMember` 162-190, especially validation loop 173-176 then snapshot 179; private `Opaque` 577-611, especially `ValidateCallShape` 584-590 then `ToImmutableArray` 604-611. `SharpProof.Ir/IrProgramBuilder.cs` `MemberLocation` 33-40, especially validation 37-38 then copy 39.
- Mechanism: All three validate a caller-retained params array and only afterward copy it. The factory lock does not protect that array from a second caller thread. An element can be valid when visited and be replaced before the later copy. `GetOrCreateMember` can retain a foreign/default `IrTypeId` in `IrMemberInfo`; `Opaque` can retain a foreign-factory or signature-mismatched `IrTerm` and hashes only its numeric `Id.Value`; `MemberLocation` can return a location whose arguments were never validated. This breaches the factory-scoped typed-term/model invariant.
- Impact: Malformed member metadata or an invalid hash-consed opaque node becomes publicly reachable; later traversal/encoding/evaluation can reject unexpectedly or consume foreign nodes, and the bad opaque node remains in the factory interning table. `MemberLocation` is rechecked if later appended, but it has already been returned as a supposedly valid model object.
- Safe evidence: Pass a large params array whose early element is toggled by another thread between a valid local term/type and a foreign or wrong-typed one while repeatedly constructing. Validation walks the original array, then `ToImmutableArray`/`[..arguments]` rereads it. Snapshot-before-validation removes the gap.
- Duplicate distinction: Wave 13.9 is the analogous unsnapshotted dictionary bug only in `ApiSpecInstantiation`; it does not cover these public IR constructors or persistent hash-cons poisoning. Wave 6.17 covers callbacks under `_gate`/deadlock, not caller-array mutation.

## Wave 18.34. LOW - IR metadata accepts ill-formed UTF-16, making portable graph bytes/fingerprints non-injective

- Files/members/current lines: `SharpProof.Ir/IrFactory.cs` `InternString` 80-87; `CreateVariable` 142-150; `CreateOperation` 203-210; `ValidateName` 620-625 (only whitespace); `GetOrCreateReferenceType`/`GetOrCreateSequenceType`/`GetOrCreateMember` use that validator. `SharpProof.Ir/IrProgramBuilder.cs` `CreateBlock` 14-21. Portable encoding: `SharpProof.CompilerArtifact/PortableIrModel.generated.cs` `Encoder.TypeRow`/`VariableRow`/`MemberRow`/`OperationRow` 246-279 and `PortableIrGraphCodec.cs` `BlockRow` 331-335. Fingerprint: `CompilerFeatureScopeFingerprint.AddJson` 96-106.
- Mechanism: Unlike `IrFactory.String` (317-325), metadata constructors accept lone surrogates. D800 and D801 remain distinct in the factory's ordinal string table but `System.Text.Json` UTF-8 serialization replaces both with U+FFFD. Encoded graphs that differ only in accepted type/variable/member/operation/block metadata therefore serialize identically and receive the same feature-scope hash; JSON round-trip also loses the accepted value.
- Impact: Accepted in-memory graph metadata is not faithfully round-trippable and canonical authenticated bytes are not injective over accepted state; diagnostics/provenance can change or collide.
- Safe evidence: Build two otherwise identical graphs with a block name or operation description containing lone D800 versus lone D801; both constructors succeed, `GetString` distinguishes them, but `SerializeToUtf8Bytes(graph, WorkerProtocolJson.Options)` and `CompilerFeatureScopeFingerprint` collapse them.
- Duplicate distinction: Wave 3.11 covers runtime `IrValue` strings; Wave 13.4 covers protocol request/manifest identity fields; Wave 14.25 covers unescaped legal type names in `IrPrinter`. None covers IR metadata admission plus portable graph/fingerprint collision.

## Wave 18.35. MEDIUM - UNSAT-core decoding ignores cancellation and query resource limits

- File/member/current lines: `SharpProof.Smt/IrSmtBackend.cs`, `CheckCore` lines 146-154 (dispatch at 151), `CreateUnsatisfiable(Solver,...)` lines 202-211, generic `CreateUnsatisfiable<T>` lines 213-241 (array decode 219-233 and full disposal pass 235-240).
- Mechanism: Cancellation is checked immediately after `solver.Check()`/`AccountResources` at line 148, but the UNSAT branch then calls `solver.UnsatCore` and synchronously materializes every native core wrapper, formats each through `Expr.ToString()`, does dictionary lookup/dedup/sort, and finally walks the entire array again to dispose it. Neither overload accepts/polls the token; Z3 rlimit meters only `solver.Check()`. The next token check is only after `CheckCore` returns (`CheckAsync` line 59), so cancellation arriving during core extraction cannot complete until all result work and cleanup finish.
- Impact: A proof with a very large essential assumption core can hold the backend gate and delay method/project cancellation while consuming native/managed CPU and allocation after the solver budget has finished. Queued checks remain blocked behind it.
- Safe reproduction/evidence: Make N Boolean assumptions `b0`...`bN` and goal their conjunction so the proof core needs all N labels; cancel after `solver.Check` returns while core materialization/formatting runs. The task cannot observe cancellation until both O(N) passes complete. The generic internal helper's signature itself proves no token path.
- Novelty/distinction: Live `BUGS.md` had no UNSAT-core/cancellation finding. Wave 2.6 explicitly covers model-variable construction and SAT model decode; Wave 13.7 covers pre-solve wide AST encoding. This is the opposite, post-solve UNSAT result path.
- Validation: Canonical container `docker compose run --rm tooling test -Target SharpProof.Smt.Test` passed 23/23.

## Wave 18.36. MEDIUM - Ordinary backend check exceptions bypass the proof outcome and typed InfrastructureFailure channel

- Files/members/current lines: `SharpProof.Verify/ProofKernel.cs`, `ProofKernel.VerifyAsync`, lines 8-28, especially direct await at line 14; public backend contract `SharpProof.Verify/Backend.cs`, `ISmtBackend.CheckAsync`, lines 69-74, and `BackendFailureReason.InfrastructureFailure`, lines 10-19. Current production manifestation remains possible because `SharpProof.Smt/IrSmtBackend.cs`, `CheckAsync`, lines 32-82, catches only `Z3Exception`, `InvalidOperationException`, `ArgumentException`, and `ArithmeticException` at lines 67-75.
- Mechanism: `ProofKernel.VerifyAsync` directly awaits `_backend.CheckAsync(...)` without an ordinary-exception containment boundary. A synchronous throw or faulted task (for example `TimeoutException`, `IOException`, `NullReferenceException`, or an injected backend's `InvalidDataException`) escapes instead of becoming `UnknownOutcome(AbstentionReason.InfrastructureFailure)`. The typed failure channel exists, and `docs/unknown-reasons.md` lines 120-125 states failed checks become `Unknown`, but only backends that voluntarily translate their errors can reach it.
- Impact: One backend implementation/runtime defect aborts orchestration without any `ProofOutcome`; direct callers receive the raw exception, while the packaged path can lose claim-local association and fall into outer generic failure handling instead of returning a typed claim-level infrastructure result. This defeats the kernel's intended failure-containment boundary.
- Safe example/evidence: Implement `ISmtBackend.CheckAsync` as `throw new InvalidDataException("boom")` (or return `Task.FromException<BackendCheckResult>(...)`), construct any valid `VerificationQuery`, then await `new ProofKernel(backend).VerifyAsync(query)`. Actual: original exception escapes. Expected: `UnknownOutcome` with `AbstentionReason.InfrastructureFailure`. `OperationCanceledException`, `OutOfMemoryException`, and `StackOverflowException` should remain propagation exceptions.
- Duplicate distinction: Wave 2.10 covers `OperationCanceledException` being misclassified as method timeout; Wave 3.33 covers backend `Dispose` exceptions; Wave 3.34 and Wave 13.2 cover lane/factory construction failures; Wave 9.1 covers Program file-load classification. None covers an ordinary exception from `ISmtBackend.CheckAsync` escaping `ProofKernel.VerifyAsync` itself.

## Wave 18.37. MEDIUM - Callable-wide lowering failure erases independent compiler effect evidence

- Files/members/current lines: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`, `BuildTarget`, lines 94-110 and 131-136: effect claims/evidence are independently produced and appended after postcondition claims. `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`, `Prepare`, lines 86-104, especially `requiresBodyAdmission = !target.Claims.IsDefaultOrEmpty || !contracts.Clauses.IsDefaultOrEmpty` and the final `target.Claims.IsDefaultOrEmpty ? null : preparedBody`; `PrepareBody`, lines 121-125. `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `Create`, lines 53-64: effect claims and authorities are deliberately reattached after callable encoding, including failed lowering. `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `Decode`, lines 264-278: decoded effect claims are preserved on failed `CompilerCallablePreparation` values. Downstream `SharpProof.Worker/CallableVerificationPolicy.cs`, `VerifyTargetAsync`, lines 15-20 and 27-46.
- Mechanism: `target.Claims` contains postconditions only; effects live separately in `target.EffectClaims`. Compiler production and decoding preserve those per-claim authenticated effect artifacts even when postcondition/body lowering fails. Nevertheless, `VerifyTargetAsync` sees `!target.IsSuccess` and immediately replaces every claim with callable-wide `Unknown` before inspecting the effect artifacts. A deterministic subtype occurs for an effect-only target with a `Requires`/`Assume`: it has no postcondition claim, but any contract clause forces full body admission. The lowerer then throws the prepared body away because `target.Claims` is empty. For a void method, any executable statement makes `ContainsOnlyContractStatements` false at `PrepareBody` lines 121-125; cyclic/nonlowerable non-void bodies fail similarly. Preparation becomes `UnsupportedBody`, so policy exits at lines 15-20 instead of assembling the already-produced effect result and applying entry feasibility.
- Impact: Any unrelated postcondition/body-lowering abstention can erase an otherwise independent, authenticated compiler effect outcome. In the clearest trigger, merely adding a valid precondition turns a proven effect-only claim into deterministic `Unknown/UnsupportedBody`; every effect-only executable void method with a precondition is affected. Valid `DoesNotThrow`, `ZeroAllocations`, and other effect results are lost for supported selected callables.
- Safe reproduction/evidence: Compare `[DoesNotThrow] static void M(int x) { x++; }` with the same body plus `Contract.Requires(x >= 0);`. The first has no clauses, so `Prepare` line 89 skips body admission and its effect evidence reaches assembly. The second has one clause, enters `PrepareBody`, fails at lines 121-125 because `x++;` is executable, and policy emits `Unknown` even though the analyzer-produced effect claim is present and retained in the artifact. This follows by deterministic control-flow trace; no mutation is needed.
- Novelty/duplicate distinction: Live `BUGS.md` had no effect-only/precondition/body-admission or callable-lowering-erases-effect finding. Distinct from Wave 6.4, which concerns expression-bodied void contract-only syntax; Wave 4.4, which concerns postcondition vacuity after contradictory entry; and Wave 5.21, which concerns effect-tuple protocol rejection after effect assembly. This finding prevents effect assembly altogether.

## Wave 18.38. MEDIUM - Noncacheable outcomes hide cache read failures as ordinary misses

- Files/functions/lines: `SharpProof.Worker/VerificationCache.cs`, `VerificationCache.TryReadAsync`, lines 12-96 (all clean misses, corrupt-entry failures, lock/contention IO failures, and permission failures collapse to `null`, especially catch lines 91-95); `SharpProof.Worker/SharpProofWorker.cs`, `SharpProofWorker.VerifyAsync`, cache initialization/read lines 178-206, initial response assembly line 309, conditional write/status update lines 324-335; `VerificationCache.IsCacheable`, lines 408-435.
- Mechanism: `CreateCacheIfEnabled` initializes `cacheStatus = WorkerCacheStatus.Miss`. `TryReadAsync` has no result channel distinguishing a missing key from `IOException`, `UnauthorizedAccessException`, invalid/corrupt cache data, or lock contention; each returns `null`. After recomputation, `SharpProofWorker` changes the status to `Unavailable` only inside the conditional write branch. That branch is entered only for responses accepted by `IsCacheable`, which requires replayable `Refuted` postconditions. `Proven`, `Unknown`, effect, and other noncacheable results skip the write branch and return the original `Miss` status even when the cache lock could not be opened or the cache was unreadable/corrupt.
- Impact: Response telemetry falsely reports a functioning cache miss and hides persistent cache outages for the majority of noncacheable outcomes. Operational detection/remediation is defeated, and the same cache failure is reported differently solely according to the computed proof outcome.
- Safe reproduction/example: Create a cache-enabled project with a tautological `Proven` postcondition. Precreate and hold `.sharp-proof-cache.lock` using an exclusive `FileStream`, following the setup in `SharpProof.Worker.Test/WorkerTests.cs`, `CacheDirectoryLockMakesReadMissAndWriteUnavailable`, starting at line 4720. Call `VerifyAsync`. Read lock acquisition throws and is swallowed; verification computes `Proven`; `IsCacheable` returns false; `Summary.CacheStatus` is `Miss` rather than `Unavailable`. `Unknown` and effect-only results reproduce the same state loss.
- Duplicate distinction: This is not Wave 9.2 (replaceable lock pathname enabling simultaneous cache transactions), Wave 6.10 (interrupted rollback/eviction artifacts), or Wave 13.1 (capacity reconciliation only after cacheable access). It is loss of cache failure state at the `TryReadAsync`/caller boundary.

## Wave 18.39. MEDIUM - Supervisor retries an incomplete descendant cleanup forever and never publishes failure

- File: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`
- Members/current lines: `Run`, lines 118-154, especially `StopDescendants` at 127-130 and unbounded `while (!cleanup.Complete)` at 133-143; `StopDescendants`, lines 204-279, especially false result at 276-278. Downstream ownership: `SharpProof.BuildTasks/RunVerifier.cs`, `Execute`, lines 229-270 and 354-367; `ObserveCleanupAnchorAsync`, lines 688-719.
- Detailed mechanism: `StopDescendants` is intentionally bounded and can return `Complete=false` (the existing `BuildTaskTests.VerifierSupervisorReportsBoundedCleanupFailure` fixture establishes this state). `Run` converts that bounded failure into an unbounded exponential retry loop. The loop has no overall deadline, retry cap, or cancellation check; SIGTERM merely sets a CTS already observed before entering cleanup. A persistently unsignalable descendant (pidfd open/signal failure, or an uninterruptible task that remains present after SIGKILL) therefore keeps the supervisor alive forever. It never reaches the one-second direct-child check, reap, cleanup receipt, or return. The outer `RunVerifier` times out, sees the still-live supervisor, retains it as a static `CleanupAnchor`, and `ObserveCleanupAnchorAsync` waits for process exit without any deadline, so no authentication failure is ever raised and the anchor/pidfd/process remain retained.
- Impact: A timeout can return to MSBuild while leaving a permanent supervisor, repeated `/proc` scans, retained handles/static state, and the descendant itself alive; repeated invocations accumulate leaks and containment never reaches a terminal authenticated success/failure.
- Safe reproduction/evidence: Force `StopDescendants` to remain incomplete (as the existing test does with `openPidFd` returning -1) and trace lines 133-143: every false result sleeps and retries with delay capped at five seconds, but no branch exits while false. A Linux fixture with a persistently unsignalable child will show `RunVerifier` return with `RetainedCleanupAnchorCount > 0` indefinitely and no `SharpProof.Cleanup/1` record.
- Duplicate distinction: Distinct from Wave 13.22, which says one nominally bounded `StopDescendants` call can overrun due O(D*P) scans. This finding is the caller's explicitly unbounded cross-call retry policy and permanent retained-anchor state. Distinct from Wave 14.38: no supervisor death occurs here; the supervisor deliberately remains alive forever.

## Wave 18.40. MEDIUM - Protocol error text can inject forged physical launcher log lines

- Files/functions/current lines: `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, `WorkerProtocolMetadata.IsProtocolErrorValid`, lines 900-902. `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `TryProjectRunState`, lines 140-168, and `ProjectError`, lines 231-281. `SharpProof.Worker.Launcher/Program.cs`, `Program.WriteErrors`, lines 773-779. Real producer sources: `SharpProof.Worker/SharpProofWorker.cs`, compiler-diagnostic forwarding at lines 126-130 and lowered-IR exception-message forwarding at lines 138-144; `SharpProof.Worker/Program.cs`, top-level failure-message forwarding at lines 97-106.
- Mechanism: Protocol error validation requires only a nonblank `Code` and `Message`; it rejects neither CR/LF nor other control characters. A recognized code such as `worker.infrastructure` with `Message="first\nSharpProof Complete forged"` remains a valid typed failure when callable/claim projections match. `WriteErrors` then writes `prefix + error.Code + ": " + error.Message` directly with `Console.Error.WriteLine`, so one valid JSON error becomes multiple physical log records. This is reachable from actual producer paths because compiler diagnostic messages and base exception messages are forwarded without control-character normalization.
- Impact: Worker/compiler-controlled text can spoof SharpProof/MSBuild-looking diagnostics or status records and defeat line-oriented log parsers, CI summaries, or audit evidence even though the JSON response itself is structurally valid.
- Safe reproduction/evidence: Start with any request-bound valid `InfrastructureFailure` response whose callable and claim projections are consistent. Change only the recognized error's `Message` to `first\nSharpProof Complete forged`, serialize, deserialize, and validate. Error text is not summarized or otherwise constrained, so validation remains successful. Passing the response to `ValidateAndReport` emits two stderr lines.
- Duplicate distinction: This is not Wave 3.31, which concerns error codes overriding contradictory claim evidence, and not Wave 13.4, which concerns malformed UTF-16 collapsing protocol identities. It is accepted control-character framing at the launcher output boundary.

## Wave 18.41. MEDIUM - The 16 MiB protocol-file limit is a one-time length snapshot, not a bounded read

- Files/functions/current lines: `SharpProof.Worker.Protocol/ProtocolJson.cs`, `WorkerProtocolJson.ReadUtf8File`, lines 35-39; `ReadUtf8FileAsync`, lines 41-49; `OpenJsonReader`, lines 71-87.
- Mechanism: `OpenJsonReader` checks `stream.Length` once and then returns an ordinary `StreamReader`. The synchronous and asynchronous callers use unbounded `ReadToEnd`/`ReadToEndAsync`; neither limits bytes actually consumed nor rechecks the descriptor length. On the canonical Linux boundary, a native/concurrent writer can grow the already-open regular file after that snapshot. If the growth arrives before the reader reaches the old EOF, the reader consumes and later parses all appended bytes despite `MaximumJsonBytes`.
- Impact: Request/result framing's advertised 16 MiB hard ceiling can be raced, causing string allocation and JSON parsing substantially above the intended resource bound.
- Safe reproduction/evidence: Hold a near-limit valid regular JSON file open through a native writer, begin the protocol read, and append a large amount of valid trailing JSON whitespace after the length check but before the reader reaches EOF. The current read has no byte-limiting wrapper and can return more than 16 MiB rather than raising `InvalidDataException`. A deterministic regression test can expose a post-length-check seam or exercise a bounded-stream replacement.
- Duplicate distinction: Wave 3.30 concerns serializers producing a document larger than the fixed reader ceiling, while Wave 2.14 concerns blocking on a FIFO/non-regular result path. This is a regular-file mutation bypass of the reader ceiling itself.

## Wave 18.42. HIGH - Shipped MSBuild target never activates exact-invocation result validation

- Files: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`; `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`
- Members/current lines: `_SharpProofVerifyCore`; `ValidatePublishedVerificationResult.Execute`; target invocation `SharpProof.Verifier.targets:214-217`; task inputs/private validation `ValidatePublishedVerificationResult.cs:23-25, 46-59, 70-79, 104-118`.
- Mechanism: The task now accepts `InvocationResultPath`, validates that private per-GUID response, constrains the public response with its `InputHash`/`Manifest`, and compares the serialized public response with that exact invocation. The shipped target still passes only `RequestPath`, `ResultPath`, and `ManifestPath`. Consequently `InvocationResultPath` is always null in normal package builds, all exact-invocation checks are skipped, and the public response is validated only as a standalone internally consistent response.
- Impact: Concurrent builds that share public publication paths can make one invocation accept and advertise another invocation's valid published evidence. The per-run private response exists specifically to distinguish those executions, but package integration never uses it. This affects two same-target-framework builds using default paths, not only multitarget custom-path scenarios.
- Safe reproduction/evidence: Run two same-project/same-output builds with different verification inputs, pause build A after its launcher returns, let B publish its request/result/manifest trio, then resume A at final validation. A validates B's internally consistent public response because lines 214-217 omit `InvocationResultPath="$(_SharpProofInvocationResultFile)"`. Supplying that input makes lines 104-118 reject the substitution.
- Duplicate distinction: Not Wave 2.21, which concerns configured paths lacking target-framework scoping, and not Wave 7.36, which concerns invocation paths colliding with compiler outputs. This is a later integration regression: commit blame shows the private-binding input/checks were added while the target invocation remained unchanged.

## Wave 18.43. MEDIUM - Any structured semantic error suppresses reporting of an unrelated verifier timeout or crash

- Files: `SharpProof.BuildTasks/RunVerifier.cs`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`
- Members/current lines: `RunVerifier.Execute`; `RunVerifier.LogStandardError`; `_SharpProofVerifyCore`; exit classification `RunVerifier.cs:317-321`; coarse structured-error flag `RunVerifier.cs:1001-1037`, especially 1024-1027; target suppression `SharpProof.Verifier.targets:207-213`.
- Mechanism: `HasStructuredError` becomes true after any parsed error diagnostic. The target suppresses its verifier-exit error for every nonzero exit code whenever that Boolean is true; it does not distinguish an expected policy-error exit from timeout 124, containment/launch failure -1, or an arbitrary crash exit.
- Impact: A verifier can emit one ordinary SP0047/SP0048 error and then hang or crash. The build fails only with that semantic diagnostic and omits the timeout/crash exit evidence, misclassifying an infrastructure failure as an ordinary proof-policy failure and obscuring remediation/telemetry.
- Safe reproduction/evidence: Use a controlled verifier helper that writes a valid structured error to stderr and exits 17, or writes it and hangs past the project deadline. `RunVerifier` yields `(ExitCode=17 or 124, HasStructuredError=true)`; the condition at lines 212-213 is false, so no `SharpProof verifier failed with exit code ...` error is emitted.
- Duplicate distinction: Not Wave 13.13, which concerns launcher-side remapping of containment status to exit 3. This occurs afterward at the MSBuild task/target boundary and suppresses any unexpected nonzero exit when an unrelated structured error was observed.

## Wave 18.44. MEDIUM - Unselected nested-callable chains can stack-overflow compiler-manifest ID construction

- File: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`
- Members/current lines: `Build`, 22-27 (especially 25-26); `DiscoverMethods`, 459-511 (anonymous/local callable capture at 478-484); `CreateCallableIds`, 552-605 (all seeds at 573-577; recursive local function `Resolve` at 581-596, especially 592-595).
- Mechanism: `DiscoverMethods` inventories every local function and anonymous function in every syntax tree, regardless of whether it has a selected SharpProof feature or claim. `Build` then calls `CreateCallableIds` on that complete inventory before `BuildTarget` filters unselected callables. `Resolve` recursively obtains a nested callable's parent ID via `Resolve(parentMethod)`, with no depth bound or iterative parent-chain walk. A valid/generated chain of nested lambdas or local functions therefore produces call-stack depth proportional to nesting. `ImmutableHashSet` materialization does not establish parent-first order; the first sufficiently deep seed can recursively traverse most/all of the chain. The per-frame cancellation check does not prevent an ordinary uncancelled `StackOverflowException`.
- Impact: A source/generated file that does not use SharpProof contracts at all can terminate the compiler/analyzer host during final-manifest collection. `StackOverflowException` is not recoverable by `FinalCompilationCollector.Collect`'s ordinary `Exception` handler, so the build loses the typed SP0049 failure path and can crash rather than merely abstain.
- Safe reproduction/evidence: Generate one ordinary method containing a long valid nesting chain of block-bodied lambdas/local functions (each body declares the next, the innermost returns 0), with no contract/effect attributes. Run the collector under an enabled profile and increase nesting until `CreateCallableIds.Resolve` exhausts the stack. Static evidence is that every nested symbol is added at 478-484, IDs are computed for all of them before target filtering at 25-26, and each uncached parent adds one recursive frame at 592-595. Fix direction: resolve parent chains iteratively with a hard resource bound, or first filter to selected targets while retaining only the bounded ancestor context needed for IDs.
- Duplicate distinction: Wave 6.2 covers ID renumbering when an unrelated earlier nested callable changes a sibling ordinal; it does not cover unbounded parent recursion or host termination. Wave 11.9 covers recursive expression lowering, and Wave 15.15 covers recursive delegate-alias reachability; this failure occurs earlier in manifest identity construction and is triggered by wholly unselected nested callables.

## Wave 18.45. MEDIUM - Cross-reference ContractFor companion collisions receive no SPCF0002 diagnostic

- Exact files/members/current lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `ValidateContractForCompanions`, lines 187-200. `SharpProof.Analyzer.Core/ContractForValidation/ContractForValidationEngine.cs`, `Validate`, lines 5-14 and 40-68; `FindCandidates`, lines 71-110. `SharpProof.Contracts/ContractForSymbolMatcher.cs`, `DiscoverCompanions`, lines 109-131; `ResolveCompanion`, lines 134-180, especially 143-152. `SharpProof.Frontend/ReferencedTypeSymbols.cs`, `GetAll`, lines 5-25.
- Detailed mechanism: Final-compilation reconciliation obtains `candidates` solely by walking current compilation syntax trees. `ContractForValidationEngine.Validate` returns immediately at lines 11-14 when that source/generated-source candidate array is empty. Even when it is nonempty, duplicate diagnostics at lines 47-67 are emitted only for resolved current candidates. In contrast, `ContractForSymbolMatcher.DiscoverCompanions` sees the current assembly plus all referenced assemblies via `ReferencedTypeSymbols.GetAll`, but referenced descriptors are used by validation only to mark a current companion as overlapping. Therefore, two referenced assemblies can each contain an individually valid companion for the same third-party target while a consuming compilation with no companion declaration reports no `SPCF0002`. Downstream `ResolveCompanion` does see both descriptors and returns `AmbiguousCompanion` at lines 150-152 before selecting either contract.
- Impact: A collision that exists only in the final consumer reference graph silently disables/ambiguates the affected external contracts without the dedicated error-level duplicate-companion diagnostic or any source-independent explanation identifying the conflicting references. The analysis becomes incomplete precisely where the final-compilation reconciliation claims ownership of cross-generator/reference state.
- Safe reproduction/evidence: Create metadata references A and B, each declaring one static `[ContractFor(typeof(Common.ITarget))]` companion with an exact member. Analyze a third compilation referencing Common+A+B and containing no `ContractFor` declaration. `FindCandidates` returns empty, and `Validate` exits at lines 11-14. Direct `DiscoverCompanions` enumerates both referenced types (`ReferencedTypeSymbols` lines 16-23), and `ResolveCompanion` returns `AmbiguousCompanion` at lines 150-152. Adding an unrelated current companion candidate avoids only the early return; neither referenced descriptor can itself receive `SPCF0002`, because the reporting loop iterates only current `companions`.
- Duplicate distinction: Distinct from existing source/source and source/reference overlap tests and recorded BUGS findings. The live `BUGS.md` search found no cross-reference, cross-assembly, or reference-only duplicate-companion entry. This mechanism requires a collision entirely among referenced assemblies, which cannot exist when either referenced assembly is validated alone.

## Wave 18.46. MEDIUM - Repeated source-line-map authentication makes valid manifest validation multiplicative in diagnostics/authorities x source lines

- Files/members/current lines: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`: `CompilerManifestArtifactJson.Deserialize`, 340-350; `HasValidDiagnostics`, 636-642; `HasValidDiagnosticBinding`, 656-685. `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs`: `HasValidLineMap`, 20-28 and 31-50; `HasValidLocationGeometry`, 53-76; `IsBound`, 110-148; `TryMap`, 247-290. `SharpProof.CompilerArtifact/CompilationFingerprint.cs`: `ComputeLineMapSha256`, 16-28.
- Mechanism: Every source diagnostic calls `IsBound`; that calls `HasValidLocationGeometry`; and that calls `HasValidLineMap`. `HasValidLineMap` reserializes and SHA-256 hashes the entire line-map array every time, then `TryMap` linearly scans it again. The already-proved tree/line-map tuple is not memoized per snapshot. Generic location authorities and replay geometry repeat the same work too. Therefore D valid diagnostics on an L-line tree perform Theta(D*L) element scanning plus D complete JSON serializations/hashes of the same line map. Neither validation nor these helpers accept cancellation.
- Impact: An under-16-MiB, structurally valid manifest can consume enormous CPU/allocation before verification and before a structured timeout/cancellation outcome, exhausting launcher grace or killing in-process worker availability.
- Safe reproduction/evidence: Create one valid snapshot with a large line map and many canonically ordered source diagnostics all bound near its last line (duplicates are accepted because diagnostic canonicality uses `Compare <= 0`). Each diagnostic deterministically re-enters lines 20-28 and 247-290 over the same entries. A tens-of-thousands-line map plus thousands of small diagnostic rows is representable under the 16-MiB artifact ceiling but causes tens of millions of entry visits and repeatedly hashes multi-MiB JSON.
- Novelty/distinction: No live BUGS entry mentions repeated line-map hashing/scanning. Distinct from Wave 15.21 (missing timeout/cancellation boundary), Wave 14.35 (quadratic claim lookup during canonicalization), Wave 5.17 (collector recapture), and Wave 15.19 (`SourceLength` canonicality).

## Wave 18.47. MEDIUM - Duplicate legal preprocessor symbols make an empty syntax tree's producer artifact reject itself

- Files/members/current lines: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`, `CaptureTree`, 118-158, especially 151-155; `SharpProof.Frontend/CSharpPreprocessorSymbols.cs`, `GetDefined`, 8-40, especially 19-21; `SharpProof.CompilerArtifact/CompilationFingerprint.cs`, `ValidTree`, 268-289, especially 280-287; `SharpProof.CompilerArtifact/CompilerCaptureAuthority.cs`, `IsCanonicalEmptyTree`, 145-155; producer `CompilerManifestArtifactProducer.Create`, 79-92.
- Mechanism: Roslyn preserves duplicates in `CSharpParseOptions.PreprocessorSymbolNames`. Capture stores that sorted list without `Distinct` at line 151, but `GetDefined` converts it to an `ImmutableHashSet`, so `EffectivePreprocessorSymbols` contains one copy. Validation explicitly allows duplicates in raw symbols (`IsOrdered(... unique:false)`), but for a zero-length tree `IsCanonicalEmptyTree` requires the effective set sequence to equal the raw list. Thus the producer creates `['DUP','DUP']` versus `['DUP']` and rejects its own legal capture at line 92. On nonempty trees the early `TextLength != 0` branch instead admits the duplicate-bearing, semantically redundant fingerprint.
- Impact: A legal compilation containing an empty generated/ordinary syntax tree with duplicate define constants reports compiler-manifest failure and cannot verify; nonempty equivalents also have multiple accepted compilation identities for the same preprocessor semantics/cache behavior.
- Safe reproduction/evidence: Read-only Roslyn 4.14 reflection probe in the workspace: `CSharpParseOptions.Default.WithPreprocessorSymbols(['DUP','DUP'])`, then `CSharpSyntaxTree.ParseText('', opts, 'empty.cs')`, yielded `text_length=0 raw_count=2 effective_set_count=1 raw=DUP,DUP effective=DUP`. Those are exactly the two capture paths above; the empty-tree equality is false.
- Novelty/distinction: Live `BUGS.md` has no duplicate-preprocessor/symbol finding. Distinct from Wave 15.3 (duplicate file paths violate snapshot path uniqueness) and Wave 15.19 (line-map length canonicality).

## Wave 18.48. MEDIUM - Final-compilation probe hashes pathname bytes, not the PE image actually bound by Roslyn

- File/member/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateReferenceRow`, 343-382; path capture at 347 and `File.Exists(path) ? ProbeHash.File(path) : string.Empty` at 381-382. Related identity comes from `compilation.GetAssemblyOrModuleSymbol(reference)` at 392-401, i.e. bound metadata, while the hash independently reopens `FilePath`.
- Mechanism: `PortableExecutableReference` can retain cached/bound metadata after its backing path is atomically replaced; the probe then pairs the old compilation symbol identity with SHA-256 of replacement pathname bytes. A custom PE reference can likewise expose a `FilePath` unrelated to returned metadata.
- Impact: The purported final-compilation snapshot can attest bytes the compiler did not consume, so package/compiler-capture comparisons can falsely pass or diagnose the wrong input.
- Safe evidence: Create `CSharpCompilation` from `CreateFromFile(genuine)`, force symbol binding, atomically replace the file with a same-identity/different-body PE, then snapshot; `assemblyOrModuleIdentity` describes bound metadata while `fileSha256` hashes replacement.
- Duplicate distinction: BUGS Wave 5.16 is production Contract API trust; this is the independent final-compilation probe/oracle.

## Wave 18.49. MEDIUM - Probe silently omits legal CompilationReference inputs

- File/member/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateReferenceRows`, 333-340; `.OfType<PortableExecutableReference>()` at 336-337.
- Mechanism: `CSharpCompilation.References` may contain `CompilationReference` instances (created by another compilation's `ToMetadataReference`), but filtering to `PortableExecutableReference` drops them completely.
- Impact: Compiler compilations differing only by a source-compilation reference emit indistinguishable `portableReferences`; the probe cannot detect missing/wrong source-reference closure and may validate an incomplete collector view.
- Safe evidence: Snapshot C with and without `referencedCompilation.ToMetadataReference()`; semantic binding/reference set changes, but no row for that reference exists.
- Duplicate distinction: Production source-compilation ownership findings concern analysis/collector behavior; this is omission by the test oracle itself.

## Wave 18.50. MEDIUM - Null AdditionalText content is interpreted as an authentic empty file by both generator and snapshot

- Files/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeGenerator.cs`, `Initialize` projection, 19-29, especially 21-22; `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateAdditionalFileRows`, 404-437, especially 411-433.
- Mechanism: `AdditionalText.GetText` is nullable by contract. Both paths use `?.ToString() ?? string.Empty`, so an unreadable/unavailable input and a readable zero-length input generate identical source/fingerprint and identical `textSha256`.
- Impact: The health probe can report a deterministic successful compilation and validate the wrong `AdditionalFile` payload even though the compiler host could not supply it.
- Safe evidence: Custom `AdditionalText` with expected `FilePath` and `GetText` returning null versus one returning `SourceText.From("")`; both hash/generate as empty.
- Duplicate distinction: No live BUGS entry covered this probe conflation.

## Wave 18.51. LOW - `generatedKind` is a filename/comment heuristic that mislabels compiler input origin

- File/member/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateSyntaxTreeRow` 212-240 (classification 230-233); `IsGenerated` 447-455.
- Mechanism: Handwritten tree named `*.g.cs`/`*.generated.cs` or beginning `// <auto-generated` becomes `Generated`; generator-emitted tree with arbitrary hint name and no marker becomes `NotGenerated`. These conditions do not establish Roslyn tree origin.
- Impact: The final-compilation oracle can claim generated/handwritten provenance opposite to the actual pipeline, weakening detection of generator output and handwritten-source mutation.
- Safe evidence: `ParseText` handwritten code with path `Subject.g.cs` => `Generated`; `AddSource("Subject.cs", source without marker)` => `NotGenerated`.
- Duplicate distinction: Collector path/ownership bugs do not cover probe result interpretation.

## Wave 18.52. HIGH - SPMETA002's shallow mutability classifier permits readonly shared mutable holders

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members/current lines: `AnalyzeField` 345-363 (gate 359-360); `AnalyzeProperty` 366-378 (gate 369-371); `IsMutableStorageType` 401-430, especially early safe exits 408-414 and member scan 417-429.
- Mechanism: A readonly static reference is reported only if `IsMutableStorageType(declared type)` returns true. The classifier recognizes selected mutable collection interfaces and only directly declared, non-readonly fields/settable properties. It ignores mutation methods, readonly references to mutable children, inherited state, type-parameter constraints, and metadata-backed implementation state; it also trusts any type whose simple name begins `Immutable` or `ReadOnly`. Concrete safe reproduction in `SharpProof.Analyzer`: `sealed class Holder { readonly List<int> values=new(); internal void Add(int x)=>values.Add(x); } static class C { static readonly Holder State=new(); }`. `Holder` has no matching mutable interface, its sole field is readonly, and it has no settable property, so lines 424-429 return false; `AnalyzeField` emits no SPMETA002 although `State.Add` mutates cross-compilation shared storage. A user-defined `ImmutableHolder` with a mutable field bypasses earlier at 411-412.
- Impact: `SharpProofSoundnessAnalyzer.Initialize` calls `EnableConcurrentExecution` at line 60, so analyzer callbacks/compilations can race on or contaminate persistent process-wide state despite the error-level compilation/worker-scoping invariant.
- Duplicate distinction: Existing SPMETA002 coverage/log material concerns direct mutable properties/events and known `Dictionary`/`ConcurrentDictionary` storage. Existing SPMETA010 findings concern unstable-answer cache writes, not SPMETA002's transitive type classification.

## Wave 18.53. HIGH - SPMETA002 omits the meta-analyzer and source-generator namespaces entirely

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members/current lines: `IsForbiddenMutableStaticStorage` 395-399; `IsCriticalStateNamespace` 457-462.
- Related exact evidence: `SharpProof.ContractForGenerator/ContractForValidatorGenerator.cs` lines 1-6 (`SharpProof.ContractForGenerator`, `IIncrementalGenerator`); `SharpProof.ContractForGenerator/SharpProof.ContractForGenerator.csproj` lines 20-23 loads the meta-analyzer with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"` (the project is also marked `IsRoslynComponent`/`IsRoslynAnalyzer`).
- Mechanism: SPMETA002 applies only when a namespace is or nests exactly `SharpProof.Analyzer`, `SharpProof.Frontend`, or `SharpProof.Verify`. `SharpProof.Meta.Analyzers` and `SharpProof.ContractForGenerator` match none. Thus even an ordinary `internal static Dictionary<...> Cache = new();` in either Roslyn component returns false at line 398 and receives no diagnostic, independently of the mutable-type classifier.
- Impact: Production Roslyn components can introduce mutable process-wide caches/state with no enforcement, enabling concurrent compilation races and cross-compilation contamination.
- Safe evidence: Namespace matching at 464-491 is segment-exact; neither omitted namespace can match the three accepted prefixes.
- Duplicate distinction: Wave 8.9 concerns the exact `SharpProof.Meta.Analyzers` namespace exemption for SPMETA005 descriptor construction. This is the separate SPMETA002 mutable-state gate and additionally covers `SharpProof.ContractForGenerator`.

## Wave 18.54. MEDIUM - Unknown-reason ratchet does not ratchet down after precision improves

- Files: `SharpProof.Gates/Corpus/CorpusGate.cs`; `SharpProof.Gates/Corpus/unknown-reason-ratchet.json`.
- Members/current lines: `ValidateUnknownReasonRatchet`, lines 619-669, especially the loop over `actual` only at 651-668; `LoadUnknownReasonRatchet`, lines 671-720; configured `maximumByReason` buckets in JSON lines 6-13.
- Mechanism: Validation iterates only reasons present in the current observations and checks `item.Count > configuredMaximum`. It never iterates configured maxima to reject a bucket that disappeared, and it never requires a configured maximum to be lowered to the newly observed count. Thus an improvement from N Unknowns in a reason to zero (or any smaller count) passes while leaving the old high-water allowance intact. A later regression can restore that reason up to the stale maximum; after updating the per-case snapshot, the aggregate ratchet also passes. The `maximumTotalUnknown` has the same stale-ceiling behavior.
- Impact: The gate does not enforce the documented monotonic unsupported-surface ratchet. Precision improvements can be lost later without reducing supported-case counts or exceeding an Unknown ceiling, so snapshot rewriting can re-bless a previously eliminated Unknown category.
- Safe reproduction/evidence: With current `SP0016: 20`, make all 20 cases cease producing SP0016 and update the canonical snapshot, leaving `unknown-reason-ratchet.json` unchanged; `actual` contains no SP0016 row, so lines 651-668 perform no check and the gate passes. Later restore 1-20 SP0016 Unknowns and update the snapshot; the reason count is <=20 and total is <=309, so the gate passes again.
- Duplicate distinction: Not Wave 5.30 (snapshot byte/diagnostic ordering) and not Wave 14.20 (cross-variant semantic-classification divergence). This is specifically loss of aggregate high-water-mark monotonicity because absent/reduced reason buckets are not required to lower their configured ceilings.

## Wave 18.55. HIGH - VerifyTarget cancellation exemption accepts an arbitrary semantic fallback after guarding only caller cancellation

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Members/current lines: `IsAuditedCancellationBoundary` 317-377, especially OR at 371-376; `ThrowsIfCallerCancellationRequested` 622-642; `ReifiesCallerCancellation` 644-691.
- Mechanism: For the trusted `CallableVerificationPolicy.VerifyTargetAsync` shape, `IsAuditedCancellationBoundary` suppresses SPMETA003 when either predicate returns true. `ThrowsIfCallerCancellationRequested` returns true solely because the first catch statement invokes `callerCancellation.ThrowIfCancellationRequested()`; it never inspects any remaining statements or requires the non-caller cancellation path to return `Unknown`/`MethodTimeout`/`ProjectTimeout`. `ReifiesCallerCancellation` similarly proves only the `if(callerCancellation.IsCancellationRequested) return Unknown(...Canceled...)` branch and never validates the fallthrough. With a live caller token, the guard is a no-op, so the caught internal/backend/method-boundary `OperationCanceledException` can be converted into any `CallableVerificationResult`, including a success/proven result, while SPMETA003 is suppressed. This directly contradicts SPMETA003's descriptor that cancellation may not become a semantic answer.
- Impact: A refactor can turn timeout/backend cancellation into unsound positive evidence without the build-error boundary detecting it.
- Safe evidence/reproduction: `SharpProof.Meta.Analyzers.Test/SharpProofSoundnessAnalyzerTests.cs` lines 912-954 already contains two catches: bad Worker Main at 923-924 and `VerifyTargetAsync` at 940-944 with `callerCancellation.ThrowIfCancellationRequested(); return new CallableVerificationResult();`. The assertion expects exactly one SPMETA003, proving the VerifyTarget catch is accepted. A standalone correctly typed fixture with that catch yields zero SPMETA003.
- Distinction: Wave 2.10 is the current production policy's unsignaled-OCE misclassification; Wave 12.16 requires token parameter reassignment; Wave 13.16 concerns a no-op Respond helper at Worker Program. This bypass needs no reassignment or helper spoof and is specifically the unchecked non-caller fallthrough of the VerifyTarget exemption.

## Wave 18.57. MEDIUM - Supported-domain abstentions fail the run but emit no per-case failure evidence

- Files/members/lines: `Tools/SharpProof.Fuzz/FuzzRunner.cs`: `FuzzSummary.Passed`, lines 93-110; `RunAsync`, classification/counting lines 239-251 and evidence selection lines 254-258; `SelectFailureKeys`, lines 359-395; `ClassifyCase`, lines 398-411. Confirming test: `SharpProof.Fuzz.Test/FuzzRunnerTests.cs`, `PartialAbstentionIsNotClassifiedAsMismatchEvidence`, lines 44-61.
- Mechanism: `ClassifyCase` recognizes any oracle `Abstained` and `RunAsync` increments `abstentions`; `Passed` rejects every nonzero abstention. But `SelectFailureKeys` adds only statuses equal to `Mismatch`, never `Abstained`. Therefore an otherwise-agreeing case with one supported-domain abstention makes the campaign fail while `Failures` remains empty. The per-oracle result/detail and case seed are discarded after the parallel loop.
- Impact: The fuzz output exposes only an aggregate abstention count, not which case/oracle abstained, the generated term/source, or the oracle reason. A campaign can fail with no actionable/reproducible failure record.
- Safe evidence: Targeted canonical-container test `docker compose run --rm tooling test -Target SharpProof.Fuzz.Test -TestFilter 'FullyQualifiedName~PartialAbstentionIsNotClassifiedAsMismatchEvidence'` passed (1/1). That test explicitly proves `HasAbstention == true` and `SelectFailureKeys(...)` is empty. Constructing a corresponding `FuzzSummary` yields `Passed == false` with `Failures == []`.
- Novelty/distinction: Not Wave 2.24 (campaign.json withheld on failed runs); even if failed campaign publication is fixed, runner schema still contains no abstention reproducer. Not Wave 14.50 partial-case repetition.

## Wave 18.58. MEDIUM - Concurrent fuzz campaigns destructively share one unleased evidence namespace

- Files/members/lines: `scripts/Invoke-SharpProofFuzzCampaign.ps1`: initialization line 28; date-default seed lines 45-49; fixed per-seed stdout/stderr paths lines 86-87; `Start-Process` redirection lines 123-130; run scheduling lines 182-191; fixed `campaign.json` finalization lines 212-220. `scripts/SharpProof.FuzzEvidenceLifecycle.ps1`: `Initialize-SharpProofFuzzEvidence`, lines 141-170; `Publish-SharpProofFuzzEvidence`, lines 173-196, especially fixed `.campaign.json.tmp`/`campaign.json` lines 183-190.
- Mechanism: There is no output-directory lease/lock or run-specific staging directory. Every invocation first deletes all canonical campaign/run files. Invocations sharing an output directory (and normally the same `yyyyMMdd` rotating seed on the same day) redirect processes to identical filenames and publish through the same fixed temp/destination names. One run can delete/truncate another's active output or race its temp-file move.
- Impact: Overlapping local/manual/scheduled campaigns can fail spuriously, validate mixed bytes, overwrite each other's campaign summary, or upload evidence attributed to the wrong run.
- Safe reproduction/evidence: In a disposable checkout/output directory, start two campaign invocations with the same explicit rotating seed and pause the first runner; the second initialization deletes the first namespace, then both target `rotating-<seed>.stdout.json`, `rotating-<seed>.stderr.txt`, `.campaign.json.tmp`, and `campaign.json`. Source path equality plus absence of any lock is direct evidence.
- Novelty/distinction: Wave 2.24 concerns a single failed campaign deleting/withholding its own summary. This is cross-invocation ownership and collision, and remains after changing failure finalization.

## Wave 18.59. MEDIUM - Campaign hash-validates a snapshot but leaves the named stdout artifact replaceable afterward

- Files/members/lines: `scripts/Assert-SharpProofFuzzRunnerResult.ps1`, `Assert-SharpProofFuzzRunnerResult`: snapshot stream/read/close lines 66-93; post-validation hook and detached parse/hash lines 199-207. `scripts/Invoke-SharpProofFuzzCampaign.ps1`, `Invoke-FuzzRun`: validation lines 145-155; independently records `resultSha256` and `standardOutput` path lines 160-178. `scripts/Test-SharpProofFuzzRunnerResult.ps1`, canonical race fixture lines 79-95.
- Mechanism: The decoder correctly binds returned fields/hash to bytes read from its temporary open handle, but closes the handle and returns only data/hash. Campaign then records that hash beside the original mutable pathname; it never atomically stages the validated bytes, reopens/re-hashes at publication, or gives the summary ownership of an immutable file. Replacement after validation therefore leaves `campaign.json` describing old bytes while `standardOutput` names new bytes. The existing race fixture deliberately replaces the path in `AfterValidation` and proves the returned Seed/hash stay bound to the old snapshot, demonstrating this exact path/snapshot divergence.
- Impact: Uploaded evidence can contain a valid campaign summary and a different raw runner result at its cited path. There is no campaign consumer found that re-hashes the referenced artifact, so review/reproduction can read unvalidated or cross-run bytes.
- Safe reproduction/evidence: Use the existing `AfterValidation` seam as lines 79-95 do, then compare returned `ResultSha256` with a hash of the now-current path: they differ while the decoder succeeds. In the live campaign, another process or overlapping run supplies the replacement.
- Novelty/distinction: Distinct from finding 2 (even a single campaign plus an external path replacement is affected) and from Wave 2.24 (successful campaigns are affected). It also differs from general Z3/worker execute-after-hash TOCTOU entries: this is fuzz evidence path integrity, not executed payload.

## Wave 18.60. MEDIUM - Release-authority closure silently drops imported PowerShell modules

- Files/members: `scripts/Get-SharpProofReleaseAuthorityClosure.ps1`, `Get-SharpProofReleaseAuthorityClosure`, lines 45-67, especially reference regexes at 59-65. Representative direct imports: `scripts/Invoke-SharpProofContainer.ps1` line 26; `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1` lines 21-25; `scripts/Test-SharpProofTrustedMutations.ps1` lines 31-33. Incomplete oracle fixture: `scripts/Test-SharpProofReleaseAuthorityClosureFixtures.ps1`, lines 54-80.
- Mechanism: The closure walker accepts a path ending in `.psm1` for AST parsing at lines 45-46, but neither literal-reference pattern recognizes module references. The rooted pattern at line 59 permits only `ps1|json|yaml|yml|props|targets|nuspec` (plus the separate `.cs` case), and the sibling-leaf pattern at line 63 permits only `.ps1`. Thus a normal `Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')` literal is discarded. `SharpProof.PublicationPlanIdentity.psm1` enters only because it is manually seeded as a root at line 22, not because imports are followed.
- Current exact evidence: A live read-only invocation returned 110 closure paths, but `$closure -ccontains ...` was false for all four tracked and directly imported modules: `scripts/SharpProof.ContainerExecution.psm1`, `scripts/SharpProof.MutationBaselines.psm1`, `scripts/SharpProof.MutationEvidence.psm1`, and `scripts/SharpProof.MutationScheduling.psm1`. `git ls-files --error-unmatch` succeeded for each. The importing `.ps1` scripts themselves are in the derived closure. The fixture manually creates `PublicationPlanIdentity.psm1` as an explicit required root and creates only `.ps1` dependency leaves, so it cannot expose the missing `.psm1` traversal.
- Impact: `releaseAuthorityClosure` and its equality/mutation oracle can claim a complete transitive release-script closure while omitting executable modules that implement container execution and mutation-evidence gates. The four current modules happen to be listed in other TCB components in `eng/acceptance/contract.json`, which limits current whole-TCB exposure, but their changes are not attributed to or mutation-tested through the release-authority closure, and a future imported module can disappear from authority entirely.
- Safe reproduction/evidence: Dot-source `Get-SharpProofReleaseAuthorityClosure.ps1`, derive the closure, and evaluate `-ccontains` for the four modules; all are false while Git confirms each is tracked. No mutation is needed.
- Duplicate distinction: Live `BUGS.md` contains no authority-closure/module-dependency finding. Its lone `.psm1` reference belongs to an unrelated mutation-receipt issue.

## Wave 18.61. MEDIUM - Release-configuration exact-set oracle inspects only the first REST page

- Files/members: `scripts/Test-SharpProofReleaseConfiguration.ps1`, `Invoke-GitHubJson`, lines 24-31. Repository-ruleset lookup, lines 175-182. Deployment policy, environment-variable, and environment-secret collection checks, lines 246-282. `scripts/Test-SharpProofReleaseConfigurationFixtures.ps1`, mock GitHub API/state setup (single complete response objects; no pagination fixture).
- Mechanism: Every collection request is issued as `gh api $Endpoint` at line 27. There is no `--paginate`, `per_page`, page loop, Link-header processing, or comparison of returned row count against `total_count`. GitHub CLI documents that subsequent pages are fetched only with `--paginate`; the GitHub REST list-deployment-branch-policies endpoint is paginated with a default of 30 rows, as are the other list endpoints used here. `Require-ExactSet` therefore establishes equality only over whatever appears on page one.
- Impact: Once a live collection exceeds its first page, an unauthorized additional active tag ruleset, deployment-ref policy, environment variable, or environment secret on a later page is invisible. If all contract-required rows remain on page one, the release-configuration gate can emit passing evidence although live configuration is a strict superset of the contract. Conversely, a required row pushed to a later page creates a false failure.
- Safe reproduction/evidence: Use the existing fixture-style mock `gh` to return a page-one object containing exactly the expected `branch_policies`, `variables`, or `secrets`, but set `total_count` greater than the number returned to represent an unauthorized page-two row. The script ignores `total_count` and passes. Supporting primary documentation: `https://cli.github.com/manual/gh_api` (`--paginate` makes additional requests) and `https://docs.github.com/en/rest/deployments/branch-policies` (default `per_page=30`, `page=1`).
- Duplicate distinction: Live `BUGS.md` has no pagination, first-page, `per_page`, or release-configuration collection finding.

## Wave 18.62. MEDIUM - Release-bundle replacement has a crash window with no destination and no recovery

- Files/members: `scripts/SharpProof.ReleaseChecksums.ps1`, `Publish-SharpProofReleaseBundleAtomically`, lines 190-235: destination-to-backup move at 216-219, staging-to-destination move at 220-224, rollback only in catch at 226-233. Caller `scripts/New-SharpProofReleaseEvidence.ps1`, final/source directory selection and staging at lines 543-570, invocation/final output at 965-975. Incomplete fixture `scripts/Test-SharpProofReleaseChecksumFixtures.ps1`, ordinary success/caught-failure cases around lines 88-120.
- Mechanism: Despite its name, bundle replacement is two separate directory renames. It first moves the live destination to a GUID-named hidden `.backup`, making the canonical destination absent, then moves staging to the destination. The only rollback runs from the current process's `catch`. `SIGKILL`, container/host termination, or power loss after line 219 but before line 221 leaves only the hidden backup, and there is no startup scan or recovery for `.backup` directories. If interruption happens after staging is moved but before backup deletion, a complete stale backup is stranded indefinitely. The fixture exercises synchronous success and an exception that reaches `catch`; it never exercises process loss between moves.
- Impact: Interruption during release-evidence generation can make the only canonical package directory disappear and wedge retries, or leave a stale package bundle outside normal topology validation. When `OutputDirectory` is omitted, `finalOutput` is `PackageSource`; after the first crash window the next invocation fails its initial `Resolve-Path` because that source path no longer exists. This violates the advertised atomic replacement at the release boundary.
- Safe reproduction/evidence: Static control flow is exact. In an isolated disposable fixture, terminate the process immediately after destination-to-backup rename and observe that the destination is absent and the hidden backup remains; rerunning with the old `PackageSource` then fails during source resolution.
- Duplicate distinction: This differs from Wave 8.15 (single-file replacement discards metadata), Wave 13.6 (verifier publication deletion durability), and Wave 2.19 (multi-file corpus updates). This finding concerns the release-bundle directory swap and its unrecoverable inter-rename crash window.

# Read-Only Multi-Agent Bug Audit - Wave 19 - 2026-08-29

## Wave 19.1. MEDIUM - Nested property references inside `nameof` are treated as executed accessor calls

- Exact file: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`.
- Members/current lines: `GetPotentialCallOwners`, lines 44-76; `Get`, lines 147-225; `GetPropertyCalls`, lines 1516-1566, especially the immediate-parent-only exclusion at 1562-1564.
- Mechanism: `GetPropertyCalls` suppresses a property getter only when `property.Parent is INameOfOperation`. That is sufficient for the outermost property named by `nameof`, but not for an inner property used as the receiver of another named member. In `nameof(C.Value.Length)`, the outer `Length` property has the `INameOfOperation` parent and is suppressed, while the inner `C.Value` property has the outer property reference as its immediate parent and therefore reaches `CreateGetterCall`. Both potential-owner discovery and candidate discovery recursively enumerate the full operation subtree, so the inner getter is classified as a call even though the entire `nameof` operand is compile-time-only and executes no accessors. For a static inner getter, the later receiver-null guard cannot turn the candidate Unknown; a replayable constant-false Requires is evaluated and refuted.
- Concrete impact: Valid code can receive a false SP0027 / Refuted Requires outcome at a property expression that never runs. This can fail warning-as-error builds and makes analyzer results depend on whether the property is the outermost name or an intermediate receiver in an otherwise equivalent `nameof` expression.
- Safe reproduction/evidence: Analyze this valid shape with the normal analyzer: `sealed class Leaf { public int Length => 0; } static class C { public static Leaf Value { get { Contract.Requires(false); return new Leaf(); } } public static string M() => nameof(Value.Length); }`. Runtime/CLR semantics evaluate `M` to the constant string `"Length"`; `C.Value.get` is never invoked. In the operation tree, `Length` is the direct child of `nameof` and is suppressed at lines 1562-1564, but `Value` is a child of the `Length` property reference and is returned by `GetPropertyCalls`; lines 179-225 then add its getter candidate. No repository mutation is needed to establish the source-path mismatch. A chain ending in a field, e.g. `nameof(Value.Field)`, demonstrates the same inner-getter problem.
- Closest `BUGS.md` distinction: Wave 5.25 is the only existing `nameof` Requires entry. It concerns `RequiresCallSiteTreeAnalyzer.TryCollectLocalReferences` treating method references in `nameof` as local-function reachability/escape edges. This finding is in the separate `RequiresCallSiteDiscovery.GetPropertyCalls` path, needs no method reference or nested callable, and directly invents an ordinary property-get call because the exclusion checks only the immediate parent. Wave 3.1 is the opposite class (executed non-invocation shapes omitted, causing false negatives), not compile-time-only accessor calls invented as executed.

## Wave 19.2. HIGH - Delegate invocation inside a simple-assignment LHS is mistaken for overwriting the delegate, so a definitely executed local function is classified dead

- Exact files/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`: `TreeAnalysis.GetNestedCallables`, lines 297-367 (reachable-local filter at 311-318); `TreeAnalysis.CanReachConsumption`, lines 567-783, especially local-reference classification/kill at 589-629; `HasEnclosingSimpleAssignment`, lines 1019-1032; `IsAssignmentTarget`, lines 1267-1276; `AssignmentKillsTrackedValue`, lines 1278-1296.
- Detailed mechanism: `IsAssignmentTarget` returns true for every local reference syntactically anywhere under the left side of a simple assignment; it does not require that the assignment target itself be that local. Thus in `values[callback()] = 0`, the read/invocation of `callback` used to evaluate the array index is classified as target-shaped merely because it is inside `assignment.Left`. `HasEnclosingSimpleAssignment` then succeeds, and with an unprojected delegate (`tuplePath == null`) `AssignmentKillsTrackedValue` unconditionally returns true. `CanReachConsumption` sets `killed` and breaks before reaching the normal-consumption return at line 753. With no handler, the exceptional enqueue contributes nothing, and the normal successor is deliberately suppressed for a killed value. `GetNestedCallables` therefore excludes the referenced local function and records it visited without analyzing its CFG.
- Concrete impact: A definitely invoked local function/lambda body can be skipped entirely, so reachable violated call preconditions in that body emit no SP0027 and the nested callable gets no real semantic analysis. The same defect applies to delegate reads/invocations in indexer arguments, property receivers, or other evaluation subexpressions of a simple-assignment LHS.
- Safe reproduction/evidence: Analyze a method shaped as follows: `static int Outer(){ Func<int> callback=Dead; var values=new int[1]; values[callback()]=0; return 0; int Dead(){ _=Positive(-1); return 0; } }`, where `Positive(int x)` has `Contract.Requires(x > 0)`. C# evaluates `callback()` before performing the array store, so `Dead` and `Positive(-1)` definitely execute. Direct source trace follows `IsAssignmentTarget=true` -> `HasEnclosingSimpleAssignment=true` -> `AssignmentKillsTrackedValue=true` -> `killed`/no regular successor, producing a false dead-local result.
- Novelty/distinction from current `BUGS.md`: Wave 18.10 concerns a real delegate overwrite whose RHS user-defined operator/conversion can throw before commit, requiring preservation of the old value on an exceptional path. This finding has no overwrite of the tracked delegate at all: an executing use embedded in another target's location evaluation is itself misidentified as the commit. Wave 3.1 concerns discovery of the contract on a method invoked through a delegate; here the delegate target has no own precondition and its entire reachable nested body (which contains a separate bad call) is skipped by local-function reachability.

## Wave 19.3. MEDIUM - Structurally reachable but semantically impossible catch bodies can emit false SP0027 for accessor preconditions

- Exact files/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`: `Get`, lines 90-278, especially block/candidate admission at 132-177 and accessor replay choice at 196-205; `IsAccessorCall`, lines 474-480; `HasReplayableAccessorEvaluation`, lines 483-492; `IsInsideExceptionHandler`, lines 1639-1646. Downstream diagnostic path: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, `AnalyzeCallSite`, lines 264-327, especially replay gate at 313-326, and `CompleteEvaluation`, lines 495-519.
- Detailed mechanism: Discovery scans every Roslyn `BasicBlock.IsReachable`; Roslyn's flag is structural and can remain true for a catch whose declared type cannot catch the only thrown runtime type. Managed abstract flow has no state for such an exceptional handler, but the complete-flow pruning condition explicitly exempts every operation lexically inside any catch/filter/finally (`!IsInsideExceptionHandler` at line 173). For an accessor, candidate replay then takes `HasReplayableAccessorEvaluation` rather than requiring `hasFlowState`/a reachable replayable prefix. A static zero-argument getter with a constant-false `Requires` therefore concrete-replays successfully, and `CompleteEvaluation` reports SP0027, even though the handler and getter call cannot execute.
- Concrete impact: Valid code receives a false call-precondition diagnostic and a `Refuted` semantic outcome solely from an impossible exception handler. This can fail strict builds and misstate analyzer evidence.
- Safe reproduction/evidence: Use unrelated sealed exceptions `A` and `B`, and `static int Outer(A ex){ try { throw ex; } catch(B){ return Guard.Bad; } catch(A){ return 0; } }`, where static getter `Guard.Bad` has `Contract.Requires(false)` and no arguments/receiver state. `throw ex` can produce only `A` (or `NullReferenceException` if null), never `B`; the first catch is impossible. Roslyn nevertheless treats impossible catches structurally reachable (also documented by current BUGS Wave 15.10). The getter has no inputs, passes `HasReplayableAccessorEvaluation`, concrete evaluation returns false, and lines 508-516 necessarily emit SP0027.
- Novelty/distinction from current `BUGS.md`: Wave 18.11 is the separate delegate/local-function tracker: `ExceptionalSuccessors` manually enqueues every sibling handler, making a callback body appear reachable. This finding requires no delegate or nested callable and arises in `RequiresCallSiteDiscovery.Get` from the unconditional lexical-handler exemption plus accessor-specific replay, causing a direct false SP0027. Wave 15.10 uses Roslyn's structural catch reachability only to fabricate an Effects `MayDiverge` result from a handler cycle; it does not cover call-precondition diagnostics or accessor replay.

## Wave 19.4. MEDIUM - Post-backend proof hygiene ignores late cancellation and can return Proven after cancellation

- Exact file/member/current lines: `SharpProof.Verify/ProofKernel.cs`, `ProofKernel.VerifyAsync`, lines 8-28 (only post-backend token check at line 15 and UNSAT dispatch at line 23); `CreateProven`, lines 30-46 (full core validation/materialization with no token); `ReplayCounterexample`, lines 47-92, especially uncancelable `ValidateAssignments` call at lines 59-62; `ValidateAssignments`, lines 93-122 (two full model-closure/type passes without a token).
- Mechanism: After `_backend.CheckAsync` completes, `VerifyAsync` checks the token once at line 15. The UNSAT branch then scans the entire core at lines 32-42 and performs `Distinct`/projection/materialization at lines 44-45, but `CreateProven` neither accepts nor checks the token. Cancellation arriving during that work is never observed, so the method can return a cacheable `ProvenOutcome` while its caller token is canceled. The SAT branch passes the token to replay, but exact assignment closure runs first through `ValidateAssignments`; its `Count`/`Any(ContainsKey)`/`All(GetVariableInfo)` passes cannot observe cancellation. SAT eventually reaches a token check before assumption/goal replay, but only after the potentially large closure scan.
- Concrete impact: Direct `ProofKernel` callers can receive semantic proof success after cancellation, contrary to the documented invariant that caller cancellation remains cancellation rather than a semantic outcome (`SEMANTICS.md` lines 36-38). In all callers, large cores/models can also overrun cancellation latency and consume CPU after the boundary is canceled. Current worker call sites generally perform another check after awaiting the kernel, reducing publication risk there, but that does not repair the public kernel contract or the wasted work.
- Safe reproduction/evidence: Construct a valid query with a very large assumption set and a stub backend returning `BackendCheckResult.Unsatisfiable(Enumerable.Range(0, query.Assumptions.Length))`. Start `VerifyAsync` with a live CTS and cancel from another thread after the backend has returned while core materialization is running. The task can complete with `ProvenOutcome` rather than throwing `OperationCanceledException`. A corresponding SAT fixture with a large exact bool/int assignment dictionary demonstrates delayed cancellation until `ValidateAssignments` finishes. Static evidence is exact: line 15 is the last token observation on the UNSAT path, and neither `CreateProven` nor `ValidateAssignments` has a token parameter.
- Novelty/distinction from live `BUGS.md`: Wave 18.35 covers cancellation/resource omission while `IrSmtBackend` extracts and decodes the native Z3 UNSAT core before producing `BackendCheckResult`; this finding begins after the backend has returned and concerns the proof kernel's separate core-hygiene/materialization and exact model-closure passes, including a late-canceled `Proven` return. Wave 2.6 covers SMT-side model-variable construction/SAT decode, not kernel validation. Wave 15.17 covers non-refuted effect-result assembly in `SharpProof.Worker`, not proof-kernel outcome construction. Wave 18.36 covers ordinary backend exceptions escaping at the await, not cancellation after a successful backend result.

## Wave 19.5. HIGH - Implicit `foreach` element conversions are absent from exception reachability

- Exact file: `SharpProof.Effects/ExceptionHandlerReachability.cs`.
- Members/current lines: `GetPotentialExceptions`, `foreach` arm lines 672-690; `GetForEachExceptions`, lines 2270-2397, especially `GetForEachStatementInfo` at 2291-2294 and protocol-only handling at 2295-2395.
- Detailed mechanism: The `foreach` arm delegates implicit-loop exception discovery to `GetForEachExceptions`, then separately pushes `Collection`, `LoopControlVariable`, `Body`, and `NextVariables`. `GetForEachExceptions` reads `GetEnumeratorMethod`, `MoveNextMethod`, `CurrentProperty`, and `DisposeMethod`, but never reads `ForEachStatementInfo.ElementConversion` or `CurrentConversion`. A user-defined conversion applied when assigning `Current` to the iteration variable is executable and can throw, diverge, write, allocate, or require static initialization. Roslyn does not expose that implicit iteration-variable conversion as a child operation: in a Roslyn 4.14 in-memory probe for `foreach (int x in IEnumerable<Item>)` with `Item.op_Implicit`, `ElementConversion` was user-defined with `MethodSymbol=op_Implicit`, while `LoopControlVariable` was a zero-child `VariableDeclarator` and the loop `DescendantsAndSelf` contained no conversion for `Item` -> `int`. Therefore lines 681-688 cannot recover it. With source, nonvirtual, normally completing `GetEnumerator`/`MoveNext`/`Current` methods, `GetForEachExceptions` can return `EmptyPotential` even though the conversion necessarily throws; `reachesBody` is also derived from `Current` alone and can remain true after a terminal conversion.
- Concrete impact: A matching catch can be classified unreachable by `GetReachability`, so handler writes, allocations, calls, capabilities, and throws are omitted from an otherwise complete effect summary. The loop body may simultaneously be treated as reachable even when the conversion always terminates before entry. This can admit unsound allowed-effect/allowed-exception claims.
- Safe reproduction/evidence: Compile and analyze: `sealed class Item { public static implicit operator int(Item _) => throw new ApplicationException(); } struct Enumerator { int i; public bool MoveNext() => i++ == 0; public Item Current => new(); } struct Values { public Enumerator GetEnumerator() => new(); } static object? State; static void M() { try { foreach (int x in new Values()) { } } catch (ApplicationException) { State = new object(); } }`. Runtime enters the catch on the first iteration. Static source trace: `GetEnumerator`, `MoveNext`, and `Current` can all contribute `EmptyPotential`; `ElementConversion.op_Implicit` is never queried, and `LoopControlVariable` has no conversion child, so `ApplicationException` never reaches the catch gate.
- Novelty/closest `BUGS.md` distinction: Wave 3.1 mentions implicit `foreach` protocol calls only in Requires call-site discovery, not effect exception reachability or the iteration conversion. Wave 11.6 is the analogous omission of `ICompoundAssignmentOperation.InConversion`/`OutConversion` metadata, but not `ForEachStatementInfo.ElementConversion`/`CurrentConversion`. Wave 11.7 covers positional-pattern `Deconstruct`, and Wave 12.28 covers implicit formatting calls. No live `BUGS.md` entry covers `foreach` iteration conversions or this handler/body reachability consequence.

## Wave 19.6. HIGH - Nullable reference pattern mismatch paths are discarded when a member accessor is noncompleting

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`: `DefiniteOperationFacts.MethodCanCompleteNormally`, lines 1918-1942; `MayCompleteNormally`, `IIsPatternOperation` / list / recursive dispatch, lines 1991-2000; `MayCompleteRecursivePattern`, lines 2098-2108; `MayCompleteListPattern`, lines 2110-2162. Downstream: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation`, lines 588-609; `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep`, lines 609-668, especially 665-667; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, CFG successor suppression in `AnalyzeControlFlowGraph`, lines 351-373.
- Detailed mechanism: `MayCompleteNormally(IIsPatternOperation)` requires both the governing value and the pattern to complete. For recursive/property or positional patterns, `MayCompleteRecursivePattern` unconditionally asks whether `Deconstruct` and property subpatterns can complete, with no null-mismatch alternative. For list patterns, `MayCompleteListPattern` tries to preserve the null mismatch only when the governing expression's source text contains the substring `"null"`; an ordinary nullable parameter/local such as `value` does not satisfy that textual heuristic. At runtime, any null reference governing value makes a recursive or list pattern evaluate normally to false without invoking `Deconstruct`, `Length`, the indexer, or a nested getter. If such a source member is known nonreturning, the helpers nevertheless return false for the whole pattern and then for the containing source method.
- Concrete impact: A source helper that definitely returns on a null argument is classified nonreturning. Calls to it are marked terminal, so CFG successor propagation stops and real suffix writes, allocations, calls, and exceptions in callers are omitted. A complete purity/no-write/no-allocation result can therefore be accepted despite runtime effects.
- Safe reproduction/evidence: `sealed class Bomb { public int P { get { while (true) { } } } public int Length { get { while (true) { } } } public int this[int i] => 0; } static bool Property(Bomb? value) => value is { P: 0 }; static bool List(Bomb? value) => value is []; static int state; static void Caller1() { _ = Property(null); state++; } static void Caller2() { _ = List(null); state++; }`. Both pattern tests return false immediately at runtime and both callers increment `state`. Static source trace is exact: `MayCompleteRecursivePattern` reaches the diverging `P` getter and returns false; `MayCompleteListPattern` does not take lines 2113-2120 because syntax `value` lacks `null`, then reaches the diverging `Length` getter and returns false. `CanCompleteInvocation` propagates that false and the builder does not enqueue regular successors after the call.
- Closest `BUGS.md` distinction: Wave 3.14 is scanner ordering/path insensitivity that *adds impossible subpattern effects* before null/Length gates. This defect is the separate interprocedural completion predicate and has the opposite unsound consequence: it discards a real null mismatch path and then omits caller suffix effects. Wave 18.30 loses terminality for array initializers/interpolations and retains impossible suffixes; it does not concern patterns. Waves 18.27-18.29 poison completion for virtual dispatch, unreachable lexical statements, and async/iterators respectively, not reference-pattern null mismatch.

## Wave 19.7. HIGH - Reduced extension invocations on null receivers are falsely classified noncompleting

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`: `DefiniteOperationFacts.InvocationMayCompleteNormally`, lines 2248-2267, especially the null-instance check at 2257-2261; reached through `MethodCanCompleteNormally`, lines 1918-1942. Contrasting correct direct-call logic: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation`, lines 588-609, especially the `method.ReducedFrom == null` guard at 594-598; `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep`, lines 609-668, especially its same reduced-extension guard at 629-633. Downstream omission: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`, lines 351-373.
- Detailed mechanism: Roslyn represents a reduced extension invocation with an instance-shaped receiver and a reduced target symbol (`ReducedFrom != null`, `IsStatic == false`), but runtime dispatch is the original static extension method and null is a valid ordinary first argument. `InvocationMayCompleteNormally` checks only `!invocation.TargetMethod.IsStatic && IsDefinitelyNull(invocation.Instance)` and returns false; it omits the `ReducedFrom == null` exemption already present in both the direct completion evaluator and scanner. Consequently a wrapper method containing a reduced extension call on a definite-null receiver is globally classified nonreturning even when the extension body returns.
- Concrete impact: Any caller of the wrapper loses reachable post-call effects, allowing complete no-write/purity/no-allocation summaries to omit actual runtime behavior.
- Safe reproduction/evidence: `sealed class C { } static class Extensions { public static void Accept(this C? value) { } public static void Wrapper() => ((C?)null).Accept(); private static int state; public static void Caller() { Wrapper(); state++; } }`. Runtime calls the static `Accept(null)`, returns, and increments `state`. The reduced invocation has `ReducedFrom` pointing to `Accept`; the repository's direct-call checks at `OperationCompletionEvaluator.cs:596` and `OperationEffectScanner.cs:629` explicitly use that fact to avoid a null receiver dereference. The recursive helper at `ManagedAbstractFlow.cs:2257` lacks the same condition, returns false for `Wrapper`, and causes `Caller`'s successor block to be suppressed.
- Closest `BUGS.md` distinction: Wave 18.27 is virtual source dispatch inheriting a base body's noncompletion; this defect is nonvirtual static extension-call lowering and the missing reduced-extension exemption. Wave 7.16 concerns receiver null-check ordering before argument evaluation, not treating a null extension receiver as a dereference. Wave 18.29 concerns async/iterator call-expression boundaries. The live `BUGS.md` contains no `ReducedFrom`, reduced-extension, or null-extension completion entry.

## Wave 19.8. HIGH - `foreach` protocol calls are omitted from complete effect summaries

- Exact file/member/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `OperationEffectScanner.ScanCoreOperationTail`, lines 509-546, especially the blanket `ILoopOperation` arm at 538-539; supporting scanner traversal in `SharpProof.Effects/OperationEffectScanner.cs`, `ScanChildren`/`ScanSequence`, lines 942-963.
- Mechanism: Every `IForEachLoopOperation` is handled as an ordinary loop by scanning only `ChildOperations` and joining `MayDiverge`. Roslyn's `foreach` operation children expose the collection, control variable/body/next variables, but the executable pattern calls (`GetEnumerator`, repeated `MoveNext`, `Current`, and enumerator `Dispose`) are semantic-model information, not invocation children. The scanner has no `foreach`-specific semantic lookup and records none of those calls or their summaries, yet it does not add `Unsupported` because the explicit `ILoopOperation` arm bypasses `ScanDefault` classification.
- Concrete impact: A method can remain `Complete` while omitting writes, reads, allocations, capabilities, and escaping exceptions performed by any `foreach` protocol member. It can therefore be falsely certified nonwriting, nonallocating, or `DoesNotThrow`, and its call graph lacks the executed source callees.
- Safe reproduction/evidence: Define `sealed class Seq { public E GetEnumerator() { State.Value++; return new E(); } }` and `struct E { public bool MoveNext()=>false; public int Current=>0; }`, then analyze `static void M(Seq s) { foreach (var x in s) { } }`. The only scanner path for the loop is lines 538-539; no protocol symbol is resolved, so `State.Value`'s write and `GetEnumerator` call are absent despite runtime executing them. A variant with throwing/writing `MoveNext`, `Current`, or `Dispose` demonstrates the same omission.
- Closest `BUGS.md` distinction: Wave 3.1 mentions omitted `foreach` protocol calls only in `RequiresCallSiteDiscovery`/SP0027 applicability, not effect discovery or effect summaries. Wave 15.22 suppresses an actual implicit collection-initializer `Add` invocation under `lock`; this finding is the scanner's total absence of `foreach`'s non-child implicit protocol calls, with or without `lock`.

## Wave 19.9. HIGH - Built-in delegate combination/removal omits possible managed allocation

- Exact file/member/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `OperationEffectScanner.ScanBinary`, lines 338-368, especially effect assembly at 352-365.
- Mechanism: Built-in delegate `+`/`-` is an `IBinaryOperation` with no user `OperatorMethod`. `ScanBinary` scans operands, asks the string-only concatenation resolver, integral-division/overflow helpers, and `ResolveOperatorEffects`; all are empty for a delegate binary operation. There is no delegate branch adding allocation. With two nonnull delegates, combination creates a multicast delegate; subtraction can also create a new delegate invocation list. Since summaries are may-effects over inputs, unknown delegate parameters require a possible managed-allocation effect.
- Concrete impact: `ZeroAllocations`/nonallocating effect claims can be falsely established for methods that allocate a combined or reduced delegate, and callers inherit the false complete summary.
- Safe reproduction/evidence: Analyze `static Action Combine(Action a, Action b) => a + b;`. Operand parameter references add no effects, the built-in operator has no method symbol, the result type is not string, and lines 352-365 contribute no effect; the returned summary can be complete with `Allocation=None`. Calling it with two nonnull delegates constructs a multicast delegate.
- Closest `BUGS.md` distinction: No live entry mentions delegate combination, delegate removal, multicast delegates, or `Delegate.Combine`. Entries about delegate creation/method references concern receiver exceptions or direct `IDelegateCreationOperation`; this defect is the built-in binary delegate operator, which is not represented as delegate creation.

## Wave 19.10. HIGH - A null reference-type awaiter can dereference without a modeled NullReferenceException

- Exact file/member/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `OperationEffectScanner.ScanAwait`, lines 93-202, especially synthetic awaiter creation and protocol calls at 154-169 and 189-201; `ScanAwaitProtocolCall`, lines 246-265.
- Mechanism: The scanner null-checks the awaitable before an instance `GetAwaiter` (123-132), but after `GetAwaiter` it represents the returned awaiter only as a synthetic fresh region (154-155). `ScanAwaitProtocolCall` resolves `IsCompleted`, continuation registration, and `GetResult` with that region while passing `instance: null`, and never applies `PotentialNullReceiver` or return-nullability to the returned awaiter. A reference-type `GetAwaiter` may legally return null, in which case the compiler-generated `IsCompleted` dereference throws NRE before entering the getter.
- Concrete impact: The await expression's complete summary can omit a reachable `NullReferenceException`, allowing a false allowed-exception/`DoesNotThrow` result and losing correct sequencing at the failed protocol boundary.
- Safe reproduction/evidence: Use `sealed class Awaitable { public Awaiter? GetAwaiter()=>null; }` and a reference `Awaiter : INotifyCompletion` with `IsCompleted`, `OnCompleted`, and `GetResult`, then `await new Awaitable();`. Runtime throws NRE on the first access to the null awaiter. The scanner reaches line 165 and resolves the getter with no null check; only the original `Awaitable` receiver was checked.
- Closest `BUGS.md` distinction: Live `BUGS.md` has no null-awaiter or await-protocol scanner entry. Wave 7.16 is receiver/argument ordering for ordinary calls, and Wave 13.5 is the wrong exception for nonvirtual method-group conversion; neither concerns the compiler-generated protocol receiver returned by `GetAwaiter`.

## Wave 19.11. MEDIUM - `FormattableString` interpolation eagerly attributes deferred formatting effects

- Exact file/member/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `OperationEffectScanner.ScanInterpolatedString`, lines 371-437, especially per-hole formatting resolution/completion at 413-429.
- Mechanism: The method treats every nonconstant interpolation as immediate string formatting. For each nonstring hole it resolves a formatting/`ToString` call and sequences that call's summary. But an interpolated string converted to `FormattableString` (or `IFormattable`) creates a format template plus argument array; it does not format holes or invoke their `ToString`/`IFormattable.ToString` until the returned object is later formatted. `ScanInterpolatedString` never branches on `interpolation.Type`/conversion target.
- Concrete impact: Pure construction of a `FormattableString` gains calls, writes, throws, or divergence that do not execute there, causing false effect-contract failures and potentially suppressing reachable suffix effects when the deferred formatter is modeled noncompleting.
- Safe reproduction/evidence: `sealed class V { public override string ToString()=>throw new InvalidOperationException(); } static FormattableString Make(V v) => $"{v}";`. `Make` returns normally without calling `v.ToString`; formatting the returned value later throws. Current lines 413-429 resolve and sequence `V.ToString` during `Make`.
- Closest `BUGS.md` distinction: Wave 7.23 concerns choosing parameterless `ToString` instead of `IFormattable.ToString` when formatting really occurs immediately, and Wave 18.30 concerns loss of interpolation terminality. This finding is target-kind/timing: `FormattableString` construction performs no hole formatting at all.

## Wave 19.13. MEDIUM - Core `IrSubstitution` validates one dictionary view but consumes another

- Exact file/member/current lines: `SharpProof.Ir/IrSubstitution.cs`, public overload `IrSubstitution.Substitute(IrFactory, IrTerm, IReadOnlyDictionary<IrVarId, IrTerm>)`, lines 21-48, especially validation enumeration at 30-41; private `Rewrite`, lines 56-99, especially later `TryGetValue` and unchecked memo insertion at 72-76.
- Mechanism: The public overload enumerates the caller-owned `IReadOnlyDictionary` to validate every key, destination-factory ownership, and replacement type, but retains that same unsnapshotted object. Rewriting later calls `TryGetValue` and directly stores the returned value without repeating null/ownership/type checks. A mutable dictionary changed after validation, or a custom `IReadOnlyDictionary` whose enumeration and lookup views differ, can therefore return a foreign-factory, wrong-typed, or even null replacement. The bare-variable/root-variable path performs no factory operation that would incidentally reject it: line 75 places the unchecked value in `memo`, and line 99 returns it.
- Concrete impact: A public core-IR operation can return a term that does not belong to the supplied factory (or return null despite its nonnullable return type), breaking the central factory-scoped typed-term invariant. Downstream `EnsureTerm`/encoding/building can fail unexpectedly; consumers that accept the returned node before another factory check can propagate malformed IR. Current in-repo callers generally pass ordinary internally owned dictionaries, which limits present production reach but does not repair the public boundary.
- Safe reproduction/evidence: Implement a nonmutating custom `IReadOnlyDictionary<IrVarId,IrTerm>` whose enumerator yields `(localVariable, localFactory.Boolean(true))`, while `TryGetValue(localVariable, out value)` returns `foreignFactory.Boolean(false)`. Call `IrSubstitution.Substitute(localFactory, localFactory.Variable(localVariable), dictionary)`. Validation passes, and the result is reference-equal to the foreign term; `localFactory.EnsureTerm(result, ...)` then rejects it. A `TryGetValue` implementation returning `null!` makes the public method return null.
- Closest `BUGS.md` distinction: Wave 13.9 is the analogous unsnapshotted lookup in `SharpProof.Specs/ApiSpecInstantiation`; it does not cover the independent public core `IrSubstitution` boundary used across Worker, Contracts, Summaries, and Analyzer. Wave 18.33 covers validate-then-copy races in caller arrays in `IrFactory`/`IrProgramBuilder`, not an `IReadOnlyDictionary` whose enumeration and lookup views differ. Live `BUGS.md` contains no `IrSubstitution` finding.

## Wave 19.14. LOW - Portable IR round-trip silently converts structural sequence types into identity-bearing sequence types

- Exact files/members/current lines: `SharpProof.Ir/IrFactory.cs`, structural `GetOrCreateSequenceType(IrTypeId)`, lines 108-120, contrasted with identity-bearing overload at 123-131; `SharpProof.CompilerArtifact/PortableIrModel.generated.cs`, `PortableIrType`, lines 25-34 and `Encoder.TypeRow`, lines 246-252; `SharpProof.CompilerArtifact/PortableIrGraphCodec.cs`, `Decoder.DecodeType`, lines 510-539, especially 524-530.
- Mechanism: The structural factory overload interns sequence types under the special identity key `-1` (`IrFactory` line 113), whereas the second overload interns under an explicit semantic identity. `PortableIrType` serializes only `Kind`, `Name`, and element index, so it does not record which form created the type. On decode, every sequence row unconditionally calls the identity-bearing overload with a fresh `CreateIdentity()` (`PortableIrGraphCodec` lines 528-529). The canonical re-encode check cannot detect the change because both forms project to exactly the same portable row.
- Concrete impact: An accepted encode/decode round-trip changes the decoded factory's type-interning semantics. Resolving the same structural sequence again in the decoded factory creates a second, unequal `IrTypeId`; terms/variables using the decoded type then fail exact type checks against terms created from the factory's canonical structural sequence type. This can cause unexpected type mismatches when a decoded graph is extended or combined. Current Roslyn production lowering normally uses the identity-bearing array overload, limiting the current packaged path, but the codec accepts structural sequence IR without rejecting or preserving it.
- Safe reproduction/evidence: Create `originalType = f.GetOrCreateSequenceType(f.IntegerType)`, make a variable/root of that type, and encode/decode it. Before encoding, `f.GetOrCreateSequenceType(f.IntegerType) == originalType`. After decoding, let `decodedType = decoded.Roots[0].Type`; `decoded.Factory.GetOrCreateSequenceType(decoded.Factory.IntegerType) != decodedType`. Re-encoding still produces byte-identical portable metadata, proving the canonical-image check misses the semantic change.
- Closest `BUGS.md` distinction: Wave 10.2 concerns unbound `DocumentationCommentId` on members; Wave 18.34 concerns malformed UTF-16 metadata collapsing during JSON serialization. Neither concerns the omitted structural-versus-identity-bearing sequence discriminator or post-decode factory interning behavior. Live `BUGS.md` has no structural-sequence round-trip entry.

## Wave 19.15. MEDIUM - Unsupported compound/increment lowering omits the mandatory lvalue read and its faults before evaluating the RHS

- Exact files/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LowerStatement`, lines 158-167 (routes increment/decrement and compound assignment to unsupported mutation handling); `LowerUnsupportedMutation`, lines 445-462, especially 451-459; `LowerLocation`, lines 333-371, especially one-dimensional array handling at 352-356 and field handling at 340-343.
- Detailed mechanism: C# compound assignment and increment/decrement first evaluate the lvalue and read its current value; for an array element or instance field, that read performs the null/bounds checks before the compound RHS (and an increment has the same mandatory read even with no explicit RHS). `LowerUnsupportedMutation` calls `LowerLocation` for a non-variable target, but `LowerLocation` only constructs an `IrLocation`; it emits no `Load`. The method then lowers the compound RHS, records `UnsupportedMutation`, and emits only havoc. It never emits either a load or store. Consequently a null receiver or null/out-of-range array never faults in the lowered program, and a side-effecting RHS call is emitted and can execute even though C# would throw before reaching it. The final havoc cannot restore the missing exceptional boundary or undo the impossible RHS observation.
- Concrete impact: The returned abstained `FrontendProgramLoweringResult.Program` contains a normal continuation and RHS calls that are unreachable at runtime, while omitting a mandatory `NullReferenceException`/`IndexOutOfRangeException`. Exact-only compiler-body admission does fail closed because of the abstention, limiting proof exposure, but public frontend consumers or replay/diagnostic tooling retaining the abstained program observe incorrect order and reachability.
- Safe reproduction/evidence: Lower a CFG for `static int Probe(){ Seen++; return 1; } static int M(int[] a){ a[0] += Probe(); return Seen; }`. Static trace: lines 352-356 lower `a` and `0` only into `SequenceLocation`; lines 456-459 then lower `Probe`, emitting its `IrCallInstruction`; lines 460-461 add abstention/havoc, with no `IrLoadInstruction` or `IrStoreInstruction`. For `a == null` or `a.Length == 0`, compiled C# throws before calling `Probe`, whereas the lowered instruction stream includes the call and reaches the return. `a[0]++` is an even smaller proof that the mandatory target read/fault is absent.
- Closest `BUGS.md` distinction: Wave 3.6 is a simple ref-return assignment whose unsupported target call is skipped before the RHS; it does not cover the mandatory read/fault of field/array compound or increment targets. Wave 18.1 is exact ref-local alias erasure. Wave 5.34 is the Effects scanner's missing setter value region, not frontend program evaluation. No live entry covers an unforced lvalue read causing RHS execution and normal continuation on a target fault.

## Wave 19.16. LOW - Null conversions to Roslyn error types are lowered as Exact reference nulls

- Exact files/members/current lines: `SharpProof.Frontend/RoslynOperationLowerer.cs`, `GetTypeId`, lines 78-91, especially the explicit `TypeKind.Error` mapping at 86-90; `LowerConstant`, lines 377-414, especially 388-402; `LoweringVisitor.VisitConversion`, lines 813-863, especially the constant-operand route at 844-850. Whole-program manifestation: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LowerDeclarator`, lines 184-195, and `LowerValue`, lines 241-262.
- Detailed mechanism: `GetTypeId` deliberately represents an `IErrorTypeSymbol` as an IR reference type. For an erroneous conversion such as the implicit conversion of `null` to unresolved `Missing`, `VisitConversion` first obtains an exact null operand and then takes the `operation.Operand.ConstantValue.HasValue` branch to `LowerConstant(operation)`. `LowerConstant` rejects only non-special value types; an error type is not admitted by that guard. Because `GetTypeId(errorType)` has `IrTypeKind.Reference`, the null branch returns `LoweredExpression.Exact(factory.Null(errorReferenceType))`. This bypasses `DefaultVisit`'s explicit `TypeKind.Error -> FrontendAbstention.ErrorOperation` policy and also bypasses `CompilerIdentityBridge.IsSupportedValueDomain`, which rejects error types.
- Concrete impact: The public expression lowerer can certify an operation from an erroneous compilation as Exact instead of closed-abstaining. In a CFG, an error-typed local initialized to null can receive a matching error-reference IR variable and exact null assignment, allowing the entire otherwise scalar program to remain Exact. Normal successful builds exclude such source, so production proof exposure is limited; analyzer/IDE and direct public-API consumers can nevertheless receive a false exact classification rather than the documented typed abstention.
- Safe reproduction/evidence: Build a Roslyn compilation for `static long M(){ Missing value = null; return 0L; }` without defining `Missing`; obtain the initializer's `IConversionOperation` and lower it. Its target is `TypeKind.Error` and operand constant is present/null. The line-by-line route above returns `IsExact == true` and an `IrNullTerm` whose type kind is Reference. Lower the CFG to observe an error-reference local assignment with no abstention. Expected closed result is `ErrorOperation` (or at minimum `UnsupportedType`), matching `DefaultVisit` and `IsSupportedValueDomain`.
- Closest `BUGS.md` distinction: Wave 3.7 covers null-to-pointer/function-pointer conversions entering exact reference IR; this is a distinct Roslyn error-type route that contradicts the lowerer's dedicated `ErrorOperation` classification and occurs in ordinary erroneous source without unsafe code. No live `BUGS.md` entry mentions `TypeKind.Error` or error-type operations bypassing frontend abstention.

## Wave 19.17. HIGH - Scalar mutations used as values abstain but leave the mutated variable falsely unchanged

- Exact files/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`: `LowerStatement`, lines 124-173 (top-level mutation cases at 158-167); `LowerDeclarator`, lines 184-195; `LowerValue`, lines 241-263; `Observe`, lines 514-520. `SharpProof.Frontend/RoslynOperationLowerer.cs`: `Opaque`, lines 285-332; `LoweringVisitor.DefaultVisit`, lines 449-463. There is no `VisitIncrementOrDecrement` or assignment visitor capable of emitting program state changes.
- Detailed mechanism: Program lowering recognizes `IIncrementOrDecrementOperation` and `ICompoundAssignmentOperation` only when they arrive as top-level statements through `LowerStatement`. The same executable mutations are legal value operations, for example a post-increment used as a local initializer. `LowerDeclarator` sends the initializer to `LowerValue`; because `LowerValue` special-cases only invocations and root field/array reads, it delegates the mutation to `RoslynOperationLowerer`. That expression lowerer can construct only terms, not program instructions. `DefaultVisit` returns an impure opaque term (recursively containing the old variable term) and a closed abstention. `Observe` records the abstention, but neither the expression lowerer nor `LowerValue` assigns or havocs the mutated variable. Since the opaque result has the expected scalar type, `AssignOrHavoc` assigns it only to the initializer target. A later read therefore uses the stale pre-mutation variable.
- Concrete impact: The returned `FrontendProgramLoweringResult.Program` underapproximates state changes even though other unsupported-statement paths deliberately call `HavocKnownState`. A consumer retaining an abstained program can derive a return/state result C# cannot produce. The current compiler-body admission requires `IsExact`, which limits accepted-proof exposure, but the public program artifact itself is not conservative on its documented closed-abstention path.
- Safe reproduction/evidence: For `static long Target(long x) { long old = x++; return x; }`, C# returns 1 when `x == 0`. The deterministic source trace is declaration -> `LowerDeclarator` -> `LowerValue(IIncrementOrDecrementOperation)` -> expression `DefaultVisit`/`Opaque`; it emits an assignment for `old` and records `UnsupportedOperationKind`, but emits no assignment/havoc for `x`. The return lowers the original `x` variable, so the produced program returns 0 for that input. No repository mutation is needed to establish the missing state instruction. `SharpProof.Frontend.Test/ProgramLoweringTests.cs` only covers an increment as a standalone expression statement (`UnsupportedMutationAbstainsAndHavocsWithoutThrowing`, lines 172-205), which takes the different `LowerStatement` arm and therefore does not cover this value-position path.
- Closest live-`BUGS.md` distinction: Wave 3.3 is the analogous omission for invocations nested inside larger expressions (lost calls/ref-out/memory havoc), not mutation operations and their direct local state update. Wave 18.1 is ref-local alias erasure while lowering remains `Exact`; this finding needs no ref local and is the missing havoc/update after an explicitly abstained value-position mutation. Wave 11.5 concerns overflow proof gating in `SharpProof.Effects`, not frontend program state.

## Wave 19.18. HIGH - Nested array reads bypass `IrLoadInstruction`, so post-store reads use the original sequence term while lowering remains `Exact`

- Exact files/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`: `LowerValue`, lines 241-263, especially the root-only `IArrayElementReferenceOperation` load path at 247-255; `LowerAssignment`, lines 205-239; `LowerLocation`, lines 333-372, especially sequence locations at 352-356. `SharpProof.Frontend/RoslynOperationLowerer.cs`: `VisitBinaryOperator`, lines 655-768; `VisitArrayElementReference`, lines 907-930, especially direct construction of `SequenceAccess` at 922-925.
- Detailed mechanism: A root array-element value is translated by `RoslynProgramLowerer.LowerValue` into an `IrLoadInstruction` from an `IrSequenceLocation`, so it participates in program memory. But when the same array read is nested under an otherwise supported expression (equality, checked arithmetic, etc.), `LowerValue` sees only the outer operation and delegates the entire tree to `RoslynOperationLowerer`. Its array-element visitor returns a pure `IrSequenceAccessTerm` over the original array variable and index; it cannot emit a load instruction and does not consult preceding stores. All subexpressions remain supported, so `Observe` sees `Exact` and the overall program classification remains `Exact`. Thus writes use the program location/memory vocabulary while nested reads of the same location use the immutable source sequence term vocabulary.
- Concrete impact: The exact program can return a value contradicted by mandatory preceding C# stores. This is not merely a missed abstention: it publishes an `Exact` lowering with internally inconsistent read-after-write semantics. Any program consumer that models `IrStoreInstruction` as memory cannot make the nested `SequenceAccess` consult that updated memory; consumers that reject memory instructions instead cannot honor the frontend's exactness claim.
- Safe reproduction/evidence: For `static bool Target(long[] values) { values[0] = 41L; return values[0] == 41L; }`, every normally completing C# execution (nonnull array with length > 0) returns true. Current lowering emits a sequence `Store` for the first statement. The return's root is `IBinaryOperation`, so its nested array child goes through `RoslynOperationLowerer.VisitArrayElementReference` and becomes `SequenceAccess(values, 0)`; no `IrLoadInstruction` is emitted before the return. With an initial element other than 41, the return term compares that original element to 41 and can be false despite the preceding store. By contrast, changing the return to root `return values[0];` takes `LowerValue` lines 247-255 and emits the required load, demonstrating the root/nested split directly. Existing `AssignmentLocationsAreEvaluatedBeforeValues` tests only store operand ordering and does not read the stored element through a nested expression afterward.
- Closest live-`BUGS.md` distinction: Wave 3.3 covers nested calls losing side effects and havoc; it does not cover array reads bypassing the location/load memory model or an `Exact` read-after-write contradiction. Wave 3.4 covers root field loads bypassing supported-value-domain admission, not supported array elements nested under expressions. Wave 13.20 concerns lowering unreachable CFG blocks, not location coherence.

## Wave 19.19. HIGH - Advisory activation ignores clause-based preconditions from source `CompilationReference`s

- Files/members/current lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `GetAdvisoryActivation` lines 203-247; `MayContainExternalClosedPreconditions` lines 303-360; `TypeContainsClosedPrecondition` lines 502-526. Supporting intended source-reference binding path: `SharpProof.Contracts/ContractClauseInventoryBuilder.cs`, `CreateCore` lines 44-101 (especially 55-59); `SharpProof.Frontend/CompilationModelProvider.cs`, `FindOwningCompilation` lines 29-56.
- Mechanism: Advisory activation scans syntax only in `compilation.SyntaxTrees`, which excludes trees owned by a referenced source compilation. If local syntax has no attribute or Contract API candidate invocation, it falls through to `MayContainExternalClosedPreconditions`. For a `CompilationReference`, that helper recursively inspects only method return/parameter `NotNull`/`Positive`/`InRange` attributes; it never looks for direct `Contract.Requires` clauses or clauses supplied by a referenced source `[ContractFor]` companion. It therefore returns false for source-reference methods whose only precondition is clause-based. Activation becomes `None`, `analysisEnabled` is false, no session/operation action is registered, and the local call is never checked. This is not an inherent inability to read referenced source: the clause builder requests semantic models for callable syntax, and `CompilationModelProvider` explicitly traverses `CompilationReference` compilations to find the owning tree.
- Concrete impact: In the default advisory profile, an ordinary call can violate a source project/reference `Contract.Requires` with no SP0027 and no semantic outcome. Roslyn workspace/IDE hosts that use source `CompilationReference`s are affected; the same caller is analyzable once full activation is otherwise forced (or under strict profile).
- Safe reproduction/evidence: Build an `external` `CSharpCompilation` containing `public static void Need(int x) { Contract.Requires(x > 0); }` and a `caller` compilation with `[external.ToMetadataReference()]` containing only `External.Need(-1);` (no local attributes or Contract candidate names). Run the analyzer with default advisory and a recording session factory: the local syntax scan finds nothing, `TypeContainsClosedPrecondition` finds no closed attribute, factory creation remains zero, and SP0027 is absent. Re-run strict (or add an unrelated local attribute to force Full); the source-reference body is available through the cited model-provider path and the violating call reaches analysis.
- Closest `BUGS.md` distinction: Wave 12.9 is the same activation phase but only the narrower failure to enumerate `CompilationReference` property/event accessors that do carry closed return/parameter attributes. This finding concerns ordinary methods (and referenced `ContractFor` companions) whose preconditions are executable Contract clauses, a category the helper never scans at all. Wave 18.49 is a final-compilation probe dropping `CompilationReference`s, not analyzer activation or SP0027. Wave 5.17 concerns ambiguous ownership when the same tree is shared, not omission of unique referenced trees.

## Wave 19.20. MEDIUM - Member-initializer reachability ignores earlier initializers in other partial declarations

- File/member/current lines: `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `AnalyzeMemberInitializer` lines 414-512; `CanReachMemberInitializer` lines 515-561, especially `FirstAncestorOrSelf<TypeDeclarationSyntax>` at 522 and the loop over only `containingType.Members` at 529-560.
- Mechanism: For each target initializer, reachability is computed from the nearest single `TypeDeclarationSyntax`. The helper walks earlier fields/properties only in that one partial declaration and returns true without consulting any other declaration syntax of the same `INamedTypeSymbol`. Roslyn emits one actual instance/static initialization sequence across all partial parts. If a part ordered earlier by the compilation has a definitely non-completing initializer, later initializers in another part are unreachable at runtime, but this helper cannot see the barrier and analyzes them as reachable.
- Concrete impact: SharpProof can emit false SP0027 diagnostics and record non-NotApplicable constructor outcomes for initializer calls that the CLR can never execute because type/instance initialization already terminates in an earlier partial part.
- Safe reproduction/evidence: Create two syntax trees in that order. Tree A: `partial class C { static int First = Stop(); static int Stop() => throw new Exception(); }`. Tree B: `partial class C { static int Later = Guard.NeedPositive(-1); }`, where `NeedPositive` has a recognized positive precondition. The emitted type initializer executes `First` before `Later` and cannot reach `Later`; when analyzing Tree B, line 522 selects only Tree B's declaration and the line-529 member loop never observes `First`, so `CanReachMemberInitializer` returns true and the call is checked/reported. The same construction works for instance initializer ordering across partial parts.
- Closest `BUGS.md` distinction: Wave 3.2 covers a non-completing base-constructor path before instance initialization, not initializer ordering across partial declarations. Wave 5.13 is the first-constructor entry-state selection false-negative; Wave 7.33 is delegating-constructor suppression. Wave 18.16 is a cross-tree `SpanStart` collision in the Effects scanner's array-region cache, not the analyzer pipeline's reachability walk or partial-member ordering.

## Wave 19.21. HIGH - Nested expression-bodied local functions can be mistaken for a custom list pattern's constant Length, suppressing a mandatory indexer call

- Exact files/functions/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`: `TryGetIntegralConstantReturn`, lines 502-563, especially broad descendant-arrow fallback 530-556; `TryGetGoverningListLength`, 468-495; `CanCompleteListPattern`, 261-340; `GetReachableImplicitListPatternMembers`, 350-415. Consumer: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanListPattern`, 899-934, especially member enumeration 906-932.
- Mechanism: For an explicit block-bodied Length/indexer getter that is not a one-statement return, `TryGetIntegralConstantReturn` searches every descendant `ArrowExpressionClauseSyntax` and uses the first one's constant value. An unrelated expression-bodied local function inside the getter is therefore accepted as the getter's return. Example: `get { int Dummy() => 0; return 1; }` is inferred as Length 0. For a one-element pattern, both completion paths then treat the length as mismatched; `GetReachableImplicitListPatternMembers` returns after Length and never emits the runtime indexer, while `CanCompleteListPattern` says the pattern can complete.
- Concrete impact: Indexer writes, allocation, capabilities, exceptions, and noncompletion can disappear from an otherwise Complete effect summary. This can falsely prove purity/`DoesNotThrow`/allowed-effect contracts and retain impossible suffix effects.
- Safe reproduction/evidence: Use `sealed class L { public int Length { get { int Dummy()=>0; return 1; } } public int this[int i] { get { Global.State++; throw new InvalidOperationException(); } } }` and analyze a nonnull `L x` in `_ = x is [_]; Global.After++;`. C# runtime gets Length=1 and necessarily calls the throwing/writing indexer. Syntax tracing is deterministic: the accessor has two statements, so lines 523-529 do not match; its local function contributes the first descendant arrow at 530-534; constant 0 passes 544-556; `requiredLength=1` causes the mismatch early return at 376-380, so scanner lines 906-932 resolve only Length.
- Closest `BUGS.md` distinction: Wave 3.14 concerns eager nested-subpattern scanning before real Length/indexer/null gates; it does not fabricate the governing length or omit the mandatory indexer. Wave 5.24 is the call-site Requires analyzer discarding implicit indexer arguments, not Effects call discovery. Wave 10.9 suppresses metadata ref-struct list members as intrinsics. Wave 18.30 is default-true completion for array initializers/interpolation; here the specialized list-pattern completion/member enumerator runs and is poisoned by the wrong syntax node.

## Wave 19.23. MEDIUM - User-defined conditional operators ignore mandatory RHS/operator noncompletion when the truth operator has a fixed forcing result

- Exact files/functions/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteBinary`, 1032-1088, especially user-defined conditional arm 1054-1065; consumer `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanBinary`, 338-368, especially final completion label 366-368.
- Mechanism: For user-defined `&&`/`||`, completion checks only whether `op_False`/`op_True` itself can return. It never evaluates whether that truth operator has a fixed result that mandates RHS evaluation, and it never checks the selected `op_BitwiseAnd`/`op_BitwiseOr` completion. Thus a pure, returning `op_False => false` followed by an always-throwing `operator &` is relabeled completing.
- Concrete impact: Effects after an expression that cannot return are retained, producing false writes/calls/allocations and false effect-contract failures.
- Safe reproduction/evidence: `sealed class Flag { public static bool operator false(Flag _) => false; public static bool operator true(Flag _) => false; public static Flag operator &(Flag a, Flag b) => throw new InvalidOperationException(); } static void M(Flag f){ _ = f && f; Global.State++; }`. C# must evaluate RHS and `operator &` because `op_False` returns false; runtime never reaches the write. Lines 1054-1065 see only that `op_False` completes and return true. `ScanBinary` resolves the throwing `operator &` summary but uses that true completion flag at 366-368, so CFG traversal retains the suffix.
- Closest `BUGS.md` distinction: Wave 7.18 says scanner omits the truth-operator effects; this reproduction makes the truth operator pure and exposes separate terminality loss after it returns. Wave 7.19 says RHS is scanned unconditionally even when short-circuited; here RHS/operator execution is mandatory. Wave 18.27 is virtual dispatch completion, not conditional-operator control semantics.

## Wave 19.24. MEDIUM - API-spec catalog generator silently treats a missing or misspelled `postconditions` property as an explicitly empty list

- Exact files/members/current lines: `scripts/Generate-ApiSpecCatalog.ps1`, top-level declaration emission at lines 1038-1058 (especially `$postconditions = @($declaration.postconditions)` at 1038 and the empty emission at 1039-1041), plus documentation emission at lines 1173-1181; the script already has `Get-RequiredProperty` at lines 113-130 but does not use it here. The catalog contract is evidenced by `SharpProof.Specs/DefaultApiSpecCatalog.json`, where every declaration explicitly supplies `postconditions` (current lines 121, 163, 207, 253, 319, 367, 413, 461, 507, 552, 594, 663, 707, 753, 797, 841), and `SharpProof.Specs.Test/DefaultApiSpecCatalogGenerationTests.cs`, `AssertDeclaration`, line 288, which reads it with `JsonElement.GetProperty("postconditions")` as required.
- Detailed mechanism: PowerShell returns `$null` for an absent `PSCustomObject` property. Wrapping it in `@(...)` at line 1038 produces an empty collection, so the generator takes the zero-count branch and emits `[]` into `CreateDefaultDeclarations`. The same permissive access at line 1173 makes the generated documentation say `None`. Unknown extra JSON properties are not rejected, so a typo such as `postconditons` plus omission of `postconditions` follows exactly this path. Runtime `ApiSpecTable.Create` sees a properly initialized empty immutable array and cannot distinguish omission from an intentional empty declaration; regenerating expected artifacts makes parity checks compare the normalized empty result rather than fail the malformed schema.
- Concrete impact: A catalog edit can silently delete all trusted relational facts for a row. For example, misspelling/removing the `postconditions` property on `bcl.math.abs.int32` removes the shipped `result >= 0` fact while generation, runtime table construction, and generated documentation remain internally consistent. Exact verification then loses a proof assumption rather than failing the catalog build, so contracts that rely on the reviewed fact degrade to Unknown/failure with no diagnostic identifying the catalog typo. This violates fail-closed catalog generation and can conceal accidental semantic weakening during review.
- Safe reproduction/evidence: In an isolated copy of `DefaultApiSpecCatalog.json`, rename only the Math.Abs row's `postconditions` key to `postconditons`, then invoke `Generate-ApiSpecCatalog.ps1` with all output paths directed to a temporary directory. Source trace guarantees exit success: line 1038 reads absent `postconditions` as null/empty, lines 1039-1041 emit `[]`, and lines 1173-1178 render `None`. The generated Math.Abs declaration has no postconditions. No repository mutation is needed to establish the trace. `SharpProof.Specs.Test` passed 55/55 in the canonical tooling container.
- Closest live `BUGS.md` distinction: Wave 6.8 concerns blank semicolon-separated specification-pack IDs being discarded by `FinalCompilationCollector.ParseSpecificationPacks`; it is a different runtime compiler-property parser and not the JSON API-spec generator. Wave 3.20 concerns incompatible result facets accepted by `ApiSpecTable`; it does not cover omission/typo of the required `postconditions` property or generator normalization. Wave 15.25 concerns a test fixture that never reaches conditional-shape validation; it does not concern catalog schema enforcement. A fresh search of the current live `BUGS.md` found no missing-property/API-spec-generator entry.

## Wave 19.25. HIGH - Definitely-null lifted user-defined operators are still invoked in scanning and completion, suppressing real suffix effects

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`: `ScanIncrementOrDecrement`, lines 320-335; `ScanBinary`, lines 338-368, especially unconditional `ResolveOperatorEffects` at 362-365; `ScanUnary`, lines 440-453, especially 448-453. `SharpProof.Effects/OperationEffectScanner.Assignments.cs`: `ScanCompoundAssignment`, lines 79-100; `ScanReadModifyWrite`, lines 103-134. Supporting completion path: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteCompoundValue`, lines 867-888 (operator call 883-887); `CanCompleteIncrementValue`, lines 890-898; `CanCompleteBinary`, lines 1032-1087 (operator call 1083-1087); `CanCompleteUnary`, lines 1090-1097. Existing but unused intended predicate: `SharpProof.Effects/ConversionEffectClassifier.cs`, `SkipsLiftedOperator`, lines 107-129. Downstream successor suppression: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`, lines 351-373, especially 367-373.
- Detailed mechanism: A lifted user-defined nullable operator does not invoke its underlying operator method when a nullable operand is definitely null; operand evaluation still occurs and the lifted result is produced directly. The code already encodes that rule in `SkipsLiftedOperator` for binary, unary, increment/decrement, and compound assignment, but the scanner uses that helper only for intrinsic overflow/division hazards. Every listed scan path still resolves `OperatorMethod` unconditionally. The completion evaluator independently calls `CanCompleteInvocation` for the same operator without an `IsLifted`/definitely-null bypass. Thus an operator body that writes/throws/diverges contributes impossible effects; more seriously, a proven-nonreturning operator makes the enclosing lifted expression falsely terminal even though the null path skips it and completes.
- Concrete impact: `EffectMethodNodeBuilder` does not enqueue regular CFG successors after the falsely terminal expression. Real later writes, allocations, calls, and exceptions can disappear from an otherwise complete caller summary, permitting unsound no-write/purity/allocation claims. Impossible operator effects can also cause false diagnostics.
- Safe reproduction/evidence: `struct S { public static S operator -(S _) { while (true) { } } } static int state; static void M() { S? value = null; _ = -value; state++; }`. C# evaluates `value`, skips `S.op_UnaryNegation` because the lifted operand has no value, produces null, and increments `state`. Managed flow can prove the local null, and `SkipsLiftedOperator` would return true. Current `ScanUnary` nevertheless resolves the diverging operator; `CanCompleteUnary` returns false from that source body; lines 367-373 in the node builder suppress the block containing `state++`. Binary (`S? left=null; _=left+right`), `value++`, and `value += right` follow the same unconditional operator paths.
- Closest live `BUGS.md` distinction: Wave 14.13 covers only the intrinsic lifted nullable `/ 0` and `/= 0` zero-divisor completion gate; it explicitly notes that the scanner hazard helper already skips that intrinsic hazard. This finding is the separate user-defined `OperatorMethod` call-resolution/completion path across unary, binary, increment/decrement, and compound assignment, where the operator body itself is impossible yet can poison completion and erase suffix effects. Wave 11.6 concerns omitted compound-assignment conversion methods, not a lifted operator that must be skipped. No live entry mentions lifted user-defined operator calls or the unused `SkipsLiftedOperator` predicate on these paths.

## Wave 19.26. MEDIUM - A timed-out injected backend is retired only for the current run, then reused by the next `VerifyAsync` call

- Exact file/member/current lines: `SharpProof.Worker/SharpProofWorker.cs`: persistent injected-backend fields/constructors, lines 8-25; `SharpProofWorker.VerifyAsync`, entry/disposed check lines 39-42, method-timeout renewal path lines 253-280, and final cleanup lines 341-347; `TryCreateLanes`, lines 421-465, especially injected-backend reuse at 431-435; `VerificationLane.Renew`, lines 490-532, especially `Unsupported` for an injected lane at 494-497; `VerificationLane.DisposeOwnedBackend`, lines 535-539.
- Detailed mechanism: The public injected-backend constructor stores one `_backend` for the lifetime of the worker. On a method timeout, `RunLane` explicitly attempts to retire/renew that lane. Because an injected lane is created with no factory and no owned backend (lines 431-435), `Renew` returns `Unsupported`; the current run stops/marks remaining work timed out. However, no worker-level retired/poisoned state is recorded. Final cleanup is a no-op for that caller-owned backend, and the next sequential `VerifyAsync` passes the sole `_disposed` check and `TryCreateLanes` wraps the exact same timed-out backend again. Caller/project cancellation has the same cross-run risk because it ends the run without replacing or invalidating `_backend` at all. The factory-backed path demonstrates the intended isolation: timed-out lanes are replaced during a run and all factory-owned lanes are disposed before the next run.
- Concrete impact: A long-lived public `SharpProofWorker` can contaminate a later independent request with backend state from an earlier timeout/cancellation. The later request can immediately become `Failed/BackendUnavailable` or `InfrastructureFailure`, inherit stale resource/interruption state, or enter an unsafe native backend after cancellation, even though a fresh backend proves the same request. The worker exposes no indication that the instance itself must now be discarded.
- Safe reproduction/evidence: Inject a deterministic test backend whose first `CheckAsync` waits for its token to cancel, sets `poisoned=true`, and throws `OperationCanceledException`; every later check returns `BackendCheckResult.Unknown(BackendFailureReason.Unavailable)`. Use one valid target and a short method wall time for request A. A returns `TimedOut/MethodTimeout`, and renewal is `Unsupported`. On the same worker, run request B with a generous wall time: it reuses the same instance and returns `Failed/BackendUnavailable` without a real check. Constructing a fresh worker/backend makes B complete. The production `IrSmtBackend` supplies concrete corroboration: its cancellation callback sets sticky `_interrupted`, and its next check returns `Unavailable`.
- Closest live `BUGS.md` distinction: Wave 2.4 is the backend-local defect that `IrSmtBackend._interrupted` is sticky. This finding is the separate worker lifecycle/state defect: `SharpProofWorker` knowingly retires an injected lane on timeout but forgets that retirement across sequential public calls, and it affects any legitimately nonreusable backend even if `IrSmtBackend`'s flag handling is repaired. Wave 3.35 covers concurrent calls sharing one injected backend, not sequential reuse after a completed canceled/timed-out call. Waves 3.36-3.37 concern classification/global retirement within one run, not worker-instance state carried into the next run.

## Wave 19.27. MEDIUM - Compiler response evidence validation is uncancellable and quadratic in postcondition claims, then redundantly replays the whole body per claim

- Files/members/current lines: `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`: `Validate`, lines 27-60, especially target/claim loop 42-55; `ValidateClaim`, lines 74-140, especially per-claim `FirstOrDefault` scans at 85-89; `TryReplayPostcondition`, lines 625-746, especially full `Ensures` materialization and `Array.FindIndex` at 725-729 and whole-program replay at 667-670. `SharpProof.Worker.Protocol/ResponseEvidenceAuthority.cs`: `IWorkerResponseEvidenceAuthority.Validate`, lines 8-10 (no cancellation/budget parameter). `SharpProof.Worker.Protocol/ProtocolJson.cs`: evidence-authority invocation, lines 319-337, especially 323-325. Reachable callers: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`, lines 305-315; `SharpProof.Worker.Launcher/Program.cs`, `ValidateAndReport`, lines 334-371.
- Mechanism: For every claim ID in a target, `Validate` calls `ValidateClaim`. That method linearly searches all effect claims and clauses. For every Refuted postcondition, `TryReplayPostcondition` again enumerates every clause into a new `Ensures` array and linearly searches it for the claim ID, then executes the same prepared program from entry. Thus one callable with N Refuted postconditions costs Theta(N^2) clause work plus N complete body interpretations. Neither the authority interface nor these validation methods accepts a cancellation token or resource budget. This runs after the worker result exists, both inside the worker's final validation and synchronously in the launcher.
- Concrete impact: A structurally valid, under-16-MiB compiler manifest/response can consume enough CPU and allocation after verification to overrun the project/launcher boundary, turn a valid result into an external timeout, or prevent the worker from promptly honoring cancellation. The N repeated program interpretations amplify the quadratic lookup cost for nontrivial bodies.
- Safe reproduction/evidence: In an internal read-only fixture, create one successful target with a trivial replayable one-integer program and N `Ensures` clauses with distinct claim IDs but the same false-on-model condition; create N canonical Refuted response rows carrying the same valid scalar input model. Invoke `CompilerResponseEvidenceAuthority.Validate(response).ToArray()` directly to isolate this path. Static counting gives at least N full clause enumerations plus N linear claim searches at lines 725-729 (and the earlier searches at 85-89), followed by N `IrProgramInterpreter.Execute` calls. A response with thousands of compact rows is representable below the protocol's 16-MiB cap; no cancellation can be injected while validation runs.
- Novelty/distinction from live `BUGS.md`: Wave 14.35 is quadratic claim lookup during protocol manifest/response canonicalization (`ProtocolManifest`/`ProtocolJson`); this is the separate artifact-aware evidence authority and includes redundant semantic body replay. Wave 14.34 is failure/cancellation assembly owner lookup, not evidence validation. Wave 18.46 is repeated source-line-map authentication per diagnostic/authority; this finding needs no source locations or line maps. Wave 15.21 concerns manifest acquisition/hydration outside the timeout, not the later response-evidence replay loop.

## Wave 19.28. MEDIUM - Fatal backend abstentions are mislabeled as semantic callable unknowns

- Exact files/members/current lines: `SharpProof.Worker/CallableVerificationPolicy.cs`, `CallableVerificationPolicy.VerifyTargetAsync`, lines 27-46, especially callable-reason derivation at lines 43-45. Production producer: `SharpProof.Smt/IrSmtBackend.cs`, `IrSmtBackend.CheckAsync`, lines 47-50 and 67-75. Downstream evidence of the inconsistency: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `Classify`, lines 99-137, especially claim-failure mapping at lines 112-120; `MatchesCallableProjection`, lines 170-228, especially the explicitly admitted direct-infrastructure projection at lines 220-227.
- Mechanism: `IrSmtBackend` legitimately returns `BackendCheckResult.Unknown(BackendFailureReason.Unavailable)` when the backend is disposed/interrupted and `Unknown(BackendFailureReason.InfrastructureFailure)` for caught Z3/encoding-runtime failures. The proof kernel and callable verifier preserve these as claim-level `Unknown/BackendUnavailable` or `Unknown/InfrastructureFailure`. But after verification, `VerifyTargetAsync` ignores every claim reason: if any record is Unknown it unconditionally assigns `WorkerCallableCoverageReason.SemanticUnknown` (lines 43-45). `WorkerResultAssembler.Classify` then correctly derives a fatal run-level `BackendUnavailable` or `InfrastructureFailure` from the claim reasons, yielding an internally contradictory response: the run and claim report a non-semantic backend/infrastructure failure while the owning callable reports `SemanticUnknown`. Protocol validation does not repair or reject this because `MatchesCallableProjection` treats `SemanticUnknown` as its default and merely also permits the more accurate direct `InfrastructureFailure` projection.
- Concrete impact: Per-callable coverage telemetry and any consumer grouping failures by `WorkerCallableCoverageReason` attribute a backend outage or worker infrastructure defect to semantic incompleteness. The run still fails, but the callable-level diagnostic channel cannot identify the affected callable as infrastructure-failed, undermining remediation, aggregation, and consistency of the typed response model. The same backend fault is labeled `InfrastructureFailure` when it throws into the policy catch, but `SemanticUnknown` when the backend uses its documented typed Unknown channel.
- Safe reproduction/evidence: The existing parameterized test `SharpProof.Worker.Test/WorkerTests.cs`, `FatalBackendFailuresFailTheRun`, current lines 4207-4241, already injects `BackendCheckResult.Unknown(BackendFailureReason.Unavailable)` and `...InfrastructureFailure`. It asserts the fatal run reason and claim reason but omits the callable reason. Add/read an assertion on `response.CallableResults.Single().Reason`: current control flow deterministically produces `WorkerCallableCoverageReason.SemanticUnknown` for both cases. By contrast, `UnexpectedBackendExceptionBecomesTypedInfrastructureFailure`, lines 4244-4266, reaches the catch path and reports callable `InfrastructureFailure`, demonstrating the channel-dependent inconsistency. No mutation or unsafe execution is required.
- Closest live `BUGS.md` distinction: Wave 2.10 concerns an un-signaled `OperationCanceledException` being mislabeled `MethodTimeout` and lane retirement, not typed backend Unavailable/InfrastructureFailure outcomes. Wave 18.36 concerns ordinary backend exceptions escaping `ProofKernel` rather than entering the typed Unknown channel; this finding occurs precisely when that channel works. Wave 11.1 concerns `UnsupportedContract` callable projection. No live entry covers fatal backend claim reasons being collapsed to callable `SemanticUnknown`.

## Wave 19.29. MEDIUM - Requested SARIF is never validated, and the shipped target does not even pass its path to the validator

- Files/members/current lines: `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `ValidatePublishedVerificationResult` properties and `Execute`, lines 21 and 27-130, especially the complete absence of any `SarifPath` use after its declaration. `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, target `_SharpProofVerifyCore`, validator invocation lines 214-217 and SARIF success message lines 220-222.
- Detailed mechanism: The validation task exposes `SarifPath` at line 21 but `Execute` validates only request, result, and manifest. The shipped target invokes the task with only `RequestPath`, `ResultPath`, and `ManifestPath`; it never supplies `_SharpProofEffectiveSarifFile`. Immediately afterward, when SARIF was requested, the target unconditionally emits `SharpProof verifier SARIF result: ...`. Therefore neither a missing SARIF file nor arbitrary/malformed/stale bytes at that pathname affect task success once the protocol trio is valid.
- Concrete impact: A build can succeed and advertise a SARIF result that does not exist or does not correspond to the accepted verification response. CI/security tooling consuming the advertised artifact can silently receive stale, corrupted, or cross-invocation diagnostics even while MSBuild reports successful verification.
- Safe reproduction/evidence: Use any protocol-valid request/result/manifest fixture accepted by `ValidatePublishedVerificationResult`. Set `SarifPath` to a nonexistent path, or create arbitrary non-JSON bytes there; `Execute` never reads the property and returns the same value. At package-integration level, configure `SharpProofVerifySarifFile`, pause after the launcher returns, remove/replace the effective SARIF before lines 214-222, and the validator still succeeds and the success message is emitted. Pure source evidence is also decisive: `SarifPath` has zero reads in the class, and the target has no `SarifPath=` attribute.
- Closest `BUGS.md` distinction: Wave 18.42 covers the omitted `InvocationResultPath` and consequent failure to bind the public protocol response to the private invocation. This finding concerns the separately requested SARIF artifact: even if Wave 18.42 is fixed by passing `InvocationResultPath`, SARIF remains neither passed nor validated. Wave 8.3 concerns URI encoding inside genuine SARIF; it does not cover missing/stale/arbitrary SARIF acceptance.

## Wave 19.30. MEDIUM - Final MSBuild validation bypasses the protocol size ceiling and reads attacker/stale-controlled files wholly into memory

- File/member/current lines: `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `ValidatePublishedVerificationResult.Execute`, lines 47-51 (private result), 60-62 (published request), 68-69 (published result), and 85-87 (compiler manifest).
- Detailed mechanism: The MSBuild boundary uses `File.ReadAllBytes` on every publication input before decoding/parsing/hashing. It neither checks file length nor calls the protocol's bounded file reader. Thus an oversized malformed request/result is fully allocated before it can be rejected. The configured paths are external filesystem boundaries and can be replaced between launcher completion and validation; the result/request concurrency risk is already recognized elsewhere in the ledger. `OutOfMemoryException` is also outside the catch filter at lines 121-123.
- Concrete impact: A concurrent or stale oversized publication file can allocate hundreds of MiB/GiB inside the long-lived MSBuild process, causing severe memory pressure or terminating the build host instead of producing the intended bounded invalid-result diagnostic. If exact-invocation validation is wired up, the same issue applies to the private result at lines 49-51.
- Safe reproduction/evidence: Point `ResultPath` at a large regular file (or atomically replace the configured result after launcher return) while supplying otherwise ordinary paths. `File.ReadAllBytes` allocates according to the entire length before `DeserializeResponse` can reject it; no task seam or malformed protocol construction is required. A sparse file larger than available managed memory demonstrates the uncaught `OutOfMemoryException` path without modifying product code.
- Closest `BUGS.md` distinction: Wave 18.41 concerns a race that grows a file after `WorkerProtocolJson.OpenJsonReader` performs its nominal 16 MiB check. This task does not use that bounded reader or perform any size snapshot at all, so the ceiling is bypassed unconditionally at a separate MSBuild validation boundary. Wave 8.1 concerns rollback snapshots in `LinuxPathIdentity`, not final protocol validation.

## Wave 19.31. MEDIUM - Final-compilation snapshot omits compiler diagnostic policy

- Exact file/member/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `AppendOptions`, lines 70-149.
- Detailed mechanism: `AppendOptions` records `AllowUnsafe`, `CheckOverflow`, `Deterministic`, a few language/preprocessor aggregates, `NullableContextOptions`, `OptimizationLevel`, `OutputKind`, `Platform`, and `Usings`. It never records `CompilationOptions.GeneralDiagnosticOption`, `SpecificDiagnosticOptions`, `WarningLevel`, or `ReportSuppressedDiagnostics`. These are legal final `CSharpCompilationOptions` and materially control compiler/analyzer diagnostic severity and whether a compilation succeeds. Therefore two final compilations with identical sources, references, identities, and every field serialized here, but different warning/error/suppression policy, produce byte-identical probe JSON.
- Concrete impact: The package-backed oracle can certify or compare the wrong final compiler configuration. A regression that suppresses an error/warning (including an analyzer diagnostic) or promotes a warning to an error can go undetected even though build outcome and emitted diagnostic set change.
- Safe reproduction/evidence: Construct the same `CSharpCompilation` twice, changing only `WithSpecificDiagnosticOptions` for a source diagnostic such as CS0168 from `ReportDiagnostic.Suppress` to `ReportDiagnostic.Error` (or change `GeneralDiagnosticOption`). `GetDiagnostics()`/emit success differs, while every property written by lines 81-147 remains equal and the complete snapshot is identical.
- Closest live `BUGS.md` distinction: Wave 18.47 concerns duplicate preprocessor symbols captured inconsistently, and Wave 18.51 concerns heuristic generated-source classification. Neither covers omitted `CompilationOptions` diagnostic policy; live `BUGS.md` has no compiler-warning/diagnostic-option snapshot entry.

## Wave 19.32. MEDIUM - Syntax-tree source encoding and checksum algorithm are erased from the final-compilation snapshot

- Exact files/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateSyntaxTreeRow`, lines 212-252, especially text conversion at 218 and `textSha256` at 246-250; `SharpProof.CompilerProbe.TestAsset/ProbeHash.cs`, `Text`, lines 5-8.
- Detailed mechanism: The probe converts `SourceText` to a plain `string` and always hashes the UTF-8 encoding of that string. It does not record `SourceText.Encoding`, `SourceText.ChecksumAlgorithm`, or `SourceText.GetChecksum()`. Roslyn syntax trees can legally have identical characters/path/parse options but different source encodings or SHA-1 versus SHA-256 document-checksum algorithms. Those distinctions affect portable-PDB document records and source provenance, but all snapshot rows remain identical.
- Concrete impact: Two compiler invocations that emit different PDB/source-checksum evidence can have the same purported final-compilation probe artifact. The oracle therefore cannot catch an encoding/checksum-algorithm regression or faithfully attest the compiler input used for debugging/source authentication.
- Safe reproduction/evidence: Create two `SourceText` instances from the same string, one with UTF-8 plus `SourceHashAlgorithm.Sha1` and one with UTF-16 (or UTF-8) plus `SourceHashAlgorithm.Sha256`; parse both with the same path/options and emit portable PDBs. Their document hash algorithm/checksum records differ, while line 218 produces the same string and `ProbeHash.Text` produces the same UTF-8 SHA-256, so the snapshots match.
- Closest live `BUGS.md` distinction: Wave 8.17 is downstream symbol validation ignoring the PE portable-PDB checksum; it does not concern the probe erasing the source text's encoding/checksum identity before emission. Wave 18.34 concerns malformed UTF-16 IR metadata serialization, not legal Roslyn `SourceText` encoding/checksum metadata.

## Wave 19.33. MEDIUM - Reference hashing ignores analyzer cancellation for the entire backing file

- Exact files/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `Create`, lines 7-49 (reference rows called at 37-41 without a token); `CreateReferenceRows`, lines 333-340; `CreateReferenceRow`, lines 343-382; `SharpProof.CompilerProbe.TestAsset/ProbeHash.cs`, `File`, lines 10-14. The only pre-snapshot token check is `CompilerProbeAnalyzer.WriteSnapshot`, lines 44-49.
- Detailed mechanism: Reference projection accepts no `CancellationToken`. For every file-backed `PortableExecutableReference`, `ProbeHash.File` synchronously calls `SHA256.ComputeHash(stream)`, which reads to EOF with no cancellation observation. Cancellation arriving after the single check in `WriteSnapshot` cannot interrupt the current hash or any subsequent reference hashes. PE files may legally contain large overlay data, so file size is not inherently bounded by compiler metadata size.
- Concrete impact: Canceling a build/test can leave the compiler analysis callback reading gigabytes of reference bytes (or waiting on a slow backing filesystem) before Roslyn can complete cancellation. The final-compilation health probe can therefore make cancellation ineffective and stall the compiler/test host.
- Safe reproduction/evidence: Use a valid referenced DLL with a large appended overlay (PE readers accept trailing bytes), start snapshotting, then cancel the analyzer token after hashing begins. Static control flow shows no token reaches lines 10-14, and `ComputeHash` must consume the entire stream before control returns.
- Closest live `BUGS.md` distinction: Wave 18.48 covers this probe reopening a pathname and hashing bytes different from Roslyn's bound metadata (TOCTOU); this finding remains even for an immutable file and concerns the absence of cancellation/bounds during the full read. Wave 18.13 concerns uncanceled source-tree activation scans in a different analyzer pipeline, not reference hashing in this probe.

## Wave 19.34. HIGH - Final publication validation can block the MSBuild host indefinitely on a replaced FIFO

- Exact files/members/current lines: `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, class declaration (line 10) and `Execute` (lines 27-130), especially `ResolvePath`/`RequireLocalPath` at 35-40 and unbounded `File.ReadAllBytes` calls for the private invocation result at 49-51, published request at 60, published result at 68-69, and compiler manifest at 85.
- Detailed mechanism: `LinuxPathIdentity.RequireLocalPath` rejects symlink traversal and selected remote filesystem types, but it does not require the leaf to be a regular file. `Execute` then opens each path with `File.ReadAllBytes` before any descriptor-backed regular-file check, byte bound, timeout, or cancellation boundary; the task also does not implement `ICancelableTask`. On Linux, opening a FIFO for read blocks until a writer appears. The launcher releases the publication lease before this separate MSBuild task runs, so a same-uid replacement of a published request or manifest with a FIFO reaches this path. The verifier process hard deadline no longer applies.
- Concrete impact: A build can hang its MSBuild node indefinitely after verifier supervision and publication have completed. Cancellation cannot interrupt the task, and the remaining published artifacts/locks may strand later builds.
- Safe reproduction/evidence: In the canonical Linux amd64 tooling container, create a disposable FIFO and invoke `[IO.File]::ReadAllBytes($fifo)` with no writer. A bounded 2-second probe exited via GNU `timeout` with status 124, establishing the blocking primitive used at lines 51/60/69/85. For product-level coverage, publish a normal trio, replace only the request path with `mkfifo` after launcher lease release, and invoke `ValidatePublishedVerificationResult.Execute` under an external test timeout; it never returns.
- Closest `BUGS.md` distinction: Wave 2.14 is the launcher's `ValidateAndReport` opening the private worker result through `WorkerProtocolJson` after worker supervision. This defect is the later, separate MSBuild final-validation task and covers the published request and compiler manifest (and optional invocation result) even after the launcher has already completed successfully. Wave 18.42 concerns the shipped target omitting `InvocationResultPath` and therefore skipping semantic cross-invocation binding; it does not cover non-regular path blocking and remains present even if that input is wired.

## Wave 19.35. HIGH - Fallback containment can SIGKILL descendants of an unrelated process after supervisor PID reuse

- Exact files/members/current lines: `SharpProof.BuildTasks/RunVerifier.cs`, `TryTerminate`, lines 873-966, especially stable pidfd signaling at 926-928 followed by numeric `processGroupId` fallback at 948-953. `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`, `StopDescendants`, lines 204-279; numeric ancestry discovery at 216-224, 238-243, and pidfd SIGSTOP/SIGKILL of every discovered process at 245-263; `DescendantProcessIds`/`IsDescendant`, lines 308-334.
- Detailed mechanism: `RunVerifier` correctly retains `_processGroupPidFd`, but `TryTerminate` never binds the numeric `processGroupId` used by `StopDescendants` back to that pidfd. If the original supervisor has exited and been reaped while its stdout is still incomplete (for example, a surviving inherited stdout holder after supervisor failure, or simply delayed drain), its numeric PID can be recycled while the retained pidfd continues to identify the dead original. `pidfd_send_signal(SIGTERM)` then returns ESRCH for the old process, but the fallback scans `/proc` using the recycled integer. `StopDescendants` confirms ancestry only against the new process currently owning that PID and deliberately sends SIGSTOP then SIGKILL via fresh pidfds to all of its descendants. The later old-pidfd SIGSTOP fails, but that occurs only after the unrelated descendants have already been killed.
- Concrete impact: A verifier timeout/output-drain failure can terminate arbitrary sibling workload descendants on the build host. It also falsely reports the old containment boundary as cleaned before cleanup authentication eventually fails.
- Safe reproduction/evidence: The source branch is exact: `TryTerminate` lacks an early `process.HasExited`/pidfd-identity gate before line 948, and `StopDescendants` accepts only an integer supervisor ID. A safe disposable PID-namespace harness can (1) retain a pidfd to an exited/reaped supervisor, (2) recycle its numeric PID onto a controlled leader with a sleep child, (3) enter the `terminateSent == false` fallback, and (4) observe that the controlled child is killed while signaling the retained old pidfd returns ESRCH. No production process need be targeted.
- Closest `BUGS.md` distinction: Wave 8.13 is a performance probe attaching to a reused worker PID, producing only a false gate failure/delay; this is production containment actively SIGKILLing descendants of a recycled supervisor PID despite retaining a stable pidfd. Wave 14.38 covers verifier descendants escaping when the supervisor dies; it does not identify collateral signaling of an unrelated recycled process tree by the later numeric fallback. Wave 13.22/18.39 concern cleanup cost and unbounded retries, not process identity.

## Wave 19.36. MEDIUM - Summary-evidence validator rejects the producer's canonical order for legal overload identities

- Exact files/members/current lines: `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`, property `SummaryEvidenceAuthorities`, lines 40-45. `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `Create` summary-evidence handoff lines 67-69 and `BuildSummaryEvidence`, lines 96-159, especially line 100 (order-preserving projection). `SharpProof.CompilerArtifact/CompilationFingerprint.cs`, `ValidSummaryEvidence`, lines 108-148, especially flattened key construction/comparison at 136-140.
- Detailed mechanism: The producer orders authorities lexicographically as the tuple `(Origin, CallIdentity, EvidenceIdentity, EvidenceSha256)` at `CompilerRelationalSummaryProvider.cs:40-45`. `BuildSummaryEvidence` preserves that order. Validation does not compare the same tuple; it concatenates the fields with `|` and compares the resulting string ordinally. Those orders differ whenever one legal `CallIdentity` is a prefix of another and the next character of the longer identity sorts before `|`. Roslyn documentation IDs for overloads have exactly this form: a parameterless `Read` is `M:Subject.Read`, while `Read(bool)` is `M:Subject.Read(System.Boolean)`. Tuple ordering places the shorter ID first, but flattened-key ordering places the parameterized ID first because `(` (U+0028) sorts before `|` (U+007C). Consequently, the second row makes `Compare(previous, key) >= 0` true and the artifact validator rejects the producer's own output.
- Concrete impact: A legal selected callable whose exact lowered body uses both a parameterless scalar helper and an overload with parameters can infer both reusable source summaries successfully, then fail compiler-manifest production at `CompilerManifestArtifactProducer.Create` line 92. Verification is aborted as a compiler-manifest failure despite valid source and valid lowered summaries. The same prefix shape can affect IL summary overloads as well.
- Safe reproduction/evidence: Use a compiler fixture such as `using SharpProof.Attributes; internal static class Subject { private static bool Read() => true; private static bool Read(bool value) => value; internal static bool Verify(bool value) { Contract.Ensures(Contract.Result<bool>() == value); return Read() == Read(value); } }`. Both helpers meet `IsSourceCandidate` (ordinary, static, scalar, exact). Their documentation IDs are the prefix pair above. A read-only ordinal probe returned `StringComparer.Ordinal.Compare("M:Subject.Read", "M:Subject.Read(System.Boolean)") == -16`, but comparing the corresponding validator keys `0|M:Subject.Read||<sha>` and `0|M:Subject.Read(System.Boolean)||<sha>` returned `84`; this directly proves producer order is rejected. No repository edits are needed.
- Novelty/distinction from live `BUGS.md`: No live entry describes summary-evidence ordering or this tuple/flattened-key disagreement. Wave 2.11 concerns delimiter-ambiguous dependency-provenance deduplication in `IrRelationalSummaryBuilder` and silently loses a row; this finding needs no key collision and instead rejects producer output in `CompilationFingerprint`. Wave 2.13 is a relative-path normalization failure for source-summary authority, not ordering. Wave 10.3 accepts multiple diagnostic orders because authority fields are omitted from its comparator; this finding has the opposite failure mode, where two different comparators disagree. Wave 12.5 concerns failure to bind summary identity to a declaration/token, not producer canonicality.

## Wave 19.37. HIGH - Generated code bypasses both cache-soundness and mutable-static-state enforcement

- Exact files/members/current lines: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `SharpProofSoundnessAnalyzer.Initialize`, lines 53-82, especially `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` at line 61 and cache/static registrations at lines 65-74. Concrete production exposure: `SharpProof.Frontend/OperationSupportCatalog.generated.cs`, `OperationSupportCatalogData.ContractExpression` lines 12-26 and `.EffectDiscovery` lines 27-93; `SharpProof.Frontend/OperationSupportProjections.generated.cs`, `GetSupported` lines 12-21; analyzer loading is confirmed by `SharpProof.Frontend/SharpProof.Frontend.csproj` lines 16-18.
- Detailed mechanism: `GeneratedCodeAnalysisFlags.None` tells Roslyn not to run this analyzer on compiler-classified generated code. Thus none of the operation actions reaching `CacheSoundnessRules.AnalyzeWrite`/`AnalyzeAssignment`, nor the field/property/event symbol actions reaching SPMETA002, run for generated declarations/bodies. This is not merely theoretical: the covered `SharpProof.Frontend` namespace currently contains generated `internal static readonly OperationKind[]` fields. Arrays are recognized as mutable by `IsMutableStorageType` at `SharpProofSoundnessAnalyzer.cs` lines 401-405, but the generated-code exclusion prevents the field action before that predicate can enforce SPMETA002. `OperationSupportProjections.GetSupported` returns those array references directly, so internal/friend code can mutate process-wide support policy.
- Concrete impact: Generated analyzer/frontend/verify code can introduce mutable process-wide state or write Unknown/timeout/failure results into a semantic cache while the error-level mechanical boundaries emit no diagnostic. In the current frontend, mutation of either exposed catalog array would change operation admission across concurrent compilations and persist for the host process.
- Safe reproduction/evidence: Analyze a source tree marked `// <auto-generated>` in namespace `SharpProof.Frontend` containing `static readonly int[] State = [1];`, or containing a `ProofCache.Write(Answer.Unknown)` call. The configured flags suppress the relevant action and yield no SPMETA002/SPMETA010. The checked-in generated arrays at lines 12 and 27 are concrete static evidence for SPMETA002: they meet the array predicate but compile because generated code is skipped.
- Duplicate distinction: Distinct from Wave 18.53, which omits the `SharpProof.Meta.Analyzers` and `SharpProof.ContractForGenerator` namespaces; this bypass applies even in the three explicitly covered namespaces. Distinct from Waves 18.52/18.56, where `IsMutableStorageType` misclassifies a type; arrays are correctly classified at lines 403-405, but the action never runs. No live `BUGS.md` entry mentions `ConfigureGeneratedCodeAnalysis` or generated-code bypass of SPMETA002/SPMETA010.

## Wave 19.39. HIGH - `default(Answer)` bypasses SPMETA010 when zero is Unknown

- Exact file/member/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `IsNonCacheableSemanticAnswer` lines 65-90, especially normalization lines 68-73 and fallback lines 88-89.
- Detailed mechanism: The switch has no `IDefaultValueOperation` arm. For a semantic enum `Answer` with `Unknown = 0`, Roslyn represents `default(Answer)` as a default-value operation of semantic type with a present constant value of zero. The fallback returns `IsSemanticAnswerType(operation.Type) && !operation.ConstantValue.HasValue`; because the constant is present, it declares the value cacheable without mapping zero to the enum member.
- Concrete impact: A direct, common initialization form writes Unknown into a semantic cache while bypassing the error-level invariant.
- Safe reproduction/evidence: `namespace SharpProof.Verify; enum Answer { Unknown = 0, Proven = 1 } ... cache.Write(default(Answer));` reaches neither the enum-field nor creation/local/property/invocation arms; the fallback sees `ConstantValue.HasValue == true` and emits no SPMETA010.
- Duplicate distinction: Wave 12.21 covers an explicit numeric conversion `(Answer)0`, whose conversion normalization erases the enum type. Here there is no conversion to peel: the unhandled `IDefaultValueOperation` retains the semantic enum type and is suppressed solely by the fallback's blanket trust in constants. Wave 12.22/15.1 concern named aliases, also absent here. No live entry mentions default values/literals in SPMETA010.

## Wave 19.40. MEDIUM - Symbol-only recursion detection falsely rejects harmless time-separated alias copies

- Exact file/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `ResolveLocal` lines 93-115, especially `resolving.Add`/immediate `return true` at lines 97-100; reaching-value flow at lines 117-181 and write extraction at lines 235-251.
- Detailed mechanism: Recursive local resolution keys the in-progress set only by `ILocalSymbol`, not by the reference/program point. A later copy can make the reaching definition of `a` be `b`, while the reaching definition of `b` at that earlier RHS is an older, already-proven value of `a`. Resolving that earlier `a` reference finds the same symbol already in the set and returns unsafe immediately, even though this is not a value cycle and every runtime value is Proven.
- Concrete impact: SPMETA010 is error severity, so harmless alias-copy refactors can break builds even though no transient/abstaining value reaches the cache.
- Safe reproduction/evidence: `var a = Answer.Proven; var b = a; a = b; cache.Write(a);`. At the write, reaching `a` is the RHS `b`; reaching `b` at that assignment is the initializer's earlier `a`; the symbol-only `resolving` set already contains `a`, so line 99 returns true and reports. Runtime `a` is Proven on every path.
- Duplicate distinction: Wave 6.29 is a false negative in helper-return alias extraction; Waves 10.18/12.23 are false negatives from omitted writes/nested callables. This is a false positive caused specifically by conflating the same symbol at different program points during local reaching-definition recursion. No live entry mentions `ResolveLocal`, cyclic aliases, or this recursion guard.

## Wave 19.41. MEDIUM - Static abstract interface properties and events are mistaken for process-wide storage

- Exact file/members/current lines: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `AnalyzeProperty` lines 366-378, `AnalyzeEvent` lines 381-392, `IsForbiddenMutableStaticStorage` lines 395-399, `IsAutoProperty` lines 432-444, and `IsFieldLikeEvent` lines 446-454.
- Detailed mechanism: The syntactic storage helpers treat a bodyless accessor-list property as an auto-property and every `EventFieldDeclarationSyntax` as a field-like event. Legal `static abstract` interface properties and events use exactly those forms but are contracts with no backing field or process-wide storage. Their static symbols therefore trigger SPMETA002 solely because they are declared in a critical namespace.
- Concrete impact: The error-level analyzer rejects valid storage-free static interface contracts, forcing suppression or API redesign despite there being no shared state to race or contaminate.
- Safe reproduction/evidence: Both `interface IState<TSelf> where TSelf : IState<TSelf> { static abstract int Value { get; set; } }` and `interface IHooks<TSelf> where TSelf : IHooks<TSelf> { static abstract event Action Changed; }` are legal storage-free declarations, but the current property and event actions report SPMETA002.

## Wave 19.42. MEDIUM - Suppressed warnings promoted to errors are reclassified as real compiler errors

- Exact files/members/current lines: `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `Create`, lines 8-93, especially diagnostic filtering at 24-28 and the diagnostic-failure callable branch at 31-45; `CreateDiagnostic`, lines 202-245, which has no suppression field. Related capture schema: `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs`, `CompilerCompilationOptionsSnapshot`, lines 48-65, which does not capture `ReportSuppressedDiagnostics`.
- Detailed mechanism: Roslyn can return suppressed compiler diagnostics when `CSharpCompilationOptions.ReportSuppressedDiagnostics` is true. A warning promoted through `SpecificDiagnosticOptions[id] = ReportDiagnostic.Error` retains effective `Severity == Error` even when `#pragma warning disable` makes `Diagnostic.IsSuppressed == true`. Producer line 25 filters only by severity and never checks `!item.IsSuppressed`; `CreateDiagnostic` then erases suppression entirely. Any such row makes `diagnosticArtifacts.Length != 0`, and lines 31-45 mark every selected callable with `CompilerErrors`, even though Roslyn/emit treats the diagnostic as suppressed. The snapshot does not record `ReportSuppressedDiagnostics`, so the artifact cannot explain or recover the distinction.
- Concrete impact: A valid compilation that emits successfully can lose all callable lowering and verification solely because its host requested visibility of suppressed diagnostics. Claims become `Unknown/CompilerErrors` rather than being verified. This affects custom Roslyn/MSBuild hosts that enable `ReportSuppressedDiagnostics` while promoting selected warnings.
- Safe reproduction/evidence: Construct options with `SpecificDiagnosticOptions["CS0612"] = ReportDiagnostic.Error` and `ReportSuppressedDiagnostics = true`, then compile `#pragma warning disable CS0612\nclass Old { [System.Obsolete] public static void M(){} } class C { void X(){ Old.M(); } }`. A read-only Roslyn 4.14 probe against the workspace SDK returned exactly `Id=CS0612, Severity=Error, IsSuppressed=True, IsWarningAsError=True`. The producer's predicate necessarily selects that row and enters its compiler-error branch, despite the pragma. Fix boundary is to exclude `IsSuppressed` diagnostics (or preserve and deliberately interpret suppression) rather than using effective severity alone.
- Closest `BUGS.md` distinction: Current live `BUGS.md` has no `IsSuppressed` or `ReportSuppressedDiagnostics` finding. Wave 18.14 and Wave 5.2 concern analyzer-configuration diagnostic suppression/early returns, not Roslyn compiler diagnostics deliberately returned as suppressed. Wave 18.43 concerns structured verifier errors masking timeouts, not compiler diagnostic suppression.

## Wave 19.43. MEDIUM - A single legal pathless syntax tree aborts compiler-manifest capture

- Exact files/members/current lines: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`, `CaptureTree`, lines 118-158, especially `Path = NormalizePath(tree.FilePath ...)` at 141-143; same file `MappedPath`, lines 161-173, which explicitly has a `<compiler-generated>` fallback for an empty `tree.FilePath`. `SharpProof.CompilerArtifact/CompilerCaptureAuthority.cs`, `NormalizePath`, lines 11-21, especially empty/whitespace rejection at 13-17. Observable failure path: `SharpProof.CompilerCollector/FinalCompilationCollector.cs`, `Collect`, lines 14-51, catch/report at 42-50.
- Detailed mechanism: `SyntaxTree.FilePath` is non-null but may legally be the empty string; `CSharpSyntaxTree.ParseText(source)` creates exactly that ordinary pathless tree, and `CSharpCompilation` accepts it. The null-coalescing throw at capture line 141 does not fire, but `NormalizePath("")` rejects it. Thus capture cannot represent even one pathless tree and the collector converts the exception to SP0049. This is internally inconsistent with `MappedPath`, which explicitly attempts to represent an empty tree path as `<compiler-generated>` but is rendered useless by the later snapshot-path assignment.
- Concrete impact: Valid in-memory Roslyn hosts (including tests, IDE tooling, or compiler integrations that do not assign source paths) cannot emit any SharpProof compiler manifest when the profile/output option activates the collector. The failure is whole-compilation, not limited to a claim located in that tree.
- Safe reproduction/evidence: `var tree = CSharpSyntaxTree.ParseText("class C {}");` yields `tree.FilePath == ""`; `CSharpCompilation.Create("A").AddSyntaxTrees(tree)` is legal. Invoke the collector with the required private output/project/TFM options: `CaptureTree` passes the empty string to `NormalizePath`, which deterministically throws `ArgumentException`; `Collect` reports SP0049 and writes no artifact. No mutation or malformed source is required.
- Closest `BUGS.md` distinction: Wave 15.3 covers two distinct legal inputs that share the same nonempty path and are later rejected by path uniqueness; this finding needs only one tree and fails immediately because the path is empty. Wave 2.13 covers relative nonempty paths being normalized on only one side of source-summary binding. Wave 18.47 concerns empty *text* plus duplicate preprocessor symbols, not an empty `FilePath`.

## Wave 19.44. HIGH - Release qualification receipts can be minted from self-asserted JSON without evidence that the qualifying gate ran

- Exact files/members/current lines: `scripts/Write-SharpProofQualificationReceipt.ps1`, top-level `$valid` gate switch, lines 56-97 (acceptance lines 57-61; portable lines 62-67; release-configuration lines 69-72; coverage lines 73-76; mutation lines 78-84; package-consumers lines 85-90). Receipt construction labels the result passed at lines 114-125. `scripts/Invoke-SharpProofReleaseContainer.ps1`, `WriteQualificationEvidence` receipt-consumption loop, lines 179-210, especially the only downstream evidence checks at 184-205. Incomplete oracle: `SharpProof.ArchitectureTest/ReleaseQualificationMatrixTests.cs`, `ReceiptWriterRejectsStaleAndPackageMismatchedMatrixRows`, lines 74-141, especially its wholly fabricated package records/evidence at 101-127 and accepted case at 131.
- Detailed mechanism: `Write-SharpProofQualificationReceipt.ps1` treats user-selected `EvidencePath` JSON as authoritative and validates only a few self-declared top-level values. For `release-configuration`, `{schemaVersion:1, commit:HEAD}` is sufficient: it checks no success/status marker, ruleset/environment/job inventory, or producer identity. Acceptance needs only self-declared `schemaVersion/status/commit`; coverage needs only self-declared `passed/commit`; mutation needs only self-declared `selection`, positive/equal counts, and commit; package/portable checks accept six arbitrary hash-shaped package rows plus self-declared status/OS/commit. Only pilots invokes a substantive evidence validator. The receipt then unconditionally writes `status = 'passed'`. The final `WriteQualificationEvidence` consumer does not dispatch any gate-specific evidence validator or inspect evidence semantics; it merely checks the receipt's status/commit, that the named evidence bytes still match the receipt hash/length, and (for five gates) that copied package metadata matches the release bundle. Hashing the claimant-supplied JSON preserves bytes, not truth or producer provenance.
- Concrete impact: A caller can create apparently valid receipts, and ultimately a `qualification.json` with `status: passed`, without running acceptance builds/tests, forced-termination regression, release-configuration inspection, coverage, mutation testing, portable consumers, or package-consumer tests. For package-bound rows the caller only needs to copy the already-visible six release artifact names/sizes/hashes. This defeats the release qualification matrix's central correctness oracle and can authorize publication from unexecuted or failed qualification gates.
- Safe reproduction/evidence: In an isolated disposable clone, resolve HEAD and write an evidence file containing only `{"schemaVersion":1,"commit":"<HEAD>"}`. Invoke `Write-SharpProofQualificationReceipt.ps1 -Gate release-configuration -EvidencePath <file>`. The exact branch at lines 69-72 returns true and the script emits a passed receipt, although no GitHub ruleset/environment check ran and the evidence has none of the producer's fields from `Test-SharpProofReleaseConfiguration.ps1` lines 292-302. Deterministic source corroboration: the existing architecture test lines 101-131 fabricates six nonexistent `package-*.nupkg` rows with one-byte sizes and synthetic digests, then asserts the portable receipt writer succeeds. Supplying the real bundle metadata instead satisfies the only additional final comparison at `Invoke-SharpProofReleaseContainer.ps1` lines 199-205.
- Closest live `BUGS.md` distinctions: Wave 2.26 is confined to unauthenticated cached mutation-shard receipts before merge, where a standalone semantic validator exists; this finding is the final cross-gate release qualification receipt issuer/consumer and affects acceptance, configuration, coverage, mutation, portable, and package-consumer qualification. Wave 18.61 shows the real release-configuration gate can miss later REST pages when it runs; this finding allows a passing release-configuration receipt when that gate did not run at all. Current live `BUGS.md` contains no qualification-receipt/self-asserted-evidence finding.

## Wave 19.46. MEDIUM - Auto-properties in `SharpProof.Ir` bypass SPMETA006's typed-identity boundary

- Exact file: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`.
- Members/current lines: `Initialize`, symbol registrations at 72-74; `AnalyzeField`, 345-363, especially the only SPMETA006 check at 347-352; `AnalyzeProperty`, 366-378, which checks only SPMETA002 mutable static state and never string IR storage.
- Detailed mechanism: The typed-identity rule is implemented only as a field-symbol action. A source auto-property stores its value in a compiler-synthesized backing field, but that synthesized field is not delivered as a source field declaration to this analyzer action; the property action has no corresponding `SharpProof.Ir`/string check. Thus changing an IR member from `readonly string identity` to `string Identity { get; }`, or adding a positional/property-backed string identity, removes the error without removing the semantic string storage.
- Concrete impact: New IR node models can persist member/type/block identity as unscoped strings and bypass the required scoped typed identifiers, reintroducing collisions or accidental cross-domain identity comparison that SPMETA006 is meant to prevent.
- Safe reproduction/evidence: Analyze `namespace SharpProof.Ir; sealed class C { internal string Identity { get; } = ""; }`. The existing built analyzer returned zero diagnostics, while the existing test fixture with `private readonly string identity` reports SPMETA006. The registration and handlers at the cited lines show no other property path to SPMETA006.
- Closest `BUGS.md` distinction: The live ledger has no SPMETA006/StringFieldInIr finding. Waves 18.52 and 18.56 concern SPMETA002's static mutability classifier; this is instance IR semantic-identity storage and a different descriptor/invariant.

## Wave 19.49. MEDIUM - Canonical container fuzz evidence records task-host-relative escape paths that are invalid once the artifact is downloaded

- Exact files/functions/current lines: `scripts/Invoke-SharpProofFuzzCampaign.ps1`: top-level output resolution, lines 25-28; `Invoke-FuzzRun`, stdout/stderr construction at lines 86-87 and evidence path projection at lines 173-178. `scripts/Resolve-SharpProofContainedPath.ps1`: `Resolve-SharpProofContainedPath`, artifact-root special handling and physical resolution/return, lines 99-122, especially lines 110-122. `eng/container/entrypoint.sh`: task-workspace construction, artifact symlink, and task-root export, lines 114-165, especially `ln -s "${repo_root}/artifacts" "${task_root}/artifacts"` at line 156 and `SHARPPROOF_REPO_ROOT=${task_root}`/`cd` at lines 164-165.
- Detailed mechanism: Every non-dev canonical container command runs in a disposable `/tmp/sharpproof-task.*` clone whose `artifacts` entry is a symlink to the host repository's `/workspace/SharpProof/artifacts`. The contained-path helper deliberately detects that redirect, resolves both the artifact root and requested output physically, and returns the physical host target at line 122. The fuzz campaign therefore builds `$standardOutput` and `$standardError` under `/workspace/SharpProof/artifacts/...`, but later calls `Path.GetRelativePath($repositoryRoot, ...)` with `$repositoryRoot` still equal to the disposable `/tmp/sharpproof-task.*` clone. The serialized paths are consequently escape paths such as `../../workspace/SharpProof/artifacts/fuzz/nightly/rotating-20260829.stdout.json`, rather than portable repository/artifact-relative paths such as `artifacts/fuzz/nightly/...`.
- Concrete impact: The canonical `fuzz-nightly` producer emits a passing `campaign.json` whose `standardOutput` and `standardError` references only resolve inside the ephemeral live container topology. After GitHub uploads `artifacts` and a reviewer downloads/extracts it elsewhere, joining either recorded path to the checkout or artifact root points outside the evidence bundle and cannot locate the cited runner JSON/stderr. This breaks the campaign's reproducibility/audit trail even without tampering; the referenced stdout file is present in the bundle but the evidence names a nonportable location.
- Safe reproduction/evidence: No fuzz run or repository mutation is required. In a disposable Linux fixture, create `/workspace/SharpProof/artifacts`, create `/tmp/sharpproof-task.fixture/artifacts` as a symlink to it, dot-source `Resolve-SharpProofContainedPath.ps1`, and resolve `/tmp/sharpproof-task.fixture/artifacts/fuzz/nightly`. The function returns `/workspace/SharpProof/artifacts/fuzz/nightly`. Then `[IO.Path]::GetRelativePath('/tmp/sharpproof-task.fixture', '/workspace/SharpProof/artifacts/fuzz/nightly/rotating-1.stdout.json')` returns an escaping `../../workspace/...` path. The same result follows directly from entrypoint line 156, resolver line 122, and campaign lines 173-178.
- Novelty/distinction from live `BUGS.md`: Distinct from Wave 18.59, which concerns replacement of stdout after snapshot/hash validation; this defect occurs in an untampered single campaign because the canonical container's physical artifact redirect is serialized relative to the disposable logical repository root. Distinct from Wave 18.58's concurrent namespace collision and Wave 2.24's missing failed-campaign summary. Wave 14.18 concerns an out-of-tree corpus license symlink escaping lexical containment; here the artifact symlink is intentional and accepted, but returning its physical target makes emitted fuzz evidence references nonportable.

# Read-Only Multi-Agent Bug Audit - Wave 20 - 2026-08-29

## Wave 20.1. MEDIUM - Delegate subtraction propagates the removed method group as the local's post-assignment value

- Exact file/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `TreeAnalysis.TryCollectLocalReferences` lines 432-460; `IsAnonymousExecutableOrEscaped` lines 500-522; `TryGetLocalDestination` lines 525-565, especially 545-553; `CanReachConsumption` lines 567-783, especially the later-consumption return at 749-753; downstream `GetNestedCallables` lines 297-367.
- Detailed mechanism: For every method reference on the right side of any `AssignmentExpressionSyntax`, `TryGetLocalDestination` treats the left local as receiving that method-reference value. It does not restrict the syntax to `SimpleAssignmentExpression`. Consequently, for `callback -= Dead`, the `Dead` method reference is modeled as the new value of `callback`. `IsAnonymousExecutableOrEscaped` starts `CanReachConsumption` after the subtraction assignment; a later `callback()` is found as a consumption, so `TryCollectLocalReferences` marks `Dead` reachable and `GetNestedCallables` analyzes its body. C# delegate subtraction does the opposite: it removes a matching invocation-list entry and never invokes or retains the right-hand target when the left delegate has no match. Thus a method group used only as a removal operand is spuriously treated as the delegate that will later execute.
- Concrete impact: A local function or lambda that is never invoked or escaped can have its body analyzed and emit false SP0027 diagnostics / a false `Refuted` semantic outcome. Warning-as-error builds can fail because of code that only compares/removes a delegate target.
- Safe reproduction/evidence: Analyze `static int Outer() { Func<int> callback = Safe; callback -= Dead; return callback(); int Safe() => 1; int Dead() => Positive(-1); }`, where `Positive(int x)` begins with `Contract.Requires(x > 0)`. Runtime `callback` contains only `Safe`; removing unequal `Dead` leaves `Safe`, and `Dead` never executes. Static trace is deterministic: the RHS method reference enters `TryGetLocalDestination`; lines 545-553 bind the subtract-assignment LHS local; lines 516-522 begin tracking after that assignment; the later invocation reaches lines 749-753; lines 453-460 add `Dead`; its body is analyzed and `Positive(-1)` can report SP0027.
- Closest live `BUGS.md` distinction: Wave 19.9 concerns missing allocation effects for the built-in delegate `+`/`-` operator in `OperationEffectScanner`; it does not concern Requires nested-callable reachability or false body execution. Wave 18.10 concerns preserving an old delegate on an exceptional path before a genuine overwrite; here no exception or genuine overwrite is needed, and the bug is that the subtraction RHS is treated as the post-assignment value. Wave 19.2 misclassifies an invocation embedded in another assignment's LHS as a kill, causing a false negative; this is a false positive from an RHS removal operand.

## Wave 20.2. MEDIUM - A method group or lambda converted directly into a discard is treated as escaped and executable

- Exact file/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `TreeAnalysis.TryCollectLocalReferences` lines 432-460; `IsAnonymousExecutableOrEscaped` lines 500-522, especially unconditional true at 504-510; `TryGetLocalDestination` lines 525-565; downstream `GetNestedCallables` lines 297-367.
- Detailed mechanism: The tracker can prove a delegate value dead only when `TryGetLocalDestination` finds an `ILocalSymbol`. A discard assignment such as `_ = (Func<int>)Dead` has no local destination (`_` binds as a discard rather than `ILocalSymbol`), so `TryGetLocalDestination` returns false. `IsAnonymousExecutableOrEscaped` then immediately returns true without examining that the value is discarded. `TryCollectLocalReferences` consequently marks the target local function reachable. The same path applies to a directly discarded lambda. Constructing/converting and discarding a delegate does not invoke its body or expose the delegate to any consumer.
- Concrete impact: Dead local-function/lambda bodies are analyzed and can produce false SP0027 diagnostics / `Refuted` outcomes, potentially failing strict or warning-as-error builds.
- Safe reproduction/evidence: Analyze `static int Outer() { _ = (Func<int>)Dead; return 0; int Dead() => Positive(-1); }`, with `Positive(int x)` requiring `x > 0`. C# creates (at most) a delegate value and immediately discards it; `Dead` is never invoked or escaped. Source trace: the RHS contains an `IMethodReferenceOperation`; lines 545-553 cannot bind the discard as `ILocalSymbol`; lines 562-564 return false; lines 504-510 classify it executable/escaped; lines 453-460 add `Dead` as reachable, enabling a false diagnostic from its body. An equivalent direct lambda `_ = (Func<int>)(() => Positive(-1));` follows the anonymous-function path at lines 333-356 and the same unconditional result.
- Closest live `BUGS.md` distinction: Wave 5.25 is the special compile-time-only `nameof` omission; this reproduction contains a real delegate conversion but a discard that neither invokes nor escapes it. Wave 13.12 starts with a delegate successfully stored in a local and later misclassifies a metadata read in `CanReachConsumption`; here no tracked local exists and the false-positive decision occurs earlier at the unconditional return in `IsAnonymousExecutableOrEscaped`. Wave 19.9 covers allocation effects of delegate operators, not execution reachability of discarded bodies.

## Wave 20.3. MEDIUM - Accessor Requires can be refuted after a definitely noncompleting prior statement

- Exact files/members/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, `Get` lines 135-207, especially `hasFlowState = flowResult?.TryGetState(operation, out _)` at 169-170 and the accessor/list-pattern `CanReplay` branch at 196-205; `HasReplayableAccessorEvaluation` lines 483-492. `SharpProof.Effects/ManagedAbstractFlow.cs`, `TransferCore` lines 223-292, especially ordinary invocation transfer at 277-281; `ManagedFlowResult.TryGetState` lines 1185-1187; contrasting reachability-aware `ManagedFlowResult.IsReachable` lines 1190-1194 and `ManagedAbstractFlow.IsBlockedAfterNoncompletingStatement` lines 1076-1100. Diagnostic sink: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, `Analysis.AnalyzeCallSite` lines 297-326, and `AnalyzeConcreteCall` / `CompleteEvaluation` lines 410-516.
- Detailed mechanism: Roslyn CFG reachability is structural and does not mark a suffix unreachable merely because a source callee is known never to complete. Managed abstract transfer likewise records an invocation and returns a havoced state at lines 277-281; it does not bottom the state when `DefiniteOperationFacts.MethodCanCompleteNormally` is false. Consequently a later operation still satisfies `TryGetState` at discovery lines 169-170. The flow type already has the missing semantic gate: `ManagedFlowResult.IsReachable` calls `IsBlockedAfterNoncompletingStatement`, which walks prior statements and rejects a suffix after a source operation that cannot complete, but discovery asks only `TryGetState` and never uses it. Ordinary invocation candidates are partly protected because their `CanReplay` route calls `HasReplayablePrefix`, which checks all prior statements. Accessor targets (property get/set and event add/remove) and implicit list-pattern member calls take the other ternary arm at lines 196-205: `HasReplayableAccessorEvaluation` checks only the receiver/current arguments and ignores the preceding statements. A zero-input/constant-false accessor Requires therefore concrete-replays and reaches `CompleteEvaluation`, which emits SP0027, although runtime can never reach the accessor.
- Concrete impact: Valid unreachable code receives a false SP0027 / `Refuted` semantic outcome. This can fail warning-as-error builds and makes property/event/list-pattern syntax behave differently from an equivalent ordinary method invocation after the same proven-nonreturning prefix.
- Safe reproduction/evidence: Analyze with the contracts profile: `using SharpProof.Attributes; sealed class Guard { public int Bad { get { Contract.Requires(false); return 0; } } } static class C { static void Stop() => throw new System.Exception(); static void M() { Stop(); _ = new Guard().Bad; } }`. Runtime never evaluates `new Guard().Bad`. Roslyn leaves the second statement structurally reachable; `TransferCore` preserves a state after `Stop`; discovery sees `hasFlowState=true`; the getter is an accessor so `HasReplayableAccessorEvaluation` succeeds; the Requires has no variables and concrete evaluation is exactly false, so lines 508-516 emit SP0027. As direct internal corroboration, calling the already-implemented `flowResult.IsReachable` for the getter would invoke lines 1076-1100, prove `Stop()` cannot complete, and return false. A corresponding implicit-list form (`Stop(); _ = value is [];` where `Length` has `Contract.Requires(false)`) follows the same special replay arm.
- Closest live `BUGS.md` distinction: Wave 19.3 also reaches accessor-specific replay, but it requires a structurally reachable yet type-impossible catch and the explicit `IsInsideExceptionHandler` exemption when there is no flow state. This finding occurs in an ordinary block, has a stale recorded flow state, and specifically arises because discovery uses `TryGetState` instead of the existing reachability-aware `IsReachable` plus bypasses `HasReplayablePrefix` for accessors. Wave 3.2 concerns `AnalyzeMemberInitializer` failing to account for a noncompleting base/this constructor chain, not ordinary callable-body suffix discovery. Wave 3.1 is the opposite failure class: executed implicit calls are omitted, producing false negatives; here a recognized accessor/implicit list member is invented as reachable and produces a false positive.

## Wave 20.4. HIGH - An early contract invocation makes later symbol-only feature selections disappear

- Exact files/members/current lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `GetAdvisoryActivation` lines 203-247, especially the immediate invocation return at 225-231; `InitializeCompilation` lines 114-149, especially symbol-action registration guarded by `activation.RequiresSymbolAnalysis` at 114-149. `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `ValidateMethodAttributes` lines 72-168 (the only ordinary-method symbol path that validates effect/closed attributes, selects bodyless methods, registers selected semicolon accessors, and emits missing-root outcomes).
- Detailed mechanism: In advisory mode, `GetAdvisoryActivation` walks trees/nodes in order. It returns `AdvisoryActivation.Full` if it encounters any non-assembly/module attribute, but if it first encounters a Contract API candidate invocation it immediately returns `(RequiresSymbolAnalysis:false, RequiresOperationAnalysis:true, RequiresFullOperationAnalysis:true)`. It never scans the remaining nodes or trees to discover later attributes. `InitializeCompilation` therefore omits all method/type/assembly symbol actions for the whole compilation. Concrete attributed bodies may still reach the operation-block path, but symbol-only cases - abstract/extern selected methods, selected concrete semicolon accessors, empty declared scopes with invalid controls, and the dedicated attribute validation performed only by `ValidateMethodAttributes` - can disappear or be misclassified solely because an unrelated Contract invocation appeared earlier in syntax-tree order.
- Concrete impact: An explicitly selected abstract/extern method can yield neither its required SP0047 `MissingOperationRoot` diagnostic nor any semantic outcome. Malformed later effect/closed/control attributes can also evade SP0024 validation. Reordering source files or declarations changes analyzer reporting and outcome accountability without changing program semantics.
- Safe reproduction/evidence: Under the default/advisory `all` configuration, analyze one source (or two syntax trees in this order): `using SharpProof.Attributes; class A { static void First(bool x) { Contract.Requires(x); } } abstract class B { [ZeroAllocations] public abstract void Selected(); }`. `DescendantNodes()` reaches `Contract.Requires` before `[ZeroAllocations]`, so lines 225-231 return operation-only activation. No symbol action is registered at lines 124-128, and abstract `Selected` has no operation block, so the SP0047 expected for an explicitly selected bodyless member is absent. Moving `B` before `A` makes the attribute win and registers the symbol path, restoring SP0047.
- Closest `BUGS.md` distinction: Wave 18.12 reports that lambda effect selections never enter the effect pipeline even when full symbol activation is registered; this finding affects ordinary methods and is caused by order-dependent early activation selection. Wave 18.13 concerns cancellation inside the same scan, not the scan returning an insufficient activation. Wave 19.18 concerns source-reference preconditions being invisible to advisory activation, not local source-order truncation.

## Wave 20.5. MEDIUM - Explicitly blank active analyzer options silently enable defaults instead of failing closed

- Exact files/members/current lines: `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs`, `FromOptions` lines 40-59 (defaults at 57-58); `GetInvalidGlobalConfigurationValues` lines 70-106; `TryGet` lines 204-225, especially whitespace rejection at 216-218. `SharpProof.Analyzer.Core/Configuration/AnalyzerConfigurationOptionRegistry.cs`, `IsAcceptedValue` lines 22-26.
- Detailed mechanism: `TryGet` treats a present `sharpproof_profile`/`sharpproof_features` alias whose value is empty or whitespace as absent. Consequently `GetInvalidGlobalConfigurationValues` never passes that value to `IsAcceptedValue` (which explicitly considers blank invalid), emits no SP0025, and `FromOptions` substitutes `advisory` and/or `all`. The same helper makes a tree-local blank assignment invisible to `GetInvalidTreeConfigurationValues`, so that invalid local attempt also receives no diagnostic.
- Concrete impact: A malformed configuration fails open: blank `sharpproof_profile` silently runs advisory analysis, and blank `sharpproof_features` silently enables all features. This contradicts the documented finite accepted-value set and SP0025's stated behavior that invalid configuration is reported and analyzed as off; users can get unexpected diagnostics/work or unknowingly run broader analysis.
- Safe reproduction/evidence: Call `AnalyzerConfiguration.FromOptions` with a provider whose `GlobalOptions` contains only `("sharpproof_features", "   ")`. `TryGet` returns false at lines 216-218, `InvalidConfigurationValues` remains empty, and line 58 selects `SharpProofFeatures.All`. Likewise blank `sharpproof_profile` becomes `Advisory`. A direct call to `AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, "   ")` returns false, demonstrating that the invalid-value policy exists but is bypassed.
- Closest `BUGS.md` distinction: Wave 5.2 is the early return that hides a retired global alias behind another global error, and Wave 18.14 is the engine early return that hides tree-local diagnostics when a global error already exists. Neither covers blank active values being converted to absence/default before any invalid value is recorded. Wave 2.23 is whitespace normalization of verifier policy in MSBuild, not analyzer configuration parsing.

## Wave 20.6. MEDIUM - Constant-true bottom-tested and explicitly conditioned for-loops are treated as normally completing

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MayCompleteNormally` lines 1961-2057, especially loop dispatch at 2051-2057; `LoopConditionIsAlwaysTrue` lines 2213-2225. Downstream: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` lines 588-609; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373.
- Detailed mechanism: `LoopConditionIsAlwaysTrue` recognizes only a top-tested `IWhileLoopOperation` whose condition is constant true and an `IForLoopOperation` whose condition is null. A `do { ... } while (true)` has `ConditionIsTop == false`, and `for (; true; )` has a non-null constant-true condition. Both therefore miss the noncompletion arm at lines 2051-2054 and fall through to the unconditional `ILoopOperation => true` at 2056 even when there is no break.
- Concrete impact: A source helper that cannot return is classified as permitting normal completion. Calls to it keep regular CFG successors, so unreachable writes, allocations, calls, and exceptions after the call enter the caller's effect summary and can produce false purity/effect/exception-contract diagnostics.
- Safe reproduction/evidence: `static void NeverDo() { do { } while (true); } static void NeverFor() { for (; true; ) { } } static void C() { NeverDo(); Global.State++; }`. Runtime never reaches the write. Static dispatch is deterministic: the do-loop fails `ConditionIsTop: true`; the for-loop fails `Condition: null`; each reaches line 2056 and makes `MethodCanCompleteNormally` true, which `CanCompleteInvocation` propagates.
- Closest live `BUGS.md` distinction: Wave 18.28 is lexical unreachable terminal code after a reachable return making a returning method falsely noncompleting; this finding is the opposite classification and is caused by incomplete recognition of constant-true loop forms. Wave 18.29 is async/iterator call-expression semantics. The live ledger contains no `LoopConditionIsAlwaysTrue`, do-while, or `for (; true ;)` completion entry.

## Wave 20.7. MEDIUM - A break whose mandatory finally cannot complete is accepted as a normal exit from an infinite loop

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MayCompleteNormally`, loop dispatch at lines 2051-2057; `LoopHasReachableBreak` lines 2228-2245. Downstream: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` lines 588-609; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373.
- Detailed mechanism: For an always-true loop, `LoopHasReachableBreak` returns true for any descendant `IBranchOperation` with `BranchKind.Break`. It checks neither whether that branch can complete nor the mandatory `finally` regions traversed while leaving the loop. Thus a reachable `break` inside a `try` counts as a normal loop exit even when its `finally` always throws or diverges, so the special noncompletion case is bypassed and the generic loop arm returns true.
- Concrete impact: A method with no normal exit is classified as returning. Callers retain impossible suffix effects, yielding false Complete summaries/contract failures and incorrect effect witnesses.
- Safe reproduction/evidence: `static void Never() { while (true) { try { break; } finally { throw new InvalidOperationException(); } } } static void C() { Never(); Global.State++; }`. The break is reached, but C# must execute the finally before completing it; the finally throws, so `Never` has no normal return and `C` cannot write. The recursive child walk finds the break and returns true without inspecting the finally's completion.
- Closest live `BUGS.md` distinction: Wave 18.26 concerns `EffectExceptionFlow.KeepEscaping` retaining an original protected exception when a finally calls a nonreturning helper; it does not classify break completion or method normal exit. Wave 18.8 concerns frontend/nested-callable CFG walkers skipping `FinallyRegions`, not `DefiniteOperationFacts`' syntactic break predicate. Wave 18.28 concerns unreachable lexical statements after return, not a reachable break overridden by mandatory finally execution.

## Wave 20.8. MEDIUM - Guaranteed assignable base-type recursive patterns skip mandatory accessor completion

- Exact files/members/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteSwitchExpression` lines 138-152; `CanCompletePatternEvaluation` lines 169-258, especially exact matched/input-type equality gate at 208-215 and property-member completion at 238-257. Consumers: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanSwitchExpression` lines 868-896, and `ScanStep` lines 971-974; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373.
- Detailed mechanism: Recursive-pattern completion is analyzed only when `recursive.MatchedType` is symbol-identical to `pattern.InputType`. For a definitely non-null value whose static sealed type derives from the pattern's base type (or implements its interface), the runtime type test is guaranteed to succeed, so its Deconstruct/property accessors are mandatory. Exact symbol inequality nevertheless returns true at lines 208-215 before any accessor completion is checked. `CanCompleteSwitchExpression` can then label the whole expression completing even when the mandatory accessor cannot return.
- Concrete impact: Unreachable suffix writes, allocations, calls, and exceptions after the switch expression remain in the effect summary, causing false effect/exception contract failures and misleading witnesses.
- Safe reproduction/evidence: `class B { public int P => throw new InvalidOperationException(); } sealed class D : B { } static void M() { _ = new D() switch { B { P: 0 } => 0, _ => 1 }; Global.State++; }`. `new D()` is non-null and necessarily satisfies the `B` type test; evaluating `P` then throws, so the fallback and write cannot run. The evaluator receives input type `D` and matched type `B`, fails exact equality at 211-213, returns true at 215, and never reaches the getter check at 238-242.
- Closest live `BUGS.md` distinction: Wave 10.10 is `SwitchExpressionFacts.GetPatternSelection` forgetting the non-null flag for a symbol-identical reference pattern and inventing `SwitchExpressionException`; this case already has a definitely non-null input, uses an assignable but non-identical base/interface matched type, and corrupts accessor/whole-expression normal completion. Wave 19.6 is the opposite interprocedural error: nullable mismatch paths are discarded and a completing pattern helper is made falsely terminal. Wave 18.27 is virtual-dispatch completion, while this reproduction uses a nonvirtual getter and fails before dispatch/body checking.

## Wave 20.9. MEDIUM - A semantically nonreturning catch filter leaves the protected exception falsely escaping

- Exact file/member/current lines: `SharpProof.Effects/EffectExceptionFlow.cs`, `ApplyCatches` lines 137-160; `CanEscape` lines 163-188; `CanUnknownEscape` lines 190-223; and `GetFilterSelection` lines 244-258. Downstream entry: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanLexicalControlEffects` lines 129-175, especially `KeepEscaping` at 162-170. Interprocedural catch reachability consumes the resulting throw set through `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetCallableExceptions` lines 2536-2583.
- Detailed mechanism: `EffectExceptionFlow.GetFilterSelection` distinguishes only absent filters and Roslyn compile-time Boolean constants. Every other filter is `Maybe`; it has no completion predicate and cannot recognize a filter expression whose source call is proven never to complete normally. For a matching catch whose filter diverges, `CanEscape` therefore leaves `canReachNext` true (and `CanUnknownEscape` does the same), retaining the original protected exception as escaping. Runtime exception search cannot continue or propagate the original exception because execution remains forever in the filter. This is not the ordinary throwing-filter rule: an exception thrown by a filter is treated as filter false and the original can continue, whereas a divergent filter never returns control to exception dispatch.
- Concrete impact: A method that only diverges while evaluating its filter receives a false concrete or Unknown `Throws` effect. Allowed-exception/`DoesNotThrow` validation can fail, and a caller-side matching catch can be classified reachable from the callee's false throw set, adding impossible handler writes, allocations, capabilities, or further throws to an otherwise precise summary.
- Safe reproduction/evidence: `static bool Spin() { while (true) { } } static void F() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) when (Spin()) { } } static int state; static void Caller() { try { F(); } catch (InvalidOperationException) { state++; } }`. At runtime `F` remains in `Spin`; the original `InvalidOperationException` never escapes and `Caller` never increments `state`. Static trace: the filter is not a Roslyn constant, so lines 253-258 return `Maybe`; the exact catch type combines `Always` with `Maybe`; lines 179-187 retain the original exception; `GetCallableExceptions(F)` exposes it to caller catch reachability.
- Closest live `BUGS.md` distinction: Wave 18.26 is the analogous failure to suppress a protected exception through a semantically noncompleting `finally`, but it is implemented in the separate ancestor/finally check at `EffectExceptionFlow.KeepEscaping` lines 87-135, using Roslyn endpoint reachability. This finding is catch-filter dispatch through `ApplyCatches`/`GetFilterSelection`; fixing the finally check does not change it. Wave 7.12 concerns `CanCompleteTry` inventing a normal suffix path for a literal-false filter, not the escaping throw set or a filter that never returns. Wave 7.3 concerns syntactically unreachable bare rethrows inside handlers.

## Wave 20.10. HIGH - Shared source-completion cycle guard can classify an ordinary returning helper as nonreturning under concurrent analysis

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, compilation-wide `Sessions` cache and `_completionFacts` lines 31-39; constructor and `ForCompilation` lines 60-69; `ManagedAbstractFlow.IsBlockedAfterNoncompletingStatement` lines 1076-1099; `ManagedFlowResult.IsReachable` lines 1190-1194; `DefiniteOperationFacts._activeMethods` lines 1815-1819; `DefiniteOperationFacts.MethodCanCompleteNormally` lines 1918-1950, especially the `Add`/false return at 1927-1929 and removal at 1948-1950. Concurrency/consumption evidence: `SharpProof.Analyzer/SharpProofAnalyzer.cs`, `Initialize` lines 25-32, especially `EnableConcurrentExecution()` at 28; `SharpProof.Effects/OperationEffectScanner.cs`, `IsReachable` lines 1251-1276; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373.
- Detailed mechanism: `ManagedAbstractFlow.ForCompilation` deliberately returns one shared instance per `Compilation`, and that instance owns one `DefiniteOperationFacts`. Its recursion guard is a mutable, unsynchronized `HashSet<IMethodSymbol>`. During concurrent method analysis, two callers can ask whether suffix operations after a call to the same nonrecursive source helper are reachable. The first thread adds the helper while walking its body. The second thread's `_activeMethods.Add(normalized)` can then return false, so line 1929 reports semantic noncompletion even though this is cross-thread overlap, not recursive reentry. `IsBlockedAfterNoncompletingStatement` consumes that false result and `ManagedFlowResult.IsReachable` marks the caller suffix blocked. Concurrent mutation of `HashSet<T>` is itself unsupported and can also corrupt or throw. The file's `[ThreadStatic]` treatment of `_walkDepth` at lines 26-29 shows the surrounding object is expected to be used by concurrent analysis, but the source-method guard has no equivalent isolation.
- Concrete impact: Results become scheduling-dependent. A real suffix write, allocation, call, or throw after an ordinary returning helper can be omitted from one caller's complete effect summary, permitting false no-write/purity/no-allocation/no-throw conclusions; alternatively the analyzer can fail from the data race.
- Safe reproduction/evidence: Compile one large but definitely returning source helper `Returns()` and two callers `A(){ Returns(); StateA++; }` and `B(){ Returns(); StateB++; }`. Analyze the two callers concurrently against the same `Compilation`/`ManagedAbstractFlow` and query reachability of both increments repeatedly (a long helper-call chain widens the overlap window). While one `MethodCanCompleteNormally(Returns)` call owns the set entry, the other follows the exact 1927-1929 false branch and its increment becomes unreachable. A deterministic unit-level proof can pause the first call after guard insertion (or preseed the shared guard via a test hook), then query the second caller before releasing it. No external state or repository mutation is required.
- Closest live `BUGS.md` distinction: Wave 11.4 covers actual same-thread recursive reentry being interpreted as noncompletion. This finding requires no recursion: independent callers of an ordinary returning helper interfere because a compilation-shared cycle guard is being used as cross-thread state. Wave 15.20 concerns dropped cancellation in completion walks, not shared mutable state or scheduling-dependent semantic answers. The live ledger has no `DefiniteOperationFacts` concurrency/thread-safety entry.

## Wave 20.11. HIGH - Ref-conditional arguments evade ref/out havoc and preserve stale nonnull facts

- Exact file/member/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `TransferCore` invocation arm lines 277-281; `HavocCall` lines 834-840; `HavocArguments` lines 842-855; `TryStorage` lines 863-874. Downstream proof use: `ManagedFlowResult.ProvesNonNull` lines 1268-1270; `SharpProof.Effects/OperationNullnessEvaluator.cs`, `IsProvenNonNull` lines 98-104; `SharpProof.Effects/OperationEffectScanner.cs`, `PotentialNullReceiver` lines 976-983.
- Detailed mechanism: After an ordinary call, `HavocArguments` invalidates a ref/out argument only when `TryStorage(argument.Value)` directly recognizes an `IParameterReferenceOperation`, `ILocalReferenceOperation`, or `IFlowCaptureReferenceOperation`. A legal ref conditional such as `ref (choose ? ref x : ref y)` is represented by an `IConditionalOperation` whose two arms are writable storages. `TryStorage` rejects the composite operation, so neither possible target is invalidated. The preceding recursive transfer merely reads the condition and arms; it does not change either local. Both pre-call abstract values therefore survive after a callee may write the selected storage.
- Concrete impact: Stale scalar, cardinality, and nullness facts can suppress real post-call faults and effects. In particular, a callee that stores null through the selected ref leaves both locals proven nonnull, so subsequent dereferences contribute no `NullReferenceException`; a no-throw result can be accepted even though every runtime path throws.
- Safe reproduction/evidence: Use `static void Clear(ref string? value) => value = null; static int Calls(bool choose) { string? x = "x"; string? y = "y"; Clear(ref (choose ? ref x : ref y)); return x.Length + y.Length; }`. C# writes null to exactly one local, so `Calls` throws for either Boolean value (at `x.Length` when true, otherwise at `y.Length`). Managed flow initializes both locals as `NonNull`; at `Clear`, `argument.Value` is a ref `IConditionalOperation`, `TryStorage` returns false, and both values remain `NonNull`. `ProvesNonNull` then succeeds for each dereference and `PotentialNullReceiver` returns empty. A Roslyn-operation regression test can additionally assert the argument shape and inspect the two post-call values.
- Closest live `BUGS.md` distinction: Wave 7.11 is failure to havoc captured locals that a by-value delegate argument may mutate; this case is an explicit ref/out write whose argument is already known to designate one of two local storages, but the composite lvalue is not expanded. Wave 7.26 is the separate source-initial-null textual fallback after indirect mutation; this reproduction starts nonnull and the wrong answer comes from managed abstract flow's post-call state. Wave 7.7 concerns effect-region scanning of ref-local pointee writes, not ref/out state invalidation for a conditional reference. No live entry covers ref-conditional arguments in `HavocArguments`/`TryStorage`.

## Wave 20.12. HIGH - Exact metadata call specifications omit mandatory type-initializer effects

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep` lines 609-668, especially external/source call handoff at 655-667; `ScanObjectCreation` lines 706-742. `SharpProof.Effects/EffectCallSiteResolver.cs`, `Resolve` lines 39-67; `ResolveConstruction` lines 89-149; `HasExplicitSourceTypeInitialization` lines 151-160. `SharpProof.Effects/EffectAnalysisSession.cs`, `ResolveCall` lines 146-209, especially the metadata branch at 201-208. `SharpProof.Effects/ExternalEffectResolver.cs`, `Resolve` lines 33-48, especially exact API-spec return at 42-45. Corroborating intended type-initialization treatment: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `HasPotentialStaticInitialization` lines 170-198, especially metadata types returning true at 185-187; `SharpProof.Effects/ExceptionHandlerReachability.cs`, `AddStaticInitializationPotential` lines 1599-1630.
- Detailed mechanism: After receiver/argument evaluation, `ScanCallStep` adds only the summary returned by `EffectCallSiteResolver`. For a metadata target, `EffectAnalysisSession.ResolveCall` joins `DirectCall` with the remapped `ExternalEffectResolver.Resolve` result. An exact custom API-spec row can therefore contribute a Complete, effect-free, DoesNotThrow summary. No layer joins a type-initialization boundary for a static metadata method/accessor, even though an explicit metadata `.cctor` must run before the first static call. Construction has a nominal type-initialization hook, but `HasExplicitSourceTypeInitialization` explicitly requires the constructor's assembly to equal the source compilation, so exact metadata constructor specs bypass the same boundary. This is not merely an inability to inspect metadata: `HasPotentialStaticInitialization` deliberately treats every metadata type as potentially initialized, and `ExceptionHandlerReachability.AddStaticInitializationPotential` separately adds possible `TypeInitializationException`; that exception-reachability bookkeeping does not add the initializer's effects or escaping exception to the scanner summary.
- Concrete impact: A caller can receive a Complete summary that omits static writes, allocation, capabilities, divergence, and `TypeInitializationException` performed by the target type initializer. This can falsely establish purity, zero-write/zero-allocation, and `DoesNotThrow` claims. Constructor calls still record the object allocation itself, but can omit all `.cctor` effects and failure.
- Safe reproduction/evidence: Emit a referenced assembly containing `public static class External { public static object? State; static External() { State = new object(); throw new InvalidOperationException(); } public static int Clean() => 0; }`. Supply a matching approved custom `ApiSpecTable` row for `External.Clean` with `Effects=None`, `Allocation=None`, `DoesNotThrow`, and `Terminates`, then analyze `static int M() => External.Clean();`. On first runtime use, the explicit `.cctor` allocates, writes `State`, and the call throws `TypeInitializationException` before `Clean` runs. Static control flow shows analysis instead takes `ResolveCall` lines 201-208 -> exact spec lines 42-45 and never invokes any initialization boundary. A parallel exact constructor row demonstrates the construction branch's source-only gate at `EffectCallSiteResolver.cs:151-160`.
- Closest live `BUGS.md` distinction: Wave 18.15 concerns a same-compilation `beforefieldinit` static method whose compiler-generated source `.cctor` is omitted by `EffectMethodNodeBuilder`; this finding is the separate metadata/exact-spec path, where no method node exists and even an explicit `.cctor` is bypassed. Wave 12.11 concerns a definitely diverging explicit source `.cctor` at its own source method entry. Waves 3.13 and 9.19 concern source static-field access resolution, not metadata static calls or constructors. Wave 18.23 concerns source exception construction in exception reachability, not complete external summaries.

## Wave 20.13. MEDIUM - Constructed generic string downcasts that become identity conversions still force the entire contract to abstain

- Exact files/members/current lines: `SharpProof.Frontend/RoslynOperationLowerer.cs`, `GetTypeId` lines 78-110; `IsSupportedValueDomain` lines 112-116; `LoweringVisitor.VisitParameterReference` lines 507-515; `LoweringVisitor.VisitConversion` lines 813-863, especially the unspecialized symbol/special-type tests at 829-841 and the reference-kind gate at 853-858. Downstream binding path: `SharpProof.Contracts/ContractBinder.cs`, `BindCore` lines 108-116 (type-specializer installation at 112); `SharpProof.Contracts/ContractExpressionBinder.cs`, `BindWithFrontend` lines 98-104.
- Detailed mechanism: Contract binding deliberately installs `ContractCanonicalization.CreateTypeSpecializer(source)` so operations from an open generic declaration are lowered in the constructed target's value domains. For a legal clause in `Read<T>` such as `(string)value != null`, binding the constructed method `Read<string>` makes `VisitParameterReference` specialize `T` to `string`, pass value-domain admission, and create an `IrTypeKind.String` operand term. `VisitConversion`, however, does not apply `TypeSpecializer` to its semantic tests. `SymbolEqualityComparer.Default.Equals(operation.Operand.Type, operation.Type)` compares the original `T` with `string` and is false. The same-IR-type fast path is restricted to `IsValuePreservingIntegerConversion`, whose unspecialized special types are `None` and `System_String`, so it is also false. Finally, the only exact reference-cast route requires the already-specialized operand term to have `IrTypeKind.Reference`; an exact string term has `IrTypeKind.String`, so the conversion falls to `ConversionMayChangeValue`. The constructed conversion is in fact an identity conversion for `T=string`, but specialization is used for term creation and ignored for conversion classification.
- Concrete impact: One otherwise supported Requires/Ensures clause makes `ContractExpressionBinder.BindWithFrontend` return unsupported and the caller-facing contract binding fail as `UnsupportedExpression`. Preconditions on constructed generic methods can therefore disappear from enforcement/diagnostics. For example, a call `Read<string>(null!)` can avoid the expected violated-precondition result solely because the valid `(string)value != null` clause was declared on the open generic method.
- Safe reproduction/evidence: Using the existing constructed-contract test pattern, compile `public static T Read<T>(T value) where T : class { Contract.Requires((string)value != null); return value; }` plus a caller invoking `Read<string>(null!)`. Obtain the constructed `IMethodSymbol` from the invocation and call `new ContractBinder(compilation, new IrFactory()).Bind(target)`. Static tracing gives: type specialization maps the parameter reference to `System_String`; its term type is `factory.StringType`; the source/target symbol comparison is `T` versus `string`; integer-preserving conversion is false; `GetTypeInfo(factory.StringType).Kind` is `String`, not `Reference`; the result is `ClosedAbstention/ConversionMayChangeValue`, and binding fails `UnsupportedExpression`. A direct control comparison is the nongeneric `string` parameter version, where the source/target symbol equality at lines 830-834 returns the exact operand.
- Closest live `BUGS.md` distinction: Wave 11.8 is a different false abstention caused by stripping compiler-inserted common reference conversions from ordinary `object == string` / `Base == Derived` equality and then producing mismatched IR types. Here the operands' specialized IR types already match, and the bug is that `VisitConversion` compares unspecialized Roslyn types and then excludes the dedicated string IR kind from its cast fast path. Wave 7.15 concerns downstream abstract Requires evaluation unsoundly assuming an unrelated string cast succeeds; this finding occurs earlier and is the opposite direction: a cast proven to be identity by the constructed generic substitution is rejected, so no bound clause reaches evaluation. Wave 6.19 concerns semantic-identity aliasing between `as` and throwing casts in abstained opaque terms; this finding does not rely on aliasing or `as` and affects the exactness decision for a constructed identity conversion.

## Wave 20.14. HIGH - Reachable catch handlers are omitted while program lowering remains Exact

- Exact file/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LoweringSession.SelectBlocks` lines 552-580, especially successor-only traversal at 560-574; `LoweringSession.LowerTerminator` lines 374-430, especially ordinary return handling at 383-388; exactness decision in `LoweringSession.Lower` lines 80-101, especially 94-100.
- Detailed mechanism: `SelectBlocks` discovers blocks exclusively by following each block's `FallThroughSuccessor.Destination` and `ConditionalSuccessor.Destination`. Roslyn does not expose exception dispatch from a potentially throwing operation to a catch as one of those two ordinary successors; the catch is represented by enclosing CFG regions/handler topology and can be `BasicBlock.IsReachable == true` without being reachable through this successor walk. Therefore a catch block is never selected or lowered. If the try path itself uses supported operations and ordinary return edges, no abstention is recorded, so lines 94-100 publish the truncated program as `Exact`. `LowerTerminator` then returns directly from the try value and has no representation of the catch's normal return.
- Concrete impact: A caught exceptional execution, which is a normally returning execution of the C# method, disappears from the exact IR. This can make compiler postcondition verification unsound. For example, `static long Target(long x) { Contract.Ensures(Contract.Result<long>() != 42L); try { return 10L / x; } catch (DivideByZeroException) { return 42L; } }` violates its postcondition at `x == 0`. The lowered exact program contains only the `10L / x` return. Downstream relational-summary construction constrains that return to successful/defined division, so its normal relation covers only `x != 0`; the catch return 42 is absent and the false postcondition can be proven over the truncated normal-return set.
- Safe reproduction/evidence: A read-only Roslyn 4.14 in-memory probe for `static long M(long x) { try { return 10L / x; } catch (System.DivideByZeroException) { return 42L; } }` produced: B0 Entry `Regular -> B1`; B1 `BranchValue=Binary`, `Return -> B3`; B2 (catch) `IsReachable=True`, `BranchValue=Literal`, `Return -> B3`; B3 Exit. There is no ordinary successor edge into B2. Running the current built `RoslynProgramLowerer` on that CFG returned `exact=True`, 3 IR blocks (B0/B1/B3), no abstentions, and instruction shapes `Goto`; `Return`; `Return`; the catch B2 and literal 42 were absent. This is also established statically by lines 560-574, which have no region/handler traversal.
- Closest live `BUGS.md` distinction: Wave 18.8 covers normal branches that must execute `ControlFlowBranch.FinallyRegions` but jump directly past those finally blocks. This finding is the distinct exception-dispatch topology for catch handlers: no normal successor enters the handler, and a caught fault becomes an omitted normal return while lowering can remain Exact. Wave 13.20 is the opposite reachability error (including dead blocks and gaining abstentions), not omission of a reachable catch and exact semantic corruption.

## Wave 20.15. MEDIUM - Exceptional terminators discard the executable throw operand and its state changes

- Exact file/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LoweringSession.LowerTerminator` lines 374-430, especially exceptional-edge handling at 390-395; `LoweringSession.IsExceptional` lines 543-550; `LoweringSession.SelectBlocks` lines 552-575, especially the early stop at 560-563.
- Detailed mechanism: Roslyn stores the operand of `throw expression` in `BasicBlock.BranchValue` and marks the fall-through branch `ControlFlowBranchSemantics.Throw`. `LowerTerminator` detects that semantic at lines 390-395 but never calls `LowerValue` on `source.BranchValue`; it merely records `UnsupportedControlFlow`, emits memory-only havoc, and emits an IR return. Thus evaluation of the throw operand is absent. A root invocation in the operand emits no `IrCallInstruction`; ref/out local changes receive no variable havoc; nested exceptions and evaluation order are lost. The memory-only havoc cannot conservatively represent local/ref mutations, and converting the throw to a normal return also changes terminal outcome.
- Concrete impact: The public closed-abstention program is not a conservative trace of the source: it can omit calls and local mutations that definitely execute before the throw, and it exposes a normal return for an execution that never returns. Compiler body admission currently rejects the abstention, limiting direct accepted-proof exposure, but frontend/replay/diagnostic consumers retaining the supplied program observe incorrect instructions and state.
- Safe reproduction/evidence: Use `static Exception Make(ref long x) { x++; return new Exception(); } static long Target(long x) { throw Make(ref x); }`. A read-only Roslyn 4.14 probe produced B1 with `BranchValue=Invocation`, `ConditionKind=None`, and `FallThroughSuccessor.Semantics=Throw`. Running the current built lowerer returned `exact=False`, abstention `UnsupportedControlFlow`, and IR shapes `Goto`; `Havoc,Return`, with zero call instructions and no variable havoc for `x`. Static source trace is exact: lines 390-395 contain no operand lowering, while normal return and condition paths do call `LowerReturn`/`LowerValue` at 385-388 and 398-405.
- Closest live `BUGS.md` distinction: Wave 3.3 concerns a call nested under a larger expression being delegated to the term-only expression lowerer. Here the invocation is the root CFG `BranchValue` and is skipped because the exceptional terminator never evaluates any throw operand. Wave 19.17 concerns increment/assignment used as an ordinary value and stale state after expression lowering; this finding is the exceptional-edge path and also loses root calls/ref-out havoc before a throw. No live entry records exceptional `BranchValue` omission in `LowerTerminator`.

## Wave 20.16. LOW - SMT encoding eagerly rejects unsupported terms in semantically unreachable branches

- Exact files/members/current lines: `SharpProof.Smt/IrSmtBackend.cs`, `QueryEncoder.Encode` lines 429-451 (unsupported fallback at 448), `QueryEncoder.EncodeBinary` lines 467-493 (both operands eagerly encoded at 469-470 before short-circuit handling at 471-492), and `QueryEncoder.EncodeConditional` lines 530-550 (both arms eagerly encoded at 533-534 before `MkITE`); public failure projection in `IrSmtBackend.CheckAsync` lines 62-65. Semantic contrast: `SharpProof.Ir/IrInterpreter.cs`, `EvaluateBinary` lines 251-295 (short-circuit return at 259-273 before RHS evaluation) and `EvaluateConditional` lines 375-389 (only the selected arm is evaluated at 388).
- Detailed mechanism: The encoder recursively calls `Encode` on the RHS of every binary operation before it checks for `AndAlso`/`OrElse`, and it recursively encodes both conditional arms before constructing the ITE. Consequently, an unsupported IR node in an operand or arm that the controlling constant makes unreachable still throws `UnsupportedIrEncodingException`. `CheckAsync` converts that to `Unknown(UnsupportedEncoding)`. This disagrees with the IR execution semantics: the interpreter never evaluates the dead RHS/arm, so the enclosing Boolean term is total and has a definite value. The existing definedness formulas correctly preserve short-circuiting only after both subtrees have already survived syntactic encoding; they cannot rescue an unsupported dead subtree.
- Concrete impact: A valid, semantically total public verification query can be neither proved nor refuted solely because unreachable syntax is outside the SMT vocabulary. For example, `true || unsupported` and `true ? true : unsupported` are definite true goals but return `Unknown/UnsupportedEncoding`, reducing proof coverage and making harmless dead-code/specification refactors change verification outcomes.
- Safe reproduction/evidence: Create an `IrFactory`; create a static Boolean member with `GetOrCreateMember`; create `unsupported = factory.PureOpaque(member, receiver: null)`; use it as the RHS of `factory.Binary(IrBinaryOperator.OrElse, factory.Boolean(true), unsupported)` (or the false arm of `factory.Conditional(factory.Boolean(true), factory.Boolean(true), unsupported)`); construct a `VerificationQuery` with no assumptions and that goal; call `new ProofKernel(new IrSmtBackend()).VerifyAsync(query)`. Static control flow is deterministic: `EncodeBinary` line 470 or `EncodeConditional` line 534 reaches `Encode` line 448 and the result is `UnknownOutcome(UnsupportedEncoding)`, while `IrInterpreter` returns Boolean true at line 271 or evaluates only the true arm at line 388.
- Closest live `BUGS.md` distinction: Wave 3.21 concerns the separate `ApiSpecTermValidator` rejecting statically unreachable partial but otherwise supported spec terms before SMT query construction. This finding is in the public SMT encoder itself and is triggered by an unsupported dead IR node in an already-constructed query. Wave 13.7 concerns cancellation and resource-budget bypass while eagerly encoding a wide formula; it remains even when every term is supported and does not identify the incorrect `UnsupportedEncoding` abstention caused by dead syntax. Wave 14.27 is only native `Sort` wrapper leakage in supported conditional encoding.

## Wave 20.17. HIGH - Built-in string compound assignment omits concatenation allocation, implicit formatting calls, and their terminal behavior

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanCompoundAssignment` lines 79-100, especially effect construction at 85-98. Contrast `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanBinary` lines 338-368, especially `StringConcatenationEffectResolver.Resolve` at 352-358. Supporting intended logic: `SharpProof.Effects/StringConcatenationEffectResolver.cs`, `Resolve` lines 11-48, especially managed allocation at 25-26 and formatted-operand resolution at 32-47; `ResolveFormattedValue` lines 50-82. Completion consequence: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteCompoundValue` lines 867-888, especially the `OperatorMethod == null` success at 883-887. Catch-reachability consequence: `SharpProof.Effects/ExceptionHandlerReachability.cs`, compound branch lines 449-490, especially operator-only handling at 455-475.
- Detailed mechanism: C# built-in `string += value` performs the same built-in concatenation as `string + value`, including a possible managed string allocation and, for a non-string/non-null operand, an implicit formatting/`ToString` call. Roslyn exposes this as `ICompoundAssignmentOperation` with `OperatorKind.Add`, string result/target type, and no user `OperatorMethod`; the implicit formatting call is not a child invocation. `ScanBinary` explicitly invokes `StringConcatenationEffectResolver`, but `ScanCompoundAssignment` joins only user-operator effects, integral division exceptions, and checked overflow. All three are empty for built-in string `+=`. It then stores to the target without adding allocation or resolving formatting. `CanCompleteCompoundValue` likewise returns true solely because `OperatorMethod` is null, even if the omitted `ToString` method is proven nonreturning. The exception-reachability compound branch has the same operator/intrinsic-only shape, so a catch matching the omitted formatting exception can be classified unreachable.
- Concrete impact: A supported method using a local string target can receive a complete empty/no-allocation/no-throw summary even though runtime allocates and calls effectful user code. This can unsoundly satisfy `ZeroAllocations`, purity/no-write, or allowed-exception/`DoesNotThrow` contracts. A throwing `ToString` can also leave impossible suffix effects in the summary, while a matching catch's real effects may be omitted.
- Safe reproduction/evidence: Allocation-only: `static string Join(string left, string right) { left += right; return left; }`. Ordinary parameter/local reads and the local write contribute no observable regions. At lines 85-98 every compound-operation helper is empty, so the scanner records no allocation, although nontrivial runtime inputs produce a new string. Hidden call/throw: `sealed class V { public override string ToString() { Global.State++; throw new ApplicationException(); } } static void M(V value) { string text = ""; text += value; Global.After++; }`. Runtime calls `V.ToString`, writes `Global.State`, throws, and never reaches `Global.After`. Current compound scanning never resolves `V.ToString`; completion returns true at lines 883-887, so it can retain `Global.After`. Wrapping the assignment in `try/catch (ApplicationException)` also exercises the compound reachability branch, which sees no operator method and no checked/division intrinsic. Admission is not a fail-closed escape hatch: `SharpProof.Analyzer.Core/LanguageSubsetGate.cs`, `SupportsOperationShape` lines 182-183 explicitly accepts compound assignments whose `OperatorMethod` is null, which is exactly this built-in form.
- Closest live `BUGS.md` distinction: Wave 7.23 concerns the resolver selecting the wrong formatting method when binary/interpolation paths actually invoke it; this compound path never calls the resolver. Wave 12.28 covers exception reachability for `IBinaryOperation` concatenation and interpolation, not `ICompoundAssignmentOperation`. Wave 19.9 covers missing allocation for built-in delegate binary `+`/`-`, not string `+=` or omitted implicit formatting. The entry near current line 2790 concerns the separate meta-analyzer's semantic-string fragment inspection, not effect discovery, allocation, throws, completion, or catch reachability. Wave 11.6 concerns metadata-carried user-defined compound conversions, while this is a built-in null-`OperatorMethod` concatenation.

## Wave 20.18. MEDIUM - Simple assignment through a nonreturning ref-return invocation is relabeled completing, retaining impossible suffix effects

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanSimpleAssignment` lines 36-50; `ScanWriteTargetEvaluation` lines 53-76, especially default target scanning at 75. `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteNormally` simple-assignment arm lines 93-95; `CanCompleteWriteTarget` lines 740-759, especially default `true` at 758. Downstream: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanStep` lines 971-974; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373, especially successor retention at 367-373.
- Detailed mechanism: A ref-return invocation is a legal lvalue target of `ISimpleAssignmentOperation`. `ScanSimpleAssignment` correctly sends an unrecognized target shape through `ScanWriteTargetEvaluation`'s default arm, so the invocation is scanned and the method's noncompletion is observed internally; if it cannot complete, the scanner returns only that target summary before the RHS/store. However, the enclosing `ScanStep` does not preserve that internal terminal state. It reconstructs completion by calling `OperationCompletionEvaluator.CanCompleteNormally` on the whole simple assignment. That arm checks `CanCompleteWriteTarget(target)` plus RHS completion. `CanCompleteWriteTarget` knows fields, arrays, properties, locals, parameters, and discards, but a ref-return `IInvocationOperation` falls to unconditional `true`. Thus a definitely nonreturning target invocation is relabeled as a completing assignment, and block scanning/CFG traversal proceeds into code that runtime can never reach.
- Concrete impact: Complete effect summaries can contain impossible suffix writes, allocations, calls, or exceptions after a ref-return target that is proven to diverge or always throw. This causes false purity/effect/exception contract failures and misleading direct evidence. The target invocation's own effects are scanned, so this is specifically a terminality/successor error, not merely an unsupported-write-region issue.
- Safe reproduction/evidence: `static int cell; static int state; static ref int Never() { while (true) { } } static void M() { Never() = 1; state++; }`. C# must finish `Never()` to obtain the managed reference before evaluating/storing the RHS; it never does, so `state++` is unreachable. Static trace: `ScanWriteTargetEvaluation` reaches `ScanStep(Never())`, whose invocation completion is false, and `ScanSimpleAssignment` returns at lines 40-42. The outer `ScanStep` then asks completion for the assignment; the target invocation reaches `CanCompleteWriteTarget` default true at line 758, RHS literal completes, and the assignment is labeled completing. `ScanSequence`/`AnalyzeControlFlowGraph` therefore retain and scan the `state++` suffix. `MayDiverge` is a complete termination effect, so this path need not add `Unsupported`. The source shape is admitted: `LanguageSubsetGate.SupportsCall` lines 211-236 does not reject `ReturnsByRef`, and `SimpleAssignment`/`Invocation` are supported operation kinds.
- Closest live `BUGS.md` distinction: Wave 3.6 is in `SharpProof.Frontend/RoslynProgramLowerer` and says ref-return target evaluation is omitted before the RHS; here the Effects scanner does evaluate/scan the target and the defect is the separate completion evaluator relabeling the already-observed terminal assignment as completing. Wave 7.7 concerns erased pointee effects for ref-local targets, and Wave 7.8 concerns `IsRef` rebinding of ref parameters; neither concerns a ref-return invocation lvalue or CFG successor retention. Wave 18.30 covers missing outer completion cases for array initializers/interpolated strings; simple assignment has an explicit completion arm, but its write-target classifier omits this legal lvalue kind.

## Wave 20.19. MEDIUM - Invalid structured diagnostics are reinterpreted as legacy diagnostics from JSON string contents

- Exact files/members/current lines: `SharpProof.Host/VerifierDiagnosticTransport.cs`, `VerifierDiagnosticTransport.TryDeserialize` lines 33-86 (prefix recognition at 38-41; validation failures return false at 61-67 or through the catch at 81-86). `SharpProof.BuildTasks/RunVerifier.cs`, `RunVerifier.LogStandardError` lines 1001-1051 (unconditional legacy fallback at 1013-1015 after structured parsing fails); `TryParseLegacyDiagnostic` lines 1054-1098 (marker search over the entire line at 1067-1096); `TryParseLocation` lines 1101-1143.
- Detailed mechanism: A line beginning with the reserved `##sharpproof-diagnostic-v1##` prefix is recognized as structured transport, but `TryDeserialize` communicates every invalid schema, field, or allowlist result only as `false`. `LogStandardError` then feeds that same reserved-prefix line to the legacy free-text parser. The legacy parser searches the whole line for `: error SP0047: ` / `: warning SP0048: ` markers and accepts any preceding text whose suffix looks like `(line,column)`. Therefore marker text embedded inside a JSON string (notably `file` or `message`) in an otherwise invalid structured envelope is promoted into a real SP0047/SP0048 diagnostic. The rejected envelope is not kept opaque and the structured transport has an unintended downgrade path.
- Concrete impact: Malformed, future-schema, or otherwise disallowed verifier output can fabricate a warning/error and arbitrary coordinates instead of being treated as malformed output. A fabricated error also sets `HasStructuredError` at `RunVerifier.cs` lines 1024-1027, so it can participate in the already-recorded coarse exit-error suppression behavior and obscure the actual verifier failure. Even without that downstream interaction, this can turn invalid output into a false build failure or misleading source diagnostic.
- Safe reproduction/evidence: Pass this single line to `RunVerifier.LogStandardError`: `##sharpproof-diagnostic-v1##{"schema":1,"severity":"warning","code":"SP9999","file":"victim.cs(3,4): error SP0047: injected","line":1,"column":1,"message":"ignored"}`. `TryDeserialize` builds the record, `Validate` rejects `SP9999`, and the catch returns false. Legacy fallback finds the embedded `: error SP0047: `; the prefix before it ends in `victim.cs(3,4)`, so `TryParseLocation` accepts line 3/column 4 and `LogStandardError` logs an SP0047 error (with the JSON prefix included in the parsed file text) and sets `HasStructuredError=true`. This is a deterministic control-flow reproduction and requires no filesystem mutation.
- Closest `BUGS.md` distinction: Wave 6.26 covers `FormatException` escaping `TryDeserialize` for malformed numeric JSON; this finding uses a normally caught validation failure and does not throw. Wave 18.43 covers the later coarse `HasStructuredError` suppression of unrelated exit failures after a genuine parsed error; this finding is the earlier parser-confusion mechanism that manufactures such an error by downgrading a reserved-prefix structured line into legacy text. No live entry records structured-to-legacy fallback or marker injection from JSON string contents.

## Wave 20.20. HIGH - Worker/cache identity omits the shared .NET framework and execution engine

- Exact files/members/current lines: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`, `CompilerArtifactInputHash.Compute` lines 10-35, especially incomplete `workerBinarySha256` inclusion at 27-34; `WorkerBinaryIdentity.CreateSnapshot` lines 48-105; `WorkerBinaryIdentity.RuntimeComponents` lines 193-244, especially the app-directory component seed/extraction at 197-225 and app-directory path construction at 239-241. Downstream identity use: `SharpProof.Worker/WorkerInputSnapshot.cs`, `WorkerCacheIdentity.Current` lines 61-68 and `LoadAsync` lines 44-46. Project configuration corroboration: `SharpProof.Worker/SharpProof.Worker.csproj` line 4 (`net9.0`) with no `SelfContained` or `RuntimeFrameworkVersion` pin.
- Detailed mechanism: `WorkerBinaryIdentity` calls its product a runtime-closure snapshot, but it hashes and stages only the worker DLL, its `.deps.json`/`.runtimeconfig.json`, and DLL filenames resolved beneath the worker's own directory. A framework-dependent `net9.0` process is actually executed by the `dotnet` host/CoreCLR and loads `System.Private.CoreLib` and the rest of `Microsoft.NETCore.App` from the host's shared-framework directory; none of those bytes, the host, or the selected framework patch version is included. The generated runtimeconfig names a framework/TFM and normal framework-dependent resolution can roll to an installed patch. `WorkerCacheIdentity.Current.WorkerBinarySha256` is therefore identical across different shared-framework/CoreCLR installations, and `CompilerArtifactInputHash.Compute` seals that incomplete digest as the semantic cache/tool identity.
- Concrete impact: The same `InputHash`, cache pathname, and reported `WorkerBinarySha256` can identify verifier executions performed by different managed runtimes. A project-local cache can survive a container/runtime servicing update and cross that semantic TCB change without invalidation; more fundamentally, response provenance claims the same worker binary identity although different JIT, core-library, JSON, task/cancellation, and arithmetic/runtime code executed the verifier and cache replay. A runtime regression/fix capable of changing verifier behavior is invisible to the authenticated identity.
- Safe reproduction/evidence: Copy one unchanged framework-dependent worker output directory to two isolated hosts or `DOTNET_ROOT`s that provide different compatible `Microsoft.NETCore.App` 9.0 patch builds. `WorkerBinaryIdentity.ComputeSha256`/`CreateSnapshot(...).Sha256` is equal because every enumerated app-local file is equal, while `dotnet --list-runtimes` and the actually selected shared-framework/CoreCLR bytes differ. With the same request and manifest, `WorkerInputSnapshot.LoadAsync` consequently produces the same `InputHash`. Static evidence is decisive: `RuntimeComponents` never resolves or hashes the selected shared framework/host, and the project is not self-contained or patch-pinned.
- Closest live `BUGS.md` distinctions: Initial audit item 18 is specifically the omitted app-local native `libz3.so`; this finding is the separately resolved managed shared framework, CoreCLR/JIT, and `dotnet` host, and remains after adding `libz3.so`. Wave 10.21 says container marker validation omits some generated runtime/image fields; even if marker validation is fixed, those fields/bytes are still absent from `WorkerBinarySha256` and `InputHash`, so an intentional valid runtime/image upgrade reuses old cache identity. Wave 14.36 is a same-uid pathname replacement race after hashing the staged app-local worker; this needs no race or mutation and occurs under ordinary framework-dependent roll-forward/runtime resolution.

## Wave 20.23. HIGH - A mid-release push failure leaves an immutable partial release that the publisher refuses to resume

- Exact files/members/current lines: `scripts/Publish-SharpProofRelease.ps1`, top-level remote-state/action construction lines 881-943; `Invoke-NuGetPush` lines 688-724; top-level publication loop lines 985-1007. `scripts/SharpProof.PublicationDestination.ps1`, `Invoke-SharpProofMainPackagePreflight` lines 359-395, especially existing-package rejection at 381-388.
- Detailed mechanism: Before publication, the script preflights only each main package and requires every main identity to be absent. It then publishes each package as an irreversible main push followed immediately by its symbol push, before proceeding to the next package. There is no transaction, completion receipt, or resumable exact-match state. If the symbol push for a package fails, if a later package push fails, or if the process/container is terminated after any successful main push, an externally visible prefix of the release remains published. On retry, `Invoke-SharpProofMainPackagePreflight` receives HTTP 200 for the already-published main and unconditionally throws that publication is non-overwriting. It neither compares the downloaded remote bytes with the certified local artifact nor classifies an exact match as already completed, so the automated release workflow cannot resume to publish the missing symbols or later packages. The explicit plan action fields do not help: the execution loop ignores them and the live registry authority admits only `Absent` before execution.
- Concrete impact: A transient symbol-service failure, later-package rejection, runner loss, or network interruption can permanently strand a partially published version. Consumers can observe an incomplete package graph and/or missing symbols, while normal reruns fail before reaching the uncompleted work. Recovery requires ad hoc manual pushes or a new version, defeating the release publisher's validated ordered workflow and making external state difficult to recover.
- Safe reproduction/evidence: In a disposable mock NuGet harness, have the first `dotnet nuget push <main>` return 0 and the immediately following `.snupkg` push return nonzero (or terminate the process between lines 996 and 1002). Record the main identity as present. On a second run, return HTTP 200 from the PackageBaseAddress GET for that identity. The exact branch at `SharpProof.PublicationDestination.ps1:381-388` throws before the publication loop, so the missing symbol and remaining packages are never attempted. This requires no real registry and no repository mutation.
- Closest live `BUGS.md` distinction: Wave 18.62 concerns a two-rename crash window while replacing a local release-bundle directory; it can lose the local canonical destination before any remote publication. This finding is the later immutable remote NuGet workflow: a successful external push followed by failure creates a real partial release and the preflight policy prevents resumption. Wave 19.44 concerns qualification receipts that can authorize publication without real gate evidence; this finding remains even with genuine qualification and fully validated bytes.

## Wave 20.24. HIGH - Release dotnet validation does not bind the version-probed executable to the credential-bearing push

- Exact file/members/current lines: `scripts/Publish-SharpProofRelease.ps1`, `Resolve-ReleaseDotNet` lines 109-146; `Invoke-NuGetPush` lines 688-724, especially execution at 719; top-level host resolution lines 860-864.
- Detailed mechanism: `Resolve-ReleaseDotNet` accepts either the PATH command `dotnet` or any absolute executable whose basename is `dotnet`. It converts `Get-Command.Path` only with `Path.GetFullPath`, performs a lexical repository-prefix rejection, executes that pathname once with `--version`, and returns the same pathname. It does not resolve symlinks for the containment check, require a trusted owner/non-writable installation, capture device/inode/hash identity, or retain an executable handle. `Invoke-NuGetPush` later reopens that pathname in a separate process and supplies the NuGet API key and certified package path. An out-of-repository symlink can therefore target repository-local code while passing the lexical check, and either a symlink target or ordinary same-name executable can be atomically replaced after the benign version probe. A wrapper need only print the pinned SDK version for `--version`; the later credential-bearing invocation is unrestricted.
- Concrete impact: A PATH/symlink replacement race or untrusted absolute `DotNetPath` can execute different code than the executable that passed the SDK-version check. That code receives the NuGet API key in its arguments and the staged release paths, so it can exfiltrate the key, suppress or redirect publication, or substitute arbitrary network behavior while the release script treats a zero exit as a successful push.
- Safe reproduction/evidence: In a disposable directory outside the repository, create an executable named exactly `dotnet` that prints the pinned `global.json` SDK version for `--version` and records or rejects all other arguments. Passing its absolute path satisfies lines 118-145 despite not being a dotnet installation; a symlink at that outside path to a repository-local executable also passes because `GetFullPath` does not resolve the target. For the TOCTOU form, let a benign first executable satisfy `Resolve-ReleaseDotNet`, atomically replace the pathname, then invoke `Invoke-NuGetPush`; line 719 executes the replacement with the API-key arguments. A unit harness can use inert dummy key/package values and no network.
- Closest live `BUGS.md` distinction: Wave 14.36 covers a hashed staged worker DLL being replaced before the verifier's managed load. This is a separate release-publication trust boundary: the dotnet host is only behaviorally probed, no byte identity is ever established, and the second execution receives publication credentials. Wave 14.18 concerns a lexical corpus-license containment check following an out-of-tree symlink; it does not execute the target or expose release credentials. The initial Z3 TOCTOU finding concerns native solver load, not the NuGet publication host.

## Wave 20.25. MEDIUM - In-memory PE references lose content identity in compiler-probe snapshots

- Exact file/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateReferenceRows` lines 333-340; `CreateReferenceRow` lines 343-389, especially path projection at 347, identity at 358-362, and `fileSha256` at 378-382; `GetReferenceIdentity` lines 392-401.
- Detailed mechanism: A legal `PortableExecutableReference` created with `MetadataReference.CreateFromImage` has no `FilePath`. The row still includes it, but projects `filePath = ""` and `fileSha256 = ""`. With the default null display, the only content-related value left is `assemblyOrModuleIdentity`, which is only the declared assembly identity. Two different in-memory PE images may intentionally share the same assembly name/version/public-key identity while differing in metadata constants, API shape, or method bodies. With equal `MetadataReferenceProperties`, both references therefore produce the same complete JSON row even though Roslyn bound different image bytes. This is a direct non-injective input-provenance mapping, not a file race.
- Concrete impact: Distinct final compilations can emit byte-identical probe artifacts while producing different binding, diagnostics, or emitted consumer IL. The package oracle can consequently certify the wrong compiler reference closure. In particular, differing public constants are inlined into the consumer, so the consumer binary can change while the probe snapshot remains identical.
- Safe reproduction/evidence: Emit two in-memory assemblies both named/versioned `Lib, Version=1.0.0.0` where `public const int C` is 1 in one image and 2 in the other. Create one `PortableExecutableReference` from each byte array with the default `filePath`, and compile the same source `public static int Get() => LibType.C;` once against each. Each reference has null `FilePath`/default display, identical properties and assembly identity; lines 347 and 381-382 emit empty path/hash in both rows, while the emitted `Get` body returns a different constant. No filesystem mutation is required.
- Closest live `BUGS.md` distinction: Wave 18.48 concerns a file-backed reference whose pathname is reopened after Roslyn bound cached metadata (TOCTOU); this finding needs no pathname and loses the image bytes unconditionally. Wave 18.49 silently drops `CompilationReference`, whereas this finding concerns a retained `PortableExecutableReference` whose row lacks content identity. Wave 8.4 is the production Contract API resolver rejecting an authentic in-memory Attributes reference, not the test probe accepting two distinct in-memory images under one snapshot identity.

## Wave 20.26. MEDIUM - Compiler-probe snapshots omit executable entry-point selection

- Exact file/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `Create` lines 7-49; `AppendAssembly` lines 51-68; `AppendOptions` lines 70-149, especially the enumerated option fields at 81-147. `CSharpCompilationOptions.MainTypeName` (and `ModuleName`) are never projected.
- Detailed mechanism: The snapshot records assembly identity and a selected subset of compilation options, but omits `CSharpCompilationOptions.MainTypeName`, which is the compiler input selecting the entry point when multiple valid `Main` methods exist. Assembly identity does not include the selected entry-point type, and the syntax-tree/reference/additional-file rows are unchanged. Therefore two otherwise identical executable compilations that differ only in `MainTypeName` serialize identically, despite Roslyn emitting a different PE entry-point token and executing different code. The same omission pattern also leaves `ModuleName` unauthenticated, but `MainTypeName` alone supplies a concrete semantic collision.
- Concrete impact: The purported final-compilation probe cannot attest which program the compiler selected to run. A regression or cross-wiring of `/main` can change executable behavior while all snapshot bytes and canonicality checks remain unchanged, so the final-compilation oracle can falsely pass.
- Safe reproduction/evidence: Parse one tree containing `class A { public static void Main() { Console.Write("A"); } } class B { public static void Main() { Console.Write("B"); } }`. Create two `CSharpCompilationOptions(OutputKind.ConsoleApplication, mainTypeName: "A")` / `mainTypeName: "B"` compilations with identical assembly name, tree, and references. Both are valid; their snapshot assembly/options/tree/reference rows are identical because lines 51-149 never read `MainTypeName`, but their emitted PE entry points target `A.Main` versus `B.Main` and print different output.
- Closest live `BUGS.md` distinction: Wave 19.31 covers omitted warning/diagnostic policy (`WarningLevel`, `GeneralDiagnosticOption`, `SpecificDiagnosticOptions`, `ReportSuppressedDiagnostics`) and the resulting inability to attest diagnostic behavior. This finding is independent executable semantics: entry-point selection is omitted even with identical diagnostic policy and zero diagnostics. Wave 19.32 covers `SourceText` encoding/checksum provenance, not compilation-option identity. No live entry mentions `MainTypeName`, `/main`, module naming, or executable entry-point selection.

## Wave 20.27. MEDIUM - Response evidence authority treats fresh allocation as an EnforcePure violation, contradicting worker replay

- Exact files/members/current lines: `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`, `ValidateEffectClaim` lines 194-243, especially the Refuted gate at 212-220; `WitnessContradictsContract` lines 586-623, especially the `EnforcePure` arm at 600-602. Contradicting runtime authority: `SharpProof.Worker/EffectCounterexampleReplayer.cs`, `Replay` lines 33-56; `IsViolation` lines 158-169, whose only accepted allocation violations are `ZeroAllocations` and an `EffectContract` excluding `Allocates`. Intended semantic authority: `SharpProof.Effects/EffectContractMappings.cs`, `IsObservablePure` lines 132-137 and `IsPurityViolation` lines 140-144; allocation alone is not an observable-purity violation. This is also stated in `docs/soundness-notes/2026-07-30-allocation-effect-replay.md` lines 42-56, especially 49-55.
- Detailed mechanism: The only currently replayable effect event is a definite managed object/array allocation. `EffectCounterexampleReplayer.Interpret` converts it to a witness with `Effects=Allocates`; `IsViolation` deliberately does not treat that as an `EnforcePure` violation because fresh allocation is observationally pure. In contrast, response evidence validation accepts an `EnforcePure` Refuted result whenever `witness.Effects != None || witness.Capabilities != None`. Therefore a canonical allocation witness makes `WitnessContradictsContract` return true even though replaying the same event against the same contract returns no violation. The artifact codec does not close this gap: `CompilerEffectClaimArtifactCodec.HasValidOutcome` accepts any protocol-valid Refuted witness/replay tuple, and its EnforcePure constraint rule requires only the normal empty constraint.
- Concrete impact: The artifact-aware authority can certify a `Refuted/DefiniteViolation` EnforcePure response that the worker's actual result assembler deterministically downgrades to `Unknown/CounterexampleReplayFailed`. A malformed, corrupted, or resealed response/artifact can therefore cross final response validation as a false contract violation and fail a build, while worker-owned replay says the claimed event does not violate the contract. This also breaks the core invariant that launcher-side evidence authority accepts only results the worker replay can establish.
- Safe reproduction/evidence: Reuse the in-memory allocation fixture shape from `SharpProof.Worker.Test/EffectCounterexampleReplayTests.cs`, `CreateFixture` current lines 450-573. Set `ContractKind=EnforcePure`, keep the empty constraint, set evidence/result to `Refuted/None/DefiniteViolation`, and keep a canonical managed-allocation witness exactly matching the replay event; reseal with `CompilerEffectClaimArtifactCodec.Seal`. A matching `WorkerClaimResult` with empty model/core and that witness passes the generic Refuted effect tuple. `CompilerResponseEvidenceAuthority.Validate` emits no `response.effect_witness_authority` because witness equality holds and lines 600-602 call `Allocates` contradictory. Calling `EffectCounterexampleReplayer.Replay` on the identical target/evidence returns null because lines 162-168 have no EnforcePure case; `EffectClaimResultAssembler.Assemble` then returns `Unknown/CounterexampleReplayFailed` at lines 80-93.
- Closest live `BUGS.md` distinction: Wave 4.5 covers the worker replayer's inability to validate capability/exception violations; this finding is the opposite-side semantic error in response authority for the already-supported allocation event and specifically violates observable-purity semantics. Wave 5.3 covers failure to bind a response effect outcome/reason/certainty to compiler evidence; here compiler evidence and response both say Refuted and the defect persists. Wave 14.6 covers a witness not bound to its replay; here the witness exactly equals the allocation witness derived from replay, but the authority and replayer disagree about whether that observation violates EnforcePure.

## Wave 20.28. HIGH - SBOM workflow oracle authenticates only a step label and two input-looking lines, not an attestation action or executable step

- Exact files/members/current lines: `scripts/Test-SharpProofPackageDependencies.ps1`, `Test-SharpProofSbomAttestationWorkflow` lines 814-847, especially step selection by trimmed display name at 817-824, block delimiting at 826-833, and the only asserted content (`subject-path`/`sbom-path`) at 834-845. Release consumers: `scripts/New-SharpProofReleaseEvidence.ps1`, top-level workflow check at lines 810-812; `scripts/Test-SharpProofReleaseArtifacts.ps1`, top-level workflow check at lines 248-250; `scripts/Publish-SharpProofRelease.ps1`, `Get-ValidatedRelease` lines 549-551. Incomplete test oracle: `scripts/Test-SharpProofSbomArtifactScopeFixtures.ps1`, fixture workflow lines 71-80, notably deliberately noncanonical `uses: actions/attest@example` at 76, accepted as `canonical` by `SharpProof.ArchitectureTest/SbomSymbolArtifactScopeTests.cs`, test case and runner at lines 9 and 24-32. Actual checked-in action intended to be authenticated: `.github/workflows/package-consumers.yml` lines 95-99.
- Detailed mechanism: The validator does not parse YAML and never checks the step's `uses` value, immutable action digest, `if` condition, job/step reachability, permissions, or even that `subject-path` and `sbom-path` are inputs to an attestation action. It accepts any textual block whose trimmed step name is exactly `Attest package SBOM` and which contains exactly the two expected prefix lines before the next six-space `- name:`. Consequently, replacing the pinned `actions/attest@1e69...` action with an arbitrary action (including an attacker-controlled one that ignores the inputs), or adding `if: ${{ false }}` to skip the step, leaves the oracle passing. This is not hypothetical test incompleteness: the suite's own supposedly canonical fixture uses `actions/attest@example`, and the validator accepts it because `uses` is entirely unobserved.
- Concrete impact: Release evidence generation, final artifact validation, and publication can all certify the checked-in workflow while the workflow never creates an SBOM attestation for the NuGet packages. A release can therefore be published without the supply-chain provenance property these three independent release gates claim to enforce; an arbitrary replacement action could also execute with the release job's authority while still passing this oracle.
- Safe reproduction/evidence: Run `docker compose run --rm tooling test -Target SharpProof.ArchitectureTest -TestFilter 'FullyQualifiedName~SbomSymbolArtifactScopeTests.SymbolPackagesAreProvenanceArtifactsButNotSbomSubjects'`. In the current tree this passed all 15 cases. Its `canonical` row (C# line 9) necessarily ran the workflow fixture at PowerShell lines 71-80, whose line 76 is `uses: actions/attest@example`; success directly demonstrates that an unauthenticated/fictitious action is accepted. A second no-product-change proof follows from source: insert an `if: ${{ false }}` line into that in-memory workflow string; lines 834-845 still see exactly the same two path lines and have no predicate that can reject the disabled step.
- Closest live `BUGS.md` distinction: Wave 5.23 includes `ValidateAdvisoryPackagePolicy` ignoring `Condition` attributes on required MSBuild analyzer/verifier nodes, allowing shipped package behavior to be disabled. This finding is a different oracle and execution system: the release/SBOM validator ignores the GitHub Actions `uses` identity and reachability of the SBOM attestation step, so the defect remains even with perfectly valid package props/targets. Wave 19.44 concerns qualification receipts accepting self-asserted gate JSON without proof that qualification gates ran; here the affected release-evidence, artifact-validation, and publication scripts do run their SBOM workflow check, but that check falsely accepts a nonexistent, replaced, or disabled attestation action. No live `BUGS.md` entry covers authentication or reachability of the SBOM attestation workflow step.

## Wave 20.29. HIGH - Replaceable dotnet and supervisor paths become authenticated containment endpoints

- Exact files/members/current lines: `SharpProof.BuildTasks/RunVerifier.cs`, `Execute` lines 151-216, `ResolveSupervisorAssemblyRequired` lines 848-860, `ResolveDotNetHost`/`ResolveDotNetFromPath`/`ValidateDotNetInstallation` lines 1146-1257, and receipt matching in `ReadBoundedOutputAsync` lines 493-588.
- Detailed mechanism: Both the native `dotnet` executable and managed supervisor assembly are reduced to mutable pathnames before `Process.Start`; no opened executable descriptor or durable byte/inode identity is carried through use. The ordinary dotnet lookup also accepts an arbitrary executable named `dotnet` beside a `host/fxr` directory. Atomic replacement after validation, or direct supply of such a fake host, makes replacement code the endpoint that receives the fresh nonce and can emit exact Armed/Cleanup receipts without installing a genuine subreaper or cleaning descendants.
- Concrete impact: The containment TCB can be substituted while the task reports authenticated success, suppressing or fabricating verification and leaving arbitrary descendants alive. Benign concurrent replacement can also mix incompatible task, host, and supervisor generations.
- Safe reproduction/evidence: In a disposable installation, replace either the selected dotnet pathname or the copied supervisor DLL after resolution but before managed load. Replacement code can read `SharpProof.Start/1 <nonce>`, start a controlled detached child, print exact `SharpProof.Armed/1` and `SharpProof.Cleanup/1` records with that nonce, and exit zero; the current consumer accepts the forged containment lifecycle.

## Wave 20.31. MEDIUM - Request-bound protocol validation is quadratic in valid manifest/result cardinality and can overrun the worker/launcher deadline

- Exact files/members/current lines: `SharpProof.Worker.Protocol/ProtocolJson.cs`, `WorkerProtocolJson.ValidateManifestCore` lines 427-462, especially the per-callable `ValidateClaimMembership` call at 444-450; `ValidateClaimMembership` lines 464-474, especially the full `claims.Where(...)` scan at 467-469; `ValidateCallableResults` lines 476-494, especially the per-result `manifest.Callables.FirstOrDefault` at 487-491; `ValidateClaimResults`/`ValidateClaimResult` lines 495-551, especially the per-claim `manifest.Claims.FirstOrDefault` at 512-513 and `manifest.Callables.FirstOrDefault` at 548-551; `ValidateRun` lines 585-620, especially the per-callable call at 606-618. Supporting repeated scans: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `MatchesCallableProjection` lines 170-228, especially the manifest lookup and whole-claim filtering at 178-181. Deadline-sensitive callers: `SharpProof.Worker/SharpProofWorker.cs`, `SharpProofWorker.VerifyAsync` lines 305-315, and `SharpProof.Worker.Launcher/Program.cs`, `ValidateAndReport` lines 334-380.
- Detailed mechanism: Validation builds no callable/claim identity indexes. For each manifest callable, `ValidateClaimMembership` scans and sorts the entire claim array. It then separately scans all manifest callables once per callable-result, scans all claims and all callables once per claim-result, and, during run projection, invokes `MatchesCallableProjection` once per callable; that method again linearly finds the manifest callable and filters the complete claim-result array. A legal one-claim-per-callable response therefore performs several independent Theta(N^2) passes. Neither `Validate`, `ValidateForRequest`, nor the evidence-independent validation helpers accept a cancellation token or work budget. `SharpProofWorker.VerifyAsync` checks the project token immediately before assembly/validation, but a deadline arriving after line 307 cannot interrupt validation; launcher validation occurs after worker supervision with no remaining process deadline.
- Concrete impact: A structurally valid, under-16-MiB compiler manifest and response with thousands of selected callables can spend enough CPU in protocol validation to exceed the declared project wall-time, causing the worker to be externally killed instead of publishing its already-computed typed response. The launcher can likewise remain busy after the supervised worker has exited, so the end-to-end hard limit does not bound result acceptance. This is an availability/correctness failure on legitimate large projects, not merely malformed-input handling.
- Safe reproduction/evidence: Construct a sealed manifest with N distinct callables, one distinct dense-ordinal postcondition claim per callable, and matching canonical callable/claim result rows (for example all `Proven/None`, `Complete/None`) plus a matching summary. Invoke `WorkerProtocolJson.Validate(response)` or request-bound validation and compare N with 2N. Static source counting already establishes at least N full claim scans in `ValidateClaimMembership`, N full callable scans for callable rows, two further full manifest scans for each claim row, and both a callable lookup and claim-result scan for every callable in `MatchesCallableProjection`. N around 8,000 is representable within the 16-MiB file ceiling while inducing hundreds of millions of comparisons. No source mutation or unsafe external action is required.
- Closest live `BUGS.md` distinction: Wave 14.35 covers quadratic canonicalization/serialization from `FindClaimOrdinal`/`FindClaimCallableId`; this finding is the separate semantic validation path and remains after canonicalization is indexed. Wave 14.34 covers only `CreateIncomplete` recovery assembly owner lookup. Wave 19.27 covers artifact-aware `CompilerResponseEvidenceAuthority` replay and repeated per-claim semantic work; this finding occurs in the base protocol validator even with no evidence authority and includes manifest membership, result ownership, and callable/run projection scans. Wave 2.14 covers blocking on a FIFO before validation, not CPU complexity on an ordinary valid regular JSON response.

## Wave 20.32. HIGH - Exceptional cache-write paths lose the pre-assignment unstable value

- Exact file/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `GetReachingLocalValues` lines 117-181, especially output initialization at 141-143, propagation solely through `BasicBlock.Predecessors` at 148-160 and target-state assembly at 170-180; `TransferLocalValues` lines 204-227; `GetLocalWriteValue` lines 235-251.
- Detailed mechanism: The reaching-definition pass models each CFG block with one ordinary-predecessor output set. Roslyn does not expose exception dispatch from a potentially throwing RHS to a catch as an ordinary `BasicBlock.Predecessors` edge. A reachable catch can therefore begin with the empty set initialized at lines 141-143 rather than the value held before the try. If `answer` starts as `Unknown`, the try performs the recognized simple assignment `answer = Resolve(fail)`, and `Resolve` either throws or returns `Proven`, the normal try output becomes only `Proven`; the catch output remains empty; the post-catch join unions `Proven` with empty and concludes only `Proven` reaches the cache. At runtime, when `Resolve` throws, the assignment never occurs and the caught path writes the original `Unknown`.
- Concrete impact: An ordinary caught failure allows Unknown/timeout/failure state to be persisted while the error-level SPMETA010 soundness boundary emits no diagnostic. This is a direct false negative on a real semantic-cache write.
- Safe reproduction/evidence: Analyze `namespace SharpProof.Verify; enum Answer { Unknown, Proven } sealed class ProofCache { internal void Write(Answer a){} } sealed class C { static Answer Resolve(bool fail) { if (fail) throw new System.Exception(); return Answer.Proven; } void M(ProofCache cache, bool fail) { var a = Answer.Unknown; try { a = Resolve(fail); } catch (System.Exception) { } cache.Write(a); } }`. With `fail=true`, runtime reaches `cache.Write(Answer.Unknown)`. A read-only in-memory Roslyn C# 12 probe using the current built `SharpProofSoundnessAnalyzer` produced zero SPMETA010 diagnostics for this method. Static corroboration is that the transfer has no exceptional-edge or pre-region state channel: it reads only ordinary `Predecessors`.
- Closest live `BUGS.md` distinction: Wave 10.18 concerns omitted `ref`/`out` and deconstruction write kinds; both writes here are recognized declarator/simple-assignment forms. Wave 12.23 concerns definitions discarded specifically inside nested callables; this fixture has none. Wave 19.40 is a false positive from symbol-only alias recursion; this is a false negative caused by losing the pre-try definition on an exceptional catch path. No live entry mentions SPMETA010 reaching definitions across catch/exception flow.

## Wave 20.33. HIGH - Nested simple assignments are applied in source-span order instead of runtime evaluation order

- Exact file/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `TransferLocalValues` lines 204-227, especially descendant harvesting at 212-220, `OrderBy(value.Syntax.SpanStart)` at 221, and last-write replacement at 223-224; `GetLocalWriteValue` lines 235-251.
- Detailed mechanism: For one CFG block, the pass collects every descendant simple assignment to the tracked local, projects each assignment to its RHS operation, then orders those RHS nodes by lexical `SpanStart`. In a nested assignment, the outer RHS syntactically begins before the inner RHS even though runtime must evaluate the inner assignment first and apply the outer assignment last. The transfer therefore processes the outer write first and the inner write last, reversing the actual final value. For `a = (a = Answer.Proven, Answer.Unknown).Item2`, runtime first assigns `Proven` inside the tuple, obtains `Unknown` from `Item2`, then the outer assignment leaves `a=Unknown`. The analyzer visits the outer tuple/member-access RHS first, then clears it with the later-starting inner `Answer.Proven` RHS, leaving only `Proven` as the reaching value.
- Concrete impact: A cache can receive a definitely Unknown/timeout/failure answer through standard nested assignment syntax without SPMETA010. The same reversal can also create false positives in the opposite value ordering, so the error-level rule is not stable under semantics-preserving expression refactoring.
- Safe reproduction/evidence: Analyze `namespace SharpProof.Verify; enum Answer { Unknown, Proven } sealed class ProofCache { internal void Write(Answer a){} } sealed class C { void M(ProofCache cache) { var a = Answer.Proven; a = (a = Answer.Proven, Answer.Unknown).Item2; cache.Write(a); } }`. Runtime deterministically writes `Unknown`. A read-only in-memory probe using the current built analyzer produced zero SPMETA010 diagnostics for this method. The source-order inversion follows directly from the outer RHS span beginning at the tuple before the inner `Answer.Proven` span, while lines 223-224 retain only the last sorted value.
- Closest live `BUGS.md` distinction: Wave 10.18 concerns non-simple writes that `GetLocalWriteValue` never recognizes; here both outer and inner writes are `ISimpleAssignmentOperation` and are recognized, but ordered incorrectly. Wave 12.23 is nested-callable filtering; this nesting is purely an expression in one callable. Wave 19.40 is time-separated alias recursion and produces a false positive; this requires no aliases or recursion and produces a false negative from lexical-versus-evaluation order. No live entry mentions nested assignments or `OrderBy(value.Syntax.SpanStart)` in SPMETA010.

## Wave 20.34. MEDIUM - Calendar seed jumps can make a nominally rotating nightly campaign repeat 9,999 of 10,000 prior cases

- Exact files/members/current lines: `scripts/Invoke-SharpProofFuzzCampaign.ps1`, top-level default rotating-seed selection lines 45-49; `Tools/SharpProof.Fuzz/FuzzRunner.cs`, `RunAsync` case-seed consumption for frontend generation lines 152-155, per-case oracle execution lines 197-225, and `CreateCaseSeed` lines 552-555. The finite-domain generator additionally derives only from that case seed at lines 518-523.
- Detailed mechanism: The campaign converts the UTC date text `yyyyMMdd` directly to an integer. `CreateCaseSeed` then uses the arithmetic progression `seed + index * 397`. Therefore two valid calendar dates whose numeric `yyyyMMdd` values differ by 397 produce almost the same case-seed sequence: for later seed `S + 397`, later case `i` has seed `S + 397 + 397*i = S + 397*(i+1)`, exactly the prior campaign's case `i+1`. All three oracle inputs are deterministic functions of this same case seed: frontend XORs it with `0x35A1D7`, finite-domain generation XORs it with `0x6C8E9CF5`, and partial-term generation XORs it with `0x243F6A88`. Thus the overlap is semantic, not merely a displayed-seed collision.
- Concrete impact: At the configured 10,000 nightly cases, affected dates contribute only one genuinely new rotating case while `campaign.json` still reports 10,000 requested/observed cases and full agreements. Long-term nightly fuzz evidence can substantially overstate fresh exploration and repeatedly miss defects that require inputs outside the almost-identical window.
- Safe reproduction/evidence: The current date pair 2026-08-29 and 2026-12-26 is a direct example: integer seeds `20260829` and `20261226` differ by exactly 397. For every `i` from 0 through 9,998, evaluate the current function: `CreateCaseSeed(20261226, i) == CreateCaseSeed(20260829, i + 1)`. Consequently 9,999 of the 10,000 case seeds, and therefore all three generated oracle inputs for them, are identical. No execution or repository mutation is required. Other valid pairs recur (for example 2026-08-30/2026-12-27 and 2027-01-04/2027-05-01).
- Closest live `BUGS.md` distinction: Wave 14.50 is an intra-run defect specific to `PartialTermSmtCaseGenerator`: only three low seed bits affect that oracle, so eight semantic partial-term cases repeat even when case seeds differ. This finding is a cross-night collision in the campaign-level arithmetic progression and duplicates frontend, finite-domain SMT, and partial-term inputs together despite distinct nominal rotating seeds. Wave 2.25 concerns dirty/stale binaries being attributed to HEAD, not deterministic input overlap, and Wave 18.58 concerns concurrent writers colliding in one evidence namespace, not separated nightly campaigns generating the same cases.

# Read-Only Multi-Agent Bug Audit - Wave 21 - 2026-08-29

## Wave 21.1. HIGH - Null instance method-group construction can throw before a delegate overwrite, but the old callback is classified dead

- Exact path/member/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`; `TreeAnalysis.CanReachConsumption` lines 567-629 (assignment kill and `exceptionalStateSurvivesKill` at 619-628); `BlockMayThrowBeforeAssignmentCommit` lines 1034-1064; `OperationMayThrow` lines 1066-1107, especially the closed list at 1076-1106. Downstream nested selection is `GetNestedCallables` lines 297-367 and `AnalyzeGraph` lines 198-231.
- Detailed mechanism: A conversion of an instance method group to a closed delegate evaluates the receiver while constructing the delegate. For a null receiver, the runtime delegate construction throws `ArgumentException` before an enclosing assignment commits. Roslyn represents this shape with `IDelegateCreationOperation`/`IMethodReferenceOperation`; neither operation kind is recognized by `OperationMayThrow`. If a tracked delegate local is assigned such a method group, `CanReachConsumption` reaches the assignment target, asks `BlockMayThrowBeforeAssignmentCommit`, gets false when the receiver is just a local reference, marks the old value killed, and suppresses both the normal path and exceptional handler traversal. A catch that invokes the unchanged old delegate is therefore never connected to that local function/lambda.
- Impact: A genuinely executed nested callable can be omitted from Requires analysis. Deterministically false precondition calls in its body emit no SP0027 and the nested callable receives no real semantic outcome, creating a call-site verification false negative.
- Safe reproduction/evidence: Analyze `sealed class Holder { public int Safe() => 0; } static int Outer() { Func<int> callback = Reachable; Holder holder = null!; try { callback = holder.Safe; } catch (ArgumentException) { return callback(); } return 0; int Reachable() => Positive(-1); }`, where `Positive(int x)` begins with `Contract.Requires(x > 0)`. At runtime the closed-delegate creation rejects null `this` with `ArgumentException` before storing to `callback`; the catch invokes the original `Reachable`. Static trace: the callback assignment kills at lines 619-628; its RHS descendants are delegate creation, method reference, and local receiver, none matching lines 1076-1106; no exceptional successor is enqueued. The live Wave 13.5 entry independently records runtime evidence that this exact null nonvirtual method-group conversion throws `ArgumentException`, corroborating the language/runtime premise.
- Closest-entry distinction: Wave 18.10 is the same old-delegate reachability consequence but only for omitted user-defined conversions/operators (`OperatorMethod`); this case has no user-defined operator/conversion and is the built-in closed-delegate construction operation itself. Wave 13.5 concerns the separate Effects scanner assigning the wrong exception identity (NRE instead of `ArgumentException`); it does not cover `RequiresCallSiteTreeAnalyzer`, assignment commit, nested-callable reachability, or missing SP0027. Wave 19.9 concerns allocation of delegate `+`/`-`, not receiver failure during delegate creation.

## Wave 21.2. MEDIUM - Invoking and discarding an iterator local function is treated as executing its deferred body

- Exact path/member/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`; `TreeAnalysis.TryCollectLocalReferences` lines 432-460, especially unconditional local-target reachability for every `IInvocationOperation` at 441-459; `GetNestedCallables` lines 297-367; `AnalyzeGraph` lines 198-231 (child CFG traversal). There is no `IsIterator`/yield or call-result-consumption check on this path.
- Detailed mechanism: Calling an iterator method executes argument evaluation and creates/returns the iterator object; none of the iterator body runs until `MoveNext` (enumeration). `TryCollectLocalReferences` nevertheless treats every invocation whose target is a candidate local function as an immediate body-reachability edge. It does not distinguish iterator local functions or determine whether the returned enumerable/enumerator can ever be consumed. Consequently, even a discarded iterator result causes `GetNestedCallables` to select and `AnalyzeGraph` to analyze the whole iterator CFG as executable.
- Impact: Calls in a never-started iterator body can emit false SP0027 diagnostics and a false `Refuted` semantic outcome. Warning-as-error builds may fail although the precondition call cannot execute on any runtime path in the fixture.
- Safe reproduction/evidence: Analyze `static int Outer() { _ = Dead(); return 0; IEnumerable<int> Dead() { yield return Positive(-1); } }`, with `Positive(int x)` beginning `Contract.Requires(x > 0)`. C# invocation of `Dead()` only creates the iterator and the discard makes enumeration impossible; `Positive(-1)` is never executed. The outer CFG still contains an `IInvocationOperation` targeting `Dead`, so lines 443-459 add it to `reachable`; lines 311-329 select it and lines 198-231 analyze its child CFG, where the false precondition is reported.
- Closest-entry distinction: Wave 18.29 records async/iterator call-expression completion mistakes in the Effects subsystem, causing real caller suffix effects to disappear; it does not cover Requires nested-callable discovery or false diagnostics from an unenumerated body. Wave 20.2 covers a method group/lambda value converted directly to a discard with no invocation; here a real invocation occurs, but iterator semantics defer the body and the returned iterator is discarded. Wave 5.25 is the compile-time-only `nameof` method-reference case, not iterator state-machine execution.

## Wave 21.3. HIGH - Always-true-loop exit detection misses valid exits, so returning methods are classified as nonreturning

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MayCompleteNormally` lines 1961-2057, `LoopConditionIsAlwaysTrue` lines 2213-2225, and `LoopHasReachableBreak` lines 2228-2245; propagation through `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation`, and `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`.
- Detailed mechanism: For a recognized constant-true loop, completion depends on `LoopHasReachableBreak`. That helper recognizes only descendant `break` operations: it ignores reachable `return` and outward `goto`, and it enumerates only `body.ChildOperations`, so an unbraced root-level `break` with no children is invisible too. The loop and containing method are therefore marked noncompleting even though control returns normally.
- Concrete impact: Calls become terminal effect steps and real caller suffix writes, allocations, calls, and exceptions are omitted from Complete summaries, enabling unsound no-write, purity, allocation, or exception conclusions.
- Safe reproduction/evidence: All three helpers return normally but are classified otherwise: `while (true) { return; }`; `while (true) { goto Done; } Done: return;`; and `while (true) break;`. A caller mutation immediately after any such invocation is consequently dropped.

## Wave 21.4. HIGH - Constant-false switch-expression guards erase mandatory pattern/accessor evaluation

- Exact paths/members/current lines: `SharpProof.Effects/SwitchExpressionFacts.cs`, `GetReachableArms` lines 57-100 (constant route 78-99), `GetReachableArmsForUnknownValue` lines 183-214, and `ApplyGuard` lines 363-373. Consumers: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanSwitchExpression` lines 868-896, especially arm scanning 877-886; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteSwitchExpression` lines 138-152; `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MayCompleteSwitchExpression` lines 2061-2081; `SharpProof.Effects/ExceptionHandlerReachability.cs`, switch-expression arm scheduling lines 1154-1167.
- Detailed mechanism: `ApplyGuard` collapses any pattern plus a compile-time-false guard to `SwitchExpressionSelection.Never`. Both reachable-arm builders then omit that arm entirely. `Never` is correct for the arm VALUE being selected, but not for evaluation of the arm's pattern: C# must attempt the pattern before it can reach/evaluate the guard. A recursive/property/list/positional pattern can invoke getters, indexers, `Length`, or `Deconstruct`, and those calls can write, allocate, throw, or diverge. `ScanSwitchExpression` scans only the returned arms, so it loses the whole mandatory pattern evaluation. The same returned set feeds completion and exception reachability, so a nonreturning/throwing pattern accessor can be ignored and the fallback/suffix treated as reachable; a matching catch can also be considered unreachable.
- Impact: Real accessor effects and exceptions can disappear from a complete summary, permitting false purity/no-write/no-throw/allowed-exception proofs. A mandatory nonreturning accessor can also leave impossible suffix effects reachable, creating false diagnostics in the other direction.
- Safe reproduction/evidence: `struct Probe { public int P { get { Global.Seen++; throw new ApplicationException(); } } } static void M(Probe value) { _ = value switch { { P: _ } when false => 1, _ => 2 }; Global.After++; }`. Because `Probe` is a value type and `{ P: _ }` is total, runtime necessarily calls `P`; it writes `Seen`, throws, and never reaches `After`. `GetPatternSelectionForUnknownValue` classifies the pattern `Always`, `ApplyGuard` turns it into `Never`, lines 197-200 omit the arm, and the fallback is retained. Therefore the getter write/throw is not scanned and completion can be taken from the fallback. A `try/catch (ApplicationException)` around the switch exercises the same omission in `ExceptionHandlerReachability`.
- Closest-entry distinction: Wave 3.14 reports eager subpattern scanning before null/Length gates, which adds impossible nested effects; this finding is the opposite omission caused specifically by folding a false arm guard into whole-arm reachability before prerequisite pattern evaluation. Wave 7.19 covers `&&`, `||`, conditional, and coalesce expression branches, not switch-arm pattern prerequisites. Wave 20.8 concerns exact-type completion for an included recursive arm; here the arm is excluded solely by its constant-false guard before its mandatory pattern accessor is considered. No live entry mentions false switch guards or `ApplyGuard` erasing pattern evaluation.

## Wave 21.5. MEDIUM - Event assignments are unconditionally relabeled as normally completing

- Exact paths/members/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteNormally` lines 38-135, especially the absent `IEventAssignmentOperation` case and default `true` at line 134. `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanEventAssignment` lines 34-91, especially handler terminal return 61-64 and accessor step completion 88-90. Downstream relabeling: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanSequence`/`ScanStep` lines 952-974; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` successor gate lines 351-373.
- Detailed mechanism: The scanner can observe that `HandlerValue` does not complete and return early at lines 61-64, but its caller reconstructs an `EffectStep` from `CanCompleteNormally(eventAssignment)`, which falls through to `true`. For a normally evaluated handler and a source add/remove accessor that cannot return, lines 88-90 use that same default-true event predicate as the accessor step's completion flag, so terminality is lost inside the specialized scanner as well. The outer `ScanStep` again labels the whole event assignment completing. Regular CFG successors are consequently traversed after an event assignment with no normal path.
- Impact: Writes, allocations, calls, and exceptions after a definitely throwing/diverging handler expression or add/remove accessor are included even though unreachable, producing false effect/exception contract failures and misleading evidence.
- Safe reproduction/evidence: `static event Action E { add { throw new InvalidOperationException(); } remove { } } static void Handler() { } static void M() { E += Handler; Global.After++; }`. This is a static event, so the receiver defect in Wave 5.33 is not involved. `ScanEventAssignment` resolves the throwing `add_E` summary, but line 90 asks completion for the event assignment and receives default `true`; `ScanSequence` therefore reaches `Global.After++`, which runtime cannot. An alternative `E += Build()` with `Build` definitely nonreturning proves the outer relabeling after the early return at 61-64.
- Closest-entry distinction: Wave 5.33 is receiver ordering/nullness that prematurely suppresses reachable handler/accessor effects; this static-event case has no receiver and instead retains impossible suffix effects because event completion is absent. Wave 18.30 is the same default-true defect family for array initializers and interpolated strings, but does not cover `IEventAssignmentOperation` or the specialized accessor step's direct use of the default predicate. No live entry covers event-assignment terminality.

## Wave 21.6. HIGH - Await continuation-registration exceptions cannot make a matching catch reachable

- Exact paths/members/current lines: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `ExceptionHandlerReachability.GetPotentialExceptions(IOperation, HashSet<IMethodSymbol>, int, bool)`, await arm lines 857-961, especially protocol discovery at 870-950; catch decision in `GetReachability` lines 44-72. Downstream gate: `SharpProof.Effects/OperationEffectScanner.cs`, `OperationEffectScanner.IsReachable`, lines 1251-1276. Corroborating intended protocol handling: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `OperationEffectScanner.ScanAwait`, lines 93-202, especially continuation discovery/call at 179-195.
- Detailed mechanism: A C# await whose `IsCompleted` is false invokes the awaiter's `INotifyCompletion.OnCompleted` or `ICriticalNotifyCompletion.UnsafeOnCompleted` to register the continuation. The ordinary effect scanner explicitly resolves that method with `FindAwaitContinuationMethod` and joins its summary. The independent exception-reachability walker, however, models only the operand, `GetAwaiter`, `IsCompleted`, and `GetResult`; it never resolves either continuation-registration method. If that source continuation method is the sole throwing phase, `GetPotentialExceptions` returns no matching potential exception for the protected block, so `GetReachability` marks the source catch handler unreachable. `OperationEffectScanner.IsReachable` then drops every handler operation. At the same time `ScanAwait` has already included the continuation method's throw and normal lexical exception flow removes it as caught, so both the caught exception and the handler effects can disappear from the final method summary.
- Concrete impact: A complete effect summary can omit writes, allocations, calls, capabilities, or further exceptions executed by a catch that handles a failing await continuation registration. This can unsoundly satisfy purity/no-write/no-allocation or exception contracts.
- Safe reproduction/evidence: Analyze `sealed class Awaitable { public Awaiter GetAwaiter() => new(); } sealed class Awaiter : System.Runtime.CompilerServices.INotifyCompletion { public bool IsCompleted => false; public void OnCompleted(System.Action continuation) => throw new System.ApplicationException(); public void GetResult() { } } static int state; static async System.Threading.Tasks.Task M() { try { await new Awaitable(); } catch (System.ApplicationException) { state++; } }`. C# await semantics call `OnCompleted` after the false `IsCompleted`; its exception is inside the source try and the matching catch increments `state`. Static source trace is decisive: lines 870-950 enumerate `GetAwaiter`, `IsCompleted`, and `GetResult` with no continuation lookup, while scanner lines 179-195 explicitly find and scan `OnCompleted`; with those other phases nonthrowing, the catch gate sees an empty potential set and suppresses `state++`.
- Closest live `BUGS.md` distinction: Wave 19.10 covers a different await defect in `OperationEffectScanner.ScanAwait`: a null reference-type awaiter misses the implicit NRE at the `IsCompleted` dereference. This finding uses a nonnull awaiter and an explicit throw from the continuation-registration method; the scanner does include that method, but the separate `ExceptionHandlerReachability` await model omits it and therefore suppresses the matching handler. Wave 7.18 similarly concerns omitted user-defined truth-operator calls, not await continuation registration. No live entry mentions `FindAwaitContinuationMethod`, `OnCompleted`/`UnsafeOnCompleted` omission from `ExceptionHandlerReachability`, or this catch-gating mismatch.

## Wave 21.7. MEDIUM - Unknown entry-feasibility aborts before an independently replayable postcondition refutation

- Exact path/member/current lines: `SharpProof.Worker/CallableVerifier.cs`, `CallableVerifier.VerifyWithEntryFeasibilityAsync`, lines 54-68 (entry result is passed into postcondition verification); `CallableVerifier.VerifyPostconditionsAsync`, lines 74-101, especially the unconditional `entryFeasibility.IsUnknown` return at lines 96-101. The per-claim query and replay that this bypasses are lines 220-249. Closely related source of the Unknown state: `SharpProof.Worker/CallableEntryFeasibility.cs`, `CallableEntryFeasibilityEvaluator.EvaluateAsync`, lines 115-159, especially mapping an `UnknownOutcome` at lines 154-159.
- Detailed mechanism: A nonliteral `Requires` triggers the separate false-goal entry-feasibility query. If that query returns `Unknown` (for example a transient backend infrastructure/availability result), `VerifyWithEntryFeasibilityAsync` still calls `VerifyPostconditionsAsync`, but lines 96-101 immediately replace every postcondition with the same Unknown. The verifier never symbolically executes the body and never submits the actual postcondition query. That loses a stronger outcome the later query can establish independently: a `RefutedOutcome` includes a proof-kernel-replayed complete scalar model satisfying the very same preconditions/body assumptions and falsifying the postcondition, and `CallableCounterexampleReplayer` then replays the body/claim. Such a model simultaneously witnesses entry feasibility, so uncertainty from the earlier feasibility-only query cannot invalidate the refutation. The implementation already applies this exact outcome-sensitive rule to an inconclusive normal-completion probe: lines 236-244 downgrade only a later `Proven`, while lines 247-249 still replay and retain a later refutation. Entry uncertainty is instead treated as an unconditional early cutoff.
- Concrete impact: A real, replayable contract violation is hidden as `Unknown` whenever the preliminary feasibility query is inconclusive, even if the following postcondition query would return a validated counterexample. This loses actionable counterexamples and turns a deterministic `Refuted` result into incomplete semantic coverage under transient backend uncertainty. It is fail-closed rather than a false proof, but it is a correctness/diagnostic false negative and contradicts the documented soundness decision that a replay-validated postcondition refutation remains valid when the earlier satisfiability probe is unknown.
- Safe reproduction/evidence: Construct the existing internal trivial-target shape with one integer parameter `x`, clauses `Requires(x > 0)` and `Ensures(false)`, and `CompilerPreparedBody.Trivial()`. Use a scripted backend whose first response is `BackendCheckResult.Unknown(BackendFailureReason.InfrastructureFailure)` and whose second response is `BackendCheckResult.Satisfiable` with the complete assignment `x = 1`. Current control flow calls the backend exactly once and returns `Unknown(InfrastructureFailure)` at lines 96-101. If allowed to reach lines 220-249, the second query has assumption `x > 0` and goal `false`; `ProofKernel` validates `x = 1`, and trivial-body claim replay evaluates `Ensures(false)` to false, yielding a valid `Refuted`. This is read-only/internal evidence; no repository mutation or external action is needed. `docs/soundness-notes/2026-07-29-semantic-precondition-vacuity.md` lines 27-31 independently states the intended rule: an unknown feasibility probe must not become vacuity, but a replay-validated postcondition refutation remains valid.
- Closest-entry distinction: Live `BUGS.md` Wave 4.4 is the opposite control-flow error: a *known contradictory* entry is not short-circuited, so later body/goal abstention loses a valid vacuous proof. This finding concerns an *unknown* entry being short-circuited too aggressively, so a later independently validating refutation is never attempted. Wave 18.37 erases independent compiler effect evidence on callable-wide lowering failure; here lowering succeeds, the affected claim is a postcondition, and only the preliminary SMT feasibility outcome suppresses the per-claim replay path. No live entry records unknown entry feasibility masking a postcondition counterexample.

## Wave 21.8. MEDIUM - Constant-null switch values retain impossible type-pattern arms

- Exact paths/members/current lines: `SharpProof.Effects/SwitchExpressionFacts.cs`, `GetReachableArms` lines 57-101, especially the constant-value route at 70-82 and inclusion at 83-86; `GetPatternSelection` lines 376-413, especially the type/recursive fallback at 410-412. Direct consumer: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanSwitchExpression` lines 868-896, especially scanning every returned arm at 877-886.
- Detailed mechanism: When a switch governing expression has `ConstantValue.HasValue == true` with `Value == null`, `GetPatternSelection` handles constant, relational, negated, and binary patterns, then asks `IsTotalPattern(pattern, pattern.InputType)` for all remaining shapes. It never classifies a type/declaration/recursive pattern as `Never` when the known value is null. Such a pattern cannot match null in C#, but the fallback returns `Maybe`. `GetReachableArms` therefore adds its value expression to the reachable-arm set before continuing to the actual null/discard arm. The effect scanner scans the impossible arm and joins its effects as though it could execute.
- Impact: A valid method can acquire false writes, allocations, capabilities, or throws from an arm that is unreachable for the compile-time-null value. This can reject `EnforcePure`, `DoesNotThrow`, allowed-effect, or zero-allocation contracts and produce misleading witnesses; the direction is conservative but correctness-affecting.
- Safe reproduction/evidence: `static int Bomb() => throw new ApplicationException(); static int M() => ((object?)null) switch { string => Bomb(), null => 0 };`. Runtime deterministically selects the null arm and never calls `Bomb`. Roslyn retains a constant-null governing operation and the legal `string` type arm. Static trace: line 70 takes the constant route; `GetPatternSelection(stringPattern, null)` reaches line 410, where `IsTotalPattern` is false because matched type `string` differs from input type `object`, then line 412 returns `Maybe`; lines 83-86 retain the arm, and `ScanSwitchExpression` scans `Bomb`, adding an impossible `ApplicationException`.
- Closest-entry distinction: Wave 10.10 is the same selector's nonnull/exact-reference case: it fails to mark a guaranteed type match `Always` and invents an unmatched `SwitchExpressionException`. This finding is the opposite known-null case: it fails to mark an impossible type arm `Never` and imports that arm body's arbitrary effects. Wave 10.8 concerns nullable value types being globally mislabeled definitely-nonnull and can omit a real null arm; here the governing value is an actual compile-time null and the defect adds an impossible nonnull type arm. Wave 3.14 concerns nested pattern accessors scanned before null/length gates, not the selected arm value expression. Wave 21.4 concerns a constant-false guard erasing mandatory pattern evaluation, the opposite omission direction. Live `BUGS.md` had no constant-null/type-arm reachability entry at final cross-check.

## Wave 21.9. MEDIUM - Unselected generated declarations can emit error-level malformed-control diagnostics

- Exact paths/members/current lines: `SharpProof.Analyzer/SharpProofAnalyzer.cs`, `SharpProofAnalyzer.Initialize`, lines 25-32 (generated code is explicitly analyzed and diagnostics are reportable); `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `InitializeCompilation`, lines 114-149, especially the generated-code-enabled Method/NamedType symbol actions at 124-143; `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `ValidateMethodAttributes`, lines 72-117, especially validation and `GetSelection` at 87-104 before the only generated guard at 105-117 (which applies only to selected semicolon accessors); contrast `ValidateNestedCallableDeclaration`, lines 5-15; `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs`, `ValidateDeclaredScope` lines 19-25 and `ValidateScope`/`ReportInvalidReason` lines 147-193; generated classification is `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs`, `IsGenerated`, lines 48-67.
- Detailed mechanism: The analyzer opts into Analyze|ReportDiagnostics for generated code. A genuine `[SharpProofSuppress]` or `[SharpProofTrusted]` syntax node forces advisory Full activation, so Roslyn invokes the Method and NamedType symbol actions even for a `.g.cs`/exact-header generated tree. `ValidateDeclaredScope` has no generated-code guard. For an ordinary method, `ValidateMethodAttributes` calls `GetSelection`, which calls `ValidateAndShouldSuppress`, before checking generated status; moreover its later generated check is restricted to concrete semicolon accessors. A blank control reason therefore reaches `ReportInvalidReason` and emits error-severity SP0024 even though a control attribute is not a Contracts/Effects selection and the declaration is otherwise unselected generated code. The nested-callable path explicitly checks `AnalyzerGeneratedCodePolicy.IsGenerated` before validating the same control arguments, so equivalent generated local functions/lambdas remain quiet; existing `GeneratedNestedCallableControlReasonsRemainExcluded` codifies that behavior.
- Impact: Generated source that contains a malformed/placeholder SharpProof control reason can break a consumer build with an error diagnostic at code the consumer does not own, while the same malformed control on a generated nested callable is suppressed. Reporting therefore depends on callable/declaration shape rather than the generated/unselected policy.
- Safe reproduction/evidence: Analyze a tree named `GeneratedSubject.g.cs` under advisory/all containing `using SharpProof.Attributes; internal static class GeneratedSubject { [SharpProofSuppress("")] internal static void M() { } }` (optionally add the exact generated header). The attribute makes activation Full; `ValidateMethodAttributes` reaches `ValidateScope`, `TryGetReason` returns false, and SP0024 is reported before any generated check. A type-level `[SharpProofSuppress("")]` on an otherwise empty generated class deterministically reaches the unguarded NamedType action and does the same. No compiler error prevents an empty string attribute argument. The existing generated nested-control test at `SharpProof.Analyzer.Test/NestedRequiresCallSiteTests.cs` lines 1191-1218 expects zero SP0024 for the nested equivalent.
- Closest live `BUGS.md` distinction: Wave 7.35 is duplicate reporting of rejected/lookalike property-level controls across accessors, not genuine malformed controls escaping generated-code exclusion. Wave 19.37 concerns the separate meta-analyzer doing the opposite (skipping generated code and thereby missing soundness enforcement). Wave 20.4 concerns order-dependent advisory activation omitting symbol actions. No live entry covers SP0024 emitted from unselected generated ordinary method/type controls or the ordinary-vs-nested inconsistency.

## Wave 21.10. HIGH - Reduced `ref` extension receivers are never havoced, preserving stale scalar/nullness facts

- Exact paths/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `TransferCore` invocation arm lines 277-281; `HavocCall` lines 834-840; `HavocArguments` lines 842-855; proof consumers `ManagedFlowResult.ProvesNonNull`/`ProvesNull`/`ProvesNonZero` lines 1268-1280. Contrasting correct reduced-extension normalization: `SharpProof.Effects/EffectAnalysisSession.cs`, `ResolveCall` lines 146-164, especially receiver insertion at 156-163. Admission: `SharpProof.Analyzer.Core/LanguageSubsetGate.cs`, `SupportsOperationShape` lines 147-187 (invocation at 166-167) and `SupportsCall` lines 211-236 (ref-parameter check at 224-226). Downstream exception suppression: `SharpProof.Effects/OperationEffectScanner.cs`, `IntegralDivisionExceptions` lines 516-539, especially 527-536.
- Detailed mechanism: Roslyn represents reduced extension syntax `x.Zero()` with `x` as `IInvocationOperation.Instance`, a reduced target whose `ReducedFrom` is the original static extension method, and `Arguments`/reduced `Parameters` excluding the `this` receiver. If the original first parameter is `this ref int`, the call can overwrite `x`. Managed flow transfers the receiver, then calls `HavocCall` with only `invocation.Arguments`; `HavocArguments` therefore sees no ref argument and preserves `x`'s pre-call abstract value. This differs from effect-call remapping, which explicitly inserts the receiver as argument zero when `ReducedFrom != null`. The subset gate also checks only the reduced method's visible `Parameters`, so it does not see the original receiver's `RefKind.Ref` and admits the selected caller.
- Concrete impact: A stale exact nonzero or nonnull fact can suppress a real post-call `DivideByZeroException` or `NullReferenceException`, yielding a complete false no-throw/effect summary and allowing an invalid `DoesNotThrow` contract to pass.
- Safe reproduction/evidence: `static class E { public static void Zero(this ref int value) => value = 0; }` and selected method `[SharpProof.Attributes.DoesNotThrow] static int M() { int divisor = 1; divisor.Zero(); return 1 / divisor; }`. Runtime always throws `DivideByZeroException`. Static trace: entry/initializer sets `divisor` to interval `[1,1]`; the reduced call has receiver `divisor` but no ordinary arguments; lines 277-281/834-855 leave `[1,1]` unchanged; `ProvesNonZero` returns true and scanner lines 527-529 omit divide-by-zero. Numerator/divisor `[1,1]` also proves signed division overflow absent, so no compensating throw remains. The source extension call is admitted because the reduced method's parameter list omits `this ref`.
- Closest live `BUGS.md` distinction: Wave 20.11 is explicit ref/out conditional-argument havoc failing because `TryStorage` rejects a composite lvalue; here there is no `IArgumentOperation` to inspect because the writable location is the reduced extension `Instance`. Wave 19.7 concerns reduced-extension null receivers being falsely classified noncompleting, not receiver mutation or abstract-state havoc. Wave 7.11 concerns by-value delegate arguments mutating captures. No live entry covers reduced `ref this` state invalidation.

## Wave 21.11. HIGH - Managed abstract flow snapshots ref-local aliases as independent values, so pointee writes leave stale proofs

- Exact paths/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `TransferCore` variable/simple-assignment handling lines 231-268, especially the unconditional value copy at 258-267; `SetStorage`/`TryStorage` lines 857-874; proof use at `ManagedFlowResult.ProvesNonZero` lines 1278-1280. `SharpProof.Effects/OperationEffectScanner.cs`, `IntegralDivisionExceptions` lines 516-539, especially 527-536. Production reachability despite selected-root gating: `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, selected method subset check lines 259-295; `SharpProof.Effects/EffectAnalysisSession.cs`, recursive `BuildNodes` lines 427-456, which builds source callees without applying `LanguageSubsetGate` to their bodies.
- Detailed mechanism: A CFG ref-local initializer `ref int alias = ref divisor` is an `ISimpleAssignmentOperation` with `IsRef == true`. `TransferCore` never inspects `IsRef`; it evaluates `divisor` and stores a copied abstract value under the distinct `alias` local symbol. A later ordinary `alias = 0` updates only the alias key through `TryStorage`, even though C# writes the aliased `divisor` storage. Thus `divisor` retains its stale pre-alias interval/nullness/cardinality. Directly selected methods with ref locals are subset-rejected, but source callees are recursively built and summarized without a callee-body subset gate, so a supported selected wrapper can consume the stale complete summary.
- Concrete impact: A transitive helper can suppress a mandatory exception/effect and make a supported annotated wrapper falsely satisfy `DoesNotThrow` (or other effect contracts). The problem is an unsound proof, not only lost precision.
- Safe reproduction/evidence: `static int Helper() { int divisor = 1; ref int alias = ref divisor; alias = 0; return 1 / divisor; } [SharpProof.Attributes.DoesNotThrow] static int Caller() => Helper();`. Runtime `Helper` and `Caller` throw `DivideByZeroException`. Static trace: the ref initializer copies `[1,1]` to the `alias` key; `alias = 0` changes only that key; `divisor` remains `[1,1]`; `ProvesNonZero` suppresses divide-by-zero and exact `[1,1]` also suppresses signed-overflow potential. `Caller` itself contains only an ordinary supported call, while `BuildNodes` reaches `Helper` without classifying its ref-local body.
- Closest live `BUGS.md` distinction: Wave 7.7 is effect-region scanning that erases ref-local pointee reads/writes; this finding is the separate scalar abstract state retaining a stale value and using it to remove a real exception. Wave 7.26 is source-initial-null textual fallback after indirect writes; this reproduction begins with nonzero/nonnull state and does not use `IsSourceDefinitelyNull`. Wave 18.1 is frontend IR lowering that remains `Exact`, not Effects managed flow or exception suppression. Wave 20.11 covers a ref-conditional call argument, not ref-local alias establishment and direct pointee assignment. Fixing any one of those entries does not make `TransferCore` alias-aware.

## Wave 21.12. MEDIUM - API-spec resolution ignores constructor staticness and property generic arity

- Exact paths/members/current lines: `SharpProof.Specs/ApiSpecTable.cs`, `ApiSpecTable.ValidateDeclaration`, lines 173-238, especially independent `MemberKind`/nonnegative `GenericArity` validation at 185-187 and receiver/static consistency only at 232-238; `SharpProof.Effects/ApiSpecResolution.cs`, `ApiSpecResolver.MatchesTarget`, lines 193-219, especially constructor arm 197-204 and property-get arm 213-218.
- Detailed mechanism: `ValidateDeclaration` accepts every defined member kind with any nonnegative generic arity and accepts a constructor declared `IsStatic=true` as long as its receiver type is absent. During symbol resolution, the constructor arm checks that the Roslyn constructor itself is nonstatic but never compares `target.IsStatic`, so that contradictory target resolves to an instance `.ctor`. Conversely, the property-get arm compares staticness but never checks `target.GenericArity`; C# properties/indexers cannot be generic, yet a property row claiming arity 1 (or any nonzero value) resolves normally. Method resolution checks both fields, and constructor resolution checks actual arity, making these omissions asymmetric rather than an intentional global relaxation. `ContentSha256` authenticates both target fields, but the resolved symbol does not satisfy them.
- Impact: A malformed or misgenerated trusted row is reported as exactly resolved and `ResolvedApiSpecTable.TryGet` exposes its facets/postconditions for a symbol whose authenticated target shape is impossible or contradictory, rather than producing `MissingMember`. This defeats the resolver's fail-closed member-shape check and can attach trusted effect/throw/result claims to an unintended instance constructor/property getter.
- Safe reproduction/evidence: In an isolated resolver test using the existing platform-compilation helpers, build an `ApiSpecTarget` for `M:System.Exception.#ctor` with `MemberKind=Constructor`, `IsStatic=true`, `GenericArity=0`, no receiver, and zero parameters; `ApiSpecTable.Create` accepts it and the constructor arm resolves the real nonstatic `.ctor`. Separately clone the `P:System.String.Length` row but set `GenericArity=1`; table creation accepts it and the property arm resolves `String.Length`, even though no generic property exists. Assert `resolved.IsComplete`/one spec in both cases; expected fail-closed result is `MissingMember` (or earlier declaration rejection).
- Closest-entry distinction: Wave 3.20 is about result nullness/cardinality facets not being checked against the declared result type before totality validation; it does not concern symbol resolution or ignored `IsStatic`/`GenericArity`. Wave 12.26 is about regional effect bits incompatible with zero-parameter/static target shapes collapsing to empty regions; this finding remains with ordinary compatible facets and is the earlier false member-resolution decision itself. Wave 18.31 concerns assembly/reference-family authentication spoofing after a member shape resolves; this needs no spoofed assembly and shows the resolver ignoring two authenticated target-shape fields.

## Wave 21.13. MEDIUM - Closed sealed virtual calls make impossible catch handlers reachable

- Exact paths/members/current lines: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions(IOperation, HashSet<IMethodSymbol>, int, bool)`, invocation arm lines 221-261, especially unconditional `invocation.IsVirtual ? UnknownPotential : GetCallableExceptions(...)` at 250-257. Contrasting exact-dispatch classifier: `SharpProof.Effects/OperationEffectScanner.cs`, `IsDispatchUncertain` / `IsOpenDispatchTarget`, lines 1308-1315. Downstream catch gate: `SharpProof.Effects/OperationEffectScanner.cs`, `IsReachable`, lines 1251-1276, especially catch delegation at 1267-1271; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`, lines 351-373.
- Detailed mechanism: Roslyn sets `IInvocationOperation.IsVirtual` for a call to a sealed override even though the override and/or containing type closes dispatch and the exact method body is known. The ordinary effect scanner handles this distinction: `IsDispatchUncertain` requires both `invocation.IsVirtual` and `IsOpenDispatchTarget`, whose sealed-method/type test returns false, so it resolves the exact source summary. The independent exception-reachability walker ignores that closed-dispatch test and maps every `invocation.IsVirtual` directly to `UnknownPotential`. That fabricated unknown exception potential makes otherwise impossible source catches reachable. `OperationEffectScanner.IsReachable` then admits their operations and the CFG scan joins their effects, without adding any uncertainty to the method summary. Thus the same call is exact/nonthrowing for effect resolution but unknown-throwing solely for catch selection.
- Impact: A method can receive a Complete summary containing writes, allocations, calls, capabilities, or throws from a catch that cannot execute. Valid purity/no-write/no-allocation/allowed-exception contracts can be rejected, and direct evidence can point at impossible handler operations. This is a deterministic false-positive correctness defect rather than conservative incompleteness, because the fabricated `UnknownPotential` is only a reachability side channel and does not mark the final summary incomplete.
- Safe reproduction/evidence: Use `class B { public virtual void M() { } } sealed class D : B { public sealed override void M() { } } static class Global { public static int State; } static void F() { try { new D().M(); } catch (ApplicationException) { Global.State++; } }`. In the modeled source semantics, construction and the exact empty sealed override cannot throw `ApplicationException`, so the handler never runs. A read-only Roslyn 4.14 in-memory probe on the call produced `Kind=Invocation IsVirtual=True MethodIsVirtual=False IsSealed=True TypeSealed=True`. The scanner's lines 1308-1315 therefore classify dispatch as closed, while exception reachability takes `UnknownPotential` solely from `IsVirtual` at lines 250-257. `CanUnknownReach` admits the `ApplicationException` catch, and lines 1267-1271 allow `Global.State++` into the complete effect scan.
- Closest live `BUGS.md` distinction: Wave 18.27 is an open virtual-dispatch completion error: it trusts a nonreturning declared base body and omits real suffix effects even though a returning override may run. This finding uses a statically closed sealed override with an exact empty body; completion is not the problem. The separate exception-reachability walker invents unknown throws and includes impossible catch effects. Wave 10.13 suppresses real metadata ref-like getter exceptions (the opposite direction and property-specific), while Wave 7.12 concerns literal-false catch filters rather than dispatch precision. The live ledger contains no closed/sealed virtual-call exception-reachability entry.

## Wave 21.14. MEDIUM - Aggregate cache-length overflow escapes the read-side cache failure boundary

- Exact path/functions/members/current lines: `SharpProof.Worker/VerificationCache.cs`, `VerificationCache.TryStageCapacity`, lines 239-285, especially checked accumulation at 251-258; caller `TryReadAsync`, lines 12-96, especially catch filter at 91-95; contrast `TryWriteAsync` catch at 169-174.
- Detailed mechanism: `TryStageCapacity` adds every canonical active entry's `FileInfo.Length` into a signed `long` under `checked`. A corrupt/adversarial dedicated cache directory can contain sparse canonical-looking entries whose aggregate logical lengths exceed `long.MaxValue`, so the addition throws `OverflowException` before eviction begins. The write caller explicitly catches `OverflowException` and degrades to `false`, but the read caller's cache-failure catch omits it. Thus a successfully decoded/replayed hit followed by capacity reconciliation lets an ordinary capacity-accounting failure escape instead of becoming a cache miss/unavailable state.
- Impact: Cache contents can abort `SharpProofWorker.VerifyAsync`/force outer infrastructure failure rather than leave semantic verification unaffected, contradicting the cache's fail-open intent. No large physical allocation is required on a sparse-file-capable filesystem.
- Safe reproduction/evidence: Seed one valid replayable hit for the requested hash plus canonical `<64 lowercase hex>.sharp-proof-cache.json` sparse files whose logical lengths sum above `long.MaxValue` (on a filesystem supporting sufficiently large sparse files, two or a bounded set suffice), then read the valid key. Lines 255-258 deterministically throw; lines 91-93 do not catch the exception. A controlled filesystem/FileInfo seam can exercise the same arithmetic without allocating data. The asymmetric explicit `OverflowException` catch at lines 169-171 corroborates that this is intended to be a recoverable cache failure.
- Closest-entry distinction: Wave 13.1 covers capacity reconciliation not running on misses/noncacheable writes; Wave 6.10 covers crash-stranded rollback/eviction artifacts escaping accounting. Neither covers signed aggregate overflow escaping only the read-side failure boundary.

## Wave 21.15. MEDIUM - Capacity discovery and accounting ignore cancellation across the unbounded scan and sort

- Exact path/function/member/current lines: `SharpProof.Worker/VerificationCache.cs`, `VerificationCache.TryStageCapacity`, lines 239-285, especially the only pre-scan check at 244, uninterruptible enumeration/sort/materialization at 245-250, uninterruptible validation/length pass at 251-259, and next check only at 262.
- Detailed mechanism: After one token check, the method enumerates every matching directory entry, filters names, stats `LastWriteTimeUtc`, sorts the full population, materializes it, then validates and stats every owned entry while summing lengths. None of those O(N log N)/O(N) phases observes `cancellationToken`; cancellation is not checked again until eviction begins. The configured byte cap does not bound entry count or directory metadata work (zero-byte or tiny canonical entries are enough), and the cache lock stays held throughout.
- Impact: Project/caller cancellation can be delayed arbitrarily by cache metadata population, with sustained CPU/allocation and the cache locked; direct worker calls cannot complete cancellation promptly, and packaged execution may reach its hard termination boundary instead of cooperative cancellation.
- Safe reproduction/evidence: Populate a dedicated cache directory with a large number of zero-byte files named as 64 lowercase hex plus `.sharp-proof-cache.json`, arrange a valid hit or cacheable write so `TryStageCapacity` runs, start the operation, and cancel while enumeration/sorting is in progress. The token has no observation path until line 262, after `ToArray` and the complete length pass. Instrumented timing can assert completion does not occur at cancellation and scales with entry count.
- Closest-entry distinction: Wave 13.1 concerns capacity enforcement being skipped on misses, and Wave 14.44 concerns cancellation omission in the meta-analyzer reaching-definition pass. No live entry covers cancellation/resource bounds inside cache capacity enumeration and sorting.

## Wave 21.16. MEDIUM - Changing optional publication membership wedges both verification and the currently configured Clean

- Exact paths/members/current lines: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, `_SharpProofInitializeVerify` optional SARIF/publication selection lines 45-56 and `InvalidatePublishedResult` invocation lines 99-113; `SharpProofResetPublishedVerification` lines 234-253, especially optional clean SARIF selection at 245-247 and reset call at 249-253. `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, `Execute(CancellationToken)` publication-set construction lines 92-107 and acquisition lines 226-238. `SharpProof.Host/LinuxPathIdentity.cs`, `ResetPublicationSet` lines 174-226, especially marker-count decision at 183-200 and acquisition at 202-205; `AcquirePublicationSet` lines 228-297 and `BindPublicationSet` lines 469-525, especially existing-marker validation at 480-483 against the set ID computed at 471-472.
- Detailed mechanism: Publication ownership markers encode the hash of the complete member list, not merely per-path ownership. The target makes SARIF an optional member. After a successful no-SARIF build, request/result/manifest have markers naming the three-member set. If `SharpProofVerifySarifFile` is then enabled, initialization asks `InvalidatePublishedResult` to acquire a four-member set. `BindPublicationSet` encounters each existing marker and validates it against the new four-member set ID, so it throws before verification. Running Clean under that new configuration is not a recovery path: `ResetPublicationSet` sees three existing marker paths out of the requested four and immediately throws `cannot reset an incomplete publication set` before acquiring locks. The reverse transition is also wedged: after a four-member SARIF build, disabling SARIF makes Clean request three members; all three selected markers exist, but acquisition rejects their four-member set contents against the expected three-member ID. Changing any configured request/result/manifest member has the same topology-transition problem. The only supported reset requires knowing and resupplying the exact old member list, which normal Build/Clean no longer has after configuration changes.
- Concrete impact: A routine configuration edit such as enabling or disabling SARIF can make subsequent verified builds and Clean fail indefinitely under the new configuration, leaving stale public evidence and markers. Recovery requires restoring the old settings for a Clean or manually deleting product metadata, so the advertised Clean lifecycle is not configuration-robust.
- Safe reproduction/evidence: In a disposable canonical Linux package-consumer fixture, (1) build once with verification enabled and no `SharpProofVerifySarifFile`; (2) rerun Build with `SharpProofVerifySarifFile=<absolute disposable path>` and observe the publication-set overlap failure; (3) run Clean with that same new property and observe `SharpProof cannot reset an incomplete publication set.` For the inverse, first build with SARIF and then run Clean without the SARIF property; existing markers reach `ValidatePublicationMarker` and fail because the expected set ID changed. This follows deterministically from the three-versus-four path arrays and requires no malformed files or concurrent writer.
- Closest live `BUGS.md` distinction: Initial item 16 is partial marker deletion caused by cancellation/I/O and wedges retry; here every marker is complete and authentic, but a legitimate configuration change alters the requested member set. Wave 6.23 is reset inspecting marker state before locking during concurrent publication; this reproduces serially with no concurrent publisher. Wave 14.46 is Clean being skipped on unsupported hosts, and Wave 2.22 is relative paths resolving against the ambient directory; this occurs on the supported host with absolute paths and Clean actually executing.

## Wave 21.17. MEDIUM - Final MSBuild validation reads the publication trio without its publication lease and can observe a torn generation

- Exact paths/members/current lines: `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `ValidatePublishedVerificationResult.Execute` lines 27-130, especially independent reads of invocation result at 47-51, public request at 60-62, public result at 68-69, and compiler manifest at 85-87, with cross-file checks at 88-118; there is no `AcquirePublicationSet` call. Producer evidence: `SharpProof.Worker.Launcher/Program.cs`, `PublishOutputs` lines 481-560, publication lease at 493-501, member order at 515-535, and sequential pathname replacement with result last at 541-548. Scheduling: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, `_SharpProofVerifyCore` runs `RunVerifier` at 201-211 and only afterward invokes the validator at 214-217.
- Detailed mechanism: The launcher correctly holds the publication-set lock while replacing manifest, request, optional SARIF, and finally result as a commit marker, but releases that lease before `RunVerifier` returns to MSBuild. The later validator opens each public pathname separately and never takes the same lease. A second legitimate build sharing the default same-project/same-TFM paths can therefore publish between reads. For example, validator A reads request A at line 60; publisher B then replaces manifest B, request B, and result B under its lease; validator A reads result B and manifest B at lines 68/85. The request-hash check fails on the torn A/B tuple. The inverse window (request B visible while result A is still the commit marker) fails likewise, and concurrent invalidation can make the result temporarily absent. Both complete generations are individually valid; only the unlocked reader assembles an invalid view.
- Concrete impact: Parallel builds of the same project/output can nondeterministically fail final validation with `SharpProof verification did not publish a valid current result` even though each launcher produced a valid generation. Wiring exact-invocation validation would prevent accepting another invocation but does not prevent this false failure; a read-side lease/snapshot is still required.
- Safe reproduction/evidence: Use two disposable same-project/same-TFM invocations with the default shared publication paths and a controlled pause in validator A after its request read. Let launcher B complete `PublishOutputs`, then resume A; A deterministically compares request A with result/manifest B and returns false. A lower-level safe fixture can publish two ordinary protocol-valid generations through the existing publication helper while pausing the validator between its current `File.ReadAllBytes` calls. Static ordering is sufficient: publisher lines 542-548 expose sequential replacements only to readers outside the lock, and the validator contains no acquisition.
- Closest live `BUGS.md` distinction: Wave 18.42 covers a coherent later generation being falsely accepted because the shipped target omits `InvocationResultPath`; this finding is the separate torn-generation false failure caused by the validator not acquiring a read lease and remains after `InvocationResultPath` is wired. Wave 6.23 is the Clean/reset pre-lock marker race, not final request/result/manifest reading. Waves 19.30 and 19.34 concern unbounded allocation and FIFO blocking on replaced inputs; this uses ordinary bounded regular JSON files. Wave 2.21 concerns user-configured paths colliding across target frameworks; this reproduces for two same-TFM builds on default paths.

## Wave 21.18. HIGH - Ref-conditional lvalue phis write only a synthetic scalar capture, leaving the selected storage unchanged while lowering remains Exact

- Exact paths/members/current lines: `SharpProof.Frontend/RoslynProgramLowerer.cs`: `LoweringSession.Lower` lines 80-101 (especially exactness at 94-100); `LowerStatement` lines 124-173 (flow-capture dispatch at 147-149 and simple-assignment dispatch at 142-146); `LowerCapture` lines 197-203; `LowerAssignment` lines 205-220. `SharpProof.Frontend/RoslynOperationLowerer.cs`: `GetCapture` lines 130-141; `GetReferencedVariable` lines 144-164 (especially the `IFlowCaptureReferenceOperation` arm at 161-162).
- Detailed mechanism: A writable ref conditional such as `(choose ? ref x : ref y) = 1L` is lowered by Roslyn CFG as a branch whose two arms each contain an `IFlowCaptureOperation` for the selected parameter storage, followed at the join by an `ISimpleAssignmentOperation` whose target is one `IFlowCaptureReferenceOperation`. Crucially, that joined assignment has `IsRef == false`: it is the ordinary write through the selected managed reference, not a ref-rebinding assignment. `LowerCapture` does not preserve an lvalue/location; it calls `LowerValue` and assigns the current scalar value of `x` or `y` into a synthetic capture variable. At the join, `GetReferencedVariable` unconditionally maps the flow-capture target to that same scalar IR variable, so `LowerAssignment` writes `1` only into the synthetic capture. Neither `x` nor `y` is changed. All involved Boolean/integer parameter references, flow captures, the integer conversion, and the simple assignment are accepted, so no abstention is recorded and `Lower` publishes `Exact`.
- Concrete impact: This is exact semantic corruption on ordinary acyclic code and can enable false compiler postcondition proofs. For example, with `Contract.Requires(choose); Contract.Requires(x == 0L); Contract.Ensures(Contract.Result<long>() == 0L); (choose ? ref x : ref y) = 1L; return x;`, every admitted execution has `choose == true` and runtime returns 1, violating the postcondition. The exact IR leaves `x` equal to its entry value 0 and returns 0, so an exact-only consumer can prove the false postcondition.
- Safe reproduction/evidence: A read-only Roslyn 4.14 probe for `class C { static long M(bool b,long x,long y){ (b ? ref x : ref y)=1; return x; } }` produced B2 `FlowCapture(ParameterReference x)`, B3 `FlowCapture(ParameterReference y)`, and B4 `SimpleAssignment(IsRef=False, Target=FlowCaptureReference, Value=Conversion(1))`. Running the current built `RoslynProgramLowerer` read-only returned `exact=True`, zero abstentions, parameter bindings `choose=v0, x=v2, y=v3`, and one capture `v1`. Its assignments were `v1 <- v2`, `v1 <- v3`, then `v1 <- 1`; its return was `v2`. Thus for `b=true,x=0`, C# returns 1 while the emitted exact program returns the unchanged x value 0. No repository file was changed. Existing `ProgramLoweringTests` has no ref-conditional or writable-flow-capture coverage.
- Closest live `BUGS.md` distinction: Wave 18.1 is ref-local alias erasure: it requires an `ILocalSymbol` with `RefKind.Ref` and an `IsRef` binding/rebinding assignment. This reproduction has no ref local at all, and Roslyn reports the joined write as `IsRef=False`; the lost alias is the CFG phi/capture that merges two lvalue locations. Wave 20.11 concerns `ManagedAbstractFlow` failing to havoc either arm when a ref conditional is passed to a ref/out call; this finding is in `RoslynProgramLowerer`, needs no call, and directly emits an `Exact` wrong standalone assignment. Wave 7.28 concerns freshness ownership in the Effects subsystem for value captures, not preservation of writable locations in frontend IR. No live entry covers a flow-capture reference used as an lvalue target being lowered as a scalar capture.

## Wave 21.19. HIGH - Worker timeout can send SIGTERM to an unrelated process after numeric PID reuse

- Exact path/function/member/current lines: `SharpProof.Host/LinuxWorkerProcess.cs`, `LinuxWorkerProcess.WaitForExit`, lines 87-116; `CompleteAtDeadline`, lines 156-167; `Terminate`, lines 170-215, especially the separate `process.HasExited` test at 175-178 and native `kill(process.Id, SIGTERM)` at 179-188. The same `Terminate` is called by `Dispose`, lines 141-153.
- Detailed mechanism: The code keeps a managed `Process`, but the cooperative signal is sent through a fresh numeric lookup: first `process.HasExited` is sampled, then `process.Id` is passed to libc `kill`. On Linux, the original worker can exit and be reaped after the false sample but before `kill`; its PID can then be assigned to an unrelated process. `kill` succeeds against that new process because no pidfd/start-time identity is retained or checked. The following `process.WaitForExit` still observes the original worker's stored completion and can return immediately, so the method reports an ordinary timed-out worker and never detects the collateral signal. The later `process.Kill(entireProcessTree: true)` is not a safeguard when the original is already observed exited.
- Concrete impact: At a normal worker deadline (and on disposal cleanup), SharpProof can terminate an unrelated same-namespace process. In a shared build container this can kill another build/test/service and still return the expected timeout status, making the collateral failure hard to attribute.
- Safe reproduction/evidence: Use a disposable PID namespace with a deliberately small/recycled PID space. Pause (debugger/test seam) after line 175 returns false; make the controlled worker exit and ensure it is reaped, churn controlled processes until the same integer is assigned to a process that records SIGTERM, then resume. Line 179 necessarily targets the replacement while `process.WaitForExit` continues to describe the original. No production process needs to be targeted. The source-level check/use gap plus numeric libc API is decisive.
- Closest-entry distinction: Wave 19.35 is `RunVerifier`'s separate supervisor fallback: it scans descendants of a recycled supervisor ID and SIGKILLs them despite retaining an old pidfd. This finding is the worker launcher's direct-child `LinuxWorkerProcess` path, which has no pidfd at all and sends SIGTERM in the initial timeout action before any supervisor fallback. Wave 8.13 only lets a performance probe attach to a reused PID, causing false gate failure/delay; it does not signal the process.

## Wave 21.21. HIGH - Order-insensitive exhaustive-filter reasoning lets a throwing property pattern route cancellation into an undiagnosed later catch

- Exact path/functions/members/current lines: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`, `CancellationHandledEarlier`, lines 45-85, especially the `FilterIncludesAllCancellation` decision at 75-80; `FilterIncludesAllCancellation`, lines 87-131, especially operation routing at 111-130; `PatternIncludesAllCancellation`, lines 133-155, especially the order-insensitive `or` rule at 142-147. The eventual suppression is in `AnalyzeCatchClause`, lines 14-43, especially line 27.
- Detailed mechanism: `PatternIncludesAllCancellation` declares an `or` pattern exhaustive for `OperationCanceledException` whenever either arm's syntax includes all cancellation types. It does not account for left-to-right pattern evaluation or the fact that recursive/property patterns can invoke user property getters and throw. For a previous filter shaped `e is { Message: null } or OperationCanceledException`, the left arm is an `IRecursivePatternOperation` and the right arm is an `ITypePatternOperation`; lines 142-147 return `false || true`, so `CancellationHandledEarlier` concludes that every cancellation must enter the previous catch. At runtime the left arm is evaluated first. An `OperationCanceledException` subtype may override the virtual `Exception.Message` getter and throw. C# catch-filter semantics treat an exception thrown by a filter as filter failure, so the original cancellation skips the first catch and reaches a later broad catch. The later catch is nevertheless exempted at line 27 and can swallow cancellation without SPMETA003. The algorithm also treats the safe reversed order (`OperationCanceledException or { Message: null }`) identically, although only that ordering short-circuits before the getter for every cancellation.
- Concrete impact: The error-level cancellation boundary can miss a real cancellation-swallowing handler. A backend, host, or injected component that throws a derived `OperationCanceledException` with an exceptional virtual property can be converted into ordinary success/fallback behavior by the later catch, defeating the analyzer's soundness guard.
- Safe reproduction/evidence: Analyze this source: `sealed class EvilCancellation : OperationCanceledException { public override string Message => throw new InvalidOperationException(); }` and a method containing `try { throw new EvilCancellation(); } catch (Exception e) when (e is { Message: null } or OperationCanceledException) { throw; } catch (Exception) { /* swallowed */ }`. The first catch is independently accepted because its first statement is bare `throw;`. For the second catch, the source trace is deterministic: `PatternIncludesAllCancellation` returns true from the right `OperationCanceledException` arm, so `CancellationHandledEarlier` suppresses SPMETA003. Runtime evaluates the left property pattern first; its virtual getter throws, the filter is treated as false, and the original `EvilCancellation` enters and is swallowed by the second catch. Reversing the two `or` arms demonstrates why the current commutative Boolean rule is unsound for potentially throwing patterns.
- Closest live `BUGS.md` distinction: Wave 15.5 is the opposite false-positive problem: safe prior filters such as `e is not null` are not recognized as exhaustive. This finding concerns a filter the analyzer *does* classify exhaustive even though exceptional left-arm evaluation makes it non-exhaustive at runtime. Wave 12.15 is a false negative caused by erasing a user-defined conversion in the currently analyzed filter; this finding needs no conversion and arises in earlier-catch reachability from order-insensitive `or` handling plus a throwing virtual property getter. Waves 19.47 and 20.21 are false positives on unary-not/null filters in the current catch, not a cancellation-swallowing bypass through a later catch.

## Wave 21.22. MEDIUM - Portable callable artifact round-trip accepts non-Boolean contract clauses

- Exact paths/members/current lines: `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `CompilerLoweredArtifact.Encode` lines 12-58, especially untyped clause-root collection at 29-33 and clause projection/hash at 53-58; private `Decode(CompilerCallableArtifact, ...)` lines 250-384, especially clause shape checks and construction at 305-325; there is no `Root(row.Root).Type == decoded.Factory.BooleanType` check. Downstream manifestation: `SharpProof.Worker/CallableEvidenceBuilder.cs`, `Build` lines 26-58 (non-Ensures becomes `new Assumption` at 55-58), with Boolean guard in `SharpProof.Verify/Evidence.cs`, `Assumption` constructor / `FactoryGuards.RequireBooleanTerm` lines 83-93 and 121-137. For Ensures, `SharpProof.Worker/CallableVerifier.cs`, `VerifyPostconditionsAsync` lines 184-208, especially `Guard(factory, executionCondition, pathCondition)` at 191-201.
- Detailed mechanism: A contract clause condition is semantically required to be Boolean, but the portable callable encoder accepts every `IrTerm` in `preparation.Clauses`, serializes it as a root, and seals its exact bytes in `PredicateSha256`; it never checks the term type. Decode validates the clause enum/evidence/IDs/root ordinal/hash, and the portable graph codec validates only each term's own IR shape/type metadata. Decode then constructs `CompilerPreparedClause(row.Kind, Root(row.Root), ...)` without requiring that root to be `decoded.Factory.BooleanType`. Consequently a completely canonical, self-consistent artifact can hydrate successfully with an Integer/String/reference contract condition. A Requires/Assume reaches `Assumption` only later and throws `ArgumentException` from `RequireBooleanTerm`; an Ensures reaches `IrSemanticTerms.Guard`, whose Boolean `OrElse` construction rejects the non-Boolean consequent. `CallableVerificationPolicy.VerifyTargetAsync` converts this late exception into generic per-call `InfrastructureFailure`, rather than the artifact being rejected at hydration as `CompilerManifestMismatch`.
- Concrete impact: The serialized compiler-artifact trust boundary admits a type-invalid successful callable. Corrupt/resealed input, or an internal producer regression, survives canonical graph and predicate-hash validation and fails only during verification, changing a deterministic malformed-manifest error into generic incomplete/infrastructure results and wasting backend work. It cannot currently produce a false proof because the later Boolean constructors fail closed, so severity is Medium rather than High.
- Safe reproduction/evidence: In the existing `SharpProof.Worker.Test` friend assembly, adapt any successful one-postcondition round-trip fixture: set its sole clause to `new CompilerPreparedClause(CompilerContractKind.Ensures, factory.Integer(1), validEvidence, claimId, null)` and use a trivial body. Call `CompilerLoweredArtifact.Encode(preparation)`: it succeeds and emits both a canonical portable graph and matching `PredicateSha256`. Pass the artifact through the ordinary full `CompilerLoweredArtifact.Decode`/manifest fixture: decode succeeds, and the returned successful preparation has `Clauses[0].Condition.Type == decoded.Factory.IntegerType`. Sending it through callable verification reaches `Guard(true, Integer(1))`, where `IrFactory.Binary(OrElse, ...)` throws; the policy reports `InfrastructureFailure`. The Requires variant similarly decodes, then throws in `new Assumption` at the cited Boolean guard.
- Closest live `BUGS.md` distinction: Wave 3.8 concerns `IrSemanticTerms` singleton/no-witness fast paths directly returning a foreign or non-Boolean term; this finding needs no helper fast path and is the portable callable encoder/decoder failing to enforce the contract-clause Boolean schema. Wave 12.4 concerns top-level manifest validation not requiring any decodable graph at all; this artifact is present, canonical, hash-consistent, and survives `DecodeCallables`, failing only at semantic consumption. Wave 9.3 concerns void-body return-value shape, not clause predicate typing. No live entry mentions non-Boolean serialized clause roots or the missing type check in `CompilerLoweredArtifact.Decode`.

## Wave 21.23. HIGH - Semantic-model forbidden-API coverage omits direct speculative and CSharp-specific APIs

- Exact path/member/current lines: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `KnownTypeNames`, lines 13-33; `ForbiddenMethods`, lines 35-46; `IsForbidden`, lines 103-130, especially exact-type checks at 109-119. Corroborating policy catalog: `BannedSymbols.txt` lines 12-38.
- Detailed mechanism: SPMETA001's semantic-model catalog is materially narrower than the repository's explicit forbidden-API policy. `ForbiddenMethods[SemanticModel]` contains only `TryGetSpeculativeSemanticModel`, `GetSpeculativeTypeInfo`, and `GetDiagnostics`; ordinary direct calls to `SemanticModel.GetSpeculativeSymbolInfo` and `GetSpeculativeAliasInfo` therefore fall through. Separately, `KnownTypeNames` has no `CSharpCompilation`, `CSharpSemanticModel`, or `CSharpExtensions`, and the special `GetSemanticModel` branch requires the declaring type to be exactly `Compilation`. Consequently the direct `CSharpCompilation.GetSemanticModel(SyntaxTree, SemanticModelOptions)` overload and the direct CSharp speculative-model members/extensions also fall through. These APIs are expressly forbidden in `BannedSymbols.txt`, so this is not an inferred expansion of policy.
- Impact: A consumer running the SharpProof meta-analyzer without the repository-wide BannedApiAnalyzer can directly acquire unauthorized semantic models or speculatively bind syntax in soundness-critical code without the error-level SPMETA001 boundary. The repository production projects currently also load BannedApiAnalyzer/RS0030, which is defense in depth for the explicitly listed signatures, but the custom analyzer's own advertised enforcement is incomplete and standalone analyzer tests/consumers are unprotected.
- Safe reproduction/evidence: Analyze an ordinary invocation such as `model.GetSpeculativeSymbolInfo(position, node, SpeculativeBindingOption.BindAsExpression)` or `csharpCompilation.GetSemanticModel(tree, SemanticModelOptions.IgnoreAccessibility)`. The first method name is absent from lines 35-46; the second method's declaring type is `CSharpCompilation`, so the exact `Compilation` condition at line 117 is false. Both then miss the `ToDisplayString` branch and return false at lines 122-130. No mutation is required; the exhaustive code branches plus the explicit `BannedSymbols.txt` signatures are direct evidence.
- Closest-entry distinction: Wave 15.7 covers direct parser APIs (`ParseCompilationUnit`, `ParseMemberDeclaration`, `CSharpSyntaxTree.ParseText`) omitted from the parser catalog; Wave 8.8 covers delegate/dynamic indirect dispatch. This finding uses ordinary statically bound invocations in the separate semantic-model/speculative-binding family, including APIs already explicitly named in `BannedSymbols.txt`. The live ledger contains no `GetSpeculativeSymbolInfo`, `GetSpeculativeAliasInfo`, `CSharpSemanticModel`, `CSharpExtensions`, or `SemanticModelOptions` finding.

## Wave 21.26. HIGH - Virtual semantic-answer producers are classified from the statically selected declaration, so an override can return Unknown without SPMETA010

- Exact path/functions/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `ResolveProperty`, lines 281-287; `ResolveInvocation`, lines 289-295; `GetReturnedValueNames`, lines 297-327; `IsNonCacheableName`, lines 350-358.
- Detailed mechanism: For an invocation or property read, the rule inspects only `invocation.TargetMethod` or `property.Property`. `GetReturnedValueNames` then scans only that statically selected symbol's declaring syntax. There is no virtual/interface dispatch check and no inspection or conservative join of overrides/implementations. Thus a base virtual method/getter whose declared body returns `Answer.Proven` is classified cacheable even when the runtime receiver is an override whose body returns `Answer.Unknown`.
- Concrete impact: Ordinary subtype substitution can persist Unknown/timeout/failure answers in a semantic cache while the error-severity SPMETA010 boundary emits no diagnostic. A refactor from a sealed producer to a virtual/interface producer silently removes enforcement.
- Safe reproduction/evidence: Analyze `namespace SharpProof.Verify; enum Answer { Proven, Unknown } class AnswerSource { internal virtual Answer Resolve() => Answer.Proven; } sealed class UnknownSource : AnswerSource { internal override Answer Resolve() => Answer.Unknown; } sealed class ProofCache { internal void Write(Answer value) { } } sealed class C { internal void M(ProofCache cache, AnswerSource source) => cache.Write(source.Resolve()); }`. Calling `M(cache, new UnknownSource())` writes `Unknown`. Static analyzer trace: `TargetMethod` is `AnswerSource.Resolve`; returned-name extraction yields only `Proven`; `values.Any(IsNonCacheableName)` is false, so no SPMETA010. The same defect applies to virtual/interface properties via `ResolveProperty`.
- Closest live `BUGS.md` distinction: Wave 6.29 concerns aliases/compound return expressions inside the already selected helper declaration; here that declaration is fully understood and stable, but runtime dispatch executes a different override. Wave 6.28 concerns the cache receiver being interface/base typed, not the semantic-answer producer receiver. Wave 8.8 concerns indirect dispatch for SPMETA001 forbidden APIs, a different rule and call classification.

## Wave 21.27. MEDIUM - Unreachable unsafe helper returns cause a build-blocking SPMETA010 false positive

- Exact path/functions/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `ResolveInvocation`, lines 289-295; `GetReturnedValueNames`, lines 297-327, especially unconditional syntax-descendant collection at 301-312 and name projection at 314-324; `IsNonCacheableName`, lines 350-358.
- Detailed mechanism: Helper return classification scans every arrow expression and return statement syntactically, without a CFG, compiler reachability, or constant-condition evaluation. `ResolveInvocation` treats any extracted unsafe name as sufficient. Therefore an unreachable `return Answer.Unknown` poisons an otherwise always-Proven helper result.
- Concrete impact: SPMETA010 is error severity, so dead fallback/debug code or a constant-disabled return blocks a valid build even though no unstable value can reach the cache. This makes enforcement depend on retaining/removing unreachable syntax rather than runtime semantics.
- Safe reproduction/evidence: Analyze `namespace SharpProof.Verify; enum Answer { Unknown, Proven } sealed class ProofCache { internal void Write(Answer value) { } } sealed class C { const bool UseFallback = false; static Answer Resolve() { if (UseFallback) return Answer.Unknown; return Answer.Proven; } void M(ProofCache cache) => cache.Write(Resolve()); }`. Runtime always returns and caches `Proven`. `GetReturnedValueNames` nevertheless yields both `Unknown` and `Proven`, and `Any(IsNonCacheableName)` reports SPMETA010.
- Closest live `BUGS.md` distinction: Wave 6.29 is the opposite-direction false negative caused by alias/compound-expression name extraction. Wave 14.44 concerns cancellation during the same scanning. Waves 20.32 and 20.33 concern local CFG reaching definitions, not reachability of returns in a called helper. No live entry covers dead/unreachable helper returns causing SPMETA010.

## Wave 21.29. MEDIUM - Linux backslash-bearing paths collapse onto distinct separator paths in compiler-probe provenance

- Exact paths/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `NormalizePath`, lines 458-461; syntax-tree path projection in `CreateSyntaxTreeRow`, lines 241-245; reference `display`/`filePath` projections in `CreateReferenceRow`, lines 363-377; additional-file path projection in `CreateAdditionalFileRows`, lines 424-433. The same normalization is used for generator inputs in `SharpProof.CompilerProbe.TestAsset/CompilerProbeGenerator.cs`, `Initialize`, lines 19-29, and `NormalizePath`, lines 121-124.
- Detailed mechanism: Both helpers unconditionally replace every backslash with `/`. That is appropriate for a Windows directory separator, but the canonical execution host is Linux, where backslash is a legal filename character rather than a separator. Consequently two distinct legal raw paths such as `/tmp/a\\b/SharpProofProbeInput.txt` (one directory component containing a backslash) and `/tmp/a/b/SharpProofProbeInput.txt` normalize to the same string. The snapshot uses only the normalized form for syntax-tree and additional-file path provenance, and normalizes both `Display` and `FilePath` for PE references. With equal source/additional content or equal PE bytes/properties, the complete corresponding rows collide even though Roslyn's actual `FilePath` values and filesystem objects are distinct. For matching probe AdditionalTexts, `Path.GetFileName` sees the same final `SharpProofProbeInput.txt` component in both raw paths, then the generator normalizes both selected paths to the same generated `InputPath`, erasing which legal Linux input supplied it.
- Impact: The package-backed final-compilation oracle can attest the wrong source, generated input, or reference pathname. Distinct compiler inputs can serialize identically while diagnostic/source-location provenance and the actual filesystem object differ, so path cross-wiring on the supported Linux host can pass the probe.
- Safe reproduction/evidence: No repository mutation is needed. Construct two otherwise identical `CSharpSyntaxTree.ParseText` compilations with `path: "/tmp/a\\b/Subject.cs"` and `path: "/tmp/a/b/Subject.cs"`. Roslyn retains the two distinct `SyntaxTree.FilePath`/diagnostic-location paths; `NormalizePath` maps both to `/tmp/a/b/Subject.cs`, and every other field in `CreateSyntaxTreeRow` is equal. The AdditionalText form uses two custom instances with raw paths `/tmp/a\\b/SharpProofProbeInput.txt` and `/tmp/a/b/SharpProofProbeInput.txt` and identical text/metadata; both pass the filename filter and generate/snapshot the same normalized path.
- Closest-entry distinction: Wave 8.3 concerns invalid SARIF URI projection of relative mapped paths; it does not collapse two raw compiler input paths inside the probe. Wave 15.3 concerns the production collector preserving duplicate raw paths and later rejecting its own snapshot; this finding starts with two distinct legal Linux paths and the probe itself makes them identical. Wave 18.51 concerns heuristic generated/handwritten classification, not path identity.

## Wave 21.30. MEDIUM - The package-backed final-compilation test collects reference and additional-input provenance but never binds it to the compiler manifest

- Exact path/members/current lines: `SharpProof.Package.Test/FinalCompilationProbeTests.cs`, `PackedCollectorAttestsAndVerifiesGeneratorOutput`, lines 111-179; `ProbeArtifact` constructor/properties, lines 314-348; `ProbeArtifact.ReadAsync`, lines 363-413, especially loading `portableReferences` and `additionalFiles` at 405-413; `CompilerManifestArtifact` record and `ReadAsync`, lines 486-526.
- Detailed mechanism: The independent probe artifact faithfully exposes `PortableReferences` and `AdditionalFiles`, but the packed end-to-end test never examines either collection. It asserts only generated/handwritten syntax-tree hashes, claim paths, and that the manifest's self-computed `compilationSha256` changes after the generator input changes. The manifest wrapper discards the manifest compilation snapshot entirely: it parses only `compilationSha256` and claim location paths. Therefore no assertion compares the oracle's PE identity/path/hash/aliases or additional-file path/hash with `manifest.compilation.References` or `manifest.compilation.AdditionalFiles`. A defective collector that omits an AdditionalFile (or reference) and recomputes its digest consistently still changes `compilationSha256` when the generated syntax tree changes, satisfying every assertion.
- Impact: The package/release acceptance path can remain green while final-compilation reference closure or generator-input provenance is absent or wrong in the artifact consumed by the worker. The test proves that generated output changed, but not that the collector authenticated the inputs and references that produced/bound that output.
- Safe reproduction/evidence: A safe mutation test can change `CompilerCompilationCapture.Capture` to return `AdditionalFiles = []` while leaving syntax trees intact, then run `FinalCompilationProbeTests.PackedCollectorAttestsAndVerifiesGeneratorOutput` in the canonical tooling container. Empty additional-file arrays are a valid snapshot shape; changing `SharpProofProbeInput.txt` still changes `SharpProofProbe.Contract.g.cs`, so lines 170-178 still observe changed generated-tree and compilation hashes, while lines 111-179 have no assertion capable of noticing the missing additional-file row. An analogous mutation removing an otherwise unused framework-reference row is likewise invisible to the test's oracle/manifest comparison. Static evidence is direct: the only packed-test accesses to `firstOracle`/`changedOracle` are syntax-tree paths/checksums, while `CompilerManifestArtifact.ReadAsync` never reads the compilation snapshot.
- Closest-entry distinction: Wave 18.49 says the probe producer itself omits legal `CompilationReference` objects; Wave 20.25 says retained in-memory PE rows lack byte identity. This finding remains for ordinary file-backed `PortableExecutableReference`s and AdditionalTexts that the probe captures correctly: the acceptance test never compares those correct oracle rows with the manifest at all. Wave 18.50 concerns null AdditionalText content being conflated with empty content inside both producers, not failure to assert cross-artifact provenance.

## Wave 21.31. MEDIUM - Concurrent corpus replay deterministically exercises only the first synthetic seed

- Exact path/function/member/current lines: `SharpProof.Gates/Corpus/CorpusGate.cs`, `CorpusGate.VerifyConcurrentReplayAsync`, lines 439-463, especially selection at 444-448. Supporting construction order: `SharpProof.Gates/Corpus/CorpusCatalog.cs`, `CreateCases` at 19-25 and private `CreateCases(CorpusSeed)` at 259-262.
- Detailed mechanism: The concurrency oracle iterates `CorpusCatalog.Variants` and, for each variant, calls `cases.First(...)` with predicates only for `SyntheticMetamorphic` origin and the variant. `CorpusCatalog` constructs synthetic cases as `Seeds.SelectMany(CreateCases)`, and each seed emits every variant in enum order. Therefore the first matching case for every variant belongs to the first seed, E01. `Task.WhenAll` concurrently replays only the ten E01 variants; the other 27 seeds x 10 variants (270 synthetic cases), including every contracts-mode case and all refuted/unsupported effect shapes, never enter concurrent replay. The returned gate metadata still reports `ConcurrentReplayCount` as `CorpusCatalog.Variants.Length` (10), which describes the small selection without revealing that seed/path coverage is one of 28.
- Impact: Concurrency-sensitive shared/static analyzer state, cache contamination, or nondeterminism that is reachable only through contract analysis, refutation, abstention/Unknown paths, allocation/exception/capability analysis, or any seed after E01 can regress while the corpus concurrency gate remains green. The advertised 280-case synthetic corpus is reduced to ten variants of the same simple proven EnforcePure method for this oracle.
- Safe reproduction/evidence: Read-only/static: `CorpusCatalog.CreateCases` orders cases seed-major because line 23 uses `Seeds.SelectMany(CreateCases)`, and lines 259-262 generate all variants for one seed. Applying the exact lines 444-448 selection returns IDs E01.baseline, E01.rename, ... E01.reorder-independent-statements only. `CorpusGateTests.cs` lines 131 and 136 establish 480 total cases and 28 synthetic seeds, while lines 172-173 merely require both replay counts to be greater than zero. Equivalently, 28*10=280 synthetic cases exist but `selected.Length` is 10, leaving 270 excluded.
- Closest-entry distinction: Wave 5.27 reports that cache/concurrent replay exclude all 200 OpenSource cases; this finding is the independent omission of 270 SyntheticMetamorphic cases even within the origin that `VerifyConcurrentReplayAsync` purports to cover. Wave 10.23 reports that `AlphaRenameContractFormals` is byte-identical to baseline for effect seeds and notes the selected E01 alpha representative; it does not identify that First-by-variant makes every selected concurrent case E01 or that all other 27 seed families and the entire contracts mode are omitted. No current live `BUGS.md` entry covers this seed-selection collapse.

## Wave 21.32. HIGH - Qualification receipt validation and hashing can bind different evidence generations

- Exact paths/members/current lines: `scripts/Write-SharpProofQualificationReceipt.ps1`, top-level evidence validation and receipt construction: evidence pathname resolution/read/JSON parse at lines 20-30; semantic `$valid` switch at lines 56-97; later independent pathname metadata/hash reads at lines 114-125, especially `Get-Item` 121 and `Get-FileHash` 122-124. Downstream: `scripts/Invoke-SharpProofReleaseContainer.ps1`, `WriteQualificationEvidence` receipt loop lines 179-210, especially evidence checks at 184-196.
- Detailed mechanism: The writer validates one in-memory JSON generation returned by `Get-Content | ConvertFrom-Json`, closes that read, and retains only the mutable pathname. After deciding `$valid`, it reopens that pathname separately for length and SHA-256. An atomic rename/replacement between lines 98 and 121 therefore makes the passed receipt's `status='passed'` describe generation A's semantics while `evidence.bytes`/`sha256` authenticate generation B. The final release consumer never parses B or repeats the gate-specific semantic validation; it only reopens the named path and compares length/hash to the receipt. Thus B is accepted precisely because the writer authenticated its bytes after validating A. Separate `Get-Item` and `Get-FileHash` calls also permit a further length/hash torn read, though the A/B semantic-to-digest split alone is sufficient.
- Concrete impact: Concurrent gate reruns, cleanup/publication, or a controlled replacement can cause final release qualification to accept unvalidated, failed, malformed, or cross-generation evidence as a passed gate. This is a release-authority integrity failure and remains even if the currently weak gate-specific predicates are strengthened.
- Safe reproduction/evidence: In a disposable repository copy, provide an evidence file containing a genuinely valid gate result and pause after `$valid` is computed at line 98 (debugger breakpoint or focused test seam). Atomically replace the evidence pathname with arbitrary different JSON, then resume. The receipt records `status=passed` from the old object but the new file's length/hash. Leave the replacement in place and run/read the final consumer: lines 184-196 accept because they check only the receipt plus the replacement's matching bytes/hash and never semantically parse the evidence. Static source trace establishes the two independent opens and absence of a shared snapshot/handle.
- Closest live `BUGS.md` distinction: Wave 19.44 says the writer's gate predicates accept self-asserted JSON; this finding is a separate validate/reopen race and still bypasses a fully rigorous semantic validator because the bytes hashed into the receipt were not the bytes validated. Wave 18.59 is the inverse fuzz-artifact split: its campaign hashes validated old bytes but leaves a cited path replaceable, producing a hash/path mismatch; here the qualification writer hashes the replacement itself after validating old bytes, so the downstream hash check positively authenticates the unvalidated replacement. Initial item 15 and other TOCTOU entries concern executed binaries, not semantic release evidence.

## Wave 21.33. MEDIUM - A same-seed retained fallback double-counts an exact rotating-run prefix as new campaign cases

- Exact paths/members/current lines: `scripts/Invoke-SharpProofFuzzCampaign.ps1`, `$retainedRunSeeds` filtering lines 65-68, campaign budget lines 69-73, rotating/retained scheduling lines 182-191, and summary `requestedCases`/`totalCases` lines 193-207. Determinism source: `Tools/SharpProof.Fuzz/FuzzRunner.cs`, per-index seed creation/consumption lines 149-155 and 191-225, `CreateCaseSeed` lines 552-555.
- Detailed mechanism: When the rotating seed equals a retained seed, the filter skips the retained run only if `effectiveRotatingCases >= effectiveRetainedCases`. If the rotating run is shorter, it schedules a second retained run from case index zero with the identical seed and full retained count. `FuzzRunner` is deterministic and uses `CreateCaseSeed(seed,index)` for all three oracle inputs, so every rotating case is exactly repeated as the prefix of the retained run. Nevertheless the budget and final summary add both full run counts.
- Concrete impact: An allowed campaign can report and budget N+M requested/observed cases while executing only max(N,M) distinct semantic cases for that seed. This overstates fresh frontend, finite-domain SMT, and partial-term coverage and consumes campaign wall time without exploring the missing distinct cases.
- Safe reproduction/evidence: With the checked-in retained seed 23063, invoke a disposable campaign with `-RotatingSeed 23063 -RotatingCases 500 -RetainedCases 1000`. Lines 65-68 retain seed 23063 because 500 < 1000; scheduling produces `rotating-23063` indices 0..499 and `retained-23063` indices 0..999. For each i in 0..499, both runs compute the identical `CreateCaseSeed(23063,i)` and therefore identical inputs to all three oracles, while `requestedCampaignCases` and `totalCases` report 1,500 rather than 1,000 distinct cases. No mutation is required; equality follows directly from the deterministic code and is also consistent with `FixedSeedIsDeterministicAndSound` in `SharpProof.Fuzz.Test/FuzzRunnerTests.cs` lines 65-72.
- Closest live `BUGS.md` distinction: Wave 20.34 is cross-night overlap caused by calendar seeds separated by 397; this is same-campaign duplication caused by the rotating/retained fallback branch and occurs even without calendar defaults. Wave 14.50 is only the partial-term generator's eight-case state space; this duplicates frontend and finite-domain inputs too. Wave 18.58 concerns simultaneous campaign writers colliding in one output namespace; these two runs are intentionally sequential and use distinct `rotating-`/`retained-` filenames.

## Wave 21.34. MEDIUM - Replayable refutations with any non-scalar input are systematically excluded from the verification cache

- Exact paths/members/current lines: `SharpProof.Worker/CallableEvidenceBuilder.cs`, `CallableEvidenceBuilder.Build`, replay-variable selection at lines 181-193 (only Boolean/Integer Receiver or Parameter variables are exported). `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`, `TryCreateModel`, lines 768-819, especially required-variable filtering at 782-789 and completion check at 806-815 (only Boolean/Integer Receiver or Parameter variables are required, matching production model emission). `SharpProof.Worker/VerificationCache.cs`, `VerificationCache.IsCacheable`, lines 408-435; `ReplayCachedClaims`, lines 438-485; `TryCreateModel`, lines 487-528, especially the all-input loop at 513-523. Write-side caller: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`, cacheability/write decision at lines 324-336.
- Detailed mechanism: Production proof/model construction deliberately limits replay variables to Boolean and Integer receiver/parameter variables. The artifact-aware response authority applies the same closure: it requires all such scalar inputs and permits a method to have other input variables that are absent from the model. Concrete replay can still be exact when a reference/sequence/string input is unused by the admitted body and postcondition. The cache has a stricter, inconsistent closure. After parsing the model, `VerificationCache.TryCreateModel` loops over every canonical Receiver or Parameter and returns false whenever that variable's type is neither the factory Boolean nor Integer type, even if the variable is unused and correctly absent from the response model. `IsCacheable` invokes this path for both a prospective write and a hit. Thus the mere presence of one non-scalar input makes an otherwise protocol-valid, artifact-authority-valid, concretely replayed Refuted response noncacheable.
- Concrete impact: Valid replayable counterexamples for common methods with an unused `object`, `string`, array/sequence, or reference receiver never get written to or served from the cache. Repeated identical builds rerun the solver/body verification and continue reporting an ordinary miss instead of a write/hit. This defeats the cache for a broader input surface than the documented scalar proof domain requires, and the exclusion is silent and unrelated to semantic replayability.
- Safe reproduction/evidence: Use a static selected method such as `static int Bad(object unused) { Contract.Ensures(Contract.Result<int>() > 0); return 0; }` with cache enabled. The unused reference parameter does not occur in the lowered body or claim. A satisfiable backend model is therefore empty; `CallableCounterexampleReplayer` executes the exact constant-return body and establishes the false postcondition, and `CompilerResponseEvidenceAuthority.TryCreateModel` accepts the empty model because its `required` set contains only Boolean/Integer inputs. At the write decision, `VerificationCache.IsCacheable` reaches `TryCreateModel`; its lines 513-523 enumerate `unused`, observe a non-Boolean/non-Integer type, and return false. A second identical run consequently recomputes and again cannot write/hit. The same can be shown directly with the existing internal target fixtures by adding one unused reference-typed canonical Parameter to an otherwise replayable constant refutation: response-authority validation remains valid while `VerificationCache.IsCacheable` is false.
- Closest-entry distinction: Wave 18.38 concerns actual cache read/I/O/corruption failures being reported as ordinary misses for noncacheable outcomes; this finding needs no cache failure and starts from a valid replayable Refuted outcome, but an inconsistent model-closure predicate prevents both write and hit. Wave 13.1 concerns capacity reconciliation being skipped after misses or genuinely noncacheable results; this finding is the prior semantic classification error that wrongly makes a replayable result noncacheable and does not depend on the byte cap. Wave 5.27 concerns corpus-gate test selection excluding OSS methods from replay, not production `VerificationCache` model binding. Final live `BUGS.md` cross-check found no entry for non-scalar/unused inputs disabling cached counterexample replay.

## Wave 21.35. HIGH - Release gates authenticate only DLL/SO archive entries, so executable package build files can be substituted before evidence generation

- Exact paths/members/current lines: `scripts/Test-SharpProofPackagePayloads.ps1`, `Test-SharpProofPackagePayload`, lines 111-281, especially payload selection at 161-180 and per-entry validation at 190-270; `scripts/New-SharpProofReleaseEvidence.ps1`, package validation/evidence creation at 648-663 and whole-archive self-hash at 732-748; downstream consumers `scripts/Test-SharpProofReleaseArtifacts.ps1`, payload revalidation at 147-187, and `scripts/Publish-SharpProofRelease.ps1`, `Get-ValidatedRelease`, lines 385-476, especially 453-468.
- Detailed mechanism: `Test-SharpProofPackagePayload` defines the package payload closure as archive entries whose names end only in `.dll` or `.so` (lines 163-168). It compares and authenticates only those binary entries against repository outputs/evidence. NuGet identity/dependency/license validation separately reads the embedded nuspec, but no release-path validator binds other shipped archive entries to repository sources. In particular, `buildTransitive/SharpProof.props`, `buildTransitive/SharpProof.targets`, the verifier package's buildTransitive files, `RelationalSpecPackCatalog.json`, README, notices, and other non-DLL/SO content are outside the authenticated payload graph. Evidence generation then hashes the already-mutated entire archive and records that self-derived hash in the release manifest, so subsequent artifact validation and publication merely confirm the malicious archive equals its own generated manifest. An archive mutation after package tests but before evidence generation is therefore promoted into canonical evidence.
- Impact: A substituted `.props`/`.targets` entry can execute arbitrary MSBuild targets in every restoring consumer, remove or replace analyzers, or disable verification, yet release evidence generation, release artifact validation, and publication accept and seal the package. This is a release supply-chain integrity failure, not just missing test coverage.
- Safe reproduction/evidence: In a disposable copy of the six canonical artifacts, rewrite only `buildTransitive/SharpProof.targets` inside `SharpProof.<version>.nupkg` (for example add a harmless property-setting target or an `<Error Text="fixture" BeforeTargets="CoreCompile"/>`), leaving the nuspec and all `.dll`/`.so` bytes unchanged. Run release evidence generation against that copied package directory, then validate its generated release bundle. Static trace shows the changed entry never enters `$payloadEntries`; binary hashes, package identity/dependencies, symbol pairing, and third-party inventory remain unchanged; lines 732-748 hash and bless the mutated archive. A consumer restoring the resulting package evaluates the injected target.
- Closest-entry distinction: Wave 5.23 consolidates weaknesses in `PerformanceGate.ValidateAdvisoryPackagePolicy` while inspecting repository/source MSBuild documents (ignored executable props work, disabling conditions, and imports). This finding is the independent post-pack provenance boundary: even if that source policy validator is fixed and passes the genuine source files, the release scripts do not prove the nonbinary files actually inside the `.nupkg` are those validated source files. Live `BUGS.md` has no archive-to-source binding finding for executable nonbinary package content.

## Wave 21.36. HIGH - Staged NuGet packages can be replaced after the last hash check and before `dotnet nuget push`

- Exact paths/members/current lines: `scripts/Publish-SharpProofRelease.ps1`, `New-SharpProofPublicationStage`, lines 727-789, especially copy and mode change at 745-753; publication loop at 985-1007, especially validation at 987-988 followed by main push at 992-996 and validation at 997-998 followed by symbol push at 1002-1006; `Invoke-NuGetPush`, lines 688-725, reopening the package pathname through dotnet at 719. Validator: `scripts/SharpProof.PublicationPlanIdentity.psm1`, `Test-SharpProofPublicationPlanIdentity`, lines 33-433, especially pathname hash at 226-242.
- Detailed mechanism: Each pre-push `Test-SharpProofPublicationPlanIdentity` opens and hashes the staged package by pathname, closes it, and returns. `Invoke-NuGetPush` subsequently passes only that pathname to a new dotnet process, which reopens it. No retained handle, file identity comparison after launch, or immutable filesystem boundary ties the validated bytes to the bytes uploaded. `chmod 0400` protects contents from ordinary writes but does not prevent the owning release user from atomically renaming/unlinking the file inside the owner-writable 0700 staging directory. The `Write-Host` between validation and invocation widens an observable synchronization point. A replacement with the same package ID/version but arbitrary contents is accepted by the registry command; the plan object remains unchanged.
- Impact: The publisher can upload bytes different from every hash, payload, SBOM, and publication-plan check while reporting a successful release. A same-user concurrent process, compromised build step, or accidental staging mutation can therefore publish an uncertified package under the certified identity.
- Safe reproduction/evidence: Use the existing mock-dotnet publication harness or a debugger seam. Pause after line 988 (or 998), atomically rename a different valid same-ID/version `.nupkg`/`.snupkg` over `$package.mainPath`/`symbolsPath`, then resume. The validation has already succeeded; line 719 opens the replacement and the mock records replacement bytes. Mode 0400 does not block rename by the owner because directory write permission controls replacement. No network is needed with the mock destination.
- Closest-entry distinction: Wave 20.24 is replacement/spoofing of the `dotnet` executable and exposure of the API key; this finding leaves the genuine pinned dotnet host intact and replaces its package data input after validation. Wave 14.36 is the analogous staged worker-DLL execute-after-hash race in verifier execution, not release publication. Wave 20.23 concerns irreversible partial publication after a push failure, not pushing bytes that differ from the certified plan. No live entry covers the package pathname check/use gap at NuGet push.

## Wave 21.37. MEDIUM - The duplicate acceptance-contract property fixture is invalid JSON, so it passes without exercising duplicate-key rejection

- Exact paths/members/current lines: `scripts/Test-SharpProofDocumentationSupportFixtures.ps1`, `duplicate-acceptance-property` mutation, lines 134-138; `scripts/Generate-Readme.ps1`, acceptance contract parse at 756-757 and `mutationParallelism` consumption at 866-879; test oracle `SharpProof.ArchitectureTest/DocumentationSupportContractTests.cs`, case declaration at line 23 and exit-code-only assertion in `DocumentationSupportContractRejectsDrift`, lines 31-60.
- Detailed mechanism: The replacement text at fixture line 137 is a single-quoted PowerShell string: `'"mutationParallelism": 99,`n        "mutationParallelism": 4'`. In single quotes, backtick is literal, so the output contains the two raw characters backtick and `n`, not a newline. The resulting JSON is syntactically invalid and `ConvertFrom-Json` fails before any duplicate-property behavior is exercised. The NUnit oracle checks only nonzero exit and therefore marks that unrelated parse failure as successful duplicate-key rejection. With an actual newline, PowerShell `ConvertFrom-Json` accepts duplicate keys and retains the last value. Because the fixture orders `99` first and canonical `4` last, `Generate-Readme` consumes 4 and can pass verification despite the duplicate authority row.
- Impact: The advertised exact acceptance-contract/documentation authority is not tested: a valid JSON document with duplicate `mutationParallelism` properties can pass the documentation gate, leaving ambiguous source authority and parser-dependent behavior. The test currently gives false assurance that duplicates are rejected.
- Safe reproduction/evidence: Read-only host probe: `$x = '{"a":1,' + [char]10 + '"a":2}'; ($x | ConvertFrom-Json).a` returns `2`, whereas `$bad = '{"a":1,`n"a":2}'; $bad | ConvertFrom-Json` throws `Invalid property identifier character: backtick`. A disposable override of `eng/acceptance/contract.json` containing real-newline rows `"mutationParallelism": 99,` then `"mutationParallelism": 4` reaches lines 866-879 with value 4; the checked-in fixture never constructs that input.
- Closest-entry distinction: Wave 18.61 is a release-configuration oracle that inspects only the first REST pagination page; it does not concern local JSON mutation syntax or duplicate-property parsing. Wave 15.24 and Wave 15.25 are other false-positive fixture mechanisms (dot-sourcing-only and payload-authentication-before-condition), but affect fuzz decoding and conditional attribute elision respectively. Live `BUGS.md` has no documentation-support fixture or acceptance-contract duplicate-key entry.

## Wave 21.38. HIGH - Effect replay event semantics are self-asserted and can fabricate a worker-replayable allocation violation at any valid source span

- Exact paths/members/current lines: `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs`: `HasValidReplayGeometry`, lines 42-98; `HasValidReplayEvent`, lines 158-193; `ComputeReplayOperationSha256`/`AddReplayEvent`, lines 225-234 and 274-315. Honest producer contrast: `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs`, `TryCreateEvent`, lines 83-135 (Roslyn operation determines event kind/member/type identities), and event projection lines 147-170. Downstream worker trust: `SharpProof.Worker/EffectCounterexampleReplayer.cs`, `ValidateEvent`, lines 59-115; `Interpret`, lines 117-155; `IsViolation`, lines 158-169. Result manifestation: `SharpProof.Worker/EffectClaimResultAssembler.cs`, `Assemble`, lines 80-103.
- Detailed mechanism: The honest lowerer derives `ManagedObjectAllocation` versus `ManagedArrayAllocation`, member identity/documentation ID, and type identity/documentation ID from a concrete Roslyn `IObjectCreationOperation` or `IArrayCreationOperation`. None of that compiler semantic relationship survives as independently checked authority. The codec only checks that an event kind is supported, indices/hashes/span/location have valid geometry, object events have any nonblank `MemberIdentity` (array events have an empty one), identity strings have superficial optional-text shape, and `OperationIdentitySha256` equals a hash recomputed from those same attacker-controlled fields. It never proves that the captured syntax at `SyntaxStart`/`SyntaxLength` is an allocation, that `Kind` matches its operation, or that member/type identities describe the symbol at that span. The compilation snapshot authenticates only whole-tree text SHA and a line map; it contains no text bytes or operation inventory from which hydration can recover this binding. `EffectCounterexampleReplayer.ValidateEvent` repeats only tree/location geometry. `Interpret` then trusts `Kind` and the self-asserted identity fields to manufacture an `Allocates` witness, and `IsViolation` treats that witness as a definite `ZeroAllocations` (or disallowed-Allocates `EffectContract`) violation. Thus the actual worker replay, not merely a launcher-side validator, can validate fabricated semantic evidence.
- Concrete impact: A corrupt/resealed compiler artifact can change a genuinely Proven `ZeroAllocations` claim into `Refuted/DefiniteViolation`, place a fictitious managed-allocation event on any nonempty valid span in the callable's captured tree (even an attribute/method-name span with no allocation), and make the worker emit a replay-backed Refuted result. This can falsely fail a build while all canonical hashes, feature scope, tree geometry, hydration checks, effect authority equality, and worker counterexample replay succeed. It also permits arbitrary allocation kind/member/type/detail attribution.
- Safe reproduction/evidence: Start from the existing in-memory artifact fixture for a pure selected `[ZeroAllocations]` method. Keep its compilation snapshot and manifest claim. Replace that claim's compiler effect evidence with `Outcome=Refuted`, `Reason=None`, `Certainty=DefiniteViolation`, the ordinary empty ZeroAllocations constraint, and an unconditional one-event replay. Bind the event's syntax/source ordinals and all tree/line-map hashes to the claim's unique tree; set `SyntaxStart`/`SyntaxLength` and event/witness `Location` to the claim's existing nonempty valid location; set `Kind=ManagedObjectAllocation`, any nonblank `MemberIdentity`/`TypeIdentity`, empty operand/exception arrays, and a matching `managed-allocation` witness with `Effects=Allocates`. Mirror the changed evidence into `EffectAuthorities`, call `CompilerEffectClaimArtifactCodec.Seal`, and recompute `FeatureScopeSha256`. `CompilerManifestArtifactJson.Serialize`/`Deserialize` and `DecodeCallables` accept it: the geometry predicates see a real tree/span but never inspect the operation. `EffectCounterexampleReplayer.Replay` returns the fabricated witness because `Interpret` maps the asserted kind directly to `Allocates`; `EffectClaimResultAssembler.Assemble` returns Refuted at lines 96-103. Using the method/attribute span demonstrates the claimed operation need not exist there.
- Closest-entry distinction: Wave 14.6 says the witness is not bound to its replay; its example changes an object-allocation replay's witness to an array witness, which the actual worker replayer detects and downgrades. Here witness and replay match exactly, but the replay event itself is not bound to the compiler operation at the authenticated span, so the actual worker replayer accepts the false violation. Wave 7.6 splices syntax provenance from one tree with mapped source provenance from another. This finding uses one correctly bound tree, identical ordinals/hashes, and valid geometry; the missing authority is semantic operation kind/symbol binding. Wave 3.25 concerns `EffectAuthorities` being outside feature-scope hashing/manifest validation. This finding persists with a perfectly matching authority and recomputed feature hash because both copies repeat the same self-asserted event. Wave 20.35 is an EnforcePure disagreement between response authority and worker replay for a genuine allocation. This finding uses ZeroAllocations, for which both worker replay and response authority agree, but the underlying allocation event is fabricated. No live `BUGS.md` entry binds replay event kind/member/type semantics to the Roslyn operation at its captured span.

# Read-Only Multi-Agent Bug Audit - Wave 22 - 2026-08-29

## Wave 22.1. MEDIUM - Compiler-synthesized derived constructors never check mandatory base-constructor Requires

- Exact files/members/current lines: `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`, `Initialize`, lines 150-175 (contracts register syntax actions only for primary constructors and member initializers, plus operation-block actions); `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `AnalyzeOperationBlock`, lines 170-216, especially the declarationless-method exit at 203-208, and `AnalyzePrimaryConstructor`, lines 376-411 (only `PrimaryConstructorCallableInventory.TryGet`); `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, `TryGetImplicitParameterlessBaseConstructor`, lines 382-413, especially the required `ConstructorDeclarationSyntax` at 386-389; `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, `Analysis.AnalyzeCallSite`, lines 264-327, especially exact candidate binding at 276-300.
- Mechanism: For `sealed class Derived : Base { }`, C# synthesizes `Derived..ctor()` and that constructor necessarily executes implicit `base()`. It has no `ConstructorDeclarationSyntax`; the primary-constructor syntax action does not select it, and any declarationless operation-block path is discarded/abstained. The only Requires-specific omitted-base discovery helper is hard-gated to an explicit `ConstructorDeclarationSyntax`, so it is never invoked for the synthesized constructor. Separately, a caller's `new Derived()` candidate binds only the exact implicit `Derived..ctor`; `AnalyzeCallSite` does not follow its mandatory base-constructor chain, so the direct Requires on `Base..ctor()` is absent.
- Impact: A definitely executed, deterministically false base-constructor precondition receives no SP0027 anywhere on an ordinary implicit-derived-constructor path. The construction caller can remain `NotApplicable` even though runtime enters `Base..ctor` and executes the failing `Contract.Requires`.
- Safe reproduction/evidence: Analyze under contracts/full activation: `using SharpProof.Attributes; class Base { public Base() { Contract.Requires(false); } } sealed class Derived : Base { } static class C { static object Make() => new Derived(); }`. The creation target is the compiler-synthesized `Derived..ctor`, whose clause inventory is empty; there is no derived constructor declaration to reach lines 382-413, and the call-site binder never switches to `Base..ctor`. Add only `public Derived() { }`: now the declared constructor reaches `TryGetImplicitParameterlessBaseConstructor`, selects the real zero-parameter `Base..ctor`, and the same false Requires is reportable. This is a safe source-only analyzer reproduction; no malformed/error code is required.
- Closest-entry distinction: Wave 18.7 covers an explicit declared constructor that does enter the omitted-base helper, but the helper incorrectly rejects an optional/params base constructor because its formal parameter list is nonempty. This case uses a true zero-parameter base constructor that the helper would accept, but no declared derived constructor exists, so the analysis is never dispatched. Wave 14.12 covers the same synthesized-constructor language shape only in Effects completion/exception reachability, not Requires discovery or SP0027. Wave 18.6 is virtual/interface contract-owner association, not mandatory constructor chaining.

## Wave 22.2. HIGH - Lifted nullable user-defined conversions are treated as unconditional operator calls, erasing real normal-path effects and fabricating catch reachability

- Exact files/members/current lines: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteConversion`, lines 1011-1029, especially 1018-1029; `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanConversion`, lines 456-477, especially 471-477; `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions` conversion arm, lines 626-649; existing but conversion-incomplete helper `SharpProof.Effects/ConversionEffectClassifier.cs`, `SkipsLiftedOperator`, lines 107-129 (recognizes lifted binary, unary, increment, and compound assignment, but no `IConversionOperation`); downstream suppression in `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`, lines 351-373, especially 367-373.
- Mechanism: C# lifts a user-defined value conversion such as `int -> S` to `int? -> S?`. When the nullable source has no value, runtime produces null and does not call `S.op_Implicit`; when source nullability is unknown, this is still a valid normally completing path. Roslyn retains the underlying `OperatorMethod` on the `IConversionOperation`. `ScanConversion` nevertheless resolves that method unconditionally, `CanCompleteConversion` calls `CanCompleteInvocation` unconditionally after its unrelated constant-null/value-type special case, and exception reachability unconditionally adds the operator's exceptions whenever the operand expression can complete. `SkipsLiftedOperator` already encodes the skip rule for four operator operation kinds but omits conversions. Therefore an always-throwing/diverging underlying conversion is treated as mandatory even for a nullable-null path.
- Impact: A real normal path after the lifted conversion can be suppressed from the effect CFG. Later writes, allocations, calls, and exceptions disappear, permitting unsound no-write/purity/allocation conclusions. For definitely-null input, impossible operator throws also make catch handlers falsely reachable and can add handler effects that cannot execute.
- Safe reproduction/evidence: `struct S { public static implicit operator S(int _) => throw new InvalidOperationException(); } static int state; static void M(int? value) { S? converted = value; state++; }`. For `value == null`, C# skips `op_Implicit`, produces null, and increments `state`; therefore the conversion as an operation may complete. Current `CanCompleteConversion` sees no operand `ConstantValue`, invokes the source operator completion analysis, returns false, and `AnalyzeControlFlowGraph` does not retain the suffix. A companion `try { S? converted = value; } catch (InvalidOperationException) { state++; }` has an unreachable handler for definitely-null `value`, but the conversion arm adds the operator exception. A read-only Roslyn 4.14 probe against the repository package confirmed the initializer is `IConversionOperation`, operand type `int?`, result type `S?`, `OperatorMethod = S.implicit operator S(int)`, and operand `ConstantValue.HasValue == false` (Roslyn's `Conversion.IsNullable` is also false, so the fix must infer lifting from nullable source/result plus the underlying method rather than rely on that flag alone).
- Closest live `BUGS.md` distinction: Wave 19.25 covers the same lifted-null skip law only for `IBinaryOperation`, `IUnaryOperation`, `IIncrementOrDecrementOperation`, and `ICompoundAssignmentOperation`; its exact file/member list excludes `ScanConversion`, `CanCompleteConversion`, and the conversion arm of exception reachability, and the helper it cites demonstrably omits `IConversionOperation`. Wave 5.10 is the separate blanket `ConstantValue == null`/value-type-result gate affecting literal/default nullable conversions and non-lifted null-to-struct user conversions; this reproduction deliberately uses a nullable local/parameter with no constant value and an actually lifted conversion whose underlying operator must be skipped. Wave 18.10 concerns delegate-overwrite reachability around a conversion that really executes and may throw, not a lifted conversion that conditionally does not execute. Final live `BUGS.md` cross-check found no lifted user-defined conversion entry.

## Wave 22.3. MEDIUM - Undefined precondition/effect obligations are mislabeled as counterexample replay corruption

- Exact paths/members/current lines: `SharpProof.Verify/Evidence.cs`, `ProofDiagnosticKind`, lines 13-19 (declares `EffectContract` and `Precondition` alongside `Postcondition` and `InternalConsistency`); `SharpProof.Verify/Outcomes.cs`, `AbstentionReason`, lines 3-17 (has only `PostconditionMayBeUndefined` and `InternalConsistencyMayBeUndefined`); `SharpProof.Verify/ProofKernel.cs`, `ProofKernel.ReplayCounterexample`, lines 47-92, especially the exception-to-reason switch at 75-86 and fallback at 85. Downstream projection: `SharpProof.Worker/WorkerProjections.generated.cs`, `MapAbstention`, lines 28-42, especially `CounterexampleReplayFailed` at 39; `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `Classify`, lines 112-120, especially fatal projection at 118-119.
- Mechanism: The SMT obligation deliberately searches for `!(goal.Defined && goal.Value)`, so a legitimate SAT model may witness that a partial goal throws rather than evaluates false. Kernel replay correctly detects `IrEvaluationStatus.Exception`, but it assigns a semantic undefined reason only when `Goal.Diagnostic` is `Postcondition` or `InternalConsistency`. The other two declared goal kinds fall through to `CounterexampleReplayFailed`, a reason reserved for an exact model/replay inconsistency. Thus the same partial predicate and real model are typed as semantic uncertainty for two diagnostic kinds and as replay corruption for the other two.
- Concrete impact: A valid partial `Precondition` or `EffectContract` obligation cannot produce a truthful typed-undefined outcome. Direct kernel consumers receive the corruption reason, and any worker projection of that result makes the entire run fail as `WorkerRunFailureReason.CounterexampleReplayFailed` instead of remaining a semantic `Unknown`. Current checked-in worker producers submit only `Postcondition` and `InternalConsistency` goals, so today's packaged postcondition path avoids the trigger; the defect is observable through the accepted kernel/query surface and makes the declared `Precondition`/`EffectContract` cases unsafe to use.
- Safe reproduction/evidence: With the real `IrSmtBackend`, create integer variable `d` and goal `0 / d == 0`, no assumptions, diagnostic `ProofDiagnosticKind.Precondition`, and request `d` as a model variable. For every nonzero `d` the goal is true, while at `d=0` it is undefined, so the backend's negated-defined-goal formula is satisfiable specifically with `d=0`. `IrInterpreter` then reports `Exception`, and lines 79-86 return `UnknownOutcome(CounterexampleReplayFailed)`. Change only the diagnostic to `Postcondition`; the identical term/model returns `PostconditionMayBeUndefined`. `EffectContract` follows the same erroneous default arm. This can be encoded as a targeted test without malformed input or repository mutation.
- Closest live `BUGS.md` distinction: Wave 18.36 covers ordinary exceptions escaping `ISmtBackend.CheckAsync` instead of entering the typed `InfrastructureFailure` channel; here the real backend succeeds and returns a valid SAT model, and only semantic exception classification during kernel replay is wrong. Wave 19.28 covers already-typed fatal backend abstentions being collapsed to callable `SemanticUnknown`; this is the inverse boundary, where a legitimate semantic undefined goal is first mislabeled as fatal replay corruption. Wave 4.5 concerns effect-event replay lacking capability/exception witnesses, not an SMT `EffectContract` goal's partial Boolean evaluation. Live ledger cross-check found no entry for `ProofDiagnosticKind.Precondition`/`EffectContract` falling through the undefined-goal switch.

## Wave 22.4. HIGH - Explicitly selected generated methods silently drop reachable nested-callable precondition violations

- Exact paths/members/current lines: `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `AnalyzerFeaturePipeline.AnalyzeOperationBlock`, selection/generated gate at lines 223-233 and call-site analysis at 298-308. `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`, `TreeAnalysis.AnalyzeGraph`, lines 146-206, especially the root-only exception at 156-159 and unconditional nested generated skip at 198-206; `RecordGeneratedSubtree`, lines 258-275. Generated classification: `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs`, `IsGenerated(SyntaxTree,...)`, lines 48-66. Existing intended-behavior contrasts: `SharpProof.Analyzer.Test/GeneratedCodeAnalyzerTests.cs`, `SelectedGeneratedMethodIsAnalyzedAndReported`, lines 12-40; `SharpProof.Analyzer.Test/NestedRequiresCallSiteTests.cs`, `BlockAndExpressionBodiedLocalFunctionsAreAnalyzed`, lines 12-39.
- Mechanism: `AnalyzeOperationBlock` deliberately lets a generated method through when it has any selected feature: its generated guard is conditioned on `!selection.Any`. The Requires tree pass also deliberately exempts the root from generated exclusion (`!isRoot && IsGenerated`), but when it enumerates reachable local functions/lambdas it applies `IsGenerated` unconditionally. In a `.g.cs` or exact-header tree, every reachable nested callable is therefore sent to `RecordGeneratedSubtree` and never receives its child CFG analysis, even though it is executable body of the explicitly selected generated root. The local is recorded `NotApplicable`; the root can remain free of both the definite call-site diagnostic and an incomplete outcome.
- Impact: A definite closed/source `Contract.Requires` violation inside a reachable local function or executable lambda of an explicitly selected generated method is silently omitted. This contradicts the established policy that selected generated methods are analyzed and makes explicit selection depend on whether code was factored into a nested callable. It is an analyzer soundness false negative (no SP0027) for generated code the user/generator explicitly opted into analysis.
- Safe reproduction/evidence: Analyze a file `Selected.g.cs` under advisory + `SharpProofFeatures=contracts`: `// <auto-generated />\nusing SharpProof.Attributes; internal static class G { static int Guard(int v) { Contract.Requires(v > 0); return v; } [return: Positive] internal static int Selected() { int Local() => Guard(-1); return Local(); } }`. The return attribute selects `Selected`, so `AnalyzeOperationBlock` bypasses lines 225-233. `GetNestedCallables` finds the reachable `Local`, but lines 203-206 classify the `.g.cs` tree generated and skip its CFG; no SP0027 reaches `Guard(-1)`. The same source at a non-generated path takes the existing nested-callable path and reports SP0027 (the ordinary behavior is directly covered by `NestedRequiresCallSiteTests` lines 12-39).
- Closest live entry distinction: Wave 21.9 is the opposite policy leak: malformed controls on otherwise unselected generated ordinary declarations can report SP0024 before the generated guard. This finding starts from a valid explicitly selected generated root that is correctly admitted, then over-applies generated suppression only to its reachable nested body and loses SP0027. Wave 18.12 concerns effect attributes placed directly on lambda symbols never entering the effect feature pipeline; this finding needs no nested annotation and concerns Requires call-site traversal inherited from a selected ordinary root. No live entry covers `RecordGeneratedSubtree` truncating an explicitly selected generated root.

## Wave 22.5. HIGH - `SharpProofSuppress` is discarded at compiler-manifest collection, so suppressed claims still fail verifier builds

- Exact paths/members/current lines: live analyzer honor point `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs`, `ValidateAndShouldSuppress`, lines 5-17, called from `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, `GetSelection`, lines 708-726. Divergent compiler path: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`, `BuildTarget`, lines 49-136 (claims/selection/effects at 56-99), `SelectFeatures`, lines 229-248, and `CreateEffectClaims`, lines 307-350. Downstream unconditional failure: `SharpProof.Worker.Launcher/Program.cs`, `ValidateAndReport`, claim/refutation collection at lines 394-405 and return at 432-437. Contract statement: `docs/public-api.md`, reporting controls at lines 72-76. Existing narrow test: `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs`, `SuppressionAloneDoesNotSelectCallable`, lines 1257-1271 (does not combine suppression with a selected claim).
- Mechanism: The live analyzer enumerates method/property/type/assembly scopes and converts a valid `SharpProofSuppress` into `selection.IsSuppressed`, stopping reporting. `ClaimManifestBuilder` never performs that control-policy check. It creates postconditions, selected features, assumptions, and effect claims solely from contract/effect/trust inventories. `ContractSelectionInventory.Select` does not remove selected features in the presence of suppression, and the manifest carries no reporting-suppression bit for later consumers. Thus suppression alone stays unselected (the existing test), but suppression combined with any real contract/effect annotation emits the same callable/claim as if suppression were absent. The worker and launcher cannot recover the lost intent; any Refuted claim makes the launcher return 5 regardless of verify policy.
- Impact: The public reporting control is honored by the IDE/live analyzer but ignored by the opt-in compiler/worker path. A method, type, or assembly explicitly suppressed with a documented reason can still emit SARIF/refutation output and fail the build; because `refuted` wins unconditionally, this can fail even advisory verifier runs, not only strict `require-proven` runs. This makes suppression non-compositional across the product's two analysis/reporting paths.
- Safe reproduction/evidence: Build an in-memory collector fixture with effects enabled containing `using SharpProof.Attributes; internal static class G { [SharpProofSuppress("reviewed generated allocation")] [ZeroAllocations] internal static object M() => new object(); }`. The live analyzer's `GetSelection` reaches `ValidateAndShouldSuppress` and emits no effect diagnostic. In `ClaimManifestBuilder`, `SelectFeatures` still returns Effects, `EffectContractDiagnostics.Evaluate` observes the definite managed allocation, and `CreateEffectClaims` emits the ZeroAllocations claim/evidence because no suppression check exists anywhere in `BuildTarget`. The normal effect replay yields Refuted; launcher line 437 returns 5. A contracts-only variant using a false replayable `Contract.Ensures` reaches the same boundary.
- Closest live entry distinction: Wave 7.33 is a live-analyzer member-initializer ownership error that attributes initializer analysis to an unsuppressed delegating constructor; here even a directly and validly suppressed selected callable is handled correctly by the live analyzer but its suppression is wholly absent from the compiler-manifest/worker protocol. Wave 21.9 concerns malformed suppression reasons on unselected generated declarations; this uses a valid nonempty reason and a genuinely selected claim. Wave 19.42 concerns Roslyn `Diagnostic.IsSuppressed` on compiler warnings, not `SharpProofSuppress` or worker claims. Final live-ledger search found no entry for suppression being lost between `ClaimManifestBuilder` and launcher reporting.

## Wave 22.6. MEDIUM - Built-in binary string concatenation treats a nonreturning implicit formatter as normally completing

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanBinary`, lines 338-368, especially `StringConcatenationEffectResolver.Resolve` at 352-358 and the enclosing completion flag `_completionEvaluator.CanCompleteNormally(binary)` at 366-368. `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteBinary`, lines 1032-1088, especially the null-`OperatorMethod` success at 1083-1087. The already-existing completion helper that is not used on this path is `SharpProof.Effects/StringConcatenationEffectResolver.cs`, `CanFormattedValueCompleteNormally`, lines 84-105; contrast its use for interpolation at `OperationEffectScanner.Expressions.cs` lines 421-429. Downstream sequencing is `SharpProof.Effects/OperationEffectScanner.cs`, `ScanSequence`/`ScanStep`, lines 952-974.
- Mechanism: Roslyn exposes built-in string concatenation with a nonstring operand as an `IBinaryOperation` with no `OperatorMethod`; the implicit `ToString` call is not a child. `ScanBinary` correctly asks `StringConcatenationEffectResolver.Resolve` for that hidden formatter's effects, so a source `ToString` that always throws/diverges contributes its terminal summary. But the step's completion is reconstructed by `CanCompleteBinary`, which checks child operands and then returns true whenever `OperatorMethod == null`; it never calls the resolver's dedicated `CanFormattedValueCompleteNormally`. The enclosing expression/statement is therefore relabeled completing even though the scanner just resolved a formatter with no normal return. In a nested concatenation, that also allows later operand evaluation that runtime never reaches.
- Impact: Complete effect summaries retain impossible suffix writes, allocations, calls, and exceptions after a definitely nonreturning formatter, and can also attribute the concatenation allocation even when formatting fails first. Valid purity/no-write/no-allocation/allowed-exception contracts can be rejected with misleading evidence. This is a deterministic false-positive correctness defect, so Medium.
- Safe reproduction/evidence: `sealed class V { public override string ToString() => throw new System.ApplicationException(); } static class Global { public static int State; } static void M(V value) { _ = "" + value; Global.State++; }`. Runtime evaluates `value`, calls the exact sealed/source `V.ToString`, throws, and never writes `Global.State`. Static trace: both explicit binary operands complete; lines 352-358 resolve `V.ToString` and add its `ApplicationException`; lines 366-368 ask `CanCompleteBinary`; with no user operator, lines 1083-1087 return true; the outer expression statement and CFG successor remain completing and `Global.State++` is scanned into the complete summary. A nested variant `_ = ("" + value) + Mutate();` shows impossible later-operand effects. No mutation or external action is needed; this is an exhaustive source-path trace.
- Closest-entry distinction: Wave 7.23 selects the wrong formatting member (`IFormattable.ToString` versus parameterless `ToString`); this uses a plain sealed override whose selected member is unambiguous, and fails because its known terminality is ignored. Wave 12.28 omits implicit formatting from catch reachability; this reproduction has no catch and the formatter's throw is already present in the scanner summary, but the binary is relabeled completing. Wave 20.17 is the analogous omission for `string +=`, whose compound path never calls the resolver at all; this uses ordinary binary `+`, which does resolve the call but ignores `CanFormattedValueCompleteNormally`. No live entry covers binary-concatenation completion/suffix sequencing.

## Wave 22.7. MEDIUM - Failed construction type initialization does not gate object-initializer exception traversal

- Exact file/member/current lines: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions(IOperation, HashSet<IMethodSymbol>, int, bool)`, object-creation arm lines 536-573, especially `AddStaticInitializationPotential` and its `initializationCompletes` result at 542-552; local `PushChildren`, object-creation case lines 1111-1121, especially the initializer gate at 1112-1118. Static-initialization completion authority is `AddStaticInitializationPotential`, lines 1599-1630, and `StaticInitializationMayComplete`, lines 1632-1666. Downstream catch admission is `SharpProof.Effects/OperationEffectScanner.cs`, `IsReachable`, lines 1251-1276.
- Mechanism: The object-creation arm correctly computes that mandatory type initialization cannot complete and therefore suppresses constructor exception traversal. It then unconditionally calls `PushChildren(creation)`. The specialized child routine independently pushes `creation.Initializer` when explicit arguments and the constructor body can complete; it does not require the earlier `initializationCompletes` result or recheck static initialization. Thus a property/indexer/collection initializer that can only run after a definitely failing `.cctor` is traversed as though reachable, and its accessor/call exceptions can make an impossible catch reachable.
- Impact: Catch bodies that runtime cannot enter contribute writes, allocations, capabilities, calls, and throws to an otherwise complete method summary. Valid effect contracts can be rejected and direct evidence can point into an impossible handler. This is deterministic conservative corruption, so Medium.
- Safe reproduction/evidence: `sealed class Bomb { static Bomb() { throw new System.InvalidOperationException(); } public int P { set { throw new System.ApplicationException(); } } } static class Global { public static int State; public static void M() { try { _ = new Bomb { P = 1 }; } catch (System.ApplicationException) { State++; } catch (System.TypeInitializationException) { } } }`. Runtime must run `Bomb..cctor` at `newobj`; its failure is wrapped as `TypeInitializationException`, so allocation, constructor, setter, and the `ApplicationException` catch are never reached. Static trace: lines 542-552 get `initializationCompletes=false` because lines 1632-1666 see the noncompleting explicit `.cctor`; nevertheless line 572 calls `PushChildren`; lines 1112-1118 see no arguments plus a completing implicit instance constructor and push the initializer; setter traversal adds `ApplicationException`; catch reachability admits the first catch and scanner `IsReachable` allows `State++`. No repository mutation is required.
- Closest-entry distinction: Wave 7.22 is scanner-side omission of real object-initializer effects because `CanCompleteConstruction` includes initializer completion; this is the independent exception-reachability walker doing the opposite, adding initializer exceptions after an earlier type-initialization barrier. Wave 14.12 concerns an implicit derived constructor erasing its mandatory base-constructor completion/throws; this fixture has no base-chain dependency and the static-initialization gate correctly returns false before its result is discarded by `PushChildren`. Wave 18.23 concerns source exception construction skipping type-initialization reachability; this uses an ordinary non-exception constructed type whose type-initialization failure is recognized. Wave 20.12 concerns omitted type-initializer effects for exact metadata calls/constructions; this is same-compilation source with the TIE recognized, but that terminality is not carried into initializer traversal. No live entry covers the missing `initializationCompletes` gate on object-initializer child exception traversal.

## Wave 22.8. HIGH - Binary-pattern short-circuiting is ignored by interprocedural method completion, so returning helpers can be classified nonreturning

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MayCompleteNormally`, `IIsPatternOperation`/pattern dispatch at lines 1991-2002; `ChildrenMayCompleteNormally` lines 2301-2304. Downstream: `DefiniteOperationFacts.MethodCanCompleteNormally` lines 1918-1950; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` lines 588-609; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373. Correct contrasting direct-pattern logic: `OperationCompletionEvaluator.CanCompletePatternEvaluation`, binary-pattern arm lines 179-199.
- Mechanism: `MayCompleteNormally` has no `IBinaryPatternOperation` case. It sends every unrecognized `IPatternOperation` to `ChildrenMayCompleteNormally`, which requires both pattern arms to complete. C# `or` and `and` patterns evaluate left-to-right and short-circuit. If an `or` pattern's left arm is guaranteed to match, its right arm is never evaluated; if an `and` pattern's left arm is guaranteed not to match, the same is true. A nonreturning accessor in that skipped right arm nevertheless makes the right recursive pattern false, then the binary pattern false, then the enclosing source method false. Every caller treats the genuinely returning helper invocation as terminal and drops its regular successor.
- Impact: Real caller suffix writes, allocations, calls, and exceptions disappear from a Complete effect summary, permitting unsound no-write/purity/allocation conclusions.
- Safe reproduction/evidence: `sealed class Bomb { public int P { get { while (true) { } } } } static bool Returns() => new Bomb() is {} or { P: 0 }; static int state; static void Caller() { _ = Returns(); state++; }`. Runtime left `{}` necessarily matches the nonnull `new Bomb()`, so the right property pattern is skipped, `Returns()` returns true, and `state` increments. A read-only Roslyn 4.14 probe against the repository package produced an `IIsPatternOperation` with `IBinaryPatternOperation`, left/right both `IRecursivePatternOperation`, and no compilation diagnostics. Static trace: left recursive pattern completes; right reaches the diverging `P` getter and returns false; generic child-all makes the binary false; `CanCompleteInvocation(Returns)` returns false and CFG propagation suppresses `state++`.
- Closest live distinction: Wave 19.6 discards the nullable governing-value mismatch path for one recursive/list pattern; this input is provably nonnull and the defect is binary-pattern left-to-right short-circuiting. Wave 20.8 misses a mandatory accessor for assignable nonidentical types and retains impossible suffix effects; here the accessor is correctly known nonreturning but is runtime-skipped, and the erroneous result is the opposite (real suffix omission). Wave 21.21 concerns order-insensitive `or` reasoning in the separate cancellation meta-analyzer, not Effects method completion.

## Wave 22.10. MEDIUM - A noncompleting await operand in an async method's synchronous prefix is ignored, so an invocation that cannot return a Task is classified completing

- Exact files/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `DefiniteOperationFacts.MethodCanCompleteNormally` lines 1918-1950; `MayCompleteNormally` lines 1961-2057, which has no `IAwaitOperation` arm and reaches default `true` at 2057. Downstream: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteInvocation` lines 588-609; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph` lines 351-373. Corroborating operand order: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanAwait` lines 93-105 evaluates/scans the await operand first.
- Mechanism: Invoking an async method executes its state machine synchronously through the first incomplete suspension. The await operand must be evaluated before `GetAwaiter`/suspension; if it diverges, the async method invocation itself never returns its Task. `MethodCanCompleteNormally`, however, encounters `IAwaitOperation` and returns the permissive default `true` without examining `awaitOperation.Operation`. It therefore labels the async method completing and retains caller successors that runtime cannot reach.
- Impact: Impossible caller suffix writes, allocations, calls, and exceptions enter effect summaries, causing false effect/exception contract failures and misleading witnesses.
- Safe reproduction/evidence: `static System.Threading.Tasks.Task SpinTask() { while (true) { } } static async System.Threading.Tasks.Task NeverReturnsTask() { await SpinTask(); } static int state; static void Caller() { _ = NeverReturnsTask(); state++; }`. `NeverReturnsTask()` synchronously enters `SpinTask()` while evaluating the first await operand and never reaches a suspension or returns a Task, so `state++` is impossible. Static trace: `SpinTask` is proven noncompleting by the recognized `while(true)` arm, but the enclosing `IAwaitOperation` is default-true in `MayCompleteNormally`; `MethodCanCompleteNormally(NeverReturnsTask)` is true; `CanCompleteInvocation` preserves the caller successor.
- Closest live distinction: Wave 18.29 is the opposite async boundary error: it treats an eventual/deferred throw as preventing an async call expression from returning a faulted Task. This case is pre-suspension divergence during mandatory await-operand evaluation, which truly prevents Task return and is currently ignored. Wave 18.30 is `OperationCompletionEvaluator` relabeling array initializers/interpolated strings after specialized scanning; this defect is the separate `DefiniteOperationFacts` source-method completion path and specifically the synchronous async prefix. Wave 21.6 is missing await continuation-registration exceptions in catch reachability, not normal completion or operand evaluation.

## Wave 22.11. HIGH - Callable counterexample replay accepts integer returns outside the declared C# result domain

- Exact files/members/current lines: `SharpProof.Worker/CallableCounterexampleReplayer.cs`, `Replay`, lines 65-76, especially the return check at 67-75; duplicate launcher-side authority path `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`, `TryReplayPostcondition`, lines 686-699; cache path `SharpProof.Worker/VerificationCache.cs`, `ReplayCachedClaims`, lines 467-478. Artifact admission contrast: `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `ValidateExecutableBody`, lines 883-918, especially 911-917. Intended proof-domain constraint: `SharpProof.Worker/PostconditionObligationBuilder.cs`, `TryAddSourceDomainAssumptions`, lines 15-47.
- Mechanism: Canonical integer variables use one IR integer type for all C# integral types and preserve the real source range in `CompilerCanonicalVariable.SourceIntegerInterval`. Proof construction explicitly adds the result interval as an assumption for every symbolic return. Concrete replay does not enforce that same invariant. Both worker replay and response authority check only that `execution.ReturnValue.Type` equals the Result variable's IR type; they then install the value and evaluate the Ensures clause. `ValidateExecutableBody` likewise checks only IR type, so a canonical/resealed executable body may return, for example, IR integer 256 for a result whose source interval is byte [0,255]. The response model parsers correctly range-check input rows, but the returned result is not a model row and is never range-checked. Thus a non-source-realizable execution can authenticate a nonvacuous Refuted result. Verification-cache replay inherits the worker replayer's omission.
- Impact: A corrupt/resealed compiler artifact plus a forged/corrupt worker or cache response can produce an accepted, replay-backed Refuted postcondition and falsely fail a build even though the alleged return cannot exist under the declared C# signature. The authority disagrees with proof construction: the genuine query would include the result-domain assumption and cannot obtain that counterexample, while the later replay admits it.
- Safe reproduction/evidence: Build an internal in-memory successful target with no inputs, one integer Result whose `SourceIntegerInterval` is [0,255], an acyclic one-block program returning `factory.Integer(256)`, and one `Ensures(false)` claim. This passes the current `ValidateExecutableBody` return-shape predicate because both are the shared IR integer type. Supply a canonical Refuted response with `Model=[]`, empty core, no vacuity, and matching claim ID. `CompilerResponseEvidenceAuthority.TryCreateModel` accepts the empty input model, program replay returns 256, lines 686-699 install it, and `Ensures(false)` makes `TryReplayPostcondition` return true. Direct `CallableCounterexampleReplayer.Replay` also returns `WorkerClaimReason.None`; consequently the cache replay predicate accepts the same impossible counterexample. In contrast, normal `CallableEvidenceBuilder` adds `0 <= return <= 255` through lines 15-47, so the honest proof query cannot produce this model.
- Closest live `BUGS.md` distinction: Wave 9.3 is the neighboring return-shape omission for a void callable (zero Result variables accepting an arbitrary nonnull return). This finding has exactly one correctly typed integer Result and exploits the separate source-domain invariant that collapses byte/short/int/long onto the same IR type. Wave 10.1 concerns vacuity labels being accepted without establishing contradictory entry/no-return semantics; this result is nonvacuous Refuted and goes through concrete program/claim replay. Wave 11.2 concerns swapped pre-state association, not return range. Wave 21.38 concerns effect-event semantics at a source span, not callable scalar result replay. Final live-ledger exact searches for `SourceIntegerInterval`, `result interval`, `return interval`, `out-of-domain`, and `source domain` found no existing entry.

## Wave 22.12. HIGH - Nested component pattern aliases drop definitely reachable delegate targets

- Exact path/member/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`; `TreeAnalysis.CanReachConsumption` lines 567-783, especially pattern propagation 717-747 and nonexecuting-pattern skip 749-753; `GetPatternDestinations` lines 1199-1228; `WholeInputDesignations` lines 1230-1265; downstream `GetNestedCallables` lines 297-367 / `AnalyzeGraph` 198-244.
- Mechanism: `GetPatternDestinations` sees only `IsPatternExpressionSyntax`, then `WholeInputDesignations` returns declarations only for the pattern's whole input (top-level declaration/var/recursive designation, parenthesized/binary combinations). It never descends a `RecursivePatternSyntax` positional/property clause or a `ListPatternSyntax` to find aliases bound to a tracked tuple component. When a tracked tuple-containing local is used in `pair is { Callback: var callback }`, `patternDestinations` is empty. `IsNonExecutingObservation` then treats the entire `IIsPatternOperation` as harmless and skips the `pair` reference, so `CanReachConsumption` never starts tracking `callback` and returns false even when `callback` is immediately invoked.
- Impact: A definitely executed local function/lambda body can be excluded from Requires analysis, omitting SP0027 and recording no real nested semantic outcome. This is a verification false negative.
- Safe reproduction/evidence: `static int Outer(){ var pair=(Callback:(Func<int>)Reachable, Number:1); if (pair is { Callback: var callback }) return callback(); return 0; int Reachable()=>Positive(-1); }`, where `Positive` requires `x>0`. The value-tuple property pattern necessarily binds the stored delegate and `callback()` executes `Reachable`. Initial `GetTuplePath` records `Callback`; the later `pair` reference reaches `GetPatternDestinations`, but the top `RecursivePattern` has no whole-input `Designation` and its property subpattern is never traversed; `patternDestinations` stays empty and `IIsPattern` is skipped. The existing positive control `NestedRequiresCallSiteTests.PatternAliasesReachLocalFunctions` lines 1002-1028 covers only a whole-input `value is (var alias)`, exactly the supported case, not nested component aliases.
- Closest-entry distinction: Wave 15.15 is stack overflow from recursive alias propagation after an alias is discovered; here the legal nested alias is never discovered. Wave 3.14 concerns effect/accessor ordering inside patterns, not delegate/local-function reachability. Wave 21.4 concerns switch-arm false guards erasing pattern evaluation, not an `is` designation whose bound delegate is invoked.

## Wave 22.13. HIGH - Named tuple delegates invoked through their canonical ItemN alias are treated as a different component

- Exact path/member/current lines: `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs`; `CanReachConsumption` tuple comparison lines 633-654; `GetTuplePath` lines 785-805, especially `GetConvertedTupleElementName` result at 798-800; `GetAccessedTuplePath` lines 821-843, especially raw `field.Field.Name` at 834-839. Corroborating correct dual-name handling exists in `TryGetDeconstructionDestination` lines 845-939, specifically comparison against both `element.Name` and `element.CorrespondingTupleField.Name` at 875-892.
- Mechanism: A method reference placed in `(Callback: ..., Number: ...)` gets tracked under converted tuple name `Callback`. C# also permits access through its canonical alias `Item1`. `GetAccessedTuplePath` records only the accessed field symbol's raw `Name` (`Item1`) and does not normalize via `CorrespondingTupleField` or tuple ordinal. The shared-prefix loop compares `Callback` to `Item1`; because both paths have one unequal component, lines 646-649 classify the invocation as an unrelated tuple component and continue rather than consume it.
- Impact: A directly invoked local function/lambda can be classified dead, suppressing its reachable Requires violations and semantic outcome.
- Safe reproduction/evidence: `static int Outer(){ var pair=(Callback:(Func<int>)Reachable, Number:1); return pair.Item1(); int Reachable()=>Positive(-1); }`. C# names and `ItemN` are aliases for the same tuple slot, so `Reachable` definitely runs. Static trace: initial path is `Callback`; access path is `Item1`; shared remains 0; both counts are 1; line 648 continues; no other `pair` reference consumes the target. The deconstruction code's explicit `element.Name || element.CorrespondingTupleField.Name` handling independently demonstrates that both names must be normalized, while `GetAccessedTuplePath` does not.
- Closest-entry distinction: No live entry mentions `Item1`/canonical tuple names or `GetAccessedTuplePath`. Wave 19.2 is an assignment-LHS overbroad kill; this has no assignment after initialization and fails only on tuple-name alias comparison. Wave 15.15 concerns recursion depth, not component identity.

## Wave 22.16. HIGH - Coalesce-assignment flow writes only the synthetic capture, so stale-null facts can certify a method as pure while hiding a real static write

- Exact paths/members/current lines: `SharpProof.Effects/ManagedAbstractFlow.cs`, `ManagedAbstractFlow.TransferCore`, flow-capture and simple-assignment arms at lines 248-268, especially `SetStorage(assignment.Target, ...)` at 258-267; `TryStorage`, lines 863-874 (maps `IFlowCaptureReferenceOperation` only to its `CaptureId`). The missing storage resolution is visible by contrast in `SharpProof.Effects/CoalesceAssignmentFlowCaptures.cs`, `Record`/`Resolve`, lines 10-48, and its correct effect-side use in `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanWriteTarget` and `ScanWriteTargetEvaluation`, lines 5-32 and 53-76. Downstream suppression: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanIntrinsicProperty`, lines 369-401; `SharpProof.Effects/EffectMethodNodeBuilder.cs`, `AnalyzeControlFlowGraph`, lines 351-373. Purity acceptance: `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs`, purity completeness/evaluation at lines 117-144.
- Mechanism: Roslyn lowers `local ??= value` through an `IFlowCaptureOperation` for the original writable target and a later `ISimpleAssignmentOperation` whose target is an `IFlowCaptureReferenceOperation`. The effect scanner has a dedicated `CoalesceAssignmentFlowCaptures` map because that target must be resolved back to the captured local/field/property before classifying the write. Managed abstract flow has no analogous resolver. It records the pre-assignment local value under the capture ID, then the simple-assignment arm writes the RHS fact back only under that capture ID. The original local's fact is never changed. This is not made conservative: the analysis remains complete and can keep a definite-null fact after the language has assigned a nonnull RHS.
- Concrete impact: A post-`??=` dereference can be falsely classified as a definite `NullReferenceException`. The effect CFG scan then treats that operation as terminal and omits actual suffix effects. Because a known NRE does not make Reads/Writes/Capabilities unknown, `[EnforcePure]` can be accepted even though the omitted suffix writes static state. This is an unsound effect-contract proof, not only lost precision.
- Safe reproduction/evidence: Analyze `static int s_state; [SharpProof.Attributes.EnforcePure] static void Bad() { string? value = null; value ??= "ok"; _ = value.Length; s_state++; }`. Runtime always assigns the interned nonnull literal, reads length 2, and increments `s_state`. In the CFG, the coalesce target capture is recorded with `Null`; the later simple assignment changes only `CaptureId` to `NonNull`, leaving the `value` symbol `Null`. At `value.Length`, `ManagedFlowResult.ProvesNull`/`OperationNullnessEvaluator` therefore proves the receiver null; `ScanIntrinsicProperty` returns a terminal known NRE, and `AnalyzeControlFlowGraph` does not scan `s_state++`. The checked-in presence and use of `CoalesceAssignmentFlowCaptures.Resolve` on the effect side is direct evidence of the CFG target shape and of the missing corresponding managed-flow step. No repository mutation or external action is required.
- Closest live `BUGS.md` distinction: Wave 18.20 concerns ordinary `x ?? fallback` retaining a spurious nullable result and only losing nonnull precision; it does not mutate storage or suppress a real suffix write. Wave 21.11 is a ref-local alias snapshot, requiring `IsRef` and a ref local; this uses an ordinary nullable local and the compiler's coalesce-assignment capture. Wave 21.18 is frontend IR corruption for a ref-conditional lvalue phi; this finding is in Effects managed flow, uses `??=`, and corrupts effect reachability/purity. Wave 20.11 concerns ref/out call havoc for a composite ref conditional. Live final search found no entry for `??=`/coalesce-assignment storage updates in `ManagedAbstractFlow`.

## Wave 22.17. MEDIUM - Artifact-invalid cache hits can evict valid entries and receive fresh LRU state before rejection

- Exact files/members/current lines: `SharpProof.Worker/VerificationCache.cs`, `VerificationCache.TryReadAsync`, cache-level acceptance and capacity/LRU mutation at lines 67-89, especially `IsCacheable` at 70-75, `TryStageCapacity` at 81-84, and `File.SetLastWriteTimeUtc` at 85-86; `VerificationCache.IsCacheable`, lines 408-435, especially the authority-free `WorkerProtocolJson.Validate(response, expectedInputHash, expectedManifest)` at 434. The later full gate is `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`, lines 182-205, especially artifact-authority validation at 197-201. The missing distinction is concrete in `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateClaimResult` lines 509-551 and `SameAssumptionDeclarations` lines 819-830, which compare only assumption Id/Kind and ignore `Used`; the artifact gate `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs`, `ValidateAssumptionShape` lines 171-192, especially 184-190, enforces the authoritative `Used` set.
- Mechanism: `TryReadAsync` treats its weaker protocol-plus-replay predicate as sufficient to commit cache state. A rehashed payload can preserve a concretely replayable Refuted model and all protocol shapes while changing a claim assumption's `Used` bit. Basic protocol validation accepts that bit because claim assumption declarations compare only `(Id, Kind)` and the reassembled summary is self-consistent; cache replay ignores assumption evidence flags and re-evaluates the actual entry assumptions, so `IsCacheable` returns true. `TryReadAsync` then stages/deletes older entries to meet the active byte cap, touches the invalid entry as newest, commits those mutations, and returns it. Only afterward does `SharpProofWorker` assemble the candidate with current request state and invoke `CompilerResponseEvidenceAuthority`, which rejects `response.assumption_usage_authority`. There is no rollback channel from that rejection to the already-committed eviction/touch transaction.
- Impact: Untrusted cache bytes that can never be served as a hit can nevertheless evict valid replayable entries and become the newest protected entry. A transient noncacheable recomputation (timeout, Unknown, backend failure) leaves the artifact-invalid entry persistent, so repeated requests can continue wasting reads/replay and displacing useful cache state. Even a successful recomputation has already caused unnecessary loss of other valid entries. Semantic evidence still fails closed at the outer gate, so severity is Medium rather than High.
- Safe reproduction/evidence: Create two valid replayable-refutation entries A and B under a large cap; use a source with a user `Contract.Assume` so B's Refuted claim contains an assumption row whose authoritative `Used` value is false. Using the existing test helper pattern at `SharpProof.Worker.Test/WorkerTests.cs` `RewriteCachedPayloadAsync`, current lines 5797-5815, flip only `payload.claimResults[0].assumptions[0].used` to true and recompute `payloadHash`. Set B's request `MaximumBytes` to B's file length so A+B is over cap, then read B with a controlled backend returning a noncacheable Unknown. Static checks are decisive: protocol `SameAssumptionDeclarations` lines 822-830 projects away `Used`; cache replay checks actual entry assumptions and the model, not the evidence flag; `TryReadAsync` evicts A and touches B before returning; the caller's artifact authority rejects B at lines 197-201; Unknown skips the write branch at worker lines 324-336, leaving invalid B and deleted A. A focused regression can assert B is not touched and A remains after the outer rejection (or move the artifact authority into the cache transaction before capacity mutation).
- Closest live-ledger distinction: Wave 18.38 covers cache I/O/corruption failures being reported as ordinary `Miss` for noncacheable outcomes; this finding is the prior committed mutation of other cache entries by a candidate that passes the weaker inner gate but fails the later artifact authority. Wave 13.1 covers capacity reconciliation being skipped entirely on misses/noncacheable results; here reconciliation does run and wrongly commits eviction for an invalid candidate. Wave 6.10 concerns crash-stranded `.rollback`/`.eviction` artifacts, and Wave 9.2 concerns replacement of the lock pathname; neither needs a validator-order mismatch or a clean serial run. Wave 21.34 is overly strict non-scalar model admission, not artifact-invalid evidence mutating LRU state. Final live `BUGS.md` cross-check at length 852545 / timestamp 2026-08-29 23:22:48 found no entry for full artifact validation occurring after cache eviction/touch.

## Wave 22.18. MEDIUM - Unknown entry feasibility erases an already-authenticated Proven compiler effect result

- Exact paths/members/current lines: `SharpProof.Worker/CallableVerifier.cs`, `CallableVerifier.VerifyWithEntryFeasibilityAsync`, lines 47-68, especially the nonempty-claim entry-feasibility evaluation at 54-61 and its propagation at 63-68; `SharpProof.Worker/CallableEntryFeasibility.cs`, `CallableEntryFeasibilityEvaluator.EvaluateAsync`, lines 110-159, especially the budget/backend Unknown mappings at 115-159; `SharpProof.Worker/CallableVerificationPolicy.cs`, `VerifyTargetAsync`, lines 27-46, especially effect assembly using that feasibility result at 34-40; `SharpProof.Worker/EffectClaimResultAssembler.cs`, `Assemble`, lines 41-59 and 106-114, especially the unconditional Unknown overwrite at 52-59 before the already-authenticated compiler result would otherwise be preserved at 106-114. Executable paired evidence is already checked in at `SharpProof.Worker.Test/WorkerTests.cs`: `EffectOnlyClaimUsesSealedCompilerEvidenceWithoutSmtQuery`, lines 201-238, and `UnknownEntryFeasibilityKeepsEffectClaimUnknown`, lines 345-389.
- Mechanism: A nonliteral `Requires` makes `VerifyWithEntryFeasibilityAsync` spend a solver query even for an effect-only callable. If that preliminary false-goal query is Unknown (resource limit, infrastructure, malformed backend evidence, etc.), `EffectClaimResultAssembler` discards the sealed `CompilerEffectClaimArtifact` tuple without looking at its outcome and manufactures `Unknown/<feasibility reason>/Unavailable`. This loses even `Proven/None/CompleteMayEffectSummary` or `Proven/None/TrustedCompleteBoundary`. Such effect proof is independent of entry satisfiability and is stronger in either possible state: if entry is feasible, the complete compiler summary proves the contract normally; if entry is contradictory, the same whole-body proof still proves it without needing vacuity. Unknown feasibility therefore cannot invalidate it, and the existing nonvacuous `compiler-effect:<EvidenceSha256>` proof core at lines 106-114 remains valid.
- Concrete impact: Merely adding a valid nonliteral precondition to an effect-only method changes an already-proven `DoesNotThrow`, `ZeroAllocations`, purity, or complete custom effect claim into incomplete Unknown whenever the otherwise-unneeded SMT probe is inconclusive. Resource pressure causes false SP0047/incomplete coverage under strict policy; an InfrastructureFailure result can escalate an otherwise compiler-proven effect-only run to Failed.
- Safe reproduction/evidence: The checked-in baseline `EffectOnlyClaimUsesSealedCompilerEvidenceWithoutSmtQuery` uses `[DoesNotThrow] static int Identity(int value) => value;` and establishes `Proven/CompleteMayEffectSummary` with zero backend calls (test lines 201-238). The paired trigger `UnknownEntryFeasibilityKeepsEffectClaimUnknown` adds only `Contract.Requires(value > 0)` to the same identity body and scripts the feasibility backend as `Unknown(ResourceLimit)`; current assertions establish one backend call followed by `Unknown/ResourceLimit/Unavailable` and incomplete callable coverage (lines 345-389). A smaller unit reproduction can pass any valid `Proven/CompleteMayEffectSummary` artifact plus `CallableEntryFeasibility.Unknown(ResourceLimit)` to `EffectClaimResultAssembler.Assemble`; lines 52-59 deterministically erase the proof. This is read-only/safe. I launched the canonical isolated two-test container filter, but its compile produced no output for several intervals and I interrupted it rather than treat a long build as additional evidence; the static checked-in fixtures and exact branch are sufficient.
- Closest-entry distinction: Live `BUGS.md` Wave 21.7 concerns the same preliminary Unknown state aborting before a *postcondition Refuted* query whose later model could independently establish feasibility; this defect needs no later SMT query or counterexample and discards a compiler-authenticated *effect Proven* result that is valid irrespective of feasibility. Wave 18.37 discards independent effect evidence because callable lowering has already failed and policy exits before effect assembly; here `target.IsSuccess`, effect assembly is reached, and its line 52 branch itself overwrites the valid evidence solely because the feasibility probe was inconclusive. Wave 4.4 concerns known-contradictory entry losing postcondition vacuity during unsupported body/goal processing, not Unknown feasibility or compiler effect proof. Final live-ledger check through Wave 22.3 found no entry for this mechanism.

## Wave 22.19. MEDIUM - Provably incompatible covariant array stores are treated as normally completing

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.Assignments.cs`, `ScanSimpleAssignment`, lines 36-50; `SharpProof.Effects/OperationEffectScanner.cs`, `ScanArrayElement`, lines 423-475, especially the `ArrayTypeMismatchException` branch at 464-470, and `ArrayStoreIsDefinitelyCompatible`, lines 485-514; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteNormally`, assignment arms at 89-108, plus `CanCompleteArrayElement`/`CanCompleteWriteTarget`, lines 733-759.
- Mechanism: The effect scanner can know a fresh array's exact runtime element type. For `object[] values = new string[1]; values[0] = new object();`, `ClassifyRegion` preserves the fresh `string[]` identity, `_freshArrayTypes` returns `string`, and `ClassifyCommonConversion(object,string).IsImplicit` is false, so lines 464-470 correctly add `ArrayTypeMismatchException`. Normal-completion analysis has no corresponding store-value/runtime-type check: simple assignment delegates to `CanCompleteWriteTarget`, whose array arm calls `CanCompleteArrayElement`; that helper tests only receiver evaluation/nullness and index evaluation. It therefore returns true for this guaranteed-failing store. `ScanStep` relabels the assignment completing, and CFG propagation visits its regular successor even though the runtime store cannot return.
- Impact: Writes, allocations, calls, and exceptions after a guaranteed `ArrayTypeMismatchException` enter an otherwise Complete summary. Valid purity/no-write/no-allocation/allowed-exception contracts can be rejected and evidence points to impossible suffix work. This is deterministic conservative correctness corruption.
- Safe reproduction/evidence: Analyze `static int After; static void M() { object[] values = new string[1]; values[0] = new object(); After++; }`. Runtime necessarily throws at the covariant store and never increments `After`. Static trace above proves the scanner records the exception but completion remains true. Existing `SharpProof.Effects.Test/EffectAnalysisTests.cs`, `ArrayStoreCompatibilityUsesExactFreshRuntimeElementType`, lines 4391-4441, already includes the exact `Covariant` shape and asserts that the summary contains `ArrayTypeMismatchException`; it does not assert that a suffix is unreachable. I ran that targeted test in the canonical Linux amd64 tooling container; it passed (1/1), confirming the live exact-runtime-type branch is active.
- Closest-entry distinction: Wave 7.13 is the same completion helper omitting provably out-of-range bounds, but has no assigned value or covariant runtime type. Wave 18.16 is a cross-tree `SpanStart` collision that makes `ArrayStoreIsDefinitelyCompatible` incorrectly return true and omits the exception; here the runtime type is correctly resolved, the exception is correctly included, and the independent defect is failure to turn that proven store failure into noncompletion. Wave 9.14 concerns negative array creation, not an array-element assignment.

## Wave 22.20. MEDIUM - Event accessors bypass defensive-copy receiver mapping for readonly struct receivers

- Exact files/members/current lines: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, `ScanEventAssignment`, lines 34-90, especially direct `_callResolver.Resolve` at 79-87 with `_conversionOwnership.ClassifyRegion(reference.Instance)` passed as both receiver and write receiver at 81-82. Correct contrasting path: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanCallStep`, lines 609-668, especially the distinct `writeReceiver` at 650-658, and `UsesDefensiveReceiverCopy`, lines 670-696. Downstream remapping: `SharpProof.Effects/EffectSummaryOperations.cs`, `Remap`, lines 130-146.
- Mechanism: A custom event add/remove accessor is an instance method. Invoking a non-readonly member on a mutable value type through an `in` parameter, ref-readonly local/parameter, readonly field, or ref-readonly property/invocation receiver uses a defensive receiver copy; receiver reads originate from the caller value, but writes mutate only the copy. The ordinary call/property path explicitly detects these receiver shapes and passes `EffectRegionSet.Empty` as `writeReceiver`. `ScanEventAssignment` bypasses that path and sends the same external receiver region for reads and writes. Thus an accessor summary containing `Writes(Receiver)` is remapped onto the caller-owned readonly region even though the runtime write is discarded.
- Impact: Legal event subscriptions/removals can acquire false `WritesArgumentState`, `WritesReceiverState`, or unknown-write effects. Complete non-writing/purity contracts are rejected and interprocedural summaries claim caller-state mutation that cannot occur.
- Safe reproduction/evidence: `public struct Counter { int count; public int Count => count; public event Action Changed { add { count++; } remove { count--; } } } static void Subscribe(in Counter source, Action handler) => source.Changed += handler;`. `ClassifyRegion(source)` is `Parameter(0)`; the explicit add accessor's `count++` summary writes `Receiver`; event lines 81-82 map it to `Parameter(0)`. The ordinary `UsesDefensiveReceiverCopy` predicate would match the `in` parameter at lines 684-686 and map writes to Empty, but is never called. A read-only canonical-container C# probe compiled this source successfully; executing `var counter=new Counter(); Subscribe(in counter,()=>{}); return counter.Count;` returned `0`, confirming the accessor mutated only a defensive copy. No repository file was changed.
- Closest-entry distinction: Wave 5.33 concerns null-receiver ordering that omits reachable handler/accessor effects; this uses a nonnull value-type receiver and the accessor is included, but its write ownership is wrong. Wave 21.5 concerns event-assignment terminality and impossible suffix retention; this accessor completes normally and the defect is region remapping. Wave 14.14 loses heap writes for by-value structs; this is the inverse defensive-copy problem (a write is falsely retained) and specifically arises because the event path bypasses the existing receiver-copy helper.

## Wave 22.21. MEDIUM - Delimiter-ambiguous OSS file identity lets a method bind to a file from a different or nonexistent licensed source

- Exact files/members/current lines: `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs`, `Validate`, source dictionary and file-key construction at lines 123-167, method file lookup at lines 190-212, and source-file counting at lines 254-257; permissive `ValidateRelativePath` at lines 349-357. Downstream acceptance repeats the same key in `SharpProof.Gates/Corpus/OpenSourceCorpusRunner.cs`, `ObserveAsync`, lines 38-70. Published method provenance is formed in `OpenSourceCorpusCatalog.CreateCases`, lines 43-56.
- Mechanism: File identity is represented by the unescaped concatenation `$"{SourceId}|{Path}"`. Source IDs are checked only for non-whitespace, and relative paths may contain `|`. A method is never separately required to name an existing source; it is accepted if its concatenated `SourceId|Path` happens to match a file key. Thus file `(SourceId="A|B", Path="C.cs")` and method `(SourceId="A", Path="B|C.cs")` share key `A|B|C.cs`. Source `A` may be entirely absent. `FindDeclaration`, hash/name checks, source-file breadth counting, runner grouping, and instrumentation all follow the collided string key, so validation and execution succeed while the method's recorded provenance names a different/nonexistent source/path.
- Impact: The licensed-provenance gate can certify a selected OSS method whose declared source ID has no repository, commit, SPDX license, or license hash record. Gate output then publishes the false `A:B|C.cs:line` provenance even though the executed bytes belong to source `A|B` at `C.cs`. This defeats exact per-method source/license attribution and can also misstate per-source corpus breadth.
- Safe reproduction/evidence: In a disposable schema-2 document that otherwise preserves the required 200 methods/25 files, give one valid source ID `A|B`, its valid hashed file path `C.cs`, and the corresponding method the fields `SourceId="A"`, `Path="B|C.cs"`, with the real declaration line/hash/name. Do not add source `A`. Static trace: lines 137-149 accept the file because `A|B` exists; lines 190-212 resolve the method through identical composite key; no method-source existence check runs; runner lines 38-70 uses the same key and instruments the declaration. No malformed C# or filesystem mutation is needed.
- Closest-entry distinction: Wave 5.31 also notes that `ValidateRelativePath` admits `|`, but only for canonical snapshot grammar corruption after rendering; this finding needs no snapshot delimiter parse and instead cross-binds two distinct `(SourceId, Path)` tuples inside catalog validation and runner execution. Wave 12.24 merely undercounts two legitimate sources that share the same path when reporting `OpenSourceFileCount`; here a method can name a source that does not exist and still validate. Wave 15.13 is the generic exception from duplicate source IDs, not silent acceptance of a mismatched method/file provenance tuple. Wave 2.11 is delimiter ambiguity in relational-summary provenance, a different subsystem and key.

## Wave 22.22. MEDIUM - Outer build-task deadline truncates the launcher's 30-second publication lock wait to at most five seconds

- Exact files/members/current lines: `SharpProof.BuildTasks/RunVerifier.cs`, constants `LauncherProcessReserveMilliseconds` / `WorkerLauncherProcessReserveMilliseconds`, lines 18-25; `RunVerifier.Execute`, deadline construction and worker-launcher adjustment at 133-150 and exit wait at 218-246; `ComputeProcessTimeout`, lines 781-795. `SharpProof.Worker.Launcher/Program.cs`, `RunWorker`, lines 214-236; `ComputeHardLimit` / `ComputeFinalLimit`, lines 275-285; `PublishOutputs`, publication lease acquisition at 481-501.
- Detailed mechanism: For the genuine packaged launcher, `RunVerifier` recognizes `--project-wall-ms` and gives the whole launcher process exactly `project wall + termination grace + 5000 ms`, measured from immediately after process startup. The inner worker is independently allowed through `project wall + termination grace` (lines 218-236 and 275-285). If worker execution consumes that legitimate inner budget, less than five seconds remains for launcher-side response reconciliation, reporting, staging, and publication (managed startup has already consumed part of the outer window). Nevertheless `PublishOutputs` explicitly allows `AcquirePublicationSet` to wait 30 seconds for a legitimate existing lease. A holder that releases after 6-29 seconds is therefore within the publication API's declared timeout, but the outer task kills the launcher first. The lock cannot perform its intended serialization under precisely the contention it was designed to handle.
- Concrete impact: Concurrent same-project builds sharing the normal publication paths can spuriously fail/timed-out after the worker produced a valid response, solely because a genuine publisher held the lease for longer than the much smaller hidden outer reserve. Termination can also interrupt the launcher while it is entering or performing publication; at minimum the otherwise valid build loses its result and fails, and depending on the boundary reached may retain non-commit members until the next invalidation.
- Safe reproduction/evidence: In a disposable canonical-container fixture, let a controlled worker consume nearly its configured project wall budget and write a valid response. After initial invalidation, hold the same publication-set lease so `PublishOutputs` blocks, then release it after roughly six seconds (well below its 30-second timeout). `RunVerifier` reaches its `project + grace + 5 s` outer deadline first, terminates the genuine launcher, and reports failure instead of allowing lease acquisition to complete. A deterministic unit seam can replace the worker with an existing controlled helper that emits a valid response just before its final limit and coordinate the lease holder; no production files or network are involved. Static arithmetic is decisive: the publisher advertises 30 seconds while its supervising process has at most five post-worker seconds, minus startup/reconciliation time.
- Closest-entry distinction: Wave 9.16 concerns an arbitrary direct helper spoofing the bare `--project-wall-ms` token to receive an undeserved extra four seconds; this finding uses the genuine launcher and shows that its intended reserve is too short for its own 30-second publication wait. Wave 21.17 is the later unlocked final validator observing a torn generation after publication; here the build is killed while the genuine publisher is still waiting under the publication-lock protocol, before final validation. Wave 6.23 is Clean/reset inspecting markers before taking its lease, and Wave 14.39 is cancellation of Clean; neither concerns the successful worker's publisher being supervised by a shorter deadline than its lock acquisition contract. Wave 5.32 publishes a Complete response after an abnormal worker exit; here the worker response is ordinary and valid, and failure is created solely by the outer/inner scheduling mismatch. Fresh live `BUGS.md` search found no publication-reserve/deadline entry.

## Wave 22.23. HIGH - Generic forwarding erases the semantic-answer type before an otherwise recognized cache write

- Exact file/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `AnalyzeWrite`, lines 16-28 (recognized method/cache gate and argument inspection at 18-22); `IsNonCacheableSemanticAnswer`, lines 65-90, especially the operation switch/fallback at 74-90; `IsSemanticAnswerType`, lines 329-338. Registration is `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `Initialize`, invocation action at line 65.
- Mechanism: Cache arguments are classified only from the operation at the generic method body's write site. An `IParameterReferenceOperation` has no dedicated arm, so it falls to lines 88-89. For a type parameter `T`, `IsSemanticAnswerType` returns false because the static type name is `T`, not a SharpProof type name containing `Answer`, `Result`, or `Outcome`. The analyzer does not specialize generic bodies or propagate actual arguments from callers. Thus in `Persist<T>`, the recognized `cache.Write(value)` sees only `T`; the caller's `Persist(cache, Answer.Unknown)` is itself ignored because `Persist` is absent from `WriteMethods`.
- Impact: An ordinary generic helper/refactor permits a definite Unknown, timeout, or failure value to enter a semantic cache with no error-severity SPMETA010. This defeats the value-side cache invariant even when the concrete receiver is exactly a `ProofCache` and the final mutation method is exactly allowlisted `Write`.
- Safe reproduction/evidence: Analyze C# 12 source: `namespace SharpProof.Verify; enum Answer { Unknown, Proven } sealed class ProofCache { internal void Write<T>(T value) { } } sealed class C { static void Persist<T>(ProofCache cache, T value) => cache.Write(value); void M(ProofCache cache) => Persist(cache, Answer.Unknown); }`. Runtime generic substitution sends `Answer.Unknown` to `ProofCache.Write<Answer>`. Static trace is deterministic: inner argument is a `T` parameter and lines 88-89 return false; outer invocation fails line 18 on name `Persist`. Existing test contrast in `SharpProof.Meta.Analyzers.Test/SharpProofSoundnessAnalyzerTests.cs`, `SemanticCacheWritesTrackAliasesAndAssignments`, lines 306-307, deliberately reports the concrete `Answer` parameter form `cache.Write(answer)`; changing only that forwarding parameter to `T` removes the semantic type recognized at line 329.
- Closest-entry distinction: Wave 6.28 erases the cache *receiver* behind an interface/base type; this fixture keeps the receiver exactly `ProofCache`. Wave 21.26 concerns virtual dispatch selecting a different answer-producing override; no dispatch is involved here. Wave 6.29 concerns aliases/compound expressions in a helper's returned value; this is a generic input parameter passed to an exact `Write`, with no return-value analysis.

## Wave 22.24. HIGH - `SetAsync` and other mutation names outside the ten-entry catalog bypass SPMETA010 before value inspection

- Exact file/members/current lines: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`, `WriteMethods`, lines 12-14; `AnalyzeWrite`, lines 16-28, especially the method-name early return at line 18. Invocation registration is `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `Initialize`, line 65.
- Mechanism: Invocation coverage is a closed ten-name catalog: `Add`, `AddOrUpdate`, `GetOrAdd`, `Set`, `TryAdd`, `TryUpdate`, `TryWrite`, `TryWriteAsync`, `Write`, and `WriteAsync`. A cache mutation named `SetAsync` (the direct asynchronous counterpart of allowlisted `Set`) is rejected at line 18 before the exact cache receiver and definite `Answer.Unknown` argument are examined. The same structural gap applies to ordinary cache APIs named `Put`, `Store`, `Insert`, or `Update`; no semantic binding identifies mutation behavior.
- Impact: Merely making an allowlisted cache API asynchronous/renaming `Set` to `SetAsync` removes the error-level invariant and permits definite transient or abstaining results to be persisted. This is a direct call-site false negative with exact static types and no aliases, wrappers, factories, assignments, or indirect dispatch.
- Safe reproduction/evidence: Analyze `using System.Threading.Tasks; namespace SharpProof.Verify; enum Answer { Unknown, Proven } interface IProofCache { Task SetAsync(string key, Answer value); } sealed class C { Task M(IProofCache cache) => cache.SetAsync("k", Answer.Unknown); }`. The receiver type name contains `Cache`, and the value is the directly recognized enum field `Answer.Unknown`, but `AnalyzeWrite` returns solely because `SetAsync` is absent at lines 12-14. Renaming only `SetAsync` to `Set` reaches lines 20-22 and reports SPMETA010. The interface safely models an external/metadata cache implementation, so the reproduction does not depend on any separate body-analysis gap.
- Closest-entry distinction: Wave 6.28 requires a receiver whose static type name lacks `Cache`; this receiver is exactly `IProofCache`. Wave 10.19 concerns unregistered coalesce/compound assignment operation kinds, not an ordinary registered invocation. Wave 12.20 concerns an allowlisted `GetOrAdd` whose delegate factory is opaque; this argument is the direct enum constant. Wave 14.42 is a false positive from inspecting the comparison argument of allowlisted `TryUpdate`; this is a false negative before any argument inspection. Live `BUGS.md` has no `WriteMethods`, `SetAsync`, or cache-mutation catalog entry.

## Wave 22.26. MEDIUM - Frontend fuzz coverage can pass while entire supported operator families are never exercised

- Exact files/members/current lines: `Tools/SharpProof.Fuzz/FuzzRunner.cs`, `FrontendFuzzCoverage` and `HasExpandedCategories`, lines 23-66; `FuzzSummary.Passed`, lines 79-110, especially 102-105; `CreateFrontendCoverage`, lines 414-508, especially the only syntax-kind switch at 472-502; `HasRequiredFrontendCoverage`, lines 510-513. Mirrored evidence authority: `scripts/Assert-SharpProofFuzzRunnerResult.ps1`, accepted coverage property list and positive-count checks, lines 141-155. Generator families that have no coverage field: `Tools/SharpProof.Fuzz/FrontendFuzzing.cs`, `GeneratedExpressionKind`, lines 24-57, and `SmallCSharpCaseGenerator.Integer`/`Boolean`/`RandomComparison`, lines 813-920. Existing direct acceptance evidence: `SharpProof.Fuzz.Test/FuzzRunnerTests.cs`, `MalformedSummaryEvidenceDoesNotPass`, lines 158-187, where an all-one 13-field coverage object makes a 1,000-case summary pass.
- Mechanism: The only required frontend coverage facets are text/string/array nodes plus five observed exception kinds. Neither the record, its `HasExpandedCategories` predicate, the collector switch, nor the strict JSON decoder carries any facet for unary negation, conditional expressions, addition/subtraction/multiplication/remainder, short-circuit `&&`/`||`, equality/inequality, or ordered comparisons. `FuzzSummary.Passed` nevertheless treats positivity of the listed fields as complete `CoverageSatisfied`. Thus a generator regression that stops producing any of those unsupported-by-the-coverage-schema families can still return 1,000/10,000 agreements, positive listed fields/exceptions, and a passing coverage result. Counts for divide-by-zero and overflow do not establish remainder, short-circuit, conditional, or comparison coverage (and overflow does not distinguish the several arithmetic operators).
- Impact: Pull-request/nightly campaign evidence can certify "passing coverage" while broad, core Roslyn-to-IR paths are absent from the campaign. A lowering/interpreter regression isolated to an omitted family can therefore survive the fuzz gate; the published schema cannot reveal the omission after the run.
- Safe reproduction/evidence: The checked-in test at lines 161-187 already constructs `FrontendFuzzCoverage(1, ..., 1)` and asserts that a 1,000-case, all-agreement summary is `Passed == true`; there is no place in that value to state whether remainder, conditionals, short-circuit operators, or comparisons occurred. A safe mutation test can change only `SmallCSharpCaseGenerator` to stop emitting `Remainder`, `Conditional`, `AndAlso`, `OrElse`, and all comparison operators while retaining string/array cases, division, and an overflowing arithmetic shape; the current `HasExpandedCategories`, `Passed`, and PowerShell evidence decoder have no predicate capable of detecting those lost families.
- Closest live-entry distinction: Wave 14.50 reports that the separate partial-term SMT generator repeats only eight semantic cases; this finding concerns missing frontend operator-family facets in the nominal coverage authority even when frontend case seeds are distinct. Waves 20.34 and 21.33 concern duplicated case streams, not which frontend shapes a distinct stream covers. Wave 18.57 concerns failing supported-domain abstentions lacking per-case evidence, whereas this mechanism produces a successful campaign with affirmative but incomplete coverage evidence. Wave 13.25 is a sequence-value comparison oracle that can hide a specific semantic mismatch, not omission of whole generated operator families from the coverage contract. Final live `BUGS.md` cross-check found no `FrontendFuzzCoverage`, `HasExpandedCategories`, or `CoverageSatisfied` entry.

## Wave 22.27. LOW - Decimal optional defaults compare numerically, so representation-distinct ContractFor signatures are certified exact

- Exact file/member/current lines: `SharpProof.Contracts/ContractForSymbolMatcher.cs`, `ExplicitDefaultValuesMatch`, lines 359-378, especially the fallback `Equals(left.ExplicitDefaultValue, right.ExplicitDefaultValue)` at 375-377. Supporting intended contract: `docs/architecture.md` lines 149-153 says ContractFor uses exact symbol identity including defaults; `docs/diagnostic-examples.md` lines 324-329 says SPCF0005 matching includes defaults.
- Mechanism: The matcher gives `float` and `double` bitwise treatment but sends `decimal` through `decimal.Equals`. Decimal equality is numeric and ignores representational scale (and signed-zero representation). Legal optional defaults `1.0m` and `1.00m` therefore have distinct encoded `DecimalConstantAttribute`/runtime bit patterns but compare equal. With all other member fields equal, `ParametersMatch` accepts the pair and no SPCF0005 is emitted.
- Impact: A companion whose optional-parameter default is not representation-identical to the target is accepted as an exact one-to-one symbol match. Decimal representation is observable through `decimal.GetBits`, so the target and companion can supply observably different values when each method is invoked with the argument omitted; the enabled-by-default exact-signature diagnostic silently misses the malformed companion surface.
- Safe reproduction/evidence: Compile `public interface ITarget { void M(decimal value = 1.0m); }` and `[ContractFor(typeof(ITarget))] public static class TargetContracts { public static void M(ITarget receiver, decimal value = 1.00m) { } }`. Static trace reaches the fallback `Equals` and accepts. A read-only PowerShell `Add-Type` probe compiled the equivalent legal declarations and returned: target default bits `10,0,0,65536`; companion bits `100,0,0,131072`; `left.Equals(right) == true`. Thus the exact branch behavior and distinct representations are directly established without repository edits.
- Closest-entry distinction: Live `BUGS.md` has no decimal/default-value entry. Wave 14.10 concerns array geometry and nested generic custom modifiers erased inside compound type matching, not optional constant equality. Wave 12.13 concerns suppression of an extra-member diagnostic on an incomplete surface, not certification of a mismatched member. The checked-in floating-default tests cover bit identity only for `float`/`double`, matching the special cases at lines 370-374; this defect is the unhandled `decimal` path.

## Wave 22.28. MEDIUM - AdditionalText provenance can attest a later value than the generator actually consumed

- Exact files/members/current lines: `SharpProof.CompilerProbe.TestAsset/CompilerProbeGenerator.cs`, `CompilerProbeGenerator.Initialize`, lines 12-35, especially `GetText` at 19-29; `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CompilerProbeSnapshot.CreateAdditionalFileRows`, lines 404-438, especially the independent `GetText` at 408-433; production `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`, `Capture` lines 108-116 and `CaptureAdditionalFile` lines 317-328, especially the independent `GetText` at 322; lack of a relation assertion in `SharpProof.Package.Test/FinalCompilationProbeTests.cs`, `PackedCollectorAttestsAndVerifiesGeneratorOutput`, lines 111-179.
- Mechanism: The generator retrieves each `AdditionalText` while producing its syntax trees. Later, after generation, both the independent probe analyzer and production collector call `GetText` again and hash that later result. `AdditionalText` is an abstract provider; neither code caches/binds the source text consumed by the generator nor requires repeated reads to agree. A stateful provider, or input that changes between generator and analyzer phases, therefore yields generated syntax from value A while both purported input-provenance artifacts record value B. Because both later captures can agree, even adding the missing oracle/manifest collection comparison would not expose the mismatch.
- Impact: The final compiler manifest and package-backed oracle can both authenticate an AdditionalFile payload that did not produce the captured generated program. Generator-input mutation/cross-wiring can consequently pass provenance and feed verification claims from stale generated source while the manifest names the replacement input.
- Safe reproduction/evidence: Use one legal custom `AdditionalText` named `SharpProofProbeInput.txt` whose first `GetText` returns `SourceText.From("first")` and subsequent calls return `SourceText.From("second")`. Pass the same instance to the generator driver and analyzer options. Generator lines 21-29 embed `first` and its fingerprint in `SharpProofProbe.Contract.g.cs`; snapshot lines 411-433 and production capture lines 322-327 hash `second`. No repository mutation is needed. Static evidence also shows no field/validator binding an AdditionalFile hash to the generated tree that consumed it.
- Closest live-entry distinction: Wave 18.48 is a PE-reference pathname reopen after Roslyn has bound cached metadata; this finding is the separately scheduled `AdditionalText.GetText` producer/consumer boundary and causes both the probe and production manifest to agree on the wrong generator input. Wave 18.50 conflates null content with authentic empty content even in one read; here both values are non-null and distinct. Wave 21.30 omits the oracle-to-manifest comparison; this survives that fix because both later captures contain the same wrong B value.

## Wave 22.29. LOW - Probe emitter and reader disagree on canonical JSON for legal Unicode provenance

- Exact files/members/current lines: `SharpProof.CompilerProbe.TestAsset/ProbeJson.cs`, `ProbeJson.String`, lines 101-145, especially raw append of every non-control character at 129-141; representative provenance projection `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs`, `CreateSyntaxTreeRow`, lines 212-252, path at 241-245 and Unicode-capable declared symbols at 255-274; consumer `SharpProof.Package.Test/FinalCompilationProbeTests.cs`, `ProbeArtifact.ReadAsync`, lines 363-413, especially canonical reserialization/equality at 365-381.
- Mechanism: The custom writer emits non-ASCII characters literally. `ProbeArtifact.ReadAsync` reparses the valid JSON and defines canonical form as default `System.Text.Json` serialization, whose default encoder escapes such characters (for example U+00E9 becomes `\u00E9`). Thus a producer-created artifact can be valid JSON yet deterministically fail its own consumer's byte-canonicality assertion solely because a legal path, assembly/symbol name, option, alias, or other serialized provenance contains Unicode.
- Impact: A valid final compilation with Unicode provenance cannot be accepted by the package probe reader, creating a false package/acceptance failure and preventing the oracle from covering ordinary non-ASCII project inputs.
- Safe reproduction/evidence: Read-only PowerShell against the active runtime: parse `{"x":"é"}` and call `JsonSerializer.Serialize(root, typeof(JsonElement))`; the result is `{"x":"\u00E9"}` and ordinal equality is false. `ProbeJson.String` produces the first spelling, while lines 377-381 require the second. A syntax tree path ending in `café.cs` is sufficient.
- Closest live-entry distinction: Wave 21.29 collapses two Linux paths by rewriting backslashes; no JSON reserialization is involved. Wave 19.32 erases `SourceText` encoding/checksum provenance inside a hash; this finding preserves the Unicode value semantically but producer and consumer reject each other's byte spelling. No live entry mentions probe JSON escaping or Unicode canonicality.

## Wave 22.30. MEDIUM - A long acyclic relational-summary call chain can stack-overflow the compiler collector before any summary resource limit applies

- Exact files/members/current lines: `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`, `TryGet`, lines 86-151, especially source/IL build dispatch at 105-126; `TryBuildSource`, lines 242-263, especially recursive `TryGet` at 253-257. The implementation-IL equivalent is `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`, `Translator.Call`, lines 926-1016, especially `_resolveSummary` at 987-992. Initial consumer: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`, `TryPrepareSummaryCall`, lines 299-318, especially 311-315.
- Mechanism: `CompilerRelationalSummaryProvider.TryGet` is a recursive call-graph resolver. `_active` rejects a cycle only when the same method is re-entered; every distinct method in a legal linear chain succeeds at `_active.Add`. Source lowering recursively invokes `TryGet` for each direct call, and IL lowering invokes the same delegate. There is no dependency-depth/count budget and no iterative worklist. Crucially, `IrRelationalSummaryBuilder.Build` (and its block/instruction/expression/symbolic-operation limits) runs only after all dependencies of the current method have already resolved. For `M0 -> M1 -> ... -> MN`, execution therefore accumulates N live `TryGet`/`TryBuildSource` (or `Translator.Call`) frames before the leaf can build and before an expression-depth or symbolic-operation abstention is possible. Per-frame CFG/lowering state is also retained. `StackOverflowException` is not caught or recoverable at this analyzer boundary.
- Impact: A valid generated project, or a referenced implementation library, containing a sufficiently long acyclic chain of tiny static scalar helpers can terminate the compiler/analyzer/MSBuild host (or impose severe retained-memory pressure) instead of producing typed `SummaryResourceLimit`/unsupported evidence. No malformed IL, recursive call cycle, large individual method, or oversized artifact is required.
- Safe reproduction/evidence: Generate ordinary methods `static int M0(int x) => M1(x); ... static int MN(int x) => x;` and make a selected SharpProof callable directly call `M0`. All helpers meet `IsSourceCandidate`; at lines 253-257 each distinct callee recursively enters `TryGet`, so the first summary builder invocation occurs only at `MN` with every ancestor frame still live. Run increasing N only in a disposable canonical-container child process because an actual `StackOverflowException` kills that process. A noncrashing regression seam can count concurrent resolver depth and assert a typed cutoff well below process stack exhaustion. The same static trace follows MethodDef `call` instructions through IL lines 987-992.
- Closest-entry distinction: Wave 18.44 is recursion in nested-callable ID construction over syntactic parent chains, including unselected lambdas/local functions; this finding uses flat ordinary methods with unique identities and arises only from transitive relational-summary call resolution. Wave 9.12 is quadratic rescanning of instructions inside one IL body, not interprocedural resolver stack depth. Wave 12.2 allows a sealed artifact to omit/replace a summary dependency closure; here the honest producer crashes while constructing the closure, before sealing. Wave 2.11 is delimiter collision while deduplicating already-built provenance, and Wave 6.7 is constructed-generic cache conflation; neither bounds an acyclic dependency chain. Final exact live-ledger searches for summary chain/depth/closure/recursion and `CompilerRelationalSummaryProvider` found no entry for this mechanism.

## Wave 22.31. HIGH - Scalar-domain rows are self-authoritative, so a canonical artifact can narrow an `int` parameter to `byte` and obtain a false SMT proof

- Exact files/members/current lines: `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs`, `CompilerVariableArtifact`, lines 312-323, especially `Minimum`, `Maximum`, and `ScalarDomain` at 319-321; `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `Encode`, lines 59-79 (producer projection); private `Decode`, lines 350-365 (reconstructs `CompilerIntegerInterval` wholly from the row); `ValidateVariables`, lines 478-539, especially artifact-controlled `sourceInterval`/`sourceOrdinal` at 503-516 and the only domain checks at 516 and 532-537; `SharpProof.CompilerArtifact/CompilerFeatureScopeFingerprint.cs`, `AddCallable`, lines 64-85 (hashes the same row but supplies no independent type authority); `SharpProof.Worker/PostconditionObligationBuilder.cs`, `TryAddSourceDomainAssumptions`, lines 7-49, especially range-assumption construction at 15-46; `SharpProof.Worker/CallableVerifier.cs`, `VerifyPostconditionsAsync`, lines 121-228, especially consumption of the hydrated assumptions at 121-138 and solver query at 225-228.
- Mechanism: The honest producer maps a Roslyn integral source type to `SourceIntegerInterval`, then writes its primitive interval plus `CompilerScalarDomain`. Hydration reconstructs the interval from those same serialized `Minimum`/`Maximum` values and validates only internal agreement: the interval must equal one of the supported primitive ranges, `ScalarDomain` must equal `ScalarDomain(interval)`, and the graph variable must have the shared IR Integer type. All supported C# integral types use that one IR type. Nothing compares the row to the callable's actual Roslyn parameter/result type or any independently captured signature; the compilation snapshot only hashes source text and is not semantically re-opened during hydration. `FeatureScopeSha256` authenticates the row's claim but is a recomputable self-seal. Consequently a coordinated/canonical artifact can change an `int` parameter from `[-2147483648,2147483647]/Int` to `[0,255]/Byte`, recompute `FeatureScopeSha256`, and pass serialize/deserialize plus `DecodeCallables` with graph, clause, manifest, and parameter binding unchanged. The worker then treats the forged byte range as trusted `domain:parameter:0` compiler evidence and feeds it to the real solver.
- Impact: This can produce a nonvacuous false `Proven` result for a source contract, suppressing a real violation and allowing the unsound answer to flow through ordinary response validation/cache behavior. The issue is stronger than metadata/provenance drift: it changes the proof assumptions used by the verifier.
- Safe reproduction/evidence: Start from a normal compiler artifact for `static int Identity(int value) { Contract.Ensures(Contract.Result<int>() >= 0); return value; }`. The honest parameter row is `Minimum=int.MinValue`, `Maximum=int.MaxValue`, `ScalarDomain=Int` (corroborated by `SharpProof.Worker.Test/CompilerCallableLowererTests.cs`, `BoundContractsAndExecutableBodyRetainVerifierInputs`, lines 43-90, especially 71-78). Change only that parameter row to `Minimum=0`, `Maximum=255`, `ScalarDomain=Byte`; recompute `FeatureScopeSha256` with `CompilerFeatureScopeFingerprint.ComputeSha256`; canonical round-trip and hydrate. `ValidateVariables` accepts because `[0,255]` is a permitted primitive interval and the variable is IR Integer. `TryAddSourceDomainAssumptions` emits `0 <= value <= 255`; symbolic execution returns that same value; the real solver therefore proves `result >= 0`, even though the actual C# call `Identity(-1)` violates the postcondition. This proof is nonvacuous because modeled byte-domain entries complete normally. Existing `WorkerTests.SourceDomainAssumptionsUseLoweredEvidence`, lines 1967-1995, directly confirms the query and proof core trust the lowered domain row (`domain:parameter:0`). No repository mutation or malformed JSON is required; an in-memory reseal fixture suffices.
- Closest-entry distinction: Wave 22.11 keeps an honest `[0,255]` Result interval but concrete replay fails to range-check a fabricated returned value, producing a false Refuted result. This finding forges/narrows the serialized source interval itself and makes the live SMT proof path return false Proven; it requires no out-of-range execution result or counterexample replay. Wave 11.2 swaps same-typed PreState associations while their source domains remain honest; here there is one unchanged parameter association. Wave 7.14 swaps parameter bindings; here the binding and body `return value` remain exact and only the domain authority is changed. Wave 6.3 is the honest producer's Int64 interval omission, not acceptance of a wrong supported primitive range. Wave 10.1 accepts unproved vacuity labels; this result is a genuine nonvacuous UNSAT proof under a forged trusted domain assumption. Final live `BUGS.md` cross-check (through Wave 22.27) found no scalar-domain-to-C#-signature binding entry.
