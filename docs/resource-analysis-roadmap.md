# Resource analysis: full implementation roadmap

The user expanded this branch beyond milestone one on 2026-10-18. This document
tracks the full requested scope. The completed first milestone remains documented
in [its checkpoint](resource-bounds-progress.md); completing that milestone does
not complete this roadmap. The merge request stays draft while required phases
remain incomplete.

The checklist and first checkpoint entry describe the current state. Older
checkpoint entries retain their earlier statuses, versions and verification
results as implementation history.

Every feature must have a stated cost model, compiler-bound admission rules,
sound upper bounds, explicit Unknown outcomes at unsupported boundaries, tests
against independently observed executions, and end-to-end diagnostic/reporting
coverage. Neither samples nor diagnostic silence establish a universal proof.

## A. Loop and expression bounds

- [x] Canonical increasing unit-step loops, independent nesting, cumulative arrays.
- [x] Constant positive non-unit strides with explicit overflow checks.
- [x] Descending loops with checked progress and lower bounds.
- [x] Multiplicative/divisive progress and logarithmic iteration bounds.
- [x] Dependent nested loop bounds and conservative counter-size environments.
- [x] Broader ranking-function synthesis with checked certificates, including
  protected two-counter phases.
- [x] Regression tests for boundary integers, loops that do not terminate, early exit,
  mutation, exceptional exits, and independently counted iteration/allocation costs.

## B. Calls and recursive costs

- [x] Source-call summaries with argument substitution, dispatch admission,
  receiver initialization rules, and shared analysis budgets.
- [x] Reviewed metadata cost summaries with assumption provenance (Int32
  Math.Min/Max, .NET 8.0.32, immutable source commit and catalog digest).
- [x] Recursive progress proofs and recurrence bounds for admitted families
  (positively guarded, decrement/division, static void tail recursion).
- [x] Explicit Unknown for unproved progress, opaque targets and unsupported
  recurrences; verify actual caller/callee workflows.

## C. Quantitative memory and stack

- [x] Object, string, closure, boxing, and array allocation costs.
- [x] Byte bounds under an explicit, versioned runtime layout model.
- [x] Alias/escape and lifetime reasoning; distinguish cumulative allocation,
  peak live auxiliary memory, and retained/returned output.
- [x] Stack frame and recursive stack bounds under a declared model.
- [x] Independent allocation/liveness controls, mixed-size objects, aliases,
  escaping values, and runtime-layout incompatibility tests.

## D. Contracts and native verification

- [x] Resource contract API and compiler-bound validation/selection.
- [x] Compiler-neutral proof/cost representation and collector artifact encoding.
- [x] Native-worker derivation and checking of cumulative resource upper bounds.
- [x] Exact-prefix numeric violations for array allocations and allocated elements.
- [x] Exact source-loop-entry numeric violation evidence.
- [x] Exact typed-array byte-prefix refutations with owned layout evidence.
- [x] Bind native metadata cost assumptions and their provenance.
- [x] Protocol, manifest, schema and cache-identity integration.
- [x] SARIF/result projection with independent claim outcomes and Unknown reasons.
- [x] Reject malformed/unowned proof records; verify packaged consumer behavior.

## E. Asymptotic and aggregate analysis

- [x] Upper/lower asymptotic expressions and tightness only with both bounds.
- [x] Amortized costs through checked potential/accounting rules.
- [x] Average-case bounds conditional on an explicit validated input model;
  never invent a probability distribution from source structure.
- [x] Adversarial controls that separate loose upper bounds from tight/expected
  results, and report all assumptions in consumer evidence.

## F. Async, concurrency and performance measurements

- [x] Async/state-machine storage for the admitted normal-completion task-join
  model, with conditional peak/retained byte proofs.
- [x] Joined source work and array/element allocation prefixes, including the
  params array and arbitrary isolated child overlap, under stated runtime scope.
- [ ] Cumulative task runtime-byte prefixes, including fault/exception storage;
  keep Unknown until the complete declared cost model is established.
- [x] End-to-end runtime/allocation benchmark workflow, with input scales,
  units, environmental identity and comparable baselines. Measurements must
  remain distinguishable from proofs and must not establish Big-O by themselves.
- [x] Cancellation, exceptional completion, shared-state and repeated-entry controls
  for the admitted task model; unsupported execution shapes remain Unknown.

The cumulative-byte item remains open until all of these obligations are joined:

- [x] Private fixed task carriers, fault records and notification components.
- [x] Source-owned primitive fault candidates and fixed source-exception instances.
- [x] Private compiler-owned source activation bounds.
- [ ] Checked CLR/helper-frame bridge.
- [ ] Checked exception escape/catch boundaries for every admitted root path.
- [ ] Cumulative trace growth, retry, thread-affinity copy and dispatch-copy costs.
- [ ] Referenced payloads, messages, scheduler faults and initialization costs.
- [ ] Root composition with bound runtime premises, evidence identities and
  consumer-level proof/Unknown controls. Private components do not satisfy this.

## Delivery and verification

- [x] Preserve the original dirty checkout using an isolated branch/worktree.
- [x] First milestone: 1184 analyzer tests plus docs/layout/container gates passed.
- [x] Open and attach the draft merge request for this branch.
- [ ] Keep this document current after each verified implementation phase.
- [ ] Run focused checks before the relevant broader gate; record exact pushed
  commit and CI status after each delivery checkpoint.
- [ ] Final requirement-by-requirement audit of phases A-F and the actual
  consumer workflow, with no unchecked required items, before marking complete.

## Current checkpoint

### Independent memory extraction guards

Five additional memory-program controls cover incomplete/unsupported tails,
malformed field-member metadata and extraction's own event budget. Static and
method-shaped field stores are first rejected by the typed builder, leaving
its admitted allocation prefix intact. Deliberately malformed instructions
using owned IDs then verify extraction's separate rejection. Incomplete,
unsupported and over-budget extraction inputs return Unknown without partial
memory bounds even when valid allocations precede the rejection.

Published `64125e57e` passed complete canonical CI (39812478216) and package
consumers (39812478160). The retained coverage report still fails the unchanged
100% floor; these passes do not qualify the newer local commits.

All 108 memory-program cases passed on the formatted final source with zero
build warnings or errors
(`artifacts/resource-memory-program-boundaries-tests-verified.log`). The first
probe expected evaluation to receive invalid stores; it instead confirmed the
builder's earlier validation. The corrected controls exercise both layers.
The canonical formatter updated the new action block before the passing run
(`artifacts/resource-memory-program-boundaries-format.log`). No production
admission rule or cost model changed. Task runtime-byte obligations remain open.

Full quality/API checks passed
(`artifacts/resource-memory-program-boundaries-lint.log`), as did documentation
checks (`artifacts/resource-memory-program-boundaries-docs.log`). The final
canonical private formatter copied zero source changes
(`artifacts/resource-memory-program-boundaries-format-verified.log`).

### Carrier shape admission and throwable-construction audit

Additional collector controls cover primitive field widths in generated class
and struct carriers, ignored static storage, unsupported generic/inherited/layout
shapes, shadow generated attributes, the exact field-count boundary, long
nested metadata names, enclosing-symbol ownership and cancellation without a
partial field inventory. These probes use real Roslyn symbols and the actual
collector helpers; production admission rules remain unchanged.

All 263 carrier tests passed on the final source, with zero build warnings or
errors (`artifacts/resource-carrier-shape-tests-final.log`), including fourteen
new shape controls. Full quality/API checks passed
(`artifacts/resource-carrier-shape-lint.log`), as did documentation checks
(`artifacts/resource-carrier-shape-docs.log`). The canonical private formatter
copied zero source changes (`artifacts/resource-carrier-shape-format-final.log`).

Pinned source review reached the throwable-construction guard in
[`CLRException::GetThrowable`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/clrex.cpp#L90).
For `EEException`,
[`IsSameInstanceType`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/clrex.h#L217)
compares native exception type and managed exception kind. Failed construction
can delegate to `GetThrowableFromException`; the guard is not an explicit
numeric limit on all helper/constructor attempts. Constructor and resource
paths still require separate accounting before root task-byte composition.
The exception headers have verified blob identities, byte counts and SHA-256
hashes in the ignored research manifest.

The automatic report for published `64125e57e` measures 6837/7007 changed trusted
lines (97.57%, 170 gaps), failing the unchanged 100% minimum. Seven previous
image-write and length-budget guard lines are now covered. The complete report
for merge `b3570a3c4356eb956dd05480de191839ff58a8a5` is retained at
`artifacts/resource-64125-coverage-evidence/coverage/SharpProof.coverage.json`
(run 39812477832). This report predates the current carrier-shape controls.

Initial GitHub listings returned no latest-head runs; later reads exposed the
automatic PR runs. Four duplicate manual test/security dispatches were cancelled
and the completed manual docs run was left terminal. The automatic PR runs are
the qualification evidence; cancelled duplicates are not passes.

### Compiler image serialization budget controls

New controls invoke the collector's actual private image stream through byte,
array, span, async-array and async-memory writes. Each API must reject a write
past the 16 MiB serialized-image cap without extending the image. A separate
control permits the last byte, rejects length growth and a crossing array
write without altering the admitted prefix, then permits shrinking to zero.

The suspected bypass through newer overloads was ruled out by pinned source review:
[`MemoryStream.Write(ReadOnlySpan<byte>)`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/IO/MemoryStream.cs#L636)
preserves derived-type behavior by delegating through the legacy write path.
The async-memory overload likewise routes through array or span writes. The
source identity, byte count and SHA-256 are recorded in the ignored runtime
research manifest. The production stream implementation remains unchanged.

All 249 carrier cases passed with zero build warnings or errors
(`artifacts/resource-image-stream-tests-verified.log`), including the six new
image-budget controls. Full quality/API checks passed
(`artifacts/resource-image-stream-lint.log`), as did documentation checks
(`artifacts/resource-image-stream-docs.log`). The final canonical private
formatter copied zero source changes (`artifacts/resource-image-stream-format-final.log`).

Published `59501c3bc` passed complete canonical CI (39806832622) and package
consumers (39806832161). All fifteen coverage shards passed in run 39806832032.
Aggregation measured 6830/7007 changed trusted lines (97.47%, 177 gaps), failing
the unchanged 100% minimum. Fourteen previous gaps closed in raw object
initializer admission, field-value lowering and initializer completion. The
complete report for merge `09cd2c9feb202b8ee89191b8065caa2ebeb23ee3` is retained at
`artifacts/resource-59501-coverage-evidence/coverage/SharpProof.coverage.json`.
This report predates the image-budget controls. Cumulative task runtime-byte
proof obligations remain open.

### Object initializer lifetime boundaries and measured coverage

Additional native lifetime controls exercise field initialization, constructor
assignment and the final object initializer in their C# execution order. They
also cover an initializer value returned by a direct source call. Each admitted
case checks both the exact finite memory bound and a one-byte-lower refutation.
Ordinary property, indexer and valid nested-member initializers have positive
controls. Separate raw constructions inside field initializers exercise the
property/indexer rejection boundary; a null nested-member target also requires
Unknown. Rejections retain no partial inferred/observed bound or metadata
evidence, despite an earlier array allocation in the same method.

All 674 native resource-contract cases passed on the corrected final source,
with zero build warnings or errors (`artifacts/resource-object-initializer-tests-final.log`).
Full quality/API checks passed (`artifacts/resource-object-initializer-lint-final.log`),
as did documentation checks (`artifacts/resource-object-initializer-docs-final.log`).
The canonical private formatter copied zero source changes
(`artifacts/resource-object-initializer-format-final.log`). No production
admission rule or cost model changed.

Published `836d0d13b` passed complete canonical CI (39801977550), package consumers (39801975090), and completed
all fifteen coverage shards in run 39801975063.
Aggregation measured 6816/7007 changed trusted lines (97.27%, 191 gaps), failing
the unchanged 100% minimum. Seven previous gaps closed in Boolean case handling
and metadata-assumption merging. The complete report for merge
`980bdf220c89ff46eb8527630c61e8c046b4e38b` is retained at
`artifacts/resource-836d-coverage-evidence/coverage/SharpProof.coverage.json`.
This report predates the local warmed-message and initializer controls.

### Default exception messages and resource initialization

The pinned runtime controls now exercise all seven reviewed ordinary exception
families with both explicit preexisting messages and parameterless constructors.
The default-constructor controls warm the message lookup before measurement,
check that repeated instances share the message reference, then count actual
managed allocation for 128 constructions. All seven fit the existing fixed
instance bounds after warmup. Constructors are measured without throwing;
stack traces and native helper message replacement remain separate costs.

All 241 Specs cases passed in the canonical Linux amd64 container on
.NET 8.0.32 x64, with zero build warnings or errors
(`artifacts/resource-default-exception-messages-tests-final.log`). The intentional
parameterless argument-exception cases have a two-expression `CA2208`
suppression because those exact overloads supply the default messages under
measurement. The canonical private formatter copied zero source changes
(`artifacts/resource-default-exception-messages-format.log`).

Source review of the pinned
[CoreLib resource helper](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/SR.cs#L15)
found a static lock object and a loading list. A cold lookup explicitly runs
class constructors for `ResourceManager`, `ResourceReader`, `RuntimeResourceSet`
and `BinaryReader`, then retrieves the resource. The
[shared helper](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/Common/src/System/SR.cs#L28)
can bypass lookup with the resource-key switch, but that does not remove the
CoreLib static initialization obligation. Both files have verified immutable
blob identities, downloaded byte counts and SHA-256 hashes in the ignored
research manifest (`CoreLibSR.cs` and `CommonSR.cs` distinguish their local names).

These observations establish warmed constructor controls. Cold lookup,
initialization, specialized replacement strings and failure/retry paths still
need a declared upper bound before cumulative task bytes can be proved. The
runtime-byte roadmap item remains open, and its root results remain Unknown.

Full quality/API checks passed (`artifacts/resource-default-exception-messages-lint.log`),
and documentation checks passed (`artifacts/resource-default-exception-messages-docs.log`).

### Boolean case meaning and metadata provenance

Additional compiler-to-native controls exercise Boolean negation, explicit
Boolean conversion, literal conditions and a separate Boolean input without a
declared distribution. They compare both case bounds and the rounded weighted
bound against independently executed allocation counts. The separate flag
retains the maximum branch cost in both modeled cases; no distribution is
invented for it. A companion control requires dependencies from both reviewed
`Math.Min`/`Math.Max` branches to remain in the native proof evidence and be
marked as used.

All 663 native resource-contract cases passed on the final source with zero
build warnings or errors (`artifacts/resource-boolean-branches-tests-verified.log`).
The earlier metadata assertion failed `CA1861`; a shared readonly witness array
resolved that test-only finding. Full quality/API checks passed
(`artifacts/resource-boolean-branches-lint-final.log`), and the final canonical
private formatter copied zero source changes
(`artifacts/resource-boolean-branches-format-final.log`).

Published `0fb889b1b` passed complete canonical CI (39798309137) and completed
all fifteen coverage shards in run 39798308654.
Aggregation measured 6809/7007 changed trusted lines (97.17%, 198 gaps), failing
the unchanged 100% minimum. Compared with `2ddc058d8`, coverage closed the
external-write lifetime rejection, child-isolation rejection and child-cost
ceiling-division substitution lines. The complete report for merge
`80e1a6835a241c1cf38c5806a1f5e02ff1dd2a1a` is retained at
`artifacts/resource-0fb889-coverage-evidence/coverage/SharpProof.coverage.json`.
This report predates the current Boolean controls and does not qualify them.

The exception-message audit reached
[`EEException::CreateThrowable`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/clrex.cpp#L1002).
It allocates the exception, invokes its default constructor, and can then
allocate a replacement managed message string when `GetThrowableMessage`
supplies specialized text. The existing fixed-instance component covers
neither the constructor's resource initialization nor that replacement string.
Both remain explicit obligations before root byte composition. The source's
immutable blob identity, byte count and SHA-256 are retained in the ignored
research manifest. No runtime-byte admission changed.

The retry of package run 39792643381 (attempt 2) passed for published
`2ddc058d8`, resolving its archive TLS timeout. This predecessor consumer pass
does not qualify the subsequent head or close the coverage requirement.

### Lifetime replay rejects external writes after allocation

Four execution controls allocate and return an owned primitive array, with a
write between allocation and return. A local write preserves the exact 32-byte
cumulative and retained bounds, with zero auxiliary bytes because returned
output is excluded from auxiliary storage. Static, parameter and unknown-region
writes each return `MemoryReplayInstructionUnsupported` with no partial bounds.

All 55 memory-replay tests passed in the canonical Linux amd64 container, with
zero build warnings or errors
(`artifacts/resource-memory-replay-writes-tests-final.log`). The initial test
signature used an internal enum in a public test method and failed compilation;
the corrected string-valued test cases passed. No production admission or cost
model changed in this checkpoint.
Full quality/API checks passed (`artifacts/resource-memory-replay-writes-lint.log`);
the canonical private formatter copied zero source changes
(`artifacts/resource-memory-replay-writes-format.log`).

### Native isolation and rounded child-cost composition

Three new native controls retain an ordinary first child and replace the later
child with a static, parameter or unknown-region write. Every source-work,
storage, source-activation and callback/source-activation result must discard
partial bounds and return `ForkJoinChildIsolationUnproved`, without publishing
an activation model. These controls exercise native admission independently of
the compiler's rejection of shared writes. Two additional source-loop cases
check ceiling-division and ceiling-log substitution across parameter and
constant child arguments.

All 107 native fork/join tests passed with zero build warnings or errors in the
canonical Linux amd64 container (`artifacts/resource-child-isolation-tests.log`).
The canonical private formatter copied zero changes
(`artifacts/resource-child-isolation-format.log`).
Full quality/API checks passed (`artifacts/resource-child-isolation-lint.log`).

The complete coverage artifact for published `2ddc058d8` is retained at
`artifacts/resource-2ddc-coverage-evidence/coverage/SharpProof.coverage.json`.
All fifteen shards passed in run 39792642948. The report measures merge
`bbdb4f593150f387bc3b16db526013d77bad7a48`: 6806/7007 changed trusted lines,
97.13%, with 201 uncovered lines against the unchanged 100% minimum. The
direct primitive helper-frame component has no uncovered changed lines. This
report predates the local array-token and isolation test checkpoints; it does
not establish their resulting coverage or complete the coverage requirement.

Package run 39792643381 failed before consumer execution on a TLS handshake
timeout downloading the checksum-pinned Ubuntu `xz-utils` package. The failed
log is retained in `artifacts/resource-2ddc-package-failed.log`; only failed
jobs were rerun. This infrastructure failure does not count as a consumer pass.

### Array metadata rejection and unwind/delegate source audit

Additional emitted-PE controls replace the `newarr` element token with an
invalid token kind, an absent or nil type-reference row, a method-definition
token, and an assembly-reference token. These inputs must discard the source
body evidence and return an explicit Unknown reason. They exercise the parser
and metadata exception boundary as well as the element-type admission rule;
none supplies a physical-frame or root allocation bound.

All 243 task-carrier tests passed in the canonical Linux amd64 container,
including the five new token controls, with zero build warnings or errors:
`artifacts/resource-array-token-tests.log`. Documentation checks passed:
`artifacts/resource-array-token-docs.log`. Full quality/API checks passed:
`artifacts/resource-array-token-lint.log`; the canonical private formatter
copied zero source changes (`artifacts/resource-array-token-format.log`).
Hosted qualification of this
checkpoint and the unchanged 100% changed-TCB coverage floor remain required.

The pinned runtime audit also established the following narrower facts:

- [`LowerDelegateInvoke`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/jit/lower.cpp#L4909)
  loads the delegate instance and first target for the indirect call.
- The reviewed open-delegate constructor selects a shuffle thunk; the reviewed
  closed-instance path stores the target pointer directly in
  [`comdelegate.cpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/comdelegate.cpp#L1656).
  The AMD64 shuffle emitter finishes with a jump to the target in
  [`stublinkerx86.cpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/i386/stublinkerx86.cpp#L3513),
  included by the AMD64 source. This does not admit multicast, virtual,
  wrapper, collectible or arbitrary externally supplied delegates.
- [`genOSRRecordTier0CalleeSavedRegistersAndFrame`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/jit/codegenxarch.cpp#L9681)
  emits unwind records accounting for inherited Tier0 storage and the fake
  call slot. The non-x86
  [`UnwindStackFrame`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/eetwain.cpp#L4367)
  delegates to `Thread::VirtualUnwindCallFrame`. These facts alone do not
  establish a bound on repeated first-pass trace appends.

All seven newly inspected source files have immutable blob identities,
matching downloaded byte counts and SHA-256 hashes in the ignored research
manifest `artifacts/resource-runtime-fault-review/sources.json`. The complete
CLR/helper bridge, escape/rethrow accounting and root byte composition remain
unchecked above. No new runtime model or root verdict is published here.

### Direct primitive helper-frame contribution

The private model `coreclr8-direct-primitive-hcall-trace-frames-v1` now admits
only the reviewed divide-by-zero, overflow, null-reference, range-check and
`JIT_NewArr1` worker entries. Their direct explicit helper frames contribute
zero trace elements: `HCIMPL` clears the `FCall` identity, the helper frame's
method-descriptor lookup maps null to null, and the VM explicit-frame trace
path appends only frames with a non-null descriptor. The implementation records
the immutable source links for that chain. Entries without review and incompatible
runtime layouts return Unknown without a frame bound or model; cancellation
propagates.

Tests cover the closed entry set, unknown kinds, layout/cancellation, and raw VM
traces from actual primitive faults in the pinned runtime. The independent
runtime controls disable inlining/optimization and allow only the source and
catch frames; they do not rely on formatted stack traces hiding helper frames.
All 234 Specs cases passed with zero build warnings or errors, including all
five primitive-fault raw-trace controls:
`artifacts/resource-hcall-frames-specs-final.log`. Full quality/API and
documentation checks passed: `artifacts/resource-hcall-frames-lint-final.log`
and `artifacts/resource-hcall-frames-docs.log`. The final canonical private
formatter copied zero changes:
`artifacts/resource-hcall-frames-format-verified.log`. Exact-head hosted
qualification of this direct-helper checkpoint remains required.

This component does not count transitive managed calls, all possible JIT
implementations of an opcode, tiered/inlining/`OSR` frames or unwind/rethrow
visits. Those obligations remain open before any attempted-frame budget or
root byte bound can be derived. No protocol/cache change or root verdict change.

Pinned helper selection is in `src/coreclr/inc/jithelpers.h` and
`src/coreclr/vm/jitinterface.cpp`. `OSR` frame review starts in
`src/coreclr/jit/codegencommon.cpp`; `src/coreclr/jit/osr.cpp` is absent at this
revision and its failed-fetch payload is not registered as a source. Successful
downloads are byte-count/hash checked in
`artifacts/resource-runtime-fault-review/sources.json`.

Published `4456993d7` passed complete canonical CI (39787702379), package consumers
(39787702403), documentation and security. All fifteen coverage shards passed
in run 39787702098; aggregation measured 6794/6995 changed trusted lines
(97.13%, 201 uncovered), failing the unchanged 100% floor. The new source-body
codec and native checker have no uncovered changed lines. Complete evidence for
merge `cae2639348cf4e295ba268fd0163e79cd68579bc`:
`artifacts/resource-445699-coverage-evidence/coverage/SharpProof.coverage.json`.
This report does not measure the local direct-helper component.

### Emitted source-body inventory for the frame bridge

A private collector now inspects each distinct source-child method from the
same cached emitted module as the checked callback forwarders. It retains the
root/module identity and complete sorted method-token inventory only when every
body is call-free primitive integer/Int32-array IL. Array element references
must bind to the actual framework assembly identity. Calls, constructors,
fields, casts/unboxing, handlers, unsupported element types, decoding failures and
oversized bodies return Unknown without a partial inventory. Rejection of this
inventory does not erase the independent callback-forwarding fact.

Controls cover Debug/Release and configured/synchronous roots, array loops,
checked arithmetic, repeated callbacks sharing one source method, cache identity,
unsupported operations, partial-inventory rejection, cancellation, foreign
framework identities and malformed bodies. The collector phase passed all 887
selected worker/parser/artifact/resource-contract cases with zero build warnings
or errors: `artifacts/resource-source-body-workflows.log`.

This fact inspects a trusted compiler emission rather than independently
verifying IL stack typing. The shared artifact now transports it in nested
fork/join projection version 6, with cache version 58 invalidating prior
responses. Protocol 43, compiler artifact 55 and manifest 6 are unchanged.
Native checking binds the root/module identity and requires exactly the distinct
forwarded child-token set, without invalid/duplicate/missing/extra tokens.
Successful admission establishes one directly entered source body per forwarded
invocation, excluding helpers, initialization and external reentry. Missing or
unsupported evidence yields Unknown without a bound or model identity. All 1044
transport/protocol/cache/parser/artifact/resource-contract cases passed with zero
build warnings or errors:
`artifacts/resource-source-body-transport-workflows-verified.log`. Reference-pack
array allocation checks the actual collector attachment and native admission;
array stores remain rejected by the existing shared-effects admission rule.
Both cases preserve Unknown cumulative runtime bytes rather than inventing a
root bound.

Full quality/API, documentation, container contract and layout checks passed:
`artifacts/resource-source-body-lint-final.log`,
`artifacts/resource-source-body-docs-final.log`,
`artifacts/resource-source-body-contract.log` and
`artifacts/resource-source-body-layout.log`. Canonical private formatting was
applied before the passing workflow run; the final pass copied zero changes:
`artifacts/resource-source-body-format-committable.log`. Exact-head hosted
qualification of this source-body checkpoint remains required.

The reviewed CLR/helper/unwind bridge remains required. This fact asserts no
bound on runtime helper frames or trace bytes and changes no root verdict. The
phase F checklist remains open.

The next helper-frame audit has pinned copies of `frames.h`, `frames.cpp`,
`fcall.h`, `ecall.cpp` and `jithelpers.cpp` at runtime revision
`b9a49e4a6d3a444a3685e318c74a7892d01873f7`, with Git blob, byte-count and SHA-256
checks in `artifacts/resource-runtime-fault-review/sources.json`. `HCall` helpers
clear their `FCall` identity; the VM's explicit-frame trace path appends only
frames with a non-null method descriptor. This is source evidence to check
against every admitted opcode's helper path, not a complete numeric bridge.
JIT/inlining/`OSR` behavior, first-pass visits and dispatch rethrows still need
checking before deriving the total attempted-frame budget.

Published `ed7a5ff42` passed complete canonical CI (39740654146), package consumers
(39740654194), documentation and security. All fifteen coverage shards passed
in run 39740653952; aggregation measured 6667/6866 changed trusted lines
(97.10%, 199 uncovered) and failed the unchanged 100% floor. Evidence for merge
`c490983b97db2099b44c12bb3bafd414d20d2e01`:
`artifacts/resource-ed7a5-coverage-evidence/coverage/SharpProof.coverage.json`.
These results supersede the earlier published checkpoint below and do not
measure the local source-body inventory changes.

### Latest master integration and complete published evidence

The branch includes master `fe2a05235` after the MR developed conflicts
with its CI/tooling changes. The merged workflows retain master's complete
fifteen-shard coverage and four package partitions, including authenticated
per-attempt evidence, plus this branch's Docker termination records. Package
container and telemetry artifact names now include the partition to avoid
collisions. The canonical package resource envelope and cold preparation fix
are preserved.

The xz-utils pin keeps revision `5.6.1+really5.4.5-1ubuntu0.4` and adopts master's
immutable archive installation. A canonical-container download from Ubuntu's
archive verified package identity/version/amd64 and SHA-256
`18ef3c30308990626cfbda8b6336ef6ef5a412d1991bdad0cdaa226fd2041ffe`:
`artifacts/resource-xz-archive-probe.log`. The tooling image rebuilt successfully:
`artifacts/resource-master-merge-tooling-build.log`. Merged layout, container
contract, documentation and full quality/API checks passed:
`artifacts/resource-master-merge-layout.log`,
`artifacts/resource-master-merge-contract.log`,
`artifacts/resource-master-merge-docs-final.log` and
`artifacts/resource-master-merge-lint-final.log`. Full formatting passed:
`artifacts/resource-master-merge-format-check.log`. The final private formatting
pass after the callback validation fix copied zero changes:
`artifacts/resource-callback-malformed-format-final.log`. All 868 affected
worker/parser/artifact/resource-contract cases passed with zero build warnings
or errors: `artifacts/resource-master-merge-worker-tests-final.log`.

Published `bbe9896a7` completed every coverage shard in run 39575056512. Its
complete report for merge `64e9238229d53055e34170e362245e7bf18ef66d` measures
6661/6864 changed trusted lines (97.04%, 203 uncovered), failing the unchanged
100% floor. The symbolic trace catalog has no uncovered changed lines. Evidence:
`artifacts/resource-bbe989-coverage-evidence/coverage/SharpProof.coverage.json`.
Canonical run 39575056665 passed main builds, self-application, quality and
package reproducibility; its corpus gate failed on worker method timeouts.
Package consumers, documentation and security also passed. The failure logs are
`artifacts/resource-bbe989-canonical-failure.log` and
`artifacts/resource-bbe989-coverage-failure.log`. The previously failing
`GateCountsAndRejectsAnUnreviewedSilentUnknown` passed in isolation at the
original budgets (one case, 37 seconds, zero build warnings/errors):
`artifacts/resource-corpus-timeout-repro-final.log`. This does not establish the
hosted timeout's cause. Master's isolated canonical corpus cohort was checked
in Release using the unchanged canonical category filter. All 14 cases,
including all five previously failing workflows, passed in 2 minutes 47 seconds
with zero build warnings/errors:
`artifacts/resource-master-merge-corpus-gate-final.log`. The retained-evidence
step then refused this local run because it requires an actual GitHub run,
attempt and checked-out SHA. No identity was fabricated; hosted cohort
authentication remains pending.
Published `77dc4cc7e` adds the previously
verified native contract rejection controls; master conflicts prevented its
pull-request workflows from starting.

Real-PE mutation controls exposed an unhandled parser exception in the callback
body validator: a truncated opcode prefix escaped instead of clearing its fact.
The shared parser intentionally preserves malformed-bytecode exceptions, so
the fix belongs at the callback boundary. That boundary now converts malformed
image and checked branch-target overflow failures to
`TaskCallbackForwarderBodyUnsupported`, with no forwarding target. Cancellation
still propagates. Controls cover truncation, overflow, an undefined opcode and
a missing return; the latter cases also discard an already recognized owned
call. Final verification includes the parser's existing exception/cursor
contract and normal artifact and resource-contract workflows. No accepted
evidence, schema or root verdict is widened. Exact-head hosted qualification
of the merged checkpoint remains required. No root trace-byte proof is
claimed; phase F remains open.

### Native contract rejection controls

Additional native contract controls exercise a sealed declaration marked
UnsupportedContract and a missing root activation for both source depth and
source stack bytes. A zero-cost resource program cannot substitute for a missing
activation certificate. Both direct checking and asynchronous replay keep the
result Unknown without a numeric bound or witness. These controls change no
production semantics. All 655 resource-contract workflow cases passed with zero
build warnings or errors: `artifacts/resource-contract-rejections-tests-final.log`.
Full quality/API passed on retry after an external Debian license link refused
the first connection: `artifacts/resource-contract-rejections-lint-retry.log`.
Documentation checks passed and private formatting copied zero changes:
`artifacts/resource-contract-rejections-docs.log` and
`artifacts/resource-contract-rejections-format.log`.

Published `bbe9896a7` contains callback transport/native composition and symbolic
trace-buffer composition. Its documentation and security workflows passed; five
coverage shards passed, with package coverage and complete canonical/package
qualification still running at this checkpoint. The additional rejection tests
remain local while that published run completes, preserving its exact-head
evidence. They have not yet been measured by hosted coverage.

### Symbolic trace-buffer composition

The private trace catalog now composes its raw and resolver array bounds using
nonnegative resource expressions. For upper bounds F on all appended frames,
R on raw copies, D on resolver copies and K on initial resolver capacity, it
constructs `(2F + R)(56 + 64F) + (F + D)(24 + 8 max(K, 4F))`. The inputs retain
their separate identities; neither copy count is inferred from the other.
Arithmetic uses mathematical integers and the existing expression budget.
Budget exhaustion and incompatible runtime layouts publish Unknown with no
partial bound or model identity. Cancellation propagates.

This component requires the same closed-collection premises as its two
constituent models. Its inputs need separate ownership and runtime proofs.
Logical callback depth is not an appended-frame bound. Referents, helper faults,
payloads, initialization and the root byte composition remain open, as do the
unchecked phase F obligations above. No root verdict or protocol changes.

All 221 Specs cases passed with zero build warnings or errors, including symbolic
inputs, independent copy counts, large arithmetic, budget/layout failures,
cancellation and measured pinned runtime throw/restore controls:
`artifacts/resource-trace-composition-specs.log`. Full quality/API and
documentation checks passed: `artifacts/resource-trace-composition-lint.log`
and `artifacts/resource-trace-composition-docs.log`. The private formatter
normalized the test source before the passing test run.

Published `f87ff40bc` also completed canonical CI run 39570412886 successfully,
including self-application and package reproducibility. Its latest complete
coverage remains 97.02% with 201 gaps; the callback transport and symbolic
composition changes are not measured by that report. Exact-head qualification
of the next published checkpoint remains required.

### Callback activation transport and native composition

The nested fork/join projection is version 5 and the response cache is version
57. Selected storage metrics now carry callback activation evidence alongside
the emitted root boundary. The codec preserves absent and Unknown evidence and
rejects malformed fields, identities, counts and method-definition tokens.
Wrapper tokens must be distinct and disjoint from child targets; the number of
distinct targets cannot exceed the owned source-child count.

Native checking reuses source/carrier/layout admission before accepting the
callback fact. Failed checks expose neither a wrapper bound nor its model. A
separate private composition adds the checked forwarding activation to logical
source depth, preserving Unknown on missing ownership, layout, source depth or
evidence. This is a logical source-plus-forwarder bound, not a CLR helper-frame
or exception trace-byte bound. It changes no cumulative root-byte verdict.

Controls cover Debug/Release, synchronous/configured roots, real codec round
trips, malformed and old payloads, absent/Unknown evidence, native identity
checks, source composition and cancellation. Artifact workflow controls check
the actual collector attachment. All 327 first-pass targeted carrier, protocol
and artifact-selection cases passed with zero build warnings or errors:
`artifacts/resource-callback-native-tests.log`. Full quality/API, documentation
and container contract checks passed:
`artifacts/resource-callback-native-lint.log`,
`artifacts/resource-callback-native-docs.log` and
`artifacts/resource-callback-native-contract.log`. The final private formatting
pass copied zero changes: `artifacts/resource-callback-native-format-final.log`.
The final set includes target-cardinality and missing-source-activation controls
and the broader resource-contract/projection workflows. All 1074 cases passed
with zero build warnings or errors:
`artifacts/resource-callback-native-workflows.log`.

Published `f87ff40bc` completed all six coverage shards and aggregation in run
39570413127 after the xz-utils pin repair. The complete report for merge
`2615914abf7e6b2486eeecb240c3b067a731d54e` measured 6537/6738 changed trusted
lines (97.02%, 201 uncovered), failing the unchanged 100% floor. This supersedes
the 96.93% report below. Evidence:
`artifacts/resource-f87ff-coverage-evidence/coverage/SharpProof.coverage.json`.
The callback transport/native composition changes are local and unmeasured by
that report.

### Refreshing the available Ubuntu tooling pin

The canonical Dockerfile now pins xz-utils to the available Ubuntu revision
`5.6.1+really5.4.5-1ubuntu0.4`. Published coverage aggregation had failed because
the former `...ubuntu0.3` revision was no longer in the signed noble package
indexes. A canonical-container probe fetched the current indexes into temporary
APT state and found revision 0.4 in noble-updates and noble-security:
`artifacts/resource-xz-package-probe-final.log`.

The canonical tooling image rebuilt successfully and its install log confirms
revision 0.4: `artifacts/resource-xz-tooling-build.log`. The tooling doctor and
contract checks passed: `artifacts/resource-xz-tooling-doctor.log` and
`artifacts/resource-xz-tooling-contract.log`. Full quality/API and documentation
checks passed: `artifacts/resource-xz-tooling-lint.log` and
`artifacts/resource-xz-tooling-docs.log`. Qualification of the next published
head remains pending. This repairs the
image-preparation failure; coverage still needs to reach the unchanged 100% floor.

### Emitted callback forwarding activations

A collector-only fact now checks generated callback forwarding bodies against
the emitted method tokens of the root plan's owned source children. Admission
requires no handlers, no backward branches, one owned direct call and a final
return. Only argument/constant/field/local loads and stores, no-ops and forward
branches are admitted around that call. Calls to other targets, multiple calls,
allocations, handlers and unsupported bodies return Unknown without an activation
record. The fact retains root/module identity and every checked wrapper/child
method-token pair from the same cached emission.

The upper bound is one wrapper activation per entered forwarding call, excluding
its callee. Source-child activations, indirect reentry, runtime helpers and trace
frame enumeration remain separate obligations. The fact is not transported or
consumed by native root composition yet and changes no public verdict or cache
identity. It does not complete the CLR/helper-frame bridge.

Controls cover Debug/Release and synchronous/configured roots, repeated capture
with the same module identity, constant operands, named argument order, target
ownership, unsupported bodies and cancellation. The first build caught a
constant instance-property style error; the bound now derives zero/one from the
forwarder set. IL opcode names were aligned with the decoder's ECMA names before
the corrected targeted run. All 180 targeted carrier/parser cases passed with
zero build warnings or errors: `artifacts/resource-callback-activation-tests-final.log`.
Full quality/API and documentation checks passed:
`artifacts/resource-callback-activation-lint.log` and
`artifacts/resource-callback-activation-docs.log`. Canonical private formatting
copied zero changes: `artifacts/resource-callback-activation-format-final.log`.
All 754 broader resource-contract and projection cases passed with zero build
warnings or errors: `artifacts/resource-callback-activation-workflows.log`.

Published `e3a21d9a7` completed all six coverage shards in run 39565332287, but
aggregation failed while rebuilding the package-preparation image: the pinned
xz-utils `5.6.1+really5.4.5-1ubuntu0.3` was absent from the current Ubuntu indexes.
No aggregate report was produced. Evidence: `artifacts/resource-e3a21-coverage-failure.log`.
A canonical-container probe using temporary APT state found
`5.6.1+really5.4.5-1ubuntu0.4` in noble-updates and noble-security:
`artifacts/resource-xz-package-probe-final.log`. Repair and qualification of the
pin remain pending. The last complete coverage report remains 96.93% with 204
gaps; this failed aggregation does not supersede it.
Published `e3a21d9a7` passed complete canonical CI (run 39565332413) and
package-consumer qualification (run 39565332532).

### Direct emitted-boundary rejection controls

The emitted-body checks now have a shared internal validator. Capture still
binds the real emitted image, state-machine method and callable before creating
a boundary record; the validator returns only a rejection reason or success for
body facts. It does not establish module ownership, independent IL validity,
physical frame counts or a root byte bound. Join counts outside the native
domain of two through sixteen children return Unknown.

New controls compile real method bodies and check rejection of missing/finally
handlers, wrong framework catches, local method-definition calls, indirect
calls, unprotected allocations/throws/joins, incomplete joins, generic awaiter
shapes, dense switch instructions and oversized bodies. A separate control
corrupts an emitted call token to exercise malformed metadata handling. Existing
Debug/Release emission and artifact workflow tests continue through the shared
production validator. Cancellation is checked directly. The first targeted run
passed 163 of 164 cases; the indirect-call fixture was rejected during initial
test compilation because its unsafe option was applied after the helper's
diagnostic check. Fixture creation now passes the option directly and the
targeted set then passed all 164 cases with zero build warnings or errors:
`artifacts/resource-boundary-body-tests-final.log`. All 652 broader resource-contract
workflow cases passed with zero build warnings or errors:
`artifacts/resource-boundary-body-workflows.log`. Full quality/API, documentation and canonical
private formatting checks passed:
`artifacts/resource-boundary-body-lint.log`,
`artifacts/resource-boundary-body-docs.log` and
`artifacts/resource-boundary-body-format.log` (zero changes).
No coverage improvement is claimed before a complete report.

Published `2b78ed310` completed all six coverage shards in run 39562323764.
Its aggregate for merge `38e48ccefe79434328f27ab1f15970271c62e5ed` measured
6441/6645 changed trusted lines (96.93%, 204 uncovered), failing the unchanged
100% floor. This supersedes the 96.91% report below. The malformed block
inventory rejection and fresh source-fault tracker additions have no uncovered
changed lines; the previously uncovered missing-block rejection is now covered.
Evidence: `artifacts/resource-2b78-coverage-evidence/coverage/SharpProof.coverage.json`.
The direct body-validator work is local and is not measured by that report.
Published `2b78ed310` also passed complete canonical CI (run 39562323885) and
package-consumer qualification (run 39562324011).

### Memory control-flow inventory rejection

The last complete changed-TCB report left the malformed-control-flow rejection
in ResourceMemoryProgram uncovered. New controls construct an IR container with
an omitted entry or successor block through its existing constructor. They
verify Unknown and no partial storage result, including a missing successor
after an allocation has already been traversed. All 211 resource-memory cases
and all 859 IR cases passed with zero build warnings or errors:
`artifacts/resource-memory-block-inventory-fixed-final.log` and
`artifacts/resource-memory-block-inventory-ir-all.log`.
Full quality/API, documentation and private-workspace formatting checks passed:
`artifacts/resource-memory-block-inventory-lint.log`,
`artifacts/resource-memory-block-inventory-docs.log` and
`artifacts/resource-memory-block-inventory-format.log` (zero formatted changes).
All 652 affected compiler/native worker cases passed with zero build warnings
or errors: `artifacts/resource-memory-block-inventory-worker.log`.
The audit also reproduced exceptions for duplicate/null block
inventories and a verified result for foreign-scope inventories. Evaluation now
checks entry scope, block scope, duplicates and null blocks before traversal,
returning MalformedMemoryControlFlow with no partial bound. Reproduction:
`artifacts/resource-memory-block-inventory-repro-all.log`. The first fix used
Dictionary.TryAdd, which is unavailable to this netstandard target; it now uses
the compatible ContainsKey/Add operations on its private dictionary.
No overall coverage improvement is claimed until a complete
coverage report measures the updated head.

Two candidate field-store probes were rejected by the IR builder before reaching
memory analysis. Those probes were removed; they do not establish coverage of
the later memory guard.

The published coverage campaign for `f7aa2bb6c` completed all six shards in run
39558045780. The aggregate for merge `25fc402e077d7c8a562604e0cd299280f5d930b0`
measured 6430/6635 changed trusted lines (96.91%, 205 uncovered), failing the
unchanged 100% floor. The raw/resolver buffer catalog, carrier transport decoder
and native carrier verifier additions have no uncovered changed lines in that
report. Evidence: `artifacts/resource-f7aa-coverage-evidence/coverage/SharpProof.coverage.json`.
It supersedes the older complete 97.06% report below. The tracker-entry and
inventory changes still need qualification on their next published head.
Published `f7aa2bb6c` also passed complete canonical CI (run 39558045979) and
package-consumer qualification (run 39558045983).

### Fresh source-fault tracker entry counters

The native source-exception check now exposes the resolver entry-counter bound
for its admitted fresh ordinary source faults under an explicit closed-runtime
tracker model. A nonempty source-fault candidate set has upper seed 30; an empty
set has seed zero. Failed source ownership, handling and layout checks expose
neither the seed nor its model. This fact excludes scheduler/helper faults and
imported exception traces and does not supply a physical frame budget.

The pinned tracker dispatch creates an ExceptionTracker for new, nested and
rethrown dispatches using its constructor, including the emergency allocation
path. Both constructors call StackTraceInfo.Init, resetting the counter to zero.
The initial native frame-buffer allocation seeds the resolver counter with 30
in retail builds, 2 in debug builds, or zero on allocation failure. Continuing
saves and restored arrays remain covered by the separate resolver prefix model;
30 is an entry seed rather than a bound on later counter values.

The additional source is pinned `exceptionhandling.h`, blob
`d8eb19e6ef208a9c9303fe24a85dd0a031461110`, SHA-256
`ad5f915202c6a006fe1bb57bf3538dfaa3b602909cf7b1a51d12f00ddd9779cd`.
The reviewed `exceptionhandling.cpp` SHA-256 is
`dcc92f01ddc25607057ed773148478a675c84894affdc7d0b2534047a02ad843`.
Both are from runtime commit `b9a49e4a6d3a444a3685e318c74a7892d01873f7`.

Native controls check supported exception families, an empty source profile,
unsupported handling and incompatible layouts. A runtime control creates an
ambient dynamic trace with more than 30 resolver slots, restores and catches it,
then throws a fresh dynamic exception inside that handler. It checks the fresh
array seed and measures only its raw/resolver allocation interval. All 216 Specs
tests passed with zero build warnings or errors:
`artifacts/resource-source-tracker-seed-specs.log`. Full quality/API and
documentation checks passed: `artifacts/resource-source-tracker-seed-lint.log`
and `artifacts/resource-source-tracker-seed-docs.log`. The canonical private
formatter copied zero changes: `artifacts/resource-source-tracker-seed-format.log`.
The native test build identified a missing Specs namespace import in the new
assertions; it was fixed and all 102 native projection cases passed with zero
build warnings or errors: `artifacts/resource-source-tracker-seed-worker-final.log`.
Frame-budget derivation, remaining runtime fault origins and
complete cumulative root bytes are still open.

### Closed resolver-array prefixes

A separate private component now bounds resolver `object[]` storage for a closed
trace collection. It requires proved upper bounds on attempted appended frames
`F`, resolver capture/restore copies `D`, and every participating StackTraceInfo's
entry `m_cDynamicMethodItems` counter `K`. The last input is explicit because
ClearStackTrace preserves the counter. It is not inferred from logical source
activations, native frame-buffer capacity or a surviving trace.

Pinned SaveStackTrace either retains the counter or doubles a dynamic batch for
a replacement. For extensions it recounts the whole stored trace, adds the new
dynamic batch and can double that sum. Both counts are at most `F`, bounding
every resolver-array capacity by `max(K, 4 * F)`. Each nonempty save allocates at
most one resolver array, including abandoned allocations, and each dispatch copy
allocates at most one more. The cumulative upper bound is therefore
`(F + D) * (24 + 8 * max(K, 4 * F))` under the pinned x64 layout. BigInteger
arithmetic prevents overflow. Invalid budgets and incompatible layouts return
Unknown with no bound or model identity; cancellation propagates.

The first 51 targeted trace tests passed, including dynamic-method traces with
actual resolver arrays and repeated dispatch restores:
`artifacts/resource-resolver-prefix-tests.log`. New controls preserve a large
entry counter even with few frames, exercise negative budgets and arithmetic
beyond Int64, and check measured trace allocations against both buffer
components. All 215 tests in the complete Specs project passed with zero build
warnings or errors: `artifacts/resource-resolver-prefix-specs.log`. Full
quality/API and documentation checks passed:
`artifacts/resource-resolver-prefix-lint.log` and
`artifacts/resource-resolver-prefix-docs.log`. The canonical private formatter
copied zero source changes: `artifacts/resource-resolver-prefix-format.log`.

This changes no root verdict or response identity. The runtime must still prove
the frame/copy/counter budgets and account for referenced resolver and loader
objects, initialization, helper failures, payloads and root composition. The
cumulative runtime-byte checklist remains open.

### Closed raw-trace buffer prefixes

A private component now bounds cumulative raw `byte[]` trace storage for a
closed collection that starts without traces and copies only traces belonging
to that collection. Its inputs are upper bounds on all attempted appended
frames `F` (including failed saves, resets and retries) and capture/restore raw
array copies `D`. It does not derive these inputs from source activations.

In the pinned x64 runtime, a trace element uses 32 bytes and its embedded header
uses 16. Growth only doubles when the old capacity is below the required
capacity, giving a maximum payload capacity of `2 * (16 + 32 * F)`. An affinity
copy preserves capacity. Each nonempty save attempts at most one append; each
append can allocate an affinity copy and a growth array. Therefore there are
at most `2 * F + D` raw arrays, each at most `56 + 64 * F` managed bytes under
the selected layout. The cumulative upper bound is their product. BigInteger
arithmetic prevents overflow; negative budgets and incompatible layouts return
Unknown without a bound or model identity. Cancellation propagates.

The source rules come from pinned `object.cpp` (StackTraceArray append, growth
and copy) and `excep.cpp` (SaveStackTrace), commit
`b9a49e4a6d3a444a3685e318c74a7892d01873f7`. Their SHA-256 digests are
`d323c06c064b314f6f81f2ec215e035d51ee6040d614968eea5fe3d81dd3e813` and
`721bfdaff7bfbb52fa99cc3d1ec2bbf4521997f5df49c6aa5e2fbe0804bbcac5`.
The failure cleanup clears references rather than allocating a replacement raw
array. This bounds all raw-array allocations across a supplied prefix budget,
including abandoned allocations; a surviving buffer's size alone would not.

The first 37 focused tests passed, including fresh closed throws with measured
allocation intervals excluding exception construction, reflection and setup:
`artifacts/resource-raw-trace-prefix-tests.log`. All 49 targeted trace and
fault-notification cases passed, including cross-thread dispatch with a changed
buffer owner and allocation of both restore and affinity copies:
`artifacts/resource-raw-trace-prefix-runtime-final.log`. The complete Specs
project passed all 202 cases with zero build warnings or errors:
`artifacts/resource-raw-trace-prefix-specs-all.log`. Full quality/API and
documentation checks passed: `artifacts/resource-raw-trace-prefix-lint.log` and
`artifacts/resource-raw-trace-prefix-docs.log`. The first broader test build
reported formatting in the new thread initializer; the canonical private
formatter corrected it before the passing runs:
`artifacts/resource-raw-trace-prefix-format.log`.

Resolver arrays remain separate: native StackTraceInfo scratch capacity seeds
their allocation and survives ClearStackTrace. Native scratch storage, physical
frame-budget derivation, imported traces, helper failures, payloads and root
composition are also unproved. This component changes no root verdict, protocol
or cache identity. The cumulative runtime-byte checklist remains open.

### Transporting async-root boundary evidence

The nested fork/join projection is now version 4 and the response cache is
version 56. Carrier records transport the emitted root boundary or its explicit
Unknown reason. The decoder checks exact properties, bounded ranges, method
tokens and root/module/state identities. The native carrier verifier separately
checks ownership against admitted source costs and the selected runtime layout.
Missing or inconsistent evidence cannot become a successful boundary check.

Controls cover Debug/Release emission round trips, altered identities, malformed
ranges and payloads, old versions, absent evidence and Unknown preservation. An
artifact workflow control also checks that storage metrics carry the boundary
capture while loop-only metrics do not request it. All 276 focused carrier,
IL-parser, protocol and artifact-selection tests passed with zero build warnings
or errors. Evidence: `artifacts/resource-boundary-transport-tests-final.log`.
Documentation and full quality/API checks passed:
`artifacts/resource-boundary-transport-docs.log` and
`artifacts/resource-boundary-transport-lint.log`. Full formatting and container
contract checks passed: `artifacts/resource-boundary-transport-format-final.log`
and `artifacts/resource-boundary-transport-contract.log`. The first test build identified two
test-code analyzer errors, which were corrected before the passing run; a
formatting check also identified added switch statements, now corrected.
The first broader run passed 754 of 755 cases. Its duplicate-property fixture
still searched for projection version 3, so it did not mutate version 4 JSON.
The fixture now uses the owning version constant and checks that its target
property exists. All 879 affected resource-contract, projection, cache and carrier
workflow cases passed with zero build warnings or errors after this fix:
`artifacts/resource-boundary-transport-workflows-final.log`. All 124 final carrier
cases also passed separately: `artifacts/resource-boundary-transport-admission-tests.log`.
Additional boundary controls check incompatible layouts, unowned carriers and
cancellation. The final private-workspace formatting pass copied zero changes:
`artifacts/resource-boundary-transport-format-last.log`.

This is a collector-owned source fact, not independent IL replay or a physical
CLR-frame certificate. Runtime helper failures, cumulative trace growth and
copies, payloads, initialization and composition into root cumulative bytes
remain open. The full roadmap and unchanged coverage floor still apply.

Published `ab254500c` passed package-consumer CI (run 39551817889), including
Linux, macOS, Windows and CLI consumers, and complete canonical CI (run
39551817943). Coverage run 39551817887 passed five
shards but the package campaign exceeded its existing 1200-second limit. That
run has no complete aggregate; it does not replace the last complete 97.06%
report below. Evidence: `artifacts/resource-ab254-coverage-failure.log`.

### Emitted async-root exception boundaries

`CaptureExceptionBoundary` inspects the actual emitted async state-machine
`MoveNext` method, using the same module image/hash as carrier capture. Admission
requires one framework Exception catch, protected Task.Run/WhenAll/GetResult
sites and allocations, and one handler call that records the fault in the
framework async builder. Synchronous roots, unsupported emission/body shapes,
incomplete call sites and unverified handlers return Unknown without a boundary
record. The cached emitted image is retained under the existing 16 MiB bound.

The IL reader has a separate metadata-operand entry point; scalar provider
admission keeps its existing operand policy. New lexical controls check metadata
tokens and floating-point operand bits without treating byte arrays as CLR
program evidence. Emission controls cover Debug/Release, capture order/module
reuse, synchronous-root rejection and cancellation. All 121 carrier/parser cases
passed with zero build errors after strengthening awaiter framework identity and
rejecting indirect calls. Evidence:
`artifacts/resource-emitted-task-boundary-tests-2.log`.
Full quality/API and documentation checks passed. Evidence:
`artifacts/resource-emitted-task-boundary-lint.log` and
`artifacts/resource-emitted-task-boundary-docs-checkpoint.log`.
Full formatting passed before the final awaiter-identity tightening; its final
private-workspace whitespace check copied zero changes. Evidence:
`artifacts/resource-emitted-task-boundary-format.log` and
`artifacts/resource-emitted-task-boundary-apply-format-final.log`. Final
documentation checks also passed:
`artifacts/resource-emitted-task-boundary-docs-final.log`.
This checkpoint established compiler-side source-boundary evidence. The newer
checkpoint transports it, but does not compose a native CLR-frame/trace-byte
proof. It does not cover failures
in runtime helpers outside the source catch.

Published `6ef27aa7b` completed all six coverage shards in run 39549217069.
Its complete report measured 6233/6422 changed trusted lines (97.06%, 189
uncovered), failing the unchanged 100% floor. Task-only dispatch-copy changes
have no uncovered changed lines in that report. Evidence:
`artifacts/resource-6ef27-coverage-evidence/coverage/SharpProof.coverage.json`.

### Execution-context wrapper dispatch counts

The fault-carrier checker now keeps context-wrapper captures/restores separate
from the existing task-only model and supplies a combined operation count under
`task-join-context-dispatch-copy-counts-v1`. Each child can enter one context
wrapper; a configured root can resume its single await through one async-box
wrapper. Each wrapper can capture and rethrow one exception, so each context
operation is bounded by `children + 1` for configured roots or `children`
otherwise. Combined task/context dispatch copies are bounded by
`8 * children + 8` arrays for configured roots, or `8 * children` otherwise.
Failure results expose neither combined counts nor a combined model identity.

This still counts operations, not bytes. External context-change callback work,
trace growth/affinity copies, helper failures and initialization remain separate
obligations. The pinned Task, ExecutionContext and async-box implementation
sources establish the wrapper sites and capture/rethrow paths. Runtime controls
exercise normal/faulting public and thread-pool wrapper paths, checking captured
flow values, restored flow state and exception identity. All 190 Specs cases
and 96 native carrier cases passed with zero build errors. Evidence:
`artifacts/resource-context-dispatch-copy-runtime.log` and
`artifacts/resource-context-dispatch-copy-worker.log`.
The pinned async entry implementation calls the initial `MoveNext` directly,
restoring context in a finally block without an extra capture/rethrow wrapper.
Its source identity is recorded in
`artifacts/resource-runtime-fault-review/async-entry-context-source.json`.
Full formatting, quality/API and documentation checks passed. Evidence:
`artifacts/resource-context-dispatch-copy-format.log`,
`artifacts/resource-context-dispatch-copy-lint.log` and
`artifacts/resource-context-dispatch-copy-docs.log`.

Canonical CI passed completely on published `cfa23e7cb`, including package
reproducibility, in run 39545675384. Its complete coverage report remains below
the unchanged 100% floor at 97.07%. The verified source-activation and task-only
copy-count commits were published together at `6ef27aa7b`; that head needs its
own exact-commit CI and coverage qualification.

### Task-owned dispatch-copy operations

The fixed fault-carrier check now derives task-owned dispatch-operation counts
under `task-join-task-dispatch-copy-counts-v1`. The two fault-recording sites per
child give at most `2 * children` captures; a configured async root adds one
capture and one restore. Each capture/restore copies at most one trace byte array
and one resolver array, giving at most `4 * children + 4` copied arrays for a
configured root, or `4 * children` otherwise. Rejected ownership/layout/counts
retain no copy counts or model identity. Existing bookkeeping byte bounds and
their model remain unchanged.

These are operation counts, not a trace-byte bound. Save/growth retries,
thread-affinity copies, execution-context dispatches and runtime/helper work
remain separate obligations. Pinned TaskAwaiter and async-builder sources and
blob/SHA-256 provenance are cached in
`artifacts/resource-runtime-fault-review/task-dispatch-copy-sources.json`;
the existing holder/Task/CoreCLR source evidence supplies the remaining paths.
The pinned ExecutionContext source explicitly captures and rethrows callback
failures in both `RunInternal` and `RunFromThreadPoolDispatchLoop`. Those
additional operations must be included by root composition; the task-only
counts cannot stand in for them. Helper-frame source provenance is cached in
`artifacts/resource-runtime-fault-review/runtime-helper-frame-sources.json`.

Six independent runtime controls isolate capture or restore from the following
throw, measuring only the reviewed dispatch object and trace/resolver payload
copies. Reflection, preparation, warmup and cleanup throws stay outside allocation
intervals. All 186 Specs cases passed on .NET 8.0.32 x64. Evidence:
`artifacts/resource-task-dispatch-copy-runtime.log`. All 96 native carrier cases
passed with zero build errors, including rejection, layout and cancellation
controls: `artifacts/resource-task-dispatch-copy-worker.log`.
Full formatting and quality/API checks passed. Evidence:
`artifacts/resource-task-dispatch-copy-format.log` and
`artifacts/resource-task-dispatch-copy-lint.log`.
Final documentation checks passed in
`artifacts/resource-task-dispatch-copy-docs-final.log`.

### Source activations and trace catch boundaries

Six pinned-runtime controls distinguish a catch inside an owned call chain from
an exception escaping through ambient callers. With inlining and tail-call
elision disabled for the controls, the closed trace has `throwDepth + 2` frames,
independent of ambient depths zero, eight and 32. The escaping trace has
`ambientDepth + throwDepth + 3` frames. All 180 Specs cases passed on .NET 8.0.32
x64. Evidence: `artifacts/resource-trace-catch-boundary-specs-2.log`.
These observations are controls for the required compiler/runtime bridge, not a
universal proof or a caller-supplied frame cap.

`CheckSourceActivations` reuses source ownership checking and the native source
stack kernel, substitutes the checked callback argument mappings, and takes the
maximum callback source depth. Missing activation structure or unsupported
ownership returns Unknown with no partial depth/model. Its source-side model is
explicitly separate from physical CLR frames and trace-byte costs. All 102
projection/source-activation cases passed with zero build errors on the final
source, including argument substitution:
`artifacts/resource-owned-source-activation-worker-2.log`. The initial build
caught formatting diagnostics in a test record update; formatting was corrected.
Full quality/API checks passed in
`artifacts/resource-owned-source-activation-lint-final.log`.
Full formatting and final documentation checks passed. Evidence:
`artifacts/resource-owned-source-activation-format.log` and
`artifacts/resource-owned-source-activation-docs-final.log`.

Coverage for published `104e2e69b` failed before worker-a tests because the
container registry returned HTTP 503. Evidence:
`artifacts/resource-104-worker-a-failed.log`. The failed jobs in run 39543577096
were retried against the same head. Attempt two failed before tests when the
script-tool download received a Web Application Firewall response. Evidence:
`artifacts/resource-104-coverage-attempt2-failed.log`. No complete measurement is
claimed for that head; the tool source and integrity checks were not changed.

### Fixed source-exception instance storage

`NativeTaskSourceExceptionInstanceVerifier` adds a private conditional component
for fixed ordinary source-exception instances. It rechecks owned fault families,
requires compatible runtime layout and requires every explicit source throw to
target a callback exceptional exit. Caught/retried source throws return Unknown
with no numeric component or required model identity. Its model is
`task-join-source-exception-instances-v1`; it has no root proof authority.

The reviewed inherited Exception slots bound an instance by 136 bytes. The
ArgumentException `_paramName` slot raises this to 144; ArgumentOutOfRangeException
also has `_actualValue`, raising it to 152. The pinned runtime source cache and
blob/SHA-256 provenance for all seven families and their SystemException
and ArithmeticException bases, which add no instance fields, are recorded in
`artifacts/resource-runtime-fault-review/primitive-exception-sources.json`.
The checker conservatively uses the largest candidate for every callback and
permits at most one escaping ordinary source exception per at-most-once callback
invocation. Runtime invocation multiplicity is a separate premise. An empty
candidate set gives a zero source-instance component, not exception-free CLR
execution. Messages, referenced actual values, initialization, allocation/helper
failures, scheduler wrappers, traces and dispatch copies remain separate work.

All 98 projection/profile/instance worker cases passed with zero build errors:
`artifacts/resource-source-exception-instance-worker.log`. Independent
warmed-constructor allocation controls passed all ten runtime cases on .NET
8.0.32 x64 with zero build warnings/errors:
`artifacts/resource-source-exception-instance-runtime-3.log`. The initial runtime
test builds caught test-style and parameter-name diagnostics; those were fixed.
The reserved runtime exception construction controls intentionally suppress
CA2201 only on the two measured constructor expressions; they do not throw them.
The new production
file is registered in the acceptance contract's trusted paths. Phase F remains
open until complete declared-cost-model composition is established.
The broader Specs suite passed all 174 tests on .NET 8.0.32 x64:
`artifacts/resource-source-exception-instance-specs-all.log`.
Evaluated layout passed: 2851 source files, 59 projects and 151 pinned common
files. Full quality/API checks passed. Evidence:
`artifacts/resource-source-exception-instance-layout.log` and
`artifacts/resource-source-exception-instance-lint.log`.
Full C# formatting and final documentation checks passed. Evidence:
`artifacts/resource-source-exception-instance-format.log` and
`artifacts/resource-source-exception-instance-docs-final.log`.

### Construction-order coverage controls

Five new frontend controls compare compiled execution with exact IR replay for
field zero initialization, instance field initializers, constructor updates,
object-initializer overwrites and argument evaluation order. All 100 typed
source-call cases passed in `artifacts/resource-construction-order-coverage.log`.
Auto-property initializers normalize to field storage and are supported; that
case now compares compiled execution with exact replay. Three additional controls
require incomplete lowering for instance events, generic construction and split
partial declarations. All 104 typed source-call cases passed in
`artifacts/resource-construction-order-coverage-3.log`.

The first coverage attempt for `5dadd9e3b` passed five shards but failed to
prepare the semantic-projects image: its Linux Arm64 native-payload download
ended prematurely (`ResponseEnded`). No project tests ran in that failed job.
Evidence: `artifacts/resource-5dadd-coverage-failed.log`. The failed jobs were
retried against the same commit in run 39540486904, attempt two. All six shards
then passed and the complete aggregate measured 6168/6360 changed trusted lines
(96.98%, 192 uncovered), failing the unchanged 100% floor. Evidence:
`artifacts/resource-5dadd-coverage-evidence/coverage/SharpProof.coverage.json`.

### Owned-IR fault-profile coverage boundaries

The next three controls exercise object/array reference casts and exhaustion
between instructions after term traversal. Upcasts must add no type-fault
candidate, downcasts retain a conservative candidate even for a null execution,
and exhausted classification must discard all facts. The initial build caught
a test assertion using a nonexistent value property and a missing lambda
argument guard; both test-source errors were corrected. The next run exposed
constant folding of literal-null casts and a callback-factory mismatch in the
test setup. The final controls use variable operands and each callback's own
factory. All 95 projection/profile cases passed with zero build errors.
Evidence: `artifacts/resource-fault-profile-coverage-boundaries-3.log`.
Full formatting passed before the final test-setup corrections; the final
private-workspace whitespace pass copied zero changes. Evidence:
`artifacts/resource-fault-profile-boundary-format.log` and
`artifacts/resource-fault-profile-boundary-apply-format.log`. Final documentation
checks passed in `artifacts/resource-fault-profile-boundary-docs-final.log`.

### Shared normal-domain proof budgets

New worker controls spend the shared budget across clauses, resource regions,
instruction discovery and return-term checking. A separate Boolean snapshot
chain checks both sides of the resolution-depth boundary: losing the constant
must retain the unproved exceptional edge, even though independent execution
returns normally. All 34 normal-domain cases passed with zero build errors.
Evidence: `artifacts/resource-normal-domain-shared-budget.log`.

### Package compiler lifetime and observed container OOM

Canonical CI for `01a00a77f` captured Docker state `oomKilled: true`, exit code
one, for the failed package job. The BuildTask test host crashed after reporting
one passed test; the first 134-case package layout shard had passed. Evidence:
`artifacts/resource-01a00-package-container/canonical-package.json` and
`artifacts/resource-01a00-package-job.log`. This establishes an OOM event for
this container, unlike the earlier shutdown logs; it does not identify the
allocation that exhausted the container.

Package consumer preparation now disables persistent compiler servers for
ordinary runs as well as coverage runs. Temporary consumer workspaces use
distinct compiler pipes, so retaining servers across the campaign can accumulate
idle processes. This is a source-based retention diagnosis; the full constrained
package run must qualify the fix. All four actual preparation callback modes
passed, together with coverage-evidence regressions. Evidence:
`artifacts/resource-package-compiler-lifetime-regressions.log`.
The complete constrained package run passed all 492 tests with one existing skip
across 14 shards, including all 95 BuildTask tests, in 541.1 seconds. Full
quality/API, formatting and documentation checks also passed. Evidence:
`artifacts/resource-package-compiler-lifetime-full.log`,
`artifacts/resource-package-compiler-lifetime-lint.log`,
`artifacts/resource-package-lifetime-budget-format.log` and
`artifacts/resource-package-lifetime-budget-docs.log`. The comparable previous
local package baseline was 642.7 seconds at four CPUs, 12 GiB memory, 4 GiB
scratch and parallelism four, with 492 passed and one existing skip.
One local timing comparison does not establish a general performance gain.
Canonical package CI then passed on published `5dadd9e3b`, run 39540486893,
job 119027232479. Evidence: `artifacts/resource-5dadd-package-job.log` and
`artifacts/resource-5dadd-package-container/canonical-package.json`.
Docker recorded exit code zero and `oomKilled: false`.

### Loop rejection coverage follow-up

The coverage follow-up adds five valid-source rejection controls: a dynamic
descending bound, a negative descending bound, assignment-style progress,
updates to a different counter, and an empty first phase body. Each requires
an explicit Unknown reason and no partial bounds. All 50 rejection cases passed
with zero build errors. Evidence:
`artifacts/resource-loop-unsupported-boundaries-2.log`. Documentation checks
passed in `artifacts/resource-loop-boundary-docs.log`.
Two attempted oversized mixed-width compound assignments were rejected by the
C# compiler before analysis and were removed. Those attempts do not qualify
the corresponding defensive analyzer branches as covered.

### Owned-IR primitive fault candidates

`NativeTaskIrFaultOriginVerifier` derives conservative primitive fault families
from source-owned callback IR after the existing source-cost/ownership check.
It handles typed throws, arithmetic, array length/indexing and cast candidates,
and records the presence of IR allocation sites separately. The set and flag
are discarded together on unclassified throws/exits, approximations, unsupported
control assumptions, ownership failures or the shared traversal budget. The
new production file is registered in the acceptance contract's trusted paths.

All 92 projection/profile cases passed with zero build warnings/errors. Primitive
arithmetic, null and indexing failures are independently executed by the IR
interpreter. Total IR can return typed defaults for unguarded unboxing; the
profile retains conservative candidates without inferring an actual throw.
The source lowerer's unsupported array downcast remains explicitly unclassified.
Controls also cover every typed throw family, later-child rejection, shared
budgets, control transfers and cancellation. Evidence:
`artifacts/resource-task-ir-fault-origins-tests-3.log`.
Evaluated layout passed: 2850 source files, 59 projects, 151 pinned common files.
Evidence: `artifacts/resource-task-ir-fault-origins-layout.log`.
Full C# formatting passed:
`artifacts/resource-task-ir-fault-origins-format.log`.

This is an IR candidate profile, not complete CLR fault coverage or a runtime
byte bound. Empty candidates do not prove exception-free execution. Allocator
failure, initialization, runtime helpers, payloads, exception escape boundaries
and trace growth/copy costs still require separate checked models before root
composition. Runtime array-limit approximations deliberately remain Unknown.

### Canonical package isolation and termination evidence

On `12c8a8516`, the canonical package runner reported shutdown after its first
134-case shard passed. The aggregate `build-and-test` check then rejected that
failed prerequisite; other canonical prerequisites passed. This repeats the
shutdown symptom seen on `0cc594782`, but still does not establish OOM as its
cause. The job inherited Compose's 40 GiB memory and 8 GiB scratch defaults.
[GitHub's public Linux runner specifications](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job)
provide 16 GB RAM. The package job now uses the same 12 GiB memory / 4 GiB
scratch envelope already used for coverage, keeps its CPU/filter/parallelism,
and retains its named container until Docker exit/OOM/error state is captured
and uploaded. This corrects the isolation configuration and adds evidence;
exact-head hosted CI is still needed to qualify it.

The complete canonical package command passed locally in that 12 GiB / 4 GiB
envelope: 492 tests passed, one existing skip, across 14 isolated shards with
parallelism four, in 642.7 seconds. Evidence:
`artifacts/resource-canonical-package-constrained.log` and
`artifacts/timings/package-tests-release-fast.json`.
Full quality/API and documentation checks also passed. Evidence:
`artifacts/resource-ir-origins-package-isolation-lint-final.log` and
`artifacts/resource-ir-origins-package-isolation-docs-final.log`.

### Intermediate fault observation controls

The pinned-runtime fault-recording matrix now covers both recording orders,
with and without observation between records. The actual holder transitions
from unhandled to handled after observation and back to unhandled after the
second record; final cleanup marks it handled again. Both dispatch identities
and capacity-two growth remain intact. Allocation measurements use separate
intervals around each record, excluding reflection and observation work; their
sum remains within the independently calculated 736-byte fixed-carrier envelope.
All 167 Specs cases passed with zero build warnings/errors on .NET 8.0.32 x64.
Evidence: `artifacts/resource-task-observed-holder-runtime.log`.
Full formatting, quality/API and documentation checks passed. Evidence:
`artifacts/resource-task-observed-holder-format.log`,
`artifacts/resource-task-observed-holder-lint.log` and
`artifacts/resource-task-observed-holder-docs.log`.

This validates the re-registration path used by the conditional notification
component. It does not reproduce automatic finalization frequency or establish
a complete root fault-byte bound; phase F remains open.

### CI-sharding integration

After `e17baf9e8` was published, the MR reported a new master conflict. The
integration includes master `b96f69ee8` (CI task/shard splitting, nine files)
and resolves the single coverage-workflow conflict by retaining all six new
shard names together with the branch's per-shard environment and named-container
termination evidence. CPU/memory/scratch limits and the 100% changed-TCB floor
are unchanged. No production C# files changed in this master update. The prior
four-shard evidence remains a historical measurement; the integrated head needs
its own six-shard qualification.
The integrated sources passed the Release repository gates (including evaluated
layout and documentation), full quality/API checks, and coverage-evidence
regressions including the four-mode actual package preparation callback.
Evidence: `artifacts/resource-master-b96-repository-gates.log`,
`artifacts/resource-master-b96-lint.log` and
`artifacts/resource-master-b96-coverage-regressions-2.log`.
Full formatting passed: `artifacts/resource-master-b96-format.log`.

### Dual fault-recording paths and notification prefixes

The pinned `Task.cs` cache still matches SHA-256
`5967259aff81a295fc2280e6400ed76b14d29f2687882c55d5297aabd281af70`.
`ScheduleAndStart` records a scheduler wrapper at lines 1699-1701, while the
at-most-once callback path records its exception through `HandleException` at
lines 2416-2436. The fixed carrier model now budgets both recording sites per
child without assuming they are mutually exclusive. This allows up to 32
joined dispatch records for 16 children, including publication candidates and
list growth. Its model is `task-join-fixed-fault-carriers-v2`; the corresponding
single-notification construction model is `task-fault-notification-storage-v2`.

The new `task-join-closed-fault-notification-prefixes-v1` component separately
bounds notification constructions under a declared closed-holder lifecycle:
standard holders, one queue attempt and at most one callback invocation per
child, no externally requested finalization registration, resurrection or
runtime mutation. Each populated holder starts registered; later fault
recording can re-register it through `MarkAsUnhandled`. Two recording sites
therefore allow two constructions per child; the join and configured async box
each have one completion-reserved fault publication. A leaf construction has
at most two dispatches, the join at most twice the child count, and the async
box one. This gives `464 * children + 208` notification bytes, plus 216 for the
configured box, under the same preexisting-message/stable-list premises as the
construction component. Initialization, event work and original payload/trace
allocations remain separate. The component exposes its required runtime model explicitly. This premise is
not bound into a root verdict;
cumulative task runtime bytes remain Unknown and phase F remains incomplete.

All 165 Specs tests passed with zero build warnings/errors on .NET 8.0.32 x64.
New runtime controls invoke the actual private fault recorder twice in both
orders, retain two distinct dispatches, verify capacity-two list growth and
measure fixed allocations within the independently calculated 736-byte envelope.
This validates recording/storage behavior; it does not reproduce a scheduler
race or establish exception trace bounds. Notification constructor measurements
also passed at 17 and 32 dispatches. Evidence:
`artifacts/resource-task-dual-fault-runtime-2.log`.
All 174 selected worker cases passed with zero build warnings/errors, including
the prefix ledger, required-model identity, ownership/layout failures,
cancellation and existing task-root contract behavior. Evidence:
`artifacts/resource-task-dual-fault-worker-final.log`. Full quality/API and
documentation checks also passed in
`artifacts/resource-task-dual-fault-lint-final.log` and
`artifacts/resource-task-dual-fault-docs-final.log`.
Full formatting passed: `artifacts/resource-task-dual-fault-format-final.log`.

### Consolidated memory read validation

Memory extraction now validates reads once in reverse instruction order. The
first recorded use is each variable's greatest read index. A separate live-copy
set preserves the existing rule for unobserved copy chains: their reads retain
the conservative last-use envelope without making prior copies observed. This
replaces two traversals of the same immutable terms and removes the second
pass's unreachable read/depth rejection; the 1024-read and depth-32 budgets are
unchanged. Numeric bounds and model identities are unchanged.

The complete memory group passed 205 cases with zero build warnings/errors.
New controls cover initializer reads plus their extent, scalar casts used both
in returns and assigned snapshots, nested Boolean guards, and nested owned
field reads. Supported results agree with independent replay. For deeper terms,
the replay checker also returns its explicit depth rejection; interpreter return
values and heap identities independently verify the actual array/graph outcome.
Evidence: `artifacts/resource-memory-single-pass-controls-4.log`.
All 716 selected native resource-contract and fork/join worker tests passed
with zero build warnings/errors. The full quality/API and documentation gates
also passed. Evidence: `artifacts/resource-memory-single-pass-worker.log`,
`artifacts/resource-memory-single-pass-lint.log` and
`artifacts/resource-memory-single-pass-docs.log`.
Full formatting passed: `artifacts/resource-memory-single-pass-format.log`.

### Memory-envelope ownership and budget controls

Seven additional controls reject exhausted instruction, definition, expression
and dominance budgets, admit scalar havoc, and reject reference/memory havoc.
Two divisor-snapshot controls exercise the normal-domain resolution boundary;
both independently execute to seven, while the deeper chain conservatively
returns Unknown. The final combined group passed 73 cases with zero build
warnings/errors. Formatting passed. Evidence:
`artifacts/resource-memory-envelope-domain-final.log` and
`artifacts/resource-memory-envelope-budget-format.log`.
The full quality/API gate subsequently passed, including Markdown, spelling,
links, compatibility and negative fixtures; documentation regressions passed.
Evidence: `artifacts/resource-memory-envelope-budget-lint-final.log` and
`artifacts/resource-memory-envelope-budget-docs.log`.

At published `0cc594782`, all coverage shards, documentation, security and the
separate package/consumer workflow passed. Canonical quality found missing
blank lines around roadmap headings (MD022); this checkpoint corrects them.
The canonical package job passed its first 134-case shard, then the runner
reported a shutdown signal and killed the Docker client with exit 137. That
job has no complete package result, and this log does not establish OOM as the
cause. Exact-head CI must still qualify the subsequent published sources.

### Fault notification lifecycle review

The pinned `TaskExceptionHolder.cs` cache still matches SHA-256
`bc4dda204de133879e4e334b37f74fc6c57690c05a179ae4648fbfa653a38604`
(blob `4d9a68fdca4f11df2a493f8695dc3ea82f38544f`). Its finalization method constructs the
notification at lines 54-70, while `MarkAsUnhandled` can re-register an observed
holder for finalization at lines 213-224. The finalization method itself does not mark the
holder handled. Therefore a per-construction bound alone is insufficient to
multiply by an assumed one-notification-per-holder count. The admitted root
still needs a lifecycle proof excluding later exception additions and external
re-registration/resurrection effects. Pinned `Task.cs` documents the ordinary
fault-publication ordering at lines 1813-1827 and reserves promise completion
before recording exceptions at lines 3377-3393; those facts are inputs to that
proof, rather than an existing notification-frequency certificate. No new
cumulative runtime-byte verdict is published from this review.

### Analysis-budget boundary controls

Two additional scalar-traversal controls admit depth 32 with independent replay
and reject depth 33 with `MemoryScalarEffectUnsupported` and no partial bounds.
The complete memory group passed 196 cases with zero build warnings/errors in
the canonical Linux amd64 container. Evidence:
`artifacts/resource-memory-traversal-budget-controls.log`.

Four additional fork/join controls reject empty/control-character input names,
oversized input names, and a later child exceeding the combined instruction
budget. Each checks both source costs and child storage, retaining no partial
cumulative, peak or retained bounds. All 64 projection/composition/transport
cases passed with zero build warnings/errors. Evidence:
`artifacts/resource-fork-join-budget-controls.log`.
Canonical formatting and documentation checks passed as recorded in
`artifacts/resource-fork-join-budget-format.log` and
`artifacts/resource-fork-join-budget-docs-final.log`.

### Memory ownership rejection controls

At checkpoint `0cc594782`, the memory group passed 194 cases in the canonical Linux amd64
container. Six additional controls allocate owned storage before presenting a
borrowed or null field receiver, a borrowed child, an unsupported scalar field
expression, a directly returned borrowed reference, or a read of an uninitialized
reference field. Each returns its explicit Unknown reason and no partial memory
bounds. These controls exercise rejection after otherwise countable allocations;
they do not extend the admitted memory model or close the task fault-byte item.
Six additional signed/unsigned narrowing-cast controls compare representable
constant values with independent replay and require Unknown for out-of-range
values. The final build reported zero warnings and zero errors.
Evidence: `artifacts/resource-memory-ownership-boundaries-final.log`.
The final canonical formatting and documentation checks also passed; their logs
are `artifacts/resource-memory-ownership-format-final.log` and
`artifacts/resource-memory-ownership-docs-final.log`.

### Current coverage qualification

Published `6ef27aa7b` passed all six coverage shards in run 39549217069. Its
complete merge-commit report (b0355107616598e00ef7b99beabbcfd73e62317b) measured
6233/6422 changed trusted lines (97.06%, 189 uncovered), failing the unchanged
100% floor. This is the latest complete numeric measurement:
`artifacts/resource-6ef27-coverage-evidence/coverage/SharpProof.coverage.json`.
Task-only dispatch-copy work has no uncovered changed lines. The context-wrapper
and emitted-boundary work is newer and needs its own qualification.

Published `cfa23e7cb` passed all six coverage shards in run 39545675178. Its
complete merge-commit report (8c25b7674f9933c7d27ca4bb0f91b6d08befb5e8) measured
6201/6388 changed trusted lines (97.07%, 187 uncovered), failing the unchanged
100% floor. This earlier complete numeric measurement is retained at
`artifacts/resource-cfa23-coverage-evidence/coverage/SharpProof.coverage.json`.
The fixed source-exception instance verifier has no uncovered changed lines in
that report. Source-activation and task-dispatch-copy work is newer and requires
its own qualification.

Published `5dadd9e3b` passed all six coverage shards in run 39540486904, attempt
two. Its complete merge-commit report (0a3d1d8123995ac9a62da95b9f739f00468735af)
measured 6168/6360 changed trusted lines (96.98%, 192 uncovered), below the
unchanged 100% floor. This earlier complete numeric measurement is retained at
`artifacts/resource-5dadd-coverage-evidence/coverage/SharpProof.coverage.json`.
The profiler boundary controls, construction-order controls and source-exception
instance component are newer than this report.

Published `01a00a77f` passed all six coverage shards in run 39537817090.
Its complete merge-commit report (6a6e5d3a480ce4d98b27f90d4be3a7ccbf796e69)
measured 6159/6360 changed trusted lines (96.84%, 201 uncovered), below the
unchanged 100% floor. This earlier complete numeric measurement is retained at
`artifacts/resource-01a00-coverage-evidence/coverage/SharpProof.coverage.json`.
Four uncovered lines belong to the new owned-IR profiler. The local loop and
shared-budget controls and compiler-lifetime repair are newer than this report.

Published `12c8a8516` passed all six coverage shards in run 39532409230.
The complete merge-commit report (65e4b165ad64b0340d2a5275be40c13045d326c7)
measured 6095/6292 changed trusted lines (96.87%, 197 uncovered), below the
unchanged 100% floor. This earlier
report is retained at
`artifacts/resource-12c8-coverage-evidence/coverage/SharpProof.coverage.json`.
The observed-holder controls and owned-IR profiler were newer than this report.

Published `ff069b5e4` passed all four coverage shards in run 39527309371.
The complete merge-commit report (9afe98ad12cf941d29eb12a4edbc7e21d4a645de)
measured 6069/6273 changed trusted lines (96.75%, 204 uncovered), below the
unchanged 100% floor. This is an earlier complete numeric measurement; the
report is retained at
`artifacts/resource-ff069b-coverage-evidence/coverage/SharpProof.coverage.json`.
All non-coverage workflows also passed on that published head, including
canonical package tests, reproducibility and self-application. The consolidated
read pass and dual-fault components are newer and need exact-head qualification.

Published `0cc594782` passed all four coverage shards in run 39524271604.
The complete merge-commit report (a7dc4fa8b1a3de4817fee37afb7f13d16bb4c82e)
measured 6056/6273 changed trusted lines (96.54%, 217 uncovered), below the
unchanged 100% floor. This is an earlier complete numeric measurement; the
report is retained at
`artifacts/resource-0cc594-coverage-evidence/coverage/SharpProof.coverage.json`.
The newer analysis-budget controls are not part of this measurement.

Published `96dcfa4ee` passed all four coverage shards in run 39522192698,
including the repaired package preparation. The complete merge-commit report
(92a24329ec1e45fad2e364651d8c30853c0f465f) measured 6052/6273 changed trusted
lines (96.48%, 221 uncovered), below the unchanged 100% floor. This earlier report superseded
the complete numerical measurements below. The report is retained at
`artifacts/resource-96dc-coverage-evidence/coverage/SharpProof.coverage.json`.
The additional local memory and fork/join controls above are newer than this report
and require their own exact-head qualification. The roadmap and draft MR remain
incomplete while the fault-byte proof, coverage floor and final audit are open.

Published `c1cab0c1e` passed all four coverage shards in run 39504679149.
The complete merge-commit report (cd7093d7bf7187a8cfc5bc3970629e7c6c1f2946)
measured 6016/6246 changed trusted lines (96.32%, 230 uncovered lines), below
the unchanged 100% floor. This earlier complete measurement includes
the notification component and replay controls. The report is at
`artifacts/resource-c1cab-coverage-evidence/coverage/SharpProof.coverage.json`
and the aggregate failure log is `artifacts/resource-c1cab-coverage-failed.log`.
The new violation-search controls and cache 55 require their own exact-head CI.

Published merge `373f97c1e` passed all four coverage shards in run 39498435327.
The complete merge-commit report (8df25f37cf0562bc227c409412468fec0b826dc8)
measured 5980/6223 changed trusted lines (96.10%, 243 uncovered lines), below
the unchanged 100% floor. This earlier report
is at `artifacts/resource-373f-coverage-evidence/coverage/SharpProof.coverage.json`
and its failed aggregate log is `artifacts/resource-373f-coverage-failed.log`.
The notification component and replay controls were not part of this earlier report.

Published `17d4f5cf5` passed all four coverage shards in run 39495448694.
The complete merge-commit report (0f9d7d9ed44e120005b5b0a2bae5876a312a2cc9)
measured 5969/6223 changed trusted lines (95.92%, 254 uncovered lines), below the
unchanged 100% floor. The report is at
`artifacts/resource-17d4-coverage-evidence/coverage/SharpProof.coverage.json` and
the failed aggregate log is `artifacts/resource-17d4-coverage-failed.log`.
The newer closure-plan controls and runtime-source review in `ff68f82cd` are
not part of this measurement and still need exact-head CI qualification.

Published `7a94e4a89` passed all four coverage shards in run 39491749196.
The complete merge-commit report (ef2e90e620a966be29b1bc9017fc291c0e365413)
measured 5955/6223 changed trusted lines (95.69%, 268 uncovered lines), failing
the unchanged 100% floor. The report
is retained at `artifacts/resource-7a94-coverage-evidence/coverage/SharpProof.coverage.json`
and the failed aggregate log at `artifacts/resource-7a94-coverage-failed.log`.
The newer normal-domain and overload controls in `215a598c4` are not part of
this report and require their own exact-head CI qualification.

Published `ecdbd2c5c` passed all four coverage shards in run 39485547326.
Its complete merge-commit measurement (64a90fb395a27dcb1af99f98197603c713672bbf)
is 5900/6177 changed trusted lines (95.52%, 277 uncovered lines), below the
unchanged 100% floor. The report is retained under
`artifacts/resource-ecdb-coverage-evidence` and the failed aggregate log is
`artifacts/resource-ecdb-coverage-failed.log`. This supersedes the numerical
measurement below; it does not qualify the subsequent local component or tests.

Published 0da3ee9f9 passed all four coverage shards. Its exact run
39474925632 aggregated 40 reports for merge commit
180ab4c467714041cf6af26cefaf6c9f0304bb78, associated with that branch head.
Changed trusted coverage measured 5882/6169 lines (95.35%, 287 uncovered lines),
failing the unchanged 100% floor. The full report is retained at
artifacts/resource-0da3-coverage-evidence/coverage/SharpProof.coverage.json;
the failed gate log is artifacts/resource-0da3-coverage-failed.log. This is an
earlier complete measurement. All non-coverage checks also passed on that head. It confirms the hosted package lifecycle fix,
but does not qualify unfinished features or subsequent local tests.

The following paragraphs retain earlier measurements as history.

Published c46e073c3 passed all four coverage shards, including the instrumented
package shard, and all non-coverage workflows. Aggregate enforcement measured
5140/5390 changed trusted lines (95.36%, 250 gaps), failing the unchanged 100%
floor. This supersedes the older a9f759896 measurement (4978/5213, 235 gaps).
The report and gap inventory are preserved locally. No floor or exclusion was
changed. The aab335ab9 coverage run reached a test failure before aggregation:
one compiler-driver assertion and two artifact golden files still expected schema 52.
Later checkpoints corrected those expectations; the current source uses artifact
schema 55. The compiler-driver and golden-worker gates were rerun. This failed run produced no newer
aggregate coverage measurement.
Published 24cb6d7ed passed all non-coverage workflows and the three semantic
coverage shards. Its instrumented package shard failed
LauncherCancellationWhilePublishingPreservesThePreviousPublication at the
publication-member acquisition readiness assertion after 50 seconds. Aggregation
was skipped, so c46e073c3 remains the latest complete numeric measurement. The
fixture failure needs investigation and an exact-head coverage rerun; its cause
has not been established by the passing local resource package cases.

At source commit b2584a9d7, the isolated publication-cancellation fixture passed
unchanged in the ordinary package runner and in a fresh Release checkout with
static managed Code Coverage instrumentation. Each selected test completed in
about one second. The instrumented probe used the repository's managed settings
and private output workspace. These probes do not reproduce the earlier failure
in isolation and do not establish its cause in the full package phase context. The
five-second readiness and cancellation assertions remain unchanged.

The b2584a9d7 CI run passed all three semantic coverage shards, but its package
shard stopped with exit code 143 after a 125-case package-layout group passed.
There was no test assertion or complete coverage report for that termination.
The cause is not established. A failed-job rerun was requested, preserving the
successful shard evidence and all gate requirements. That retry passed the same
125 package-layout cases before the host shell reported its docker compose
process killed and the job ended with code 137. No newer aggregate measurement
was produced. The Docker-limit mismatch and its bounded validation are tracked
below; no further blind retry was started.

The 3795ebdc7 coverage run also failed in the package phase with exit code 137,
after package creation and before a test-result summary. The shell reported the
container PowerShell process killed. The 12 GB container limit therefore did not
resolve the full-phase failure. All three semantic shards passed; aggregation
was skipped. The failure log is retained locally as
`artifacts/resource-3795-coverage-failed.log`. No OOM cause is asserted from the
exit code alone, and no coverage requirement or test deadline was weakened.
The 41ae18f82 coverage run repeated the package-phase exit 137 with the container
PowerShell process reported killed. All three semantic shards passed, but no
new aggregate report was produced. Its log is retained as
`artifacts/resource-41ae-coverage-failed.log`. The failure remains unresolved;
Docker state evidence is needed to distinguish causes before changing limits
or test concurrency again.

The ef285be0c coverage run also passed all three semantic shards and ended its
package phase with the container PowerShell process killed and exit 137. It
produced no newer aggregate measurement. The log is retained as
`artifacts/resource-ef285-coverage-failed.log`. The new container termination
reporting has not yet run on a hosted job.

### Quantitative-memory feature audit

Phase C now has compiler, native-worker, protocol, consumer and independent
runtime controls for its declared models. The finite owned-graph checker tracks
aliases, sharing, cycles, field overwrites, closure storage and returned roots;
it keeps cumulative allocation, peak auxiliary storage and retained output
separate. Current-receiver source calls and admitted deferred capture chains
compose with those lifetime rules. Parametric primitive arrays use the separate
conservative cumulative envelope, including initialized references across compiler
guards. Logical source activation/stack bounds have their own declared model.

The latest native/resource/protocol gate passed 778 cases. All four parametric
package cases passed, covering direct/cross-block arrays, strict/advisory policy,
and JSON/SARIF upper growth. Earlier independent x64 runtime controls measured
the same storage categories and cache/frame placements; this step changes no
layout constants. Borrowed or uninitialized graphs, unmodeled field/effect access,
unproved closure placement and arbitrary dispatch remain explicit Unknown
boundaries. Phase C's feature checkbox records these implemented models; it does
not waive those boundaries or the separate 100% coverage and final-audit gates.

The remaining feature implementation is phase F's cumulative task allocation
composition and completion of its concurrency controls. Source loop work and
normal-completion compiler/runtime storage now have native root proofs. These
declared models and their unsupported boundaries are described below. Coverage
qualification and the final audit remain open; the MR remains draft.

### Package coverage compiler-server lifecycle

At 7f049edd3 the corrected reporter succeeded. All three semantic shards passed;
the package collector ended with exit 137 and Docker reported oomKilled true,
with no Docker error. This establishes an actual container OOM event, replacing
the earlier unconfirmed inference. The state is retained as
`artifacts/resource-7f-container-state/package.json`; the failed log is
`artifacts/resource-7f-coverage-failed.log`. No aggregate report was produced.

The first containment experiment used one outer shard and one nested NUnit
worker under the same four-CPU/12-GiB/4-GiB-scratch Docker budget. A live Docker
process listing showed many idle Roslyn compiler-server processes accumulating.
Package workspaces assign unique compiler pipe IDs but do not end their servers
when disposing each workspace. That probe was deliberately stopped after 708
seconds to test the lifecycle correction; its 130-case layout group passed in
10 minutes 13 seconds. Exit 143 records the stop, not a test failure or OOM.

The second experiment disabled shared compilation in coverage consumer
workspaces, retaining the serial configuration. It passed nine groups (458
passing cases and one existing skipped case) but exceeded the unchanged
1200-second suite deadline. End-to-end time was 1280.36 seconds; exit 1 records
the deadline failure. Serial execution is therefore rejected, not shipped.

The current candidate restores the original shard and NUnit concurrency and
collector settings. Only coverage consumer processes set
UseSharedCompilation=false to avoid retaining one-off compiler servers. All
case selection, assertion deadlines, instrumentation, authority evidence and
coverage floors remain unchanged. A fresh full probe uses the same Docker
budget. The complete phase passed all 14 isolated shards at parallelism 4:
487 tests passed and one existing test was skipped. End-to-end time was 551.98
seconds, including build, discovery, test execution and report normalization.
The phase exported authority and shard evidence and normalized 28 report files.
Logs and elapsed-time evidence are retained as
`artifacts/resource-package-server-lifecycle-coverage.log` and
`artifacts/resource-package-server-lifecycle-elapsed.json`. This establishes a
local full-phase pass under the constrained budget; fresh exact-head CI and the
unchanged aggregate coverage requirements remain open.

The same reporter confirms that 0e420a470's package phase also ended with Docker
oomKilled true and exit 137. Its three semantic shards, canonical build/package
gates and platform consumers passed. Package reproducibility was still running
at this checkpoint. The second local probe passed its 130-case layout group in
13 minutes 15 seconds, with no failed or skipped cases, before the later suite
deadline. These are partial results, not a full coverage-phase pass.

### Concurrency rejection and repeated verification controls

New compiler-to-worker controls require Unknown without numeric bounds or
metadata proof dependencies when a callback throws explicitly, accesses shared
state, locks, performs an interlocked mutation or calls cancellation operations.
Repeated verification alternates an invalid later child with a valid owned
projection, checking that rejection cannot poison or accumulate the next
invocation's normal-storage bound. Frontend controls also reject exception
regions and roots that launch several joins or launch joins in a loop. These
tests passed the 35-case worker task gate and all 44 frontend planner cases.
Targeted C# formatting also passed. These controls reject unmodeled behavior;
they do not establish cancellation or exceptional-path allocation bounds.

### Carrier validation invariants

The successful semantic shards from 0e420a470 provide partial line evidence,
not aggregate qualification. They identify untested storage-composition paths.
The fixed compiler-carrier calculations use bounded constant extents and field
counts. Once field validation and the first array layout check succeed, the
delegate/object shapes cannot fail the same layout or expression budgets.
Redundant failure branches after those checks are removed; incompatible contexts
and unsupported fields still return Unknown at their validated boundaries.
A new control distinguishes a valid array-count program from the missing byte
storage shape: it must return Unknown with no partial peak or retained bound.
The combined worker/resource/protocol gate passed all 865 cases on this cleanup.
Targeted formatting passed. Quality/API compatibility and negative fixtures
passed, except that the remote link audit received HTTP 504 for one existing
pinned LICENSE URL referenced by three package READMEs. The repository API
confirms that file exists at its pinned commit. The link-only retry passed,
including the missing-file and missing-fragment negative controls. Final roadmap
spelling and Markdown checks passed. No coverage floor or exclusion changed.

### Source task allocation prefixes

The native root checker now composes ArrayAllocations and AllocatedElements
from every isolated child, adding the compiler-owned params array and its
child-count elements. These are source-allocation prefixes, including partial
launches and arbitrary child overlap, under the existing at-most-once execution
premise. Framework/scheduler arrays are excluded by that declared source model.
The required joined-isolated-source-allocation-prefixes result scope distinguishes
these claims from callback-only loop work and normal-completion lifetime bytes.
Empty callbacks still need one array and two elements for a two-child join.

Protocol 43/cache 54 identify the added proof scope. Compiler artifact 55,
projection codec 3, manifest 6 and resource program 8 retain their formats.
Worker JSON/cache/SARIF and strict/advisory package controls now cover both
metrics and returned/configured joins. Exchanged task scopes must be rejected.
The combined native/resource/protocol/compiler-driver/golden gate passed all
965 cases. All 1482 analyzer tests passed, including seven new compiler-capture
controls for nested namespaces, emission reuse, ambiguous parameter modes and
metadata/callback/field budgets. All six independent runtime observation cases
passed for negative, zero and positive inputs across returned/configured joins,
with three joined invocations per case. The instrumentation records successful
source allocation events and is not submitted as proof evidence. All 12 selected
strict/advisory packaged task cases passed, including source allocation scopes,
normal lifetime scopes and unsupported byte-prefix controls. Full repository
formatting, quality/API compatibility, negative fixtures and documentation gates
passed. Logs are retained in artifacts with resource-task-source-allocation and
resource-task-source-runtime filename prefixes. No public resource enum changed.
Cumulative root task bytes remain Unknown pending the separate runtime prefix
model below. No feature-completion or coverage improvement is claimed yet.

### Coverage preparation repair

At integration head `5f9a25598`, all three semantic coverage shards passed in
run 39520008544, but the package shard failed before starting its tests. The
container exited 1 with `oomKilled=false`. The merged callback still wrote
`UseSharedCompilation` through the retired mutable environment map, causing
"Cannot index into a null array." The fix adds the same `false` value to the
immutable environment builder, retaining the compiler-server mitigation.

A permanent child-process regression extracts and executes the actual package
preparation callback in four coverage/exclusivity modes. It checks the package
source, compiler-server setting, isolated coverage output, collector routing and
direct test-host binding. Both this probe and the existing coverage-evidence
regressions passed. Script formatting passed for 89 PowerShell and 19 shell
files. The complete local Release package coverage shard passed 492 cases with
one existing skip across 14 isolated shards at parallelism four. Total package
phase time was 527.369 seconds. It retained the same four CPUs, 12 GiB memory
and 4 GiB scratch limits as CI; no concurrency, deadline, instrumentation or
coverage floor was relaxed. Normalization completed successfully for 28 reports.
New exact-head CI must qualify the committed repair; no newer aggregate
coverage percentage is claimed.
Evidence uses the `resource-package-environment` and
`resource-coverage-environment-package` artifact prefixes.

### Fault-origin review

The existing normal-domain checker explicitly assumes ideal allocation success
and a nonnegative counter domain. It is not a certificate that real task faults
can only use preallocated exceptions. The pinned
[task scheduling implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L1699)
constructs a new `TaskSchedulerException` when queueing throws, records it, finishes
the task, marks ordinary starts handled, and throws that wrapper. Internal task
creation reaches this shared scheduling path. Proving normal callback source
alone therefore does not account for launch-failure wrapper storage and traces.

The reviewed Task.cs blob is `0ab2d859c0b96f818ed1b56c5f55fcd3dae43e91`,
SHA-256 `5967259aff81a295fc2280e6400ed76b14d29f2687882c55d5297aabd281af70`;
source and provenance are cached under `artifacts/resource-runtime-fault-review/`.
This rejects an unproved shortcut, not an implementation of the full remaining
fault-cost model. Root cumulative task bytes remain Unknown.

### Master integration

Integrated reviewed master `015c1db92` after its immutable-model migration caused
21 textual conflicts. Resource metadata and guards are retained. Constructor
arguments are pinned in the builder before snapshotting; closure storage is
prepared before constructing immutable lowering configuration. Resource result
and metadata projection return updated records, and replay observers are supplied
when constructing options. Fixtures now replace and reattach immutable records
and collections. No tampering/rejection cases were removed.

Merged-source verification passed all 2248 frontend, 182 memory, 161 Specs and
1489 analyzer tests. The complete worker run passed 7600 cases and failed one
new master assertion for the specific default-interface-metadata rejection
message. Replacing nullable immutable-array comparisons with null patterns
preserved that assertion; all 1155 selected codec/resource/protocol regressions
then passed, including the failed case. This is not a second full-worker run.

Both packaged contract/resource benchmark workflows passed after converting 44
variadic command/diagnostic call sites to immutable argument collections, keeping
argument values and order. Full repository formatting, quality/API lint, layout
(2848 source files, 59 projects, 151 pinned common files) and documentation gates
passed. Logs use the `resource-immutable-master` artifact prefix; the successful
worker regression and benchmark logs end in `codec-regression.log` and
`performance-consumers-2.log` respectively. The temporary syntax rewriter and
private-workspace formatter are ignored verification helpers.

Exact-head CI and changed-TCB coverage must qualify the published integration.
The last complete coverage measurement remains 96.32% at `c1cab0c1e`, below the
unchanged 100% floor. Cumulative task runtime bytes remain Unknown; this merge
does not complete the remaining fault-cost model or the final roadmap audit.

### Exception trace element layout controls

Pinned CoreCLR source now identifies `StackTraceElement` in
[the runtime header](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/clrex.h#L34):
`ip`, `sp`, `pFunc` and a 32-bit `flags` field. Together with `ArrayHeader`
in object.h, the reviewed x64 ABI gives a 16-byte raw header and 32-byte element
stride. The source blob is `2918c5cd3b6cf92e38f2792724cc06f10c0269b3`,
SHA-256 `b4450aacce56acd88867e9b00fa26669251f6dac82fe506ffe91e470e7a7fc6e`.
The public source cache and provenance record are under
`artifacts/resource-runtime-fault-review/`.

Three independent runtime controls passed on .NET 8.0.32 x64, throwing through
0, 3 and 15 recursive calls with inlining/tail-call elimination prevented in the
throwing helper. They inspect the resulting byte-array header, capacity, method
descriptor offsets and reviewed flag bits. This checks storage layout only;
no allocation-counter observation or complete trace-cost proof is claimed.
All 161 Specs tests, final formatting, layout (2803 source files), documentation
and full quality/API lint gates passed. An initial spelling failure in the new
source-link label was corrected and the complete lint gate rerun successfully.
Cumulative trace costs still require bounds on frames, updates, affinity/deep
copies, resolver arrays and allocation failures; the root task byte verdict
remains Unknown. In particular, element size does not solve the failure/update
accounting issue recorded below.

### Stored field facts

Six direct IR controls passed for Boolean/integer fields used to select the
returned graph. They check the initial false/zero value, a true/one overwrite,
and overwrite from an unknown input. Known values produce independently checked
24-byte object storage plus the selected 32- or 40-byte returned array; concrete
interpreter replay agrees with the cumulative, auxiliary and retained bounds.
Unknown overwrites discard the earlier field fact and return
`MemoryControlFlowUnsupported` with no partial bounds. All 182 memory tests
passed. Final combined formatting, layout (2803 source files), documentation
and full quality/API lint checks passed. These controls
change no resource cost model or public verdict rule.

### Violation search entry controls

Six focused controls passed for unsupported metrics, missing layout evidence,
unproved parametric lifetime envelopes, entry-expression budgets and exact
threshold/solver candidates. Four additional cases check array/string size
limits with nullable and non-null entries. Their first fixture used a scalar
selector for a reference input; decoding rejected it before replay. With the
required `value.Length` selector, three passed and the non-null string case
exposed an actual search gap: every sampled string was null.

Search now creates bounded non-null string candidates for nonnegative sample
sizes, including the empty string. Each candidate still passes entry constraints,
preconditions and concrete replay before it can refute a bound. Cache schema 55
invalidates older search results; protocol 43 and compiler artifact 55 remain
unchanged. All 764 selected native-resource/protocol cases passed. Two further
array/string controls require positive entry lengths and reject excluded empty
candidates. All 1151 expanded worker cases passed, including the 12 new
boundary controls, resource/task checks, protocol and container contracts.
Final repository formatting passed.
Layout (2801 files, 59 projects, 151 pinned common files), documentation and
full quality/API lint gates passed. Runtime byte-prefix scope and the remaining
roadmap obligations are unchanged.

### Fault notification construction (in progress)

A separate native component now bounds construction of one fault notification
from a preexisting message and a stable, nonempty dispatch list of 1..16 entries.
Its graph contains the aggregate exception with all inherited exception fields,
the exception-reference array and event arguments. The lazy collection-view slot
is included; construction does not read the property that creates its object.
The reviewed x64 layout gives the conditional upper 208 + 8*n bytes. Notification
frequency, original exceptions/traces, resource initialization, later property
reads, event subscribers and complete runtime binding remain separate obligations.
Root cumulative task bytes therefore remain Unknown; no public scope or cache
identity changed.

Before the latest master merge, seven component controls, all 158 specs tests
and all 176 IR memory controls passed. Four .NET 8.0.32 constructor observations
use 64 warmup calls followed by 128 measured calls each. A fifth runtime control
checks the complete inherited field tables. Nineteen new replay controls corrupt
individual real interpreter observations and require Unknown without partial
bounds; supported fresh/empty identities retain their expected bounds.

Remote merge `373f97c1e` incorporated master `71c9b117f` and cancelled the old
`363d09752` coverage run before aggregation. Its log is retained at
`artifacts/resource-363d-coverage-failed.log`; no newer coverage percentage came
from that run. The worktree fast-forwarded to the merge without losing local
changes and rebuilt the slimmer tooling image. Post-merge verification passed
978 worker/container-contract cases, five pinned-runtime controls, 176 IR memory
cases, full formatting, layout, lint/API compatibility and documentation checks.
The test runtime remains pinned at 8.0.32. Evidence uses the resource-fault-notification
and resource-memory-replay-boundaries artifact prefixes.

### Closure plan admission controls

All 2245 frontend tests passed, including 36 direct compiler-backed closure-plan
controls. They check primitive field kinds and unsupported numeric captures,
parameter/local initialization order, assignment/increment/ref/out writes,
initializer-introduced locals and deferred query captures. Limits cover both
accepted local/cache-field boundaries and rejected excess storage/traversal,
plus the 16/17 deferred-function boundary. Actual lambda matching rejects method
groups, deferred sibling frames cannot reuse the single-chain placement rule,
and cancellation yields no partial plan. The structural layout gate also passed.
Evidence is retained under artifacts with the resource-root-closure-boundaries
prefix. These tests leave public verdicts unchanged; they require a new complete
coverage report before any coverage improvement can be claimed.

### Normal completion and carrier admission controls

Direct normal-domain controls now exercise all six integer comparisons through
immutable snapshots, both Boolean short-circuit operators and negation,
positive-divisor admission, value-preserving integer conversions, declared
preconditions, budget exhaustion, missing returns, body abstraction and
cancellation. Their unsupported branch is deliberately reachable if the
checker selects the wrong edge, so success requires the stated guard fact.
Separate compiler controls inspect primitive fields in uncalled overloads and
require an unmodeled decimal capture to reject the entire carrier record.
All 32 compiler projection/capture controls passed, as did the 960-case native
worker gate containing all 27 direct normal-domain controls. Full formatting
and the structural layout gate also passed. Evidence is retained under artifacts
with the resource-normal-domain-boundaries, resource-carrier-overload-boundaries
and resource-domain-carrier-boundaries prefixes. These additions do not change
public verdicts or complete cumulative task runtime-byte analysis. A new full
coverage report is still required before claiming any coverage improvement.

### Fixed task fault carriers (in progress)

A separate internal component now derives conditional fixed fault-bookkeeping
allocation envelopes for owned 2..16-child joins. Typed field tables cover
ContingentProperties, TaskExceptionHolder, ExceptionDispatchInfo's instance and
embedded DispatchState, and List reference/size/version fields. List envelopes
include capacity-one starts, AddRange growth, capacity-four starts, later doubling
and temporary candidate lists that lose publication races. A configured root
adds its own fault record. This is not a root allocation verdict: exception
objects and referenced string/trace payloads, compiler/base task carriers and
runtime premise binding still require complete composition.

The component has numeric boundary cases, pinned-runtime field-signature checks,
ownership/layout rejection and cancellation controls. All 161 focused worker controls, the broader 933-case worker gate and
the required structural layout gate passed. All 157 IR memory controls passed,
including layout rejection for objects/strings and malformed allocation boundaries. No protocol, cache identity or
public result scope changed, and cumulative root task bytes remain Unknown.

Coverage work separately passed all 14 object-allocation controls: extern
primitive properties retain conservative storage bounds; unmodeled decimal
storage, extern constructor bodies and variable-argument constructors publish
Unknown without partial numeric or dependency evidence.

Full repository formatting, lint/API compatibility and negative fixtures, and
documentation automation passed. Evidence is retained under artifacts with the
resource-task-fault-carriers and resource-memory-boundary prefixes. The
unintentionally selected full developer check was stopped during restoration;
it is not counted as verification. Only the completed scoped gates above qualify
this checkpoint. Root cumulative task bytes and the final coverage/audit gates
remain unfinished.

### Cumulative task prefix review (in progress)

The pinned [first-pass exception callbacks](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/exceptionhandling.cpp#L2605)
append frames and invoke trace saving repeatedly. The
[saver failure cleanup](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/excep.cpp#L2483)
clears buffered trace state and can clear the exception trace after a failed
replacement. A cumulative proof must therefore bound trace updates and failed
attempts as well as capacity growth. Array-capacity doubling alone does not
establish that complete bound. The caller and header sources are cached with
pinned Git blob IDs and SHA-256 records in
`artifacts/resource-runtime-fault-review/trace-caller-sources.json`.

The pinned [native trace saver](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/excep.cpp#L2143)
skips shared preallocated exception handles. The
[managed exception implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs#L141)
also skips restoring dispatch state for immutable agile exceptions. These guards
alone do not prove a cumulative byte bound: dispatch capture still requests
trace copies, and the
[holder finalization path](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs#L54)
can allocate an aggregate exception and event arguments for an unobserved fault.
Those paths must be accounted for before publishing the complete runtime model.
The two newly cached managed sources have pinned Git blob IDs and SHA-256 records
in `artifacts/resource-runtime-fault-review/managed-fault-sources.json`. No numeric
fault-payload budget or new public result scope is inferred from this review.

The pinned [Task implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs)
creates additional fault bookkeeping. In WhenAll, concurrent failed children can
allocate lists that lose the compare-and-swap race; a cumulative bound must count
those temporary allocations, not only the final reachable list. Child and join
exception holders also allocate lists and dispatch records. The pinned
[exception holder](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs)
returns its existing dispatch-info list; it does not create a read-only wrapper
for that access. The pinned
[dispatch record](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/ExceptionServices/ExceptionDispatchInfo.cs)
captures exception state, whose
[CoreCLR implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs)
copies trace data. The existing normal-completion storage catalog cannot supply
an exceptional-prefix summary. The next model must account for fault carriers,
list growth and losing races, and explicitly resolve exception/trace storage
ownership before publishing cumulative root bytes. Until then that result remains
Unknown. No runtime fault-path numeric bound is claimed by this review.

Additional coverage controls passed for negative, input-dependent
and floating-point accounting credit, a drain without progress, and a child byte
expression that fits its own budget but exceeds the limit after carrier
composition. All 16 accounting cases and 133 carrier/fork-join cases passed; the controls return Unknown without partial numeric evidence.

### Root lifetime publication

PeakAuxiliaryBytes and RetainedBytes now publish conditional task-root proofs
only with owned task-execution, normal-storage and layout dependencies. The
required joined-isolated-normal-completion scope distinguishes successful joined
execution from callable-source and callback-work scopes. Proof markers bind the
compiler-carrier model and projection; consumers require consistent reference
images across all three dependencies. Missing premises, capture or valid child
storage produce Unknown without partial numeric evidence. Cumulative task bytes
remain unsupported because that contract includes exceptional prefixes.

The expanded 1068-case worker/resource/protocol/compiler-driver/golden gate,
all 1475 analyzer tests and eight packaged task cases passed. Packaged controls
verify advisory success, strict trusted-assumption rejection and cumulative-byte
Unknown. JSON/cache/SARIF mutation controls reject scope, model, projection,
reference and layout laundering. Full formatting, quality/API compatibility,
negative fixtures and documentation lint passed; the new package tests also
passed explicit whitespace verification. Layout checked 2791 sources, 59
projects and 151 pinned common files; the trusted inventory includes 542 paths.
Documentation checked 728 local links. Logs are retained under
`artifacts/resource-task-lifetime-*`.

Protocol 42 and cache 53 identify the added verdict domain. Artifact 55,
projection codec 3, manifest 6 and resource program 8 retain their formats.
Cumulative prefix/runtime allocation, remaining concurrency controls, full
coverage qualification and final audit remain required.

### Reviewed runtime task storage

The normal-storage catalog records typed Task, WhenAll and async-box fields
against the pinned runtime source. Runtime implementation metadata independently
confirms Task's two integer/five reference fields, the promise's two added fields,
the generic result field, the one-byte empty VoidTaskResult and the box's single
state field. The box reuses Task state storage for execution context. Derived
layout bytes include possible helper delegates and compose compiler carriers
with separately checked child storage under arbitrary overlap. The retained upper
envelope excludes private void-child arrays and conservatively keeps carriers.
Default/suppressed ambient context, disabled debugger/tracing and excluded
scheduler/global/caller-induced storage are visible model premises, not detected
deployment facts. The independent field probe is retained as
`artifacts/resource-runtime-task-field-probe.log`.

### Coverage diagnostic ownership correction

At 6d28322c0, semantic-projects and semantic-gates completed coverage collection
successfully, but the new reporter could not create artifacts/container-state
because the container exporter owns that directory. Its upload then failed.
The package collector separately ended with exit 137, and the same ownership
failure prevented its Docker state from being recorded. Completed job logs are
retained locally under `artifacts/resource-6d-*-job.log`.

The reporter now writes JSON under runner-owned RUNNER_TEMP and uploads that
separate path. Exact-container cleanup is registered before creating the output
directory, so recording failures cannot skip cleanup. A canonical non-root
permission probe reproduced the read-only artifacts failure and recorded JSON
successfully in the separate writable directory. The full workflow quality/API
compatibility and negative-fixture gate passed. Logs are retained as
`artifacts/resource-coverage-state-permissions-probe.log` and
`artifacts/resource-coverage-state-ownership-quality.log`. Hosted verification
is pending. This correction changes no proof coverage requirement or task limit.

### Coverage container termination evidence

Each coverage job now retains its named task container until Docker's exit
status, OOM flag and error are saved in a separate diagnostic artifact, then
removes that exact container. Coverage evidence, selection, deadlines, limits
and floors remain unchanged. A missing container is recorded as unavailable;
exit 137 alone remains insufficient to assign a cause. A controlled canonical
container exit 23 reported exit code 23 with no OOM flag or Docker error,
and its exact container was removed afterward. The full workflow quality/API
compatibility and negative-fixture gate passed. Hosted termination evidence is
still pending. No host RSS reader, cgroup reader or process-memory budget was
introduced. The local probe is retained as
`artifacts/resource-carrier-container-state-probe.json`.

### Compiler-carrier transport

The root collector now attaches carrier shapes or an explicit capture-Unknown
reason only for selected AllocatedBytes, PeakAuxiliaryBytes or RetainedBytes
contracts. One emission collector is reused per compilation. Closed detached
decoding checks root ownership, execution kind, typed storage fields, module
hash, text/count limits and a shared field budget. Capture failures preserve
source work: an oversized-source control proves the loop bound while keeping
storage Unknown. Loop-only selections skip emission.

The 1042-case worker/resource/protocol/compiler-driver/golden gate passed. Its
fallback case was then strengthened to check actual separate Proven/Unknown
verdicts and passed again. All 1475 analyzer tests and three packaged task-source
cases passed. The first package attempt stopped during analyzer restore with
exit 139, before tests; a fresh canonical retry passed without source or test
changes for that failure. Full formatting, quality/API compatibility, negative
fixtures and documentation lint passed. The strengthened test also passed
explicit whitespace verification. Documentation checked 728 links; layout
checked 2789 sources, 59 projects and 151 pinned common files; the inventory
includes 540 trusted paths. Logs are retained under
`artifacts/resource-carrier-transport-*`.

Projection codec 3, compiler artifact 55 and cache 52 identify the extended
transport. Protocol 41, manifest 6 and resource program 8 retain their formats.
The public API and resource verdict domain are unchanged. Reviewed runtime
Task/async-box storage, root lifetime composition, remaining consumer controls,
coverage closure and final audit remain required. Root task storage verdicts
continue to be Unknown.

### Native compiler-carrier storage

The native checker independently validates captured root ownership, callback
counts, typed fields, state-machine kind and aggregate budgets. It derives
closure, delegate and source task-array storage under the conditional layout.
Debug state-machine classes count as direct objects; Release value fields are
returned separately for the future runtime async-box model. Shared frames and
cached delegates are conservatively counted more than once. Any invalid later
carrier
returns Unknown without a partial storage bound.

All 1003 worker/resource/protocol/compiler-driver/golden cases passed, including
29 new carrier cases with numeric checks, real Debug/Release compiler emissions,
malformed ownership/fields, incompatible layout, budgets and cancellation.
Full C# formatting and quality/API compatibility with negative fixtures passed.
The corrected numeric test literal also passed targeted whitespace verification.
Layout checked 2788 sources, 59 projects and 151 unchanged pinned common files;
the inventory includes 539 trusted paths, and documentation checked 728 links.
Logs are retained under `artifacts/resource-native-carrier-*`.

This remains an internal component. Closed transport, root collector attachment,
reviewed runtime Task/async-box storage, lifetime composition, consumer controls
and coverage closure are still required. Root task memory claims remain Unknown;
no public API or wire format changed.

### Emitted compiler carrier shapes

The compiler collector now captures generated callback-frame and async
state-machine fields from an actual emission, preserving module hash, compiler
identity and root ownership. Debug class state machines and Release value-type
state machines have distinct captured shapes. The capture supplies typed fields,
not byte totals. Framework-owned attributes and supported builder/awaiter value
fields are authenticated; unknown fields, custom layouts, metadata mismatches,
source/image budgets and emission failures produce explicit Unknown reasons.
Emission metadata is cached per collector instance; cancellation propagates.

All 1475 analyzer tests passed, including six new cases covering Debug/Release
shapes, cancellation recovery, emission errors and oversized source. Full C#
formatting passed. Quality/API
compatibility and negative fixtures passed. Documentation checked 728 links;
layout checked 2786 sources, 59 projects and 151 pinned common files. The trusted
inventory includes 538 classified paths. Logs are retained under
`artifacts/resource-carrier-*`.

This is an internal compiler-carrier component. It is not yet attached to root
projections or transported to the worker. Native byte derivation, reviewed
runtime carriers and normal-completion lifetime composition remain required.
Root memory claims remain Unknown, and no wire version or public API changed.

Runtime-carrier review must distinguish the successful join path from faulted
completion. The pinned
[Task implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs)
allocates a non-generic WhenAll promise and registers it on each child. Faulted
or canceled children introduce additional storage. Multiple continuations can
allocate lists. A normal-completion storage model must state its ambient-context,
debugging/tracing, caller-continuation and scheduler ownership premises, and
must not reuse that model as a cumulative exception-prefix guarantee.

### Coverage container limits

The failed-job retry of b2584a9d7 passed the same 125-case package-layout group,
then the host shell reported that its docker compose process was killed; the job
ended with code 137. No aggregate report was produced. The coverage workflow
inherited a 40 GiB container limit and 8 GiB memory-backed scratch from Compose,
while this public repository's standard Linux runner has
[16 GB RAM](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
That mismatch does not establish the exact cause of either termination, but it
fails to contain the job within the runner's capacity. Set coverage-only Docker
limits to 12 GiB with 4 GiB scratch, retaining the existing four CPUs, test
selection, deadlines and coverage floor. Verify the resolved Compose settings,
a representative instrumented package fixture group under those limits, and fresh
exact-head CI. No host resource monitor or per-process memory budget is added.
Compose resolved the proposed settings to 12884901888 bytes, four CPUs and a
4 GiB scratch mount. A fresh private Release checkout of source b2584a9d7 passed
all 94 BuildTaskTests with static managed instrumentation under those limits
(about 66 seconds for the test phase, four package lanes). The earlier isolated
publication-cancellation case also passed with and without instrumentation.
Workflow/quality/API compatibility checks passed. The coverage workflow now sets
those Docker limits explicitly; its default local Compose limits and all test,
deadline, artifact and 100% coverage requirements are unchanged. This is a local
representative validation, not proof that the full hosted coverage job will pass.
Fresh exact-head CI remains required.

### Joined child storage envelopes

Derive each isolated child's cumulative source-allocation byte upper bound with
the existing native allocation/layout checker, then substitute its formal input
map and sum across children. The sum is a conservative child-only peak auxiliary
bound under arbitrary overlap. Void, isolated children retain no owned output on
normal completion. Reject incompatible layouts and invalid later children without
partial numeric results. This component is conditional on the owned layout and
at-most-once runtime premises. Root byte/lifetime claims must remain Unknown until
compiler-generated closure/state-machine and reviewed framework carrier storage
are included. This is required composition work, not completion of phase F.
The combined worker/resource/protocol/compiler-driver/golden gate passed all
974 cases, including 14 child-storage cases for mixed widths, zero-length arrays,
named formal arguments, geometric loops, transport, incompatible layouts, invalid
later children and cancellation. Quality/API compatibility passed. The first test
build found one block-body style issue in a helper; it was corrected before the
passing gate. Full repository formatting passed. No root byte/lifetime verdict, public
API, protocol or cache format was changed by this component.

### Configured async task roots

Extend the closed task-root model to a single authenticated await of
Task.WhenAll(...).ConfigureAwait(false), with no source work before or after the
await beyond the existing isolated children. Carry the root execution kind in the
owned projection, bind a distinct runtime premise for standard async builders
and configured task awaiting, and preserve the joined-callback source-work scope.
Reject captured-context, custom awaiter/builder, repeated-await and extra-statement
shapes. Establish source work first; compiler/framework state-machine allocation
and live-space costs remain separate required phase F work. Verify admitted and
unsupported shapes through compiler, native, transport and packaged consumers.

The configured-kind projection uses codec 2 and compiler artifact 54. Protocol 41
and cache 51 identify the separate runtime-model variant; manifest 6 and resource
program 8 are unchanged. The initial 39 frontend cases passed. A custom-builder
control and malformed execution-kind transport cases were then added. The first
worker attempt exited with code 139 while building the bootstrap analyzer before
project tests; the direct canonical task entrypoint is being used for the rerun.
The final frontend gate passed all 40 cases. The first native run passed 59 of
62 cases; three configured-root failures traced to the existing synchronous
signature gates. The compiler now prepares a distinct scalar resource entry only
for the recognized async shape with valid resource-only claims and no functional
clauses or additional effect contracts. Functional body admission is unchanged.
The repaired focused worker gate passed all 62 native, transport and cache/SARIF
cases. The full frontend suite passed 2205 tests, the full analyzer suite passed
1469, and the combined worker/resource/protocol/compiler-driver/golden gate passed
960. All three real package cases passed, including strict/advisory policy and
scope/growth evidence for returned and configured joins. The additional-contract
control confirms that the async scalar entry does not bypass source contracts.
Formatting, quality/API compatibility, documentation lint, 728 local links and
layout passed (2785 sources, 59 projects, 151 pinned common files; 537 trusted
paths). All local checks for this source-work checkpoint are complete. Native
state-machine/framework allocation and live-space bounds, remaining phase F
controls, exact-head CI, unchanged 100% changed-TCB coverage and final audit remain
required. This does not complete phase F or the roadmap.

### Native task-root loop results

Publish the independently checked sum of isolated callback loop-body entries for
admitted joined task roots under the bound at-most-once runtime premise. Evidence
must state the joined-callback execution scope explicitly, including callback work
through join completion. Decode declared limits against the owned entry bindings
without constructing a fictitious functional body. Keep unproved comparisons,
unbound premises, invalid children and all remaining task metrics Unknown, with
no partial sums or invented exact/lower/expected/tight bounds. Validate execution
scope, dependency and projection records through protocol, cache, SARIF and real
package consumers. Protocol 40 and cache 50 identify the required scope field;
compiler artifact 53 and manifest 6 retain their formats.
The initial focused gate passed 17 cases, including independent concurrent loop
counts and a validated cache/SARIF consumer with scope-corruption controls.
The broader worker/resource/protocol gate passed 869 tests, and all 1468 analyzer
tests passed after correcting a stale protocol-39 assertion. Both real package
cases passed, including strict rejection of the trusted premise, advisory JSON
and SARIF scope/growth evidence, and an unproved task bound that stays Unknown.
The package retry passed after its initial restore exited with code 139 before
tests. All 83 compiler-driver and golden-worker cases passed after updating the
artifact-53 expectations. Quality/API compatibility, documentation lint and layout
passed (2785 sources, 59 projects, 151 pinned common files; 537 trusted paths).
Final scope controls reject stripped metadata with relabeled scope and duplicate
model records. Their final targeted scope/protocol rerun passed all 133 cases,
and full repository formatting passed. All local checks for this checkpoint are
complete. Fresh exact-head CI, the unchanged 100% changed-TCB coverage gate, the
remaining phase F features and the final audit remain required.

### Task runtime premise binding

The compiler now declares the admitted Task.Run(Action)/Task.WhenAll(Task[])
projection's trusted at-most-once callback-source premise. A native binder checks
that premise against the owning callable, reviewed framework identity, net8.0
target and one unambiguous captured reference-image digest. The versioned catalog
hashes its source pin and scope: isolated callback source work per root invocation
through join completion, under arbitrary callback overlap. Scheduler, framework
and ambient callback work are excluded from this source-work model. Reference
evidence identifies compiler inputs; deployment must satisfy the runtime premise.
This is not a framework allocation summary. Root task claims remain Unknown.

All 28 focused binding/artifact cases passed, including missing, conflicting,
foreign and cross-owner evidence, target qualification and cancellation. The full
selected worker/resource/protocol gate passed 852 tests, and all 1468 analyzer
tests passed. The packaged task consumer retained its Unknown result. Full
formatting, quality/API compatibility, documentation lint and 728 links passed.
Layout passed at 2783 sources, 59 projects and 151 pinned common files; the trusted
inventory passed at 537 paths. Cache 49 identifies the new callable premise;
compiler artifact 53, protocol 39 and manifest 6 retain their formats.

The next phase must publish native root results with explicit task scope and
metric semantics, compare only independently checked upper costs, and preserve
Unknown for unsupported metrics and unbound premises. Framework/compiler carrier
allocations, async state machines, concurrent live-space bounds and the remaining
phase F controls still require implementation and consumer verification.

### Task-join projection transport

Add a closed, bounded projection codec for compiler-neutral root/child data,
ordinary Total child bodies, cost programs and formal argument maps. Decode must
bind the expected root identity and input count, enforce child identities and
resource-only roles, reject duplicate/unknown/missing properties and malformed
maps, and validate child cost input ownership. Round-trip transport must preserve
native source-cost composition. This codec is not source authentication or a
runtime premise. The validated projection is now a separate compiler callable
artifact field, bound to its resource claim/root entry and rejected beside an
ordinary functional body. The producer retains framework and child metadata
reference evidence. Compiler artifact 53 and cache 48 identify this transport;
protocol 39 and manifest 6 are unchanged. Task-join claims remain Unknown while
runtime premises and the remaining phase F models are incomplete.

The full worker/resource/protocol gate passed all 825 tests, and the full analyzer
suite passed all 1468. All five package cases passed: a real task consumer kept
its Unknown result, and four parametric memory cases retained their published
behavior. Full formatting, quality/API compatibility and negative fixtures passed.
Layout passed at 2780 sources, 59 projects and 151 pinned common files; the
trusted inventory passed at 535 paths. Documentation generation and 728 links
passed. The codec includes payload, depth and instruction budgets, closed property
sets, ordinary Total body decoding, cost input ownership and malformed-data tests.

### Native task-join source-cost composition

Projection data now lives in the compiler-neutral artifact assembly and retains
root parameter names as display metadata. Native checking validates root and child
input maps, distinct entry/current/old storage, child identities and closed local
ownership. It independently verifies each child cost program, substitutes scalar
root inputs/constants by formal ordinal and sums upper callback loop/allocation
counts. Invalid or unsupported children return Unknown without partial sums.
Negative constants use the admitted nonnegative size domain. Named argument
order, positive differences, geometric counts and maximum expressions are checked.

The 26 composition cases and existing resource/protocol regressions passed all
804 tests. The full analyzer suite passed all 1468 tests after moving the models.
Full formatting, quality/API compatibility and negative fixtures passed. Layout
passed at 2778 sources, 59 projects and 151 pinned common files; documentation
links passed at 728 and the trusted inventory at 534 paths.

These are conditional source costs, not root verdicts. Compiler provenance and an
at-most-once runtime model remain required before root claim publication.
Framework/state-machine costs and lifetime composition remain separate required
work, not zero-cost assumptions. The projection is not yet a root artifact field;
no public API, protocol, artifact or cache version changes in this step.

### Task-join child projection

The compiler now lowers each authenticated callback into a separate exact Total
program and cost program, preserving symbol-bound scalar argument mappings.
It rejects incomplete lowering, child preconditions/contracts, entry initialization,
shared field effects and aggregate IR budget excess. Failure returns an Unknown
reason with no partial child list. All 1468 analyzer tests passed, including 14
projection cases for loops, allocations, source calls, named arguments, shared
state, module initialization, direct/transitive contracts, cancellation and budget
controls. Layout passed at 2776 sources, 59 projects and all 151 pinned common
files; the trusted inventory passed at 532 paths. Quality/API compatibility,
negative fixtures, full formatting and 728 documentation links passed.

The projection stays distinct from an ordinary callable's functional proof. The
native cost kernel checks cost semantics and input ownership; source provenance
requires the compiler envelope. The shadow-body decoder intentionally cannot
carry entry-effect authority, so child codec controls use the ordinary Total
codec. All three native child round-trip/input-ownership controls passed; the
existing 778 native/resource/protocol cases also passed.
No root bound is published until dedicated artifact ownership, root input
substitution/composition and runtime assumptions are integrated. Async state-machine,
framework allocation/liveness and concurrency controls remain required. This is
an implementation step toward phase F, not its completion.

### Structured task-work admission foundation

Phase F starts with compiler recognition of a closed Task.Run/Task.WhenAll
pattern: one returned join, authenticated framework APIs, two to sixteen source
callbacks, scalar arguments, and no additional parent statements. Async lambdas,
arbitrary delegates, cancellation overloads, foreign methods and shared captured
locals remain outside this first admission. The next native integration must
prove total logical callback work across all child prefixes, keep generic
functional verification separate, bind runtime/reference evidence, and report
Unknown for allocation/liveness/state-machine costs until their models exist.

Source admission is implemented and passed 31 cases, including source-defined
framework names, missing/foreign compilations, the child-count limit, cancellation,
unsupported callbacks and root initialization/signature controls. The full frontend
suite passed all 2196 tests. Layout passed at 2773 sources, 59 projects and all 151
pinned common files. Recognition alone publishes no numeric claims and enables no
new worker capability; existing native Unknown behavior remains in force.
Quality/API compatibility, negative fixtures, documentation lint, 728 links and
the 531-path trusted inventory and full repository formatting passed.

Next add dedicated resource projection/ownership, native composition, schema/cache
identity and packaged consumer evidence. Task scheduling, join carriers, delegate
and state-machine allocations must have their own reviewed costs; they must never
inherit the zero-allocation Math metadata summaries. Child source bodies still
require independent effect and resource admission. All unchecked C/F and final
coverage requirements remain open throughout this foundation.

The next runtime evidence source is the [pinned Task implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs).
Its Action overload of Run selects the default scheduler and denies child
attachment. WhenAll uses a completion promise with a remaining-child count.
These source facts guide the pending model; this checkpoint does not bind them
as native assumptions or assign their allocation cost.

### Dominating primitive-array origins

The next phase C step admits initialized owned references across IR blocks.
A bounded native graph traversal must find a definition that dominates each
cross-block use. Dominance establishes initialization; the checker separately
validates every reference write and operand in the complete typed program.
Because reference inputs, external stores and unmodeled effects are rejected,
induction over execution keeps admitted references null or locally owned. This
admits compiler default initialization and later owned assignments, while rejecting
uninitialized reads, paths bypassing initialization, and unowned redefinitions.
Same-block checks remain unchanged. The fallback transfers only the independently
verified cumulative byte upper envelope; it claims no precise liveness or lower
bound. Source array-length/conditional-output and adversarial graph controls are
validated successfully in the 778-case native gate and four package cases.
Model v2 and cache 47 identify the new capability; wire formats are unchanged.
Full formatting, quality/API compatibility and negative fixtures, documentation
lint, 728 links and the 531-path trusted inventory passed. Source/project layout
remains the validated 2773 sources, 59 projects and 151 pinned common files.

### Parametric memory upper envelopes

The next phase C step targets primitive arrays whose extent depends on scalar
inputs. When precise finite lifetime extraction is unavailable, independently
checked cumulative allocated bytes can conservatively bound both peak auxiliary
and retained owned storage. A separate native admission check must first establish
closed array ownership: static methods, scalar entry inputs, primitive arrays,
and reference definitions/uses confined to their IR block. Borrowed inputs,
fields, opaque calls, unmodeled effects and cross-block reference flow remain
Unknown in this fallback. Scalar/void results retain no owned output graph.

Reuse the cumulative allocation kernel and declared layout binding; never trust
a producer-supplied upper bound or claim a tight/lower bound from this transfer.
Keep the precise lifetime path first. Add native proof-core provenance for the
transfer, adversarial origin/ownership tests, symbolic source contracts and a
packaged consumer. Keep all phase C/F and 100% coverage requirements open while
this admitted family is implemented and qualified.
The native fallback is implemented and passed all 651 selected worker/resource/
protocol cases. Parametric Boolean/integer arrays and aliases within the same
allocation block project linear upper growth. Loop temporaries use their checked
cumulative envelope; scalar/void outputs retain zero owned bytes. Tests reject
borrowed/receiver/cross-block origins, reference reads before allocation, oversized
graphs and absent/unverified cost programs, and preserve the precise path first.
A scalar array-length case crossed a compiler-generated block boundary and remains
explicitly Unknown; it is not silently widened into the admitted family.

Layout passed at 2771 source files, 59 projects and all 151 pinned common files.
The new checker is explicitly trusted; the inventory passed at 530 paths.
Quality/API compatibility and negative fixtures passed. All four package cases
passed: two parametric memory projections (strict/advisory and JSON/SARIF O(n))
and both revised deadline fixtures. Full repository formatting passed. Cache 46 invalidates earlier capability results;
protocol, artifact, resource-expression/program and manifest formats are unchanged.

Published e55f02609 passed canonical, package, docs and security CI, but coverage
again failed in deadline controls before a usable aggregate. The output-drain
helper now anchors completion to the parent operation's monotonic timestamp, so
startup consumes its wait interval. The active-process test checks child death
by the original process deadline and grants bounded publication cleanup a separate
observation window. Production deadlines are unchanged. Both repaired fixtures passed with the
parametric package cases; exact-head instrumented CI validation remains required. Last measured aggregate remains
4734/4953 lines (95.58%, 219 gaps); the 100% floor remains unchanged.

### Deferred intermediate capture scopes

Factories now admit a single nested chain with read-only captures declared by
deferred functions. The planner classifies each captured symbol by its declaring
function scope, counts only factory-owned root fields, and reserves a root cache
slot only for nested delegates whose free captures share that root. Child frames
belong to later invocations. Sibling intermediate scopes, mutable captured
bindings, local functions, async bodies and factories without an admitted root
remain Unknown; this does not prove later invocation costs.

Four independent compiled controls check field/cache/parent relationships and
measure factory and invocation allocations separately. Scalar parameter/local
scopes allocate 88 bytes at factory completion; a root cache slot makes 96 bytes;
a captured owned two-element Int32 array makes 128 bytes. Source/native checks
cover allocated, auxiliary and retained bounds for direct and called factories.

Final verification passed all 620 selected worker/resource/protocol cases,
782 IR tests, 2165 frontend tests and 1454 analyzer tests. All nine package cases
passed: seven resource consumer variants plus both deadline/publication controls
that failed in published-head CI. Quality/API compatibility, negative fixtures,
full repository formatting, documentation lint and all 728 local links passed.
An initial IR restore failed with exit 139 before compilation; the serialized
retry and final suite passed. No production files or projects were added; the
2769-source/59-project layout and 529-path trusted inventory remain applicable.

Cache 45 invalidates earlier capability results. Protocol 39, compiler artifact
52, resource program 8, expression 1 and manifest 6 keep their formats. This
checkpoint still needs publication and exact-head CI. Broader lifetime/call
composition, phase F and final coverage/audit requirements remain open.

Published head bf8636c2e passed its three semantic coverage shards, but the package
shard failed in two deadline/publication controls: the output-drain fixture did
not produce its child-completion marker, and the refutation fixture hit a
launcher result-publication timeout. The failure log is preserved, and both
cases are included in the current local package run. This head produced no
usable aggregate; the latest measured qualification remains 8d73b0049 at
4734/4953 lines (95.58%, 219 gaps), with the 100% floor unchanged.

### Current-receiver factory call composition

The source-call extension admits direct calls on the current receiver to methods
that are not virtual, preserving argument binding and shared cost-analysis budgets. Virtual,
overriding and arbitrary receiver calls remain outside this admission. Finite
memory extraction now follows pure-copy liveness backward so unread receiver
entry/current/pre-state chains do not invent owned input graphs. Any observed
alias still requires ownership. New controls cover explicit and implicit this,
borrowed copy chains and virtual/arbitrary receiver rejection. The initial
liveness implementation incorrectly removed an owned graph's last read; the
existing 72-byte peak regression caught that change. The repaired rule preserves
all owned reads/roots and rejects unowned overwrites of existing owned roots.
All 778 IR tests and all 594 selected worker/resource/protocol cases passed on
the guarded implementation. All three packaged-consumer variants passed,
including the current-receiver call with constant argument substitution and
strict/advisory JSON/SARIF controls. The full analyzer suite found the obsolete
instance-call rejection expectation; supported-call assertions and separate
virtual/arbitrary receiver rejection cases replace it. The complete analyzer
suite then passed all 1454 tests. Quality/API compatibility, negative fixtures,
full repository formatting, documentation lint and all 728 local links passed.
There are no new production files or projects; the prior 2769-source layout and
529-path trusted inventory remain applicable. Exact pushed-head CI and aggregate
coverage qualification remain required. Cache 44 invalidates
earlier capability results. This remains part of phase C; all other unchecked
roadmap and qualification requirements remain.

Published instance-factory head 8d73b0049 passed all four coverage shards but
failed the unchanged aggregate floor at 4734/4953 changed trusted lines (95.58%,
219 gaps). Its report is preserved separately from predecessor f72dbc694.
The new current-receiver changes have no measured aggregate claim yet.

### Instance factory lifetime extension

Instance factories now use the shared-root closure model only when deferred
captures belong to read-only factory parameters or initialized locals. Any
instance reference inside a deferred body rejects this plan. A native memory
rule ignores pure variable copies into unread locals, allowing unused receiver
entry/current/pre-state bindings without inventing an owned input graph.
Observed borrowed references remain Unknown. Source-level controls cover all
three byte/lifetime metrics and explicit/implicit receiver capture; an independent
compiled-frame control checks the actual root/cache fields and shared target.
All 772 IR tests passed, including borrowed-copy acceptance and rejection.
All 584 selected native/resource/protocol cases passed, including the instance
factory bounds, receiver-capture rejection and independent compiled-frame
control. All 2165 frontend tests and quality/API compatibility checks passed.
The complete attribute suite passed 44 tests, including declared weight
boundaries and invalid/empty probability distributions. Both packaged-consumer
cases passed, checking static source-call and instance factory retained bounds
under strict/advisory policy and in JSON/SARIF. Layout passed with 2769 source
files, 59 projects and all 151 pinned common files unchanged. The trusted-source
inventory passed with 529 classified paths. Full repository formatting passed.
Cache schema 43 invalidates earlier capability results; wire formats are unchanged.
Broader capture/call lifetimes, phase F and 100% coverage qualification remain open.

Published head f72dbc694 passed all four coverage shards but failed aggregate
qualification at 4724/4952 changed trusted lines (95.40%, 228 gaps). The report
is preserved. The 100% floor and trusted-source inventory are unchanged;
these newer local tests have no claimed measured coverage gain yet.

Nested root-capture lifetime support is implemented for the admitted shared-root
scope. The root-frame planner now checks
each deferred anonymous function and admits up to 16 nested functions only when
all captures belong to the same read-only factory scope. Intermediate captures,
local functions, async functions and unproved placements remain Unknown. The
factory counts its root frame, deferred delegate cache slots and immediately
returned delegate; inner instances belong to later invocations. Compiled-frame
controls exposed the extra cache field, which is now included and initialized
to null in IR. Corrected frame storage and bare-lambda costs passed all 807
resource/native/protocol/cache controls, including independently compiled one-
and two-level cache observations. The published CI found a missing exported
input-model catalog entry. Its non-selecting category and exact surface snapshot
are corrected; all 2165 frontend tests, all 1451 analyzer tests and quality/API
gates passed. Full formatting and three package workflows are completing; their
results are not claimed yet. Cache 42 invalidates older Unknown results;
protocol 39, compiler artifact 52 and resource program 8 keep their formats. This
step does not complete the broader capture/call lifetime requirement in phase C.

Nested lifetime implementation d861d40ea was merged with remote branch update
22fc71906, preserving latest master b0ce85e23 (immutable KeyValuePair getter
models). Published merge 9036caa5d includes both changes. The 807/2165/1451 local
results above predate that latest integration; exact-head and relevant spec
validation are still required. No history was rewritten.

### Packaging and kernel-validation follow-up

After the remote integration, the five targeted KeyValuePair/runtime-digest spec
controls passed. Full formatting and 728 documentation links passed. A minimal
constant-argument nested factory also passed native verification. The new package
fixture initially omitted PlatformTarget=x64, so its Unknown reason was
ResourceLayoutAssumptionsNotBound. The fixture now supplies x64, checks strict
SP0048 rejection of the declared layout dependency, and checks advisory proof
and SARIF using the same policy as existing layout package workflows. That
corrected package check passed, including strict rejection, advisory Proven and
96-byte result/SARIF projection. No product admission rule was weakened.

Fourteen new memory-kernel controls exercise stored integer/Boolean facts and
negated comparisons before path selection. All 14 targeted cases and the full
770-test IR suite passed. They target gaps in the last completed coverage report;
no numerical coverage gain is claimed before exact-head measurement. Broader
capture/call composition, phase F and final coverage/audit requirements remain.

### Conditional expected-cost checkpoint

Conditional expected upper bounds are implemented for the admitted single-Boolean
input model. A parameter-level
ResourceBooleanInput attribute declares one Boolean marginal using bounded
integer true/false weights, conditional on the other entry inputs. The compiler
specializes both immutable entry valuations and retains their maximum as the
ordinary resource proof. A root-only typed distribution carries both programs;
the native worker checks ownership, derives both costs and computes the ceiling
of their weighted expectation. Nontrivial input contracts, unsupported costs,
invalid models and mutations have explicit Unknown boundaries. Protocol records
separate expected and worst-case upper bounds and bind declared model evidence.
Protocol 39, cache 41, compiler artifact 52 and resource program 8 invalidate the
previous formats. Initial verification passed 39 source, independent weighted-execution,
malformed-certificate, input-support and cache/SARIF controls, plus all 756 IR
tests. Quality/API, negative fixtures, layout, the 528-path trusted inventory,
documentation lint and 727 links passed. Layout verifies 2762 sources, 59
projects and 151 unchanged pinned common files. Broader verification passed all 794 worker/native/protocol/parameter-attribute
cases, including final record-digest controls, and the full formatting gate.
Final verification passed all 1451 analyzer tests and seven strict-package
workflows, including expected-cost JSON/SARIF, accounting and lower/tightness.
The documentation example is independently compiled as part of the analyzer
suite. Final documentation lint, 727 links and an edited-file formatting check
also passed. Implementation 1e33c6a71 and fixture corrections 4b4bc7930 are
published in draft MR #334.

Latest fetched master 59353cb05 adds constrained integer comparable calls and
receiver-mutation behavior. Merge 0e4f70ef2 preserves both this branch's initializer
stores and master's specialized field binding. The local checkpoint above
predates that merge. Post-merge verification passed 2070 resource/closure/
expected-cost/comparable/aliasing/specified-call worker cases and 21 constrained
comparable frontend cases. Layout passed 2766 sources, 59 projects and 151
unchanged pinned common files; the trusted inventory classifies 529 paths, and
documentation checks passed 728 links. Exact-head CI must qualify the resulting
published commit ce51bfd72; documentation and toolchain-security CI passed.

The completed expected-cost row covers conditional upper expectations under
the stated Boolean model. It does not establish expected lower bounds or
tightness. Broader lifetime composition, async/concurrency, coverage and final
audit remain required. Fresh exact-head CI remains unqualified; the prior
accounting coverage result below is still the latest completed qualification.

### Credit-accounting checkpoint

Credit-potential accounting is implemented for the admitted unit-spend model.
Program 7 adds a typed credit loop
with initial credit, fixed deposit, width and overflow mode. The native worker
checks nonnegative potential and derives the total unit-spend budget from the
outer rank. Source extraction protects the credit against other writes and
aliases. Prefix/drain costs compose separately, with explicit Unknown for
unsupported transitions or unproved wraparound. Accounting evidence is bound to
the resource-program proof and validated independently in protocol records.
Protocol 38, cache 40 and compiler artifact 51 invalidate prior formats.
Verification passed 750 worker/native/protocol/cache/SARIF cases and all 756 IR
tests. Layout passed 2757 sources, 59 projects and 151 unchanged pinned common
files; the trusted inventory classifies 524 paths. Quality/API checks, negative
fixtures, documentation lint and 726 links passed. Package lower/tightness
controls passed three cases. The two new package accounting cases first exposed
an incorrect test expectation: the canonical spend formula is max(0, n), which
preserves zero iterations for negative n. Both expectations were corrected.

Docker Desktop exited during verification. After recovery, the merged branch
passed all 1450 analyzer tests, 12 focused source accounting controls including
layout-bound allocated bytes, and both corrected strict-package consumer cases.
Together with the earlier 750 worker and 756 IR cases, these verify the admitted
accounting behavior and its cache/SARIF/package projection. The full formatting
gate also passed after recovery, and the final documentation check passed 727
local links.

Implementation commit f3203c870 was followed by merge e7b781538 of latest fetched
master cf42dd52d (CI critical-path changes). Draft MR #334 targets this checkpoint.
The later progress commit 0a35263ad passed canonical, package, documentation and
security CI. All four coverage shards passed, but aggregate qualification failed
at 4503/4715 changed trusted-code lines (95.50%, 212 gaps). Coverage qualification
and final CI remain open. The 100% changed trusted-code floor is unchanged.

The completed accounting row covers fixed positive deposits and positive-guard,
unit-spend drains under its stated model. It does not imply arbitrary potential
synthesis or expected-cost results. Broader lifetime composition, validated
expected costs, async/concurrency, coverage and final audit remain required.

### Lower/tightness checkpoint

Consumer lower/tightness evidence is implemented and verified for the admitted
loop-entry, primitive-array-count and element-count model.
The native normal-domain checker admits nonnegative scalar/counter extensions
with ideal primitive allocation, immutable snapshot facts and proved guard
pruning. It rejects finite input contracts, mutable divisors, casts to zero,
opaque operations, heap reads and upper-only regions. Numeric upper proofs remain
independent of Unknown lower domains. Native loop, array-count and element-count
claims publish Omega; Theta requires matching proved orders. Conservative
dependent-loop results carry ResourceTightnessUnproved. Protocol 37 and cache 39
invalidate older responses; artifact 50 and program 6 are unchanged. Initial
verification passed 12 admission controls and 641 native/protocol/cache/SARIF
cases. Final verification passed all 654 worker cases, 1450 analyzer tests and
seven strict-package workflows, including a symbolic array extent. Quality/API,
full formatting with a final edited-file check, layout, documentation lint and
726 links passed. The new domain checker is classified in the 522-path trusted
inventory; layout has 2754 sources, 59 projects and 151 unchanged pinned common
files. Scoped failure-choice tests reject foreign markers, origins, effects,
variables, exception kinds and allocation sites. The ideal array model selects
only the checked source-bound size-limit success continuation.

The completed asymptotic row covers this explicit model, with Unknown outside
admission. Broader lifetime composition, amortized accounting, validated expected
costs, async/concurrency models, coverage qualification and final audit remain
required. No expected-cost or amortized result is implied by Omega/Theta evidence.

The previous foundation checkpoint f0d505b0c passed canonical, package, docs and
security CI. All four coverage shards passed, but aggregate qualification failed
at 4081/4265 changed trusted-code lines (95.69%, 184 gaps). The next published
469b37ca9 checkpoint also passed all four shards, but aggregate qualification
failed at 4321/4529 (95.41%, 208 gaps). The 100% floor remains
unchanged and fresh exact-head CI is required for the consumer extension.

### Foundation and local-capture checkpoints

The lower-bound foundation is implemented: program model 6 adds separate lower array extents
and loop-rank size envelopes. Native derivation validates ownership and ordering
and composes conditional normal-completion lower costs. Upper-only regions retain
their numeric upper proofs without contributing guaranteed work. Early returns,
short-circuit evaluation, dependent counters and source-call substitutions have
adversarial controls. Protocol 36, cache 38 and compiler artifact 50 invalidate
older certificates. Verification passed 542 native/protocol cases before one
reflective fixture repair, all 96 program/allocation cases after that repair,
1450 analyzer tests, 756 IR tests, 81 worker golden cases and six package workflows.
Formatting, API/quality checks and 726 documentation links passed; the quality
run's spelling finding was corrected and verified with targeted documentation
lint. Consumer lower/tightness
evidence, feasible input-domain admission, amortized and expected-cost proofs remain
required; none of the phase E checkboxes is complete yet.

CI for local-capture checkpoint 7c6e0c9a6 passed package, docs and security, but
canonical and semantic-worker coverage failed on two stale artifact-schema golden
headers (48 instead of 49). Those fixtures now expect schema 50 and all 81 worker
golden cases pass. No new aggregate coverage percentage is available for that failed
shard; 8b3561e76 remains the last numerical qualification result. Master
1b292a43f (cached default comparer values) was integrated in 0e89bcc93. The
combined tree passed 803 worker cases, 1450 analyzer tests, 756 IR tests and eight
default-comparer SMT cases. Layout validates 2753
source files, 59 projects and 151 unchanged pinned common files; the trusted
inventory remains 521 paths and documentation checks 726 local links. Formatting
and all quality/API checks passed, with the final progress-note spelling repair
verified by targeted lint. Fresh exact-head CI is required after publication.

Root closure lifetime composition now includes initialized, read-only locals.
Entry frame allocation and ordered default stores precede initializer updates,
preserving allocation order. Extraction and replay check updates independently
against the certified slot set; unregistered fields and self-frame edges fail
closed. Parameter-only frames retain immutable capture initialization. Owned
arrays, objects and aliases compose with source factories. Debug and Release
runtime controls independently measure the 120-byte frame/delegate/array case.
Protocol 35, cache 37 and compiler artifact 49 invalidate prior evidence.

Verification passed 486 worker cases, 21 additional runtime/factory controls,
756 IR tests, 1450 analyzer tests, two real package workflows, six final factory
return cases, the quality/API and full formatting gates, and 726 documentation
links. The trusted production inventory remains 521 paths.
The lifetime row remains open for broader capture/call composition. Remaining
phase E/F requirements and the unchanged 100% changed-TCB coverage gate remain required.

The published boundary checkpoint 8b3561e76 passed canonical, package, docs and
security CI. All four coverage shards passed, but aggregate qualification failed
at 3943/4119 changed trusted-code lines (95.73%, 176 gaps). This improves on the
previous 3926/4119 checkpoint; it does not meet the 100% floor. Fresh exact-head
CI is required after publishing this local-capture extension.

The branch now includes native asymptotic upper evidence, structured protocol
validation and trusted string-length size/contract binding, in addition to the
expression foundation and packaged SP0060 activation repair. Before the latest
master merge, verification passed 828 affected worker tests, 748 IR tests and five
strict native growth/stack/string package workflows. The full analyzer run passed
1429 cases plus the separately repaired protocol-seal assertion. The combined tree
then passed 920 worker, 1430 analyzer, 10 architecture and 11 package/closure cases. Independent lower-bound/tightness evidence, amortized accounting and
validated distributional models remain required.

The previously published stack checkpoint eec823652 passed canonical/package/docs/
security CI, then failed the unchanged 100% changed-trusted-code requirement at
3720/3910 covered lines (95.14%, 190 gaps). Fresh exact-head CI is required for
this checkpoint. The draft MR remains [#334](https://github.com/alexyorke/SharpProof/pull/334).

Remaining required work is the unchecked phase C/E/F scope and final coverage/CI
qualification. The checked phases describe explicitly admitted models with Unknown
outside admission; they do not claim arbitrary C# analysis. Details and evidence
for each extension follow below.

### Historical initial checkpoints

Branch: `codex/resource-bounds-milestone-one`; initial master: `cd3363c63`.
Milestone-one implementation: `cd7901b72`; planning document: `488f4300e`.
Draft MR: [#334](https://github.com/alexyorke/SharpProof/pull/334), initially
published at `6118749b5`. CI was still running at the first inspection.

Loop generalization and source-call size substitution passed all 83 focused
resource tests with zero build warnings/errors. This includes independent small
executions, checked/unchecked overflow controls, dependent allocations, symbol
substitution, argument-evaluation allocation costs, type-initializer abstention,
and a shared call-expansion budget. The initial loop-only run's invalid C# case
was replaced with a valid Int64 overflow control before this passing run.

Decreasing-while ranking rules and guarded tail-recursive allocation bounds passed
101 focused tests, then the analyzer suite passed 1238 tests after consumer
projection and final-expression-budget controls were added. Independent controls
include negative/positive inputs, 138 combinations of loop sizes and strides,
and 19 recursive executions.

The metadata catalog and assumption projection then passed all 108 focused
resource tests. Source evidence is now pinned to runtime commit
`b9a49e4a6d3a444a3685e318c74a7892d01873f7` (.NET 8.0.32). The final full analyzer
run for this checkpoint passed all 1243 tests with zero build warnings/errors,
including 108 resource cases and the immutable source pin.

CI for initial MR head `6118749b5` exposed two issues: spelling in new prose and
the corpus host enabling SP0060 while normalizing every diagnostic to warnings.
The host now preserves opt-in disabled defaults. The corrected full Gates suite
passed all 471 tests, and the canonical lint/quality gate passed. No assertions
were weakened and no existing corpus snapshots were rewritten to hide the extra
diagnostic. Updated docs and evaluated layout also passed with the metadata
files: 2616 source files, 59 evaluated projects, 151 pinned common files.

Checkpoint commands use `eng/scripts/Invoke-SharpProof.ps1` with the canonical
container environment recorded in the milestone-one document:

| Arguments | Verified result |
| --- | --- |
| `test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj -TestFilter FullyQualifiedName~ResourceBoundsTests` | 108 passed |
| `test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj` | 1243 passed |
| `test -Target src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj` | 471 passed, including the CI regressions |
| `lint` | All quality checks and negative fixtures passed |
| `docs` | Drift, links and automation lifecycle checks passed |
| `layout` | 2616 source files, 59 evaluated projects, 151 pinned common files |

The full goal is not complete: the unchecked phase A certificate requirement and
all phase C-F requirements still need implementation and verification. The MR
remains draft. These passing checks establish this checkpoint, not completion of
the roadmap.

## Next implementation dependencies

The native proof representation (phase D) must precede quantitative memory proof
composition (phase C); both phases remain required. Introduce compiler-neutral
resource expression and proof nodes with validated input identities, sizes,
arithmetic, budgets and serialization. The current presentation strings must
never become unchecked worker assumptions. Wire that representation into the
existing portable analysis before the collector and independent worker checker.

Memory evidence must distinguish cumulative allocation, logically reachable
live space, retained output and stack frames. Byte bounds require an explicit
versioned layout assumption; source-level reasoning alone must not claim an
exact JIT stack layout or physical GC high-water mark. Native claim ownership,
runtime compatibility, contract validation, protocol/cache identity and SARIF
projection remain open. All asymptotic/aggregate and async/concurrent/measurement
requirements in phases E-F also remain open.

## Native expression foundation (in progress)

Resource arithmetic now has a compiler-neutral tree in `SharpProof.Ir`, using
nonnegative mathematical integers rather than parsed display text. Input identity
consists of parameter ordinal, scalar/array/string size kind and physical domain;
renaming a display label does not change identity. Addition, multiplication,
maximum, positive difference, ceiling division, integer digit/log counts and
alignment have explicit semantics. Construction, evaluation and serialization
have bounded depth, node count, numeric representation and text size. Cancellation
propagates rather than becoming an Unknown proof or malformed-input verdict.

The portable analysis uses these nodes internally, and proven SP0060 results
publish expression model version 1. Its shared implementation now lives in
`SharpProof.Compiler.Semantics`, accessible to both the analyzer and collector
without adding a compiler-artifact dependency to the analyzer core. The resource
cost model remains v3 because its measured units and admitted source behavior
have not changed.

The strict artifact codec rejects unknown or duplicate fields, wrong versions,
invalid operators/arity/domains and payload budgets before constant folding.
Its owned-callable entry point additionally checks the callable ID, concrete
body, parameter map, input ordinal/type and exact physical domain. Display names
provide no ownership evidence. The preparation must already belong to the
validated manifest envelope; matching a caller-provided label cannot establish
external source ownership.

This is an arithmetic and input-binding foundation, not a native resource proof.
It does not yet emit resource claims from the collector or verify their cost
composition in the worker. The phase D proof representation checkbox remains
open until those parts and adversarial consumer tests are implemented. Memory,
asymptotic, amortized, distribution, async, concurrency and measurement phases
remain required.

The first expression checkpoint passed 15 focused IR tests and 126 focused
analyzer/resource-codec tests, including the source-analysis-to-codec bridge.
The same 126 tests passed again after the shared semantic layer move. With
ownership and numeric-budget controls added, all 132 focused resource/codec/
ownership tests, all 497 IR tests, and all 10 architecture tests passed with zero
build warnings/errors. The full analyzer suite passed all 1267 tests with zero
build warnings/errors. Evaluated layout passed with 2622 source files, 59 projects
and 151 pinned common files; documentation drift/lifecycle checks also passed.
All quality checks except markdown passed on the first run. After correcting one
heading's blank-line spacing, the pinned markdown engine checked both changed
documents with zero findings. Package compatibility and negative gate fixtures
passed. The published predecessor `bf966074c` still had four CI checks pending
at the latest inspection (25 successful, four skipped, no failures).

## Closed resource programs and native derivation (in progress)

The compiler now emits a resource program alongside its presentation bounds.
Nodes describe zero cost, array allocation, sequence, branch alternatives and
repeated regions. Repeats carry initialization, strict guard, positive update,
counter width and overflow mode rather than a producer-asserted trip count.
Source-call expansion and input substitution apply to this program too. Guarded
tail recurrence uses a separate repeat kind so it does not invent loop entries.

This is an upper-envelope lowering: early exits can shorten a source execution,
and dependent sizes can be replaced by conservative enclosing-counter envelopes.
The compiler lowering remains in the trusted boundary for preserving allocations,
closed operation admission and counter stability. A large modeled upper cost
alone cannot refute a source contract; refutation still requires owned replay
of an actual violating execution.

An independent worker module checks rank domains, monotone progress and overflow,
then derives all three cumulative metrics from the nodes. Its strict program
codec binds program/expression/cost-model versions and rejects malformed fields,
arity, payloads and raw tree budgets before simplification. Native input binding
uses the concrete owned callable's parameter types and domains. Conditional
metadata leaves an explicit boundary with native Unknown until its provenance is
bound, preserving the analyzer's existing conditional results.

The first 38 focused native tests passed, including independent small executions,
checked-prefix versus unchecked-wrap controls, source-to-worker agreement and
malformed transport controls. The expanded run passed all 41 cases, adding
source-call, metadata-boundary and construction-budget controls. The full
analyzer regression suite passed all 1267 tests with zero build warnings/errors.
Evaluated layout passed with 2626 source files, 59 projects and 151 pinned common
files; documentation drift and lifecycle checks passed. All 497 IR tests and
10 architecture tests passed. Both source-level metadata-boundary assertions
passed after they were added. Nine quality categories passed initially; one
prose spelling finding was corrected, then the pinned spelling checker passed
all 71 configured files and markdown checked the changed roadmap with no findings.
The published predecessor `9f7a9769e` still had CI running at the latest
inspection, with no failures. The native resource-program consumer workflow
remains pending as described below.

Collector attachment, resource contracts, protocol/cache/SARIF integration,
native metadata assumptions and replayed numeric violations remain open. This
module verifies a supplied closed program; it is not yet a published native
resource-claim workflow. Broader ranking synthesis and all required phases C-F
remain incomplete.

The checklist and first checkpoint entry describe the current state. Older
checkpoint entries retain their earlier statuses, versions and verification
results as implementation history.

## Native resource contract integration (in progress)

The public API now declares constant or linear bounds for loop-body entries,
primitive-array allocations and allocated elements. Trusted compiler identity,
parameter binding, independent attribute outcomes and invalid-metadata abstention
use the existing Effects selection path. Resource programs are attached only to
selected resource contracts. The consumer binds their expressions to concrete
callable parameter types and rejects unowned records, mismatched constraints and
compiler-supplied proof or trusted-cost verdicts.

The native checker derives cumulative costs, then applies sufficient nonnegative
arithmetic rules to prove the declared inequality. Failure of that comparison
is Unknown, not a numeric refutation. Compiler-elided clauses and their arguments
are omitted through the repository's existing conditional-call policy. Enabled
conditional calls retain their costs.

Protocol 14, manifest 6, cache 16 and compiler artifact 32 bind the new declaration,
program and result shapes. Resource evidence reaches validated worker responses,
cache hits and SARIF, with metric/model identities and declared-bound digests.
Quantitative memory/stack and later phases remain open. Replayed numeric
violations, native metadata assumptions, broader ranking synthesis, packaged
consumer coverage and final protocol/cache qualification are still required.

The first integrated run verified the full worker/cache/SARIF path and most
focused cases. Tests were corrected to exercise the untrusted consumer decoder
and production parse options; the versioned manifest hash fixture was updated.
The full IR suite now passes 502 tests, including independent controls over 256
linear coefficient/constant combinations. The final focused native/protocol run
passed all 181 tests. The full analyzer suite passed 1271 tests; architecture
passed 10 tests; documentation, evaluated layout and all quality/compatibility
categories passed. Final layout evaluated 2637 source files and 59 projects.

A strict consumer built from the actual NuGet packages passed: all three metrics
produced Proven results and validated JSON/SARIF evidence. The full worker suite
passed 5675 tests and failed only three old schema-number fixtures. Their schema
values were corrected without changing verdict assertions; all 82 targeted
schema/golden tests then passed. Additional arithmetic-domain and malformed-record
boundary controls passed with the full IR suite, now 508 tests. All recorded
builds reported zero warnings/errors. A final real-worker/cache/SARIF control
also passed after null-constraint validation was hardened.

CI for published predecessor `80a15d016` failed the required 100% coverage gate:
91 resource-code lines were uncovered. The exact report has been inspected;
targeted boundary coverage work is in progress. The coverage baseline is
unchanged. Coverage qualification, native numeric refutations and every remaining
unchecked roadmap requirement still block completion; the MR remains draft.

## Boundary coverage and CI hardening (in progress)

The next checkpoint adds explicit malformed arithmetic/program transport controls,
aggregate encoding budgets, detached-record validation, rank-domain/overflow
controls and native claim ownership/Unknown controls. Supported primitive
expression forms and unsupported loop updates also have direct regressions.
Encoding reuses the immutable trees' expanded-node/depth invariants while
retaining aggregate payload limits. Decoding still enforces raw budgets before
folding, and its admitted arithmetic operators are explicitly closed.

The diagnostic resource coverage invocation initially ran static collectors in
parallel and produced compiler-reference identity mismatches. Repeating the same
filter with `SHARPPROOF_TEST_PROJECT_PARALLELISM=1` eliminated those failures:
90 worker resource tests, 151 analyzer resource tests and the actual packaged
resource consumer passed. This filtered run is diagnostic evidence only; it does
not qualify the repository-wide coverage gate. The separate Attributes phase
then exposed missing XML property-value documentation, now corrected; all 21
Attributes tests passed. Coverage floors remain unchanged.

Published head `e3abf12a4` also failed the packaging job's formatting prerequisite
at `ProtocolModel.generated.cs:805`. The exact CI job log was inspected and the
canonical formatter with `--include-generated` applied the missing line break.
Final suite and gate results are recorded below. Coverage qualification is
still in progress. No remaining roadmap phase is marked complete by these
boundary fixes, and numeric violations still require exact owned replay.

The full analyzer suite passed 1301 tests and the full IR suite passed 512 tests.
After adding the exact payload-boundary control, all 170 focused analyzer
resource tests passed. All 75 focused native resource tests and all 32 Attributes
tests passed, including direct constant/linear construction and rejection paths.
Encoding accepts an exactly one-MiB expression envelope and rejects one extra
byte; the writer reserves the envelope's closing byte explicitly. Builds reported
zero warnings/errors. All ten lint/quality/compatibility categories passed, final
changed-source formatting (including generated source) passed, and layout passed
with 2638 source files, 59 projects and 151 pinned common files.

The next inspection of CI for `e3abf12a4` found two exact frontend catalog parity
failures: the attribute roster omitted ResourceUpperBound, and the shared effect
category test assumed every attribute also targeted constructors/properties.
The expectations now include the new attribute and check its declared method-only
target separately while retaining the existing targets for all other effects.
The serialized diagnostic coverage run completed its tests and report export;
its repository-wide floor check failed as expected for a resource-only filter.
That report predates the final native/Attributes controls and is not final
coverage qualification. Documentation drift and automation lifecycle checks passed.

After the catalog expectation fixes, all 2052 frontend tests passed with zero
build warnings/errors.

## Exact numeric array violations (in progress)

The native contract path now attempts exact replay after its universal upper-bound
comparison remains unproved. The first admitted refutations are cumulative
primitive-array allocation counts and allocated elements. Internal IR events fire
only after successful allocation and include the evaluated length and sticky
approximation state. Replay rejects a candidate unless its typed entry values,
receiver constraint, declared disjointness, Requires values and definedness guards
hold. Search uses bounded concrete candidates and native feasibility/allocation
models; neither search exhaustion nor an envelope comparison becomes a proof.

Refuted evidence carries canonical observed-prefix and evaluated-entry-limit
values, a concrete source witness and the replayed model. Protocol 15 and cache
17 bind the new response shape; manifest 6 and compiler artifact 32 are unchanged.
Validation rejects noncanonical values, values that do not exceed the bound,
missing numeric witnesses and incompatible outcome fields. Cache hits and SARIF preserve the
numeric comparison and source location.

The loose-envelope control exposed a missing Requires guard in the initial new
replay path. Direct concrete evaluation of all owned Requires/definedness terms
was added before any observation could be accepted. With that correction, all
84 focused native/program/worker-cache-SARIF controls passed with zero build
warnings/errors. Broader gate results are recorded below. Phase D remains open:
loop-body-entry violations still need exact source-entry instrumentation, native
metadata assumptions remain unbound, and all other unchecked roadmap work is
still required. CI for checkpoint `ac99f280e` was cancelled across its workflows;
those cancelled runs do not qualify the published checkpoint.

Final checkpoint gates passed: 5702 full worker tests, 1302 analyzer tests,
517 IR tests and 10 architecture tests. Independent allocation-event controls
passed within the 70 focused total-execution cases. Both actual NuGet strict
consumers passed: valid bounds build with three Proven metrics, while violated
array count/element bounds fail the build with two Refuted claims and concrete
JSON/SARIF metric evidence. All builds reported zero warnings/errors.

Layout passed with 2639 source files, 59 projects and 151 pinned common files.
Documentation drift/lifecycle and final changed-source formatting passed. The
quality run passed every category except one spelling finding in this document;
after rewording it, the pinned spelling engine checked all 71 configured files
with zero findings. The 100% coverage baseline is unchanged and final coverage
qualification remains required. No full-roadmap completion is claimed.

## Exact source-loop-entry violations (in progress)

Resource-selected total lowering now identifies admitted C# for/while guards by
their owned source syntax and typed strict comparison. A compiler-owned semantic
marker records the true edge's body-entry meaning and original source span. The
shared source-call session preserves these annotations through inlining. Ordinary
lowering keeps its prior operation sites. The worker observes marked true edges
only after condition evaluation and retains the sticky approximation state.
Neither an ordinary conditional, a jump to an earlier block nor a loose upper envelope invents
a source body entry. Guard annotations are trusted lowering semantics, not an
independent progress certificate; the native upper-bound checker still validates
rank/overflow evidence separately.

Numeric loop witnesses use no allocation/effect flags. Resource-specific protocol
validation checks the metric/flag relationship without allowing empty qualitative
effect witnesses. Protocol 16, cache 18 and compiler artifact 33 bind these
semantics; manifest 6 and the v3 cost units remain unchanged. Schema-only golden
expectations were updated without changing verdicts.

The first 46 focused native contract tests passed, followed by 301 native,
protocol, worker/cache/SARIF and golden controls. Independent total-execution
controls passed all 74 cases. Controls include parenthesized/Int64 guards,
non-unit/geometric/descending progress, dependent nesting, inlined callees,
checked-overflow prefixes, early return, approximation and loose envelopes.
Broader gate results are recorded below.

CI aggregate failures for published `0e69f12d0` were traced to cancelled upstream
jobs: the build aggregator received cancelled quality/main/package/Gates/
self-application jobs, and the coverage aggregator received cancelled shards.
These runs contain no completed cohort/coverage qualification and do not establish
a code regression. Final coverage qualification is still required.

Final loop-entry checkpoint gates passed: 2052 frontend tests, 521 IR tests,
1302 analyzer tests, 10 architecture tests, the 301 focused native/protocol/
consumer/golden cases, and all three ordinary-conditional/loose-loop controls.
All three actual NuGet strict consumers passed, covering valid bounds, array
violations and loop-entry violations with numeric JSON/SARIF evidence. Builds
reported zero warnings/errors. Final changed-source formatting and documentation
drift/lifecycle passed; layout evaluated 2640 source files, 59 projects and 151
pinned common files. The quality categories passed after two technical words in
this document/guide were reworded and the pinned spelling engine rechecked all
71 configured files with zero findings.

The native source-loop-entry evidence item is complete for the admitted families.
Native metadata cost assumptions/provenance remain the next phase D item. Every
other unchecked requirement remains required, including final coverage at the
unchanged 100% baseline. The merge request remains draft.

Before publication, the remote branch advanced to `91fe3b184`, the user's merge
of master containing dictionary-state modeling (#331) and CI image caching
(#339). The local loop-entry commit rebased cleanly onto that merge. The shared
source-call session retained both the upstream dictionary changes and the new
resource-tracking flag. Integrated-state rechecks passed 2062 frontend tests,
302 native/protocol/consumer/golden cases and all three actual NuGet consumers,
with zero build warnings/errors. Earlier layout counts above describe the
pre-merge checkpoint; the remote merge is preserved in the published history.

Integrated layout also passed: 2648 source files, 59 projects and 151 pinned
common files.

The completed coverage run for published `62aef3a092` changes the earlier CI
status: all four coverage shards passed, but the aggregate gate failed. Changed
trusted-code coverage was 1597/1660 lines (96.2%) against the unchanged 100%
requirement, leaving 63 uncovered lines. This is an actual coverage gap, unlike
the earlier cancelled aggregate runs. Coverage qualification remains open.

The next implementation phase binds native metadata cost assumptions. Its closed
program representation now carries catalog/method/reference identities; the
compiler manifest declares callable-owned dependencies, and native results
project the reviewed runtime/source and captured reference hash. The compiler
artifact must preserve target-framework and reference-snapshot evidence across
serialization. General compiler references remain excluded; only the subset
used by resource metadata calls crosses the boundary. Protocol admission distinguishes the catalog's established
uppercase digest encoding from lowercase reference-image hashes. Focused native/protocol/cache/SARIF/artifact checks passed 285 cases, the
analyzer/collector checks passed 241 cases, and IR resource checks passed 40
cases. The actual NuGet consumer passed three conditional Proven results,
strict SP0048 rejection and explicitly enabled advisory verification, with
provenance in JSON/SARIF. Exact execution replay still abstains after an
unsupported metadata call; a cost summary does not supply concrete values.

Metadata-phase quality categories, 10 architecture tests, documentation drift
and 722 local links passed. Layout evaluated 2651 source files, 59 projects and
151 pinned common files. Final spelling checked all 71 configured files with
zero findings. A broad worker run caught the general-reference transport
regression; the producer now emits only the resource-used reference subset,
leaving general references excluded. The corrected full worker gate passed all 5809 tests with zero warnings/errors.
All other unchecked roadmap items and final 100% coverage remain required.

Published metadata checkpoint: `2e9ecfb61fc005a4808729e19e86e50fc51567ad`.
Its exact-head documentation maintenance and toolchain image security workflows
passed; container coverage, canonical PR gates, and package/consumer workflows
were still running at inspection. The draft MR description now records the
metadata behavior, strict/advisory policy, transport correction and coverage gap.

Coverage follow-up added four adversarial arithmetic controls: left-hand
constant multiplication is checked against independently evaluated costs, and
three reversed maximum trees must stop at the shared comparison budget. All 12
arithmetic-order tests passed with zero build warnings/errors. This exercises
two previously uncovered rule families; complete coverage must still be measured
and qualified, and no coverage percentage is inferred from these test results.

The next phase extends checked ranking synthesis for top-tested while loops.
Increasing additive/multiplicative ranks require an unchanged, exact nonnegative
constant initializer; a clamped size envelope cannot establish the actual
initial counter value. Decreasing additive ranks can use nonnegative constant
lower bounds. The compiler emits the existing typed rank certificates for native
checking. Mutation, unsupported starts, missing progress and unchecked overflow
remain Unknown. Source, independent execution, native proof and exact-prefix
replay controls passed: 190 focused source-resource tests and 129 native resource
tests, followed by the full 1322-test analyzer gate. Builds reported zero warnings
or errors. The broader ranking checkbox remains open for multi-counter synthesis.

Exact-head coverage for metadata checkpoint `2e9ecfb61` completed all four shards
successfully but failed qualification at 1766/1833 changed trusted-code lines
(96.34%), leaving 67 uncovered lines. The 100% baseline is unchanged. Its package,
consumer and release workflow passed. The local arithmetic follow-up and while
extension were not part of that measurement. The next ranking work is checked
composition of multiple counter phases; unsupported coupled updates will remain
Unknown. Remaining metadata/protocol coverage gaps are also being mapped to
boundary controls rather than excluded from qualification.

Published while-ranking checkpoint: `2848a1d8c237f9f62325bcb0b299746d04a14ce5`.
Its exact-head documentation maintenance and toolchain image security checks
passed; package/consumer, coverage and canonical PR gates were queued at inspection.

The next coverage follow-up passed two source and five native/protocol boundary
tests, with zero warnings/errors. Controls cover instance metadata receivers,
mutating metadata arguments, invalid dependency projection, null or oversized
metadata arrays, null witness identity, and real Unknown responses corrupted with
partial numeric or undefined-outcome evidence. Every mutation is rejected and the
restored response validates. No coverage percentage is inferred until the next
complete measurement; the 100% baseline and remaining roadmap requirements stay
unchanged.

## Composed counter phases

The implementation admits a top-tested loop with two distinct Int32/Int64 locals, initialized from
supported sizes, and this fixed phase selection:

```csharp
public static class PhasedExample
{
    public static void Run(int leftSize, int rightSize)
    {
        int left = leftSize, right = rightSize;
        while (left > 0 || right > 0)
        {
            if (left > 0) { /* phase prefix */ left -= 2; }
            else { /* phase prefix */ right -= 3; }
        }
    }
}
```

Each continuing phase must reach its own checked progress rule. The decrement
family uses ceiling division of the initial nonnegative size; division by a
constant greater than one uses the existing digit bound. Both counters remain
protected from all other writes, aliases and jumps. Earlier mutation, missing
steps, mismatched phase selection and coupled updates produce Unknown.

Compose the two existing typed rank certificates as a sequence of resource
repeats. The native checker derives each phase count and its prefix costs, then
adds the results. This preserves separate phase allocation/nested-loop costs
without treating a mathematical sum as a physical counter domain.

Roslyn can split the logical-or guard into separate branches. Extend source-entry
markers only where a true edge enters this source while body; ordinary body
conditionals must not add entries. Check left-only, right-only, both-positive and
both-inactive executions independently before using numeric violation evidence.

Verification must include independent small executions, constant and same-input
linear contracts, all three resource metrics, exact violating prefixes, and
explicit Unknown controls. Keep the broader ranking checkbox open until the
source, native and relevant broader gates pass for this admitted family.

The composed-phase implementation now emits a sequence of existing typed rank
certificates, with separate prefix costs and protected counter environments.
Logical-or source guard leaves preserve exactly one body-entry event; the phase
selection does not create an event. All 149 focused native resource tests passed,
including repeated inlining, early-return abstention, mixed progress, and costs
whose natural sums exceed Int64. The source run passed 218 cases before exposing
one invalid C# test operand; it was replaced with a valid wrapped negative stride.
The broader analyzer run then passed 1354 cases, with its sole failure in the
roadmap's fragmentary C# fence. That example is now standalone. The full frontend
gate passed all 2062 tests. The native restore crash was terminal and its retry
passed; it did not produce a code failure. The corrected full analyzer gate passed all 1355 tests, and both actual NuGet
consumer cases passed. Strict builds prove all three valid metrics and reject
all three smaller bounds with concrete numeric JSON/SARIF evidence.

The broader ranking item is complete for the documented increasing/decreasing,
geometric, nested and protected two-counter families. Independent controls ran
200 small two-counter executions, with separate array counts and element costs.
The native, analyzer, frontend, packaged-consumer and quality gates passed with
zero build warnings/errors. Progress for unsupported coupled updates stays
Unknown. The full roadmap remains open for quantitative memory/stack, asymptotic,
amortized and average-case models, async/concurrency, measurements and final
coverage qualification.

For published boundary checkpoint `d9760676e`, documentation, toolchain security,
package/consumer/release and canonical PR workflows all passed. All four coverage
shards passed, but qualification failed at 2198/2259 changed trusted-code lines
(97.30%), leaving 61 uncovered lines against the unchanged 100% baseline. Its
changed-line report lists no remaining gaps in the resource metadata catalog,
metadata binder, protocol validation or arithmetic-order kernel. The composed
phase implementation is not part of that measurement and needs its own exact-head
CI results after publication.

## Quantitative memory implementation plan

First introduce compiler-neutral allocation shapes for SZ arrays, ordinary
non-overlapping fields, UTF-16 strings, primitive boxing, delegates, and a fresh
closure frame plus delegate. A versioned CoreCLR 8.0.32 x64 layout model will
derive byte intervals from typed storage slots and natural size expressions.
Object field packing can make an interval loose; an upper byte bound must never
be used as an exact violating cost. Unsupported shapes or incompatible model
contexts produce Unknown with no partial interval.

The layout model is conditional on its declared runtime and architecture. Its
constants and digest bind the reviewed runtime source at commit
`b9a49e4a6d3a444a3685e318c74a7892d01873f7`. Validate it with independent allocation
controls in the canonical .NET 8.0.32 x64 runtime, including mixed fields and
empty/cached results. Instance storage excludes referenced objects unless they
are explicitly part of the allocation bundle.

Then transport typed shapes in the resource program, bind layout assumptions to
compiler-owned target/platform/reference evidence, and expose cumulative byte
contracts through native checking and consumer results. Continue with source
lowering for every category above, alias/escape and lifetime certificates,
separate peak auxiliary and retained output bounds, and declared stack models.
None of the phase C checkboxes is complete until the corresponding compiler,
worker, protocol, consumer and independent controls pass. These steps preserve
the full memory scope; the layout kernel is its initial implementation layer.

Primary layout evidence:
[object and array layout](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/object.h),
[string sizing](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/object.cpp), and
[delegate implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs).

The first memory layer is implemented: typed allocation shapes cover arrays,
ordinary non-overlapping object fields, strings, primitive boxes, delegates and
fresh closure bundles. Model `coreclr-8.0.32-x64-source-storage-v1` computes byte
intervals, rejects incompatible contexts, propagates cancellation and returns
Unknown on unsupported shapes or arithmetic budgets. Its digest is derived from
the actual model constants, storage widths, supported context and pinned source.
Closed shape transport rejects duplicate/missing/unrelated fields, undefined
storage, noncanonical arithmetic encoding, supplied byte verdicts and runtime
assertions. Shape transport alone establishes neither input ownership nor a
runtime/layout assumption; those remain worker integration requirements.

All 25 initial layout tests and 20 transport tests passed, including 13 independent
allocation controls in the canonical CoreCLR 8.0.32 x64 process. A final digest
regression increased the passing focused layout suite to 26 tests. Loose object
packing remains an interval, and an empty-string factory's cached result is not
mistaken for a fresh allocation. The full IR suite passed 554 tests before that
additional identity test; the full analyzer suite passed 1375. All quality and
compatibility categories passed with zero build warnings/errors. Layout passed
for 2656 source files, 59 projects and 151 pinned common files.

Cumulative byte contracts, compiler shape extraction, native binding/projection,
liveness/escape, retained output and stack models are still unimplemented. Phase
C stays unchecked. The next step is typed shape transport within the owned
resource representation, followed by bound target/platform/reference assumptions
and native byte contracts; continue through every allocation category before
completing the cumulative allocation item.

For published composed-rank checkpoint `45f6419c7`, all non-coverage workflows and
all four coverage shards passed. Qualification failed at 2510/2572 changed trusted
lines (97.59%), leaving 62 gaps against the unchanged 100% baseline. The memory
layer is not included in that measurement and requires its own exact-head CI.

## Owned typed-array program integration

Resource program model 3 now embeds typed allocation shapes. Primitive source
array creation preserves 1-, 2-, 4- and 8-byte element storage, including arrays
inside source calls. The strict program codec rejects unrelated shapes, mismatched
extents and noncanonical encodings. Native program validation checks shape/input
ownership before deriving any established count metric.

An internal native allocation checker independently derives conditional cumulative
byte upper bounds from those facts and the reviewed layout model. Sequences add,
choices take upper maxima, and repeats multiply by native-checked rank envelopes.
It deliberately supplies no cumulative lower bound: a branch maximum or loop
upper envelope is insufficient evidence for one. Untyped arrays, opaque metadata,
foreign inputs, invalid ranks and incompatible layouts return Unknown with no
partial bound. Cancellation propagates. The result is explicitly conditional,
not a published byte-contract verdict or proof of the deployed runtime.

This embedding changes protocol 17 to 18, cache schema 19 to 20, compiler artifact
schema 34 to 35 and resource program model 2 to 3. Manifest schema 6, expression
model 1 and the existing public three-metric cost model remain unchanged.

Initial focused validation passed 194 native resource/protocol tests and 325
source resource/compiler-capture tests. The latter includes all twelve admitted
primitive element types. Expanded compiler-to-native controls additionally cover
mixed widths, source-call substitution, repetition and branch envelopes. Final
broader and quality results are recorded below when complete.

Phase C remains unchecked. Next bind the declared runtime/layout assumption to
compiler target platform, reviewed references and owned manifest evidence; then
expose native byte contracts and projection. Continue with object/string/boxing/
delegate/closure extraction and effect accounting, liveness/escape, retained
output and stack analysis, then every remaining phase E/F requirement. No item
is considered complete merely because this infrastructure exists.

Broader validation passed all 555 IR tests and all 1387 analyzer tests. The first
full analyzer attempt terminated in NuGet SDK resolver migration with a mutex
exception; a clean retry passed with zero build warnings/errors. The worker run
exposed two golden expectations for compiler artifact schema 34; both were updated
to 35, and all 81 golden controls then passed. The final combined native resource,
protocol, compiler-artifact and golden run passed 380 tests, including direct
native-entry rejection of conflicting detached storage facts. The full worker
run is tracked separately until its terminal result is available.
All quality/compatibility categories passed, as did final C# formatting,
documentation drift and 723 local links. Layout evaluated 2658 source files,
59 projects and 151 pinned common files.

Exact published foundation `9628aff14` passed every non-coverage CI workflow and
all four coverage shards. Aggregate qualification failed at 2807/2876 changed
trusted lines (97.60%), with 69 gaps against the unchanged 100% baseline. That
measurement excludes the newer typed-program integration and cannot qualify it.
The exported local report records the complete gap list for later qualification.

## Native byte-contract integration plan

Add AllocatedBytes as public metric 4. Its initial cost model explicitly names
CoreCLR 8.0.32 x64 source-owned fresh-instance storage; it does not represent
process RSS or arbitrary runtime allocations. Existing three-metric contracts
retain their cost model. A byte contract declares a callable-owned trusted layout
dependency using the existing resource assumption identity/evidence machinery.
The strict profile retains its existing rejection policy for such dependencies.

Capture the compiler target platform, preserve the relevant reviewed core-library
reference evidence only for byte-selected compilations, and require net8.0 plus
an explicit x64 target. Bind the owned declaration, actual captured reference
identity/hash, layout digest and immutable source evidence before deriving bytes.
An incompatible/missing target, reference or assumption yields Unknown. A context
string alone cannot establish target compatibility or a deployed runtime.

The native upper-bound verifier will consume the already checked typed array
program, compare its byte envelope with the declared bound, and project the layout
dependency into validated results/SARIF. Numerical refutations require a later
exact byte-event replay; an insufficient envelope alone remains Unknown. Other
allocation families and all remaining phase C/E/F items stay required.

Coordinate protocol/cache/artifact invalidation for the new metric and captured
target evidence. Also correct the evidence program-model constant, which still
reported 2 after the typed-program codec moved to 3; add a cross-layer identity
control to prevent recurrence. Validate missing/conflicting declarations,
platform/framework/reference mismatches, ownership, cached/result tampering,
independent outcomes and actual consumers.

The completed broad worker run for typed-program checkpoint 180d4a95a recorded
5862 passing tests and the two previously identified schema-only golden failures.
All 81 corrected golden controls passed separately, as did the final 380-test
native/protocol/golden run. This is distinct from an entirely green full-suite
rerun; exact-head CI remains the authoritative delivery qualification.

## Layout-bound native byte contracts

Implemented public AllocatedBytes metric 4 with its distinct versioned cost model.
Compiler capture carries target platform and only the core-library reference
needed by selected byte claims. Discovery declares the callable-owned layout
assumption before sealing the manifest. Native binding requires net8.0, an x64
compiler target, a reviewed reference identity with a single consistent captured
module hash, and the exact owned assumption. Missing/conflicting inputs yield
Unknown. The reference identifies compiler inputs; the runtime model remains an
explicit trusted execution assumption, never detected from a context label.

Native checking now compares conditional byte envelopes to constant/linear
contracts and projects the layout digest, runtime/source model and reference
identity/hash into results. Protocol validation requires the layout dependency
for nonvacuous byte proofs, including cached results, and rejects stripped or
changed evidence. Byte and element claims retain independent outcomes. Protocol
19, cache 21 and compiler artifact 36 invalidate earlier evidence; resource
program/evidence model 3 and expression model 1 have cross-layer identity tests.

Initial combined native/protocol/golden/consumer validation passed 397 tests.
An invalid C# fixture mixing incompatible conditional array types was replaced
with separate branches before this passing run. Added symbolic alignment and
repetition cases are undergoing their final run. An actual NuGet consumer also
passed strict/advisory/x64/AnyCPU controls: strict reports SP0048 for the trusted
layout dependency; explicitly enabled advisory verification proves the x64 bound
and publishes JSON/SARIF provenance, while AnyCPU returns Unknown. Earlier
fixture formatting failures were corrected with the pinned formatter.

This completes the first array byte-contract integration, not phase C. Exact
numerical byte-event refutation still requires implementation; a failed envelope
comparison remains Unknown with no invented prefix. Continue with that replay,
then source extraction/effect accounting for every remaining allocation family,
liveness/escape, peak auxiliary and retained output, and stack models. All phase
E/F requirements and unchanged 100% coverage qualification remain open.

Final byte-contract validation passed 401 focused native/protocol/golden/consumer
tests, all 33 public attribute API tests, all 1388 analyzer tests and the actual
NuGet consumer described above. Symbolic controls prove aligned primitive-array
sizes and repeated per-iteration storage under the owned layout assumption.
All quality/compatibility categories passed with zero build warnings/errors.
Generated public API documentation was refreshed; drift and 723 local links
passed. Layout evaluated 2661 source files, 59 projects and 151 pinned common files.

Published typed-program checkpoint 180d4a95a passed every non-coverage workflow
and all four coverage shards. Qualification failed at 2987/3062 changed trusted
lines (97.55%), leaving 75 gaps against the unchanged 100% baseline. This report
excludes the newer byte-contract implementation; its exact-head CI and eventual
full qualification remain required.

## Exact typed-array byte prefixes

Byte refutation now observes successful typed IR array-allocation events. It
computes header-plus-payload alignment under the owned layout model, accumulates
actual execution-prefix costs and requires the prefix to exceed the evaluated
entry bound. Boolean and typed 8/16/32/64-bit integer storage, including char,
are admitted; unknown storage never acquires a guessed width. Unsupported storage
stops later byte evidence from crossing the missing event. The existing entry,
Requires, approximation, source-location and replay-budget guards remain required.

Both Proven and Refuted byte results project the owned layout dependency. Protocol
validation rejects decided byte evidence without that dependency, including
cached results. Protocol 20/cache 22 invalidate the earlier semantics; compiler
artifact 36 and program/evidence model 3 are unchanged because the event typing
and evidence fields already existed. This concerns the declared source storage
model, not physical JIT/GC/process allocation measurements.

Initial targeted validation passed 246 native/protocol cases, then 411 combined
native/resource/protocol/golden/cache/SARIF cases. Added controls cover zero-length
arrays, mixed widths, sequences, repeated allocations, executed branches, Requires,
and loop envelopes whose allocations never execute. Further prefix-boundary and
actual NuGet proof/refutation controls are undergoing final validation.

This advances the array byte path. Continue through object/string/boxing/delegate/
closure extraction and effect accounting, alias/escape/liveness, peak auxiliary
versus retained output, stack models and every remaining phase E/F requirement.
The full roadmap and unchanged 100% qualification remain open.

Final focused replay validation passed 417 native/resource/protocol/golden/cache/
SARIF tests, including infeasible Requires branches, prior exceptions, opaque
metadata and floating/unknown IR storage. Both actual NuGet proof/refutation
consumers passed. An initial test incorrectly expected advisory verification to
succeed for an explicit refuted contract; it now preserves and checks the existing
SP0051 build failure while verifying the concrete byte prefix and layout evidence.

Exact-head CI for c9170a1a0 exposed a separate semantic-cache regression:
CompilerOptionChangesReuseEquivalentSemanticArtifacts failed in canonical-package
and the package coverage shard because target platform was serialized even when
no byte contract used it. Capture now records platform only for byte-selected
compilations. The original cache-reuse integration test and both byte consumers
then passed together (3 tests), and all 417 focused native tests passed after the
fix. Ordinary resource contracts assert that both platform and layout references
remain absent. This preserves the prior cache semantics without weakening the
layout binding for byte claims. A new exact-head run remains required.

After the platform-capture fix, the final full analyzer suite passed all 1388
tests with zero build warnings/errors. All quality/compatibility categories
passed; final C# formatting, documentation drift and 723 local links passed.
Layout remains 2661 source files, 59 projects and 151 pinned common files.
The canonical/package failure on c9170a1a0 and its failed package coverage shard
are not a qualification pass; the fixed pushed commit must receive fresh CI.
The last complete aggregate coverage report remains the earlier 180d4a95a report,
with the unchanged 100% baseline still required for final delivery.

## Managed object and constructor integration

The next memory layer adds a closed non-array managed-allocation leaf to resource
program model 4. It transports typed storage facts, validates extent ownership,
charges fields against the shared program budget and derives byte intervals in
the native layout checker. The existing three count metrics charge zero for the
managed instance itself and continue to charge all constructor/initializer arrays
and loop entries. Protocol 21/cache 23/compiler artifact 37 invalidate model 3
programs; expression model 1 and the reviewed layout formulas remain unchanged.

Source admission covers intrinsic System.Object creation and ordinary source
classes directly derived from System.Object, with primitive/reference fields.
The fundamental object constructor reuses the existing total-lowering intrinsic
rule. User constructor arguments, instance field/property initializers, explicit
constructor bodies and field object-initializers are visited separately. Static
initialization, recursive construction, inheritance, records/generics, COM
activation, cleanup callbacks and events, custom layouts, unsupported value storage and
unmodeled constructor chains remain explicit Unknown boundaries. User constructor
effects are never inferred from instance size alone.

Native replay observes concrete managed-reference allocations as well as arrays.
An ordinary fresh instance has a model lower cost of at least 24 bytes; loose
field-packing intervals cannot become exact costs. Mixed constructor prefixes
are undergoing replay validation before this boundary is considered verified.
The complete source/native tests and final checks are recorded after they pass.
Other allocation families, liveness/escape, retained/peak memory, stack, phase E/F
and final unchanged 100% qualification remain required.

Managed integration passed 438 focused native/resource/protocol/cache/golden
checks, all 555 IR tests and 195 source resource controls. A mixed field-initializer
refutation initially stayed Unknown; a bounded replay trace established that the
existing functional backend treats instance-initializer constructors as opaque,
consuming approximation before allocation. The verifier correctly preserves
Unknown there. Explicit plain constructor bodies replay exact array events and
compose them with managed lower costs. Exact initializer execution is therefore
an explicit remaining backend task, not a waived requirement. Diagnostic scaffolding
was removed after establishing the boundary.

The initial structural layout check used a banned display-string API and was
replaced with typed namespace/name matching. Formatting and test-only guard/import
issues were corrected with the pinned tooling. Existing Unknown expectations for
reference locals now identify the underlying unsupported operation. Broader and
actual NuGet validation is tracked below after completion.

Final managed-object validation passed all 1400 analyzer tests, 438 focused native
resource/protocol/cache/golden tests, all 555 IR tests and 5 actual package cases.
The package cases cover managed/array proof and refutation, strict/advisory policy,
AnyCPU Unknown outcomes and equivalent-artifact cache reuse. Layout passed with
2664 source files, 59 projects and 151 pinned common files. Documentation drift
and 723 local links passed. All non-spelling quality/compatibility categories
passed; targeted spelling/Markdown verification passed after plain-language fixes.
Final source formatting remains part of the delivery checks.

Published checkpoint 457146536 passed every non-coverage workflow and all four
coverage shards. Aggregate qualification failed at 3099/3180 changed trusted lines
(97.45%), leaving 81 gaps against the unchanged 100% baseline. That measurement
excludes this managed-object layer; fresh exact-head CI and final qualification
remain required. No phase C/E/F requirement is removed by this checkpoint.

## Scalar boxing source integration

Scalar boxing to object now reuses the total backend's typed conversion predicate.
Operand effects precede a managed Box storage fact, so source-call arrays, loop
counts and exceptions retain their ordering. Boolean and physical 8/16/32/64-bit
integer storage (including char) use the reviewed 24-byte box model. Existing
array count/element metrics remain zero for the box itself. Floating, decimal,
nullable, interface and user-defined conversions remain explicit Unknown cases.
Protocol 22/cache 24 invalidate older capability results; artifact 37 and program
model 4 already represent managed box facts and are unchanged.

Initial native validation passed 275 contract/protocol tests. Seven actual package
cases then passed across arrays, managed objects and scalar boxes, including
strict/advisory proof/refutation, AnyCPU Unknown and semantic artifact/cache reuse.
Final native operand-ordering and broader checks are recorded after completion.

String construction must preserve the runtime's cached zero-length path and
negative-length exceptions, rather than treating every constructor call as a
fresh instance. The next source/native implementation will follow the pinned
[String constructor implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/String.cs).
Exact initializer replay, remaining string/delegate/closure extraction, alias/
escape/liveness, peak/retained output, stack, phase E/F and final unchanged 100%
qualification remain required. No existing roadmap item is removed here.

Final scalar-boxing validation passed 449 focused native/resource/protocol/cache/
golden tests, all 1405 analyzer tests and 7 actual package cases. Operand-call
arrays precede the box in concrete prefixes; a throwing operand cannot create a
box event. All quality/compatibility categories passed with zero build warnings
or errors. Documentation drift and 723 local links passed. Layout evaluated
2665 source files, 59 projects and 151 pinned common files. Exact-head CI for the
published managed-object checkpoint was still running at this local delivery
check; neither it nor the newer boxing layer is represented by an older coverage
percentage. The unchanged 100% qualification remains required.

## Repeated-character string integration

The next phase C slice admits the metadata `string(char, int)` constructor with a
constant character outside the surrogate range. The count may be a supported
constant or scalar size expression. Argument effects retain source evaluation
order; successful positive counts produce a typed String storage fact. Zero
returns the cached empty value without an allocation event. Negative counts throw
ArgumentOutOfRange before a string instance is created.

The symbolic backend keeps content conservative, retains non-null/length facts,
and never infers unequal string values from fresh storage. Concrete replay fills
the requested character and reports positive successful allocation prefixes under
the owned layout model. Runtime helper work remains outside source loop counts.
Dynamic characters, surrogate characters, other string overloads and opaque
string factories remain Unknown. The well-formed UTF-16 value domain is unchanged.

Protocol 23, cache 25 and artifact 38 invalidate old capability results. The
existing resource program model 4 already represents the typed String shape.
Final validation is recorded below. Remaining phase C/E/F requirements, including exact
initializer replay, delegates/closures, alias/lifetime analysis, peak/retained
space, stack models, asymptotic/amortized/average-case analysis and concurrency,
remain open. Final exact-head CI and unchanged 100% qualification remain required.
String validation passed all 1408 analyzer tests, 562 IR tests (including the
independent pinned-runtime allocation controls), and 9 actual package cases.
The 659-case focused worker run passed 657 cases; the only failures were two
artifact-schema golden numbers, corrected from 37 to 38 and independently rerun.
All 81 golden cases passed after the schema correction. Layout passed with 2665
source files, 59 evaluated projects and 151 pinned common files.
Quality/compatibility categories passed apart from documentation spelling/newline
issues; targeted spelling and Markdown checks passed after correction. Final
source whitespace verification passed. Documentation drift and 723 local links
passed.

Published boxing checkpoint f59fb2fda passed every non-coverage workflow and all
four coverage shards. Aggregate qualification failed at 3219/3308 changed trusted
lines (97.31%), leaving 89 gaps against the unchanged 100% floor. This measurement
excludes the string layer. Fresh exact-head CI and final qualification remain
required; no roadmap item is removed by this checkpoint.

## Constructor initializer replay

The functional/native backend now lowers admitted instance field and simple
auto-property initializers in source declaration order. Caller arguments are
captured first, then the instance is allocated, then initializers execute, then
the explicit body runs. Implicit constructors execute initializers before
returning. Direct field object initializers then execute after construction. Field stores share the fresh instance with body/caller reads, and
exceptions stop later initializers and body effects. Root constructor body-only
admission remains separate; a constructor body CFG is never silently treated as
containing its field initializers.

The exact construction path requires one source type declaration without type
parameters directly derived from System.Object. Static initialization, inheritance,
constructor chains, partial declarations, property-setter/nested object initializers and
unsupported initializer values retain conservative boundaries. Compiler-backed
instance auto-property reads have no hidden getter cost; custom/virtual getters
remain unsupported in resource cost extraction.

Protocol 24, cache 26 and artifact 39 invalidate previous capability evidence;
resource program/evidence model 4 and expression model 1 are unchanged. Tests
cover mixed byte prefixes, initializer value flow, throwing arguments/initializers
and package consumers. Broader phase C/E/F requirements and
unchanged 100% final qualification remain open.
Validation passed 881 broader native/resource/source-call/protocol/cache/golden
cases, 180 final construction cases plus 2 constructor-fault controls, all 2062
frontend tests and all 1409 analyzer
tests. Eleven actual package cases passed across arrays, explicit/implicit
construction, boxing and strings, including strict/advisory handling, AnyCPU
Unknown and equivalent-artifact reuse. All quality and compatibility categories
passed. The 26 pinned-runtime layout tests also passed, independently measuring
64-byte field/auto-property construction and 104-byte field replacement after
construction. Final source formatting passed. Layout passed with 2665 source
files, 59 evaluated projects and 151 pinned common files; documentation drift and
723 local links passed.

Published string checkpoint 518c98d80 passed every non-coverage workflow and all
four coverage shards. Aggregate qualification failed at 3292/3382 changed trusted
lines (97.34%), leaving 90 gaps against the unchanged 100% baseline. This result
excludes initializer replay. Exact-head CI, coverage gap closure and all remaining
phase C/E/F work remain required.

## Master reconciliation

The draft MR became conflicted after master advanced to `deaa0a57c`, so GitHub
could not run pull-request CI for initializer checkpoint `17d028b08`. The branch
now merges that master revision. Its only content conflict was source call
preparation: source binding types must refresh before receiver creation, while
constructed calls must still model their fresh receiver. The resolution preserves
both rules and retains the upstream array range, collection, conversion and
runtime type-identity implementations.

Merged-state verification passed 882 worker resource/source-call and upstream
feature cases, all 577 IR tests, all 2112 frontend tests and all 1409 analyzer
tests. All 11 package cases and all quality/compatibility categories passed.
Documentation generation and 723 local links passed. Layout passed with 2704
source files, 59 evaluated projects and 151 pinned common files. Delegate/closure allocation work
resumes after merge qualification; no remaining phase C/E/F requirement or the
unchanged 100% coverage qualification is removed by this reconciliation.

## Explicit delegate storage

Compiler-bound delegate type certificates now survive portable IR transport and
identify successful fresh delegate events without parsing display names. The
factory/decoder reject built-in, legacy, class/interface-conflicting and foreign
certificates. This uses the same compiler-fact trust boundary as other typed IR
certificates; arbitrary producer assertions are not independent source proofs.

The resource source/native path admits explicit method-bound construction with
source targets, preserving receiver effects and skipping target method bodies.
Each fresh delegate uses the reviewed 64-byte runtime footprint. Erased delegate
construction contributes zero, and null receiver failures cannot create an event.
Repeated construction composes with loop bounds. Managed delegate signatures are
now admitted; body support remains independently checked. Discard assignments
preserve RHS effects, including the delegate-specific erasure rule.

Implicit method-group/lambda conversions, capture-frame placement/sharing,
metadata/extern target binding, target type initialization and unmodeled dispatch
remain Unknown. Closure frames must be placed at their actual compiler allocation
scope; charging a bundle only at a lambda expression would miss frames allocated
before zero-trip loops or exceptional exits. Closure/lifetime/peak/stack and phase
E/F requirements remain open.

Protocol 25, cache 27 and artifact 40 invalidate previous capability evidence;
resource program/evidence model 4, expression model 1 and the runtime layout model
are unchanged. Initial validation passed 77 native/codec cases and 585 IR tests.
Final validation passed 961 focused worker cases, all 1412 analyzer tests, all
2112 frontend tests and 13 actual package cases. All quality/compatibility
categories passed. Independent pinned-runtime controls measured fresh static
delegates at 64 bytes, instance-bound creation including its receiver at 88 bytes,
and erased construction at zero; all 26 runtime layout cases passed. Layout
evaluated 2705 source files, 59 projects and 151 pinned common files. Documentation
generation and 723 local links passed.

Published merge checkpoint 11e3a57ed restored CI eligibility. Canonical CI found
three old target-typed constructor expectations that excluded a valid proof after
field replay became exact. The strengthened regression now checks both the actual
result and false postconditions under an invalid callee precondition. All seven
constructor/callee-precondition controls passed, confirming false callee requires
are not imported as assumptions. The same three expectations blocked the published semantic-worker coverage shard,
so no fresh aggregate percentage is available for that merge. Coverage
qualification and exact-head CI remain required against the unchanged 100% baseline.

## Root parameter closure frames

A shared bounded capture plan now admits synchronous static methods whose sole
return constructs one delegate closing over entry parameters. The plan uses
compiler data-flow capture facts, not textual variable matches; `nameof` does not
invent captures. Nested functions, local capture scopes, broader control flow,
async lambdas and unsupported value storage retain Unknown boundaries.

The native program allocates a typed frame at entry and the delegate at the return
expression. Immutable frame storage certificates survive portable transport and
are incompatible with ordinary class/interface/delegate certificates. Source
bounds charge the same frame separately from delegate creation. A packed lower
bound and conservative field-layout upper bound remain distinct. No lambda body
is executed while computing construction costs.

Independent Debug/Release emission controls confirm frame construction precedes
user branches, zero-trip loops and exceptional exits. The zero-loop example also
contains a delegate-cache field, making its entry frame 32 bytes. It remains
Unknown because a general capture/sharing model is still required; moving all
closure cost to the lambda expression would miss that allocation. The admitted
single-return family has no such cache field.

Protocol 26, cache 28 and artifact 41 invalidate previous capability evidence.
Resource program/evidence model 4, expression model 1 and the runtime layout model
are unchanged. Validation passed 934 focused worker cases plus three packing-bound
controls, all 1415 analyzer tests, all 2112 frontend tests, all 593 IR tests and
15 package cases. The 95 capture/transport controls include independent Debug
and Release compiler observations. Full quality and compatibility checks passed;
layout evaluated 2708 source files, 59 projects and 151 pinned common files.
Documentation generation and 723 local links passed. Published delegate CI also
exposed a stale signature expectation: admitting a delegate parameter does not
supply a complete effect summary, which remains Unknown. The regression now
checks that distinction explicitly; all 88 manifest cases passed. Broader closure scopes,
sharing, lifetime/peak/retained memory, stack and phase E/F remain required, along
with unchanged 100% coverage qualification and exact-head CI.

The closure implementation is committed at 399215b6b. Latest master 9e3b1ade2
(HashSet state and signed callback counts) is merged at c8baf35fb. The one
insertion conflict preserves both repeated-character string construction and
HashSet lowering. Combined-state validation passed 581 focused resource,
delegate, closure, manifest, HashSet and constructor controls, all 2121 frontend
tests, all quality/compatibility categories, documentation generation and 723
local links. Layout evaluated 2713 source files, 59 projects and 151 pinned common
files. Published b3e0d2db6 coverage failed in the semantic-projects shard on the
same stale manifest expectation now corrected; it produced no qualified aggregate.
Fresh exact-head CI and unchanged 100% coverage qualification remain open.

## Logical memory lifetime evaluator

Phase C now has a bounded finite-trace evaluator for fresh source-owned objects,
root aliases and strong-reference field stores. It computes cumulative storage,
peak auxiliary storage and retained output separately. The declared logical model
excludes the final returned object graph from auxiliary storage throughout the
trace; it does not claim GC collection times or physical heap residency. Shared
objects and cycles count once. Replacing a root includes the transient overlap
while the new object is allocated before the old root is overwritten. Removing a
field reference can release a former child from the logical reachable set.

Allocation sizes remain lower/upper intervals supplied by the separately checked
layout model. Unsupported escapes, malformed references, resurrection of objects
that lost every root, invalid output roots and exhausted traversal budgets return
Unknown without partial metrics. The finite event and traversal limits bound work
and snapshot storage; cancellation propagates.

This evaluator is an internal foundation. Compiler-owned trace extraction,
portable ownership binding, static path/repetition composition, native contracts,
and consumer reporting for peak/retained metrics remain to be connected. It does
not yet expand public metric support. All 612 IR tests passed, including 19 new
lifetime cases and replacement traces for 100 sizes.
All quality and compatibility checks passed. Layout evaluated 2715 source files,
59 projects and 151 pinned common files. The phase C checklist remains incomplete.

## Compiler-owned finite lifetime extraction

The lifetime evaluator now accepts an internally extracted trace from a typed IR
program, rather than caller-provided allocation numbers. The initial admitted
family is a single finite chain of primitive-array allocations, scalar/reference
copies, and normal returns. Array storage widths and lengths come from typed IR
and the separately validated runtime layout. Reference aliases preserve object
identity; last-use root drops distinguish dead temporaries from live aliases.
Literal copies through compiler temporaries retain their values until overwritten;
unknown writes invalidate them. Constant comparison guards select only their proven
edge. Local-write effect markers do not invent heap edges. Unreachable blocks do
not contribute execution costs. Cycles, unproved branches, calls,
unknown scalar effects, unproved symbolic or negative lengths, borrowed references and
unsupported allocation shapes remain Unknown without partial metrics.

The extractor bounds block/instruction/term scanning and emitted event counts.
This is a normal-completion logical model. Exceptional-path composition, field
stores/loads, object graphs, source-call lifetime summaries and symbolic repeated
regions remain required. Native contract transport/reporting and public peak and
retained metrics are still pending. All 635 IR tests passed, including 23 new
extractor cases. Four validated-artifact source workflows passed: direct array
return, returned alias, a dead temporary before the returned allocation, and
borrowed-output rejection. The first three also execute emitted C# and verify the
returned array lengths. Tests check the actual compiler-generated constant-copy
and exception-guard representation rather than assuming source literals remain
literal IR terms. Layout evaluated 2718 source files, 59 projects and 151 pinned
common files. All quality and compatibility categories passed. Documentation
generation and 723 local links passed.

Exact published closure head fcbf7a467 passed all four coverage shards. The
aggregate enforced the unchanged 100% floor and failed at 2665/2785 changed
trusted lines (95.69%, 120 uncovered lines). This measurement predates the
lifetime evaluator/extractor; it does not qualify the newer work. Coverage gap
closure and fresh final exact-head CI remain required.

Published closure head fcbf7a467 also passed every canonical container PR gate,
package/consumer/release workflow, documentation and toolchain security checks.
Its coverage threshold is the remaining failing workflow.

## Native logical lifetime upper-bound contracts

The public contract API now names `PeakAuxiliaryBytes` and `RetainedBytes`.
Compiler collection binds the same reviewed layout dependency and target/reference
evidence as cumulative bytes. A distinct lifetime cost-model identity declares
normal-completion logical reachability; cumulative metrics keep their existing
exceptional-prefix definition. The native worker derives finite primitive-array
lifetime bounds from the owned typed IR, records its model and program digest,
and projects the owned layout dependency only on a decided proof.

Unsupported lifetime paths and unproved comparisons return Unknown without partial
numbers or used assumptions. Numerical counterexamples for these metrics remain
pending independent replay; upper estimates cannot manufacture refutations.
Broader field/object graph extraction, borrowed ownership, exceptional-path
composition, symbolic repetition, calls, stack and phases E/F remain required.

Protocol 27, cache 29 and compiler artifact 42 invalidate older capabilities.
Manifest 6, resource program/evidence 4, expression 1 and the allocation-layout
identity remain unchanged. All 14 focused public contract API tests, all 1416
analyzer tests, all 635 IR tests and 17 actual packaged-consumer cases passed.
Full quality/compatibility categories, layout (2719 source files, 59 projects,
151 pinned common files), documentation generation and 723 local links passed.
The final broader worker/protocol run passed all 549 cases, including the mixed
cumulative/peak/retained and conflicting-cost-model controls. Two initial restore failures were shared NuGet
vulnerability-cache collisions. The affected gates passed with container-private
HTTP caches and auditing left enabled.

Native lifetime implementation 77e0aff40 is merged with latest master 7f6e25f21
(Dictionary capacity construction) at 520600efb. Combined-state checks passed 776
focused worker/protocol/Dictionary cases and all 2122 frontend tests, followed by
all quality/compatibility categories. Final review separated lifetime layout
ownership from cumulative-program availability. Two fixed-local-size source cases
prove peak/retained bounds with a null cumulative program; the expanded affected
worker/protocol/Dictionary run passed all 778 cases. Both added packaged-consumer
controls and all 281 affected analyzer/collector cases passed. Together with the
earlier 17 package cases, 19 distinct package cases now cover this milestone.
Final quality/compatibility categories passed. The added package controls used
the Git-aware wrapper required by Arcade packaging; a terminal restore-process
crash was rerun successfully with the same pinned toolchain.

## Completed-execution lifetime refutations

Native lifetime violations now require a concrete completed execution, independent
of the source-produced upper envelope. A bounded interpreter observer records
successful instructions and produced values. Replay reconstructs primitive-array
identities, byte footprints, aliases, last-use roots and returned ownership from
those observations, then checks the declared lifetime model. Unknown, approximate,
exceptional, truncated, foreign or unsupported observations cannot supply numbers.

Entry constraints, receiver requirements and all Requires definedness/value
conditions are checked before replay. Samples only supply candidates. A separate
solver-entry control requires n == 37, outside the cheap sample set, and validates
that entry concretely. False and contradictory Requires produce no refutation.
Lifetime witnesses identify normal completion; cumulative witnesses retain their
execution-prefix semantics. Owned layout evidence is projected for decided
lifetime refutations without requiring a cumulative resource program.

Protocol 28, cache 30 and compiler artifact 43 invalidate earlier cached Unknown
outcomes. Manifest 6, resource program/evidence 4, expression 1 and the lifetime
model identity remain unchanged. All 781 affected worker/protocol/Dictionary
cases, all 641 IR tests, all 1416 analyzer tests and five selected package cases
passed. The package selection includes three new lifetime refutations and two
independent-path proofs; 22 distinct package cases have now been validated across
the lifetime milestones. Layout evaluated 2721 source files, 59 projects and 151
pinned common files; documentation generation and 723 local links passed. A full
IR restore-process crash was rerun successfully with private CLI/HTTP state.
All quality categories passed. The first external-link audit received a 504 for
an unchanged GitHub URL; its targeted retry passed without source-link changes.
Broader graph, call/repetition/exceptional lifetime composition, stack and phases
E/F remain open, together with unchanged 100% coverage and exact-head CI.

## Owned object field graphs

Compiler-issued complete object-storage certificates now cover every instance
field of admitted direct source classes, including unused and backing fields.
Typed immutable storage survives portable IR and cannot be mixed with interface,
delegate, closure, inherited, legacy or built-in certificates. Layout facts are
compiler facts rather than inferences from whichever fields a body happens to read.

Finite lifetime extraction and independent concrete replay now preserve owned
field graphs. Shared children and cycles count once; overwriting a field edge can
release a former child. Proven non-null facts and safe object casts preserve field
store order. Packed lower and conservative upper object bounds remain separate.
Borrowed child references and unsupported layouts produce Unknown without partial
numbers or used layout dependencies. Field reads and broader lifetime composition
remain required; this does not finish phase C or phases E/F.

Protocol 29, cache 31 and compiler artifact 44 invalidate older object-layout
capabilities. Manifest 6, resource program/evidence 4, expression 1, the allocation
layout and lifetime model identities remain unchanged. Four initial IR graph
controls and 16 focused native/portable cases passed. Final validation passed
574 affected worker/protocol/portable cases, all 645 IR tests, all 2122 frontend
tests, all 1416 analyzer tests and four actual packaged object-graph proof and
refutation cases. Across the lifetime milestones, 26 distinct package cases have
passed. Quality/compatibility, documentation generation and 723 local links passed;
layout evaluated 2724 source files, 59 projects and 151 pinned common files.
Broad regression checks caught an initially excessive certificate restriction on
sealed classes. Sealed and direct-class facts now compose, and both original
suites plus the explicit sealed-object control pass. Packing controls preserve
Unknown between the packed lower and conservative upper bounds.

## Reads of explicitly stored owned references

Finite lifetime extraction now resolves instance field reads by scoped receiver and
member identity after an explicit supported store. Object aliases see the same
field; copying a child reference into a local preserves the child's identity when
the field is later overwritten. Explicitly stored null references are also known.
The bounded reference resolver follows safe casts and nested field reads while
last-use traversal keeps their receiver roots live through evaluation.

Concrete replay derives read results from actual completed instruction values,
independently of the static field map. The existing normal-completion and entry
validation requirements remain in force. Unwritten fields remain Unknown because
the current storage certificate does not bind field members to complete default
storage facts. Scalar field computations and broader lifetime composition are
still required; this checkpoint does not finish phases C, E or F.

Protocol 30, cache 32 and compiler artifact 45 invalidate older capabilities and
cached Unknown results. Manifest 6, resource program/evidence 4, expression 1 and
the reviewed layout/lifetime model identities remain unchanged. Focused checks
passed all five IR graph cases and five native read controls, including proof and
concrete refutation, aliases, overwritten edges, explicit null and an unwritten
field Unknown with no partial numbers or used layout dependencies.

Broader checks passed all 646 IR tests, 578 affected worker/resource/protocol/portable
cases, 352 analyzer/collector cases and four actual packaged field-read cases.
Across lifetime milestones, 30 distinct packaged consumer cases have passed.
Quality/compatibility, formatting, documentation generation and 723 local links
passed. A restore-process crash was retried successfully with fresh container-private
CLI/HTTP state and auditing enabled.

Published coverage CI for object-graph head 143901c17 exposed a missed acceptance
contract cache update: the contract still specified schema 28 while the worker
used 31. The contract now matches schema 32. Exact-head published CI and the
unchanged 100% coverage floor remain required; local checks do not substitute for
that qualification.

The corrected acceptance contract and updated artifact snapshots passed all 122
selected acceptance/golden worker cases. Final documentation spelling and Markdown
checks passed.

## Comparable runtime and allocation measurement workflow

The existing packaged-consumer performance harness now has an explicit resource
workload. It builds the same eight allocation kernels against an Attributes-only
baseline, advisory analysis and strict native verification, rotating variant order
between iterations. Restore, first build, no-op, warm rebuild and small-edit times
stay separate. Every strict build must retain eight nonvacuous Proven resource
claims and complete selected coverage before its measurements can be accepted.

The final edited source is executed at explicit input scales with repeated calls
and ten warmups per scale. A separate process runs each variant. Raw stopwatch
ticks/frequency describe kernel execution; process milliseconds include startup,
warmups and report serialization. GC thread allocation counters describe total
bytes over the measured repetitions, including fixed entry allocation at input
zero. They do not describe peak live or retained storage. Output checksums validate
actual execution independently of selected source proofs.

Report schema 3 records source/harness/package identities, actual runtime and
architecture, OS, GC mode, the tiered compilation environment setting, inputs,
repetitions, raw samples and signed comparisons with each iteration's baseline.
Comparison admission requires matching source, environment and output. Invalid or
duplicate scales are rejected before package loading. The resource sample and final combined controls passed against the packaged
product, including the original contract workload, invalid-scale rejection and
baseline report comparisons.

The [consumer performance guide](consumer-performance.md) documents the command
and interpretation.
Measurements are explicitly separate from asymptotic/tight/expected/amortized
proofs. Async/concurrent models, cancellation and exceptional completion remain
required along with the other unfinished roadmap items.

Final validation passed all four package integration controls. Both complete
benchmark workloads ran concurrently. The initial combined run exposed shared
compiler-server analyzer loading state across separate package caches; a per-run
compiler identity now isolates independent runs while preserving sharing among
that run's variants. The final combined run passed after this correction.

The .NET 8.0.32 x64 resource smoke run used one iteration and three repetitions per
input. Every variant observed 576, 12864 and 98880 allocated bytes for input sizes
0, 16 and 128 respectively, with matching output checksums. These are functional
measurement controls rather than timing conclusions; routine measurements default
to three iterations and 100 repetitions. Raw reports are retained as test evidence.

All quality/compatibility categories passed, followed by the focused PowerShell
check after compiler isolation. Documentation generation and 724 local links passed.
Native protocol 30, cache 32 and compiler artifact 45 remain unchanged because this
phase adds measurement tooling rather than trusted proof capabilities. Benchmark
report schema 3 distinguishes measured process/kernel time and allocation samples
from source-derived resource proofs. The benchmark workflow requirement is now
complete; all other unchecked C/E/F and final qualification requirements remain.

## String lifetimes and guard root uses

Finite typed-IR lifetime extraction and independent completed-execution replay
now admit repeated-character string construction with constant nonnegative sizes.
The reviewed UTF-16 layout charges distinct positive-length instances; equal
contents do not collapse allocation identity. Object widening, saved aliases,
field edges and overwrite preserve ownership. Shared empty construction and empty
literals use a permanent non-null zero-byte anchor, so no new owned bytes are
charged and repeated use cannot resurrect a reclaimed instance.

Guard operands now participate in last-use root tracking in both extraction and
replay. This corrects an earlier omission that could drop two references before
the comparison that reads them. String content equality is explicitly excluded
from reference-identity guard inference. Borrowed/nonempty literal storage,
negative lengths, unproved content branches and broader composition remain Unknown
without numeric evidence or used assumptions.

Protocol 31, cache 33 and compiler artifact 46 invalidate earlier capabilities and
cached outcomes, including the guard-root correction. Acceptance worker/cache
versions and artifact snapshots move together. Manifest 6, resource program/evidence
4, expression 1 and reviewed layout/lifetime model identities remain unchanged.
Initial validation passed 58 memory IR cases and five native string cases.
Compiler-pinned Boolean comparisons initially prevented two new native guard
controls from being decided. Proven comparison values now propagate through
Boolean temporaries, while unproved content/effect conditions remain Unknown.
The expanded 11 native string controls and all 653 IR tests passed after this
correction, along with 352 analyzer/collector cases. The final broad worker/
protocol/acceptance/golden run passed 711 cases, and all four package controls
passed.
Phase C and the other unchecked roadmap requirements remain open.

The four packaged string lifetime controls passed after Boolean guard propagation,
including independently replayed retained and guard-root peak violations. There
are now 34 distinct validated lifetime package cases, alongside the separate
measurement workflow controls. The broad worker retry passed all 711 affected worker/protocol/acceptance/golden
cases. An earlier .NET build-process exit 139 occurred before tests; the serial
retry used fresh private CLI/HTTP state.

Published benchmark-head f6e63144c coverage passed all four shards, then failed the
unchanged 100% changed-trusted-code floor: 3242/3439 lines (94.27%), with 197 gaps.
This is newer than the earlier closure-only measurement but predates string
lifetimes and cannot qualify this implementation. Coverage gap closure and fresh
exact-head CI remain required.

Published benchmark-head canonical CI identified a script formatter failure in
Measure-SharpProofConsumerPerformance.ps1, separate from its passing PowerShell
lint. The formatter changed only script whitespace. The complete canonical
formatting gate passed after that correction, including 87 PowerShell files,
19 shell scripts, script-tool regression checks and complete C# formatting.
The earlier package/consumer/
release, documentation and security workflows passed. Native string changes
remain subject to fresh exact-head CI and unchanged coverage qualification.

## Completed replay path integrity and boundary controls

Coverage review identified missing controls around lifetime replay validation.
A regression reproduced acceptance of omitted and reordered callbacks from an
otherwise completed real execution. In particular, omitting a returned-graph
field store could misclassify the child as auxiliary storage. Instruction ownership
and the final return alone did not establish a complete observation path.

Replay now indexes each owned instruction's block and position. Observations must
start at the entry instruction, remain contiguous inside each block, and follow
the preceding terminator. The interpreter supplies the actual evaluated Boolean
at branch completion; replay requires the next block to match that decision.
Missing stores, missing entry instructions, reordering and missing/contradictory
branch observations produce MemoryReplayTraceIncomplete without numeric evidence.
Foreign or repeated instructions remain unsupported, and approximation/budget
failures remain sticky even when later callbacks arrive.

Additional controls exercise primitive storage widths, incompatible layouts,
reference-element and initialized arrays, uncertified objects, allocations without
a result target,
borrowed copies/returns, graph/observation budgets, legacy semantics, actual
completed repetition and cancellation. These are boundary controls around existing
admission rules; they do not add a repetition or borrowed-ownership model.
All 679 IR tests passed. A test setup initially supplied Total replay options to
the legacy interpreter and was corrected to observe an actual legacy completion.
All 517 selected native/golden/cache-identity cases and 17 packaged lifetime
proof/refutation cases passed.

Protocol 31, cache 33 and compiler artifact 46 remain unchanged: this hardens an
internal callback invariant and adds no wire fields or newly admitted source
capability. Worker binary identity includes every SharpProof companion assembly,
including SharpProof.Ir.dll, so changed interpreter/replay code invalidates caches.
A dedicated control mutates that companion module and checks the digest changes,
while an unrelated module leaves it stable. Fresh exact-head CI and unchanged
100% changed-trusted-code coverage qualification remain required, along with all
other unfinished C/E/F roadmap items.

Final validation passed all 679 IR tests, 517 selected worker/golden/cache-identity
cases and 17 packaged lifetime cases. Repository quality categories passed after
correcting two documentation spelling findings; complete canonical formatting,
including script-tool checks and full C# formatting, passed. Documentation
generation and 724 local links passed. These controls do not establish the final
coverage percentage; the unchanged 100% aggregate floor and the full remaining
roadmap stay open.

The latest completed published coverage aggregate is string head 18d11e69c.
All four shards passed, but 3299/3501 changed trusted lines (94.23%) left 202 gaps
against the unchanged 100% floor. That aggregate predates this replay-path fix and
its new boundary controls; it cannot qualify this checkpoint.

## Compiler field initialization and finite scalar state

Eligible direct source-class construction now materializes CLR zero/null
initialization as scoped typed field stores, after allocation and before source
initializers and constructor execution. This uses the existing instruction model
rather than inferring member defaults from byte storage certificates. It includes
backing fields and preserves reference/owner identity across aliases.

Memory extraction records known integer and Boolean field values, resolves scalar
reads into guards and extents, and clears stale facts after unknown writes. It
supports safe constant conversions and uses mathematical integer values rather
than a signed intermediary, including UInt64.MaxValue. Independent replay continues
to derive actual sizes, values, identities and completed paths from execution.
Truncating conversions without already-folded typed facts remain unsupported.

Initial native controls passed default reference reads, default/updated scalar
extents, Boolean guards, source field initializers, backing fields, aliases and
separate instances. A constructor-body control exposed approximation at new Node()
before allocation; the constructor-receiver follow-up below resolves that
boundary. Broader constructor/call lifetime admission remains required.
The scalar overwrite control prevents an unknown input from reusing default zero.

All 682 IR tests, 2122 frontend tests and 1416 analyzer tests passed, along with
four actual packaged default-field/scalar proof and refutation cases. One lowering
golden gained the reviewed zero stores and instruction renumbering; both original
and reformatted source controls passed in the full frontend run. An initial
truncation test was corrected to prevent the factory from folding its constant
before memory analysis. The final affected native/protocol/artifact/acceptance/
golden gate passed all 723 cases.

Protocol 32, cache 34 and compiler artifact 47 invalidate older capabilities and
cached compiler results. Acceptance worker/cache constants and artifact snapshots
move together. Manifest 6, resource program/evidence 4, expression 1 and declared
layout/lifetime model identities remain unchanged. Phase C and remaining E/F work,
unchanged 100% changed-trusted-code coverage and exact-head CI remain required.

Quality/compatibility categories and complete canonical formatting passed. The
new package cases bring the cumulative validated lifetime package controls to 38.
Published replay-head 5567c1bb0 passed all four coverage shards, then reported
3338/3518 changed trusted lines (94.88%), with 180 gaps against the unchanged
100% floor. That improves on the preceding 202 gaps but predates field-default
analysis and cannot qualify this checkpoint. Fresh exact-head CI remains required.

## Fresh constructor receivers and initialization effects

Direct constructor frames already lowered scalar stores and conditional bodies
exactly. Caller lowering nevertheless emitted an approximation for the receiver
before evaluating arguments, then replaced that receiver with the allocation.
Construction now binds only the fresh allocated receiver, after arguments and
before the constructor body. Ordinary instance calls retain their evaluated
receiver and null/freshness rules.

Native lifetime controls cover constructor scalar stores, guards over CLR
defaults, field initializers followed by constructor updates, and constant
constructor arguments. They check both exact bounds and independently replayed
violations. An unconstrained constructor argument must still produce Unknown
without partial numbers or used metadata assumptions.

The first receiver-fix verification passed 302 native/source-call cases, all
2122 frontend cases, and four actual packaged constructor lifetime proof and
refutation cases. The package controls exercise retained output and auxiliary
memory with strict/advisory diagnostics and result evidence.

Published field-default checkpoint 36c649ad9 did not reach aggregate coverage:
the semantic-worker shard failed six existing purity/synchronization controls.
CLR zero stores had ordinary observable write sites. Zero initialization now
uses the existing compiler-owned fresh-write marker, before any source
initializer or constructor can publish the object. Source initializer and body
writes retain their previous classification. All 40 affected purity, ownership,
constructor, synchronization and independent runtime controls passed after this
correction, including escape/rebind controls. The canonical-main failure at the
published checkpoint contains the same six regressions. Its package/release run
passed. No coverage qualification or full-roadmap completion is claimed.

This correction uses existing receiver assignments, field stores and compiler
fresh-write sites. Wire versions remain unchanged; worker identities include the
frontend and other SharpProof companion assemblies, invalidating prior cached
lowering results. Final canonical formatting, repository quality/compatibility,
documentation generation and all 724 local links passed. The final frontend
regression run again passed all 2122 tests.

The final packaged rerun passed all four constructor cases after the fresh-write
correction, bringing the cumulative validated lifetime package controls to 42.
Final checkpoint Markdown and spelling checks passed as well.

The full final worker suite passed all 6595 cases with no failures. This includes
the native lifetime, purity, synchronization, replay, artifact, protocol and
consumer-result regressions. Fresh exact-head CI remains required after publishing
this checkpoint; the unchanged coverage floor and remaining C/E/F work stay open.

## Static delegate and scalar box lifetimes

Finite lifetime extraction and completed replay now recognize explicit static
delegate allocations through an exact compiler-owned operation marker plus the
existing nominal delegate type certificate. The compiler marker requires an
explicit static method binding, owned source declaration and admitted type
initialization. A delegate type alone, a marker without that type certificate,
or a similar-looking marker cannot establish capture-free storage.

The declared layout supplies the delegate's 64-byte storage; no producer byte
estimate enters the lifetime trace. Existing aliases, reference guards, last-use
roots and returned-graph exclusion compose with these fresh identities. Scalar
boxes already use the supported fresh-object path; new native controls verify
their 24-byte storage and distinct identities for Boolean and integer payloads.

Initial verification passed six IR admission/replay cases and eight native
lifetime cases. Additional controls preserve Unknown for captured instance
delegates, cached conversions, closures, metadata bindings and target type
initialization. Full affected checks and packaged proof/refutation controls are
in progress. Instance-target and closure capture edges, broader repeated and
exceptional lifetimes, stack bounds and remaining E/F requirements remain open.
Wire versions and model identities are unchanged: the operation descriptor and
delegate certificate use existing owned IR transport; companion-module identities
invalidate earlier implementations.

The full IR suite passed 688 tests and the full frontend suite passed 2122.
All 583 affected native/resource, delegate/closure, allocation, protocol,
artifact/golden and cache-identity controls passed. Independent pinned-runtime
allocation controls passed after adding Boolean and 64-bit box payloads alongside
the existing integer-box and static-delegate measurements. Layout passed for
2725 source files, 59 projects and all 151 pinned common files; documentation
generation and 724 local links passed. Quality categories passed apart from one
guide spelling correction; the focused spelling gate then passed with zero
findings. Complete canonical formatting passed as well.

All 11 final native controls passed, including a generic static delegate whose
target throws if invoked. This confirms that construction does not execute the
target body. Six actual packaged static-delegate/box proof and refutation cases
passed with strict/advisory diagnostics and serialized evidence, bringing the
cumulative validated lifetime package controls to 48.

Next, model instance delegate targets as mandatory owned reference edges and
connect returned closure delegates to their entry frames and captured values.
Missing, borrowed or unproved capture edges must prevent numerical lifetime
claims. Preserve field-graph sharing/cycles and independently observe these edges
during completed replay before admitting broader capture families. Stack bounds,
remaining C/E/F requirements and unchanged coverage qualification stay open.

## Owned instance delegate target graphs

Explicit admitted instance bindings now emit a delegate allocation followed by a
scoped synthetic target-field store. The target is evaluated and checked before
allocation; the target body does not execute. The store uses the existing fresh
write classification because it initializes the newly allocated delegate.

Finite extraction and completed replay require the exact instance-allocation
marker, nominal delegate type and immediately following target store. The field
must have its reserved role, type and allocated receiver. Its value must be an
already owned, non-null reference distinct from the new delegate; the edge can
be initialized only once. Missing, unrelated, null, overwritten or self-capture
evidence returns Unknown without partial bounds. Existing object-graph traversal
then includes the target's children and cycles in retained output and excludes
the returned graph from auxiliary storage.

Initial verification passed seven IR capture mutation cases and 11 native
controls covering target retention, child arrays, target cycles, generic methods,
dead auxiliary storage, borrowed targets and unsupported conversions. Broader
checks, self-capture rejection, runtime graph controls and packaged evidence are
in progress. Ambiguous non-direct bindings remain Unknown under the existing
compiler erasure model. Closure frame/capture links, broader lifetimes, stack
bounds and remaining E/F work stay required.

Final verification passed all 697 IR tests, including eight malformed-capture
controls and independent runtime checks of the 120-byte delegate/target/child
bundle and its actual target link. All 847 affected worker tests passed across
resource, purity/effect, ownership, artifact/protocol, golden and cache families.
Six final native cases passed, including binding through an owned field read
while releasing the parent root. Six actual packaged proof/refutation controls
passed, bringing cumulative validated lifetime package controls to 54.

The frontend run passed 2120 cases and identified the two comparisons of the
instance-delegate lowering snapshot. Its reviewed update adds the capture store
and subsequent instruction numbering; both original and reformatted comparisons
then passed. Quality/compatibility, scoped C# formatting, spelling/Markdown,
documentation generation and 724 local links passed. Layout passed for 2726
source files, 59 projects and all 151 pinned common files.

Published static-delegate checkpoint 19b28e12c passed all four coverage shards,
but aggregate validation stopped before percentage enforcement because the new
static helper was missing from the explicit trusted-source inventory. Both
delegate helpers are now classified in the acceptance contract. The authoritative
evaluated production inventory validates all 516 trusted paths with both helpers
included. No coverage exclusion or floor reduction was introduced. The latest
numerical aggregate remains constructor checkpoint f98b6d080: 3395/3577 changed
trusted lines (94.91%, 182 gaps). Fresh exact-head CI and coverage qualification
remain required for this extension.

## Closure frame capture links

Admitted root parameter closures now initialize every captured slot at entry in
certificate order, using scoped synthetic fields with checked storage types.
Their returned delegate records the mandatory target edge to the frame. The
closure plan retains the ordered parameter symbols as well as storage widths,
so initialization uses the actual bound values instead of inferred defaults.

Extraction and completed replay require the frame marker, complete ordered slot
stores and matching receiver/member/storage roles. They reject omissions,
reordering, mismatched widths, overwritten slots and impossible direct frame
self-references. Null captured references remain valid; non-null references must
be owned. Existing graph traversal preserves shared captures and captured child
graphs without counting shared objects twice. Primitive inputs need not have
known values when their storage and construction path are fixed.

The initial build caught a local-name collision in the extractor; it was fixed.
Representative verification then passed 13 IR admission/replay controls and 18
native tests, including existing cumulative closure bounds and packing intervals.
New controls cover primitive frames, inlined source factories, owned reference
captures and borrowed-reference Unknown results. Broader checks, lifetime packing
controls, independent runtime graph measurements and packaged workflows are in
progress. The new helper is included in the explicit trusted-source inventory.
Closure invocation, broader capture scopes and remaining C/E/F requirements stay
open; existing wire transport and model identities remain unchanged.

Final verification passed all 711 IR tests and all 2122 frontend tests. Runtime
controls independently measured 144-byte owned and 152-byte shared closure
bundles and verified their returned target/child behavior. Required parameter
guards were added to those runtime test helpers after the broader build flagged
them. All 864 affected worker controls passed, followed by six final factory
cases including known-null captures, shared children and mixed primitive/reference
slots. Lifetime packing controls preserve Refuted/Unknown/Proven separation.

Six actual packaged primitive/owned/shared closure proof and refutation cases
passed, bringing cumulative validated lifetime package controls to 60. Layout
passed for 2727 source files, 59 projects and all 151 pinned common files.
Authoritative production inventory validates all 517 trusted paths, including the
new closure helper. Quality/compatibility, scoped C# formatting, documentation
generation and 724 local links passed.

Published capture checkpoint f306d8dfb passed canonical/package gates and all four
coverage shards. Its aggregate reached 3479/3659 changed trusted lines (95.08%,
180 gaps), failing the unchanged 100% floor. The inventory repair now reaches
percentage enforcement. This evidence predates closure-frame links; fresh
exact-head CI and gap closure remain required. Broader capture/call lifetimes,
stack models and remaining E/F requirements are still open.

## Logical source activation bounds

The stack requirement now has an explicit source activation model rather than a
JIT-frame estimate. Each activation reserves eight-byte variable slots for its
parameters, declared locals, receiver/result when present, and two bookkeeping
slots. Generated/runtime/native frames, evaluation temporaries and tail-call
elimination are outside this model. These bounds do not establish physical stack
usage or stack-overflow safety.

Compiler resource programs preserve method and constructor activation boundaries,
including calls in field initializers. Typed activation shapes contain storage
facts, not supplied depth/byte verdicts. Native derivation takes maxima across
sequences/choices and loop iterations, accumulates retained recursive activations
only under checked ranking programs, and adds conservative prefix-call storage.
Existing cumulative work/allocation visitors pass through activation boundaries.

SourceActivationDepth and SourceStackBytes contracts select this model. Current
stack claims support Proven upper bounds and explicit Unknown results; completed
stack-violation replay is not implemented, and protocol validation rejects stack
refutations. Unknown frames, unmodeled calls and unsupported recursion cannot
publish partial stack numbers. Strict transport validates shape fields, flags,
arity and unrelated payloads.

Resource program/evidence model 5, protocol 33, cache 35 and compiler artifact 48
invalidate older encodings/capabilities. Manifest 6 and expression model 1 remain
unchanged. Public API entries, owning assertions, acceptance versions and artifact
snapshots move together. Representative compiler/native/transport and independent
executed-recursion controls are being validated before broader gates and packaged
consumer qualification. The full stack milestone is not yet marked complete.

The completed stack checkpoint passed all 1416 analyzer tests, all 711 IR tests,
685 affected native/protocol/artifact controls, and 27 final stack/consumer
controls. Two real packaged strict-profile direct-call/recursive stack-contract
cases passed. Final consumer controls exercise validated cache hits, SARIF model
projection and rejection of forged stack refutations. Two additional IR controls for
oversized block/instruction streams passed with no partial memory bounds.

Phase C has implementations for the declared admitted families documented in the
resource guide: typed allocation costs under the pinned layout, finite owned
normal-completion reachability, and logical source activation storage with checked
recursive ranks. This does not expand admission to arbitrary borrowed graphs,
closure invocation, unbounded lifetime paths or physical JIT/GC peaks; those
boundaries return Unknown. Broader capture/call lifetime composition remains required; the lifetime checkbox
stays open. Unsupported cases are not evidence of a completed proof family.

Quality/compatibility and full repository formatting passed before final fixture
additions; scoped whitespace formatting passed for those additions. Documentation
generation and 724 local links passed. Layout validates 2731 source files,
59 evaluated projects and all 151 pinned common files. The authoritative production
inventory validates all 519 trusted paths, including both stack implementation
files. Final docs and formatting drift checks follow the added validation notes.

Published closure checkpoint 8eb137d67 passed the other 29 checks (six skipped)
but failed changed-trusted-code coverage: 3587/3765 lines, 95.27%, 178 gaps.
This predates stack changes. The 100% floor remains unchanged. Coverage gap
closure and exact-head CI qualification remain mandatory, as do all phase E/F
unchecked requirements. Latest fetched master 8556188d1 adds exception-constructor
and callback-effect semantics; it must be incorporated and its affected workflows
verified before this checkpoint is qualified.

## Stack checkpoint and latest-master integration

Stack implementation and its progress document are committed in bdef19d084.
Latest master 8556188d1 merged cleanly in d5ce7c3ae. On the combined tree, all
2144 frontend tests and 636 selected native resource, exception, callback,
cache/SARIF and artifact controls passed. This directly checks the merged
constructor/callback semantics alongside resource derivation. Repository quality
and negative-fixture checks passed; documentation generation and all 725 local
links passed. Evaluated layout passed for 2740 sources, 59 projects and all
151 unchanged pinned common files. Complete post-merge repository formatting passed.

The earlier stack-specific package cases, full analyzer/IR suites, final stack
boundaries and independent recursive controls remain recorded above. The merge
did not change their resource implementation. Fresh pushed-head CI must verify
the combined state before qualification. Broader capture/call lifetimes, phase E/F implementation and the unchanged
100% coverage gate remain required; this checkpoint does not complete the goal.

## Asymptotic expression foundation

Phase E now has a bounded classifier for the single-input unbounded mathematical
extension of closed resource upper formulas. It handles nonnegative constants,
addition/maxima, polynomial/logarithmic products, constant subtraction/division
and alignment, and logarithms of polynomial growth. It distinguishes zero from
positive constants, validates operator arity/payloads, retains input ordinal/kind
identity across renaming, and propagates cancellation. Multiple independent inputs,
iterated logarithms and malformed/budget-exhausted expressions yield explicit
Unknown results without a partial order.

SP0060 projects the model and upper orders into machine-readable properties and
diagnostic text, including the selected input scale. Numeric proofs remain valid
when the growth classifier abstains. Finite CLR input caps are explicitly separate
from the unbounded formula extension. Upper expressions do not establish lower
source-cost bounds or tightness. All phase E checkboxes therefore remain open
until native evidence, independent lower bounds, checked amortized rules and
validated distributional models are implemented and verified.

Initial focused verification passed 19 expression tests and five analyzer tests.
Additional noncanonical unary-constant controls and full IR/analyzer gates are in
progress. The new classifier is classified in the trusted-source inventory.
No wire/schema/cache change is needed for these advisory diagnostic properties.

Published stack head eec823652 passed canonical, package, documentation and
toolchain-security CI. Coverage attempt one failed before aggregate enforcement
when the native-payload download connection reset during container preparation;
the failed job was rerun on the same exact head. Attempt two reached aggregate
enforcement and failed at 3720/3910 changed trusted lines (95.14%, 190 gaps).
The gap inventory is preserved for targeted tests. The unchanged 100% requirement
remains mandatory.

The classifier's final full IR gate passed 748 tests, including 35 new growth,
constant-folding, malformed-record, cancellation and adversarial budget controls.
The full analyzer gate passed 1422 tests before the packaged activation repair.
Five final visible-message/property controls passed.

The first real package runs exposed a pre-existing SP0060 activation defect:
compiler-owned editorconfig severities were not read from SyntaxTreeOptionsProvider.
Generic analyzer-option tests had masked that path. A separate eight-case
reproduction failed all five enabling severities and passed the three disabled
cases on the unfixed implementation. SP0060 now reads effective per-tree/global
compiler severities before generic-option fallback. The default/off profiles
remain disabled, and the production severity fix is undergoing full analyzer and
real packaged-consumer checks. A transient compiler-flags probe was removed from
the final tests after diagnosis.

Quality/compatibility and scoped C# formatting passed before the final activation
repair. Layout validates 2743 source files, 59 projects and all 151 pinned common
files. Documentation generation and 725 local links passed, and the authoritative
production inventory validates all 520 trusted paths with the classifier included.
Final formatting, documentation notes and the activation workflow are being
checked before committing this foundation. Phase E/F and coverage qualification
remain open.

Final activation verification passed all 1430 analyzer tests, including all eight
compiler-owned severity cases. Both real packaged consumers then passed using
the documented editorconfig opt-in: the quadratic case emits its O(n^2) upper
order and input identity; independent inputs emit Unknown complexity while
retaining numeric upper bounds. Neither consumer reports tightness. The complete
IR gate passed 748 tests, including all 35 classifier controls.

The production activation repair fixes an existing packaged SP0060 gap rather
than relying on test-host severity overrides. The final package tests retain
normal build commands and assertions; temporary verbose compiler probes were
removed. Full quality/compatibility, repository formatting and documentation checks passed.
Native asymptotic evidence, independently justified lower bounds, tightness,
checked amortized accounting, validated average-case models and phase F remain
required. This foundation does not mark any phase E requirement complete.

A requirement audit reopened the phase C lifetime item. RootParameterClosurePlan
still admits only static ordinary factories without type parameters returning one root
anonymous delegate, with no nested closure/local-function bodies. The existing
finite owned graph model is implemented and tested, but the previously named
broader capture/call lifetime composition remains required. The earlier statement
that phase C was fully complete overstated that evidence; admission limits do not
make this remaining requested work optional.

## Native asymptotic upper evidence

Native resource proofs now classify the owned inferred upper expression and
publish structured growth evidence. The record contains the versioned extension
model, canonical upper order, polynomial/logarithmic degrees, zero status and
source input identity. Growth Unknown results keep the numeric proof while
withholding all partial growth data. Numeric Unknown/refuted outcomes do not
publish asymptotic proofs; observed finite lower bounds do not become universal
asymptotic lower bounds.

Protocol 34 and cache 36 reject prior responses. Strict validation requires the
record for non-vacuous native proofs, checks canonical syntax against structured
fields, checks bounded degrees/input shape, and rejects contradictory known/Unknown
records or growth evidence on refutations. Compiler artifact 48, manifest 6,
resource program/evidence 5 and expression model 1 remain unchanged.

The first native build found formatting violations in the expanded classifier
constructor. Canonical whitespace formatting repaired them. The representative
native, cache/SARIF and malformed-record run then passed all 17 tests. Broader
resource/memory/lifetime/protocol regressions, the full IR suite with structured
field assertions, and strict packaged growth/stack workflows are in progress.
This implements native upper evidence; independent lower bounds, tightness,
amortized rules, validated average-case models, broader phase C lifetime
composition and phase F remain required.

The published foundation head 691b72875 has no coverage qualification result:
its coverage workflow was cancelled. The latest numerical gap inventory remains
stack head eec823652 at 3720/3910 changed trusted lines (95.14%, 190 gaps).
The unchanged 100% floor and fresh exact-head CI remain mandatory.

The broader native run passed 819 of 820 cases and found that string-length
inputs were still rejected before growth classification. Array-length input
ownership already passed. The repair uses the existing trusted intrinsic
sequence-length matcher, admits string-length contract sizes, and preserves
source-call/constructor string-size substitution. Fixed repeated-character
strings and literals retain their actual size expressions; bindings are removed
after each call. A focused follow-up passed all 24 native/consumer controls,
including direct/callee/constructor string lengths and repeated calls with
different literal sizes. Added controls require Unknown for fake Length getters,
unsupported aliases and changing non-ranking recursive string arguments.

Four real strict package growth/stack cases passed before adding the new packaged
string-length case. The IR task's first attempt stopped in restore with exit 139;
after confirming its process had terminated, a fresh isolated CLI/cache retry
passed all 748 tests. The final broad worker gate, full analyzer suite and five
strict packaged growth/stack/string workflows are now running. No floor or
coverage exclusion changed.

Final feature verification passed all 828 affected worker tests, all 748 IR tests
and five actual strict package workflows (known growth, independent-input Unknown,
string-length contracts, and both stack metrics). The full analyzer run passed
1429 cases and identified one stale final-compilation protocol assertion expecting
33; it now expects 34 and its isolated check is in progress. Quality/compatibility,
documentation generation and 725 local links passed. Layout validates 2745 source
files, 59 projects and all 151 pinned common files. The authoritative production
inventory validates all 520 trusted paths. Complete repository formatting passed.

Latest fetched master advanced by two commits: 1cdf105b0 adds proven HashSet
ICollection dispatch, and 11a70a2f1 trims verifier references and internalizes
implementation assemblies. They must be merged after this feature checkpoint,
with new implementation types following the revised visibility policy and affected
workflows verified on the combined tree. This checkpoint remains unqualified
until that integration, fresh exact-head CI and unchanged coverage enforcement.

The native feature and progress document are committed in b3be527fb. Latest
master 11a70a2f1 merged in c9f8174ac after resolving the protocol-model visibility
conflict. Native resource types and the internal IR budget exception follow the
new implementation visibility policy. A type-specific CA1064 suppression explains
that the budget exception is an internal signal; it is not a consumer API.

The merged architecture suite passed all 10 cases, including the compiler-neutral
worker closure. Merged compilation initially flagged the internal exception's
design rule; the scoped annotation repaired it. Full merged worker, analyzer and
strict package checks are running. The separately repaired final-compilation
protocol test passed before this merge. Both aggregate and changed-trusted-code
coverage floors remain 100%; upstream declaration-only baseline updates were
preserved, with no resource-source exclusions added.

Merged quality/compatibility and complete repository formatting passed. Layout
validates 2747 sources, 59 projects and all 151 pinned common files; documentation
and 726 local links passed, with 521 trusted paths classified.

The next merged compilation exposed inconsistent partial fixture accessibility:
master internalized WorkerTests and its enum parameter types, while branch-added
resource partial declarations were still public. The 13 affected fixture
declarations now follow the same internal visibility policy; no assertions or
test cases were removed. The worker/resource/HashSet gate is running again on
that adaptation. Strict packages must be rerun after it compiles. The merged
analyzer task stopped in restore with exit 139 before tests; its retry is deferred
until the current worker task finishes to serialize restore-heavy verification.
These integration adaptations are not yet qualified or published.

The repaired combined worker gate passed all 920 selected cases, including the
resource/memory/lifetime/protocol/cache/SARIF families and merged HashSet dispatch.
The serialized analyzer retry passed all 1430 tests. This confirms the visibility
repair and current protocol-seal assertion on the integrated tree. The subsequent
strict package task is running from the same source state. Completion still
requires its result, publication and fresh exact-head CI; all remaining phase C/E/F
requirements and the unchanged coverage floor stay open.

Final merged verification passed all 920 selected worker cases, all 1430 analyzer
tests, all 10 architecture tests and all 11 selected strict package/runtime-closure
cases. Quality/compatibility, full formatting, documentation, 726 local links,
2747-source layout and the 521-path trusted inventory passed. This validates the
internal implementation and partial-fixture adaptations without weakening tests.

The next fetched master commit 101971bba changes only .gitignore for local
development/scratch outputs. It will be incorporated after committing these
verified integration repairs; it changes no production or test code. Fresh
pushed-head CI and unchanged 100% coverage qualification still remain required.

Verified integration repairs are committed in eacbc7951. Ignore-only master
101971bba merged cleanly in be7073b50; no production/test code changed after the
passing combined-tree gates. The next publication includes this complete
checkpoint. Native lower bounds/tightness, broader lifetime composition, amortized
and validated average-case models, phase F and final coverage/CI remain open.

## Resource transport and memory boundary qualification

Targeted gap work now covers allocation envelopes after base64 expansion, malformed
root/integer fields, unknown allocation kinds, resource-program enum/arity/depth
limits, unrelated activation payloads, noncanonical managed-allocation encodings
and maximum arithmetic transport. Allocation-kind admission is centralized in
the existing exhaustive dispatch; removing its redundant early kind check does
not admit any new shape. Invalid storage kinds remain rejected separately.

Detached activation/managed-allocation records must produce no partial native
costs. Memory results must contain either bounds or an Unknown reason, and mixed
storage intervals keep cumulative, auxiliary and retained lower/upper values
distinct. These tests exercise semantic rejection and published projections; they
do not replace the unchanged coverage requirement.

Focused verification passed all 26 allocation-codec cases, all 63 combined
allocation/program transport cases, all 92 native allocation/program cases and
all 23 finite memory-trace cases. Full analyzer and IR suites are running
sequentially. No coverage percentage improvement is claimed until the aggregate
report measures this checkpoint.

For published head 2d58b8593, all four coverage shards passed; its aggregate is
still running. The full phase C/E/F and 100% qualification requirements remain
open. No baseline floor or resource-source exclusion was changed.

Published native-growth head 2d58b8593 passed all four coverage shards and reached
aggregate enforcement at 3926/4119 changed trusted lines (95.31%, 193 gaps), failing
the unchanged 100% floor. Its report and deduplicated gap inventory are preserved.
The largest remaining groups are memory extraction/replay, call lowering and
violation replay. This result predates the current transport/ownership/memory
boundary additions; those additions have no measured aggregate claim yet.

The full analyzer suite for the boundary checkpoint passed all 1450 tests.
The native allocation/program suite passed 92 tests and the finite memory-trace
suite passed 23. Quality/compatibility, generated documentation and 726 local
links passed. The full IR and repository formatting checks are still running.
All remaining feature scope stays unchanged.

Final boundary qualification passed all 1450 analyzer tests and all 752 IR tests,
plus the 92 native allocation/program and 23 finite memory-trace cases recorded
above. Repository quality/API compatibility, full formatting, documentation
generation and 726 local links passed. No structural project change was made.
The checkpoint is ready for publication and fresh exact-head coverage measurement;
it does not claim the 193-gap inventory is closed or any unfinished feature done.
