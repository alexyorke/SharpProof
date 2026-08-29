# SharpProof bug audit status

This file is the current, evidence-backed status ledger for the repository audit. It keeps unresolved findings, accepted limitations, deferred security/integrity work, rejected leads, and the detailed evidence needed to trace resolved fixes. The compact ledger below provides a quick index without requiring every historical report to be reread.

## Open and accepted findings

The current audit wave is running against exact baseline
ffe74fff1c852d073610cfbebc54c141521a25fb. Twenty read-only agents are
reviewing non-overlapping subsystems. Agents may build or execute disposable
probes, but they do not modify the repository and do not write this ledger.
The main agent is the sole BUGS.md writer.

The following non-security findings were reproduced by their reporting agents
before being added here. No production, test, build, or configuration changes
are included in this audit-only wave.

## Deferred by explicit scope

The following findings concern cybersecurity, raceable trust decisions, or filesystem durability/integrity. They are recorded for a separate security review and were not implemented in this audit, per the user's explicit no-cybersecurity instruction.

## Rejected or reclassified leads

- **1-3:** `ArgumentNullGuard` assignments are intentional null-state narrowing/field initialization patterns, not correctness bugs.
- **4:** `LazyThreadSafetyMode.ExecutionAndPublication` already supplies the required synchronization; no race was reproduced.
- **5:** Documentation breadth is maintenance debt, not an independently reproducible product defect; the documentation audit is tracked separately.
- **6:** `RegisterCompilationEndAction` is a valid Roslyn registration API; the naming claim was based on a mistaken signature assumption.
- **275:** Exact `Contract.Result<T>` nullability matching is intentional contract identity behavior and is covered by binder tests.
- **279:** The original silent profile/configuration disagreement report is superseded. Current configuration parsing detects conflicting aliases and reports the authoritative invalid-configuration diagnostic; no silent shadowing remains.
- **317:** The GUID-bearing compiler-manifest path was replaced with a stable compiler-visible source path and the unchanged-build editorconfig regression is covered by `8127933fc`. Target-level verifier reuse remains intentionally deferred because an inputs/outputs-only skip could bypass repeated refutation and infrastructure checks; a persisted canonical status fingerprint is required before changing that behavior.
- **369:** Explicit-interface implementation admission is fixed by `fa58c7533`; static constructors remain intentionally fail-closed because type-initialization ordering and replay evidence are not modeled.
- **366:** Leading-double-slash path identity divergence was not reproduced by the canonical Linux/.NET path implementation; no publication split was observed.
- **371:** `[SharpProofSuppress]` is documented and tested as analyzer-reporting policy only; collector verification remaining active is intentional fail-closed behavior.
- **412:** The claimed Gates RS0030 build failure was not reproduced; the current Release Gates build is clean and the remaining mutation calls are intentional harness code.
- **417-419:** The reported Linux backslash failures were disproved by canonical PowerShell `Join-Path` normalization and passing path-authority probes.

## Resolved in this branch

Resolved reports are removed after reproduction, implementation, regression testing, and review. This compact table preserves the local evidence anchors.

| Findings | Resolution commit(s) |
| --- | --- |
| 151 | `7e3ef5c8e` (UTF-16 sequence/null-tag SMT encoding and replay tests) |
| 280 | `8d166cad1` (defined divide/remainder cases and retained seed evidence) |
| 284 | `8d166cad1`, `4d2749126` (semantic-cache marker, field/compound alias coverage) |
| 285 | `8d166cad1` (semantic Roslyn outcome-construction architecture scan) |
| 324 | `e5850507a` (audited Roslyn construction and whole-compilation diagnostics boundary) |
| 409 | `6462246f7` (protocol answer catalog and guarded semantic-cache writes) |
| 317 | `8127933fc` (stable compiler-visible manifest source path and incremental regression) |
| 369 | `fa58c7533` (explicit-interface implementation boundary; static constructors remain fail-closed) |
| 364 | `02a645e69` (regular-file validation for private verifier paths) |
| 337 | `cc0f2bc6b` (validated recovery for interrupted release-bundle swaps) |
| 273 | `91636b24f` (directory synchronization after publication deletion) |
| 202 | `0a2c179f9` (runtime companion path validation and generated launcher coverage) |
| 257-262 | `68afb8ca1`, `c3ab72290`, `8bd08c6e0` |
| 263-270 | `0c9e0ec0d`, `0c95dad38`, `0a2c179f9`, `a7b99ca24` |
| 274, 276 | `549c76510` |
| 277 | `68afb8ca1` (bounded summary dependency regression) |
| 278 | `68afb8ca1` |
| 281-283 | `68afb8ca1`, `0a2c179f9`, `a7b99ca24` |
| 286-287 | `a7b99ca24` |
| 288 | `4d2749126` (unknown event receivers retain add/remove accessor effects) |
| 295 | `47b8d6f7b` (captured closure state and allocation effects) |
| 403 | `616f9e619`, `6448cab79`, `f00be7ef3` (authoritative pre-manifest failure rebinding) |
| 410 | `b92cba235`, `f9e77c5b4` (factory method-group delegate inspection) |
| 326-327 | `f336f1213` (meta-analyzer recursive fragments and interface storage) |
| 351 | `8e34fcfca` (nonblank retired-mode alias fallback) |
| 422 | `0ef01b488` (preflight timing evidence remains recordable) |
| 296 | `8c71195e9` (canonicalization arity guards and typed regression) |
| 311 | `9bcb41bd5` (public evidence-authority validation overloads) |
| 350 | `adc74ffaa` (banner-aware generated header detection) |
| 396 | `bf772d063`, `87da87603` (attribute-aware metadata companion prefilter and metadata-table fast path) |
| 411 | `3cf5c3747`, `39dc0ab87` (cataloged semantic strings and static-constructor-safe readonly inference) |
| 416 | `acdf88263` (inventory-driven semantic framework identity scan) |
| 423 | `87da87603` (package tests honor `SHARPPROOF_REPO_ROOT` under isolated coverage output) |

The audit does not claim that the deferred security findings are fixed. Any future change to those areas should receive a separate threat-model review and dedicated validation.

## Active, deferred, and rejected findings

Historical resolved reports are intentionally removed from this file; the compact resolution table above retains their evidence anchors. The entries below are the remaining deferred, partial, policy, rejected, disproved, or not-reproduced records.

### 318. [DEFERRED CONTAINMENT] Retained Cleanup Anchors Invoke Environment.FailFast Asynchronously After RunVerifier Has Returned - Killing Reused MSBuild Nodes (and Unrelated Concurrent Builds) After the Task Reported Its Result

**Location**: `SharpProof.BuildTasks\RunVerifier.cs` (Callback construction ~Lines 434-449: authenticationFailure null ONLY when terminal cause == Canceled; retention-deadline arm and receipt-recheck arm inside ObserveCleanupAnchorAsync ~Lines 813-855 invoking anchor.AuthenticationFailure; overflow-eviction invoke ~Lines 805-807; sink HandleContainmentAuthenticationFailure -> Environment.FailFast at Lines ~720-728; deferral gate Lines 366-385); supervisor exit-without-receipt semantics `SharpProof.BuildTasks\VerifierProcessSupervisor.cs` Lines 172-182 (return 125 with no receipt when cleanup incomplete or the protected launcher outlives its grace).
**Description**: When a run terminates by timeout/output-limit while the supervisor cannot finish within its bounded window, Execute retains a cleanup anchor whose authenticationFailure callback is non-null for every terminal cause except Canceled. The anchor lives on background tasks (ObserveCleanupAnchorAsync): if the armed supervisor later exits without an authenticated cleanup receipt (exactly when a descendant could not be proven dead or the launcher needed >1 s to notice its killed worker), or if the 30 s retention deadline expires, the anchor invokes the callback, which calls Environment.FailFast. This executes AFTER Execute() has returned ExitCode=124/-1 and MSBuild has raised its own error and moved on: on node-reuse hosts the process dies asynchronously seconds-to-minutes later, aborting whatever else that node is doing (parallel project builds, subsequent scheduled requests), with a failfast dump naming neither the original timeout nor the wedged descendant. Notably, CleanupRetryBudgetMilliseconds bounds what used to hang the node (#241) into precisely these no-receipt 125 exits - converting the old livelock into this post-hoc FailFast. DISTINCTNESS vs #240: #240 acknowledges only the INLINE/synchronous missing-receipt FailFast during Execute; the residual defect reported here is the asynchronous, post-task anchor callback crashing nodes outside any task boundary - distinct site (ObserveCleanupAnchorAsync/RetainCleanupAnchor vs RequireSupervisorCleanupReceipt), timing, and trigger.
**Reproduction Steps**:
1. Run a verified build sized so a callable times out while the launcher needs >1 s after worker death to publish (or block a worker in D-state so the supervisor's bounded cleanup completes=false).
2. Let the wall budget expire: RunVerifier logs timeout, sets ExitCode=124, the targets raise their error, and the build finishes failing normally - but the pipes were open at decision time, so the anchor was retained with a non-null callback.
3. Within <=30 s the anchor observes the supervisor's receipt-less 125 exit (or hits the retention deadline) and Environment.FailFast terminates the MSBuild node after completion - observable as abrupt node death while other work runs on it; replacing the callback body with logging removes the crash, proving the anchor path fired.
**Confidence**: High (every cited branch read; trigger chain statically complete; frequency depends on launcher/descendant slow-fail timing that CI load routinely produces).

### 406. [DEFERRED SECURITY] The Worker's `--request`/`--result` Distinctness Guard Is Ordinal-String-Only - Symlink/Hardlink Aliases Bypass It While the Launcher's Own Path-Conflict Machinery Classifies the Identical Layout as a Conflict

**Location**: `SharpProof.Worker\Program.cs` (Lines 217-250 `TryParseArguments`: GetFullPath normalization at Lines 239-240; sole alias guard `!string.Equals(request, result, StringComparison.Ordinal)` at Line 249); pinning test `SharpProof.Worker.Test\WorkerProgramTests.cs` Lines 34-67 (`DirectInvocationRejectsRequestResultAliasBeforeStartBarrier` - identical STRING only); in-repo standard for the same decision `SharpProof.Host\LinuxPathIdentity.cs` (Line 650 `AreSameExistingFile` via stat device+inode, consumed by `PathsConflict` at Line 681) used by the launcher's ValidateDistinctPaths.
**Description**: The worker validates that --request and --result differ using only an ordinal comparison of two GetFullPath outputs. GetFullPath resolves dot segments and anchors but never symlink or link identity, so `/dir/request.json` and a hardlink/symlink `/dir/result.json` targeting it pass the guard while denoting one filesystem object - precisely the layout the launcher classifies as conflicting via the stat fallback, proving the project's own standard treats lexical equality as insufficient. The pinned regression cements only the exact-string case, so the bypass is unpinned. Consequences for direct invokers (usage documents direct invocation): the handshake proceeds past the intended barrier and `AtomicFile.WriteUtf8Async` renames staged bytes over the aliased name - through a symlink POSIX rename replaces the link entry with a regular file; across a hardlink the result name is re-pointed while the sibling retains the old inode. No verdict corruption and no cross-run damage in the launcher flow (launcher stages private files and screens aliases itself); the damage is that the documented hygiene invariant degrades to best-effort lexical matching at the process boundary where it is the ONLY defense. Distinct from #364 (non-regular file TYPES accepted on the private-path pipeline and clobbered - here both paths are ordinary regular files and the defect is missing inode-identity alias detection in argv parsing), #366 (Canonicalize returning GetFullPath verbatim splitting PUBLICATION marker ownership - no markers/publication machinery involved here), #308 (relative cache anchoring).
**Reproduction Steps**:
1. In the canonical container create `/tmp/h11/a.json` and `ln -s a.json /tmp/h11/b.json` (or `ln` for a hardlink).
2. Invoke the worker directly with `verify --request /tmp/h11/a.json --result /tmp/h11/b.json --start-stdin --parent-pid $$`, stdin held open.
3. Observe the process proceeds past argument validation (reaching start-barrier behavior) instead of returning usage exit 2; contrast the SAME string for both flags, which returns 2 immediately (pinned test).
4. Static confirmation: `Path.GetFullPath` outputs differ while `stat -c '%d:%i'` shows equal device/inode - exactly the pair `AreSameExistingFile` treats as conflicting.
**Confidence**: High on mechanism (guard composition, pin scope, launcher contrast read line-by-line); Low-Medium significance (direct invocation with aliased spellings required; worst outcome silent transformation of the aliased name).

### 407. [DEFERRED SECURITY] Z3 Import Resolver Is Registered Before the Verified Native Handle Is Published - a Concurrent First P/Invoke Reads a Zero Handle and Falls Through to Ambient Default Probing That Never Consults the Container-Validated Payload

**Location**: `SharpProof.Host\ContainerNativeLibrary.cs` (Lines 13-48 `InstallZ3ResolverRequired`; decisive ordering Lines 28-36: `NativeLibrary.Load` -> `SetDllImportResolver(ResolveZ3Import)` at Line 32 -> `Volatile.Write(ref _z3Handle, handle)` at Line 36; resolver body Lines 50-66 returning `Volatile.Read(ref _z3Handle)` at Line 65). Installers: `SharpProof.Worker\SharpProofWorker.cs` Line 34, `Tools\SharpProof.Fuzz` fuzzing setups, both ContainerNativeLibrarySetup test helpers.
**Description**: The resolver is registered BEFORE the loaded handle is published. During that window `ResolveZ3Import` reads `_z3Handle == IntPtr.Zero` and returns it; per the DllImportResolver contract a Zero return means "callback did not resolve", so the runtime falls back to DEFAULT probing for "libz3" - the runtime-closure directory (which deliberately lacks libz3.so; the payload lives under the versioned native tree), then system directories and anything on the inherited, unscrubbed LD_LIBRARY_PATH. Two outcomes, both defeating the resolver's purpose: (a) `DllNotFoundException("libz3")` from a perfectly healthy container - absorbed by IsBackendUnavailable into a typed backend-unavailable verdict, i.e., spurious infrastructure attribution caused by the resolver's own ordering; or (b) silent mapping of an ambient, hash-unverified libz3 if one is probeable. The lock serializes installers but does nothing about this intra-install ordering versus a concurrent first P/Invoke from another thread. Blast radius: embedding/fuzz hosts issuing their first Z3 P/Invoke concurrently with install (shipped worker installs single-threaded before any query); fail direction mixed - usually fail-closed (a), worst-case trust-boundary bypass (b). Distinct from #271 (deferred security: STATIC identity gap - contract validates bytes separately from the library later loaded, i.e., WHICH bytes bind even when everything is ordered; here the internal ordering means resolution bypasses the validated payload entirely because no pinned handle exists yet to fall back FROM; fixing either does not fix the other). No other entry touches `ContainerNativeLibrary` resolution logic (#374/#380 are wrapper lifecycles).
**Reproduction Steps**:
1. Confirm ordering: Line 32 registers ResolveZ3Import; only Line 36 publishes _z3Handle; Line 65 returns Volatile.Read verbatim.
2. Contract check: a DllImportResolver returning IntPtr.Zero signals "not resolved", after which the runtime performs default probing for the literal name "libz3".
3. Dynamic: start thread B looping on `new Microsoft.Z3.Context()` BEFORE thread A calls InstallZ3ResolverRequired; place a stub libz3.so on LD_LIBRARY_PATH (inherited by spawned workers unscrubbed).
4. Observe intermittently either DllNotFoundException/typed backend-unavailable despite a valid contract+payload, or the ambient stub mapped (/proc/<pid>/maps shows the LD_LIBRARY_PATH copy); reordering Lines 32-36 (publish handle first) eliminates both.
**Confidence**: High on mechanism (ordering, Zero-return fallback semantics, environment inheritance verified line-by-line); Low on present-day reachability (concurrent-warmup embedders only).
