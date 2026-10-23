# Resource coverage admission audit

This ledger records measured gaps and their admission context. It grants no
coverage exemption and does not establish that every gap is impossible. The
unchanged 100% changed-TCB floor still applies.

## Qualified evidence and scope

The latest measured report is [workflow 39933180738](https://github.com/alexyorke/SharpProof/actions/runs/39933180738).
All fifteen shards passed. It covers merge commit
`7e5c83bb58ea44e89f228e5ceab81292bf699643`, whose verified parents are master
`70fba9efeefe8cb621cbf834998941985a5227ee` and feature head
`17c2d67036a0fd1ae1213e205bc93172cb788ad1`. It measures 8231/8329
changed-TCB lines (98.82%), leaving 98 gaps against the unchanged 100% floor.
The 102 added controls closed 49 of the previous 147 gaps. Local follow-up
tests are not counted in this report. The report and deduplicated gap list are
retained in `artifacts/resource-17c-coverage-evidence` and
`artifacts/resource-17c-coverage-gaps.txt`.

The baseline partition below remains historical evidence. The remaining 98
units are under renewed review, distinguishing enforced helper preconditions
from invariants supplied only by current callers. Neither classification
grants a coverage exemption.

The follow-up batch adds 26 verified cases (128 across eleven boundary fixtures
in total). Its local checks passed 20 analyzer/SDK/source cases, 19 frontend
cases, 10 IR cases, 25 focused worker cases and two replay-observation cases.
It covers genuine parenthesized initializer syntax, recursive varargs,
256/257-parameter expression-bodied closures, nested object initializers and
partial-property recovery, as well as explicitly scoped typed helper negatives.
A malformed typed allocation observation is tested separately from interpreter
output, with atomic Unknown on its mismatched identity. The coverage effect of
this batch is not measured yet. Formatting, lint and layout passed.

The report from [workflow 39925263744](https://github.com/alexyorke/SharpProof/actions/runs/39925263744) covers merge commit
`1a583de04cb43b72af502e822682ea702810c873`, whose parents are master
`70fba9efeefe8cb621cbf834998941985a5227ee` and feature head
`ca3b70301e1404040a8ce166b1f37f5b3082b48f`. All fifteen shards passed. Against
that master, 8182/8329 changed-TCB lines were covered (98.24%); 147 were not.
The report and exact deduplicated gap list are retained locally under
`artifacts/resource-ca3-coverage-evidence` and
`artifacts/resource-ca3-coverage-gaps.txt`. They are ignored verification outputs.

The following partition covers all 147 units without overlap. The 102 new controls passed locally in the combined worker, analyzer, frontend
and IR checks. A fresh merged report is still required; their expected line
coverage is not counted as achieved coverage.

| Assigned component | Gap units | New controls and remaining audit |
| --- | ---: | --- |
| Carrier collector and other collector admission, excluding runtime declarations | 47 | Nine source/admission cases and six async ownership cases; compiler-emission invariants and bounded-stream checks remain. |
| Runtime declaration collector, premise identities and native binding | 23 | Thirty declaration, metadata-lookalike, identity and atomic binding controls; canonical-ID uniqueness and namespace recursion invariants remain. |
| Native resource contracts and task runtime arithmetic | 14 | Six comparison-budget/emitted-inventory publication cases; composition/replay catches without established compiled triggers remain unresolved. |
| IR representation, memory replay, growth, layout and helper selection | 16 | Nine representation/lifetime cases passed locally; three constructor boundaries targeted. Other checks depend on factory/registration, replay and layout invariants. |
| Resource analysis in compiler semantics | 16 | Ten real source budget/body/input-composition/call-expansion cases; parameter/storage invariants remain, and foreign compilation ownership has not been validated. |
| Frontend closure, initializer, storage and loop admission | 12 | Sixteen real compilation/operation/replay cases; nested capture count, descendant count and per-variable initializer invariants remain. |
| Native emitted-site, program, allocation, accounting and layout checks | 7 | Three emitted-source/stale-binding cases; earlier deterministic scan, kind/rank checks and immutable node limits dominate the other five. |
| Native replay, memory, stack, expected evidence and analyzer diagnostic fallback | 9 | Four zero-probability worst-case controls; eight worker guards are dominated by previous validation. The null method symbol source case is unresolved. |
| Resource artifact and fork/join codec admission | 3 | Nine proof/gap, payload-budget, constructor, record-copy and detached JSON controls. |
| Total | 147 | No floor, exclusion or production guard changed. |

## Invariant evidence

These are causal findings from source review. They do not change the measured
coverage report. A future source change can invalidate an invariant and requires
renewed review.

- Certified closure frames require the Total factory, whose supported integer
  widths and type kinds exclude the fallback cases in `IrClosureFrameSite`.
  This is a caller invariant: the standalone helper also accepts Legacy inputs.
  A verified helper-only control now rejects a width-zero Legacy integer field;
  it establishes no certified Total lifetime proof.
  Allocation-shape constructors require initialized arrays and valid storage
  kinds; zero multiplication is normalized before growth traversal.
- Replay event bounds follow the admitted observation count. Normal completion
  must end in a handled Return. Cast-depth checks run before the same-depth
  replay fallback. Null receiver execution cannot establish normal completion.
- Helper selection uses the same immutable layout validation and admitted
  constants for worker and portable routes; no case of portable Unknown after worker
  success was established. Cancellation throws instead.
- Resource-program construction enforces recursive node count at most 4096.
  Upper verification rejects invalid rank/program kinds before lower traversal.
  Emitted ownership validation scans the same immutable code before its later
  deterministic scan.
- A reviewed layout and validated literal allocation/frame shapes give constant
  replay bounds. Unsupported reference-array storage fails before violation
  replay. Root activation establishes stack ownership before repetition.
  Expected verification yields complete success or explicit Unknown. Its
  projection helper separately accepts nullable typed records: five verified
  controls cover a complete result and four individually missing fields, with
  atomic `ResourceExpectedEvidenceIncomplete` and no partial evidence.
- Canonical runtime declarations contain exactly eleven unique sorted quantities
  and separate deployment/quantity identity domains. Namespace-name recursion
  stops before the global namespace. These precede the repeated guards.
- Real Roslyn source emission provides the method/token and signature structure
  required by later carrier revalidation. The image stream rejects growth beyond
  its limit before the later image-length check. No compiler assembly emission
  that passes creation but produces a non-assembly reference was established.
  The validator component has a broader input domain than generated MoveNext:
  real emitted constructors now verify mutable versus readonly owned stores,
  and a nil method handle in the same valid image is rejected explicitly.
- Outer lambda capture analysis covers the nested capture union, and the complete
  descendant scan precedes parent traversal. Valid C# supplies one field per
  variable initializer; the new frontend controls inspect actual operations.

## Unresolved reachability

Do not label these proven unreachable merely because no test was found:

- Compiler emission stream overflow within the admitted source limit. Source
  exception shadows now establish a real emit-only failure control.
- Foreign compilation-reference ownership is now covered by verified rejection
  and same-compilation controls; the fresh coverage effect remains unmeasured.
- Runtime arithmetic composition and contract replay budget catches after all
  earlier source, envelope and immutable-limit checks.
- A genuine speculative semantic model now verifies the diagnostic helper's
  missing-declared-symbol fallback through public SDK context construction.
  This is not a normal analyzer-driver callback: that driver rejects trees not
  present in the compilation. A normal-model control reports Proven.

The next stopping condition is a fresh report after the new controls pass,
followed by a deduplicated update of each remaining unit. Any change to coverage
policy requires an explicit decision; this ledger grants no exception.

## Exact baseline units

These are the 147 uncovered units in the qualified report, before the new
controls. Line numbers refer to the unchanged production source at that commit.
They are not a list of approved exclusions.

| File | Uncovered lines |
| --- | --- |
| [ResourceBoundsDiagnostics.cs](../src/SharpProof.Analyzer.Core/ResourceBoundsDiagnostics.cs#L38) | 38 |
| [ResourceBoundsAnalysis.Allocations.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Allocations.cs#L164) | 164 |
| [ResourceBoundsAnalysis.Calls.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Calls.cs#L66) | 66, 84 |
| [ResourceBoundsAnalysis.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.cs#L132) | 132, 158, 160, 182, 204, 354 |
| [ResourceBoundsAnalysis.InputModel.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.InputModel.cs#L47) | 47, 48 |
| [ResourceBoundsAnalysis.Recursion.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Recursion.cs#L54) | 54, 70, 89 |
| [ResourceBoundsAnalysis.Stack.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Stack.cs#L54) | 54, 83 |
| [CompilerForkJoinProjectionCodec.cs](../src/SharpProof.CompilerArtifact/CompilerForkJoinProjectionCodec.cs#L43) | 43 |
| [CompilerLoweredArtifact.Resources.cs](../src/SharpProof.CompilerArtifact/CompilerLoweredArtifact.Resources.cs#L15) | 15, 18 |
| [CompilerCallableLowerer.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs#L71) | 71 |
| [CompilerForkJoinResourceLowering.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerForkJoinResourceLowering.cs#L14) | 14, 38 |
| [CompilerManifestArtifactProducer.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs#L153) | 153 |
| [CompilerTaskCarrierCollector.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerTaskCarrierCollector.cs#L51) | 51, 52, 65, 68, 82, 86, 102, 103, 120, 160, 191, 194, 195, 289, 297, 301, 304, 308, 309, 321, 324, 335, 353, 366, 381, 384, 387, 426, 430, 442, 445, 460, 461, 469, 474, 490, 625, 643, 684, 706, 707, 712, 721 |
| [CompilerTaskRuntimePremiseCollector.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerTaskRuntimePremiseCollector.cs#L22) | 22, 34, 64, 69, 77, 86, 91, 95, 141, 154, 163, 185 |
| [CSharpOperationSemantics.ObjectStorage.cs](../src/SharpProof.Frontend/CSharpOperationSemantics.ObjectStorage.cs#L32) | 32, 38, 42 |
| [RootParameterClosurePlan.cs](../src/SharpProof.Frontend/RootParameterClosurePlan.cs#L129) | 129, 147 |
| [RoslynTotalExpressionLowerer.cs](../src/SharpProof.Frontend/RoslynTotalExpressionLowerer.cs#L291) | 291, 1350 |
| [RoslynTotalProgramLowerer.Calls.cs](../src/SharpProof.Frontend/RoslynTotalProgramLowerer.Calls.cs#L146) | 146, 171, 224, 235 |
| [RoslynTotalProgramLowerer.ResourceLoops.cs](../src/SharpProof.Frontend/RoslynTotalProgramLowerer.ResourceLoops.cs#L32) | 32 |
| [IrClosureFrameSite.cs](../src/SharpProof.Ir/IrClosureFrameSite.cs#L14) | 14, 73, 75 |
| [ResourceAllocationShape.cs](../src/SharpProof.Ir/ResourceAllocationShape.cs#L26) | 26, 70 |
| [ResourceAsymptoticGrowth.cs](../src/SharpProof.Ir/ResourceAsymptoticGrowth.cs#L80) | 80, 120 |
| [ResourceBooleanDistribution.cs](../src/SharpProof.Ir/ResourceBooleanDistribution.cs#L20) | 20 |
| [ResourceMemoryReplay.cs](../src/SharpProof.Ir/ResourceMemoryReplay.cs#L210) | 210, 301, 303, 315, 325, 365 |
| [ResourceRuntimeLayout.cs](../src/SharpProof.Ir/ResourceRuntimeLayout.cs#L160) | 160 |
| [ResourcePrimitiveHelperSelection.cs](../src/SharpProof.Specs/ResourcePrimitiveHelperSelection.cs#L66) | 66 |
| [ResourceTaskRuntimePremiseIdentity.cs](../src/SharpProof.Worker.Protocol/ResourceTaskRuntimePremiseIdentity.cs#L109) | 109, 128, 140, 173, 181, 189, 198 |
| [NativeResourceAccountingVerifier.cs](../src/SharpProof.Worker/NativeResourceAccountingVerifier.cs#L20) | 20 |
| [NativeResourceAllocationVerifier.cs](../src/SharpProof.Worker/NativeResourceAllocationVerifier.cs#L70) | 70 |
| [NativeResourceContractVerifier.cs](../src/SharpProof.Worker/NativeResourceContractVerifier.cs#L28) | 28, 35, 38, 44, 46, 47, 48, 68, 69, 166, 247 |
| [NativeResourceExpectedVerifier.cs](../src/SharpProof.Worker/NativeResourceExpectedVerifier.cs#L74) | 74 |
| [NativeResourceLayoutBinding.cs](../src/SharpProof.Worker/NativeResourceLayoutBinding.cs#L52) | 52 |
| [NativeResourceMemoryEnvelope.cs](../src/SharpProof.Worker/NativeResourceMemoryEnvelope.cs#L60) | 60, 143 |
| [NativeResourceProgramVerifier.cs](../src/SharpProof.Worker/NativeResourceProgramVerifier.cs#L169) | 169, 177 |
| [NativeResourceStackVerifier.cs](../src/SharpProof.Worker/NativeResourceStackVerifier.cs#L73) | 73 |
| [NativeResourceTaskRuntimeBinding.cs](../src/SharpProof.Worker/NativeResourceTaskRuntimeBinding.cs#L17) | 17, 30, 33, 86 |
| [NativeResourceViolationReplay.cs](../src/SharpProof.Worker/NativeResourceViolationReplay.cs#L83) | 83, 140, 153, 266 |
| [NativeTaskEmittedFaultSiteVerifier.cs](../src/SharpProof.Worker/NativeTaskEmittedFaultSiteVerifier.cs#L37) | 37, 44 |
| [NativeTaskRuntimeCostVerifier.cs](../src/SharpProof.Worker/NativeTaskRuntimeCostVerifier.cs#L62) | 62, 80, 81 |

## Exact latest measured units

These 98 units remain uncovered in the latest qualified report. Follow-up
controls are pending verification and measurement; these are not exclusions.

| File | Uncovered lines |
| --- | --- |
| [ResourceBoundsDiagnostics.cs](../src/SharpProof.Analyzer.Core/ResourceBoundsDiagnostics.cs#L38) | 38 |
| [ResourceBoundsAnalysis.Allocations.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Allocations.cs#L164) | 164 |
| [ResourceBoundsAnalysis.Calls.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Calls.cs#L66) | 66, 84 |
| [ResourceBoundsAnalysis.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.cs#L182) | 182 |
| [ResourceBoundsAnalysis.Recursion.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Recursion.cs#L54) | 54, 70 |
| [ResourceBoundsAnalysis.Stack.cs](../src/SharpProof.Compiler.Semantics/ResourceBoundsAnalysis.Stack.cs#L54) | 54, 83 |
| [CompilerForkJoinResourceLowering.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerForkJoinResourceLowering.cs#L38) | 38 |
| [CompilerTaskCarrierCollector.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerTaskCarrierCollector.cs#L51) | 51, 52, 65, 68, 82, 86, 102, 103, 120, 160, 191, 194, 195, 289, 297, 301, 304, 308, 309, 321, 324, 335, 353, 366, 381, 384, 387, 426, 430, 442, 445, 460, 461, 469, 474, 490, 625, 684, 706, 707, 712, 721 |
| [CompilerTaskRuntimePremiseCollector.cs](../src/SharpProof.CompilerCollector/CompilerArtifact/CompilerTaskRuntimePremiseCollector.cs#L34) | 34, 154, 163 |
| [RootParameterClosurePlan.cs](../src/SharpProof.Frontend/RootParameterClosurePlan.cs#L129) | 129, 147 |
| [RoslynTotalProgramLowerer.Calls.cs](../src/SharpProof.Frontend/RoslynTotalProgramLowerer.Calls.cs#L171) | 171, 224, 235 |
| [IrClosureFrameSite.cs](../src/SharpProof.Ir/IrClosureFrameSite.cs#L73) | 73, 75 |
| [ResourceAllocationShape.cs](../src/SharpProof.Ir/ResourceAllocationShape.cs#L26) | 26 |
| [ResourceAsymptoticGrowth.cs](../src/SharpProof.Ir/ResourceAsymptoticGrowth.cs#L80) | 80, 120 |
| [ResourceBooleanDistribution.cs](../src/SharpProof.Ir/ResourceBooleanDistribution.cs#L20) | 20 |
| [ResourceMemoryReplay.cs](../src/SharpProof.Ir/ResourceMemoryReplay.cs#L210) | 210, 301, 303, 315, 325, 365 |
| [ResourceRuntimeLayout.cs](../src/SharpProof.Ir/ResourceRuntimeLayout.cs#L160) | 160 |
| [ResourcePrimitiveHelperSelection.cs](../src/SharpProof.Specs/ResourcePrimitiveHelperSelection.cs#L66) | 66 |
| [NativeResourceAccountingVerifier.cs](../src/SharpProof.Worker/NativeResourceAccountingVerifier.cs#L20) | 20 |
| [NativeResourceAllocationVerifier.cs](../src/SharpProof.Worker/NativeResourceAllocationVerifier.cs#L70) | 70 |
| [NativeResourceContractVerifier.cs](../src/SharpProof.Worker/NativeResourceContractVerifier.cs#L28) | 28, 35, 38, 44, 46, 47, 48, 166 |
| [NativeResourceExpectedVerifier.cs](../src/SharpProof.Worker/NativeResourceExpectedVerifier.cs#L74) | 74 |
| [NativeResourceMemoryEnvelope.cs](../src/SharpProof.Worker/NativeResourceMemoryEnvelope.cs#L60) | 60, 143 |
| [NativeResourceProgramVerifier.cs](../src/SharpProof.Worker/NativeResourceProgramVerifier.cs#L169) | 169, 177 |
| [NativeResourceStackVerifier.cs](../src/SharpProof.Worker/NativeResourceStackVerifier.cs#L73) | 73 |
| [NativeResourceViolationReplay.cs](../src/SharpProof.Worker/NativeResourceViolationReplay.cs#L83) | 83, 140, 153, 266 |
| [NativeTaskEmittedFaultSiteVerifier.cs](../src/SharpProof.Worker/NativeTaskEmittedFaultSiteVerifier.cs#L44) | 44 |
| [NativeTaskRuntimeCostVerifier.cs](../src/SharpProof.Worker/NativeTaskRuntimeCostVerifier.cs#L62) | 62, 80, 81 |
