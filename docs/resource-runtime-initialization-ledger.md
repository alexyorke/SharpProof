# Task runtime initialization and referent evidence ledger

This is a bounded next-implementation artifact for the open cumulative task-byte
item in [phase F](resource-analysis-roadmap.md#f-async-concurrency-and-performance-measurements).
It records evidence checked on 2026-10-22. It does not establish a complete runtime
cost model, a root proof, deployment identity, or a finite bound for an unresolved
entry. The runtime-proof policy choice remains open: neither reported runtime
assumptions nor a closed program entrypoint is selected here.

The source universe is the cold initialization and referenced-payload edges
visible from the admitted returned/configured `Task.Run(Action)` and
`Task.WhenAll(Task[])` roots, their source faults, queue-failure wrappers and
fault notifications. This ledger covers 15 previously cached evidence files and
18 retrieved initial closure files. It is not an exhaustive transitive runtime audit.
CLR/helper frames, exception escape boundaries and trace retry counts remain
separate obligations in the roadmap.

## Immutable evidence already checked

All runtime references below use `dotnet/runtime` revision
`b9a49e4a6d3a444a3685e318c74a7892d01873f7`, the existing .NET 8.0.32 source
model. Line numbers refer to those immutable files, not current runtime main.
Cached files and manifests are under the local, ignored research directory
`artifacts/resource-runtime-fault-review/`; they are not repository delivery
artifacts. Public source links and SHA-256 values below preserve the identities
needed to retrieve the evidence again.

For each of these 15 local files, SHA-256 and the Git blob SHA-1 were recomputed
from its bytes and matched the named JSON manifest. Git blob hashing included
the `blob <byte-count>` header and its terminating zero byte. Recorded byte counts
were also checked where the manifest supplied them; the table gives the actual
checked counts for all files. This was read-only evidence verification, not a
runtime execution, build or test.

Manifest abbreviations: S = `sources.json`; Q = `task-scheduling-source.json`;
A = `aggregate-fault-sources.json`; D = `task-dispatch-copy-sources.json`;
B = `async-entry-context-source.json`; H = `runtime-helper-frame-sources.json`;
M = `managed-fault-sources.json`; P = `primitive-exception-sources.json`.

| ID | Immutable source and local filename | Manifest | Bytes | SHA-256 |
| --- | --- | --- | ---: | --- |
| E01 | [`Task.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs) | Q | 369458 | `5967259aff81a295fc2280e6400ed76b14d29f2687882c55d5297aabd281af70` |
| E02 | [`TaskScheduler.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskScheduler.cs) | A | 29624 | `4beaf9335037763995d8b4f3414088526d00c3f0e22d1b3e8e784d127577e714` |
| E03 | [`AsyncTaskMethodBuilderT.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs) | D | 31593 | `e358d67bcd5815a50083706ec88d0b8489a9872915d46156ba24277cd21bd662` |
| E04 | [`AsyncMethodBuilderCore.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderCore.cs) | B | 8104 | `f1e3ac12df01cb7bf745685126a140ccc039d1ca0066a080c1f2f619ecfc48f7` |
| E05 | [`ExecutionContext.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs) | H | 27084 | `5098771c4515b115fc5e694d8e79c3523cebd8fda4a73efa7dfa67220105a1c9` |
| E06 | [`System/SR.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/SR.cs), local `CoreLibSR.cs` | S | 5415 | `10074bb87dd3f7d53b1fca163e86d96b1cdb6eb1f5f36c5c7ecf5e593e5d3791` |
| E07 | [`Common System/SR.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/Common/src/System/SR.cs), local `CommonSR.cs` | S | 5273 | `4c61588a7613f8442ed8aad40b490be1cabda2b79dbd4e3d29a8d9b66b5d5833` |
| E08 | [`excep.cpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/excep.cpp) | S | 429689 | `721bfdaff7bfbb52fa99cc3d1ec2bbf4521997f5df49c6aa5e2fbe0804bbcac5` |
| E09 | [`Exception.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Exception.cs) | A | 11730 | `3f44c5b9eb6851301091fc65ce6dd6671e1a750f8a7f120837bc09021ecf12ab` |
| E10 | [`Exception.CoreCLR.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs) | M | 13681 | `43cd2feba3fe735921de07bcfd2ea8d3c58967cab54fc11a817c29c1bcfb0dee` |
| E11 | [`TaskSchedulerException.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskSchedulerException.cs) | S | 3467 | `f211ee8b4fe97c27ea5ec5d685981340a24fb9ff0bd8c77bb59a365299c4bb9e` |
| E12 | [`TaskExceptionHolder.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs) | M | 14029 | `bc4dda204de133879e4e334b37f74fc6c57690c05a179ae4648fbfa653a38604` |
| E13 | [`ArgumentException.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/ArgumentException.cs) | P | 5603 | `23b393789bb834fac80f4c285f7e317cf0a0b42874eedab1321366c17501016c` |
| E14 | [`ArgumentOutOfRangeException.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/ArgumentOutOfRangeException.cs) | P | 12316 | `31e72e62776b9d98691e517c210aff97687f39e03d37be35513da2b6183dedf5` |
| E15 | [`clrex.cpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/clrex.cpp) | S | 69068 | `f190fd973544c36abce5df9ec211e2daada10d869f91bd4eeb1370df0c03e1ea` |

## Quantity and ownership rules

The accounting unit here is managed storage allocated during an execution prefix
of one root invocation, including abandoned candidates. Native buffers are not
added to managed byte totals; their control flow still matters when it invokes
managed allocation or materializes further faults.

- **Fresh:** an allocation occurs inside the prefix. Record its allocation site,
  layout, attempt multiplicity and publication/abandonment behavior. Publication
  failure does not erase an allocation that already occurred.
- **Preexisting:** an object is proved to exist at root entry. Referencing it does
  not allocate its graph again. The fact of preexistence requires evidence; a
  familiar static field or warmed test is not sufficient.
- **Retained only:** a new carrier/array keeps an existing referent alive. Charge
  the new carrier/array once. Retained graph size belongs to a different metric;
  neither a reference slot nor its graph size proves fresh cumulative allocation.
- **Unresolved:** creation time, reachable behavior or multiplicity is unproved.
  Do not replace this state with zero or an invented finite cap. Deduplicate
  shared allocations across components before any eventual root composition.

## Cold tracing and task infrastructure

| Entry | Checked current source observations | Missing quantity or evidence |
| --- | --- | --- |
| T1: tracing access | E01:1683, E02:234 and E03:229/352 access `TplEventSource.Log` before testing whether logging is enabled. | Follow the singleton's initialization, generated constructor and EventSource dependencies. Identify allocations and externally reachable listener/command work, their first-entry state, attempts and failures. Disabled logging does not itself establish an already initialized singleton or zero cold cost. |
| T2: task static storage | E01:176 declares the completion sentinel; E01:1466 the TaskFactory; E01:1470 the completed Task; E01:2383 the context callback. | Determine which initializers can first execute inside the root prefix, typed field shapes, constructor dependencies and failure effects. Record sharing and entry-state evidence instead of charging the same static allocation once per child. |
| T3: scheduler and queue | E02:250 constructs the default ThreadPoolTaskScheduler. E01:1693 invokes queueing; E01:1695-1721 catches its exception, constructs a wrapper, records it and throws it. | Close the default scheduler/queue/backend source paths, including cold queue/worker infrastructure, allocation candidates and queue-failure origins. A callback's empty source body supplies no bound on these costs. |
| T4: execution context and async types | E05:22 declares `ExecutionContext.Default`; E05:86-95 contains suppressed-context creation; E03:280 declares a callback on the generic async-box type. | Distinguish default/suppressed context already present at entry from fresh storage. Establish participating closed generic identities and reachable initialization. Source isolation does not establish the state of runtime statics or ambient callback effects. |

The normal-completion storage scope in
`src/SharpProof.Specs/ResourceTaskStorageCatalog.cs:29-32` explicitly excludes
scheduler/global initialization. It cannot discharge T1-T4 for a cumulative
exceptional-prefix model. The current lifetime documentation remains in
[resource bounds](resource-bounds.md#normal-task-join-lifetime-storage).

## Resource messages, construction and retries

| Entry | Checked current source observations | Missing quantity or evidence |
| --- | --- | --- |
| R1: message demand | E11:48-49 requests the scheduler default message. E12:64-68 requests the unobserved-fault message during notification construction. E13:22-23 and E14:24-31 show ordinary argument-family default-message calls. | Build a message-demand inventory for every admitted materialization path, including the other admitted primitive families. Bind resource key, constructor overload, lookup/materialization attempts and whether the returned string is new or shared. Exception-kind sets alone do not establish all helper attempts. |
| R2: cold SR state | E06:15 declares a lock object; E06:75 creates the loading list; E06:84-87 explicitly runs four class constructors; E06:92-95 pushes, looks up and pops a key. E07:10 reads the resource-key switch; E07:30-45 selects lookup or key return and handles missing-manifest failure. | Obtain resource definitions, generated SR/build evidence and the ResourceManager/Reader/Set/BinaryReader closure. Establish selected switch/build/platform/culture behavior, string extents, list growth, resource/cache creation and reachable callbacks. No resource-key switch is selected here. |
| R3: discarded lookup state | E06:100-108 clears manager and loading-list references when lookup fails. E15:167-199 can delegate after throwable construction fails; its type guard is not a numeric attempt budget. | Count cumulative attempts, abandoned lists/managers/strings and nested materialization paths. A surviving cache, a successful final constructor or an initialized flag cannot replace this prefix accounting. Derive a finite quantity or retain Unknown. |
| R4: existing narrow message components | `ResourceTaskFaultCarrierCatalog.cs:82-157` separately models Linux array-dimension strings, returned native OOM message strings and best-effort OOM instances. | Preserve their independent materialization/conversion count inputs and platform/layout obligations. They do not supply cold SR costs or counts for other fault paths. Do not duplicate these components or count only the final replacement string. |

`src/SharpProof.Specs.Tests/ExceptionStackTraceRuntimeTests.cs:1028-1037` explicitly
warms ordinary constructors before measurement and requires shared default-message
identity. The scheduler and native OOM controls in that file are also warmed.
They validate their measured components; they are not cold bounds or universal
attempt limits.

## Referenced payloads and notification effects

| Entry | Checked current source observations | Missing quantity or evidence |
| --- | --- | --- |
| P1: scheduler inner fault | E01:1695-1699 passes the caught queueing exception into a fresh wrapper. E09:36-40 assigns the message and inner references. | Classify the caught exception and every relevant payload as fresh, preexisting or unresolved. Review the queue/backend fault origin before claiming its graph was preexisting. A wrapper's fixed instance shape does not bound the inner graph or its construction. |
| P2: resolver/loader referents | E08:2443-2463 obtains a managed resolver for a dynamic method or an exposed loader object for a collectible method, then stores that reference in the resolver array. | Obtain the getter definitions and creation/publication paths. Determine whether each getter returns an existing object or can allocate, the entry evidence, participating identities and failed/losing candidates. Array element counts do not bound referent graph creation. Do not charge already existing referents again. |
| P3: lazy exception state | E09:61-63 exposes lazy message/data behavior; E10:170-185 stores message, data, inner, trace, resolver and source references. E13:76-84 formats a parameter-bearing message. | Inventory actual reachable property reads and writes. Charge fresh formatting/data/trace-related objects only where the path reaches their construction; fields alone do not justify graph-wide charges or zero costs. Imported traces, custom actual values and arbitrary inner graphs require separate provenance. |
| P4: notification callbacks | E12:64-68 publishes the notification; E02:437-439 invokes registered event handlers. | Establish the applicable observer/subscriber behavior and its allocation/exception effects under the eventual human-approved policy. The private closed-holder lifecycle component does not prove external handler costs or choose their exclusion. |

The existing `NativeTaskFaultCarrierStorageVerifier.cs:32-52` already bounds the
explicit scheduler wrapper instance candidates, and
`NativeTaskFaultNotificationStorageVerifier.cs:8-16` expressly keeps notification
construction separate from root authority. The resolver/raw array components in
`ResourceTaskFaultCarrierCatalog.cs:170-259` do not include referent creation.

## Verified initial source closure

All 18 initial paths below were retrieved from official pinned raw sources and
added to the ignored research directory. For each file, the exact path and size
were checked against GitHub's contents API at the pinned revision, the Git blob
SHA-1 was recomputed and matched the API identity, and SHA-256 was recomputed.
There are 18 verified retrievals and zero missing initial files. The evidence is
recorded in `initialization-closure-sources.json`; its frozen SHA-256 is
`a8eb10fd7e6225ac9e3c98f50d458ba3aff4eaad90b6b1a6368b0d9f44fb470f`.
The original E01-E15 evidence and manifests were preserved.

The manifest records source/API URLs, revision, local filename, bytes, both
digests and verification status. Build selection and transitive behavior remain
unresolved where identified below. Retrieval does not make these files complete
cost models. In particular, the tracing type's path is
`System/Threading/Tasks/TplEventSource.cs`.

| ID | Exact path at the pinned revision | Ledger questions |
| --- | --- | --- |
| C01 | [`src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TplEventSource.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TplEventSource.cs) | T1: singleton, generated constructor and outgoing initialization edges. |
| C02 | [`src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventSource.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventSource.cs) | T1: base initialization, allocation attempts and externally reachable work. |
| C03 | [`src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ThreadPoolTaskScheduler.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ThreadPoolTaskScheduler.cs) | T3/P1: constructor, queue dispatch and fault origins. |
| C04 | [`src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs) | T3: reachable queue/worker storage and candidate publication. |
| C05 | [`src/libraries/System.Private.CoreLib/src/System/Threading/PortableThreadPool.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/PortableThreadPool.cs) | T3: confirm platform/backend selection, then follow reachable partials and native entries. |
| C06 | [`src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskFactory.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskFactory.cs) | T2: static factory shape and construction closure. |
| C07 | [`src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Future.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Future.cs) | T2/T4: generic Task initialization and constructor closure. |
| C08 | [`src/libraries/System.Private.CoreLib/src/System/Threading/AsyncLocal.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/AsyncLocal.cs) | T4: value-map dependencies and applicable entry-state/callback edges. |
| C09 | [`src/libraries/System.Private.CoreLib/src/System/Resources/ResourceManager.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/ResourceManager.cs) | R2/R3: lookup/cache/culture/failure closure and attempts. |
| C10 | [`src/libraries/System.Private.CoreLib/src/System/Resources/ResourceReader.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/ResourceReader.cs) | R2/R3: resource reading, string creation and failure candidates. |
| C11 | [`src/libraries/System.Private.CoreLib/src/System/Resources/RuntimeResourceSet.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/RuntimeResourceSet.cs) | R2/R3: set construction, cached strings and abandoned storage. |
| C12 | [`src/libraries/System.Private.CoreLib/src/System/IO/BinaryReader.cs`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/IO/BinaryReader.cs) | R2/R3: initialization and reachable decoding allocations. |
| C13 | [`src/libraries/System.Private.CoreLib/src/Resources/Strings.resx`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/Resources/Strings.resx) | R1/R2: demanded keys and text extents; identify additional selected resource inputs. |
| C14 | [`src/libraries/System.Private.CoreLib/src/System.Private.CoreLib.Shared.projitems`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System.Private.CoreLib.Shared.projitems) | T1/R2: establish applicable source/resource inputs and discover generator/build edges. |
| C15 | [`src/coreclr/vm/dynamicmethod.h`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/dynamicmethod.h) | P2: resolver getter declaration and inline definitions and ownership contracts. |
| C16 | [`src/coreclr/vm/dynamicmethod.cpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/dynamicmethod.cpp) | P2: resolver creation/access, entry identity and allocation attempts. |
| C17 | [`src/coreclr/vm/loaderallocator.hpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/loaderallocator.hpp) | P2: exposed-object getter declaration and inline definitions and ownership contracts. |
| C18 | [`src/coreclr/vm/loaderallocator.cpp`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/loaderallocator.cpp) | P2: exposed-object creation/access, publication and failed candidates. |

| ID | Verified bytes | SHA-256 |
| --- | ---: | --- |
| C01 | 28839 | `cc6a8a8ecea82c81ffdf6c401b079cf1839b2d069829e6fcad106a5f87226969` |
| C02 | 292585 | `1c3e95fe66e51913f26aa2a4816b2e6cab2e16a35aa14bb336e120e15e223e73` |
| C03 | 4615 | `6f0383e48481c19c99299c5fc4cb6b91f29bfb8314fc96de016473f27226f814` |
| C04 | 73380 | `de58a169cb9dce25a2eed76b66ea34d1d06ed647f5641d7c1b78db13fe91f70e` |
| C05 | 20975 | `cac5549cdb8d8735f58133e7ce6e56cd1ad7d2185f292cdcf981958f53c7b5cd` |
| C06 | 186637 | `73cd4b3331d0ec7354ff57ffafbc2378033401a319d67f35ca021da616e5507f` |
| C07 | 77931 | `13cbfe6856867dd1ed4e39575e46d8016dae2a146a08ceec8f76b58907f5e0fe` |
| C08 | 25980 | `ddf43175640a71d1594855242d8e1f9269d18e928daefe35912dfecca34d6fd1` |
| C09 | 34632 | `8e93f52343c948bea9eedabd68a2b6a4555c8848095360ee2df76911eb7e10ac` |
| C10 | 47636 | `4aa21db806b549e759765046d38142131381fb57f7ba4e0010858f8d384984da` |
| C11 | 17083 | `41e4948bd2d94704e4cbfe1b1cacf407746fb5fd088bca8945838fdd50ceb5f6` |
| C12 | 21673 | `fa644c7979e1571b0302492a884cca7f7bfb993e8557f8541d6ce1c5b55cc296` |
| C13 | 226168 | `489453bf0b17d398453b29c98f41570fff303bd549e327d5f0b47da95c9c204a` |
| C14 | 222820 | `b2f20c0e4ad3c9f83baac3c85dfa90c9ba69fd217d9942345f96e4208ae25926` |
| C15 | 12883 | `d150193e3c2d2cbcea1bebd1f60426d0eff6ea7d57230d8fb4c7391c05153aea` |
| C16 | 52574 | `329c6956b94c369b32e0a8ee18a99b7866c9f51647cef6c55b6c7bdc1f3a7209` |
| C17 | 31784 | `4ccbd81a670b829f9e385440f23efd0926c78ea5dc30b8b34de0ac77b299a4ba` |
| C18 | 69553 | `87063025aa75f62c9683587a24899d7da35a59414d57575aca137e8952fff288` |

## Bounded edge and allocation inventory

This inventory records direct source observations and specific outgoing edges.
It does not claim every allocation in these large files is reachable or that all
outgoing edges have been enumerated. Fresh candidates still need attempt counts;
retained referents still need evidence of their creation time relative to entry.
The links use physical line numbers in the verified files.

| Files | Observed allocation or reference behavior | Remaining edge or quantity |
| --- | --- | --- |
| C01-C02 | [`TplEventSource.cs:43-47`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TplEventSource.cs#L43-L47) declares the singleton and identifies a generated default constructor. [`EventSource.cs:1599-1626`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventSource.cs#L1599-L1626) reaches the activity tracker, conditional provider construction/registration and global source registration. | Generated `TplEventSource::.ctor()` body and its build artifact path are unresolved. Activity/provider implementations and selected feature flags remain open; disabled event output is not a cold initialization bound. |
| C02 | [`EventSource.cs:4243-4257`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventSource.cs#L4243-L4257) can add a weak reference and walks existing listeners. [`EventSource.cs:3572-3580`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventSource.cs#L3572-L3580) creates a dispatcher, conditionally an enabled array, then invokes a virtual listener callback. Construction failure reaches diagnostic reporting at lines 1657-1660. | Global list capacity, listeners, callback behavior, callbacks entering again and diagnostic allocations are unresolved. Existing listeners are retained references; new dispatchers/arrays and callback allocations require separate prefix accounting. |
| C03-C04 | [`ThreadPoolTaskScheduler.cs:24-59`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ThreadPoolTaskScheduler.cs#L24-L59) declares a static delegate and routes the ordinary queue branch to the thread pool; its separate long-running branch constructs a thread. [`ThreadPoolWorkQueue.cs:1710-1711`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L1710-L1711) enqueues the supplied object directly. | Bind the admitted task options to the applicable branch. Enqueueing an existing Task does not recreate its graph, but downstream queues, workers and queueing faults still require evidence. Do not charge every visible branch as an established execution path. |
| C04-C05 | [`ThreadPoolWorkQueue.cs:405-439`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L405-L439) declares queue objects and configuration-sized arrays; work-stealing storage grows at lines 91/137 and thread-local creation occurs at lines 565/1127-1133. [`PortableThreadPool.cs:65-116`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/PortableThreadPool.cs#L65-L116) declares the singleton, counter and lock candidates. | Backend selection, participating threads, queue entry capacities, growth attempts and worker/gate-thread paths remain unresolved. Source configuration constants are not a root allocation budget. |
| C06-C07 | [`TaskFactory.cs:37-40`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskFactory.cs#L37-L40) supplies instance fields; its default constructor at lines 72-74 has an empty body. [`Future.cs:67-104`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Future.cs#L67-L104) supplies result storage and constructors that delegate to the base Task constructor. | Preserve the separate object allocation, inherited initialization, closed generic identity and cold/preexisting distinction. An empty constructor body does not remove the instance allocation or establish a root bound. |
| C08 | [`AsyncLocal.cs:49-54`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/AsyncLocal.cs#L49-L54) invokes the change handler; line 100 allocates the shared empty map. Replacement map/array candidates occur in the map implementations, including lines 339/409/455/493. | Entry context state, reachable context changes, handler effects and replacement attempts remain unresolved. Existing map references and new maps must be distinguished. |
| C09 | [`ResourceManager.cs:246-253`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/ResourceManager.cs#L246-L253) constructs cache, mediator and resource-lookup objects. Lines 595-622 select current UI culture and traverse fallback/lookups. [`ResourceManager.cs:498-521`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/ResourceManager.cs#L498-L521) handles a losing resource-set publication by disposing the losing candidate. | Selected resource/culture inputs, recursive or reentrant lookup attempts, abandoned sets and transitive resource loading remain unresolved. Losing publication does not refund earlier managed allocations. |
| C10-C11 | [`ResourceReader.cs:100-140`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/ResourceReader.cs#L100-L140) constructs cache/reader objects in visible overloads; lines 864-919 create metadata arrays. String paths include lines 406-445/475-505. [`RuntimeResourceSet.cs:183-187`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Resources/RuntimeResourceSet.cs#L183-L187) constructs its cache and reader. | The selected internal reader constructor body is not established here; C14 also includes another reader partial. Resource extents, parser path selection, decoding failures, caches and reflection/deserialization reachability require separate evidence; visible alternate paths are not assumed reachable. |
| C12 | [`BinaryReader.cs:39-58`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/IO/BinaryReader.cs#L39-L58) obtains a decoder and creates a buffer. Lines 274-303 lazily create byte/character buffers and can use a cached string builder while reading a string. | Encoding/decoder and string-builder dependencies, selected resource byte lengths, initial cache state and failure attempts remain unresolved. Cached reuse does not establish that storage existed before root entry. |
| C13-C14 | [`Strings.resx:202-205`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/Resources/Strings.resx#L202-L205) and lines 286/406/418/675/690/3539/3554 locate the nine ordinary/scheduler/notification message keys in this source resource input. [`System.Private.CoreLib.Shared.projitems:2600-2615`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System.Private.CoreLib.Shared.projitems#L2600-L2615) selects thread-pool sources by build properties. | Source text availability is not proof of deployed resource/culture selection or lookup attempts. Generated SR members, the runtime build configuration and additional selected inputs remain unresolved. |
| C15-C16 | [`dynamicmethod.h:150-151`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/dynamicmethod.h#L150-L151) declares the resolver getter and assigns an existing handle in the setter. [`dynamicmethod.cpp:1478-1481`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/dynamicmethod.cpp#L1478-L1481) returns `ObjectFromHandle(m_managedResolver)` without an explicit allocation in that body. | This is evidence for a retained referent at this access, not proof that its graph preexisted root entry. Resolver creation/publication and handle provenance still require ownership evidence; no root zero-cost conclusion follows. |
| C17-C18 | [`loaderallocator.hpp:633`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/loaderallocator.hpp#L633) only declares the exposed-object getter; line 944 includes another file. [`loaderallocator.cpp:1001-1028`](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/loaderallocator.cpp#L1001-L1028) allocates a managed loader object, invokes its constructor and publishes a weak handle. | The getter definition, managed constructor, setup callers and their timing/failures are unresolved. Native memory-pressure accounting in this setup is not managed allocated bytes. No creation cost is inferred merely from the later reference. |

The following exact source targets are exposed by C14's include records and
C17's final include, but were not fetched in this bounded pass. Their contents
and runtime costs remain unchecked. Let `L` denote the repository prefix
`src/libraries/System.Private.CoreLib/src` at the same pinned revision; expansion
of `L` below is a path abbreviation, not a build-selection assertion.

| Edge | Unchecked exact target or unresolved definition | Discovery evidence |
| --- | --- | --- |
| Tracing | `L/System/Diagnostics/Tracing/ActivityTracker.cs`, `L/System/Diagnostics/Tracing/EventProvider.cs`, `L/System/Diagnostics/Tracing/TraceLogging/TraceLoggingEventHandleTable.cs`, `L/System/Diagnostics/Tracing/TraceLogging/XplatEventLogger.cs` | C14:1462/1474/1509/1516. Provider registration's selected native/partial targets and generated `TplEventSource::.ctor()` artifact path are not yet identified. |
| Queue and workers | `L/System/Collections/Concurrent/ConcurrentQueue.cs`, `L/System/Threading/LowLevelLock.cs`, `L/System/Threading/ThreadInt64PersistentCounter.cs`, `L/System/Threading/ThreadPool.Unix.cs`, `L/System/Threading/PortableThreadPool.GateThread.cs`, `L/System/Threading/PortableThreadPool.WorkerThread.cs`, `L/System/Threading/PortableThreadPool.Unix.cs` | C14:175/1178/1225/2603/2606/2612/2615. Backend/build selection and further worker/native dependencies remain open. |
| Resource loading | `L/System/Globalization/CultureInfo.cs`, `L/System/Resources/FileBasedResourceGroveler.cs`, `L/System/Resources/ManifestBasedResourceGroveler.cs`, `L/System/Resources/ResourceFallbackManager.cs`, `L/System/Resources/ResourceReader.Core.cs`, `L/System/Resources/ResourceSet.cs` | C14:344/753/756/760/762/764. Generated SR resource-manager/getter definitions and their build artifact paths remain unidentified. |
| String decoding | `L/System/Text/Encoding.cs`, `L/System/Text/UTF8Encoding.cs`; include expression `$(CommonPath)System/Text/StringBuilderCache.cs` | C14:1121/1154/1421-1422. C14 does not define the `CommonPath` repository prefix, so that expanded target path remains unresolved. Decoder implementation selection and builder/storage creation attempts remain open. |
| Loader and resolver | `src/coreclr/vm/loaderallocator.inl`; callable `METHOD__LOADERALLOCATOR__CTOR`; resolver creation/publication callers and `ObjectFromHandle` definition | C17:944 includes the first exact path. Definition paths for the remaining named targets were not established by C01-C18; no guessed paths or implementation claims are supplied. |

## Next implementation boundary and stopping condition

Source retrieval, identity verification, allocation-site inventories and reachable
edge classification can proceed without selecting a proof policy. For each
reachable edge, record the owning call path, selected build/platform, creation
time relative to root entry, typed layout, allocation-attempt quantity, shared
identity and any external effects. Keep these quantities symbolic or unresolved
until their inputs are independently established.

The C01-C18 retrieval and bounded direct-inventory checkpoint is frozen here.
All initial files have verified identities; the tables preserve the observed
sites and explicit unresolved source/callable targets. No transitive files were
retrieved in this pass. The unchecked transitive edges, unidentified generated
definition paths, entry state and attempt quantities remain gaps. This is not
exhaustive source closure or completion of phase F.

A cumulative root bound still requires every reachable managed allocation and
attempt quantity, the other roadmap obligations, and runtime premises bound to
the chosen evidence/API policy. Missing callbacks, cold state, constructor/helper
attempts or referent provenance keep that result Unknown. Private components,
warmed measurements and a smaller surviving graph do not satisfy this condition.

## Declared-premise route and bounded getter follow-up

The user selected explicit reported runtime assumptions on 2026-10-22. The
[conditional task-byte model](resource-task-runtime-premises.md) therefore uses
declared transitive initialization and referent budgets instead of claiming that
this source ledger establishes their values. Source-derived closure remains a
separate strengthening, not a prerequisite silently assumed by that model.

Two additional pinned files were retrieved and their byte counts, SHA-256 values
and Git blob identities independently rechecked:

| Source | Bytes | SHA-256 |
| --- | --- | --- |
| [loaderallocator.inl](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/loaderallocator.inl) | 5392 | `337a856d3ecb67cdb637a86e00e34818a623f93ae583f39161c49d15b53b6e51` |
| [LoaderAllocator.cs](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Reflection/LoaderAllocator.cs) | 2870 | `cbb24b7e0a3b32620b8c905ff15cfa5e9d07adae6e971ae8ca63bde952cd43e1` |

The getter selects null or reads the existing weak handle. Its body does not
establish when the referent was created or the helper's effects. The managed
constructor allocates an initial reference array and scout; setup can abandon
candidates before publication. Later growth, native helpers, generated binding,
cleanup and finalizer retries remain outside this bounded source audit. The
conditional runtime budgets explicitly include those transitive costs and
attempts rather than treating the getter as proof of root-entry preexistence.
