# SharpProof ordinary correctness and reliability audit

## Scope and method

This report covers ordinary correctness and reliability behavior in verifier
process supervision and the directly connected build-task, launcher,
publication, protocol, cache, and Linux-worker paths. It excludes analyzers,
corpus tooling, documentation-only defects, test infrastructure, scripts,
non-routine process behavior, and hardening work.

The final pass was static only: source, tests, targets, and documented contracts
were inspected, but no build, test, or executable reproduction was run after the
scope changed to documentation-only work. Where an audit report supplied a prior
isolated reproduction, that is called out explicitly. Otherwise, confidence is
based on reachable control flow and the documented contract. Duplicate reports
about the same root cause are combined below.

## Confirmed bugs

### 1. Supervisor cleanup can reap the managed direct child

- Severity: High
- Affected code: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`,
  `Run`, `StopDescendants`, and `ReapExitedChildren` (approximately lines
  118-149, 204-279, and 397-405).
- Normal trigger: cancellation or timeout starts descendant cleanup while the
  supervisor's direct verifier child has not yet been observed as exited by its
  managed `Process` instance.
- Expected: cleanup reaps only adopted descendants; the direct child remains
  available to `Process.WaitForExit`, `HasExited`, and `ExitCode`.
- Actual impact: `waitpid(-1, WNOHANG)` can consume the direct child's status.
  Subsequent managed process observation can fail, causing the supervisor to
  exit before writing its cleanup receipt and turning an ordinary timeout or
  cancellation into a containment failure.
- Evidence confidence: High. The broad `waitpid` call and ordering are explicit
  in source, and the audit supplied a prior end-to-end isolated reproduction.
- Suggested fix: pass the managed direct PID into cleanup and reap only other
  direct children of the subreaper until the managed `Process` has consumed its
  own child. Broad reaping is safe only afterward.
- Regression test: start a managed direct child with another descendant, invoke
  supervisor cleanup, then assert cleanup completes, `Process.WaitForExit`
  succeeds for the direct child, its exit status remains readable, and the
  authenticated cleanup receipt is emitted.

### 2. Active cancellation is reported as timeout 124

- Severity: Medium
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `Execute` and
  `WaitForExitOrCancellation` (approximately lines 192-291 and 748-768).
- Normal trigger: MSBuild calls `Cancel` after the verifier process is active and
  supervisor cleanup succeeds.
- Expected: active cancellation preserves the supervisor's deliberate
  cancellation result, exit code 143. Pre-launch cancellation can retain its
  existing separate `-1` result.
- Actual impact: interruption makes `WaitForExitOrCancellation` return false,
  which sets `timedOut`; final result selection then chooses 124 even though the
  supervisor exits 143. Logs and automation misclassify cancellation as a wall
  timeout.
- Evidence confidence: High. The result-selection path is unambiguous in source,
  and a prior isolated reproduction reported 124.
- Suggested fix: track cancellation independently from timeout/output-limit
  interruption and select 143 after successful authenticated cleanup.
- Regression test: cancel an active sleeping verifier, wait for cleanup, and
  assert task exit 143, no timeout diagnostic, and no containment failure. Keep
  the existing pre-launch cancellation assertion separate.

### 3. A normal child-start exception after `Armed` omits cleanup

- Severity: High
- Affected code: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`, `Run`
  (approximately lines 83-116).
- Normal trigger: the resolved dotnet host disappears, loses execute permission,
  or otherwise fails in `Process.Start` after the supervisor has accepted the
  gate and written `SharpProof.Armed/1`.
- Expected: an armed supervisor reports authenticated cleanup and exits with the
  infrastructure code 125 when no child was started.
- Actual impact: expected `Process.Start` exceptions escape `Run`, so no cleanup
  receipt is written. The parent has already authenticated `Armed` and therefore
  treats the missing receipt as a process-boundary failure instead of a normal
  launch failure.
- Evidence confidence: High from source ordering and independent audit
  corroboration.
- Suggested fix: catch the documented ordinary start exceptions around only the
  direct child start, write the cleanup receipt, and return 125. Do not broaden
  the catch over descendant supervision failures.
- Regression test: run the supervisor through a valid gate with a nonexistent
  child executable and assert ordered `Armed`, `Cleanup`, and exit 125.

### 4. Published-result validation reads a multi-file set without its lease

- Severity: High
- Affected code:
  `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `Execute`
  (approximately lines 19-69), and
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, task wiring
  near lines 214-217.
- Normal trigger: another cooperative build with the same publication set
  invalidates or republishes request, result, manifest, or optional SARIF while
  validation is reading them.
- Expected: request/result/manifest observations form one stable publication
  snapshot protected by the exact request/result/manifest/optional-SARIF set
  lease documented in `docs/architecture.md` and `docs/preview-support.md`.
- Actual impact: each file is read independently with no lease. Validation can
  combine generations, fail a valid run nondeterministically, or accept a set
  that was never simultaneously current.
- Evidence confidence: High from static control flow and the documented
  full-publication-set contract.
- Suggested fix: add the optional SARIF path to the task contract and acquire the
  exact full publication set before the first read, holding it through all
  binding checks.
- Regression test: hold the exact set lease, begin validation on another task,
  mutate a generation while still holding the lease, and prove validation does
  not read until the stable generation is released.

### 5. Equal-path concurrent builds are not bound to their own publication

- Severity: High
- Affected code: `SharpProof.Worker.Launcher/Program.cs`, `PublishOutputs`
  (approximately lines 481-568);
  `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, `Execute`; and
  `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `Execute`.
- Normal trigger: two supported concurrent builds use exactly the same canonical
  publication paths. Their invalidation and publication operations serialize,
  but verification runs overlap.
- Expected: each invocation either validates its own committed generation or
  receives a deterministic superseded/conflict result.
- Actual impact: there is no publication transaction or invocation identity at
  the MSBuild validation boundary. Build A can validate build B's later coherent
  publication and report success as its own, or build B can invalidate A after A
  publishes but before A validates. Adding only the missing read lease from bug
  4 does not establish invocation identity.
- Evidence confidence: High from the separated lease scopes and the documented
  support for exactly equal concurrent publication sets.
- Suggested fix: propagate an invocation/transaction identity into the
  publication and validation contract, or keep publish and invocation-specific
  validation within one lease. Validation must compare against immutable
  invocation evidence rather than only whichever public request is current.
- Regression test: pause two same-path invocations at invalidation, publication,
  and validation barriers; exercise both A-then-B and B-invalidates-A orders and
  assert neither invocation can claim the other's generation.

### 6. Reset marker handling is neither fully serialized nor retry-safe

- Severity: Medium
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`,
  `ResetPublicationSet` (approximately lines 174-225).
- Normal trigger: clean/reset either overlaps the first publication of a set
  while `BindPublicationSet` is creating ownership markers, or cancellation/an
  ordinary delete error occurs after reset has deleted only a prefix of those
  markers.
- Expected: reset waits for the publisher, observes marker state under the same
  locks, and leaves either a complete owned set or a retryable fully reset set.
- Actual impact: marker count is computed before `AcquirePublicationSet`, so a
  publisher's transient partial marker sequence is rejected. Separately, marker
  deletion is sequential and cancellation-aware; a mid-loop failure leaves a
  partial marker set that the next reset rejects before it can acquire locks,
  making routine cleanup non-retryable.
- Evidence confidence: High from the ordering of marker counting and lock
  acquisition. This deduplicates both reset-race reports.
- Suggested fix: introduce a lock-only publication lease for reset, acquire it
  before checking marker/file state, and make marker removal a recoverable
  transaction. A retry must be able to recognize and complete an interrupted
  reset without adopting unrelated files.
- Regression test: first prove reset waits while a publisher exposes only a
  marker prefix; then inject cancellation/delete failure after every marker and
  assert a later reset completes without deleting unrelated files.

### 7. Descendant scans can authenticate too early or overrun their deadline

- Severity: Medium
- Affected code: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`,
  `StopDescendants`, `DescendantProcessIds`, and `ReadProcessParents`
  (approximately lines 204-279 and 308-377).
- Normal trigger: ordinary fork/parent-exit/reparent churn overlaps the dynamic
  `/proc` directory walk, a live entry produces a transient stat-read error, or
  a host has enough processes/descendants that repeated full scans are slow.
- Expected: cleanup is authenticated only after the process tree is quiescent;
  one incomplete observation should cause another bounded scan.
- Actual impact: `discovered.Count == 0` immediately returns `Complete: true`.
  Because the scan is not atomic, a parent can disappear from the snapshot while
  its newly created/reparented child is not yet represented in that same scan.
  Conversely, the elapsed deadline is checked only around the outer loop;
  `DescendantProcessIds`, each per-descendant `ReadProcessParents`, the signal
  loop, and the final scan can run past `maximumMilliseconds` without checks.
- Evidence confidence: Moderate. Source supports the missed-observation window,
  but a prior churn attempt did not reproduce the final false completion.
- Suggested fix: require multiple consecutive empty scans separated by a short
  bounded quiescence interval, treat transient read failures as a reason to
  rescan, and pass a monotonic absolute deadline through scan/enumeration helpers
  so they can stop work and report incomplete when the budget expires.
- Regression test: inject scan sequences `empty -> descendant -> empty`,
  transient stat failures, and deliberately slow large enumerations; prove the
  first empty result never authenticates cleanup and wall time remains within a
  small scheduling tolerance of the requested bound.

### 8. A stale `DOTNET_HOST_PATH` can override the actual current muxer

- Severity: High
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `ResolveDotNetHost` and
  `ValidateDotNetInstallation` (approximately lines 1077-1189).
- Normal trigger: a nested SDK/MSBuild invocation inherits a complete but stale
  `DOTNET_HOST_PATH` referring to a different installed dotnet tree while
  `Environment.ProcessPath` identifies the muxer actually running MSBuild.
- Expected: the verifier uses the current dotnet muxer, and a configured absolute
  executable must match that current file identity.
- Actual impact: any complete installation disclosed by `DOTNET_HOST_PATH` is
  labeled trusted without comparison to `Environment.ProcessPath`; `dotnet`
  therefore launches the stale runtime.
- Evidence confidence: High from source and a prior isolated stale-environment
  reproduction.
- Suggested fix: when `Environment.ProcessPath` is a complete direct `dotnet`
  muxer, make it authoritative and require `DOTNET_HOST_PATH` and an explicit
  executable to resolve to the same existing file. Retain the environment/PATH
  fallback only for apphost processes where the current path is not a muxer.
- Regression test: supply two complete dotnet trees, set process-path input to A
  and `DOTNET_HOST_PATH` to B, and assert mismatch rejection; also cover the
  apphost fallback.

### 9. Publication lock release stops after the first unlock failure

- Severity: Medium
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `ReleaseLocks` and
  `PublicationLease.Dispose` (approximately lines 692-702 and 877-893).
- Normal trigger: one `flock(LOCK_UN)` operation fails during disposal after
  multiple publication locks were acquired.
- Expected: disposal attempts to release/close every lock and then reports the
  first failure.
- Actual impact: `ReleaseLocks` throws immediately from the reverse release loop
  and never disposes that lock or any remaining handles. `PublicationLease` has
  already exchanged away its array, so a retry is impossible; later builds can
  remain blocked until handle finalization.
- Evidence confidence: High from exception flow.
- Suggested fix: attempt release and disposal for every element, collect the
  first exception, and throw only after all descriptors have been closed. Make
  partial acquisition cleanup use the same best-effort-all routine.
- Regression test: inject an unlock failure at each position of a multi-lock set
  and assert all later descriptors are closed, all other locks can be reacquired,
  and the original unlock error is still reported.

### 10. Atomic-file cleanup can mask the original failure

- Severity: Medium
- Affected code: `SharpProof.Ir/AtomicFile.cs`, `WriteUtf8` and
  `WriteBytesAsync` (approximately lines 70-113).
- Normal trigger: write, cancellation, or publish fails and deletion of the
  temporary file also throws `IOException` or `UnauthorizedAccessException`.
- Expected: callers receive the original operation/cancellation failure; staging
  cleanup is best effort.
- Actual impact: the unconditional `File.Delete` in `finally` replaces the
  original exception. The class already has `TryDeleteStaged`, but these two
  paths do not use it. Diagnostic causality is lost and debris can still remain.
- Evidence confidence: High from standard `finally` exception behavior.
- Suggested fix: use non-throwing best-effort staging deletion in the `finally`
  blocks while preserving the primary exception. If cleanup diagnostics are
  needed, record them without replacing the primary failure.
- Regression test: inject a primary write/cancellation failure plus a delete
  failure and assert the primary exception identity is preserved.

### 11. Slow setup can consume the verifier cleanup reserve

- Severity: Medium
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `Execute`,
  `ComputeProcessTimeout`, and `RemainingMilliseconds` (approximately lines
  121-219 and 722-768).
- Normal trigger: path canonicalization, runtime validation, process start,
  pidfd acquisition, or startup I/O takes longer than the fixed 1000 ms launcher
  reserve under ordinary host load.
- Expected: verifier execution is bounded and the configured termination grace
  plus required cleanup/readiness reserve remain available after a timeout.
- Actual impact: the sole stopwatch starts before setup. The foreground wait can
  consume all remaining `processTimeout`, leaving zero milliseconds for
  `TryTerminate` and output authentication. A slow launch can therefore become
  `-1`/retained cleanup instead of the expected bounded timeout result.
- Evidence confidence: Moderate to high from deadline arithmetic; no isolated
  timing reproduction was run in the final pass.
- Suggested fix: explicitly budget setup, verifier wall time, termination grace,
  and cleanup authentication, or cap the foreground wait so the cleanup reserve
  cannot be spent regardless of setup duration.
- Regression test: inject controlled setup delays below and above 1000 ms and
  assert termination still receives its full reserved budget without violating
  the overall bound.

### 12. Routine invalidation failures escape as unclassified task exceptions

- Severity: Medium
- Affected code:
  `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, public `Execute` and
  private `Execute(CancellationToken)` (approximately lines 49-75 and 77-245).
- Normal trigger: local path canonicalization, metadata lock acquisition, file
  deletion, container validation, or another expected filesystem operation
  throws a routine exception.
- Expected: the MSBuild task logs a concise error and returns false, consistent
  with `ResetPublishedVerification` and the task's explicit cancellation result.
- Actual impact: only requested cancellation is caught. Other expected
  `ArgumentException`, `IOException`, `UnauthorizedAccessException`, and
  `InvalidOperationException` paths escape `ITask.Execute`, producing generic
  MSB4018 failures and bypassing the task's classification.
- Evidence confidence: High from reachable exception paths and catch coverage.
- Suggested fix: add a narrow public task-boundary catch for expected validation
  and filesystem exceptions, log through `Log`, and return false. Preserve
  unexpected runtime failures.
- Regression test: induce invalid local paths, lock timeout, and deletion denial;
  assert `Execute` returns false with the intended diagnostic and does not throw.

### 13. Cancellation callbacks can race cancellation-source disposal

- Severity: Medium
- Affected code: `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, `Execute`
  and `Cancel` (approximately lines 49-75 and 247-256), and
  `SharpProof.Worker/Program.cs`, the `Console.CancelKeyPress` handler and cleanup
  (approximately lines 65-108).
- Normal trigger: either the build task captures `cancellation.Cancel` before
  execution disposes the source, or an in-flight console callback passes event
  unsubscription and reaches `cancellation.Cancel()` after worker teardown has
  disposed the source.
- Expected: concurrent cancellation is idempotent and callback lifetimes are
  drained before their `CancellationTokenSource` is disposed.
- Actual impact: both paths can call `Cancel` on a disposed source and throw
  `ObjectDisposedException` from the cancellation callback/thread. Removing an
  event handler prevents new callbacks but does not wait for one already running.
- Evidence confidence: High from both lifetime interleavings; static only for the
  worker-program facet.
- Suggested fix: synchronize delegate invocation with clearing and source
  disposal, or maintain an in-flight cancellation lifetime so disposal cannot
  occur until a captured callback finishes. Avoid holding the task lock across
  arbitrary cancellation callbacks if future callbacks can re-enter.
- Regression test: use barriers immediately after task delegate capture and
  inside an already-entered console callback; let each owner unsubscribe/clear
  and begin teardown, then release the callback and assert completion without
  `ObjectDisposedException`.

### 14. Rejected post-publication evidence remains publicly committed

- Severity: Medium
- Affected code:
  `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `Execute`, and
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
  `_SharpProofCleanupInvocation` and `_SharpProofVerifyCore`.
- Normal trigger: public request/result/manifest files are missing, malformed,
  stale, or changed after the launcher published them, so the final MSBuild
  validator rejects them.
- Expected: a failed final validation leaves no public success commit marker;
  invocation staging cleanup may remain separate.
- Actual impact: the validator only logs an error. The target's error cleanup
  removes the invocation directory but does not invalidate the rejected public
  result/request/manifest set. Invalid public evidence remains until a later
  build or explicit clean.
- Evidence confidence: High from validator and target error paths.
- Suggested fix: while holding the exact publication lease, invalidate the
  result commit marker first when validation fails, then either remove the full
  owned set or retain non-result evidence under a clearly incomplete state.
- Regression test: publish each malformed/stale case, run final validation, and
  assert failure removes the public result commit marker without touching
  unrelated files.

### 15. Cooperative worker exit skips cleanup of its descendants

- Severity: High
- Affected code: `SharpProof.Host/LinuxWorkerProcess.cs`, `Terminate` and
  `Dispose` (approximately lines 144-200).
- Normal trigger: a worker has started an ordinary child process, receives
  SIGTERM, and exits promptly while that child remains alive.
- Expected: timeout/disposal contains the complete worker process tree within the
  final grace period.
- Actual impact: tree kill runs only when the direct worker fails its SIGTERM
  wait. If the direct worker exits cooperatively, `Terminate` returns without
  checking or stopping descendants, which can continue after the worker wrapper
  reports completion.
- Evidence confidence: High from control flow and a prior isolated normal
  reproduction that observed the descendant still alive.
- Suggested fix: launch the worker in a dedicated process group or equivalent
  stable tree boundary and clean that boundary after SIGTERM regardless of the
  direct child's exit. Do not rely on discovering a tree from an already exited
  parent.
- Regression test: worker starts a sleeping child, exits on SIGTERM, and records
  the child PID; assert `WaitForExit`/`Dispose` returns only after the child is no
  longer running and remains within the final limit.

### 16. Final validation accepts protocol-incomplete or unbound results

- Severity: Medium (P2)
- Affected code:
  `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs`, `Execute`
  (approximately lines 19-69).
- Normal trigger: a stale, partially rewritten, or otherwise inconsistent result
  retains the exact request hash but contains either an arbitrary 64-character
  `inputHash` or only the four response fields inspected by this task.
- Expected: the final task accepts only a complete worker-protocol response whose
  canonical SHA-256 input identity is derived for the exact request, compiler
  artifacts, and runtime inputs.
- Actual impact: validation uses an ad hoc property predicate rather than protocol
  deserialization/validation. A JSON object containing only `protocolVersion`,
  `requestHash`, `inputHash`, and `runStatus: Complete` passes when the hashes it
  checks match, despite omitting manifest, callable/claim results, summary,
  failure reason, and errors. `inputHash` itself is checked only for string length,
  not hexadecimal syntax or binding, so invalid or stale identity also passes.
- Evidence confidence: High from the complete validation predicate; static only.
- Suggested fix: deserialize and run full worker-protocol response validation
  against the authoritative manifest/request, share the launcher's input-hash
  derivation, and require/comparatively validate canonical SHA-256 text.
- Regression test: reject the minimal four-field object, each omitted required
  response field, 64 non-hex characters, and a different valid SHA-256; accept
  only a fully valid response with the exact derived hash.

### 17. Reset ignores MSBuild cancellation while waiting up to 30 seconds

- Severity: Medium (P2)
- Affected code: `SharpProof.BuildTasks/ResetPublishedVerification.cs`,
  `Execute` (approximately lines 19-33), and
  `SharpProof.Host/LinuxPathIdentity.cs`, `AcquirePublicationSet`.
- Normal trigger: `Clean` waits behind another publication lease and MSBuild is
  canceled during the fixed 30-second acquisition window.
- Expected: the cancelable build stops the reset wait promptly and releases any
  partially acquired locks.
- Actual impact: the task does not implement `ICancelableTask` and calls
  `ResetPublicationSet` without a token, so lock acquisition remains active
  until success or the full timeout.
- Evidence confidence: High from the task interface and call arguments; static
  only.
- Suggested fix: implement a race-safe task cancellation lifetime, pass its token
  through reset/acquisition, and classify requested cancellation without
  allowing `Cancel` to race source disposal.
- Regression test: hold a publication lock, start reset, invoke `Cancel`, and
  assert prompt task completion and reacquisition of every partially held lock.

### 18. Equivalent path spellings split publication identity

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `Canonicalize`,
  `CanonicalPublicationPaths`, `ValidatePublicationTopology`,
  `AreSameExistingFile`, and `PublicationMetadataPath` (approximately lines
  50-108, 299-307, and 365-434).
- Normal trigger: one supported invocation configures a publication file as
  `/path/result.json` and another supplies `/path/result.json/`; or two
  cooperative invocations name the same absent destination with different case
  on a local case-folding filesystem.
- Expected: canonical file-path identity either normalizes equivalent spellings
  to one lock/marker identity or rejects a spelling whose equivalence cannot be
  represented safely.
- Actual impact: segment checks can address the same filesystem object while the
  returned full path preserves the trailing separator. For an absent final
  component, `AreSameExistingFile` has no identity to compare and every topology,
  sort, and metadata comparison remains ordinal, so case-fold aliases are also
  missed. Hashes, parent directories, marker paths, and locks are derived from
  the distinct strings; cooperative operations can fail or proceed concurrently
  against one eventual file identity.
- Evidence confidence: High from string-preserving canonicalization, absent-file
  identity fallback, ordinal topology, metadata hashing, and the support contract
  for local paths without a case-sensitivity restriction; static only.
- Suggested fix: reject trailing separators for publication members, and derive
  absent final-component identity from the kernel-visible parent filesystem's
  name semantics (or reject case-folding filesystems if that cannot be done
  reliably) before topology, hash, and metadata derivation.
- Regression test: acquire/reset equivalent paths with and without a trailing
  slash, then repeat with differently cased absent names on a local case-folding
  fixture; assert neither case can produce distinct locks/markers or concurrent
  publication to one destination.

### 19. Marker flush failure can be mistaken for a benign bind collision

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `BindPublicationSet`
  (approximately lines 469-549).
- Normal trigger: `stream.Write` or `stream.Flush(true)` throws `IOException`
  after `FileMode.CreateNew` has created the marker and the file remains visible.
- Expected: failure to durably write a newly owned marker aborts binding and
  rolls back that marker.
- Actual impact: the catch filter tests only `File.Exists(markerPath)`. It also
  matches failure from this invocation's own write/flush, validates bytes that
  may still be visible in cache, and continues as though another binder won a
  harmless create collision. Publication can proceed without the required
  durability guarantee.
- Evidence confidence: High from catch scope and `created.Add` ordering; static
  only.
- Suggested fix: narrow collision handling to an atomic `CreateNew` collision
  identified before ownership is recorded; never suppress write or flush errors
  from the stream this invocation created.
- Regression test: inject failure separately in marker create, write, and durable
  flush; only a genuine pre-existing exact marker may succeed, while owned-marker
  failures roll back and preserve the original exception.

### 20. Worker disposal loses ownership when termination throws

- Severity: High (P1)
- Affected code: `SharpProof.Host/LinuxWorkerProcess.cs`, `Dispose` and
  `Terminate` (approximately lines 144-200).
- Normal trigger: an ordinary native signal error, process observation error, or
  grace-period expiry makes `Terminate` throw during disposal.
- Expected: disposal either completes termination and disposes the `Process`, or
  retains retryable ownership so containment cleanup can continue.
- Actual impact: `Interlocked.Exchange` clears `_process` before termination.
  When `Terminate` throws, `process.Dispose()` is skipped and later `Dispose`
  calls see null, permanently losing both retry ownership and deterministic
  handle disposal for a possibly live worker.
- Evidence confidence: High from exception ordering; static only.
- Suggested fix: keep ownership until termination and process disposal have been
  attempted, using a synchronized state that permits a retry after failure and a
  `finally` path that does not abandon the handle.
- Regression test: inject a first termination failure followed by success;
  assert the wrapper still owns the same process, the second dispose retries,
  and the process handle is eventually disposed exactly once.

### 21. Fixed polling can overshoot or misclassify the worker deadline

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxWorkerProcess.cs`, `WaitForExit` and
  `Terminate` (approximately lines 87-119 and 159-200).
- Normal trigger: the worker exits close to `terminationStart`, or the remaining
  time before that threshold is less than the fixed 25 ms polling interval.
- Expected: the monotonic threshold determines whether completion is `Exited` or
  `TimedOut`, and waits are capped by the exact remaining duration.
- Actual impact: each cancellation wait sleeps a fixed 25 ms. At the threshold,
  the prior zero-time exit observation is stale; a worker that exits between
  that observation and termination can be returned as timeout 124. Polling can
  also delay the start of termination beyond the configured threshold.
- Evidence confidence: High for overshoot and moderate for the narrow exit race;
  static only.
- Suggested fix: wait for at most the monotonic remaining duration and perform a
  final zero-time exit observation immediately before classifying/terminating.
- Regression test: use a controllable clock/process observer at threshold-minus
  and threshold-plus boundaries; assert no wait exceeds the remainder and an
  observed normal exit is never rewritten to 124.

### 22. Diagnostic `TryDeserialize` can throw `FormatException`

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/VerifierDiagnosticTransport.cs`,
  `TryDeserialize` (approximately lines 33-86), called by
  `SharpProof.BuildTasks/RunVerifier.cs`, `LogStandardError`.
- Normal trigger: a prefixed JSON diagnostic has a numeric `schema`, `line`, or
  `column` token that is not representable as `Int32`.
- Expected: the `TryDeserialize` API returns false for every malformed transport
  line so stderr logging can fall back safely.
- Actual impact: `JsonElement.GetInt32` can throw `FormatException`, but the catch
  filter omits that type. The exception escapes diagnostic logging and can turn
  malformed verifier output into an unclassified task failure.
- Evidence confidence: High from the `GetInt32` contract and catch list; static
  only.
- Suggested fix: include `FormatException` or use `TryGetInt32` for all numeric
  fields, retaining the nonthrowing `TryDeserialize` contract.
- Regression test: cover fractional, exponent-overflow, and out-of-range numeric
  values for all three fields and assert false with no exception.

### 23. Relative configured paths use inconsistent base directories

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
  `_SharpProofInitializeVerify` and `SharpProofResetPublishedVerification`
  (approximately lines 46-55 and 239-247);
  `SharpProof.Worker.Launcher/Program.cs`, `CreateRequest` and
  `ValidateDistinctPaths` (approximately lines 855-926); and
  `SharpProof.Worker.Protocol/WorkerCachePath.cs`, `Resolve`.
- Normal trigger: MSBuild runs from a solution or other working directory while
  custom request, result, compiler-manifest, SARIF, or cache properties are
  relative.
- Expected: every phase resolves configured relative paths once against
  `MSBuildProjectDirectory`, including multi-target projection, clean,
  prevalidation, and actual cache access.
- Actual impact: SARIF `Path.GetFullPath` property functions use the MSBuild
  process working directory. Cache alias prevalidation canonicalizes the raw
  value against launcher CWD, then actual cache use resolves it against the
  compiler project directory. Custom request/result/manifest values are
  project-relative for invalidation and the child launcher, but `Clean` passes
  the same raw strings to `ResetPublishedVerification`, which resolves them
  against the MSBuild process CWD. Build and clean can therefore address
  different publications, and cache prevalidation can reason about a different
  directory from the one later opened.
- Evidence confidence: High from the paired resolution sites; static only.
- Suggested fix: project-anchor and canonicalize every configured publication
  and cache path once in the targets, pass only absolute effective paths to all
  tasks/children, and remove prevalidation of unresolved raw cache text.
- Regression test: invoke the same project from its directory and a solution
  directory with all relative custom path settings; assert identical projected,
  invalidated, launched, validated, accessed, and cleaned absolute paths.

### 24. Unsupported hosts skip existing-publication invalidation

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
  `_SharpProofInitializeVerify`, `SharpProofResetPublishedVerification`, and
  `SharpProofRejectUnsupportedWorkerHost` (approximately lines 43-118 and
  234-260), plus `SharpProof.BuildTasks/InvalidatePublishedResult.cs` and
  `ResetPublishedVerification.cs`.
- Normal trigger: a checkout was verified in the canonical Linux host, then an
  enabled verifier `Build` or `Clean` is invoked from a portable/unsupported
  MSBuild host while the prior default or custom publication still exists.
- Expected: an enabled build removes the prior stable result before reporting
  that verification cannot run on this host, and `Clean` removes known build
  publications without attempting verification.
- Actual impact: the invalidation task inside `_SharpProofInitializeVerify` and
  the reset target both require `_SharpProofVerifierHostSupported == true`.
  Unsupported `Build` instead reaches the later post-compile rejection with the
  old successful result still committed; unsupported `Clean` skips reset
  entirely, so custom publications outside ordinary intermediate-directory
  cleanup can remain stale across a successful clean.
- Evidence confidence: High from the two support-gated mutation sites and the
  later unsupported-host rejection target; static only.
- Suggested fix: separate portable, ownership-aware publication invalidation
  and cleanup from verifier execution support. Delete the stable result first
  on every enabled unsupported-host build, reset the full owned set on clean,
  and preserve the Linux lease protocol on hosts where it is available.
- Regression test: create a complete owned publication on the supported host,
  then force the unsupported-host target path. Assert an enabled `Build` fails
  with the host diagnostic but removes the result commit, while `Clean` removes
  every configured member without starting verification.

### 25. Failure-result recovery mutations can replace the original classification

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/Program.cs`, `RunMain`,
  `WriteLauncherFailureAsync`, and `DeleteIfExists` (approximately lines 89-153
  and 747-770).
- Normal trigger: the launcher classifies an ordinary worker/launcher failure and
  its recovery response write fails, or a worker timeout 124 is followed by an
  ordinary failure deleting a prior private result.
- Expected: recovery either publishes the classified failure response or returns
  a deterministic infrastructure/publication result while retaining the
  original failure as context.
- Actual impact: the writes at the catch recovery path, missing-result path, and
  malformed-result replacement path are outside any write-failure boundary.
  The post-timeout `DeleteIfExists` call is outside that boundary as well and
  occurs before timeout-response synthesis. Any of these mutation exceptions
  escapes `RunMain`, replacing the original classification and potentially
  leaving no protocol result.
- Evidence confidence: High from catch boundaries; static only.
- Suggested fix: centralize timeout cleanup and best-effort failure publication
  behind one bounded mutation classifier and return its deterministic exit code
  without recursively attempting the same failing destination.
- Regression test: inject write failure at each recovery call site and deletion
  failure after exit 124; assert deterministic exit behavior and preserved
  diagnostic classification with no unhandled exception.

### 26. Assumption notifications can suppress the SARIF run-failure notification

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/SarifProjection.cs`, `Serialize`
  (approximately lines 31-51).
- Normal trigger: a non-`Complete` response reports user/trusted assumptions but
  has no protocol error notification.
- Expected: SARIF always includes a run-failure notification for non-complete
  execution, independently of assumption or other informational notifications.
- Actual impact: adding SP0048 makes `notifications.Count` nonzero, so the
  subsequent conditional omits `worker.<RunStatus>`. Consumers see the assumption
  note but no tool-execution notification explaining timeout, cancellation, or
  failure.
- Evidence confidence: High from list mutation and condition ordering; static
  only.
- Suggested fix: test specifically for an existing run-failure notification, or
  always add one for non-complete status rather than requiring an empty list.
- Regression test: serialize each non-complete status with assumptions and with
  unrelated errors; assert exactly one run-status notification plus all other
  notifications.

### 27. SARIF relative locations have no base and are not URI-escaped

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/SarifProjection.cs`, `Result` and
  `LocationUri` (approximately lines 135-183).
- Normal trigger: compiler locations are project-relative and contain spaces,
  `#`, `%`, or other URI-significant characters, especially when SARIF is
  published outside the project directory.
- Expected: SARIF artifact locations resolve to the intended source file and use
  valid URI-reference escaping.
- Actual impact: relative paths are emitted without `uriBaseId`/base metadata, so
  consumers resolve them relative to the SARIF file rather than the project.
  The fallback merely replaces backslashes and leaves reserved characters
  unescaped, changing URI meaning or producing invalid references.
- Evidence confidence: High from the serialized SARIF shape and `LocationUri`;
  static only.
- Suggested fix: emit a stable project source base and `uriBaseId`, and construct
  escaped relative URI references with `Uri` APIs instead of string replacement.
- Regression test: publish SARIF outside the project for relative source names
  containing spaces, `#`, and `%`; resolve each URI through a SARIF consumer and
  assert the exact source path.

### 28. Cache maintenance ignores cancellation while holding the exclusive lock

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Protocol/ProtocolJson.cs`,
  `ReadUtf8FileAsync` (approximately lines 41-48), and
  `SharpProof.Worker/VerificationCache.cs`, `TryReadAsync`, `TryWriteAsync`, and
  `TryStageCapacity` (approximately lines 12-209 and 239-286).
- Normal trigger: cancellation arrives either during an asynchronous cache-file
  read or while a directory with many owned entries is being enumerated,
  ordered, and length-accounted for capacity, all while the cache's exclusive
  `FileShare.None` lock is held.
- Expected: every potentially slow cache operation observes the supplied token
  promptly and the `finally` releases the lock for other verifier invocations.
- Actual impact: the reader checks cancellation only before and after the
  parameterless `ReadToEndAsync`, so in-flight I/O cannot observe it.
  `TryStageCapacity` checks once before a tokenless
  `EnumerateFiles`/`OrderBy`/`ToArray` pipeline and performs its entire initial
  file-length accumulation without another check. Cancellation and competing
  cache users can therefore remain blocked for the duration of either slow
  operation.
- Evidence confidence: High from the async overload, token-check placement, and
  lock lifetime; static only.
- Suggested fix: use a cancellation-aware read and make capacity discovery,
  materialization, ordering, and length accounting incrementally token-aware,
  while preserving lock disposal and transaction rollback in `finally`.
- Regression test: separately block a cache read and populate a controllably
  large capacity scan under the lock; cancel each operation and assert prompt
  interruption, rollback where applicable, and immediate lock reacquisition by
  a second invocation.

### 29. Out-of-range numeric enum strings escape malformed-request handling

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Worker.Protocol/ProtocolJsonSupport.cs`, `EnsureCanonicalEnum`
  (approximately lines 151-177), and `SharpProof.Worker/Program.cs`, request
  deserialization (approximately lines 50-64).
- Normal trigger: a protocol enum is encoded as a numeric string outside its
  underlying integer range.
- Expected: every invalid enum spelling becomes `JsonException` and the worker
  publishes the normal `InvalidRequest`/`request.malformed` response.
- Actual impact: `Enum.Parse` throws `OverflowException`; the helper converts only
  `ArgumentException`, and the worker's malformed-request catch omits overflow.
  The worker exits through an unhandled exception instead of its protocol
  invalid-request path.
- Evidence confidence: High from the documented `Enum.Parse` exceptions and both
  catch filters; static only.
- Suggested fix: convert both `ArgumentException` and `OverflowException` to
  `JsonException`, or reject numeric-looking enum strings before parsing.
- Regression test: deserialize minimum-minus-one, maximum-plus-one, and very long
  numeric strings for every underlying enum width; assert a canonical malformed
  request response rather than process failure.

### 30. Effect-certainty admission disagrees with composite response validation

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Worker.Protocol/ProtocolModel.generated.cs`,
  `MatchesEffectCertainty` and `MatchesEffectEvidenceTuple` (approximately lines
  756-781), and `SharpProof.Worker/EffectClaimResultAssembler.cs`, `Assemble`.
- Normal trigger: the compiler emits a schema-admitted unknown effect result with
  `TrustedCompleteBoundary`, such as `EffectSummaryIncomplete`,
  `EffectContractNotEstablished`, `ResourceLimit`, or `UnsupportedBody`.
- Expected: a tuple accepted by compiler-artifact mapping and the assembler's
  initial certainty check also satisfies final worker response validation.
- Actual impact: `MatchesEffectCertainty` explicitly admits those tuples and the
  production assembler forwards them, but `MatchesEffectEvidenceTuple` admits
  trusted-boundary certainty only for `Proven`. Final validation therefore
  rejects the worker's own assembled response as malformed.
- Evidence confidence: High. Both generated tables and the production forwarding
  path are explicit; static only.
- Suggested fix: make one generated tuple authority drive compiler admission,
  assembly, and composite validation; either support the unknown trusted tuples
  consistently or remove them at every producer boundary.
- Regression test: round-trip every schema-admitted effect tuple from compiler
  evidence through assembly and full response validation, asserting identical
  acceptance decisions.

### 31. One throwing backend dispose leaks later lanes and can replace a result

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync` cleanup,
  `TryCreateLanes`, and `VerificationLane.DisposeOwnedBackend` (approximately
  lines 341-347 and 421-539).
- Normal trigger: one owned backend throws during disposal after several solver
  lanes have been created, either on normal verification completion or partial
  lane-creation failure.
- Expected: cleanup attempts every owned backend, preserving the verification or
  creation failure and reporting disposal failure only after all lanes are
  released.
- Actual impact: both `foreach` cleanup loops stop at the first exception. Later
  backends remain undisposed; on the normal `finally` path, the dispose exception
  also replaces the already assembled verifier response.
- Evidence confidence: High from cleanup loops and exception propagation; static
  only.
- Suggested fix: dispose every lane best-effort, collect the first cleanup error,
  and preserve any primary exception/result classification. Make per-lane dispose
  clear ownership in a `finally`.
- Regression test: make each lane position throw during dispose; assert every
  other backend is disposed and the original verification/creation outcome is
  retained with deterministic cleanup diagnostics.

### 32. Cache transaction debris is neither recovered nor capacity-accounted

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/VerificationCache.cs`, `TryWriteAsync`,
  `TryStageCapacity`, `RestoreStaged`, and `RestorePrevious` (approximately lines
  123-209 and 239-330).
- Normal trigger: timeout/process interruption occurs after a cache entry is moved
  to a GUID-suffixed `.rollback` or `.eviction` file, or a best-effort rollback
  move/delete fails.
- Expected: the next lock owner recovers or removes incomplete transactions and
  all cache-owned bytes count toward the configured capacity.
- Actual impact: lock acquisition performs no recovery, and capacity enumeration
  includes only `*.sharp-proof-cache.json`. Orphan transaction files can hide the
  only prior valid entry and accumulate indefinitely outside the byte limit.
- Evidence confidence: High from filename patterns, startup flow, and capacity
  enumeration; static only.
- Suggested fix: while holding the cache lock, recover transaction suffixes using
  validated original names before reads/writes, remove superseded debris, and
  include all owned transaction bytes in capacity decisions.
- Regression test: interrupt after every move boundary and inject rollback
  failures; on the next operation assert deterministic restore/discard, no lost
  valid entry, and total owned bytes at or below the configured maximum.

### 33. Publication operations admit non-regular filesystem nodes

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `Canonicalize`,
  `BindPublicationSet`, and `ResetPublicationSet` (approximately lines 50-108,
  174-225, and 469-550), plus `SharpProof.Worker.Launcher/Program.cs`,
  `CapturePreviousPublication` and `PublishMember` (approximately lines 571-615).
- Normal trigger: an exact ownership marker remains while an absent/reused
  publication path is populated by an ordinary FIFO, Unix-domain socket, or
  device node before a later `Clean`/reset or another publication.
- Expected: every publication mutator enforces the stated regular-file contract,
  rejecting a present non-regular member without reading, unlinking, or replacing
  it.
- Actual impact: canonicalization admits every non-symlink final node, and an
  existing exact marker makes binding skip the pre-existing-destination refusal.
  Reset rejects only `Directory.Exists(path)` before `File.Delete`, which unlinks
  other node types on Linux. The launcher similarly treats `File.Exists` as
  sufficient for `File.ReadAllBytes` and rejects only directories; a FIFO read
  can block, a device read can fail or be unbounded, and publication can proceed
  toward atomic replacement rather than rejecting the node's type.
- Evidence confidence: High from final-component admission, marker reuse, and the
  reset/capture/publish predicates; static only.
- Suggested fix: use one `lstat`-based final-component authority immediately
  before every read, delete, and rename while holding the set lease. Require a
  present member to be `FileTypeRegular`; treat every other node type as an
  ownership/type mismatch.
- Regression test: retain an exact marker while replacing each member with a
  FIFO, socket, and available device-node fixture; exercise both reset and
  launcher publication and assert prompt rejection with no read, unlink, or
  replacement, while regular files retain current behavior.

### 34. Equal mountpoints can select the hidden filesystem type

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `RequireLocalPath` and
  `FindFileSystemType` (approximately lines 111-121 and 730-765).
- Normal trigger: a container or host has stacked/overmounted filesystems at the
  same decoded mountpoint, with the visible top mount and hidden lower mount
  having different filesystem types.
- Expected: local-only publication policy classifies the filesystem actually
  visible for the canonical path.
- Actual impact: the parser retains the first longest mount and skips every later
  equal-length match because of `mount.Length <= bestMount.Length`. For a stacked
  mount, that can retain the hidden lower entry rather than the visible top one,
  wrongly accepting a remote publication path or rejecting a visible local path.
- Evidence confidence: High from equal-length selection; the ordinary stacked
  mount trigger is supported by Linux mount metadata. Static only.
- Suggested fix: query the visible filesystem directly (`statfs`/`fstatfs` on the
  path or nearest existing ancestor), or implement mount-stack-aware selection
  rather than first textual longest-prefix selection.
- Regression test: provide/construct mount metadata with identical mountpoints in
  both local-over-remote and remote-over-local orderings; assert classification
  follows the visible top mount, not the first line.

### 35. A later caller cancellation can rewrite a project timeout

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`, the linked
  `projectBoundary` and local `Interrupted` classifier (approximately lines
  39-77, 112-119, 283-288, and 337-340).
- Normal trigger: the project wall-time token fires first, but the caller token is
  canceled before the resulting `OperationCanceledException` reaches
  `Interrupted`.
- Expected: the first interruption cause is stable: project deadline produces
  `TimedOut`/`ProjectTimeout`, while caller cancellation produces `Canceled`.
- Actual impact: both causes cancel one linked token, and `Interrupted` decides by
  reading the caller token's current state. A later caller cancellation therefore
  rewrites an already-triggered timeout as `Canceled`, changing run status,
  callable/claim reasons, diagnostics, and launcher exit behavior. This is
  distinct from build-task bug 2, which maps an active supervisor cancellation
  to 124.
- Evidence confidence: High from linked-token causality loss and classification
  timing; static only.
- Suggested fix: record the first cause atomically with separate caller and
  timeout registrations/tokens, and have every interruption path classify from
  that immutable cause rather than current token state.
- Regression test: use barriers after timeout firing but before catch
  classification, cancel the caller, and assert timeout remains timeout; reverse
  the order and assert cancellation remains cancellation.

### 36. Inner verifier start exceptions bypass infrastructure exit 125

- Severity: Medium (P2)
- Affected code: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`,
  `RunWorker` (approximately lines 178-201), and
  `SharpProof.BuildTasks/Program.cs`, `Main`.
- Normal trigger: the validated dotnet executable disappears, loses execute
  permission, or otherwise becomes unstartable after the outer supervisor starts
  its intermediary but before that intermediary calls its own `Process.Start`.
- Expected: the inner start boundary converts ordinary start failure to
  infrastructure exit 125; the outer supervisor then emits its authenticated
  cleanup receipt and returns the classified code.
- Actual impact: only `Process.Start() == false` maps to 125. Ordinary start
  exceptions escape `RunWorker` and the executable entry point, producing a
  runtime-defined abnormal exit. The outer supervisor still performs cleanup,
  but propagates that abnormal code instead of the stable infrastructure result.
  This is distinct from bug 3, where the outer post-`Armed` start itself omits the
  cleanup receipt.
- Evidence confidence: High from the uncaught entry-point path; static only.
- Suggested fix: catch the documented ordinary `Process.Start` exceptions around
  only the inner start call and return 125, leaving wait/containment failures
  independently observable.
- Regression test: let the outer intermediary start, remove or deauthorize the
  executable before the inner start barrier, and assert authenticated cleanup plus
  final exit 125 with no unhandled-exception exit.

### 37. Worker failure-response writes can escape their own catch paths

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/Program.cs`, local `Respond`, malformed-request,
  cancellation, and infrastructure catches (approximately lines 45-107), plus
  `WriteResponseAtomicAsync` near lines 184-187.
- Normal trigger: the result directory is removed, becomes unwritable, or has an
  ordinary I/O failure while a catch body is publishing its protocol failure
  response.
- Expected: inability to publish a worker response produces a deterministic
  nonzero worker result so the launcher can execute its missing-result recovery.
- Actual impact: each catch body directly awaits `Respond`. An exception thrown
  from one catch body is not considered by sibling catches on the original
  `try`, so response serialization/write failure escapes `Main` and leaves an
  unhandled worker with no result. A successful-path response write can first
  enter the infrastructure catch, but its second write escapes the same way.
  This is the worker boundary counterpart to launcher bug 25, not the same call
  site.
- Evidence confidence: High from C# catch semantics and the unbounded awaited
  write; static only.
- Suggested fix: give response publication one non-recursive boundary that maps
  ordinary serialization/filesystem failures to a deterministic nonzero exit and
  never tries to report a write failure through the same failing destination.
- Regression test: inject write failure from the success, malformed-request,
  cancellation, and infrastructure response paths; assert no exception escapes
  and each returns the documented recovery exit with no partial committed result.

### 38. Apostrophes in valid paths break MSBuild target expressions

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, configured
  path initialization/normalization (approximately lines 45-55), verifier
  argument construction (approximately lines 156-171 and 193-198), and clean
  projection (approximately lines 238-247).
- Normal trigger: a project directory or configured request, result, manifest,
  SARIF, or cache path contains an apostrophe, such as `/src/O'Brien/result.json`.
- Expected: every otherwise valid Linux path is passed as opaque property/item
  data through initialization, launch, and clean.
- Actual impact: raw expanded property values are embedded inside single-quoted
  MSBuild conditions and property-function arguments. An apostrophe terminates
  that literal, making the resulting condition/function expression malformed or
  changing its tokenization before `[MSBuild]::Escape` can encode the value.
  Verification or `Clean` therefore fails during target evaluation instead of
  operating on the configured path. This is distinct from bug 23's choice of
  relative base directory.
- Evidence confidence: High from the target expression construction and absence
  of any path contract excluding apostrophes; static only.
- Suggested fix: never interpolate raw path text into a quoted MSBuild expression.
  Normalize/escape it as data before expression parsing, or pass it through task
  parameters/item metadata whose values are not reparsed as condition syntax.
- Regression test: place the project and, separately, every configurable output
  path beneath `O'Brien`; assert initialization, launcher arguments, publication,
  validation, and clean preserve the exact path.

### 39. Accepted short grace values cannot provide the cleanup reserve

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Worker.Protocol/WorkerExecutionEnvelope.cs`,
  `MaximumElapsedMilliseconds` (approximately lines 3-26);
  `SharpProof.Worker.Launcher/Program.cs`, `RunWorker`, `ComputeHardLimit`,
  `ComputeFinalLimit`, and `LauncherArguments.ValidatePreflight` (approximately
  lines 214-236, 275-286, and 942-952); and
  `SharpProof.Host/LinuxWorkerProcess.cs`, `WaitForExit`/`Terminate`.
- Normal trigger: a supported build configures
  `SharpProofVerifyTerminationGraceMilliseconds` between 1 and 100, inclusive,
  and the worker reaches its project wall-time limit.
- Expected: every accepted grace value leaves the declared 100 ms cleanup reserve
  available after producer time, including time for graceful termination and the
  final forced-stop wait.
- Actual impact: preflight and the protocol envelope accept any value from 1
  through 300000, but producer/termination start is computed as
  `project + Max(1, grace - 100)` while final limit is `project + grace`. At
  grace 1, both limits are identical, so `Terminate` has zero remaining wait; at
  grace 2-100, only part of the 100 ms reserve exists. Routine timeout cleanup can
  therefore immediately force-kill or throw grace-period failure despite an
  accepted configuration. Unlike bug 11, the reserve is impossible even with no
  setup delay; unlike bug 21, the defect exists before polling overshoot.
- Evidence confidence: High from the shared arithmetic and preflight range;
  static only.
- Suggested fix: require grace to exceed `CleanupReserveMilliseconds` (minimum
  101 with the current one-millisecond producer allowance), or redefine the
  envelope so producer allowance and cleanup reserve are explicit nonoverlapping
  validated budgets sourced from one authority.
- Regression test: cover grace 1, 2, 100, 101, and the maximum; assert rejected
  values cannot enter launch, and every accepted boundary gives `Terminate` the
  full cleanup reserve without overflow.

### 40. PATH parsing corrupts legal dotnet installation directories

- Severity: Medium (P2)
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `ResolveDotNetHost`,
  `ResolveDotNetFromPath`, and `ValidateDotNetInstallation` (approximately lines
  1077-1188).
- Normal trigger: `DOTNET_HOST_PATH` is absent and the usable direct dotnet muxer
  is in an absolute PATH directory whose final name ends in whitespace or a
  double quote, both legal Linux filename characters.
- Expected: PATH fields are colon-delimited opaque directory names; resolution
  preserves each absolute field exactly before appending `dotnet`.
- Actual impact: each field is transformed by `value.Trim().Trim('"')`. The task
  therefore probes a different pathname, either reporting no trusted muxer or
  selecting a different complete installation at the trimmed sibling path.
  This is distinct from bug 8: that issue chooses stale `DOTNET_HOST_PATH` over
  the current muxer, while this issue corrupts the PATH fallback itself.
- Evidence confidence: High from the unconditional transformations and the Linux
  path contract; static only.
- Suggested fix: treat nonempty PATH fields verbatim, retaining the existing
  explicit rejection of relative/current-directory entries. Do not apply shell
  quote parsing to an environment value that the invoking shell has already
  parsed.
- Regression test: put complete dotnet installations under absolute directories
  ending in a space and in `"`, with a different installation at each trimmed
  sibling; assert exact resolution or deterministic rejection without silently
  substituting the sibling.

### 41. Private staging I/O failures are reported as invalid launcher input

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/Program.cs`, the initial `RunMain`
  preflight/request/staging block (approximately lines 48-86), especially
  `AtomicFile.WriteUtf8Async(arguments.RequestPath, ...)` and
  `DeleteIfExists(arguments.ResultPath)`.
- Normal trigger: arguments, runtime snapshot, compiler manifest, and projected
  request have already validated, but writing the invocation-private request or
  deleting its prior private result fails with an ordinary `IOException` or
  `UnauthorizedAccessException`.
- Expected: staging/publication I/O failure is classified as launcher
  infrastructure/publication failure with its corresponding stable non-input
  exit and diagnostic.
- Actual impact: the same catch covers both input construction and the subsequent
  staging mutations. It prints `SharpProof launcher input is invalid` and returns
  2 even though input was valid, misleading users and automation about the
  corrective action. This precedes and is distinct from bug 25, which concerns
  writes made while recovering an already classified launcher failure.
- Evidence confidence: High from statement and catch ordering; static only.
- Suggested fix: end the invalid-input boundary once the request validates, then
  place private request write/result deletion under a separate narrow I/O
  classifier with an infrastructure/publication result.
- Regression test: inject write and delete failures only after successful request
  validation; assert neither reports invalid input or exit 2, while genuine
  malformed input retains the existing classification.

### 42. Cache-hit validation can return Complete after its deadline is canceled

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync` cache-hit
  path and local `Assemble` helper (approximately lines 150-203), together with
  `WorkerProtocolJson.Validate` and `CompilerResponseEvidenceAuthority.Validate`.
- Normal trigger: a cache entry is read successfully, then caller cancellation or
  the project deadline fires while the synchronously assembled response is being
  checked against the full manifest/evidence authority.
- Expected: a token canceled before cache-hit validation completes prevents a
  `Complete`/`Hit` response and follows the normal interrupted classification.
- Actual impact: `Assemble` checks the linked token before and after response
  construction, but the subsequent protocol/evidence validation can enumerate
  the complete callable and claim sets without a token. On valid evidence the
  branch returns immediately with no final token check, so cancellation during
  that validation is ignored and a completed cache hit escapes the project
  boundary. This is distinct from bug 28's noncancelable cache-file read and bug
  35's choice between two already-observed interruption causes.
- Evidence confidence: High from the last token check, nontrivial synchronous
  validation path, and immediate return; static only.
- Suggested fix: make validation cancellation-aware or perform a final
  `projectBoundary.Token.ThrowIfCancellationRequested()` immediately before the
  cache-hit return, with the interruption cause recorded consistently.
- Regression test: pause evidence validation after the last existing token check,
  trigger caller cancellation and project timeout separately, then release it;
  assert neither path returns `Complete`/`Hit`.

### 43. Semicolons turn validated paths into multiple MSBuild items

- Severity: High (P1)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, compiler-owned
  output item construction (approximately lines 79-97), cleanup `RemoveDir`
  (approximately lines 117-135), and verification `MakeDir` (approximately line
  153).
- Normal trigger: a supported Linux project or intermediate path contains a
  semicolon, so the derived invocation directory or a compiler output path also
  contains that legal filename character.
- Expected: the scalar path validated as the exact direct child of the
  package-owned `runs` directory, and every compiler output path, reaches its task
  as one opaque filesystem pathname.
- Actual impact: the target passes the raw scalar to list-valued `Directories`
  parameters and raw paths to item `Include`. MSBuild treats semicolons as item
  separators, so `MakeDir` and compiler-output alias validation operate on path
  fragments. More seriously, cleanup first validates the unsplit scalar and then
  `RemoveDir` recursively deletes the split fragments; an ordinary semicolon path
  can therefore delete directories outside the validated invocation root. This
  is distinct from bug 38: apostrophes break expression parsing, while this path
  parses successfully and is reinterpreted as a list.
- Evidence confidence: High from scalar validation followed by unescaped
  list-valued task/item expansion; static only.
- Suggested fix: escape each scalar path with MSBuild's data/item escaping before
  assigning it to `Directories` or `Include`, or carry paths as single task items
  whose `ItemSpec` is never reparsed as list syntax. Apply the same rule to all
  three facets so creation, invalidation, and cleanup agree.
- Regression test: build and clean a project beneath a directory containing `;`,
  with sentinel directories matching both would-be fragments; assert one exact
  invocation directory is created/removed, no sentinel is touched, and compiler
  outputs arrive at invalidation as unsplit exact paths.

### 44. A failed post-commit directory sync can leave a visible publication

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/Program.cs`, `PublishOutputs`,
  `PublishMember`, `TryRollbackPublication`, `RestorePreviousPublication`,
  `TryInvalidatePublication`, and `InvalidatePublication` (approximately lines
  481-568 and 608-717).
- Normal trigger: publication renames the result member successfully, then the
  result directory sync fails; restoring the previous generation or invalidating
  the new generation also encounters an ordinary filesystem failure.
- Expected: a launcher publication failure leaves either the complete prior
  generation or no visible result commit marker, and any failure to re-establish
  that invariant remains observable.
- Actual impact: `PublishMember` renames the final result before syncing its
  directory. A sync exception enters rollback after the result is already
  visible. Restore can partially republish members, and invalidation records
  per-member failures but continues; the outer best-effort invalidation then
  suppresses its own failure completely. The launcher reports publication
  failure while a result commit marker, a mixed generation, or an incomplete set
  can remain visible. This is distinct from bugs 5, 14, 19, and 25: no concurrent
  build, rejected-response validation, ownership-marker bind, or failure-response
  write is required.
- Evidence confidence: High from commit/sync ordering and the nested suppressed
  rollback paths; static only.
- Suggested fix: define an explicit durable commit protocol in which the result
  marker becomes visible only after all non-result members and their directories
  are durable. If final commit durability fails, remove the result first and make
  failure to restore/invalidate a mandatory, recoverable transaction state rather
  than suppressing it; a generation directory plus atomic pointer is another
  option.
- Regression test: inject a directory-sync failure immediately after the result
  rename and failures at each restore/delete boundary; assert the result marker
  is absent or the complete prior generation is restored, never a mixed visible
  set, and rollback failure is not silently discarded.

### 45. Sequential task disposal can break retained cleanup authentication

- Severity: High (P1)
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `Dispose`, `Execute`,
  `ReadBoundedOutputAsync`, `RetainCleanupAnchor`, and
  `ObserveCleanupAnchorAsync` (approximately lines 87-92, 178-186, 221-340,
  458-547, and 617-690).
- Normal trigger: bounded output interrupts the foreground wait while the
  supervisor output drain is incomplete, so `Execute` transfers the process and
  reader tasks to a cleanup anchor; the normal owner then disposes the completed
  task object before the retained supervisor emits more output and its valid
  cleanup receipt.
- Expected: retained cleanup owns every resource its output readers require until
  authentication completes, and ordinary sequential `Dispose` after `Execute`
  cannot invalidate that transferred lifetime.
- Actual impact: both retained readers captured the task-owned
  `_outputLimitSignal`, but `Dispose` immediately disposes that event. Every
  subsequent over-limit read calls `Set()` and faults with
  `ObjectDisposedException`, potentially before parsing the valid cleanup line.
  The anchor deliberately treats a faulted output task as no authenticated
  output and invokes the containment-failure callback, whose production action
  is `Environment.FailFast`. This is neither the rejected concurrent-`Dispose`
  scenario nor bug 13's cancellation-source callback race.
- Evidence confidence: High from the explicit resource capture, ownership
  transfer, event disposal, fault-to-null authentication path, and production
  callback; static only.
- Suggested fix: transfer ownership of every reader dependency to the cleanup
  anchor, or replace the disposable event captured by asynchronous readers with
  an independently owned atomic/cancellation signal. Dispose the signal only
  after both readers have completed, including retained-cleanup completion.
- Regression test: gate stdout after it exceeds the capture limit, let `Execute`
  retain cleanup and return, dispose the task sequentially, then release more
  output followed by a valid cleanup receipt; assert the reader does not fault,
  no authentication-failure callback runs, and the anchor is released.

### 46. Custom PDB outputs are absent from compiler/publication collision checks

- Severity: High (P1)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
  `_SharpProofInitializeVerify` compiler-owned output inventory (approximately
  lines 41-113) and post-`CoreCompile` `SharpProofVerify` ordering (approximately
  lines 227-232), plus
  `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, compiler-output alias
  validation (approximately lines 108-113 and 185-223).
- Normal trigger: a supported project uses `PdbFile` or
  `_DebugSymbolsIntermediatePath` to place its PDB somewhere other than the two
  guessed default locations, configures `SharpProofVerifyResultFile` to that same
  path, and the PDB does not exist when pre-compilation invalidation runs.
- Expected: every evaluated compiler-owned output, including a customized PDB,
  is reserved against all verifier publication paths before either writer can
  mutate it.
- Actual impact: the inventory includes only
  `$(IntermediateOutputPath)$(TargetName).pdb` and the `.pdb` sibling of
  `$(TargetPath)`; it omits the compiler's supported custom PDB destinations.
  Because the file is initially absent, file-identity checks cannot compensate
  for the missing path string. `CoreCompile` then creates the PDB, and the
  post-compile launcher publishes result JSON over that same pathname. The build
  can report verifier success while destroying its compiler-produced debug
  symbols. This is distinct from bugs 12, 16, and 43: no task exception,
  incomplete result predicate, or MSBuild list splitting is required.
- Evidence confidence: High from the incomplete evaluated inventory, exact/absent
  alias predicate, and target ordering; static only.
- Suggested fix: derive the compiler-owned inventory from the SDK's evaluated
  actual output properties/items, explicitly including `PdbFile` and
  `_DebugSymbolsIntermediatePath`, and revalidate the final inventory before
  post-compile publication. Avoid guessing compiler filenames from target and
  intermediate defaults.
- Regression test: vary `PdbFile` and `_DebugSymbolsIntermediatePath` independently
  with no pre-existing PDB, alias each custom destination to the verifier result,
  and assert the build rejects the topology before publication and preserves the
  compiler PDB bytes.

### 47. A typed containment failure exit 125 is projected to generic exit 3

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/Program.cs`, `RunMain`,
  `ClassifyLauncherFailure`, and `ValidateAndReport` (approximately lines 90-207
  and 334-437), plus
  `SharpProof.Worker.Launcher/LauncherProjections.generated.cs`, `ExitCode` and
  `NoResultFailure` (approximately lines 12-32).
- Normal trigger: worker containment setup throws an ordinary classified
  exception, or the contained worker exits 125 without producing a result.
- Expected: the launcher publishes a valid `Failed`/`ContainmentFailure` response
  and preserves the deliberately classified infrastructure/containment exit 125.
- Actual impact: both exception classification and no-result projection correctly
  synthesize exit 125 with `ContainmentFailure`. After the response validates,
  however, `ValidateAndReport` maps every non-complete `Failed` status through a
  status-only table to 3. `RunMain` prefers that nonzero response projection over
  the original 125, so the final process result contradicts its own typed
  evidence. This is distinct from bug 36's uncaught inner start exception.
- Evidence confidence: High from the generated 125 mappings, status-only exit
  table, and final return ordering; static only.
- Suggested fix: make exit projection depend on the validated `(RunStatus,
  FailureReason)` pair, preserving 125 for `Failed`/`ContainmentFailure`, and use
  that single projection authority for caught and no-result recovery paths.
- Regression test: inject both a classified containment-start exception and a
  child exit 125 with no result; assert the published response validates as
  `Failed`/`ContainmentFailure` and the launcher's final exit remains 125.

### 48. Request validation and hashing occur outside the project wall timer

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync`
  initialization (approximately lines 39-55), together with
  `SharpProof.Worker.Protocol/ProtocolJson.cs`, request validation/hash work, and
  `SharpProof.Host/LinuxWorkerProcess.cs`, the enclosing grace-inclusive wait.
- Normal trigger: a valid request uses a short supported project wall value and
  ordinary scheduling makes request validation and request-hash computation
  consume a material portion of the configured budget.
- Expected: the documented outer project wall bounds the full valid-request
  worker operation; setup time is charged to that same monotonic boundary.
- Actual impact: elapsed reporting starts on entry, but the linked timer is
  created and armed only after full request validation and request hashing. The
  worker therefore receives a fresh complete project budget after that setup.
  It can return `Complete` with elapsed time already greater than the configured
  project wall, provided it still finishes inside the launcher's separate
  grace-inclusive hard limit. This is distinct from bug 11's build-task cleanup
  reserve and bug 42's missing cancellation check after cache-hit validation.
- Evidence confidence: High from stopwatch/timer ordering, the accepted positive
  wall-time range, and the larger enclosing launcher limit; static only.
- Suggested fix: establish one absolute monotonic deadline at `VerifyAsync` entry.
  After preserving invalid-request precedence, subtract validation/hash elapsed
  before arming the linked token and classify an already-expired valid request as
  timed out; do not grant a fresh relative interval.
- Regression test: gate valid request validation and hashing across a 1 ms and a
  normal project boundary, then release them before the launcher hard limit;
  assert neither case can return `Complete` after the project deadline and that
  elapsed time and timeout classification remain consistent.

### 49. Pre-editor-config dependency failures preserve a prior successful result

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
  `_SharpProofInitializeVerify` and its `InvalidatePublishedResult` invocation
  (approximately lines 41-114), `_SharpProofVerifyCore` dependencies near lines
  138-140, and the post-`CoreCompile` `SharpProofVerify` target near lines
  227-232.
- Normal trigger: one build publishes a successful result, then a later supported
  build fails in `ResolveReferences` or another dependency that runs before
  `GenerateMSBuildEditorConfigFileShouldRun`/`GenerateMSBuildEditorConfigFile`.
- Expected: beginning a verifier-enabled build invalidates the previous result
  commit marker before any ordinary dependency failure can leave it appearing
  current.
- Actual impact: invalidation is hosted only in `_SharpProofInitializeVerify`,
  whose early hook is `BeforeTargets` for the editor-config targets. A preceding
  dependency failure prevents that hook from running; failed `CoreCompile` also
  prevents the post-target `SharpProofVerify` dependency chain from running.
  The prior complete public request/result/manifest set therefore remains visible
  after the later build fails. This is distinct from bug 14, which leaves a newly
  published but rejected response, and from compiler failures occurring after
  the initialization hook has already invalidated the prior result.
- Evidence confidence: High from the only invalidation call site and
  target/dependency ordering; static only.
- Suggested fix: add a dedicated earliest verifier-enabled build target that
  invalidates the stable result before reference resolution and other fallible
  precompile dependencies. Keep later compiler-output inventory initialization
  separate if those evaluated properties are not yet available at that point.
- Regression test: seed a valid public generation, then fail `ResolveReferences`
  and a custom pre-editor-config dependency separately; assert the old result
  commit marker is removed in both cases, while a later compiler failure and a
  successful rebuild retain their intended behavior.

### 50. Late output overflow can preserve verifier exit zero

- Severity: Medium (P2)
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `Execute`,
  `WaitForExitOrCancellation`, `WaitForOutputCompletion`, and
  `ReadBoundedOutputAsync` (approximately lines 178-291, 351-390, 462-500, and
  748-767).
- Normal trigger: the supervisor/verifier emits more than the bounded capture
  limit and exits zero, while process exit is observed before the asynchronous
  pipe reader records the overflow; the reader then completes before the drain
  loop tests the overflow signal.
- Expected: an output-limit breach has one stable nonzero classification
  regardless of whether process exit or pipe draining wins the scheduling race.
- Actual impact: the foreground wait returns success because the process exited,
  so `timedOut` remains false. The drain wait also returns success as soon as the
  reader task is complete, before consulting the now-set interrupt signal. The
  task logs the overflow error but finally copies the child's zero exit code.
  The exported exit property and any already-published result therefore appear
  successful even though the same task reported a bounded-output failure.
- Evidence confidence: High from the two completion-before-signal branches and
  final exit selection; static only.
- Suggested fix: latch output overflow as an independent final failure state and
  include it in exit classification after both readers are observed. Preserve
  cleanup-receipt authentication, but never allow a latched overflow to fall
  through to child exit zero.
- Regression test: gate the output reader so a zero-exited process is observed
  first, then release more than the capture limit and a valid cleanup receipt;
  assert cleanup authenticates while the exported exit remains nonzero and is
  identical to the overflow-before-exit ordering.

### 51. Cancellation after invalidation lock acquisition can preserve stale success

- Severity: Medium (P2)
- Affected code:
  `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, private `Execute`, the
  publication lease and output deletion loop (approximately lines 226-244).
- Normal trigger: MSBuild cancellation arrives after the task acquires the
  publication-set lease but before the first loop iteration deletes `ResultPath`.
- Expected: acquisition remains cancelable, but once exclusive ownership is
  obtained the minimal fail-closed invalidation commit removes the old result
  marker before cancellation can return control.
- Actual impact: the first statement in the deletion loop rechecks cancellation.
  It throws before deleting the result, the catch returns `false`, and the prior
  successful public result remains visible after the new build has entered its
  invalidation transaction. This is distinct from cancellation while waiting for
  a lock, where no publication ownership has yet been obtained.
- Evidence confidence: High from lease acquisition, output ordering (`ResultPath`
  first), cancellation check, and filtered catch; static only.
- Suggested fix: keep lock acquisition cancelable, then complete an uncancelable
  minimal commit-marker deletion under the acquired lease. Honor cancellation
  only after the result is absent, before optional cleanup such as SARIF, while
  retaining observable handling for deletion failures.
- Regression test: place a barrier immediately after lease acquisition, cancel
  the active task, and release the barrier; assert execution reports cancellation
  but the prior result is absent. Retain the existing cancel-before-acquisition
  case, which must not mutate anything.

### 52. Publication-lock retry can succeed after its timeout

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`,
  `AcquirePublicationSet` and `PublicationLock.Acquire` (approximately lines
  228-295 and 814-847).
- Normal trigger: a contended publication lock is released just after the
  requested wait expires but before the contender's first retry following its
  final timed wait.
- Expected: the supplied timeout is an absolute monotonic deadline; no lock is
  reported acquired after that deadline.
- Actual impact: `PublicationLock.Acquire` checks remaining time only after a
  failed `flock`. Once `WaitOne(delay)` consumes the last remaining interval, the
  loop retries `flock` before checking time again. A release in that window is
  accepted late, so an operation documented as timed out can instead proceed and
  mutate publication state after its caller's deadline.
- Evidence confidence: High from the retry/check ordering and monotonic elapsed
  calculation; static only.
- Suggested fix: compute one absolute deadline and check it before every retry,
  including immediately after a timed wait. Preserve a single initial
  nonblocking attempt only when the caller enters with positive remaining time.
- Regression test: hold a publication lock through the deadline, release it at a
  barrier before the next retry, and assert acquisition times out; release just
  before the deadline and assert it still succeeds.

### 53. Outer multi-target Clean probes the unprojected SARIF set

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
  `SharpProofResetPublishedVerification` and its clean SARIF projection
  (approximately lines 234-253).
- Normal trigger: a multi-target project configures
  `SharpProofVerifySarifFile`, and the unprojected base path contains a prior
  single-target/legacy file or another ordinary local file when outer `Clean`
  runs with an empty `TargetFramework`.
- Expected: the cross-target outer build leaves the unprojected base alone and
  dispatches cleanup only to the inner per-TFM publication sets.
- Actual impact: the projection condition tests nonempty `TargetFrameworks` but
  not nonempty `TargetFramework`. Combining the base directory, the empty outer
  TFM, and filename reconstructs the unprojected base and passes it to
  `ResetPublishedVerification` as part of an outer publication set. An unowned or
  incomplete base makes outer `Clean` fail before inner sets are cleaned; an
  exactly marked legacy set can be reset even though current builds publish only
  to TFM subdirectories. This is distinct from bug 23's relative-base mismatch.
- Evidence confidence: High from the empty-TFM property-function result, target
  condition, and reset's exact-marker requirements; static only.
- Suggested fix: skip this reset target in the outer cross-target build when
  `TargetFrameworks` is nonempty and `TargetFramework` is empty, leaving each
  inner build to reset its projected set. If outer enumeration is preferred,
  enumerate only evaluated inner TFMs and never synthesize an empty projection.
- Regression test: seed owned per-TFM sets and an unowned sentinel at the
  configured base, invoke outer multi-target `Clean`, and assert every projected
  set is removed while the base sentinel is untouched and cannot fail cleanup.

### 54. Manifest/result association is quadratic at supported cardinalities

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Protocol/ProtocolManifest.cs`,
  `Canonicalize`; `SharpProof.Worker.Protocol/ProtocolJson.cs`, response
  canonicalization, `ValidateManifestCore`, `ValidateClaimMembership`,
  `ValidateCallableResults`, `ValidateClaimResult`, and `ValidateRun`
  (approximately lines 203-220, 427-552, 585-619, and 850-859); and
  `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `CreateIncomplete` and
  `MatchesCallableProjection` (approximately lines 44-72 and 170-228).
- Normal trigger: an ordinary generated project produces a valid manifest and
  result with many callables and claims while remaining below the supported
  16 MiB JSON limit.
- Expected: canonicalization and association validation scale linearly or
  `O(n log n)` in the declared rows and remain within the verifier's lifecycle
  boundary for every supported cardinality.
- Actual impact: claim ordering repeatedly uses `FirstOrDefault` over all claims;
  each callable rescans the full claim set; result validation repeatedly searches
  manifest arrays; and callable projection combines per-callable membership
  scans with the full result set. Failure assembly also searches all callables
  once per claim. With no item-count bound below the byte limit, these paths are
  quadratic across valid declaration/result sets, so large supported projects
  can consume the worker/launcher hard boundary
  and end as timeout or missing/malformed result rather than verification. This
  is a correctness boundary failure, not merely an unmeasured micro-optimization.
- Evidence confidence: High from the nested full-array scans and sole 16 MiB
  aggregate input bound; static only.
- Suggested fix: build one validated immutable index by callable ID and claim ID,
  plus claims grouped by owner, then reuse it for canonicalization, assembly, and
  projection. Add cancellation checks to remaining long loops or define an
  explicit cardinality limit whose worst-case work fits the documented envelope.
- Regression test: validate and canonicalize increasing valid manifests near the
  supported cardinality boundary with instrumented association counts; assert
  near-linear lookup growth, correct output, and prompt cancellation rather than
  exhausting the enclosing wall limit.

### 55. Post-load interruption of an empty manifest produces an invalid response

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, local `Interrupted`
  and its cancellation catch (approximately lines 61-77 and 337-340), together
  with `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `Classify` and
  `TryProjectRunState` (approximately lines 99-168), and
  `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateRun` (approximately
  lines 585-619).
- Normal trigger: a valid compiler snapshot has zero callables and zero claims,
  and caller cancellation or project timeout occurs after that snapshot has
  loaded.
- Expected: the worker emits a protocol-valid `Canceled` or `TimedOut` response
  carrying exact interruption evidence even when no manifest rows exist.
- Actual impact: `Interrupted` supplies `worker.canceled`/`worker.timeout` only
  when no snapshot was loaded. With a loaded empty manifest it creates no rows
  and no errors. Projection classifies the empty evidence arrays as `Complete`,
  contradicting the declared interrupted status; launcher validation therefore
  rewrites an ordinary cancellation/timeout into `MalformedResult` and generic
  failure behavior.
- Evidence confidence: High from the conditional error argument, empty-array
  classification, exact run-projection validator, and protocol tests that require
  typed evidence for empty-manifest interruption; static only.
- Suggested fix: include the exact typed interruption error whenever a manifest
  has no rows capable of projecting the cause (or consistently include it on all
  interrupted responses), and generate status, rows, and errors through one
  projection authority.
- Regression test: load a sealed empty compiler manifest, interrupt immediately
  after the snapshot barrier by timeout and cancellation separately, and assert
  `ValidateForRequest` accepts both responses and the launcher preserves their
  typed status rather than returning malformed result.

### 56. Manifest-to-response assumption expansion can exceed the JSON limit

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `CreateIncomplete`
  (approximately lines 44-72), normal callable/claim result assembly in
  `SharpProof.Worker`, `SharpProof.Worker.Protocol/ProtocolJson.cs`,
  `MaximumJsonBytes`, `ReadUtf8File`, and `SerializeResponse` (approximately
  lines 12, 35-93), `SharpProof.Worker/Program.cs`, `WriteResponseAtomicAsync`,
  and `SharpProof.Worker.Launcher/Program.cs`, `ValidateAndReport` and
  `WriteLauncherFailureAsync` (approximately lines 334-375 and 747-762).
- Normal trigger: a valid sub-16 MiB manifest gives one or more callables many
  assumption rows and many owned claims; result assembly copies the callable's
  complete assumption array into every claim result.
- Expected: every producer enforces the same 16 MiB response envelope as the
  reader, or the schema/cardinality rules prevent a valid input from requiring an
  unrepresentable response and provide a bounded typed fallback.
- Actual impact: the assumptions-by-claims product can make a normal or failure
  response exceed 16 MiB even though its manifest was valid. Worker publication
  serializes and atomically writes the oversized JSON without a byte check, then
  the launcher reader rejects it. Malformed-result recovery calls
  `CreateIncomplete` over the same manifest, repeats the expansion, and can write
  another response that the second validation also rejects. The invocation ends
  as generic malformed failure after producing large unusable staging output.
- Evidence confidence: High from per-claim array copying, aggregate-only manifest
  limit, unbounded response writer, bounded reader, and recovery reuse; static
  only.
- Suggested fix: make serialized response size a producer-side invariant before
  commit and define a bounded valid failure representation that does not copy
  manifest assumptions per claim. Prefer referencing manifest declarations or
  otherwise cap the assumptions-by-claims product during request validation.
- Regression test: construct a valid manifest just below 16 MiB whose assembled
  response would exceed the limit; exercise success and recovery assembly and
  assert neither commits oversized JSON and the launcher receives a readable,
  protocol-valid typed failure.

### 57. Cache misses bypass a lowered capacity limit

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/VerificationCache.cs`, `TryReadAsync`,
  `TryWriteAsync`, and `TryStageCapacity` (approximately lines 12-209 and
  239-286), plus `SharpProof.Worker/SharpProofWorker.cs`, cache read/write flow
  (approximately lines 178-206 and 324-336).
- Normal trigger: active cache entries were created under a larger maximum, a
  later supported request lowers `SharpProofVerifyCacheMaximumBytes`, its key is
  a miss, and verification returns a noncacheable result so no write follows.
- Expected: every enabled cache operation enforces the request's current maximum,
  evicting owned active entries until total active bytes are at or below it.
- Actual impact: a missing key throws from `FileInfo.Length` and is caught as a
  cache miss before `TryStageCapacity`; malformed/mismatched entries also return
  before that call. Capacity staging otherwise runs only on a valid hit or write.
  When the resulting verification is noncacheable, no write occurs, so recognized
  active entries can remain indefinitely above the documented maximum. This is
  distinct from bug 32's unaccounted transaction-suffix debris.
- Evidence confidence: High from every early-return path, the write eligibility
  guard, and the documented maximum-cache-size contract; static only.
- Suggested fix: after acquiring the cache lock, recover transactions and enforce
  active capacity before key-specific hit logic, including misses. Centralize
  that maintenance so a noncacheable semantic result cannot bypass it and keep
  cache failures nonsemantic.
- Regression test: seed multiple active entries under a large maximum, lower the
  maximum below their total, then issue a different-key miss whose result is not
  cacheable; assert oldest owned entries are evicted and total active bytes obey
  the new limit.

### 58. Reset can remove companions before the result commit marker

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`,
  `ResetPublicationSet` and `CanonicalPublicationPaths` (approximately lines
  174-225 and 365-371), plus
  `SharpProof.BuildTasks/ResetPublishedVerification.cs`, `Execute` and `Present`
  (approximately lines 19-38).
- Normal trigger: `Clean` resets a complete owned publication and cancellation or
  an ordinary delete failure occurs after a lexically earlier manifest, request,
  or SARIF member is removed but before the result path is reached. The default
  manifest/request names both sort before `result.json`.
- Expected: the stable result is removed first, as required by the publication
  commit contract, so an interrupted reset can never leave success visible for a
  set whose companions are already missing.
- Actual impact: `CanonicalPublicationPaths` sorts by ordinal path for locking and
  that same role-free array drives deletion. Each iteration checks cancellation
  and can throw from type checking or `File.Delete`. A prefix of companions can
  therefore disappear while the result commit marker and all ownership markers
  remain, exposing a visibly committed but incomplete generation after failed
  cleanup. This is distinct from bug 6's partial ownership-marker transaction,
  bug 14's rejected validation, bug 44's publication rollback, and bug 51's
  pre-first-delete invalidation window.
- Evidence confidence: High from ordinal canonicalization, deletion ordering,
  failure boundaries, default filenames, and the documented result-first reset
  invariant; static only.
- Suggested fix: separate deadlock-safe lock ordering from mutation ordering and
  make the reset API identify the result commit member explicitly. After lease
  and marker validation, remove the result first without a cancellation point;
  only then make companion cleanup cancelable. If result deletion fails, no
  companion should have been touched.
- Regression test: use default and deliberately reordered member names, inject
  cancellation and delete/type failures at every member boundary, and assert the
  result is either removed first or the complete prior set remains unchanged.

### 59. The worker outer deadline starts after its startup gate

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxWorkerProcess.cs`, `Start` and
  `WaitForExit` (approximately lines 41-119), and
  `SharpProof.Worker.Launcher/Program.cs`, `RunWorker` (approximately lines
  214-236).
- Normal trigger: the launcher thread is descheduled or otherwise delayed long
  enough to consume material deadline time after `Start` writes and closes
  `SharpProof.Start/1` but before `RunWorker` calls `WaitForExit`; the released
  worker proceeds during that delay.
- Expected: `terminationStart` and `finalLimit` are measured from the instant the
  authenticated startup gate releases the worker.
- Actual impact: `Start` releases the child and returns without recording a
  timestamp, while `WaitForExit` creates a fresh stopwatch only on entry. All
  post-gate child work during the caller gap is uncharged. A worker that exits in
  the gap is returned as normally exited without any elapsed check; one still
  running receives the full outer allowance again, so launcher containment can
  exceed both configured limits. This is distinct from bug 21's polling error,
  bug 39's short-grace arithmetic, and bug 48's internal project-timer start.
- Evidence confidence: High from gate-write, return, caller, and stopwatch
  ordering; static only.
- Suggested fix: record a monotonic start timestamp immediately before releasing
  the gate and retain it in `LinuxWorkerProcess`, then compute absolute
  termination/final deadlines from that timestamp in `WaitForExit`. Do not let a
  caller scheduling gap reset either budget.
- Regression test: pause the launcher immediately after the gate write until
  beyond the termination threshold, then enter `WaitForExit`; assert elapsed time
  is charged from gate release and the worker cannot receive a fresh allowance
  or be reported as an on-time exit.

### 60. Per-claim launcher logging can overflow task output before publication

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker.Launcher/Program.cs`, `RunMain` and
  `ValidateAndReport` (approximately lines 138-160 and 334-437), together with
  `SharpProof.BuildTasks/RunVerifier.cs`,
  `MaximumCapturedOutputCharacters`, `Execute`, and `ReadBoundedOutputAsync`
  (approximately lines 18, 178-291, and 458-500).
- Normal trigger: a valid response remains below the supported 16 MiB JSON limit
  but contains enough ordinary claim rows that one console line per claim exceeds
  the build task's 1,048,576-character capture limit.
- Expected: human diagnostics are bounded independently of result cardinality;
  the already validated typed result reaches publication and its normal exit
  projection.
- Actual impact: `ValidateAndReport` writes every claim before its summary/status
  decision and returns before `RunMain` calls `PublishOutputs`. The build task's
  reader latches overflow and interrupts the supervisor while the launcher is
  still reporting, so the valid result can remain private and the invocation is
  converted to timeout/containment behavior instead of its semantic exit. This
  is distinct from bug 50's final exit race after overflow, bug 54's quadratic
  association work, and bug 56's serialized-response size expansion.
- Evidence confidence: High from the unbounded claim loop, 16 MiB input versus
  1 MiB output bounds, publication ordering, and output-limit interrupt path;
  static only.
- Suggested fix: give launcher presentation a fixed aggregate character budget
  safely below the parent cap, emit a bounded sample plus counts, and leave full
  per-claim detail in the result/SARIF artifacts. Reserve space for summary and
  typed failure diagnostics regardless of claim count.
- Regression test: supply a protocol-valid sub-16 MiB response whose claim lines
  would exceed the parent cap; assert launcher output remains bounded, the public
  result is committed, and the original typed exit/status is preserved.

### 61. Claimless callable interruption fails callable projection

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, local `Interrupted`
  (approximately lines 61-77 and 337-340);
  `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `CreateIncomplete` and
  `MatchesCallableProjection` (approximately lines 44-72 and 170-228); and
  `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateRun` (approximately
  lines 585-619).
- Normal trigger: a valid manifest contains at least one selected callable with
  an empty `ClaimIds` set, and project timeout or caller cancellation occurs after
  the compiler snapshot has loaded.
- Expected: the claimless callable can carry the run-wide incomplete
  `ProjectTimeout`/`Canceled` reason and the complete response remains
  protocol-valid.
- Actual impact: `CreateIncomplete` emits an incomplete callable row with the
  interruption reason. That row is enough for run projection to derive the
  correct `TimedOut`/`Canceled` status, but `MatchesCallableProjection` treats
  every callable with zero owned claim results as necessarily `Complete/None`.
  Full validation rejects the worker's own response with
  `response.callable_projection`, and the launcher reports malformed result.
  This is distinct from bug 55, where a manifest with no callable rows makes the
  run-status projection itself default to `Complete`.
- Evidence confidence: High from assembler/projection branches, protocol tests
  that accept claimless callables, and the production manifest shape that permits
  selected callables without claims; static only.
- Suggested fix: represent global interruption explicitly in the projection
  authority and allow its exact reason on claimless callable rows, while retaining
  `Complete/None` for ordinary claimless completion. Do not infer that every
  claimless callable timed out merely because another callable had a method-level
  timeout.
- Regression test: interrupt a claimless manifest and a mixed
  claimless/claim-bearing manifest after snapshot load for both timeout and
  cancellation; require `ValidateForRequest` success, while a local method timeout
  in another callable leaves an unaffected claimless callable complete.

### 62. Cache commit and interruption can both win one invocation

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/VerificationCache.cs`, `TryWriteAsync`
  (approximately lines 123-209), and
  `SharpProof.Worker/SharpProofWorker.cs`, cache write, assembly, and final
  cancellation checks (approximately lines 324-340).
- Normal trigger: a cacheable miss finishes verification, then caller
  cancellation or the project deadline fires after `TryWriteAsync`'s final token
  check/commit but before the worker's post-write checks and final response
  assembly complete.
- Expected: cancellation and cache commit have one atomic winner: an interrupted
  invocation rolls back the new entry, or a committed entry is paired with the
  completed `Written` response that won the race.
- Actual impact: `TryWriteAsync` sets `committed = true`, discards rollback state,
  and returns a durable entry after its last token check. The caller then checks
  the same token multiple times; a cancellation in between throws and is
  converted to `Interrupted(snapshot)`. The invocation reports
  `Canceled`/`TimedOut` while its newly committed entry remains and can be a hit
  for the next invocation, violating the existing invariant that canceled cache
  publication cannot become a later hit.
- Evidence confidence: High from commit/rollback ordering, caller token checks,
  interruption catch, and the explicit cache cancellation test contract; static
  only.
- Suggested fix: use a two-phase cache transaction whose commit is finalized only
  after the response outcome is selected, or coordinate cancellation and commit
  through one atomic winner state. If cancellation wins, roll back under the
  cache lock; if commit wins, return the completed response without later
  reclassifying that same operation as interrupted.
- Regression test: add barriers after the last precommit token check, after
  `committed = true`, and before caller assembly/final checks; trigger cancellation
  and timeout at each barrier and assert the impossible combination
  `Interrupted` plus a reusable new entry never occurs.

### 63. Nested output and readiness waits do not share an absolute deadline

- Severity: Medium (P2)
- Affected code: `SharpProof.BuildTasks/RunVerifier.cs`, `Execute`,
  `WaitForOutputCompletion`, `TryTerminate`, and
  `WaitForSupervisorReadiness` (approximately lines 116-240, 351-433, and
  825-868).
- Normal trigger: process output drains or the authenticated supervisor `Armed`
  record becomes observable close to the enclosing process/termination
  deadline, with ordinary scheduling delay between the caller's remaining-time
  calculation and the helper's first observation.
- Expected: every nested wait uses the caller's one monotonic absolute deadline
  and applies one deterministic rule to completion versus deadline expiry.
- Actual impact: callers pass a relative remainder, but both helpers start a new
  stopwatch and grant that full duration again, so call and scheduling time
  after the outer sample is uncharged. `WaitForOutputCompletion` tests and even
  resamples completion before classifying expiry, allowing output that completes
  after the real outer deadline to preserve a normal child exit such as zero.
  `WaitForSupervisorReadiness` also tests `armed` before expiry on the next loop,
  accepting a late record, yet returns `NotReady` without a final resample when
  expiry is observed, so a record completing between those observations can be
  rejected. Boundary scheduling can therefore change timeout, readiness,
  authentication, and containment classification. This is distinct from bug
  11's setup reserve consumption, bug 21's worker polling, bug 50's output-limit
  signal race, and bug 59's post-gate launcher gap.
- Evidence confidence: High from the outer remainder calculations, fresh helper
  stopwatches, and opposite completion/expiry observation orders; static only.
- Suggested fix: compute absolute monotonic process and termination deadlines
  once and pass those timestamps through every helper. Centralize an explicit
  boundary arbitration rule and never infer timeliness from task state sampled
  after the deadline.
- Regression test: use a controllable monotonic clock and wait barrier to make
  output completion and `Armed` occur immediately before, exactly at, and after
  the absolute deadline. Assert stable timeout/readiness results and ensure a
  late drain cannot return the child's zero exit.

### 64. Topology validation can preserve a prior successful commit

- Severity: Medium (P2)
- Affected code:
  `SharpProof.BuildTasks/InvalidatePublishedResult.cs`, private `Execute`, the
  alias/topology checks and `Log.HasLoggedErrors` return before the publication
  lease (approximately lines 114-236).
- Normal trigger: a prior owned publication is complete and its result path is
  safe, but the next enabled build discovers a conflict confined to another
  member or relationship, such as the existing request path becoming a
  compiler-owned output after a normal output-configuration change, or a cache
  path conflicting with a non-result input.
- Expected: when the old set can still be authenticated and its result is safe
  to mutate, pre-verification invalidation removes that stable commit before
  reporting the new topology error.
- Actual impact: all output, input, worker-tree, cache, and compiler-output
  conflicts are logged before any lease is acquired. A single non-result
  conflict makes `Log.HasLoggedErrors` return `false` immediately, so the old
  result is never deleted even though this build has entered the invalidation
  task and cannot produce current evidence. The failed build can therefore
  leave the prior success looking current. This is distinct from bug 49's
  dependency failure before invalidation runs and bug 51's cancellation after
  lease acquisition.
- Evidence confidence: High from the aggregate preflight, unconditional early
  return, and later result-first deletion path; static only.
- Suggested fix: separate safe invalidation of an already-owned result from
  validation of paths that will be used by the new invocation. When the
  existing set and result identity can be authenticated without binding new
  markers, acquire its lease and remove the result commit first; then report
  unrelated topology errors. Preserve fail-closed behavior when the result
  itself or its ownership cannot be authenticated safely.
- Regression test: seed a complete owned set, then introduce each conflict class
  on a non-result member while leaving the result safe. Assert task failure and
  diagnostics but no public result; separately verify that an unsafe result
  conflict does not mutate the protected target.

### 65. Backend construction failure can override an earlier interruption

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, `VerifyAsync` around
  the `TryCreateLanes` failure branch (approximately lines 207-212), and
  `TryCreateLanes` (approximately lines 421-463).
- Normal trigger: a cache miss with at least one target begins synchronous
  backend/lane construction; caller cancellation or the project deadline fires,
  then the backend factory ordinarily throws, returns null, or produces a
  duplicate instance before construction returns.
- Expected: interruption and backend failure have one stable first winner. If
  cancellation or timeout fired first, the response is `Canceled` or `TimedOut`;
  only a backend failure that won first is `BackendUnavailable`.
- Actual impact: `TryCreateLanes` accepts no token and converts ordinary factory
  exceptions into `false`. Its caller immediately returns
  `BackendUnavailable` without rechecking `projectBoundary` or the caller token.
  An interruption that already fired can therefore be erased by the later
  construction failure, changing run status, claim reasons, diagnostics, and
  launcher exit behavior. This is distinct from bug 35, where two interruption
  sources are both observed but their ordering is lost during classification.
- Evidence confidence: High from the tokenless synchronous construction loop,
  broad ordinary-exception conversion, and direct failure return; static only.
- Suggested fix: coordinate backend failure with the same atomic first-cause
  state used for cancellation and project timeout. Check that state before each
  factory call and atomically record either a caught construction failure or an
  interruption immediately afterward; dispose partial lanes and classify from
  the winning cause.
- Regression test: gate a backend factory, fire caller cancellation and project
  timeout separately, then release it to throw; require the prior interruption
  classification. Reverse the order with a second barrier and require
  `BackendUnavailable`, with all partially created lanes disposed in both cases.

### 66. Cache read failures are reported as misses for noncacheable results

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/VerificationCache.cs`, `TryReadAsync`
  (approximately lines 12-120), and
  `SharpProof.Worker/SharpProofWorker.cs`, `CreateCacheIfEnabled` and the cache
  read/write flow (approximately lines 178-206, 324-336, and 354-375).
- Normal trigger: cache use is enabled but an ordinary lock, path-validation, or
  read/I/O failure prevents lookup, and verification then produces a
  noncacheable response such as a proven or unknown result, so no write attempt
  follows.
- Expected: an absent key is `Miss`, while inability to determine whether an
  entry exists is `Unavailable`, independently of whether the semantic response
  is eligible for a later write.
- Actual impact: cache creation initializes status to `Miss`, and
  `TryReadAsync` returns the same `null` for a true miss, invalid entry, lock
  contention, path failure, and caught I/O failure. Only a subsequent write can
  replace that status with `Written` or `Unavailable`. A noncacheable result
  skips the write and therefore publishes the false `Miss`; protocol validation
  explicitly accepts that status for the non-storable shape, so the inaccurate
  operational evidence survives end to end.
- Evidence confidence: High from the default status, collapsed return type,
  catch filter, write-eligibility guard, and cache-status validator; static only.
- Suggested fix: return a discriminated lookup outcome such as `Hit`, `Miss`,
  `Rejected`, and `Unavailable`, and set summary status immediately from it.
  Keep write eligibility and write failure as later independent transitions.
- Regression test: force lock contention, path rejection, and read failure for a
  request whose semantic response is noncacheable and assert `Unavailable`;
  compare with an actually absent key, which must remain `Miss`.

### 67. Incomplete responses discard an already-established cache state

- Severity: Medium (P2)
- Affected code: `SharpProof.Worker/SharpProofWorker.cs`, local `Interrupted`,
  the cache flow, the late caller-cancellation branch, and the final
  cancellation catch (approximately lines 61-77, 160-212, and 284-340);
  `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, `CreateIncomplete`
  (approximately lines 44-72); and
  `SharpProof.Worker.Protocol/ProtocolJson.cs`, `ValidateCacheForRequest`
  (approximately lines 342-390).
- Normal trigger: cache use is enabled and the worker has established a cache
  miss, then caller cancellation or the project deadline reaches an
  exception-based interruption path, for example while reading the cache or
  after project timeout terminates proof work. The same loss can follow a cache
  operation classified `Unavailable` if interruption arrives during subsequent
  response assembly.
- Expected: `Disabled` means cache processing was inactive or had not begun;
  once an enabled invocation has established `Miss` or `Unavailable`, its
  incomplete response preserves that operational state regardless of which
  interruption checkpoint observes cancellation.
- Actual impact: local `Interrupted` cannot accept the current cache state, and
  `CreateIncomplete` unconditionally supplies `WorkerCacheStatus.Disabled`.
  Thus a cache-enabled interrupted invocation reports that cache processing was
  disabled even after a lookup or failure was observed. The sibling
  post-lane caller-cancellation branch explicitly calls `Canceled(cacheStatus)`
  and preserves the state, so identical cancellation at different checkpoints
  produces contradictory summaries. Request-bound validation allows
  `Disabled` on any non-complete active-cache response, letting the inaccurate
  evidence survive worker validation and launcher publication. This is
  distinct from bug 66's conflation of read failure with miss, bug 35's choice
  of interruption cause, bug 42's ignored interruption on a cache hit, bug 62's
  cache-commit race, and bug 65's backend-failure precedence.
- Evidence confidence: High from the hard-coded assembler argument, the
  state-preserving sibling cancellation branch, reachable project-timeout and
  cache-read cancellation paths, and the permissive request-bound validator;
  static only.
- Suggested fix: initialize and retain one cache-status state before cache
  setup, update it at each lookup/write outcome, and pass it explicitly through
  `Interrupted` and `CreateIncomplete`. Reserve `Disabled` for inactive policy
  or interruption before cache engagement, while preserving early launcher and
  input-failure responses that legitimately never reached cache setup.
- Regression test: with cache enabled, gate a true miss and interrupt during
  cache read and proof work by caller cancellation and project timeout; assert
  `Miss` in every incomplete response. Repeat with an established unavailable
  cache operation and assert `Unavailable`, while pre-cache interruption remains
  `Disabled`.

### 68. MSBuild argument items trim terminal whitespace from configured paths

- Severity: Medium (P2)
- Affected code:
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`, configured
  path initialization, `_SharpProofVerifierArgument` construction, validation,
  and clean wiring (approximately lines 45-55, 99-117, 156-171, 192-217, and
  238-253); `SharpProof.BuildTasks/RunVerifier.cs`, `Arguments` and `ItemSpec`
  forwarding (approximately lines 55-61 and 147-150); and
  `SharpProof.Worker.Launcher/Program.cs` plus
  `LauncherArguments.generated.cs`, argument parsing, canonicalization, cache
  setup, and publication.
- Normal trigger: a public configured request, result, compiler-manifest, SARIF,
  or cache path has a legal Linux basename ending in a space or tab. The value
  can come from a project/global property or property function and is otherwise
  accepted by the local-path contract.
- Expected: every configured path is opaque data and retains its exact filename
  through invalidation, launcher arguments, publication, validation, cache use,
  and `Clean`.
- Actual impact: scalar properties and task parameters retain the terminal
  character, but each launcher path is expanded into an MSBuild item
  `Include`. `[MSBuild]::Escape` protects MSBuild special characters, not
  terminal whitespace from item-spec tokenization, so the resulting
  `_SharpProofVerifierArgument` identity is trimmed. `RunVerifier` then forwards
  that changed `ItemSpec` verbatim and the launcher consistently canonicalizes
  the wrong sibling path. Meanwhile invalidation, result validation, and clean
  still receive the exact scalar value. A single changed publication member can
  be rejected as a partial-overlap set after invalidation; changing all members
  can publish a coherent set at trimmed sibling paths that exact-path validation
  rejects and exact-path `Clean` does not remove. A cache path silently selects
  the trimmed sibling directory. This is distinct from bug 23's relative base,
  bug 38's quoted-expression parsing, bug 40's PATH-field trimming, bug 43's
  semicolon list splitting, and bug 53's outer multi-target SARIF projection.
- Evidence confidence: High from the item-versus-scalar target wiring, MSBuild
  item tokenization, `ItemSpec` forwarding, and the launcher's absence of any
  compensating path transform; static only.
- Suggested fix: carry each path through a channel that preserves opaque scalar
  data, such as dedicated task parameters or item metadata, or explicitly
  encode terminal whitespace before item construction and verify that MSBuild
  decodes it only when producing the final argument. Canonicalize once and pass
  the same exact value to every build, launch, validation, and clean phase.
- Regression test: configure every public path separately with basenames ending
  in a space and a tab, place sentinels at the corresponding trimmed siblings,
  and assert launcher arguments, publication, validation, cache use, and clean
  operate only on the exact configured paths. Include both a single-member
  publication change and a fully disjoint all-member set.

### 69. A carriage return in a mountpoint hides the visible filesystem record

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `RequireLocalPath`,
  `FindFileSystemType`, and `DecodeMountPath` (approximately lines 111-121 and
  730-785).
- Normal trigger: a supported local request, result, manifest, cache, or SARIF
  path is below a Linux mountpoint whose legal directory name contains a literal
  carriage return, and the mounted filesystem type differs from the nearest
  ancestor mount.
- Expected: mount metadata parsing preserves every legal pathname character and
  classifies the uniquely longest, visible mount that contains the canonical
  path.
- Actual impact: Linux mountinfo escapes space, tab, line feed, and backslash in
  pathname fields, as reflected by `DecodeMountPath`, but leaves carriage return
  literal. `File.ReadLines` uses text-line framing that treats that carriage
  return as a record terminator. The exact mount record is therefore split into
  structurally unusable fragments and skipped, so `FindFileSystemType` selects
  an ancestor instead. A local mount over a recognized remote ancestor is
  rejected, while a recognized remote mount over a local ancestor is wrongly
  accepted for local-only publication. This is distinct from bug 34, where the
  parser retains multiple equal-length records but chooses the wrong one.
- Evidence confidence: High from the mountinfo escape contract, the decoder's
  matching escape table, `File.ReadLines` carriage-return framing, the parser's
  skip predicates, and longest-remaining-prefix selection; static only.
- Suggested fix: avoid generic text-line framing for mountinfo. Parse bytes or
  characters using only line feed as the kernel record delimiter while
  preserving literal carriage returns in fields, then apply mount-field
  decoding. A direct `statfs`/`fstatfs` query on the path or nearest existing
  ancestor would also avoid this parser root and bug 34's stack-order root.
- Regression test: factor the mountinfo selector behind a fixtureable parser and
  provide records whose uniquely longest mountpoint contains a literal carriage
  return, once for local-over-remote and once for remote-over-local. Assert that
  both select the child record's type, while the existing escaped space, tab,
  line-feed, and backslash cases remain intact.

### 70. A hidden descendant mount can override its visible ancestor's type

- Severity: Medium (P2)
- Affected code: `SharpProof.Host/LinuxPathIdentity.cs`, `RequireLocalPath` and
  `FindFileSystemType` (approximately lines 111-121 and 730-764).
- Normal trigger: a filesystem is mounted at a child path such as
  `/workspace/tree`, then a different filesystem is mounted over its ancestor
  `/workspace`. The earlier child remains represented in the mount namespace
  but is hidden by the later ancestor overmount, and a publication or cache path
  is lexically below the old child path.
- Expected: local-only path validation classifies the filesystem currently
  visible at the canonical path, or at its nearest existing ancestor when the
  output does not yet exist.
- Actual impact: mountinfo exposes mount IDs and parent IDs needed to distinguish
  the covered child from the visible ancestor, but `FindFileSystemType` ignores
  both and selects only the longest decoded mountpoint string. The hidden child
  therefore beats the shorter visible overmount. If their types straddle the
  recognized remote-filesystem set, a valid local output is rejected or an
  unsupported remote output is accepted. This is distinct from bug 34's
  equal-mountpoint ordering and bug 69's carriage-return record splitting: the
  incorrect record here is unique, longer, and parsed successfully.
- Evidence confidence: High from Linux mount-tree/overmount semantics, the
  retained mount and parent identifiers, and the unconditional lexical
  longest-prefix selection; static only.
- Suggested fix: classify the target or nearest existing ancestor with
  `statfs`/`fstatfs` so the kernel resolves the visible mount. If mountinfo must
  remain the source, model mount IDs, parent relationships, and covering stacks
  before performing path selection rather than treating every listed
  mountpoint as simultaneously visible.
- Regression test: in fixtureable mount metadata or an isolated mount namespace,
  create a child mount and then overmount its ancestor. Test local-over-remote
  and remote-over-local type permutations and assert that classification follows
  the later visible ancestor, not the uniquely longer hidden child. Retain the
  equal-path and carriage-return cases for bugs 34 and 69.

## Round 17 triage ledger

- No new P0, P1, or P2 root was accepted, and no supplied report was grouped
  into an existing root.
- `P20-R17-C1` is rejected. On ordinary publication-lock timeout,
  `PublicationLock.Acquire` returns false and `AcquirePublicationSet` converts
  that result to `IOException`. The `RunMain` publication boundary catches
  `IOException`, reports that the worker result could not be published, and
  returns exit 3. Its omission of `InvalidOperationException` therefore does not
  let the alleged timeout escape; bug 52 remains the distinct late-success
  timeout defect.
- Groups A, B, C, and E reported no candidates, as did the remaining Group D
  partitions. All 30 fixed partitions produced no new reachable P0-P2 root.

## Round 16 triage ledger

- Accepted `P12-C1` as one new Medium/P2 root (bug 70). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P30-C1` is rejected. `AtomicFile` stages beside the destination and
  `File.Replace`/`File.Move` is the final fallible publication action. After a
  successful rename the temporary path is absent, so the `finally` block has no
  deletion to perform; there is no directory sync, validation, or other
  post-commit operation that can normally fail and report an error for an
  already-published file. Failed-operation cleanup masking remains bug 10, not a
  successful-publication ambiguity.
- Groups A, C, and D reported no candidates, as did the remaining Group B and E
  partitions. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 15 triage ledger

- Accepted `P12-C1` as one new Medium/P2 root (bug 69). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P17-C1` remains rejected. The transitive targets are evaluated after the
  project body, recompute `_SharpProofToolsDirectory` and all derived runtime
  paths, and require them to equal the package-owned closure before
  publication. The repository also has explicit project-body override coverage;
  public runtime-closure overrides are intentionally unsupported rather than a
  stale derived-property defect.
- Groups A, D, and E reported no candidates, as did the remaining Group B and C
  partitions. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 14 triage ledger

- Accepted `P19-C1` as one new Medium/P2 root (bug 68). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P02-C1` is rejected. `WriteCleanupReceipt` runs after descendant cleanup and
  emits an unconditional separator newline before the exact authenticated
  receipt line, then flushes. The bounded reader authenticates only a complete
  exact line, and the repository's unterminated-verifier-output contract test
  exercises this framing, so a normal final output fragment cannot concatenate
  with the cleanup record.
- Groups B, C, and E reported no candidates, as did the remaining Group A and D
  partitions. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 13 triage ledger

- Accepted `P28-C1` as one new Medium/P2 root (bug 67). No P0 or P1 root was
  accepted, and no supplied report was grouped into an existing root.
- `P29-R13-C1` remains rejected. Before launching the worker, the launcher
  successfully deserializes and invokes the same deterministic
  `DecodeCallables` implementation on the canonical compiler artifact. The
  worker then binds its read to the launcher's SHA-256 and repeats canonical
  deserialization before decoding; any ordinary byte change is rejected at that
  earlier boundary. No supported writer or state change can therefore make the
  second decode newly fail and overwrite an interruption.
- `P01` through `P24` and the remaining Group E partitions reported no
  candidate. In total, 29 of the 30 fixed partitions produced no new reachable
  P0-P2 root.

## Round 12 triage ledger

- Accepted as four new Medium/P2 roots: the grouped `P01`/`P02` absolute-deadline
  arbitration root, `P06`, `P29-C2`, and `P30-C1` (bugs 63 through 66). No new
  P0 or P1 root was accepted.
- Grouped the unsupported-host build-invalidation facet into bug 24 and the
  tokenless cache-capacity scan into bug 28; `P01` and `P02` are one shared
  deadline/arbitration root rather than duplicate entries.
- `P17` remains rejected because target evaluation recomputes and validates the
  exact package-owned runtime closure. `P29-C1` does not establish a separate
  post-cancellation dispatch failure beyond the checked/caught interruption
  paths. The remaining fixed partitions produced no new reachable P0-P2 root;
  in total, 25 of the 30 partitions added no new root.

## Round 11 triage ledger

- Accepted as five new Medium/P2 roots: `P11`, `P13`, `P21`, `P28`, and `P29`
  (bugs 58 through 62). No new P0 or P1 root was accepted.
- No supplied Round 11 report was grouped into an existing root; each accepted
  control-flow failure has a distinct commit, timing, output, projection, or
  cache-transaction boundary.
- The remaining 25 fixed partitions produced no new reachable P0-P2 root. The
  repeated `P24` private-helper report remains rejected because partial class
  declarations expose those helpers to their generated wrappers; all other
  no-new partitions reported no candidate.

## Round 10 triage ledger

- Accepted as eight new Medium/P2 roots: `P01`, `P06`, `P12-C1`, `P19`, the
  grouped `P25`/`P28` association root, `P29-C1`, `P29-C2`, and `P30` (bugs 50
  through 57). No new P0 or P1 root was accepted.
- Grouped two supplied facets rather than adding roots: `P12-C2` broadens bug 18
  with absent case-fold aliases, and `P28` shares bug 54's missing association
  indexes with `P25`.
- The remaining 22 fixed partitions produced no new reachable P0-P2 root.
  `P24` was explicitly rejected because the relevant partial test wrappers do
  expose the private helpers on which that report's inaccessibility premise
  depended; the other reported partitions had zero new candidate.

## Round 9 triage ledger

- Accepted as one new root: `P18-C1` as Medium/P2 (bug 49).
- Grouped two supported facets into existing roots: marker-owned non-regular
  members during launcher publication broaden bug 33, and the post-exit-124
  result deletion broadens bug 25. Neither is a separate root.
- Rejected or no-new outcomes: `P03` is the intentional legacy rightmost-marker
  grammar and current producers use the structured diagnostic transport;
  `P07` through `P12` produced no other root; `P17` cannot override the exact
  package runtime closure; `P18-C2` is blocked by existing-destination ownership,
  worker-tree topology checks, and required native-Z3 preexistence; and the final
  triage cohort reported no candidate.

## Round 8 triage ledger

- Accepted as new roots: `P02` and `P18` as High/P1 (bugs 45 and 46), and
  `P24` and `P29` as Medium/P2 (bugs 47 and 48). The `PdbFile` and
  `_DebugSymbolsIntermediatePath` omissions are two configuration facets of the
  single compiler-inventory root in bug 46.
- No new root was accepted from `P07` through `P12`, `P19`, or `P28` (eight
  rejected/no-new partitions). `P07` through `P12` reported no reachable new
  issue; partial-overlap publication sets in `P19` are explicitly unsupported;
  and the production SMT model extractor in `P28` emits only Boolean and integer
  values, returning malformed-result for other types rather than producing the
  proposed blank string.
- Bug 45 narrows two prior rejections: arbitrary faulted retained output is not
  dereferenced, and concurrent disposal remains unsupported, but the concrete
  task-owned signal fault arises after supported sequential post-`Execute`
  disposal and is converted into failed cleanup authentication.

## Round 7 triage ledger

- Accepted as new roots: `P18` as High/P1 (bug 43) and `P22` as Medium/P2
  (bug 44). The raw `MakeDir` and compiler-output `Include` reports are two
  grouped facets of bug 43, not additional roots.
- No new root was accepted from `P03`, `P07` through `P12`, `P28`, or `P29`
  (nine rejected/no-new partitions). `P03` uses a fixed outer reserve and retains
  cleanup ownership intentionally; `P07` through `P12` reported no reachable new
  issue; `P28` has no production producer beyond malformed input; and supported
  `P29` timeout/cancellation is contained by private-result deletion and the
  launcher/parent-death boundary.
- The accepted round-7 `P18` is a newly supplied MSBuild list-expansion candidate;
  it is not one of the earlier audit reports that happened to use labels in the
  `P13`-through-`P18` range.

## Round 6 triage ledger

- Accepted as new P2 roots: `P03` (bug 40), `P20` (bug 41), and `P29`
  (bug 42).
- The other 27 fixed round-6 partitions, including that round's reports labeled
  `P07` through `P18`, produced no new reachable P0-P2 root after unsupported,
  edge-only, and duplicate reports were discarded. No existing issue required a
  round-6 facet amendment.

## Rejected or not substantiated

- External SIGINT-to-143 supervisor behavior is not a supported invocation path;
  supported cancellation is the authenticated parent/task control flow already
  covered where defective.
- Blank reset-path variants remain rejected: stock defaults and exact ownership
  markers prevent the claimed unowned partial deletion.
- Expanding the remote-filesystem blacklist is hardening/policy work, not a new
  ordinary correctness failure on the supported local publication path.
- Relative `SharpProofToolsDirectory` remains unsupported and does not add a new
  root beyond the configured-path findings already recorded.
- Direct `SharpProofVerify` invocation is skipped by its public condition unless
  an internal state property is forcibly overridden; that forced state is not a
  supported trigger.
- A compiler failure after `_SharpProofInitializeVerify` has run does not leave
  the claimed stale success because initialization already invalidated the prior
  result. Bug 49 records the distinct earlier `ResolveReferences` and other
  pre-editor-config failures for which that hook never runs.
- The runtime-snapshot alias premise is false because `CreateRequest` revalidates
  the resolved snapshot/path set before worker launch.
- All other round-5 partitions reported no in-scope candidate.
- A stale `Interrupted` state premise was false: the cited state is recomputed on
  each classification path rather than retained from the earlier observation.
- Blank invalidation paths do not establish deletion of an owned publication;
  stock paths are supplied by the targets and blank/unowned values fail before
  the claimed partial cleanup.
- Publication marker aliasing is already rejected by
  `ValidatePublicationMetadataAliases`; no distinct supported alias path was
  found.
- Symlink/`..` spellings and bind aliases were not accepted: the former violate
  the explicit lexical/canonical path contract, and the latter are rejected by
  supported publication topology/identity checks.
- Concurrent creation of an unrelated object at a publication destination is an
  unsupported replacement scenario, not a new ordinary cooperative-build root.
- The earlier audit reports labeled `P13` through `P18` did not establish any
  reachable in-scope defect and are retained as no-findings rather than register
  entries; this statement applies to those report instances, not later label
  reuse.
- `Exited(124)` together with a committed worker result is not produced by the
  production launcher/worker control flow, so the proposed projection mismatch
  is unreachable.
- `P27` concerns proof semantics outside this verifier process/publication scope.
- A worker parent that never sends the startup line violates the supported start
  gate contract; the existing hard boundary handles the ordinary supported path.
- Compiler-manifest snapshot loading intentionally uses `CancellationToken.None`
  so timeout/cancellation results remain manifest-accountable; the launcher hard
  limit is the enclosing boundary.
- One initial prohibited-path report was invalidated; its authoritative-worktree
  rerun found no candidate, so no finding from that result is included here.
- Generic faulted retained stdout is not a separate issue because retention
  consumes output only when `IsCompletedSuccessfully`; a faulted capture is not
  dereferenced as successful. Bug 45 records the distinct supported source and
  consequence: sequential task disposal faults the retained reader, which is
  then treated as missing authenticated cleanup.
- Final zombie observation: not accepted as a separate correctness failure. It
  causes the cleanup owner to retain/retry rather than authenticate false
  completion or abandon the process boundary.
- `DeleteIfUnprotected` destination replacement, raw PID reuse, and the native
  resolver timing window were not reopened; they are unsupported replacement or
  hardening scenarios outside this ordinary supported-path scope.
- Relative custom publication paths (`P19`) are not a new root; their build/clean
  base mismatch is now included in bug 23.
- `LauncherMarker`'s trailing semicolon is valid C# 12 simple-type syntax, not a
  parse or build defect.
- The generic post-manifest fallback has no distinct ordinary trigger after the
  throwing backend-dispose path is accounted for by bug 31.
- Compiler-output containment (`P06`): not accepted as a separate deletion bug;
  that check does not delete compiler outputs, and its routine escaping-exception
  behavior is already covered by bug 12.
- Blank reset paths: not accepted. Blank values are filtered before topology and
  deletion, while a nonblank set with mismatched markers is rejected before any
  publication member is removed.
- Relative `ToolsDirectory`: unsupported configuration and, for the general
  relative-base concern, already represented by bug 23; no new supported-path
  defect was established.
- `NoResultFailure(4)`: not accepted because production launcher control flow
  does not supply exit code 4 to that recovery projection.
- Additional protocol assumption enum kinds: not accepted; the protocol is
  intentionally closed to its declared producers, and no production assembler
  emits the proposed extra kinds.
- JSON null admission: rejected because the shape validator requires each
  non-nullable declared value's exact token kind before deserialization.
- Pre-gate signal handling: not accepted. Before authenticated gate release the
  supervisor has not armed or started the managed verifier, so the reported
  post-arm cleanup contract is not reachable.
- The additional ordinary-descendant `P05` report is a duplicate of confirmed
  bug 7, including the transient `/proc` observation root; it is not a separate
  issue.
- Broader remote-filesystem enumeration, destination replacement checks, and
  native-library replacement/resolver proposals were not reopened: they are
  hardening work outside this ordinary-correctness scope, not evidence of a new
  supported-path failure.
- Mixed launcher private request/result generations: not substantiated. Both
  private paths are invocation-owned and sequenced by one launcher; no ordinary
  cross-invocation writer to those exact paths was identified.
- `ValidateAndReport` returning an exclusive-or null out-parameter state: not
  reachable in its current branches, which assign validation state and response
  together before returning.
- Effect-witness locations escaping validation: rejected because full response
  validation already calls `HasValidLocation` for a present witness.
- Null protocol-error entries escaping sanitization: rejected because the
  assembler/validator path normalizes or rejects them before launcher reporting.
- Containing-host crash leaves a detached supervisor: not accepted as a project
  bug under the documented in-process task lifetime. The canonical container is
  the external lifetime boundary, and a child-only change cannot guarantee
  cleanup if either containing process is abruptly gone. A broader lifecycle
  contract would be needed before changing behavior.
- Retained cleanup anchors can live indefinitely: not accepted as an independent
  bug. Source comments and tests deliberately retain ownership until a known
  live supervisor exits. Releasing or time-capping the anchor while that process
  is still alive would abandon the containment boundary rather than fix it. The
  underlying reason a supervisor fails to exit should be fixed instead.
- Ambient .NET diagnostics/instrumentation variables are inherited: not
  substantiated as a defect. Environment inheritance is normal process behavior
  in the trusted canonical container and is required by ordinary tracing and
  coverage workflows. No hermetic child-environment contract was found.
- Concurrent `RunVerifier.Dispose` and `Execute` remains unaccepted without a
  lifecycle contract. `ICancelableTask.Cancel` is the supported concurrent
  operation and is synchronized; neither `IDisposable` nor the MSBuild task
  contract promises concurrent disposal with execution. Bug 45 instead concerns
  ordinary sequential disposal after `Execute` has transferred live reader
  ownership to a retained cleanup anchor.
- Output capture can stop after a high surrogate: the boundary fact is true, but
  no correctness bug was substantiated. .NET strings can contain unpaired UTF-16
  code units, the limit is explicitly measured in characters, protocol parsing
  continues independently of captured logging text, and no downstream scalar or
  serialization contract requiring a complete pair was identified.

## Validation caveat

No current build or test validation is claimed. The requested final activity was
static inspection and documentation only. Items marked as previously reproduced
refer solely to isolated evidence supplied before or alongside this audit and
should receive focused regression tests as part of implementation.
