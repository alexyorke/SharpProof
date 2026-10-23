# Conditional task runtime allocation bounds

Task-root `AllocatedBytes` analysis can use explicitly declared runtime premises.
These declarations are assumptions supplied by the caller, not observations of
the worker's runtime or facts inferred from reference assemblies. Source work,
compiler carriers and runtime work remain separately accounted for.

## Admission and ownership

The root must already satisfy the isolated task-join compiler projection. Both
returned joins and the admitted configured-await root are supported. One
`ResourceTaskDeployment` declaration and all eleven
`ResourceTaskRuntimeUpperBound` quantities are required on that root. There are
no numeric defaults. Each quantity requires a nonnegative integer and printable
supporting evidence; deployment requires a SHA-256 manifest identity and evidence.

The deployment profile is
`CoreClr8032LinuxX64OrdinaryJitStockClosed`: stock CoreCLR 8.0.32 Linux x64,
ordinary JIT at every tier, unchanged helper bindings and emitted module, default
scheduler, default or suppressed execution context, invariant culture and stock
resources, and a closed loader with no dynamic or collectible assemblies. ReadyToRun, NativeAOT,
instrumentation, external callbacks, external lifecycle mutation,
re-registration and resurrection are excluded. Internal stock retries are
included in the quantitative budgets, including later internal finalization. This is a declared deployment condition;
the analyzer does not detect it on the machine running the worker.

The collector reads constants without executing attribute constructors. It
binds declarations to the root, emitted module, compiler and framework identity.
Native verification additionally binds the reference/layout catalog and all
twelve manifest assumption identities. Duplicate, missing, malformed, foreign
or unsupported declarations produce `Unknown` without a partial numeric bound,
used dependencies or a successful proof core.

## Quantity units and accounting

An episode is an outermost fault-processing entry, including existing-exception
processing, rethrow, dispatch capture/restore, recording and notification. Nested
fault construction and replacement retries belong to that episode. Re-entry
after exit starts another episode. All allocation counts include attempted,
abandoned, losing and retried candidates.

| Quantity | Units and scope |
| --- | --- |
| `RawTraceArrayAttemptsPerFaultEpisode` | Raw trace allocation attempts per episode, including growth, affinity, restore and dispatch copies |
| `RawTracePayloadBytesPerArrayAttempt` | Payload bytes per raw trace array attempt, including imported/copied state |
| `ResolverArrayAttemptsPerFaultEpisode` | Resolver array allocation attempts per episode, including copies and growth |
| `ResolverSlotsPerArrayAttempt` | Reference slots per resolver array attempt |
| `FaultConstructionBytesPerFaultEpisode` | Cumulative managed instance bytes for transitive fault construction, messages and replacement retries per episode |
| `ResolverReferentBytesPerFaultEpisode` | Cumulative managed instance bytes for fresh loader/resolver referents and losing candidates per episode |
| `SchedulingBytesPerChildStartAttempt` | Remaining transitive scheduler, queue and worker control managed bytes per child start attempt |
| `JoinBytesPerJoinAttempt` | Remaining transitive runtime control managed bytes per join attempt |
| `AwaitBytesPerConfiguredAwait` | Remaining transitive runtime control managed bytes per configured await |
| `InitializationBytesPerInvocation` | All causally reached transitive cold initialization and retry managed bytes per invocation |
| `TotalFaultEpisodesPerInvocation` | Total source, helper, runtime, scheduler, initialization and existing-exception episodes per invocation |

Checked compiler/source allocation sites are excluded from these runtime domains
and charged independently. Remaining allocations are classified in this order:
initialization, trace arrays, resolver referents, fault construction, remaining
control work. Remaining control covers the entire causal operation lifecycle, including async builder entry/completion, fault recording, dispatch captures, notification objects and later internal holder finalization. Fixed runtime task/fault carriers are included in these domains;
the private fixed-carrier bounds are not added a second time.

For owned child count `N`, configured-await indicator `A`, source cumulative
bytes `C`, compiler carrier bytes `D`, declared episode count `E`, raw attempts
`R`, raw payload `P`, resolver attempts `Q`, resolver slots `L`, and declared
byte budgets `F`, `V`, `S`, `J`, `W`, `I`, native checking composes:

```text
C + D + N*S + J + A*W + E*(R*ByteArray(P) + Q*ReferenceArray(L) + F + V) + I
```

Array header, reference size and alignment come from the separately bound layout
model. Child and compiler allocation costs and the operation multiplicities are
checked independently. The total episode count is a separate runtime premise;
it is never inferred from the number of source fault candidates. An insufficient
declared resource limit returns `Unknown`, since an upper estimate cannot prove
a concrete violating execution.

## Results and limits

Conditional proofs use execution scope
`joined-isolated-cumulative-runtime-premises`. JSON evidence reports every used
quantity, unit, scope, evidence reference, deployment profile/catalog digest,
root/module/compiler/reference identity and canonical assumption digest. SARIF
includes the scope and the same resource evidence. Assumption policy applies to
these declarations as `UserAssume` premises.

Raw trace capacity is not a physical frame-count proof. This model uses declared
attempt and capacity bounds directly, including imported traces, and does not
substitute logical source depth or a seed capacity for physical CLR frames.
Source-derived closure of every runtime/helper frame and initialization path is
a separate future strengthening. A deployment or quantity declaration that is
false invalidates the conditional guarantee.

## Verification progress

The private arithmetic checker passed 21 targeted tests; the Linux Int32 helper
selector passed 18 targeted tests. The package's existing no-premise consumer
passed its corrected `Unknown` assertion against the preceding production head.
All 1736 selected worker tests passed, including the integrated compiler/worker,
response tamper, cache, policy and SARIF checks for both root forms. The Attributes
suite passed 83 tests, the frontend catalog two, and analyzer resource/projection
checks 296. Exact-head hosted qualification and the required coverage gate remain open.
