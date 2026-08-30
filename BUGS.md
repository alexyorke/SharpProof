# Read-Only Multi-Agent Bug Audit - 2026-08-29

This section records the coordinator's unverified compilation of findings from exactly 10 read-only auditors. The central writer did not inspect or reverify the code. Auditor coverage: Analyzer/Core (4), Frontend/IR (4), Dataflow/Effects (2), Contracts/Specs/Summaries (2), SMT/Verifier (2), Worker/Host/Verify (2), Compiler/Build/Generators (4), Gates/Package/Meta (3), Tests/Fuzz/Misc (4), and Scripts/CI (1).

## 15. HIGH - Z3 validation and native load are vulnerable to a file-replacement race

- Files and members: `SharpProof.Host/ContainerContract.cs`, `ResolveZ3LibraryRequired`, lines 120-155; `SharpProof.Host/ContainerNativeLibrary.cs`, `InstallZ3ResolverRequired`, lines 28-36.
- Mechanism: The resolver hashes and closes a stream, returns only the pathname, and `NativeLibrary.Load` later reopens it. The verified bytes are not tied to the loaded file handle or inode.
- Impact: Deployment/update races or mutation in a writable native root can load bytes different from those hashed, defeating native-payload integrity.
- Safe reproduction/evidence: The code has a TOCTOU gap. A controlled unit or integration harness can pause between validation and load and replace the test fixture with a different same-length fixture.

## 16. MEDIUM - Interrupted marker deletion permanently wedges publication reset

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Member: `ResetPublicationSet`
- Lines: 174-225, especially 189-200 and 211-225
- Mechanism: Markers are deleted sequentially with cancellation checks. Cancellation or I/O failure after some deletions leaves a subset, and the next reset rejects the marker-count mismatch before cleanup.
- Impact: The output set cannot recover through the public reset API; acquire and publish remain unusable until manual metadata cleanup.
- Safe reproduction/evidence: Use a fixture with at least two markers, inject cancellation or a filesystem failure after the first marker deletion, then retry with `CancellationToken.None`.

## 18. HIGH - Worker and cache identity exclude the native Z3 solver

- File: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`
- Members: `WorkerBinaryIdentity.CreateSnapshot`, `RuntimeComponents`
- Lines: 48-105 and 193-244, especially 207-219
- Mechanism: The closure seeds the worker DLL, deps, and runtimeconfig and extracts only DLL names. `libz3.so` is absent from components, staging, and `WorkerBinarySha256`.
- Impact: Solver replacement or upgrade leaves cache/input identity unchanged. Cache results can cross solver versions, and the staged runtime does not pin the actual solver.
- Safe reproduction/evidence: Compute identity for two isolated fixture closures differing only in `libz3.so`; the identities remain equal. The package ships `tools/native/linux-x64/libz3.so`.

## 28. HIGH - Production inventory silently drops repository-local analyzer identities when binaries are absent or unreadable

- File: `scripts/Get-SharpProofProductionInventory.ps1`
- Member: Main analyzer loop
- Lines: 346-359, especially the try at 351-355 and catch at 356
- Related locations: `eng/acceptance/Verify.ps1` lines 235-247; `eng/container/entrypoint.sh` lines 134-145; `SharpProof.AnalyzerConsumer.props` lines 39-66.
- Mechanism: Exceptions from `Resolve-RepositoryPath` or `Get-Sha256Hex` are blanket-caught and produce a blank path and hash, indistinguishable from an expected external analyzer. The canonical task copy excludes `bin` and `obj`, and acceptance inventories after restore but before build, so real local analyzer outputs can be absent.
- Impact: `sourceUniverseSha256` and `generatorInputs` omit the path and bytes of analyzers influencing production; missing, stale, permission, and hashing failures fail open.
- Safe reproduction/evidence: Run inventory in an isolated clean task copy after restore, or evaluate a repository-local Analyzer `FullPath` pointing to an absent fixture. The output contains blank path and hash rather than failing.

# Read-Only Multi-Agent Bug Audit - Wave 2 - 2026-08-29

This section records the coordinator's unverified compilation of 26 new findings from exactly 10 fresh read-only auditors, after title/mechanism deduplication against the prior audit and within this wave. The central writer did not inspect or reverify the code. Auditor coverage: Dataflow (1), SMT core (8), Verify core (1), Summaries (1), CompilerCollector (2), ContractForGenerator and Attributes (0), Worker/Launcher/Protocol (2), Gates (5), Package and BuildTasks (3), and release scripts (3).

## Wave 2.1. MEDIUM - Public forward dataflow solver silently assumes bottom-strict transfers

- File: `SharpProof.Dataflow/ForwardDataflowAnalysis.cs`
- Member: `ForwardDataflowAnalysis.AnalyzeCore<T>`
- Current lines: 120, 138-150, 158-197
- Mechanism: Only the entry is initially scheduled; successors run only after predecessor output changes, and enqueue only when joined input changes from bottom. The accepted `Func<T,T>` can validly and monotonically map bottom to nonbottom, but such a block is never evaluated when its input stays bottom, so the result is not a fixed point.
- Impact: The public API returns incorrect states without warning. The exhaustive oracle privately restricts itself to bottom-strict transfers, but the production contract does not.
- Safe reproduction/evidence: With `NullnessDomain`, use block 0 `AssumeNonNull`, block 1 `_ => NullnessValue.Null`, edge 0 to 1, and initial `Null`. Block 0 output equals initialized bottom, so block 1 never runs even though `transfer(bottom)=Null`.

## Wave 2.2. HIGH - Resource accounting treats an ordinary lower solver snapshot as 32-bit wrap

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Member: `AccountResources`
- Current lines: 176-200, especially 193-197
- Mechanism: Any lower `rlimit count` becomes `(1L<<32)-previous+observed`; a fresh or reset solver may legitimately report a lower value without wrap.
- Impact: One cheap query can fabricate approximately 4.29 billion consumed units and prematurely exhaust the method budget.
- Safe reproduction/evidence: Use a controlled backend/statistics fixture whose second-query rlimit snapshot is lower than its first; the consumed count jumps near 2^32.

## Wave 2.3. MEDIUM - `long.MinValue % -1` incorrectly marked undefined

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Members: `QueryEncoder.EncodeDivision`, `DivisionDefined`
- Current lines: 514-527, 618-630
- Mechanism: Divide and Remainder share a guard rejecting min/-1. Division overflows, but C# remainder is defined as zero.
- Impact: Valid formulas abstain as `PostconditionMayBeUndefined`.
- Safe reproduction/evidence: Assume dividend equals `long.MinValue` and divisor equals -1, with a goal that remainder equals 0. The backend returns `Unknown` rather than `Proven`.

## Wave 2.4. MEDIUM - Cancellation permanently poisons a reusable SMT backend

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Members: `CheckAsync`, `Interrupt`
- Current lines: 47-54, 85-89
- Mechanism: The callback sets `_interrupted=true`; no successful path clears it, including a late-cancellation race. Every subsequent `CheckAsync` returns `Unavailable`.
- Impact: One cancellation retires an otherwise reusable backend and can cascade into an availability failure.
- Safe reproduction/evidence: Cancel an active check, await cancellation, and then submit `goal=true` with `CancellationToken.None`; the later check remains unavailable.

## Wave 2.5. MEDIUM - Canceled checks queued behind an active solver pin ThreadPool workers

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Member: `CheckAsync`
- Current lines: 39-46, 78-82
- Mechanism: Each call starts `Task.Run` and then synchronously blocks in `lock(_gate)`. Cancellation cannot stop a started delegate, and the callback is not registered until monitor admission.
- Impact: Concurrent canceled waiters occupy pool threads until the active solver releases the gate, delaying unrelated work.
- Safe reproduction/evidence: In a controlled test, hold the gate, start multiple checks, cancel the waiter tokens, and observe that the tasks remain blocked until gate release.

## Wave 2.6. MEDIUM - Large model-variable phases ignore cancellation and resource metering

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Members: `QueryEncoder` constructor, `CheckCore` bound loop, `CreateSatisfiable` decode loop
- Current lines: 314-345, 117-126, 249-260
- Mechanism: The loops do not poll the token. `Context.Interrupt` targets the native solve, not managed AST construction or enumeration, and rlimit meters only `solver.Check`.
- Impact: A canceled query may continue allocating and evaluating for a long time and bypass the query budget.
- Safe reproduction/evidence: Supply many explicit bool/int model variables, cancel after the gate is acquired, and observe delayed completion through the loops.

## Wave 2.7. MEDIUM - Depth validation rewalks a shared DAG for every assumption

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Members: `QueryEncoder` constructor, `ValidateDepth`
- Current lines: 308-312, 349-375
- Mechanism: A fresh `maximumDepths` dictionary is created per root, so the shared term DAG is traversed independently for each assumption before rlimit accounting.
- Impact: A compact query can cause O(assumptions * DAG size) unmetered CPU and allocation.
- Safe reproduction/evidence: Reuse one large shared predicate as many assumptions and compare construction time with a single-assumption query.

## Wave 2.8. LOW - Invalid options allocate native Z3 Context before validation

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Location: Primary-constructor field initialization
- Current lines: 6, 8-9
- Mechanism: `_context=new()` executes before `_options=NotNull(options)`. Null construction throws after the native owner is allocated, and the partially constructed `IDisposable` cannot be explicitly disposed.
- Impact: Repeated invalid construction leaves transient native cleanup to garbage collection and finalization.
- Safe reproduction/evidence: Repeatedly construct with null in an isolated memory test and observe native pressure before collection.

## Wave 2.9. LOW - Z3 rlimit symbol wrapper lacks deterministic per-query ownership

- File: `SharpProof.Smt/IrSmtBackend.cs`
- Member: `CheckCore`
- Current line: 114
- Mechanism: The string overload `parameters.Add("rlimit",...)` internally creates a symbol wrapper, unlike the explicitly disposed solver, parameters, statistics, model, and expressions.
- Impact: Many queries retain wrappers until finalization.
- Safe reproduction/evidence: Run repeated trivial checks without forced garbage collection and compare wrapper or native growth with an explicitly owned symbol.

## Wave 2.10. MEDIUM - Unsignaled backend cancellation is misreported as method timeout and retires a healthy lane

- File: `SharpProof.Worker/CallableVerificationPolicy.cs`
- Member: `VerifyTargetAsync`
- Current lines: 48-59
- Downstream: `SharpProof.Worker/SharpProofWorker.cs`, `RunLane`, lines 257-279
- Mechanism: Every `OperationCanceledException` becomes `ProjectTimeout` if the project token is canceled and otherwise becomes `MethodTimeout`; the code never checks `methodBoundary.IsCancellationRequested` or the exception token. An internal backend abort while all boundaries are live is labeled `MethodTimeout`, and the worker renews and disposes the lane.
- Impact: Telemetry and coverage falsely report `TimedOut`/`MethodTimeout`; an unnecessary lane replacement occurs, with possible work loss if renewal fails.
- Safe reproduction/evidence: Use a test backend that immediately throws `OperationCanceledException` while all time boundaries remain generous and uncanceled. Expected behavior is infrastructure failure or propagation; actual behavior is `MethodTimeout`.

## Wave 2.11. MEDIUM - Dependency provenance deduplication key is delimiter-ambiguous

- File: `SharpProof.Summaries/IrRelationalSummaryBuilder.cs`
- Member: `IrRelationalSummaryBuilder.Run.AddDependencyProvenance`
- Current lines: 480-487, especially 482-487
- Mechanism: The key concatenates origin, call identity, `|`, evidence identity, `|`, and digest without escaping or length-prefixing. Valid arbitrary identities can collide, and dictionary assignment overwrites the first entry.
- Impact: A composed summary silently omits genuine specification-pack dependency evidence.
- Safe reproduction/evidence: With the same origin and digest, use pairs `("A|B","C")` and `("A","B|C")` in two called summaries. The caller's `DependencyProvenance` contains one instead of two.

## Wave 2.12. MEDIUM - Enhanced `#line` character offsets make authenticated source locations unreconstructable

- Primary file: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`
- Member: `CaptureTree`
- Current lines: 125-137, especially 128-136
- Downstream: `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `CreateLocationAuthorities`/`CreateDiagnostic`, lines 84-86, 180-199, 202-245
- Mechanism: Capture samples only the mapped path, line, and column at a physical line start, then authority reconstructs later columns linearly. Enhanced C# `#line` mapping uses `startColumn + max(c-characterOffset,0)`, so the first generated line is not captured by that linear rule. Exact Roslyn mapped locations disagree and binding fails.
- Impact: Legal generated or Razor-style code with a selected location or diagnostic aborts manifest emission.
- Safe reproduction/evidence: Use a syntax tree containing `#line (5,3)-(5,17) 11 "template.dsl"` and a selected call or diagnostic after the character offset; capture reconstruction differs from Roslyn.

## Wave 2.13. MEDIUM - Relative syntax-tree paths are normalized only on the snapshot side

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`, `CaptureTree`, lines 139-145, especially 141; `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`, `CreateAuthority`, lines 343-363, especially 358; `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `BuildSummaryEvidence`, lines 116-127, especially 118-125.
- Mechanism: The snapshot uses `Path.GetFullPath(tree.FilePath)`, while source-summary authority stores the raw relative path; exact ordinal comparison cannot bind them.
- Impact: A compilation with relative-path generated trees and an inferred source summary aborts compiler manifest creation.
- Safe reproduction/evidence: Use syntax-tree path `generated/helper.g.cs` defining a scalar helper called by the selected method. The snapshot path is absolute while the authority path is relative.

## Wave 2.14. HIGH - Launcher can hang on a non-regular result path after worker supervision ends

- Files and members: `SharpProof.Worker.Protocol/ProtocolJson.cs`, `WorkerProtocolJson.OpenJsonReader`, lines 71-87, especially `FileStream` at 73-79; `SharpProof.Worker.Launcher/Program.cs`, `ValidateAndReport`, lines 334-349.
- Mechanism: The result path is opened before the regular-file metadata check. Opening a FIFO for reading can block with no writer. Validation occurs after worker `WaitForExit` and has no timeout or cancellation, so the hard limit no longer bounds it.
- Impact: The launcher or build can hang indefinitely.
- Safe reproduction/evidence: In a bounded Linux integration fixture, present a FIFO as `resultPath` at `ValidateAndReport` with no writer and observe that open blocks.

## Wave 2.15. MEDIUM - Unsupported platform or container preflight escapes unhandled

- File: `SharpProof.Worker.Launcher/Program.cs`
- Member: `RunMain` preflight
- Current lines: 48-87, especially `ValidatePreflight` at 50 and the catch filter at 75-79
- Related member: `ClassifyLauncherFailure`, lines 187-207
- Mechanism: `ContainerContract.ValidateRequired` can throw `PlatformNotSupportedException`, but the preflight catch omits it. The classifier that maps it to exit 125 is used only later.
- Impact: An unhandled stack trace appears instead of a controlled `containment.unsupported` response and exit.
- Safe reproduction/evidence: Invoke a valid launcher fixture outside the required platform or container marker and observe that the exception escapes `RunMain`.

## Wave 2.16. MEDIUM - Cooperative launcher probe accepts partial or invalid timeout evidence

- File: `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs`
- Member: `VerifyCooperativeLauncherCancellationAsync`
- Current lines: 156-173
- Related validation: `SharpProof.Worker.Protocol/ProtocolJson.cs`, `DeserializeResponse`, lines 56-59, and `ValidateResponse`, lines 265-290
- Mechanism: The probe checks only no errors, `TimedOut`, and any one `ProjectTimeout` claim or callable. It never runs `WorkerProtocolJson.Validate` or checks that every manifest item has a terminal result.
- Impact: A launcher regression that publishes a partial response can still pass-certify the timeout path.
- Safe reproduction/evidence: Provide a deserializable `TimedOut` response with no errors and one `ProjectTimeout`, but omissions that make `Validate` false. The current guard accepts it.

## Wave 2.17. MEDIUM - Worker cancellation measurement can hang indefinitely and ignores outer cancellation

- File: `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs`
- Member: `MeasureWorkerCancellationAsync`
- Current lines: 77-96, especially 80-88
- Mechanism: `VerifyAsync` receives only a private CTS. After the backend signals `Entered`, the method awaits `CancelAsync` and verification without a timeout or `WaitAsync(outer token)`.
- Impact: The cancellation regression under test can hang the gate forever, and canceling the gate caller cannot release it.
- Safe reproduction/evidence: Use a controlled backend that reaches `Entered` but does not complete after private cancellation. Cancel the outer token and observe that the verification await remains blocked.

## Wave 2.18. LOW/MEDIUM - Git child process survives OSS corpus import cancellation

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`
- Member: `ReadGitAsync`
- Current lines: 379-410
- Mechanism: The token is passed to stream reads and `WaitForExitAsync`, but there is no catch/finally kill-and-wait path. Disposing the `Process` wrapper does not terminate the OS process or tree.
- Impact: A canceled corpus update leaves a child running against the checkout and overlapping later work.
- Safe reproduction/evidence: In a disposable test environment, use a controlled slow git wrapper, cancel import, and verify that the child PID remains alive. Compare the kill paths in `PerformanceGate.RunDotnetAsync`, lines 644-664, and `PackageBuildSdkPin.ResolveSdkVersionAsync`, lines 105-125.

## Wave 2.19. MEDIUM - Corpus update writes tracked artifacts non-atomically and independently

- Files and members: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`, `ImportAsync`, lines 132-137 and 187-195; `SharpProof.Gates/Corpus/CorpusGate.cs`, `WriteActualSnapshotAsync`, lines 319-328.
- Mechanism: Direct `WriteAllTextAsync` truncates the final path; license, manifest, and snapshot are committed in separate steps.
- Impact: Cancellation, disk-full, or termination can destroy a previous valid file or leave a cross-file mismatch.
- Safe reproduction/evidence: Use an isolated temporary fixture that injects cancellation or a fault after destination creation or between license and manifest writes.

## Wave 2.20. MEDIUM - OSS corpus aggregation drops diagnostics outside selected method spans

- File: `SharpProof.Gates/Corpus/OpenSourceCorpusRunner.cs`
- Member: `ObserveAsync`
- Current lines: 147-155
- Mechanism: The analyzer returns all diagnostics, but each observation filters by exact tree plus `target.Span.Contains(location)`; there is no assertion that every diagnostic was assigned. `Location.None`, compilation-level, containing-type/global-using, and unselected-code diagnostics vanish.
- Impact: A new analyzer failure or diagnostic class can occur without changing the snapshot or count.
- Safe reproduction/evidence: Use an analyzer fixture that emits a compilation-end `Location.None` diagnostic while method outcomes exist. `canonicalDiagnostics` excludes it, so the gate can pass.

## Wave 2.21. HIGH - User-configured publication triple is not target-framework scoped

- File: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`
- Target: `_SharpProofInitializeVerify`
- Current lines: 46-51, especially 46-48
- Mechanism: Configured request, result, and compiler-manifest paths are used verbatim for each inner target framework. Only SARIF adds a `TargetFramework` subdirectory.
- Impact: Parallel target frameworks race invalidation and publication and may validate another framework's result; a serial build overwrites earlier evidence.
- Safe reproduction/evidence: Use a multitarget `net8.0`/`net9.0` fixture with verification and common configured publication paths; compare SARIF scoping at lines 50-51.

## Wave 2.22. MEDIUM - Clean resolves relative publication paths against process working directory

- Files and members: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, `SharpProofResetPublishedVerification`, lines 239-245 and task call 249-253; `SharpProof.BuildTasks/ResetPublishedVerification.cs`, `Execute`, lines 19-25.
- Mechanism: Clean passes relative user paths unchanged and the task lacks a `ProjectDirectory` base. Verification's working directory is the project directory at target line 204, while invalidation explicitly resolves against `ProjectDirectory` in `InvalidatePublishedResult.cs`, lines 80-89.
- Impact: Clean invoked from a solution or parent directory leaves real owned artifacts and can inspect or delete a same-named set under the caller's working directory, or fail on that set's metadata.
- Safe reproduction/evidence: Configure relative paths, build from the project directory, and clean the project from its parent directory.

## Wave 2.23. LOW - Verification policy normalization lowercases but does not trim

- File: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`
- Location: Property group at current lines 25-30; checks at 142-145
- Mechanism: The policy is normalized with `ToLowerInvariant` only, unlike adjacent configuration that trims.
- Impact: Leading or trailing whitespace in an otherwise valid CI or command-line value causes validation failure.
- Safe reproduction/evidence: Configure the verification or assumption policy with surrounding whitespace.

## Wave 2.24. MEDIUM - Failed fuzz campaigns withhold the campaign evidence named in the error

- Files and members: `scripts/Invoke-SharpProofFuzzCampaign.ps1`, `Invoke-FuzzRun` and campaign finalization, current lines 193-220, especially 212-220; `scripts/SharpProof.FuzzEvidenceLifecycle.ps1`, `Initialize-SharpProofFuzzEvidence`, lines 141-170.
- Mechanism: Initialization deletes prior `campaign.json`. The finalizer builds the summary and JSON but throws on any failed run before `Publish-SharpProofFuzzEvidence`, while the exception points to `summaryPath`.
- Impact: A failed nightly run loses aggregate commit, seed, count, run, and error evidence, and the reported path is absent.
- Safe reproduction/evidence: Use a disposable output with a runner that returns nonzero or invalid JSON. The thrown message names `campaign.json`, but that file does not exist.

## Wave 2.25. MEDIUM - Fuzz evidence labels dirty or stale execution as the HEAD commit

- File: `scripts/Invoke-SharpProofFuzzCampaign.ps1`
- Locations: Top-level current lines 21-43; run construction at 88-103; summary at 193-203
- Mechanism: The script records `git rev-parse HEAD` but does not reject dirty tracked state or authenticate binaries. It runs existing output with `--no-build` and writes the HEAD SHA regardless of working-tree or binary changes.
- Impact: A campaign from modified source or stale binaries is attributed to an unchanged commit, weakening reproducibility and freshness.
- Safe reproduction/evidence: In a disposable checkout, change tracked fuzz/product source or Release output without changing HEAD. The campaign commit remains HEAD. By contrast, `Test-SharpProofMutationCatalog.ps1` lines 21-27 rejects dirty tracked trees.

## Wave 2.26. MEDIUM - Parallel mutation shard cache does not authenticate receipt contents before merge

- File: `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1`
- Members/locations: `Test-CompleteShard`, current lines 83-109; reuse branch 234-244; merge/write 326-368
- Related file: `scripts/SharpProof.MutationEvidence.psm1`, lines 4-40
- Mechanism: Cached-shard validation checks schema, commit, configuration, catalog, counts, a hash-shaped string, and nonempty ledger, but does not hash the log, TRX, or baseline TRX and does not call `Read-SharpProofMutationTestEvidence`. A reused shard is merged and final evidence is written without `Test-SharpProofMutationCatalog`.
- Impact: A stale or corrupt cache can be published as a complete campaign; detection depends on a separately invoked validator.
- Safe reproduction/evidence: Use an isolated cached-shard fixture with internally inconsistent receipt contents but unchanged top-level descriptor, count, and hash-shaped fields. The producer reuses and merges it without rerun, while the standalone validator rejects it.

# Read-Only Multi-Agent Bug Audit - Wave 3 - 2026-08-29

This section records 37 findings from exactly 10 fresh read-only auditors after title/mechanism-only deduplication against Waves 1-2 and within Wave 3. The coordinator compiled the findings without reverification, and the central writer did not inspect or reverify the code.

## Wave 3.1. MEDIUM - User-defined operator and conversion calls bypass Requires call-site checking

- File: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`
- Member: `RequiresCallSiteDiscovery.GetCalls`
- Lines: 559-592, especially switch 565-591
- Mechanism: `GetCalls` recognizes invocation, object creation, property, event, and list-pattern operations, but Roslyn represents overloaded binary, unary, compound, and increment operations and user-defined conversions with unhandled operation shapes carrying `OperatorMethod`. `GetPotentialCallOwners` at lines 44-76 can screen out a caller containing only such a call before `RequiresCallSiteAnalyzer`, yielding silent `NotApplicable` instead of conservative `Unknown`.
- Impact: Violated `Contract.Requires` or closed parameter attributes on overloaded operators or conversions may produce no SP0027, and the caller may be recorded as `NotApplicable`.
- Safe evidence: Define `public static int operator +(Number x, [Positive] int y) => y;` and call `new Number() + -1`; alternatively, assign -1 through an implicit conversion taking `[Positive] int`. The bound operation exposes `OperatorMethod`, but the switch omits its operation shape.

## Wave 3.2. MEDIUM - Member-initializer reachability does not account for a non-completing base-constructor path

- File: `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`
- Members: `AnalyzeMemberInitializer`, `CanReachMemberInitializer`
- Lines: 439-512, especially 491-499; 515-561
- Mechanism: `CanReachMemberInitializer` checks only earlier initializers, not whether the selected constructor's explicit or implicit base initializer or delegated this-constructor chain can complete normally.
- Impact: SP0027 can be reported for an instance initializer that is provably unreachable because base construction always terminates first.
- Safe evidence: `class Base { protected Base() => throw new Exception(); } class Derived : Base { int x = Guard.Positive(-1); }`. The helper has no constructor/base completion check.

## Wave 3.3. HIGH - Calls nested inside larger expressions lose side effects and ref/out havoc

- Files and members: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LowerValue`, lines 241-262, and `LowerInvocation`, lines 291-303; `SharpProof.Frontend/RoslynOperationLowerer.cs`, `VisitInvocation`, lines 906-912, and `Opaque`, lines 279-316.
- Mechanism: Only a root invocation uses program-call lowering. An invocation nested beneath a binary operation, conversion, or other expression becomes an opaque term; the expression route cannot emit `IrCallInstruction` or havoc ref/out/memory. Observation records abstention without updating state.
- Impact: Later reads retain pre-call state, underapproximating behavior on the abstaining path.
- Safe evidence: Use `Mutate(ref x) + 1L` in a local initializer and then return `x`. The root is binary, so the call/havoc route is missed.

## Wave 3.4. HIGH - Program field loads bypass supported-value-domain admission

- Files and members: `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LowerValue`, lines 247-255, and `LowerLocation`, lines 340-343; `SharpProof.Frontend/RoslynOperationLowerer.cs`, `GetTypeId`, lines 78-109; intended check in `SharpProof.Frontend/CompilerIdentityBridge.cs`, `IsSupportedValueDomain`, lines 56-76.
- Mechanism: All field references are special-cased to `MemberLocation` plus `Load` without checking the field type. An unsupported struct falls through `GetTypeId` to an IR reference type with no abstention.
- Impact: Mutable struct copy/value semantics are represented as references while lowering remains `Exact`.
- Safe evidence: `struct Token { public long X; } static Token value; static Token Target()=>value;`.

## Wave 3.5. HIGH - Struct `this` bypasses value-domain admission

- Files and members: `SharpProof.Frontend/RoslynOperationLowerer.cs`, `VisitInstanceReference`, lines 509-513, `GetInstance`, lines 237-253, and `GetTypeId`, lines 78-109; `SharpProof.Frontend/RoslynProgramLowerer.cs`, `LowerReturn`/`LowerOptionalValue`, lines 476-485.
- Mechanism: `VisitInstanceReference` is always `Exact`; `GetInstance` maps a struct through `GetTypeId` to reference type with no `IsSupportedValueDomain` check.
- Impact: A struct instance escapes as exact reference IR, losing copy and alias semantics.
- Safe evidence: `struct Token { public long X; public Token Target()=>this; }`.

## Wave 3.6. MEDIUM-HIGH - Side-effecting ref-return assignment targets are skipped before the RHS

- File: `SharpProof.Frontend/RoslynProgramLowerer.cs`
- Members: `LowerAssignment`, lines 223-229; `LowerLocation`, lines 333-366; `LowerValue`, lines 241-263
- Mechanism: A ref-return invocation target is unsupported by `LowerLocation`, which returns without traversing the target. `LowerAssignment` lowers only the RHS and then havocs. C# evaluates the target call before the RHS.
- Impact: The target call, its side effects and exceptions, and evaluation order are absent; final havoc cannot restore call or RHS observations.
- Safe evidence: `Pick() = Probe();`, where `Pick` calls `Touch(1)` and returns a ref cell while `Probe` calls `Touch(2)`. Runtime order is 1 then 2, but lowering skips `Pick`.

## Wave 3.7. HIGH - Null-to-pointer conversions are admitted as exact null references

- Files and members: `SharpProof.Frontend/RoslynOperationLowerer.cs`, `VisitConversion`, lines 786-835, especially 821-824, `LowerConstant`, lines 361-397, and `GetTypeId`, lines 78-109; pointer rejection in `SharpProof.Frontend/CompilerIdentityBridge.cs`, `IsSupportedValueDomain`, lines 56-76.
- Mechanism: A null literal conversion to pointer routes to `LowerConstant`; `GetTypeId` creates an IR reference type, and the constant guard does not reject pointer kinds, returning exact null.
- Impact: Pointer or function-pointer domain enters exact reference IR despite explicit exclusion elsewhere.
- Safe evidence: `public static unsafe int* Target()=>null;` yields `factory.Null(pointerReferenceType)` with `Exact`.

## Wave 3.8. MEDIUM - Singleton Boolean combinators bypass type and factory validation

- File: `SharpProof.Ir/IrSemanticTerms.cs`
- Member: `Combine`
- Lines: 82-100, especially 91-94
- Mechanism: The `Conjoin`/`Disjoin` singleton path returns `terms[start]` after only a null check, unlike the multi-term path validated through `factory.Binary`.
- Impact: A canonical Boolean helper can return a non-Boolean or foreign-factory term, violating predicate invariants.
- Safe evidence: `Conjoin(factory,new[]{factory.Integer(1)})` returns an integer; a foreign-factory singleton likewise passes. `Disjoin` behaves the same way.

## Wave 3.9. MEDIUM - ConstrainSuccessfulEvaluation fast path bypasses predicate validation

- File: `SharpProof.Ir/IrSemanticTerms.cs`
- Member: `ConstrainSuccessfulEvaluation`
- Lines: 21-41, especially 29-32
- Mechanism: When the evaluated value is null, literal, or variable, the no-witness path returns the predicate without Boolean-type or factory-ownership checks; the witness path gets indirect `factory.Binary` validation.
- Impact: A malformed or foreign predicate can enter interning, substitution, summaries, or verification with invalid invariants.
- Safe evidence: `ConstrainSuccessfulEvaluation(factory, foreignFactory.Integer(1), null)` returns the foreign integer.

## Wave 3.10. MEDIUM - Boolean-typed IrValue with the wrong runtime kind causes a raw interpreter exception

- Files and members: `SharpProof.Ir/IrProgramInterpreter.cs`, `Execute`, lines 35-44 and 70-95, with `.Boolean` at 80 and 95; `SharpProof.Ir/IrInterpreter.cs`, `EvaluateVariable`, lines 174-189; `IrValue.Boolean`/`Get<T>`, lines 61-73.
- Mechanism: Initial values are validated only by `Type`. `Assume`, `Assert`, or `Branch` reads Boolean without a `Kind` check and reaches `Get<T>`'s `InvalidOperationException`.
- Impact: Execution escapes the structured `Unsupported`/`Exception` result model for malformed decoded or friend-produced values.
- Safe evidence: In a friend or test assembly, bind a Boolean condition variable to `new IrValue(factory.BooleanType, IrValueKind.Integer,1L)` and execute `Assume`, `Assert`, or `Branch`.

## Wave 3.11. MEDIUM - Public string-value construction accepts malformed UTF-16

- File: `SharpProof.Ir/IrFactory.cs`
- Member: `CreateStringValue`
- Lines: 240-245; contrast `String`, lines 317-325; supporting `SharpProof.Ir/IrInterpreter.cs`, `Text`, lines 505-507
- Mechanism: `CreateStringValue` checks only null, while the equivalent literal rejects malformed UTF-16. Object-to-string also routes through the permissive factory.
- Impact: Model or counterexample values with unpaired surrogates may be replaced or rejected by UTF-8/JSON, corrupting round trips or collapsing distinct values, while equivalent literal IR cannot be represented.
- Safe evidence: `factory.CreateStringValue("\uD800")` succeeds while `factory.String("\uD800")` throws.

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

## Wave 3.22. LOW - MayThrow exception metadata names are not canonicalized before semantic content hashing

- Files and members: `SharpProof.Specs/ApiSpecContentDigest.cs`, `Compute`, lines 33-37; `SharpProof.Specs/ApiSpecTable.cs`, `NormalizeFacets`, lines 280-294.
- Mechanism: `ExceptionMetadataNames` are hashed in supplied order and duplicates are accepted, although downstream semantics are set-valued. `[A,B]` and `[B,A]` differ, as do `[A]` and `[A,A]`.
- Impact: Equivalent declarations have unstable `ContentSha256`, weakening content-keyed cache and version identity.
- Safe evidence: Otherwise identical `MayThrow` declarations with reversed exception arrays produce different hashes.

## Wave 3.23. MEDIUM - Manifest validation accepts failed callables retaining lowered payloads that hydration rejects

- Files and members: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`, `HasFeatureScopeParity`, lines 532-535, and `HasValidCallableStates`, lines 775-787; contrasting `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `Decode`, lines 264-271.
- Mechanism: An allowed producer failure reason can retain Graph, Body, Clauses, and Variables. Parity skips payload after failure and state validation checks only the reason, while hydration requires null graph/body and empty clauses/variables.
- Impact: The wire validator accepts artifacts that downstream hydration rejects, causing a late failure and inconsistent cache/input admission.
- Safe evidence: Start from a valid successful artifact, set callable `FailureReason=UnsupportedBody`, retain the payload, recompute `FeatureScopeSha256`, and round-trip. `Deserialize` accepts while `DecodeCallables` rejects.

## Wave 3.24. MEDIUM - Manifest replay validation does not bind SyntaxTreeLineMapSha256 to the selected syntax-tree snapshot

- Files and members: `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`, `HasValidEffectReplayTrees`, lines 820-828, and `HasFeatureScopeParity`, lines 522-525; stricter `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs`, `HasValidReplayGeometry`, lines 65-75, especially 70.
- Mechanism: Manifest validation compares tree hash, snapshot, and geometry but omits the event line-map hash. Resealed evidence and self-hashes pass the manifest, while compilation-bound hydration rejects it.
- Impact: The canonical manifest admits replay evidence that is rejected as unbound later.
- Safe evidence: Replace an allocation replay's `SyntaxTreeLineMapSha256` with another 64-hex value, mirror the authority replay, seal the evidence, and recompute the feature hash. Serialization/deserialization passes; hydration rejects.

## Wave 3.25. MEDIUM - EffectAuthorities are outside the feature-scope fingerprint and manifest validation

- Files and members: `SharpProof.CompilerArtifact/CompilerFeatureScopeFingerprint.cs`, `AddCallable`, lines 39-93; `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`, `Validate`, lines 381-389, `HasFeatureScopeParity`, lines 504-530, and `HasValidEffectReplayTrees`, lines 799-833; later `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`, `DecodeEffects`, lines 439-474.
- Mechanism: Authority fields are neither hashed nor manifest-validated; they are checked only later by `CompilerEffectAuthority.Matches`.
- Impact: Different authority metadata shares feature-scope identity and passes the boundary, while malformed variants fail only during hydration.
- Safe evidence: Mutate `EffectAuthorities[0].SourceTreeSha256` in a valid artifact without hash changes. Round-trip does not inspect it, but `DecodeCallables` rejects.

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

## Wave 3.31. MEDIUM - Failed error responses can retain Proven claims and validate

- Files and members: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `TryProjectRunState`, lines 140-168, especially 147-167, and `MatchesCallableProjection`, lines 170-229, especially 178-228; `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateRun`, lines 585-620, and `ValidateUnknownCoverage`, lines 564-584.
- Mechanism: Recognized errors dictate run state without reconciling claim evidence. The failed-plus-errors projection does not inspect owned claim outcomes, and unknown coverage constrains only `Unknown` claims.
- Impact: A contradictory fatal failed run plus successful proof evidence can mislead consumers.
- Safe evidence: Start from a valid one-`Proven` response; add a `backend.unavailable` error, set run `Failed`/`BackendUnavailable`, callable `Incomplete`/`InfrastructureFailure`, cache `Disabled`, and recompute summary. Validation accepts it.

## Wave 3.32. LOW - CreateIncomplete throws on null entries in malformed manifests

- File: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`
- Member: `CreateIncomplete`
- Lines: 44-73; callable projection 50-57; claim projection 58-71
- Mechanism: `Select` projections dereference entries and collection properties without null guards despite the malformed-manifest failure-path comment.
- Impact: A structured failure is replaced by `NullReferenceException`, possibly leaving no result.
- Safe evidence: `Callables=[null!]` throws at line 53, `Claims=[null!]` at line 60, and null collections at `Select`.

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

## Wave 4.1. MEDIUM - Minimum compiler-host gate fails open when NETCoreSdkVersion is unset

- File: `SharpProof.Package/buildTransitive/SharpProof.targets`
- Members/locations: Analyzer `ItemGroup`s at current lines 20-62; target `_SharpProofValidateConfiguration` at lines 64-68, especially line 67
- Mechanism: The default profile is `advisory` at line 11, so analyzer and generator items are added whenever the profile is not `off` at lines 20-46. The minimum-host check requires both a nonempty `NETCoreSdkVersion` and a version below 9.0.300. On an MSBuild/Roslyn host that does not define `NETCoreSdkVersion`, such as a non-SDK-style project using `PackageReference` under Visual Studio/MSBuild, the second conjunct is false, so the target emits no error even though the package requires Roslyn 4.14 or newer. Analyzer assemblies are still passed to the compiler.
- Impact: Unsupported compiler hosts proceed into analyzer and source-generator loading instead of receiving the intended actionable configuration error. This can degrade to loader diagnostics or leave requested analysis unavailable; in strict configurations, it undermines the package's effort to reject unsupported hosts before compilation.
- Safe evidence: The line 67 truth table is sufficient: with `NETCoreSdkVersion=''`, `'$(NETCoreSdkVersion)' != ''` is false and the `<Error>` cannot execute, while item conditions at lines 20 and 47 do not test that property.

## Wave 4.2. MEDIUM - Invalid variable bindings escape Compare as host exceptions or false mismatches

- Files and members: `SharpProof.Testing/IrCSharpDifferentialOracle.cs`, `IrCSharpDifferentialOracle.Compare`, lines 37-38 and 75-87; `TryCreateProgram`, lines 107-115; `ToRuntimeValue`, lines 347-368. Related: `SharpProof.Ir/IrInterpreter.cs`, binding validation at lines 174-189.
- Mechanism: `IrInterpreter.Evaluate` converts a null or wrong-type binding into `IrEvaluationStatus.Unsupported`, but `TryCreateProgram` checks only that every referenced variable key exists. `Compare` then evaluates `ToRuntimeValue(variables[binding], ...)` and performs reflection argument binding inside a try that catches only `TargetInvocationException`. A null value dereferences `value.Kind` and throws `NullReferenceException`; a wrong-type value such as Boolean `IrValue` for a long parameter reaches `MethodInfo.Invoke` and throws an uncaught `ArgumentException`. If reflection accepts the mismatch, such as a string object supplied to an object parameter despite incompatible `IrValue.Type`, compiled execution can instead report a misleading `Mismatch` against the interpreter's `Unsupported` result.
- Impact: A public differential comparison can terminate a fuzz or test run instead of returning `DifferentialResult`, or can report a semantic mismatch caused only by an invalid test environment.
- Safe evidence: Source-path proof: `IrInterpreter` lines 174-189 returns `Unsupported` for missing, wrong, or null values; oracle lines 107-115 check only `ContainsKey`; lines 81-82 convert and invoke; lines 85-87 catch only exceptions thrown by the generated target.

## Wave 4.3. MEDIUM - Source-summary evidence compares normalized captured paths with raw syntax-tree paths

- Files and members: `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`, `CaptureTree`, lines 139-143, which stores `Path = CompilerCaptureAuthority.NormalizePath(tree.FilePath...)`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`, `CreateAuthority`, lines 343-363, especially line 358, which stores raw `declaration.SyntaxTree.FilePath`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`, `BuildSummaryEvidence`, lines 116-127, especially 118-123.
- Mechanism: The captured syntax-tree path and source-summary authority path derive from the same tree under different canonicalization rules. A valid syntax-tree path changed by `NormalizePath`, notably a relative path, cannot satisfy the exact ordinal `tree.Path == authority.SourcePath` comparison even though the tree SHA and span bind to the same tree.
- Impact: When callable lowering obtains a relational summary from such a source helper, final manifest creation throws `InvalidOperationException` stating that the source-summary authority is not bound to the captured source tree. `FinalCompilationCollector.Collect` then reports the compiler-manifest failure diagnostic instead of producing the current artifact, so otherwise valid projects using a source helper can lose verification output.
- Safe evidence: With helper `SyntaxTree.FilePath` such as `Helpers.cs` and a valid absolute project directory, `CaptureTree` records the normalized form while `CreateAuthority` retains `Helpers.cs`; `BuildSummaryEvidence` rejects the exact-string mismatch before serialization.

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

## Wave 4.7. HIGH - Cancellation disables retained-supervisor cleanup authentication

- File: `SharpProof.BuildTasks/RunVerifier.cs`
- Members: `RunVerifier.Execute`, lines 354-367; `RunVerifier.TryTerminate`, lines 939-946; `RunVerifier.ObserveCleanupAnchorAsync`, lines 694-708, with pidfd disposal/removal at 711-718
- Mechanism: When SIGTERM was sent but the supervisor remains alive, `TryTerminate` returns true and relies on a retained cleanup anchor to await the authenticated cleanup receipt. In `Execute.finally`, however, `_canceled` selects `authenticationFailure = null` before creating that anchor. `ObserveCleanupAnchorAsync` checks for a receipt only when `AuthenticationFailure` is nonnull. After cancellation, if the supervisor crashes, is SIGKILLed, or exits without publishing `SharpProof.Cleanup/1`, the observer closes the only retained pidfd and removes the anchor without reporting or attempting fallback cleanup.
- Impact: Verifier descendants, including session-escaping descendants reparented to the supervisor, can outlive a canceled build with no containment failure. This defeats the process-containment guarantee on the cancellation path.
- Safe evidence: Direct branch trace only. A focused unit test can create a retained anchor under `_canceled == true`, complete the process without completing `supervisorCleanupSignal`, and assert that the failure callback must still run or fallback cleanup occurs. Current construction makes the callback null, so the observer necessarily skips lines 697-707.

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

## Wave 5.23. HIGH - Advisory package policy validation ignores executable work in either props document

- File: `SharpProof.Gates/Performance/PerformanceGate.cs`
- Member: `ValidateAdvisoryPackagePolicy(XDocument...)`
- Current lines: 1358-1388, especially 1383-1388
- Mechanism: `portableContainsVerifierWork` scans only `portableTargets`; all verifier-work scans for unexpected core dependency, `CallTarget`, `Exec`, and `RunVerifier` inspect only `verifierTargets`. `portableProps` and `verifierProps` are used only for visible properties, marker, and host checks. Targets in `.props` are legal MSBuild, so a `Target BeforeTargets=CoreCompile` containing `Exec` or `RunVerifier` can be appended while expected elements remain unchanged and validation still succeeds.
- Impact: The performance/release gate can certify that advisory mode runs only the analyzer and omits verifier work while package props execute verifier or arbitrary process work during build, invalidating packaging policy and measured behavior.
- Safe evidence: The same `XDocument` mutation pattern as existing tests applies; no predicate observes executable `Target` or `Exec` in `portableProps`, unlike `portableTargets`.

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

## Wave 5.33. HIGH - Event assignment makes the accessor and handler path terminal unless the receiver is proven nonnull

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanEventAssignment`
- Current lines: 47-58, especially 49-56; accessor resolution at 64-87
- Mechanism: `receiverCheck.CompletesNormally` equals `_nullnessEvaluator.IsProvenNonNull`. For an unknown or maybe-null receiver this is false even though a nonnull runtime path exists. The method returns before scanning `HandlerValue` and resolving add or remove.
- Impact: Handler-expression calls, throws, allocations, and accessor effects can be absent, allowing false complete, pure, no-throw, or no-write proofs for ordinary nullable event receivers.
- Safe evidence: The same file's `ScanLock` is terminal only when proven null at line 148; `OperationEffectScanner.cs` `ScanCallStep` lines 629-638 uses `!IsProvenNull`. In `t.Changed += MakeHandler()` with maybe-null `t`, `MakeHandler` and `add_Changed` are reachable when `t` is nonnull but suppressed unless proven nonnull.

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

## Wave 6.13. HIGH - IrFactory.Cast accepts invalid scalar and nonreference casts

- File: `SharpProof.Ir/IrFactory.cs`
- Member: `Cast`
- Current lines: 465-485
- Mechanism: After ownership and identity/null folding, every remaining source and target pair is interned. `f.Cast(f.BooleanType, f.Integer(1))` creates a Boolean-declared cast with an integer operand, and int-to-object also succeeds. Construction does not require reference-like source and target types for nonidentity casts.
- Impact: Invalid IR crosses a central public factory invariant and reaches consumers, where it is only later classified `Unsupported`.
- Safe evidence: The two direct construction calls above.

## Wave 6.14. MEDIUM - IrFactory.Cast accepts null-to-nonnullable casts

- File: `SharpProof.Ir/IrFactory.cs`
- Member: `Cast`
- Current lines: 478-485
- Mechanism: Nullable targets are folded, but a null operand with `IntegerType` or `BooleanType` target falls through and becomes `IrCastTerm`; null's special path bypasses general source-kind validation.
- Impact: The factory constructs a term whose null operand cannot inhabit its declared scalar result; evaluation later reports `InvalidCast` instead of construction failing.
- Safe evidence: `f.Cast(f.IntegerType, f.Null(f.StringType))` succeeds.

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

## Wave 6.24. MEDIUM - Cancellation or deletion failure can strand an incomplete publication ownership set

- File: `SharpProof.Host/LinuxPathIdentity.cs`
- Member: `ResetPublicationSet`
- Current lines: 211-225, especially cancellation check at 223 and `File.Delete` at 224; incomplete-set rejection at 189-200
- Mechanism: After deleting one marker in a multimember set, cancellation before the next deletion or an `IOException` leaves a partial marker count. Every subsequent reset rejects that partial set before taking locks; there is no rollback or completion-finally path.
- Impact: Supported reset attempts fail permanently until metadata is repaired manually.
- Safe evidence: Direct irreversible per-item deletion control flow.

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

## Wave 7.2. HIGH - Nullable throw operands can make reachable NullReferenceException catches disappear

- Files and members: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions`, current lines 174-218, especially 193-217, consumed by `CanKnownReach`, lines 2743-2764; contrast `SharpProof.Effects/EffectExceptionFlow.cs`, `ResolveThrownException`, lines 17-32.
- Mechanism: For a throw operand not proven null, potential exceptions include only its static exception type, not `NullReferenceException`. A maybe-null operand can throw NRE at runtime, but `CanKnownReach` fails to match an NRE catch and marks it unreachable.
- Impact: Handler writes, capabilities, and throws are unsoundly omitted for direct nullable throws and source callees containing them.
- Safe evidence: `void M(InvalidOperationException? e) { try { throw e; } catch (NullReferenceException) { Mutate(); } }`; when `e` is null, runtime reaches the catch. Existing tests establish the intended maybe-null union but not this handler-reachability case.

## Wave 7.3. MEDIUM - Unreachable bare rethrows cause false escaping exceptions

- File: `SharpProof.Effects/EffectExceptionFlow.cs`
- Members: `ApplyCatches`/`ContainsRethrow`
- Current lines: 137-160 and 277-286
- Mechanism: `ContainsRethrow` is purely syntactic, so it marks a catch as rethrowing even when `throw;` is unreachable after return or a proven diverging call. `ApplyCatches` then preserves the protected exception whenever the catch is selected; `ManagedAbstractFlow` reachability is not consulted.
- Impact: False Throws effects or incompleteness and downstream false diagnostics.
- Safe evidence: `catch (E) { return; throw; }` or an unreachable rethrow after a diverging call. Existing tests around lines 5092 and 5327 cover handler writes, not the escaping throw set.

## Wave 7.4. MEDIUM - CreateIncomplete is not tolerant of malformed manifests it is used to report

- File: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`
- Member: `CreateIncomplete`
- Current lines: 50-72, with dereferences at 51-70
- Mechanism: The method directly enumerates `manifest.Callables` and `Claims` and dereferences entries. Null collections or null entries throw `NullReferenceException`. The method serves a failure path where the manifest can be malformed, and protocol validation represents these shapes as errors. `FirstOrDefault` over Callables can also dereference null while matching a nonnull claim.
- Impact: A malformed compiler or in-memory manifest turns an intended structured failure into an unhandled worker failure or no result, losing the diagnostic.
- Safe evidence: Invoke it with `Callables=null`, a null callable entry, `Claims=null`, or a null claim entry; current code throws instead of returning an incomplete response.

## Wave 7.5. MEDIUM - Protocol-error projection can discard contradictory fatal result evidence

- Files and members: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `TryProjectRunState`, current lines 140-167, especially 159-163, `Classify`, lines 99-137, and `MatchesCallableProjection`, lines 170-228; `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateRun`, lines 585-619.
- Mechanism: `Classify` maps `BackendUnavailable` evidence to `Failed/BackendUnavailable`, but when Errors is nonempty `TryProjectRunState` returns only the error-code tuple without comparing evidence. `worker.timeout` forces `TimedOut/None` despite `Unknown/BackendUnavailable` claim evidence; an `Incomplete/InfrastructureFailure` callable passes a status-independent `directInfrastructureFailure` branch.
- Impact: A corrupt producer can suppress fatal backend failure as timeout in a response accepted by `ValidateForRequest`, changing retry, telemetry, and policy behavior.
- Safe evidence: One `Unknown/BackendUnavailable` claim, an `Incomplete/InfrastructureFailure` callable, a `worker.timeout` error, `TimedOut/None` run status, and consistent summary/cache fields currently validate although `Classify` requires `Failed/BackendUnavailable`.

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

## Wave 7.12. MEDIUM - Constant-false catch filters are treated as possible normal paths

- Files and members: `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteTry`, current lines 155-166; `DefiniteOperationFacts.TryMayCompleteNormally` in `SharpProof.Effects/ManagedAbstractFlow.cs`, lines 2084-2095.
- Mechanism: The logic requires only that a catch-filter expression can complete, not that it can be true and enter the handler. Literal false therefore lets the handler count as a completing path.
- Impact: Suffix effects remain reachable after a try that always propagates, causing false effect contracts or witnesses.
- Safe evidence: `try { throw new Exception(); } catch (Exception) when (false) { } Mutate();`; runtime never reaches `Mutate`, but both predicates return true.

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

## Wave 7.17. HIGH - Event assignment null-checks the receiver before evaluating the handler value

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanEventAssignment`
- Current lines: 31-87, especially 47-62
- Mechanism: The scanner returns on a null receiver before scanning `HandlerValue`, but runtime order is receiver, handler expression, then accessor invocation and dereference.
- Impact: Handler-construction effects are omitted before an inevitable NRE.
- Safe evidence: `((Publisher)null!).E += BuildHandler()` executes `BuildHandler`; `ExceptionHandlerReachability` lines 313-327 models both receiver and handler completion before NRE.

## Wave 7.18. HIGH - User-defined conditional binary scanning omits op_True and op_False calls

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanBinary`
- Current lines: 175-205, especially 199-202; contrast `OperationCompletionEvaluator`, lines 1034-1065
- Mechanism: User-defined `&&` and `||` invoke a truth operator after the left operand to decide whether to evaluate the right, but the scanner resolves only `binary.OperatorMethod`, namely `op_BitwiseAnd` or `op_BitwiseOr`, never the distinct `op_False` or `op_True`.
- Impact: Writes, throws, divergence, and capabilities of truth operators are absent from effect summaries.
- Safe evidence: The completion evaluator explicitly locates these truth operators as separate calls.

## Wave 7.19. MEDIUM - Built-in constant short-circuit still scans unreachable RHS effects

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Member: `ScanBinary`
- Current lines: 175-205, RHS scan at 183
- Mechanism: The scanner processes the RHS before classifying the operator, so `false && rhs` and `true || rhs` always acquire RHS effects.
- Impact: Impossible writes, calls, and throws are reported, causing false effect diagnostics.
- Safe evidence: `false && Mutate()` or `true || Mutate()`; `ExceptionHandlerReachability` lines 1015-1037 skips the RHS for these constants.

## Wave 7.20. HIGH - Conditional expressions are scanned as a linear child sequence

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Members: `ScanCoreOperationTail`/`ScanDefault`
- Current lines: 335-343 and 346-383; downstream `OperationEffectScanner.ScanSequence`, lines 952-963
- Mechanism: No `IConditionalOperation` case exists. Fallback scans both arms linearly; constant unchosen arms produce false positives, and if an unknown condition's first arm cannot complete, `ScanSequence` breaks before scanning the reachable second arm.
- Impact: Reachable effects can be omitted and unreachable effects can be added.
- Safe evidence: `b ? AlwaysThrows() : Mutate()` omits `Mutate`; `OperationCompletionEvaluator` lines 1100-1117 and `ExceptionHandlerReachability` lines 1038-1058 implement branch selection and joining.

## Wave 7.21. MEDIUM - Coalesce fallback always scans WhenNull even when the left side is definitely nonnull

- File: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
- Members: Fallback at current lines 335-343 and 346-383; `ScanSequence`, lines 952-963
- Mechanism: No `ICoalesceOperation` case exists, so the child sequence always includes the RHS regardless of known nullness.
- Impact: Impossible RHS effects are reported and can create false contract failures.
- Safe evidence: `new object() ?? Mutate()`; `ExceptionHandlerReachability` lines 1059-1071 suppresses `WhenNull` when the left side is definitely or proven nonnull.

## Wave 7.22. HIGH - External exception-construction throw path omits object-initializer effects

- File: `SharpProof.Effects/OperationEffectScanner.cs`
- Member: `ScanThrow`
- Current lines: 756-781; contrast `ScanObjectCreation`, lines 737-740
- Mechanism: The special unmodeled external-construction path scans constructor arguments and construction but never `creation.Initializer` before the explicit throw.
- Impact: Initializer setter writes, calls, and throws are omitted from effect summaries.
- Safe evidence: `throw new Exception { Source = Mutate() };` evaluates `Mutate` and invokes the `Source` setter before throwing; the normal object-creation path scans the initializer.

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

## Wave 7.26. HIGH - Source-nullness fallback misses mutation through ref-local aliases

- File: `SharpProof.Effects/OperationNullnessEvaluator.cs`
- Member: `IsSourceDefinitelyNull`
- Current lines: 76-98; consumed by `ScanCallStep`
- Mechanism: Textual invalidation compares assignment targets and ref arguments only to the original local symbol. Assignment through a ref-local alias targets the alias symbol, so initial-null fallback remains true; `IsProvenNull` ORs it with abstract flow, preventing correct nonnull flow from overriding it.
- Impact: Later calls on the reassigned original local are treated definitely null, and their real receiver effects can be omitted.
- Safe evidence: `object? x=null; ref object? alias=ref x; alias=new Effectful(); x.ToString();`; target `alias.Local` differs from `x.Local`, and no ref/out argument exists.

## Wave 7.27. MEDIUM - Reference nulls inside conditional spec terms are not contextualized to the peer type

- File: `SharpProof.Specs/ApiSpecInstantiation.cs`
- Members: `ApiSpecInstantiator.Instantiation.Null`, current lines 172-183; `Conditional`, lines 269-287; direct equality handling at 199-229 and 248-267
- Mechanism: `SpecNullDeclaration(Reference)` always becomes `factory.ObjectType`; `Conditional` instantiates branches independently. With a concrete reference substitution such as `Widget`, a null/Object branch and Widget peer fail `factory.Conditional` type equality although the specification expression is validator-accepted and total.
- Impact: Trusted postconditions for concrete reference arguments or results are silently dropped as `Failed/InvalidExpression`.
- Safe evidence: `Equal(Conditional(flag, Null(Reference), value, Reference), value)` with Boolean `flag` and Widget-typed `value`; `ApiSpecTable.Create` accepts, while `InstantiatePostconditions` fails.

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

## Wave 8.6. MEDIUM - Classic `is Type` catch filters excluding cancellation are falsely diagnosed

- File: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`
- Member: `FilterExcludesCancellation`
- Current lines: 187-208, especially syntax and operation requirements at 193 and 197-199; contrast `FilterIncludesAllCancellation`, lines 113-121
- Mechanism: Roslyn represents `caught is ArgumentException` as `IIsTypeOperation`. The exclusion path requires `IsPatternExpressionSyntax` and `IIsPatternOperation`, so it never recognizes this filter, while the inclusion path explicitly handles `IIsTypeOperation`.
- Impact: Error-severity SPMETA003 blocks a build for a catch that cannot swallow `OperationCanceledException`.
- Safe evidence: `catch (Exception caught) when (caught is ArgumentException) { }` should produce no SPMETA003, but the analyzer reports it.

## Wave 8.7. MEDIUM - Generic exception identity omits assembly identity of type arguments

- Files and members: `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs`, `Encode`, current lines 5-15; `CompilerIdentityBridge.CreateTypeDisplay`/`TypeReference`, lines 151-156 and 171-175; `ClaimManifestBuilder`, lines 373-376.
- Mechanism: Identity prefixes only the outer generic exception's assembly and then appends the Roslyn documentation reference ID. Embedded type-argument IDs omit their defining assembly, so constructed types with same metadata-named arguments from different assemblies collide.
- Impact: Distinct allowed-exception constraints collapse in manifest identity and hash, and evidence cannot identify which constructed exception was analyzed.
- Safe evidence: Use one `GenericBoomException<T>` plus extern-aliased `Collision.Payload` types from distinct assembly identities; the constructed symbols differ but `Encode` returns equal strings.

## Wave 8.8. HIGH - SPMETA001 is bypassed by method-group or delegate invocation

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `Initialize` registration at line 65; `AnalyzeInvocation`, current lines 90-101
- Mechanism: The analyzer registers only `OperationKind.Invocation` and checks `invocation.TargetMethod`. Capturing a forbidden API as `IMethodReferenceOperation` is unseen; the later delegate call targets delegate `Invoke`, not the original forbidden method.
- Impact: Soundness-critical forbidden APIs can execute without SPMETA001, bypassing semantic-model and other enforced boundaries.
- Safe evidence: `Func<SymbolDisplayFormat?,string> f = symbol.ToDisplayString; return f(null);`; the same pattern applies to delegate-wrapped `Compilation.GetSemanticModel` or `ReplaceSyntaxTree`.

## Wave 8.9. MEDIUM - SPMETA005 has a namespace-wide self-exemption unrelated to generated catalog identity

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Member: `AnalyzeObjectCreation`
- Current lines: 132-141, especially 136-138; generated-code exclusion at line 61
- Mechanism: Any `DiagnosticDescriptor` construction in namespace exactly `SharpProof.Meta.Analyzers` is exempt regardless of containing type, file, or generated status.
- Impact: Production code can declare that namespace and bypass stable IDs, help links, and catalog generation; handwritten descriptors within the meta-analyzer namespace are unchecked.
- Safe evidence: Handwritten `namespace SharpProof.Meta.Analyzers; static class Rogue { static readonly DiagnosticDescriptor Rule = new(...); }` yields no SPMETA005. Generated code is already separately excluded.

## Wave 8.10. HIGH - MIT license validation accepts modified or restricted text and labels it SPDX MIT

- Files and members: `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs`, `ImportAsync`, current lines 99-117 and 145-151; catalog `ValidateSource`, lines 318-345.
- Mechanism: The importer checks only that decoded LICENSE starts with `The MIT License (MIT)` and then unconditionally emits `LicenseSpdx=MIT`. The catalog trusts the label and verifies copied bytes only against their recorded hash.
- Impact: The corpus can redistribute code under added or incompatible restrictions while asserting MIT, defeating the legal and provenance gate.
- Safe evidence: `The MIT License (MIT)\nAdditional restriction: no redistribution` passes the prefix check, is copied and hashed, and later validates.

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

## Wave 9.10. MEDIUM - `foreach` protocol calls bypass Requires call-site checking

- File: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`
- Members: `GetCalls`, current lines 559-592; `GetPotentialCallOwners`, lines 44-76; downstream `RequiresCallSiteTreeAnalyzer.Analyze`, lines 41-44
- Mechanism: Recognized shapes omit `IForEachLoopOperation`. Implicit `GetEnumerator`, `MoveNext`, `Current`, and disposal calls are not invocation or property descendants and never become candidates; screening may return `NotApplicable`.
- Impact: Violated preconditions on user-defined foreach protocol members produce no SP0027, and the caller may be recorded `NotApplicable`.
- Safe evidence: `Seq.GetEnumerator` begins with `Contract.Requires(false)`; `foreach (var x in s) {}` executes it, but discovery omits the protocol call.

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

## Wave 9.13. HIGH - Noncompleting object initializer is skipped entirely

- Files and members: `SharpProof.Effects/OperationEffectScanner.cs`, `ScanObjectCreation`, current lines 706-742, especially 733-740; `SharpProof.Effects/OperationCompletionEvaluator.cs`, `CanCompleteConstruction`, lines 901-914.
- Mechanism: The constructor `EffectStep` uses a completion predicate for the whole creation, including the initializer. If the constructor completes but the initializer cannot, the constructor step is marked noncompleting and the scanner never visits the initializer.
- Impact: Initializer argument calls, setters, writes, capabilities, and terminal throw or nontermination are omitted, enabling false purity, no-write, or no-throw results.
- Safe evidence: `new C { P = Boom() }`, where `Boom` writes static state and throws; construction completion is false, causing the scanner to skip `Boom` and the setter.

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

## Wave 10.11. HIGH - Base-typed thrown values make reachable subtype catches disappear

- Files and members: `SharpProof.Effects/ExceptionHandlerReachability.cs`, `GetPotentialExceptions` for throw, current lines 174-219, `CanKnownReach`, lines 2743-2764, and `CatchesKnownType`, lines 2787-2799; contrast `EffectExceptionFlow.GetTypeSelection`, lines 225-241.
- Mechanism: The throw operand is tracked only by static type, and a catch is accepted only if the static thrown type derives from the caught type. A runtime subtype held in a base-typed value is ignored.
- Impact: Reachable handler writes, capabilities, and throws are omitted, yielding unsound summaries.
- Safe evidence: An `Exception e` holds `InvalidOperationException`; `throw e` inside `catch (InvalidOperationException) { Mutate(); }`. Potential type is only `Exception`, so the catch is marked unreachable.

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

## Wave 10.18. HIGH - Ref and out mutations are absent from reaching-local definitions

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Members: `GetReachingLocalValues`/`TransferLocalValues`/`GetLocalWriteValue`, current lines 117-181 and 204-251, with write switch at 239-250
- Mechanism: Only initializers and simple assignments to a local are recognized. An invocation writing a local through out or ref is ignored.
- Impact: Helper mutation bypasses SPMETA010 and permits an unsafe answer into the cache.
- Safe evidence: `var answer=Answer.Proven; SetUnknown(out answer); cache.Write(answer);`; analysis retains only the Proven initializer.

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

## Wave 11.1. MEDIUM - Callable projection rejects the declared UnsupportedContract coverage state

- Files and members: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `MatchesCallableProjection`, current lines 189-219, especially 200-212; `ProtocolJson.ValidateRun`, lines 606-618; generated tuple catalogs, lines 710-755.
- Mechanism: Owned `Unknown` claims recognize only all-`UnsupportedCallable`; all-`UnsupportedContract` falls to `SemanticUnknown`, despite the schema admitting callable `Incomplete/UnsupportedContract` and claim `Unknown/UnsupportedContract`.
- Impact: A schema-conforming response is rejected as malformed, preventing the producer from preserving the advertised reason.
- Safe evidence: For a `Complete/None` run with callable `Incomplete/UnsupportedContract` and all claims `Unknown/UnsupportedContract`, `Classify` returns `Complete/None`, but projection demands `SemanticUnknown`.

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

## Wave 11.12. HIGH - Dynamic dispatch bypasses every SPMETA001 forbidden-API check

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `Initialize`, current lines 62-71, with Invocation registration at line 65; `AnalyzeInvocation`, lines 90-100
- Mechanism: Dynamic receiver calls are `IDynamicInvocationOperation` and `OperationKind.DynamicInvocation`, not `IInvocationOperation`; no action is registered and no `TargetMethod` is inspected.
- Impact: Soundness-critical code executes forbidden Roslyn APIs without SPMETA001.
- Safe evidence: `dynamic c=compilation; c.ReplaceSyntaxTree(oldTree,newTree);` or dynamic `symbol.ToDisplayString`; direct calls diagnose, while dynamic calls are unseen.

## Wave 11.13. HIGH - Constructor-bypass allocation fabricates kernel-only outcomes without SPMETA011

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `Initialize`, current lines 62-71; `AnalyzeObjectCreation`, lines 132-175, with protected-type check at 167-174
- Mechanism: Enforcement applies only to `IObjectCreationOperation`. `RuntimeHelpers.GetUninitializedObject` allocates a protected type through an ordinary invocation and is not forbidden or inspected.
- Impact: Nonkernel code can manufacture a `ProvenOutcome` runtime instance; consumers trust type identity for cache and proof paths despite uninitialized fields.
- Safe evidence: `(ProvenOutcome)RuntimeHelpers.GetUninitializedObject(typeof(ProvenOutcome))` contains no ObjectCreation, and `RuntimeHelpers` is absent from the forbidden catalog.

## Wave 11.14. MEDIUM - Whitespace-specific operator fragments bypass SPMETA009

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Members: `CSharpExpressionFragments`, current lines 48-49; `AnalyzeCSharpExpressionText`/`GetFragment`, lines 256-268 and 301-308
- Mechanism: The catalog uses literal padded fragments ` == `, ` != `, ` && `, and ` || `; C# allows zero or asymmetric whitespace, and `IndexOf` misses those forms.
- Impact: Soundness layers can synthesize equivalent C# expression text while the error-level anti-synthesis analyzer remains silent.
- Safe evidence: `x + "==null"`, `"!=null"`, `"&&true"`, or `"||false"` contain no padded fragment but form valid expression text.

## Wave 11.15. MEDIUM - object.Equals bypasses SPMETA004 semantic-string comparison coverage

- File: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`
- Member: `AnalyzeSemanticEquals`
- Current lines: 207-224, especially 212-214
- Mechanism: Analysis proceeds only when `TargetMethod.ContainingType` is `System.String`. `object.Equals(reason,"ir_timeout")` targets `System.Object`; binary and pattern handlers do not apply.
- Impact: `ir.*` or `ir_*` reason strings can directly control logic without SPMETA004, preserving stringly semantic behavior.
- Safe evidence: `if (object.Equals(reason,"ir_timeout")) return true;` is a recognized control condition with a literal but fails the containing-type gate.

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

## Wave 12.22. HIGH - Const enum aliases are treated as inherently cacheable

- File: `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs`
- Member: `IsNonCacheableSemanticAnswer`
- Current lines: 74-90, especially 76-78 and 88-89
- Mechanism: Special enum handling applies only when the referenced field's containing type is the enum. A const Answer field on another type misses it, and then `ConstantValue.HasValue` marks it safe without checking the aliased value or initializer.
- Impact: A named constant can alias Unknown, timeout, or failure and be persisted without SPMETA010.
- Safe evidence: `const Answer Abstain = Answer.Unknown; cache.Write(Abstain);`.

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
