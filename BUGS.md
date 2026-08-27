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

### 424. [CONFIRMED] Explicitly selected local functions are silently ignored instead of receiving SP0047

**Location**: SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs around
lines 116-122; SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs around
lines 5-34 and 383-387; SharpProof.Analyzer.Core/LanguageSubsetGate.cs around
lines 51-57 and 120-145.

**Description**: The analyzer registers a syntax action for local functions and
lambdas, but ValidateNestedCallableDeclaration only validates trusted and
suppression control attributes. It never applies feature selection or the
language-subset gate. Roslyn does not send local functions through the ordinary
method analysis path used here, and AnalyzeUnselectedOperationBlock explicitly
returns for MethodKind.LocalFunction and MethodKind.AnonymousFunction.
Consequently, a local function explicitly marked EnforcePure or otherwise
selected can receive no SharpProof diagnostic at all. The equivalent ordinary
method is analyzed, while LanguageSubsetGate itself classifies local functions
as UnsupportedCallable. This conflicts with the documented rule that an
explicitly selected unsupported callable receives SP0047 rather than silently
losing coverage.

**Reproduction**:

1. Compile a method containing
   [EnforcePure] static void SelectedLocal() { state = 1; }.
2. Include an equivalent ordinary EnforcePure method as an analyzer-activation
   control.
3. Run the built analyzer. The control produces SP0002, compiler diagnostics
   are empty, and the selected local function produces no diagnostic.

The reporting agent's disposable harness at
C:/w/audit-local-selected produced only:

    SP0002|19|Method 'SelectedOrdinary' is marked [EnforcePure], but its effects do not prove observable purity

**Impact**: Users can explicitly request proof or effect analysis for a local
callable and receive silence instead of either evidence or a fail-closed
unsupported-callable diagnostic. This creates an analyzer coverage blind spot
and makes selection appear to have succeeded.

**Root cause**: Nested-callable syntax handling is limited to control-attribute
validation, while every operation-block path intentionally excludes nested
callables. No reconciliation path owns selected nested callables.

**Recommended fix**: Resolve the nested callable IMethodSymbol in the syntax
action, reuse the ordinary feature-selection and suppression policy, and for
each selected unsuppressed nested callable report
SelectedAnalysisIncompleteRule with UnsupportedCallable. Record the semantic
outcome as Abstained so reconciliation and telemetry remain consistent. Keep
the existing control-attribute validation in the same path.

**Regression coverage**: Add cases for an impure selected local function and an
attributed lambda expecting exactly SP0047, an unannotated nested callable
expecting silence, and a selected plus suppressed nested callable retaining the
documented suppression behavior.

**Confidence**: High; reproduced with an exact analyzer harness outside the
repository and traced through all registered nested-callable analysis paths.

### 425. [CONFIRMED] Nullable string concatenation ignores null tags and produces spurious SMT counterexamples

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 329-335, 548-549,
603-610, and model decoding around lines 469-471.

**Description**: A nullable string variable is encoded as an unconstrained
sequence payload plus an independent Boolean null tag. StringConcat passes the
raw sequence payloads directly to Z3 MkConcat and ignores the null tags. The IR
interpreter follows C# concatenation semantics and treats a null string operand
as the empty string. When a variable is null, Z3 can therefore choose an
arbitrary nonempty hidden payload and refute a property that is universally
true under interpreter semantics. Model decoding discards that hidden payload
and returns Null, so replay rejects the spurious counterexample.

**Reproduction**: Verify the tautology:

    text != null || text + "x" == "x"

The canonical Linux-amd64 probe reported:

    interpreter null-case goal=Value/True
    backend=Satisfiable failure=None text=Null
    kernel=UnknownOutcome reason=CounterexampleReplayFailed

The raw backend result is satisfiable even though the decoded null model
satisfies the property in the interpreter.

**Impact**: The public SMT backend returns false SAT and ProofKernel degrades a
provable query to Unknown/CounterexampleReplayFailed. Replay prevents a false
refutation, and the current worker proof-domain gate rejects string
concatenation, so this is a precision and backend-correctness defect rather than
a present false worker verdict.

**Root cause**: Nullness is modeled separately, but StringConcat consumes only
EncodedValue.Value. Existing coverage checks a different null case and does not
constrain the hidden payload.

**Recommended fix**: Normalize each concatenation operand to
ITE(IsNull, EmptySeq, Value) before MkConcat, or globally assert the invariant
IsNull implies Value equals EmptySeq for every nullable string symbol. Preserve
the existing Defined and non-null result semantics.

**Regression coverage**: Add left-null and right-null variable tautologies and
require backend Unsatisfiable plus kernel Proven. Retain a non-null variable
control and the existing interpreter/model replay checks.

**Confidence**: High; independently reproduced by two audit agents with
canonical probes and confirmed against the encoder and decoder paths.

### 426. [CONFIRMED] Acceptance skip-mode tests abort before exercising status semantics because their fixture omits a required module

**Location**: SharpProof.ArchitectureTest/AcceptanceScriptTests.cs around
lines 139-180 and WriteHarness around line 237; eng/acceptance/Verify.ps1 around
line 240.

**Description**: WriteHarness constructs a temporary repository and copies
SharpProof.FuzzEvidenceLifecycle.ps1, but it does not copy
scripts/SharpProof.ContainerExecution.psm1. The retained preflight prefix of
Verify.ps1 imports that module and calls Get-SharpProofTestProjectParallelism.
Every SkipModesCannotProduceQualifyingAcceptanceEvidence case therefore exits
during preflight, before the test can distinguish passed from incomplete
evidence. The catch path rethrows a generic ScriptHalted exception, obscuring
the missing fixture dependency.

**Reproduction**: Run the four parameterizations of
SkipModesCannotProduceQualifyingAcceptanceEvidence in the canonical container.
All four exit 1 at VerifyHarness.ps1 instead of returning the expected evidence.
In a disposable copy, adding only SharpProof.ContainerExecution.psm1 to the
fixture made all AcceptanceScriptTests pass: 19 passed, 0 failed.

**Impact**: The Architecture suite is red and the intended release-evidence
invariant is not tested. A real regression in acceptance passed/incomplete
classification could be hidden behind the fixture's earlier module failure.

**Root cause**: Commit 785c64391 added a new Verify.ps1 module dependency
without updating the source-slicing fixture. Later preflight refactoring kept
the dependency in the retained prefix.

**Recommended fix**: Copy SharpProof.ContainerExecution.psm1 into the fixture's
scripts directory beside SharpProof.FuzzEvidenceLifecycle.ps1. Longer term,
make the harness dependency list explicit or introduce a test seam after
preflight so source slicing cannot silently omit imported modules.

**Regression coverage**: Keep all four skip-mode cases, assert exit 0 and exact
passed/incomplete evidence, and add a fixture-integrity assertion that every
module imported by the retained prefix exists before execution.

**Confidence**: High; reproduced red at HEAD and green in a disposable
one-change probe.

### 427. [CONFIRMED] Structural complexity ratchets are stale and make the canonical Architecture suite fail

**Location**: eng/acceptance/algorithm-size-ratchets.json and
SharpProof.ArchitectureTest/ArchitectureTests.cs, with violations in
SharpProof.Frontend/RoslynOperationLowerer.cs,
SharpProof.Frontend/RoslynProgramLowerer.cs,
SharpProof.Dataflow/ForwardDataflowAnalysis.cs,
SharpProof.Dataflow/IntervalDomain.cs,
SharpProof.Effects/OperationEffectScanner.cs, and
SharpProof.Effects/OperationEffectScanner.Assignments.cs.

**Description**: AlgorithmLayersStayWithinStructuralComplexityCaps fails with
nine deterministic violations:

- RoslynOperationLowerer.cs: 2960 expressions over 2792 and 155 decisions over
  150.
- VisitBinaryOperator: 353 expressions over 340.
- RoslynProgramLowerer.cs: 1956 expressions over 1920.
- ForwardDataflowAnalysis.cs: 512 expressions over 495.
- ForwardDataflowAnalysis.AnalyzeCore: 353 expressions over 350.
- IntervalDomain.Create: 173 expressions over 150.
- OperationEffectScanner.cs: 265 decisions over 256.
- OperationEffectScanner.Assignments.cs: 437 expressions over 435.

The ratchet manifest was last updated by 3a04e6c8b, while all six measured
sources changed afterward. The production-ceiling rationale test still passes,
so the failure is not a Roslyn measurement or inventory anomaly.

**Impact**: The canonical test suite cannot pass, preventing the architecture
gate from distinguishing new complexity growth from already-landed growth.
Simply leaving the suite red also removes the practical enforcement value of
the ratchet for subsequent changes.

**Root cause**: Recent correctness work increased measured expressions and
decisions without decomposing affected members or reviewing the corresponding
ratchets.

**Recommended fix**: First extract and simplify the three member-level
violations, especially VisitBinaryOperator, AnalyzeCore, and IntervalDomain.Create.
Split file-level responsibilities where that materially reduces complexity.
Only after review, update unavoidable file-level ceilings to the measured
values with a recorded rationale. Do not raise the separate aggregate
production contract and do not delete assertions.

**Regression coverage**: Run the focused
AlgorithmLayersStayWithinStructuralComplexityCaps test plus Frontend, Dataflow,
and Effects suites after decomposition. Require the ratchet to pass from a clean
canonical build.

**Confidence**: High; reproduced by two agents and by the canonical full test
run with identical metrics.

### 428. [CONFIRMED] Fuzz case-seed truncation silently repeats cases within supported campaigns

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs around lines 152, 197, 263,
and 562-572; existing coverage in
SharpProof.Fuzz.Test/FuzzRunnerTests.cs around lines 88-105.

**Description**: CreateCaseSeed hashes the 64-bit seed/index pair through a
bijective SplitMix64 transform, then returns only the low signed 32 bits.
Truncation destroys injectivity. Equal case seeds drive the frontend, finite
SMT, and partial SMT generators, so a collision repeats the entire semantic
case bundle while both entries are counted as distinct cases and agreements.

**Reproduction evidence**:

- Seed 20260523, 10,000 requested cases: 9,999 unique; indices 5055 and 6447
  both produce 81445770.
- Seed 20260605, 10,000 requested cases: 9,999 unique; indices 3773 and 7592
  both produce 1860187170.
- Retained seed 23063 at MaximumCases 1,000,000: 999,890 unique; 110
  duplicates. The first collision is indices 28670 and 48952 producing
  -1505345985.

The frontend generator uses caseSeed xor 0x35A1D7, the finite SMT generator
uses caseSeed xor 0x6C8E9CF5, and the partial SMT generator uses caseSeed xor
0x243F6A88. Each downstream generator is otherwise deterministic, so a
collision repeats the observable generated case. The current regression checks
only 1,000 indices and misses scheduled nightly collisions.

**Impact**: Fuzz evidence overstates unique semantic coverage. The scheduled
10,000-case campaign already has a known duplicate, and maximum-size campaigns
can count many repeated bundles as independent agreements.

**Root cause**: A 64-bit permutation is projected into a 32-bit seed without a
within-campaign uniqueness guarantee.

**Recommended fix**: Use a seed-keyed bijective 32-bit permutation of the case
index. A keyed multi-round Feistel permutation or another explicitly invertible
32-bit construction preserves uniqueness for every fixed base seed. Inject the
base-seed key between rounds so different campaign seeds do not become simple
shifted streams.

**Regression coverage**: Pin both known collision pairs, assert one million
indices for retained seed 23063 are distinct, and retain the existing
cross-seed shifted-stream test.

**Confidence**: High; exact unchecked UInt64 arithmetic was reproduced outside
the repository and every downstream entropy path was traced.

### 429. [CONFIRMED] SPMETA010 drops earlier reaching values after two conditional assignments

**Location**: SharpProof.Meta.Analyzers/CacheSoundnessRules.cs around
lines 253-294.

**Description**: ResolveLocal gathers all preceding writes to a local, but if
the last write is conditionally executed it analyzes only writes[-2] and
writes[-1]. Earlier reaching values are discarded. This leaves a normal
control-flow bypass:

    var answer = Answer.Unknown;
    if (first) answer = Answer.Proven;
    if (second) answer = Answer.Proven;
    cache.Write(answer);

When both conditions are false, Answer.Unknown reaches the semantic cache. The
analyzer inspects only the two Proven right-hand sides and emits no SPMETA010.

**Reproduction**: A canonical exact-source probe using ProofCache :
ISemanticCache reported:

    COMPILER_ERROR_COUNT=0
    SPMETA010_COUNT=0
    CONTROL_SPMETA010_COUNT=1

The control writes the direct Unknown value and proves the analyzer was active
and recognized the vocabulary. This is distinct from the older two-write loop
case: the current implementation handles its previous and last writes, but
still drops the third predecessor.

**Impact**: The static guard against caching transient, unknown, or abstaining
semantic answers can be bypassed with ordinary sibling conditionals. Runtime
validation may still protect current production caches, but the soundness
analyzer does not enforce the policy it claims to enforce.

**Root cause**: A last-two-write syntax heuristic substitutes for a
control-flow reaching-definition join.

**Recommended fix**: Replace the heuristic with CFG-based may-be-noncacheable
state analysis. Join all feasible predecessor states at conditionals and loops,
and clear a noncacheable state only when an unconditional assignment dominates
the cache write. Preserve nested-callable and alias handling.

**Regression coverage**: Add the three-write sibling-conditional case expecting
exactly one SPMETA010 and an unconditional final Answer.Proven assignment
control expecting none. Add a four-predecessor switch or nested-conditional
case to prevent another fixed-depth heuristic.

**Confidence**: High; reproduced with zero compiler errors against the exact
Meta analyzer sources and a positive direct-write control.

### 430. [CONFIRMED] Failed performance-gate preflight can leave stale passing evidence for CI upload

**Location**: scripts/Invoke-SharpProofGateEvidence.ps1 around lines 23-40;
scripts/Invoke-SharpProofContainer.ps1 around lines 43-44, 80-90, and 237-245;
eng/container/entrypoint.sh around lines 178-188; .github/workflows/ci.yml
around lines 37-52.

**Description**: Invoke-SharpProofGateEvidence computes test-project
parallelism before it resolves and purges its output, raw-output, and stderr
files. Its pr-gates and performance callers perform additional failure-prone
parallelism, restore, and build work before invoking that producer. Container
entry preserves the artifacts directory, and CI uploads it with an
always-running artifact step. A failed rerun can therefore retain and upload a
previous successful performance.json.

**Reproduction**: In a disposable minimal repository, seed all three owned
evidence files and use a contract with CPU divisor 0 so
Get-SharpProofTestProjectParallelism fails. The script exits 1 with "The
test-project CPU divisor must be positive", while output, raw output, and stderr
sentinels all remain present.

**Impact**: A later failed gate can expose stale passing evidence to CI,
operators, or downstream automation. Embedded identity may let a careful
consumer detect staleness, but the artifact lifecycle itself incorrectly
preserves a prior success.

**Root cause**: Evidence invalidation is nested inside a producer reached only
after caller preflight/build work, and a later parallelism preflight was placed
above even the producer's own purge.

**Recommended fix**: Centralize owned performance-evidence invalidation or a
failure tombstone at command entry before parallelism, restore, and build.
Also move the producer's parallelism lookup below that purge. Ensure every
entrypoint that owns these paths uses the same lifecycle helper.

**Regression coverage**: Seed all owned outputs in a persistent-artifacts
fixture, independently force producer-parallelism failure and outer-build
failure, and assert no prior success survives. Replace the current
contains-only architecture assertion with an executable ordering fixture.

**Confidence**: High; the agent reproduced the stale files in a temp fixture
and traced the complete persistent-artifact and always-upload path.

### 431. [CONFIRMED] API-spec generators accept ambiguous duplicate JSON properties

**Location**: scripts/Generate-ApiSpecCatalog.ps1 around line 584;
SharpProof.Specs.Test/Generate-ApiSpecRuntimeWitnesses.ps1 around line 62;
SharpProof.Specs.Test/DefaultApiSpecCatalogGenerationTests.cs around line 18;
consumer identity in SharpProof.Worker/WorkerInputSnapshot.cs around line 63.

**Description**: The API-spec catalog is the human-reviewed source of truth,
but its generators parse it with ConvertFrom-Json and parity tests read it with
JsonDocument.GetProperty. Neither path recursively rejects duplicate object
property names. Both select a last-wins value, so contradictory reviewed
properties can generate code and still pass parity.

**Reproduction**: In an isolated canonical container, duplicate the root
property:

    "tableVersion": "5",
    "tableVersion": "shadow",

The generator exits 0 and emits:

    public const string DefaultTableVersion = "shadow";

JsonDocument.GetProperty also returns shadow, so the parity test agrees with
the ambiguous generator input and remains green.

**Impact**: Contradictory root or nested catalog values can silently change
generated claims, protocol summaries, and cache/input identity while the
review-source parity tests pass.

**Root cause**: The catalog has no strict duplicate-property preflight before
PowerShell or .NET JSON conversion. Sibling generators already implement this
class of validation, but the API-spec paths do not share it.

**Recommended fix**: Add a shared strict catalog reader that recursively walks
every JsonDocument object with an ordinal property-name set and rejects
duplicates before conversion. Use it in both API-spec generators and in parity
source loading, with path-qualified errors.

**Regression coverage**: Require nonzero generation for duplicate root
tableVersion and a nested duplicate such as facets.nullness.result. Add a
parity-reader test proving rejection occurs before value comparison.

**Confidence**: High; reproduced in the canonical container, including the
green last-wins parity behavior.

### 432. [CONFIRMED] Protocol response compaction materializes the full expanded JSON before enforcing the size limit

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs around lines 137-151;
existing large-shape coverage in
SharpProof.Worker.Test/ProtocolJsonTests.cs around line 86.

**Description**: SerializeResponse first calls JsonSerializer.Serialize for the
fully expanded response, then measures its UTF-8 size, and only afterward calls
CompactClaimAssumptions. Claim evidence can grow as
O(assumptions multiplied by claims), while the manifest itself grows only
linearly. The nominal 16 MiB protocol cap is therefore a post-allocation check,
not a memory bound.

**Reproduction**: A disposable probe against the built protocol-11 assembly
created a valid 400-assumption, 100-claim response. The compact result was
488,056 UTF-8 bytes and round-tripped as valid, but the initial serialization
allocated 115,969,504 bytes before compaction. It retained 99 compact claim rows
with one used assumption.

**Impact**: A valid sub-limit input can exhaust worker memory before compaction
or the typed response-too-large path runs. An allocation failure can terminate
the worker without publishing worker.response_too_large.

**Root cause**: The output-size limit is evaluated only after materializing an
unbounded expanded string.

**Recommended fix**: Serialize the first pass into a capped UTF-8
IBufferWriter<byte> or stream that stops at MaximumJsonBytes plus one. On
overflow, compact assumptions and serialize again through the same bounded
writer. Never materialize the full expanded string.

**Regression coverage**: Reuse the valid 400 by 100 fixture, prove validation
before serialization and after compact round-trip, assert the first writer
cannot retain or write beyond the cap, and add an allocation regression showing
memory remains proportional to MaximumJsonBytes rather than expanded payload
size.

**Confidence**: High; the reporting agent measured the allocation and validated
both expanded input and compact round-trip.

### 433. [CONFIRMED] Non-completing member initializers do not suppress unreachable constructor call-site diagnostics

**Location**: SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs around
lines 153-185; SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs around
lines 330-358, 409-444, 503-510, and 555-627;
SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines 111-145;
SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs around lines 90-196.

**Description**: C# executes instance field and property initializers before an
explicit constructor initializer, a primary-constructor base initializer, and
the constructor body. AnalyzerFeaturePipeline tracks non-completion only when
deciding whether a later member initializer is reachable. Ordinary constructor
call-site analysis, injected base/this initializer calls, and primary
constructor base analysis do not consume that prefix reachability. If an
earlier initializer definitely does not complete, SharpProof still analyzes
calls that the runtime cannot execute and emits SP0027.

**Reproduction**: The reporting agent used two types:

- Explicit has private readonly int stop = Guard.Fail(), then an explicit
  base(-1) initializer and Guard.Positive(-2) in its constructor body.
- Primary(int marker) has the same throwing field initializer and Base(-3) as
  its primary-constructor base call.

The analyzer emitted three SP0027 diagnostics: explicit base, explicit body
call, and primary base. A separate runtime-order control emitted only
explicit-field and primary-field; no base-argument, base-body, or
constructor-body event executed.

**Impact**: The analyzer produces deterministic false precondition-violation
diagnostics and disproven semantic outcomes for unreachable code. Warnings as
errors can reject valid builds solely because an earlier initializer prevents
constructor entry.

**Root cause**: Member-initializer reachability is implemented as a private
ordering check used only by AnalyzeMemberInitializer. Constructor and primary
constructor Requires discovery build independent roots without joining the
applicable initializer prefix.

**Recommended fix**: Factor initializer ordering from
CanReachMemberInitializer into a shared
AllApplicableMemberInitializersMayComplete helper keyed by containing type and
static/instance context. Gate ordinary and static constructor call-site
analysis and AnalyzePrimaryConstructorInitializer on that prefix. Preserve
contract placement and intrinsic validation; classify unreachable constructor
call sites as NotApplicable rather than Proven.

**Regression coverage**: Add a throwing instance initializer before explicit
base and body calls, a throwing initializer before a primary base call, and a
static-initializer/static-constructor equivalent. Completing-initializer
controls must retain the existing SP0027 diagnostics.

**Confidence**: High; the agent reproduced all three diagnostics and verified
the actual runtime event order in independent disposable projects.

### 434. [CONFIRMED] Every bounded protocol-file read allocates the full 16 MiB limit even for tiny files

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs around lines 40,
70, and 118-134; amplified consumers in
SharpProof.Worker/VerificationCache.cs around lines 55 and 125 and
SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs around line 48.

**Description**: The synchronous reader allocates a new
byte[MaximumJsonBytes], and the asynchronous reader allocates
byte[MaximumJsonBytes + 1], for every input. OpenJsonStream already checks the
actual stream length against the limit, so using the upper bound as the initial
buffer size is unnecessary.

**Reproduction**: After warmup, the agent read a 1,042-byte protocol project
file through the built protocol assembly and measured:

    ReadBytesFileAllocated    = 16,873,664 bytes
    ReadUtf8FileAllocated     = 16,866,192 bytes
    ReadUtf8FileAsync total   = 16,828,136 bytes
    FileReadAllBytesAllocated = 3,328 bytes
    ReadBytesAmplification    = 16,193.5x

**Impact**: Every tiny request, result, cache entry, or publication read creates
a large-object-heap allocation. A small cache hit reads the candidate twice and
can allocate roughly 32 MiB; validation paths with four reads can transiently
allocate roughly 64 MiB. Repeated verification adds avoidable GC pressure,
timing-envelope failures, and memory-failure risk.

**Root cause**: MaximumJsonBytes is used as buffer capacity rather than solely
as a validated upper bound.

**Recommended fix**: Have OpenJsonStream return the checked file length,
allocate exactly that many bytes, read exactly the expected length, and then
perform one additional byte read to detect post-open growth. Apply the same
pattern asynchronously. Continue rejecting shrink/growth inconsistencies,
oversized data, invalid UTF-8, and cancellation.

**Regression coverage**: Add warmed allocation tests for synchronous bytes,
synchronous UTF-8, and asynchronous UTF-8 with a small fixture and a generous
sub-1-MiB ceiling. Retain exact-limit, oversized, growth, cancellation, BOM, and
invalid-UTF-8 cases.

**Confidence**: High; measured against the HEAD protocol assembly with a
standard-library allocation control.

### 435. [CONFIRMED] SMT string variables admit malformed UTF-16 values that the IR domain rejects

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 117-126, 295-296,
329-334, and 496-513; SharpProof.Ir/Utf16WellFormedness.cs around lines 11-28.

**Description**: SMT strings are unconstrained Seq<Int> payloads plus null
tags. Solver constraints do not require code units to be in the UTF-16 range or
surrogates to form valid pairs. IrFactory and the interpreter reject lone
surrogates, and model decoding later enforces that restriction. The solver can
therefore refute a theorem with a value that does not exist in the IR domain,
then fail while decoding its own model.

**Reproduction theorem**:

    left == null ||
    right == null ||
    left.Length != 1 ||
    right.Length != 1 ||
    left + right != "\uD83D\uDE00"

No well-formed one-code-unit strings can hold the two isolated halves of the
emoji surrogate pair, so the theorem is universally true in the IR domain. Z3
uses left = [0xD83D] and right = [0xDE00], both non-null and length one, then
concatenates them into the emoji. The canonical probe reported:

    IR rejects isolated surrogate=True
    backend=Unknown failure=MalformedResult
    kernel=UnknownOutcome reason=MalformedBackendResult

**Impact**: Supported theorems can become fatal malformed-backend outcomes
instead of Proven. This is fail-closed, not a false green, but it is a direct
domain mismatch between the public SMT backend and the IR interpreter.

**Root cause**: String literals and decoded models are validated, but symbolic
string variables receive no well-formed UTF-16 invariant.

**Recommended fix**: Constrain each non-null symbolic string to the regular
language:

    ([0000-D7FF] | [E000-FFFF] | [D800-DBFF][DC00-DFFF])*

A Seq<Int> regex-membership assertion guarded by the inverse null tag enforces
both the 16-bit range and surrogate pairing without quantifiers. Retain decoder
validation as defense in depth.

**Regression coverage**: Add the theorem above beside the non-BMP UTF-16 length
tests and require direct backend Unsatisfiable plus kernel Proven. Retain a
valid emoji literal/model control.

**Confidence**: High; the agent reproduced IR rejection, backend malformed
result, and kernel outcome in a bounded canonical probe. This is distinct from
the null-concatenation defect because both null tags are false.

### 436. [CONFIRMED] Failed pilots reruns preserve a stale passing report and qualification receipt

**Location**: scripts/Test-SharpProofPilots.ps1 around lines 6, 12-19,
92-106, and 371-377; scripts/Invoke-SharpProofContainer.ps1 around
lines 411-422; scripts/Write-SharpProofQualificationReceipt.ps1 around
lines 24-27, 97-106, and 120-149; persistent artifact setup in
eng/container/entrypoint.sh around lines 178-188; release consumption in
scripts/Invoke-SharpProofReleaseContainer.ps1 around lines 175-210.

**Description**: Test-SharpProofPilots owns its report only at the end of a
successful run. Imports, parallelism, catalog loading, package-source
validation, clean-tree checks, and all pilot work can fail before the old report
is touched. The container wrapper invokes the receipt writer only after the
pilot script succeeds, and the receipt writer validates before overwriting.
Consequently, a failed retry leaves both the previous passing pilot report and
its qualification receipt intact.

**Reproduction**: In a disposable fixture, seed the pilot report with
stale-passing-report and the pilots qualification receipt with
stale-passing-receipt, then invoke Test-SharpProofPilots.ps1 with a missing
package source. The reporting agent observed:

    EXIT_CODE=1
    REPORT_SURVIVED=True
    REPORT_CONTENT=stale-passing-report
    RECEIPT_SURVIVED=True

The failure occurred at package-source validation before either evidence file
was owned by the new attempt.

**Impact**: The retry correctly exits nonzero, but artifact presence and
content still describe the previous passing attempt. On the same commit and
package set, release qualification accepts a receipt whose commit, evidence
hash, and package identity match; it has no attempt-recency field. A later or
manual qualification can therefore accept the stale pair as if it represented
the latest pilots attempt. Persistent container artifacts and always-upload CI
steps expose the stale evidence even when fresh hosted runners reduce
cross-workflow frequency.

**Root cause**: Pilot report and receipt ownership is split across scripts, and
both use publish-only-on-success without command-entry invalidation or an
attempt-bound pending/failure state.

**Recommended fix**: At pilots command entry, before parallelism, package, and
clean-tree checks, atomically remove or replace both the report and receipt with
pending/failure tombstones bound to the current commit and attempt identifier.
Publish the passing report/receipt pair atomically only after all pilots
succeed. Direct Test-SharpProofPilots.ps1 invocation must apply equivalent
report invalidation.

**Regression coverage**: In a persistent-artifacts fixture, create a valid
same-commit report and receipt, rerun with a missing package source and
separately with a child-pilot failure, require the old pair to be absent or
failure-tombstoned, and prove release qualification rejects it.

**Confidence**: High; reproduced in a temp-only fixture and traced through
report production, receipt production, persistent storage, and release
consumption.

### 437. [CONFIRMED] The verifier supervisor outlives an abruptly terminated MSBuild host

**Location**: SharpProof.BuildTasks/RunVerifier.cs around lines 205-225 and
269-271; SharpProof.BuildTasks/VerifierProcessSupervisor.cs around lines 77-91
and 140-143; contrast with worker-only parent protection in
SharpProof.Host/LinuxWorkerProcess.cs around lines 146-166.

**Description**: RunVerifier starts the supervisor under setsid but passes no
expected parent PID. After sending the nonce gate it deliberately closes
stdin, so later pipe EOF cannot represent MSBuild host death. The supervisor
handles explicit SIGTERM and SIGINT, then waits for its direct child
indefinitely. It neither installs PR_SET_PDEATHSIG nor checks getppid. Existing
parent-death protection covers the worker when the launcher dies, not the full
supervisor tree when MSBuild itself disappears.

**Reproduction evidence**: A bounded canonical Linux lifecycle probe launched
setsid sleep 15 from a short-lived parent shell. After the parent exited, the
child remained alive:

    setsid_child_survived_parent_exit=1

This confirms that setsid supplies session isolation rather than
parent-lifetime coupling, matching the supervisor's source path.

**Impact**: An MSBuild crash, OOM, or forced termination can leave the
supervisor, wrapper, launcher, and worker alive. They may continue consuming
CPU and memory for the remaining project budget, or longer if the launcher
wedges, after the owning build no longer exists.

**Root cause**: The containment protocol authenticates and cleans descendants
after task-directed termination but has no authenticated parent-liveness
contract for abrupt host death.

**Recommended fix**: Pass Environment.ProcessId to the supervisor separately
from verifier arguments. Before arming, install prctl(PR_SET_PDEATHSIG,
SIGTERM), register termination handling, then verify getppid still equals the
supplied expected parent to close the setup race. If the parent is already
gone, enter the existing cancellation/descendant-cleanup path without spawning
the verifier child.

**Regression coverage**: Add a Linux integration fixture whose short-lived
parent arms a supervisor around a long-running helper, records supervisor and
descendant PIDs, and exits abruptly. Require both PIDs to disappear within the
cleanup bound. Add a parent-already-gone race case expecting exit 125 and no
SharpProof.Armed/1 record.

**Confidence**: High; the agent traced every lifecycle edge and reproduced the
underlying setsid behavior in the canonical environment.

### 438. [CONFIRMED] IR schema generation and parity accept duplicate JSON properties

**Location**: scripts/Generate-IrModel.ps1 around line 168 and
SharpProof.Ir.Test/IrModelSchemaTests.cs around line 71.

**Description**: The authoritative IR vocabulary schema is parsed with
ConvertFrom-Json, and the runtime parity test reads properties with
JsonDocument.GetProperty. Both silently select the last duplicate object
property. A contradictory schema can therefore pass both generated-source
verification and runtime-shape parity.

**Reproduction**: In an ephemeral canonical container, change the schema root
to:

    "schemaVersion": 999,
    "schemaVersion": 1,

Generate-IrModel.ps1 -Verify exits 0 and reports:

    Verified SharpProof.Ir/IrModel.generated.cs and IR identifier aliases.

The parity primitive independently returns PARITY_VALUE=1 for the same input.
Because the last value matches generated output, neither authority detects the
contradictory review source.

**Impact**: Root or nested IR declarations can be ambiguous while all
generation and parity gates remain green. Reviewers cannot know which duplicate
was intended, and a last-wins edit can silently change the generated semantic
vocabulary.

**Root cause**: Neither PowerShell conversion nor .NET parity loading performs
a recursive duplicate-property preflight.

**Recommended fix**: Reuse a strict, ordinal JSON reader that walks every
object and rejects duplicate names before conversion or GetProperty lookup.
The declarative-model generator already provides a suitable implementation
pattern.

**Regression coverage**: Run the generator on temporary schemas with duplicate
root schemaVersion and nested declaration name properties, require nonzero exit
with path-qualified errors, and add a strict schema-reader parity test that
fails before value comparison.

**Confidence**: High; the reporting agent reproduced both the green generator
verify and the green last-wins parity primitive in the canonical container.

### 439. [CONFIRMED] Record copy constructors are treated as owners of instance member initializers

**Location**: SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs around
lines 472-487 and 524-552; existing record-copy identification in
SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines 382-422.

**Description**: AnalyzeMemberInitializer builds its candidate owners from
every type.InstanceConstructor. That includes record copy constructors, but C#
copy construction does not execute instance field or property initializers.
The repository already encodes this semantic distinction when discovering
implicit base-constructor calls, but member-initializer analysis does not reuse
it.

**Reproduction**: A record has an initializer calling Guard.Positive(-1), a
suppressed ordinary constructor, and an unsuppressed user-declared protected
copy constructor. The analyzer still emits:

    SP0027|14|Call to 'Positive' violates precondition 'false'

Since the only actual initializer-running constructor is suppressed, the
diagnostic can only be attributed to the copy constructor. A runtime control
prints:

    ordinary=1
    copy=0

confirming that ordinary construction executes the initializer and with/copy
construction does not.

**Impact**: Records receive false SP0027 diagnostics and incorrect semantic
outcomes for copy constructors. Constructor-level suppression or selection on
the constructors that really execute initializers is defeated by a callable
that never owns that initialization.

**Root cause**: InstanceConstructors is consumed without filtering the special
record-copy constructor, despite an existing IsRecordCopyConstructor predicate
elsewhere in the analyzer.

**Recommended fix**: Factor or reuse the existing record-copy predicate and
exclude copy constructors only from AnalyzeMemberInitializer's owner list.
Continue ordinary callable analysis of the copy-constructor body itself.

**Regression coverage**: Add
RecordCopyConstructorDoesNotOwnMemberInitializers with an invalid Requires call
in a record field initializer, suppressed ordinary constructor, and
unsuppressed explicit copy constructor, expecting no SP0027. Add an unsuppressed
ordinary-constructor control expecting one SP0027.

**Confidence**: High; the agent reproduced the diagnostic and separately
verified the runtime initialization counts.

### 440. [CONFIRMED] SPMETA010 misses semantic-cache writes through tuple deconstruction aliases

**Location**: SharpProof.Meta.Analyzers/CacheSoundnessRules.cs around
lines 61-88 and 115-122.

**Description**: Deconstruction analysis recurses element-by-element only when
both the assignment target and right-hand side are ITupleOperation. When the
right-hand side is a tuple-typed local reference or an invocation, the rule
falls through with the whole tuple target. AnalyzeAssignmentTarget recognizes
only property and field targets, so it never identifies the semantic-cache
element inside the tuple.

**Reproduction**: Assign a tuple containing Answer.TimedOut through either a
local alias or factory:

    (cache.Latest, stamp) = pair;
    (cache.Latest, stamp) = Create();

The receiver is ProofCache : ISemanticCache and Latest has the recognized
SharpProof.Verify.Answer type. The canonical exact-source probe reported:

    INLINE_TUPLE_CONTROL_SPMETA010_COUNT=1
    LOCAL_TUPLE_ALIAS_SPMETA010_COUNT=0
    FACTORY_TUPLE_ALIAS_SPMETA010_COUNT=0

Compiler error counts were zero in all three cases. The inline tuple control
proves the rule and vocabulary were active.

**Impact**: A transient or abstaining answer can be stored in a semantic cache
with no SPMETA010 by introducing a tuple local or tuple-returning factory. This
is independent of the local reaching-write defect and survives a fix to the
answer vocabulary.

**Root cause**: Tuple target decomposition is coupled to one syntactic shape of
the source value instead of the target's conversion/deconstruction mapping.

**Recommended fix**: Decompose tuple targets regardless of whether the source
is an inline tuple. Map source tuple elements through conversions, locals, and
invocation return types; when an element value cannot be resolved, conservatively
classify recognized semantic-answer storage as potentially noncacheable.

**Regression coverage**: Add inline tuple, tuple-local alias, and factory-return
cases with the same Answer.TimedOut element. Require one SPMETA010 for every
unsafe cache property target and retain a fully cacheable tuple control.

**Confidence**: High; self-verified in a canonical exact-source probe with
positive inline and negative alias/factory controls.

### 441. [CONFIRMED] ProofKernel swallows cancellation when a backend returns a null task

**Location**: SharpProof.Verify/ProofKernel.cs around lines 13-30; existing
tests in SharpProof.Verify.Test/ProofKernelTests.cs around lines 343 and 465.

**Description**: The null-task guard returns
Unknown(MalformedBackendResult) immediately, before the common
cancellationToken.ThrowIfCancellationRequested checkpoint. If a backend
cancels the supplied token during CheckAsync and then returns null, ProofKernel
reports an ordinary semantic outcome after cancellation has already been
requested.

**Reproduction**: A disposable compiled probe used two backends that cancel the
captured CancellationTokenSource during CheckAsync. The null-task backend
returned null; the control returned a completed result:

    NULL_TASK:RETURNED=UnknownOutcome
    NULL_TASK:REASON=MalformedBackendResult
    NULL_TASK:TOKEN_CANCELED=True
    COMPLETED_TASK:THREW=OperationCanceledException

**Impact**: Public ISmtBackend and ProofKernel callers can receive malformed
semantic evidence instead of OperationCanceledException, violating the
kernel's cancellation invariant. Current worker callers add cancellation
checks, reducing the shipped worker impact, but the public kernel behavior is
wrong.

**Root cause**: Null-task mapping was implemented as an early return instead of
flowing through the shared post-backend cancellation checkpoint.

**Recommended fix**: Assign a null result when CheckAsync returns a null task,
then execute the existing cancellation check and common null-result mapping.
For example, await only when backendTask is non-null, call
ThrowIfCancellationRequested, then map result == null to
MalformedBackendResult.

**Regression coverage**: Add a backend that cancels its source and returns
null, expecting OperationCanceledException. Retain uncanceled-null-task
coverage expecting Unknown(MalformedBackendResult) and pre-canceled-token
coverage expecting cancellation.

**Confidence**: High; reproduced with a compiled throwaway probe and a
completed-task control that differs only in the return shape.

### 442. [CONFIRMED] SAT string-model decoding does not observe cancellation

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 59, 247-273,
422-455, and 457-515.

**Description**: CreateSatisfiable, TryCreateValue, and DecodeString do not
receive a CancellationToken. DecodeString accepts models with up to 1,000,000
UTF-16 code units and evaluates every sequence element without a cancellation
checkpoint. CheckAsync observes the token only after all of CheckCore,
including model materialization, returns. Solver Context.Interrupt does not
bound this post-solver managed decoding loop.

**Reproduction**: A canonical immutable-snapshot probe forced a non-null
1,000,000-code-unit satisfiable model with:

    s == null || s.Length != 1_000_000

Negating this goal requires a non-null string of exactly that length. With
CancelAfter(10,000), the probe reported:

    canceled; elapsedMs=13315; requested=True

Cancellation overshot by 3.315 seconds while model work continued. A separate
100,000-unit run under host contention remained active beyond its 60-second
scheduled cancellation and had to be terminated.

**Impact**: Cancellation of a public IrSmtBackend query is not prompt or
bounded after SAT. Large symbolic string models can continue consuming CPU
after the caller's budget expires. Current worker policy rejects string
variables, so the present reachability is the public backend and direct
clients, not a worker false verdict.

**Root cause**: Cancellation is threaded through solving but stops at the
model-construction boundary.

**Recommended fix**: Pass the token through CreateSatisfiable, TryCreateValue,
and DecodeString. Check it before evaluating length and inside every element
iteration. Ensure cancellation disposes or abandons model work without
poisoning the reusable backend/context.

**Regression coverage**: Use a forced exact-length non-null string model and a
deterministic decoder-entry hook. Cancel after the first decoded element,
require OperationCanceledException before the second, then run a fresh query
successfully on the same backend.

**Confidence**: High; self-verified in the canonical Linux-amd64 image against
an immutable ffe74fff1 snapshot with timed cancellation evidence.

### 443. [CONFIRMED] Signed System.Random seed aliases repeat downstream fuzz cases even when raw case seeds differ

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs around lines 152-155 and
526-559; Tools/SharpProof.Fuzz/FrontendFuzzing.cs around lines 702-716;
Tools/SharpProof.Fuzz/WellSortedIrGenerator.cs around lines 43-44; existing
coverage in SharpProof.Fuzz.Test/FuzzRunnerTests.cs around lines 88-105.

**Description**: The frontend and finite-SMT fuzz generators XOR each raw case
seed with an oracle salt, cast the result to signed int, and pass it to
System.Random(int). In the supported .NET 9 runtime, new Random(n) and
new Random(-n) produce identical streams. Distinct raw case seeds can therefore
alias after salting and generate the same oracle input. This remains after any
fix that merely makes CreateCaseSeed itself injective.

**Reproduction evidence**:

- Nightly campaign seed 20260424, frontend indices 6787 and 7525:
  raw seeds 2135852073 and -2135852075 become salted seeds
  2138777086 and -2138777086.
- Nightly campaign seed 20260830, finite-SMT indices 3580 and 7042:
  raw seeds 1138160897 and -1138160903 become salted seeds
  794323444 and -794323444.

The canonical .NET 9.0.1 container produced identical first-eight
Random.Next() streams for both opposite-sign pairs. The finite-SMT pair also
has the same target index and fallback parity, so it produces structurally
identical formulas. Frontend generation is likewise deterministic from the
aliased Random stream.

**Impact**: FrontendAgreements and SmtAgreements can count repeated oracle
inputs as independent cases even when raw case-seed uniqueness is enforced.
The existing test checks only distinct CreateCaseSeed integers and cannot see
effective generator-state aliases.

**Root cause**: System.Random(int) seed initialization does not preserve all
32 signed seed bit patterns, but the campaign treats those patterns as distinct
entropy.

**Recommended fix**: Replace System.Random(int) in fuzz generators with a
deterministic PRNG whose initialization preserves every uint or ulong seed bit.
Seed it directly from campaign seed, case index, and oracle identifier rather
than converting through a signed int. Keep the algorithm/version explicit so
retained-seed reproduction remains stable.

**Regression coverage**: Pin both opposite-sign pairs at the PRNG stream and
generated-case levels, assert distinct effective generator states for the
supported campaign range, and retain raw CreateCaseSeed uniqueness and
cross-seed tests.

**Confidence**: High; exact mappings and runtime streams were reproduced in the
canonical .NET image and downstream deterministic inputs were traced.

### 444. [CONFIRMED] Strict protocol deserialization parses every document twice

**Location**: SharpProof.Worker.Protocol/ProtocolJsonSupport.cs around
lines 27-35 and SharpProof.Worker.Protocol/ProtocolJson.cs around
lines 1029-1033.

**Description**: Strict deserialization first parses the entire input into a
JsonDocument to validate property order, duplicates, token kinds, enums, and
depth. It disposes that document, then JsonSerializer.Deserialize<T> reparses
the same full string to materialize the model. This is separate from the
fixed-size file-read allocation: even an exactly sized input pays for two full
JSON parses.

**Reproduction**: A protocol-valid request with a 4 MiB nullable
cache-directory value measured:

    JsonUtf8Bytes         = 4,194,731
    DirectFirstAllocated  = 67,140,936
    StrictSecondAllocated = 83,922,360
    StrictExtraBytes      = 16,781,424

An 8 MiB run allocated 167,817,008 bytes strictly versus 134,240,696 bytes
directly, an extra 33,576,312 bytes. The strict pre-pass adds approximately
four allocated bytes per input byte.

**Impact**: A valid document near the 16 MiB limit incurs roughly another
64 MiB solely for a discarded validation DOM, in addition to file decoding,
model creation, and the actual deserialization. Worker, launcher, and
publication validation all use these APIs, increasing GC, timing, and memory
failure risk.

**Root cause**: Shape validation and model materialization own separate parses
instead of sharing one parsed representation.

**Recommended fix**: Parse once, validate document.RootElement, and deserialize
from that same JsonElement while the document remains alive with
RootElement.Deserialize<T>(options). A single streaming Utf8JsonReader path is
an alternative, but DOM reuse is the smallest change. Preserve every strict
duplicate, ordering, token-kind, enum, and depth check.

**Regression coverage**: Add a warmed allocation regression with a large
sub-limit valid request. Compare strict allocation with deserialization from an
already parsed root and permit only a small fixed margin. Retain all strict
shape rejection and canonical round-trip tests.

**Confidence**: High; self-verified with 4 MiB and 8 MiB valid inputs and
linear allocation measurements.

### 445. [CONFIRMED] Strict verification silently succeeds for non-C# projects

**Location**: SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets
around lines 4-5, 110, 225, 323, and 364;
SharpProof.Package/buildTransitive/SharpProof.targets around lines 15-16 and
85-89; C#-only analyzer, generator, and collector registrations.

**Description**: The verifier targets mark every project extension except
.csproj as language-unsupported. Verification initialization, the public
SharpProofVerify target, verifier execution, and even the unsupported-host
rejection target are gated on language support. Strict profile still sets
SharpProofVerify=true, but its package check validates only that the verifier
package is present. Roslyn ignores the C#-only analyzer, generator, and
collector in VB or F# projects. The complete pipeline therefore skips silently.

**Canonical executable probe**:

    CASE=vb-strict EXIT=0
    Build succeeded.
    0 Warning(s)
    0 Error(s)
    request/result/compiler-manifest/SARIF: all absent

    CASE=cs-strict EXIT=1
    error SP0054 at SharpProof.Verifier.targets(365,5)

    CASE=vb-forced-language-supported EXIT=1
    error SP0054 at SharpProof.Verifier.targets(365,5)

The C# and forced-VB controls used identical verification inputs, isolating the
language-support guard.

**Impact**: A .vbproj or .fsproj can opt into strict or explicit verification,
exit green, and produce no analyzer diagnostic or proof evidence. A
mixed-language solution can therefore appear fully strict while some projects
were never analyzed.

**Root cause**: Unsupported language is used as a condition for both execution
and rejection, converting an explicit strict request into a no-op.

**Recommended fix**: Add a language-specific SP0054 rejection target before
CoreCompile and direct SharpProofVerify whenever verification is enabled and
the profile is not off. Keep advisory/off behavior explicit rather than
implicitly skipping.

**Regression coverage**: Add isolated-feed VB and F# consumers. Strict and
explicit SharpProofVerify=true must fail with the language SP0054 and create no
evidence; profile off should remain a successful control.

**Confidence**: High; self-verified with executable canonical MSBuild target
graphs and two controls that activate the existing rejection path.

### 446. [CONFIRMED] SPMETA010 loses semantic-cache identity through ref and out storage aliases

**Location**: SharpProof.Meta.Analyzers/CacheSoundnessRules.cs around
lines 108-129.

**Description**: Assignment-target analysis recognizes only immediate field or
property targets. A ref local that aliases a semantic-cache field appears as an
ILocalReferenceOperation at the write, and an out helper writes through an
IParameterReferenceOperation. Neither target is traced back to the cache
storage.

**Reproduction**:

    ref var slot = ref cache.Latest;
    slot = Answer.Unknown;

and:

    AssignUnknown(out cache.Latest);

where ProofCache implements ISemanticCache and Latest has recognized
SharpProof.Verify.Answer type. The exact-source probe reported:

    DIRECT_FIELD_CONTROL_SPMETA010_COUNT=1
    REF_LOCAL_ALIAS_SPMETA010_COUNT=0
    OUT_ARGUMENT_ALIAS_SPMETA010_COUNT=0

All compiler error counts were zero.

**Impact**: Transient semantic answers can be written to marked cache storage
without SPMETA010 by using standard ref aliases or out parameters. This is
distinct from value-flow and tuple-deconstruction blind spots.

**Root cause**: The rule resolves value aliases but not storage-location
aliases.

**Recommended fix**: Resolve ref local declarations and ref reassignments back
to their underlying field/property storage before classifying writes.
Supporting arbitrary out/ref helpers additionally needs a callee storage-write
summary or a documented conservative policy for marked cache destinations.

**Regression coverage**: Extend direct-field tests with ref-local Unknown and
Proven controls. Add the out-helper case if interprocedural storage writes are
within the rule's contract, plus ref reassignment and ref-return controls.

**Confidence**: High; exact-source canonical probe included direct, ref-local,
and out-argument cases with compiler-clean inputs.

### 447. [CONFIRMED] Scalar-semantics generation accepts duplicate semantic properties

**Location**: scripts/Generate-CSharpScalarSemantics.ps1 around line 195;
owned outputs SharpProof.Frontend/CSharpScalarSemantics.generated.cs and
SharpProof.Ir/IrOperatorCatalog.generated.cs.

**Description**: The shared scalar-semantics catalog is parsed with
ConvertFrom-Json before Assert-Properties and Assert-Boolean run. Duplicate
properties have already collapsed to their last values, so structural
validators see an apparently valid single property. Both frontend integer
semantics and IR operator vocabulary can be generated from an ambiguous review
source.

**Reproduction**: In an ephemeral canonical container, insert:

    "signed": false,
    "signed": true,

into the first integer entry. Verification against copied checked-in outputs
reports NESTED_DUPLICATE_VERIFY_EXIT=0. A duplicate root schemaVersion 999
followed by schemaVersion 2 also passes -Verify.

**Impact**: Contradictory root or nested semantics can remain in the
authoritative catalog while both generated outputs and acceptance verification
stay green. Hard-coded runtime tests cannot detect ambiguity when the last
value matches current output.

**Root cause**: Property validation occurs after a last-wins JSON conversion and
there is no raw recursive duplicate-name preflight.

**Recommended fix**: Parse the raw catalog through the shared strict ordinal
duplicate-property loader before ConvertFrom-Json. Keep current type and
allowed-property validation after the strict preflight.

**Regression coverage**: Add malformed catalogs with duplicate root
schemaVersion and nested integers[0].signed, require nonzero exit with
path-qualified errors, and preserve byte-identical valid output.

**Confidence**: High; both root and nested duplicates were self-verified
against the generator's -Verify path in the canonical container.

### 448. [CONFIRMED] Initial solver-lane factory faults are always mislabeled as backend unavailable

**Location**: SharpProof.Worker/SharpProofWorker.cs around lines 251-256,
538-556, and renewal handling around lines 617-623; authoritative exception
classification in SharpProof.Worker/Program.cs around lines 280-291.

**Description**: TryCreateLanes catches every non-OOM, non-cancellation
exception from the initial backend factory and returns only false plus an error
string. Its sole caller unconditionally maps false to BackendUnavailable,
backend.unavailable, and claim reason BackendUnavailable. Program's
authoritative classifier explicitly says InvalidOperationException is not
backend-unavailable, and lane renewal already preserves that distinction.

**Reproduction**: A canonical reflection probe against the built worker
reported:

    created=False
    error=ordinary factory failure
    invalidOperationIsBackendUnavailable=False
    dllNotFoundIsBackendUnavailable=True

**Impact**: Configuration, implementation, or other ordinary infrastructure
faults during initial lane creation are reported as missing native backend
availability. Diagnostics, claim reasons, telemetry, and operator remediation
all point to the wrong failure class.

**Root cause**: TryCreateLanes erases exception category and the caller assumes
all creation failures are native-load failures.

**Recommended fix**: Return a typed lane-creation failure or preserve the
caught exception for Program.IsBackendUnavailable classification. Map ordinary
faults to worker.infrastructure plus InfrastructureFailure at run and claim
levels; reserve backend.unavailable and BackendUnavailable for classified
native availability failures.

**Regression coverage**: Initial InvalidOperationException must produce
InfrastructureFailure and worker.infrastructure. DllNotFoundException must
remain BackendUnavailable and backend.unavailable. Both responses should
retain authoritative manifest shape and pass protocol validation.

**Confidence**: High; self-verified in the canonical container with classifier
controls and matched against the already-distinct renewal path.

### 449. [CONFIRMED] ProofKernel also swallows cancellation when a backend throws ArgumentException

**Location**: SharpProof.Verify/ProofKernel.cs around lines 26-30.

**Description**: The catch (ArgumentException) branch returns
Unknown(MalformedBackendResult) before the shared post-backend cancellation
checkpoint. A backend that cancels during CheckAsync and then throws
ArgumentException converts cancellation into a semantic outcome. This is a
separate early-return branch from the null-task defect.

**Reproduction**: A compiled probe compared an ArgumentException backend with
a backend that cancels identically but returns a completed result:

    ARGUMENT_EXCEPTION:RETURNED=UnknownOutcome
    ARGUMENT_EXCEPTION:REASON=MalformedBackendResult
    ARGUMENT_EXCEPTION:TOKEN_CANCELED=True
    COMPLETED_RESULT:THREW=OperationCanceledException

**Impact**: Public ProofKernel/ISmtBackend consumers lose cancellation and
receive misleading malformed evidence. Worker callers add later cancellation
checks and the outcome is not cacheable, but the kernel contract is violated.

**Root cause**: ArgumentException mapping is an early semantic return rather
than a value routed through the common cancellation checkpoint.

**Recommended fix**: In the catch, assign a null or typed malformed result,
then execute ThrowIfCancellationRequested before mapping it to
Unknown(MalformedBackendResult). Apply the same control-flow shape to the
null-task case so no malformed backend branch bypasses cancellation.

**Regression coverage**: Add CancellationDuringBackendArgumentFailurePropagates
with a backend that cancels its source then throws ArgumentException. Retain an
uncanceled ArgumentException control expecting MalformedBackendResult and
parameterize both malformed branches.

**Confidence**: High; self-verified with a compiled probe and a completed-result
control differing only in backend termination shape.

### 450. [CONFIRMED] Compiler-synthesized record members omit executable base calls from Requires analysis

**Location**: SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs around
lines 77-80; record syntax registration in
SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs around lines 157-165;
record-copy handling in
SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines 382-422.

**Description**: Compiler-synthesized record methods have no declaring syntax
or operation block, so ValidateMethodAttributes returns immediately. The record
syntax action models only primary constructors. SharpProof therefore omits
compiler-mandated base calls from synthesized record copy constructors,
PrintMembers overrides, and GetHashCode overrides. Explicitly spelling the
compiler-equivalent member is analyzed, making SP0027 behavior depend only on
source shape.

**Self-verified variants**:

1. A base record copy constructor has Contract.Requires(false). Copying a
   derived record with a synthesized copy constructor executes the base copy
   constructor but produces no SP0027. An explicit derived copy constructor
   calling base(original) produces SP0027. Runtime copy increments the base-copy
   counter.
2. A base record's user-defined PrintMembers(StringBuilder) has
   Contract.Requires(false). The generated derived PrintMembers calls it during
   ToString but is silent; an explicit override calling base.PrintMembers
   produces SP0027. Runtime counters show both paths call the base once.
3. A base record override of GetHashCode has Contract.Requires(false). The
   generated derived GetHashCode calls it but is silent; an explicit override
   produces SP0027. Runtime counters again show both paths call the base once.
4. A positional record has an explicit property getter with
   Contract.Requires(false). Its generated Deconstruct invokes that getter but
   is silent; an explicit Deconstruct body produces SP0027. Runtime counters
   show both deconstruction paths invoke the getter once.

**Impact**: Real precondition violations on always-emitted record calls are
missed, semantic reconciliation has no outcome for those synthesized methods,
and spelling out compiler-generated code changes diagnostics without changing
runtime behavior.

**Root cause**: The analyzer has no closed inventory of synthesized record
call edges. Its existing record-copy predicate only prevents modeling the wrong
parameterless base call; it does not model the actual copy call or other
generated overrides.

**Recommended fix**: Add a SynthesizedRecordCallAnalyzer to the existing record
declaration syntax path. Resolve each implicitly declared derived member and
its compiler-specified base target, then analyze synthetic calls for:

- copy constructor to base copy constructor, mapping parameter ordinal 0;
- PrintMembers to base PrintMembers, mapping the builder parameter;
- GetHashCode to base GetHashCode, with no arguments.
- positional Deconstruct to each associated property getter in compiler order,
  preserving left-to-right completion before assigning out parameters.

Use the derived record declaration as diagnostic location, honor generated-code
and type/assembly suppression, and record each synthesized method outcome once.
Keep the inventory closed to compiler-specified edges rather than guessing by
name.

**Regression coverage**: For each edge, compare synthesized and explicit
derived records against a base member with Requires(false); require equivalent
SP0027 results. Add derived-type suppression controls and satisfiable/no-contract
controls.

**Confidence**: High; all four edges were independently tested with analyzer
differentials and runtime counters.

### 451. [CONFIRMED] Failed release-qualification reruns preserve the previous passing qualification.json

**Location**: scripts/Invoke-SharpProofContainer.ps1 around lines 458-463;
scripts/Invoke-SharpProofReleaseContainer.ps1 around lines 17-24, 92-232;
persistent artifact handling in eng/container/entrypoint.sh around
lines 65-130 and 178-188; always-upload workflow path in
.github/workflows/package-consumers.yml around lines 300-313.

**Description**: WriteQualificationEvidence performs platform, environment,
checkout, tag, package, SBOM, and ten-receipt preflights before it owns
qualification.json. The outer release-qualification command also verifies
README generation first. Neither path invalidates the prior final record at
attempt start. The passed record binds commit, tag, inputs, and receipt hashes
but has no run-attempt identity. A same-commit/tag retry that fails before the
final write leaves the prior pass indistinguishable from current evidence.

**Reproduction**: In a temporary canonical fixture, seed a known
qualification.json and invoke WriteQualificationEvidence without GITHUB_SHA.
The script exits 1 with "The GITHUB_SHA environment variable is required", but:

    QUALIFICATION_SURVIVED=True
    HASH_UNCHANGED=True

The SHA-256 remains exactly the seeded value.

**Impact**: Command and job status remain failed, so this is not an automatic CI
false green. However, persistent/self-hosted/local workspaces and always-upload
steps can publish a previous passing record under the failed attempt. Operators
and artifact consumers cannot attribute the surviving record to the current
run.

**Root cause**: qualification.json uses publish-only-on-success in persistent
artifact storage with no pending/failure lifecycle state.

**Recommended fix**: At both outer release-qualification entry and
WriteQualificationEvidence mode entry, resolve the final path before any
failure-prone preflight and atomically remove it or replace it with a nonpassing
tombstone containing commit, tag, and run-attempt identity when available.
Atomically replace it with status passed only after all checks.

**Regression coverage**: Seed a valid same-commit/tag record, then fail on
missing GITHUB_SHA, corrupt/missing receipt, and release-artifact validation.
Require the old pass to be absent or failure-tombstoned and the evidence
validator to reject it.

**Confidence**: High; reproduced with unchanged production scripts in the
canonical Linux image and traced through persistence and upload paths.

### 452. [CONFIRMED] SPMETA010 ignores writes into cache-owned arrays and collections

**Location**: SharpProof.Meta.Analyzers/CacheSoundnessRules.cs around
lines 115-122.

**Description**: Array assignments use IArrayElementReferenceOperation, which
assignment-target analysis does not handle. Collection indexers appear as
IPropertyReferenceOperation, but their immediate receiver is the array or
Dictionary rather than the outer ISemanticCache. Cache ownership is discarded
instead of being traced through a cache member and optional local alias.

**Reproduction**:

    cache.Slots[0] = Answer.Unknown;
    var slots = cache.Slots;
    slots[0] = Answer.Unknown;
    cache.Slots["answer"] = Answer.Unknown;

with ProofCache : ISemanticCache. The canonical probe reported:

    CACHE_INDEXER_CONTROL_SPMETA010_COUNT=1
    OWNED_ARRAY_ELEMENT_SPMETA010_COUNT=0
    ALIASED_ARRAY_ELEMENT_SPMETA010_COUNT=0
    OWNED_DICTIONARY_INDEXER_SPMETA010_COUNT=0

All compiler-error counts were zero.

**Impact**: Noncacheable answers can be stored in aggregates owned by a marked
semantic cache without SPMETA010. The direct cache indexer control is detected,
so equivalent policy depends on storage shape.

**Root cause**: Storage ownership resolution stops at the immediate assignment
operation and immediate receiver type.

**Recommended fix**: Resolve ownership recursively through array element
references, collection indexer receivers, cache field/property accesses, and
local aliases to those members. Diagnose when the root owner implements
ISemanticCache and the stored value is noncacheable.

**Regression coverage**: Add cache-owned array, aliased array, and dictionary
writes expecting SPMETA010, with unrelated aggregates and Answer.Proven as
negative controls.

**Confidence**: High; all variants and a direct-indexer positive control were
self-verified in a compiler-clean canonical probe.

### 453. [CONFIRMED] Strict protocol shape validation allocates property arrays and strings for every object

**Location**: SharpProof.Worker.Protocol/ProtocolJsonSupport.cs around
lines 50-77, especially EnumerateObject().ToArray() near line 58 and
property.Name access near line 69.

**Description**: The recursive strict-shape walker materializes every object's
properties into an array, then materializes each property name as a string.
This happens after the JsonDocument already exists and is separate from both
the double-parse and file-buffer findings.

**Reproduction**: The agent isolated the private shape walker after parsing:

- 100,000 two-property error objects, 2,500,981-byte JSON:
  20,820,768 bytes allocated.
- 50,000 semantically valid two-property error objects, 1,350,981-byte JSON:
  10,420,648 bytes allocated.

Allocation scales at roughly 208 bytes per object, or 7.7-8.3 times the input
size. A near-limit payload can add about 129 MiB solely during shape walking.

**Impact**: Valid large protocol artifacts incur avoidable LOH/Gen0 pressure
and possible OOM in addition to DOM, typed model, and file-decoding costs,
despite a nominal 16 MiB protocol limit.

**Root cause**: LINQ array materialization and property-name string extraction
are used where streaming JsonElement enumeration can enforce identical rules.

**Recommended fix**: Use GetPropertyCount() for exact-count validation, iterate
EnumerateObject() directly with an index, and compare with
JsonProperty.NameEquals(expected.Name) before recursively checking Value.

**Regression coverage**: Expose or split an internal EnsureJsonShape helper,
parse a large error array before allocation measurement, warm it, and require a
small fixed allocation ceiling. Retain count, order, duplicate-property, and
token-kind rejection tests.

**Confidence**: High; two self-verified sizes demonstrate linear amplification
with parsing and deserialization excluded.

### 454. [CONFIRMED] Protocol model generation and schema parity accept duplicate properties

**Location**: scripts/Generate-ProtocolModel.ps1 around line 358 and
SharpProof.Worker.Test/ProtocolModelSchemaTests.cs around line 25.

**Description**: ConvertFrom-Json collapses duplicate protocol-schema
properties before validation, while schema parity independently uses
JsonDocument.GetProperty and selects the same last value. The authoritative
source for protocol, manifest, cache versions, model declarations, and
validation tables can therefore be contradictory while generation,
acceptance, and parity all pass.

**Reproduction**: Change versionMembers to:

    "protocol": "RetiredProtocolVersion.Current",
    "protocol": "WorkerProtocolVersions.Current"

Full verification against copied checked-in outputs exits 0:

    Verified deterministic worker protocol model.

The parity primitive returns WorkerProtocolVersions.Current.

**Impact**: Generated protocol code and analyzer effect-certainty tables can
silently follow a last duplicate property while the review source remains
ambiguous and every authority gate is green.

**Root cause**: Both generator and parity consumer share last-wins JSON
semantics without a raw recursive duplicate-name check.

**Recommended fix**: Apply the shared strict ordinal duplicate-property reader
before conversion and reuse it in ReadSchema and every other consumer of
ProtocolModel.schema.json.

**Regression coverage**: Add duplicate root schemaVersion, nested
versionMembers.protocol, and validation-table property cases. Require nonzero
generation and path-qualified errors; parity must reject before GetProperty.

**Confidence**: High; generator verification and parity last-wins behavior were
self-verified independently.

### 455. [CONFIRMED] Fuzz campaigns discard valid semantic-failure JSON before parsing and accounting

**Location**: Tools/SharpProof.Fuzz/Program.cs around lines 31-37;
scripts/Invoke-SharpProofFuzzCampaign.ps1 around lines 131-179 and 193-206;
scripts/Assert-SharpProofFuzzRunnerResult.ps1 around lines 130-189.

**Description**: The fuzz runner writes a complete FuzzSummary and returns 1
when it finds a semantic mismatch. The campaign script throws immediately on
any nonzero exit before parsing, hashing, or populating that JSON. Its catch
leaves observedCases and agreement counts at zero and schema/hash null. The
only validator combines structural validation with a pass-only policy, so it
also rejects internally consistent failing summaries.

**Reproduction**: A one-case fixture models a frontend mismatch with finite and
partial SMT agreement, zero abstentions, and Passed=false. The self-verification
reported:

    validator=rejected message=Invalid fuzz runner result:
      The fuzz runner counts do not form a complete agreement partition.
    campaign branch for ExitCode=1:
      throws before Assert-SharpProofFuzzRunnerResult

FuzzRunner's actual control flow can produce exactly this shape: per-oracle
agreements are recorded, the mismatch is retained, overall agreement and
abstention are not incremented, JSON is written, and exit 1 follows.

**Impact**: The first real bug found by fuzzing is collapsed into the same path
as a crash or tool failure. Campaign evidence undercounts completed work, drops
schema and structured failure details, omits resultSha256 binding, and forces
operators to inspect an unvalidated stdout file manually.

**Root cause**: Process success is treated as a prerequisite for decoding
output, and schema/integrity validation is inseparable from require-pass policy.

**Recommended fix**: Split a structural FuzzSummary decoder from the
require-pass policy. Always parse and hash available stdout for expected exits.
Classify valid exit 1 plus Passed=false as semantic failure while preserving
observed counts, schema, failures, and hash. Reserve malformed or missing JSON
and unexpected exits for infrastructure failure. Compute campaign pass only
after structured classification.

**Regression coverage**: Use a fake runner that emits a valid mismatch summary
and exits 1. Require the campaign to fail while its run record has structural
validation passed, schema 4, observedCases equal requested, non-null result
hash, preserved failures, and totalCases including the run. Retain malformed
output and crash controls.

**Confidence**: High; the agent self-verified the valid failure fixture against
the current validator and traced the exact campaign branches.

### 456. [CONFIRMED] ProofKernel propagates foreign backend cancellation as caller cancellation

**Location**: SharpProof.Verify/ProofKernel.cs around lines 17-30; downstream
classification in SharpProof.Worker/SharpProofWorker.cs around lines 96-104
and 412.

**Description**: ProofKernel propagates every OperationCanceledException or
TaskCanceledException from a backend task without checking whether the caller's
supplied token was canceled. A backend can return a task canceled with an
unrelated token while the caller token remains live. This is the inverse of the
null-task and ArgumentException defects: those swallow genuine cancellation,
while this path invents cancellation.

**Reproduction**: A compiled probe used
Task.FromCanceled<BackendCheckResult> with a distinct canceled token:

    THREW=TaskCanceledException
    CALLER_TOKEN_CANCELED=False
    EXCEPTION_TOKEN_IS_CALLER=False

**Impact**: Direct ProofKernel callers receive false cancellation. The worker
catches every OperationCanceledException, then interprets an uncanceled caller
token as timeout/project-timeout state. An injected backend can therefore
produce a protocol-valid but factually false timeout response before any
deadline fires.

**Root cause**: Backend cancellation provenance is trusted implicitly rather
than correlated with the supplied token.

**Recommended fix**: Catch OperationCanceledException when the supplied token
is not canceled and route it through malformed-backend handling. Preserve
propagation only when cancellationToken.IsCancellationRequested is true, then
execute the common cancellation checkpoint.

**Regression coverage**: Add a live caller token plus a backend task canceled
with a foreign token, expecting Unknown(MalformedBackendResult), not an
exception. Retain genuine caller-cancellation coverage and add a worker
integration control proving the response is not TimedOut/ProjectTimeout.

**Confidence**: High; self-verified with a compiled foreign-token probe and
traced through worker timeout classification.

### 457. [CONFIRMED] Failed package-consumers reruns preserve the prior report and qualification receipt

**Location**: scripts/Invoke-SharpProofContainer.ps1 around lines 43-44 and
161-218; scripts/Write-SharpProofQualificationReceipt.ps1 around
lines 91-149; scripts/Invoke-SharpProofReleaseContainer.ps1 around
lines 175-210; persistent artifacts in eng/container/entrypoint.sh around
lines 76-87 and 178-188.

**Description**: The package-consumers command performs parallelism,
PackageSource validation, restore, full consumer validation, and minimum-SDK
validation before it owns package-consumers.json. It writes the report and
receipt only after both validations succeed. Neither prior artifact is
invalidated at attempt start, and release qualification validates commit,
evidence bytes/hash, and package identities without a run-attempt identity.

**Reproduction**: In a temporary canonical fixture, seed both evidence paths
and invoke the unchanged command without PackageSource:

    EXIT_CODE=1
    REPORT_SURVIVED=True
    REPORT_HASH_UNCHANGED=True
    RECEIPT_SURVIVED=True
    RECEIPT_HASH_UNCHANGED=True

The failure occurs before either consumer validation runs.

**Impact**: The command correctly fails, but on persistent/self-hosted/local
workspaces a same-SHA/package retry leaves a passing pair that release
qualification can still accept. Always-upload steps can misattribute the pair
to the failed current attempt.

**Root cause**: Report and receipt ownership begins only after all
failure-prone work, despite persistent artifact storage and no attempt identity.

**Recommended fix**: Initialize or invalidate both paths at package-consumers
entry before parallelism, restore, and validation. Publish the passing report
and receipt as an atomic pair only after both runs succeed. Direct receipt
writes should independently tombstone the old gate receipt before validation.

**Regression coverage**: Seed a valid same-commit/package pair, then fail on
missing PackageSource, restore, first consumer, and minimum-SDK consumer.
Require the old pair absent or nonpassing and prove qualification rejects it.

**Confidence**: High; self-verified byte-for-byte in a temp canonical fixture
and traced through qualification consumption.

### 458. [CONFIRMED] Distinct unsupported folded constants collapse to one pure opaque IR term

**Location**: SharpProof.Frontend/RoslynOperationLowerer.cs around
lines 319, 332, and 469; SharpProof.Frontend/CompilerIdentityBridge.cs around
line 124.

**Description**: Representable folded constants are classified as pure, but
unsupported-operation child structure is discarded and OperationSemanticIdentity
does not include ConstantValue. Pure opaque interning therefore assigns the
same identity to semantically distinct folded constants such as nameof(First)
and nameof(Second).

**Reproduction**:

    first constant=First  exact=False abstention=UnsupportedOperationKind term=IrOpaqueTerm id=0
    second constant=Second exact=False abstention=UnsupportedOperationKind term=IrOpaqueTerm id=0
    same-term=True

**Impact**: The outer classification remains a closed abstention, but retained
IR falsely correlates distinct unknown values. Program lowering reuses one
expression lowerer across nodes, so downstream partial analysis can treat
different constants as one deterministic value. This violates the existing
UnsupportedConstantsDoNotSharePureOpaqueTerms invariant.

**Root cause**: Pure-operation semantic identity distinguishes operation shape
and type but not constant presence, nullness, or value.

**Recommended fix**: Add a type-tagged constant discriminator to
OperationSemanticIdentity, distinguishing no constant, null, and concrete
values. Preserve interning only for semantically identical constants.

**Regression coverage**: Lower nameof(First) == nameof(Second), inspect the
outer opaque term's arguments, and require distinct pure opaque terms. Add a
same-value control proving identical nameof(First) occurrences may still share.

**Confidence**: High; self-verified through the exact lowerer/factory with
distinct constants and term IDs.

### 459. [CONFIRMED] SPMETA010 loses cache ownership for mutation methods on owned child objects

**Location**: SharpProof.Meta.Analyzers/CacheSoundnessRules.cs around
lines 20-26.

**Description**: AnalyzeWrite recognizes method names such as Add and Write but
checks only invocation.Instance.Type for ISemanticCache. For
cache.Items.Add(...) the immediate type is List<Answer>; for
cache.Partition.Write(...) it is the child helper type. The outer cache owner
is discarded, including through local aliases.

**Reproduction**:

    cache.Items.Add(Answer.Unknown);
    cache.Partition.Write(Answer.Unknown);

The exact-source probe reported:

    DIRECT_METHOD_CONTROL_SPMETA010_COUNT=1
    OWNED_LIST_ADD_SPMETA010_COUNT=0
    ALIASED_LIST_ADD_SPMETA010_COUNT=0
    OWNED_CHILD_WRITE_SPMETA010_COUNT=0

All compiler errors were zero, and Add/Write are already in WriteMethods.

**Impact**: Noncacheable answers can be stored through ordinary mutation APIs
on cache-owned state while the direct equivalent is diagnosed.

**Root cause**: Invocation-side ownership classification uses only the
receiver's immediate type instead of receiver-operation provenance.

**Recommended fix**: Resolve invocation receiver ownership recursively through
cache fields/properties and local aliases. Treat mutation on a child rooted at
an ISemanticCache as cache storage when a recognized answer flows into a
cataloged write method.

**Regression coverage**: Add owned-list Add, aliased-list Add, and owned-child
Write cases expecting SPMETA010, with unrelated collections and Answer.Proven
as negative controls.

**Confidence**: High; canonical exact-source probe included direct and owned
receiver controls.

### 460. [CONFIRMED] Compiler-artifact schema generation and parity accept duplicate properties

**Location**: scripts/Generate-CompilerArtifactModel.ps1 around line 371 and
SharpProof.Worker.Test/CompilerArtifactModelSchemaTests.cs around line 82.

**Description**: ConvertFrom-Json drops earlier duplicate properties before
envelope and mapping validation, while parity tests use last-wins
JsonDocument.GetProperty. The generator owns compiler-artifact, portable IR,
compilation, and collector wire models, so contradictory schema declarations
can pass every authority.

**Reproduction**: Change artifactEnvelope to:

    "version": 999,
    "version": 15

Full verification of all four copied outputs exits 0:

    Verified deterministic compiler-artifact model.

The parity lookup independently returns PARITY_VALUE=15.

**Impact**: Envelope versions and nested wire/IR/effect mappings can be
ambiguous while generation, acceptance, and parity remain green.

**Root cause**: Generator and parity consumer share last-wins JSON behavior with
no raw duplicate-name preflight.

**Recommended fix**: Recursively reject duplicate names before conversion and
reuse the same strict reader in schema parity tests.

**Regression coverage**: Add duplicate root schemaVersion,
artifactEnvelope.version, and nested portable-IR/collector mapping cases.
Require path-qualified rejection before any output verification.

**Confidence**: High; self-verified with full generator verification and an
independent parity primitive.

### 461. [CONFIRMED] The intentional SMT string materialization ceiling is mislabeled as malformed backend output

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 260-269 and
478-485; mapping in
SharpProof.Verify/VerificationProjections.generated.cs around lines 18-23.

**Description**: DecodeString intentionally refuses non-null model strings
longer than 1,000,000 code units by returning null. CreateSatisfiable maps every
decode failure, including that explicit resource ceiling, to
BackendFailureReason.MalformedResult. The projection then escalates it to fatal
MalformedBackendResult even though ResourceLimit already exists.

**Reproduction**: The agent supplied a well-formed 1,000,001-character string
and compact ground Z3 sequence:

    IR value length=1000001
    compact Z3 sequence length=1000001
    DecodeString returned null=True

IrFactory accepts the same all-'a' value. A public query
s == null || s.Length <= 1_000_000 has such a valid counterexample; inability
to materialize it is a backend resource limit, not malformed solver data.

**Impact**: A bounded model policy is reported as backend corruption and can
turn an otherwise valid unknown result into a fatal worker run. The direction
is fail-closed, but failure taxonomy and operator remediation are wrong.

**Root cause**: DecodeString returns one untyped null for structural
malformation and intentional size limits.

**Recommended fix**: Return a typed decode result such as Success,
ResourceLimit, or Malformed. Map length above MaximumDecodedStringLength to
ResourceLimit and preserve MalformedResult for invalid native/model structure.
Do not constrain symbolic strings to the decoder cap, which could exclude real
counterexamples and create false proofs.

**Regression coverage**: Add a configurable small decoder limit, query a model
one unit above it, and require backend/kernel ResourceLimit. Add an at-limit
control that decodes and replays and retain true malformed-value tests.

**Confidence**: High; the exact cap branch was exercised with valid IR and
compact Z3 values.

### 462. [CONFIRMED] Strict protocol enum validation allocates hundreds of bytes per token

**Location**: SharpProof.Worker.Protocol/ProtocolJsonSupport.cs around
lines 151-178.

**Description**: EnsureCanonicalEnum constructs namespace/type-name strings,
calls Assembly.GetType, materializes JsonElement.GetString, runs reflection
Enum.Parse with boxing, and calls ToString for every enum token. The same enum
metadata is rediscovered repeatedly during one shape walk.

**Reproduction**:

- 100,000 WorkerClaimOutcome tokens: 37,601,168 bytes allocated.
- 50,000-token rerun: 18,800,584 bytes, the same 376 bytes/token.
- 100,000 WorkerSelectedFeature tokens: 39,201,072 bytes.
- Full pre-parsed 1,001,137-byte response with 100,000 Effects tokens:
  39,221,776 shape-walk bytes.

A 100,000-iteration JsonElement.ValueEquals control allocated zero bytes.

**Impact**: A near-16-MiB canonical enum array can drive hundreds of MiB of
ephemeral allocation before semantic duplicate checks, causing severe GC,
latency, or memory failure despite the protocol size cap.

**Root cause**: Canonical enum metadata and spelling are resolved through
reflection and strings per token rather than generated/cached metadata.

**Recommended fix**: Generate or cache canonical enum spellings by declared
type and validate non-flags tokens with JsonElement.ValueEquals. Use dedicated
generated flag metadata or a raw-token parser for canonical flags, avoiding
per-token Enum.Parse/boxing/string round trips.

**Regression coverage**: Shape-walk a pre-parsed large selectedFeatures array
after warmup and require a small constant allocation ceiling. Retain lowercase,
numeric, unknown, and flags-canonicalization cases.

**Confidence**: High; isolated method and full traversal measurements agree,
with a zero-allocation primitive control.

### 463. [CONFIRMED] Bound-contract schema generation accepts duplicate properties

**Location**: scripts/Generate-BoundContractModel.ps1 around line 21.

**Description**: The documented authoritative bound-contract schema is passed
directly to ConvertFrom-Json. Earlier duplicate properties are discarded before
any model validation, so contradictory constructor accessibility, enum
vocabulary, property types, or projections can pass verification.

**Reproduction**: Change the first class constructor to:

    "access": "public",
    "access": "internal"

Full verification against copied output exits 0:

    Verified deterministic bound-contract model.

The generator parser reports GENERATOR_PARSE_VALUE=internal.

**Impact**: Public-surface declarations can be ambiguous while acceptance and
generated-output verification remain green.

**Root cause**: No strict raw JSON duplicate-property check precedes
ConvertFrom-Json.

**Recommended fix**: Use the shared recursive ordinal duplicate-name loader
before conversion.

**Regression coverage**: Add duplicate root schemaVersion and nested
classes[0].constructor.access cases, require nonzero generation with
path-qualified errors before output writes, and retain byte-identical valid
output.

**Confidence**: High; nested duplicate verification and selected parse value
were self-verified.

### 464. [CONFIRMED] Sample validation accepts timeout parameters but never enforces them

**Location**: scripts/Test-SharpProofSamples.ps1 around lines 43-50, 72,
304, 345, and 359.

**Description**: Invoke-CapturedDotNet accepts TimeoutSeconds, with call sites
requesting 900 seconds for package creation and 300 seconds for restore/build.
The value is never read. The helper invokes dotnet synchronously and has no
bounded wait or process-tree termination.

**Reproduction**: In the canonical Linux image, extract the unchanged helper,
substitute a fake dotnet that runs for two seconds, and request one second:

    {"RequestedTimeoutSeconds":1,"ElapsedMilliseconds":2111,"ExitCode":0}

The helper waits for normal completion and returns success.

**Impact**: tooling samples and package-consumers sample validation can hang
until a much broader workflow timeout when restore, build, pack, an analyzer,
or MSBuild deadlocks, defeating documented per-command budgets.

**Root cause**: TimeoutSeconds is dead API surface; direct synchronous
invocation bypasses the repository's established bounded process runner.

**Recommended fix**: Execute dotnet with a bounded ProcessStartInfo runner,
asynchronously drain stdout/stderr, and on expiry Kill(true), wait for full
process-tree exit, and return or throw a typed timeout such as exit code 124.
Factor the established Invoke-SharpProofDotnet.ps1 logic for reuse.

**Regression coverage**: Use a fake dotnet that spawns a long-lived child,
request a one-second timeout, and require prompt exit 124 with no surviving
child. Add fast success and ordinary nonzero controls preserving captured
output and exit codes.

**Confidence**: High; self-verified with exact helper logic in the canonical
container.

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

### 370. [INTENTIONAL FAIL-CLOSED] The Collector-Only HasStaticStateMutation Gate Voids Effect Evidence for Every Selected Member of a Type Whose Static Constructor Contains Any Assignment - Analyzer Proves Pure, Verifier Fails the Strict Build Opaquely

**Status**: The apparent analyzer/collector difference is an intentional fail-closed boundary. The analyzer's effect model joins an explicit static-constructor body with an `UnknownBoundary` outcome, and existing tests/documentation preserve that conservative treatment; the collector gate prevents replaying unsupported static state. No soundness-preserving production simplification was identified.

**Location**: `SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs` (Conjunct at Line 105: `!(analyzerEffectsSelected && HasStaticStateMutation(target))` inside `supported`; predicate Lines 186-207 scanning ALL statements of explicitly declared static constructors for any assignment/increment; unavailable-routing Lines 463-472 `MarkUnavailable(evidence, WorkerClaimReason.UnsupportedContract)`); no analyzer counterpart `SharpProof.Analyzer.Core\AnalyzerFeaturePipeline.cs` (Lines 192-360; the only `StaticConstructors` uses in Analyzer.Core are member-initializer reachability at scattered lines).
**Description**: When any effect attribute selects a method, the collector additionally demands that the containing type's declared static constructors contain NO assignment whatsoever - any field, any statement. If one exists (`Threshold = 0;`), `supported=false` routes EVERY effect evaluation of EVERY member of that type through unavailable evidence (Outcome Unknown, Certainty Unavailable, witness/replay wiped), while the analyzer pipeline applies no such gate and records Proven normally. Cross-check-triangle divergence: under require-proven the launcher's incomplete check fails the build with the generic aggregate message naming neither the static constructor nor the reason - an opaque kill for code the analyzer certified; under advisory, effect verification silently vanishes for those callables. The gate is over-broad even as a soundness measure (a benign cctor write poisons unrelated statics forever). Fail direction safe (never false Proven). Distinct from #277 (recursion depth), #298 (trusted mirroring), #369 (callable-KIND axis): this is the static-state WRITE gate, a selection predicate with no analyzer mirror.
**Reproduction Steps**:
1. Strict-profile project; add `internal static class Config { internal static int Threshold; static Config() { Threshold = 1; } [SharpProof.EnforcePure] internal static int Square(int x) => x * x; }`.
2. Build in-container: expected per analyzer - `Square` selected, Supported, purity Proven, silent; actual - `HasStaticStateMutation` sees the cctor write, `supported=false`, the EnforcePure claim becomes Unknown(UnsupportedContract), coverage Incomplete, require-proven exits 6/SP0047 with no explanation referencing the cctor.
3. Remove only the `Threshold = 1;` statement and rebuild - green, isolating the collector-only gate.
**Confidence**: Medium (divergence certain from code; a type-initializer soundness rationale could justify collector strictness, but nothing documents it and no analyzer counterpart exists).

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
