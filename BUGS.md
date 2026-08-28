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

### 465. [CONFIRMED] Derived catch handlers are marked unreachable for base-typed throw expressions

**Location**: SharpProof.Effects/ExceptionHandlerReachability.cs around
lines 2742 and 2786-2797; related overlap logic in
SharpProof.Effects/EffectExceptionFlow.cs around line 225.

**Description**: CatchesKnownType returns true only when the static thrown type
is assignable to the caught type. A throw expression statically typed Exception
can hold InvalidOperationException and reach catch (InvalidOperationException),
but Exception is not assignable to InvalidOperationException, so the handler
is excluded. EffectExceptionFlow already recognizes the reverse subtype
relation as Maybe, exposing an internal semantic disagreement.

**Reproduction**:

    Exception error = new InvalidOperationException();
    try { throw error; }
    catch (InvalidOperationException) { s_state++; }

Runtime always executes the catch. The Effects summary reported:

    Writes.IsUnknown = false
    Writes = [Fresh]
    Throws = [System.Exception]
    Completeness = Complete
    Termination = Terminates

The reachable static write is absent from a supposedly complete exact set.

**Impact**: This is not merely lost precision: a complete effect summary omits
a real reachable static mutation. Purity/effect contracts can consume that
summary as stronger evidence than the program permits.

**Root cause**: Catch matching is represented as a Boolean one-way subtype test
instead of overlap certainty.

**Recommended fix**: Replace it with a tri-state catch selection:

- thrown type assignable to caught type => Always;
- caught type assignable to thrown type => Maybe;
- otherwise => Never.

Treat Maybe and Always target handlers as reachable. An earlier catch should
block later handlers only when both its type selection and filter are Always.

**Regression coverage**: Add the base-typed throw/derived-catch case and require
a complete summary containing Static. Retain exact escaping-exception tests.
Split the existing broad exception-handler test so widened Writes.Unknown cases
assert fail-closed unknown rather than querying Contains(false) on top.

**Confidence**: High; reproduced in a pinned temp checkout, and a temporary
tri-state implementation passed the new regression plus existing exception
escape coverage.

### 467. [CONFIRMED] SPMETA010 treats ConcurrentDictionary.TryUpdate comparisonValue as a stored value

**Location**: SharpProof.Meta.Analyzers/CacheSoundnessRules.cs around
lines 20-26.

**Description**: AnalyzeWrite scans every invocation argument for a
noncacheable answer without considering parameter roles. For TryUpdate,
newValue is the only value that can be stored; comparisonValue is read-only
expected-state input. A safe update can therefore receive SPMETA010 solely
because it compares against Unknown.

**Reproduction**:

    cache.TryUpdate(
        key: "answer",
        newValue: Answer.Proven,
        comparisonValue: Answer.Unknown);

with ProofCache : ConcurrentDictionary<string, Answer>, ISemanticCache:

    SAFE_CONTROL_SPMETA010_COUNT=0
    UNSAFE_NEW_VALUE_CONTROL_SPMETA010_COUNT=1
    UNKNOWN_COMPARISON_ONLY_SPMETA010_COUNT=1

All compiler-error counts were zero.

**Impact**: The soundness analyzer produces a false positive for a standard
conditional cache update that can only store a cacheable value. Warnings as
errors can block legitimate cache code and encourage suppressing the broader
rule.

**Root cause**: Write-method recognition is name-based and all arguments are
treated as potential stored values.

**Recommended fix**: Resolve the method symbol/signature and select only
stored-value parameters. For TryUpdate inspect newValue and never
comparisonValue. Define roles for every cataloged write method rather than a
single Any(argument) rule.

**Regression coverage**: Unknown newValue plus Proven comparison must report;
Proven newValue plus Unknown comparison must not. Include named and positional
arguments and a different overload/lookalike control.

**Confidence**: High; self-verified with safe, unsafe, and comparison-only
controls.

### 468. [CONFIRMED] Boxing conversions are interned as pure and collapse fresh object identities

**Location**: SharpProof.Frontend/RoslynOperationLowerer.cs around
lines 319, 346, and 889.

**Description**: IsDemonstrablyPure treats every built-in conversion with a
pure operand as pure. Unsupported boxing conversions then use pure opaque
structural interning. Two identical boxing operations are interned as one term,
even though C# allocates a fresh box for each conversion.

**Reproduction**:

    public static bool Target(long value) =>
        (object)value == (object)value;

The compiled result is false. Lowering reports:

    runtime=False
    exact=False abstention=ConversionMayChangeValue
    left purity=Pure id=1
    right purity=Pure id=1
    same-box-term=True

**Impact**: Although classification stays a closed abstention, retained IR
falsely correlates separately allocated objects as the same reference.
Downstream partial analysis can inherit an impossible identity equality.

**Root cause**: Built-in conversion is treated as synonymous with
allocation-free/deterministic purity; boxing allocation identity is ignored.

**Recommended fix**: Exclude value-type-to-reference-type boxing conversions
from IsDemonstrablyPure and emit occurrence-specific impure opaque terms.

**Regression coverage**: Lower the expression above, assert runtime false,
both child conversions impure, and distinct term identities. Retain
allocation-free numeric conversion interning controls.

**Confidence**: High; runtime and lowerer identities were self-verified in one
probe.

### 470. [CONFIRMED] Release-configuration evidence can remain stale and its receipt validates only schema plus commit

**Location**: scripts/Test-SharpProofReleaseConfiguration.ps1 around
lines 9-21 and 151-311;
.github/workflows/package-consumers.yml around lines 253-264 and 300-313;
scripts/Write-SharpProofQualificationReceipt.ps1 around lines 75-78 and
120-149; scripts/Invoke-SharpProofReleaseContainer.ps1 around lines 175-210.

**Description**: Live release-configuration checks can fail before owning the
old report, and the receipt writer runs only after success. More seriously, the
receipt authority accepts release-configuration evidence using only
schemaVersion == 1 and commit == HEAD. It ignores checkedAtUtc, repository,
rulesets, jobs, environments, and all live-check content. Final qualification
checks the receipt/evidence hash but never reparses those semantics.

**Self-verification**:

1. Seed report and receipt, remove gh from PATH, and rerun the unchanged live
   check. It exits 1 while both artifacts survive byte-for-byte.
2. Supply the receipt writer with only
   schemaVersion 1, matching commit, and checkedAtUtc in year 2000. It emits a
   status-passed receipt:

       MINIMAL_STALE_EVIDENCE_ACCEPTED=True

**Impact**: GitHub rulesets, environments, variables, and secrets can change
without repository SHA changes. A failed rerun or ancient same-commit snapshot
can remain accepted as current passing evidence. Normal job failure still
blocks automatic publish, but resumed/manual/persistent workflows can consume
stale mutable-state authority.

**Root cause**: Split publish-only-on-success lifecycle is compounded by an
under-specified receipt arm that treats commit identity as sufficient for
mutable external state.

**Recommended fix**: Tombstone report and receipt before any live check and
publish atomically after success. Define an exact report schema with status,
required configuration fields, checkedAtUtc bound, and an attempt identity such
as GITHUB_RUN_ID/GITHUB_RUN_ATTEMPT or a local nonce. Require all of these in
receipt and final qualification validation.

**Regression coverage**: Persistent fixture failures for missing gh, API error,
and detected drift must invalidate old evidence. Receipt fixtures must reject
schema-plus-commit-only, ancient, missing-field, and wrong-attempt evidence and
accept one complete current report.

**Confidence**: High; both producer lifecycle and weak receipt authority were
self-verified with unchanged scripts.

### 471. [CONFIRMED] Lane renewal disposes a backend still owned by another live lane

**Location**: SharpProof.Worker/SharpProofWorker.cs around lines 591-607,
especially duplicate detection near 602-604 and disposal near 606; concurrent
caller around lines 298-328.

**Description**: With multiple lanes, a renewal factory can return the backend
already owned by another lane. Renew correctly detects the duplicate and
returns BackendUnavailable, but then unconditionally disposes the returned
instance. The other lane retains the now-disposed backend, may be actively
using it, and later disposes it again during owner cleanup.

**Canonical probe**:

    renewal=BackendUnavailable
    firstDisposeCount=1
    borrowedDisposeCountBeforeOwnerCleanup=1
    secondStillReferencesBorrowed=True
    borrowedDisposeCountAfterOwnerCleanup=2

**Impact**: One invalid renewal corrupts a different live solver lane, causing
mid-check failures, subsequent use-after-dispose behavior, and double disposal.

**Root cause**: The same cleanup branch handles a newly created rejected
replacement and an instance whose ownership never left another lane.

**Recommended fix**: Split duplicate cases. If replacement is reference-equal
to any existing lane backend, return the typed renewal failure without
disposing it. Dispose only a newly acquired replacement owned by the renewal
attempt. Prefer explicit acquisition/ownership tracking.

**Regression coverage**: Two lanes own A and B; A's renewal factory returns B.
Require typed failure, B disposal count zero and usability retained until B's
own cleanup, then exactly one disposal. Add a concurrency variant with B held
inside CheckAsync during A renewal.

**Confidence**: High; self-verified through the private lifecycle in the
canonical container with reference and disposal-count inspection.

### 473. [CONFIRMED] Campaign-fatal fuzz abstentions retain no case-level evidence

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs around lines 239-366 and
369-405; detailed oracle results in
Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs around lines 195-205 and
Tools/SharpProof.Fuzz/PartialTermSmtFuzzing.cs around lines 160-209.

**Description**: Any no-mismatch case with an abstained oracle increments the
aggregate Abstentions count and makes FuzzSummary.Passed false. However,
SelectFailureKeys retains only Mismatch statuses. An abstained case produces no
FuzzFailure, and the detailed reason already returned by the oracle is
discarded after status counting.

**Verification**: Existing focused tests prove both halves:

- Agreement/Agreement/Abstained has HasAbstention true and no failure key.
- SupportedDomainAbstentionFailsTheCampaign requires the summary to fail.

The canonical filtered run passed both tests, confirming the current
contradictory contract.

**Impact**: Nightly/release fuzzing can fail with only Abstentions: N. JSON
contains no case, effective seed, oracle, source/formula/scenarios, or reason.
Operators must rerun the full campaign, and resource-sensitive abstentions may
not reproduce. This persists even if valid exit-1 JSON is later parsed.

**Root cause**: Retained evidence is modeled as mismatch-only even though pass
policy treats mismatch and abstention as fatal.

**Recommended fix**: Add a bounded, schema-owned AbstentionEvidence collection
with Case, effective seed, oracle, unminimized input/scenarios, and original
Detail. Keep Failures for semantic mismatches and retain at least one
representative per abstaining oracle before applying global caps.

**Regression coverage**: Inject Agreement/Agreement/Abstained with a known
detail into aggregation. Require Passed=false, Abstentions=1, Failures empty,
and one serialized abstention record with case/oracle/seed/input/detail.
Cover finite-SMT Unknown and partial-backend unrecognized paths.

**Confidence**: High; self-verified through focused canonical tests and exact
result-retention flow.

### 474. [CONFIRMED] Generated protocol enum membership boxes every validated value

**Location**: SharpProof.Worker.Protocol/ProtocolModel.generated.cs around
lines 620-652 and generated validation calls around lines 826-870;
SharpProof.Worker.Protocol/ProtocolJson.cs around lines 1059-1062.

**Description**: WorkerProtocolMetadata stores every value from every protocol
enum in one HashSet<Enum>. Generic IsKnown<T> executes
s_knownValues.Contains((Enum)(object)value), boxing each enum on every semantic
validation. Request, manifest, callable, and claim rules call IsDefined/IsKnown
for each relevant field after parsing and deserialization have already
allocated their models.

**Reproduction**:

    100,000 IsKnown<WorkerSelectedFeature> checks:
      2,400,000 bytes allocated
    50,000 checks:
      1,200,000 bytes allocated
    type-specific HashSet<WorkerSelectedFeature> control:
      0 bytes allocated

The exact 24 bytes per lookup demonstrate boxing rather than test harness
noise. The shared set also places equal underlying numbers from different enum
types into colliding buckets, adding comparisons even though Enum.Equals
preserves type correctness.

**Impact**: Large claim and manifest arrays incur needless Gen0 allocation
after strict shape walking and typed deserialization. Duplicate-filled arrays
pay the boxing cost before later uniqueness rejection.

**Root cause**: Generator convenience uses a nongeneric Enum collection for
type-specific membership.

**Recommended fix**: Generate type-specific switches, overloads, or
HashSet<T>/generic static caches and dispatch IsKnown without boxing. Preserve
current unknown and Unspecified semantics.

**Regression coverage**: Warm IsKnown(WorkerSelectedFeature.Effects), perform
100,000 accumulated checks, and require near-zero allocation. Verify all known
generated values remain accepted and cast 999 plus Unspecified behavior through
IsDefined remains unchanged.

**Confidence**: High; self-verified with exact linear allocation counts and a
zero-allocation type-specific control.

### 476. [CONFIRMED] Distinct unsupported parameters and locals collapse to one opaque value

**Location**: SharpProof.Frontend/RoslynOperationLowerer.cs around
lines 339, 492, and 503; SharpProof.Frontend/CompilerIdentityBridge.cs around
line 38.

**Description**: Unsupported parameter and local reads call Opaque without
passing the referenced symbol. They are classified pure, and semantic identity
then contains only operation kind and type. Distinct symbols of the same
unsupported type are structurally interned as one PureOpaque term.

**Reproduction**:

    public static bool Target(double left, double right) => left == right;

The probe reported:

    runtime=False
    exact=False abstention=UnsupportedType
    left purity=Pure id=0
    right purity=Pure id=0
    same-parameter-term=True

**Impact**: Partial IR loses independence between unrelated double, decimal,
enum, custom-struct, and other unsupported values. Downstream analysis can
propagate false equality/correlation.

**Root cause**: Opaque identity omits IParameterSymbol or ILocalSymbol even
though repeated reads of one symbol should share and reads of different symbols
should not.

**Recommended fix**: Pass operation.Parameter or operation.Local as the symbol
argument for unsupported references.

**Regression coverage**: For double left == right require distinct child terms;
for left == left require shared child terms.

**Confidence**: High; runtime and lowerer term identities were self-verified.

### 477. [CONFIRMED] Generated enum-array uniqueness validation allocates on every call

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs around lines 874-879
and generated calls in
SharpProof.Worker.Protocol/ProtocolModel.generated.cs around lines 845-850.

**Description**: AreDefinedUnique<T> uses a captured All predicate followed by
Distinct().Count(). This creates closure/LINQ/iterator/hash-set allocations on
every call and performs two passes. Generated callable validation invokes it
twice for SelectedFeatures and SelectionReasons.

**Reproduction**:

    100,000 valid two-value checks = 40,000,000 bytes allocated
    50,000 checks = 20,000,000 bytes allocated

After subtracting the separately reported enum-boxing cost, this pipeline adds
352 bytes per call. An explicit uniqueness comparison control allocates zero.

**Impact**: Every valid callable pays twice; large manifests create tens of MiB
of avoidable Gen0 churn. Invalid duplicate arrays are fully scanned before
rejection.

**Root cause**: General-purpose LINQ is used for tiny generated enum sets.

**Recommended fix**: Replace with one explicit early-exit loop or generated
bitmask checks that validate definition and uniqueness together. After the
IsKnown boxing fix this path should be allocation-free.

**Regression coverage**: Cover null/empty, required-nonempty, Unspecified,
unknown, duplicate, and valid arrays, then require near-zero allocation for
100,000 warmed two-value checks.

**Confidence**: High; exact linear measurements and a zero-allocation control
were self-verified.

### 478. [CONFIRMED] Abrupt launcher termination strands worker-runtime snapshots in /tmp

**Location**: SharpProof.Worker.Launcher/Program.cs around lines 57 and
115-130; SharpProof.CompilerArtifact/CompilerManifestArtifact.cs around
lines 53-104, 257-274, and 292-299;
SharpProof.BuildTasks/VerifierProcessSupervisor.cs around lines 324-338.

**Description**: The launcher creates a WorkerRuntimeClosureSnapshot before
starting the worker. Snapshot creation copies up to 64 MiB into a random
/tmp/SharpProof.Worker.Runtime.* directory, and the only deletion path is
Dispose. Supervisor cleanup uses SIGSTOP then uncatchable SIGKILL. Abrupt
launcher death bypasses the using/finally path, and repository-wide search finds
no sweep or recovery for orphan snapshot directories.

**Impact**: Cancellation, output-limit cleanup, OOM, or other abrupt launcher
termination can strand one full runtime closure per invocation. Reused
development containers can accumulate these until /tmp or Docker storage is
exhausted.

**Root cause**: Crash recovery relies solely on process-local Dispose even
though the supported containment path deliberately uses SIGKILL.

**Recommended fix**: Add owner leases and bounded startup reclamation for
SharpProof.Worker.Runtime.* directories. Delete only snapshots whose owner is
provably dead; keep Dispose as the normal fast path.

**Regression coverage**: A helper creates a real snapshot, reports its path,
then self-SIGKILLs. Confirm current debris, start a new snapshot and require
reclamation, and prove a concurrently live helper's snapshot is never removed.
Add a supervisor-cancellation integration case.

**Confidence**: High source-closure evidence: the random directory has one
normal deletion path, no scavenger, and production cleanup uses SIGKILL.

### 479. [CONFIRMED] Unattributed backend cancellation is fabricated into MethodTimeout

**Location**: SharpProof.Worker/CallableVerificationPolicy.cs around
lines 57-68 and SharpProof.Worker/SharpProofWorker.cs around lines 302-323.

**Description**: CallableVerificationPolicy catches every
OperationCanceledException. After checking caller and project cancellation, it
defaults every remaining exception to MethodTimeout without checking
methodBoundary.IsCancellationRequested. SharpProofWorker then treats that
fabricated timeout as an expired lane and disposes/renews or retires it.

**Reproduction**: A backend returned a faulted task with
OperationCanceledException while all supplied tokens remained live:

    projectCanceled=False
    callableReason=MethodTimeout
    claimReason=MethodTimeout

**Impact**: Backend/library bugs or internal cancellation are reported as
timeouts, healthy lanes can be needlessly replaced, and factoryless lanes can
retire remaining work as timed out. Run status and remediation are false.

**Root cause**: The catch assumes every otherwise-unattributed cancellation
came from CancelAfter.

**Recommended fix**: Require methodBoundary.IsCancellationRequested before
assigning MethodTimeout. Route unattributed OCE through ordinary
InfrastructureFailure handling.

**Regression coverage**: Faulted backend OCE plus all-live tokens must yield
InfrastructureFailure and no renewal. Retain genuine caller, project, and
method timeout cases.

**Confidence**: High; self-verified in a canonical reflection probe with live
tokens.

### 481. [CONFIRMED] Dependency-audit restore failures preserve stale passing evidence with no freshness identity

**Location**: scripts/Invoke-SharpProofContainer.ps1 around lines 43-44 and
330-338; scripts/Test-SharpProofDependencyAudit.ps1 around lines 193-362 and
559-582; persistent artifacts in eng/container/entrypoint.sh around
lines 178-188; .github/workflows/nightly.yml around lines 35-54.

**Description**: Global parallelism and locked solution restore occur before
the dependency-audit producer is invoked. Even inside the producer, inputs are
resolved before old output deletion. A newly published NuGet advisory can make
restore fail before deletion. Passing JSON contains no commit, audit timestamp,
attempt ID, or feed-as-of identity.

**Reproduction**: Seed dependency-audit.json in a temp persistent-artifact
fixture and invoke the unchanged command without its solution, forcing outer
restore failure:

    EXIT_CODE=1
    EVIDENCE_SURVIVED=True
    HASH_UNCHANGED=True

**Impact**: A later refresh can fail while the artifact directory still says
the audit passed. Because the report has no commit or time, even a careful
detached consumer cannot identify it as an earlier attempt. The job still
fails, so this is stale attribution rather than automatic CI false green.

**Root cause**: Invalidation is nested after caller restore and producer input
validation; the schema omits freshness despite mutable advisory data.

**Recommended fix**: Tombstone dependency-audit.json at command entry before
parallelism/restore and at direct producer entry before input validation. Emit
commit, checkedAtUtc, and attempt identity in pass/failure records and require
them in consumers.

**Regression coverage**: Seed a pass, then force outer restore, missing
solution/config, and audit-wrapper/feed failures. Require no surviving pass and
schema validation of commit/time/attempt identity.

**Confidence**: High; self-verified byte-for-byte with unchanged scripts.

### 482. [CONFIRMED] ProofKernel can return Proven after cancellation during UNSAT-core processing

**Location**: SharpProof.Verify/ProofKernel.cs around lines 30, 36-60.

**Description**: The last cancellation check occurs before status dispatch.
CreateProven then performs full-core validation and projection passes with no
token and no final checkpoint. Cancellation arriving during a large valid core
is ignored and a semantic proof is returned.

**Reproduction**: A completed fake backend returned a valid 20,000,000-entry
core and canceled 10-15 ms after return, after the line-30 check:

    outcome=ProvenOutcome; tokenCanceled=True; postCancelWorkMs=322
    outcome=ProvenOutcome; tokenCanceled=True; postCancelWorkMs=553
    control=OperationCanceledException

**Impact**: Public ProofKernel violates cancellation and can return proof
evidence hundreds of milliseconds after cancellation. Worker callers currently
perform another check, mitigating the shipped worker path.

**Root cause**: Cancellation is not threaded through post-solver proof
construction.

**Recommended fix**: Pass the token into CreateProven, combine core validation
and projection into one token-aware pass, and add a final cancellation check
immediately before returning every outcome branch.

**Regression coverage**: Deterministically cancel during core enumeration and
require OperationCanceledException with no ProvenOutcome. Retain cancel-before
backend return and uncanceled duplicate/core semantics.

**Confidence**: High; two canonical runs reproduced post-cancel proof returns
with a working cancellation control.

### 483. [CONFIRMED] Shared protocol collection validation allocates even for empty arrays

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs around lines 869-943
and generated consumers in
SharpProof.Worker.Protocol/ProtocolModel.generated.cs around lines 850-890.

**Description**: CompleteUnique<T> combines a captured All predicate with
Select(key).Distinct(s_ordinal).Count(). It backs distinct-nonblank, model,
assumption, proof-core, claim-ID, and exception-hierarchy checks. The captured
predicate allocates even for empty arrays, while nonempty arrays also allocate
the LINQ iterator/hash-set pipeline.

**Reproduction**:

    two-string array: 376 bytes allocated per call
    empty array:       88 bytes allocated per call
    explicit loop:      0 bytes allocated per call

Thus each otherwise-valid claim with default empty ProofCore and Model pays at
least 176 bytes before broader model validation. At 100,000 claims that is
17.6 MiB for empty checks alone.

**Impact**: Common valid protocol shapes incur repeated Gen0 churn after
parsing/deserialization, and large collections allocate iterator stacks and
sets before duplicate rejection.

**Root cause**: A generic captured LINQ pipeline is used for hot structural
validation, including trivial empty/small cases.

**Recommended fix**: Implement one explicit completeness/uniqueness pass.
Return allocation-free for empty, directly compare one/small arrays, and lazily
create one capacity-sized HashSet<string> only for larger inputs. Reject
invalid/duplicate entries immediately.

**Regression coverage**: Functional cases for null, empty, blank, duplicate,
distinct, duplicate model variable, and duplicate assumption ID. Warm and run
100,000 empty calls with a near-zero allocation requirement and bound two-item
allocation.

**Confidence**: High; exact empty and two-item allocation measurements matched
zero-allocation explicit controls.

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

### 485. [CONFIRMED] Cumulative SMT resource accounting overflows after a valid solver result

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 67-74, 146-147, and
193-202.

**Description**: Resource accounting adds each fresh solver's `rlimit count` to
a backend-lifetime signed Int64 with checked arithmetic. If the accumulated
counter is near Int64.MaxValue, a later query can finish SAT or UNSAT and then
throw ArithmeticException while recording its statistics. The general backend
exception path converts that accounting overflow to Unknown /
InfrastructureFailure and leaves the backend permanently unable to account for
subsequent work.

**Reproduction**: A reflection probe preloaded the backend counter and ran the
same trivial unsatisfiable query:

    control:               Unsatisfiable / None, counter=10
    counter=Int64.MaxValue: Unknown / InfrastructureFailure,
                            counter=9223372036854775807
    ProofKernel outcome:    UnknownOutcome / InfrastructureFailure

The solver had already returned the same valid UNSAT answer in both cases; only
the post-query cumulative addition changed the verdict.

**Impact**: A sufficiently long-lived or heavily reused backend can discard a
completed proof/refutation as an infrastructure failure. Once the boundary is
reached, trivial later queries also fail, turning accounting telemetry into an
availability failure unrelated to solver correctness.

**Root cause**: An unbounded lifetime statistic is stored in a bounded signed
counter, and overflow is allowed to escape through the query-result exception
mapping. Simple saturation is insufficient because future per-query deltas
would become zero and could bypass budgets.

**Recommended fix**: Track fresh-solver deltas with nonthrowing arithmetic and
an explicit exhausted/overflow state, or use a wider accumulator. Preserve the
completed solver status for the current query where its own resource budget was
satisfied, then renew or reject later work with the typed ResourceLimit result.
Never classify accounting overflow as InfrastructureFailure.

**Regression coverage**: Exercise exact-boundary and one-past-boundary deltas,
a near-cap trivial SAT and UNSAT query, renewal after exhaustion, and subsequent
budget enforcement. Assert no wrapped or zero delta and no poisoned backend.

**Confidence**: High; the boundary probe deterministically changed a completed
UNSAT result solely through the checked accumulator.

### 486. [CONFIRMED] The fuzz evidence cap can starve every later oracle

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs around line 117 and
SelectFailureKeys around lines 369-405.

**Description**: Failure evidence is capped at 64 entries by scanning cases in
ascending order, and each case's oracles in fixed finite-domain-SMT, frontend,
then partial-SMT order. Selection stops globally as soon as 64 keys have been
retained. There is no reservation or fairness rule per active oracle.

**Reproduction**: An exact production-method probe supplied 64 early
finite-domain mismatches followed by one independent frontend mismatch:

    input finite mismatches=64 frontend mismatches=1 partial mismatches=0
    retained total=64
    retained finite=64
    retained frontend=0
    retained partial=0
    last retained case=63 oracle=finite-domain-smt
    omitted distinct failure case=64 oracle=frontend

The frontend failure is reproducible and has a distinct key, but the selector
emits neither its case evidence nor a count showing that it was omitted.

**Impact**: One frequent early defect can make independent frontend or
partial-SMT defects invisible in campaign artifacts. The campaign still records
failures, but downstream triage lacks the input, oracle, and minimized evidence
needed to reproduce the starved defect.

**Root cause**: A single deterministic prefix cap is applied across heterogeneous
oracles without stratification or omission accounting.

**Recommended fix**: First reserve the earliest mismatch for every active
oracle, then fill the remaining capacity with a deterministic round-robin or
stratified selection. Publish total and omitted distinct-failure counts per
oracle so the cap cannot silently erase a failure class.

**Regression coverage**: Use the exact 65-case shape and require the retained
set to contain the late frontend case plus at most 63 finite-domain cases. Add a
late partial-SMT mismatch, deterministic ordering checks, fewer-than-cap and
over-cap controls, and per-oracle omitted counts.

**Confidence**: High; the production selector itself produced the raw starvation
trace above.

### 487. [CONFIRMED] Package-consumer validation bypasses all command timeouts

**Location**: scripts/Test-SharpProofPackageConsumers.ps1, especially
Invoke-ConsumerDotNet around lines 245-280, restore/query/build calls around
lines 484-516, and the final package-test invocation around line 615.

**Description**: The package-consumer helper invokes `dotnet` synchronously
through PowerShell's call operator. It has no timeout parameter, deadline,
process-tree kill path, or cancellation token. The final package test bypasses
the helper and invokes raw `dotnet` too. Dead capture-output variables and an
unused RepositoryRoot parameter are remnants of bounded-runner plumbing but do
not constrain any process.

**Reproduction**: In the canonical Linux image, the exact helper was extracted
and its `dotnet` command was replaced by a blocking native program. The helper
remained blocked until that program voluntarily completed, then returned
success; no timer, signal, or child cleanup path ran. Workflow inspection found
no enclosing step or job timeout for `package-consumers`.

**Impact**: A stalled restore, MSBuild/analyzer invocation, package build, or
test can hang Linux qualification and the portable Windows/macOS/Linux consumer
gates until the CI service's external cancellation. The qualification receipt
is never written, and any descendant process can remain alive after a shallow
runner termination.

**Root cause**: This script family did not adopt the repository's bounded
cross-platform process runner. The separate samples timeout defect does not
cover these consumer commands.

**Recommended fix**: Add a validated timeout parameter and route every consumer
command, including the final test, through a cross-platform ProcessStartInfo
runner with asynchronous stdout/stderr draining, bounded WaitForExit,
Kill(entireProcessTree: true), a final wait, and disposal. Preserve each
command's exit code and output attribution.

**Regression coverage**: Inject a fake dotnet that spawns a long-lived child.
Exercise both the helper and final-test paths with a one-second budget and
require prompt exit 124/failure, captured output, restored working directory,
and no surviving process tree. Add fast success and nonzero-exit controls on
Linux, Windows, and macOS runners.

**Confidence**: High; the exact helper blocked under an executable probe, its
blob matched the assigned baseline, and no outer workflow timeout exists.

### 489. [CONFIRMED] Ordinary test fixtures require checkout Git metadata despite the archive-test contract

**Location**: SharpProof.ArchitectureTest release-authority, qualification-
receipt, resolver, and SBOM fixtures; representative task-root Git reads occur
around lines 181, 265, and 394 in their test sources. The production inventory
script reached by Test-SharpProofReleaseAuthorityClosure.ps1 also queries Git.

**Description**: The canonical `tooling test` contract intentionally supports a
source archive without `.git`, and `test` is absent from the container
entrypoint's Git-required command list. Several ordinary test fixtures
nevertheless query the task checkout's HEAD or historical ancestry rather than
creating self-contained repositories. Some negative SBOM cases then pass
vacuously because the missing-Git setup error supplies the expected nonzero
exit before the intended mutation is evaluated.

**Reproduction**: A canonical Gitless run of the four affected classes produced
29 passes and six failures. Failures were the canonical release-authority
closure invocation, two malformed qualification-receipt tests, the release
resolver test, and the canonical SBOM identity case. Thirteen negative SBOM
mutation cases still passed even though their fixture failed first at
`git rev-parse HEAD`, demonstrating the false-positive oracle.

**Impact**: Ordinary archive-based test runs are red for environmental reasons,
while multiple negative release tests can be green without exercising their
claimed mutation. This weakens both developer feedback and release-governance
evidence.

**Root cause**: Tests mix production checkout state with fixture authority.
They neither declare a Git requirement nor build the small deterministic commit
graphs needed by their assertions.

**Recommended fix**: Move the canonical current-checkout closure invocation to
the existing Git-bound acceptance command, or supply a synthetic repository.
Convert qualification and SBOM tests to temporary Git repositories with fixed
identity/timestamps. Build a synthetic tagged ancestry for the resolver test.
For every negative case, assert a mutation-specific rejection sentinel so setup
failure cannot satisfy the oracle.

**Regression coverage**: Add a Gitless/archive targeted run containing these
fixtures. Require both the intended negative reason and proof that fixture setup
completed; retain the Git-bound canonical release check separately.

**Confidence**: High; the canonical Gitless run isolated six checkout-metadata
dependencies and exposed the vacuous negative-test behavior.

### 490. [CONFIRMED] Human-reviewed pilot receipts are rejected by PowerShell operator precedence

**Location**: scripts/Write-SharpProofQualificationReceipt.ps1 around line 98;
the human-reviewed caller is eng/container/Invoke-SharpProofContainer.ps1 around
lines 424-433.

**Description**: The receipt writer admits reviewed human evidence or
unreviewed automated evidence using an unparenthesized mixture of `-and` and
`-or`. PowerShell evaluates the chain left-to-right, so the final
`-and $Automated` applies to the intermediate result. Valid Reviewed plus
non-automated input therefore evaluates false.

**Reproduction**: `ReceiptWriterRequiresReviewedPilotEvidence` creates its own
valid repository and fails independently of the Gitless fixture issue. In a
temporary baseline copy, adding only explicit grouping made the canonical
targeted test pass 1/1.

**Impact**: `tooling pilot-review` cannot write the reviewed receipt required by
release qualification, even when the human review and evidence are valid.
Automated/unreviewed behavior is not a substitute for the required reviewed
gate.

**Root cause**: The intended two-branch truth table is encoded without grouping:

    Reviewed -and -not Automated -or Unreviewed -and Automated

**Recommended fix**: Group each allowed branch explicitly:

    ((Reviewed -and (-not Automated)) -or
     (Unreviewed -and Automated))

Prefer named booleans for the two cases to prevent future precedence drift.

**Regression coverage**: Exercise the complete truth table: reviewed/human and
unreviewed/automated pass; reviewed/automated and unreviewed/human reject.
Include the real `tooling pilot-review` receipt path.

**Confidence**: High; the current fixture reproduces the rejection and the
one-expression grouping change makes it pass.

### 491. [CONFIRMED] Separate object-initializer receivers collapse to one exact IR variable

**Location**: SharpProof.Frontend/RoslynOperationLowerer.cs, `_instances` near
line 10, GetInstance near line 248, and VisitInstanceReference near line 537.

**Description**: RoslynOperationLowerer caches instance variables only by
`ITypeSymbol`. It ignores `InstanceReferenceKind` and the owning initializer or
allocation. Reusing the public lowerer for two implicit receivers of the same
type therefore returns one variable and labels both mappings Exact even though
the receivers are different objects.

**Reproduction**: Lowering the implicit receivers in two allocations:

    var first  = new Box { Value = 1 };
    var second = new Box { Value = 2 };

produced:

    implicit-receivers=2
    receiver[0] span=162 exact=True term=0
    receiver[1] span=206 exact=True term=0
    same-receiver-term=True

**Impact**: Clients that reuse the public lowerer across operation subtrees can
equate state belonging to separate allocations. Because the result is Exact,
downstream reasoning receives no abstention or uncertainty signal.

**Root cause**: Type identity is used as receiver identity. That is sufficient
for one containing instance but not for implicit object/collection initializer
receivers or other semantic receiver scopes.

**Recommended fix**: Key instances by a semantic receiver scope. For
ImplicitReceiver, anchor identity to the owning object/collection initializer or
allocation; for containing-instance references, use the enclosing receiver
context. Include reference kind and retain sharing within one initializer.

**Regression coverage**: Two separate same-type initializers must yield distinct
exact receiver variables; two member assignments inside one initializer must
share its variable. Add a containing-instance control and lowerer-reuse test.

**Confidence**: High; the exact production lowerer returned one term for two
separately allocated runtime objects.

### 492. [CONFIRMED] Constrained exception type-parameter throws disappear from handler reachability

**Location**: SharpProof.Effects/ExceptionHandlerReachability.cs, the
IThrowOperation branch around lines 174-204; related throw classification in
SharpProof.Effects/EffectAnalysisSession.cs around line 251.

**Description**: Catch reachability adds a potential thrown exception only when
the unwrapped throw operand type is an INamedTypeSymbol. A type parameter such
as `TException where TException : Exception` is an ITypeParameterSymbol, so the
throw contributes no potential exception and a matching catch body is skipped.
Separately, the escaping-throw analysis records an unknown throw and the
catch-all removes it, producing no escaping throw and no handler effects.

**Reproduction**:

    static int s_state;
    static void Probe<TException>(TException error)
        where TException : Exception
    {
        try { throw error; }
        catch (Exception) { s_state++; }
    }

Runtime always reaches the catch, including null (which throws
NullReferenceException). Baseline analysis reported:

    Writes.IsUnknown = false
    Writes = []
    Throws = []
    Completeness = Incomplete
    Termination = Terminates
    Uncertainty = None

**Impact**: A reachable catch's writes and other effects vanish from the summary
without making Writes unknown. Consumers can observe an exact empty write set
for code that deterministically mutates static state.

**Root cause**: The reachability inventory silently drops non-named operand
types instead of adding an unknown potential exception or resolving effective
exception constraints.

**Recommended fix**: Minimally add UnknownPotential whenever a completing throw
operand has no named type. For precision, resolve exception-class constraints in
both reachability and ResolveThrownException so `TException : Exception` is
known to match catch(Exception).

**Regression coverage**: The generic example must retain the exact static write
and no escaping throw. Cover nonnull and null runtime controls, nested/multiple
constraints, an unconstrained invalid-source control, and the existing
definitely-null thrown-expression test.

**Confidence**: High; a temporary minimal conservative fix restored the write
while retaining incompleteness, and the existing null test remained green.

### 493. [CONFIRMED] Pattern-based foreach omits the hidden GetEnumerator precondition call

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs,
GetCalls around lines 591-624. The analogous semantic inventory exists in
SharpProof.Effects/ExceptionHandlerReachability.cs around lines 2269-2385.

**Description**: Requires discovery handles explicit invocation, object
creation, property, event, and list-pattern calls but has no IForEachLoopOperation
path. Roslyn does not expose pattern-based foreach lowering as child
IInvocationOperations, so the compiler-emitted GetEnumerator call is absent
from precondition checking.

**Reproduction**: A custom `Sequence.GetEnumerator()` contains
`Contract.Requires(false)`. Runtime counters showed:

    generated foreach GetEnumerator calls = 1
    explicit GetEnumerator calls          = 1

Analyzer output contained one SP0027 only for the explicit call; the foreach
site produced none. The source compiled without errors in both cases.

**Impact**: A custom enumerator can hide an always-false Requires clause behind
foreach. Spelling the semantically identical call explicitly changes the
diagnostic, violating the repository's rule that unsupported foreach effect
syntax must not suppress concrete precondition violations.

**Root cause**: Call-site discovery relies on the ordinary operation tree and
does not consult `SemanticModel.GetForEachStatementInfo` for hidden pattern
methods.

**Recommended fix**: Add a synchronous foreach semantic-call path using
GetForEachStatementInfo. Materialize the GetEnumerator candidate with the
collection receiver or mapped reduced-extension argument, after proving the
collection expression can complete. Report at the foreach collection/location
through existing replay and flow rules. Share method selection with the Effects
inventory where practical.

**Regression coverage**: A pattern GetEnumerator with Requires(false) must emit
one SP0027 from foreach, matching the direct-call control. Add valid-requires,
non-completing collection-expression, and supported reduced-extension controls.
Extend MoveNext/Current/Dispose only when synthetic receiver sequencing is
modeled explicitly.

**Confidence**: High; executable analyzer and runtime differentials isolate the
missing compiler-hidden call.

### 494. [CONFIRMED] Cancellation during malformed SAT-model validation returns semantic Unknown

**Location**: SharpProof.Verify/ProofKernel.cs, last common token check around
line 30, ReplayCounterexample around lines 74-90, and tokenless
ValidateAssignments around lines 111-117.

**Description**: After a SAT backend result, ProofKernel scans the expected
variables and model assignments without a cancellation token. If the malformed
model is detected, ReplayCounterexample immediately returns
Unknown(CounterexampleReplayFailed) before the next cancellation checkpoint.
Cancellation during either full-model pass is therefore swallowed as ordinary
semantic evidence.

**Reproduction**: A completed fake backend returned a 500,000-variable model
that omitted the final expected key and added one extra key. Cancellation was
scheduled after backend return and fired during the last-key membership scan.
Two canonical runs reported:

    tokenCanceled=True; postCancelWorkMs=138
    outcome=UnknownOutcome; reason=CounterexampleReplayFailed
    control=OperationCanceledException

**Impact**: The public ProofKernel can perform substantial post-cancellation
work and return a non-canceled outcome. Current Worker callers add a later
check, but direct/kernel consumers and custom backends observe incorrect
cancellation semantics.

**Root cause**: ValidateAssignments has no token checkpoints, and its false
return is an early semantic branch before the shared post-replay check.

**Recommended fix**: Pass the token through model validation, check before and
during both expected-key and assignment-value scans, and add a final common
ThrowIfCancellationRequested before every mapped outcome or malformed-result
return.

**Regression coverage**: Deterministically cancel during expected-key
enumeration and require OperationCanceledException. Retain uncanceled malformed
model -> CounterexampleReplayFailed, cancel-before-backend-return, and valid SAT
replay controls.

**Confidence**: High; two exact repeat runs returned semantic Unknown more than
100 ms after their tokens were canceled while the control threw.

### 495. [CONFIRMED] Package-test setup failures leave earlier parallel shards running

**Location**: scripts/Invoke-SharpProofPackageTests.ps1, process creation and
tracking around lines 337-393, timeout cleanup around lines 396-402, and the
outer finally around lines 499-506.

**Description**: Parallel package-test processes are cleaned up only on normal
completion. If a later shard throws during coverage-output setup, Process.Start,
or stream/metadata initialization, the outer finally deletes directories but
never terminates, waits for, or disposes processes already in `$running`. A
process that starts and throws before `$running.Add(...)` is not tracked at all.

**Reproduction**: A canonical-container probe executed the exact baseline
finalizer after starting a disposable first child and injecting a later-shard
setup failure:

    {"FinalizerStartLine":499,
     "InjectedError":"injected-later-shard-setup-failure",
     "ProcessAliveAfterFinalizer":true,
     "RunningCountAfterFinalizer":1,
     "RootExistsAfterFinalizer":false}

**Impact**: The script exits while prior `dotnet test` shards continue consuming
CPU and memory and accessing result/feed paths that have already been deleted.
They can contaminate subsequent commands in persistent tooling containers.

**Root cause**: Process lifecycle cleanup lives in the success/timeout loop,
whereas the exception finalizer owns only filesystem cleanup and runs it in the
wrong order.

**Recommended fix**: Initialize `$running` before the outer try. Guard or
register each process immediately after successful start. In finally, kill the
entire tree, WaitForExit, and dispose every live process before deleting paths;
have timeout cleanup delegate to the same idempotent routine. Preserve the
original exception.

**Regression coverage**: Start a long-lived fake first shard and fail second-
shard preparation; require the original exception, all child PIDs gone before
return, and directories deleted afterward. Also throw immediately after
Process.Start to cover the not-yet-registered child.

**Confidence**: High; the exact finalizer left the child alive while deleting
its root, and the probed blob matched the baseline.

### 496. [CONFIRMED] Direct SharpProofVerify invocation false-greens on unsupported hosts

**Location**: SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets,
public SharpProofVerify target around lines 320-323, intended core rejection
around line 226, and AfterTargets rejection around lines 362-365.

**Description**: The public target's own Condition requires
`_SharpProofVerifierHostSupported == true`. MSBuild evaluates that condition
before DependsOnTargets, so on an unsupported host it skips both the public
target and `_SharpProofVerifyCore`, even though the core target contains the
intended SP0054 error. The alternative rejection is scheduled only after
CoreCompile and does not run for direct target invocation.

**Reproduction**: A packed ephemeral C# consumer used identical strict,
verify=true, host=false properties:

    TARGET=SharpProofVerify EXIT=0
    Target "SharpProofVerify" skipped, due to false condition
    Build succeeded; 0 errors
    request/result/compiler-manifest/SARIF: all absent

Controls:

    TARGET=_SharpProofVerifyCore EXIT=1; SP0054 at line 226
    TARGET=Build                 EXIT=1; SP0054 at line 365

**Impact**: Automation explicitly invoking the advertised public verification
target on Windows, macOS, ARM64, or another unsupported host can record success
although no verifier ran and no evidence was produced.

**Root cause**: Unsupported-host rejection is placed behind the same condition
that prevents the rejection dependency from executing.

**Recommended fix**: Remove only the host-supported conjunct from the public
target condition so direct invocation reaches `_SharpProofVerifyCore` and its
existing SP0054. Preserve verification, profile, design-time, and
building-project gates.

**Regression coverage**: Directly invoke SharpProofVerify in a packed C#
consumer with host support forced false; require nonzero exit, SP0054, and no
evidence. Retain private-core and ordinary Build controls.

**Confidence**: High; the canonical probe isolated target scheduling with two
failing controls under identical properties.

### 497. [CONFIRMED] Claim canonicalization performs quadratic manifest scans

**Location**: SharpProof.Worker.Protocol/ProtocolManifest.cs around lines 31-37;
SharpProof.Worker.Protocol/ProtocolJson.cs around lines 283-286 and helper
lookups around lines 1035-1044.

**Description**: Callable ClaimIds are sorted by FindClaimOrdinal, and response
results are sorted by both FindClaimCallableId and FindClaimOrdinal. Each helper
uses `manifest.Claims.FirstOrDefault(...)`, so every sort-key evaluation scans
the entire claim array. Canonicalizing N claims plus N IDs/results performs
quadratic string comparisons before JSON serialization or hashing.

**Reproduction**: Warmed public Canonicalize measurements for reversed IDs:

    manifest: 3,000 =   71.799 ms
              6,000 =  248.747 ms
             12,000 = 1,115.070 ms

    response: 3,000 =  141.751 ms
              6,000 =  553.243 ms
             12,000 = 1,987.112 ms

The 12,000-result response serialized to 4,849,870 bytes, comfortably below the
16 MiB protocol cap. A fully valid 3,000-claim manifest reproduced the cost and
passed ValidateManifest, so malformed input is not required.

**Impact**: SealManifest and every SerializeResponse can consume seconds or
tens of seconds on representable claim sets, reducing project time available
for actual verification and amplifying cancellation latency.

**Root cause**: Linear claim-ID lookup is nested inside sorting key selectors,
and the same manifest index is rebuilt implicitly for every element.

**Recommended fix**: After canonicalizing claims, construct one ordinal
Dictionary from claim ID to `(CallableId, Ordinal)` and reuse it for callable
IDs and response results. Preserve existing first-match/null semantics for
malformed manifests using TryAdd and an explicit missing fallback. Complexity
then becomes O(N + N log N).

**Regression coverage**: Canonicalize a large valid reversed-order manifest and
response; assert order, hash, and validation. Add a warmed size-doubling guard
or generous 12k ceiling that rejects quadratic growth without making ordinary
unit tests timing-fragile.

**Confidence**: High; two independent public-path timing series show near-
quadratic growth on valid inputs.

### 498. [CONFIRMED] Same-seed fuzz runs duplicate a prefix while campaign totals count it twice

**Location**: scripts/Invoke-SharpProofFuzzCampaign.ps1 around lines 65-73 and
182-206; Tools/SharpProof.Fuzz/FuzzRunner.cs around lines 149-155 and 191-198.

**Description**: The campaign skips a retained seed only when the rotating run
has at least as many cases. When the retained run is larger, it schedules both
roles. Every runner starts at index zero and derives cases from the same
`(seed,index)` pair, so the smaller rotating run is a byte-for-byte prefix of
the retained run. Requested and observed totals nevertheless sum both runs.

**Reproduction**: Checked-in retained seed 23063 has 1,000 cases. The supported
arguments `-RotatingSeed 23063 -RotatingCases 10` produced:

    scheduled runs=rotating-23063:10,retained-23063:1000
    reported requested/observed-on-pass=1010
    unique seed:index coordinates=1000
    duplicated scheduled coordinates=10
    prefix identical=true

With 999 rotating cases, the campaign would claim 1,999 executions while
covering only 1,000 distinct case coordinates.

**Impact**: Campaign evidence overstates distinct fuzz coverage and wastes a
large fraction of its time budget. A seed collision can satisfy or approach
case-count targets with nearly half of the claimed diversity.

**Root cause**: Seed deduplication is asymmetric and aggregation counts run
observations rather than unique planned coordinates.

**Recommended fix**: Group the plan by seed and run each seed once with the
maximum requested count, or reject rotating/retained collisions. If provenance
matters, annotate the merged run as satisfying both roles. Compute reported
totals from the unique plan.

**Regression coverage**: Same-seed rotating counts smaller than, equal to, and
larger than retained must each yield one max-count run; distinct seeds yield two
and sum. Assert unique `(seed,index)` coordinates and matching requested/actual
totals.

**Confidence**: High; generated case-seed prefixes were identical and the
production planning arithmetic reported the duplicated work as additional
coverage.

### 500. [CONFIRMED] Synchronous using omits the hidden concrete Dispose precondition call

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs,
GetCalls around lines 591-624. Source-aware disposal resolution exists in
SharpProof.Effects/UsingDisposalEffectResolver.cs around lines 5-9, 38-89,
237-312, and 452-508.

**Description**: Requires discovery has no IUsingOperation or
IUsingDeclarationOperation disposal model. Source operation trees expose no
ordinary concrete Dispose invocation. The lowered CFG may expose an implicit
IDisposable.Dispose call, but it loses the concrete receiver implementation;
Effects compensates with a source-aware resolver while Requires does not.

**Reproduction**: A sealed Resource implements IDisposable and its concrete
Dispose contains `Contract.Requires(false)`. Runtime counters showed generated
using disposal and a direct Dispose call each execute once. Analyzer output was:

    SP0027 at direct Dispose call only
    no diagnostic at using statement

**Impact**: An admitted synchronous using statement can silently invoke an
always-invalid concrete precondition at scope exit. Spelling the same call
directly changes the verdict.

**Root cause**: Potential-owner screening and call discovery omit source-level
disposal semantics, and the generic lowered operation cannot recover the
concrete method.

**Recommended fix**: Reuse or factor the Effects resource inventory and concrete
Dispose resolution. For each reachable, acquired, nonnull resource, create a
receiver-only Requires candidate at the resource/declaration location, preserve
acquisition completion and reverse disposal order, and stop later candidates
when an earlier reverse-order Dispose cannot complete. Keep async disposal
separate until modeled.

**Regression coverage**: Cover using statement and declaration parity,
Requires(false) concrete Dispose, direct-call parity, definitely-null resource,
non-completing acquisition, multiple-resource reverse order, and
interface-typed/concrete initializer resolution.

**Confidence**: High; executable analyzer/runtime differentials isolate the
missing hidden call, and the repository's Effects code documents the exact
lowered-CFG information loss.

### 501. [CONFIRMED] Escaping lambdas in field and property initializers skip Requires analysis

**Location**: SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs around lines
113-123 and 166-171; AnalyzerFeaturePipeline.cs around lines 5-24, 447-552;
RequiresCallSiteDiscovery.cs around lines 815-823; ordinary callable policy in
RequiresCallSiteTreeAnalyzer.cs around lines 252-280, 370-395, and 537-559.

**Description**: Member-initializer analysis enumerates executable unflowed
descendants, but that enumeration immediately stops at anonymous and local
functions. Nested-callable syntax registration validates only control
attributes. Thus a lambda stored by a field or auto-property initializer is
never analyzed, even though ordinary method-body policy deliberately analyzes
anonymous functions that escape rather than remain in a local.

**Reproduction**: An explicit-constructor sealed class initialized:

    readonly Func<int> Field = () => Positive(-3);
    Func<int> Property { get; } = () => Positive(-4);

where Positive Requires value > 0. Canonical analyzer results:

    direct initializer control:        1 SP0027
    method-returned lambda control:    1 SP0027
    uninvoked local lambda control:    0 SP0027
    field/property initializer lambdas:0 SP0027 (expected 2)

Compiler diagnostics were zero in every case.

**Impact**: Requires violations in common callback/factory fields and properties
are silently missed, while the same escaping lambda returned from a method is
reported. Multiple constructors do not repair the omission.

**Root cause**: Initializer traversal treats callable boundaries as terminal but
does not hand escaped callables to their own CFG-based analysis path.

**Recommended fix**: Add callable-aware initializer discovery. Analyze top-level
non-expression-tree anonymous functions stored by member initializers as escaped
callables using their normalized symbol and own CFG, reusing
RequiresCallSiteTreeAnalyzer policy. Gate on applicable unsuppressed constructor
activation and use existing session/location deduplication so multiple
constructors do not duplicate findings. Do not model the lambda body as running
during initialization.

**Regression coverage**: Field and auto-property lambdas with two constructors
must emit exactly two diagnostics at invocation spans. Retain direct initializer,
valid lambda, unreachable lambda branch, uninvoked local lambda, and
Expression<Func<T>> controls. Add field-like event coverage if supported.

**Confidence**: High; exact-baseline canonical execution held activation and
configuration constant and failed only the two initializer-lambda sites.

### 505. [CONFIRMED] Custom await omits the hidden GetAwaiter precondition call

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs,
GetCalls around lines 591-624. The equivalent semantic protocol is implemented
in SharpProof.Effects/OperationEffectScanner.Expressions.cs around lines
216-284 and ExceptionHandlerReachability.cs around lines 856-959.

**Description**: Requires discovery has no IAwaitOperation protocol path, and
Roslyn does not expose await lowering as ordinary child IInvocationOperations.
The compiler-selected GetAwaiter method is therefore absent from precondition
checking, although Effects resolves it through GetAwaitExpressionInfo and
models the protocol order.

**Reproduction**: A custom Awaitable.GetAwaiter contains
`Contract.Requires(false)`, with an already-completed awaiter. Runtime counters
showed generated await and direct invocation each call GetAwaiter exactly once.
Analyzer output contained one SP0027 only at the explicit call; the await
expression produced none.

**Impact**: A custom awaitable can hide an always-false precondition behind an
admitted language construct. Spelling the semantically identical GetAwaiter
call directly changes the analyzer verdict.

**Root cause**: Hidden await-protocol methods are not included in Requires
call-site discovery, and the existing Effects semantic inventory is not shared.

**Recommended fix**: Resolve GetAwaitExpressionInfo.GetAwaiterMethod, map the
await operand as receiver or reduced/static extension argument, and create a
candidate after operand evaluation can complete. Use the await expression as
the diagnostic location and existing flow/replay rules. Factor the symbol
selection with Effects. Add IsCompleted/GetResult only with an explicit
synthetic awaiter receiver and completion sequence.

**Regression coverage**: Custom GetAwaiter with Requires(false) under await must
emit one SP0027, matching a direct-call control. Add non-completing operand,
valid method, reduced extension, and synchronous-completion controls; separately
pin protocol ordering if later edges are modeled.

**Confidence**: High; executable analyzer/runtime probes isolated the missing
compiler-hidden call and matched the repository's existing Effects inventory.

### 506. [CONFIRMED] Null claim identities crash public protocol validators

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs around lines 514-520,
717-721, and 753-756.

**Description**: Manifest validation records null/blank identity errors but then
continues into GroupBy/ToDictionary indexes keyed by nullable CallableId or
ClaimId. `Dictionary<string,...>` rejects null keys. ValidateManifest therefore
throws for a null CallableId; a null ClaimId is initially rejected gracefully,
but embedding that manifest in WorkerVerifyResponse makes response validation
throw later.

**Reproduction**: An executable reflection probe bypassed PowerShell's property
coercion and produced:

    CallableId=null: ValidateManifest threw ArgumentNullException, Param=key
    CallableId="":   ValidateManifest returned Valid=False,
                     error=manifest.claim_callable
    ClaimId=null:    ValidateManifest returned Valid=False
                     Validate(response) threw ArgumentNullException, Param=key

**Impact**: A malformed in-memory compiler or assembler state escapes the public
validation boundary as an exception/infrastructure failure instead of stable
WorkerProtocolValidationResult codes, potentially masking the original failure.
Strict wire JSON already rejects null strings, but failure-recovery paths can
construct malformed models directly and CreateIncomplete explicitly tolerates
such inputs.

**Root cause**: Validation and dependent indexing are not separated. Invalid
identity fields are diagnosed but still materialized as dictionary keys.

**Recommended fix**: Centralize valid-key indexing and filter null/blank
CallableId and ClaimId values before GroupBy/ToDictionary. Retain the primary
identity errors and skip only dependent lookup checks that require an invalid
key. Do not weaken strict deserialization.

**Regression coverage**: Construct claims with `CallableId = null!` and
`ClaimId = null!`; both ValidateManifest and Validate(response) must return
invalid results without throwing and include manifest.claim_callable or
manifest.claim_id. Retain empty-string and strict JSON-null controls.

**Confidence**: High; exact public validators deterministically threw on null
keys while empty controls followed the intended error-result path.

### 507. [CONFIRMED] Decimal optional defaults are not matched by representation

**Location**: SharpProof.Contracts/ContractForSymbolMatcher.cs,
ExplicitDefaultValuesMatch around lines 420-439.

**Description**: Companion signature matching compares float and double default
values by their bit representation, but decimal values fall through to
`object.Equals`. Decimal equality ignores scale and signed-zero representation,
so metadata-distinct optional defaults are accepted as the same signature.

**Reproduction**: A target parameter defaulted to `-0.0m` and its companion to
`0.00m`. Canonical Roslyn plus the actual matcher/binder reported:

    target-bits=0,0,0,-2147418112
    companion-bits=0,0,0,131072
    member-signatures-match=True
    binding-success=True
    binding-failure=None
    uses-companion=True

The existing double-default regression establishes that optional defaults are
intended to match by representation, not merely numeric equality.

**Impact**: Validation and binding attach companion contracts despite a
metadata-observably different optional default. Callers omitting the argument
can therefore be analyzed against a companion signature that is not exact.

**Root cause**: Decimal has multiple bit representations for equal numeric
values, but the fallback comparer applies its value-equality semantics.

**Recommended fix**: Add a decimal branch comparing all four words returned by
`decimal.GetBits`; do not use decimal.Equals for signature identity.

**Regression coverage**: Require `-0.0m` vs `0.0m` and `0.0m` vs `0.00m` to
produce CompanionSignatureMismatch, while identical decimal bits bind. Retain
float/double bit controls.

**Confidence**: High; the actual binder accepted two defaults whose four-word
representations differ.

### 508. [CONFIRMED] Open-generic companions reject identical caller-owned type parameters after specialization

**Location**: SharpProof.Contracts/ContractForSymbolMatcher.cs, definition match
around lines 218-234, specialization/recheck around lines 288-316,
type-parameter matching around lines 558-564, and OwnersMatch around lines
743-764. Declaration validation occurs in
SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs
around lines 30-47.

**Description**: An open-generic companion validates against its target
definition and specializes correctly for a generic caller. The post-
specialization recheck then routes identical caller-owned type parameters to
OwnersMatch. That routine handles mapped target/companion owners but has no
exact-symbol/equal-owner fast path; a type parameter owned by the external
caller lies outside both scopes and is falsely rejected.

**Reproduction**: For `IRepository<T>.Read(T,bool)`, an open
`RepositoryContracts<T>` companion, and a generic forwarding caller, the
canonical probe reported:

    SPCF_IDS=<none>
    CALL_TARGET=IRepository<T>.Read(T, bool)
    MATCH_ORIGINAL=True
    SPECIALIZED_COMPANION=RepositoryContracts<T>.Read(IRepository<T>, T, bool)
    VALUE_TYPE_SYMBOL_EQUAL=True
    MATCH_SPECIALIZED=False
    RESOLUTION_FAILURE=CompanionSignatureMismatch
    BIND_SUCCESS=False
    CLAUSES=-1

The concrete IRepository<string> control matched, bound, used the companion,
and returned one Requires clause. An open-containing plus generic-member case
failed the same way.

**Impact**: Valid companion contracts disappear for invocations inside generic
forwarding code, even though analyzer declaration validation accepts the
companion and the specialized symbols are identical. The failure is fail-closed
but source-shape dependent.

**Root cause**: Structural owner mapping is applied before recognizing exact
type-parameter symbol identity.

**Recommended fix**: In the type-parameter branch, accept
SymbolEqualityComparer.Default exact identity before ordinal/owner mapping. A
narrow exact-symbol check avoids weakening structural comparison between target
and companion parameters.

**Regression coverage**: Bind the generic forwarding invocation and require
success, UsesCompanion, and one Requires clause. Retain concrete specialization,
generic-member, mismatched-owner, and ordinal-mismatch controls.

**Confidence**: High; a canonical Linux probe showed symbol equality true at
the exact point where OwnersMatch returned false.

### 509. [CONFIRMED] Renewal-disposal cancellation is fabricated into ProjectTimeout

**Location**: SharpProof.Worker/SharpProofWorker.cs around lines 298-308,
597-599, 617-618, 412, and interruption mapping around lines 83-104.

**Description**: After a genuine method timeout, a verification lane renews its
backend and synchronously disposes the old owner. Dispose receives no
CancellationToken, yet the renewal catch filter excludes
OperationCanceledException. Such an unrelated OCE escapes to the worker's
unconditional outer cancellation catch. With caller and project tokens both
live, Interrupted() fabricates TimedOut/ProjectTimeout for the whole manifest.

**Reproduction**: An exact-production reflection probe gave the lane an owned
backend whose Dispose throws a fresh OCE, then invoked Renew:

    ESCAPED_EXCEPTION=OperationCanceledException
    EXCEPTION_TOKEN_CANCELED=False

The deterministic outer path sees no registered cancellation source and maps
that exception to ProjectTimeout. The sibling InvalidOperationException test
already expects InfrastructureFailure from the same disposal point.

**Impact**: One method timeout followed by a backend cleanup failure erases the
truthful MethodTimeout plus InfrastructureFailure history and mislabels the
entire project as budget-expired. Retry, telemetry, and evidence attribution
operate on the wrong terminal cause.

**Root cause**: A tokenless lifecycle callback is allowed to use OCE as an
untrusted exception type, but the catch filters treat the type alone as proof of
caller/project cancellation.

**Recommended fix**: Catch OCE from renewal Dispose as InfrastructureFailure,
retaining only OOM/StackOverflow exclusions. More generally, map an outer OCE to
Interrupted only when a registered caller/project boundary is actually
canceled; otherwise return typed InfrastructureFailure.

**Regression coverage**: Extend InvalidRenewalStateFailsClosedWithTypedEvidence
with an OCE-throwing Dispose after a short method timeout and long live project
budget. Require RunStatus Failed, reason InfrastructureFailure, evidence reasons
MethodTimeout plus InfrastructureFailure, and specifically no ProjectTimeout.

**Confidence**: High; the exact private renewal method leaked an OCE carrying an
uncanceled token, and the outer classification has no alternate branch.

### 510. [CONFIRMED] Large string-literal encoding ignores cancellation before solver execution

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 109-145, 286-301, and
EncodeStringLiteral around lines 694-702.

**Description**: A query token is checked during depth validation and after goal
encoding/assertion, but it is not retained by QueryEncoder. EncodeStringLiteral
creates two native AST wrappers per UTF-16 code unit and one giant MkConcat
without a checkpoint. The literal is one IR leaf, so depth validation completes
immediately and Context.Interrupt cannot stop this pre-solver construction.

**Reproduction**: A public CheckAsync probe compared a string variable to a
100,000-character literal, canceled after the backend gate was acquired, and
reported:

    gate-entered=True
    cancel-request-ms=33
    cancel-observed-ms=19386
    cancel-overshoot-ms=19352
    reuse-status=Unsatisfiable failure=None

No solver Check occurred before the long encoding/unwind completed.

**Impact**: Cancellation can overshoot by roughly 19 seconds while monopolizing
the serialized backend gate and consuming CPU/native memory. The failure is
fail-closed and the backend remains reusable, but public ProofKernel/backend
callers cannot enforce timely cancellation.

**Root cause**: Encoding is modeled as an indivisible operation. The token is
not threaded through wide-term construction, and the LINQ-shaped literal build
has no bounded interruption points.

**Recommended fix**: Retain/pass the token through QueryEncoder and all recursive
encoders. Use an imperative string-unit loop with bounded checks, check before
large concatenation, and preferably chunk/balance concat construction so no
single native call is unbounded.

**Regression coverage**: With a deterministic encoder-progress hook, cancel
after N units and require prompt OCE before solver.Check, bounded owned-object
growth and cleanup, then successful reuse. Cover other wide expression forms.

**Confidence**: High; the exact public backend delayed observed cancellation by
19.3 seconds and then passed its reuse control.

### 511. [CONFIRMED] Recorded fuzz failure seeds cannot replay their own cases

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs, FuzzFailure around lines 7-15,
case generation around lines 149-155 and 191-198, failure construction around
lines 262-349, and CreateCaseSeed around lines 562-572; CLI options in
Tools/SharpProof.Fuzz/Program.cs and FuzzOptions.cs.

**Description**: FuzzFailure.Seed stores the derived case seed
`CreateCaseSeed(campaignSeed,index)`. The only CLI seed option is interpreted as
a campaign seed and is always hashed again with a fresh index. Therefore the
obvious replay command `--seed <failure.Seed> --cases 1` generates a different
case. The CLI exposes no case index, case-seed, or replay mode.

**Reproduction**: The production assembly probe reported:

    campaign --seed=20260523 failure Case=123
    recorded FuzzFailure.Seed=-736518015
    natural replay derives caseSeed=545807135
    recorded seed equals replay-derived seed=False
    original frontend SHA=ce3e6ed441f8588af4dbcbcdcaae8626d46bb590a906c957bdcfc098cb16a7a6
    replay frontend SHA=321dec8c3a17dd36ae3194b651f9a698c011052a9736f96a794ea00625a79f4a
    frontend bundles identical=False
    --case-index accepted=false

**Impact**: A deterministic retained mismatch appears unreproducible when its
own Seed is used. The only workaround is the enclosing campaign seed plus
rerunning every case from index zero; a late millionth case requires replaying
the full prefix, and direct finite-SMT replay is unavailable.

**Root cause**: Evidence names an internal derived value `Seed` without
preserving its campaign mapping or a CLI mode that consumes it directly.

**Recommended fix**: Record explicit CampaignSeed, CaseIndex, and CaseSeed, and
emit an exact replay descriptor/command. Add a mutually exclusive replay-case
mode using campaign seed/index or a case-seed mode that bypasses derivation. A
public single-case runner should execute all three oracle bundles directly.

**Regression coverage**: For campaign 20260523/index 123, replay mode must match
normal RunAsync's effective seed and frontend/finite/partial fingerprints. Cover
index zero, maximum index, CLI validation, and ensure ordinary `--seed
<CaseSeed>` is never advertised as replay.

**Confidence**: High; the production derivation, CLI parser, and generated
bundle hashes demonstrated that recorded-seed replay changes the case.

### 512. [CONFIRMED] Relational specification packs accept ill-typed authoritative terms

**Location**: SharpProof.CompilerCollector/CompilerArtifact/
CompilerSpecificationPackProvider.cs, TryBuild around lines 165-175,
Instantiate around lines 257-292, ParseMethod around lines 544-560, and ParseTerm
around lines 616-667; typed rejection in SharpProof.Ir/IrFactory.cs around lines
448-467.

**Description**: Pack parsing validates JSON shape, literals, operator names,
and only the outer result type. It does not recursively validate parameter
ordinals/types, operator operand signatures, equality types, or conditional
guard/branch types. Provider construction therefore accepts an unusable audited
pack. At the first matching call, IrFactory throws ArgumentException and
TryBuild silently converts it to false, making the selected relation disappear.

**Reproduction**: A complete pack declared an Integer conditional whose
condition was also Integer. The exact private parser and instantiator reported:

    PARSE_PACK=ACCEPTED
    TERM_TYPE=ConditionalTerm
    DECLARED_TYPE=Integer
    INSTANTIATE=REJECTED
    INNER_TYPE=System.ArgumentException
    MESSAGE=The conditional guard must be boolean.

**Impact**: A maintainer can update and correctly pin an embedded pack whose
catalog loads successfully but whose advertised relation always abstains at
use. Dependent verification degrades to Unknown without a configuration or
catalog error. This is fail-closed but violates strict-authority guarantees.

**Root cause**: Semantic type checking is deferred from authority loading to
per-call IR construction, and the latter's exception is intentionally treated
as an ordinary unsupported relation.

**Recommended fix**: During ParseMethod, recursively infer and validate every
term against method parameter types and IrOperatorCatalog. Require in-range
ordinals, exact declared parameter types, valid unary/binary signatures,
same-type supported equality, Boolean conditional guards, and equal branch/
declared types. Throw path-qualified InvalidDataException eagerly; retain the
runtime catch as defense in depth.

**Regression coverage**: Reject integer guard, unequal branches, wrong unary/
binary operands/results, out-of-range ordinal, and parameter-type mismatch at
parse/load time. Retain positive coverage for all scalar operators and an
integration proof that a valid enabled pack produces its summary.

**Confidence**: High; the exact parser accepted and exact instantiator rejected
the same authoritative term for a deterministic type error.

### 513. [CONFIRMED] One acceptance result can mint both Debug and Release receipts

**Location**: producer eng/acceptance/Verify.ps1 around lines 209-219; consumer
scripts/Write-SharpProofQualificationReceipt.ps1 around lines 62-67; matrix
eng/acceptance/preview-evidence.v1.json rows 13-14; final qualification in
scripts/Invoke-SharpProofReleaseContainer.ps1 around lines 169-210.

**Description**: Acceptance evidence records `command` and `configuration`, but
the receipt writer validates only schema version, passed status, and commit for
both acceptance-debug and acceptance-release. It trusts the caller-supplied gate
label instead of matching it to the evidence's configuration. Final
qualification trusts the receipt label/hash and does not reopen this dimension.

**Reproduction**: One canonical-Docker temp fixture generated Release evidence
and invoked the unchanged writer twice:

    EvidenceConfiguration=Release
    ReleaseRun.ExitCode=0
    DebugRun.ExitCode=0
    ReleaseReceiptGate=acceptance-release
    DebugReceiptGate=acceptance-debug
    SameEvidencePath=true
    SameEvidenceSha=true

**Impact**: Qualification can claim both required matrix rows without executing
Debug acceptance at all. A wiring or manual receipt-regeneration error is
silently certified rather than rejected.

**Root cause**: The receipt's gate identity is derived from an argument, not
from the producer identity already present in the hashed evidence.

**Recommended fix**: For each acceptance gate, require
`command -ceq 'acceptance'` and exact configuration Debug or Release. Include the
verified configuration in the receipt and recheck it during final qualification.
Prefer a shared full timing-schema validator.

**Regression coverage**: Debug->debug and Release->release pass; both cross
pairs fail. Reject missing/wrong command or configuration. Final qualification
must reject two receipts that hash the same Release evidence for both rows.

**Confidence**: High; both receipt types were produced from one identical
Release file and SHA in the canonical image.

### 514. [CONFIRMED] Collection-initializer Add calls are found but marked unreplayable

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines
188-208, 425-471, and 538-575; early Unknown mapping in
RequiresCallSiteAnalyzer.cs around lines 339-342.

**Description**: Roslyn exposes collection-initializer Add as an implicit
IInvocationOperation and discovery finds it with complete flow state. Candidate
replayability nevertheless requires the call syntax span to equal the whole
expression body or top-level statement expression. The hidden Add invocation's
syntax is only the collection element, nested under object creation, so exact-
span validation fails and analysis returns Unknown before evaluating Requires.

**Reproduction**: `new Bag { -1 }` uses Bag.Add(int) with
`Contract.Requires(value > 0)`. Probe output:

    OP/CFG Invocation implicit=True syntax=-1 target=Bag.Add(int)
    FLOW implicit-add-has-state=True
    CANDIDATE can-replay=False flow=Complete
    SP0027 emitted only for explicit bag.Add(-1) control
    runtime generated=1 explicit=1

Compiler errors and other analyzer diagnostics were zero.

**Impact**: A deterministic hidden Add precondition violation is silently
missed with no SP0047 fallback, while the equivalent explicit call is reported.

**Root cause**: Replay-prefix logic recognizes only top-level source spans and
has no collection-initializer evaluation-order model.

**Recommended fix**: Add collection-initializer-aware prefix replayability.
Walk the owning initializer/object creation in language order and require
constructor/arguments, all prior elements, receiver, and current arguments to
complete before admitting the candidate. Reuse DefiniteOperationFacts; do not
blanket-admit nested invocations.

**Regression coverage**: Invalid Add under collection initializer must emit one
SP0027 at the element, matching direct call. Cover valid elements, throwing
constructor/argument, non-completing prior Add, and multiple completing elements
in left-to-right order.

**Confidence**: High; operation, CFG, flow, candidate, analyzer, and runtime
probes all isolate the exact false replayability decision.

### 515. [CONFIRMED] Release regular-file validation accepts FIFOs and hashing can hang

**Location**: scripts/SharpProof.ReleaseChecksums.ps1,
Test-SharpProofExactRegularFileSet around lines 115-137 and topology delegation
around lines 184-187; hashing in scripts/Publish-SharpProofRelease.ps1 around
lines 322-360.

**Description**: The release topology validator rejects directories, reparse
points, nesting, and duplicate device/inode identities but never checks Linux
inode mode. PowerShell reports a FIFO as File, Leaf, non-container, and Normal;
`stat %d:%i` also succeeds. An exact-name bundle containing a FIFO therefore
passes topology, then Get-FileHash blocks waiting for a writer.

**Reproduction**: A canonical Linux fixture replaced one of the exact nine
expected artifact names with mkfifo:

    BundleTopologyAcceptedFifo=true
    ValidationError=""
    FifoStatType=fifo
    EntryCount=9 ExpectedEntryCount=9
    PathTypeLeaf=true GetChildItemFileCount=1
    GetFileHashExit=137 ElapsedMs=2012

The last probe killed the blocked hash after two seconds.

**Impact**: An accidental non-regular artifact is certified by topology and can
hang release publication indefinitely instead of failing with an attributable
validation error.

**Root cause**: Cross-platform PowerShell leaf/file attributes are treated as a
regular-file guarantee, while device/inode identity is collected without file
type.

**Recommended fix**: Collect numeric stat mode (for example `%f`) and require
`(mode & 0xF000) == 0x8000` before accepting identity. Emit a dedicated
non-regular-member error before any content read/hash.

**Regression coverage**: Create an exact valid nine-name bundle with one FIFO;
topology must throw before hashing. Retain regular-file positive, directory,
symlink/reparse, duplicate identity, and cleanup-in-finally controls.

**Confidence**: High; topology accepted a real FIFO and the next production hash
blocked until killed.

### 516. [CONFIRMED] Congruence-implied Int64 endpoints are treated as infinities

**Location**: SharpProof.Dataflow/IntervalDomain.cs, construction around lines
61-96, LessThanOrEqual around lines 111-118, and Add overflow handling around
lines 277-287.

**Description**: Create computes the first and last signed-Int64 values matching
a congruence but retains null when the caller supplied an unbounded marker.
Ordering compares those raw nullable bounds, and arithmetic substitutes
Int64.MinValue/MaxValue for null. For nontrivial congruences those extremes may
not be members, so semantically identical domains behave differently depending
on whether effective endpoints were written explicitly.

**Reproduction**:

    implicit = Create(null, null, 10, 0)
    explicit = Create(-9223372036854775800,
                       9223372036854775800, 10, 0)

Fresh-source container output:

    boundary-witnesses=9 membership-mismatches=0
    implicit<=explicit=False explicit<=implicit=True equivalent=False
    implicit+1=[-inf, +inf]
    explicit+1=[-9223372036854775799, 9223372036854775801] mod 10 = 1
    bug-reproduced=True

Both inputs denote exactly all representable multiples of ten.

**Impact**: Lattice ordering and transfer precision depend on representation.
Equivalent states can trigger needless dataflow changes, and safe arithmetic
widens to Top, losing proofs and creating avoidable warnings/abstentions.

**Root cause**: Nullable storage serves both as a widening marker and as semantic
infinity. LessThanOrEqual and overflow analysis ignore the congruence's already-
known effective signed endpoints.

**Recommended fix**: Add helpers returning effective first/last congruent Int64
endpoints and use them for semantic ordering and TryAddBounds. Retain nullable
storage only as representation/widening metadata. Alternatively canonicalize
all nontrivial congruence bounds in Create after auditing widening behavior.

**Regression coverage**: Require bidirectional LessThanOrEqual and equivalence
for the pair above; AddConstant(...,1) must yield equivalent, non-Top results
with the exact two shifted endpoints. Cover negative residues, modulus one,
near-boundary overflow, and widening controls.

**Confidence**: High; a source-compiled container probe proved equal membership
and divergent lattice/arithmetic results against the exact baseline blob.

### 517. [CONFIRMED] Manifest hashing materializes multiple full payload copies

**Location**: SharpProof.Worker.Protocol/ProtocolManifest.cs around lines 44-50
and 67-75; canonical payload construction in
SharpProof.Worker.Protocol/ProtocolJsonSupport.cs around lines 208-267.

**Description**: ComputeManifestHash builds the complete framed payload in a
growing StringBuilder, copies it to one full UTF-16 string, allocates a full
UTF-8 byte array, and only then computes SHA-256. ManifestsEqual additionally
materializes both complete canonical payload strings. This is separate from the
quadratic claim lookup in finding 497 and occurs even on already-canonical input.

**Reproduction**: Warmed direct hashing probes measured:

    10,000 claims, 3,309,011-byte payload: 18,022,992 bytes allocated
    20,000 claims, 6,629,011-byte payload: 36,013,416 bytes allocated
     5,000 fully valid claims:             9,810,992 bytes allocated

The valid manifest subsequently passed ValidateManifest, and every hash had the
expected 64-character shape.

**Impact**: SealManifest, validation rehashing, strict expected-manifest checks,
and equality can create tens or hundreds of megabytes of transient
StringBuilder chunks, LOH strings, and byte arrays for representable manifests.
GC pressure consumes verification time and increases peak memory before response
serialization.

**Root cause**: The canonical hash API accepts only a completed byte array, and
the framing writer is string-backed rather than incremental.

**Recommended fix**: Preserve the exact framing/hash identity while streaming
frames into IncrementalHash through a small reusable buffer or IBufferWriter.
Write the ASCII UTF-16-code-unit length, colon, strict UTF-8 value, and semicolon
incrementally; use Utf8Formatter for numbers. Compare canonical fields/sequences
or incremental streams in ManifestsEqual instead of building both strings.

**Regression coverage**: Retain the pinned known hash and add non-ASCII and
ill-formed-UTF16 compatibility cases. Warm a fully valid 5k-claim hash and set a
generous allocation ceiling far below 9.8 MB. Cover equality true and one-field
differences without payload materialization.

**Confidence**: High; public hash allocation scales at about 5.4 times payload
size on already-constructed, valid manifests.

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

### 521. [CONFIRMED] SPMETA002 ignores mutable get-only static auto-properties

**Location**: SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs,
AnalyzeProperty around lines 573-585; corresponding field logic around lines
552-564.

**Description**: Static auto-property analysis requires a setter, so every
get-only property is skipped regardless of storage type. A get-only static
List<T> or array cannot be rebound but remains process-wide mutable state through
its members. The equivalent static readonly mutable-reference field is already
reported by SPMETA002.

**Reproduction**:

    static List<int> Values { get; } = new();

Analyzer controls produced:

    readonly List field:       SPMETA002 count=1
    get-only List property:    SPMETA002 count=0
    settable List property:    SPMETA002 count=1

All compiler-error counts were zero.

**Impact**: Soundness-critical analyzer code can retain mutable process-wide
state in a property and evade the rule, allowing cross-compilation leakage or
concurrent-analysis nondeterminism.

**Root cause**: Rebindability (`SetMethod != null`) is used as a proxy for value
mutability, unlike the field path's mutable-reference classification.

**Recommended fix**: For static auto-properties, report when a setter exists or
`IsMutableReferenceStorage(property.Type)` is true. Preserve immutable get-only
properties.

**Regression coverage**: Get-only List<T> and array properties must report;
get-only int and ImmutableArray<T> controls remain clean. Retain field and
settable-property controls.

**Confidence**: High; the exact analyzer distinguished only the property setter
across otherwise equivalent mutable storage.

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

### 528. [CONFIRMED] A null manifest Claims collection crashes response validation

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs, sanitized manifest
validation around lines 497-520 and raw response projection around lines
751-756.

**Description**: ValidateManifestCore records `manifest.claims` for a null
collection and substitutes a safe empty local. ValidateRun later ignores that
sanitized value and directly calls `response.Manifest.Claims.Where(...)`, which
throws ArgumentNullException(source). This differs from finding 506's null
element identity keys; the collection itself is absent.

**Reproduction**: Starting from a fully valid empty Complete response:

    control: Validate returned IsValid=True
    after Manifest.Claims=null!:
      ArgumentNullException: Value cannot be null. (Parameter 'source')
      at Enumerable.Where
      at WorkerProtocolJson.ValidateRun line 753

**Impact**: Malformed in-memory failure models escape the public validation
boundary as infrastructure exceptions and mask the stable `manifest.claims`
error that the earlier stage already attempted to return. Strict JSON null
remains a separate parse rejection.

**Root cause**: Sanitized collection state is local to one validation phase and
dependent validation dereferences the raw object graph.

**Recommended fix**: Gate projection on nonnull claims or use
`response.Manifest?.Claims ?? []`, combined with valid-key filtering from 506.
Preserve the primary error and skip only checks requiring absent declarations.

**Regression coverage**: Claims=null! must not throw, must return invalid, and
must include manifest.claims. Retain valid empty and strict JSON-null controls,
including expected-hash/manifest overloads.

**Confidence**: High; changing only the collection from empty to null moves the
public validator from valid result to a deterministic LINQ exception.

### 529. [CONFIRMED] semantic-tests silently drops its TestFilter argument

**Location**: scripts/Invoke-SharpProofContainer.ps1 semantic-tests dispatch
around lines 125-128; implemented parameter in
scripts/Invoke-SharpProofSemanticTests.ps1 around lines 11 and 127-132.

**Description**: The container wrapper accepts `-TestFilter`, and the semantic
runner implements it, but the dispatch branch forwards only Configuration.
Targeted commands therefore run the broad default filter. Requests for
Performance, Coverage, or Corpus categories can instead execute the default
suite that explicitly excludes those tests.

**Reproduction**: A canonical isolated dispatcher probe ran:

    tooling semantic-tests -TestFilter FullyQualifiedName~SentinelProbe

The runner observed `SEMANTIC_FILTER_PROBE=<>`. Adding only
`-TestFilter $TestFilter` to the temporary dispatch produced
`SEMANTIC_FILTER_PROBE=<FullyQualifiedName~SentinelProbe>`. Both exited zero
without running the broad suite.

**Impact**: Developers and automation can receive success for a targeted
semantic test command that never runs the requested tests, while also wasting
time on unrelated defaults.

**Root cause**: Parameter plumbing terminates at the top-level switch case.

**Recommended fix**: Forward `-TestFilter $TestFilter` in the semantic-tests
branch, preserving null/empty default behavior.

**Regression coverage**: Add a branch-scoped architecture assertion and trusted
mutation that removes forwarding. Prefer a behavioral dispatcher fixture that
captures the bound parameter for empty and nonempty filters.

**Confidence**: High; the exact dispatch lost the value and a one-argument
forwarding change delivered it unchanged.

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

### 532. [CONFIRMED] Null SMT options allocate a native Context before validation

**Location**: SharpProof.Smt/IrSmtBackend.cs field initialization around lines
3 and 6-9.

**Description**: `_context = new()` executes before `_options =
ArgumentNullGuard.NotNull(...)`. When null options throw, no backend instance is
returned, so the already-created native Context cannot be explicitly disposed
and survives until finalization.

**Reproduction**: After draining finalizers, 100 invalid constructions and 100
valid disposed controls produced:

    invalid-caught=100
    pending-initial=3
    pending-before-collect=3
    pending-after-invalid-collect=102
    pending-after-disposed-control=3

**Impact**: Repeated invalid DI/configuration calls retain full native contexts
until GC, creating unmanaged-memory and finalizer latency. Native initialization
failure can also preempt the promised options ArgumentNullException.

**Root cause**: Resource acquisition occurs in an earlier field initializer than
argument validation.

**Recommended fix**: Use a conventional constructor body that validates/stores
options first, then creates Context, or reorder through a safe factory. Ensure
partial construction disposes any acquired resource.

**Regression coverage**: With an injected/counting context factory, null options
must throw `options` and create zero contexts; valid construction creates and
disposes exactly one. Cover native-unavailable ordering.

**Confidence**: High; failed construction added one pending native-context
finalizer per call while disposed controls did not.

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

### 536. [CONFIRMED] FuzzSummary.Passed accepts an impossible case count

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs around lines 93-110 and
127-133; authoritative maximum in FuzzOptions.cs around lines 7-8.

**Description**: FuzzSummary.Passed checks only `Cases > 0`, whereas options and
RunAsync cap cases at 1,000,000. A deserialized/constructed summary with
1,000,001 cases, matching agreement counters, complete coverage, and no failures
self-certifies even though the runner refuses to execute that domain.

**Reproduction**:

    MaximumCases=1000000
    Summary.Cases=1000001
    Summary.Passed=True
    Runner accepted out-of-domain cases=False
    Runner exception=ArgumentOutOfRangeException

**Impact**: Callers or evidence tests trusting Passed can accept impossible fuzz
evidence that no conforming runner could have produced.

**Root cause**: The upper-domain invariant is duplicated in parsing/execution but
omitted from result self-validation.

**Recommended fix**: Require `Cases <= FuzzOptions.MaximumCases` in Passed and
mirror it in the PowerShell result authority if that authority also accepts only
positive counts.

**Regression coverage**: MaximumCases+1 must fail; exactly MaximumCases can pass
when all other invariants hold. Retain zero, parallelism, counter, and coverage
invalid cases.

**Confidence**: High; production summary and runner disagree on the same exact
case count under an executable probe.

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

### 538. [CONFIRMED] Conditional API-spec terms cannot infer exact sequence-null types

**Location**: SharpProof.Specs/ApiSpecInstantiation.cs, context-free Null around
lines 136-183, equality peer inference around lines 193-263, and Conditional
around lines 266-284; validator in ApiSpecTermValidator.cs around lines 59-73.

**Description**: The validator admits a Sequence null in a conditional whose
other branch has the same declared kind. Instantiation handles exact null type
inference only as an equality special case. Conditional instantiates each branch
independently, so the null fails before the concrete sibling can provide its
exact IrTypeId.

**Reproduction**: A valid sequence-result API spec used a Boolean conditional
with Sequence null and Result branches:

    TABLE_VALIDATION=ACCEPTED POSTCONDITIONS=1 RESULT_KIND=Sequence
    INSTANTIATION_STATUS=Failed FAILURE_KIND=UnsupportedValueType
    DIRECT_TYPED_IR=ACCEPTED CONDITIONAL_TYPE=Sequence
    EXACT_SEQUENCE_TYPE_MATCH=True

**Impact**: A well-typed, validator-accepted spec loses its entire call
application in the worker and degrades verification to Unknown. Concrete
Reference subtypes have the analogous exact-type risk.

**Root cause**: Contextual nullable-type inference is an equality-only syntactic
special case instead of a bidirectional term rule.

**Recommended fix**: In Conditional, instantiate the non-null branch first and
use it as peer context for a direct null branch, symmetrically. More robustly,
thread an optional expected IrTypeId through terms so nested null expressions
inherit exact context. Preserve UnsupportedValueType only when context is absent.

**Regression coverage**: Sequence null on either branch with exact peer must
succeed; add custom Reference peers, incompatible exact peers -> TypeMismatch,
and peerless null/null -> UnsupportedValueType. Verify worker predicates survive.

**Confidence**: High; validator and typed IR accept the term while only the
context-free instantiator rejects it.

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

### 540. [CONFIRMED] Direct field loads bypass the supported value-domain gate

**Location**: SharpProof.Frontend/RoslynProgramLowerer.cs around lines 242-255
and 359-362; unsupported scalar mapping in RoslynOperationLowerer.cs around lines
101-110; domain authority in CompilerIdentityBridge.cs around lines 97-118.

**Description**: Program lowering special-cases field/array loads, constructs a
location, emits Load, and returns without Observe or supported-value checking.
An unsupported scalar field such as double is mapped to IR Reference and the
whole program is marked Exact.

**Reproduction**: `static double Value; static double Target() => Value;`
produced:

    expression-exact=False reason=UnsupportedOperationKind
    program-exact=True program-abstentions=0
    instructions=Goto,Load,Return,Return
    field-special-type=System_Double load-ir-type=Reference

**Impact**: The closed Frontend subset is bypassed and downstream consumers
receive an Exact program with invalid reference/value-domain assumptions.

**Root cause**: Successful location construction is conflated with support for
the value loaded from that location.

**Recommended fix**: Gate loaded value.Type through IsSupportedValueDomain.
For unsupported values, preserve receiver/index evaluation, record UnsupportedType,
and return havoc/opaque rather than an exact load. Keep supported bool/integer/
reference loads unchanged.

**Regression coverage**: Static-double return must be non-exact with
UnsupportedType and no accepted reference-typed exact load; supported long field
remains exact. Add unsupported instance/array element controls.

**Confidence**: High; expression lowering rejected the same double that program
lowering emitted as an exact Reference.

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

### 543. [CONFIRMED] A completing local coalesce assignment suppresses later Requires diagnostics

**Location**: SharpProof.Effects/ManagedAbstractFlow.cs,
DefiniteOperationFacts.CompletesNormally around lines 1842-1917; prefix use in
SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines 461-471;
Unknown mapping in RequiresCallSiteAnalyzer.cs around lines 339-342.

**Description**: The strict completion allowlist has no
ICoalesceAssignmentOperation case. Every preceding `??=` is considered
non-completing, so a later reachable ordinary call becomes unreplayable even
when local coalesce assignment definitely completes. Effects has a separate
completion evaluator that already models the operation.

**Reproduction**:

    string? value = null;
    value ??= "ok";
    Positive(-1);

Candidate output was `can-replay=False flow=Complete`; no SP0027 appeared. The
direct control reported SP0027. Runtime reached the call once. A definitely
throwing RHS correctly prevented the later call, showing blanket admission would
be wrong.

**Impact**: One ordinary prior statement silences all later concrete
precondition refutations in that path without a fallback diagnostic.

**Root cause**: Operation completion semantics are duplicated and the analyzer's
allowlist omits coalesce assignment entirely.

**Recommended fix**: Add sound coalesce-assignment evaluation order for local,
parameter, and discard targets when target/RHS/write definitely complete. Keep
property/index/field shapes conservative until getter/setter/receiver effects
are modeled, or share the existing completion evaluator if its contract aligns.

**Regression coverage**: Completing local ??= followed by invalid call reports;
throwing RHS suppresses. Add dependent nonnull/null state, direct control, and
unknown property target fail-closed cases.

**Confidence**: High; candidate flow was complete and runtime reached the call,
with the missing operation-kind case isolating replay rejection.

### 544. [CONFIRMED] Release-authority closure skips ordinary relative psm1 imports

**Location**: scripts/Get-SharpProofReleaseAuthorityClosure.ps1 around lines
51-70; a manual module root around line 28 masks one instance.

**Description**: The closure walker can parse queued psm1 files, but its
path-qualified extension list excludes psm1 and its sibling-literal branch
recognizes only ps1. A normal `Import-Module (Join-Path $PSScriptRoot
'Module.psm1')` therefore executes code that the independently derived closure
omits. One publication module appears only because it is manually rooted.

**Reproduction**: Current omitted imports include ContainerExecution,
MutationBaselines, MutationEvidence, and MutationScheduling modules. A canonical
fixture reported:

    ImporterInClosure=true
    ImportedPsm1InClosure=false
    ImportedPsm1Tracked=true
    ModuleChanged=true
    ClosureDigestUnchangedAfterModuleEdit=true

**Impact**: The closure-specific validator can pass while an executed release
module changes, disappears, or remains undeclared without affecting its path set
or digest. Current broader TCB inventory happens to list these examples, but the
closure invariant does not guarantee that redundancy for future modules.

**Root cause**: Both literal dependency grammars omit `.psm1`; fixture coverage
uses only ps1 dependencies and an explicit module root.

**Recommended fix**: Recognize canonical psm1 paths in both branches, preferably
by resolving literal dot-source and Import-Module AST commands rather than regex.
Queue the module so missing/moved paths use existing validation.

**Regression coverage**: Import a tracked sibling module from a closure member;
require inclusion, digest change on edit, and failure on move/delete. Retain an
unimported module decoy.

**Confidence**: High; real and fixture modules are tracked, executed, absent
from closure, and digest-invisible.

### 545. [CONFIRMED] Conditional truth-operator exceptions do not make catches reachable

**Location**: SharpProof.Effects/ExceptionHandlerReachability.cs around lines
575-599; the correct semantic lookup exists in OperationCompletionEvaluator.cs
around lines 1042-1053.

**Description**: For user-defined `a && b`/`a || b`, Roslyn's binary
OperatorMethod is op_BitwiseAnd/op_BitwiseOr, but runtime first invokes
op_False/op_True on the left operand. Handler reachability models only the binary
operator, so an exception from the truth operator does not make a matching catch
reachable even though completion evaluation knows that call.

**Reproduction**: op_False threw TruthOperatorException and a matching catch
wrote static state 1729. The baseline oracle failed only the missing static write:

    runtime exception=TruthOperatorException
    runtime state=1729
    uncaught summary contains exception=True
    caught Writes.IsUnknown=false Completeness=Complete
    caught Writes.Static=False (expected True)

A temporary fix resolving the truth operator made the oracle and an existing
throwing-operator regression pass 1/1 each.

**Impact**: Analysis can return a Complete, non-unknown write summary that omits
a runtime-reachable catch's static mutation.

**Root cause**: Exception reachability models the visible binary method but not
the earlier compiler-selected truth protocol call.

**Recommended fix**: Resolve op_False for ConditionalAnd and op_True for
ConditionalOr after the left operand completes; add its initialization/throw
potential and permit RHS/binary operator only if it completes. Share the
completion evaluator's lookup.

**Regression coverage**: Runtime/analyzer caught and uncaught cases for both
op_False/&& and op_True/||, requiring exact static write, complete known summary,
and correct escaping throw behavior.

**Confidence**: High; runtime and analyzer disagree on one hidden call, and the
minimal semantic insertion fixes both focused tests.

### 546. [CONFIRMED] Direct SharpProofVerify verifies a stale compiler manifest

**Location**: SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets,
_SharpProofVerifyCore around lines 223-224 and public SharpProofVerify around
lines 320-323.

**Description**: `AfterTargets="CoreCompile"` schedules verification after a
normal build but does not make direct target invocation depend on compilation.
The public target depends only on initialization and VerifyCore; VerifyCore
depends on ResolveReferences. A previous compiler-manifest.input.json is reused
after source edits.

**Reproduction**: A supported-host consumer first proved Boolean identity, then
changed it to `return !value`:

    initial Build: CoreCompile=1, manifest SHA A, outcome Proven
    direct -t:SharpProofVerify:
      exit=0 CoreCompile=0 manifest SHA A outcome Proven
    full Build control:
      exit=1 CoreCompile=1 manifest SHA B outcome Refuted, SP0051

**Impact**: The advertised public target can publish successful proof evidence
for prior source state while current source is refuted.

**Root cause**: Automatic after-compile hook and callable public entrypoint share
one target graph whose direct path lacks CoreCompile/freshness.

**Recommended fix**: Separate the automatic AfterTargets hook from the public
entrypoint and make explicit SharpProofVerify depend on CoreCompile before
VerifyCore. Alternatively prove manifest freshness against all current compiler
inputs, though compiling matches public target expectations.

**Regression coverage**: Build identity, edit to negation, invoke direct target;
require CoreCompile, changed manifest hash, nonzero Refuted outcome and SP0051.
Retain ordinary Build parity.

**Confidence**: High; the direct target preserved the exact stale manifest and
verdict while a full build immediately refuted current source.

### 547. [CONFIRMED] SPMETA009 classifies ordinary prose as synthesized C# expressions

**Location**: SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs, fragment
catalog around lines 70-71, concatenation analysis around lines 343-367, and
fragment extraction around lines 446-471.

**Description**: Every string concatenation is scanned without sink context, and
any occurrence of a short fragment such as `" is null"`, `" is not null"`, or
`"=>"` is treated as C# expression generation. Human-facing exception,
diagnostic, or log prose therefore triggers Error-level SPMETA009.

**Reproduction**:

    new InvalidOperationException(
        "Metadata is null for member " + member)

produced one SPMETA009 with zero compiler errors. An actual expression-shaped
control also reported; benign prose without the fragment did not.

**Impact**: Ordinary dynamic messages are build-blocked and require wording or
construction workarounds unrelated to source-synthesis soundness.

**Root cause**: Substring presence is accepted as expression identity without
structural template or destination evidence.

**Recommended fix**: Require evidence that the complete constructed template is
expression-shaped or flows to a typed source-expression sink/builder. Preserve
actual expression cases while excluding arbitrary prose.

**Regression coverage**: The exception prose above and `Missing metadata for
member` remain clean; `"(" + name + ") is null"` reports. Add diagnostic/log
sinks and common fragment controls.

**Confidence**: High; one common-English substring is the only difference
between accepted and rejected prose.

### 548. [CONFIRMED] Production fuzz JSON emits coverage properties the strict parser rejects

**Location**: Tools/SharpProof.Fuzz/FuzzRunner.cs, FrontendFuzzCoverage around
lines 38-66; serialization in Tools/SharpProof.Fuzz/Program.cs around lines
33-60; strict property set in scripts/Assert-SharpProofFuzzRunnerResult.ps1
around lines 142-149; campaign use around lines 145-149.

**Description**: FrontendFuzzCoverage exposes public read-only derived getters
HasValidCounts and HasExpandedCategories. Default System.Text.Json serialization
emits both. The strict schema parser permits exactly the 13 counters and rejects
those two fields. Fixture JSON is hand-built without the computed properties,
so tests validate a shape the producer never emits.

**Reproduction**: A real passing FuzzSummary serialized with production options:

    producer Passed=True bytes=749
    coverage keys=13 counters,HasValidCounts,HasExpandedCategories
    serialized HasValidCounts=True HasExpandedCategories=True
    Invalid fuzz runner result: Frontend coverage has an unexpected property set.

**Impact**: Every genuinely passing exit-zero runner result is marked
validationPassed=false; the campaign becomes failed and throws, so the normal
campaign cannot succeed.

**Root cause**: Public computed API properties unintentionally became wire
schema fields while parser fixtures manually encode a narrower schema.

**Recommended fix**: Prefer `[JsonIgnore]` on both derived getters to preserve
the current 13-counter schema. If they are intentionally serialized, update the
schema version and verify their consistency instead of trusting them.

**Regression coverage**: Serialize an actual passing FuzzSummary with production
options and feed the bytes to the PowerShell authority; require acceptance and
an exact intended property set. Retain malformed/extra-field controls.

**Confidence**: High; production types/options emitted a passing result that the
production parser deterministically rejected.

### 549. [CONFIRMED] Definitely nonnull conditional property access remains unreplayable

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines
188-208 and HasReplayableAccessorEvaluation around lines 483-492;
SharpProof.Effects/ManagedAbstractFlow.cs, completion facts around lines
1842-1917; early Unknown in RequiresCallSiteAnalyzer.cs around lines 339-342.

**Description**: For `receiver?.Value`, the accessor candidate instance is an
IConditionalAccessInstanceOperation placeholder, not the source receiver.
CompletesNormally does not recognize that placeholder, so replay is disabled
before flow can prove the outer receiver nonnull. This occurs even for a fresh
object or an explicit preceding `receiver != null` guard.

**Reproduction**, all with zero compiler diagnostics and getter
`Contract.Requires(false)`:

    direct receiver.Value:       one get_Value SP0027
    unknown nullable receiver:   zero (fail-closed control)
    definitely null receiver:    zero (getter not executed)
    new Subject()?.Value:        zero (expected one)
    guarded subject?.Value:      zero (expected one)

**Impact**: Common guarded nullable code can hide an unconditional getter
precondition violation; changing only `.` to `?.` changes coverage after
nonnullness is already proven.

**Root cause**: Replay checks the conditional branch placeholder in isolation
instead of the outer receiver and pre-split flow state.

**Recommended fix**: Resolve the enclosing IConditionalAccessOperation and prove
its outer receiver completes and is definitely nonnull at conditional-access
entry. Only then canonicalize/mark the accessor as definitely executing. Keep
unknown, null, and non-completing receivers fail-closed; do not use WhenNotNull
state alone.

**Regression coverage**: Direct, fresh, and branch-refined receivers each report
exactly one; unknown/null receivers do not. Add a non-completing receiver and
conditional method-call follow-up only after its semantics are verified.

**Confidence**: High; two independent nonnull forms are missed while direct,
unknown, and null controls behave as predicted by the placeholder gate.

### 550. [CONFIRMED] SPMETA006 skips const string fields in SharpProof.Ir

**Location**: SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs,
AnalyzeField around lines 552-570.

**Description**: AnalyzeField returns immediately for every const field before
the independent SharpProof.Ir string-field prohibition. The const exemption is
appropriate for SPMETA002 mutable-state analysis but unintentionally suppresses
SPMETA006's typed-identity boundary.

**Reproduction**:

    const string in SharpProof.Ir:      SPMETA006 count=0
    static readonly string in Ir:      SPMETA006 count=1
    const string outside Ir:           SPMETA006 count=0

Compiler error counts were zero.

**Impact**: IR code can encode semantic identity/provenance as embedded raw
strings and evade the Error-level boundary merely by changing static readonly to
const.

**Root cause**: One global const early return conflates the mutable-state and
string-identity rules.

**Recommended fix**: Keep early return for enum members, but apply
`!field.IsConst` only to the SPMETA002 branch. Always execute SPMETA006
type/namespace analysis.

**Regression coverage**: Const and readonly strings in SharpProof.Ir each report
once; const string outside Ir stays clean; const scalar in mutable-state
namespaces remains exempt from SPMETA002; enum members remain clean.

**Confidence**: High; const is the only semantic difference between the missed
field and reporting control.

### 551. [CONFIRMED] Valid closed attributes on ContractFor companion members are silently discarded

**Location**: SharpProof.Contracts/ContractBinder.cs around lines 154 and
243-271; AnalyzerFeaturePipeline.cs around lines 207-210;
ContractForCompanionValidator.cs around lines 134-201.

**Description**: A matched companion parameter or return value can carry a
recognized and valid closed attribute such as Positive. Validation emits no
diagnostic, but binding produces no clause because only executable companion
clauses are imported.

**Reproduction**: A companion parameter Positive was recognized and valid,
analyzer diagnostics were empty, resolver/binder succeeded, and clause count was
zero. The same attribute on the target produced one ClosedAttribute clause; a
companion Contract.Requires produced one Companion clause.

**Impact**: A declaration that appears to strengthen the generated contract is
accepted but has no effect, allowing callers and implementation analysis to
silently disagree with author intent.

**Recommended fix**: Bind companion attributes with receiver-offset parameter
mapping. If that surface is unsupported, reject it with an enabled placement
diagnostic rather than accepting it. Add parameter and return Positive controls.

### 552. [CONFIRMED] JSON exponent overflow disables performance-contract thresholds

**Location**: SharpProof.Gates/Performance/AcceptancePerformanceContract.cs
around lines 23-49; PerformanceGate.cs around lines 63-72 and 1152-1174.

**Description**: JSON numbers are read with GetDouble and positive thresholds
are checked only with value > 0. The valid JSON number 1e400 becomes positive
infinity and is accepted.

**Reproduction**:

    finite control=accepted
    negative control=rejected
    1e400=Infinity, IsFinite=false, contract=accepted

**Impact**: Any affected smoke, median, p95, retained-memory, IDE, cancellation,
or forced-termination threshold becomes impossible for a finite observation to
exceed, so the corresponding gate can false-pass.

**Recommended fix**: Require double.IsFinite(value) and value > 0 for every
positive metric. Add NaN/infinity/exponent-overflow, zero, negative, and finite
boundary tests.

### 553. [CONFIRMED] Effect replay reserializes and hashes one syntax tree per event

**Location**: CompilerEffectClaimArtifactCodec.cs around lines 61-74;
CompilerManifestArtifact.cs around lines 955-999;
EffectCounterexampleReplayer.cs around lines 15-18 and 92-96;
CompilationFingerprint.cs around lines 32-43.

**Description**: Each replay event serializes the complete immutable syntax-tree
snapshot and computes SHA-256. Manifest validation and replay repeat the same
work after codec validation.

**Reproduction**: Codec validation of one valid 10,000-line tree with 128 events
allocated 269-300 MB and took 238-324 ms; the one-event control allocated about
6.3 MB. All events referenced the same snapshot and validation returned true.

**Impact**: Generated-code projects can spend hundreds of MB, and potentially
over a GB across Worker boundaries, revalidating one identity, causing avoidable
timeouts and memory pressure.

**Recommended fix**: Cache snapshot hashes by tree in the per-operation
ValidationContext and remove redundant geometry loops. Test 256 same-tree events
compute one hash, two trees compute two, and changed/hash-mismatch evidence fails.

### 554. [CONFIRMED] Fuzz evidence attributes dirty working-tree execution to clean HEAD

**Location**: scripts/Invoke-SharpProofFuzzCampaign.ps1 around lines 29-31 and
193-205; Invoke-SharpProofContainer.ps1 around lines 319-325.

**Description**: Campaign identity uses only git rev-parse HEAD. The supported
tooling path restores and builds live working files without requiring a clean
tree, then publishes the unchanged commit as exact source identity.

**Reproduction**: In an isolated repository a tracked source blob differed from
HEAD while campaign identity remained accepted and a passing schema-3 result
published the unchanged commit.

**Impact**: Passing evidence can conceal uncommitted fixes or regressions and is
not reproducible from its declared commit.

**Recommended fix**: Reject relevant staged, unstaged, and untracked inputs
before build/publication, or explicitly bind a dirty-source digest. Test clean,
tracked-dirty, SDK-included untracked source, and ignored-artifact controls.

### 555. [CONFIRMED] Exact Worker result-set validation sorts sets after uniqueness work

**Location**: SharpProof.Worker.Protocol/ProtocolJson.cs around lines 918-937;
callable and claim consumers around lines 573-601.

**Description**: ValidateResultSet materializes and uniqueness-checks identities,
then ValidateExactIds independently sorts both actual and expected streams for
set equality.

**Reproduction**: The isolated exact-membership stage allocated 1,072,512 bytes
for 25,000 IDs, 2,003,128 for 50,000, and 4,003,032 for 100,000: roughly 40
bytes per ID after inputs were already materialized.

**Impact**: Large valid responses pay O(N log N) comparison and multi-megabyte
temporary allocations at several validation boundaries.

**Recommended fix**: Combine uniqueness and exact membership into one ordinal
HashSet pass while preserving collection, identity, and set errors. Retain
reordered, duplicate, blank, missing, extra, and invalid-manifest tests plus a
warmed allocation bound.

### 556. [CONFIRMED] API-spec resolution ignores declared parameter and result types

**Location**: SharpProof.Effects/ApiSpecResolution.cs, MatchesTarget around line
193; downstream CompilerCallableLowerer.cs around lines 270 and 400-421.

**Description**: Member resolution checks kind, staticness, name, arity, and
parameter count, but not receiver, parameter, or result IrTypeKind.

**Reproduction**: A Math.Abs(int) spec declaring Boolean result resolved
complete with one spec and zero failures although Roslyn returned Int32.
Instantiation of the actual integer result then failed TypeMismatch.

**Impact**: Catalog typos evade cross-TFM resolution, expose incorrect effect
facets, and later degrade otherwise supported verification bodies to
UnsupportedBody.

**Recommended fix**: Classify Roslyn receiver, parameters, and return into the
shared IR type domain during resolution and emit a typed incompatible-shape
failure. Test wrong/missing/extra result and wrong parameter kinds across TFMs.

### 557. [CONFIRMED] Known-pure ref/out calls omit havoc for memory locations

**Location**: SharpProof.Frontend/RoslynProgramLowerer.cs around lines 310-321;
RoslynOperationLowerer.cs around lines 155-175.

**Description**: Ref/out mutation discovery retains only locals, parameters,
and captures. Array elements, fields, and ref-return locations disappear, and an
empty variable list is incorrectly treated as no mutation.

**Reproduction**: Change(ref value) emitted VariablesAndMemory havoc; the same
known-pure callee invoked as Change(ref values[0]) emitted a load and call but
zero havoc while lowering stayed Exact.

**Impact**: The exact program preserves stale memory across a byref call and can
support reasoning over an unmodeled mutation.

**Recommended fix**: Classify variable targets, memory locations, and discards
separately. Any writable non-variable location must force Memory havoc. Add
local, element, field, ref-return, and out-discard tests.

### 558. [CONFIRMED] Dynamic dispatch bypasses SPMETA001 Roslyn API enforcement

**Location**: SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs around
lines 42-50 and 107-150; BannedSymbols.txt includes the static API.

**Description**: The analyzer registers only OperationKind.Invocation. Casting
a statically known Compilation receiver to dynamic produces DynamicInvocation,
and RS0030 has no target symbol to ban.

**Reproduction**: Direct Compilation.RemoveAllSyntaxTrees produced one
SPMETA001. ((dynamic)compilation).RemoveAllSyntaxTrees produced zero diagnostics,
compiled, and reduced the runtime tree count from one to zero. A dynamic
lookalike remained clean.

**Impact**: An ordinary language construct bypasses the Error-level compiler API
boundary for cataloged compilation mutation.

**Recommended fix**: Analyze dynamic invocations and conservatively match
cataloged member names when the receiver originates from a protected Roslyn
type, including simple aliases. Test direct, dynamic cast, alias, and lookalike.

### 559. [CONFIRMED] Package-test shards can execute zero tests and report success

**Location**: scripts/Invoke-SharpProofPackageTests.ps1 around lines 295-313,
421-425, and 491-496.

**Description**: Fixture classes are hard-coded. VSTest exits zero when a filter
matches nothing, and the scheduler checks only process exit codes, not TRX
executed counts or discovered-test coverage.

**Reproduction**: A renamed failing fixture yielded No test matches the filter,
then Package tests passed. Directly targeting the renamed fixture failed 1/1. A
temporary TRX executed-count guard correctly rejected the empty shard.

**Impact**: Added or renamed package fixtures can be omitted entirely while
developer and qualification commands remain green.

**Recommended fix**: Discover the complete inventory, partition every identity
exactly once, and reconcile TRX execution. At minimum reject missing TRX and
zero-test default shards. Test rename/add/remove mutations.

### 560. [CONFIRMED] SkipCompilerExecution can republish stale Proven evidence

**Location**: SharpProof.Package/buildTransitive/SharpProof.targets around lines
47-50; SharpProof.Verifier.targets around lines 148, 232-234, and 320-323.

**Description**: With SkipCompilerExecution=true and ProvideCommandLineArgs=true,
Csc and the collector do not run, but SharpProofVerify still consumes the
previous stable compiler manifest after CoreCompile.

**Reproduction**: Identity first built Proven. Source changed to negation.
Compiler-suppressed Build exited zero, left the manifest unchanged, and
republished Proven. A normal Build changed the manifest, returned Refuted, and
failed with SP0051.

**Impact**: An ordinary Build can certify prior source state under require-proven.

**Recommended fix**: In compiler-suppression mode invalidate prior publication
but do not invoke the verifier; emit an explicit diagnostic when verification is
requested outside design-time. Add stale-proven and normal-regeneration tests.

### 561. [CONFIRMED] Conditional member initializers lose viable-arm effects

**Location**: SharpProof.Effects/EffectMethodNodeBuilder.cs around lines 134-162;
OperationEffectScanner.Expressions.cs around lines 548-598;
OperationEffectScanner.cs around lines 1080-1101.

**Description**: Member initializers are scanned outside the constructor CFG.
IConditionalOperation falls through to sequential child scanning, which stops
when the first arm cannot complete instead of joining alternatives.

**Reproduction**: Runtime selected Mutate in chooseFailure ? Fail() : Mutate(),
wrote static state 1729, and completed construction. Analysis returned Complete,
Writes.IsUnknown=false, but omitted the static write. A temporary independent-arm
join fixed the oracle and existing initializer tests.

**Impact**: Complete constructor/static-constructor summaries can omit writes,
calls, or allocations from a runtime-viable alternative.

**Recommended fix**: Scan the condition once; choose a constant arm when known,
otherwise scan arms independently and join. Add instance and static initializer
runtime oracles.

### 562. [CONFIRMED] SP0027 misses implicit synchronous Dispose calls

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines
33-77, 131-228, and 591-624; existing compensation is in
SharpProof.Effects/UsingDisposalEffectResolver.cs.

**Description**: Roslyn lowers synchronous disposal to IDisposable.Dispose and
loses the concrete implementation contract. Requires discovery has no source
using resolution although Effects already reconstructs the actual Dispose.

**Reproduction**: A sealed Resource.Dispose with Requires(false) produced one
SP0027 when explicit, but zero for using statement and using declaration.
Runtime executed Dispose once in both using forms; a null resource executed zero.

**Impact**: Concrete disposal preconditions are unenforced for common using
syntax.

**Recommended fix**: Share source-side disposal target resolution with potential
and actual call discovery, preserving null guards, exit-time flow, reverse order,
and uncertain-dispatch fail-closed behavior. Add no-duplicate and multi-resource
tests.

### 563. [CONFIRMED] Release-configuration validation ignores rulesets after page one

**Location**: scripts/Test-SharpProofReleaseConfiguration.ps1 around lines
24-31 and 175-179; fixture around lines 145-160 and 228.

**Description**: The rulesets endpoint is called once without pagination, then
the exact-one active tag rule is checked only over the default first page.

**Reproduction**: With 30 first-page rows and a second active tag ruleset on page
two, the script exited zero, wrote evidence, certified ruleset 7, and made one
non-paginated API call although full state contained two active tag rulesets.

**Impact**: Passing qualification evidence can certify an incomplete and
contradictory GitHub release configuration.

**Recommended fix**: Use gh api --paginate --slurp, flatten all pages, and check
the complete list. Extend fixtures with a page-two active-tag failure and a
branch-only page-two passing control.

### 564. [CONFIRMED] Definitely-null instance field reads are treated as completing

**Location**: SharpProof.Effects/ManagedAbstractFlow.cs around lines 2178-2184;
consumer SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs around lines
450-460.

**Description**: MayCompleteNormally groups IFieldReferenceOperation with
generic child completion and omits the null-receiver check already used for
properties and method references.

**Reproduction**: Positive(((Box)null!).Field, -1) emitted SP0027 although
runtime threw before the call and the call count was zero. Equivalent null
property access stayed quiet; live-field and direct controls executed and
reported.

**Impact**: Deterministic false refutations can fail warnings-as-errors builds
for unreachable calls.

**Recommended fix**: Split field references out and require a completing,
non-definitely-null instance for nonstatic fields. Preserve permissiveness for
unknown receivers and test argument-order positions.

### 565. [CONFIRMED] Request serialization replaces lone UTF-16 surrogates in paths

**Location**: Worker.Protocol/ProtocolModel.generated.cs around lines 816-821;
ProtocolJson.cs around lines 108-115; ProtocolJsonSupport.cs around lines
192-205.

**Description**: Request validation accepts any nonblank path. System.Text.Json
silently replaces an unmatched surrogate with U+FFFD before strict UTF-8 hashing.

**Reproduction**: Windows created and found a path containing code unit 55296.
Both original and round-tripped requests validated; round trip contained 65533,
paths differed, only the original existed, and request hashes were equal.

**Impact**: A valid request can be serialized into a different nonexistent path
and fail verification; malformed input can also collapse onto a legitimate
literal-U+FFFD path.

**Recommended fix**: Reject ill-formed UTF-16 in every request string before
serialization while preserving valid surrogate pairs and literal U+FFFD. Add
high/low-surrogate, non-BMP, and U+FFFD path tests.

### 566. [CONFIRMED] SPMETA003 rejects safe cancellation-excluding conjunctions

**Location**: SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs around
lines 169-209 and 229-265.

**Description**: A catch filter is accepted only when its entire top-level
operation is one is-pattern. A Boolean conjunction containing a proven
OperationCanceledException exclusion is therefore rejected.

**Reproduction**: exception is not OperationCanceledException && ShouldHandle
produced one Error-level SPMETA003; the bare exclusion produced zero; the unsafe
disjunction correctly produced one.

**Impact**: Safe ordinary selective handlers are build-blocked and require
awkward rewrites.

**Recommended fix**: Recursively prove exclusion: A && B excludes when either
operand does; A || B excludes only when both do. Preserve symbol-sensitive
patterns and add both operand orders plus disjunction controls.

### 567. [CONFIRMED] Contradictory entry proof is discarded at the query-budget boundary

**Location**: SharpProof.Worker/CallableEntryFeasibility.cs around lines 115-152;
CallableVerifier.cs around lines 91-102, 145-160, 223-237, and 254-276.

**Description**: Entry analysis can spend the final query proving preconditions
contradictory. CallableVerifier then requires another postcondition query and
returns Unknown(ResourceLimit), although contradiction already proves every
postcondition vacuously. Effects short-circuit correctly.

**Reproduction**: With method limit one, the backend was called once, entry was
Contradictory, and outcome became Unknown/ResourceLimit. Limit two made a
redundant second call and returned Proven/ContradictoryPreconditions.

**Impact**: A conclusive proof is downgraded, strict builds fail, and effect and
postcondition claims disagree for identical entry evidence.

**Recommended fix**: After claim alignment, immediately assemble all
postconditions as proven vacuity with the entry core and used assumptions. Test
one-call multiple Ensures and a feasible-entry resource-limit control.

### 568. [CONFIRMED] Conditional assignments write a flow-capture temporary instead of the lvalue

**Location**: SharpProof.Frontend/RoslynProgramLowerer.cs around lines 197-220;
RoslynOperationLowerer.cs around lines 155-175.

**Description**: Every flow capture is represented as a value temporary. When
Roslyn captures assignment-target storage across conditional evaluation, the
write targets that temporary rather than the original local/location.

**Reproduction**: For value = choose ? 1L : 2L; return value, lowering was Exact
with zero abstentions, yet execution returned 0 for both choose values.

**Impact**: Exact acyclic scalar IR and reusable relational summaries can encode
wrong results.

**Recommended fix**: Track storage captures separately from value snapshots and
resolve assignment-target capture references to the original variable or
once-evaluated location. Add local, member, and array target oracles.

### 569. [CONFIRMED] Proven-failing explicit casts are treated as completing

**Location**: SharpProof.Effects/ManagedAbstractFlow.cs around lines 2178-2184,
1910-1912, and 2583-2592; RequiresCallSiteAnalyzer.cs around lines 450-460.

**Description**: MayCompleteNormally treats conversions as completing children
and ignores a statically certain InvalidCastException.

**Reproduction**: Positive((string)new object(), -1) emitted SP0027, but runtime
threw before invocation and call count stayed zero. Null and valid reference
casts executed and reported as controls.

**Impact**: Unreachable calls receive deterministic false refutations.

**Recommended fix**: Give conversions a permissive but exception-aware
classifier: reject proven bad explicit casts/unboxes and checked overflows, keep
unknown runtime casts possible, and account for user-defined conversion calls.

### 570. [CONFIRMED] A completed lock statement suppresses later SP0027 diagnostics

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines
188-208 and 425-471; ManagedAbstractFlow.cs around lines 1842-1917;
lock-aware logic already exists in OperationCompletionEvaluator.cs around
992-997.

**Description**: Replay-prefix validation requires CompletesNormally for every
prior statement, but that strict switch has no ILockOperation arm.

**Reproduction**: Direct and empty-block controls had CanReplay=true and one
SP0027. After lock(new object()){} or a known nonnull local lock, flow was
Complete, runtime reached the call once, CanReplay=false, and diagnostics were
zero. Null lock stayed quiet.

**Impact**: A common lock-then-call shape loses all later concrete precondition
refutations.

**Recommended fix**: Share lock completion: completing acquisition, proven
nonnull receiver, and completing body. Keep null/unknown, throwing acquisition,
and abrupt body fail-closed. Add discovery/analyzer parity tests.

### 571. [CONFIRMED] Event handler expressions are skipped before null receiver failure

**Location**: SharpProof.Effects/OperationEffectScanner.Expressions.cs around
lines 139-195; correct ordering exists in ExceptionHandlerReachability.cs around
lines 313-328.

**Description**: Event assignment scans the receiver and performs its null check
before scanning HandlerValue. Runtime evaluates receiver expression, then
handler expression, then invokes the accessor.

**Reproduction**: For nullTarget.Changed += MakeHandler(), MakeHandler wrote
static state 1729 before the runtime null failure. Analysis returned Complete
and Writes.IsUnknown=false but omitted the static write. Reordering the scan
fixed the oracle and retained the unknown-receiver control.

**Impact**: Complete effect summaries can omit arbitrary handler-factory calls,
writes, allocations, and throws.

**Recommended fix**: Scan HandlerValue after receiver-expression evaluation but
before the null check; resolve/invoke the accessor afterward. Test add, remove,
throwing factory, and unknown receiver.

### 572. [CONFIRMED] Release digests mix committed Git blobs with dirty checkout bytes

**Location**: scripts/Get-SharpProofReleaseDigests.ps1 around lines 365-435;
Get-SharpProofProductionInventory.ps1 around lines 58-62 and 287-396.

**Description**: Canonical production/TCB digests use commit-tree blobs, while
productionSourceUniverseSha256 uses the live filesystem. Matching rev-parse HEAD
to the requested commit does not establish clean bytes.

**Reproduction**: An uncommitted source edit left declared commit, production
digest, and TCB digest unchanged, changed the source-universe digest, and was
accepted.

**Impact**: One release-digest record straddles two source states and is not
reproducible from its declared commit.

**Recommended fix**: Evaluate inventory in a detached worktree at the requested
commit, or reject staged, unstaged, and relevant untracked inputs. Test clean,
dirty, staged, untracked Compile, and newly committed controls.

### 573. [CONFIRMED] Differential oracle throws on wrong-typed variable bindings

**Location**: SharpProof.Testing/IrCSharpDifferentialOracle.cs around lines
38-41, 81-91, and 115-123; IrInterpreter.cs around lines 174-187.

**Description**: The oracle checks binding-key presence but not IrValue type.
Reflection then throws ArgumentException when a Boolean is passed to a generated
long parameter, while the interpreter returns Unsupported(InvalidVariableValue).

**Reproduction**: Missing binding returned Abstained; the same program with a
wrong-typed binding made the interpreter return Unsupported but the oracle
escaped Object of type Boolean cannot be converted to Int64.

**Impact**: One malformed generated environment aborts the differential run and
prevents structured evidence for later cases.

**Recommended fix**: Validate nonnull binding values and exact declared types
before CLR conversion, returning a typed Abstained detail. Retain a defensive
reflection-shape catch. Add wrong type, null, missing, and correct controls.

### 574. [CONFIRMED] Explicit-interface implementation preconditions are unreachable from calls

**Location**: SharpProof.Contracts/ContractBinder.cs around lines 74-82 and
243-254; RequiresCallSiteDiscovery.cs around lines 59-63;
RequiresCallSiteAnalyzer.cs around lines 302-324.

**Description**: A closed precondition on an explicit implementation binds to
that implementation, but every source invocation targets the interface symbol.
No contract projection or placement diagnostic joins them.

**Reproduction**: Positive on the explicit implementation parameter was valid
and bound one clause there; interface/call-target binding had zero clauses and a
literal bad call emitted nothing. Moving Positive to the interface emitted
SP0027.

**Impact**: Implementation analysis can assume a condition that no source caller
can be checked against.

**Recommended fix**: Reject implementation-only strengthening unless all
implemented interface members declare an equivalent contract, and import
interface preconditions into implementation analysis. Test multiple-interface
agreement/conflict.

### 575. [CONFIRMED] Advisory performance validation accepts an effective off profile

**Location**: SharpProof.Gates/Performance/PerformanceGate.cs around lines 101,
273, 1218-1259, and 1389-1402; package targets around lines 11-20.

**Description**: ValidateAdvisoryPackagePolicy checks that expected XML nodes and
literal conditions exist, but not assignment uniqueness/order or evaluated
MSBuild properties/items.

**Reproduction**: Adding an unconditional SharpProofProfile=off after the
expected default left the validator accepted. MSBuild evaluated baseline as
advisory with analyzers and mutant as off with an empty Analyzer list.

**Impact**: The performance gate can time two analyzer-free builds, measure
deceptively low overhead, and publish passing advisory evidence.

**Recommended fix**: Evaluate a minimal imported project and assert effective
profile/normalized profile plus exact analyzer roles before timing. Also reject
override assignments. Add the off-override mutation test.

### 576. [CONFIRMED] Mapped-location collisions prevent compiler-manifest emission

**Location**: CompilerCollector/ClaimManifestBuilder.cs around lines 670-688;
CompilerManifestArtifactProducer.cs around lines 184-253;
CompilerArtifact/CompilerSourceLocationAuthority.cs around lines 139-170 and
214-249; FinalCompilationCollector.cs around lines 42-50.

**Description**: Producer discovery drops Roslyn Location.SourceTree and later
reconstructs authority from mapped geometry. Two physical trees with identical
#line path, line/column, and physical offset are declared ambiguous.

**Reproduction**: Two clean selected methods mapped to shared.g.cs:102:5 at
offset 83 caused InvalidDataException, then SP0049/no manifest. Changing only the
second virtual path yielded four authorities.

**Impact**: Valid generated/Razor-style compilations can fail verification
solely due to ordinary virtual-coordinate collision.

**Recommended fix**: Carry producer-known SourceTree/ordinal sidecars for
callables, claims, and diagnostics and geometry-check that exact tree. Retain
unique rediscovery only when identity is unavailable. Add collision round-trip
and tamper tests.

### 577. [CONFIRMED] Contract intrinsics in constructor initializers bypass validation

**Location**: SharpProof.Contracts/ContractClauseInventoryBuilder.cs around
lines 250-315; ContractIntrinsicValidator.cs around lines 12-45;
ContractBinder.cs around lines 166-240; AnalyzerSession.cs around lines 204-210.

**Description**: GetBody returns only a constructor block/expression body and
drops ConstructorDeclarationSyntax.Initializer, although base/this arguments are
executable.

**Reproduction**: base(Contract.Result<int>()) compiled with zero errors,
produced no initializer SP0024 or intrinsic violation, bound successfully with
zero clauses, emitted, then construction threw InvalidOperationException. A
body misuse control produced SP0024 and binder failure.

**Impact**: Compiler-valid Result/Old misuse is accepted as an empty contract and
crashes every affected construction before the body.

**Recommended fix**: Keep body-only prologue placement, but validate constructor
initializer operations as additional executable roots. Test base(Result),
this(Old), valid body prologue, and no duplicate diagnostics.

### 578. [CONFIRMED] A completed try statement suppresses later SP0027 diagnostics

**Location**: SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs around lines
188-208 and 425-471; SharpProof.Effects/ManagedAbstractFlow.cs around lines
1842-1917; OperationCompletionEvaluator.cs already recognizes ITryOperation.

**Description**: Strict replay-prefix completion has no ITryOperation arm, so
any preceding try makes a later direct call unreplayable even when flow, body,
and finally are all complete.

**Reproduction**: Direct and empty-block controls emitted SP0027. Empty
try/finally, empty try/catch, and a completing try/finally all reached the target
at runtime with Complete flow but CanReplay=false and zero diagnostics. Abrupt
try did not reach the call and stayed quiet.

**Impact**: Moving a direct call after an admitted try/catch/finally statement
silences concrete precondition refutations.

**Recommended fix**: Add a conservative structural try case: definitely
completing/no-throw body and completing optional finally. Keep recovered throws,
filters, abrupt body/finally, and unknown calls fail-closed until shared
exception semantics are available.

### 579. [CONFIRMED] ICancelableTask exempts every helper from SPMETA003

**Location**: SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs around
lines 317-339.

**Description**: The cancellation-translation exemption checks only whether a
method's containing type implements ICancelableTask. It does not restrict the
exemption to the actual ITask.Execute protocol boundary.

**Reproduction**: A real Execute translation emitted zero diagnostics, as
intended. A private static helper in the same task type swallowed
OperationCanceledException and also emitted zero, while the identical helper on
an unrelated type emitted one Error-level SPMETA003.

**Impact**: Private, static, or asynchronous helpers in shipped MSBuild tasks can
turn cancellation into ordinary success without the soundness boundary noticing.

**Recommended fix**: Exempt only the exact instance bool Execute()
implementation/override of Microsoft.Build.Framework.ITask.Execute. Test Execute,
static/instance helpers, Cancel, unrelated Execute, and immediate bare rethrow.

### 580. [CONFIRMED] Counterexample replay discards DAG memoization between assumptions

**Location**: SharpProof.Verify/ProofKernel.cs around lines 79-91;
SharpProof.Ir/IrInterpreter.cs around lines 107-170 and 540-547.

**Description**: ProofKernel reuses one interpreter object but calls Evaluate for
each assumption and the goal. Every call creates a fresh EvaluationState and
dictionary, so shared sub-DAGs are re-walked per root.

**Reproduction**: One 8,191-node shared Boolean DAG with one assumption allocated
1,269,824 bytes in 3 ms. Sixty-four distinct outer assumptions allocated
81,181,456 bytes in 140 ms, a 63.9x ratio. Both and the real backend returned a
correct Refuted outcome.

**Impact**: Valid refutations can incur O(assumptions * shared DAG) managed work
after the solver answers, outside Z3 rlimit accounting, causing avoidable memory
pressure and timeout/cancellation.

**Recommended fix**: Add a model-bound batch/session evaluator whose memo is
shared across replay roots, while preserving per-root depth and cancellation and
never reusing across environments. Add node-count, changed-model, and cached-root
cancellation tests.

### 581. [CONFIRMED] A global SARIF path loses earlier projects' evidence

**Location**: SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets
around lines 64-66 and 128-130; Worker.Launcher/Program.cs around lines 632-670;
SarifProjection.cs around lines 74-102.

**Description**: Configured SARIF is projected only by target framework, not by
project identity. Command-line properties propagate through ProjectReference,
so every project publishes its one-run document to the same destination and the
last cooperative writer replaces the earlier one.

**Reproduction**: App -> Lib with distinct SARIF paths produced two documents;
Lib contained SP0047 and each root matched its project. One shared absolute path
made the successful build leave a single App document with no Lib subject or
SP0047, although both projects reported using the path.

**Impact**: Multi-project advisory builds silently lose warnings, informational
evidence, and proven claims from all but the last project.

**Recommended fix**: Add a stable project-full-path hash plus TFM to configured
projection, reject cross-project reuse with SP0054, or implement locked
multi-run aggregation. Test App/Lib shared-path behavior and retain same-project
cooperative publication.

### 582. [CONFIRMED] Property-assignment arguments ignore non-returning setters

**Location**: SharpProof.Effects/ManagedAbstractFlow.cs around lines 2178-2184
and 1872-1886; consumer RequiresCallSiteAnalyzer.cs around lines 450-460.

**Description**: MayCompleteNormally handles ISimpleAssignmentOperation through
generic child traversal. For a property target that observes the read/getter
shape and never checks the setter actually invoked by assignment.

**Reproduction**: Positive(new ThrowingTarget().Value = 1, -1) emitted SP0027,
but the setter threw before invocation and runtime call count was zero. A
completing setter and direct call executed and correctly emitted SP0027.

**Impact**: Deterministic false refutations and warnings-as-errors failures are
reported for unreachable calls.

**Recommended fix**: Classify assignment write targets explicitly. Property and
indexer assignments must check receiver/index/RHS completion, nullness, static
initialization, and setter normal exit without consulting the getter. Preserve
permissiveness for unknown metadata setters; treat compound assignment
separately because it invokes both accessors. Add setter/getter, set-only,
static, indexer, null-receiver, and argument-position controls.

### 583. [CONFIRMED] Callable projection validation is quadratic

**Location**: Worker.Protocol/ProtocolJson.cs around lines 751-773;
WorkerResultAssembler.cs around lines 171-187.

**Description**: ValidateRun loops every callable result, and each
MatchesCallableProjection call linearly scans manifest.Callables from the start
with FirstOrDefault despite already building claim indexes.

**Reproduction**: Valid empty-claim responses took 18.9 ms at 1,000 callables,
123 ms at 4,000, 506 ms at 8,000, and 1,487 ms at 16,000. The 16,000-row
response validated and was only 4.87 MB.

**Impact**: Large ordinary projects spend seconds in repeated validation at
worker, launcher, and publication boundaries after verification is complete.

**Recommended fix**: Build one ordinal CallableId index beside claimsByCallable
and pass resolved declarations into projection validation. Preserve malformed
duplicate/null behavior and add deterministic lookup-count or warmed scaling
coverage.

### 584. [CONFIRMED] SPMETA002 whitelists mutable immutable-collection builders

**Location**: SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs around
lines 608-640.

**Description**: IsMutableReferenceStorage returns false for every type in
System.Collections.Immutable before checking mutable collection interfaces.
Nested Builder types therefore bypass the Error-level state-isolation rule.

**Reproduction**: Static readonly ImmutableArray<int>.Builder emitted zero
SPMETA002 and runtime Add changed Count from zero to one. A readonly List<int>
control emitted one diagnostic; an ImmutableArray value emitted zero.

**Impact**: Analyzer/frontend/verifier code can retain mutable process-global
builder state across compilations without detection.

**Recommended fix**: Recognize exact nested Builder types before the immutable
namespace exemption, preserving real immutable values. Test Array/List/Dictionary/
HashSet and sorted builders plus immutable and ordinary mutable controls.

### 585. [CONFIRMED] WellSortedIrGenerator maximumDepth forces exponential full-depth trees

**Location**: SharpProof.Testing/WellSortedIrGenerator.cs around lines 55-79,
124-147, and 150-179.

**Description**: Positive depth has no leaf production. Integer and Boolean
generators always recurse with expected two children, so maximumDepth behaves as
required full depth, with no node budget or upper bound.

**Reproduction**: Fixed-seed generation retained about 81.8 MB at depth 20 in
1.07 s and 945.4 MB at depth 24 in 13.3 s for one case.

**Impact**: Raising the public testing depth can stall or exhaust the test
process before any differential oracle runs.

**Recommended fix**: Add a bounded node budget and allow leaves at every depth,
using maximumDepth only as a ceiling; optionally enforce a documented hard cap.
Test depth/size bounds, fixed-seed determinism, and budget exhaustion.

### 586. [CONFIRMED] An all-break switch suppresses later SP0027 diagnostics

**Location**: RequiresCallSiteDiscovery.cs around lines 188-208 and 425-471;
ManagedAbstractFlow.cs around lines 1842-1917.

**Description**: Strict replay-prefix completion recognizes neither
ISwitchOperation nor a break that normally exits that switch.

**Reproduction**: Direct and empty-block controls had CanReplay=true and one
SP0027. A constant/default switch whose every branch broke had Complete flow,
runtime reached the target once, CanReplay=false, and zero diagnostics.
All-abrupt and safe controls stayed quiet.

**Impact**: A documented ordinary switch before a contracted call erases a
definite violation even when every selector continues.

**Recommended fix**: Add conservative constant/default switch completion and
treat a break owned by that exact switch as normal exit. Reject patterns, guards,
gotos, nested breaks, and abrupt sections. Add no-default and no-duplicate tests.

### 587. [CONFIRMED] Literal-true postconditions consume a redundant query reservation

**Location**: CallableEntryFeasibility.cs around lines 110-140;
CallableVerifier.cs around lines 186-237; IrSemanticTerms.cs around lines 44-62.

**Description**: A feasible entry query can use the final method reservation.
Even when the folded postcondition obligation is literal true, CallableVerifier
requires another reservation and returns Unknown(ResourceLimit).

**Reproduction**: With Requires(value > 0), Ensures(true), and method limit one,
entry was Feasible and outcome Unknown/ResourceLimit. Raising only the method
limit to two produced Proven and consumed additional SMT resources.

**Impact**: A logically unconditional claim is downgraded and strict builds can
fail; with capacity, an unnecessary solver call is charged.

**Recommended fix**: Discharge literal true before TryStartQuery as Proven with
an empty nonvacuous core. Add one-call tight-budget, nonliteral control, and
backend-test fixture adjustments.

### 588. [CONFIRMED] Disabling analyzers republishes stale Proven evidence

**Location**: CompilerCollector/FinalCompilationCollectorAnalyzer.cs around
lines 3-31; SharpProof.Package/buildTransitive/SharpProof.targets around lines
47-50; SharpProof.Verifier.targets around lines 232-234 and 320-323.

**Description**: The collector is a DiagnosticAnalyzer. When the SDK sets
_SkipAnalyzers, Csc emits the changed assembly but leaves the prior stable
manifest, and verification consumes it.

**Reproduction**: Identity first built Proven; source changed to negation.
RunAnalyzersDuringBuild=false exited zero, kept manifest unchanged, changed the
assembly, and republished Proven. A normal build regenerated the manifest,
returned Refuted, and failed SP0051.

**Impact**: A strict build can certify a previous source while shipping the
changed refuted assembly.

**Recommended fix**: When verification is enabled, force analyzer execution
before _ComputeSkipAnalyzers or fail explicitly with SP0054; do not merely skip
verification. Test RunAnalyzersDuringBuild, RunAnalyzers, implicit skip, and
normal controls.

### 589. [CONFIRMED] ConstrainSuccessfulEvaluation bypasses sort and ownership checks

**Location**: SharpProof.Ir/IrSemanticTerms.cs around lines 21-32.

**Description**: When evaluated is atomic and needs no definedness witness, the
fast path returns predicate directly without validating Boolean sort, factory
ownership, or evaluated ownership.

**Reproduction**: A foreign Boolean predicate returned the same instance, an
Integer predicate was accepted, and a later consumer rejected the foreign term.
The composite evaluated control rejected immediately because factory.Binary
incidentally performed validation.

**Impact**: A public canonical-term helper can leak wrong-sort or foreign-factory
terms and move failure to a distant interpreter/query boundary.

**Recommended fix**: Validate predicate ownership and Boolean type plus evaluated
ownership before the fast path. Test integer, foreign predicate/evaluated,
same-factory atomic identity, and composite behavior.

### 590. [CONFIRMED] Zero-claim unsupported callables are rewritten as MalformedResult

**Location**: Worker.Protocol/WorkerResultAssembler.cs around lines 195-202;
SharpProof.Worker/SharpProofWorker.cs around lines 350-367;
ClaimManifestBuilder.cs around lines 61-64 and 146-147.

**Description**: A precondition-only callable can legitimately fail preparation
as UnsupportedBody and produce Incomplete/SemanticUnknown with zero claim rows.
Projection hard-codes every zero-claim complete-run callable to Complete/None.

**Reproduction**: Manifest and preparation were valid, callable was
Incomplete/SemanticUnknown with zero claims, but response validation emitted
response.callable_projection; Worker then replaced it with
Failed/MalformedResult.

**Impact**: Ordinary typed semantic incompleteness is misreported as malformed
worker output and the true UnsupportedBody reason is lost.

**Recommended fix**: Preserve verifiable callable-level authority for zero-claim
entries or admit the finite claimless incomplete reasons against compiler
preparation authority. Add end-to-end Requires-only unsupported and supported
controls.

### 591. [CONFIRMED] Void return closed attributes are selected then discarded

**Location**: ContractSelectionInventory.cs around lines 141-146;
ClosedContractDiagnostics.cs around lines 18-26; ContractBinder.cs around lines
260-270.

**Description**: Return attributes select contracts, but analyzer and binder
skip all return validation when ReturnsVoid and construct zero clauses.

**Reproduction**: Return-target NotNull, Positive, and InRange on void compiled
cleanly, were retained by Roslyn and selected, emitted no SP0024, and bound
successfully with zero clauses. Invalid and valid nonvoid controls behaved
correctly.

**Impact**: Malformed selected declarations are silently accepted and mislead
authors into believing a contract is active.

**Recommended fix**: Always validate return attributes against ReturnType,
including System.Void; gate only clause construction on a result term. Test all
closed attributes on void plus no-attribute and nonvoid controls.

### 592. [CONFIRMED] Targeted package tests depend on default Worker shard inventory

**Location**: scripts/Invoke-SharpProofPackageTests.ps1 around lines 220-246 and
281-293.

**Description**: The script always performs WorkerMsBuildIntegrationTests
list-tests discovery and enforces at least 40 methods before checking whether an
explicit TestFilter requested a single selected shard.

**Reproduction**: A project with one passing requested LauncherArgument test and
no Worker fixture passed direct dotnet test, but the wrapper failed because
Worker discovery returned zero. Moving discovery into default mode made the
same wrapper pass one test.

**Impact**: Healthy targeted diagnosis is blocked by unrelated Worker fixture
renames/topology and always pays irrelevant discovery overhead.

**Recommended fix**: Execute Worker inventory/bucketing only for empty-filter
default mode; selected mode should enqueue its filter directly. Test zero-Worker
selected success and default-mode floor failure.

### 593. [CONFIRMED] Invalid closed attributes in referenced metadata are silent

**Location**: ContractBinder.cs around lines 249-301; AnalyzerSession.cs around
lines 154-185; RequiresCallSiteAnalyzer.cs around lines 314-326;
AnalyzerFeaturePipeline.cs around lines 345-356.

**Description**: A genuine metadata attribute that is recognized but
semantically invalid makes binding fail. Only identity-rejected metadata gets an
unconditional call-site diagnostic; other failures become Unknown and are
reported only when the caller is independently selected.

**Reproduction**: External Read([Positive] string) was recognized invalid and
bound InvalidClosedAttribute, yet an unannotated consumer call emitted nothing.
Positive int emitted SP0027; selecting the caller produced only vague SP0047.

**Impact**: Dependencies built without the analyzer can ship malformed
preconditions that consumers neither enforce nor diagnose.

**Recommended fix**: Validate genuine metadata attributes at call targets and
emit SP0024 with type/reason, or an unconditional typed SP0047. Retain identity
rejection, valid external, no-attribute, and selected-caller controls.

### 594. [CONFIRMED] SMT depth prevalidation re-walks shared DAGs per assumption

**Location**: SharpProof.Smt/IrSmtBackend.cs around lines 297-301 and 346-364.

**Description**: Query construction calls ValidateDepth separately for every
assumption and goal, and each call creates a fresh depth memo although actual
encoding shares its cache.

**Reproduction**: Sixty-four roots shared one 8,191-node DAG. Direct validation
allocation grew from 746,600 to 47,677,064 bytes, a 63.9x ratio; public backend
latency grew from 58 to 193 ms. Both results were valid Unsatisfiable.

**Impact**: O(shared nodes + assumptions) query data causes O(assumptions *
shared nodes) work before Z3.Check and outside solver accounting.

**Recommended fix**: Share the maximum-depth memo across roots while resetting
root depth and revisiting a node reached later at greater depth. Add linear
scaling, depth-256 ordering, and cancellation tests.

### 595. [CONFIRMED] Launcher snapshots the Worker closure twice before startup

**Location**: Worker.Launcher/Program.cs around lines 21-23, 57-58, and 115-119;
CompilerManifestArtifact.cs around lines 49-124 and 178-187.

**Description**: Outer CreateSnapshot stages the closure, then its prelaunch hash
delegate calls ComputeSha256, which creates another full snapshot. Each snapshot
also materializes multiple full component arrays.

**Reproduction**: A normal 16-component 2.09 MB closure allocated 15.16 MB, read
16.75 MB, and took 122 ms before launch. A one-snapshot control halved both
ratios; hashes matched and cleanup completed.

**Impact**: Every project pays roughly 7.25x allocation and 8x reads before
Worker/Z3; supported large closures multiply this into hundreds of MB.

**Recommended fix**: Hash the retained first snapshot directly; stream copy,
comparison, and canonical hashing with pooled buffers. Test one snapshot,
identical bytes/hash, cleanup, bounded I/O/allocation, and mutation/size limits.

### 596. [CONFIRMED] Non-completing object initializers lose reached effects

**Location**: EffectMethodNodeBuilder.cs around lines 138-158;
OperationEffectScanner.cs around lines 838-879;
OperationCompletionEvaluator.cs around lines 889-902.

**Description**: Direct member-initializer scanning asks whether the whole object
creation, including initializer, completes while modeling only constructor
completion. If an initializer setter/RHS fails, it skips the initializer
entirely, including reached prefix effects.

**Reproduction**: new Value { Property = Mark() } ran Mark, wrote static 1729,
then setter threw InvalidOperationException. Analysis returned Complete and
nonunknown but omitted both static write and throw. A constructor-prefix
completion fix passed the oracle and three nearby tests.

**Impact**: Constructor summaries and callers can be falsely pure/nonthrowing.

**Recommended fix**: Separate constructor-invocation completion from whole
construction, then scan initializer sequencing independently. Add one- and
two-member throwing initializer runtime oracles plus a completing control.

### 597. [CONFIRMED] Canonical container dotnet commands have no internal deadline

**Location**: scripts/Invoke-SharpProofContainer.ps1 around lines 1-40 and
ordinary test/build/gate call sites; bounded wrapper exists in
Invoke-SharpProofDotnet.ps1 around lines 21-59.

**Description**: Invoke-DotNet calls raw dotnet with no timeout, tree kill, or
exit-124 attribution, bypassing the repository's existing bounded runner.

**Reproduction**: A benign fake dotnet slept for 30 seconds. Baseline required an
external two-second watchdog. Routing through the existing wrapper with a
one-second bound failed attributably in 1.61 s and left no child alive.

**Impact**: Hung restore, build, analyzer, testhost, gate, pack, or fuzz setup can
block developer/CI tasks indefinitely until external cancellation.

**Recommended fix**: Route every dispatcher dotnet invocation through the
bounded wrapper using contract-owned build/test deadlines and an optional
validated override. Test timeout attribution, child cleanup, short-circuit, and
fast restore+test.

### 598. [CONFIRMED] Legal 513-character source call identity causes fatal SP0049

**Location**: CompilerRelationalSummaryProvider.cs around lines 136-149,
278-286, and 340-354; CompilerManifestArtifactProducer.cs around lines 69-119;
CompilationFingerprint.cs around lines 173-180 and 230-254.

**Description**: Summary authority accepts unbounded Roslyn documentation IDs,
but final compilation evidence rejects identities longer than 512 characters
and the collector converts the exception to error SP0049.

**Reproduction**: A helper name yielding identity length 512 lowered and emitted
successfully. One extra character produced compiler errors zero, lowering
success and one summary authority, then JsonException and no manifest.

**Impact**: Valid source fails verification rather than yielding typed
UnsupportedBody.

**Recommended fix**: Apply one shared identity validator before summary caching
and abstain at 513, or consistently raise/remove the schema limit. Do not
truncate/hash authority identity. Test 512, 513, nested dependency, and no-SP0049.

### 599. [CONFIRMED] Exact-commit release tasks compile Git-ignored source files

**Location**: .gitignore entry for Generated Files; eng/container/entrypoint.sh
around lines 90-130 and 137-187.

**Description**: Clean-source validation uses git ls-files --others
--exclude-standard, hiding ignored paths. The later live-worktree tar overlay
does not honor Git ignores and copies them into the detached HEAD task clone.

**Reproduction**: An ignored Generated Files/IgnoredCompileBreak.cs was absent
from ordinary status, passed the clean gate, then compiled inside canonical pack
with RepositoryCommit fixed to HEAD and failed on its #error.

**Impact**: Exact-commit release commands can fail or produce different package
assemblies from ignored local leftovers while provenance still names HEAD.

**Recommended fix**: Execute clean-required commands directly from detached HEAD
and copy only explicitly authorized external inputs, or reject every overlaid
path outside that allowlist. Test ignored Compile source, allowed nupkgs, and
excluded artifacts/bin/obj.

### 600. [CONFIRMED] Expanded params arrays are discarded before Requires evaluation

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 642-669 (`GetArgument`).

**Description**: Roslyn represents an expanded `params` call as one implicit
`IArrayCreationOperation`, but `GetArgument` returns null for
`ArgumentKind.ParamArray`. Contracts over the array therefore become Unknown
even when zero or several expanded elements create a definitely non-null array.

**Reproduction**: Temp analyzer/runtime controls compared `MustBeNull()` and
`MustBeNull(1, 2)` against explicit null and explicit non-null arrays. Expanded
calls ran with non-null arrays but emitted no definite violation; explicit
controls emitted SP0027.

**Impact**: Definite call-site contract violations disappear solely because C#
uses expanded `params` syntax.

**Recommended fix**: Admit the implicit params aggregate, snapshot its array
value, and preserve element evaluation/completion. Test zero/two elements,
throwing elements, explicit null, and named/optional controls.

### 601. [CONFIRMED] Direct-break loops suppress diagnostics on later reachable calls

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 188-208 and 425-471; `SharpProof.Effects/ManagedAbstractFlow.cs`, around
lines 1842-1917.

**Description**: Strict prefix completion has no loop/branch model. A direct
`break` leaves managed flow Complete but makes the following candidate
unreplayable.

**Reproduction**: `for (;;) { break; }` and `while (selector) { break; }`
followed by a known-invalid direct call both reached the call at runtime and
emitted zero SP0027. Direct and empty-block controls emitted one; a throwing
condition correctly did not reach the call.

**Impact**: Harmless finite loop syntax erases definite precondition failures.

**Recommended fix**: Conservatively model direct-break `for` and top-tested
`while` completion, retaining rejection for nested/conditional/goto/do shapes.

### 602. [CONFIRMED] Supported claimless callables unnecessarily require an SMT backend

**Location**: `SharpProof.Worker/SharpProofWorker.cs`, around lines 251-256 and
504-536; `SharpProof.Worker/CallableVerifier.cs`, around lines 34-52.

**Description**: Lane creation is based on target count, even though a supported
Requires-only target has zero claims and `CallableVerifier` completes it without
using a backend.

**Reproduction**: The policy returned Complete/None with zero claims/resources,
but one such target still invoked a throwing backend factory and failed. A
zero-target control did not invoke it.

**Impact**: Solver-free valid projects can fail as BackendUnavailable and pay
backend startup costs.

**Recommended fix**: Prepopulate solver-free callable results and create lanes
only for targets with solver work. Coordinate with #590 so unsupported
claimless callables remain typed incomplete.

### 603. [CONFIRMED] Failure-response claim projection is quadratic

**Location**: `SharpProof.Worker.Protocol/WorkerResultAssembler.cs`, around
lines 59-71 (`CreateIncomplete`).

**Description**: Every manifest claim performs `Callables.FirstOrDefault` to
find its owner, making failure/cancellation projection O(callables * claims).

**Reproduction**: Valid 1k/2k/4k/8k fixtures measured 8.9/35.5/143.9/563.2 ms;
an ordinal dictionary control stayed below 0.1 ms. The full 8k response was
valid and 6.56 MB.

**Impact**: Recovery after timeout/cancellation can consume remaining launcher
grace on large ordinary manifests.

**Recommended fix**: Build one first-match ordinal callable-ID dictionary,
preserving duplicate-first and missing-owner behavior. Test exact assumption
propagation and near-linear scaling.

### 604. [CONFIRMED] Non-completing deconstruction conversion phases lose reached effects

**Location**: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, around
lines 58-75; `OperationCompletionEvaluator.cs`, around lines 768-834;
`EffectSummaryOperations.cs`, lines 117-119.

**Description**: `ScanDeconstruction` scans only the RHS/root Deconstruct call.
When completion says a nested call, conversion, or target write cannot complete,
the scanner substitutes Complete `MayDiverge` and skips the reached phase.

**Reproduction**: A conversion wrote static state 1729 then threw
`InvalidOperationException`. Runtime observed both. The summary was Complete and
nonunknown but had no static write, no throw, and `MayDiverge`. A phase-ordered
temp fix passed the oracle and an existing deconstruction-effects control.

**Impact**: Purity, exception, handler-reachability, and termination consumers
can receive false results.

**Recommended fix**: Share a language-ordered deconstruction phase traversal
between completion and effects: RHS, root/nested calls, conversions, then target
writes; record each reached phase before stopping.

### 605. [CONFIRMED] Rejected contract API usage is silently accepted in companion bodies

**Location**: `SharpProof.Contracts/ContractClauseInventoryBuilder.cs`, around
lines 65-73; `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs`,
around lines 152-195; `EffectiveContractSourceResolver.cs`, around lines 109-122.

**Description**: Companion inventories set `HasRejectedContractApiUsage`, but
companion validation/resolution/binding never consume it. Ordinary operation
analysis cannot compensate because companion operation blocks are skipped.

**Reproduction**: An aliased fake exact-metadata-name `Contract.Requires` set
`companion-rejected=True`, yet emitted no diagnostic and bound successfully with
zero clauses. A direct fake control emitted SP0047; a genuine companion imported
one clause.

**Impact**: A companion can appear to contain contracts while rejected clauses
are silently dropped; mixed bodies can bind only a subset.

**Recommended fix**: Report rejected occurrences at their calls and reject the
entire selected companion inventory. Test fake, mixed, genuine, direct, and
unrelated-lookalike controls.

### 606. [CONFIRMED] Null and out-of-range array arguments cause false SP0027 reports

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 450-460; `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
2178-2184; `OperationCompletionEvaluator.cs`, around lines 721-725.

**Description**: Array element completion checks only child evaluation, not a
definitely null receiver or proven bounds failure. Analysis then evaluates a
later independent argument and reports a violation for a call never invoked.

**Reproduction**: `Positive(((int[])null!)[0], -1)` and
`Positive((new int[0])[0], -1)` each emitted SP0027, while runtime call count was
zero and the first argument threw. Live-array/direct controls executed and were
correctly diagnosed.

**Impact**: Deterministic false diagnostics and warnings-as-errors failures.

**Recommended fix**: Split array-element completion from generic child
completion, reject definitely null and definitely out-of-range accesses, and
share the rule between the two completion engines. Preserve permissive Unknown.

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

### 609. [CONFIRMED] Harmless array creation suppresses later SP0027 diagnostics

**Location**: `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
1842-1917 and 2501-2507; `RequiresCallSiteDiscovery.cs`, around lines 425-471.

**Description**: Strict prefix completion lacks an `IArrayCreationOperation`
arm even though the same class has a conservative direct-array predicate.

**Reproduction**: Fixed-size, literal-initialized, and rectangular arrays all
had Complete flow and `IsDirectArrayCreationComplete=True`, runtime reached the
invalid call, but `CanReplay=False` and SP0027=0. Negative-length and throwing
element controls did not reach it.

**Impact**: An irrelevant safe allocation erases later definite violations.

**Recommended fix**: Route `IArrayCreationOperation` through
`IsDirectArrayCreationComplete`. Test safe forms and negative/nonconstant/
throwing controls.

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

### 614. [CONFIRMED] Rejected API identity detection omits Contract.Result and Contract.Old

**Location**: `SharpProof.Frontend/ContractApiIdentityResolver.cs`, around lines
117-131; `ContractApiMetadata.generated.cs`, around lines 66-70.

**Description**: The rejected-name classifier includes Requires/Ensures/Assume
but not Old/Result. Non-authoritative exact-name intrinsics therefore fall
through both rejection and genuine intrinsic validation.

**Reproduction**: Aliased fake Result/Old calls set no rejection flag, emitted
no diagnostic, and bound successfully with zero clauses. Genuine misplaced
Result/Old emitted SP0024; fake Requires emitted SP0047.

**Impact**: Exact-name ghost intrinsics silently supply no contract semantics and
identity enforcement is inconsistent.

**Recommended fix**: Extend the authoritative-exclusion classifier to all five
API names. Test fake/genuine Result, Old, Requires, and unrelated lookalikes.

### 615. [CONFIRMED] Case-drifted nuspec dependency IDs create accepted dangling SPDX edges

**Location**: `scripts/Test-SharpProofPackageDependencies.ps1`, around lines
384-412 and 443-446.

**Description**: PowerShell `-notin` validates expected dependency IDs
case-insensitively, then preserves the untrusted spelling when deriving the
case-sensitive SPDX relationship target.

**Reproduction**: Changing only `SharpProof.Attributes` to
`sharpproof.attributes` passed graph/topology validation. The relationship used
`SPDXRef-Package-sharpproof.attributes`, while the package row remained
`SPDXRef-Package-SharpProof.Attributes`; the endpoint was dangling. A temp
`-cnotin` fix rejected it and preserved the canonical control.

**Impact**: Release generation, validation, and publication can accept a
contradictory package/SBOM graph.

**Recommended fix**: Use ordinal ID membership and require every relationship
endpoint to exist in the ordinal SPDX-ID set. Add dependency-case fixtures.

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

### 618. [CONFIRMED] Proven checked overflow in an earlier argument causes false SP0027

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 450-460; `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
2175-2177 and 1898-1901.

**Description**: Permissive binary completion checks children/divide-by-zero but
ignores `IsChecked` and proven overflow. The analyzer evaluates a later invalid
argument even though the invocation never occurs.

**Reproduction**: `Positive(checked(int.MaxValue + 1), -1)` emitted SP0027 while
runtime call count was zero and overflow threw. Checked-safe, unchecked-overflow,
and direct controls executed and were diagnosed.

**Impact**: Deterministic false diagnostics and warnings-as-errors failures.

**Recommended fix**: Add a flow-aware proven-overflow predicate for checked
operations; return non-completing only when all evaluations overflow, preserving
mixed/unknown and unchecked cases.

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

### 631. [CONFIRMED] Omitted optional in arguments miss definite violations

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 385-447 and 623-639; `CallArgumentAliasPolicy.cs`, around lines 30-32.

**Description**: Roslyn represents an omitted optional `in` argument as implicit
`ArgumentKind.DefaultValue` with invocation syntax. Alias policy rejects every
non-ArgumentSyntax alias before recognizing that this is a compiler-created
snapshot.

**Reproduction**: `[Positive] in int value = -1; Take()` bound successfully and
runtime received -1, but no SP0027. Explicit `-1` and omitted by-value controls
emitted SP0027; satisfying optional-in stayed clean.

**Impact**: Optional defaults and `in` work separately, but their intersection
creates a definite call-site false negative.

**Recommended fix**: Classify `ArgumentKind.DefaultValue` as Snapshot before the
syntax guard, preserving explicit `in local` call-entry semantics. Extend the
alias/default matrix.

### 632. [CONFIRMED] Partial-SMT outcome coverage is computed then discarded

**Location**: `Tools/SharpProof.Fuzz/PartialTermSmtFuzzing.cs`, around lines
16-22; `FuzzRunner.cs`, around lines 82-110, 223-236, and 354-365.

**Description**: The oracle exposes defined-true, defined-false, and undefined
counts, but FuzzRunner retains only one Agreement counter. Passed/Coverage cannot
establish that the undefined path ran.

**Reproduction**: Production seed/case reconstruction produced two defined-true
scenarios and zero undefined, yet a one-case campaign returned
`PartialSmtAgreements=1 Passed=True`; the summary had no other partial fields.

**Impact**: Accepted campaign evidence can be green with zero partiality
coverage, and future generator regressions eliminating undefined scenarios stay
invisible.

**Recommended fix**: Aggregate/serialize exact scenario outcome counts, validate
their sum, and require at least one defined and one undefined outcome wherever
coverage is claimed. Add malformed/round-trip/default-seed tests.

### 633. [CONFIRMED] Signed MinValue divided or reduced by -1 produces a false SP0027

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 450-460; `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
2175-2177 and 2461-2465.

**Description**: Permissive argument completion treats signed `MinValue / -1`
and `MinValue % -1` as completing because its exceptional check recognizes only
a literal zero divisor. The runtime throws `OverflowException` even in unchecked
code, so a following contracted call is unreachable.

**Reproduction**: Calls whose first unused argument was
`unchecked(long.MinValue / -1)` or the equivalent remainder produced SP0027,
while runtime controls recorded zero target calls and an overflow. Safe division
and direct-call controls remained reachable and diagnosed.

**Impact**: Deterministic false contract-violation diagnostics can fail builds
for unreachable calls.

**Recommended fix**: Add a flow-aware signed division-overflow predicate for
both Divide and Remainder, using the existing safe-direction facts and preserving
unknown/mixed-type cases. Test all signed widths, checked/unchecked syntax, and
safe-direction controls.

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

### 635. [CONFIRMED] SPMETA002 misses four catalogued concurrent collections

**Location**: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, around
lines 80-94 and 608-650.

**Description**: The catalog names `BlockingCollection`, `ConcurrentBag`,
`ConcurrentQueue`, and `ConcurrentStack`, but mutable-storage recognition checks
those names only in `System.Collections.Generic`. Their real namespace is
`System.Collections.Concurrent`. `ConcurrentDictionary` happens to be caught
through `IDictionary`; the other four are silently missed.

**Reproduction**: A Roslyn probe declared one field of each real concurrent type
and received zero SPMETA002 diagnostics. `ConcurrentDictionary` and `List`
controls each produced one; immutable controls stayed clean.

**Impact**: Soundness-critical projects can store mutable concurrent collections
despite the analyzer claiming to forbid them.

**Recommended fix**: Match exact metadata identities, or correctly include
`System.Collections.Concurrent`. Table-drive all five types, prevent duplicate
dictionary diagnostics, and keep same-named lookalikes clean.

### 636. [CONFIRMED] Omitted by-value optional arguments block reusable source summaries

**Location**: `SharpProof.Frontend/RoslynProgramLowerer.cs`, around lines 35-53
and 294-320; `SharpProof.Worker/CompilerCallableLowerer.cs`, around lines 151-167
and 305; `CompilerRelationalSummaryProvider.cs`, around line 253.

**Description**: Direct invocation lowering accepts only
`ArgumentKind.Explicit`. Roslyn represents an omitted optional value parameter
as `ArgumentKind.DefaultValue`, so an otherwise exact direct source call is
classified unsupported instead of substituting its compile-time default.

**Reproduction**: `Read(int value, int ignored = 0)` called as `Read(value)` made
preparation fail with `UnsupportedBody`; spelling `Read(value, 0)` produced a
reusable relational summary.

**Impact**: Ordinary optional-argument syntax downgrades verifiable call chains
to Unknown/incomplete.

**Recommended fix**: Admit uniquely bound by-value DefaultValue arguments when
their constant is exactly lowerable, while retaining fail-closed handling for
params, reduced extensions, and ref-like arguments. Add omitted/explicit/default
type-conversion and unsupported-default tests.

### 637. [CONFIRMED] A safe synchronous using prefix suppresses a later SP0027

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 188-208 and 425-471; `SharpProof.Effects/ManagedAbstractFlow.cs`, around
lines 1842-1917; `RequiresCallSiteAnalyzer.cs`, around lines 339-342.

**Description**: Prefix completion has no `IUsingOperation` or using-declaration
model. Even a completed acquisition/body/disposal, or a null-resource no-op,
makes a later explicit contracted call non-replayable.

**Reproduction**: Safe using statement, safe using declaration, and null-resource
prefixes all had complete flow and reached the invalid call at runtime, yet
reported no SP0027. Direct/empty-prefix controls reported it. A throwing using
statement did not reach the call, while a throwing declaration disposal occurred
after the call.

**Impact**: Common resource-management syntax creates deterministic analyzer
false negatives.

**Recommended fix**: Model synchronous using timing with the existing disposal
resolver. A statement requires acquisition, body, and disposal completion; a
declaration preceding a same-scope call requires only acquisition at that point.
Preserve throwing-disposal controls.

### 638. [CONFIRMED] Framework identity scanning misses nonconstant string concatenation

**Location**: `SharpProof.ArchitectureTest/FrameworkIdentityScanner.cs`, around
lines 107-150 and 207-232; consuming gate in `ArchitectureTests.cs`, around lines
441-464.

**Description**: The scanner detects interpolated framework identities but has
no structural fallback for string-add expressions. Expressions such as
`"System." + suffix` therefore evade the exact policy.

**Reproduction**: A fixture using `"System." + suffix` produced zero violations,
while the interpolation equivalent was caught. A temporary string-add-prefix
walker made the full six-case scanner suite pass.

**Impact**: The architecture gate can remain green while production code embeds
framework identities through ordinary concatenation.

**Recommended fix**: Flatten string-add chains, prove a leading constant
`System.` prefix, require string typing, and preserve exclusions/deduplication.
Test one- and multi-literal prefixes plus nonstring and later-segment controls.

### 639. [CONFIRMED] Successful dotnet clean leaves compiler-manifest.input.json

**Location**: `SharpProof.CompilerCollector/FinalCompilationCollector.cs`, around
lines 7 and 19-35; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
around lines 148-149, 178, 193, and 327-359; `ResetPublishedVerification.cs`,
around lines 11-48.

**Description**: The stable compiler-manifest source is generated beside the
project and consumed by verification, but neither the clean target nor reset
logic removes it or records it in MSBuild `@(FileWrites)`.

**Reproduction**: A verification build created the default file. `dotnet clean`
with verification both disabled and enabled returned zero and removed bin/obj
and publications, but the source manifest survived byte-for-byte.

**Impact**: Clean is incomplete and leaves derived compiler input in source
trees, creating stale artifacts and dirty checkouts.

**Recommended fix**: Add the stable manifest to `@(FileWrites)` and explicitly
remove legacy/untracked copies during reset. Test build-clean in both profiles,
failed builds, custom paths, and repeated clean.

### 640. [CONFIRMED] Finite-domain SMT outcome coverage is collapsed and discarded

**Location**: `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs`, around lines
15-20; `FuzzRunner.cs`, around lines 82-110, 207-220, and 354-365.

**Description**: Each finite-domain result contains expected outcome, actual
outcome, and assumption count, but the campaign retains only an aggregate
agreement count. Passing evidence cannot prove that SAT, UNSAT, or nonempty
finite-domain assumptions were exercised.

**Reproduction**: A one-case seed produced a constant-false UNSAT formula with
zero assumptions. The campaign still returned `SmtAgreements=1 Passed=True` and
serialized no outcome/assumption coverage.

**Impact**: The SMT campaign can certify a degenerate generator that exercises
only one outcome or no finite-domain constraints.

**Recommended fix**: Aggregate and serialize SAT/UNSAT/assumption counts, check
their exact sum, and require both outcomes plus nonzero assumptions where this
coverage is claimed. Add deterministic seed and malformed-summary tests.

### 641. [CONFIRMED] Function-pointer overloads collide in compiler identity

**Location**: `SharpProof.Frontend/CompilerIdentityBridge.cs`, around lines
146-194; `SharpProof.Effects/EffectValues.cs`, around lines 225-270.

**Description**: Documentation IDs omit function-pointer parameter structure,
and the fallback display identity does too. Overloads that differ only by
`delegate*` signature therefore compare equal and become order-dependent.

**Reproduction**: `Pick(delegate*<int, void>)` and
`Pick(delegate*<string, void>)` both produced `M:Subject.Pick()` and identical
fallback text; metadata comparison returned zero despite distinct interned
types. Anonymous structural types have the same fallback weakness.

**Impact**: Deterministic ordering, hashing, and identity-based evidence can
collapse distinct legal compiler symbols.

**Recommended fix**: Add a stable structural encoder for doc-ID-inexpressible
types, including function-pointer calling convention, return/ref shape,
parameters/ref kinds, and anonymous ordered properties. Test overload order and
round-trip stability.

### 642. [CONFIRMED] Invalid Ensures or Assume placement poisons BindRequires

**Location**: `SharpProof.Contracts/EffectiveContractSourceResolver.cs`, around
lines 71-91; `ContractClauseInventory.cs`, around lines 21-24;
`ContractBinder.cs`, around lines 87-103, 192-195, and 225-240.

**Description**: Mode-specific `BindRequires` fails on the inventory's global
placement-error bit. A valid leading Requires is therefore discarded when only
a later Ensures or Assume is misplaced.

**Reproduction**: A valid Requires followed by an ordinary statement and a late
Ensures/Assume made full binding fail as expected, but also made BindRequires
fail, suppressing SP0027 at a known-invalid caller. A wrong-signature Ensures
control still allowed Requires binding.

**Impact**: One postcondition/assumption diagnostic can hide independent,
definite call-site precondition violations.

**Recommended fix**: Make placement failure mode-specific: BindRequires should
reject invalid Requires placement while ignoring unrelated clause placement,
without allowing a bad direct postcondition to expose companion contracts. Add
full/requires, late-Requires, and direct-plus-companion tests.

### 643. [CONFIRMED] Completed refutations are emitted as failed SARIF invocations

**Location**: `SharpProof.Worker.Launcher/SarifProjection.cs`, around lines
87-94 and 117-147; `SharpProof.Package.Test/LauncherArgumentTests.cs`, around
lines 1673-1754.

**Description**: `executionSuccessful` requires that no claim is Refuted. SARIF
defines this flag as operational tool success; findings belong in `results` and
do not make a completed analysis invocation fail.

**Reproduction**: A validated Complete response with one ordinary refuted
postcondition serialized as
`executionSuccessful=False resultKind=fail resultLevel=error`. The existing test
pins the false flag.

**Impact**: SARIF consumers conflate valid proof findings with analyzer or
infrastructure failure, distorting dashboards and CI ingestion.

**Recommended fix**: Base invocation success on operational status and tool
errors only, retaining refutations as fail/error results. Test Complete
proven/refuted/unknown as true and failed/timed-out/canceled/tool-error runs as
false, including one real refuted build.

### 644. [CONFIRMED] Reused reference awaiters hide caller-owned writes as Fresh

**Location**: `SharpProof.Effects/OperationEffectScanner.Expressions.cs`, around
lines 238-258 and 280-284; `EffectSummaryOperations.cs`, around lines 149-165;
`EffectContractMappings.generated.cs`, around line 218.

**Description**: Await lowering always assigns the `GetAwaiter` result a Fresh
region. The await pattern permits a reference awaiter to return `this`, so later
`IsCompleted`/`GetResult` receiver effects can mutate the caller-owned operand.

**Reproduction**: A sealed awaiter returned itself and wrote `State=1729` in
`GetResult`. Runtime observed the argument write; Effects returned Complete and
nonunknown with only a Fresh write and no WritesArgumentState. A new-awaitable
control correctly remained Fresh.

**Impact**: Effect contracts can falsely certify custom awaiters as pure or as
not writing argument state.

**Recommended fix**: Classify the actual GetAwaiter return alias: map `this` and
reduced-extension receiver returns to the operand, allocations to Fresh, join
conditional alternatives, and use Unknown when unresolved. Test alias/fresh,
conditional, and extension controls.

### 645. [CONFIRMED] Non-returning user binary operators cause a false SP0027

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 450-460; `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
2053-2103 and 2175-2177; `OperationCompletionEvaluator.cs`, around lines
1059-1075.

**Description**: Permissive binary-operation completion checks children and
division-by-zero only, never `IBinaryOperation.OperatorMethod`. A source-defined
operator that always throws is therefore treated as completing before a later
contract-bearing call.

**Reproduction**: A throwing `operator +` in the first unused argument produced
SP0027 for the following invalid call. Runtime recorded zero target calls and the
operator exception. Returning operator, built-in operator, and direct controls
correctly reached the call and diagnosed it.

**Impact**: Deterministic false Refuted diagnostics and warnings-as-errors build
failures arise for unreachable calls.

**Recommended fix**: Share the stricter binary completion logic and require
`OperatorMethod == null || MethodCanCompleteNormally(OperatorMethod)`, while
preserving conditional-operator truth/short-circuit semantics. Add source,
metadata, built-in, and throwing-operand controls.

### 646. [CONFIRMED] Queued SMT checks delay cancellation and pin worker threads

**Location**: `SharpProof.Smt/IrSmtBackend.cs`, around lines 31-55.

**Description**: `CheckAsync` schedules every request with `Task.Run` before a
non-cancelable monitor lock. Token checking and cancellation registration happen
only after gate admission, so each queued request blocks one thread-pool worker
and cannot finish cancellation behind a slow active query.

**Reproduction**: With the gate held, 32 well-formed queued checks reduced
available workers by 32. After cancellation, zero completed during 500 ms; all
canceled immediately after gate release. A healthy reuse query then returned
Unsatisfiable.

**Impact**: Ordinary concurrent use creates O(waiters) blocked workers, delayed
cancellation, thread-pool starvation, and avoidable memory pressure.

**Recommended fix**: Use cancellation-aware asynchronous admission before
assigning the one blocking worker needed for Z3, and coordinate Dispose through
the same state protocol. Test queued cancellation before active release, worker
availability, active interruption, and reuse.

### 647. [CONFIRMED] Frontend fuzz coverage counts unreachable syntax as executed coverage

**Location**: `Tools/SharpProof.Fuzz/FuzzRunner.cs`, around lines 53-66,
179-184, and 424-517.

**Description**: Frontend category coverage recursively counts every syntax
child without reachability/evaluation information. Operations placed only under
literal-false conditionals therefore satisfy the semantic coverage gate.

**Reproduction**: In a 1,000-case benign campaign, all Text/StringLiteral/
NullString/Concat/StringLength nodes were confined to false branches. Every
frontend comparison agreed, and the summary reported those counters nonzero,
`Expanded=True CoverageSatisfied=True Passed=True`.

**Impact**: Release evidence can claim semantic coverage for operations that
never affect either oracle, masking operation-specific regressions.

**Recommended fix**: Separate generated-shape from live/evaluated coverage and
gate on the latter, or dedicate root-level live cases. Test false/true branch and
short-circuit controls with deterministic parallel totals.

### 648. [CONFIRMED] WellSortedIrGenerator category labels survive operation folding

**Location**: `SharpProof.Testing/WellSortedIrGenerator.cs`, around lines 58-69
and 182 onward; `SharpProof.Ir/IrFactory.cs`, around lines 516-533;
`IrCSharpDifferentialOracleTests.cs`, around lines 20-30.

**Description**: The generator records the requested category before building
the term. `Length` of a string literal is folded immediately into an integer, but
the result remains labelled StringLength and is credited as such by tests.

**Reproduction**: In the canonical 200-case sequence, 26 cases were labelled
StringLength but only 22 had length roots; indices 115, 119, 128, and 157 were
integer literals after folding.

**Impact**: Category-based coverage overstates exercised IR operations and can
stay green if every selected operation is folded away.

**Recommended fix**: Use guaranteed nonfoldable operands for operation-specific
categories, or derive/retry the category from the emitted term. Assert category
shape invariants for every operation-specific label.

### 649. [CONFIRMED] SPMETA001 misses forbidden Roslyn method-group delegates

**Location**: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, around
lines 42-68, 107-150.

**Description**: The analyzer registers only Invocation operations. A forbidden
method converted to a delegate is exposed as `IMethodReferenceOperation`, and
the later call targets `Func.Invoke`, so the exact forbidden symbol never reaches
the rule.

**Reproduction**: Direct `Compilation.AddSyntaxTrees` and
`RemoveAllSyntaxTrees` calls each produced one SPMETA001. Equivalent method-group
conversions retained the exact Roslyn method symbols, executed successfully, and
produced zero diagnostics.

**Impact**: Analyzer-attached code can store, pass, and invoke delegates to every
catalogued forbidden API while SPMETA001 remains green.

**Recommended fix**: Register MethodReference operations and run their original
method symbol through the shared forbidden/allowlist predicate. Test direct and
delegate forms, no duplicate at `Func.Invoke`, allowlisted adapter references,
and same-named lookalikes.

### 650. [CONFIRMED] API-spec exception sets hash as ordered multisets

**Location**: `SharpProof.Specs/ApiSpecContentDigest.cs`, around lines 33-37;
`ApiSpecTable.cs`, around lines 280-293.

**Description**: Digest construction hashes exception array length, order, and
duplicates, while all runtime consumers canonicalize exception names as a set.
Validation enforces only initialized, nonblank names.

**Reproduction**: Otherwise-identical valid MayThrow tables with forward,
reversed, and duplicated exception names had three different content hashes,
although runtime `SetEquals` and effect throw-set semantics were identical.

**Impact**: Semantically identical custom catalogs cause cache/input identity
churn and reproducibility differences.

**Recommended fix**: Canonicalize once with ordinal distinct/sort (or reject
duplicates and hash sorted names), then store and hash that representation. Test
reorder/duplicate equality and real-member changes.

### 651. [CONFIRMED] API-spec digest variable lookup is quadratic

**Location**: `SharpProof.Specs/ApiSpecContentDigest.cs`, around lines 84-86.

**Description**: Every variable-reference leaf calls `variables.Single(...)`,
which scans the whole declaration array to prove uniqueness even though table
validation already builds a slot dictionary.

**Reproduction**: Valid balanced templates with 2k/4k/8k/16k distinct Boolean
variables took 53/176/618/2371 ms minimum, approximately quadrupling per doubling
despite logarithmic expression depth.

**Impact**: Large generated or programmatic Specs spend seconds constructing a
single table/content identity.

**Recommended fix**: Build one `(Role, Ordinal) -> Id` dictionary per template
and pass it through traversal. Preserve the golden digest and prove one O(1)
lookup per leaf with scaling coverage.

### 652. [CONFIRMED] Default release-evidence generation cannot rerun in place

**Location**: `scripts/New-SharpProofReleaseEvidence.ps1`, around lines 543-586
and 751-973; `scripts/SharpProof.ReleaseChecksums.ps1`, around lines 144-187.

**Description**: By default output equals PackageSource. The command requires
exactly six package inputs, then adds three evidence files and publishes the
nine-file bundle back into that same directory. Its next default invocation
rejects its own prior output for having the wrong file count.

**Reproduction**: A valid six-file source was accepted and became a nine-file
bundle; a second unchanged invocation returned
`Release package input has an unexpected file or directory count.` Existing
rerun coverage always supplies a separate output directory.

**Impact**: Direct retries require manual deletion or a full expensive repack.

**Recommended fix**: Default to a separate sibling output, or recognize and
atomically replace only the exact three owned evidence files when input equals
output. Test two identical default invocations, unrelated extras, corruption,
and explicit-output reruns.

### 653. [CONFIRMED] Supplied SBOMs are rejected unless their input basename is canonical

**Location**: `scripts/New-SharpProofReleaseEvidence.ps1`, around lines 765-769
and 901-910; `scripts/SharpProof.ReleaseChecksums.ps1`, around lines 152-160.

**Description**: `-SbomPath` preserves the supplied basename in staging and the
manifest, while final topology requires the SBOM artifact to be named exactly
`SharpProof.spdx.json`.

**Reproduction**: Identical valid SPDX bytes named `custom.spdx.json` failed the
exact bundle authority; the canonical basename passed.

**Impact**: The public path parameter rejects ordinary externally generated
SBOM filenames late after expensive validation.

**Recommended fix**: Always copy supplied bytes to the canonical staging name
and record that name. Test arbitrary input names, byte identity, malformed
content, and exact manifest topology.

### 654. [CONFIRMED] Framework interpolation fallback reports a later System. segment as a prefix

**Location**: `SharpProof.ArchitectureTest/FrameworkIdentityScanner.cs`, around
lines 124-140 and 234-246.

**Description**: For nonconstant interpolation, the fallback selects the first
text node anywhere with `OfType<InterpolatedStringTextSyntax>().FirstOrDefault()`.
It ignores preceding holes, so a later `System.` text segment is mistaken for the
produced string's prefix.

**Reproduction**: `$"{intPrefix}System.String"` produced a violation although
the runtime value always starts with the formatted integer. A leading-content
fix made all six scanner fixtures pass; `$"System.{suffix}"` remained caught.

**Impact**: Benign logging/composition can falsely fail the architecture gate.

**Recommended fix**: Require the interpolation's first content item itself to
be a qualifying text node, or explicitly model possibly empty leading holes.
Test leading text, leading int hole, later System text, and constants.

### 655. [CONFIRMED] Package scheduler governance stays green after queue sorting is deleted

**Location**: `scripts/Invoke-SharpProofPackageTests.ps1`, around lines 331-335;
`SharpProof.ArchitectureTest/ArchitectureTests.cs`, around lines 1465-1580.

**Description**: The test asserts that the script contains timing vocabulary and
some `Sort-Object` token, but several unrelated sorts satisfy it. It does not
couple descending EstimatedMilliseconds order to pending-queue insertion.

**Reproduction**: Removing only the longest-estimate-first queue sort left the
named governance test passing. A queue-specific assertion failed under mutation
and passed after restoration.

**Impact**: A scheduling regression to insertion order can increase package-suite
makespan and deadline failures while the supposed backstop remains green.

**Recommended fix**: Extract a pure shard-schedule helper and test synthetic
estimates, descending order, deterministic name ties, selected mode, and unknown
estimates. At minimum pin the unique queue-sort adjacency with a deletion test.

### 656. [CONFIRMED] NoModeledNormalReturn proof is discarded at the query-budget boundary

**Location**: `SharpProof.Worker/CallableVerifier.cs`, around lines 145-184,
186-237, and 254-276.

**Description**: After proving normal completion UNSAT, the verifier still
requires a separate per-postcondition query reservation before applying the
conclusive `NoModeledNormalReturn` vacuity result. Exhausting the budget after
the completion proof downgrades it to Unknown.

**Reproduction**: For an exact divide body with no modeled normal return, a
200-unit budget made two queries then returned `Unknown/ResourceLimit`; 300 units
made a redundant third query then returned `Proven/NoModeledNormalReturn` with
core `body:normal-completion`.

**Impact**: Completed proofs become strict verification failures and consume
unnecessary SMT budget.

**Recommended fix**: After clause substitution/domain validation, assemble
aligned claims directly from the conclusive normal-completion proof without
reserving another query. Test exact two-call tight budget, reachable-normal
controls, multiple Ensures, and malformed clauses.

### 657. [CONFIRMED] Symbol validation accepts Source Link mappings covering no documents

**Location**: `scripts/SharpProof.SymbolPackageValidator.cs`, around lines
258-293.

**Description**: Validation checks that mappings are nonempty, wildcard-shaped,
and point to the expected commit URL, but never applies mapping keys to the
portable PDB document table.

**Reproduction**: In a valid Attributes symbol package, changing the same-length
mapping key from `/_/*` to `/x/*` left identifiers and URL intact. Production
validation accepted it although all 17 documents began `/_/` and zero were
covered.

**Impact**: Release validation can certify a symbol package from which debuggers
cannot resolve any source document.

**Recommended fix**: Require exactly one applicable Source Link mapping for
every PDB document using Source Link wildcard semantics. Reject uncovered,
ambiguous, malformed, and unused mappings; test full, partial, and overlapping
coverage.

### 658. [CONFIRMED] Self-targeting ContractFor suppresses executable method analysis

**Location**: `SharpProof.Contracts/ContractForSymbolMatcher.cs`, around lines
53-112, 154-164, and 244-249; `ContractForValidationEngine.cs`, around lines
135-141; `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`, around lines
31-35 and 207-210.

**Description**: `[ContractFor(typeof(Itself))]` is accepted on a static class.
Discovery then classifies the executable target as a companion, so both selected
and unselected operation-block paths skip every real method in the class.

**Reproduction**: A self-targeted static class contained `[EnforcePure] Write()`
that mutated static state. It discovered `Target->Target`, emitted no SPCF or
SP0002 diagnostics, and wrote state at runtime. No-ContractFor and separate-
companion controls both emitted SP0002.

**Impact**: One accepted attribute suppresses effect, contract-body, and
call-site analysis for an entire executable class, creating analyzer/collector
divergence.

**Recommended fix**: Reject equal original definitions during validation and
binding, and harden companion classification so invalid/self descriptors cannot
drive operation-block suppression. Test non-generic/open-generic self targets,
SPCF0003 plus continued SP0002, binder failure, and valid separate companions.

### 659. [CONFIRMED] A missing compiler manifest does not trigger incremental recompilation

**Location**: `SharpProof.CompilerCollector/FinalCompilationCollector.cs`, around
lines 7 and 19-35; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`,
around lines 148-182 and 232-234.

**Description**: The analyzer-generated manifest is a required CoreCompile side
output, but the package never declares it in `@(CustomAdditionalCompileOutputs)`.
MSBuild can therefore skip Csc while the verifier immediately requires the
missing undeclared file.

**Reproduction**: A verified project built Proven. Deleting only
`obj/Release/net8.0/SharpProof/compiler-manifest.input.json` and running ordinary
Build produced `CoreCompile` up-to-date, zero Csc tasks, then SP0049. The assembly
was byte-identical and the file stayed absent. Rebuild recreated it and proved
the unchanged project.

**Impact**: Partial obj cleanup or incomplete build-cache restoration leaves a
project repeatedly broken until another compiler output changes or Rebuild runs.

**Recommended fix**: Under the active collector condition, add the stable
manifest to `@(CustomAdditionalCompileOutputs)` before CoreCompile and to
`@(FileWrites)` for Clean. Test delete-only repair, subsequent no-op build,
custom paths, and verify-disabled behavior.

### 660. [CONFIRMED] List-pattern indexer and Slice calls lose synthesized arguments

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 209-225 and 627-703; `RequiresCallSiteAnalyzer.cs`, around lines 380-405
and 570-593.

**Description**: List-pattern discovery finds Length, indexers, and Slice, but
constructs every implicit member call with empty argument arrays. Reconciliation
also keys only by syntax and target, collapsing repeated indexer calls at
different synthesized indices.

**Reproduction**: Runtime traced `Length, Item(0), Item(1)` for `[1,2]` and
`Length, Item(0), Slice(1,1)` for `[1,..var r]`. Discovered candidates had one or
two parameters but zero actuals and emitted no SP0027 for index/start-dependent
violations. Equivalent direct calls diagnosed; constant-false clauses showed
that implicit member discovery itself was active.

**Impact**: List-pattern contracts depending on index, slice start, or slice
length silently become Unknown, and distinct implicit invocations lose context.

**Recommended fix**: Carry synthetic typed actual values and an invocation
ordinal through candidates/reconciliation. Derive prefix/suffix indices and
Slice start/length from sound length facts, preserving evaluation order and
fail-closed unknown-length/noncompletion cases. Test repeated getter vectors,
prefix/suffix/slice parameters, mismatches, and throwing members.

### 661. [CONFIRMED] Definitely-null property ??= setters remain unreplayable

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 180-186, 230-270, 524-535, and 1556-1571;
`RequiresCallSiteAnalyzer.cs`, around lines 339-342.

**Description**: Property coalesce-assignment always creates the potential setter
with `CanReplay=false`, no flow, and BudgetExceeded. It models whether the getter
can complete, but not whether its result is definitely null or nonnull.

**Reproduction**: A getter that always returned null caused runtime to call the
setter once with null, violating its Requires. Discovery emitted a nonreplayable
setter and no SP0027; direct assignment diagnosed. Nonnull and throwing-getter
controls correctly executed no setter.

**Impact**: Definite property/indexer setter violations through `??=` disappear
even when source semantics prove the setter executes.

**Recommended fix**: Add conservative getter-result classification. Omit the
setter when definitely nonnull, retain fail-closed Unknown when uncertain, and
build an exact post-getter/RHS replay state when definitely null. Test block and
expression getters, RHS failure, indexers, nullable values, and single-evaluation
ordering.

### 662. [CONFIRMED] Supervisor control records leak into user-visible verifier output

**Location**: `SharpProof.BuildTasks/VerifierProcessSupervisor.cs`, around lines
94-105 and 218-225; `RunVerifier.cs`, around lines 342-353 and 607-705.

**Description**: Output draining appends raw chunks before recognizing
`SharpProof.Armed/1 <nonce>` and `SharpProof.Cleanup/1 <nonce>`. Authentication
signals are set, but the same internal records remain in returned text and are
logged at high importance. They also consume the verifier diagnostic-size budget.

**Reproduction**: The exact parser consumed Armed, ordinary output, separator,
and Cleanup. Both authentication flags were true, while returned log text still
contained both full nonce records.

**Impact**: Every normal verified build emits random internal protocol frames,
making logs noisy and nondeterministic; large solutions multiply the noise and
protocol overhead can prematurely exhaust diagnostic capture.

**Recommended fix**: Parse control-plane lines separately and suppress exact
authenticated frames plus their inserted separator from visible output. Apply
the cap to verifier diagnostics only while preserving byte/line-ending behavior.
Test split chunks, final unterminated lines, near-limit output, and build-engine
logging.

### 663. [CONFIRMED] Parameterized array range slices omit mandatory allocation

**Location**: `SharpProof.Effects/OperationEffectScanner.cs`, around lines
544-596.

**Description**: Array-element scanning treats a single `System.Range` index as
ordinary element access. It evaluates receiver/index and exceptions but never
records that a successful array slice allocates a new managed array.

**Reproduction**: `int[] Slice(int[] values, Range range) => values[range]`
returned a distinct array; mutation did not affect the source, and 64 escaping
calls allocated 2,048 bytes. Effects reported `Allocation=None`, Complete, and a
complete projection. Ordinary int-index and jagged-array selection controls
correctly allocated nothing.

**Impact**: No-allocation and purity contracts can accept a method that allocates
on every successful call.

**Recommended fix**: Recognize the semantic `System.Range` index identity and
join a Managed allocation/direct witness after receiver/index/null gating. Do
not infer from array result type alone. Test range, int index, jagged selection,
and invalid receiver controls.

### 664. [CONFIRMED] Array range slices report the wrong exception type

**Location**: `SharpProof.Effects/OperationEffectScanner.cs`, around lines
544-596.

**Description**: Every unproven array access is assigned
`IndexOutOfRangeException`. Range slicing instead validates offsets through
Range/GetOffsetAndLength and throws `ArgumentOutOfRangeException` for invalid
ranges.

**Reproduction**: `values[new Range(new Index(10), ^0)]` threw
ArgumentOutOfRangeException at runtime, while the Complete summary listed
IndexOutOfRangeException and omitted the actual type. Ordinary `values[10]`
correctly threw/listed IndexOutOfRangeException.

**Impact**: AllowedExceptions, DoesNotThrow, and exception-handler reachability
can prove the wrong contract for array slicing.

**Recommended fix**: For the exact System.Range index form, add canonical
ArgumentOutOfRangeException and retain IndexOutOfRangeException for integer
indices. Add valid/invalid range and ordinary-index controls.

### 665. [CONFIRMED] Constant conditionals select a non-returning arm but still cause SP0027

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 450-460; `SharpProof.Effects/ManagedAbstractFlow.cs`, around lines
2127-2130; `OperationCompletionEvaluator.cs`, around lines 1088-1105.

**Description**: Definite-operation completion combines both conditional arms
with OR even when the condition has a constant Boolean value. A returning
unselected arm therefore makes a definitely non-returning selected expression
appear to complete before a contracted call.

**Reproduction**: Both `true ? Never() : 0` and `false ? 0 : Never()` in the
first unused argument produced SP0027 for the later invalid call. Runtime recorded
zero target calls and the selected-arm exception. The two live-arm mirrors
reached the call and correctly diagnosed.

**Impact**: Literal conditional syntax can create deterministic false Refuted
diagnostics and warnings-as-errors failures for unreachable calls.

**Recommended fix**: If the condition is a constant bool, recurse only into the
selected arm; otherwise preserve permissive OR. Share the already-correct strict
completion logic and test true/false, nonconstant, and nested controls.

### 666. [CONFIRMED] SPMETA003 trusts CallerCancellationWon by name, not behavior

**Location**: `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs`, around
lines 490-603; production helper in `SharpProof.Worker/SharpProofWorker.cs`,
around lines 83-113.

**Description**: The exact Worker cancellation rule accepts any zero-parameter
Boolean local function named `CallerCancellationWon`. It never inspects the
helper body, captured token, returned values, or interruption cause.

**Reproduction**: `CallerCancellationWon() => false` and a same-named helper
checking `CancellationToken.None` both emitted zero SPMETA003. Renaming only the
false helper emitted the error. Executing the false-helper version with a
canceled caller returned `TimedOut` instead of `Canceled`.

**Impact**: A name-preserving typo/refactor can misclassify every caller
cancellation while SharpProof's self-application remains green.

**Recommended fix**: Prefer an inline, symbol-bound exact caller-token predicate.
If helper support remains, analyze all normal returns and require derivation from
the exact token and audited interruption/deadline state. Test constants, wrong
tokens, early returns, correct latch formulas, and runtime Canceled/TimedOut
projection.

### 667. [CONFIRMED] Unsupported finite-domain formulas fabricate Expected=Unsatisfiable

**Location**: `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs`, around lines
15-20 and 139-166.

**Description**: Expected is nonnullable, so the early unsupported-domain
abstention stamps Unsatisfiable before any enumeration. The value is a placeholder
but is exposed as an actual oracle verdict.

**Reproduction**: A well-typed string equality was true under a concrete binding,
yet the finite result was
`Status=Abstained Expected=Unsatisfiable Actual=null Assumptions=0`.

**Impact**: Reports, tests, or minimizers consuming Expected receive a false
semantic assertion on ordinary abstentions.

**Recommended fix**: Make Expected nullable or add an explicit not-computed
state, returning no expectation until enumeration completes. Test satisfying and
false unsupported formulas plus supported SAT/UNSAT controls.

### 668. [CONFIRMED] Accepted finite-domain formulas are enumerated twice

**Location**: `Tools/SharpProof.Fuzz/FuzzRunner.cs`, around lines 534-556;
`FiniteDomainSmtFuzzing.cs`, around lines 30-106, 168-174, and 223-285.

**Description**: Candidate acceptance traverses every assignment to prove
totality, discards satisfiability, then the oracle traverses the same Cartesian
domain again to compute the expected verdict.

**Reproduction**: A production-shaped total UNSAT formula with two integer and
one Boolean variables performed 50 leaf evaluations for definedness and another
50 for satisfiability. Repeated timing was 475 ms plus 413 ms for 20,000 runs.

**Impact**: Accepted UNSAT campaigns do exactly twice the necessary interpreter
work; large campaign ceilings allow tens of millions of redundant evaluations.

**Recommended fix**: Return `{AllDefined, AnyTrue}` from one enumeration and
carry the expected verdict into comparison. Retain a standalone one-pass public
oracle. Add deterministic leaf-count, early-SAT, partial, and cancellation tests.

### 669. [CONFIRMED] Package configuration failures have no stable diagnostic code

**Location**: `SharpProof.Package/buildTransitive/SharpProof.targets`, around
lines 64-89.

**Description**: All nine MSBuild `<Error>` tasks omit `Code=`. Because the
configuration target runs before CoreCompile, the analyzer cannot emit its
documented SP0025 replacement; users receive bare `error :` messages.

**Reproduction**: Building with `SharpProofProfile=invalid` exited 1 with
`error : SharpProofProfile must be advisory, strict, or off` and zero SharpProof
codes. An invalid verifier policy correctly emitted SP0054.

**Impact**: IDEs, CI parsers, documentation links, and support tooling cannot
classify or group nine public configuration failures.

**Recommended fix**: Assign SP0025 to analyzer profile/features/mode/runtime
configuration errors and SP0054 to verifier/package integration errors. Require
allowed codes on every Error element in both target files and add consumer
integration tests.

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

### 671. [CONFIRMED] yield break-only iterators omit state-machine allocation

**Location**: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, around lines
22-101.

**Description**: Method effects are built from source/CFG operations and never
recognize iterator lowering. A body containing only `yield break` has no explicit
creation or unsupported operation, so the hidden iterator object disappears and
the summary remains Complete/Allocation=None.

**Reproduction**: Repeated `EmptyIterator()` calls returned distinct reference
objects and 64 calls allocated measurable bytes. Effects reported None/Complete.
A `yield return` control already abstained Unknown/Incomplete; explicit array
allocation reported Managed.

**Impact**: ZeroAllocations and effect contracts can accept a method allocating
a new compiler-generated object on every call.

**Recommended fix**: Detect the method's own iterator semantics (excluding nested
local/lambda yields) and join Managed allocation with a direct witness. Test
yield-break, yield-return conservative behavior, explicit allocation, and nested
iterators.

### 672. [CONFIRMED] Implicit base calls miss optional-only base constructors

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 79-126 and 382-412; `RequiresCallSiteAnalyzer.cs`, around lines 570-593.

**Description**: Implicit base-call discovery equates an empty argument list with
a constructor declaring zero parameters. C# can select a constructor whose every
parameter is optional, and compiler-synthesized default actuals are also missing.

**Reproduction**: `Base(int value=-1)` with `Requires(value>0)` was invoked by an
ordinary derived constructor with no initializer, but discovery returned zero
candidates and zero SP0027. Writing explicit `: base()` produced one default-
argument candidate and SP0027. Runtime values were identical.

**Impact**: Adding or omitting syntactically redundant `: base()` changes
precondition coverage across ordinary and record constructors.

**Recommended fix**: Resolve the compiler-selected empty-argument base overload
and carry typed synthesized defaults, rather than choosing any all-optional
member. Test overload preference, multiple defaults, enums/null, this-chains,
records, metadata, and inaccessible/error cases.

### 673. [CONFIRMED] Any explicit static constructor suppresses member SP0027 diagnostics

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`, around
lines 290-312; reusable completion logic in
`SharpProof.Effects/OperationCompletionEvaluator.cs`, around lines 576-639 and
889-966.

**Description**: Call-site analysis returns Unknown for every static target or
instance constructor whenever the containing type has any static constructor.
It never asks whether initialization can complete or is already complete.

**Reproduction**: An empty, completing static constructor suppressed SP0027 for
both a static method call and `new Target(-1)`, although candidates were Complete
and runtime reached each target. No-cctor controls warned; throwing-cctor controls
correctly stayed quiet and never reached the target.

**Impact**: Common initialized types lose all call-site precondition diagnostics
for static members and instance construction.

**Recommended fix**: Replace the syntactic gate with shared static-initialization
reachability/completion, proceeding only when initialization is complete or can
complete and retaining Unknown otherwise. Test empty/throwing/mixed initializers,
same-type contexts, accessors, constructor chains, metadata, and state effects.

### 674. [CONFIRMED] Compact assumption validation rebuilds owner indexes per claim

**Location**: `SharpProof.Worker.Protocol/ProtocolJson.cs`, around lines 595-663
and 975-1015.

**Description**: For every claim result, validation sorts the same callable's
full declaration array and, for compact rows, rebuilds declaration and actual
dictionaries. The expected declaration set is invariant per callable.

**Reproduction**: A valid 1.22 MB response with 2,000 declarations/claims took
845 ms and allocated 943 MB during public validation. A real serializer compacted
a valid 25.27 MB response to 534 KB, but validating it still allocated 80.1 MB.
An indexed control allocated about 386 KB at 2,000.

**Impact**: The size-saving protocol form causes O(claims * declarations log
declarations) work and extreme transient GC pressure after verification.

**Recommended fix**: Build one duplicate-safe per-callable descriptor containing
declaration count, ordinal ID/kind map, and trusted IDs, then validate all rows
against it. Test full/compact/null inheritance, trusted rows, malformed shapes,
and one-index-build/allocation scaling.

### 675. [CONFIRMED] Reported suppressed compiler errors poison every callable

**Location**: `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`,
around lines 26-48; `CompilerWireMappings.generated.cs`, around line 314.

**Description**: Compiler artifact production filters diagnostics only by
`Severity == Error`. With `ReportSuppressedDiagnostics=true`, a pragma-suppressed
warning promoted to Error is retained despite `IsSuppressed=true`, then assigned
to every callable as UnsupportedCallable.

**Reproduction**: Suppressed CS0168 promoted to Error yielded zero normal Roslyn
errors, but reporting mode returned one suppressed Error, one artifact error, and
UnsupportedCallable for the target. The otherwise identical baseline artifact
had no errors/failure.

**Impact**: Requesting suppressed diagnostics for reporting can downgrade all
claims to Unknown and fail strict verification without changing compilation
semantics.

**Recommended fix**: Retain only Error diagnostics with `!IsSuppressed`. Compare
reporting false/true artifacts and fingerprints, and keep an unsuppressed promoted
error as the poisoning control.

### 676. [CONFIRMED] Delegate signature contracts are selected but silently ignored

**Location**: `SharpProof.Attributes/ClosedContractAttributes.cs`, around lines
3-16; `ContractSelectionInventory.cs`, around lines 135-146;
`ContractBinder.cs`, around lines 74-84; `SharpProofAnalyzerEngine.cs`, around
lines 125-131.

**Description**: Closed-contract attributes legally target delegate parameters
and returns, and selection sees the synthesized DelegateInvoke method. The binder
rejects DelegateInvoke, while Roslyn method symbol actions do not visit synthesized
Invoke methods, so neither validation nor the documented unsupported SP0047 runs.

**Reproduction**: Calls violating `[Positive]` and `[NotNull]` delegate
signatures emitted nothing. A malformed `[Positive] string` delegate also emitted
no SP0024. Binder reported `selection=Contracts` then `UnsupportedTarget`; an
ordinary method control emitted SP0027.

**Impact**: Valid delegate contracts appear supported but are unenforced, and
malformed attributes are silent.

**Recommended fix**: Analyze delegate declarations/named types explicitly,
validate DelegateInvoke closed attributes, and under current policy emit SP0047
at the declaration. Alternatively implement full binder/call-site delegate
support. Test valid/malformed parameter/return attributes and exactly-once output.

### 677. [CONFIRMED] Release mutation catalog maps a mutation to an unrelated test

**Location**: `scripts/Test-SharpProofTrustedMutations.ps1`, around lines 880-886
and 2826-2864; mutation target in
`scripts/Invoke-SharpProofReleaseContainer.ps1`, around lines 165-173.

**Description**: The `release-qualification-matrix-receipt-projection` mutation
removes `$requiredGates` from the release writer, but its focused filter selects
a workflow/catalog test that never reads that file. The mutation runner treats
the resulting green test as a survivor.

**Reproduction**: The catalog's exact mutation left the mapped test passing 1/1.
The existing `QualificationWriterRevalidatesArtifactsAndGateReceipts` test failed
on the same mutation and passed again after restoration.

**Impact**: Any mutation shard containing this row fails falsely despite an
existing test that kills the production mutation, wasting and misattributing the
release mutation gate.

**Recommended fix**: Map the row to the writer test (and refresh receipts), or
add a focused behavioral projection test. Regression should require the selected
test to pass baseline and fail the exact catalog mutation.

### 678. [CONFIRMED] Meta-analyzer enrollment gate trusts unevaluated project XML

**Location**: `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs`, around
lines 295-315.

**Description**: Enrollment is checked by raw XDocument element presence and
metadata. MSBuild conditions, ItemGroup conditions, imports, Choose, and Update
can remove or alter the analyzer reference after evaluation while XML still looks
correct.

**Reproduction**: Adding `Condition="'$(Configuration)' == 'Disabled'"` to the
Effects meta-analyzer ProjectReference left the architecture test passing. Actual
Debug and Release `-getItem:ProjectReference` evaluation omitted the analyzer.

**Impact**: A soundness-critical project can stop running every SPMETA rule while
the test named `RunsTheMetaAnalyzer` remains green.

**Recommended fix**: Reuse evaluated-MSBuild inspection for every critical project
in Debug and Release, requiring exactly one evaluated analyzer reference with
the exact metadata. Test element/ItemGroup conditions, configuration-only items,
imports/Update, and valid unconditional enrollment.

### 679. [CONFIRMED] Verifier-only policies force full C# recompilation

**Location**: `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props`,
around lines 31-32; `SharpProof.AnalyzerConsumer.props`, around lines 18-19;
consumption in `SharpProof.Verifier.targets`, around lines 262-265.

**Description**: VerifyPolicy and AssumptionPolicy are registered as
CompilerVisibleProperty even though no analyzer/collector reads them. Changing
either rewrites generated editorconfig and invalidates CoreCompile.

**Reproduction**: Changing only `require-proven` to `advisory` ran Csc once and
changed editorconfig, while assembly and compiler manifest hashes stayed
identical; the request policy correctly changed. Same-policy and worker-query-
budget controls skipped CoreCompile.

**Impact**: Solution-wide policy changes needlessly rerun Csc, analyzers, and
generators for every affected project.

**Recommended fix**: Remove both compiler-visible registrations in package and
self-apply props while continuing direct target normalization/forwarding. Test
both policy changes skip Csc but update request/result behavior; retain real
compiler-semantic property controls.

### 680. [CONFIRMED] Z3 rlimit setup leaves one StringSymbol finalizer per query

**Location**: `SharpProof.Smt/IrSmtBackend.cs`, around lines 112-115.

**Description**: `parameters.Add("rlimit", value)` uses a convenience overload
that creates a disposable native-backed StringSymbol wrapper the caller cannot
dispose. Params disposal does not own it.

**Reproduction**: 128 real queries produced 128 proportional pending finalizers
(129 including a measured one-object Task.Run baseline). The explicit
`MkSymbol("rlimit")` plus `Add(Symbol, uint)` and disposal control produced zero
while preserving UNSAT/resource behavior.

**Impact**: Long-lived solver lanes accumulate native references and finalizer/GC
pressure on every query, including trivial Boolean checks.

**Recommended fix**: Create and explicitly dispose the rlimit symbol per setup,
or cache one backend-owned symbol disposed before Context. Test N-query finalizer
growth, correct resource limits, cancellation, and exceptional exits.

### 681. [CONFIRMED] Debug mutation campaigns can satisfy Release qualification

**Location**: `scripts/Invoke-SharpProofContainer.ps1`, around lines 7-8 and
298-313; `Test-SharpProofMutationCatalog.ps1`, around lines 44-55 and 108-112;
`Write-SharpProofQualificationReceipt.ps1`, around lines 84-90;
`Invoke-SharpProofReleaseContainer.ps1`, around lines 184-209.

**Description**: Mutation evidence serializes its configuration but validators
derive identities from that value and never require Release. Receipt generation
and final qualification likewise ignore it. The dispatcher default is Debug;
only current workflow wiring happens to pass Release explicitly.

**Reproduction**: Exact authorities accepted a complete 2/2 Debug campaign,
minted a passed mutation receipt, and accepted that receipt in a ten-gate final
qualification with `status=passed`.

**Impact**: A default/manual mutation run or workflow drift can certify Debug-only
mutation kills for Release artifacts.

**Recommended fix**: Require exact Release configuration in catalog validation,
receipt minting, and final qualification, and bind the expected configuration in
receipt semantics. Test fully valid Debug rejection at all three layers and
explicit Release success.

### 682. [CONFIRMED] BannedSymbols omits eight nullable-aware SymbolDisplay overloads

**Location**: `BannedSymbols.txt`, around lines 48-53; policy catalog in
`SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, around lines 75-79
and 179-187.

**Description**: The inventory bans reduced ISymbol display APIs but omits all
public CSharp `SymbolDisplay` overloads taking `ITypeSymbol` plus
`NullableFlowState` or `NullableAnnotation` across ToDisplayString,
ToDisplayParts, ToMinimalDisplayString, and ToMinimalDisplayParts.

**Reproduction**: The real BannedApi analyzer emitted RS0030 for ordinary
ISymbol.ToDisplayString and AddSyntaxTrees controls but zero diagnostics for all
eight exact candidates. Adding their static documentation IDs produced exactly
eight warnings, one per call.

**Impact**: Production projects outside the Meta-analyzer subset can use nullable-
aware presentation strings as semantic identities while the repo-wide banned-API
layer stays green.

**Recommended fix**: Add the eight exact
`Microsoft.CodeAnalysis.CSharp.SymbolDisplay.*` documentation IDs. Add a compile
fixture/inventory test that enumerates every public pinned-Roslyn overload in the
four policy families, plus lookalike and warnings-as-errors controls.

### 683. [CONFIRMED] SARIF treats colon-bearing relative source paths as URI schemes

**Location**: `SharpProof.Worker.Launcher/SarifProjection.cs`, around lines
172-255; source-location validation in
`SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, around lines 839-844.

**Description**: `LocationUri` first tries absolute generic URI parsing. A legal
relative Linux/compiler-mapped path such as `generated:Subject.cs` is therefore
interpreted as an absolute `generated:` scheme, bypassing relative segment
escaping even though the SARIF location carries `uriBaseId=PROJECTROOT`.

**Reproduction**: A protocol-valid completed response emitted
`uri=generated:Subject.cs`; resolving it against `file:///tmp/project/` still
returned the custom scheme. The expected project file was
`file:///tmp/project/generated%3ASubject.cs`.

**Impact**: Editor/code-scanning navigation, artifact correlation, and baselining
can fail or split for claim, incomplete-callable, witness, and notification
locations sharing this helper.

**Recommended fix**: Classify filesystem paths before URI construction: recognize
Unix-rooted, Windows drive-rooted, and supported UNC forms as absolute file paths;
otherwise always escape relative segments. Represent intentional non-file URIs
explicitly. Test first/nested colons and Unix/Windows absolute controls.

### 684. [CONFIRMED] IrPrinter exponentially expands tiny shared DAGs until OOM

**Location**: `SharpProof.Ir/IrPrinter.cs`, around lines 5-26;
`IrPrinterProjections.generated.cs`, around lines 24-29; consumers include
`RequiresCallSiteAnalyzer.cs`, around lines 537-545, and `FuzzRunner.cs`, around
lines 309-329.

**Description**: The printer guards only maximum depth, then recursively expands
both child references as if the hash-consed DAG were a tree. A small repeated-
child DAG can have shallow depth but exponential rendered length; no output/node/
character budget exists.

**Reproduction**: Repeatedly setting `term = AndAlso(term, term)` produced only 24
unique nodes and depth 24. Printing under a 256 MiB heap cap threw
OutOfMemoryException after about 3.96 GB cumulative allocations. Depth 21 already
rendered 8.39 MB and allocated 470 MB.

**Impact**: Valid compact IR can replace expected diagnostics/fuzz evidence with
OOM despite being far below the advertised depth limit.

**Recommended fix**: Compute expanded length iteratively with saturating arithmetic
or format into a capped builder, returning an attributable bounded result before
large allocation. Preserve canonical text below the cap. Test shared-DAG boundary,
linear-depth guard, low allocations, and analyzer/fuzz graceful degradation.

### 685. [CONFIRMED] A transient symbol push failure makes release publication non-retryable

**Location**: `scripts/Publish-SharpProofRelease.ps1`, around lines 690-727,
883-945, and 982-1009; `scripts/SharpProof.PublicationDestination.ps1`, around
lines 359-389.

**Description**: Publication pushes each main nupkg before its snupkg. If the
symbol push fails transiently, a retry sees the already-published main, and
preflight throws on every HTTP 200 without comparing bytes. Although the planner
computes main/symbol actions, the execution loop ignores them.

**Reproduction**: Exact preflight returned Absent initially. The inspected loop
proved main-before-symbol ordering and no action use or skip-duplicate behavior.
On retry, byte-identical present-main state was rejected as
`Remote main package already exists`, before reaching the missing symbol.

**Impact**: One ordinary transient feed failure can leave an immutable public
version permanently missing symbols with no supported automated repair.

**Recommended fix**: Download/compare present main bytes, classify exact identity
as ExactPresent/Skip and mismatch as Collision, and make execution honor both
planned actions so missing symbols retry independently. Test main success plus
symbol failure then successful retry, collision, fresh publish, and complete no-op.

### 686. [CONFIRMED] Unsupported IR cast pairs become false differential mismatches

**Location**: `SharpProof.Ir/IrTermServices.cs`, around lines 206-214;
`SharpProof.Testing/IrCSharpDifferentialOracle.cs`, around lines 55-68, 255-265,
and 302-318.

**Description**: IrFactory accepts broad nullable/reference-like cast pairs, but
the differential renderer emits every pair as a C# explicit cast without checking
the common interpreter/C# conversion subset. A generated compiler error is then
classified as Mismatch.

**Reproduction**: A valid IR `Cast(string, Variable(long[]))` with benign sequence
data returned interpreter `UnsupportedCast`; the oracle generated `(string)long[]`,
hit CS0030, and reported Mismatch rather than Abstained.

**Impact**: The public differential oracle produces false reds for factory-valid
terms where neither implementation semantically disagrees.

**Recommended fix**: Preflight cast source/target pairs and abstain unless both
the interpreter and C# implement the conversion. Do not blanket-abstain all
compiler errors. Test sequence/string directions, supported object-to-string
values/null/failure, and folded identity/null cases.

### 687. [CONFIRMED] Finite-SMT term generation materializes unused environments

**Location**: `SharpProof.Testing/WellSortedIrGenerator.cs`, around lines 77-121;
`Tools/SharpProof.Fuzz/FuzzRunner.cs`, around lines 526-559.

**Description**: The finite-SMT candidate loop needs only `generated.Term`, but
`NextArithmeticOrBoolean` always creates a full GeneratedIrCase, six concrete
bindings, a dictionary, and unrelated random values before returning the term.

**Reproduction**: 100,000 fixed-term calls materialized 600,000 bindings and
allocated 123,775,944 bytes, about 1,238 bytes per discarded case. Replaying the
term-only path with the same seed changed the next term because discarded
environment draws also consume the shared RNG stream.

**Impact**: Up to roughly 79 KB per 64-attempt case is irrelevant allocation, and
environment refactors perturb formula coverage for the same seed.

**Recommended fix**: Split term generation from environment materialization and
use independent/versioned deterministic PRNG streams. Test no binding allocations,
stable term fingerprints across environment-only changes, and full-case controls.

### 688. [CONFIRMED] Seven CSharp speculative-binding APIs bypass both enforcement layers

**Location**: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, around
lines 51-66; `BannedSymbols.txt`, around lines 18-20.

**Description**: GetSpeculativeSymbolInfo, GetSpeculativeTypeInfo, and
GetSpeculativeAliasInfo are catalogued under SemanticModel/internal
CSharpSemanticModel, while all seven public C# overloads bind to static
`Microsoft.CodeAnalysis.CSharp.CSharpExtensions`. The banned IDs likewise use
nonmatching reduced-looking SemanticModel signatures.

**Reproduction**: Exact Roslyn operation binding showed all seven calls with
ContainingType=CSharpExtensions and `ReducedFrom=null`. A GetDiagnostics control
emitted SPMETA001/RS0030; all seven candidates emitted neither. Adding the seven
exact static documentation IDs produced exactly seven RS0030 warnings.

**Impact**: Analyzer-attached code can perform speculative expression, cref,
attribute, constructor-initializer, primary-base, type, and alias binding while
both claimed compile-time safeguards remain green.

**Recommended fix**: Add all three method families to the CSharpExtensions
SPMETA catalog and replace/add the seven exact static BannedSymbols IDs. Generate
inventory coverage from pinned Roslyn symbols. Test extension/static syntax,
TryGet controls, lookalikes, deletion, and RS0030-as-error behavior.

### 689. [CONFIRMED] Inline method-group delegate calls omit the exact target contract

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 44-75 and 591-624; `RequiresCallSiteTreeAnalyzer.cs`, around lines 35-64;
`RequiresCallSiteAnalyzer.cs`, around lines 302-337.

**Description**: For `((Action<int>)Positive)(-1)`, discovery records only
`Action<int>.Invoke`. It ignores the child DelegateCreation/MethodReference that
statically identifies `Positive`, so potential-owner screening drops the caller
and the target's Requires is never bound.

**Reproduction**: Roslyn exposed exact `MethodReference=Fixture.Positive(int)`;
the candidate remained `Action<int>.Invoke`, potential owners excluded the caller,
and no SP0027 appeared. Runtime invoked Positive with -1. A direct-call control
produced one replayable candidate and SP0027.

**Impact**: A definite precondition violation disappears solely because the
exact method group is invoked through an inline delegate conversion.

**Recommended fix**: When DelegateInvoke's instance is an inline delegate creation
with an exact static method reference, add a second target and map arguments by
ordinal/ref-kind. Preserve ordinary DelegateInvoke and fail closed for locals,
parameters, multicast, lambdas, and uncertain closed-instance receivers. Test
positive/negative/direct/unknown/null-receiver controls.

### 690. [CONFIRMED] Custom interpolated-handler calls are discovered but never replayed

**Location**: `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`, around
lines 178-208, 425-471, and 538-624; `RequiresCallSiteAnalyzer.cs`, around lines
339-342.

**Description**: Compiler-generated handler construction, AppendLiteral, and
AppendFormatted calls are discovered with exact targets, arguments, and Complete
flow, but exact-span replay ownership rejects every nested protocol call.

**Reproduction**: `Consume($"literal{1}")` executed handler ctor, literal append,
and formatted append once each. All three false Requires candidates had
`CanReplay=False HasFlow=True Status=Complete` and emitted nothing; direct controls
emitted three SP0027 diagnostics.

**Impact**: Contracts on custom logging/interpolation handlers are silently
unenforced at their primary compiler-generated call sites.

**Recommended fix**: Add handler-protocol-aware ordering: admit ungated ctor and
append calls only after proving all earlier phases complete, while failing closed
for out-bool construction gates and bool-returning short-circuit append methods
unless their branches are proven. Test throwing/short-circuit/order, alignment,
format overloads, and direct controls.

### 691. [CONFIRMED] Redundant user Assume eclipses authoritative source-domain evidence

**Location**: `SharpProof.Worker/CallableEvidenceBuilder.cs`, around lines 26-76
and 153-160; `PostconditionObligationBuilder.cs`, around lines 14 and 51-55;
`CallableClaimResultAssembler.cs`, around lines 39-70.

**Description**: User clauses are interned before source-domain predicates.
Predicate-ID dedup keeps the first evidence row regardless of provenance, so an
identical user Assume removes stronger compiler-derived range evidence and is
then marked Used.

**Reproduction**: A narrow integral source-domain proof had core
`domain:parameter:0` and no user row. Adding an identical range Assume changed
the same theorem to core `assume:0` with the user row `Used=true`; both used one
SMT query and proved.

**Impact**: Public proof provenance falsely claims dependence on user evidence
when language type semantics alone establish the result.

**Recommended fix**: Make predicate dedup provenance-aware and prefer source
LoweredJustification over identical UserAssumedJustification, retaining the user
declaration as unused. Test redundant/nonredundant/mixed evidence controls.

### 692. [CONFIRMED] Plan output inside PackageSource invalidates the certified bundle

**Location**: `scripts/SharpProof.PublicationPlanTopology.ps1`, around lines
46-176; `Publish-SharpProofRelease.ps1`, around lines 841-850 and 963-970;
`SharpProof.ReleaseChecksums.ps1`, around lines 95-187.

**Description**: Topology rejects reserved names and aliases of existing inputs,
but permits a new ordinary PlanOutputPath inside PackageSource. Its filtered
snapshot ignores that JSON file, while the release-bundle authority requires
exactly nine top-level files.

**Reproduction**: A valid nine-file bundle accepted and wrote
`PackageSource/publication-plan.json`, returned success, then contained ten files
and immediately failed exact-bundle validation.

**Impact**: A natural plan-only output location self-invalidates qualified release
bytes until manually removed.

**Recommended fix**: Reject any canonically resolved plan output contained under
PackageSource or the remote fixture directory before writing. Test new paths,
case/symlink-resolved containment, disjoint output, and bundle preservation.

### 693. [CONFIRMED] Discarded supported non-void calls become UnsupportedBody

**Location**: `SharpProof.Frontend/RoslynProgramLowerer.cs`, around lines 142-152
and 300-305; `CompilerCallableLowerer.cs`, around lines 202, 274, and 304;
`AcyclicBlockPredicateExecutor.cs`, around lines 165-167.

**Description**: Invocation statements pass `wantsResult=false`, so non-void calls
receive no target variable. Compiler preparation requires a target for source
summaries and API-spec calls and rejects the otherwise exact body.

**Reproduction**: Discarded `Identity(value);` and `Math.Abs(value);` calls each
made preparation fail `UnsupportedBody`. Assigning the unused result made both
prepare successfully with one summary/spec call. Runtime results were identical.

**Impact**: Ordinary statement-call syntax downgrades verifiable bodies to
Unknown and prevents reusable summaries.

**Recommended fix**: Allocate a sink temporary for every supported non-void call;
omit targets only for void/unsupported returns. Test source-summary, API-spec,
worker outcome parity, and void controls.

### 694. [CONFIRMED] Async Task result allocation is omitted from complete effects

**Location**: `SharpProof.Effects/EffectMethodNodeBuilder.cs`, around lines 22-102.

**Description**: Source/CFG scanning never models compiler-generated async method
builder/result allocation. An async Task/Task<T> method with no explicit creation
can therefore remain Complete with Allocation=None.

**Reproduction**: Sixty-four calls to `async Task<int>` returning noncached 1729
allocated 4,608 bytes and returned distinct tasks; Effects reported None/Complete
and a complete no-allocation projection. A non-async Task identity control
allocated zero and correctly stayed None.

**Impact**: ZeroAllocations/purity contracts can accept ordinary async methods
that allocate fresh result objects.

**Recommended fix**: Add method-level async lowering effects for canonical Task/
Task<T>, conservatively Managed unless a proven cache-only case applies. Cover
suspension, cached constants, ValueTask/custom task-like, async void, and witnesses.

### 695. [CONFIRMED] Direct capturing local functions falsely report heap allocation

**Location**: `SharpProof.Effects/OperationEffectScanner.cs`, around lines 20-29
and 102-153; `ConversionEffectClassifier.cs`, around lines 70-75.

**Description**: Any captured symbol/receiver unconditionally adds Managed
allocation, conflating captured-state tracking with delegate/closure
materialization. A local function invoked directly can be stack-lowered with no
heap object; actual delegate conversion already has separate allocation logic.

**Reproduction**: 256 direct calls to a capturing local function allocated exactly
zero bytes, but summary was Managed/Complete with Allocates. Returning the local
function as Func allocated 5,632 bytes and correctly remained Managed.

**Impact**: Complete summaries reject allocation-free code and produce false
Allocates contracts.

**Recommended fix**: Retain captured read/write regions but charge allocation only
at actual anonymous-function/method-group materialization. Correct the stale
direct-call test and add receiver/parameter plus escaping controls.

### 696. [CONFIRMED] SP0048 points at the first assumption of any kind

**Location**: `SharpProof.Worker.Launcher/Program.cs`, around lines 553-592;
`SarifProjection.cs`, around lines 43-54.

**Description**: Policy triggering counts only UserAssume/TrustedBoundary, but
location selection chooses the first callable with any assumption. Launcher and
SARIF independently repeat the broad predicate.

**Reproduction**: In a fully valid response, an earlier callable contained only
a Precondition and a later callable contained the used UserAssume. Both console
SP0048 and SARIF notification pointed to `precondition.cs` instead of `user.cs`.

**Impact**: Diagnostics navigate to unrelated code while reporting user/trusted
evidence policy failures.

**Recommended fix**: Centralize selection of the first callable/result containing
UserAssume or TrustedBoundary only. Test earlier preconditions, both policy kinds,
console structure, and SARIF URI.

### 697. [CONFIRMED] Filesystem-root projects produce malformed SARIF base URIs

**Location**: `SharpProof.Worker.Launcher/SarifProjection.cs`, around lines
107-114 and 172-234.

**Description**: ProjectRootUri unconditionally appends a directory separator.
For `/`, it creates `//` and serializes `file:////`; relative artifact locations
then resolve as URI authorities/hosts.

**Reproduction**: With canonical project directory `/`, relative `user.cs`
resolved from emitted `file:////` to `file://user.cs/` instead of
`file:///user.cs`. Non-root already-terminated paths also gain a duplicate slash.

**Impact**: SARIF navigation, artifact correlation, and baselining break for
valid root-located projects.

**Recommended fix**: Append a separator only when `Path.EndsInDirectorySeparator`
is false, preserving root exactly. Parameterize root, terminated, and unterminated
project paths with resolved-artifact assertions.

### 698. [CONFIRMED] Banned-symbol inventory test uses overlapping substring matches

**Location**: `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs`, around
lines 168-201; example exact ID in `BannedSymbols.txt`, around line 13.

**Description**: The architecture test treats BannedSymbols as arbitrary text and
checks broad substrings. Sibling type/overload entries can satisfy a deleted exact
documentation ID even though BannedApi matching is exact.

**Reproduction**: Deleting only
`Compilation.GetSemanticModel(SyntaxTree,bool)` left the inventory test passing.
A direct compiler probe then built clean; restoring that line produced RS0030.

**Impact**: Exact enforcement can disappear while the claimed inventory backstop
remains green, particularly in BannedApi-only production projects.

**Recommended fix**: Parse each noncomment line's exact documentation ID into a
set, reject malformed/duplicate rows, and assert fully qualified IDs. Mutation-
delete every overload independently and retain compiler probes.

### 699. [CONFIRMED] Release needs test accepts commented-out dependencies

**Location**: `SharpProof.ArchitectureTest/ReleaseCoverageBaselineTests.cs`,
around lines 12-39; workflow dependency block in
`.github/workflows/package-consumers.yml`, around lines 197-208.

**Description**: The test searches the entire YAML text for dependency strings
instead of parsing `jobs.release-qualification.needs`. Comments or unrelated job
text satisfy it.

**Reproduction**: Replacing active `- security` with `#      - security` left the
test passing, while scoped parsing showed security absent from active needs. A
job-scoped assertion failed the mutation and passed after restoration.

**Impact**: Required qualification jobs can be disabled without the architecture
backstop noticing, allowing publication jobs to bypass the omitted dependency.

**Recommended fix**: Parse YAML and require the exact needs set, plus separate
publisher-to-qualification dependencies. Test comment, move, misspell, duplicate,
removal, and valid reorderings.

### 700. [CONFIRMED] IrStructuralShrinker expands shared DAG occurrences exponentially

**Location**: `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs`, around lines
430-559.

**Description**: Candidate generation creates a fresh local seen set on every
recursive call and traverses shared children once per occurrence. Duplicate
filtering happens only after repeated recursive rebuilding; cancellation cannot
interrupt GetCandidates.

**Reproduction**: Shared `Add(term,term)` DAGs with 9/13/17 unique nodes returned
only three candidates but allocated 1.26/20.13/322.13 MB. Every four added unique
nodes multiplied allocation by 16.

**Impact**: Minimization can consume hundreds of MB or more on tiny valid terms
and fail before emitting fuzz evidence.

**Recommended fix**: Memoize candidate arrays by IrId for the full traversal (or
visit unique DAG nodes/parent edges once), preserve deterministic order, and
thread cancellation through enumeration. Add shared-depth allocation/visit-count
and cancellation tests.

### 701. [CONFIRMED] Generated frontend model admits unsupported sequence equality

**Location**: `Tools/SharpProof.Fuzz/FrontendFuzzing.cs`, around lines 329-362
and 1078-1086; `SharpProof.Frontend/RoslynOperationLowerer.cs`, around lines
694-712.

**Description**: GeneratedCSharpExpression accepts Equal/NotEqual over Sequence,
but the frontend scalar subset rejects C# array equality as UnsupportedType. The
oracle labels any non-exact lowering Mismatch.

**Reproduction**: Benign `(values == values)` over `long[]` returned Mismatch with
`Generated supported C# closed the frontend subset: UnsupportedType`; an
otherwise same reference-equality control agreed.

**Impact**: Public generator/test infrastructure false-reds model-valid cases and
future sequence-coverage expansion.

**Recommended fix**: Remove Sequence from supported equality operands unless
array identity is implemented end-to-end, or return explicit Abstained. Add a
model-vs-lowerer support matrix and integer/string/reference controls.

### 702. [CONFIRMED] SPMETA008 misses EffectSummary record with-expressions

**Location**: `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, around
lines 120 and 190-221; `SharpProof.Effects/EffectSummary.cs`.

**Description**: Construction enforcement registers only ObjectCreation and casts
to `IObjectCreationOperation`. C# record cloning is `IWithOperation`, so
`source with { }` creates a distinct EffectSummary outside the authority allowlist
without SPMETA008.

**Reproduction**: Direct `new` emitted one SPMETA008. A synthetic record and the
real repository EffectSummary both compiled a With operation with zero
diagnostics; runtime confirmed a distinct but equal instance.

**Impact**: Consumers bypass the stated construction/identity boundary, and any
future init member would also bypass constructor validation.

**Recommended fix**: Register OperationKind.With, compare its type/operand to the
known EffectSummary symbol, and apply the identical containing-type allowlist.
Test rejected consumers, allowed EffectSummary/operations authorities, and direct
new preservation.

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
