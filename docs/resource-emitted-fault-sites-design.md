# Owned emitted fault sites: implementation design

Status: implemented; combined validation pending. This slice continues the checked CLR/helper-frame bridge in the resource-analysis roadmap. It does not choose between explicit reported runtime assumptions and derivation from a closed program entrypoint. Cumulative task runtime bytes remain Unknown.

## Outcome and smallest useful slice

Carry complete, bounded emitted source bodies through the existing compiler-owned fork/join envelope. A compiler-neutral scanner derives every admitted primitive fault site from those bodies. A native consumer validates ownership, scans all bodies again, derives conservative helper alternatives, and participates in the actual `AllocatedBytes` task-root rejection path.

The first delivery must include producer, transport, native consumer, and an actual compiler-artifact-to-worker test. A record used only by a new direct unit test is insufficient. Successful structural checking does not authorize a numeric helper-frame, trace-prefix, or root-byte result.

The smallest slice handles the complete existing emitted-body instruction grammar, but its runtime selection result may remain unresolved. In particular, `newarr System.Int32` must preserve the dynamic value-array helper obligation. It must not select the already reviewed portable helper merely because that summary is available.

## Baseline authority and reusable components

- `CompilerTaskCarrierCollector.CaptureSourceBodies` scans the exact cached PE and checks every distinct forwarded child method, then publishes only method tokens (`CompilerTaskCarrierCollector.cs:24-49`). Its existing admission rejects calls and handlers, admits only framework `System.Int32` for `newarr`, and scans through the complete method (`:55-209`).
- `CompilerTaskSourceBodyInventory` currently contains root identity, module hash, and method tokens (`CompilerForkJoinProjection.cs:74-84`). The carrier codec requires that method set to equal the callback forwarders' child target set (`CompilerForkJoinProjectionCodec.Carriers.cs:160-176`).
- `NativeTaskCarrierStorageVerifier.CheckSourceBodies` independently repeats the root/module/token-set binding (`:91-119`). `CheckOwnedSourceFrames` already composes one source invocation and the checked callback invocation per completed pass (`:44-86`). Reuse these checks; do not replace them with caller-provided counts.
- `NativeForkJoinSourceCostVerifier.CheckSourceActivations` already derives logical source depth from owned resource programs and argument substitution (`:56-92`). It is not a physical trace-prefix budget.
- `ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames` already checks seven reviewed entry-local summaries (`:57-80`). It expressly excludes opcode selection, managed calls inside helpers, and unwind visits. Do not duplicate those summaries.
- `NativeTaskIrFaultOriginVerifier` already derives conservative IR fault candidates. Preserve that analysis: the emitted-body inventory covers the emitted instruction/helper boundary, a different obligation. A mismatch between the two representations is not a reason to reinterpret or silently discard either representation.
- `ArtifactValidator.cs:4-25` explicitly treats the enclosing compiler artifact as trusted build output and checks its digest. A module hash or a newly added IL hash does not independently prove that arbitrary supplied bytes came from a module. This design retains the existing compiler-artifact trust boundary; it does not claim independent PE authentication or introduce an IL verifier.

## Records and wire representation

Add a sibling capture property to `CompilerTaskCarrierShapes`, leaving `SourceBodies` and its existing frame admission intact:

```text
CompilerTaskEmittedFaultBodyCapture
    Inventory: CompilerTaskEmittedFaultBodyInventory?
    UnknownReason: string?

CompilerTaskEmittedFaultBodyInventory
    RootCallableId: string
    ModuleSha256: string
    CompilerIdentity: string
    FrameworkAssemblyIdentity: string
    Bodies: ImmutableArray<CompilerTaskEmittedFaultBody>

CompilerTaskEmittedFaultBody
    MethodToken: int
    Code: ImmutableArray<byte>
    CodeSha256: string
    ArrayTypeOperands: ImmutableArray<CompilerTaskEmittedArrayTypeOperand>

CompilerTaskEmittedArrayTypeOperand
    IlOffset: int
    TypeToken: int
    ElementKind: closed enum with FrameworkInt32 as its only admitted value
    AssemblyIdentity: string
```

`Code` is the complete `MethodBodyBlock.GetILBytes()` result, not a window around fault sites. `CodeSha256` is computed from exactly those bytes and provides a stable body identity and corruption check; it is not a second provenance authority. Length and instruction/site counts are derived instead of supplied as numeric premises. `ArrayTypeOperands` records the metadata ownership fact that raw IL alone cannot resolve. No runtime helper name or numeric frame bound is supplied by the compiler.

The capture is either a complete inventory or a bounded nonempty Unknown reason, never both. An absent property value means evidence unavailable, not an empty body set. Every valid inventory contains one body for every distinct owned child target, including bodies with zero ordinary primitive fault sites.

Encode the capture under a new carrier `faultBodies` property. Use the existing `{unknown, value}` convention; use base64 for each complete `code` byte array. Give each nested object an exact property set, with duplicate and unknown properties rejected. Encode bodies in increasing method-token order and array operands in increasing IL-offset order; require that canonical order on decode. Use canonical lowercase SHA-256 strings consistently with existing protocol helpers.

## Ownership and completeness

The collector must obtain `Code`, method token, exception-region admission, and array type facts from the same cached `_image` and `PEReader` used for the existing source-body inventory. Capture is atomic across all methods. Do not emit a successful inventory after a later method fails admission.

The owning root is the selected root entry, not an arbitrary child-provided identifier. Require all of the following on transport and again at the native boundary for in-memory callers:

1. Inventory root, module, compiler identity, and framework identity match the containing carrier/projection and selected root. The framework identity also binds to the existing reviewed runtime/reference checks when a runtime selection consumer is requested.
2. The body method-token set equals both `SourceBodies.Inventory.MethodTokens` and the distinct callback forwarder child target set. Reject absent callback/source evidence, duplicates, extra methods, and incomplete later-method coverage. Retain existing limits of 1-16 distinct source bodies and valid nonzero MethodDef tokens.
3. Every body has 1-4096 IL bytes, the existing source-body admission limit. Retain the overall 1 MiB nested payload cap. These are analyzer transport/admission budgets, not runtime stack/frame caps. Sixteen maximum-sized bodies contribute at most 65,536 raw code bytes before base64 and metadata overhead.
4. The scanner starts at byte zero, makes positive progress, decodes every byte to `Code.Length`, admits only the grammar below, and requires the existing final `ret` condition. Every encoded branch destination is in range and starts an admitted instruction. Loops remain admitted; absence of cycles is not required. This is lexical structure/admission checking, not a new stack/type-safety verifier.
5. Exactly one array-operand fact exists for every decoded `newarr`, and none exists at any other offset. The fact's token equals the instruction's raw InlineType token. Require the current TypeRef form and the exact reviewed `System.Int32` namespace/name/assembly ownership admission. Reject TypeDef, TypeSpec, constructed-generic, reference-element, and fake framework types as today.
6. A successful repeated scan derives the complete site set from the complete code. There is no separately serialized sparse site list that can omit a faulting instruction while retaining a successful completeness flag.

Reusing a body for several callback projections is valid: inventory coverage counts distinct emitted bodies, whereas dynamic callback multiplicity remains owned by the existing forwarder/projection checks. A static site count is never an execution count; a loop may visit a site repeatedly.

Source targets are already admitted as ordinary static, non-generic methods in non-generic types by `ResourceForkJoinPlan.cs:126-127`. Preserve that fact from the same compiler-owned selection. A hash of bytes alone cannot establish that a method is not a type initializer.

## Shared scanner and exact admitted grammar

Add a small compiler-neutral primitive scanner in `SharpProof.CompilerArtifact`, used by both the collector and worker. Keep Roslyn symbols, `MetadataReader`, and PE ownership resolution in the collector. The existing general `CompilerScalarIlDecoder` lives inside `CompilerTotalIlBodyProvider.cs:239-333` and mixes lexical parsing with Roslyn-specific candidate/provider logic; do not reference that assembly from the worker or move the whole provider into the compiler-neutral closure.

One shared closed opcode/operand table must own this new primitive grammar. `ValidateSourceBody` should delegate lexical admission to it and retain metadata resolution in the collector, preserving its existing public-to-tests signature and failure semantics. Do not maintain a second independent whitelist in the native worker.

| Operand encoding | All admitted operations |
| --- | --- |
| None: fixed argument/local forms | `ldarg.0` through `ldarg.3`, `ldloc.0` through `ldloc.3`, `stloc.0` through `stloc.3` |
| Unsigned byte variable index | `ldarg.s`, `ldloc.s`, `stloc.s`, `starg.s` |
| Unsigned 16-bit variable index | `ldarg`, `ldloc`, `stloc`, `starg` |
| None: integer constants | `ldc.i4.m1`, `ldc.i4.0` through `ldc.i4.8` |
| Signed byte / signed 32-bit integer | `ldc.i4.s` / `ldc.i4` |
| Signed byte displacement | `br.s`, `brfalse.s`, `brtrue.s`, `beq.s`, `bne.un.s`, `bge.s`, `bge.un.s`, `bgt.s`, `bgt.un.s`, `ble.s`, `ble.un.s`, `blt.s`, `blt.un.s` |
| Signed 32-bit displacement | The same branch families without `.s` |
| InlineType, 32-bit metadata token | `newarr`, with the exact framework Int32 operand fact above |
| None: bookkeeping | `nop`, `ret`, `dup`, `pop`, `ldnull` |
| None: arithmetic/bitwise | `add`, `sub`, `mul`, `div`, `div.un`, `rem`, `rem.un`, `neg`, `not`, `and`, `or`, `xor`, `shl`, `shr`, `shr.un` |
| None: checked arithmetic | `add.ovf`, `add.ovf.un`, `sub.ovf`, `sub.ovf.un`, `mul.ovf`, `mul.ovf.un` |
| None: integer conversions | `conv.i1`, `conv.u1`, `conv.i2`, `conv.u2`, `conv.i4`, `conv.u4` |
| None: checked integer conversions | `conv.ovf.i1`, `conv.ovf.u1`, `conv.ovf.i2`, `conv.ovf.u2`, `conv.ovf.i4`, `conv.ovf.u4`, and each corresponding `.un` form |
| None: comparisons | `ceq`, `clt`, `clt.un`, `cgt`, `cgt.un` |
| None: array operations | `ldlen`, `ldelem.i4`, `stelem.i4` |

This is the complete current whitelist in `CompilerTaskCarrierCollector.cs:81-203`. No calls, constructors, explicit throw/rethrow, fields, prefixes, switch, address-taking, native-int/64-bit/floating constants or conversions, other array-element instructions, casts/unboxing, or EH regions are newly admitted. Operand widths describe encoding, not a new inferred stack-type proof. Valid emitted IL and metadata ownership retain their current compiler authority.

The scanner derives a native `EmittedPrimitiveFaultSite` with method token, byte offset, raw admitted opcode, and a closed candidate-family set:

| Operation family | Ordinary primitive candidates / remaining runtime route |
| --- | --- |
| Checked arithmetic and checked conversions | Overflow route; JIT-code-generation selection still needs its own reviewed coverage |
| `div`, `rem` | Divide-by-zero and signed-overflow candidates conservatively; optimized, hardware-fault, and explicit helper alternatives remain separate |
| `div.un`, `rem.un` | Divide-by-zero candidate; do not import a signed-overflow premise |
| `ldlen` | Null-reference route |
| `ldelem.i4`, `stelem.i4` | Null-reference and range-check routes |
| `newarr Int32` | Array-allocation route, including negative-length overflow, dimension/allocation failures and their helper closure |
| Remaining admitted operations | No additional ordinary primitive site in this emitted grammar; this is not a claim of runtime-fault-free execution |

Do not equate this classification with exact throw feasibility or resource-program costs. It may conservatively retain eliminated or infeasible sites. `newarr` is a runtime route even if source-domain analysis proves its length nonnegative.

## Runtime helper alternatives and unresolved paths

All runtime source below is pinned to .NET 8.0.32 commit `b9a49e4a6d3a444a3685e318c74a7892d01873f7`. The retained importer, selector, helper table/body, FCall identity, and frame mapping files were checked against their research-manifest SHA-256 values during this audit.

For an ordinary JIT `newarr Int32` site, derive an alternative set, not a guessed active helper:

- [Importer lines 9437-9445](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/jit/importer.cpp#L9437) call `getNewArrHelper` when neither the ReadyToRun nor frozen route applies.
- [Selector lines 5947-6005](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jitinterface.cpp#L5947) allow `NEWARR_1_DIRECT` under allocation stress/logging/tracking and `NEWARR_1_VC` for the ordinary Int32 value-element case. The generic/reference and double-alignment branches do not describe the owned Int32 operand. Derive that exclusion from the exact type fact and applicable platform source, not a name match alone.
- [Helper table lines 81-85](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/inc/jithelpers.h#L81) bind DIRECT to `JIT_NewArr1`, but VC is a dynamic helper entry initially associated with that worker. The complete binding/override and architecture implementation closure has not been reviewed. The available portable VC function is not proof that the dynamic VC slot selects it.
- [Worker lines 2769-2805](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jithelpers.cpp#L2769) installs its helper frame, throws for negative length, and calls `AllocateSzArray`. Reuse its existing zero direct explicit-trace-frame summary; managed callbacks/fault construction inside that route are still outside that summary.
- [Importer lines 9378-9407 and 9413-9434](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/jit/importer.cpp#L9378) show separate frozen and ReadyToRun paths. The ordinary source-method ownership excludes the static-constructor pattern. ReadyToRun exclusion requires a bound execution-mode premise or closure of its route; the emitted body does not supply that premise. The initial native selector may return an ordinary-JIT conditional alternative set with this explicit unsatisfied obligation, not a complete deployment-level helper result.

The four primitive throw helpers are explicitly listed in `jithelpers.h:119-122`; this does not prove that every admitted opcode uses those entries. Hardware fault mapping exists at `excep.cpp:3075-3080`, and x64 code-generation/lowering can optimize or emit faults differently. Until their selection coverage is reviewed, return a route-closure Unknown rather than assuming an explicit throw-helper call.

Keep compiler classification and runtime closure as different result types. A successful structural inventory may be inspected internally even when the runtime closure check is Unknown. A runtime result containing `DirectHelperFramesUpper` must carry a bound for every possible selected implementation and all required model identities, or carry no numeric value and no successful-model identities. Never combine a known DIRECT result with an unresolved VC result into a partial numeric bound.

## Native consumer and actual root behavior

Add `NativeTaskEmittedFaultSiteVerifier` for structural ownership and derived sites. It first reuses the existing source/forwarder ownership checks, validates the inventory, and scans every body again with the shared scanner. Its successful compiler model is `owned-emitted-primitive-fault-sites-v1`; it has no runtime-proof authority.

Add a narrow `NativeTaskPrimitiveHelperRouteVerifier` that consumes only a successful complete emitted-site result and the runtime layout/reference context. It produces either a complete conditional route result or an explicit closure Unknown. In the first slice, dynamic VC, execution-mode, hardware/JIT-selection, and helper-managed-call obligations remain explicit and unresolved. Use a separate versioned private route model; do not reuse the stronger-sounding existing completed-pass frame model for this result.

Connect these checks to `NativeResourceContractVerifier.VerifyTaskJoin` for `AllocatedBytes`, before the current generic unsupported-metric return (`NativeResourceContractVerifier.cs:219-224`). This branch must:

1. Retain root/entry/projection ownership validation and bind the existing required layout/reference dependencies before requesting runtime checks. Do not mark a runtime assumption used in a successful proof: this branch still returns Unknown.
2. Return the inventory's specific Unknown if its structural evidence is unavailable or invalid. Invalid transport continues to fail the existing artifact boundary, without publishing a partial callable/resource result.
3. Run helper-route checking only after complete structural success. Return its closure Unknown for unresolved paths. A body with zero primitive sites must continue to return an explicit remaining runtime-frame/root-composition Unknown; it must not be treated as a complete fault model.
4. Publish no inferred numeric upper, trace bound, asymptotic proof, or successful runtime-model proof-core token. Preserve all other metrics' existing behavior. In particular, do not make supported source-work or normal-lifetime claims depend on the new cumulative-byte-only bridge.

This gives the record a real consumer and observable rejection behavior while leaving the full requested model open. Do not route a successful inventory directly to `BoundTraceBuffers`: that API requires all attempted appends, raw/resolver copies, initial resolver capacity, and closed collection scope (`ResourceTaskFaultCarrierCatalog.cs:170-208`).

## Atomic Unknown and control matrix

Use bounded, specific reasons for absent evidence, ownership/coverage mismatch, unsupported/truncated instruction, invalid array operand, scanner budget, unresolved helper implementation, execution-mode coverage, and incomplete cumulative runtime composition. Follow existing cancellation propagation; cancellation is not a successful empty inventory or a swallowed Unknown.

Required controls cover:

- Real Release and Debug compiler emissions, empty primitive bodies, loops containing multiple sites, several distinct children, and shared child methods used by multiple forwarders.
- Every admitted opcode/operand encoding and every fault family against independent emitted-byte decoding. Preserve all existing excluded-shape controls.
- Round trip into the worker; whole-body deletion, duplicate/foreign method, changed root/module/compiler/framework identity, missing callback/source inventories, and later-child failure.
- Missing/extra/wrong-offset array facts, wrong raw token, fake Int32 scope, unsupported token kind, and inconsistent element kind.
- Truncated one/two-byte opcode or operand, unsupported prefix/call, branch into operand bytes, bad code hash, invalid enum, uninitialized arrays, duplicate/missing JSON properties, mixed value/Unknown, and payload/admission budget edges.
- An actual `AllocatedBytes` compiler-artifact-to-worker run that reaches the new consumer. A `new int[...]` child must surface unresolved dynamic/helper closure with no numeric root bound. Zero-site children must still expose remaining runtime composition Unknown. Missing or corrupted later evidence must not leave an earlier site/frame result attached.
- Existing source-array/element, loop-entry, normal lifetime and configured/returned-root controls retain their existing outcomes. The new runtime selector is not authorization to change the public proof policy or narrow scope to warm execution.

The implementation adds 55 emitted-site test cases covering the producer, strict transport, native ownership checks and actual cumulative-byte Unknown consumer. Combined validation is pending; this list remains the verification checklist rather than a claim that every obligation has passed.

## Compatibility and model versions

The current uncommitted implementation uses nested codec 12 and response cache 66. Protocol 43, manifest 6 and outer artifact 55 are unchanged. New production paths are classified in the acceptance contract. The following baseline transition rationale remains applicable:

The checked baseline is nested fork/join codec 11, response cache 65, protocol 43, manifest 6, outer compiler artifact 55. Adding `faultBodies` changes the exact nested property set: advance nested codec to 12 and reject codec 11 for the new shape. The real worker Unknown path changes, so advance the response cache to the next available version (66 if no concurrent allocation has occurred). The coordinator must serialize/reserve version edits and re-read current values first.

Keep protocol 43, manifest 6, and outer artifact 55 if their structured schemas remain unchanged, matching prior nested-envelope changes. The outer fork/join field is already an opaque nested payload. Reassess only if implementation adds public structured result fields. Leave the existing compiler-carrier and direct/owned-source frame model identities unchanged because their claims are unchanged. Add the new compiler/route identities without adding successful public proof-core tokens.

Coordinate the response-cache owner in `src/SharpProof.Worker.Protocol/ProtocolModel.generated.cs`, cache expectations in `ProtocolJsonTests.cs`, `eng/acceptance/contract.json`, and current compatibility prose in `docs/release-constants.md`, `docs/architecture.md`, and `docs/resource-bounds.md`. Keep the acceptance trusted-path classification current for new production files. Do not change coverage floors or exclusions.

## Implementation ownership record

The coordinator assigned the producer, scanner, transport and native consumer to one writer, and the new emitted-site fixture to a separate test writer. Both returned ownership before combined formatting. The table records the intended boundaries:

| Owner role | Existing files to reserve exclusively | New files to own |
| --- | --- | --- |
| Producer and canonical scanner | `src/SharpProof.CompilerCollector/CompilerArtifact/CompilerTaskCarrierCollector.cs`; `src/SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs`; `src/SharpProof.CompilerArtifact/CompilerForkJoinProjection.cs` | `src/SharpProof.CompilerArtifact/CompilerTaskEmittedFaultBody.cs`; `src/SharpProof.CompilerArtifact/CompilerTaskPrimitiveIlScanner.cs` |
| Nested transport | `src/SharpProof.CompilerArtifact/CompilerForkJoinProjectionCodec.cs`; `src/SharpProof.CompilerArtifact/CompilerForkJoinProjectionCodec.Carriers.cs` | Optional separate partial `src/SharpProof.CompilerArtifact/CompilerForkJoinProjectionCodec.FaultBodies.cs` |
| Native consumer | `src/SharpProof.Worker/NativeResourceContractVerifier.cs` | `src/SharpProof.Worker/NativeTaskEmittedFaultSiteVerifier.cs`; `src/SharpProof.Worker/NativeTaskPrimitiveHelperRouteVerifier.cs` |
| Runtime selection catalog, if separated from native consumer | None of the existing HCall catalog is required to change for this slice | Optional `src/SharpProof.Specs/ResourcePrimitiveHelperRouteCatalog.cs`, only with a concrete consumer |
| Test owner | `src/SharpProof.Worker.Tests/NativeTaskCarrierStorageTests.cs`; `src/SharpProof.Worker.Tests/NativeResourceForkJoinArtifactTests.cs`; relevant `NativeResourceTaskRootTests.cs` assertions only after checking actual reason expectations | Prefer dedicated `src/SharpProof.Worker.Tests/NativeTaskEmittedFaultSiteTests.cs` to avoid the just-finished collector-controls writer's file |
| Coordinator compatibility and delivery | `src/SharpProof.Worker.Protocol/ProtocolModel.generated.cs`; `src/SharpProof.Worker.Tests/ProtocolJsonTests.cs`; `eng/acceptance/contract.json`; `docs/release-constants.md`; `docs/architecture.md`; `docs/resource-bounds.md`; `docs/resource-analysis-roadmap.md` | None |

`CompilerTotalIlBodyProvider.cs`, `NativeForkJoinSourceCostVerifier.cs`, `NativeTaskIrFaultOriginVerifier.cs`, `NativeTaskSourceExceptionInstanceVerifier.cs`, `NativeTaskCarrierStorageVerifier.cs`, and `ResourceTaskFaultCarrierCatalog.cs` need no semantic edits for this slice. If implementation finds a necessary interface adjustment there, request explicit file ownership before writing. Do not edit the completed tests-only collector controls concurrently.

Source and design ownership have returned to the coordinator. The implementation writer performed no builds, tests, formatting or Git operations.

## Remaining proof obligations after this slice

The compiler-to-helper boundary is useful progress, not closure of the roadmap item. Complete dynamic/architecture helper selection, helper-managed calls and fault construction, framework catch/rethrow segments, repeated/failed unwind attempts, cold initialization, referenced payloads, scheduler failures, and root composition remain required.

For example, `Task.ExecuteWithThreadLocal` chooses direct or execution-context execution and catches at `Task.cs:2334-2356`; the execution-context runner catches the first pass, captures, restores, and rethrows at `ExecutionContext.cs:261-289`. CLR trace appends occur in explicit-frame and managed-frame first-pass processing at `exceptionhandling.cpp:2323-2324` and `:2601-2606`. Counting one completed source pass cannot replace a bound on those cumulative events.

The pending human policy decision remains a real blocker for binding caller-stack and initialization premises to a public root proof. Nothing in this design selects a numeric premise, sets a stack/frame cap, discards cold execution, or turns private component success into root proof authority.
