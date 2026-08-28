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

### 215. Trusted-attributes payload/hash binding race

**Status:** Deferred security review.

The analyzer hashes the file at a path after Roslyn has loaded the reference, without proving that both reads describe the same bytes.

### 271. Z3 pin/hash versus loaded-library identity

**Status:** Deferred security review.

The container contract validates bytes separately from the library later loaded by the native resolver.

### 272. Publication-path validation versus use

**Status:** Deferred security review.

Path identity is checked by a userspace walk and is not kernel-enforced against a concurrent symlink replacement.

### 273. Publication deletion durability

**Status:** Deferred integrity/durability review.

Reset/invalidation removes publication members without the full filesystem durability protocol required to survive a power loss.

## Rejected or reclassified leads

- **1-3:** `ArgumentNullGuard` assignments are intentional null-state narrowing/field initialization patterns, not correctness bugs.
- **4:** `LazyThreadSafetyMode.ExecutionAndPublication` already supplies the required synchronization; no race was reproduced.
- **5:** Documentation breadth is maintenance debt, not an independently reproducible product defect; the documentation audit is tracked separately.
- **6:** `RegisterCompilationEndAction` is a valid Roslyn registration API; the naming claim was based on a mistaken signature assumption.
- **275:** Exact `Contract.Result<T>` nullability matching is intentional contract identity behavior and is covered by binder tests.
- **279:** The original silent profile/configuration disagreement report is superseded. Current configuration parsing detects conflicting aliases and reports the authoritative invalid-configuration diagnostic; no silent shadowing remains.

## Resolved in this branch

Resolved reports are removed after reproduction, implementation, regression testing, and review. This compact table preserves the local evidence anchors.

| Findings | Resolution commit(s) |
| --- | --- |
| 151 | `7e3ef5c8e` (UTF-16 sequence/null-tag SMT encoding and replay tests) |
| 280 | `8d166cad1` (defined divide/remainder cases and retained seed evidence) |
| 284 | `8d166cad1`, `4d2749126` (semantic-cache marker, field/compound alias coverage) |
| 285 | `8d166cad1` (semantic Roslyn outcome-construction architecture scan) |
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

### 317. [PARTIALLY RESOLVED 8127933fc] Per-Invocation GUID Leaks Through CompilerVisibleProperty Into GeneratedMSBuildEditorConfig.editorconfig, Defeating Incremental Compilation for Every Verification-Enabled Project

**Location**: `SharpProof.Verifier\buildTransitive\SharpProof.Verifier.targets` (GUID mint at Line ~126: `_SharpProofInvocationId Condition="'$(_SharpProofInvocationId)' == ''"` -> NewGuid; `_SharpProofExpectedInvocationDirectory`/`_SharpProofInvocationDirectory` = runs/<guid> at Lines ~128/131; `_SharpProofCompilerManifestPath` = Combine(invocation dir, 'compiler-manifest.json') at Line 146; hook at Line 92); `SharpProof.Verifier\buildTransitive\SharpProof.Verifier.props` (Line 33: `<CompilerVisibleProperty Include="_SharpProofCompilerManifestPath" />`); consumer proof `SharpProof.CompilerCollector\FinalCompilationCollector.cs` (Line 7 reads analyzer-config key `build_property._SharpProofCompilerManifestPath`); SDK chain: Microsoft.Managed.Core.targets writes CompilerVisibleProperty values into `$(IntermediateOutputPath)$(MSBuildProjectName).GeneratedMSBuildEditorConfig.editorconfig` and adds it to @(EditorConfigFiles); Microsoft.CSharp.Core.targets lists @(EditorConfigFiles) among CoreCompile Inputs and passes them to Csc.
**Description**: Every build that runs _SharpProofInitializeVerify mints a fresh _SharpProofInvocationId GUID (condition `'== ''` means new per project evaluation per msbuild invocation) and derives _SharpProofCompilerManifestPath under runs/<guid>/ - that property is registered as compiler-visible and read back by the collector through the generated editorconfig, proving the value travels there. Because the manifest path embeds a fresh GUID on every invocation, the generated editorconfig content differs on every build, the write-always target rewrites it, its timestamp bumps, and CoreCompile's Inputs check always fails: csc, all analyzers/generators, and then the entire verifier pipeline (launcher + worker + Z3) rerun on every no-op `dotnet build` of any project with SharpProofVerify=true (strict profile makes that opt-out-free via AnalyzerConsumer.props/portable targets). Secondary effect: during design-time builds the setting PropertyGroup condition excludes DesignTimeBuild, so the property flips empty and back around every F5, churning the same file. A stable path already exists ($(_SharpProofVerifyDirectory), computed identically without the invocation id). Verified mechanically against both repo files and installed SDK 9.0.317 targets.
**Reproduction Steps**:
1. In the canonical container create a consumer project with strict profile plus both SharpProof packages; build twice with zero source changes.
2. After build 2, observe obj/Debug/net8.0/<Project>.GeneratedMSBuildEditorConfig.editorconfig contains a DIFFERENT build_property._sharpproofcompilermanifestpath (.../runs/<new-guid>/compiler-manifest.json) than after build 1.
3. Observe CoreCompile/Csc re-executed on build 2 (-v:n shows no "Skipping target CoreCompile") followed by a fresh launcher spawn although no input changed. Delete only the GUID from that one line and rebuild: the file stops changing and CoreCompile skips again, isolating the GUID as the cause.
**Confidence**: High (each link verified statically; blast radius is all verification-enabled projects).

**Status**: The compiler-visible value now uses the stable, package-owned
`_SharpProofCompilerManifestSourcePath` (`compiler-manifest.input.json`) while
the GUID-bearing `_SharpProofCompilerManifestPath` remains a private
per-invocation copy. The compiler collector writes the stable source, the
verification target copies it into the isolated run directory, and the
invalidation task protects both paths. The incremental regression confirms the
generated editorconfig is byte-identical on an unchanged second build and no
longer contains a `/runs/` path. The `SharpProofVerify` target still has no
incremental `Inputs`/`Outputs`, so whether the verifier itself is skipped on a
no-op remains a separate follow-up. This is intentionally deferred: the
pre-dependency invalidation target removes owned outputs before verification,
and a safe skip requires a persisted input/status fingerprint that still
rechecks unchanged refuted, missing, corrupt, and infrastructure results. An
`Inputs`/`Outputs`-only change would weaken the repeated-refutation guarantee.

### 318. [DEFERRED CONTAINMENT] Retained Cleanup Anchors Invoke Environment.FailFast Asynchronously After RunVerifier Has Returned - Killing Reused MSBuild Nodes (and Unrelated Concurrent Builds) After the Task Reported Its Result

**Location**: `SharpProof.BuildTasks\RunVerifier.cs` (Callback construction ~Lines 434-449: authenticationFailure null ONLY when terminal cause == Canceled; retention-deadline arm and receipt-recheck arm inside ObserveCleanupAnchorAsync ~Lines 813-855 invoking anchor.AuthenticationFailure; overflow-eviction invoke ~Lines 805-807; sink HandleContainmentAuthenticationFailure -> Environment.FailFast at Lines ~720-728; deferral gate Lines 366-385); supervisor exit-without-receipt semantics `SharpProof.BuildTasks\VerifierProcessSupervisor.cs` Lines 172-182 (return 125 with no receipt when cleanup incomplete or the protected launcher outlives its grace).
**Description**: When a run terminates by timeout/output-limit while the supervisor cannot finish within its bounded window, Execute retains a cleanup anchor whose authenticationFailure callback is non-null for every terminal cause except Canceled. The anchor lives on background tasks (ObserveCleanupAnchorAsync): if the armed supervisor later exits without an authenticated cleanup receipt (exactly when a descendant could not be proven dead or the launcher needed >1 s to notice its killed worker), or if the 30 s retention deadline expires, the anchor invokes the callback, which calls Environment.FailFast. This executes AFTER Execute() has returned ExitCode=124/-1 and MSBuild has raised its own error and moved on: on node-reuse hosts the process dies asynchronously seconds-to-minutes later, aborting whatever else that node is doing (parallel project builds, subsequent scheduled requests), with a failfast dump naming neither the original timeout nor the wedged descendant. Notably, CleanupRetryBudgetMilliseconds bounds what used to hang the node (#241) into precisely these no-receipt 125 exits - converting the old livelock into this post-hoc FailFast. DISTINCTNESS vs #240: #240 acknowledges only the INLINE/synchronous missing-receipt FailFast during Execute; the residual defect reported here is the asynchronous, post-task anchor callback crashing nodes outside any task boundary - distinct site (ObserveCleanupAnchorAsync/RetainCleanupAnchor vs RequireSupervisorCleanupReceipt), timing, and trigger.
**Reproduction Steps**:
1. Run a verified build sized so a callable times out while the launcher needs >1 s after worker death to publish (or block a worker in D-state so the supervisor's bounded cleanup completes=false).
2. Let the wall budget expire: RunVerifier logs timeout, sets ExitCode=124, the targets raise their error, and the build finishes failing normally - but the pipes were open at decision time, so the anchor was retained with a non-null callback.
3. Within <=30 s the anchor observes the supervisor's receipt-less 125 exit (or hits the retention deadline) and Environment.FailFast terminates the MSBuild node after completion - observable as abrupt node death while other work runs on it; replacing the callback body with logging removes the crash, proving the anchor path fired.
**Confidence**: High (every cited branch read; trigger chain statically complete; frequency depends on launcher/descendant slow-fail timing that CI load routinely produces).

### 324. [DEFERRED GOVERNANCE] CSharpSyntaxTree.ParseText + CSharpCompilation.Create(syntaxTrees:) Are Banned Nowhere While AddSyntaxTrees Is - With Live Production Usage in SharpProof.Gates

**Location**: `SharpProof.Meta.Analyzers\SharpProofSoundnessAnalyzer.cs` (KnownTypeNames Lines 13-37 - no CSharpSyntaxTree entry; ForbiddenMethods[SyntaxFactory] = ParseStatement/ParseExpression/ParseTypeName only; GetDiagnostics banned only on SemanticModel types); `BannedSymbols.txt` (Lines 4-5 ban only Compilation.Add/RemoveSyntaxTrees; Lines 40-44 ban only the three SyntaxFactory.Parse* overloads); `SharpProof.ArchitectureTest\BoundaryEnforcementTests.cs` (required-inventory list pins the same incomplete set). Live sites: `SharpProof.Gates\AnalyzerGateHost.cs` (Lines 53-55: CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(source, ParseOptions, "input.cs")], ...); Lines 202/220 again), `SharpProof.Gates\Performance\WorkerPerformanceProbe.cs`, `SharpProof.Gates\Corpus\*`, `SharpProof.Testing\IrCSharpDifferentialOracle.cs`.
**Description**: All three governance layers defend "synthesize source" exclusively at the post-construction mutation points (AddSyntaxTrees) and the fragment parse points (SyntaxFactory.ParseStatement/Expression/TypeName). The equivalent front door - parsing arbitrary whole-source text into a tree and injecting it directly through the CSharpCompilation.Create(..., syntaxTrees: [...]) constructor - appears in none of them: not in KnownTypeNames, not in any ForbiddenMethods arm, not in BannedSymbols.txt, not in the arch-test inventory that structurally freezes that list. This is not hypothetical: SharpProof.Gates builds entire compilations from parsed strings today, and Compilation.GetDiagnostics/GetDeclarationDiagnostics - the whole-compilation enumeration family named in SPMETA001's own description text - are likewise absent while SemanticModel.GetDiagnostics alone is banned. Any soundness-critical layer can adopt the Gates pattern and synthesize/verify-against fabricated trees with zero SPMETA001 diagnostics, zero RS0030, and a green ArchitectureTest suite. Distinct from #187/#245 (display-text gaps, remediated), #244 (speculative-binding receiver shapes, remediated), #285 (Gates missing from the SPMETA011 outcome-constructor wiring): different API surface (tree fabrication + whole-compilation enumeration), a hole in the artifacts that DO apply to Gates, and live in-tree usage rather than hypothetical widening precondition.
**Reproduction Steps**:
1. In SharpProof.Frontend add `var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("static class X { }"); var c = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("t", [tree]);` and build - zero RS0030 (no doc-ID match) and zero SPMETA001 (name/type absent from tables).
2. Contrast with `c.AddSyntaxTrees(tree)` - immediate RS0030 error under TreatWarningsAsErrors, proving the boundary defends only the post-construction route.
3. Run SharpProof.ArchitectureTest: BannedSymbolInventoryCoversEverySoundnessBoundary passes - its required substrings never mention ParseText/Create, confirming no layer would flag the omission.
**Confidence**: High (absence verified by full-file reads of all three enforcement artifacts; live bypass usage verified in Gates source).

**Status**: Deferred as a governance-boundary decision. Gates, Testing, and Fuzz intentionally synthesize compilations for release evidence and differential checks, so a blanket ban would break those supported harnesses. A future change should define and test a narrow source-synthesis exception before expanding the banned-symbol inventory; no safe production patch is justified here.

### 337. [DEFERRED INTEGRITY] Release-Evidence Publication Swaps Directories With Two Non-Crash-Safe Moves - Process Death Between Them Deletes PackageSource and Strands the Previous Bundle in an Unreferenced Hidden Backup Until Manual Recovery

**Location**: `scripts\SharpProof.ReleaseChecksums.ps1` (Publish-SharpProofReleaseBundleAtomically Lines 190-234: backup move at ~Line 219, staging move at ~Line 221, catch-only restore at Lines 226-234); caller `scripts\New-SharpProofReleaseEvidence.ps1` (staging sibling creation, trap, swap invocation ~Lines 969-973, destination DEFAULTING TO THE PACKAGE SOURCE ITSELF at Lines 543-544); no recovery consumer repo-wide (the only `.backup` reference is the creation site).
**Description**: The atomic commit is implemented as two separate renames: move the live destination aside to a hidden GUID-named .backup sibling, then move the validated staging directory into place. The catch restores the backup only for in-process exceptions; SIGKILL/OOM/container teardown between the two moves leaves (a) destination absent, (b) the previous good bundle parked in .<name>.<guid>.backup, and (c) no code anywhere that knows about that pattern. The primary caller makes this worse by defaulting the destination to the package source itself: the pack command's artifacts/container-packages directory - the exact -PackageSource that pack's subsequent steps and the pilots/package-consumers commands require - is the directory being swapped underneath the pipeline. After such a crash, every rerun fails at Resolve-Path ("path does not exist"), the orphaned .backup accumulates beside it, and nothing distinguishes recoverable state from corruption; the operator must notice and hand-move the hidden directory. The repo treats crash-window publication as a defect class elsewhere (#281 generator WriteAllText, #134/#219 AtomicFile durability, #242/#273 marker durability); this is the release-packaging member of that family. Distinct from all of those: a PowerShell directory-swap with missing-backup-recovery consequence for PackageSource.
**Reproduction Steps**:
1. Run `docker compose run --rm tooling pack -Configuration Release` and SIGKILL the container in the window between entering Publish-SharpProofReleaseBundleAtomically's first and second Directory.Move (easily widened by inserting a delay).
2. Observe artifacts/container-packages gone and artifacts/.container-packages.<guid>.backup present.
3. Rerun any dependent command (pack/pilots/package-consumers): fails at Resolve-Path with "PackageSource is not a directory"; grep confirms no script ever references *.backup to self-heal.
**Confidence**: High mechanics (both move sites, catch-only restoration, absence of recovery consumer, destination==source default all verified line-by-line); Low-Medium likelihood/severity (kill window is narrow, but the result bricks the release pipeline's input directory until manual recovery).

### 364. [DEFERRED I/O SAFETY] Private I/O Paths Accept Pre-Existing Non-Regular Files - --result /dev/null Unlinks the Device Node and Renames a Regular File Over It While the Run Reports Success

**Location**: `SharpProof.Worker.Launcher\Program.cs` (Staging + `DeleteIfExists` Lines 92-96; generic delete helper Lines 926-932 using `File.Exists`/`File.Delete` with no file-type check; `ValidateDistinctPaths` Lines 1045-1108 applying `RequireLocalPath` ONLY to publication paths at Lines 1059-1062); canonicalization type-checks ANCESTORS only `SharpProof.Host\LinuxPathIdentity.cs` (Lines 96-118 `Canonicalize`: final component unchecked); blind rename-over-inode `SharpProof.Ir\AtomicFile.cs` (Lines 144-187 `Prepare`/`Publish` via `File.Replace`/`File.Move`); worker writes through the same rename `SharpProof.Worker\Program.cs` (Lines 283-286); deliberate contrast on the publication side `LinuxPathIdentity.cs` (Lines 1215-1233 `EnsureRegularPublicationPath`, Lines 684-714 `DeleteIfUnprotected` demanding regular files).
**Description**: `--request/--result/--compiler-manifest/--cache-directory` receive no regular-file/type validation anywhere: `Canonicalize` validates only that ancestors are directories, distinctness checks compare path strings/stat identity, and nothing refuses character devices/FIFOs. Execution then deletes the existing node (`File.Exists` is true for `/dev/null`) and the worker's staged-write rename recreates the path as a REGULAR FILE containing the result JSON - the container's `/dev/null` is silently destroyed while the launcher exits 0 with a genuine green verdict; every subsequent process writing to /dev/null fills a growing regular file (disk exhaustion, bizarre downstream failures). Unprivileged variant: any user-owned FIFO/block device passed as `--result` is clobbered identically. The codebase's own publication layer explicitly demands regular outputs ("publication members must be regular files"), so private-path acceptance is a gap, not a choice. Distinct from #308 (relative cache-directory anchors), #316/#290 (temp sweeping/test globs): none covers inode-type validation of the private path pipeline.
**Reproduction Steps**:
1. In the canonical container, run a verification with the result redirected at the device: `dotnet build -p:SharpProofVerify=true -p:SharpProofVerifyResultFile=/dev/null`.
2. Build completes with exit 0 (the launcher reads back its own JSON from the path).
3. `stat -c '%F %s' /dev/null` -> `regular file` with size > 0 (was `character special file`, size 0); `echo x >/dev/null` now appends to disk.
4. Unprivileged cross-check: `mkfifo /tmp/f` and pass it via the same property - exit 0 and `/tmp/f` is now a regular file.
**Confidence**: High on mechanism (no type gate exists anywhere on the pipeline; POSIX unlink/rename semantics standard); impact-weighted Medium overall because triggering requires an operator-supplied pathological path.

### 366. [NOT REPRODUCED] Canonicalize Validates a Reconstructed Single-Slash Walk but Returns Path.GetFullPath Verbatim - the POSIX Leading-'//' Spelling Produces Two Identities for One File, Splitting Publication-Marker Ownership

**Status**: The claimed identity split was not reproduced in the canonical Linux amd64 container with the supported .NET/PowerShell toolchain: the path APIs normalize the tested leading-double-slash spelling to the same effective path. No production change is claimed; retain this as a rejected lead unless a supported runtime and concrete publication-marker divergence are demonstrated.

**Location**: `SharpProof.Host\LinuxPathIdentity.cs` (`Canonicalize` Lines 84-135: rejects dot segments, walks reconstructed `"/"+segments` prefixes with lstat symlink rejection, but `return fullPath;` returns `Path.GetFullPath(path)` UNMODIFIED - and .NET on Linux preserves exactly two leading slashes per the POSIX implementation-defined rule); lexical consumers: `AcquirePublicationLocks` (Lines 587-595), `ValidatePublicationTopology`/`IsStrictPathAncestor` (Lines 760-795, `StringComparison.Ordinal`), `PublicationMetadataPath` (Lines 805-821, SHA-256 over the UTF-8 canonical STRING), `BindPublicationSet` adoption keyed on that hash (Lines 867-896), `IsSameOrDescendant`/stat fallback only for already-existing files (Lines 660-675, 1308-1318); entry of user-supplied spellings `SharpProof.Worker.Launcher\Program.cs` (Lines 1139-1149 `FullPath`/`OptionalFullPath`).
**Description**: `"/w/out"` and `"//w/out"` denote the same kernel object, pass validation identically (the walk checks single-slash prefixes), yet escape as two DIFFERENT identity strings. Every sameness mechanism downstream is lexical on that string, so: (a) within one invocation, a publication set spelled with mixed slash counts passes distinctness/topology gates (nonexistent destinations have no stat identity to fall back on) and `BindPublicationSet` commits TWO live ownership markers over one destination; (b) across invocations, respelling an unchanged output makes `InvalidatePreviousPublication` find no marker under the new hash, skip invalidation, and then abort with "refuses to adopt a pre-existing publication destination without an exact ownership marker" (exit 3) on a tree SharpProof itself published seconds earlier - stranding permanent orphaned `.set` markers and flip-flopping between spellings. Availability/identity-integrity defect (fail-closed direction, not soundness-breaking). Distinct from #272 (validation-vs-use TOCTOU - no concurrency needed here), #316/#242 (crash-orphaned/torn markers - these are fully written markers duplicated by ordinary operation), #243/#313 (unrelated gates).
**Reproduction Steps**:
1. Verify the primitive: print `Path.GetFullPath("//tmp/x/result.json")` on Linux - the leading `//` survives while `Canonicalize` accepts the input.
2. Publish normally once with `--publish-result /tmp/pub-demo/result.json`; note the marker under `/tmp/pub-demo/.sharpproof-publication/<hex>.set`.
3. Rerun the identical command spelled `--publish-result //tmp/pub-demo/result.json`: no marker matches the new hash; the launch fails with the adoption-refusal error despite the file being SharpProof's own prior output; cleanup now requires manually deleting the output and the stranded marker.
4. Single-run variant: supply `--publish-request /tmp/pub-demo/req.json --publish-result //tmp/pub-demo/req.json`-style overlapping spellings; distinctness/topology pass and two `<hash>.set` markers claim one destination.
**Confidence**: Medium (walk-vs-return asymmetry and all lexical consumers verified; the one external premise - .NET preserving exactly two leading slashes on Unix - is documented POSIX behavior and self-verifiable per step 1).

### 369. [PARTIALLY RESOLVED fa58c7533] Explicit Interface Implementations and Static Constructors Are Analyzed In-Process but Marked Unsupported in the Manifest - Strict Builds Kill Analyzer-Blessed Members While Advisory Builds Silently Lose All Coverage for Them

**Location**: `SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs` (the callable-kind whitelist now admits `ExplicitInterfaceImplementation` but intentionally excludes `StaticConstructor`); admitting counterparts `SharpProof.Analyzer.Core\LanguageSubsetGate.cs` and `SharpProof.Contracts\ContractBinder.cs`; enforcement `SharpProof.CompilerCollector\CompilerArtifact\CompilerCallableLowerer.cs` (unsupported targets fail closed with `WorkerClaimReason.UnsupportedCallable`); strict consequence `SharpProof.Worker.Launcher\Program.cs` (incomplete callables fail under `require-proven`).
**Description**: The explicit-interface half of the original report was resolved by `fa58c7533` and is covered by manifest and binder tests; EII bodies now remain in the verifier-supported callable set. Static constructors remain analyzer/binder-admitted but collector-unsupported. The collector and worker do not replay type initialization or static-constructor ordering, so broadening that whitelist would make replay evidence unsound. This is a deliberate fail-closed boundary, not a claim that static constructors are currently proven. Distinct from #341 (documentation enumerating fewer kinds than the gate) and #370 (the separate static-state mutation gate).
**Reproduction Steps**:
1. Strict-profile project (`SharpProofProfile=strict` => verify policy require-proven) referencing SharpProof.Attributes; add an explicitly initialized static constructor and a selected static constructor or static member.
2. Build in-container: static-constructor replay remains unsupported, so the worker returns Unknown/Incomplete and require-proven fails closed with SP0047. This is expected until type-initialization ordering and replay evidence are modeled.
3. Use an explicit interface implementation as a control: the same selected EII body remains in the verifier-supported callable set and is covered by `ClaimManifestBuilderTests.ExplicitInterfaceImplementationUsesTheSupportedCallableSet`.
**Confidence**: High for the original explicit-interface divergence, which is resolved by `fa58c7533`; static-constructor admission remains intentionally fail-closed because constructor initialization and replay semantics need a separate design.

### 370. [CONFIRMED] HasStaticStateMutation treats static-constructor local assignments as static-state writes

**Status**: A conservative gate for actual or unknown static-state writes may be
intentional, but the current syntactic implementation has a separately verified
false-positive slice: local-only assignments and increments in a static
constructor are classified as static-state mutation. This rejects a member even
when the analyzer proves its effect claim and no static storage is written.

**Location**: `SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs` (Conjunct at Line 105: `!(analyzerEffectsSelected && HasStaticStateMutation(target))` inside `supported`; predicate Lines 186-207 scanning ALL statements of explicitly declared static constructors for any assignment/increment; unavailable-routing Lines 463-472 `MarkUnavailable(evidence, WorkerClaimReason.UnsupportedContract)`); no analyzer counterpart `SharpProof.Analyzer.Core\AnalyzerFeaturePipeline.cs` (Lines 192-360; the only `StaticConstructors` uses in Analyzer.Core are member-initializer reachability at scattered lines).
**Description**: When any effect attribute selects a method, the collector
rejects it if the containing type's explicit static constructor contains any
IAssignmentOperation or IIncrementOrDecrementOperation. The helper never
examines the destination. Thus `int local = 0; local++;` sets `supported=false`
and routes the member's effect evidence to Unknown/UnsupportedContract/
Unavailable even though it mutates no static state. CompilerCallableLowerer
then emits UnsupportedCallable. The analyzer's effect evaluator independently
returns Proven for the selected member. Actual static-field writes remain a
sound conservative control; the confirmed defect is the destination-blind
classification.
**Reproduction Steps**:
1. Analyze `sealed class Subject { static Subject() { int local = 0; local++; }
   [EnforcePure] public int Identity(int value) => value; }`.
2. The isolated canonical-container probe reports analyzer effect evaluation
   Proven, but ClaimManifestBuilder returns IsVerifierSupported=false and
   Unknown/UnsupportedContract/Unavailable; the targeted assertion fails only
   on compiler support/evidence.
3. Replace the local with `static int state; static Subject() { state++; }` as a
   control; that real static-state case must remain rejected.

**Recommended fix**: Reuse the effect engine's static-constructor write-region
summary and reject only unknown writes or EffectRegionId.Static(). A smaller
fix may classify assignment/increment targets and ignore local/parameter
destinations, but it must preserve conservative handling for complex aliases.

**Regression coverage**: The local-only static constructor must retain
verifier-supported Proven evidence and lowerable IR. Pair it with an actual
static-field write that remains UnsupportedContract.

**Confidence**: High; a canonical targeted probe independently produced
analyzer Proven and collector Unknown for the local-only trigger.

### 371. [INTENTIONAL POLICY] [SharpProofSuppress] Silences the In-Process Analyzer but Is Never Consulted by the Collector - Suppression Meaning Flips When Moving Advisory->Strict With Zero Surfacing

**Status**: Suppression is documented as changing analyzer reporting only; collector verification intentionally remains active so a suppressed claim cannot bypass strict evidence checks. The profile-dependent outcome is therefore policy, not an unconfirmed implementation gap. No production change is made; any future UX improvement should add explicit diagnostics/documentation rather than skip verification.

**Location**: Analyzer side `SharpProof.Analyzer.Core\SharpProofControlAttributePolicy.cs` (Lines 5-17, 158-188 `ValidateAndShouldSuppress` returning suppress for scoped well-formed attributes) and `SharpProof.Analyzer.Core\AnalyzerFeaturePipeline.cs` (Lines 135, 154, 281: `selection.IsSuppressed` -> record and SKIP all analysis); collector side `SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs` (resolves `ContractSelectionInventory.Suppress` at Line 35 but mirrors ONLY `Trusted` into assumptions at Lines 474-487; zero other references to suppression - grep finds only the unrelated `ReportDiagnostic.Suppress` enum passthrough at `CompilerWireMappings.generated.cs` Line 84); documented intent `README.md` Line 274 / `docs/public-api.md` Line 76 ("changes reporting only").
**Description**: A well-formed `[SharpProofSuppress("reason")]` on method/property/type/assembly makes the analyzer record Suppressed and skip - no analysis, no diagnostics. The collector never consults the suppress bit: suppressed callables still receive postcondition claims, assumption rows, and fully evaluated/sealed effect evidence, and the worker verifies them. In advisory mode the documented "reporting only" contract holds; under `require-proven` the SAME annotation's effective meaning silently flips - a suppressed callable whose claims cannot be proven yields Unknown/Incomplete, launcher exit 5/6, and a hard build failure with no diagnostic acknowledging the suppression that previously silenced the identical fact. Users relying on suppressions during advisory triage hit this deterministically on profile flip, and nothing at the collector boundary explains it. Fail-open is avoided (verification is NOT skipped), so the damage is a profile-dependent semantics change without surfacing. Distinct from #348 (INTERFACE-targeted control attributes validated but inert everywhere - different axis: here class/method scoping WORKS analyzer-side and diverges collector-side).
**Reproduction Steps**:
1. Advisory-profile project: `internal static class Gate { [SharpProof.EnforcePure] [SharpProof.Suppress("triage: precision gap")] internal static int Impure(int x) { Console.WriteLine(""); return x; } }` - analyzer silent, build green.
2. Flip only `SharpProofProfile=strict`; rebuild: analyzer still silent, but the manifest now carries the EnforcePure claim; the worker marks it Unknown/Refuted and the build fails via launcher exit 5/6 with no reference to the suppression.
3. Grep confirmation: no consumer of `ContractSelectionInventory.Suppress` exists outside the analyzer.
**Confidence**: Medium (behavioral divergence verified by exhaustive grep and quoted code; open question is design intent, but the undocumented profile-dependent meaning flip is factual either way).

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

### 409. [DEFERRED DESIGN] SPMETA010 Can Never Fire on the Only Production Semantic-Cache Write - Its Answer Grammar Recognizes a Fictional Vocabulary and Its Default Arm Flags Exactly the Answers the Domain Declares Cacheable

**Location**: `SharpProof.Meta.Analyzers\CacheSoundnessRules.cs` (Lines 385-394 `IsSemanticAnswerType`: namespace-root "SharpProof" AND name contains "Answer"/"Result"/"Outcome"; Lines 186-202 all recognition arms bottoming out in it; default arm Lines 200-201 flagging ANY non-constant value of such a type); sole production write `SharpProof.Worker\SharpProofWorker.cs` Lines 376-377 `cache.TryWriteAsync(response, snapshot.InputHash, manifest, projectBoundary.Token)` with `response` typed `WorkerVerifyResponse`; production vocabulary `WorkerRunStatus`/`WorkerRunFailureReason`/`WorkerClaimReason`/`WorkerCacheStatus` (no substring match); domain policy contradiction `SharpProof.Verify\Outcomes.cs` Lines 46-53 (`OutcomeCachePolicy.IsCacheable` declaring ProvenOutcome/RefutedOutcome THE cacheable answers). Calibration `SharpProofSoundnessAnalyzerTests.cs` (fictional `enum Answer { Unknown, TimedOut, Failed, Canceled, Proven }` chosen to satisfy the substring grammar).
**Description**: SPMETA010's message promises timeout/error/failure/Unknown answers may not be written to a semantic cache, but value recognition bottoms out in `IsSemanticAnswerType`. No member of the production answer vocabulary contains "Answer"/"Result"/"Outcome", so every arm (enum-member gated on the enum TYPE passing, object-creation, invocation-name, default) returns false for `WorkerVerifyResponse` and friends: the product's single semantic-cache write site can NEVER produce SPMETA010 even if the response were provably a canceled/malformed shape constructed inline. The rule fires only on synthetic shapes - the test suite calibrates it with a fictional enum whose NAME satisfies the substring grammar, so the regression net structurally cannot detect the mismatch (pinning-cements-wrong-calibration pattern accepted as #293). Polarity is also inverted relative to the domain: the default arm flags any non-constant reference typed *Outcome/*Result/*Answer in the SharpProof namespace, so `void N(ProvenOutcome p, ProofCache c) { c.Add("k", p); }` reports Error - flagging exactly the answer OutcomeCachePolicy.IsCacheable declares cacheable. Latent defense-layer loss, not wrong verdicts today. Distinctness stated explicitly per vetting: #186 covered cache IDENTIFICATION via the ISemanticCache marker interface independent of names; #284 covered local/field/compound ALIAS tracking once a write is recognized - both present and pinned; neither touches the VALUE-side grammar (`IsSemanticAnswerType`'s substring test), the WorkerVerifyResponse blind spot, or the cacheability polarity inversion. Not #352 (thread-safety), not #301/#302/#226 (runtime cache behavior).
**Reproduction Steps**:
1. In a Meta-Analyzers-attached project declare the marker interface, a `WorkerVerifyResponse`-shaped class and `WorkerRunStatus`-shaped enum under SharpProof-rooted namespaces, a `ProofCache : ISemanticCache` with `TryWriteAsync`, and analyze `void M(ProofCache c) => c.TryWriteAsync(new WorkerVerifyResponse(), "h");` - zero SPMETA010 regardless of any status facts.
2. Rename ONLY the parameter type to a synthetic `TimedOutAnswer`: SPMETA010 fires (object-creation arm) - isolating the type-name grammar as the discriminator.
3. Confirm the production path: grep `TryWriteAsync(` - the only ISemanticCache-typed call site is SharpProofWorker.cs Lines 376-377 with argument types none of which pass IsSemanticAnswerType.
4. Polarity control: analyze `void N(ProvenOutcome p, ProofCache c) => c.Add("k", p);` - SPMETA010 fires, contradicting OutcomeCachePolicy.IsCacheable(ProvenOutcome) == true.
**Confidence**: High on mechanism (grammar evaluated symbol-by-symbol against real type names; sole-write-site claim grep-established). Impact framed honestly as latent defense-layer loss.

**Status**: Deferred as a design decision rather than a production defect. The Worker cache write is guarded by `VerificationCache.IsCacheable`, which requires complete, replay-validated refutations and independently rejects unknown/error responses; current runtime tests cover those invariants. The analyzer fixtures use synthetic `Answer`/`ProofCache` names, so broadening the vocabulary or aligning it with `OutcomeCachePolicy` would require an explicit cache contract and could create false positives. No safe production change was justified in this audit.

### 412. [NOT REPRODUCED] SharpProof.Gates Was Enrolled Into the RS0030-as-Error Banned-API Regime Over Three Live Compilation-Mutation Call Sites - the Release-Certifying Project Cannot Compile Under the Repository's Own Mandatory pr-gates Build, and No Architecture Test Scans Mutation-API Usage

**Location**: wiring `Directory.Build.props` (Gates added to `SharpProofProductionProject` at Line 40; banned-api PackageReference + BannedSymbols.txt AdditionalFiles at Lines 82-87; `TreatWarningsAsErrors=true` + `WarningsAsErrors+=RS0030` at Lines 89-92); violating call sites `SharpProof.Gates\Performance\PerformanceGate.cs` (Lines 898-900 and 932-934: `currentCompilation.ReplaceSyntaxTree(currentTree, ...)` on a receiver chained from `AnalyzerGateHost.CreateCompilation`, which returns `CSharpCompilation` - AnalyzerGateHost.cs Line 41) and `SharpProof.Gates\Corpus\OpenSourceCorpusRunner.cs` (Lines 104-106: `template.RemoveSyntaxTrees(template.SyntaxTrees).AddSyntaxTrees(trees)` where `trees` is an `ImmutableArray<SyntaxTree>.Builder` - Line 21 - forcing the IEnumerable<SyntaxTree> overloads); ban entries `BannedSymbols.txt` Lines 3, 5, 7 (exact doc-IDs). Mandatory consumer `scripts\Invoke-SharpProofContainer.ps1` 'pr-gates' case: locked-mode restore + `build SharpProof.sln -c Release`.
**Description**: The enrolling commits put SharpProof.Gates under Microsoft.CodeAnalysis.BannedApiAnalyzers with RS0030 promoted to error under TWAE. Gates contains three statements invoking exactly the banned doc-ID overloads, with overload binding FORCED: `ImmutableArray<SyntaxTree>` and its Builder implement IEnumerable<SyntaxTree> but never implicitly convert to SyntaxTree[], so Remove/AddSyntaxTrees bind solely to the IEnumerable forms (Lines 5 and 7); ReplaceSyntaxTree(SyntaxTree,SyntaxTree) matches Line 3 directly. Critically, AddSyntaxTrees(IEnumerable)/RemoveSyntaxTrees(IEnumerable)/ReplaceSyntaxTree are NON-VIRTUAL members declared on the abstract Compilation base, so calls through CSharpCompilation-typed receivers bind to the exact banned declarations - no override-resolution subtleties. Each site therefore produces error-promoted RS0030, `dotnet build SharpProof.sln -c Release` fails inside SharpProof.Gates, and consequently the mandatory pr-gates command (and Invoke-SharpProofGateEvidence.ps1's Rebuild of Gates) cannot pass; no pragma, NoWarn, suppression file, or editorconfig carve-out exempts them (verified: the repo-wide NoWarn lists contain only CA/RS2008-family codes; Gates.csproj sets TWAE=true itself). The architecture suite provides no backstop: its inventories pin the FILE's contents, and its usage scans cover display/speculative/semantic-model families, not mutation APIs. Distinct from #324 (APIs MISSING from the ban lists - here they ARE listed and used live), #387 (GetSemanticModel overload coverage inside the same file - different section/mechanism), #285/#325/#413 (inventory-membership defects - different artifact pair and consequence), #195 (dead code): none covers live violations of existing bans created by the enrollment.
**Reproduction Steps**:
1. In the canonical container run `docker compose run --rm tooling pr-gates -Configuration Release`: locked-mode restore succeeds; the build fails inside SharpProof.Gates with error-promoted RS0030 at PerformanceGate.cs:898, PerformanceGate.cs:932, OpenSourceCorpusRunner.cs:105-106 naming Compilation.ReplaceSyntaxTree/AddSyntaxTrees/RemoveSyntaxTrees.
2. Control A: comment out Directory.Build.props Line 40's Gates arm and rebuild - Gates compiles clean, isolating the enrollment as activation.
3. Control B: keep enrollment but rewrite the three sites to non-mutating construction (or suppress the exact doc-IDs) - build goes green, isolating the call sites as sole violators.
4. Confirm the backstop hole: run SharpProof.ArchitectureTest against the enrolled state minus the fixes - every boundary test stays green despite the live banned calls.
**Confidence**: High on every statically verified link (ban entries verbatim; props wiring incl. TWAE; call sites verbatim with forced overload binding against non-virtual base declarations; absence of suppression). Residual uncertainty stated honestly: the container build itself was not executed (read-only wave), so the last mile rests on the banned-API analyzer reporting exact references to listed non-virtual base members - its core documented behavior.

**Status**: The claimed build failure is not reproduced on the current tree. A canonical Linux-amd64 Docker Release build of `SharpProof.Gates` completed with zero warnings and zero errors (parent and independent shard runs). The live mutation calls and the inventory-test coverage gap remain observations, but no gate or exit-code change is justified from this report. If enforcement scope is revisited, add a targeted mutation-API backstop or document a narrowly justified exception after inspecting evaluated analyzer inputs.

### 417. [DISPROVED] `tooling pilots` Is Dead on Arrival in the Canonical Linux Container - Test-SharpProofPilots.ps1 Builds Every Critical Path Through Windows-Only Backslash Segments

**Location**: `scripts\Test-SharpProofPilots.ps1` - Line 6 (`$OutputPath = 'artifacts\pilots\report.json'` default), Line 88 (`$pilotRoot = Join-Path $repositoryRoot 'eng\pilots'` feeding the Line-89 `Get-Content -LiteralPath (Join-Path $pilotRoot 'catalog.json')`), Line 154 (`wrapper = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'` embedded into the child-process JSON payload consumed as `& $payload.wrapper`), Line 194 (`Join-Path $projectDirectory 'obj\Release\net8.0\SharpProof'`, parent of every evidence path), Line 362 (`-CatalogPath (Join-Path $pilotRoot 'catalog.json')`). Execution premise: the canonical tooling image is Linux pwsh (PowerShell-debian base), and the script is wired into package-consumers.yml's release-qualification job.
**Description**: On Linux pwsh Join-Path concatenates the child segment verbatim after one platform separator ('\' is an ordinary filename character), so the script's first data read resolves `<repo>/eng\pilots/catalog.json` - a literally-named nonexistent leaf - and Line 89 throws ItemNotFoundException under $ErrorActionPreference='Stop' before any pilot is built. The later sites are independently fatal or corrupting: (a) the wrapper path handed to the spawned pwsh child raises CommandNotFound surfacing as a bogus restore failure; (b) `$artifactDirectory = ...\obj\Release\net8.0\SharpProof` points at literally-backslash-named directories while MSBuild publishes to forward-slash paths, so the freshness gate and evidence hashing target the wrong tree; (c) on success the default `-OutputPath` would write report.json to a root-level file literally named `artifacts\pilots\report.json`, outside artifacts/. Blast radius: the five-pilot release qualification gate can never pass and no pilots receipt can be produced; fail direction hard-red (availability), never false-green. Distinct from the accepted #190/#191 family: those occupy eng/acceptance/Verify.ps1's backslash paths - this is a NEW script/file instance (Test-SharpProofPilots.ps1), different lines and consumers, per the ledger precedent (#333 explicitly treats another file's instance as distinct); not #248 (generator defaults), #389 (samples pack redirection), #339 (dev staging bypass), #346 (samples CI orphan). No existing entry touches Test-SharpProofPilots.ps1 path construction.
**Reproduction Steps**:
1. In the canonical container run `docker compose run --rm tooling pilots -PackageSource nupkgs`.
2. Observe termination with `Get-Content : Could not find ... '/…/eng\pilots/catalog.json'` thrown from Test-SharpProofPilots.ps1 before any restore/build activity.
3. Static probe of the primitive on Linux: `pwsh -NoProfile -Command "Join-Path '/repo' 'eng\pilots'"` prints `/repo/eng\pilots`.
4. Hypothetically repair only the catalog read: execution advances to the next site (child pwsh fails invoking the backslash wrapper path), proving each site is an independent failure point; a successful run would deposit report.json at the root-level literal name.
**Confidence**: High (verbatim-backslash platform behavior is defined; sibling scripts use forward slashes exclusively; both consumers of this script run exclusively on Linux; residual uncertainty limited to pre-container-port history).

**Status**: Disproved in the canonical PowerShell 7.5/Linux container. PowerShell's `Join-Path` normalizes the backslash-containing child segments used by this script to the platform separator; the catalog, wrapper, project, artifact, and default output paths resolve to the expected repository locations. The direct probe `Join-Path '/repo' 'eng\\pilots'` produced `/repo/eng/pilots`, and the pilot path authority checks pass. No production change is warranted.

### 418. [DISPROVED] `tooling fuzz-nightly` Dies Before Executing Any Fuzz Run - Invoke-SharpProofFuzzCampaign.ps1 Reads Its Contract, Seed Manifest, Wrapper, and Project Through Backslash Paths After Startup Has Already Wiped Prior Evidence

**Location**: `scripts\Invoke-SharpProofFuzzCampaign.ps1` - Line 4 (`$OutputDirectory = 'artifacts\fuzz'` default), Line 28 (`Initialize-SharpProofFuzzEvidence` runs first), Lines 33-36 (`Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng\acceptance\contract.json')`), Lines 40-43 (`Join-Path $repositoryRoot 'eng\fuzz\retained-seeds.json'`), Line 88 (`Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'`), Line 92 (`Join-Path $repositoryRoot 'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj'`). Caller chain: nightly.yml -> `tooling fuzz-nightly` (Invoke-SharpProofContainer.ps1 Lines 304+, which first completes a full solution Release restore+build, then invokes with a forward-slash output directory).
**Description**: On Linux pwsh the campaign always terminates at Line 33 attempting to read `/repo/eng\acceptance\contract.json` - a literally-named nonexistent file - AFTER Line 28's Initialize has already deleted the previous night's campaign.json and rotating-/retained- fragments from the output directory. Net effect per scheduled run: hours of build time spent, last-good aggregated evidence destroyed, zero new evidence of any kind published - not even a failure summary (the throw precedes summary construction). The remaining sites are independently fatal: the retained-seed manifest read, the wrapper resolution passed into every spawned runner command line, and the fuzz project path in `dotnet run --project`; manual parameterless invocation additionally materializes output in a root-level directory literally named `artifacts\fuzz`. Fail direction: total loss of the nightly fuzz function (fail-red), never a green-but-hollow campaign. Distinct from #190/#191 (Verify.ps1 instances - NEW script/file instance), #282 (mod-397 seed-stream aliasing in the same script - different lines/mechanism), #344 (failed-campaign publication gating at Lines ~212-220 - code that is today UNREACHABLE because Line 36 throws first; complementary, not overlapping), #345 (numeric threshold coupling).
**Reproduction Steps**:
1. Run `docker compose run --rm tooling fuzz-nightly` against a healthy tree; note the solution Release build succeeds.
2. Observe the throw from Invoke-SharpProofFuzzCampaign.ps1 (`Could not find file '/…/eng\acceptance\contract.json'`), and confirm artifacts/fuzz/nightly/ retains neither campaign.json nor prior fragments (deleted at Line 28).
3. Static probe on Linux: `pwsh -NoProfile -Command "Join-Path '/repo' 'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj'"` -> `/repo/Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj`.
4. Contrast: temporarily copy eng/acceptance to a literal `eng\acceptance` directory - execution advances exactly one site and dies at the retained-seeds read, isolating per-site fatality.
**Confidence**: High (platform-certainty shared with #417; ordering verified - Initialize at Line 28 strictly precedes the contract read; sole caller and scheduled workflow both Linux).

**Status**: Disproved in the canonical PowerShell 7.5/Linux container. `Join-Path` normalizes the script's backslash-containing segments, and the nightly caller supplies a forward-slash output directory. The campaign path probes and evidence publication checks do not exhibit the claimed separator failure. The separate question of preserving prior evidence on a later preflight failure is covered by the resolved evidence-lifecycle work and is not this path claim.

### 419. [DISPROVED] Pilots Receipts Can Never Validate on Linux - Write-SharpProofQualificationReceipt.ps1 Forwards a Windows-Only Default Catalog Path Into the Pilot Authority, Which Launders the Load Failure Into a Generic "Stale or Failed" Verdict

**Location**: `scripts\Write-SharpProofQualificationReceipt.ps1` Line 16 (`$CatalogPath = (Join-Path $PSScriptRoot '..\eng\pilots\catalog.json')` default) forwarded at Lines 100-101 into `Test-SharpProofPilotReport` for the 'pilots' arm (Lines 97-103, throwing Line 106 "Qualification evidence is incomplete, stale, or failed: 'pilots'."); amplifying siblings sharing the identical default: `scripts\Test-SharpProofPilotReport.ps1` Line 7 with the load wrapped in try/catch whose `catch { return $false }` sits at Line 56; `scripts\Complete-SharpProofPilotReview.ps1` Line 7 forwarded at Lines 42-44 ahead of the Line-46 throw "The source pilot report is not valid unreviewed evidence.". Exhaustive grep confirms no production caller overrides -CatalogPath; only Test-SharpProofPilotAuthorityFixtures.ps1 passes an explicit temp-dir path (forward slashes).
**Description**: On Linux pwsh every production default resolves to a literally-named nonexistent leaf (`<repo>/scripts/..\eng\pilots\catalog.json`). Consequences in order of severity: (1) `tooling pilot-review` can NEVER succeed - Complete-SharpProofPilotReview validates a genuine unreviewed report against the broken path, Test-SharpProofPilotReport swallows the ItemNotFoundException into `return $false`, and Line 46 throws an objectively false diagnosis blaming valid evidence; (2) the receipt script's pilots arm evaluates false through the same laundering and throws the equally misleading stale-or-failed message; (3) therefore the pilots receipt demanded by the release qualification matrix is permanently unproducible - release qualification fails even given a valid report. Decisive intent evidence that the authority itself is sound and only the production defaults are broken: the authority's own fixture suite builds its catalog under temp dirs with forward slashes and passes explicit -CatalogPath, staying green in-container while every production entry point fails. Fail direction fail-closed but with corrupted attribution (valid evidence declared invalid). Distinct from #330/#331 (failure flattening/stale evidence in Invoke-SharpProofGateEvidence.ps1 - different script; the laundering shape rhymes with #330's catch-flattening applied to a different layer and cause), #250 (orphaned validator module), #389/#346 (samples script family), and from #417/#418 (different files and commands - pilot-review/receipt generation; survives fixing both).
**Reproduction Steps**:
1. Produce a genuinely valid artifacts/pilots/report.json plus review ledger.
2. In the canonical container run `docker compose run --rm tooling pilot-review`: observe "The source pilot report is not valid unreviewed evidence." although the report satisfies every schema rule.
3. Direct probe: `pwsh -File scripts/Write-SharpProofQualificationReceipt.ps1 -Gate pilots -EvidencePath <reviewed report>` in-container -> "Qualification evidence is incomplete, stale, or failed: 'pilots'."
4. Static isolation: print the resolved default (`Join-Path (Resolve-Path scripts).Path '..\eng\pilots\catalog.json'` -> literal-named leaf); run Test-SharpProofPilotAuthorityFixtures.ps1 (green) and observe it passes an explicit forward-slash -CatalogPath, proving the authority works whenever the path does.
**Confidence**: High on mechanism and reachability (defaults used by every production caller; grep-verified absence of overrides; fixture/production asymmetry read line-by-line).

**Status**: Disproved in the canonical PowerShell 7.5/Linux container. The default catalog paths in the receipt and review scripts are normalized by `Join-Path` to the existing `eng/pilots/catalog.json`; the production defaults do not create a literal-backslash path. No production change is warranted.
