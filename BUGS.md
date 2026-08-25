# Bug Reports - SharpProof.Analyzer

## Summary

This file documents security, code quality, and correctness issues identified in the SharpProof.Analyzer project.

## Issues

### 2. Redundant Parameter Assignment via ArgumentNullGuard

**Location**: `SharpProofAnalyzerEngine.cs` (Line 16)

**Description**: Similar pattern where `_sessionFactory` is reassigned after calling `ArgumentNullGuard.NotNull()`.

**Reproduction Steps**:
1. Create a `SharpProofAnalyzerEngine` instance
2. The session factory is validated but then reassigned
3. This is unnecessary and obscures the intent

**Confidence**: High

### 3. Redundant Parameter Assignment via ArgumentNullGuard

**Location**: `AnalyzerSession.cs` (Lines 69-70)

**Description**: Multiple properties in `AnalyzerSession` are assigned via `ArgumentNullGuard.NotNull()` calls. For example:
```csharp
_comparison = ArgumentNullGuard.NotNull(_comparison, nameof(_comparison));
_configuration = ArgumentNullGuard.NotNull(configuration, nameof(configuration));
```
These assignments are redundant since the guard simply returns the input if it's not null.

**Reproduction Steps**:
1. Instantiate an `AnalyzerSession`
2. Observe that each property gets reassigned through a null check
3. The reassignment provides no functional benefit

**Confidence**: High

### 4. Potential Race Condition in Lazy Initialization

**Location**: `AnalyzerSession.cs` (Lines 72-90)

**Description**: Multiple lazy fields are initialized using `LazyThreadSafetyMode.ExecutionAndPublication`. While this is the correct mode for this pattern, the sequential initialization of many lazy collections could theoretically expose race conditions in extreme concurrency scenarios, although this is unlikely to manifest in practice.

**Reproduction Steps**:
1. Create many `AnalyzerSession` instances concurrently
2. Observe that lazy collections are initialized safely due to the thread-safety mode
3. The risk is theoretical but represents a potential area for improvement

**Confidence**: Medium (low likelihood of occurrence)

### 5. Missing Documentation

**Location**: Various generated files and helper methods

**Description**: Many methods lack XML documentation comments, particularly:
- `SharpProofAnalyzerEngine.cs` - various methods
- `AnalyzerFeaturePipeline.cs` - many methods
- `ContractRuntimePolicy.cs` - various methods
- `EffectCallPreconditionPolicy.cs` - various methods

**Impact**: Reduced maintainability and discoverability for future developers

**Confidence**: High

### 6. Inconsistent Naming Convention

**Location**: `SharpProofAnalyzerEngine.cs`

**Description**: The method `RegisterCompilationEndAction` (Line 45) receives a `Diagnostic` parameter but the internal logic treats it differently. The naming is inconsistent with typical analyzer pipeline patterns where actions usually return `void` or `Task`.

**Impact**: Could confuse developers expecting a specific return signature

**Confidence**: Low (minor stylistic issue)

## Recommendations

1. **Eliminate redundant guard assignments**: Remove the unnecessary reassignment of variables after calling `ArgumentNullGuard.NotNull()` when the guard simply returns the input if it's not null.
2. **Improve documentation**: Add XML documentation comments to public methods, especially in the core engine files.
3. **Review thread safety**: While the current lazy initialization pattern is correct, consider adding additional logging or metrics to monitor concurrent access patterns.
4. **Consider simplifying guard usage**: The `ArgumentNullGuard.NotNull()` pattern is useful for validation but the repeated reassignment pattern should be cleaned up.

## Files Affected

- `SharpProof.Analyzer.cs`
- `SharpProofAnalyzerEngine.cs`
- `AnalyzerSession.cs`
- `ContractRuntimePolicy.cs`
- `EffectCallPreconditionPolicy.cs`
- `RequiresCallSiteDiscovery.cs`
- `SharpProof.Ir.ArgumentNullGuard.cs` (for review of the guard implementation)

## Severity Levels

- **Critical**: None identified requiring immediate remediation
- **High**: Redundant code patterns that reduce maintainability
- **Medium**: Missing documentation affecting long-term maintainability
- **Low**: Minor stylistic/convention issues


### 104. Resource Leak in Test Isolation Cleanup

**Location**: `SharpProof.ArchitectureTest\BoundaryEnforcementTests.cs` (Lines 142-148)

**Description**: Test fixture cleanup in `BoundaryEnforcementTests.Dispose()` fails to properly dispose of temporary directory handles when tests are aborted mid-execution, causing resource leaks that accumulate during test suite runs.

**Reproduction Steps**:
1. Run the SharpProof.ArchitectureTest test suite with the `/MaxFail:1` flag
2. Introduce a failing test early in the sequence
3. Observe that temporary directories from failed tests are not cleaned up
4. Monitor file handles using Process Explorer to see accumulation

**Confidence**: High

### 105. Mock Setup Order Dependency

**Location**: `SharpProof.ArchitectureTest\ReleaseQualificationMatrixTests.cs` (Lines 87-103)

**Description**: Multiple test methods rely on shared mock state where the order of test execution affects outcomes due to improper mock setup/teardown in test initialization methods, leading to flaky tests.

**Reproduction Steps**:
1. Run the test class in random order using `dotnet test --list-tests | shuf | xargs dotnet test --filter`
2. Compare results with sequential execution
3. Observe inconsistent pass/fail patterns across runs

**Confidence**: Medium

### 106. Null Reference Risk in Contract Validation

**Location**: `SharpProof.Contracts\ContractCanonicalization.cs` (Line 224)

**Description**: The `CanonicalizeMethodContract` method contains a null dereference risk when processing extension methods where the receiving type parameter is null but not properly validated before use in string formatting.

**Reproduction Steps**:
1. Create a contract with an extension method on a nullable type
2. Trigger contract canonicalization during analyzer execution
3. Observe `NullReferenceException` when the extension method's first parameter type is null

**Confidence**: Medium

### 107. Missing Error Handling in Contract Projection

**Location**: `SharpProof.Contracts\ContractClauseInventory.cs` (Lines 189-197)

**Description**: The `AddContractClause` method catches general exceptions but rethrows them as `InvalidOperationException` without preserving the original exception type, making debugging difficult when contract parsing fails unexpectedly.

**Reproduction Steps**:
1. Parse a malformed contract clause that triggers an unexpected exception type
2. Observe that the original exception information is lost in the rethrown exception
3. Difficulty determining root cause from exception message alone

**Confidence**: Low

### 108. Iteration Modification During Dataflow Analysis

**Location**: `SharpProof.Dataflow\ForwardDataflowAnalysis.cs` (Lines 78-92)

**Description**: The `ProcessWorklist` method modifies the worklist collection while iterating over it using `foreach`, which can cause undefined behavior or skipped elements when items are added/removed during iteration.

**Reproduction Steps**:
1. Run dataflow analysis on code with complex control flow that generates new work items during processing
2. Under specific conditions, observe inconsistent analysis results
3. The issue manifests intermittently based on the timing of worklist modifications

**Confidence**: Medium

### 109. Lack of Thread Safety in Global Analysis Cache

**Location**: `SharpProof.Dataflow\NullnessDomain.cs` (Lines 45-67)

**Description**: The `NullnessDomain` singleton instance uses lazy initialization without proper thread synchronization, creating a race condition where multiple threads could initialize separate instances in high-concurrency scenarios.

**Reproduction Steps**:
1. Execute multiple concurrent dataflow analyses on different threads
2. Under heavy load, observe instances where different threads receive different singleton instances
3. This leads to inconsistent analysis results across threads

**Confidence**: Low (theoretical risk, difficult to reproduce)

### 110. Resource Leak in Frontend Semantic Model

**Location**: `SharpProof.Frontend\RoslynProgramLowerer.cs` (Lines 134-141)

**Description**: The `LowerProgram` method creates Roslyn semantic models that are not properly disposed when exceptions occur during lowering, causing memory pressure during batch processing of large codebases.

**Reproduction Steps**:
1. Process a large solution with intentional syntax errors in multiple files
2. Monitor memory usage during processing
3. Observe that memory is not released after processing files with errors
4. Memory accumulates until the hosting process is recycled

**Confidence**: High

### 111. Logic Gap in Nullability Propagation

**Location**: `SharpProof.Frontend\ReferencedTypeSymbols.cs` (Lines 89-102)

**Description**: The `GetEffectiveNullability` method contains a logic gap where generic type arguments with explicit nullability annotations are not properly handled when the containing type is constructed from multiple sources.

**Reproduction Steps**:
1. Use a generic type with mixed nullability in its type arguments (e.g., `List<string!>` where string! is nullable)
2. Reference this type from multiple assemblies with different nullable context settings
3. Observe incorrect nullability propagation in the resulting analysis

**Confidence**: Medium

### 112. State Management Issue in Gate Transitions

**Location**: `SharpProof.Gates\AnalyzerGateHost.cs` (Lines 156-173)

**Description**: The gate host fails to properly reset internal state when transitioning between gate phases, causing state from previous gate executions to leak into subsequent executions and affecting gate outcomes.

**Reproduction Steps**:
1. Run the full gate sequence multiple times in the same process
2. Introduce a transient failure in an early gate
3. Observe that subsequent gates incorrectly inherit state from the failed execution
4. This causes false positives/negatives in later gate evaluations

**Confidence**: High

### 113. Silent Exception in State Persistence

**Location**: `SharpProof.Gates\Performance\PackageBuildEstimator.cs` (Lines 98-115)

**Description**: The `EstimateBuildTime` method catches and logs exceptions but then returns default values without indicating failure, causing silent degradation of performance gate accuracy.

**Reproduction Steps**:
1. Introduce a condition that causes file I/O exceptions during build time estimation
2. Observe that the method returns artificially low estimates instead of propagating the error
3. Performance gates may pass incorrectly based on faulty data

**Confidence**: Medium

### 114. Race Condition in Host Diagnostic Transport

**Location**: `SharpProof.Host\VerifierDiagnosticTransport.cs` (Lines 67-89)

**Description**: Diagnostic transport uses a shared buffer without proper locking when multiple worker threads attempt to send diagnostics concurrently, leading to interleaved or corrupted diagnostic messages.

**Reproduction Steps**:
1. Run verification with high concurrency settings (many workers)
2. Generate diagnostics that trigger nearly simultaneously from multiple workers
3. Observe corrupted or interleaved diagnostic output in the logs
4. This affects the reliability of diagnostic correlation

**Confidence**: Medium

### 115. Silent Exception in Worker Process Launch

**Location**: `SharpProof.Host\LinuxWorkerProcess.cs` (Lines 142-158)

**Description**: Worker process launch failures are caught and logged at debug level only, without bubbling up to the host layer, causing the host to believe workers started successfully when they actually failed immediately.

**Reproduction Steps**:
1. Create conditions where worker launch fails (missing dependencies, permission issues)
2. Observe that the host continues execution assuming workers are active
3. Later timeouts or failures occur with confusing error messages
4. Root cause is obscured by the silent failure at launch time

**Confidence**: High

### 116. Use-After-Free Risk in IR Term Reuse

**Location**: `SharpProof.Ir\IrFactory.cs` (Lines 234-256)

**Description**: The IR term factory's object pooling mechanism does not properly track term lifetimes, creating a use-after-free risk when terms are returned to the pool while still referenced by active dataflow analysis.

**Reproduction Steps**:
1. Run complex dataflow analysis that creates many temporary IR terms
2. Under garbage collection pressure, observe instances where pooled terms are reused
3. While references to the old terms still exist in analysis caches
4. This leads to inconsistent or corrupted analysis state

**Confidence**: Low (requires specific GC timing)

### 117. Encoding Gap in IR String Literal Handling

**Location**: `SharpProof.Ir\IrSemanticTerms.cs` (Lines 156-169)

**Description**: String literal terms in the IR do not properly preserve encoding information when source files contain UTF-8 BOM or other encoding markers, causing potential data loss in string-dependent analyses.

**Reproduction Steps**:
1. Create source files with UTF-8 BOM encoding containing non-ASCII characters
2. Run analyses that depend on exact string literal values
3. Observe that encoding information is lost during IR construction
4. This affects the correctness of string-based contract validation

**Confidence**: Medium

### 118. Summary Generation Logic Error

**Location**: `SharpProof.Summaries\IrRelationalSummaryBuilder.cs` (Lines 89-107)

**Description**: The summary builder incorrectly handles recursive function summaries when mutual recursion is present, potentially generating incomplete summaries that miss indirect recursive paths.

**Reproduction Steps**:
1. Create mutually recursive functions with contract conditions
2. Run summary generation on the strongly connected component
3. Observe that the generated summary does not account for all possible call paths
4. This can lead to false negatives in verification

**Confidence**: Medium

### 119. Test Infrastructure Resource Leak

**Location**: `SharpProof.Testing\AnalyzerTestHost.cs` (Lines 112-129)

**Description**: Test host instances fail to properly dispose of Roslyn workspaces when tests are terminated prematurely by timeout mechanisms, causing resource accumulation during extended test runs.

**Reproduction Steps**:
1. Run tests with aggressive timeout settings
2. Induce test hangs that trigger timeout termination
3. Observe that associated Roslyn workspaces remain allocated
4. Resource usage grows linearly with the number of timed-out tests

**Confidence**: High

### 120. Worker Launch Failure Race Condition

**Location**: `SharpProof.Worker\SharpProofWorker.cs` (Lines 98-115)

**Description**: Worker launch contains a race condition between process creation and readiness signaling where the worker may signal readiness before fully initializing, causing the launcher to proceed while the worker is still in an inconsistent state.

**Reproduction Steps**:
1. Launch workers under CPU or memory pressure
2. Observe that readiness signals occasionally occur before worker initialization completes
3. This leads to intermittent failures when the launcher sends initial work requests
4. The issue manifests as sporadic "worker not ready" errors

**Confidence**: Medium

### 121. Process Management Issue in Worker Cleanup

**Location**: `SharpProof.Worker\VerificationCache.cs` (Lines 143-159)

**Description**: The verification cache does not properly handle zombie worker processes, allowing references to terminated workers to remain in cache lookup structures, causing memory leaks and potential stale cache hits.

**Reproduction Steps**:
1. Run verification workloads that cause worker crashes
2. Observe that crashed workers are not removed from verification cache
3. Subsequent cache lookups may return stale results or cause exceptions
4. Cache effectiveness degrades over time as zombie entries accumulate

**Confidence**: Medium

### 122. Snapshot Handle Leak in Worker Launcher

**Location**: `SharpProof.Worker.Launcher\Program.cs` (Lines 167-184)

**Description**: The worker launcher fails to properly close memory-mapped file handles used for worker input snapshots when launcher encounters exceptions during worker initialization, causing handle leaks that accumulate over time.

**Reproduction Steps**:
1. Run worker launcher with conditions that cause initialization failures (invalid snapshots, missing dependencies)
2. Observe that memory-mapped file handles are not released in failure paths
3. Handle count increases with each failed launch attempt
4. Eventually leads to "too many open files" errors in long-running scenarios

**Confidence**: High

### 123. Process Management Race in Launcher Cleanup

**Location**: `SharpProof.Worker.LauncherLauncherMarker.cs` (Lines 78-95)

**Description**: Launcher cleanup contains a race condition where process termination checks and handle cleanup are not atomic, allowing for the possibility of attempting to close handles on processes that have already been reclaimed by the OS.

**Reproduction Steps**:
1. Launch many workers in quick succession with varying lifetimes
2. Observe occasional exceptions during launcher shutdown related to invalid handle operations
3. The issue occurs when a process exits between the alive check and handle cleanup
4. While infrequent, this can cause launcher crashes during shutdown

**Confidence**: Low







### 143. Interpreter Equality Compares Sequences by Reference Identity While All Other Kinds Compare by Value
**Location**: `SharpProof.Ir\IrInterpreter.cs` (Line 354)
**Description**: `EvaluateEquality` maps `(Sequence, Sequence)` to `ReferenceEquals(left, right)` - equality of `IrValue` wrapper instances, not elements. Structurally identical sequences yield `Equal == false`, while the identical term compared to itself folds `true` solely due to per-term-ID result memoization. Sequence comparisons become branch-dependent and memoization-sensitive, making concrete counterexample replay unstable.
**Reproduction Steps**:
1. Create two distinct sequence-valued terms with identical element content.
2. Evaluate `Binary(Equal, seqA, seqB)`: result is `false` although values are indistinguishable.
3. Evaluate `Binary(Equal, seqA, seqA)` twice in one session: memoization yields `true`.
**Confidence**: Medium

### 146. Aggregate `all` Gate Skips the Source-Binding Envelope Enforced by Every Other Gate Command
**Location**: `SharpProof.Gates\Program.cs` (Lines 31-46 vs 61-96, 110-166); caller `scripts\Invoke-SharpProofContainer.ps1` (Lines 216-224)
**Description**: `corpus`, `performance`, and `performance-smoke` wrap their JSON in `CreateStandaloneEnvelope`, which hard-fails unless the executable carries valid 40-hex lowercase `SharpProofSourceCommit` metadata and matching exe/pdb files exist. The `all` command serializes `{ corpus, performance }` raw with no envelope and none of those identity validations, so a gate binary built without `-p:SharpProofSourceCommit` silently produces unwrapped output under `all` while refusing under other commands. Exit-code-only consumers get no binding signal.
**Reproduction Steps**:
1. Build SharpProof.Gates without `-p:SharpProofSourceCommit=<sha>`.
2. Run `SharpProof.Gates corpus` versus `SharpProof.Gates all` and observe the envelope checks never execute in the latter.
**Confidence**: Low

### 148. Model-Value Reconstruction Parses Z3 Numerals Through the AST Pretty-Printer; Negative Counterexample Values Hit MalformedResult
**Location**: `SharpProof.Smt\IrSmtBackend.cs` (Lines 276-282, used from 249-260)
**Description**: `TryCreateValue` converts integer model values with `long.TryParse(integer.ToString(), ...)`. `IntNum.ToString()` is inherited from `Microsoft.Z3.AST.ToString()` (Z3_ast_to_string), whose default smt2 printer renders negative numerals as `(- N)` rather than `-N`. If the pinned binding does that, TryParse fails for every negative value and `CreateSatisfiable` returns `Unknown(MalformedResult)` whenever any integer model variable is negative - essentially every refutation requiring a negative counterexample silently degrades. Fail-closed but invisible to gates: unit tests only assert model values satisfied by 0, and fuzzers count abstentions separately from mismatches. Numeric accessors (`IntNum.Int64`) should be used instead.
**Reproduction Steps**:
1. Build a query with goal `¬(v = 0)` over integer variable `v`; run through `CheckAsync`.
2. Solver returns SAT with `v = -k`; inspect the model value formatting: if it prints `(- k)`, TryParse fails and the result is `MalformedResult` instead of a usable negative model.
**Confidence**: Medium

### 149. Unhandled Exception Types Escape CheckAsync's Closed Failure Mapping as Faulted Tasks
**Location**: `SharpProof.Smt\IrSmtBackend.cs` (Catch filter Lines 67-75; `(ArithExpr)` casts at 600-615; `GetVariable` indexer at 413-416)
**Description**: CheckAsync maps failures to typed `Unknown` only for Z3Exception, InvalidOperationException, ArgumentException, and ArithmeticException. A term referencing an unknown `IrVarId` makes the dictionary indexer throw `KeyNotFoundException`; raw `(ArithExpr)` downcasts throw `InvalidCastException`. Neither derives from the caught types, so the exception faults the task instead of returning a typed result. Per SEMANTICS.md, infrastructure failure is fatal under every build policy, escalating what should be a clean abstention into a fatal run condition.
**Reproduction Steps**:
1. Construct a query whose predicate tree contains a variable excluded from variable collection (e.g., a future term kind missed by CollectVariables).
2. Call `CheckAsync` and observe `KeyNotFoundException` escaping rather than `BackendCheckResult.Unknown`.
**Confidence**: Low

### 150. Resource Accounting Hardcodes a 32-Bit Wrap Modulus for Z3 Rlimit Statistics
**Location**: `SharpProof.Smt\IrSmtBackend.cs` (Lines 193-197); consumer `SharpProof.Worker\SharpProofWorker.cs` (Line 560)
**Description**: `AccountResources` compensates decreases with `(1L << 32) - last + observed`, hardcoding a 32-bit counter modulus. If the statistic surfaces as a full 64-bit integral value (or the binding changes how IsUInt/UIntValue project it), observed values above 2^32 make this branch add garbage to ConsumedResourceCount, prematurely converting live queries into ResourceLimit-Unknown outcomes (or under-charging, defeating the budget). The wrap branch is effectively dead code today; its correctness depends on unstated width guarantees.
**Reproduction Steps**:
1. Configure QueryRlimit near uint.MaxValue and exhaust it.
2. Any decrease below a previously observed large count triggers the 2^32-compensation branch yielding a wildly wrong delta.
**Confidence**: Low

**Confidence**: Medium

### 166. Single-Entry Seeding Leaves Blocks Not Reachable From Entry Permanently Bottom With No Diagnostic
**Location**: `SharpProof.Dataflow\ForwardDataflowAnalysis.cs` (Lines 115-120, 167-173)
**Description**: Only EntryBlockId is seeded; non-entry inputs start at domain.Bottom. A block whose predecessors are themselves unreachable from entry is never enqueued, so GetInputState/GetOutputState return Bottom ("provably unreachable") forever, and Analyze completes successfully instead of flagging the dead region. A consumer wiring exception edges incorrectly gets silent all-Bottom catch states rather than an error.
**Reproduction Steps**:
1. Graph with entry 0→1 and an isolated cycle 2↔3; Analyze returns normally with InputStates[2]/[3] == Bottom and no signal two blocks were never analyzed.
**Confidence**: High (behavior), Medium (design-gap classification)

### 168. Valid Parenthesized Prologue Clause Rejected as Misplaced (Hard Binding Failure)
**Location**: `SharpProof.Contracts\ContractClauseInventoryBuilder.cs` (Lines 160-165 TryGetDirectPlacement, 142-146 Classify)
**Description**: Roslyn operations unwrap parentheses, so for `(((Contract.Requires(x > 0))));` the invocation's syntax parent is ParenthesizedExpressionSyntax, not ExpressionStatementSyntax. TryGetDirectPlacement returns false and the ancestor scan finds no conditional node, yielding Misplaced → InvalidClausePlacement for legal C# input; removing one pair of parentheses fixes it.
**Reproduction Steps**:
1. Write `public void M(int x) { (((Contract.Requires(x > 0)))); }` (legal C#).
2. Inventory/bind the method: placement Misplaced with ContractBindingFailure.InvalidClausePlacement.
**Confidence**: Medium

### 169. Duplicate Intrinsic Declarations Crash ContractApiSymbols.TryCreate Instead of Degrading to ContractApiUnavailable
**Location**: `SharpProof.Contracts\ContractApiSymbols.cs` (Lines 59-70 FindGenericIntrinsic using SingleOrDefault)
**Description**: `SingleOrDefault(method => ...)` throws InvalidOperationException when two members satisfy the shape filter (e.g., duplicate declarations via partials/metadata). TryCreate is invoked eagerly in ContractBinder and ContractIntrinsicValidator constructors, none of which catch it - the documented graceful degradation ContractBindingFailure.ContractApiUnavailable (used when members are merely missing) is bypassed in favor of an unhandled crash of the binding pipeline.
**Reproduction Steps**:
1. Reference a Contract type declaring two applicable static generic zero-arg Result methods.
2. Construct ContractBinder or call ContractApiSymbols.TryCreate: InvalidOperationException escapes instead of null/ContractApiUnavailable.
**Confidence**: Low

### 170. Unanalyzable Statements Default to Reachable, Admitting Unreachable Contracts as Valid Prologue
**Location**: `SharpProof.Contracts\ContractClauseInventoryBuilder.cs` (Lines 214-233 IsReachable)
**Description**: IsReachable catches ArgumentException from SemanticModel.AnalyzeControlFlow and defaults to StartPointIsReachable == true. For statements Roslyn cannot analyze (error-code constructs, malformed trees), a Contract.Requires that is actually unreachable is classified ValidPrologue, bound into BoundMethodContracts, and verified as a live obligation - malformed input accepted at the boundary instead of abstaining.
**Reproduction Steps**:
1. Place a Requires clause in a construct where AnalyzeControlFlow throws (error-recovery/speculative positions).
2. The clause comes back IsValid == true and is included in bindings instead of Unreachable.
**Confidence**: Low

### 171. Protocol Identity Hashing Uses Lossy UTF-8 Replacement Fallback: Distinct Callable/Claim IDs Seal to Identical Manifest Hashes
**Location**: `SharpProof.Worker.Protocol\ProtocolJson.cs` (Line 75 ComputeRequestHash; strict encoder s_strictUtf8 defined Line 14 but unused for hashing); `ProtocolManifest.cs` (Lines 44-51, 67-76)
**Description**: Identity strings are hashed via Encoding.UTF8, whose encoder replaces unpaired surrogates with U+FFFD, while uniqueness validation operates on decoded UTF-16 strings accepting `"id\uD800"` and `"id\uFFFD"` as distinct. SealManifest/ComputeManifestHash therefore produce identical hashes and ManifestsEqual returns true, so a response verified against one manifest passes manifest-binding validation for a different manifest and cache entries alias across distinct compilations. Same collision class as accepted issue #133 but a distinct site.
**Reproduction Steps**:
1. Build two manifests differing only in CallableId `"X\uD800Y"` vs `"X\uFFFDY"`.
2. SealManifest both: identical Hash; ManifestsEqual(m1,m2): true; ValidateForRequest accepts the wrong manifest.
**Confidence**: High

### 172. "Bounded" JSON Reader Enforces Its Size Limit Only Once (TOCTOU) and Cancellation Cannot Abort Pending Reads
**Location**: `SharpProof.Worker.Protocol\ProtocolJson.cs` (Lines 78-95 OpenJsonReader; 35-39; 41-56 ReadUtf8FileAsync)
**Description**: The 16 MiB cap is checked against stream.Length once at open; the file is opened FileShare.Read and consumed with unbounded ReadToEnd/chunk-append loops, so content appended after the check is read without limit (memory exhaustion). Additionally ReadUtf8FileAsync polls its CancellationToken only between chunk reads; the underlying ReadAsync gets no token, so a stalled/growing source cannot be cancelled mid-read.
**Reproduction Steps**:
1. Start ReadUtf8FileAsync on a small file; concurrently append tens of MiB (permitted by FileShare.Read); observe consumption far beyond MaximumJsonBytes.
2. Signal ct while blocked in a read: no OperationCanceledException until a chunk boundary (potentially never).
**Confidence**: Medium

### 173. LinuxWorkerProcess: Unsynchronized _process Access Races Dispose, and Dispose Leaks the Handle When Terminate Throws
**Location**: `SharpProof.Host\LinuxWorkerProcess.cs` (Lock-free read Lines 106-107 in WaitForExit; Dispose 166-185; Terminate guards 227-241)
**Description**: WaitForExit snapshots _process without taking _synchronization while Dispose locks, terminates, disposes, and nulls the field; concurrent disposal between the null check and WaitForExit(0)/ExitCode throws ObjectDisposedException. Worse, inside Dispose the Terminate/process.Dispose/_process=null sequence is not try/finally-wrapped: if Terminate throws (grace-period expiry InvalidOperationException or KillProcessGroup NativeFailure), the Process handle is never disposed, _process stays non-null, and every subsequent Dispose retries termination on a stale handle; the exception also escapes using var process in the launcher, replacing real exit status.
**Reproduction Steps**:
1. Start a worker ignoring SIGTERM so Terminate exceeds its grace period; call Dispose(): InvalidOperationException, and a second Dispose reruns Kill/WaitForExit on the undisposed handle.
2. Race Dispose from thread B while thread A polls WaitForExit: intermittent ObjectDisposedException.
**Confidence**: Medium

### 174. Manifest-Writer Frame Violates Its Own Length-Prefix Convention: -1 ("Absent") Count Followed by Five Present Fields
**Location**: `SharpProof.Worker.Protocol\ProtocolJsonSupport.cs` (Lines 217-230 Add/AddItems: -1 strictly means no fields follow; 247-257 AddLocation)
**Description**: The ManifestWriter framing establishes negative length = absence: Add(null) emits exactly `-1:;` and AddItems emits count -1 followed by zero records. AddLocation breaks the invariant: for a null location it emits count -1 yet appends all five length-prefixed fields with -1 sentinels. Today nothing parses these payloads (only SHA-256'd/compared) so producer and consumer stay accidentally consistent, but any count-driven decoder misparses every null-location frame, and the sentinel is ambiguous between "null location" and "location with all fields -1".
**Reproduction Steps**:
1. Call `AddLocation("t", null).ToString()`: output contains five records after a -1 (absent) count.
2. Compare AddItems(domain, null, ...) emitting zero records after -1; implement the obvious count-driven decoder and observe desynchronization.
**Confidence**: Medium

### 175. Launcher Launch-Wait Polls CancellationToken.WaitHandle on a Sourceless Token, Mapping Every Real Verification to Bogus containment.unavailable
**Location**: `SharpProof.Worker.Launcher\Program.cs` (Lines 276-278 calling WaitForExit(terminationStart, finalLimit)); root cause `SharpProof.Host\LinuxWorkerProcess.cs` (Line 133); second instance `LinuxPathIdentity.cs` (Line 1285)
**Description**: RunWorker calls WaitForExit without a CancellationToken, so the poll loop accesses CancellationToken.None.WaitHandle, which throws InvalidOperationException on the first iteration whenever the worker survives one poll tick (~25 ms - i.e., every real cold start). RunMain's general handler maps InvalidOperationException to exit 125 containment.unavailable, writing a fail-closed result asserting containment failed - masking the actual verification entirely. The identical idiom in publication-lock acquire throws under lock contention.
**Reproduction Steps**:
1. Invoke the launcher with any worker startup exceeding ~25 ms (all of them).
2. Observe stderr "worker containment could not be established", exit 125, synthesized containment.unavailable result even though the worker started fine; contrast the passing test which supplies a real CTS token.
**Confidence**: Medium

### 176. SARIF PROJECTROOT Base URI Is the Launcher's CWD, Not the Verified Project Directory
**Location**: `SharpProof.Worker.Launcher\SarifProjection.cs` (Lines 69-77; project directory available at Program.cs Lines 205-206 but not passed)
**Description**: originalUriBaseIds["PROJECTROOT"] is built from Environment.CurrentDirectory of the launcher process, while compiler artifactLocations are intentionally project-relative (per the file's own comment). Every relative location resolves against whatever directory the launcher started in, not the actual project directory.
**Reproduction Steps**:
1. From /repo run the launcher for a project at /repo/sub/proj with --publish-sarif.
2. SARIF viewers resolve relative locations to /repo/src/... instead of /repo/sub/proj/src/....
**Confidence**: High

### 177. Benign Cache-Lock Contention Reported as CacheStatus.Unavailable, Silently Disabling Cache and Misreporting Health
**Location**: `SharpProof.Worker\VerificationCache.cs` (AcquireLock Lines 236-261 FileShare.None with no retry/wait; swallow at 108-112, 193-198; consumed SharpProofWorker.cs 224-227, 395-397)
**Description**: Concurrent verifications sharing one cache directory serialize on a single non-blocking lock file. The loser's open throws IOException, which is swallowed and converted into LastReadUnavailable/TryWriteAsync==false, so SharpProofWorker rebuilds the response with WorkerCacheStatus.Unavailable. The cache was healthy, merely busy; monitoring sees "Unavailable" (disk trouble) and zero caching benefit whenever builds overlap. The same swallow converts transient errors during debris recovery into full run-level cache bypass.
**Reproduction Steps**:
1. Start two verify launches for the same project concurrently.
2. One result reports Miss/Written, the other Unavailable; serial rerun shows normal behavior.
**Confidence**: High (behavior unambiguous; severity is status integrity/availability, not wrong results)

### 178. Method-Timeout Timer Stays Armed Through Result Assembly, Converting Completed Proofs Into Spurious Timeout/Incomplete
**Location**: `SharpProof.Worker\CallableVerificationPolicy.cs` (Lines 23-59)
**Description**: CancelAfter(methodWallTimeMilliseconds) remains live while the successful proof is post-processed (effect record assembly and ordering inside the same try). If the timer fires in that window (routinely fires up to a tick late), any OperationCanceledException jumps to catch, discarding the completed proof and labeling the callable Incomplete/MethodTimeout. Coverage flips Complete→Incomplete, VerificationCache.IsCacheable rejects the run, and RequireProven policies fail purely due to assembly-time jitter.
**Reproduction Steps**:
1. Set --method-wall-ms near a callable's real solve time so the deadline lands just after proof completion.
2. Intermittently observe claims computed but callable reported MethodTimeout/Incomplete and no cache write; slightly larger budget completes and caches identical work.
**Confidence**: Medium

### 179. Abnormal Worker Exit Codes Silently Re-Mapped to Exit 3 (Failed/MalformedResult), Losing the True Termination Cause
**Location**: `SharpProof.Worker.Launcher\Program.cs` (Lines 151-179 NoResultFailure special-cases only 124/125; 224-227 exit mapping); `LauncherProjections.generated.cs` (Lines 24-34)
**Description**: When the worker dies without writing result.json (SIGKILL/OOM exit 137, segfault 139, canceled exit 4), the default arm fabricates Failed/MalformedResult even though nothing was parsed, and ValidateAndReport maps that to exit 3, discarding the original code. Asymmetry: timeouts preserve 124 via a dedicated branch, cancellations do not preserve 4. Root cause (OOM/crash) appears nowhere in output.
**Reproduction Steps**:
1. Force the worker to die before publishing a result (cgroup OOM kill → exit 137).
2. Launcher exits 3; stderr says worker.no_result then "worker run Failed (MalformedResult)".
**Confidence**: Medium

### 180. Aggregate SP0048/SP0047 Diagnostics Anchored at the Wrong Callable; SARIF and Console Reports Disagree on Location Fields
**Location**: `SharpProof.Worker.Launcher\Program.cs` (SP0048 anchored at Manifest.Callables[0].Location Lines 525-528; SP0047 anchored at incomplete[0] Lines 479-483); `SarifProjection.cs` (Notifications carry no location, Lines 36-44, 177-192)
**Description**: Assumption totals are computed across ALL callables but the console diagnostic anchors at the first manifest callable's location, pointing at unrelated code; the SARIF notification carries no region/artifactLocation at all. Similarly aggregate-counted SP0047 anchors only at the first incomplete callable.
**Reproduction Steps**:
1. Verify a project where only a late-declared callable declares assumptions.
2. Console SP0048 prefixed with callable #1's file/line; the SARIF toolExecutionNotification for SP0048 has no location.
**Confidence**: Medium

### 181. SARIF invocations[0].executionSuccessful Is true for Runs Containing Refuted (Failing) Postconditions
**Location**: `SharpProof.Worker.Launcher\SarifProjection.cs` (Lines 78-82)
**Description**: executionSuccessful = runStatus == Complete && errors.Length == 0. A verification completing with a definite contract violation produces results with kind:"fail"/level:"error" and exit code 5, yet the invocation is marked executionSuccessful:true. CI consumers gating on this field treat violated postconditions as successful verification because the failure signal exists only in results[] and the process exit code.
**Reproduction Steps**:
1. Verify a project with one provably false [Ensures] clause.
2. Published SARIF contains a fail result yet invocations[0].executionSuccessful == true.
**Confidence**: Low

### 182. Raw SyntaxTree.FilePath Stored in Source-Summary Authority Never Matches Path.GetFullPath-Normalized Snapshot Trees, Aborting the Entire Manifest
**Location**: `SharpProof.CompilerCollector\CompilerArtifact\CompilerRelationalSummaryProvider.cs` (Line 358) vs `CompilerCompilationCapture.cs` (Line 141) and `CompilerManifestArtifactProducer.cs` (Lines 118-127); worker-side mirror `SharpProof.CompilerArtifact\CompilationFingerprint.cs` (Lines 167-171)
**Description**: Captured snapshot trees store normalized full paths, but summary evidence rows store the raw declaration.SyntaxTree.FilePath, and BuildSummaryEvidence demands ordinal string equality, throwing "A source summary authority is not bound to the captured source tree" - converted into a manifest-failure diagnostic dropping the whole manifest. Divergence occurs for generator-added trees with relative hint names (e.g., "Gen.g.cs") or forward-slash paths on Windows. The worker-side validator repeats the comparison, rejecting hand-repaired manifests too.
**Reproduction Steps**:
1. Enable the collector on a project with a source generator emitting a helper into a relative-hint tree; call it from a contract-annotated method so a relational summary is inferred.
2. At emission SourcePath="Gen.g.cs" while the snapshot holds the absolute path; BuildSummaryEvidence throws and the build gets only the manifest-failure diagnostic.
**Confidence**: High

### 183. Artifact Identity Embeds Host-Specific Absolute Paths, Making CompilationSha256 Irreproducible Across Machines/Platforms/Directories
**Location**: `SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs` (Lines 51-52, 141, 232-273, 326); `SharpProof.CompilerArtifact\CompilerCaptureAuthority.cs` (Lines 11-21)
**Description**: Every hashed key (ProjectDirectory, SyntaxTrees[].Path, reference module paths, additional-file paths) is Path.GetFullPath output: rooted absolute paths with OS-specific separators. For generator-added trees with relative hint names, GetFullPath resolves against the analyzer host's CWD, not the project directory. The same source produces different CompilationSha256 across launch directories, drives, or OSes, silently defeating cross-machine/cross-platform cache reuse. Location payloads intentionally keep raw spelling, so one document mixes forms.
**Reproduction Steps**:
1. Build in checkout C:\a\proj, record compilationSha256.
2. Copy identical sources to D:\b\proj (or build on Linux, or from another CWD with a relative-hint generator file): hash differs with zero semantic changes.
**Confidence**: High

### 184. CompilerProbeGenerator Silently Verifies Only the Lexicographically-First Matching Additional File, and InputFingerprint Omits the File Path
**Location**: `SharpProof.CompilerProbe.TestAsset\CompilerProbeGenerator.cs` (Lines 12-31, 48-52, 121-124); evidence disagrees at `CompilerProbeSnapshot.cs` (Lines 404-438)
**Description**: The pipeline collects all additional files named SharpProofProbeInput.txt but Generate reduces them with OrderBy(Path, Ordinal).First(): others dropped without diagnostics. The sort key is the full normalized absolute path, so which file wins depends on checkout layout; moving a folder flips the generated contract silently. The fingerprint hashes globalValue/metadata/text excluding the winning Path, so consumers cannot detect the switch, while snapshot evidence hashes all files.
**Reproduction Steps**:
1. Fixture with two SharpProofProbeInput.txt files in different folders with different contents.
2. Generated InputText reflects only the ordinal-first full path, no warning; rename folders so the winner swaps and the contract changes while InputFingerprint stays unchanged.
**Confidence**: Medium

### 185. Response Evidence Authority Crashes With Unhandled ArgumentException on Duplicate IDs Instead of Returning a Validation Error
**Location**: `SharpProof.CompilerArtifact\CompilerResponseEvidenceAuthority.cs` (Lines 35-40)
**Description**: Validate builds dictionaries with .ToDictionary(ClaimId/CallableId). Unlike every other check funneling problems into the errors set, duplicate ClaimId/CallableId rows throw ArgumentException("An item with the same key has already been added"), turning a malformed/hostile response into an unstructured crash instead of a rejected-response verdict.
**Reproduction Steps**:
1. Construct a WorkerVerifyResponse with two ClaimResults sharing a ClaimId.
2. Call Validate(response): unhandled ArgumentException rather than returned error codes.
**Confidence**: Medium

### 186. Cache-Rule Local Tracking Picks the Lexically-Last Prior Write, Missing Loop-Carried Unknown Answers (Incorrect Code Passes Unchecked)
**Location**: `SharpProof.Meta.Analyzers\CacheSoundnessRules.cs` (Lines 107-134 ResolveLocal; related guard Line 129; cycle short-circuit Line 101)
**Description**: ResolveLocal computes the reaching definition as the last write whose span starts before the read, ignoring control-flow dynamics. A write later in the same loop body than the cache write (the value flowing into iterations >= 2) is invisible, so caching Unknown/timeout/error answers goes unreported despite SPMETA010 forbidding exactly that. Additionally the conditional-write fallback evaluates only the single last write, and cyclic self-reference short-circuits to true flagging harmless self-assignments.
**Reproduction Steps**:
1. Analyze: `var answer = Answer.Proven; while (HasWork()) { cache.Set("k", answer); answer = Answer.Unknown; }`.
2. Zero SPMETA010 diagnostics although a non-cacheable Unknown answer is cached from iteration 2 onward.
**Confidence**: High

### 187. SPMETA001 Display-Text Ban Bypassed Via Static SymbolDisplay.ToDisplayString (Incorrect Code Passes)
**Location**: `SharpProof.Meta.Analyzers\SharpProofSoundnessAnalyzer.cs` (IsForbidden Lines 122-129; ForbiddenMethods 35-46; sibling hole in AnalyzeInterpolatedString 271-299)
**Description**: The rule bans using display text as identity but only detects instance symbol.ToDisplayString() via the receiver-type heuristic plus a hardcoded table. Roslyn's equally abusable static SymbolDisplay.ToDisplayString(ISymbol, ...) has no instance receiver and is absent from the table, so it produces no diagnostic. Companion rule SPMETA009 has the same-shaped hole for constant-only interpolated strings versus equivalent + concatenation.
**Reproduction Steps**:
1. Analyze `static string Key(INamedTypeSymbol t) => SymbolDisplay.ToDisplayString(t);` - no SPMETA001.
2. `t.ToDisplayString()` on the next line is flagged, proving asymmetric coverage versus the descriptor text.
**Confidence**: High

### 188. Embedded Spec-Pack Loader Rejects Uppercase Hex Public-Key Tokens Its Own Matcher Accepts (Case-Sensitivity Mismatch Between Spec Authorities)
**Location**: `SharpProof.CompilerCollector\CompilerArtifact\CompilerSpecificationPackProvider.cs` (Parse Lines 514-517 lowercase-hex-only vs MatchesAssembly 332-337 OrdinalIgnoreCase); contrast `SharpProof.Specs\ApiSpecTable.cs` (Lines 203-204) and `ApiSpecContentDigest.cs` (Line 26)
**Description**: The embedded-catalog parser validates publicKeyToken characters against lowercase hex only, throwing InvalidDataException at load for uppercase input, while the loader's own assembly matching compares tokens case-insensitively and the sibling spec authority accepts any case and canonically digests via ToUpperInvariant(). A semantically valid canonically formatted catalog fails to load purely on letter case.
**Reproduction Steps**:
1. Change a catalog token to uppercase (semantically identical), repin the SHA-256.
2. LoadCatalog throws "public-key token is invalid" although MatchesAssembly would match such tokens.
**Confidence**: Medium

### 189. Catalog Generator Orders/Dedups Witness IDs With Culture-Sensitive Sort-Object, Breaking Byte-Determinism and Falsely Rejecting Distinct IDs
**Location**: `scripts\Generate-ApiSpecCatalog.ps1` (Lines 668-686; byte-exact gate Assert-ExactGeneratedFile 1239-1246) feeding `SharpProof.Specs\DefaultApiSpecCatalog.generated.cs`
**Description**: Windows PowerShell 5.1 Sort-Object uses culture-aware, case-insensitive comparison unless overridden: (a) generated declaration order emitted before runtime re-sort varies with OS locale, so the byte-exact gate can fail on unchanged checked-in files; (b) -Unique collapses ordinally-distinct witnesses differing only in case (contract.old vs Contract.Old), aborting generation although ApiSpecTable.Create (Ordinal grouping) would accept them. Runtime is ordinal-safe; the front end is not.
**Reproduction Steps**:
1. Add a declaration with witness Contract.Old alongside contract.old; run the script: aborts claiming identifiers must be unique.
2. Run under a locale collating differently: Assert-ExactGeneratedFile fails on unchanged content.
**Confidence**: Medium

### 190. Windows-Style Backslash Hardcoded in Test-Project Path of a Now Linux-Only Script
**Location**: `scripts\Test-SharpProofPackageConsumers.ps1` (Line 531, consumed at 609); correct sibling `scripts\Invoke-SharpProofPackageTests.ps1` (Lines 33-34)
**Description**: `$testProject = Join-Path $repositoryRoot 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'` keeps a literal backslash although the script's execution path now requires Linux container mode (Windows branch deleted in the port commit). On Linux Join-Path preserves `\` as a filename character; the argument reaches dotnet test as `/repo/SharpProof.Package.Test\SharpProof.Package.Test.csproj`, working only because MSBuild switch parsing applies FixFilePath - any consumer bypassing MSBuild arg parsing breaks.
**Reproduction Steps**:
1. In the canonical Linux container run the script and inspect the path handed to native dotnet test.
2. Invoke dotnet with that path outside MSBuild switch parsing: File.Exists false.
**Confidence**: Medium

### 191. Backslash Project Paths Passed to `dotnet run --project` on Linux in Acceptance Harness
**Location**: `eng\acceptance\Verify.ps1` (Lines 741, 758-759; PowerShell-consumed paths at 710, 724-725 deliberately use '/')
**Description**: The fuzz/gates phases invoke dotnet run --project with backslash-separated csproj strings reaching the native CLI unmodified via ArgumentList. Unlike msbuild project switches, dotnet resolves --project before MSBuild sees it, so correctness depends entirely on CLI-side separator normalization; where it doesn't translate backslashes the phase fails "project file does not exist".
**Reproduction Steps**:
1. Run eng/acceptance/Verify.ps1 in the container on a host whose CLI does not normalize backslashes.
2. Fuzz phase fails to locate Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj despite presence.
**Confidence**: Low

### 192. Verifier Nuspec $nativeroot$ Input Not Validated Before Packing
**Location**: `SharpProof.Verifier\SharpProof.Verifier.csproj` (Lines 51-53)
**Description**: _SharpProofPrepareNuspecProperties errors when SHARPPROOF_CONTAINER != '1' but never validates SHARPPROOF_NATIVE_ROOT is non-empty. Unset, NuspecProperties gets nativeroot= and nuspec src entries resolve to filesystem-root-absolute `/z3/...`, producing an opaque NuGet file-not-found (or silently packing a wrong payload where such root exists) instead of a fail-fast guard naming the required environment variable.
**Reproduction Steps**:
1. In the container run dotnet pack SharpProof.Verifier with SHARPPROOF_NATIVE_ROOT cleared.
2. Packing fails confusingly at /z3/4.12.2/... rather than a targeted error.
**Confidence**: Low

### 193. .Trim() Applied to Possibly-Array Merged stdout+stderr When Validating the Release dotnet Host
**Location**: `scripts\Publish-SharpProofRelease.ps1` (Lines 140-145; sibling avoiding the pattern at Line 80)
**Description**: `$actualVersion = (& $path --version 2>&1).Trim()`. With stderr merged, any dotnet stderr line (first-run notice, warnings) turns the expression into object[], on which .Trim() throws MethodException under StrictMode/ErrorActionPreference=Stop - aborting a valid publication before the SDK-version comparison runs; $LASTEXITCODE is checked only afterwards using the same polluted value.
**Reproduction Steps**:
1. Run Publish-SharpProofRelease.ps1 with DotNetPath resolving to a dotnet emitting any stderr line for --version.
2. Script throws "Trim : method invocation failed" instead of comparing versions.
**Confidence**: Low

### 194. Differential Harness Helper Turns Into a Tautology for Reference/Array Results
**Location**: `SharpProof.Frontend.Test\FrontendLoweringTests.cs` (CompiledMethod.CompareWithInterpreter, Lines 1119-1128)
**Description**: After asserting the interpreter produced a value, the helper maps the result via Kind switch where the default arm substitutes `actual` itself (the compiled return value), then asserts interpreted Is.EqualTo(actual). For Reference/Sequence kinds (lowerer supports exact array/receiver lowerings) any differential test whose Target returns an array or reference type can never fail on interpreter mismatch - the oracle degrades to DoesNotThrow.
**Reproduction Steps**:
1. Add `public static long[] Target(long[] v) => v;` and call CompareWithInterpreter.
2. Corrupt the interpreter result (Sequence/Reference kind): the assert still passes because interpreted is literally actual.
**Confidence**: High

### 195. Shared Analyzer-Test Harness in SharpProof.Testing Is Dead Code With Self-Conflicting Severity Wiring
**Location**: `SharpProof.Testing\AnalyzerTestHost.cs` (Lines 21-55; severity conflict 38 vs 147-152; silent fallback 70-77)
**Description**: Neither GetDiagnosticsAsync overload has any caller repo-wide, shielding contained bugs forever. Its wiring escalates enabled diagnostic IDs twice with disagreeing severities - specificDiagnosticOptions maps each ID to Info while TestSyntaxTreeOptionsProvider reports Warn; compilation options win, so any eventual consumer asserting warning-severity diagnostics observes Info. Its reference fallback also silently proceeds with a 2-reference set instead of failing fast.
**Reproduction Steps**:
1. Grep GetDiagnosticsAsync across the solution: only definitions match.
2. Wire any test and inspect Severity for an enabled ID: Info despite tree provider configured Warn.
**Confidence**: Medium

### 196. SMT Cancellation Test Depends on Wall-Clock Races (Monitor-Hold Window Plus Fixed Sleep)
**Location**: `SharpProof.Smt.Test\IrSmtBackendTests.cs` (ActiveCancellationInterruptsTheNativeContext, Lines 541-580)
**Description**: The test waits up to 5 s for the backend gate to appear held, sleeps a hardcoded Thread.Sleep(10), cancels, and demands OperationCanceledException. Under loaded CI the pool thread may not acquire the gate within 5 s (first assert fails); if encoding plus the trivial SAT check complete within the 10 ms window (fast machine, warm Z3), the task finishes before Cancel lands and ThrowsAsync fails with success. Outcome varies with machine load, not just performance.
**Reproduction Steps**:
1. Temporarily lower the assumption count (e.g., 200) or run under CPU contention repeatedly.
2. Observe "Expected: OperationCanceledException... But was: success" intermittently.
**Confidence**: Medium

### 197. CHANGELOG Describes a "Windows x64 Verifier" and "Windows x64 Worker Containment" but the Shipped Verifier Is Linux amd64-Only
**Location**: `CHANGELOG.md` (Unreleased Added, Lines 15-17) vs `SharpProof.Verifier\SharpProof.Verifier.csproj` (Line 16), `SharpProof.Verifier.nuspec` (Lines 28, 57), `README.md` (Lines 658-664), `docs\preview-support.md` (Line 50)
**Description**: Changelog Added bullets claim Windows x64 worker containment/cache/resource budgets and a Windows x64 verifier. Every implementation surface says otherwise: the package describes itself as Container-only Linux amd64, ships only linux-x64 Z3 payloads, and buildTransitive props require Core MSBuild + X64 + SHARPPROOF_CONTAINER=1; README/docs declare Windows/macOS/native unsupported. No Windows payload exists anywhere in the package graph.
**Reproduction Steps**:
1. Read CHANGELOG Unreleased Added bullets; read the nuspec/csproj metadata and props host gate.
2. Direct contradiction: documented platform absent from the package.
**Confidence**: High

### 198. Native Z3 Packaging Doc Cites a Payload Path That Does Not Exist in the Package
**Location**: `docs\native-smt-packaging.md` (Lines 32-34) vs `SharpProof.Verifier.nuspec` (Line 57) and `buildTransitive\SharpProof.Verifier.props` (Line 8)
**Description**: Doc says the verifier places libz3.so at runtimes/linux-x64/native/. The nuspec actually packs tools/native/linux-x64/libz3.so and the resolver loads ../tools/native/linux-x64/libz3.so. No runtimes/ directory exists in the verifier package; the repo's own mutation test treats target="runtimes/linux-x64/native" as tampered.
**Reproduction Steps**:
1. Open the doc's "Pinned Z3 closure"; read nuspec files section and props line 8.
2. Documented path differs from shipped/resolved path.
**Confidence**: High

### 199. Docs Claim do Loops Are in the Admitted Effect Subset; the Language Gate Rejects Them (SP0047 Instead of Analysis)
**Location**: `README.md` (Lines 276-280), `SEMANTICS.md` (Lines 350-354), `docs\coverage-and-limits.md` (Line 53) vs `SharpProof.Analyzer.Core\LanguageSubsetGate.cs` (Lines 153-154)
**Description**: All three docs list for/while/do among covered statements, but SupportsOperationShape admits only LoopKind.While or For; do…while lowers to LoopKind.Do, so ClassifyEffects abstains UnsupportedOperationShape, selected callables get SP0047, and the worker manifest builder reuses the same gate so bodies are not verified. Nothing handles LoopKind.Do.
**Reproduction Steps**:
1. Annotate a method containing `do { i++; } while (i < 3);` with [EnforcePure].
2. Docs say supported; analyzer abstains and emits SP0047 UnsupportedOperationShape.
**Confidence**: Medium

### 200. Unreleased CHANGELOG Documents Superseded Wire Versions (Protocol 9, Cache 11, Artifact Schemas 8/9) Contradicting Shipped Protocol 11 / Cache 13 / Artifact Schema 15
**Location**: `CHANGELOG.md` (Lines 41, 50, 58, 136) vs `SharpProof.Worker.Protocol\ProtocolModel.generated.cs` (Lines 13-24), `SharpProof.CompilerArtifact\CompilerArtifactModel.generated.cs` (Line 14), `README.md` (Lines 400, 723-733)
**Description**: Within the single Unreleased section, Changed entries cite protocol 9/cache schema 11 and artifact schemas 8/9, while shipped constants pin protocol 11, cache 13, manifest 4, artifact schema 15 documented as current everywhere else, with no changelog entries recording those later breaks. A changelog-only reader concludes the preview ships the older wire contract.
**Reproduction Steps**:
1. Read CHANGELOG version mentions; grep Current in ProtocolModel.generated.cs and CompilerArtifactModel.generated.cs.
2. Constants are 11/13/15 conflicting with recorded 9/11/8/9.
**Confidence**: Medium

### 201. Unbounded Static Cleanup-Anchor Retention in Long-Lived MSBuild Nodes
**Location**: `SharpProof.BuildTasks\RunVerifier.cs` (Static store Lines 38-40; lifecycle 772-832; buffers cap Line 26; count exposure 60-61)
**Description**: RetainedCleanupAnchors is a process-lifetime static ConcurrentDictionary with no count/byte cap or sweep. Entries are removed only after anchor.Process.WaitForExitAsync completes; if a supervisor/worker hangs or survives kill escalation, its anchor - including up to 1 MB captured stdout/stderr each - stays resident for the MSBuild node lifetime, and repeated failed builds accumulate anchors without bound. Unlike VerificationCache's LRU byte budget, nothing enforces a limit here.
**Reproduction Steps**:
1. Run a project whose supervisor spawns but ignores SIGTERM/SIGKILL escalation.
2. RetainedCleanupAnchorCount grows monotonically across subsequent builds in the same node.
**Confidence**: Medium

### 202. Empty Assembly.Location Collapses Runtime-Closure Entries to a Bare Directory, Off-by-One-Level Weakening Containment Validation
**Location**: `SharpProof.Worker.Launcher\LauncherArguments.generated.cs` (Lines 66-68); consumed `SharpProof.Worker.Launcher\Program.cs` (Lines 1012-1030, 1038-1041)
**Description**: Path.Combine(dir, Path.GetFileName(assembly.Location)) assumes Location is always a file path. When assemblies load single-file/in-memory, Location is "" so GetFileName("")=="" and Combine returns dir itself; Program.cs then takes GetDirectoryName(Canonicalize(dir)) yielding the parent directory, so writable-path nesting validation runs against a root one level too high and dedup against runtimeSnapshot.ComponentPaths misses the real component path - admitting writable paths inside the true runtime dir or throwing spurious distinctness failures. ChangeExtension("", ".deps.json") likewise corrupts element [0]/[1]/[2] if the launcher assembly has empty Location.
**Reproduction Steps**:
1. Deploy the launcher with System.IO.Pipelines/System.Text.Json embedded (single-file publish).
2. Set a writable path (--result) inside <runtime-dir>/../sibling and observe validation passing against the parent root.
**Confidence**: Low

### 203. Root-Catalog Generators Lack the Duplicate-Key / Unknown-Key Defenses Their Sibling Catalog Enforces - Duplicated JSON Keys Silently Override (Last-Wins)
**Location**: `scripts\Generate-DeclarativeModels.ps1` (Lines 16-24) and `scripts\Generate-ProjectionCatalog.ps1` (Lines 17-25); defended sibling `scripts\Generate-ContractApiCatalog.ps1` (Lines 37-95); parity test `SharpProof.Frontend.Test\ContractApiCatalogParityTests.cs` (Lines 128-136)
**Description**: Both root catalogs are parsed with bare ConvertFrom-Json checking only key presence. Repeated JSON keys resolve last-wins and unrecognized keys are silently ignored - e.g., a duplicated schemaVersion or outputs key quietly replaces the first declaration. The repo treats this as a real threat class for ContractApi.catalog.json (Assert-UniqueJsonProperties throws on duplicates; parity test covers the mutation) but neither defense exists in these two generators.
**Reproduction Steps**:
1. Duplicate `"schemaVersion": 1,` at the top of SharpProof.Projection.catalog.json and run the generator.
2. Exits 0 silently, whereas the equivalent ContractApi.catalog.json mutation throws "contains duplicate property".
**Confidence**: High

### 204. Effect Attributes Advertise Property Target and the Live Analyzer Honors It, but the Collector/Verifier Permanently Refuses Every Property-Accessor Effect Claim
**Location**: `SharpProof.Attributes\EnforcePureAttribute.cs` (Line 2; same usage on five sibling effect attributes) vs `SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs` (Lines 85-92) and `CompilerCallableLowerer.cs` (Line 50)
**Description**: All six effect attributes allow Method | Constructor | Property and the live analyzer analyzes accessors (LanguageSubsetGate admits PropertyGet/Set; ContractSelectionInventory picks up property-level attributes). However BuildTarget computes supported only for MethodDeclarationSyntax/ConstructorDeclarationSyntax + Ordinary/Constructor MethodKind, so for `[EnforcePure] int P { get; }` every effect claim is force-marked Unknown/UnsupportedContract and the worker lowerer bails UnsupportedCallable. Identical public usage yields full verification on methods/ctors but analyzer-only enforcement plus a dead worker claim on properties, contradicting the documented one-accountable-worker-claim story.
**Reproduction Steps**:
1. Annotate a property getter with [EnforcePure] and run the collector/worker pipeline.
2. Manifest contains the claim but IsVerifierSupported=false; outcome forced Unknown with WorkerClaimReason.UnsupportedContract; no independent replay occurs.
**Confidence**: High

### 205. Version-Gated EffectContractAttribute.PreconditionFree Consumed by Raw Property Name With No Availability or Skew Diagnostics - Attributes Assembly Drift Nulls the Whole API and Degrades to Generic SP0047
**Location**: `SharpProof.Attributes\PublicAPI.Unshipped.txt` (Lines 2-3); `SharpProof.Effects\EffectContractValues.cs` (Line 11); `ExternalEffectResolver.cs` (Lines 157-205); `SharpProof.Frontend\ContractApiIdentityResolver.cs` (Lines 147-178)
**Description**: PreconditionFree is read by literal name from NamedArguments with unrecognized arguments invalidating the contract. Whether the attribute type is trusted is gated by exact assembly-version equality plus embedded SHA-256 payload match; the only fault channel (UnreadableContractApiReason/SP0050) fires solely for IO failures. Legitimate drift (older pinned transitive Attributes copy vs newest analyzers) silently nulls every attribute lookup, reporting generic ContractApiIdentityRejected (SP0047) per member - indistinguishable from a malicious lookalike - instead of one actionable version-skew diagnostic.
**Reproduction Steps**:
1. Reference a SharpProof.Attributes DLL one revision older than the analyzers; write `[EffectContract(SharpProofEffect.None, Complete = true)]`.
2. Resolution returns null; every annotated member gets SP0047 with no version-mismatch message.
**Confidence**: Medium

### 206. Advisory-Activation Metadata Probe Never Looks at Return-Value-Targeted Closed Contracts, Diverging From Declared ReturnValue Usage
**Location**: `SharpProof.Analyzer.Core\SharpProofAnalyzerEngine.cs` (ModuleContainsClosedPrecondition Lines 413-427, checks only HandleKind.Parameter) vs `SharpProof.Attributes` targets and `ClosedContractDiagnostics.cs` (Lines 8-24 validating both)
**Description**: NotNull/Positive/InRange declare Parameter | ReturnValue targets and the symbol path validates both, but the Advisory fast-path scanning referenced assemblies' metadata looks for closed contracts parented to parameters only. In CLI metadata a [return: NotNull] is parented to the MethodDef, so a library whose only closed-contract usage is return-targeted fails to trigger even Lightweight activation - return-contract evidence is invisible during activation.
**Reproduction Steps**:
1. Build lib.dll containing only `[return: NotNull] string Get() => "";`.
2. Compile an Advisory-profile consumer referencing it with no local attribute syntax: MayContainExternalClosedPreconditions false, activation None, no analyzer actions registered.
**Confidence**: Low

### 207. AllowedCapabilitiesAttribute Declares AllowMultiple=false Yet Every Consumer Loops Over Multiple Instances - Union Reachable Only Through Undocumented Property+Accessor Side Channel
**Location**: `SharpProof.Attributes\AllowedCapabilitiesAttribute.cs` (Lines 2-3) vs `SharpProof.Analyzer.Core\EffectContractDiagnostics.cs` (DecodeCapabilities Lines 259-282 OR-ing flags) and `SharpProof.Contracts\ContractSelectionInventory.cs` (Lines 125-133)
**Description**: DecodeCapabilities iterates ImmutableArray<AttributeData> and unions capability sets across instances, expecting repeatable application like AllowedExceptions/EffectContract (AllowMultiple=true). C# blocks a second application at the same level (CS0579), so the multi-instance path is reachable only by combining property-level and accessor-level attributes, producing a union impossible to author on any single level and undocumented. Surface and consumer contracts are out of sync; per-instance invalid-argument reporting inherits the asymmetry.
**Reproduction Steps**:
1. Write `[AllowedCapabilities(IO)] public int P { [AllowedCapabilities(Clock)] get => ...; }`.
2. Analyzer silently grants the union IO|Clock, expressible no other way.
**Confidence**: Low

### 208. Composed Summaries Lose the Callee's Normal-Completion Dimension - Instantiated NormalCompletion Substituted Then Discarded by Every Composer
**Location**: `SharpProof.Summaries\IrRelationalSummaryBuilder.cs` (ApplyCall conjoins only instantiated.NormalRelation, Lines 460-463; NormalCompletion computed at 449 and never read); produced at `IrRelationalSummaryInstantiator.cs` (Lines 82-92); systemic discard confirmed at `SharpProof.CompilerArtifact\CompilerArtifactModel.generated.cs` (Lines 168-178), `CompilerCallableLowerer.cs` (Line 353), `SharpProof.Worker\AcyclicBlockPredicateExecutor.cs` (Lines 444-485)
**Description**: Instantiate substitutes both halves of a dependency summary, but ApplyCall composes caller predicates from the relation half only. The callee's record of on which inputs the call completes normally is dropped; the only residue is a coarse global may-throw flag and caller-side definedness guards. Caller summaries inherit callee result equalities ungated, and the path-guarded throw dimension collapses into an unattributable bit. Masked for admitted static scalar candidates (total ops), but the public API explicitly supports instance/reference summaries where completion is non-trivial.
**Reproduction Steps**:
1. Build a dependency summary returning a witness-requiring expression (e.g., return a[0]).
2. Build a caller composing that call; observe instantiated.NormalCompletion computed and never referenced; caller NormalRelation contains the equality with no completion guard.
3. Grep confirms zero reads of instantiated.NormalCompletion outside the instantiator.
**Confidence**: High

### 209. AddReturn Records Returned-Expression Definedness Only in Completions While Building NormalRelation From the Raw Path Predicate - Relation Asserts Result Equality Exactly Where the Body Throws
**Location**: `SharpProof.Summaries\IrRelationalSummaryBuilder.cs` (Relation uses predicate Lines 512-518; guarded completion computed 505-508 stored only in _completions at 524)
**Description**: For a return whose value requires a definedness witness, the build computes completion = predicate ∧ (value == value) but emits relation = predicate ∧ (Result == value). The two disjoined outputs diverge: NormalCompletion alone knows which inputs throw, while NormalRelation claims the equality unconditionally on those same inputs, breaking the invariant "the relation holds only on normal completions" at production time - serialized summaries over-claim to any direct consumer.
**Reproduction Steps**:
1. Build a summary for a body returning a witness-requiring expression (return s.Length).
2. Inspect NormalRelation: Result == s.Length with no definedness conjunct; evaluate with s = null-value and the interpreter/SMT view accepts the equality although the real call throws.
3. NormalCompletion contains the witness conjunct, proving the guarded form was known and stored only elsewhere.
**Confidence**: Medium

### 210. Dependencies and DependencyProvenance Silently Diverge for Transitive Dependencies - Parallel-Array Consumers Misattribute Provenance
**Location**: `SharpProof.Summaries\IrRelationalSummaryBuilder.cs` (Emission ordering 281-285: Dependencies numeric vs provenance string-key sorted; population 469-475 adds only direct dependency but merges transitive provenance)
**Description**: ApplyCall adds only the direct callee to _dependencies but merges the callee's entire DependencyProvenance, so an A→B→C chain exposes Dependencies=[B] alongside DependencyProvenance=[provB, provC]. Arrays have equal lengths only in the single-level case (exactly what the existing test asserts, masking divergence). Any consumer zipping/index-pairing the arrays attributes C's evidence to B. In-tree consumers currently use provenance standalone, making this a latent public-API trap.
**Reproduction Steps**:
1. Build summary C (no deps); build B calling C (lengths equal); build A calling B.
2. A.Dependencies=[B] but A.DependencyProvenance length 2, ordered by ordinal key sort not corresponding to numeric order.
**Confidence**: Medium
