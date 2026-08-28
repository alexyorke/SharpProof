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
5. A positional record has an explicit property getter with
   Contract.Requires(false). Generated PrintMembers invokes that getter while
   formatting but is silent; an explicit PrintMembers body produces SP0027.
   Runtime counters show both formatting paths invoke the getter once.
6. A record has a user-declared PrintMembers with Contract.Requires(false).
   Generated ToString invokes it but is silent; an explicit ToString body
   produces SP0027. Runtime counters show both paths invoke PrintMembers once.
7. A record has a user-declared copy constructor with
   Contract.Requires(false). Generated clone code reached by a with expression
   invokes it but is silent; explicit construction produces SP0027. Runtime
   counters show both clone and explicit paths invoke the copy constructor.
8. A record has a user-declared typed Equals with Contract.Requires(false).
   Generated operator == invokes it for distinct non-null operands but is
   silent; an explicit Equals call produces SP0027. Runtime counters show both
   paths invoke Equals once.

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
- synthesized PrintMembers to each compiler-selected printed-member getter in
  generated order, preserving completion before subsequent reads/base calls.
- synthesized ToString to the effective PrintMembers, modeling the generated
  fresh StringBuilder argument or failing closed when its facts are unknown.
- generated clone to the effective record copy constructor, mapping clone
  receiver this to copy-constructor parameter 0.
- generated equality operator to effective typed Equals, preserving generated
  null/reference-equality short circuits and mapping right as the argument;
  operator != should reuse this edge without duplicate diagnostics.

Use the derived record declaration as diagnostic location, honor generated-code
and type/assembly suppression, and record each synthesized method outcome once.
Keep the inventory closed to compiler-specified edges rather than guessing by
name.

**Regression coverage**: For each edge, compare synthesized and explicit
derived records against a base member with Requires(false); require equivalent
SP0027 results. Add derived-type suppression controls and satisfiable/no-contract
controls.

**Confidence**: High; all eight edges were independently tested with analyzer
differentials and runtime counters.

### 484. [CONFIRMED] Abrupt MSBuild termination permanently strands per-invocation run directories

**Location**: SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets
around lines 136-147, 202-220, 239-243, and 316-317.

**Description**: Every verification invocation creates a private
`runs/<32-hex-guid>` directory and copies its compiler manifest into that
directory. Normal target cleanup removes only the current invocation ID. There
is no initialization-time, next-run, or Clean-time sweep for older run
directories whose owning MSBuild process disappeared.

**Reproduction**: The production target contains one removal of the current
invocation directory and no removal or recovery reference for the runs root:

    CurrentInvocationRemoveCount: 1
    RunsRootRemoveCount:          0
    RecoveryOrSweepReferences:   0

The existing interrupted-cleanup package fixture demonstrates that a run
directory persists when the current cleanup hook is prevented, but no later
production target knows that abandoned GUID. Terminating MSBuild after run
creation and starting another build leaves the old directory byte-for-byte
unchanged while a new sibling is created and later removed.

**Impact**: Repeated CI cancellation, host termination, or machine restart can
grow `obj/.../SharpProof/runs` without bound. Each orphan may contain the
compiler manifest and up to three protocol/evidence files whose individual
limits reach 16 MiB, so ordinary interrupted builds can consume substantial
workspace or cache storage.

**Root cause**: Cleanup ownership is represented only by the in-memory current
GUID. The on-disk directory has no lease, owner liveness record, age policy, or
recovery authority that a later process can safely use.

**Recommended fix**: Publish an invocation lease containing the authenticated
owner/process identity and creation time. During initialization and Clean,
perform a bounded scan of well-formed 32-hex child directories, reclaim only
leases proven inactive, and preserve current concurrent invocations. Keep the
normal exact-current-ID cleanup fast path.

**Regression coverage**: Kill an owner after private files are published, run a
new initialization, and require the inactive directory to be removed. Run two
concurrent owners and prove neither reclaims the other. Cover malformed names,
fresh leases, expired inactive leases, and Clean.

**Confidence**: High; target inventory and an interrupted-cleanup fixture both
confirm persistence, and the complete target has no stale-run recovery path.

### 518. [CONFIRMED] Backend-reported timeouts become malformed worker results

**Location**: SharpProof.Worker/CallableVerificationPolicy.cs around lines 43-55;
lane-renewal decision in SharpProof.Worker/SharpProofWorker.cs around lines
302-325; malformed fallback around lines 350-367; projection authority in
SharpProof.Worker.Protocol/WorkerResultAssembler.cs around lines 208-233.

**Description**: BackendCheckResult.Unknown(Timeout) is an explicitly supported
normal result and maps to an Unknown claim with MethodTimeout. Callable policy
then labels every set containing an Unknown claim as SemanticUnknown, ignoring
the typed claim reason. Protocol projection requires callable MethodTimeout when
an owned unknown claim has MethodTimeout, so the assembled response is rejected
and replaced with Failed/MalformedResult. The lane also is not renewed because
renewal looks for callable MethodTimeout.

**Reproduction**: A no-file probe against the built production protocol
assembly reported:

    classificationStatus=TimedOut
    classificationFailure=None
    semanticUnknownProjectionAccepted=False
    methodTimeoutProjectionAccepted=True

Existing executable coverage independently confirms backend Timeout -> claim
MethodTimeout; the probe proves the exact producer pair is invalid while the
correctly typed pair is accepted.

**Impact**: A legitimate backend timeout is laundered into infrastructure-style
MalformedResult, corrupting cause attribution. A factory-backed timed-out lane
is reused instead of renewed before remaining targets.

**Root cause**: Producer and validator implement different claim-to-callable
projection rules; the producer collapses typed unknown reasons too early.

**Recommended fix**: Derive callable reason through one shared projection
function using validator precedence: at minimum MethodTimeout, ProjectTimeout,
Canceled, and UnsupportedCallable before generic SemanticUnknown. Use the same
result for classification and lane renewal.

**Regression coverage**: End-to-end backend Unknown(Timeout) with an unexpired
wall budget must yield RunStatus TimedOut, FailureReason None, matching callable
and claim MethodTimeout, and a protocol-valid response. With a factory and more
work, require backend renewal before the next target.

**Confidence**: High; exact policy output fails the protocol authority while its
typed correction passes, and the upstream timeout mapping is already executable
coverage.

### 519. [CONFIRMED] Conditional encoding leaks two Z3 Sort wrappers until finalization

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 650-658, especially
`whenTrue.Value.Sort.Equals(whenFalse.Value.Sort)`; ownership contract in
SharpProof.Smt/Z3ExpressionOwner.cs around lines 3-23.

**Description**: Each Z3 Expr.Sort property access constructs a fresh,
independently disposable managed wrapper over the native sort. Conditional
encoding obtains two wrappers for equality comparison but neither disposes them
or registers them with Z3ExpressionOwner. Every conditional therefore retains
two native references until finalization.

**Reproduction**: Pinned Microsoft.Z3 4.12.2 probes reported:

    sort-is-disposable=True
    same-wrapper=False
    same-native=True
    first-live-after-dispose=False
    second-live-after-first-dispose=True
    finalization-pending-after-collect=50002

A public production query with 1,000 integer conditionals returned
Unsatisfiable/None but left `production-finalization-pending=2006`, matching
approximately two wrappers per conditional plus fixed baseline.

**Impact**: Wide or repeated conditional queries retain native references until
GC and create a proportional finalizer backlog. Verdicts remain correct, but
native memory, GC cadence, and query latency become nondeterministic under load.

**Root cause**: Property-result ownership is overlooked; Equals neither
transfers nor disposes wrappers, and the query owner never sees them.

**Recommended fix**: Prefer comparing already-validated IR branch types without
creating native Sort wrappers. Otherwise acquire both into `using` variables or
register both with OwnSort and dispose deterministically with the query owner.

**Regression coverage**: Encode N conditionals and compare forced-GC pending
finalizers against a no-conditional control; the delta must not scale as 2N.
Add an ownership observer/helper assertion that acquired native handles are
released when query encoding ends, without requiring GC.

**Confidence**: High; wrapper/native-handle probes and the production 1,000-
conditional query independently show two undisposed Sort objects per term.

### 520. [CONFIRMED] Architecture-test process harnesses can deadlock on redirected output

**Location**: SharpProof.ArchitectureTest/StandaloneGateEvidenceTests.cs around
lines 29-31 and SharpProof.ArchitectureTest/FuzzRunnerEvidenceTests.cs around
lines 31-33 and 72-74.

**Description**: Three PowerShell fixture harnesses redirect stdout and stderr,
then synchronously read stdout to EOF before reading stderr. If the child fills
the stderr pipe, it blocks waiting for the parent while the parent waits for
stdout EOF from a child that cannot exit. WaitForExit is reached only after both
reads, and no timeout exists.

**Reproduction**: The clean targeted architecture test passed in one second. A
temporary fixture was changed only to write a large stderr payload. The same
canonical Linux test reached test execution and then exceeded a 20-second bound
without an NUnit summary. Changing only the harness to start both ReadToEndAsync
operations before WaitForExitAsync made the large-stderr case pass in two
seconds.

**Impact**: A verbose PowerShell error or growing fixture output can hang the
targeted test and the full architecture suite indefinitely instead of returning
an attributable assertion failure.

**Root cause**: Redirected process streams are drained sequentially, violating
the requirement that both bounded pipes be consumed concurrently.

**Recommended fix**: Move all three call sites to a shared async process-fixture
helper that starts stdout/stderr drains concurrently, waits with a bounded
token/deadline, kills the full child tree on timeout, performs a final wait, and
returns complete output plus exit code.

**Regression coverage**: Launch a child that writes more than pipe capacity to
stderr and a stdout sentinel. Require completion within a short bound, exit zero,
and complete capture of both streams. Add nonzero-exit, timeout, and child-tree
cleanup controls.

**Confidence**: High; the exact sequential harness hung under a bounded
large-stderr fixture, while concurrent draining alone made it pass.

### 522. [CONFIRMED] Flow nullability falsely invalidates Contract.Result<T> for class? parameters

**Location**: SharpProof.Contracts/ContractIntrinsicValidator.cs around lines
60-69, surfaced by SharpProof.Analyzer.Core/ContractForValidation/
ContractForCompanionValidator.cs around lines 170-185.

**Description**: Result intrinsic validation compares
`IInvocationOperation.Type` to the owning callable's return type. For an open
`T where T : class?`, Roslyn flow-projects the invocation expression as
T/Annotated even though the constructed intrinsic method and owner both declare
T/NotAnnotated. The valid signature is rejected and the binder drops all
companion clauses.

**Reproduction**: An open ContractFor companion for `ITarget<T>.Read` used
`Contract.Result<T>()` and was called as `ITarget<string?>`. Canonical output:

    SP0024 expected a result type matching the callable return type
    DECLARED_OWNER_RETURN=T/NotAnnotated
    RESULT_METHOD_RETURN=T/NotAnnotated
    RESULT_OPERATION_TYPE=T/Annotated
    DECLARED_SIGNATURE_TYPES_EQUAL=True
    FLOW_TYPE_EQUAL=False
    BIND_SUCCESS=False
    BIND_FAILURE=InvalidIntrinsicSignature
    CLAUSES=-1

Open nonnullable `where T:class` and closed `string?` controls both bound with
two clauses.

**Impact**: Users cannot express a valid non-null postcondition over a nullable
generic return in an open companion, and otherwise valid Requires/Ensures clauses
are silently lost after the diagnostic.

**Root cause**: Flow-annotated expression type is mistaken for declared generic
signature identity.

**Recommended fix**: Compare `invocation.TargetMethod.ReturnType` to
`owner.ReturnType` with IncludeNullability. Retain exact declared-nullability
checks so genuinely mismatched Result<string> versus string? remains invalid.

**Regression coverage**: Add binder and analyzer cases for `where T:class?`
requiring success, companion source, two clauses, and no SP0024. Retain the open
nonnullable, closed nullable, and declared-type-mismatch controls.

**Confidence**: High; the probe shows declared types equal exactly while only
the flow projection differs.

### 523. [CONFIRMED] Primary constructors omit their implicit parameterless base call

**Location**: SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs around lines
157-165; AnalyzerFeaturePipeline.cs around lines 409-444;
RequiresCallSiteAnalyzer.cs around lines 98-104; implicit-base discovery in
RequiresCallSiteDiscovery.cs around lines 382-399.

**Description**: Dedicated primary-constructor analysis handles only
`PrimaryConstructorBaseTypeSyntax`, meaning an explicit `: Base(...)`. When a
class primary constructor names a non-object base with simple `: Base`, the
compiler still invokes parameterless Base(), but the dedicated path returns
NotApplicable. Generic implicit-base discovery accepts only
ConstructorDeclarationSyntax, not the primary constructor's TypeDeclarationSyntax.

**Reproduction**: Base() contains `Contract.Requires(false)` and
`sealed class Derived(int marker) : Base`. Canonical controls:

    ordinary source ctor implicit base:  1 SP0027
    primary ctor explicit : Base():      1 SP0027
    primary ctor implicit : Base:        0 SP0027 (expected 1)
    wholly synthesized derived ctor:     0 SP0027 (current policy)

Every source compiled without diagnostics.

**Impact**: A base-constructor precondition is silently unenforced for a user-
declared primary constructor. Semantically equivalent `: Base()` syntax makes
the diagnostic appear.

**Root cause**: Primary and ordinary constructor paths partition syntax shapes
without covering the implicit-call combination; the session is claimed before
the dedicated path returns NotApplicable.

**Recommended fix**: When a class primary constructor lacks an explicit base
invocation and has a non-object base, resolve the unique parameterless base
instance constructor and analyze an empty-argument replay candidate once. Exclude
structs, record copy constructors, explicit calls, and wholly synthesized
constructors; retain session deduplication.

**Regression coverage**: Add the exact implicit-primary case plus ordinary
implicit, explicit primary, Requires(true), object-base/no-custom-base, and
synthesized-constructor controls.

**Confidence**: High; the analyzer/runtime-equivalent matrix failed only the
simple-base primary-constructor form.

### 524. [CONFIRMED] Compiler response authority performs quadratic claim-to-clause matching

**Location**: SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs,
outer claim iteration around lines 57-72 and per-claim FirstOrDefault scans
around lines 100-104.

**Description**: Every target claim invokes ValidateClaim, which linearly scans
the target's effect claims and postcondition clauses to resolve its evidence.
A callable with N Ensures claims therefore performs N full clause scans before
label/evidence checks.

**Reproduction**: Ordinary protocol-valid and authority-valid responses measured:

    claims    minimum run  independent rerun
     3,000       64 ms           81 ms
     6,000      192 ms          241 ms
    12,000      740 ms        1,272 ms

The 12k response was 5.26 MiB, below the 16 MiB cap, and had zero authority
errors. Unknown results isolated the base lookup cost from Proven-label work.

**Impact**: Large valid contract sets spend seconds in each authority pass after
verification, consuming budgets and adding latency independently of protocol
canonicalization finding 497.

**Root cause**: Claim IDs are resolved by repeated linear FirstOrDefault rather
than a target-local index.

**Recommended fix**: Build `effectByClaimId` and `postconditionByClaimId` once per
target, rejecting duplicate IDs with the existing authority error, and pass
resolved entries into ValidateClaim. Preindex labels/assumptions if profiling
shows further repeated scans.

**Regression coverage**: Validate a dense large callable with zero errors and
add deterministic enumeration-count/index tests plus missing and duplicate-ID
controls. A generous warmed scaling check may supplement functional coverage.

**Confidence**: High; two independent valid-response timing series show
quadratic growth.

### 525. [CONFIRMED] API-spec totality analysis treats lazy branches as eagerly evaluated

**Location**: SharpProof.Specs/ApiSpecTermValidator.cs around lines 127-193;
rejection in SharpProof.Specs/ApiSpecTable.cs around lines 131-145; canonical lazy
semantics in SharpProof.Ir/IrInterpreter.cs around lines 127-129, 251-278, and
374-387.

**Description**: Totality validation requires both operands of AndAlso/OrElse
and both conditional branches to be total, regardless of a constant condition
that makes one side unreachable. The IR interpreter evaluates only the selected
side. Consequently a type-correct, semantically total guarded partial term is
rejected from an audited API spec.

**Reproduction**: A valid declaration used the postcondition
`true || ((1 / 0) == 0)`. Exact paths reported:

    API_SPEC_TABLE=REJECTED
    ArgumentException: Trusted postconditions must be total...
    IR_RUNTIME=VALUE
    TERM_TYPE=IrBooleanTerm
    STATUS=Value BOOLEAN=True

**Impact**: Legitimate specs cannot guard division, overflow, length, or other
partial terms with lazy Boolean/conditional control. A catalog update can break
table initialization despite matching runtime semantics.

**Root cause**: TermFacts tracks integer constants but not Boolean constants or
selected-path reachability, so definedness is folded eagerly.

**Recommended fix**: Track `bool? Boolean` in TermFacts. For AndAlso/OrElse,
require right totality only when the known left value evaluates it. For
conditionals, require only the known selected branch; require both when unknown.
Continue type-checking unreachable children without counting their definedness.

**Regression coverage**: Accept true-or-partial, false-and-partial, and constant
conditional with unselected partial branch. Reject false-or-partial,
true-and-partial, selected partial branch, and unknown condition with partial
branch. Evaluate accepted instances through IrInterpreter.

**Confidence**: High; the same typed term is rejected as non-total by Specs and
evaluated to a defined true value by the canonical interpreter.

### 526. [CONFIRMED] Enhanced #line character offsets are lost from compiler artifacts

**Location**: SharpProof.CompilerCollector/CompilerArtifact/
CompilerCompilationCapture.cs around lines 157-169; model
SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs around lines
73-80; replay in CompilerSourceLocationAuthority.cs around lines 313-351.

**Description**: Capture samples GetMappedLineSpan only at each physical line
start and stores no enhanced-line CharacterOffset. Replay assumes mapped columns
are `MappedColumn + delta`. C# enhanced mappings require subtracting/clamping the
first-line character offset. Callable and diagnostic authorities therefore
reconstruct the wrong column and reject otherwise valid generated/Razor-style
source.

**Reproduction**:

    #line (2,8)-(2,70) 15 "page.razor"
                   [EnforcePure] static int Identity(int value) => value;

Compilation had zero errors. Roslyn/manifest mapped the callable to zero-based
1:7, authority replay produced 1:22, and artifact creation threw:

    InvalidDataException: A compiler source location is not bound to one
    physical tree.

A mapped CS0103 diagnostic failed through the same Bind/CreateDiagnostic path.

**Impact**: Compiler mode rejects selected members in valid enhanced-line source,
and mapped compiler diagnostics crash artifact production instead of preserving
evidence.

**Root cause**: The snapshot format assumes every mapping is affine from column
zero and omits Roslyn LineMapping.CharacterOffset.

**Recommended fix**: Capture SyntaxTree.GetLineMappings(), persist/validate/hash
the offset in a schema/fingerprint update, and replay the first mapped line with
`MappedColumn + max(delta - offset, 0)`; later lines use offset zero.

**Regression coverage**: A clean mapped claim and mapped CS0103 diagnostic must
produce/round-trip an artifact with exact Roslyn start coordinates. Cover
ordinary directives and multi-line enhanced mappings.

**Confidence**: High; both claim and diagnostic artifact paths reproduced the
same exact 15-column authority error.

### 527. [CONFIRMED] Changed-TCB coverage accepts HEAD as its own comparison baseline

**Location**: scripts/Test-SharpProofCoverage.ps1, comparison resolution around
lines 110-157, diff around lines 649-676, zero-line scoring around lines 814-840,
and overall pass around lines 844-869; orchestration in
scripts/Invoke-SharpProofContainer.ps1 around lines 256-296.

**Description**: The resolver rejects textual `HEAD` and `@` but accepts the
exact current 40/64-hex commit without requiring it to precede HEAD. Three-dot
diff against the same commit is empty; zero coverable changed lines are assigned
100%, feeding a passed coverage receipt.

**Reproduction**: A two-commit canonical fixture changed one trusted, uncovered
line. With the real ancestor SHA, changed files=1, coverable=1, percent=0, pass
false. With exact HEAD SHA:

    ChangedFiles=0 CoverableLines=0 LinePercent=100.0
    SummaryPassed=true CoverageExit=0
    ReceiptStatus=passed ReceiptGate=coverage

The receipt was exact-commit and hashed the passing summary.

**Impact**: A stale or miswired baseline can certify 100% changed trusted-code
coverage while examining none of the release/PR delta. Aggregate floors remain,
but the dedicated changed-TCB guarantee and release matrix row false-green.

**Root cause**: Durable commit identity is mistaken for a valid temporal/
topological baseline; equality/descendant checks are absent.

**Recommended fix**: Resolve HEAD and merge-base once. Reject comparison equal
to HEAD or any comparison whose merge-base is HEAD; for release require a strict
ancestor and bind the chosen tag/baseline tuple into evidence. Reuse the
validated merge-base for diff.

**Regression coverage**: Exact ancestor with uncovered change fails coverage;
exact HEAD and descendant refs fail validation rather than yield 100%; valid
divergent/base semantics remain. Orchestration must not mint a receipt from a
HEAD comparison SHA.

**Confidence**: High; canonical A/B execution turned the same uncovered change
from failed 0% to passed 100% solely by supplying current HEAD's hex identity.

### 530. [CONFIRMED] Explicit Requires calls in nested blocks are marked unreplayable

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines
194-206, 425-471, and 538-575; Unknown mapping in
RequiresCallSiteAnalyzer.cs around lines 339-342.

**Description**: Ordinary call candidates require HasReplayablePrefix. That
logic climbs to the statement directly under the callable's outer body and
accepts only a small set of top-level expression/return/local shapes with exact
span ownership. A reachable call inside an if or nested block therefore has
complete flow state but is rejected because its outer ancestor is
IfStatementSyntax or BlockSyntax.

**Reproduction**: Positive(-1) has a positive Requires clause. Probe output:

    Direct:       can-replay=True  flow=Complete
    IfTrue:       can-replay=False flow=Complete
    IfParameter:  can-replay=False flow=Complete
    NestedBlock:  can-replay=False flow=Complete

Only the direct top-level call emitted SP0027. Runtime executed direct, if-true,
if-parameter-true, and nested-block once each; false branches were correctly
unreached. Compiler errors were zero.

**Impact**: Explicit ordinary precondition violations disappear merely because
they are nested in reachable control flow, with no SP0047 fallback.

**Root cause**: Prefix replayability is defined by one outer syntax-span shape
instead of evaluation order along the reachable CFG path.

**Recommended fix**: Walk evaluation-order ancestors from the call, validating
preceding operands/siblings/statements at each enclosing block/control construct
with DefiniteOperationFacts. Retain reachable CFG and complete-flow requirements;
do not admit calls after throwing conditions, arguments, receivers, or earlier
statements.

**Regression coverage**: Invalid calls inside if(true), if(parameter), and an
unconditional nested block must report. Cover valid arguments, if(false),
throwing condition, non-completing prior statement, and representative else/
switch/try nesting.

**Confidence**: High; candidate-flow, analyzer, and runtime probes isolate the
outer-syntax replayability gate.

### 531. [CONFIRMED] Worker creation and request verification use different QueryRlimit authorities

**Location**: SharpProof.Worker/SharpProofWorker.cs around lines 28-38;
request accounting in CallableVerificationPolicy.cs around lines 27-30; actual
solver option in SharpProof.Smt/IrSmtBackend.cs around lines 108-115; hashed
request identity in CompilerManifestArtifact.cs around lines 24-36.

**Description**: SharpProofWorker.Create closes over the caller-supplied mutable
budgets DTO and reads its QueryRlimit later when a backend is created.
VerifyAsync accepts requests with a separate budgets object and never binds or
compares the two. Accounting and input/cache identity use the request value,
while Z3 enforces the captured creation value.

**Reproduction**:

    distinct.creationQueryRlimit=11
    distinct.requestQueryRlimit=29
    distinct.factoryBackendQueryRlimit=11
    distinct.mismatch=True
    mutation.valueAtCreate=11
    mutation.valueBeforeLaneCreation=17
    mutation.factoryBackendQueryRlimit=17
    mutation.deferredCapture=True

**Impact**: Z3 can abstain prematurely under a hidden smaller cap or accept and
cache a proof/refutation after work exceeds the request's attested cap. Response
and cache identity still claim the request value.

**Root cause**: Two unsynchronized configuration authorities plus a deferred
closure over mutable request-shaped data.

**Recommended fix**: Make backend creation request-scoped and pass the validated
current QueryRlimit, or snapshot the creation limit and reject every mismatched
request explicitly. Never retain a mutable DTO as long-lived configuration.

**Regression coverage**: Create with A and verify request B, then mutate A after
Create. Require backend option B or an explicit invalid-request result, and
assert enforced limit equals response/hash budget.

**Confidence**: High; the exact factory read two values different from both its
creation-time and verified-request authorities.

### 533. [CONFIRMED] Matching timeout/cancel errors legitimize completed proven responses

**Location**: SharpProof.Worker.Protocol/WorkerResultAssembler.cs,
TryProjectRunState around lines 148-168, MatchesCallableProjection around lines
189-207, and error mapping around lines 248-254; enforced by ProtocolJson.cs
around lines 741-770.

**Description**: Mapped `worker.timeout` or `worker.canceled` errors
unconditionally replace result-derived run evidence. Callable projection then
accepts Complete/None rows for interrupted statuses. Adding the matching error
therefore makes strict validation accept a fabricated TimedOut/Canceled response
whose callable and claims are fully completed and Proven.

**Reproduction**:

    COMPLETE_CONTROL IsValid=True
    TIMED_OUT_NO_ERROR IsValid=False Codes=response.run_projection
    TIMED_OUT_WITH_ERROR IsValid=True Codes=
    ROWS Callable=Complete/None Claim=Proven/None

Actual worker behavior emits interruption errors only before manifest load with
empty results; after loading, it emits incomplete/unknown rows without such an
error.

**Impact**: A malformed assembled/persisted response can convert completed
verification into a false timeout/cancel artifact and launcher exit, misleading
summaries and downstream retry behavior.

**Root cause**: Self-reported errors override evidence instead of being
reconciled with the producer's permitted result shape.

**Recommended fix**: Permit error-based timeout/cancel only with empty manifest
and result sets, matching producer behavior. Alternatively require every
nonempty row to project to the same interruption with appropriate unknown/
incomplete reasons; never admit resolved claims.

**Regression coverage**: Extend the fabricated-interrupted-status test with
matching timeout and cancel errors and require response.run_projection. Retain
empty-manifest interruption positives and legitimate nonempty interrupted rows.

**Confidence**: High; adding one mapped error changes the same completed proven
response from invalid to valid.

### 534. [CONFIRMED] SPMETA003 rejects an exhaustive cancellation type guard

**Location**: SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs,
RethrowsCancellationImmediately around lines 311-315.

**Description**: The analyzer recognizes immediate cancellation propagation only
when a catch's first statement is a bare `throw;`. It rejects the ordinary safe
pattern `if (exception is OperationCanceledException) throw;` even though every
cancellation exception, including derived types, is rethrown before handling
continues.

**Reproduction**:

    bare rethrow control:       SPMETA003 count=0
    cancellation type guard:   SPMETA003 count=1
    unrelated type guard:      SPMETA003 count=1

All compiler-error counts were zero; SPMETA003 is Error-level.

**Impact**: Safe broad-catch code cannot compile under the repository's analyzer
without a source-shape rewrite, producing an ordinary false positive.

**Root cause**: Cancellation propagation is recognized by a one-statement syntax
shape rather than semantic/CFG proof.

**Recommended fix**: Accept a side-effect-free first guard proving the caught
local is OCE (or a subtype-exhaustive pattern) whose taken branch immediately
bare-rethrows. Keep arbitrary boolean and unrelated-type guards diagnostic.

**Regression coverage**: OCE type guard and bare rethrow are clean; bool and
ArgumentException guards report. Add negated/else forms only when semantically
proven equivalent.

**Confidence**: High; the exact analyzer reports only the semantically safe guard
relative to its bare-rethrow control.

### 535. [CONFIRMED] Invalid ContractFor surfaces still contribute partial companion facts

**Location**: whole-surface validation in
SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs
around lines 30-132; per-member resolution in
SharpProof.Contracts/ContractForSymbolMatcher.cs around lines 187-241 and
HasUniqueTarget around lines 390-394.

**Description**: The validator requires a global one-to-one target/companion
member surface and emits SPCF0004/0005 for missing or extra members. The binder
filters to only the requested target name and checks uniqueness for that pair,
never the rest of the surface. It therefore trusts the matching member of a
companion already declared invalid.

**Reproduction**: A companion added unmatched Ghost beside a matching Read:

    SPCF=SPCF0005 ... Ghost does not exactly match a target overload
    UNMATCHED_CANDIDATES=Ghost
    RESOLUTION_FAILURE=None
    BIND_SUCCESS=True USES_COMPANION=True CLAUSES=1

A missing target-member variant emitted SPCF0004 yet Read still bound; a complete
surface control validated and bound normally.

**Impact**: Suppressed/skipped validator diagnostics or direct public binder use
can consume proof/precondition facts from a malformed companion, breaking
validator/binder fail-closed parity.

**Root cause**: Global surface bijection and per-member resolution are separate
algorithms with different acceptance criteria.

**Recommended fix**: Share the validator's full surface-bijection calculation
with ResolveCompanion and reject every member when any target/candidate lacks
exactly one match, before specialization.

**Regression coverage**: Binder tests for extra Ghost and missing target member
must refuse the otherwise matching Read; complete surface succeeds. Pair them
with existing SPCF diagnostics.

**Confidence**: High; two different globally invalid surfaces both supplied a
successful companion clause.

### 537. [CONFIRMED] Deconstruction property targets are analyzed as getters, not setters

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs,
GetPropertyCalls around lines 1544-1595.

**Description**: A property reference is classified as a setter only when its
immediate parent is a recognized assignment/compound/increment operation. In a
deconstruction assignment the property is nested under the target ITupleOperation,
so it falls through to the getter branch. The runtime invokes only the setter.

**Reproduction**, with zero compiler diagnostics:

    simple invalid write:                 one set_Value SP0027
    simple invalid read:                  one get_Value SP0027
    deconstruction invalid setter value:  zero (expected one)
    deconstruction safe setter,
      getter Requires(false):             one false get_Value SP0027

**Impact**: Property/indexer setter preconditions are missed while getter
preconditions can be reported for accessors that never execute, producing both
false negatives and false positives.

**Root cause**: Accessor role is inferred only from the immediate parent, with no
IDeconstructionAssignmentOperation target-subtree handling.

**Recommended fix**: Walk transparent/tuple parents to detect property/indexer
references in the deconstruction Target. Classify them setter-only and map the
tuple path to the corresponding RHS element when replayable. If mapping is
unknown, emit an unreplayable setter candidate, never a getter.

**Regression coverage**: Invalid and safe setter cases, nested tuples, indexer
targets, and an RHS property read that remains a getter; require no duplicates.

**Confidence**: High; runtime accessor identity and analyzer diagnostics are
inverted only for the tuple target shape.

### 539. [CONFIRMED] Canonical release tooling rejects Windows linked worktrees

**Location**: compose.yaml source mount around lines 20-24 and
eng/container/entrypoint.sh Git detection around lines 42-48, command
classification around lines 65-88, and rejection around lines 122-125.

**Description**: Docker mounts only the linked worktree directory. Its `.git`
file points to an absolute Windows path outside that mount. Linux Git interprets
`C:/...` relative to the container worktree and cannot reach the common metadata,
so every Git-required release command exits before execution.

**Reproduction**:

    GitPointer=gitdir: C:/w/PurelySharp/.git/worktrees/PurelySharp-bug-hunt
    git exit=128
    fatal: not a git repository: /workspace/SharpProof/C:/w/...

The exact baseline entrypoint then returned exit 2:

    SharpProof release-tag requires a Git checkout with an exact commit...

**Impact**: pack, acceptance, pilots, release commands, and other Git-bound gates
cannot run through canonical Docker Desktop from a Windows linked worktree,
although normal in-tree CI clones work.

**Root cause**: Host Git indirection is neither resolved nor mounted into fixed
container-visible paths.

**Recommended fix**: Add a cross-platform tooling launcher that resolves host
worktree Git dir/common dir, mounts both at fixed paths, and supplies GIT_DIR,
GIT_COMMON_DIR, and GIT_WORK_TREE. Preserve in-tree checkout/archive behavior
and emit a specific diagnostic when required mounts are absent.

**Regression coverage**: Create a Windows linked worktree with an annotated
release tag and external common metadata; canonical release-tag must succeed
with matching identities. Retain normal checkout and archive controls.

**Confidence**: High; exact entrypoint execution against the real worktree mount
failed solely on its translated Git pointer.

### 541. [CONFIRMED] Changed-test planning omits newly added untracked test projects

**Location**: scripts/Invoke-SharpProofChangedTests.ps1 around line 77.

**Description**: Changed paths include untracked files, but project inventory
uses `git ls-files '*.csproj'`, which lists only indexed projects. A new
untracked Test.csproj already added to the solution never enters the project or
test-project set, so test-changed can green without running it.

**Reproduction**: A fixture showed modified solution plus untracked test project.
`dotnet sln list` contained it, baseline PlanOnly selected 18 projects and omitted
it, while direct execution failed one test. Changing only discovery to
`git ls-files --cached --others --exclude-standard -- '*.csproj'` selected 19
including the failing project.

**Impact**: During normal pre-commit development, newly added failing test
assemblies can be completely skipped by the changed-test command.

**Root cause**: The changed-file and project-inventory authorities have different
tracked/untracked scopes.

**Recommended fix**: Inventory cached plus untracked nonignored project files,
deduplicate, and exclude cached paths deleted from the worktree.

**Regression coverage**: In a fixture repository, add an untracked test project
and modify the solution; PlanOnly must select it. A mutation restoring cached-
only discovery must fail.

**Confidence**: High; the plan omitted a solution-listed project whose direct
test failed, and the broadened inventory selected it.

### 542. [CONFIRMED] Successful mutation-result reuse preserves stale timing evidence

**Location**: scripts/Invoke-SharpProofTrustedMutationsParallel.ps1, reuse fast
path around lines 51-61 and timing derivation/write around lines 370-397.

**Description**: A valid complete mutation result causes immediate successful
return before the timing path is derived or owned. A canonical
mutation-release.json from another commit therefore survives unchanged while
the command reports the current commit's evidence already complete.

**Reproduction**: Canonical fixture output:

    exit_code=0
    AUDIT_EVIDENCE_COMMIT=a7200d525bef268af1a34560288f555579b04e46
    AUDIT_TIMING_COMMIT=0000000000000000000000000000000000000000
    AUDIT_TIMING_HASH_UNCHANGED=True
    Mutation evidence behavioral fixtures passed.

**Impact**: Operators following documented timing records can attribute duration,
parallelism, shard reuse, and lanes to the current mutation command when they
describe an older campaign. Qualification uses separate result evidence, so the
impact is performance evidence correctness rather than release acceptance.

**Root cause**: Result completeness is treated as completeness of both canonical
outputs, while timing lifecycle starts only after the reuse branch.

**Recommended fix**: Own the timing path before reuse. On reuse, atomically write
a current-commit envelope explicitly marked `reused=true` and zero work, or
remove prior timing. If timing itself is reused, validate commit/config/schema.

**Regression coverage**: Seed old timing and current valid result; success must
produce current reused timing or no timing. Cover missing/malformed timing and
normal execution.

**Confidence**: High; a successful current-commit command left a byte-identical
older-commit timing artifact.

### 607. [CONFIRMED] A transient custom SARIF path makes a later plain clean fail

**Location**: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
around lines 327-359; `SharpProof.BuildTasks/ResetPublishedVerification.cs`,
around lines 38-48; `SharpProof.Host/LinuxPathIdentity.cs`, around lines 296-403.

**Description**: Clean reconstructs the publication set only from current
properties. It cannot authenticate a prior set created with a one-off command
line SARIF path.

**Reproduction**: Build with an absolute custom SARIF succeeded. A subsequent
plain `dotnet clean` exited 1 with SP0053 and left outputs/markers. Repeating the
old SARIF property made clean exit 0 and remove them.

**Impact**: Ordinary CI/developer clean becomes configuration-history dependent.

**Recommended fix**: Persist the exact successful publication topology in
project/TFM-owned metadata and have Clean authenticate/reset that recorded set.
Test transient request/result/manifest/SARIF paths and multi-target builds.

### 608. [CONFIRMED] Deep well-formed API-spec terms can terminate the process

**Location**: `SharpProof.Specs/ApiSpecTermValidator.cs`, around lines 9-12,
68-75, 96-102, 127-133, and 172-179; recursive siblings in
`ApiSpecContentDigest.cs` and `ApiSpecInstantiation.cs`.

**Description**: Public `ApiSpecTable.Create` and its validators/digesters use
unbounded recursive term walks with no depth or node budget.

**Reproduction**: A well-formed nested Boolean Not term at depth 1,000 created
and digested successfully; depth 2,000 terminated the child process with
`Stack overflow` after 1,106 repeated validator frames.

**Impact**: Bespoke programmatic specs can kill a normal host instead of
returning an attributable validation error.

**Recommended fix**: Add iterative structure/depth/node prevalidation and make
validator, digest, and instantiation iterative or consistently bounded. Use a
child-process boundary/over-limit regression.

### 610. [CONFIRMED] Default self-application excludes Meta.Analyzers from itself

**Location**: `SharpProof.SelfApply.targets`, around lines 9-10, 31-32, and
91-92; `scripts/Invoke-SharpProofSelfApplication.ps1`, around lines 76 and
144-146.

**Description**: Default self-application loads the baseline Meta analyzer for
other production projects but excludes the project named
`SharpProof.Meta.Analyzers`. One property conflates production self-analysis
with opt-in analysis of intentionally invalid test fixtures.

**Reproduction**: An imported-target probe showed no Meta analyzer item by
default and one when opting in. A production-named compile containing a manual
DiagnosticDescriptor passed by default, then failed with SPMETA005 under the
opt-in flag.

**Impact**: The documented analyzer-change workflow can false-green on defects
in the Meta analyzer itself.

**Recommended fix**: Load the frozen baseline Meta analyzer for every production
project, including itself; reserve opt-in only for non-production/test fixtures.

### 611. [CONFIRMED] Partial-SMT fuzzing repeats only 32 exact bundles

**Location**: `Tools/SharpProof.Fuzz/PartialTermSmtFuzzing.cs`, around lines
45-68; `FuzzRunner.cs`, around lines 223-236 and 363.

**Description**: The generator uses only low seed bits, with two conditionally
dead bits, yielding 32 formula/scenario bundles across the full Int32 space.
FuzzRunner nevertheless executes/counts one per case.

**Reproduction**: A 1,000-case production-seed campaign had 1,000 distinct raw
case seeds but only 32 partial bundles and 968 duplicate executions. Seed 0
equaled 16 and seed 8 equaled 12.

**Impact**: Larger budgets pay linear Z3 cost without increasing semantic
coverage, while the agreement count overstates breadth.

**Recommended fix**: Either schedule an explicit 32-row matrix once and report
it separately, or use a deterministic full-width generator. Test uniqueness and
separate accounting.

### 612. [CONFIRMED] Call-site contract evaluation cannot evaluate IrLengthTerm

**Location**: `SharpProof.Analyzer.Core/ManagedContractFacts.cs`, around lines
59-102; `RequiresCallSiteAnalyzer.cs`, around lines 355-433.

**Description**: The frontend lowers array/string Length exactly and managed
flow tracks exact cardinality, but the contract evaluator has no `IrLengthTerm`
arm and returns Unknown.

**Reproduction**: A valid ContractFor clause `Requires(values.Length > 0)` bound
as `(len(v2) > 0)`. Passing `new int[0]` produced no SP0027 (selected caller only
got SP0047). Direct evaluator input was nonnull with cardinality [0,0] yet
returned Unknown. Scalar companion control emitted SP0027.

**Impact**: Definite array/string length violations are missed for direct and
companion contracts.

**Recommended fix**: Evaluate `IrLengthTerm` by projecting known operand
cardinality to an integer abstract value; otherwise retain Unknown. Test empty,
nonempty, unknown, selected, and companion cases.

### 613. [CONFIRMED] Expression-bodied property getters become worker-incomplete

**Location**: `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`,
around lines 94-107 and 520-584; `CompilerCallableLowerer.cs`, around lines 46-57.

**Description**: Roslyn gives an expression-bodied property getter an
`ArrowExpressionClauseSyntax`. The collector admits property getter method kinds
but rejects that declaration shape and rewrites Proven effect evidence to
Unknown/UnsupportedContract; the lowerer returns UnsupportedCallable.

**Reproduction**: `[DoesNotThrow] public long Value => 1` produced
`analyzer=Proven supported=False artifact=Unknown/UnsupportedContract
preparation=UnsupportedCallable`; an explicit `get => 1` control was fully
supported and Proven.

**Impact**: A formatting-only syntax choice causes SP0047/strict exit 6.

**Recommended fix**: Centralize callable declaration/operation-root resolution
and map arrow clauses to their property/indexer owner throughout manifest and
lowering paths.

### 616. [CONFIRMED] Canonical pack output depends on its random checkout path

**Location**: `Directory.Build.props`, line 4; `compose.yaml`, around lines
16-19; `eng/container/entrypoint.sh`, around line 134; container pack invocation
around `scripts/Invoke-SharpProofContainer.ps1` lines 383-393.

**Description**: `ContinuousIntegrationBuild` is enabled only through ambient
`GITHUB_ACTIONS`, which canonical task containers do not forward. Each release
task builds under a different random `/tmp/sharpproof-task.*` path.

**Reproduction**: Two clean exact-commit builds under different paths produced
different Attributes DLL/PDB hashes. Adding only
`ContinuousIntegrationBuild=true` made both DLL and PDB hashes identical.

**Impact**: Same-commit package/SBOM/provenance bytes change across retries,
preventing byte-for-byte reconstruction.

**Recommended fix**: Explicitly enable CI/deterministic source-path normalization
for canonical release build and pack, independent of ambient GitHub variables.
Test two detached clones under distinct absolute paths.

### 617. [CONFIRMED] Acceptance receipts can certify a failed required phase

**Location**: `scripts/Write-SharpProofQualificationReceipt.ps1`, around lines
62-67; `eng/acceptance/Verify.ps1`, around lines 178-198; final trust in
`Invoke-SharpProofReleaseContainer.ps1`, around lines 184-197.

**Description**: Receipt minting checks schema version, outer status, and commit,
but never correlates outer `passed` with the required inner phase statuses.

**Reproduction**: Correct Release evidence with outer `passed` and
`static-validation.status=failed` exited 0 and minted a passed receipt whose hash
matched the evidence. Outer `failed` control exited 1.

**Impact**: Producer or evidence-assembly regressions can falsely qualify a
release despite a failed required phase.

**Recommended fix**: Share a strict acceptance evidence validator and require
every exact phase to pass for outer passed; validate at receipt mint and final
qualification. Test failed/skipped/missing/duplicate/reordered phases.

### 619. [CONFIRMED] Record with-clones misattribute receiver effects and omit allocation

**Location**: `SharpProof.Effects/OperationEffectScanner.cs`, around lines
663-679; `OperationEffectScanner.Expressions.cs`, around lines 318-345.

**Description**: Both clone paths call the record copy constructor with the
original object as receiver and no source argument. Runtime instead allocates a
fresh receiver and passes the original as parameter 0.

**Reproduction**: A sealed record copy constructor read the source into its fresh
receiver. Runtime returned a distinct record. The Complete summary reported no
managed allocation, unknown reads, and a write to caller parameter 0. A temp
fresh-receiver/source-argument fix passed the oracle.

**Impact**: Allocation contracts get a false negative and ownership/purity gets
an invented caller-state mutation.

**Recommended fix**: Model a Fresh receiver, original operand as argument 0, and
Managed allocation in both lowered/direct paths. Test throwing constructors,
initializers, structs, and open dispatch.

### 620. [CONFIRMED] Cached refutation replay is quadratic per callable

**Location**: `SharpProof.Worker/VerificationCache.cs`, around lines 661-809;
`CallableCounterexampleReplayer.cs`, around lines 4-22.

**Description**: For every cached claim, replay rematerializes/scans all Ensures
clauses, rebuilds variable-label maps, and re-enumerates entry assumptions. The
replayer then rematerializes Ensures again.

**Reproduction**: Valid 250/500/1000/2000-claim fixtures allocated
1.54/4.77/17.25/66.22 MB; 2,000 replayed successfully in 72.8 ms. Allocation
approached 4x for each 2x input.

**Impact**: A cache hit can allocate tens/hundreds of MB and consume substantial
time despite avoiding SMT work.

**Recommended fix**: Precompute claim-to-clause, per-target label, and entry
assumption indexes, and pass resolved clauses to the replayer. Test linear
allocation plus shuffled/malformed/cancellation controls.

### 621. [CONFIRMED] Exact nuspec validation accepts duplicate identity nodes

**Location**: `scripts/Test-SharpProofPackageDependencies.ps1`, around lines
203-217.

**Description**: The parser uses `SelectSingleNode` for package id/version and
checks only null, silently selecting the first of contradictory duplicates even
though adjacent metadata uses exact node counts.

**Reproduction**: Adding a second `<id>Fabricated.Package</id>` or
`<version>9.9.9</version>` left `GRAPH_ACCEPTED=true`. Requiring exactly one node
preserved canonical input and rejected both mutations.

**Impact**: Malformed packages can satisfy release graph, final validation, and
publication preflight under only their first identity.

**Recommended fix**: Require one metadata/id/version node with canonical text
shape. Add duplicate-order, missing, attributed, nested, whitespace, and symbol
package fixtures.

### 622. [CONFIRMED] Provably in-bounds array writes suppress later SP0027

**Location**: `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
1872-1887 and 1319-1328; `RequiresCallSiteDiscovery.cs`, around lines 425-471.

**Description**: Strict assignment completion omits array-element targets and
cannot consume the already-computed `ManagedFlowResult.ProvesArrayAccess` fact.

**Reproduction**: `(new int[1])[0] = 1` before a known-invalid call had Complete
flow and `FlowProvesAccess=True`, runtime reached the call, but `CanReplay=False`
and no SP0027. Out-of-bounds/null/throwing-RHS controls did not reach it.

**Impact**: A safe ordinary array store erases later definite violations. This
remains after fixing #609's separate allocation arm.

**Recommended fix**: Make prefix completion flow-aware for array writes,
requiring completing receiver/index/RHS and proven bounds. Preserve unknown,
multidimensional, null, and OOB fail-closed cases.

### 623. [CONFIRMED] Oversized-response assumption compaction is quadratic

**Location**: `SharpProof.Worker.Protocol/ProtocolJson.cs`, around lines 666-695.

**Description**: `CompactClaimAssumptions` indexes callables but linearly searches
all manifest claims for every result, making the size-saving fallback O(claims^2).

**Reproduction**: Valid 1k/2k/4k/8k inputs measured
5.4/18.9/77.1/348.7 ms; indexed control measured
0.38/0.56/1.03/1.48 ms. Public serialization compacted a valid 20.33 MB expanded
response to a valid 1.79 MB round trip.

**Impact**: The path used under greatest protocol pressure adds avoidable delay
and allocation during publication/recovery.

**Recommended fix**: Build one first-match ordinal ClaimId index beside the
callable index, preserving malformed/duplicate fallback behavior. Add compact
semantic and near-linear scaling tests.

### 624. [CONFIRMED] Ordinary proofs underreport used preconditions

**Location**: `SharpProof.Worker/CallableEvidenceBuilder.cs`, around lines 23-24
and 64-68; `CallableClaimResultAssembler.cs`, around lines 39-70;
`CompilerResponseEvidenceAuthority.cs`, around lines 123-127 and 535-549.

**Description**: Justification-to-assumption mapping and `Used` marking include
only Assume, not Requires, for ordinary proven postconditions. The authority
reconstructs and accepts the same omission.

**Reproduction**: Real SMT proof of Requires(value>0) => Ensures(value>0)
returned `proofCore=requires:0`, but the Precondition row had `used=False`,
summary used=0, and response validation passed.

**Impact**: JSON/SARIF/provenance contradict the proof core and underreport the
assumptions required for a proof.

**Recommended fix**: Map both Requires and Assume labels for ordinary proofs,
retaining Requires-only contradictory-entry handling. Test used/unused/mixed,
vacuity, and trusted controls.

### 625. [CONFIRMED] Shared SyntaxTree ownership is reference-order dependent

**Location**: `SharpProof.Frontend/CompilationModelProvider.cs`, around lines
16-59.

**Description**: `FindOwningCompilation` returns the first DFS match in a LIFO
reference traversal. If one exact tree instance is legally owned by two source
compilations, reference order silently chooses the semantic owner.

**Reproduction**: Two owners bound `Dependency.Value` as int versus string.
Reversing only root compilation-reference order flipped the returned type from
string to int; direct/unique-owner controls had zero compiler errors.

**Impact**: Collector/analyzer/effects consumers can lower against the wrong
semantic owner without an ambiguity signal.

**Recommended fix**: Traverse the closure, collect distinct owners by reference,
return only one, and reject multiple owners consistently. Preserve diamonds
reaching the same compilation instance.

### 626. [CONFIRMED] Launcher reparses mount information 36 times per SARIF run

**Location**: `SharpProof.Worker.Launcher/Program.cs`, around lines 55, 59,
620-640, 864-880, and 1220-1254; `SharpProof.Host/LinuxPathIdentity.cs`, around
lines 152-243 and 1282-1371.

**Description**: The same four publication paths are requalified nine times
through validation, invalidation, and acquisition. Each local classification
opens/parses `/proc/self/mountinfo`.

**Reproduction**: Exact flow instrumentation counted 36 RequireLocalPath calls,
36 mount scans, 36 statfs calls, and 144 parent probes. Real warm run cost 27.9
ms, 2.04 MB allocated, and 195.6 KB read on a 5.35 KB mount table.

**Impact**: Unconditional per-project overhead scales to seconds/large GC load
on solutions and larger mount tables.

**Recommended fix**: Carry one invocation-scoped prequalified publication set
through all phases and bulk-parse mountinfo once per batch. Add deterministic
counter tests with/without SARIF.

### 627. [CONFIRMED] Proof-core labels collapse distinct IL-summary authorities

**Location**: `SharpProof.Worker/CallableEvidenceBuilder.cs`, around lines
129-150; `CallableClaimResultAssembler.cs`, around lines 16-28;
`CompilerResponseEvidenceAuthority.cs`, around lines 436-500 and 605-618.

**Description**: Direct summary labels include origin/call identity but omit the
evidence digest. The SortedSet merges different modules exposing the same
documentation ID, and authority validation reconstructs the same lossy label.

**Reproduction**: Two extern-aliased assemblies exposed the same call identity
with two module SHA-256 values. Both relations were necessary for Proven, yet
two summary assumptions collapsed to one public core label and validation
accepted it.

**Impact**: Verdict remains sound, but proof/SARIF/cache provenance cannot name
the actual two-authority closure.

**Recommended fix**: Include the direct summary digest in one shared canonical
label builder and reject unexpected full-authority label collisions. Test two
different and one identical authority tuple.

### 628. [CONFIRMED] Same-named file-local types collide in callable identity

**Location**: `SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs`,
around lines 82-91; `ClaimManifestBuilder.cs`, around lines 37-46 and 587-639.

**Description**: Documentation IDs erase file-local ownership. Two legal
same-named file-local types in different files receive identical callable and
claim IDs despite distinct Roslyn MetadataName values.

**Reproduction**: Two files each declared the same selected `file static class
Subject.Value(int)`. Both IDs were `M:Subject.Value(System.Int32)~System.Int32`;
full artifact creation threw JsonException and collector surfaces fatal SP0049.

**Impact**: Legal file-local naming makes verification-enabled builds fail before
worker launch.

**Recommended fix**: Use a shared bounded source-method identity incorporating
the containing file-local metadata identity for callable and source-summary
paths. Test two files, nested file-local owners, order stability, and public-ID
compatibility.

### 629. [CONFIRMED] Interpolation analyzes the wrong ToString overload

**Location**: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, around
lines 421-487; `StringConcatenationEffectResolver.cs`, around lines 50-203.

**Description**: Ordinary interpolation reuses concatenation resolution and
always analyzes parameterless `ToString()`. Runtime can invoke
`IFormattable.ToString(format, provider)` instead.

**Reproduction**: A sealed type's parameterless override was pure, while its
IFormattable implementation wrote state 1729 and threw InvalidOperationException.
Runtime observed both; the analyzer returned Complete/nonunknown with no static
write and empty throws. A temp target-selection fix passed the oracle and five
existing controls.

**Impact**: Interpolation can be falsely certified pure/nonthrowing and later
reachability is based on the wrong method.

**Recommended fix**: Model framework formatting precedence separately from
concatenation, including IFormattable/ISpanFormattable/custom handlers, and fail
closed for unresolved dispatch.

### 630. [CONFIRMED] Any host OS can mint another OS's portable receipt

**Location**: `scripts/Test-SharpProofPortableConsumer.ps1`, around lines 6-18
and 34-43; receipt checks around `Write-SharpProofQualificationReceipt.ps1`
lines 68-74.

**Description**: Caller-supplied `OsFamily` controls evidence filename,
osFamily, and receipt gate. Neither producer nor downstream authority compares
it with the actual runtime OS.

**Reproduction**: Canonical Linux amd64 invoked with `-OsFamily windows`, exited
0, and minted passed `portable-windows` evidence/receipt. Matching Linux control
also passed.

**Impact**: Matrix/wiring mistakes can satisfy Windows or macOS release rows
using Linux; all three portability rows can come from one host.

**Recommended fix**: Derive the OS family from runtime APIs or reject mismatch
before work/output; record OS/architecture provenance. Test all cross-OS pairs.

### 634. [CONFIRMED] A failed same-OS portable rerun preserves a prior passing receipt

**Location**: `scripts/Test-SharpProofPortableConsumer.ps1`, around lines 12-43;
`scripts/Write-SharpProofQualificationReceipt.ps1`; portable validation in
`scripts/Invoke-SharpProofReleaseContainer.ps1`, around lines 175-210.

**Description**: The portable consumer runs its fallible child before taking
ownership of the output pair and publishes evidence only on success. A later
failed run therefore leaves the old passed evidence and receipt intact, while
the final authority has no attempt-freshness concept.

**Reproduction**: After a successful Linux run, a second same-OS run was forced
to fail in the child. Both evidence files survived byte-for-byte and the exact
final release predicate still returned true for the current commit, packages,
and hashes.

**Impact**: A failed current qualification attempt can be reported as passing
from stale same-host evidence.

**Recommended fix**: Atomically invalidate or tombstone both files before any
fallible work, bind evidence and receipt to one attempt ID, and make receipt
generation independently invalidate stale output on failure. Test pass-then-fail,
receipt-writer failure, and interrupted pair publication.

### 670. [CONFIRMED] NuGet package archives are not byte reproducible

**Location**: `scripts/Invoke-SharpProofContainer.ps1`, around lines 383-393;
`New-SharpProofReleaseEvidence.ps1`, around lines 732-748, 927-958, and 977.

**Description**: Compiler determinism is enabled, but raw `dotnet pack` OPC/ZIP
output is hashed without canonicalization. NuGet injects a random core-properties
part name and pack-time ZIP timestamps.

**Reproduction**: Packing the exact same already-built Attributes DLL/PDB twice
with `--no-build --no-restore` produced different nupkg and snupkg sizes/hashes.
Only GUID-named `.psmdcp` paths, their relationship, and timestamps differed.

**Impact**: Retrying a release from the same commit and payload changes packages,
SHA256SUMS, SBOM hashes, manifests, provenance, and attestations; published bytes
cannot be reconstructed from recorded inputs.

**Recommended fix**: Canonicalize packages before validation/evidence (stable
core-properties identity, entry order, timestamps, compression, and ZIP metadata)
or adopt a tested reproducible pack mode. Pack every project twice from one build
and require byte-identical packages and evidence; perturb real payload as control.

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
